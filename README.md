# <img src="Icon/icon-128.png" width="40" height="40" align="absmiddle" /> Washmachine

> Turn raw shellcode into a real loader, without writing a single line of C++.

Washmachine is a friendly little toolbox for red teamers, malware researchers and CTF players. You drop in some shellcode, pick a few options, hit go, and you get back a working Windows executable. That's pretty much it.

If you've ever spent a Saturday afternoon copy-pasting the same loader template, swapping out one anti-debug trick for another, recompiling, and wondering why this isn't automated yet, this tool is for you.

It comes with three interfaces over the same core:

* a local web app hosted by the desktop executable
* a WinUI desktop app with buttons and dropdowns
* a CLI with an interactive shell (think `msfconsole` vibes), for when you want to script things or work over SSH

📖 [Full documentation lives here](https://0xhmza.github.io/washmachine-docs/)

Local development: [project map](docs/README.md), [web UI build and tests](WebApp/README.md),
and [operability verification and limitations](docs/verification.md).
Repository layout, retained local data, and cleanup recovery are documented in
[repository cleanup](docs/repository-cleanup.md).

---

## What it does

You give it some shellcode (a `.bin` file, hex on the clipboard, or a URL). You pick which tricks you want baked into the loader: anti-debug, anti-sandbox, guardrails, decoys, persistence, process injection, the usual. You press build. Out comes a Windows executable that runs your payload.

Need to drop the payload into an existing program instead? There's a "backdoor" mode for that too, with five different injection styles to choose from.

Need to study a Windows binary before you touch it? The "analyze" command gives you a quick tour of its headers, sections, imports and code caves.

That's the whole pitch.

---

## Why you might like it

* **One YAML file controls everything.** All the loader templates, all the snippets, all the tricks live in `Assets/default.yaml`. When a technique gets flagged, edit the file. No recompile, no rebuild. You can even paste the file into a chatbot and ask it to invent new snippets for you.
* **Sensible defaults.** Pick the default template, hit go, and you get a working loader. The detailed knobs are there when you want them, hidden when you don't.
* **No babysitting.** If you don't have a compiler, it'll go and fetch MinGW for you. If `bin2shell` or `SGN` are missing, the `provision` command grabs them.
* **It tests itself.** Run `Testing\run_tests.ps1` and it'll cycle through every encoder, every snippet permutation, and every shellcode in the test folder, and tell you what's broken.

---

## Quick start

```powershell
# Build it
.\build.ps1 -Release

# Start the launcher and choose Web app, CLI, or WinUI GUI
.\Output\Release\washmachine.exe

# First time only: download bin2shell + sgn
.\Output\Release\washmachine.exe --cli-mode provision

# Encode some shellcode into a loader exe
.\Output\Release\washmachine.exe --cli-mode encode -s Testing\binary\shellcodes\messagebox.bin

# Drop into the interactive shell instead
.\Output\Release\washmachine.exe --cli-mode
```

Double-clicking `washmachine.exe` opens the three-interface launcher.

---

## What you can do with it

| Command | What it does |
|---|---|
| `encode`     | Wrap shellcode in a fresh loader and compile it. |
| `analyze`    | Look inside a Windows PE file (headers, sections, imports, code caves). |
| `backdoor`   | Inject shellcode into an existing PE. Five injection methods to pick from. |
| `strip`      | Extract or remove PE sections and overlays. |
| `show`       | Browse the playbook: templates, snippets, encoders, compilers. |
| `test`       | Run the automated test suite. |
| `doctor`     | Check that compilers and helper tools are present. |
| `provision`  | Download the external helpers (bin2shell, SGN). |

You can also paste a flag-less command (just `encode` on its own) and the CLI drops you into an interactive setup, like Metasploit's `msfconsole`. Tab completion works.

---

## Some examples

```powershell
# Encode with all the defaults
washmachine.exe --cli-mode encode -s payload.bin

# Pick an encoder and an envelope
washmachine.exe --cli-mode encode -s payload.bin -e 1 -v 1

# Add Shikata Ga Nai on top of bin2shell
washmachine.exe --cli-mode encode -s payload.bin --sgn --shikata-enc 2 --shikata-max 64

# Clone an icon and metadata off a donor exe, and pad the result with 1 MB of NOPs
washmachine.exe --cli-mode encode -s payload.bin --clone-from "C:\path\to\7z.exe" --pad-nops 1048576

# Backdoor an existing binary using the code-cave method
washmachine.exe --cli-mode backdoor --pe target.exe -s payload.bin -o patched.exe --method code-cave

# Quick PE analysis
washmachine.exe --cli-mode analyze target.exe
```

---

## Features at a glance

🗂️ **Multiple shellcode sources.** Load from a `.bin` file, paste hex, fetch from a URL, or use the built-in test payload.

🧙 **Web payload wizard.** A guided flow that walks you through encoding, envelope, web-fetch stager, and verifies that your hosted payload actually responds before generating the loader.

📄 **YAML playbook.** One file holds every template and snippet. Swap, tweak, or invent new techniques without touching the C# code.

🔌 **Pluggable sections.** Mix and match anti-emulation, anti-sandbox, anti-debug, anti-analysis, guardrails, decoys, UAC bypass, installation, persistence, evasion, process injection and shellcode execution. Most sections let you pick "none" if you don't want them in.

🔍 **Auto-discovered compiler.** Looks for `cl.exe`, `g++.exe`, `clang++.exe` on `PATH` and in the usual Visual Studio paths. If nothing's there, it'll fetch MinGW for you.

🧩 **Donor cloning.** Steal resources, icon and metadata from a real signed binary so your loader blends in.

🔄 **EXE to flat binary.** Compile a full loader exe, then strip it back down to a flat `.bin` so it can be re-injected somewhere else.

💉 **Five PE injection methods.** Code-cave, new-section, section-extension, text-section padding and TLS callback. Each one trades stealth for capacity differently.

🧪 **Built-in test harness.** Three-phase headless tests that exercise every encoder, every snippet combo, and a corpus of safe shellcodes including a synthetic 4 MB payload so the large-input path stays exercised.

---

## Requirements

For all three interfaces:

* Windows 10 (1809 or later) or Windows 11
* .NET 8 Runtime (x64)
* A C++ compiler somewhere on `PATH` (MSVC, MinGW or Clang). If you don't have one, MinGW gets fetched automatically on first run.
* Python 3.10+ if you want the encoding features (used by bin2shell).

The Web and WinUI interfaces also require the Windows App SDK 1.8 Runtime and the Web interface requires the WebView2 Runtime (normally installed with Windows).

That's it.

---

## Testing

Two scripts cover the automated tests:

```powershell
# Full sweep: small payload, big payload, snippet matrix, multi-shellcode
.\Testing\run_tests.ps1

# Just the small-payload encoder x envelope matrix (uses messagebox.bin)
.\Testing\run_tests.ps1 -Phase small

# Big-payload sweep (synthesises a 4 MB NOP-sled if it's not already there)
.\Testing\run_tests.ps1 -Phase big

# Hand-picked CLI parameter combinations
.\Testing\run_param_tests.ps1
```

Results land in `Testing\test_results.*.json` and `Testing\param_test_results.json`.

---

## Project layout

```
washmachine/
├── Assets/default.yaml           the playbook: templates + snippets
├── Washmachine.Core/             the brains (shared library)
├── Washmachine.Cli/              the CLI command host used by washmachine.exe
├── WebApp/                       the local WebView2 interface
├── Views/  Controllers/  ...     the launcher and WinUI interfaces
├── Testing/                      shellcodes, injectables, test scripts
└── Output/                       build artefacts (gitignored)
```

The Web, CLI, and WinUI interfaces all sit on top of `Washmachine.Core`. `washmachine.exe` is the common entry point.

---

## Heads up

This is a research tool. Use it on stuff you own, stuff you're paid to test, or in CTFs. Don't be a jerk.

---

## License

See the repository for license details.
