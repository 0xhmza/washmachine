/* Frame — Packing (separate page in original nav) */

function FramePacking() {
  const { useField } = window.washState;
  const [packEnabled, setPackEnabled] = useField('EnablePackingToggle');
  const [packerCombo, setPackerCombo] = useField('PackerCombo');

  const packOn = packEnabled === 'True';

  const PACKERS = [
    { id: "None",       name: "None",        tagline: "Ship binary as-is",        sub: "No transformation. Lowest risk of post-pack breakage; largest output.", capacity: "—" },
    { id: "UPX",        name: "UPX",         tagline: "Generic compressor",       sub: "Industry-standard. Signature-flagged by most EDR — use only if entropy is masked downstream.", capacity: "≈ 0.55× size", warn: true },
    { id: "CustomRC4",  name: "Custom RC4",  tagline: "Stub decrypts at runtime", sub: "Stage-0 stub maps decrypted payload into RWX and jumps. Higher CPU cost on launch.", capacity: "≈ 0.92× size" },
    { id: "Reflective", name: "Reflective",  tagline: "In-memory unpack",         sub: "No payload on disk; entire decode happens in process memory. Largest stub.", capacity: "≈ 1.05× size" },
  ];

  const selPacker = PACKERS.find(p => p.id === packerCombo) || PACKERS[0];
  const status = [
    { icon: "info", k: "method", v: selPacker.name },
    { icon: "info", k: "enabled", v: packOn ? "yes" : "no" },
  ];

  return (
    <Shell active="packing" crumbs={["Packing"]} pipeActive="pk"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "done", bd: "done", pk: "active" }}
      status={status}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Packing</h1>
          <span className="sub">Compress and obfuscate the compiled binary before final delivery.</span>
          <div style={{ flex: 1 }} />
          <Toggle on={packOn} onChange={v => setPackEnabled(v ? 'True' : 'False')} />
          <span style={{ fontSize: 12, color: packOn ? "var(--n-9)" : "var(--n-6)" }}>Enable</span>
        </div>

        <div style={{ opacity: packOn ? 1 : 0.45, pointerEvents: packOn ? 'auto' : 'none' }}>
          <Sec title="Method">
            <div style={{ display: "grid", gridTemplateColumns: "repeat(2, 1fr)", gap: 10 }}>
              {PACKERS.map(p => (
                <PackTile key={p.id} {...p}
                  selected={packerCombo === p.id || (!packerCombo && p.id === 'None')}
                  onClick={() => setPackerCombo(p.id)} />
              ))}
            </div>
          </Sec>
        </div>
      </div>

      <PackingPreview />
    </Shell>
  );
}

function PackTile({ name, tagline, sub, capacity, selected, warn, onClick }) {
  return (
    <div onClick={onClick} style={{
      padding: 16, borderRadius: 10, cursor: "pointer",
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-2)",
    }}>
      <div className="row" style={{ justifyContent: "space-between", marginBottom: 6 }}>
        <div>
          <div className="h2" style={{ fontSize: 14 }}>{name}</div>
          <div style={{ fontSize: 11, color: "var(--n-7)", marginTop: 2 }}>{tagline}</div>
        </div>
        {selected && <Chip kind="acc" dot>selected</Chip>}
        {warn && !selected && <Chip kind="warn">flagged</Chip>}
      </div>
      <div style={{ fontSize: 12, color: "var(--n-8)", lineHeight: 1.5, marginTop: 10 }}>{sub}</div>
      <div className="div" />
      <div className="row" style={{ justifyContent: "space-between" }}>
        <span className="h3">Size factor</span>
        <span className="mono" style={{ fontSize: 12, color: selected ? "var(--acc)" : "var(--n-8)" }}>{capacity}</span>
      </div>
    </div>
  );
}

function PackingPreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="layers" size={11} />info</div>
        <div style={{ flex: 1 }} />
      </div>
      <div className="pbody ppad">
        <div style={{ color: "var(--n-6)", fontSize: 12, fontFamily: "var(--f-mono)" }}>
          Packing result appears here after a successful build.
        </div>
      </div>
    </div>
  );
}

window.FramePacking = FramePacking;

