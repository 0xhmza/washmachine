/* Frame 02 — Encode stage (SGN + Bin2Shell) */

function FrameEncode() {
  return (
    <Shell active="payload" crumbs={["Payload", "Encoding"]} pipeActive="enc"
      pipeStates={{ src: "done", sgn: "done", enc: "active" }}
      status={[
        { icon: "info", k: "encoder", v: "XOR · 4-byte" },
        { icon: "info", k: "envelope", v: "Base64" },
        { icon: "info", k: "sgn", v: "2 passes · pre" },
      ]}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Encoding</h1>
          <span className="sub">Transform raw shellcode bytes before they're embedded.</span>
        </div>

        {/* Shikata Ga Nai */}
        <Sec title="Shikata Ga Nai · preprocessor"
          action={<div className="row" style={{ gap: 8 }}>
            <Chip kind="acc" dot>provisioned</Chip>
            <Toggle on />
          </div>}>
          <div className="card">
            <div className="row" style={{ gap: 16 }}>
              <Field label="Encode count" hint="Each pass adds a decoder stub.">
                <input className="input mono" defaultValue="2" style={{ width: 100 }} />
              </Field>
              <Field label="Decoder max bytes" hint="Obfuscation budget per pass.">
                <input className="input mono" defaultValue="64" style={{ width: 100 }} />
              </Field>
              <Field label="Placement">
                <Seg value="pre" options={[{ v: "pre", l: "Pre-Bin2Shell" }, { v: "post", l: "Post" }]} />
              </Field>
              <div style={{ flex: 1 }} />
            </div>
            <div className="div" />
            <div className="row" style={{ gap: 12 }}>
              <div style={{ flex: 1 }}>
                <H3>Effect</H3>
                <div className="row" style={{ gap: 14, marginTop: 8 }}>
                  <div className="mono" style={{ fontSize: 11, color: "var(--n-8)" }}>
                    327 B <span className="o">→</span> <span style={{ color: "var(--n-10)" }}>~512 B</span>
                  </div>
                  <div className="mono" style={{ fontSize: 11, color: "var(--n-8)" }}>
                    entropy 7.42 <span className="o">→</span> <span style={{ color: "var(--ok)" }}>7.96</span>
                  </div>
                </div>
              </div>
              <button className="btn ghost"><Icon name="info" size={12} />SGN reference</button>
            </div>
          </div>
        </Sec>

        {/* Bin2Shell — encoder + envelope */}
        <Sec title="Bin2Shell"
          action={<button className="btn ghost"><Icon name="refresh" size={12} />Reload catalog</button>}>
          <div className="card" style={{ padding: 0, overflow: "hidden" }}>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr" }}>
              {/* Encoder */}
              <div style={{ padding: 18, borderRight: "1px solid var(--n-4)" }}>
                <div className="row" style={{ justifyContent: "space-between", marginBottom: 10 }}>
                  <H3>Encoder</H3>
                  <span className="mono" style={{ fontSize: 10, color: "var(--n-7)" }}>algos.yaml</span>
                </div>
                <div className="list">
                  <EncRow name="None" id="0" desc="Pass-through bytes" />
                  <EncRow name="XOR" id="1" desc="4-byte rolling key" selected />
                  <EncRow name="RC4" id="2" desc="Stream cipher · 128-bit" />
                  <EncRow name="AES-128-CBC" id="3" desc="Block cipher · 16B key + IV" />
                  <EncRow name="ChaCha20" id="4" desc="Stream · 256-bit key" />
                </div>
                <div className="div" />
                <Field label="Key" hint="hex · empty = autogen">
                  <input className="input mono" defaultValue="9f a2 4c d7" />
                </Field>
              </div>
              {/* Envelope */}
              <div style={{ padding: 18 }}>
                <div className="row" style={{ justifyContent: "space-between", marginBottom: 10 }}>
                  <H3>Envelope</H3>
                  <span className="mono" style={{ fontSize: 10, color: "var(--n-7)" }}>algos.yaml</span>
                </div>
                <div className="list">
                  <EncRow name="None" id="0" desc="Raw byte array" />
                  <EncRow name="Base64" id="1" desc="Standard alphabet" selected />
                  <EncRow name="Base32" id="2" desc="Padded · A-Z 2-7" />
                  <EncRow name="Base91" id="3" desc="Higher density" />
                  <EncRow name="Hex" id="4" desc="0x.. comma-separated" />
                </div>
              </div>
            </div>
          </div>
        </Sec>

        {/* Byte distribution visualization */}
        <Sec title="Byte distribution"
          action={<div className="row" style={{ gap: 6 }}>
            <Chip>raw</Chip><Chip kind="acc">encoded</Chip>
          </div>}>
          <div className="card">
            <Histogram />
            <div className="row" style={{ gap: 14, marginTop: 10, fontSize: 11, color: "var(--n-7)" }}>
              <span>0x00</span><span style={{ flex: 1 }} /><span>0x40</span><span style={{ flex: 1 }} /><span>0x80</span><span style={{ flex: 1 }} /><span>0xC0</span><span style={{ flex: 1 }} /><span>0xFF</span>
            </div>
          </div>
        </Sec>
      </div>

      {/* Right preview: encoded bytes */}
      <EncodedPreview />
    </Shell>
  );
}

