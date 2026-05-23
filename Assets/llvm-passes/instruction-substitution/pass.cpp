// Instruction Substitution pass — LLVM 22 new pass manager.
//
// Replaces integer arithmetic / bitwise binary operators with one of several
// equivalent expression sequences chosen at random per instruction. Each build
// therefore lowers to a different sequence of machine instructions, defeating
// signature-based detection while preserving semantics.
//
// Polymorphism: seeded from the OBFUSCATION_SEED environment variable when set
// (deterministic builds) or std::random_device otherwise. Each function gets
// its own RNG so processing order does not affect the substitution table.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Plugins/PassPlugin.h"
#include "llvm/IR/Function.h"
#include "llvm/IR/IRBuilder.h"
#include "llvm/IR/Instructions.h"
#include "llvm/IR/Module.h"
#include "llvm/IR/PassManager.h"
#include "llvm/Support/raw_ostream.h"

#include <cstdlib>
#include <random>
#include <string>

using namespace llvm;

namespace {

// Seed selection: deterministic when OBFUSCATION_SEED is set, otherwise
// derived from std::random_device + a per-function salt so different
// functions get independent substitution streams under one seed.
static uint64_t pickSeed(StringRef salt) {
    if (const char *env = std::getenv("OBFUSCATION_SEED")) {
        char *end = nullptr;
        uint64_t base = std::strtoull(env, &end, 10);
        if (end != env) {
            // Mix in a per-function salt so different functions still get
            // different substitution streams under a deterministic seed.
            uint64_t h = 1469598103934665603ull; // FNV-1a offset
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

// ─── Substitution rules ──────────────────────────────────────────────────
//
// Each rule receives an IRBuilder positioned at the original instruction and
// the two operands a, b. It returns the replacement value. All rules must
// preserve bitwise semantics for any integer width.

static Value *addRule0(IRBuilder<> &B, Value *a, Value *b) {
    // a + b  ==  a - (-b)
    return B.CreateSub(a, B.CreateNeg(b));
}
static Value *addRule1(IRBuilder<> &B, Value *a, Value *b) {
    // a + b  ==  (a ^ b) + 2 * (a & b)        (carry decomposition)
    Value *x = B.CreateXor(a, b);
    Value *c = B.CreateAnd(a, b);
    Value *two = ConstantInt::get(a->getType(), 2);
    return B.CreateAdd(x, B.CreateMul(c, two));
}
static Value *addRule2(IRBuilder<> &B, Value *a, Value *b) {
    // a + b  ==  (a | b) + (a & b)
    return B.CreateAdd(B.CreateOr(a, b), B.CreateAnd(a, b));
}

static Value *subRule0(IRBuilder<> &B, Value *a, Value *b) {
    // a - b  ==  a + (-b)
    return B.CreateAdd(a, B.CreateNeg(b));
}
static Value *subRule1(IRBuilder<> &B, Value *a, Value *b) {
    // a - b  ==  (a ^ b) - 2 * (~a & b)       (borrow decomposition)
    Value *x = B.CreateXor(a, b);
    Value *na = B.CreateNot(a);
    Value *bor = B.CreateAnd(na, b);
    Value *two = ConstantInt::get(a->getType(), 2);
    return B.CreateSub(x, B.CreateMul(bor, two));
}

static Value *xorRule0(IRBuilder<> &B, Value *a, Value *b) {
    // a ^ b  ==  (a | b) - (a & b)
    return B.CreateSub(B.CreateOr(a, b), B.CreateAnd(a, b));
}
static Value *xorRule1(IRBuilder<> &B, Value *a, Value *b) {
    // a ^ b  ==  (a | b) & ~(a & b)
    return B.CreateAnd(B.CreateOr(a, b), B.CreateNot(B.CreateAnd(a, b)));
}

static Value *andRule0(IRBuilder<> &B, Value *a, Value *b) {
    // a & b  ==  ~(~a | ~b)        (De Morgan)
    return B.CreateNot(B.CreateOr(B.CreateNot(a), B.CreateNot(b)));
}
static Value *andRule1(IRBuilder<> &B, Value *a, Value *b) {
    // a & b  ==  (a + b) - (a | b)
    return B.CreateSub(B.CreateAdd(a, b), B.CreateOr(a, b));
}

static Value *orRule0(IRBuilder<> &B, Value *a, Value *b) {
    // a | b  ==  ~(~a & ~b)        (De Morgan)
    return B.CreateNot(B.CreateAnd(B.CreateNot(a), B.CreateNot(b)));
}
static Value *orRule1(IRBuilder<> &B, Value *a, Value *b) {
    // a | b  ==  (a + b) - (a & b)
    return B.CreateSub(B.CreateAdd(a, b), B.CreateAnd(a, b));
}

using RuleFn = Value *(*)(IRBuilder<> &, Value *, Value *);

static const RuleFn kAddRules[] = {addRule0, addRule1, addRule2};
static const RuleFn kSubRules[] = {subRule0, subRule1};
static const RuleFn kXorRules[] = {xorRule0, xorRule1};
static const RuleFn kAndRules[] = {andRule0, andRule1};
static const RuleFn kOrRules[]  = {orRule0,  orRule1};

struct InstructionSubstitutionPass
    : PassInfoMixin<InstructionSubstitutionPass> {

    PreservedAnalyses run(Function &F, FunctionAnalysisManager &) {
        if (F.isDeclaration() || F.empty())
            return PreservedAnalyses::all();

        // Snapshot the candidate instructions up front. Substituting mutates
        // the block contents and would invalidate live iterators; capturing
        // here also means the *new* instructions we emit are not themselves
        // substituted in this invocation, which keeps expansion bounded.
        SmallVector<BinaryOperator *, 64> work;
        for (BasicBlock &BB : F)
            for (Instruction &I : BB)
                if (auto *bin = dyn_cast<BinaryOperator>(&I))
                    work.push_back(bin);

        if (work.empty())
            return PreservedAnalyses::all();

        std::mt19937_64 rng(pickSeed(F.getName()));
        // ~70% rewrite probability; under 100% so adjacent instructions still
        // vary build-to-build without exploding code size.
        std::uniform_int_distribution<unsigned> coin(0, 99);

        bool changed = false;

        for (BinaryOperator *bin : work) {
            // The instruction may have been deleted by an earlier rewrite if
            // it was an operand of a rewritten parent (rare but possible).
            if (!bin->getParent()) continue;

            // Floating-point and pointer ops fall outside our integer rules.
            if (!bin->getType()->isIntegerTy()) continue;

            // i1 (boolean) arithmetic plays badly with neg/sub on 1-bit
            // integers — skip and let SimplifyCFG canonicalise instead.
            if (bin->getType()->isIntegerTy(1)) continue;

            if (coin(rng) >= 70) continue;

            RuleFn rule = nullptr;
            switch (bin->getOpcode()) {
                case Instruction::Add: {
                    auto n = std::uniform_int_distribution<size_t>(
                        0, std::size(kAddRules) - 1)(rng);
                    rule = kAddRules[n];
                    break;
                }
                case Instruction::Sub: {
                    auto n = std::uniform_int_distribution<size_t>(
                        0, std::size(kSubRules) - 1)(rng);
                    rule = kSubRules[n];
                    break;
                }
                case Instruction::Xor: {
                    auto n = std::uniform_int_distribution<size_t>(
                        0, std::size(kXorRules) - 1)(rng);
                    rule = kXorRules[n];
                    break;
                }
                case Instruction::And: {
                    auto n = std::uniform_int_distribution<size_t>(
                        0, std::size(kAndRules) - 1)(rng);
                    rule = kAndRules[n];
                    break;
                }
                case Instruction::Or: {
                    auto n = std::uniform_int_distribution<size_t>(
                        0, std::size(kOrRules) - 1)(rng);
                    rule = kOrRules[n];
                    break;
                }
                default: continue;
            }

            IRBuilder<> B(bin);
            Value *repl = rule(B, bin->getOperand(0), bin->getOperand(1));

            bin->replaceAllUsesWith(repl);
            bin->eraseFromParent();
            changed = true;
        }

        if (!changed) return PreservedAnalyses::all();

        PreservedAnalyses PA;
        PA.preserveSet<CFGAnalyses>();
        return PA;
    }

    static bool isRequired() { return true; }
};

// See BogusControlFlow for the rationale for the explicit module wrapper.
struct InstructionSubstitutionModulePass
    : PassInfoMixin<InstructionSubstitutionModulePass> {
    PreservedAnalyses run(Module &M, ModuleAnalysisManager &) {
        FunctionAnalysisManager dummyFAM;
        InstructionSubstitutionPass impl;
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

llvm::PassPluginLibraryInfo getInstructionSubstitutionPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "InstructionSubstitution",
            LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, ModulePassManager &MPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "sub" || Name == "instsub") {
                            MPM.addPass(InstructionSubstitutionModulePass());
                            return true;
                        }
                        return false;
                    });
                PB.registerOptimizerEarlyEPCallback(
                    [](ModulePassManager &MPM, OptimizationLevel,
                       ThinOrFullLTOPhase) {
                        MPM.addPass(InstructionSubstitutionModulePass());
                    });
            }};
}

extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getInstructionSubstitutionPluginInfo();
}
