# Washmachine — Architecture & Delivery Guide

## Overview

Washmachine is structured as a **three-project solution**. All business logic lives in a platform-neutral core library shared by both a standalone CLI and a WinUI 3 desktop app.

```
washmachine.sln
├── Washmachine.Core     (class library, net8.0)         ← headless business logic
├── Washmachine.Cli      (console app, net8.0)           ← terminal interface
└── washmachine          (WinExe, net8.0-windows10.0.…) ← WinUI 3 desktop app
```

---

## Project Map

```
washmachine/
│
├── Washmachine.Core/                   ← shared logic, no WinUI dependency
│   ├── Logging/
│   │   ├── IAppLogger.cs               ← logger interface
│   │   └── ConsoleLogger.cs            ← stdout/stderr implementation
│   ├── Models/
│   │   ├── UIData.cs                   ← headless control-value snapshot
│   │   ├── CodeSnippetModels.cs
│   │   ├── CodeTemplateModels.cs
│   │   ├── PeAnalysisModels.cs
│   │   ├── PeBackdoorModels.cs
│   │   └── …
│   ├── Services/
│   │   ├── AppPaths.cs                 ← all filesystem path resolution
│   │   ├── CompilerService.cs          ← plan → render → compile pipeline
│   │   ├── CompilerToolLocator.cs      ← discovers cl.exe / g++ / clang++
│   │   ├── CppFileConverter.cs         ← spawns the compiler subprocess
│   │   ├── Bin2ShellRunner.cs          ← spawns python main.py
│   │   ├── Bin2ShellWebOutputParser.cs ← parses Bin2Shell -w YAML output
│   │   ├── PeAnalyzerService.cs        ← PE header / section analysis
│   │   ├── PeBackdoorService.cs        ← PE code-cave injection
│   │   ├── RequirementProvisioner.cs   ← downloads Bin2Shell if missing
│   │   ├── ProgressReporter.cs         ← IProgressReporter + ConsoleProgressReporter
│   │   ├── ShellcodeEncodingCatalogService.cs
│   │   └── YamlCodeSnippetCatalogService.cs
│   ├── Testing/
│   │   └── TestHarness.cs              ← headless combinatorial test runner
│   ├── UiStrings.cs
│   └── Washmachine.Core.csproj
│
├── Washmachine.Cli/                    ← CLI entry point, delegates to Core
│   ├── Program.cs                      ← subcommands: compile / analyze / backdoor /
│   │                                      list / provision / test
│   └── Washmachine.Cli.csproj
│
├── Assets/                             ← runtime assets (shipped with both products)
│   └── vx_api_snippets.yaml            ← YAML catalog: all templates & snippets
│
├── Controllers/
│   └── MainFormCoordinator.cs          ← GUI event ↔ service coordinator (GUI only)
├── Logging/
│   └── RichTextBoxLogger.cs            ← WinUI RichEditBox logger (GUI only)
├── Services/                           ← GUI-only services
│   ├── ClipboardService.cs
│   ├── HeaderListPopulator.cs
│   ├── UiDataFactory.cs                ← walks WinUI visual tree → UIData
│   ├── UserInteractionService.cs
│   └── WindowProgressReporter.cs       ← IProgressReporter wrapping RequirementsProgressWindow
├── Views/                              ← all WinUI pages & windows (GUI only)
│   ├── MainPage.xaml[.cs]
│   ├── BackdooringPage.xaml[.cs]
│   ├── SettingsPage.xaml[.cs]
│   ├── WebPayloadWizardWindow.cs
│   ├── RequirementsProgressWindow.cs
│   └── …
├── Testing/
│   └── run_tests.ps1                   ← PowerShell driver for TestHarness
│
├── App.xaml[.cs]
├── MainWindow.xaml[.cs]
├── Program.cs                          ← GUI entry point (WinUI bootstrap)
├── washmachine.csproj                  ← GUI project (references Core)
├── washmachine.sln
└── ARCHITECTURE.md                     ← this file
```

---

## Dependency Graph

```
washmachine (GUI) ──references──► Washmachine.Core
Washmachine.Cli  ──references──► Washmachine.Core
Washmachine.Core ──no internal project dependencies──
```

The GUI and CLI **never reference each other**. Core has no WinUI or Windows-specific dependencies.

---

## Architecture: Key Flows

### Compilation pipeline (shared, both CLI and GUI)

```
UIData (control-value snapshot)
    │
    ▼
CompilerService.CompileAsync(data)
    │
    ├─ YamlCodeSnippetCatalogService  ← resolves template + snippets from YAML
    ├─ Bin2ShellRunner                ← optional encoding (spawns python main.py)
    │
    ▼
CppCompilationPlan                    ← assembled render state
    │  SnippetIncludes[], SnippetImplementations[], CustomSnippetBlocks{}, …
    │
    ▼
RenderTemplate(plan, template)        ← substitutes all {{TOKEN}} placeholders
    │
    ▼
CppFileConverter.ConvertAsync()       ← cl.exe / g++ / clang++ subprocess
    │
    ▼
<timestamp>-<sha256>.exe              ← output binary
logging/session_*/source.cpp          ← saved source
logging/session_*/build_log.txt       ← compiler stdout/stderr
```

