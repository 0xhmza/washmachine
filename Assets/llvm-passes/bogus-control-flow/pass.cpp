// Bogus Control Flow pass — LLVM 22 new pass manager.
//
// For every safe basic block in a function we:
//   1. Split the block after its PHIs so we get   real_pred -> real_body.
//   2. Clone real_body (sans terminator) into a "junk" block, prepend a
//      handful of random arithmetic instructions to it, and have its
//      terminator fall through to real_body.
//   3. Replace the unconditional branch real_pred -> real_body with a
//      conditional branch on an opaque predicate that is mathematically
//      always TRUE. The true edge goes to real_body, the false edge to junk.
//
// The predicate exploits the identity  x*(x+1) % 2 == 0  (product of two
// consecutive integers is even). The operands are read from `volatile`
// globals so the optimizer cannot constant-fold the comparison away. Each
// build picks fresh initial values, fresh per-block junk, and a fresh subset
// of blocks to transform — giving full polymorphism.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Plugins/PassPlugin.h"
#include "llvm/IR/BasicBlock.h"
#include "llvm/IR/Constants.h"
#include "llvm/IR/Function.h"
#include "llvm/IR/IRBuilder.h"
#include "llvm/IR/Instructions.h"
#include "llvm/IR/Module.h"
#include "llvm/IR/PassManager.h"
#include "llvm/IR/Verifier.h"
#include "llvm/Support/raw_ostream.h"
#include "llvm/Transforms/Utils/BasicBlockUtils.h"
#include "llvm/Transforms/Utils/Cloning.h"

#include <cstdlib>
#include <random>
#include <string>

using namespace llvm;

