# Washmachine

Shellcode loader builder with a WinUI 3 desktop interface.  
Generates C++ source from a YAML-driven template catalog, compiles it with any available toolchain, and produces a standalone executable.

## Features

| Feature | Description |
|---|---|
| **Multiple shellcode sources** | Load from `.bin` file, raw hex, URL (web delivery), or built-in test payloads |
| **Web payload wizard** | Guided flow to configure encoding, envelope, and web helper via Bin2Shell, then verify the hosted payload URL |
| **Template engine** | Single YAML catalog (`Assets/vx_api_snippets.yaml`) defines C++ templates and pluggable snippet sections — anti-debugging, evasion, guardrails, process injection, shellcode execution, UAC bypass |
| **Native compilation** | Auto-detects MSVC (`cl.exe`), GCC (`g++.exe`), or Clang (`clang++.exe`) and compiles the generated source |
| **Payload encoding** | Bin2Shell integration for encoding and envelope wrapping of shellcode payloads |

## Requirements

| Component | Version |
|---|---|
| OS | Windows 10 1809+ / Windows 11 |
| .NET | 8.0 Desktop Runtime (x64) |
| Windows App SDK | 1.8 Runtime |
| C++ Compiler | Any of: MSVC (via VS Build Tools), MinGW-w64 `g++`, or `clang++` on PATH |
| Python | 3.10+ (for Bin2Shell encoding features) |

## Quick start

```
dotnet build
```

Run from the output directory or launch from Visual Studio.

## Publishing (framework-dependent)

```
dotnet publish -c Release
```

Output goes to `Output\Release\publish\`.  
The binary requires .NET 8 Desktop Runtime and Windows App SDK Runtime on the target machine — no bundled runtime, keeps the package small.

## Project layout

```
Controllers/     UI coordinators (MVVM-ish)
Logging/         Logger abstractions
Models/          Data models, UI data, compilation plans
Services/        Bin2Shell runner, compiler, snippet catalog, clipboard
Views/           WinUI pages and wizard windows
Assets/          YAML template + snippet catalog, app icons
Testing/         Headless test harness and runner script
```

### Key files

| File | Purpose |
|---|---|
| `Assets/vx_api_snippets.yaml` | Single source of truth for C++ templates and snippet definitions |
| `Services/CompilerService.cs` | Renders templates, invokes Bin2Shell, compiles C++ |
| `Services/YamlCodeSnippetCatalogService.cs` | Parses the YAML catalog into templates and sections |
| `Controllers/MainFormCoordinator.cs` | Wires UI events to services |

## Testing

The project includes a headless combinatorial test harness that exercises encoding, template, and snippet permutations:

```powershell
# From repo root — requires a .bin shellcode file
.\Testing\run_tests.ps1 -ShellcodeFile path\to\messagebox.bin
```

Results are written to `test_results.json` in the output directory.

## License

See repository for license details.
