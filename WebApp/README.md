# WebApp/ — WebView2 frontend

Static SPA that implements the Washmachine design in HTML/CSS/JS. Hosted by
`Views/WebShellWindow.xaml` over a virtual `https://washmachine.local/` origin.

## Layout

- `app.html` — entry point. Loads vendored React, pre-compiled frames, and `app.js`.
- `app.js` — router + native bridge (`window.wash.invoke`, `setRoute`, etc.).
- `system.css` — design tokens (mirrored from the design bundle).
- `shell.jsx` — design source: Rail / Pipeline / TitleBar / StatusBar primitives + Shell wrapper.
- `frames/*.jsx` — design source: one component per page, including the standalone read-only PE scanner.
- `dist/*.js` — **build output**. Pre-compiled JSX. This is what the runtime loads.
- `vendor/*.js` — pinned React 18 production builds (offline; no CDN).
- `build.cjs` — rebuilds `dist/` from `*.jsx` (run after editing any frame).

## Rebuilding after edits

```pwsh
cd WebApp
npm ci
npm test
npm run build
```

`*.jsx` sources are checked in, `dist/` is regenerated. Editing `.jsx` without
running the build will not change runtime behavior.
`npm test` first checks that shipped JavaScript matches the JSX sources, and
fails with a rebuild instruction if it is stale. After editing JSX, run
`npm run build` before `npm test` and before building/publishing the Windows host.

The csproj copies only `dist/`, `vendor/`, `app.html`, `app.js`, `state.js`, and
`system.css` to the output folder — `*.jsx`, `node_modules/`, and `build.cjs`
are dev-only and excluded.

## Performance choices

- **No Babel at runtime.** JSX is pre-compiled to plain JS, saving the 3 MB Babel
  download and the ~200 ms parse cost on every cold start.
- **Vendored React.** No CDN round-trip; React is bundled at known versions.
- **Virtual host mapping.** Assets load via `https://washmachine.local/`
  (in-process), not `file://`.
- **Per-user WebView2 cache.** Stored under
  `%LOCALAPPDATA%\Washmachine\WebView2\`, survives restarts.
- **CSP locked to local origin.** `default-src 'self' https://washmachine.local`.
- **No layout transitions.** Route changes animate opacity only.

## Native bridge

JS → C#:
```js
await window.wash.invoke('build', { /* args */ });
```

C# → JS (push events):
```cs
bridge.PushEvent('build-log', new { line = "compiled source.cpp" });
```
JS listens via `window.addEventListener('wash:build-log', ev => …)`.

Native calls have bounded timeouts and surface failures through the global in-app notification host. The PE scanner and Backdooring page also keep explicit analysis progress and error state so an invalid path or malformed PE cannot remain stuck on “Analysing…”.

The standalone scanner has a non-persisted `scanner_path` separate from the build
recipe, local scan results, and no build actions. Browsing a PE here does not set
the Backdooring target. Its endpoint implementation is `Services/PeScanEndpoint.cs`.

## Read-only integration checks

From the repository root:

```pwsh
dotnet run --project Testing/ReadOnly/ReadOnlyChecks.csproj
$env:WASH_PE_TEST_DLL = (Resolve-Path Testing/ReadOnly/bin/Debug/ReadOnlyChecks.dll).Path
npm --prefix WebApp test
```

Tests render the actual frontend with jsdom. They cover scanner states, all active
routes, and the production PE response serialized through a simulated WebView2
message channel. Without `WASH_PE_TEST_DLL`, the native-to-frontend integration
test is explicitly skipped. These tests never generate or execute payloads.
They do not replace manual testing of Windows file pickers and the WebView2 host.

Unused Web Wizard/startup prototypes were removed during repository cleanup;
recovery information is in `docs/repository-cleanup.md`.

Method dispatch table lives in `Services/WebShellBridge.cs`. Add a method by
adding an entry to `_methods` and a `Method_<Name>` async handler.
