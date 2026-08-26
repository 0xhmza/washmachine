/* Frame 01 — Workspace / Source stage
   The main build view, default landing after open */

function FrameWorkspace() {
  const { useField } = window.washState;
  const [sourceKind, setSourceKind] = useField('sourceKind');
  const [shellcodeFile, setShellcodeFile] = useField('shellcodeFileInput');
  const [shellcodeRaw, setShellcodeRaw]   = useField('shellcodeRawInput');
  const [shellcodeUrl, setShellcodeUrl]   = useField('shellcodeUrlValue');

  // Analysis meta
  const { useFields } = window.washState;
  const analysis = useFields(
    'analysis_size', 'analysis_sizeText', 'analysis_arch',
    'analysis_entropy', 'analysis_sha256', 'analysis_bytesPreview'
  );

  // Trigger analysis whenever the file path changes
  React.useEffect(() => {
    if (sourceKind !== 'file' || !shellcodeFile) return;
    window.wash.invoke('analyze-shellcode', { path: shellcodeFile })
      .then(r => {
        if (!r || !r.ok) return;
        window.washState.update({
          analysis_size:         r.size,
          analysis_sizeText:     r.sizeText,
          analysis_arch:         r.arch,
          analysis_entropy:      String(r.entropy),
          analysis_sha256:       r.sha256,
          analysis_bytesPreview: r.bytesPreview || '',
        });
      })
      .catch(() => {});
  }, [shellcodeFile, sourceKind]);

  const statusArch = analysis.analysis_arch || '—';
  const statusSize = analysis.analysis_sizeText || '—';
  const status = [
    { icon: "info", k: "src",  v: shellcodeFile ? (shellcodeFile.split('\\').pop() || shellcodeFile) + (statusSize !== '—' ? ' · ' + statusSize : '') : 'none' },
    { icon: "info", k: "arch", v: statusArch },
  ];

  // Entropy display
  const entropyVal = analysis.analysis_entropy ? `${analysis.analysis_entropy} / 8.0` : '—';
  const entropyHigh = parseFloat(analysis.analysis_entropy) > 7.0;
  const sha256Short = analysis.analysis_sha256 ? analysis.analysis_sha256.slice(0, 4) + '…' + analysis.analysis_sha256.slice(-4) : '—';

  function handleBrowse() {
    window.wash.invoke('browse-file', { filters: [{ name: 'Shellcode binary', patterns: ['.bin', '.raw', '.exe', '.dll', '*'] }] })
      .then(r => { if (r && r.ok && r.path) setShellcodeFile(r.path); })
      .catch(() => {});
  }

  function handleRecompute() {
    if (shellcodeFile) setShellcodeFile(shellcodeFile + ''); // re-trigger useEffect
  }

  // Hex preview lines
  const hexLines = React.useMemo(() => {
    const raw = analysis.analysis_bytesPreview || '';
    return raw.split('\n').filter(Boolean);
  }, [analysis.analysis_bytesPreview]);

  return (
    <Shell active="payload" crumbs={["Payload"]} pipeActive="src" status={status}>
      {/* MIDDLE: source configuration */}
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Shellcode source</h1>
          <span className="sub">Pick one input. Everything downstream rebuilds when this changes.</span>
        </div>

        <Sec title="Input mode"
          action={<Seg value={sourceKind} onChange={setSourceKind} options={[
            { v: "file", l: "File",   icon: "file" },
            { v: "raw",  l: "Raw",    icon: "term" },
            { v: "url",  l: "URL",    icon: "globe" },
            { v: "test", l: "Test",   icon: "beaker" },
          ]} />}
        >
          <div className="card">
            <div className="row" style={{ alignItems: "stretch", gap: 16 }}>
              {/* Input area by mode */}
              <div style={{ flex: "1.4 1 0", display: "flex", flexDirection: "column", gap: 10 }}>
                {sourceKind === 'file' && <>
                  <Field label="Shellcode .bin path">
                    <input className="input mono" value={shellcodeFile}
                      onChange={e => setShellcodeFile(e.target.value)}
                      placeholder="C:\payloads\shellcode.bin" />
                  </Field>
                  <div className="row" style={{ gap: 8 }}>
                    <button className="btn" onClick={handleBrowse}><Icon name="upload" size={12} />Browse…</button>
                    <button className="btn ghost" onClick={handleRecompute}><Icon name="refresh" size={12} />Recompute hash</button>
                    <div style={{ flex: 1 }} />
                    {analysis.analysis_size != null && <Chip kind="ok" dot>analysed</Chip>}
                  </div>
                </>}
                {sourceKind === 'raw' && <>
                  <Field label="Shellcode hex" hint="space-separated or continuous hex bytes">
                    <textarea className="input mono" rows={4} value={shellcodeRaw}
                      onChange={e => setShellcodeRaw(e.target.value)}
                      placeholder="fc 48 83 e4 f0 e8 c0 00 ..."
                      style={{ resize: "vertical", fontFamily: "var(--f-mono)", fontSize: 11 }} />
                  </Field>
                </>}
                {sourceKind === 'url' && <>
                  <Field label="Shellcode URL" hint="http(s) — fetched at build time">
                    <input className="input mono" value={shellcodeUrl}
                      onChange={e => setShellcodeUrl(e.target.value)}
                      placeholder="https://your-server.com/shell.bin" />
                  </Field>
                </>}
                {sourceKind === 'test' && <>
                  <div style={{ color: "var(--n-7)", fontSize: 13, padding: "8px 0" }}>
                    Use the active playbook's generic shellcode template. No file required.
                  </div>
                </>}
              </div>

              {/* Inline analysis */}
              <div style={{ flex: 1, borderLeft: "1px solid var(--n-4)", paddingLeft: 16, display: "flex", flexDirection: "column", gap: 8 }}>
                <H3>Detected</H3>
                {analysis.analysis_size != null ? (
                  <div className="mono" style={{ fontSize: 11, color: "var(--n-8)", lineHeight: 1.85 }}>
                    size      <span style={{ color: "var(--n-10)" }}>{analysis.analysis_sizeText}</span>{"\n"}
                    arch      <span style={{ color: "var(--n-10)" }}>{analysis.analysis_arch}</span>{"\n"}
                    entropy   <span style={{ color: "var(--n-10)" }}>{entropyVal}</span>
                    {entropyHigh && <span style={{ color: "var(--warn)" }}> high</span>}{"\n"}
                    sha256    <span style={{ color: "var(--n-9)" }}>{sha256Short}</span>
                  </div>
                ) : (
                  <div className="mono" style={{ fontSize: 11, color: "var(--n-6)", lineHeight: 1.85 }}>
                    {sourceKind === 'file' ? (shellcodeFile ? 'analysing…' : 'no file selected') : 'analysis available for file mode'}
                  </div>
                )}
              </div>
            </div>

            {hexLines.length > 0 && <>
              <div className="div" />
              <div>
                <H3>First 64 bytes</H3>
                <div className="bytes" style={{ marginTop: 8 }}>
                  {hexLines.map((line, i) => {
                    const m = line.match(/^([0-9a-f]+)\s+(.*)/);
                    if (!m) return <div key={i}>{line}</div>;
                    return (
                      <div key={i}>
                        <span className="o">{m[1]}  </span>
                        <span>{m[2]}</span>
                      </div>
                    );
                  })}
                </div>
              </div>
            </>}
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
          <span className="mono" style={{ fontSize: 11, color: "var(--n-7)" }}>Preview available after first build</span>
          <div style={{ flex: 1 }} />
          <Chip kind="acc" dot>auto-rebuild</Chip>
        </div>
        <div style={{ padding: "20px 14px", color: "var(--n-6)", fontSize: 12, fontFamily: "var(--f-mono)" }}>
          Build the payload to see the rendered source.cpp here.
        </div>
      </div>
    </div>
  );
}

window.FrameWorkspace = FrameWorkspace;
window.PreviewPane = PreviewPane;

