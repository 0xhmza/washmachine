# LLVM Obfuscation Passes

Four LLVM IR obfuscation passes for the **LlvmObfuscated** compiler backend.  
Built with the bundled `Tools\msys64` MinGW toolchain — no external dependencies required.

---

## Passes

| ID | Name | Pipeline key | Purpose |
|----|------|--------------|---------|
| `bogus-control-flow` | Bogus Control Flow | `bcf` | Inserts opaque predicates + dead branches so decompilers can't trace real control flow |
| `control-flow-flattening` | Control-Flow Flattening | `cff` | Replaces the CFG with one switch-dispatch loop; destroys structural analysis |
| `instruction-substitution` | Instruction Substitution | `sub` | Replaces `a+b`, XOR, etc. with semantically equivalent but less recognizable forms |
| `string-obfuscation` | String Obfuscation | `strenc` | Encrypts string literals; emits a per-string decrypt stub that runs at load time |

Each directory contains:

```
pass.json       metadata (id / name / description / opt_name) read by LlvmPassRegistry
pass.cpp        the LLVM new-pass-manager pass implementation
CMakeLists.txt  builds pass.cpp → pass.dll (MinGW static-linked, no runtime DLL deps)
pass.dll        present only after a successful build (git-ignored)
```

---

## Build

From the repository root:

```powershell
.\Assets\llvm-passes\build-all.ps1          # incremental
.\Assets\llvm-passes\build-all.ps1 -Force   # clean rebuild
```

This will:
1. Auto-detect the bundled MinGW toolchain at `Tools\msys64\mingw64`
2. Build each of the four pass DLLs
3. Build `pass-runner.exe` — the monolithic obfuscation tool used at runtime

**Outputs (all git-ignored, must be built locally):**

| Artifact | Location |
|----------|----------|
| `bogus-control-flow/pass.dll` | `Assets/llvm-passes/bogus-control-flow/` |
| `control-flow-flattening/pass.dll` | `Assets/llvm-passes/control-flow-flattening/` |
| `instruction-substitution/pass.dll` | `Assets/llvm-passes/instruction-substitution/` |
| `string-obfuscation/pass.dll` | `Assets/llvm-passes/string-obfuscation/` |
| `pass-runner.exe` | `Assets/llvm-passes/` |

---

## Runtime pipeline

The official LLVM Windows binaries (`clang-cl.exe`, `clang++.exe`) use the **MSVC C++ ABI**.  
The MinGW-built pass DLLs use the **GNU/Itanium ABI**.  
Loading a MinGW DLL into the MSVC clang via `-fpass-plugin` crashes immediately (ABI mismatch on vtable/typeinfo layout).

The backend uses a **three-step pipeline** to work around this:

```
Step 1   clang-cl /c /clang:-emit-llvm source.cpp   →  source.bc       (MSVC frontend, no optimisation)
Step 2   pass-runner -passes=bcf,cff,sub,strenc      →  source.obf.bc   (MinGW tool, same ABI as passes)
Step 3   clang-cl /O2 /Fe:output.exe source.obf.bc  →  output.exe      (MSVC backend, full optimisation)
```

### Why pass-runner is monolithic

An earlier design loaded each `pass.dll` at runtime via `LoadLibraryA`.  
This caused a second failure: both the host EXE and each DLL statically link LLVM,  
creating **duplicate `AnalysisKey` singletons** in the same process → `0xC0000005` access violation.

The fix: compile all four `pass.cpp` files **directly into `pass-runner.exe`**.

```
pass-runner.exe
├── main.cpp                           (pipeline driver, forward-declares each pass's info fn)
├── bogus-control-flow/pass.cpp        (compiled in)
├── control-flow-flattening/pass.cpp   (compiled in)
├── instruction-substitution/pass.cpp  (compiled in)
└── string-obfuscation/pass.cpp        (compiled in)
```

Single LLVM instance, no DLL loading at runtime, no ODR conflicts.  
Each pass exposes a uniquely-named function (e.g. `getBogusControlFlowPluginInfo()`);  
`main.cpp` calls all four at startup and the `-passes=` pipeline string controls which ones run.

---

## Adding a new pass

1. Create `Assets/llvm-passes/<id>/` with `pass.json`, `pass.cpp`, `CMakeLists.txt`
2. In `pass.cpp`, expose a uniquely-named info function alongside the standard weak export:
   ```cpp
   // Unique name (forward-declared in pass-runner/main.cpp):
   llvm::PassPluginLibraryInfo getMyPassPluginInfo() { ... }

   // Standard weak export (for standalone DLL usage):
   extern "C" LLVM_ATTRIBUTE_WEAK ::llvm::PassPluginLibraryInfo
   llvmGetPassPluginInfo() { return getMyPassPluginInfo(); }
   ```
3. In `pass.json`, include an `opt_name` field (the pipeline key, e.g. `"mypass"`):
   ```json
   { "name": "My Pass", "description": "What it does.", "opt_name": "mypass" }
   ```
4. In `pass-runner/main.cpp`, add a forward declaration and entry in `kPassInfos[]`
5. In `pass-runner/CMakeLists.txt`, add `../my-pass/pass.cpp` to `add_executable`
6. In `build-all.ps1`, add `"my-pass"` to the `$passes` array
7. Register the pass in `Assets/default.yaml` under `llvm_passes:`

---

## Session history — problems solved

### P1 · ZLIB linker error in MSYS2 build
The MSYS2 LLVM CMake config lists `ZLIB::ZLIB` but the trimmed `Tools\msys64` only ships
the static `libz.a`, not the shared import library. Fixed in each pass `CMakeLists.txt` by
redirecting the `ZLIB::ZLIB` imported target to `libz.a`.

### P2 · `clang-cl: error: LTO requires -fuse-ld=lld`
When LTO was requested in the single-step (no passes) path, clang-cl needed an explicit
linker flag. Fixed by appending `-fuse-ld=lld` when LTO is active on the single-step path.

### P3 · C++ ABI mismatch → `clang frontend command failed due to signal`
Loading MinGW `pass.dll` via `-fpass-plugin` into the MSVC-ABI `clang-cl.exe` caused an
immediate crash. Root cause: incompatible vtable/typeinfo layouts. Solution: the 3-step
pipeline described above.

### P4 · Duplicate LLVM singletons → `0xC0000005` in pass-runner
An intermediate design loaded pass DLLs via `LoadLibraryA` at runtime. Both the EXE and
each DLL statically linked LLVM, creating N+1 copies of LLVM's `AnalysisKey` singletons
in the same process. Solution: monolithic compilation (all four passes built into the EXE).

### P5 · `opt_name` JSON field not deserialised
`PropertyNameCaseInsensitive = true` only handles capitalisation differences, not snake_case.
Fixed by adding `[JsonPropertyName("opt_name")]` to `LlvmPassRegistry.PassMeta.OptName`.

### P6 · `HashAndRenameAsync` clobbering intermediate bitcode paths
The existing `RunCompilerAsync` renames output files to content-hash-based names.
Step 2 couldn't find the bitcode after step 1 had renamed it. Fixed by adding
`RunIntermediateAsync` and `RunPassRunnerAsync` helpers that skip the hash-rename.

