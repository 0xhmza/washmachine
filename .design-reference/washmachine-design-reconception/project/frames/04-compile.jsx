/* Frame 04 — Compile (running build with streaming log) */

function FrameCompile() {
  return (
    <Shell crumbs={["build", "compile"]} pipeActive="cmp" running
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "active" }}
      status={[
        { icon: "info", k: "compiler", v: "cl.exe · MSVC 19.39" },
        { icon: "info", k: "stage", v: "linking" },
        { icon: "info", k: "elapsed", v: "00:04.2" },
      ]}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Compile</h1>
          <span className="sub">Render template → invoke compiler → produce binary.</span>
        </div>

        {/* Compiler card */}
        <Sec title="Toolchain" action={<button className="btn ghost"><Icon name="refresh" size={12} />Re-detect</button>}>
          <div className="card">
            <div className="row" style={{ gap: 16, alignItems: "stretch" }}>
              <CompCard name="MSVC" sub="cl.exe · 19.39.33523" path="C:\\…\\VC\\Tools\\14.39.33519\\bin\\Hostx64\\x64" selected />
              <CompCard name="MinGW" sub="g++.exe · 13.2.0" path="C:\\msys64\\mingw64\\bin\\g++.exe" />
              <CompCard name="Clang" sub="clang++.exe · 18.1.4" path="C:\\Program Files\\LLVM\\bin\\clang++.exe" />
            </div>
            <div className="div" />
            <div className="row" style={{ gap: 16 }}>
              <Field label="Optimization">
                <Seg value="O2" options={[{ v: "Od", l: "Od" }, { v: "O1", l: "O1" }, { v: "O2", l: "O2" }, { v: "Os", l: "Os" }]} />
              </Field>
              <Field label="Subsystem">
                <Seg value="console" options={[{ v: "console", l: "Console" }, { v: "windows", l: "Windows" }]} />
              </Field>
              <Field label="Strip symbols">
                <Toggle on />
              </Field>
              <Field label="Static CRT">
                <Toggle on />
              </Field>
            </div>
          </div>
        </Sec>

        {/* Live progress */}
        <Sec title="Build" action={<Chip kind="acc" dot>running</Chip>}>
          <div className="card">
            <BuildStep done label="Render template" detail="247 lines · 4.1 KB" time="0.04s" />
            <BuildStep done label="Write source.cpp" detail="logging/session_20260519_001a/source.cpp" time="0.01s" />
            <BuildStep done label="Resolve includes" detail="windows.h · tlhelp32.h · intrin.h" time="0.12s" />
            <BuildStep done label="Preprocess + compile" detail="cl.exe /c /O2 /MT /GS- source.cpp" time="3.21s" />
            <BuildStep running label="Link" detail="cl.exe /Fe:20260519-4f7ba9d2.exe source.obj kernel32.lib user32.lib" />
            <BuildStep pending label="Sign artifacts" detail="sha256 · session manifest" />
            <BuildStep pending label="Post-compile (optional)" detail="clone donor resources · NOP pad" />
          </div>
        </Sec>
      </div>

      <BuildLogPreview />
    </Shell>
  );
}

function CompCard({ name, sub, path, selected }) {
  return (
    <div style={{
      flex: 1,
      padding: 14,
      borderRadius: 10,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-1)",
    }}>
      <div className="row" style={{ justifyContent: "space-between" }}>
        <div className="h2">{name}</div>
        {selected && <Chip kind="acc" dot>active</Chip>}
      </div>
      <div className="mono" style={{ fontSize: 11, color: "var(--n-8)", marginTop: 4 }}>{sub}</div>
      <div className="mono" style={{ fontSize: 10, color: "var(--n-6)", marginTop: 8, wordBreak: "break-all" }}>{path}</div>
    </div>
  );
}

