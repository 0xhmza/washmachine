// pass-runner: washmachine LLVM obfuscation pass application tool.
//
// Monolithic design — all four obfuscation passes are compiled directly into
// this executable, so there is exactly one LLVM instance in the process.
// This avoids two failure modes that arise with DLL-based approaches:
//
//   1. C++ ABI mismatch: MinGW pass DLLs cannot be loaded by the official
//      MSVC-built clang (different vtable / typeinfo layouts → crash).
//   2. Duplicate LLVM singletons: if DLLs *and* the host exe both statically
//      link LLVM, AnalysisKey addresses differ per copy → invalid results.
//
// Usage:
//   pass-runner.exe -passes="bcf,cff,sub,strenc" input.bc -o output.bc

#include "llvm/Bitcode/BitcodeWriter.h"
#include "llvm/IRReader/IRReader.h"
#include "llvm/Passes/PassBuilder.h"
#include "llvm/Plugins/PassPlugin.h"
#include "llvm/Support/CommandLine.h"
#include "llvm/Support/FileSystem.h"
#include "llvm/Support/InitLLVM.h"
#include "llvm/Support/SourceMgr.h"
#include "llvm/Support/raw_ostream.h"

using namespace llvm;

// Forward declarations for each pass's named info function.
// The definitions live in the corresponding pass.cpp file compiled into
// this executable — no DLL loading required.
llvm::PassPluginLibraryInfo getBogusControlFlowPluginInfo();
llvm::PassPluginLibraryInfo getControlFlowFlatteningPluginInfo();
llvm::PassPluginLibraryInfo getInstructionSubstitutionPluginInfo();
llvm::PassPluginLibraryInfo getStringObfuscationPluginInfo();

static cl::opt<std::string> PassPipeline(
    "passes",
    cl::desc("Pass pipeline string (e.g. 'bcf,cff,sub,strenc')"),
    cl::value_desc("pipeline"),
    cl::Required);

static cl::opt<std::string> InputFilename(
    cl::Positional,
    cl::desc("<input .bc file>"),
    cl::Required);

static cl::opt<std::string> OutputFilename(
    "o",
    cl::desc("Output .bc file"),
    cl::value_desc("filename"),
    cl::Required);

int main(int argc, char **argv) {
    InitLLVM X(argc, argv);
    cl::ParseCommandLineOptions(argc, argv, "washmachine pass runner\n");

    // Load the input IR module
    LLVMContext Ctx;
    SMDiagnostic Diag;
    auto M = parseIRFile(InputFilename, Diag, Ctx);
    if (!M) {
        Diag.print(argv[0], errs());
        return 1;
    }

    // Build analysis / pass manager infrastructure
    PassBuilder PB;
    LoopAnalysisManager LAM;
    FunctionAnalysisManager FAM;
    CGSCCAnalysisManager CGAM;
    ModuleAnalysisManager MAM;

    // Register all four built-in obfuscation pass callbacks.
    // All passes are compiled into this executable — single LLVM instance,
    // no ABI issues, no AnalysisKey conflicts.
    PassPluginLibraryInfo kPassInfos[] = {
        getBogusControlFlowPluginInfo(),
        getControlFlowFlatteningPluginInfo(),
        getInstructionSubstitutionPluginInfo(),
        getStringObfuscationPluginInfo(),
    };
    for (auto &Info : kPassInfos) {
        if (Info.RegisterPassBuilderCallbacks)
            Info.RegisterPassBuilderCallbacks(PB);
    }

    PB.registerModuleAnalyses(MAM);
    PB.registerCGSCCAnalyses(CGAM);
    PB.registerFunctionAnalyses(FAM);
    PB.registerLoopAnalyses(LAM);
    PB.crossRegisterProxies(LAM, FAM, CGAM, MAM);

    // Parse the explicit pipeline only — do NOT use default<O2> to avoid
    // double-running passes that also hook registerOptimizerEarlyEPCallback.
    ModulePassManager MPM;
    if (auto Err = PB.parsePassPipeline(MPM, PassPipeline)) {
        errs() << argv[0] << ": error parsing pass pipeline '"
               << PassPipeline << "': " << toString(std::move(Err)) << "\n";
        return 1;
    }

    // Run the obfuscation passes
    MPM.run(*M, MAM);

    // Write the obfuscated module as bitcode
    std::error_code EC;
    raw_fd_ostream OS(OutputFilename, EC, sys::fs::OF_None);
    if (EC) {
        errs() << argv[0] << ": failed to open output '" << OutputFilename
               << "': " << EC.message() << "\n";
        return 1;
    }

    WriteBitcodeToFile(*M, OS);
    OS.flush();

    return OS.has_error() ? 1 : 0;
}
