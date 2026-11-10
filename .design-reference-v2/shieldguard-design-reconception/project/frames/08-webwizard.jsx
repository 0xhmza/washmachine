/* Frame 08 — Web Payload Wizard (modal over workspace) */

function FrameWebWizard() {
  const modal = (
    <div className="modal-scrim">
      <div className="modal" style={{ width: 820 }}>
        <div className="modal-hd">
          <div style={{ width: 32, height: 32, borderRadius: 8, background: "var(--acc-bg)", border: "1px solid var(--acc-line)", display: "grid", placeItems: "center", color: "var(--acc)" }}>
            <Icon name="globe" size={16} />
          </div>
          <div>
            <div className="h2">Web payload wizard</div>
            <div className="sub">Encode → host → fetch. Generates the C++ retrieval stub that gets stitched into the template.</div>
          </div>
          <div style={{ flex: 1 }} />
          <button className="btn ghost"><Icon name="x" size={14} /></button>
        </div>

        {/* Step rail */}
        <div className="row" style={{ padding: "14px 22px 0", gap: 0 }}>
          <Step n="1" name="Encoder & envelope" done />
          <StepArrow done />
          <Step n="2" name="Fetch helper" active />
          <StepArrow />
          <Step n="3" name="Verify URL" />
          <StepArrow />
          <Step n="4" name="Generate" />
        </div>

        <div className="modal-bd">
          <H3>Step 2 of 4 · Fetch helper</H3>
          <div style={{ marginTop: 6, marginBottom: 18, fontSize: 13, color: "var(--n-8)" }}>
            Choose how the loader retrieves and decodes the payload at runtime. Heavier helpers carry more dependencies; lighter ones surface in fewer signatures.
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <FetchOption name="WinInet" sub="InternetOpenA → HttpOpenRequest → InternetReadFile" tag="≈ +6 KB" weight="standard" />
            <FetchOption name="WinHttp" sub="WinHttpOpen → WinHttpSendRequest" tag="≈ +4 KB" weight="recommended" selected />
            <FetchOption name="URLDownloadToFile" sub="urlmon.dll · simplest, most flagged" tag="≈ +1 KB" weight="loud" warn />
            <FetchOption name="Raw socket TLS" sub="ws2_32 + manual TLS handshake" tag="≈ +18 KB" weight="quiet" />
          </div>

          <div className="div" />

          <Field label="User-Agent override" hint="Empty = use default Windows UA.">
            <input className="input mono" defaultValue="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36" />
          </Field>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginTop: 14 }}>
            <Field label="Retry policy">
              <Seg value="3x" options={[{ v: "off", l: "Off" }, { v: "3x", l: "3× backoff" }, { v: "inf", l: "Infinite" }]} />
            </Field>
            <div className="field">
              <label>Verification</label>
              <div style={{
                display: "flex", alignItems: "center", gap: 12,
                padding: "6px 12px",
                height: 34,
                border: "1px solid var(--n-4)",
                borderBottom: "1px solid var(--n-5)",
                borderRadius: "var(--r-2)",
                background: "var(--n-1)",
              }}>
                <Toggle on />
                <div style={{ flex: 1 }}>
                  <div style={{ fontSize: 13, color: "var(--n-10)", lineHeight: 1.2 }}>Pin server certificate</div>
                  <div style={{ fontSize: 11, color: "var(--n-7)", marginTop: 2 }}>SHA-256 of leaf cert · embedded in stub</div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <div className="modal-ft">
          <button className="btn ghost">Cancel</button>
          <div style={{ flex: 1 }} />
          <span className="mono" style={{ fontSize: 11, color: "var(--n-7)", marginRight: 8 }}>est. stub size: 4.1 KB · payload: 789 B</span>
          <button className="btn">← Back</button>
          <button className="btn primary">Next · verify URL <span className="sk">⏎</span></button>
        </div>
      </div>
    </div>
  );

  return (
    <Shell active="payload" crumbs={["Payload", "Web payload wizard"]} pipeActive="src" modal={modal}>
      <div className="cfg" />
      <div className="preview" />
    </Shell>
  );
}

function Step({ n, name, done, active }) {
  return (
    <div className="row" style={{ gap: 8 }}>
      <div style={{
        width: 22, height: 22, borderRadius: "50%",
        background: done ? "var(--ok-bg)" : active ? "var(--acc-bg)" : "var(--n-2)",
        color: done ? "var(--ok)" : active ? "var(--acc)" : "var(--n-7)",
        border: "1px solid " + (done ? "oklch(0.55 0.12 155 / 0.45)" : active ? "var(--acc-line)" : "var(--n-4)"),
        display: "grid", placeItems: "center",
        fontFamily: "var(--f-mono)", fontSize: 11, fontWeight: 500,
      }}>
        {done ? <Icon name="check" size={11} sw={3} /> : n}
      </div>
      <div style={{ fontSize: 12, color: active ? "var(--n-10)" : "var(--n-7)", fontWeight: active ? 500 : 400 }}>{name}</div>
    </div>
  );
}
function StepArrow({ done }) {
  return <div style={{ flex: 1, height: 1, background: done ? "var(--ok)" : "var(--n-5)", margin: "0 12px", opacity: done ? 0.5 : 1 }} />;
}

function FetchOption({ name, sub, tag, weight, selected, warn }) {
  return (
    <div style={{
      padding: 14,
      borderRadius: 10,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-1)",
      cursor: "pointer",
    }}>
      <div className="row" style={{ justifyContent: "space-between" }}>
        <div className="h2" style={{ fontSize: 13 }}>{name}</div>
        {selected ? <Chip kind="acc" dot>selected</Chip>
         : warn ? <Chip kind="warn">{weight}</Chip>
         : <Chip>{weight}</Chip>}
      </div>
      <div className="mono" style={{ fontSize: 11, color: "var(--n-7)", marginTop: 6 }}>{sub}</div>
      <div className="mono" style={{ fontSize: 10, color: "var(--n-6)", marginTop: 4 }}>{tag}</div>
    </div>
  );
}

window.FrameWebWizard = FrameWebWizard;
