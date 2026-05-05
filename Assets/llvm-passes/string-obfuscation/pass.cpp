// String Obfuscation pass stub — requires LLVM 22 SDK to build.
//
// Build this file with CMakeLists.txt in this directory to produce pass.dll.
// Once built, place pass.dll here so Washmachine can load it via -fpass-plugin=.
//
// TODO: Implement actual string obfuscation logic in `run()`.

#include "llvm/Passes/PassBuilder.h"
#include "llvm/Passes/PassPlugin.h"
#include "llvm/IR/Module.h"
#include "llvm/IR/GlobalVariable.h"
#include "llvm/IR/IRBuilder.h"
#include "llvm/IR/PassManager.h"
#include "llvm/Support/raw_ostream.h"

using namespace llvm;

namespace {

struct StringObfuscationPass : PassInfoMixin<StringObfuscationPass> {
    PreservedAnalyses run(Module &M, ModuleAnalysisManager &) {
        // TODO: Implement string obfuscation here.
        // The pass should:
        //   1. Scan all GlobalVariables for constant string data (ConstantDataArray of i8).
        //   2. XOR-encrypt each string in-place at compile time with a random key.
        //   3. Insert a decryption stub (function called from a constructor or on first use).
        //   4. Replace uses of the original string constant with a pointer to the
        //      decrypted buffer produced by the stub.
        return PreservedAnalyses::all();
    }

    static bool isRequired() { return false; }
};

} // end anonymous namespace

llvm::PassPluginLibraryInfo getStringObfuscationPluginInfo() {
    return {LLVM_PLUGIN_API_VERSION, "StringObfuscation", LLVM_VERSION_STRING,
            [](PassBuilder &PB) {
                PB.registerPipelineParsingCallback(
                    [](StringRef Name, ModulePassManager &MPM,
                       ArrayRef<PassBuilder::PipelineElement>) {
                        if (Name == "strenc") {
                            MPM.addPass(StringObfuscationPass());
                            return true;
                        }
                        return false;
                    });
                PB.registerOptimizerEarlyEPCallback(
                    [](ModulePassManager &MPM, OptimizationLevel) {
                        MPM.addPass(StringObfuscationPass());
                    });
            }};
}

extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
llvmGetPassPluginInfo() {
    return getStringObfuscationPluginInfo();
}
