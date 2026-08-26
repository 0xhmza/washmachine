/* Frame 02 — Encode stage (SGN + Bin2Shell) */

function FrameEncode() {
  const { useField, useFields } = window.washState;
  const [sgnEnabled, setSgnEnabled] = useField('shikataGaNaiEnabledCheckBox');
  const [sgnCount, setSgnCount]     = useField('shikataGaNaiEncodeCountInput');
  const [sgnMax, setSgnMax]         = useField('shikataGaNaiMaxBytesInput');
  const [sgnPlace, setSgnPlace]     = useField('shikataGaNaiPlacement');
  const [encoderIdx, setEncoderIdx] = useField('encoderCombo');
  const [envelopeIdx, setEnvelopeIdx] = useField('envelopeCombo');

  const meta = useFields('catalog_encoders', 'catalog_envelopes');
  const encoders  = meta.catalog_encoders  || [];
  const envelopes = meta.catalog_envelopes || [];

  function reloadCatalog() {
    if (window.washState) window.washState.loadCatalogs();
  }

  const sgnOn = sgnEnabled === 'True';
  const selEnc = encoders.find(e => String(e.index) === String(encoderIdx));
  const selEnv = envelopes.find(e => String(e.index) === String(envelopeIdx));

  const status = [
    { icon: "info", k: "encoder",  v: selEnc ? selEnc.name : (encoderIdx || 'none') },
    { icon: "info", k: "envelope", v: selEnv ? selEnv.name : (envelopeIdx || 'none') },
    { icon: "info", k: "sgn",      v: sgnOn ? `${sgnCount} passes · ${sgnPlace}` : 'off' },
  ];

  return (
    <Shell active="payload" crumbs={["Payload", "Encoding"]} pipeActive="enc"
      pipeStates={{ src: "done", sgn: "done", enc: "active" }}
      status={status}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Encoding</h1>
          <span className="sub">Transform raw shellcode bytes before they're embedded.</span>
        </div>

        {/* Shikata Ga Nai */}
        <Sec title="Shikata Ga Nai · preprocessor"
          action={<div className="row" style={{ gap: 8 }}>
            <Toggle on={sgnOn} onChange={v => setSgnEnabled(v ? 'True' : 'False')} />
          </div>}>
          <div className="card" style={{ opacity: sgnOn ? 1 : 0.55, pointerEvents: sgnOn ? 'auto' : 'none' }}>
            <div className="row" style={{ gap: 16 }}>
              <Field label="Encode count" hint="Each pass adds a decoder stub.">
                <input className="input mono" value={sgnCount}
                  onChange={e => setSgnCount(e.target.value)}
                  style={{ width: 100 }} />
              </Field>
              <Field label="Decoder max bytes" hint="Obfuscation budget per pass.">
                <input className="input mono" value={sgnMax}
                  onChange={e => setSgnMax(e.target.value)}
                  style={{ width: 100 }} />
              </Field>
              <Field label="Placement">
                <Seg value={sgnPlace} onChange={setSgnPlace}
                  options={[{ v: "pre", l: "Pre-Bin2Shell" }, { v: "post", l: "Post" }]} />
              </Field>
              <div style={{ flex: 1 }} />
            </div>
          </div>
        </Sec>

        {/* Bin2Shell — encoder + envelope */}
        <Sec title="Bin2Shell"
          action={<button className="btn ghost" onClick={reloadCatalog}><Icon name="refresh" size={12} />Reload catalog</button>}>
          <div className="card" style={{ padding: 0, overflow: "hidden" }}>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr" }}>
              {/* Encoder */}
              <div style={{ padding: 18, borderRight: "1px solid var(--n-4)" }}>
                <div className="row" style={{ justifyContent: "space-between", marginBottom: 10 }}>
                  <H3>Encoder</H3>
                  <span className="mono" style={{ fontSize: 10, color: "var(--n-7)" }}>algos.yaml</span>
                </div>
                {encoders.length === 0 ? (
                  <div style={{ color: "var(--n-6)", fontSize: 12, padding: "8px 0" }}>
                    Provisioning Bin2Shell… or catalog unavailable.
                  </div>
                ) : (
                  <div className="list">
                    {encoders.map(enc => (
                      <EncRow key={enc.index} name={enc.name} id={String(enc.index)} desc={enc.description}
                        selected={String(encoderIdx) === String(enc.index)}
                        onClick={() => setEncoderIdx(String(enc.index))} />
                    ))}
                  </div>
                )}
              </div>
              {/* Envelope */}
              <div style={{ padding: 18 }}>
                <div className="row" style={{ justifyContent: "space-between", marginBottom: 10 }}>
                  <H3>Envelope</H3>
                  <span className="mono" style={{ fontSize: 10, color: "var(--n-7)" }}>algos.yaml</span>
                </div>
                {envelopes.length === 0 ? (
                  <div style={{ color: "var(--n-6)", fontSize: 12, padding: "8px 0" }}>
                    Catalog unavailable.
                  </div>
                ) : (
                  <div className="list">
                    {envelopes.map(env => (
                      <EncRow key={env.index} name={env.name} id={String(env.index)} desc={env.description}
                        selected={String(envelopeIdx) === String(env.index)}
                        onClick={() => setEnvelopeIdx(String(env.index))} />
                    ))}
                  </div>
                )}
              </div>
            </div>
          </div>
        </Sec>
      </div>

      <EncodedPreview />
    </Shell>
  );
}

function EncRow({ name, id, desc, selected, onClick }) {
  return (
    <div className={"list-item" + (selected ? " sel" : "")} onClick={onClick} style={{ cursor: "pointer" }}>
      <div style={{
        width: 14, height: 14, borderRadius: "50%",
        border: "1px solid " + (selected ? "var(--acc)" : "var(--n-5)"),
        background: selected ? "var(--acc)" : "transparent",
        boxShadow: selected ? "inset 0 0 0 3px var(--n-3)" : "none",
        flexShrink: 0,
      }} />
      <div>
        <div className="ttl">{name}</div>
        <div className="meta">id {id}{desc ? " · " + desc : ""}</div>
      </div>
    </div>
  );
}

function EncodedPreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="term" size={11} />encoded.b64</div>
        <div style={{ flex: 1 }} />
        <div className="ptab"><Icon name="copy" size={11} /></div>
      </div>
      <div className="pbody ppad">
        <div style={{ color: "var(--n-6)", fontSize: 12, fontFamily: "var(--f-mono)" }}>
          Encoded output appears here after a build.
        </div>
      </div>
    </div>
  );
}

window.FrameEncode = FrameEncode;