### GUI-specific path

```
WinUI Views ──events──► MainFormCoordinator
                │  UiDataFactory.FromVisualTree()  ← walks WinUI visual tree → UIData
                └──► CompilerService (Core)
```

### IProgressReporter pattern

`RequirementProvisioner` reports download progress through `IProgressReporter` (Core interface):
- **CLI** → `ConsoleProgressReporter` (Core) — prints to stdout
- **GUI** → `WindowProgressReporter` (GUI) — updates `RequirementsProgressWindow`

---

## Building

### Development build (all projects)

```powershell
dotnet build washmachine.sln
```

Debug outputs:
| Project | Output path |
|---|---|
| Washmachine.Core | `Washmachine.Core\bin\Debug\net8.0\` |
| Washmachine.Cli | `Output\cli\Debug\net8.0\washmachine-cli.exe` |
| washmachine (GUI) | `Output\Debug\net8.0-windows10.0.19041.0\washmachine.exe` |

### Build individual projects

```powershell
dotnet build Washmachine.Core\Washmachine.Core.csproj
dotnet build Washmachine.Cli\Washmachine.Cli.csproj
dotnet build washmachine.csproj
```

---

## Publishing (Release)

### CLI — single-file executable

```powershell
dotnet publish Washmachine.Cli\Washmachine.Cli.csproj -c Release
```

Output: `Output\cli\Release\publish\washmachine-cli.exe`

The CLI publishes as a **single executable** (framework-dependent). End-users need **.NET 8 Runtime** (`dotnet-runtime-8.0`, not the full Desktop Runtime).

### GUI — framework-dependent WinExe

```powershell
dotnet publish washmachine.csproj -c Release
```

Output: `Output\Release\publish\washmachine.exe` + supporting files

The GUI is framework-dependent. End-users need:
- **.NET 8 Desktop Runtime x64**
- **Windows App SDK 1.8 Runtime**

---

## End-Product Delivery

### What to ship

Two separately distributable products come out of the same repo:

#### 1. CLI tool (`washmachine-cli`)

Minimum required files from `Output\cli\Release\publish\`:

```
washmachine-cli.exe         ← single-file executable
Assets\
└── vx_api_snippets.yaml    ← YAML catalog (required at runtime)
```

Bin2Shell is downloaded automatically on first use of encoding features (`washmachine-cli provision`).

Runtime prerequisite: [.NET 8 Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)

#### 2. GUI desktop app (`washmachine`)

Files from `Output\Release\publish\`:

```
washmachine.exe
washmachine.dll
Assets\
└── vx_api_snippets.yaml
(supporting .dll files from dotnet publish)
```

Runtime prerequisites:
- [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Windows App SDK 1.8 Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)

### Shared runtime assets

Both products rely on `Assets/vx_api_snippets.yaml` being present **next to the executable** (resolved via `AppPaths.AssetsDirectory`). Always include the `Assets/` folder alongside the binary.

### Bin2Shell (optional, auto-provisioned)

Bin2Shell is expected at `Tools\Bin2Shell\main.py` relative to the executable. It is downloaded automatically when:
- Running `washmachine-cli provision`
- Launching the GUI for the first time (via `RequirementProvisioner`)

---

## Testing

The test harness lives in `Washmachine.Core/Testing/TestHarness.cs` and is exposed through the CLI's `test` subcommand.

### Run via PowerShell driver

```powershell
# Builds CLI if needed, then runs all phases
.\Testing\run_tests.ps1 -ShellcodeFile path\to\messagebox.bin

# Run only Phase 1 (encoder × envelope combinations)
.\Testing\run_tests.ps1 -Phase 1 -ShellcodeFile path\to\messagebox.bin

# Run only Phase 3 (multiple shellcode inputs from test assets)
.\Testing\run_tests.ps1 -Phase 3
```

### Run directly via CLI

```powershell
washmachine-cli test --shellcode path\to\messagebox.bin --phase all
washmachine-cli test --shellcode path\to\messagebox.bin --phase 1 --url http://host/payload.bin
washmachine-cli test --phase 3 --test-assets "testing assets\binary\shellcodes"
```

Results are written to `test_results.json` in the CLI executable's directory.

| Phase | What it tests |
|---|---|
| 1 | All encoder × envelope × web-helper combinations (requires shellcode + optional URL) |
| 2 | All template × snippet permutations with default (0) encoding |
| 3 | Multiple shellcode inputs from the test assets directory |
