# LLVM Obfuscation Passes

Plug-in passes that the **LLVM Obfuscated** compilation backend (`CompilationBackend.LlvmObfuscated`) loads at IR level via clang's `-fpass-plugin=` flag. Each one rewrites the program after the front-end parses C++ and before the back-end emits machine code.

## Pipeline

```
.cpp ──clang──▶ LLVM IR ──opt + pass plugins──▶ LLVM IR ──back-end──▶ .exe / .dll
                              ▲
                     each pass.dll plugs in here
```

`LlvmPipelineService` appends one `-fpass-plugin=<pass.dll>` per enabled pass (`/clang:-fpass-plugin=…` when going through `clang-cl`).

## Shipped passes

| Id                          | Glyph state | Purpose                                                                                 |
| --------------------------- | ----------- | --------------------------------------------------------------------------------------- |
| `bogus-control-flow`        | source      | Inserts opaque predicates + dead branches so decompilers can't trace real flow.         |
| `control-flow-flattening`   | source      | Replaces a function's CFG with one switch-dispatch loop; destroys structural analysis.  |
| `instruction-substitution`  | source      | Swaps `a+b`, `a-b`, XORs etc. for semantically equivalent but less recognizable forms.  |
| `string-obfuscation`        | source      | Encrypts string literals; emits a per-string decrypt stub that runs at load time.       |

Each directory contains:

```
pass.json         metadata read by LlvmPassRegistry (id / name / description)
pass.cpp          the pass, written against LLVM's new pass-manager API
CMakeLists.txt    builds pass.cpp → pass.dll
pass.dll          only present after a successful build
```

`LlvmPassRegistry.IsBuilt` is just `File.Exists(pass.dll)`. Until it's there the GUI labels the checkbox `(stub)` and the pipeline silently skips it with a warning instead of failing the build.

## Building

Run the unified build script and pick **"Build LLVM passes"** from the menu:

```powershell
.\build.ps1
```

Or call the underlying script directly:

```powershell
.\Assets\llvm-passes\build-all.ps1
.\Assets\llvm-passes\build-all.ps1 -Clean
.\Assets\llvm-passes\build-all.ps1 -LlvmDir "C:\Dev\llvm-sdk\lib\cmake\llvm"
```

Each pass is configured with CMake and built to `build/Release/pass.dll`, then copied next to the source as `pass.dll`.

## LLVM SDK requirement (Windows gotcha)

The official **`LLVM-x.x.x-win64.exe`** binary release only ships the C API headers and runtime, **not** `LLVMConfig.cmake` or the C++ headers needed to build pass plugins. You need a developer build. Options:

1. Build LLVM from source with `cmake --install`, then point `build-all.ps1` at `<prefix>\lib\cmake\llvm`.
2. `scoop install llvm` — Scoop's package ships the dev headers.
3. MSYS2: `pacman -S mingw-w64-x86_64-llvm`.

`build-all.ps1` auto-discovers the SDK in the common install locations and also patches `LLVMExports.cmake` to repoint the hard-coded `diaguids.lib` path at whatever Visual Studio is on this machine (LNK1181 workaround).

## Writing a new pass

1. Create `Assets/llvm-passes/<id>/` with `pass.json`, `pass.cpp`, `CMakeLists.txt`.
2. The pass **must** be a Module pass (Windows clang plugins choke on function-pass adapters — see `~/.claude/memory/llvm-pass-static-link.md`).
3. Implement `llvmGetPassPluginInfo()` and register at `PipelineStartEPCallback` or `OptimizerLastEPCallback`.
4. Rebuild via the menu; the GUI picks it up automatically (registry rescans on every page load).
