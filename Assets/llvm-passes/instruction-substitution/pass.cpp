// Instruction Substitution pass stub — requires LLVM 22 SDK to build.
//
// Build this file with CMakeLists.txt in this directory to produce pass.dll.
// Once built, place pass.dll here so Washmachine can load it via -fpass-plugin=.
//
// TODO: Implement actual instruction substitution logic in `run()`.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Passes/PassPlugin.h"
#include "llvm/IR/Function.h"
#include "llvm/IR/Instructions.h"
#include "llvm/IR/PassManager.h"
#include "llvm/Support/raw_ostream.h"

using namespace llvm;

namespace {

struct InstructionSubstitutionPass : PassInfoMixin<InstructionSubstitutionPass> {
    PreservedAnalyses run(Function &F, FunctionAnalysisManager &) {
        // TODO: Implement instruction substitution here.
        // The pass should replace arithmetic instructions with equivalent sequences:
        //   add a, b  ->  sub a, neg(b)   (or XOR-based alternatives)
        //   sub a, b  ->  add a, neg(b)
        //   and a, b  ->  not(not(a) | not(b))
        //   or  a, b  ->  not(not(a) & not(b))
        // Each substitution should be applied probabilistically.
        return PreservedAnalyses::all();
    }

    static bool isRequired() { return false; }
};

} // end anonymous namespace

llvm::PassPluginLibraryInfo getInstructionSubstitutionPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "InstructionSubstitution", LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, FunctionPassManager &FPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "sub") {
                            FPM.addPass(InstructionSubstitutionPass());
                            return true;
                        }
                        return false;
                    });
                PB.registerOptimizerEarlyEPCallback(
                    [](ModulePassManager &MPM, OptimizationLevel) {
                        MPM.addPass(createModuleToFunctionPassAdaptor(InstructionSubstitutionPass()));
                    });
            }};
}

extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getInstructionSubstitutionPluginInfo();
}
