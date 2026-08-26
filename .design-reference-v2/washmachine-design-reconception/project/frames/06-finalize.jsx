/* Frame 06 — Packing & Finalize (last-mile output processing) */

function FrameFinalize() {
  return (
    <Shell active="finalize" crumbs={["Finalize"]} pipeActive="fn"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "done", bd: "done", pk: "done", fn: "active" }}
      status={[
        { icon: "info", k: "donor", v: "OneDrive.exe" },
        { icon: "info", k: "padding", v: "1.0 MiB NOP" },
      ]}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Finalize output</h1>
          <span className="sub">Clone metadata from a benign donor and adjust the file shape.</span>
        </div>

        <Sec title="Donor metadata" action={<Chip kind="acc">authenticode unsigned</Chip>}>
          <div className="card">
            <div className="row" style={{ gap: 16, alignItems: "stretch" }}>
              <div style={{ flex: 1 }}>
                <Field label="Donor executable" hint="Used as source for icon, resources, and version metadata.">
                  <input className="input mono" defaultValue="C:\Users\hmza\AppData\Local\Microsoft\OneDrive\OneDrive.exe" />
                </Field>
                <div className="row" style={{ marginTop: 12, gap: 12, flexWrap: "wrap" }}>
                  <CloneToggle label="Icon" on />
                  <CloneToggle label="Version info" on />
                  <CloneToggle label="Manifest" on />
                  <CloneToggle label=".rsrc tree" on />
                  <CloneToggle label="Authenticode signature" />
                  <CloneToggle label="Original file name" />
                </div>
              </div>
              <div style={{ width: 240, borderLeft: "1px solid var(--n-4)", paddingLeft: 18 }}>
                <H3>Preview · version info</H3>
                <div className="mono" style={{ fontSize: 11, color: "var(--n-8)", marginTop: 10, lineHeight: 1.85 }}>
                  CompanyName       <span style={{ color: "var(--n-10)" }}>Microsoft Corporation</span>{"\n"}
                  ProductName       <span style={{ color: "var(--n-10)" }}>Microsoft OneDrive</span>{"\n"}
                  FileVersion       <span style={{ color: "var(--n-10)" }}>24.176.0901.0002</span>{"\n"}
                  Copyright         © Microsoft{"\n"}
                  OriginalFilename  OneDrive.exe
                </div>
              </div>
            </div>
          </div>
        </Sec>

        <Sec title="File shaping">
          <div className="card">
            <div className="row" style={{ gap: 16 }}>
              <Field label="NOP padding (bytes)" hint="Append zero-effect bytes to alter hash & shape.">
                <div className="input-wrap">
                  <input className="input mono" defaultValue="1048576" style={{ width: 220 }} />
                  <span style={{ position: "absolute", right: 10, top: "50%", transform: "translateY(-50%)", color: "var(--n-7)", fontSize: 11 }}>= 1.0 MiB</span>
                </div>
              </Field>
              <Field label="Pattern">
                <Seg value="nop" options={[{ v: "nop", l: "0x90" }, { v: "zero", l: "0x00" }, { v: "rand", l: "Random" }]} />
              </Field>
              <Field label="Append location">
                <Seg value="overlay" options={[{ v: "overlay", l: "Overlay" }, { v: "section", l: "New section" }]} />
              </Field>
            </div>
            <div className="div" />
            <div>
              <H3>Result</H3>
              <div className="row" style={{ marginTop: 10, gap: 24 }}>
                <Stat k="size" v="2.21 MB" subk="was" subv="1.21 MB" />
                <Stat k="sha256" v="9c4f…2e10" subk="was" subv="4f7b…a9d2" />
                <Stat k="entropy" v="6.21" subk="was" subv="7.42" sublabel="dropped — good" ok />
                <Stat k="age stamp" v="2024-08-12" subk="from" subv="OneDrive.exe" />
              </div>
            </div>
          </div>
        </Sec>

        <Sec title="Packing" action={<Chip>configured on Packing screen</Chip>}>
          <div className="card flat row" style={{ gap: 12 }}>
            <div style={{ width: 36, height: 36, borderRadius: 8, background: "var(--n-2)", border: "1px solid var(--n-4)", display: "grid", placeItems: "center" }}>
              <Icon name="pkg" size={16} />
            </div>
            <div style={{ flex: 1 }}>
              <div className="h2">Packing pass</div>
              <div className="sub">None — ship binary as-is.</div>
            </div>
            <button className="btn ghost">Configure <Icon name="chev" size={12} /></button>
          </div>
        </Sec>
      </div>

      <FinalizePreview />
    </Shell>
  );
}

function CloneToggle({ label, on }) {
  return (
    <div className="row" style={{
      gap: 8, padding: "6px 10px",
      borderRadius: 6,
      background: on ? "var(--acc-bg)" : "var(--n-1)",
      border: "1px solid " + (on ? "var(--acc-line)" : "var(--n-4)"),
    }}>
      <Toggle on={!!on} />
      <span style={{ fontSize: 12, color: on ? "var(--n-10)" : "var(--n-8)" }}>{label}</span>
    </div>
  );
}

