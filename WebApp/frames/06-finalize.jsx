/* Frame 06 — Packing & Finalize (last-mile output processing) */

function FrameFinalize() {
  const { useField } = window.washState;
  const [donorPath, setDonorPath] = useField('DonorPathInput');
  const [cloneIcon, setCloneIcon] = useField('CloneIcon');
  const [cloneVer, setCloneVer]   = useField('CloneVersionInfo');
  const [cloneManifest, setCloneManifest] = useField('CloneManifest');
  const [cloneRsrc, setCloneRsrc] = useField('CloneRsrc');
  const [nopPad, setNopPad] = useField('NopPaddingInput');
  const [nopPattern, setNopPattern] = useField('NopPattern');
  const [appendLoc, setAppendLoc] = useField('AppendLocation');

  const nopBytes = parseInt(nopPad) || 0;
  const nopLabel = nopBytes >= 1024 * 1024
    ? `= ${(nopBytes / (1024*1024)).toFixed(1)} MiB`
    : nopBytes >= 1024
      ? `= ${(nopBytes / 1024).toFixed(1)} KiB`
      : nopBytes > 0 ? `= ${nopBytes} B` : '';

  function browseDonor() {
    window.wash.invoke('browse-file', { filters: [{ name: 'Executables', patterns: ['.exe', '.dll'] }] })
      .then(r => { if (r && r.ok && r.path) setDonorPath(r.path); })
      .catch(() => {});
  }

  const donorName = donorPath ? donorPath.split('\\').pop() : 'none';
  const status = [
    { icon: "info", k: "donor", v: donorName },
    { icon: "info", k: "padding", v: nopLabel || 'none' },
  ];

  return (
    <Shell active="finalize" crumbs={["Finalize"]} pipeActive="fn"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "done", bd: "done", pk: "done", fn: "active" }}
      status={status}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Finalize output</h1>
          <span className="sub">Clone metadata from a benign donor and adjust the file shape.</span>
        </div>

        <Sec title="Donor metadata">
          <div className="card">
            <div className="row" style={{ gap: 16, alignItems: "stretch" }}>
              <div style={{ flex: 1 }}>
                <Field label="Donor executable" hint="Source for icon, resources, and version metadata.">
                  <input className="input mono" value={donorPath}
                    onChange={e => setDonorPath(e.target.value)}
                    placeholder="C:\Windows\System32\OneDrive.exe" />
                </Field>
                <button className="btn" style={{ marginTop: 10 }} onClick={browseDonor}><Icon name="upload" size={12} />Browse…</button>
                <div className="row" style={{ marginTop: 14, gap: 12, flexWrap: "wrap" }}>
                  <CloneToggle label="Icon"             on={cloneIcon === 'True'}    onChange={v => setCloneIcon(v ? 'True' : 'False')} />
                  <CloneToggle label="Version info"     on={cloneVer === 'True'}     onChange={v => setCloneVer(v ? 'True' : 'False')} />
                  <CloneToggle label="Manifest"         on={cloneManifest === 'True'} onChange={v => setCloneManifest(v ? 'True' : 'False')} />
                  <CloneToggle label=".rsrc tree"       on={cloneRsrc === 'True'}    onChange={v => setCloneRsrc(v ? 'True' : 'False')} />
                </div>
              </div>
              <div style={{ width: 220, borderLeft: "1px solid var(--n-4)", paddingLeft: 18 }}>
                <H3>Donor</H3>
                <div className="mono" style={{ fontSize: 11, color: donorPath ? "var(--n-9)" : "var(--n-6)", marginTop: 10 }}>
                  {donorPath ? donorPath.split('\\').pop() : 'No donor selected'}
                </div>
              </div>
            </div>
          </div>
        </Sec>

        <Sec title="File shaping">
          <div className="card">
            <div className="row" style={{ gap: 16 }}>
              <Field label="NOP padding (bytes)" hint="Append zero-effect bytes to alter hash &amp; shape.">
                <div className="input-wrap">
                  <input className="input mono" value={nopPad}
                    onChange={e => setNopPad(e.target.value)}
                    style={{ width: 220 }} />
                  {nopLabel && <span style={{ position: "absolute", right: 10, top: "50%", transform: "translateY(-50%)", color: "var(--n-7)", fontSize: 11 }}>{nopLabel}</span>}
                </div>
              </Field>
              <Field label="Pattern">
                <Seg value={nopPattern || 'nop'} onChange={setNopPattern}
                  options={[{ v: "nop", l: "0x90" }, { v: "zero", l: "0x00" }, { v: "rand", l: "Random" }]} />
              </Field>
              <Field label="Append location">
                <Seg value={appendLoc || 'overlay'} onChange={setAppendLoc}
                  options={[{ v: "overlay", l: "Overlay" }, { v: "section", l: "New section" }]} />
              </Field>
            </div>
          </div>
        </Sec>
      </div>

      <FinalizePreview donorName={donorName} />
    </Shell>
  );
}

function CloneToggle({ label, on, onChange }) {
  return (
    <div className="row" style={{
      gap: 8, padding: "6px 10px", borderRadius: 6,
      background: on ? "var(--acc-bg)" : "var(--n-1)",
      border: "1px solid " + (on ? "var(--acc-line)" : "var(--n-4)"),
      cursor: "pointer",
    }} onClick={() => onChange(!on)}>
      <Toggle on={!!on} />
      <span style={{ fontSize: 12, color: on ? "var(--n-10)" : "var(--n-8)" }}>{label}</span>
    </div>
  );
}

function FinalizePreview({ donorName }) {
  const meta = window.washState.useFields('build_lastResult');
  const lastResult = meta.build_lastResult;

  function revealOutput() {
    if (lastResult && lastResult.outputPath) {
      window.wash.invoke('reveal-file', { path: lastResult.outputPath }).catch(() => {});
    }
  }

  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="layers" size={11} />artifact</div>
        <div style={{ flex: 1 }} />
      </div>
      <div className="pbody ppad">
        {lastResult && lastResult.ok ? (
          <div style={{ display: "grid", gap: 14 }}>
            <div className="card flat" style={{ padding: 14 }}>
              <H3>Output</H3>
              <div className="mono" style={{ fontSize: 12, color: "var(--n-10)", marginTop: 8 }}>
                {lastResult.outputPath || '(path unavailable)'}
              </div>
              <div className="row" style={{ marginTop: 12, gap: 8 }}>
                <button className="btn primary" onClick={revealOutput}><Icon name="dl" size={12} />Reveal in Explorer</button>
                <button className="btn" onClick={() => navigator.clipboard && navigator.clipboard.writeText(lastResult.outputPath || '')}><Icon name="copy" size={12} />Copy path</button>
              </div>
            </div>
          </div>
        ) : (
          <div style={{ color: "var(--n-6)", fontSize: 12, fontFamily: "var(--f-mono)" }}>
            Output details appear here after a successful build.
          </div>
        )}
      </div>
    </div>
  );
}

window.FrameFinalize = FrameFinalize;

