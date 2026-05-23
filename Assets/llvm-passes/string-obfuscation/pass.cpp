// String Obfuscation pass — LLVM 22 new pass manager (module-level).
//
// For each private/internal constant char-array global that holds a C string
// (printable or NUL-terminated payload), we:
//   1. Generate a random per-string key (the key length is itself randomised).
//   2. XOR-encrypt the byte buffer at compile time and replace the constant
//      initializer with the ciphertext, stored in a *mutable* global.
//   3. Emit a single per-module global constructor that walks a table of
//      (ptr, length, key, key_len) entries and decrypts them in place at
//      load time before any user code runs.
//   4. Redirect all uses of the original constant to the new mutable global.
//
// The on-disk binary contains only ciphertext; "strings" and similar tools
// cannot recover plaintext without executing the module. The per-string key
// and rotating key length make every build produce a different image.
//
// Polymorphism: OBFUSCATION_SEED env var for deterministic builds, otherwise
// std::random_device.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Plugins/PassPlugin.h"
#include "llvm/IR/Constants.h"
#include "llvm/IR/DerivedTypes.h"
#include "llvm/IR/Function.h"
#include "llvm/IR/GlobalVariable.h"
#include "llvm/IR/IRBuilder.h"
#include "llvm/IR/Module.h"
#include "llvm/IR/PassManager.h"
#include "llvm/Support/raw_ostream.h"
#include "llvm/Transforms/Utils/ModuleUtils.h"

#include <algorithm>
#include <cstdlib>
#include <random>
#include <string>
#include <vector>

using namespace llvm;

