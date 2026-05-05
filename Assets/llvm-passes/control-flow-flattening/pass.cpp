// Control-Flow Flattening pass stub — requires LLVM 22 SDK to build.
//
// Build this file with CMakeLists.txt in this directory to produce pass.dll.
// Once built, place pass.dll here so Washmachine can load it via -fpass-plugin=.
//
// TODO: Implement actual control-flow flattening logic in `run()`.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Passes/PassPlugin.h"
#include "llvm/IR/Function.h"
#include "llvm/IR/PassManager.h"
#include "llvm/Support/raw_ostream.h"

using namespace llvm;

namespace {

struct ControlFlowFlatteningPass : PassInfoMixin<ControlFlowFlatteningPass> {
    PreservedAnalyses run(Function &F, FunctionAnalysisManager &) {
        // TODO: Implement control-flow flattening here.
        // The pass should:
        //   1. Collect all basic blocks in the function.
        //   2. Create a dispatch loop with a switch on a state variable.
        //   3. Replace direct branches with updates to the state variable.
        //   4. Connect all blocks through the central dispatch block.
        return PreservedAnalyses::all();
    }

    static bool isRequired() { return false; }
};

} // end anonymous namespace

llvm::PassPluginLibraryInfo getControlFlowFlatteningPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "ControlFlowFlattening", LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, FunctionPassManager &FPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "cff") {
                            FPM.addPass(ControlFlowFlatteningPass());
                            return true;
                        }
                        return false;
                    });
                PB.registerOptimizerEarlyEPCallback(
                    [](ModulePassManager &MPM, OptimizationLevel) {
                        MPM.addPass(createModuleToFunctionPassAdaptor(ControlFlowFlatteningPass()));
                    });
            }};
}

extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getControlFlowFlatteningPluginInfo();
}