function EncRow({ name, id, desc, selected }) {
  return (
    <div className={"list-item" + (selected ? " sel" : "")}>
      <div style={{
        width: 14, height: 14, borderRadius: "50%",
        border: "1px solid " + (selected ? "var(--acc)" : "var(--n-5)"),
        background: selected ? "var(--acc)" : "transparent",
        boxShadow: selected ? "inset 0 0 0 3px var(--n-3)" : "none",
      }} />
      <div>
        <div className="ttl">{name}</div>
        <div className="meta">id {id} · {desc}</div>
      </div>
    </div>
  );
}

function Histogram() {
  // 32 bars, two series (raw faint, encoded accent)
  const bars = Array.from({ length: 48 }, (_, i) => {
    const raw = 0.2 + Math.abs(Math.sin(i * 0.4)) * 0.6 + (i % 7 === 0 ? 0.2 : 0);
    const enc = 0.4 + ((i * 31) % 17) / 30;
    return { raw, enc };
  });
  return (
    <div style={{ display: "flex", alignItems: "flex-end", height: 90, gap: 3 }}>
      {bars.map((b, i) => (
        <div key={i} style={{ flex: 1, height: "100%", position: "relative" }}>
          <div style={{
            position: "absolute", bottom: 0, left: 0, right: 0,
            height: `${b.raw * 100}%`,
            background: "var(--n-5)",
            borderRadius: "2px 2px 0 0",
          }} />
          <div style={{
            position: "absolute", bottom: 0, left: 0, right: 0,
            height: `${b.enc * 100}%`,
            background: "var(--acc)",
            opacity: 0.85,
            borderRadius: "2px 2px 0 0",
            mixBlendMode: "screen",
          }} />
        </div>
      ))}
    </div>
  );
}

function EncodedPreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="term" size={11} />encoded.b64</div>
        <div className="ptab"><Icon name="doc" size={11} />invocation</div>
        <div style={{ flex: 1 }} />
        <div className="ptab"><Icon name="copy" size={11} /></div>
      </div>
      <div className="pbody ppad">
        <div className="row" style={{ marginBottom: 10, gap: 10 }}>
          <Chip kind="acc">789 B</Chip>
          <Chip>+241 % vs raw</Chip>
          <Chip kind="ok" dot>printable</Chip>
        </div>
        <div className="mono" style={{ fontSize: 12, lineHeight: 1.7, color: "var(--n-9)", wordBreak: "break-all" }}>
          <span style={{ color: "var(--n-6)" }}>// XOR(key=9fa24cd7) → Base64</span><br/>
          /EiD5PDowAAAAEFRQVBSUVZIMdJlSItSYEiLUhhIi1IgSItyUEgPt0pKTTHJSDHA<br/>
          rDxhfAIsIEHByQ1BAcHi7VJBUUiLUiBLizQKSDHASIvSrAxA8MtT8MtIAcdK4u9I<br/>
          M8BLizR2SDHASLqAAQAAQQEAAEH/0EH/0FNQUEhB/9CDxCBoBwAAAGgEAAAAaQEA<br/>
          AGgFAAAAaAYAAAA1bGFKAVNQUEH/0Ej//8hI/zP/Q/8z/0OD7FBT/3RkEDU=
        </div>
        <div className="div" />
        <H3>Bin2Shell invocation</H3>
        <div className="mono" style={{ marginTop: 8, fontSize: 11, color: "var(--n-8)", lineHeight: 1.7 }}>
          <span className="o">$</span> python <span style={{ color: "var(--acc)" }}>main.py</span>{"  \\"}<br/>
          {"    "}-e <span style={{ color: "var(--n-10)" }}>1</span>{"  "}-x <span style={{ color: "var(--n-10)" }}>1</span>{"  \\"}<br/>
          {"    "}-i ./payload.bin{"  \\"}<br/>
          {"    "}-o ./encoded.b64{"  \\"}<br/>
          {"    "}--key 9fa24cd7
        </div>
      </div>
    </div>
  );
}

window.FrameEncode = FrameEncode;