namespace {

static uint64_t pickSeed() {
    if (const char *env = std::getenv("OBFUSCATION_SEED")) {
        char *end = nullptr;
        uint64_t v = std::strtoull(env, &end, 10);
        if (end != env) return v ^ 0x9E3779B97F4A7C15ull;
    }
    std::random_device rd;
    return (static_cast<uint64_t>(rd()) << 32) ^ rd();
}

// We tag any global we introduce so a second pass invocation (e.g. when both
// -fpass-plugin and -passes load the same plugin) does not double-encrypt.
static constexpr const char *kSection = ".wm_strenc";

static bool isTargetableString(const GlobalVariable &GV) {
    if (!GV.hasInitializer() || !GV.isConstant()) return false;
    // Skip already-processed strings (we make them non-constant on rewrite).
    if (GV.getSection() == kSection) return false;
    // Skip llvm-internal metadata, compiler-injected globals.
    StringRef name = GV.getName();
    if (name.starts_with("llvm.") || name.starts_with("__")) return false;

    auto *cda = dyn_cast<ConstantDataArray>(GV.getInitializer());
    if (!cda) return false;
    if (!cda->isString() && !cda->isCString()) return false;
    if (cda->getElementType() != Type::getInt8Ty(GV.getContext())) return false;
    // Empty / single-byte payloads aren't worth the table entry overhead.
    if (cda->getNumElements() < 2) return false;
    return true;
}

struct StringObfuscationPass : PassInfoMixin<StringObfuscationPass> {
    PreservedAnalyses run(Module &M, ModuleAnalysisManager &) {
        LLVMContext &Ctx = M.getContext();
        std::mt19937_64 rng(pickSeed());
        std::uniform_int_distribution<unsigned> keyByte(0, 255);
        std::uniform_int_distribution<unsigned> keyLen(4, 16);

        // Collect first so we don't iterate while mutating the global list.
        SmallVector<GlobalVariable *, 32> candidates;
        for (GlobalVariable &GV : M.globals())
            if (isTargetableString(GV))
                candidates.push_back(&GV);

        if (candidates.empty())
            return PreservedAnalyses::all();

        struct Entry {
            GlobalVariable *cipher;   // mutable buffer of ciphertext
            GlobalVariable *key;      // private constant key bytes
            uint32_t length;          // payload length (bytes, includes NUL)
            uint32_t keyLength;       // key length (bytes)
        };
        SmallVector<Entry, 32> entries;

        Type *i8 = Type::getInt8Ty(Ctx);
        Type *i32 = Type::getInt32Ty(Ctx);
        PointerType *i8p = PointerType::get(Ctx, 0);

        for (GlobalVariable *gv : candidates) {
            auto *cda = cast<ConstantDataArray>(gv->getInitializer());
            StringRef raw = cda->getRawDataValues();
            const uint32_t n = static_cast<uint32_t>(raw.size());

            const uint32_t kl = keyLen(rng);
            std::vector<uint8_t> key(kl);
            for (uint32_t i = 0; i < kl; ++i) key[i] = keyByte(rng);

            std::vector<uint8_t> ct(n);
            for (uint32_t i = 0; i < n; ++i)
                ct[i] = static_cast<uint8_t>(raw[i]) ^ key[i % kl];

            // Mutable cipher buffer that the ctor will decrypt in place.
            ArrayType *bufTy = ArrayType::get(i8, n);
            Constant *ctInit = ConstantDataArray::get(Ctx, ArrayRef<uint8_t>(ct));
            auto *cipher = new GlobalVariable(
                M, bufTy, /*isConstant=*/false,
                GlobalValue::PrivateLinkage, ctInit,
                gv->getName() + ".wm_ct");
            cipher->setSection(kSection);
            cipher->setAlignment(gv->getAlign().valueOrOne());

            // Private constant key.
            ArrayType *keyTy = ArrayType::get(i8, kl);
            Constant *keyInit = ConstantDataArray::get(Ctx, ArrayRef<uint8_t>(key));
            auto *keyGv = new GlobalVariable(
                M, keyTy, /*isConstant=*/true,
                GlobalValue::PrivateLinkage, keyInit,
                gv->getName() + ".wm_k");
            keyGv->setSection(kSection);

            // Redirect every use of the original to the new mutable buffer.
            // The types match (both [n x i8]) so no bitcast is needed.
            gv->replaceAllUsesWith(cipher);
            // Try to inherit visibility / linkage onto cipher when the
            // original was non-private — otherwise external code that linked
            // against gv by symbol name would break.
            if (!gv->hasPrivateLinkage() && !gv->hasInternalLinkage()) {
                cipher->setLinkage(gv->getLinkage());
                cipher->setVisibility(gv->getVisibility());
                cipher->takeName(gv);
            }
            gv->eraseFromParent();

            entries.push_back({cipher, keyGv, n, kl});
        }

        // ─── Build the decryptor ctor ────────────────────────────────────
        //
        //   void __wm_strenc_init() {
        //     for each entry e:
        //       for (uint32_t i = 0; i < e.length; ++i)
        //         e.cipher[i] ^= e.key[i % e.keyLength];
        //   }
        //
        // We emit a small dispatch loop per entry; an LLVM optimizer pass
        // running afterwards will inline / unroll as appropriate.
        FunctionType *ctorTy = FunctionType::get(Type::getVoidTy(Ctx), false);
        Function *ctor = Function::Create(
            ctorTy, GlobalValue::InternalLinkage,
            "__wm_strenc_init", &M);

        BasicBlock *bbEntry = BasicBlock::Create(Ctx, "entry", ctor);
        IRBuilder<> B(bbEntry);

        for (const Entry &e : entries) {
            // Per-entry loop block layout:
            //   header:  i_phi = phi [0, pred] [next, body]
            //            cmp = i_phi < length
            //            br cmp, body, exit
            //   body:    b = cipher[i_phi]
            //            k = key[i_phi % key_len]
            //            cipher[i_phi] = b ^ k
            //            next = i_phi + 1
            //            br header
            //   exit:    ...
            BasicBlock *pred = B.GetInsertBlock();
            BasicBlock *header = BasicBlock::Create(Ctx, "hdr", ctor);
            BasicBlock *body   = BasicBlock::Create(Ctx, "body", ctor);
            BasicBlock *exitBB = BasicBlock::Create(Ctx, "next", ctor);

            B.CreateBr(header);

            B.SetInsertPoint(header);
            PHINode *iPhi = B.CreatePHI(i32, 2, "i");
            iPhi->addIncoming(ConstantInt::get(i32, 0), pred);
            Value *len = ConstantInt::get(i32, e.length);
            Value *cmp = B.CreateICmpULT(iPhi, len);
            B.CreateCondBr(cmp, body, exitBB);

            B.SetInsertPoint(body);
            Type *cipherElTy = cast<ArrayType>(e.cipher->getValueType());
            Type *keyElTy    = cast<ArrayType>(e.key->getValueType());

            Value *zero = ConstantInt::get(i32, 0);
            Value *cipherPtr = B.CreateInBoundsGEP(
                cipherElTy, e.cipher, {zero, iPhi}, "cptr");
            Value *keyIdx = B.CreateURem(
                iPhi, ConstantInt::get(i32, e.keyLength), "kidx");
            Value *keyPtr = B.CreateInBoundsGEP(
                keyElTy, e.key, {zero, keyIdx}, "kptr");

            Value *cVal = B.CreateLoad(i8, cipherPtr, /*isVolatile=*/false);
            Value *kVal = B.CreateLoad(i8, keyPtr, /*isVolatile=*/false);
            Value *pVal = B.CreateXor(cVal, kVal);
            B.CreateStore(pVal, cipherPtr);

            Value *next = B.CreateAdd(iPhi, ConstantInt::get(i32, 1), "next");
            iPhi->addIncoming(next, body);
            B.CreateBr(header);

            B.SetInsertPoint(exitBB);
        }

        B.CreateRetVoid();

        // Highest priority (0) → runs before user-level ctors. The third
        // argument is an associated data global; null is fine.
        appendToGlobalCtors(M, ctor, /*Priority=*/0, /*Data=*/nullptr);

        // The new ctor invalidates module-level structure broadly; let the
        // optimizer recompute analyses fresh on the next request.
        return PreservedAnalyses::none();
    }

    static bool isRequired() { return true; }
};

} // namespace

llvm::PassPluginLibraryInfo getStringObfuscationPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "StringObfuscation",
            LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, ModulePassManager &MPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "strenc" || Name == "strobf") {
                            MPM.addPass(StringObfuscationPass());
                            return true;
                        }
                        return false;
                    });
                PB.registerOptimizerEarlyEPCallback(
                    [](ModulePassManager &MPM, OptimizationLevel,
                       ThinOrFullLTOPhase) {
                        MPM.addPass(StringObfuscationPass());
                    });
            }};
}

extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getStringObfuscationPluginInfo();
}
