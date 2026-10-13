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
  characters in displayed help data are removed. Complete CSI/OSC/DCS terminal
  sequences are discarded rather than leaving their parameters visible. CRLF
  and CR are normalized to line breaks.
- Console instances can be injected for testing without changing global output
  settings. TextWriter-based analysis help explicitly uses plain output.
- Width is read again for subsequent fields/paragraphs. This is not a guarantee
  of perfect visual reflow when the terminal changes during an individual write.

## Terminal defaults

The default `terminal` palette uses the terminal's own foreground and basic
semantic colors. It does not assume a dark background or request RGB sequences.
Explicit named themes, including `dracula`, remain available for saved user
preferences. Invalid color values fall back to the terminal foreground.

Default help output is plain when stdout is redirected, `NO_COLOR` is nonempty,
or `TERM=dumb`, even if a CI host advertises ANSI support. Explicitly injected
consoles retain their own capability settings. These guarantees apply to the
shared help layer; other command-specific renderers have not been migrated.

Wrapping uses a simple word scanner rather than regex matches, appends words
that fit without allocating a string per grapheme, and streams aligned rows.
Input viewport geometry works with graphemes and snaps a cursor inside a
surrogate/combining sequence to the start of that sequence. This helper does not
change the command editor's logical cursor movement or key bindings.

## Consistent list presentation

Option lists and value-choice lists now share `CatalogWriter`. Wide terminals
show a compact table without decorative boxes. Narrow terminals show labeled
fields using the same help renderer. Values wrap instead of being replaced by
ellipses, and rows use literal text rather than interpreting data as markup.
Titles, headings, footers, IDs, and required labels follow one presentation
contract. Empty lists use the same message and keep their footer visible.

`Program.CatalogUi.cs` contains presentation adapters only. It preserves the
IDs, order, values, descriptions and override labels provided by existing
callers, including blank IDs for secondary rows. Per-tool color overrides are
no longer used; explicit semantic labels carry the meaning. Parsing, option
resolution, state transitions, command vocabulary and execution remain outside
this interface refactor.

The welcome text uses the same output policy and wrapping as help and lists.
Plain output also explicitly disables ANSI capabilities after console creation:
in Spectre.Console 0.49, CI enrichment can otherwise re-enable formatting.

## Missing source recovery

This checkout referenced `TerminalLayout`, `AnalysisCommand`, and `AnalysisSession`
without containing their definitions. The terminal helper was supplied, and the
read-only analysis components were recovered from local commit `996ddcd` in the
previous workspace. Analysis help was connected to the shared formatter; analysis
argument parsing, result schema, and scan implementation were not redesigned.

## Verification

```powershell
dotnet run --project Testing/CliPresentation/CliPresentation.csproj
dotnet run --project Testing/CliPresentation/CliPresentation.csproj -c Release
```

Add `-- --preview` to print a synthetic 80-column help page. The project links only
presentation and read-only analysis sources, with the existing Spectre.Console
0.49.1 dependency. It does not reference the application projects, provision tools,
or generate/execute payloads. Dependency auditing remains enabled.

Assertions cover widths 1–200, shrinking/growing width sequences, Unicode,
literal markup, control characters, long values, deterministic output, plain and
colored console isolation, bounded input geometry, analysis help, legacy `-Pe` /
`-Json` compatibility, JSON schema, stderr/exit codes, and unchanged input bytes.
Additional checks exercise graphemes at every UTF-16 cursor offset, wide Unicode
spaces, complete terminal sequences, malformed color/Unicode values, large help
texts, and basic/256/true-color capability profiles. Real subprocess probes test
stdout redirection, `NO_COLOR`, `TERM=dumb`, CI color detection, JSON error output,
and process exit codes. They launch only the isolated verification executable.
The isolated project also links the actual list presentation adapters. Synthetic
data tests exercise narrow and wide layouts, large field values, stable supplied
IDs, secondary rows, required-label overrides, literal brackets, empty lists,
dynamic widths between renders, styled/plain output, one-pass row enumeration,
and rejection of malformed rows before writing partial output. They do not load
or run the operational session implementations.

## Research and design choices

- [Command Line Interface Guidelines](https://clig.dev/): consistent interfaces,
  readable help, restrained color and composable output.
- [Microsoft console guidance](https://learn.microsoft.com/en-us/dotnet/standard/building-console-apps):
  standard input, output and error are distinct streams that can be redirected.
- [Spectre.Console capabilities](https://spectreconsole.net/console/reference/capabilities-reference):
  adapt to detected ANSI, color, Unicode and interaction capabilities; CI does
  not imply a human is present.
- [NO_COLOR convention](https://no-color.org/): a nonempty variable requests that
  applications omit automatically added color.

The implementation uses the project's existing .NET 8 and Spectre.Console
dependency. No new rendering framework or production dependency was added.

## Limits

The initial presentation tests compiled only shared presentation and read-only
analysis sources. A subsequent GUI build-error repair restored the missing
`Views/ModeSelectionWindow.cs`, `Logging/DiagnosticsLogger.cs`, and
`Services/PeScanEndpoint.cs` from local commit `996ddcd`. After that restoration,
`dotnet build washmachine.csproj -c Release --no-restore --nologo` succeeded with
zero warnings and zero errors. No build-script change was needed for those errors.
The app was not launched or published during verification. GUI connectors and
dispatcher files are unchanged.
The current checks run on Windows .NET 8 and simulate console capabilities.
They do not establish support for Linux/macOS application execution, actual
legacy terminal hosts, live drag-resizing, or the existing editor's key bindings.
List layout is tested with synthetic data and simulated console widths rather
than a live operational session. Operational session workflows and startup
provisioning are outside this presentation/read-only pass.
