# Washmachine

Washmachine is a Windows **loader builder** designed to streamline the process of composing, encoding, and packaging shellcode payloads. It combines a .NET 8 Windows Forms front-end with a modular services layer that edits native C++ templates, invokes Python tooling, and orchestrates the build lifecycle.


> ⚠️ **Security notice**: Washmachine manipulates and embeds shellcode. Use it only in environments where you have explicit permission, and treat all payloads as sensitive.

---

## Key Features
- **WinForms UI:** Modernised entry form (`MainForm`) with asynchronous operations and contextual logging.
- **Coordinator pattern:** `MainFormCoordinator` owns all UI workflows, leaving the code-behind thin and test-friendly.
- **Service layer:** Dedicated services for path resolution, shellcode encoding, C++ section editing, clipboard access, and user interactions.
- **Native project automation:** Automatically refreshes C++ templates, injects encoded payloads, and toggles guarded sections based on user input.
- **Python integration:** Executes Bin2Shell tooling with safe argument handling and rich error reporting.
- **Path validation:** Centralised validation ensures required assets (header files, templates, scripts) exist before any build step runs.
- **Composable logging:** Thread-safe `RichTextBoxLogger` provides consistent, colour-coded feedback inside the UI.

---

## Project Structure
```
Washmachine/
├── Controllers/
│   └── MainFormCoordinator.cs      # UI workflow orchestrator
├── Logging/
│   ├── IAppLogger.cs
│   └── RichTextBoxLogger.cs        # RichTextBox-backed logger
├── Models/
│   └── UiData.cs                   # Snapshot of WinForms control state
├── Services/
│   ├── AppPaths.cs                 # Centralised path resolution/validation
│   ├── Bin2ShellRunner.cs          # Python process execution helper
│   ├── ClipboardService.cs
│   ├── CompilerService.cs          # Core compilation pipeline
│   ├── CompilerResult.cs
│   ├── HeaderListProvider.cs       # Header parsing utilities
│   ├── ShellcodeEncodingCatalogService.cs
│   ├── UserInteractionService.cs
│   └── Interfaces (I*)             # Abstractions for all services
├── Views/
│   └── IMainFormView.cs            # Contract implemented by MainForm
├── MainForm.cs
├── MainForm.Designer.cs
├── MainForm.resx
├── Program.cs
└── washmachine.csproj
```

---

## Requirements
- Windows 10 or later (x64)
- [.NET SDK 8.0](https://dotnet.microsoft.com/en-us/download/dotnet) (or newer preview as configured)
- Python 3.x available on `PATH` for Bin2Shell
- Visual Studio 2022 or any IDE that supports WinForms + .NET 8
- The native resources referenced by `AppPaths` (e.g., `VX-API` headers, templates, and Bin2Shell scripts)

---

## Getting Started

### 1. Clone the repository
```powershell
git clone https://github.com/<your-org>/washmachine.git
cd washmachine
```

### 2. Ensure external assets exist
The coordinator verifies these locations at runtime:

- `VX-API-main/VX-API/Win32Helper.h`
- `VX-API-main/VX-API/main.cpp`
- `VX-API-main/VX-API/template.cpp`
- `Tools/Bin2Shell/main.py`
- `Tools/Bin2Shell/algos.yaml`

Place or symlink the files relative to the executable directory. Update `AppPaths` if your layout differs.

### 3. Restore and build
```powershell
dotnet restore
dotnet build
```

### 4. Run the WinForms application
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
3. Configure encoders/compressors/envelopes via the Bin2Shell tab.
4. Press **Compile**. The coordinator will:
   - Validate required assets
   - Snapshot the UI state (`UiData`)
   - Refresh `main.cpp` from `template.cpp` (with backups)
   - Inject the encoded payload or URL placeholders
   - Uncomment requested C++ sections
   - Log every step in the debug pane
5. Build the native C++ project separately to produce the final executable.

---

## Architectural Highlights
- **Coordinator-driven UI**: `MainForm` delegates all logic to `MainFormCoordinator`, keeping WinForms code-behind minimal and easing unit testing.
- **Dependency inversion**: Every behaviour (logging, dialogs, clipboard, file edits) flows through interfaces (`IAppLogger`, `IUserInteractionService`, `ICppSectionEditor`, etc.). Swap implementations for tests or future platforms.
- **Async-ready**: Long-running tasks (Python invocations, file IO) run asynchronously to maintain UI responsiveness.
- **Centralised validation**: `AppPaths.Validate()` consolidates file-system checks and surfaces actionable messages to the user.
- **Extensibility**: To add a new feature (e.g., extra guard rail rules), you only need to extend a service or adjust the coordinator—UI wiring remains untouched.

---

## Extending the Project
- **Additional shellcode workflows**: Implement new strategies inside `CompilerService` or create decorator services that plug in before/after compilation.
- **Alternative logging**: Implement `IAppLogger` to log to disk, structured logs, or remote sinks.
- **CLI/automation**: Reuse `CompilerService` in a console host by providing non-UI implementations of `IUserInteractionService` and `IClipboardService`.
- **Unit testing**: Mock the view and services to exercise `MainFormCoordinator` without WinForms (e.g., using Moq or NSubstitute).

---

## Troubleshooting
| Issue | Resolution |
|-------|-----------|
| *"Missing Assets" dialog on startup* | Ensure all required files exist relative to the executable directory or adjust `AppPaths`. |
| Python process fails | Verify Python 3 is installed and accessible. Check the console log for stderr output. |
| UI freezes during compilation | Confirm Bin2Shell scripts finish; long-running external scripts can hold the coordinator. |
| Designer errors after renames | Rebuild the project and reopen `MainForm` in the WinForms designer to regenerate partial classes. |

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

