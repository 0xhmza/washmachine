/* Frame 04 — Compile (running build with streaming log) */

function FrameCompile() {
  const { useField, useFields } = window.washState;
  const [backend, setBackend] = useField('compilationBackend');
  const [llvmPasses, setLlvmPasses] = useField('llvmObfuscationPasses');
  const meta = useFields('catalog_compilers', 'catalog_llvmPasses', 'build_running', 'build_log', 'build_lastResult');

  const compilers   = meta.catalog_compilers || [];
  const running     = !!meta.build_running;
  const logLines    = meta.build_log || [];
  const lastResult  = meta.build_lastResult || null;

  const selComp = compilers[0];
  const passes = meta.catalog_llvmPasses || [];
  const selectedPasses = llvmPasses || [];

  function togglePass(id) {
    setLlvmPasses(selectedPasses.includes(id)
      ? selectedPasses.filter(x => x !== id)
      : [...selectedPasses, id]);
  }

  const status = [
    { icon: "info", k: "compiler", v: selComp ? selComp.name : 'none' },
    { icon: "info", k: "stage",    v: running ? 'building' : (lastResult ? (lastResult.ok ? 'done' : 'failed') : 'idle') },
  ];

  return (
    <Shell active="compile" crumbs={["Compile"]} pipeActive="cmp" running={running}
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "active" }}
      status={status}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Compile</h1>
          <span className="sub">Render template → invoke compiler → produce binary.</span>
        </div>

        {/* Compiler card */}
        <Sec title="Toolchain" action={<button className="btn ghost" onClick={() => window.washState && window.washState.loadCatalogs()}><Icon name="refresh" size={12} />Re-detect</button>}>
          <div className="card">
            {compilers.length === 0 ? (
              <div style={{ color: "var(--n-6)", fontSize: 12 }}>Detecting compilers…</div>
            ) : (
              <div className="row" style={{ gap: 16, alignItems: "stretch" }}>
                {compilers.map((c, index) => (
                  <CompCard key={c.index} name={c.name} sub={c.version || ''} path={c.path || ''}
                    selected={index === 0} />
                ))}
              </div>
            )}
            <div className="div" />
            <div className="row" style={{ gap: 16 }}>
              <Field label="Backend">
                <Seg value={backend || 'Deterministic'}
                  onChange={setBackend}
                  options={[{ v: "Deterministic", l: "Deterministic" }, { v: "LlvmObfuscated", l: "LLVM Obfuscated" }]} />
              </Field>
            </div>
            {backend === 'LlvmObfuscated' && (
              <div style={{ marginTop: 16 }}>
                <Field label="LLVM passes" hint="Only registered passes exposed by the core registry are shown.">
                  <div className="row" style={{ gap: 8, flexWrap: 'wrap', marginTop: 8 }}>
                    {passes.map(p => (
                      <button key={p.id} className={'btn' + (selectedPasses.includes(p.id) ? ' primary' : '')}
                        disabled={!p.available}
                        title={p.available ? p.description : 'Pass runner metadata is unavailable'}
                        onClick={() => togglePass(p.id)}>{p.name}</button>
                    ))}
                    {passes.length === 0 && <span className="sub">No LLVM passes registered.</span>}
                  </div>
                </Field>
              </div>
            )}
          </div>
        </Sec>

        {/* Live progress */}
        <Sec title="Build" action={running
          ? <Chip kind="acc" dot>running</Chip>
          : lastResult
            ? <Chip kind={lastResult.ok ? "ok" : "err"} dot>{lastResult.ok ? "success" : "failed"}</Chip>
            : null}>
          {running || logLines.length > 0 ? (
            <div className="card">
              {logLines.map((entry, i) => (
                <LogLine key={i} msg={typeof entry === 'string' ? entry : entry.line} />
              ))}
              {running && <div><span style={{ color: "var(--acc)" }}>▍</span></div>}
            </div>
          ) : (
            <div className="card" style={{ color: "var(--n-6)", fontSize: 12, fontFamily: "var(--f-mono)" }}>
              No build started yet. Press Ctrl+B or click Build.
            </div>
          )}
        </Sec>
      </div>

      <BuildLogPreview logLines={logLines} running={running} lastResult={lastResult} />
    </Shell>
  );
}

function CompCard({ name, sub, path, selected }) {
  return (
    <div style={{
      flex: 1, padding: 14, borderRadius: 10,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-1)",
    }}>
      <div className="row" style={{ justifyContent: "space-between" }}>
        <div className="h2">{name}</div>
        {selected && <Chip kind="acc" dot>auto-selected</Chip>}
      </div>
      {sub && <div className="mono" style={{ fontSize: 11, color: "var(--n-8)", marginTop: 4 }}>{sub}</div>}
      {path && <div className="mono" style={{ fontSize: 10, color: "var(--n-6)", marginTop: 8, wordBreak: "break-all" }}>{path}</div>}
    </div>
  );
}

function BuildLogPreview({ logLines, running, lastResult }) {
  const logRef = React.useRef(null);
  React.useEffect(() => {
    if (logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight;
  }, [logLines]);

  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on">
          <Icon name="term" size={11} />build_log.txt
          {running && <span className="pulse" style={{ width: 6, height: 6, background: "var(--acc)", borderRadius: "50%", marginLeft: 4, display: "inline-block" }} />}
        </div>
        <div style={{ flex: 1 }} />
      </div>
      <div className="pbody" style={{ background: "var(--n-0)" }} ref={logRef}>
        <div className="mono" style={{ fontSize: 11, lineHeight: 1.7, padding: "14px 16px", color: "var(--n-8)" }}>
          {logLines.length > 0
            ? logLines.map((entry, i) => <LogLine key={i} msg={typeof entry === 'string' ? entry : entry.line} />)
            : <span style={{ color: "var(--n-5)" }}>Build output will stream here…</span>}
          {running && <div><span style={{ color: "var(--acc)" }}>▍</span></div>}
          {lastResult && !running && (
            <div style={{ marginTop: 10, color: lastResult.ok ? "var(--ok)" : "var(--err)" }}>
              {lastResult.ok ? `✓ Build succeeded · ${lastResult.outputPath || ''}` : `✗ Build failed · ${lastResult.error || ''}`}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function LogLine({ msg }) {
  // Parse "HH:MM:SS.mmm LEVEL rest" or plain
  const m = msg && msg.match(/^(\d{2}:\d{2}:\d{2}\.\d{3})\s+(\w+)\s+(.*)/s);
  if (m) {
    const lvlColor = { info: "var(--acc)", dbg: "var(--n-7)", warn: "var(--warn)", err: "var(--err)" }[m[2].toLowerCase()] || "var(--n-7)";
    return (
      <div style={{ display: "grid", gridTemplateColumns: "90px 36px 1fr", gap: 8 }}>
        <span style={{ color: "var(--n-6)" }}>{m[1]}</span>
        <span style={{ color: lvlColor, textTransform: "uppercase", fontSize: 10 }}>{m[2]}</span>
        <span style={{ color: "var(--n-9)" }}>{m[3]}</span>
      </div>
    );
  }
  return <div style={{ color: "var(--n-9)" }}>{msg}</div>;
}

window.FrameCompile = FrameCompile;