namespace {

static uint64_t pickSeed(StringRef salt) {
    if (const char *env = std::getenv("OBFUSCATION_SEED")) {
        char *end = nullptr;
        uint64_t base = std::strtoull(env, &end, 10);
        if (end != env) {
            uint64_t h = 1469598103934665603ull;
            for (char c : salt) {
                h ^= static_cast<unsigned char>(c);
                h *= 1099511628211ull;
            }
            return base ^ h;
        }
    }
    std::random_device rd;
    return (static_cast<uint64_t>(rd()) << 32) ^ rd();
}

// Skip blocks that participate in EH / unusual terminators where naive
// cloning would corrupt the CFG.
static bool isSafeBlock(BasicBlock &BB) {
    if (BB.isEntryBlock()) return false;
    if (BB.isLandingPad() || BB.isEHPad()) return false;
    Instruction *term = BB.getTerminator();
    if (!term) return false;
    if (isa<InvokeInst>(term)) return false;
    if (isa<CatchSwitchInst>(term)) return false;
    if (isa<CatchReturnInst>(term)) return false;
    if (isa<CleanupReturnInst>(term)) return false;
    if (isa<ResumeInst>(term)) return false;
    if (isa<IndirectBrInst>(term)) return false;
    // We need at least one real instruction to split on.
    if (BB.getFirstNonPHIIt() == BB.end()) return false;
    return true;
}

// Ensure module has the two opaque-source globals; create them on first use.
// Each is volatile-loaded so the optimizer must treat the values as unknown.
struct OpaqueGlobals {
    GlobalVariable *x;
    GlobalVariable *y;
};

static OpaqueGlobals getOrCreateOpaqueGlobals(Module &M, std::mt19937_64 &rng) {
    LLVMContext &Ctx = M.getContext();
    Type *i32 = Type::getInt32Ty(Ctx);

    auto find = [&](StringRef name, uint32_t fallback) {
        if (auto *g = M.getNamedGlobal(name))
            return g;
        return new GlobalVariable(
            M, i32, /*isConstant=*/false,
            GlobalValue::InternalLinkage,
            ConstantInt::get(i32, fallback),
            name);
    };

    std::uniform_int_distribution<uint32_t> any;
    return {find("__wm_bcf_x", any(rng) | 1u),
            find("__wm_bcf_y", any(rng) | 1u)};
}

// Materialise an always-true predicate: ((x * (x+1)) & 1) == 0  OR  y < y+1.
// The OR clause is also always true; combining them gives the optimizer two
// independent reasons it cannot see through, both anchored on volatile reads.
static Value *buildOpaqueTrue(IRBuilder<> &B, const OpaqueGlobals &g) {
    Type *i32 = B.getInt32Ty();
    LoadInst *xv = B.CreateLoad(i32, g.x, /*isVolatile=*/false);
    xv->setVolatile(true);
    LoadInst *yv = B.CreateLoad(i32, g.y, /*isVolatile=*/false);
    yv->setVolatile(true);

    Value *xp1   = B.CreateAdd(xv, ConstantInt::get(i32, 1));
    Value *prod  = B.CreateMul(xv, xp1);
    Value *lsb   = B.CreateAnd(prod, ConstantInt::get(i32, 1));
    Value *part1 = B.CreateICmpEQ(lsb, ConstantInt::get(i32, 0));

    Value *yp1   = B.CreateAdd(yv, ConstantInt::get(i32, 1));
    Value *part2 = B.CreateICmpSLT(yv, yp1); // always true (no overflow guard)
                                             // Note: undefined on INT_MAX but
                                             // that is acceptable junk-side.

    return B.CreateOr(part1, part2);
}

// Fill `bb` with a few harmless arithmetic instructions in-place at the
// start, all consuming the opaque globals so they don't get DCE'd.
static void injectJunk(BasicBlock *bb, const OpaqueGlobals &g,
                       std::mt19937_64 &rng) {
    IRBuilder<> B(&*bb->getFirstInsertionPt());
    Type *i32 = B.getInt32Ty();
    LoadInst *xv = B.CreateLoad(i32, g.x);
    xv->setVolatile(true);
    LoadInst *yv = B.CreateLoad(i32, g.y);
    yv->setVolatile(true);
    Value *acc = xv;

    std::uniform_int_distribution<int> count(3, 7);
    std::uniform_int_distribution<int> op(0, 4);
    std::uniform_int_distribution<uint32_t> imm(1, 0x7fffffff);

    int n = count(rng);
    for (int i = 0; i < n; ++i) {
        Value *rhs = (i % 2 == 0) ? static_cast<Value *>(yv)
                                  : static_cast<Value *>(ConstantInt::get(i32, imm(rng)));
        switch (op(rng)) {
            case 0: acc = B.CreateAdd(acc, rhs); break;
            case 1: acc = B.CreateSub(acc, rhs); break;
            case 2: acc = B.CreateXor(acc, rhs); break;
            case 3: acc = B.CreateMul(acc, rhs); break;
            case 4: acc = B.CreateAnd(acc, rhs); break;
        }
    }
    // Sink the accumulated value back to memory so DCE keeps the chain.
    StoreInst *si = B.CreateStore(acc, g.x);
    si->setVolatile(true);
}

struct BogusControlFlowPass : PassInfoMixin<BogusControlFlowPass> {
    PreservedAnalyses run(Function &F, FunctionAnalysisManager &) {
        if (F.isDeclaration() || F.empty()) return PreservedAnalyses::all();
        if (F.hasFnAttribute(Attribute::Naked)) return PreservedAnalyses::all();
        if (F.hasPersonalityFn()) {
            // Functions with personality use the EH machinery; keeping their
            // CFG intact is much safer than poking at it with bogus edges.
            return PreservedAnalyses::all();
        }

        Module &M = *F.getParent();

        // Pre-collect candidates so we don't iterate while mutating the list.
        SmallVector<BasicBlock *, 32> candidates;
        for (BasicBlock &BB : F)
            if (isSafeBlock(BB))
                candidates.push_back(&BB);

        if (candidates.empty()) return PreservedAnalyses::all();

        std::mt19937_64 rng(pickSeed(F.getName()));
        OpaqueGlobals og = getOrCreateOpaqueGlobals(M, rng);

        // Transform a random ~60% subset. Too high and code size balloons;
        // too low and the obfuscation is sparse enough to ignore.
        std::uniform_int_distribution<unsigned> coin(0, 99);

        bool changed = false;

        for (BasicBlock *bb : candidates) {
            if (coin(rng) >= 60) continue;

            // Split off the body from any PHIs at the top of the block. After
            // the split, `realHead` keeps the PHIs and ends in `br realBody`.
            BasicBlock *realBody = bb->splitBasicBlock(
                bb->getFirstNonPHIIt(), bb->getName() + ".real");
            BasicBlock *realHead = bb;

            // Clone realBody into a junk block; CloneBasicBlock copies the
            // terminator too, which we then drop in favour of br realBody.
            ValueToValueMapTy vmap;
            BasicBlock *junk = CloneBasicBlock(
                realBody, vmap, ".junk", &F);
            // Remap operands so any SSA references inside the clone that
            // happened to alias values defined in realBody refer back to the
            // clone's own copies. References to values defined OUTSIDE the
            // block (the common case) remain valid because they dominate
            // both blocks.
            for (Instruction &I : *junk)
                RemapInstruction(&I, vmap,
                                 RF_NoModuleLevelChanges |
                                 RF_IgnoreMissingLocals);
            // Drop the cloned terminator (which mirrors realBody's exit) and
            // replace it with a straight branch back to realBody. That way
            // junk's side never affects PHIs in any successor of realBody.
            junk->getTerminator()->eraseFromParent();
            BranchInst::Create(realBody, junk);

            // Inject the bogus-junk instructions at the top of junk.
            injectJunk(junk, og, rng);

            // Replace realHead's unconditional br with the opaque branch.
            Instruction *oldTerm = realHead->getTerminator();
            IRBuilder<> B(oldTerm);
            Value *cond = buildOpaqueTrue(B, og);
            BranchInst::Create(realBody, junk, cond, realHead);
            oldTerm->eraseFromParent();

            changed = true;
        }

        if (!changed) return PreservedAnalyses::all();

        // We invalidated the CFG; conservatively report everything dirty.
        return PreservedAnalyses::none();
    }

