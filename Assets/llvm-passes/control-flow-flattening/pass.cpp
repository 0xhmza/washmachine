// Control-Flow Flattening pass — LLVM 22 new pass manager.
//
// Restructures each function's CFG into:
//
//      entry ── store first_id, %state ── br dispatch
//      dispatch ── load %state ── switch:
//         case id_A: bb_A
//         case id_B: bb_B
//         ...
//
// Every original basic block ends with `store next_id, %state; br dispatch`
// instead of branching directly to its successor. A conditional branch turns
// into a `select` over the two target IDs. Returns are kept in place.
//
// The case IDs are random 32-bit values chosen at compile time — neither the
// original numeric order nor the ordering used by the optimizer leaks. Each
// build picks a new permutation of IDs, so the on-disk switch table is fully
// shuffled. Combined with the fact that real basic-block successor edges no
// longer exist statically, this is the strongest of the four obfuscations.
//
// Polymorphism: per-function RNG seeded from OBFUSCATION_SEED + function
// name salt, or std::random_device when unset.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Plugins/PassPlugin.h"
#include "llvm/ADT/DenseMap.h"
#include "llvm/ADT/SmallSet.h"
#include "llvm/ADT/SmallVector.h"
#include "llvm/IR/BasicBlock.h"
#include "llvm/IR/Constants.h"
#include "llvm/IR/DerivedTypes.h"
#include "llvm/IR/Function.h"
#include "llvm/IR/IRBuilder.h"
#include "llvm/IR/Instructions.h"
#include "llvm/IR/Module.h"
#include "llvm/IR/PassManager.h"
#include "llvm/Support/raw_ostream.h"
#include "llvm/Transforms/Utils/Local.h"

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

// Bail if any block uses a terminator we can't rewrite cleanly.
static bool hasUnsupportedTerminator(Function &F) {
    for (BasicBlock &BB : F) {
        Instruction *t = BB.getTerminator();
        if (!t) return true;
        if (isa<InvokeInst>(t)) return true;
        if (isa<IndirectBrInst>(t)) return true;
        if (isa<CatchSwitchInst>(t)) return true;
        if (isa<CatchReturnInst>(t)) return true;
        if (isa<CleanupReturnInst>(t)) return true;
        if (isa<ResumeInst>(t)) return true;
        if (BB.isEHPad() || BB.isLandingPad()) return true;
    }
    return false;
}

