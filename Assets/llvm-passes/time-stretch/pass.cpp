// Time-Stretch pass — LLVM 22 new pass manager.
//
// Slows execution down by a controllable factor without inserting any of the
// patterns that anti-cheat / EDR / sandbox heuristics flag as "burning
// cycles":
//
//   - no rdtsc / cpuid               (anti-debug timing probe signature)
//   - no mfence / lfence / sfence    (serializing-op fingerprint)
//   - no large NOP sleds             (constant filler signature)
//   - no tight register-only loops   (spin-loop signature)
//   - no syscall storms              (abnormal cadence signature)
//
// Instead, at every safe function's entry block, we emit a straight-line
// chain of ordinary integer mixing ops (xor / add / mul / rotate) acting on
// a small bank of volatile global lanes. The op mix matches what compiled
// crypto / hash / compression code already produces, so the runtime profile
// stays statistically inside the envelope of "normal arithmetic-heavy
// software" while consuming real cycles.
//
// Slowdown is bounded per call site (rounds = level × 2, capped at 200), so
// total slowdown scales with the program's own call/loop frequency rather
// than with an artificial self-induced spin.
//
// Knobs (read once per module from the environment):
//   SLOWDOWN_LEVEL   integer 0..100. Default 0 (pass becomes a no-op).
//   OBFUSCATION_SEED reused for deterministic, polymorphic instruction
//                    selection — same convention as the other passes.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Plugins/PassPlugin.h"
#include "llvm/IR/BasicBlock.h"
#include "llvm/IR/Constants.h"
#include "llvm/IR/Function.h"
#include "llvm/IR/IRBuilder.h"
#include "llvm/IR/Instructions.h"
#include "llvm/IR/Module.h"
#include "llvm/IR/PassManager.h"

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

// Returns the user-requested intensity clamped to [0, 100]. When unset or
// malformed the pass becomes a no-op.
static unsigned readLevel() {
    const char *env = std::getenv("SLOWDOWN_LEVEL");
    if (!env) return 0;
    char *end = nullptr;
    unsigned long v = std::strtoul(env, &end, 10);
    if (end == env) return 0;
    if (v > 100) v = 100;
    return static_cast<unsigned>(v);
}

// We refuse to instrument functions where injecting an arithmetic prologue
// would either be unsafe (EH personality, naked, declarations) or pointless
// (LLVM intrinsics, our own helper globals' module-init code).
static bool isSafeFunction(Function &F) {
    if (F.isDeclaration() || F.empty()) return false;
    if (F.hasFnAttribute(Attribute::Naked)) return false;
    if (F.hasFnAttribute(Attribute::AlwaysInline)) return false;
    if (F.hasPersonalityFn()) return false;
    StringRef n = F.getName();
    if (n.starts_with("llvm.")) return false;
    if (n.starts_with("__wm_")) return false;
    return true;
}

struct StretchGlobals {
    GlobalVariable *lanes[4]; // independent mixer lanes
    GlobalVariable *counter;  // monotonic call counter
};

// All globals are internal-linkage i64 volatiles. The volatile attribute on
// every load/store keeps the optimizer from collapsing the chain or sinking
// the work past observable state.
static StretchGlobals getOrCreateGlobals(Module &M, std::mt19937_64 &rng) {
    Type *i64 = Type::getInt64Ty(M.getContext());

    auto make = [&](StringRef name, uint64_t init) -> GlobalVariable * {
        if (auto *g = M.getNamedGlobal(name)) return g;
        return new GlobalVariable(
            M, i64, /*isConstant=*/false,
            GlobalValue::InternalLinkage,
            ConstantInt::get(i64, init), name);
    };

    std::uniform_int_distribution<uint64_t> any(1, ~0ull);
    StretchGlobals g{};
    g.lanes[0] = make("__wm_ts_l0", any(rng));
    g.lanes[1] = make("__wm_ts_l1", any(rng));
    g.lanes[2] = make("__wm_ts_l2", any(rng));
    g.lanes[3] = make("__wm_ts_l3", any(rng));
    g.counter  = make("__wm_ts_ctr", 0);
    return g;
}

