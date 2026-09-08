# CLI presentation contract

This pass standardizes the shared help layer, not command execution or payload /
evasion workflows. Existing command names, option models, parser implementations,
and GUI connector code were not changed.

## Shared presentation

`UsageFormatter` now delegates text layout to `HelpWriter`. Command help, session
option details, and read-only analysis help use the same headings, spacing,
field layout, and wrapping. Examples are printed without a synthetic `>` prompt.

- Help writes forward only: it does not clear the screen, reposition the cursor,
  or start a pager. Normal terminal scrollback remains available.
- Wide terminals use aligned label/value rows. Narrow terminals stack them.
- Wrapping uses display-cell widths and text elements, not UTF-16 string length.
  Long values wrap instead of being truncated. A glyph wider than the entire
  available line is represented by `?` in extremely narrow windows.
- Internal spaces in quoted paths are retained when the text fits a line.
- Brackets are literal text, not markup. Control and directional override
  characters in displayed help data are removed.
- Console instances can be injected for testing without changing global output
  settings. TextWriter-based analysis help explicitly uses plain output.
- Width is read again for subsequent fields/paragraphs. This is not a guarantee
  of perfect visual reflow when the terminal changes during an individual write.

## Missing source recovery

This checkout referenced `TerminalLayout`, `AnalysisCommand`, and `AnalysisSession`
without containing their definitions. The terminal helper was supplied, and the
read-only analysis components were recovered from local commit `996ddcd` in the
previous workspace. Analysis help was connected to the shared formatter; analysis
argument parsing, result schema, and scan implementation were not redesigned.

## Verification

```powershell
dotnet run --project Testing/CliPresentation/CliPresentation.csproj -p:NuGetAudit=false
```

Add `-- --preview` to print a synthetic 80-column help page. The project links only
presentation and read-only analysis sources, with the existing Spectre.Console
0.49.1 dependency. It does not reference the application projects, provision tools,
or generate/execute payloads. NuGet auditing is disabled only for this isolated
local verification command, not in the application or project defaults.

Assertions cover widths 1–200, shrinking/growing width sequences, Unicode,
literal markup, control characters, long values, deterministic output, plain and
colored console isolation, bounded input geometry, analysis help, legacy `-Pe` /
`-Json` compatibility, JSON schema, stderr/exit codes, and unchanged input bytes.

## Limits

The initial presentation tests compiled only shared presentation and read-only
analysis sources. A subsequent GUI build-error repair restored the missing
`Views/ModeSelectionWindow.cs`, `Logging/DiagnosticsLogger.cs`, and
`Services/PeScanEndpoint.cs` from local commit `996ddcd`. After that restoration,
`dotnet build washmachine.csproj -c Release --no-restore --nologo` succeeded with
zero warnings and zero errors. No build-script change was needed for those errors.
The app was not launched or published during verification. GUI connectors and
dispatcher files are unchanged.
Live drag-resizing, interactive catalog tables, operational session workflows,
startup provisioning, and complex grapheme editing still require separate review.
The shared input helper preserves the logical text; it does not redesign the
existing editor's cursor movement or key bindings.
