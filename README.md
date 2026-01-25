# Washmachine

Washmachine is a Windows **loader builder** designed to streamline the process of composing, encoding, and packaging shellcode payloads. It combines a .NET 8 WPF front-end with a modular services layer that reads structured snippets from YAML and orchestrates the generation pipeline.

> ?? **Security notice**: Washmachine manipulates and embeds shellcode. Use it only in environments where you have explicit permission, and treat all payloads as sensitive.

---

## Key Features
- **WPF UI:** Main window (`MainWindow`) with asynchronous operations and contextual logging.
- **Coordinator pattern:** `MainFormCoordinator` owns all UI workflows, leaving the code-behind thin and test-friendly.
- **Snippet-driven generation:** YAML-backed catalog powers every drop-down and produces a composed C++ source file without needing the VX-UG repository.
- **Python integration:** Executes Bin2Shell tooling with safe argument handling and rich error reporting.
- **Path validation:** Centralised validation ensures required assets (snippet catalog, Bin2Shell scripts) exist before any generation step runs.
- **Composable logging:** Thread-safe `RichTextBoxLogger` provides consistent, colour-coded feedback inside the UI.

---

## Project Structure
```
Washmachine/
+-- Controllers/
¦   +-- MainFormCoordinator.cs      # UI workflow orchestrator
+-- Logging/
¦   +-- IAppLogger.cs
¦   +-- RichTextBoxLogger.cs        # RichTextBox-backed logger
+-- Models/
¦   +-- CodeSnippetModels.cs
¦   +-- CppCompilationPlan.cs
¦   +-- UiData.cs                   # Snapshot of WPF visual state
+-- Services/
¦   +-- AppPaths.cs                 # Centralised path resolution/validation
¦   +-- Bin2ShellRunner.cs          # Python process execution helper
¦   +-- ClipboardService.cs
¦   +-- CompilerResult.cs
¦   +-- CompilerService.cs          # Core snippet generation pipeline
¦   +-- HeaderListProvider.cs       # Header parsing utilities
¦   +-- RequirementProvisioner.cs   # Downloads optional external tools
¦   +-- ShellcodeEncodingCatalogService.cs
¦   +-- UserInteractionService.cs
+-- Views/
¦   +-- IMainFormView.cs            # Contract implemented by MainWindow
¦   +-- RequirementsProgressWindow.cs
+-- Assets/
¦   +-- vx_api_snippets.yaml        # Snippet catalog consumed at runtime
+-- MainWindow.xaml
+-- MainWindow.xaml.cs
+-- App.xaml
+-- App.xaml.cs
+-- washmachine.csproj
```

---

## Requirements
- Windows 10 or later (x64)
- [.NET SDK 8.0](https://dotnet.microsoft.com/en-us/download/dotnet)
- Python 3.x available on `PATH` for Bin2Shell (optional unless encoding shellcode)
- Snippet catalog at `Assets/vx_api_snippets.yaml`

---

## Getting Started

### 1. Clone the repository
```powershell
git clone https://github.com/<your-org>/washmachine.git
cd washmachine
```

### 2. Ensure assets exist
The coordinator verifies these locations at runtime:

- `Assets/vx_api_snippets.yaml`
- `Tools/Bin2Shell/main.py` (optional, required only for encoding)
- `Tools/Bin2Shell/data/yaml/algos.yaml`

Place or symlink the files relative to the executable directory. Update `AppPaths` if your layout differs.

### 3. Restore and build
```powershell
dotnet restore
dotnet build
```

### 4. Run the WPF application
```powershell
dotnet run --project washmachine.csproj
```

Alternatively, open the solution in Visual Studio and press **F5**.

---

## Usage Overview
1. Launch Washmachine and select exactly one shellcode source:
   - Local `.bin` file
   - Raw `\x`-escaped string
   - Payload URL
   - Generic payload from the predefined combo box
2. Choose optional protections or behaviours (anti-debugging, guard rails, UAC bypass, etc.).
3. Configure encoders/compressors/envelopes via the Bin2Shell tab (requires Python).
4. Press **Generate**. The coordinator will:
   - Validate required assets
   - Snapshot the UI state (`UiData`)
   - Encode or persist shellcode inputs
   - Compose a C++ source file from the YAML snippets
   - Save the generated file to the temp directory and display it in a viewer dialog
5. Copy the generated C++ source into your project or adjust it further as needed.

---

## Architectural Highlights
- **Coordinator-driven UI**: `MainWindow` delegates all logic to `MainFormCoordinator`, keeping WPF code-behind minimal and easing unit testing.
- **Dependency inversion**: Behaviours (logging, dialogs, clipboard, file IO) flow through interfaces (`IAppLogger`, `IUserInteractionService`, etc.). Swap implementations for tests or future platforms.
- **Async-ready**: Long-running tasks (Python invocations, file IO) run asynchronously to maintain UI responsiveness.
- **Centralised validation**: `AppPaths.Validate()` consolidates filesystem checks and surfaces actionable messages to the user.
- **Extensibility**: Add new snippet sections by extending the YAML catalog; the coordinator automatically surfaces them via `HeaderListProvider`.

---

## Extending the Project
- **Additional shellcode workflows**: Implement new strategies inside `CompilerService` or create decorator services that plug in before/after generation.
- **Alternative logging**: Implement `IAppLogger` to log to disk, structured logs, or remote sinks.
- **CLI/automation**: Reuse `CompilerService` in a console host by providing non-UI implementations of `IUserInteractionService` and `IClipboardService`.
- **Unit testing**: Mock the view and services to exercise `MainFormCoordinator` without WPF (e.g., using Moq or NSubstitute).

---

## Troubleshooting
| Issue | Resolution |
|-------|-----------|
| *"Missing Assets" dialog on startup* | Ensure the snippet catalog (and optional Bin2Shell files) exist or adjust `AppPaths`. |
| Python process fails | Verify Python 3 is installed and accessible. Check the console log for stderr output. |
| UI freezes during generation | Confirm Bin2Shell scripts finish; long-running external scripts can hold the coordinator. |
| Designer errors after renames | Rebuild the project and reopen `MainWindow.xaml` in the WPF designer. |

---

## Contributing
1. Fork the repository.
2. Create a feature branch (`git checkout -b feature/my-improvement`).
3. Commit changes with clear messages.
4. Open a pull request describing the motivation and testing performed.

Please ensure added functionality respects the existing dependency-inversion patterns and includes adequate logging.

---

## License

No license information is included. Add one before distributing or open-sourcing the project.