function Stat({ k, v, subk, subv, sublabel, ok }) {
  return (
    <div>
      <div className="h3" style={{ marginBottom: 4 }}>{k}</div>
      <div className="mono" style={{ fontSize: 18, color: "var(--n-10)", fontWeight: 500, letterSpacing: -0.01 }}>{v}</div>
      <div className="mono" style={{ fontSize: 10, color: "var(--n-7)", marginTop: 3 }}>
        {subk} <span style={{ color: "var(--n-8)" }}>{subv}</span> {sublabel && <span style={{ color: ok ? "var(--ok)" : "var(--n-7)" }}>· {sublabel}</span>}
      </div>
    </div>
  );
}

function PackCard({ name, sub, selected, warn }) {
  return (
    <div style={{
      flex: 1, padding: 12,
      borderRadius: 8,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-1)",
    }}>
      <div className="row" style={{ justifyContent: "space-between" }}>
        <div className="h2" style={{ fontSize: 13 }}>{name}</div>
        {warn && <Chip kind="warn">flagged</Chip>}
      </div>
      <div style={{ fontSize: 11, color: "var(--n-7)", marginTop: 4 }}>{sub}</div>
    </div>
  );
}

function FinalizePreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="layers" size={11} />artifact</div>
        <div className="ptab"><Icon name="bug" size={11} />before/after</div>
        <div className="ptab"><Icon name="doc" size={11} />manifest</div>
      </div>
      <div className="pbody ppad">
        <div style={{ display: "grid", gap: 16 }}>
          {/* Icon clone preview */}
          <div className="card flat" style={{ padding: 14 }}>
            <H3>Surface · file explorer</H3>
            <div className="row" style={{ marginTop: 12, gap: 12 }}>
              <div style={{
                width: 48, height: 48, borderRadius: 6,
                background: "var(--n-2)", border: "1px solid var(--n-4)",
                display: "grid", placeItems: "center",
                color: "var(--acc)", fontSize: 22, fontWeight: 700
              }}>☁</div>
              <div style={{ flex: 1 }}>
                <div className="mono" style={{ fontSize: 12, color: "var(--n-10)" }}>20260519-4f7ba9d2.exe</div>
                <div className="mono" style={{ fontSize: 11, color: "var(--n-7)", marginTop: 2 }}>Microsoft OneDrive · 2.21 MB</div>
              </div>
              <Chip kind="ok" dot>icon cloned</Chip>
            </div>
          </div>

          <div className="card flat" style={{ padding: 14 }}>
            <H3>Output manifest</H3>
            <div className="mono" style={{ fontSize: 11, color: "var(--n-8)", marginTop: 10, lineHeight: 1.85 }}>
              <span style={{ color: "var(--n-7)" }}>session</span>      <span style={{ color: "var(--n-10)" }}>session_20260519_001a</span>{"\n"}
              <span style={{ color: "var(--n-7)" }}>source</span>       <span style={{ color: "var(--n-10)" }}>calc_x64.bin</span>{"\n"}
              <span style={{ color: "var(--n-7)" }}>template</span>     full-loader{"\n"}
              <span style={{ color: "var(--n-7)" }}>encoder</span>      XOR + Base64 (key=9fa24cd7){"\n"}
              <span style={{ color: "var(--n-7)" }}>snippets</span>     5 selected{"\n"}
              <span style={{ color: "var(--n-7)" }}>backdoor</span>     putty.exe · code-cave @ 0x004f3a18{"\n"}
              <span style={{ color: "var(--n-7)" }}>donor</span>        OneDrive.exe{"\n"}
              <span style={{ color: "var(--n-7)" }}>output</span>       <span style={{ color: "var(--acc)" }}>out/20260519-4f7ba9d2.exe</span>{"\n"}
              <span style={{ color: "var(--n-7)" }}>sha256</span>       9c4f8d2a1bce7e10…{"\n"}
              <span style={{ color: "var(--n-7)" }}>size</span>         2.21 MB
            </div>
            <div className="row" style={{ marginTop: 14, gap: 8 }}>
              <button className="btn primary"><Icon name="dl" size={12} />Reveal in Explorer</button>
              <button className="btn"><Icon name="copy" size={12} />Copy hash</button>
              <button className="btn ghost"><Icon name="upload" size={12} />Save preset</button>
            </div>
          </div>

          <div className="card flat" style={{ padding: 14 }}>
            <H3>Recommended next</H3>
            <div className="col" style={{ marginTop: 10, gap: 8 }}>
              <NextRow icon="beaker" label="Test in detonation lab" sub="run against your VM matrix" />
              <NextRow icon="history" label="Save as preset" sub="re-run this exact composition" />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function NextRow({ icon, label, sub }) {
  return (
    <div className="row" style={{ padding: "8px 0", borderTop: "1px solid var(--n-3)", gap: 10 }}>
      <div style={{ width: 28, height: 28, borderRadius: 6, background: "var(--n-2)", border: "1px solid var(--n-4)", display: "grid", placeItems: "center" }}>
        <Icon name={icon} size={13} />
      </div>
      <div style={{ flex: 1 }}>
        <div style={{ fontSize: 12, color: "var(--n-9)" }}>{label}</div>
        <div style={{ fontSize: 11, color: "var(--n-7)" }}>{sub}</div>
      </div>
      <Icon name="chev" size={12} />
    </div>
  );
}

window.FrameFinalize = FrameFinalize;
