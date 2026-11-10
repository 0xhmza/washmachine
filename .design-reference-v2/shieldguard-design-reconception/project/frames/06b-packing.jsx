/* Frame — Packing (separate page in original nav) */

function FramePacking() {
  return (
    <Shell active="packing" crumbs={["Packing"]} pipeActive="pk"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "done", bd: "done", pk: "active" }}
      status={[
        { icon: "info", k: "method", v: "custom RC4" },
        { icon: "info", k: "stub", v: "in-memory" },
      ]}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Packing</h1>
          <span className="sub">Compress and obfuscate the compiled binary before final delivery.</span>
        </div>

        <Sec title="Method">
          <div style={{ display: "grid", gridTemplateColumns: "repeat(2, 1fr)", gap: 10 }}>
            <PackTile name="None" tagline="Ship binary as-is" sub="No transformation. Lowest risk of post-pack breakage; largest output." capacity="—" />
            <PackTile name="UPX" tagline="Generic compressor" sub="Industry-standard. Signature-flagged by most EDR — use only if entropy is masked downstream." capacity="≈ 0.55× size" warn />
            <PackTile name="Custom RC4" tagline="Stub decrypts at runtime" sub="Stage-0 stub maps decrypted payload into RWX and jumps. Higher CPU cost on launch." capacity="≈ 0.92× size" selected />
            <PackTile name="Reflective" tagline="In-memory unpack" sub="No payload on disk; entire decode happens in process memory. Largest stub." capacity="≈ 1.05× size" />
          </div>
        </Sec>

        <Sec title="Stub options">
          <div className="card">
            <div className="row" style={{ gap: 16, alignItems: "stretch" }}>
              <Field label="Key (hex)" hint="Empty = autogen at build time">
                <input className="input mono" defaultValue="9f a2 4c d7 21 0b 5e 78" style={{ width: 280 }} />
              </Field>
              <Field label="Decoder placement">
                <Seg value="prepend" options={[
                  { v: "prepend", l: "Prepend" },
                  { v: "tls", l: "TLS callback" },
                  { v: "section", l: "New section" },
                ]} />
              </Field>
              <Field label="Self-erase decoder" hint="Zero the decoder after first use">
                <Toggle on />
              </Field>
            </div>
          </div>
        </Sec>

        <Sec title="Verify pass" action={<Chip kind="acc" dot>ready</Chip>}>
          <div className="card">
            <VerifyRow label="Round-trip integrity" detail="Unpacked SHA-256 matches input" status="ok" />
            <VerifyRow label="Entropy floor" detail="Target ≤ 7.0 after final pad · current 6.21" status="ok" />
            <VerifyRow label="Static signature scan" detail="UPX / MPRESS / ASPack patterns" status="ok" />
            <VerifyRow label="Stub footprint" detail="2.4 KB · within 16 KB limit" status="ok" last />
          </div>
        </Sec>
      </div>

      <PackingPreview />
    </Shell>
  );
}