function BuildStep({ done, running, pending, label, detail, time }) {
  const dot = (
    <div style={{ width: 16, height: 16, position: "relative" }}>
      {done && (
        <div style={{ width: 14, height: 14, borderRadius: "50%", background: "var(--ok-bg)", color: "var(--ok)", display: "grid", placeItems: "center" }}>
          <Icon name="check" size={9} sw={3} />
        </div>
      )}
      {running && (
        <>
          <div className="pulse" style={{ position: "absolute", inset: 1, background: "var(--acc)", borderRadius: "50%" }} />
          <div style={{ position: "absolute", inset: 1, background: "var(--acc)", borderRadius: "50%" }} />
        </>
      )}
      {pending && (
        <div style={{ width: 10, height: 10, margin: 2, borderRadius: "50%", border: "1px dashed var(--n-5)" }} />
      )}
    </div>
  );
  return (
    <div style={{ display: "grid", gridTemplateColumns: "20px 1fr auto", gap: 14, padding: "10px 0", borderBottom: "1px solid var(--n-3)", opacity: pending ? 0.5 : 1 }}>
      {dot}
      <div>
        <div className="h2" style={{ fontSize: 13 }}>{label}</div>
        <div className="mono" style={{ fontSize: 11, color: "var(--n-7)", marginTop: 2 }}>{detail}</div>
      </div>
      <div className="mono" style={{ fontSize: 11, color: running ? "var(--acc)" : "var(--n-7)" }}>
        {time || (running ? "…" : pending ? "—" : "")}
      </div>
    </div>
  );
}

function BuildLogPreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab"><Icon name="doc" size={11} />source.cpp</div>
        <div className="ptab on"><Icon name="term" size={11} />build_log.txt <span className="pulse" style={{ width: 6, height: 6, background: "var(--acc)", borderRadius: "50%", marginLeft: 4 }} /></div>
        <div className="ptab"><Icon name="layers" size={11} />artifact</div>
        <div style={{ flex: 1 }} />
        <div className="ptab"><Icon name="dl" size={11} /></div>
      </div>
      <div className="pbody" style={{ background: "var(--n-0)" }}>
        <div className="mono" style={{ fontSize: 11, lineHeight: 1.7, padding: "14px 16px", color: "var(--n-8)" }}>
          <LogLine ts="14:22:01.082" lvl="info"  msg="› compile session_20260519_001a started" />
          <LogLine ts="14:22:01.103" lvl="dbg"   msg="catalog: Assets/vx_api_snippets.yaml (1 file, 248 KB)" />
          <LogLine ts="14:22:01.110" lvl="dbg"   msg="template: full-loader · placeholders=5" />
          <LogLine ts="14:22:01.142" lvl="info"  msg="rendered source.cpp · 247 lines · 4.1 KB · sha256=2c9e…b1" />
          <LogLine ts="14:22:01.150" lvl="info"  msg="SGN preprocess: 2 passes · max=64 (327 B → 481 B)" />
          <LogLine ts="14:22:01.842" lvl="dbg"   msg="Bin2Shell: encoder=1 envelope=1 key=9fa24cd7" />
          <LogLine ts="14:22:02.011" lvl="info"  msg="encoded: 481 B → 789 B (Base64)" />
          <LogLine ts="14:22:02.014" lvl="info"  msg="spawn cl.exe /c /O2 /MT /GS- /GR- /EHs-c- source.cpp" />
          <LogLine ts="14:22:03.418" lvl="cl"    msg="source.cpp" />
          <LogLine ts="14:22:05.221" lvl="cl"    msg="Generating code…" />
          <LogLine ts="14:22:05.224" lvl="info"  msg="compiled source.obj · 12.4 KB" />
          <LogLine ts="14:22:05.230" lvl="info"  msg="spawn cl.exe link → 20260519-4f7ba9d2.exe" />
          <LogLine ts="14:22:05.231" lvl="cl"    msg="Microsoft (R) Incremental Linker Version 14.39.33523.0" running />
          <LogLine ts="" lvl="" msg="" cursor />
        </div>
      </div>
    </div>
  );
}

function LogLine({ ts, lvl, msg, running, cursor }) {
  if (cursor) return (
    <div style={{ marginTop: 6 }}>
      <span style={{ color: "var(--acc)" }}>▍</span>
    </div>
  );
  const lvlColor = {
    info: "var(--acc)", dbg: "var(--n-7)", cl: "var(--n-8)", warn: "var(--warn)", err: "var(--err)"
  }[lvl] || "var(--n-7)";
  return (
    <div style={{ display: "grid", gridTemplateColumns: "90px 36px 1fr", gap: 8 }}>
      <span style={{ color: "var(--n-6)" }}>{ts}</span>
      <span style={{ color: lvlColor, textTransform: "uppercase", fontSize: 10, letterSpacing: 0.06 }}>{lvl}</span>
      <span style={{ color: running ? "var(--n-10)" : "var(--n-9)" }}>{msg}</span>
    </div>
  );
}

window.FrameCompile = FrameCompile;
