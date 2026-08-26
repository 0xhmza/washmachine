# WebApp/ — WebView2 frontend

Static SPA that implements the Washmachine design in HTML/CSS/JS. Hosted by
`Views/WebShellWindow.xaml` over a virtual `https://washmachine.local/` origin.

## Layout

- `app.html` — entry point. Loads vendored React, pre-compiled frames, and `app.js`.
- `app.js` — router + native bridge (`window.wash.invoke`, `setRoute`, etc.).
- `system.css` — design tokens (mirrored from the design bundle).
- `shell.jsx` — design source: Rail / Pipeline / TitleBar / StatusBar primitives + Shell wrapper.
- `frames/*.jsx` — design source: one component per page (Workspace, Encode, …, Pipeline).
- `dist/*.js` — **build output**. Pre-compiled JSX. This is what the runtime loads.
- `vendor/*.js` — pinned React 18 production builds (offline; no CDN).
- `build.cjs` — rebuilds `dist/` from `*.jsx` (run after editing any frame).

## Rebuilding after edits

```pwsh
cd WebApp
node build.cjs
```

`*.jsx` sources are checked in, `dist/` is regenerated. Editing `.jsx` without
running the build will not change runtime behavior.

The csproj copies only `dist/`, `vendor/`, `app.html`, `app.js`, and
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

Method dispatch table lives in `Services/WebShellBridge.cs`. Add a method by
adding an entry to `_methods` and a `Method_<Name>` async handler.
