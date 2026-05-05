// Bogus Control Flow pass stub — requires LLVM 22 SDK to build.
//
// Build this file with CMakeLists.txt in this directory to produce pass.dll.
// Once built, place pass.dll here so Washmachine can load it via -fpass-plugin=.
//
// TODO: Implement actual bogus control flow insertion logic in `run()`.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Passes/PassPlugin.h"
#include "llvm/IR/Function.h"
#include "llvm/IR/BasicBlock.h"
#include "llvm/IR/IRBuilder.h"
#include "llvm/IR/PassManager.h"
#include "llvm/Support/raw_ostream.h"

using namespace llvm;

namespace {

struct BogusControlFlowPass : PassInfoMixin<BogusControlFlowPass> {
    PreservedAnalyses run(Function &F, FunctionAnalysisManager &) {
        // TODO: Implement bogus control flow here.
        // The pass should:
        //   1. For each basic block, insert an opaque predicate (a condition that is
        //      always true but the compiler cannot prove it).
        //   2. Duplicate the basic block and insert it as the "false" branch of the
        //      opaque predicate — dead code that confuses decompilers.
        //   3. The dead copy may contain junk instructions that will never execute.
        //   4. Connect both branches to the original successor so the CFG is valid.
        return PreservedAnalyses::all();
    }

    static bool isRequired() { return false; }
};

} // end anonymous namespace

llvm::PassPluginLibraryInfo getBogusControlFlowPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "BogusControlFlow", LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, FunctionPassManager &FPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "bcf") {
                            FPM.addPass(BogusControlFlowPass());
                            return true;
                        }
                        return false;
                    });
                PB.registerOptimizerEarlyEPCallback(
                    [](ModulePassManager &MPM, OptimizationLevel) {
                        MPM.addPass(createModuleToFunctionPassAdaptor(BogusControlFlowPass()));
                    });
            }};
}

extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getBogusControlFlowPluginInfo();
}