// Emit `rounds` rounds of mixing as a straight-line sequence acting on four
// SSA values seeded from the globals. The result is committed via volatile
// stores so the chain is observable side-effect-wise and cannot be DCE'd.
//
// The op mix is deliberately drawn from the same primitives that real-world
// hashing, RNG, and compression code uses (xor-shift, mul-add, fibonacci
// hash, rotation, mask-add). A static analyzer dumping the basic block sees
// "an arithmetic-heavy prologue", not "obvious stalling".
static void emitStretch(IRBuilder<> &B,
                        const StretchGlobals &g,
                        unsigned rounds,
                        std::mt19937_64 &rng) {
    Type *i64 = B.getInt64Ty();

    LoadInst *ctrLd = B.CreateLoad(i64, g.counter);
    ctrLd->setVolatile(true);
    Value *ctr = B.CreateAdd(ctrLd, ConstantInt::get(i64, 1));

    Value *lane[4];
    for (int k = 0; k < 4; ++k) {
        LoadInst *ld = B.CreateLoad(i64, g.lanes[k]);
        ld->setVolatile(true);
        lane[k] = ld;
    }

    std::uniform_int_distribution<unsigned> opPick(0, 5);
    std::uniform_int_distribution<unsigned> shiftPick(1, 31);
    std::uniform_int_distribution<unsigned> lanePick(0, 3);
    std::uniform_int_distribution<uint64_t> immPick(1, ~0ull);

    for (unsigned r = 0; r < rounds; ++r) {
        unsigned t  = lanePick(rng);
        unsigned o  = lanePick(rng);
        unsigned op = opPick(rng);
        unsigned s  = shiftPick(rng);
        Value *a = lane[t];
        Value *b = lane[o];

        switch (op) {
        case 0: // xorshift-like:  a ^= b << s
            a = B.CreateXor(a, B.CreateShl(b, ConstantInt::get(i64, s)));
            break;
        case 1: // mul-add through the call counter:  a += b * ctr
            a = B.CreateAdd(a, B.CreateMul(b, ctr));
            break;
        case 2: { // rotate-right(a, s) — bog-standard hash primitive
            Value *hi = B.CreateLShr(a, ConstantInt::get(i64, s));
            Value *lo = B.CreateShl (a, ConstantInt::get(i64, 64 - s));
            a = B.CreateOr(hi, lo);
            break;
        }
        case 3: // immediate XOR + subtract
            a = B.CreateSub(a, B.CreateXor(b,
                ConstantInt::get(i64, immPick(rng))));
            break;
        case 4: { // fibonacci-hash style mix
            Value *sum  = B.CreateAdd(a, b);
            Value *prod = B.CreateMul(a,
                ConstantInt::get(i64, 0x9E3779B97F4A7C15ull));
            a = B.CreateXor(sum, prod);
            break;
        }
        default: { // bit-mask + immediate add
            Value *sh = B.CreateLShr(b, ConstantInt::get(i64, s));
            a = B.CreateAnd(a, B.CreateNot(sh));
            a = B.CreateAdd(a, ConstantInt::get(i64, immPick(rng)));
            break;
        }
        }
        lane[t] = a;
    }

    for (int k = 0; k < 4; ++k) {
        StoreInst *st = B.CreateStore(lane[k], g.lanes[k]);
        st->setVolatile(true);
    }
    StoreInst *cs = B.CreateStore(ctr, g.counter);
    cs->setVolatile(true);
}

// Module-level driver. We don't go through
// createModuleToFunctionPassAdaptor for the same reason BCF doesn't: the
// FAM-proxy lookup relies on AnalysisInfoMixin::ID() addresses being shared
// across translation units, which fails under MSVC static linkage. A
// straight loop sidesteps the issue.
struct TimeStretchModulePass : PassInfoMixin<TimeStretchModulePass> {
    PreservedAnalyses run(Module &M, ModuleAnalysisManager &) {
        unsigned level = readLevel();
        if (level == 0) return PreservedAnalyses::all();

        // Bounded per-site cost. Slowdown compounds via the user program's
        // own call/loop frequency, not via a tight self-induced loop.
        unsigned rounds = level * 2;
        if (rounds > 200) rounds = 200;

        std::mt19937_64 rng(pickSeed(M.getName()));
        StretchGlobals g = getOrCreateGlobals(M, rng);

        bool changed = false;
        for (Function &F : M) {
            if (!isSafeFunction(F)) continue;
            BasicBlock &entry = F.getEntryBlock();
            auto ip = entry.getFirstInsertionPt();
            if (ip == entry.end()) continue;

            IRBuilder<> B(&*ip);
            std::mt19937_64 frng(pickSeed(F.getName()));
            emitStretch(B, g, rounds, frng);
            changed = true;
        }
        return changed ? PreservedAnalyses::none()
                       : PreservedAnalyses::all();
    }

    static bool isRequired() { return true; }
};

} // namespace

llvm::PassPluginLibraryInfo getTimeStretchPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "TimeStretch",
            LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, ModulePassManager &MPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "tstr" || Name == "time-stretch") {
                            MPM.addPass(TimeStretchModulePass());
                            return true;
                        }
                        return false;
                    });
            }};
}

// Windows export: CMakeLists adds /EXPORT:llvmGetPassPluginInfo so the
// declaration in <llvm/Plugins/PassPlugin.h> stays unmodified.
extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getTimeStretchPluginInfo();
}
