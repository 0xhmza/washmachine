# CLI Interaction Mode Specification

> Agent directive: this document defines how the CLI must behave. Treat it as authoritative. Do not deviate, do not invent behavior not described here, and do not skip validation steps.

---

## 1. Purpose

The CLI supports **two interaction styles** for configuring and executing a build:

1. **One-liner mode** — the user supplies all arguments in a single command.
2. **Interactive mode** — the user configures parameters step-by-step in a persistent session, inspired by the Metasploit Framework console (`msfconsole`).

Both modes must be fully supported and seamlessly interchangeable. A user who starts in one-liner mode with partial arguments must be dropped into interactive mode for the rest — never rejected, never auto-filled with guesses.

---

## 2. Mode Detection

Detect the mode from the first user input:

| Input pattern | Mode |
|---|---|
| Command + flags/args (e.g. `build --target X --mode Y`) | **One-liner** |
| All required args present and valid | One-liner → build immediately |
| Some required args missing or invalid | One-liner → fall through to interactive for remainder |
| Bare command (e.g. `build`, `configure`, or tool name alone) | **Interactive** |
| No arguments / empty invocation | Interactive |

**Rule:** never silently fill a missing required parameter with a guess. Never proceed with an invalid value. Either the user provided it correctly, or you ask.

---

## 3. Mode 1 — One-liner

### 3.1 Behavior

1. Parse all supplied arguments.
2. Print a confirmation table showing every parsed parameter and its value.
3. Validate every value against its spec (type, format, range, allowed values).
4. If all required parameters are present and valid → execute immediately.
5. If any required parameter is missing or invalid → enter interactive mode **only for the missing/broken fields**. Do not re-ask for fields that were already validly supplied.

### 3.2 Confirmation table format

Print the parsed parameters before executing. Use the same responsive table format defined in §5.

### 3.3 Error reporting

If a value is invalid, report:
- the parameter name,
- the value received,
- why it is invalid (expected type/format/range/allowed values),
- an example of a valid value.

Do not proceed past an invalid value without user correction.

---

## 4. Mode 2 — Interactive (Metasploit-style)

### 4.1 Session grammar

The interactive session is a persistent configuration shell. Recognized commands:

| Command | Effect |
|---|---|
| `show options` | Reprint the full options table with current state |
| `set <OPTION> <VALUE>` | Set a parameter; validate immediately |
| `unset <OPTION>` | Clear a parameter (reverts to default if one exists, else unset) |
| `get <OPTION>` | Print the current value of one parameter |
| `help` | Print a description of each parameter with accepted formats and examples |
| `help <OPTION>` | Print detailed help for one parameter |
| `reset` | Clear all user-set values; restore defaults |
| `build` | Validate all required fields and execute |
| `exit` / `quit` | Leave the session |

Command parsing must be case-insensitive. Parameter names must be displayed in `UPPER_SNAKE_CASE` for visual consistency with Metasploit conventions.

### 4.2 Session flow

**Step 1 — Greeting + options table.**
On entering interactive mode, immediately print the options table. Do not ask the user a question first. The table is the entry point.

**Step 2 — Wait for commands.**
Do not auto-advance through parameters. Do not run a wizard. The user drives via `set` commands.

**Step 3 — Per-`set` acknowledgement.**
After each `set`:
- If valid: print `[+] <OPTION> => <VALUE>` on one line. Optionally reprint the affected row.
- If invalid: print `[-] <OPTION>: <reason>. Expected: <format/range/allowed values>. Example: <example>`. Do not store the invalid value.

**Step 4 — `build` validation.**
On `build`:
- Check every required parameter is set and valid.
- If any required field is missing: print a clear block listing exactly which required fields are still needed, with their descriptions. Do **not** execute.
- If all required fields are valid: print a final pre-build summary (full options table), then execute.

**Step 5 — Persistent state.**
All `set` values persist for the entire session until `unset`, `reset`, or `exit`. A failed `build` never clears state — the user can fix one field and retype `build`.

---

## 5. Responsive Output