    static bool isRequired() { return true; }
};

// Module-level driver that runs the function-level pass on every defined
// function. This avoids createModuleToFunctionPassAdaptor — that adaptor
// relies on AnalysisInfoMixin<>::ID() addresses being shared between the
// host clang and the plugin DLL, which they are not under MSVC static
// linkage. A direct loop sidesteps the FAM-proxy lookup entirely.
struct BogusControlFlowModulePass
    : PassInfoMixin<BogusControlFlowModulePass> {
    PreservedAnalyses run(Module &M, ModuleAnalysisManager &) {
        FunctionAnalysisManager dummyFAM;
        BogusControlFlowPass impl;
        bool changed = false;
        for (Function &F : M) {
            auto pa = impl.run(F, dummyFAM);
            if (!pa.areAllPreserved()) changed = true;
        }
        return changed ? PreservedAnalyses::none()
                       : PreservedAnalyses::all();
    }

    static bool isRequired() { return true; }
};

} // namespace

llvm::PassPluginLibraryInfo getBogusControlFlowPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "BogusControlFlow",
            LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, ModulePassManager &MPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "bcf" || Name == "bogus-cf") {
                            MPM.addPass(BogusControlFlowModulePass());
                            return true;
                        }
                        return false;
                    });
                PB.registerOptimizerEarlyEPCallback(
                    [](ModulePassManager &MPM, OptimizationLevel,
                       ThinOrFullLTOPhase) {
                        MPM.addPass(BogusControlFlowModulePass());
                    });
            }};
}

// Windows export: CMakeLists adds /EXPORT:llvmGetPassPluginInfo so the
// declaration in <llvm/Plugins/PassPlugin.h> stays unmodified.
extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getBogusControlFlowPluginInfo();
}