struct ControlFlowFlatteningPass
    : PassInfoMixin<ControlFlowFlatteningPass> {

    PreservedAnalyses run(Function &F, FunctionAnalysisManager &) {
        if (F.isDeclaration() || F.empty()) return PreservedAnalyses::all();
        if (F.hasFnAttribute(Attribute::Naked)) return PreservedAnalyses::all();
        if (F.hasPersonalityFn()) return PreservedAnalyses::all();
        if (hasUnsupportedTerminator(F)) return PreservedAnalyses::all();

        // Tiny functions aren't worth the overhead.
        if (F.size() < 3) return PreservedAnalyses::all();

        LLVMContext &Ctx = F.getContext();
        Type *i32 = Type::getInt32Ty(Ctx);

        BasicBlock &entry = F.getEntryBlock();

        // Step 1: split entry just before its terminator so the original
        // terminator lives in a freely-rewritable successor block.
        Instruction *entryTerm = entry.getTerminator();
        BasicBlock *afterEntry = entry.splitBasicBlock(
            entryTerm->getIterator(), "wm.cff.tail");
        // entry now ends with `br afterEntry`. We'll swap that branch for
        // the dispatcher jump further down.

        // Step 2: collect every block we are going to flatten (everything
        // except `entry` itself). PHIs and cross-block SSA values inside
        // these blocks will be demoted to stack slots so the dispatcher's
        // dynamic predecessor doesn't break SSA dominance.
        SmallVector<BasicBlock *, 16> blocks;
        for (BasicBlock &BB : F) {
            if (&BB == &entry) continue;
            blocks.push_back(&BB);
        }
        if (blocks.size() < 2) return PreservedAnalyses::all();

        // Step 3: demote PHIs that sit in flattened blocks. We snapshot
        // first to avoid iterator invalidation, then call the helper which
        // creates an alloca in `entry` and replaces the PHI with load/store.
        {
            SmallVector<PHINode *, 8> phis;
            for (BasicBlock *bb : blocks)
                for (Instruction &I : *bb)
                    if (auto *p = dyn_cast<PHINode>(&I))
                        phis.push_back(p);
            for (PHINode *p : phis)
                DemotePHIToStack(p);
        }

        // Step 4: demote any non-PHI instruction in a flattened block whose
        // value is consumed in a *different* basic block. After flattening
        // none of these blocks will dominate each other in the usual sense,
        // so cross-block uses need to flow through memory.
        {
            SmallVector<Instruction *, 32> demote;
            for (BasicBlock *bb : blocks) {
                for (Instruction &I : *bb) {
                    if (I.isTerminator()) continue;
                    if (isa<PHINode>(&I)) continue;
                    if (I.getType()->isVoidTy()) continue;
                    for (User *u : I.users()) {
                        if (auto *uI = dyn_cast<Instruction>(u)) {
                            if (uI->getParent() != bb) {
                                demote.push_back(&I);
                                break;
                            }
                        }
                    }
                }
            }
            for (Instruction *I : demote)
                DemoteRegToStack(*I, /*VolatileLoads=*/false);
        }

        // Step 5: assign random non-zero unique 32-bit IDs to each block.
        std::mt19937_64 rng(pickSeed(F.getName()));
        std::uniform_int_distribution<uint32_t> dist(1, 0x7fffffffu);
        DenseMap<BasicBlock *, uint32_t> blockId;
        SmallSet<uint32_t, 32> used;
        for (BasicBlock *bb : blocks) {
            uint32_t id;
            do { id = dist(rng); } while (!used.insert(id).second);
            blockId[bb] = id;
        }

        // Step 6: state slot + dispatcher.
        AllocaInst *stateSlot = new AllocaInst(
            i32, 0, "wm.cff.state", entry.getFirstInsertionPt());

        BasicBlock *dispatch = BasicBlock::Create(
            Ctx, "wm.cff.dispatch", &F);
        BasicBlock *defaultBB = BasicBlock::Create(
            Ctx, "wm.cff.default", &F);
        new UnreachableInst(Ctx, defaultBB);

        {
            IRBuilder<> B(dispatch);
            LoadInst *st = B.CreateLoad(i32, stateSlot, "wm.cff.cur");
            SwitchInst *sw = B.CreateSwitch(
                st, defaultBB, static_cast<unsigned>(blocks.size()));
            for (BasicBlock *bb : blocks)
                sw->addCase(
                    cast<ConstantInt>(ConstantInt::get(i32, blockId[bb])),
                    bb);
        }

        // Step 7: rewrite entry's terminator to seed the state with
        // afterEntry's id and jump to dispatch.
        {
            Instruction *t = entry.getTerminator();
            IRBuilder<> B(t);
            B.CreateStore(ConstantInt::get(i32, blockId[afterEntry]),
                          stateSlot);
            B.CreateBr(dispatch);
            t->eraseFromParent();
        }

        // Helper that emits a trampoline block: it stores the target id
        // into the state slot and jumps to dispatch. Used for switch
        // successors (which require a real successor block, not just an
        // expression).
        auto buildTrampoline = [&](BasicBlock *target) -> BasicBlock * {
            auto it = blockId.find(target);
            if (it == blockId.end()) return target;
            BasicBlock *tramp = BasicBlock::Create(
                Ctx, "wm.cff.tramp", &F);
            IRBuilder<> B(tramp);
            B.CreateStore(ConstantInt::get(i32, it->second), stateSlot);
            B.CreateBr(dispatch);
            return tramp;
        };

        // Step 8: rewrite each flattened block's terminator.
        for (BasicBlock *bb : blocks) {
            Instruction *t = bb->getTerminator();

            if (isa<ReturnInst>(t) || isa<UnreachableInst>(t)) {
                // Function exits stay direct; the dispatcher just never
                // schedules anything after them.
                continue;
            }

            if (auto *br = dyn_cast<BranchInst>(t)) {
                if (br->isUnconditional()) {
                    BasicBlock *succ = br->getSuccessor(0);
                    auto it = blockId.find(succ);
                    if (it == blockId.end()) continue;
                    IRBuilder<> B(br);
                    B.CreateStore(ConstantInt::get(i32, it->second),
                                  stateSlot);
                    B.CreateBr(dispatch);
                    br->eraseFromParent();
                } else {
                    BasicBlock *tBlk = br->getSuccessor(0);
                    BasicBlock *fBlk = br->getSuccessor(1);
                    Value *cond = br->getCondition();
                    auto itT = blockId.find(tBlk);
                    auto itF = blockId.find(fBlk);
                    if (itT == blockId.end() || itF == blockId.end())
                        continue;
                    IRBuilder<> B(br);
                    Value *sel = B.CreateSelect(
                        cond,
                        ConstantInt::get(i32, itT->second),
                        ConstantInt::get(i32, itF->second),
                        "wm.cff.next");
                    B.CreateStore(sel, stateSlot);
                    B.CreateBr(dispatch);
                    br->eraseFromParent();
                }
                continue;
            }

            if (auto *sw = dyn_cast<SwitchInst>(t)) {
                // Route each case through a trampoline that sets the state
                // and jumps to dispatch. The switch itself stays in place.
                BasicBlock *newDefault = buildTrampoline(sw->getDefaultDest());
                sw->setDefaultDest(newDefault);
                for (auto &c : sw->cases())
                    c.setSuccessor(buildTrampoline(c.getCaseSuccessor()));
                continue;
            }
        }

        return PreservedAnalyses::none();
    }

    static bool isRequired() { return true; }
};

// See BogusControlFlow for the rationale for the explicit module wrapper.
struct ControlFlowFlatteningModulePass
    : PassInfoMixin<ControlFlowFlatteningModulePass> {
    PreservedAnalyses run(Module &M, ModuleAnalysisManager &) {
        FunctionAnalysisManager dummyFAM;
        ControlFlowFlatteningPass impl;
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

llvm::PassPluginLibraryInfo getControlFlowFlatteningPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "ControlFlowFlattening",
            LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, ModulePassManager &MPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "cff" || Name == "flatten") {
                            MPM.addPass(ControlFlowFlatteningModulePass());
                            return true;
                        }
                        return false;
                    });
                PB.registerOptimizerEarlyEPCallback(
                    [](ModulePassManager &MPM, OptimizationLevel,
                       ThinOrFullLTOPhase) {
                        MPM.addPass(ControlFlowFlatteningModulePass());
                    });
            }};
}

extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getControlFlowFlatteningPluginInfo();
}