All tabular and boxed output must adapt to the terminal dimensions. This is non-negotiable — hardcoded widths break on narrow terminals and waste space on wide ones.

### 5.1 Detecting width

Use, in order of preference:
1. The platform's terminal size API (`os.get_terminal_size()` in Python, `process.stdout.columns` in Node, `tput cols` in shell, `$COLUMNS`, `GetConsoleScreenBufferInfo` on Windows).
2. Fall back to `80` columns if detection fails.
3. Re-measure on every full table render — do not cache across renders, because the user may resize the terminal between commands.

### 5.2 Layout rules

- **Minimum supported width:** 60 columns. Below that, switch to a compact vertical layout (one parameter per block, no table borders).
- **Between 60 and 100 columns:** render a compact table — truncate the `DESCRIPTION` column with an ellipsis (`…`) if needed, and let the user run `help <OPTION>` for the full text.
- **At or above 100 columns:** render the full table with all columns at full width.
- **Column sizing:** compute column widths proportionally from the available width, honoring a minimum of 4 characters per column. Never let a table exceed the terminal width — wrap or truncate instead.
- **Long values:** wrap values in the `CURRENT VALUE` column across multiple lines within the cell, aligning continuation lines under the value, rather than overflowing the row.
- **Unicode box-drawing** (`╔ ╗ ╚ ╝ ║ ═ ╠ ╣ ╦ ╩ ╬`) by default. If `NO_COLOR` is set or the terminal does not support UTF-8 (detect via locale / `chcp` on Windows), fall back to ASCII (`+ - | =`).
- **Color:** use color for status indicators (`[+]` green, `[-]` red, `[*]` cyan, `[!]` yellow) when the terminal supports it. Respect `NO_COLOR`. Never rely on color alone to convey meaning — always pair with a text prefix.

### 5.3 Reference table format (≥100 cols)

```
╔════════════════════╦══════════════════════╦══════════╦════════════════════════════════╗
║ OPTION             ║ CURRENT VALUE        ║ REQUIRED ║ DESCRIPTION                    ║
╠════════════════════╬══════════════════════╬══════════╬════════════════════════════════╣
║ TARGET             ║ (not set)            ║ yes      ║ <concise description>          ║
║ MODE               ║ default              ║ no       ║ <concise description>          ║
║ OUTPUT_FORMAT      ║ (not set)            ║ yes      ║ <concise description>          ║
╚════════════════════╩══════════════════════╩══════════╩════════════════════════════════╝

Type `set <OPTION> <VALUE>` to configure. `show options` to reprint. `build` to execute.
```

### 5.4 Compact table format (60–99 cols)

```
+----------------+---------------+-----+------------------------+
| OPTION         | VALUE         | REQ | DESCRIPTION            |
+----------------+---------------+-----+------------------------+
| TARGET         | (not set)     | yes | <truncated…>           |
| MODE           | default       | no  | <truncated…>           |
| OUTPUT_FORMAT  | (not set)     | yes | <truncated…>           |
+----------------+---------------+-----+------------------------+
```

### 5.5 Vertical fallback (<60 cols)

```
OPTION: TARGET
  value:    (not set)
  required: yes
  desc:     <full description, wrapped to terminal width>

OPTION: MODE
  value:    default
  required: no
  desc:     <full description, wrapped to terminal width>
```

---

## 6. Validation Rules

Every parameter must have a declared spec including:

- **Name** (`UPPER_SNAKE_CASE`)
- **Type** (string / int / float / bool / enum / path / url / ip / cidr / regex-matched)
- **Required** (yes/no)
- **Default** (if any — shown in the options table)
- **Accepted format/range/allowed values**
- **Description** (one-line for the table, extended for `help <OPTION>`)
- **Example value**

Rules:

1. Validation runs on every `set` and again at `build`-time (defense in depth).
2. Never accept an out-of-spec value. Reject with a specific reason.
3. Defaults are allowed **only** if explicitly declared in the spec. Never invent a default.
4. Never silently coerce types. If the user supplies `"yes"` for a bool, accept it explicitly because the spec says so, not by accident.
5. Paths must be checked for existence/writability where applicable before `build`.
6. Network values (URLs, IPs, CIDRs) must be syntactically validated, not just non-empty.