function PackTile({ name, tagline, sub, capacity, selected, warn }) {
  return (
    <div style={{
      padding: 16,
      borderRadius: 10,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-2)",
      cursor: "pointer",
      position: "relative",
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

function VerifyRow({ label, detail, status, last }) {
  const dot = status === "ok"
    ? <div style={{ width: 14, height: 14, borderRadius: "50%", background: "var(--ok-bg)", color: "var(--ok)", display: "grid", placeItems: "center" }}><Icon name="check" size={9} sw={3} /></div>
    : <div style={{ width: 10, height: 10, margin: 2, borderRadius: "50%", border: "1px dashed var(--n-5)" }} />;
  return (
    <div style={{
      display: "grid", gridTemplateColumns: "22px 1fr auto",
      gap: 12, padding: "10px 0",
      borderBottom: last ? "none" : "1px solid var(--n-3)",
      alignItems: "center",
    }}>
      {dot}
      <div>
        <div style={{ fontSize: 13, color: "var(--n-9)" }}>{label}</div>
        <div className="mono" style={{ fontSize: 11, color: "var(--n-7)", marginTop: 2 }}>{detail}</div>
      </div>
      <Chip kind="ok">pass</Chip>
    </div>
  );
}

function PackingPreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="layers" size={11} />before / after</div>
        <div className="ptab"><Icon name="term" size={11} />stub source</div>
        <div className="ptab"><Icon name="doc" size={11} />invocation</div>
      </div>
      <div className="pbody ppad">
        <H3>Compression visualization</H3>
        <div style={{ marginTop: 12, display: "grid", gap: 14 }}>
          <Bar label="Input" size="2.21 MB" frac={1.0} color="var(--n-5)" />
          <Bar label="After RC4 + stub" size="2.04 MB" frac={0.92} color="var(--acc)" />
          <Bar label="Net savings" size="−168 KB · −7.7%" frac={0.077} color="var(--ok)" hollow />
        </div>

        <div className="div" />

        <H3>Layout</H3>
        <div style={{
          display: "grid", gridTemplateColumns: "auto 1fr auto",
          gap: 10, padding: 12, marginTop: 10,
          background: "var(--n-2)", border: "1px solid var(--n-4)", borderRadius: 8,
          fontFamily: "var(--f-mono)", fontSize: 11, color: "var(--n-8)",
        }}>
          <span style={{ color: "var(--n-7)" }}>0x00000000</span>
          <span style={{ color: "var(--acc)" }}>┌─ stub (decoder + jmp)  2.4 KB</span>
          <span></span>
          <span style={{ color: "var(--n-7)" }}>0x00000970</span>
          <span style={{ color: "var(--n-9)" }}>├─ encrypted body  2.04 MB</span>
          <span style={{ color: "var(--n-7)" }}>RC4</span>
          <span style={{ color: "var(--n-7)" }}>0x00208000</span>
          <span style={{ color: "var(--n-9)" }}>├─ original headers  rebuilt at runtime</span>
          <span></span>
          <span style={{ color: "var(--n-7)" }}>0x002080a0</span>
          <span style={{ color: "var(--n-9)" }}>└─ overlay (NOP pad)  0 B</span>
          <span style={{ color: "var(--n-7)" }}>—</span>
        </div>

        <div className="div" />

        <H3>Entropy delta</H3>
        <div className="row" style={{ marginTop: 12, gap: 20 }}>
          <div>
            <div style={{ fontSize: 11, color: "var(--n-7)", marginBottom: 4 }}>Before</div>
            <div className="mono" style={{ fontSize: 22, color: "var(--n-9)", fontWeight: 500 }}>6.21</div>
          </div>
          <Icon name="chev" size={16} />
          <div>
            <div style={{ fontSize: 11, color: "var(--n-7)", marginBottom: 4 }}>After</div>
            <div className="mono" style={{ fontSize: 22, color: "var(--warn)", fontWeight: 500 }}>7.84</div>
          </div>
          <div style={{ flex: 1 }} />
          <Chip kind="warn">approaching ceiling</Chip>
        </div>
      </div>
    </div>
  );
}

function Bar({ label, size, frac, color, hollow }) {
  return (
    <div>
      <div className="row" style={{ justifyContent: "space-between", marginBottom: 5 }}>
        <span style={{ fontSize: 12, color: "var(--n-9)" }}>{label}</span>
        <span className="mono" style={{ fontSize: 11, color: "var(--n-7)" }}>{size}</span>
      </div>
      <div style={{ height: 8, background: "var(--n-2)", borderRadius: 4, position: "relative", overflow: "hidden" }}>
        <div style={{
          position: "absolute", top: 0, left: 0, bottom: 0,
          width: `${frac * 100}%`,
          background: hollow ? "transparent" : color,
          border: hollow ? `1px solid ${color}` : "none",
          borderRadius: 4,
          transition: "width .2s",
        }} />
      </div>
    </div>
  );
}

window.FramePacking = FramePacking;
