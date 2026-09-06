/* Frame — UPX packing. Only capabilities implemented by the native pipeline are exposed. */

function FramePacking() {
  const { useField, useFields } = window.washState;
  const [enabled, setEnabled] = useField('EnablePackingToggle');
  const [upxPath, setUpxPath] = useField('UpxPathInput');
  const [compression, setCompression] = useField('UpxCompression');
  const [stripRelocs, setStripRelocs] = useField('UpxStripRelocs');
  const [keepBackup, setKeepBackup] = useField('UpxKeepBackup');
  const meta = useFields('upx_info');
  const isEnabled = enabled === 'True';

  function browseUpx() {
    window.wash.invoke('browse-file', { filters: [{ name: 'UPX executable', patterns: ['.exe'] }] })
      .then(r => { if (r && r.ok && r.path) setUpxPath(r.path); })
      .catch(() => {});
  }

  function detectUpx() {
    window.wash.invoke('detect-upx', {})
      .then(r => {
        window.washState.update({ upx_info: r });
        if (r && r.ok && r.path) setUpxPath(r.path);
      })
      .catch(() => {});
  }

  const detected = !!upxPath || !!(meta.upx_info && meta.upx_info.ok);
  return (
    <Shell active="packing" crumbs={["Packing"]} pipeActive="pk"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "done", bd: "done", pk: "active" }}
      status={[
        { icon: 'info', k: 'packer', v: 'UPX' },
        { icon: 'info', k: 'status', v: isEnabled ? (detected ? 'ready' : 'missing') : 'disabled' },
      ]}>
      <div className="cfg">
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Packing</h1>
          <span className="sub">Compress the final executable with the installed UPX tool.</span>
          <div style={{ flex: 1 }} />
          <Toggle on={isEnabled} onChange={v => setEnabled(v ? 'True' : 'False')} />
          <span style={{ fontSize: 12, color: isEnabled ? 'var(--n-9)' : 'var(--n-6)' }}>Enable</span>
        </div>

        <div style={{ opacity: isEnabled ? 1 : 0.45, pointerEvents: isEnabled ? 'auto' : 'none' }}>
          <Sec title="UPX executable" action={<Chip kind={detected ? 'ok' : 'warn'} dot>{detected ? 'available' : 'not found'}</Chip>}>
            <div className="card">
              <Field label="upx.exe path" hint="Detected from Tools, Program Files, LocalAppData, or PATH.">
                <input className="input mono" value={upxPath || ''}
                  onChange={e => setUpxPath(e.target.value)} placeholder="C:\\Tools\\upx.exe" />
              </Field>
              <div className="row" style={{ gap: 8, marginTop: 10 }}>
                <button className="btn" onClick={browseUpx}><Icon name="upload" size={12} />Browse…</button>
                <button className="btn ghost" onClick={detectUpx}><Icon name="refresh" size={12} />Re-detect</button>
              </div>
            </div>
          </Sec>

          <Sec title="Compression">
            <div className="card">
              <Field label="Level">
                <Seg value={compression || 'best'} onChange={setCompression} options={[
                  { v: 'default', l: 'Default' },
                  { v: 'best', l: 'Best' },
                  { v: 'ultra', l: 'Ultra brute' },
                ]} />
              </Field>
              <div className="row" style={{ gap: 12, marginTop: 16, flexWrap: 'wrap' }}>
                <PackOption label="Strip relocations" on={stripRelocs === 'True'} onChange={v => setStripRelocs(v ? 'True' : 'False')} />
                <PackOption label="Keep .bak backup" on={keepBackup === 'True'} onChange={v => setKeepBackup(v ? 'True' : 'False')} />
                <Chip>overlay preserved</Chip>
              </div>
            </div>
          </Sec>
        </div>
      </div>

      <div className="preview">
        <div className="ptabs"><div className="ptab on"><Icon name="pkg" size={11} />UPX</div></div>
        <div className="pbody ppad mono" style={{ color: 'var(--n-7)', fontSize: 12 }}>
          {isEnabled
            ? detected ? `Will pack the final artifact with ${upxPath}.` : 'Select upx.exe before building.'
            : 'Packing is disabled.'}
        </div>
      </div>
    </Shell>
  );
}

function PackOption({ label, on, onChange }) {
  return <div className="row" style={{ gap: 8, cursor: 'pointer' }} onClick={() => onChange(!on)}>
    <Toggle on={on} onChange={onChange} />
    <span style={{ fontSize: 12 }}>{label}</span>
  </div>;
}

window.FramePacking = FramePacking;