---

## 7. Status Prefixes (Metasploit Convention)

Use these prefixes consistently across all output:

| Prefix | Meaning | Typical color |
|---|---|---|
| `[+]` | Success / value set | green |
| `[-]` | Failure / invalid value | red |
| `[*]` | Informational / progress | cyan |
| `[!]` | Warning | yellow |
| `[?]` | Prompt / question | white/bold |

---

## 8. Error Handling

- Unknown command → `[-] Unknown command: <cmd>. Type 'help' for available commands.`
- Unknown option in `set` → `[-] Unknown option: <name>. Type 'show options' to see available options.`
- Ambiguous partial match → list all matches and ask for disambiguation. Never guess.
- Fatal error during `build` → print the error, preserve session state, return to the prompt.

---

## 9. Session Examples

### 9.1 One-liner success

```
$ tool build --target 10.0.0.5 --mode fast --output-format json

[*] Parsing arguments...
[+] TARGET        => 10.0.0.5
[+] MODE          => fast
[+] OUTPUT_FORMAT => json
[*] All required parameters valid. Building...
[+] Build complete. Output: ./out/result.json
```

### 9.2 One-liner with missing field → fall through

```
$ tool build --target 10.0.0.5

[*] Parsing arguments...
[+] TARGET => 10.0.0.5
[!] Missing required parameter(s): OUTPUT_FORMAT
[*] Entering interactive mode for remaining parameters.

<options table printed>

> set output_format json
[+] OUTPUT_FORMAT => json
> build
[*] All required parameters valid. Building...
[+] Build complete. Output: ./out/result.json
```

### 9.3 Pure interactive

```
$ tool

<options table printed>

> set target 10.0.0.5
[+] TARGET => 10.0.0.5
> set mode aggresive
[-] MODE: invalid value 'aggresive'. Expected one of: fast, normal, aggressive. Example: aggressive
> set mode aggressive
[+] MODE => aggressive
> build
[-] Missing required parameter(s): OUTPUT_FORMAT
> set output_format json
[+] OUTPUT_FORMAT => json
> build
[*] Final configuration:
<options table printed>
[*] Building...
[+] Build complete. Output: ./out/result.json
```

---

## 10. Implementation Checklist

Before shipping, verify:

- [ ] One-liner mode parses all documented arguments correctly.
- [ ] One-liner mode with partial args falls through to interactive for the rest.
- [ ] Interactive mode prints the options table on entry, unprompted.
- [ ] `show options`, `set`, `unset`, `get`, `help`, `help <OPTION>`, `reset`, `build`, `exit` all work.
- [ ] State persists across failed `build` attempts within the session.
- [ ] Every parameter has a full spec (type, required, default, format, description, example).
- [ ] Validation runs on `set` AND on `build`.
- [ ] Never silently fills, coerces, or invents a value.
- [ ] Table renders correctly at terminal widths of 40, 60, 80, 120, 200 columns.
- [ ] Terminal resize between commands is re-detected on next render.
- [ ] `NO_COLOR` is respected.
- [ ] ASCII fallback works on non-UTF-8 terminals (test on legacy Windows `cmd.exe`).
- [ ] Status prefixes (`[+] [-] [*] [!] [?]`) are used consistently.
- [ ] All error messages include the offending value and an example of a valid one.

---

## 11. Anti-patterns (Do Not Do)

- ❌ Asking a question before showing the options table in interactive mode.
- ❌ Hardcoding column widths (e.g. `printf("%-20s", ...)`).
- ❌ Accepting an invalid value and "fixing" it silently.
- ❌ Forgetting parameters on a failed `build`.
- ❌ Inventing a default for a parameter that doesn't declare one.
- ❌ Running a forced linear wizard instead of a free-form shell.
- ❌ Mixing `set TARGET=x` and `set TARGET x` syntaxes inconsistently — pick one (`set <OPTION> <VALUE>`, space-separated) and stick to it.
- ❌ Using color as the only signal for success/failure.
