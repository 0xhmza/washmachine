/* Frame 05 — Backdoor / PE inject */

function FrameBackdoor() {
  return (
    <Shell active="backdooring" crumbs={["Backdooring"]} pipeActive="bd"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "done", bd: "active" }}
      status={[
        { icon: "info", k: "target", v: "putty.exe · 1.2 MB" },
        { icon: "info", k: "method", v: "code cave" },
      ]}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Backdoor PE</h1>
          <span className="sub">Inject the compiled loader into an existing executable.</span>
        </div>
        {/* Target picker */}
        <Sec title="Target" action={<Chip kind="ok" dot>verified PE32+</Chip>}>
          <div className="card">
            <div className="row" style={{ gap: 16, alignItems: "stretch" }}>
              <div style={{ flex: 1 }}>
                <Field label="Donor executable">
                  <input className="input mono" defaultValue="C:\\Tools\\PuTTY\\putty.exe" />
                </Field>
                <div className="row" style={{ marginTop: 10, gap: 8 }}>
                  <button className="btn"><Icon name="upload" size={12} />Browse…</button>
                  <button className="btn ghost"><Icon name="info" size={12} />Re-analyze</button>
                </div>
              </div>
              <div className="mono" style={{ borderLeft: "1px solid var(--n-4)", paddingLeft: 18, fontSize: 11, color: "var(--n-8)", lineHeight: 1.85, minWidth: 220 }}>
                size       <span style={{ color: "var(--n-10)" }}>1.21 MB</span>{"\n"}
                machine    <span style={{ color: "var(--n-10)" }}>AMD64</span>{"\n"}
                signed     <span style={{ color: "var(--warn)" }}>yes</span> · expires 2027{"\n"}
                sections   <span style={{ color: "var(--n-10)" }}>6</span>{"\n"}
                code caves <span style={{ color: "var(--ok)" }}>14</span> · max 1,184 B
              </div>
            </div>
          </div>
        </Sec>

        {/* Method picker */}
        <Sec title="Injection method">
          <div className="card" style={{ padding: 0, overflow: "hidden" }}>
            <MethodRow name="Code cave"          sub="Reuse padding inside existing sections" trait="zero growth"            traitTone="ok"   capacity="≤ 1,184 B" selected />
            <MethodRow name="New section"        sub="Append .wm section with payload"        trait="unlimited capacity"    traitTone="acc"  capacity="any" />
            <MethodRow name="Section extension"  sub="Extend .text by aligned amount"         trait="grows file size"       traitTone=""     capacity="≤ 64 KB" />
            <MethodRow name="Text padding"       sub="Use .text alignment padding"            trait="zero growth · fragile" traitTone="warn" capacity="≤ 312 B" />
            <MethodRow name="TLS callback"       sub="Pre-main execution · x64 only"           trait="silent execution"      traitTone="acc"  capacity="N/A" />
          </div>
        </Sec>

        <Sec title="Hijack" action={<Chip>entry-point patch</Chip>}>
          <div className="card row" style={{ gap: 16, alignItems: "stretch" }}>
            <Field label="Patch strategy">
              <Seg value="ep" options={[
                { v: "ep", l: "Entry point" },
                { v: "import", l: "Import" },
                { v: "tls", l: "TLS" },
              ]} />
            </Field>
            <Field label="Resume after exec">
              <Toggle on />
            </Field>
            <div style={{ flex: 1 }} />
            <Field label="Output path">
              <input className="input mono" defaultValue="putty.patched.exe" style={{ width: 240 }} />
            </Field>
          </div>
        </Sec>
      </div>

      <PeMapPreview />
    </Shell>
  );
}

function MethodRow({ name, sub, trait, traitTone, capacity, selected }) {
  const toneColor = {
    ok:   "var(--ok)",
    warn: "var(--warn)",
    acc:  "var(--acc)",
    "":   "var(--n-7)",
  }[traitTone || ""];
  return (
    <div style={{
      display: "grid",
      gridTemplateColumns: "20px 1fr auto 14px",
      gap: 18,
      padding: "14px 18px",
      alignItems: "center",
      borderBottom: "1px solid var(--n-3)",
      cursor: "pointer",
      ...(selected ? { background: "var(--acc-bg)", boxShadow: "inset 3px 0 0 var(--acc)" } : {}),
    }}>
      <div style={{
        width: 16, height: 16, borderRadius: "50%",
        border: "1px solid " + (selected ? "var(--acc)" : "var(--n-6)"),
        background: selected ? "var(--acc)" : "transparent",
        boxShadow: selected ? "inset 0 0 0 4px var(--n-2)" : "none",
      }} />
      <div style={{ minWidth: 0 }}>
        <div className="h2" style={{ fontSize: 14 }}>{name}</div>
        <div style={{ fontSize: 12, color: "var(--n-7)", marginTop: 2 }}>{sub}</div>
      </div>
      <div className="row" style={{ gap: 18, fontSize: 12, whiteSpace: "nowrap" }}>
        <span style={{ display: "flex", alignItems: "center", gap: 7, color: toneColor }}>
          <span style={{ width: 7, height: 7, borderRadius: "50%", background: toneColor }} />
          {trait}
        </span>
        <span style={{ width: 1, height: 14, background: "var(--n-4)" }} />
        <span className="mono" style={{ color: "var(--n-8)", minWidth: 64, textAlign: "right" }}>{capacity}</span>
      </div>
      <Icon name="chev" size={12} />
    </div>
  );
}

function PeMapPreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="layers" size={11} />PE map</div>
        <div className="ptab"><Icon name="doc" size={11} />headers</div>
        <div className="ptab"><Icon name="term" size={11} />imports</div>
        <div className="ptab"><Icon name="bug" size={11} />diff</div>
      </div>
      <div className="pbody ppad">
        <div className="row" style={{ marginBottom: 14, gap: 10 }}>
          <Chip kind="acc">putty.exe</Chip>
          <Chip>6 sections</Chip>
          <Chip kind="ok" dot>14 code caves</Chip>
        </div>

        {/* visual section bar */}
        <div style={{ marginBottom: 14 }}>
          <div style={{ display: "flex", height: 32, borderRadius: 6, overflow: "hidden", border: "1px solid var(--n-4)" }}>
            <SecBar w={32} name=".text" color="var(--n-5)" />
            <SecBar w={6}  name=".rdata" color="var(--n-6)" />
            <SecBar w={3}  name=".data" color="var(--n-5)" />
            <SecBar w={1}  name=".pdata" color="var(--n-6)" />
            <SecBar w={3}  name=".rsrc" color="var(--n-5)" />
            <SecBar w={1}  name=".reloc" color="var(--n-6)" />
          </div>
          <div className="row" style={{ marginTop: 8, justifyContent: "space-between" }}>
            <span className="mono" style={{ fontSize: 10, color: "var(--n-7)" }}>0x00401000</span>
            <span className="mono" style={{ fontSize: 10, color: "var(--n-7)" }}>0x0053c000</span>
          </div>
        </div>

        <H3>Candidate code caves</H3>
        <div className="pemap" style={{ marginTop: 8 }}>
          <CaveRow rva="0x004f3a18" sec=".text" sz={1184} fillPct={92} target />
          <CaveRow rva="0x004e8c20" sec=".text" sz={812}  fillPct={64} />
          <CaveRow rva="0x004a9b00" sec=".text" sz={640}  fillPct={50} />
          <CaveRow rva="0x00521c40" sec=".rdata" sz={512} fillPct={40} />
          <CaveRow rva="0x004b1280" sec=".text" sz={384}  fillPct={30} />
          <CaveRow rva="0x004f9100" sec=".text" sz={296}  fillPct={23} />
        </div>

        <div className="div" />

        <H3>Patch preview</H3>
        <div className="mono" style={{ fontSize: 11, lineHeight: 1.8, color: "var(--n-8)", marginTop: 8 }}>
          <div><span className="o">0x004f3a18</span>  <span style={{ color: "var(--n-6)" }}>cc cc cc cc cc cc cc cc</span>  <span style={{ color: "var(--n-6)" }}>// before</span></div>
          <div><span className="o">0x004f3a18</span>  <span style={{ color: "var(--acc)" }}>e8 a3 12 00 00 90 90 90</span>  <span style={{ color: "var(--n-6)" }}>// after (call → cave)</span></div>
          <div style={{ marginTop: 6 }}><span className="o">EP</span>          <span style={{ color: "var(--n-6)" }}>48 83 ec 28 e8 d7 04 00</span>  <span style={{ color: "var(--n-6)" }}>// before</span></div>
          <div><span className="o">EP</span>          <span style={{ color: "var(--acc)" }}>e9 13 3a 4f 00</span><span style={{ color: "var(--n-6)" }}> 90 90 90</span>  <span style={{ color: "var(--n-6)" }}>// after (jmp → loader)</span></div>
        </div>
      </div>
    </div>
  );
}

function SecBar({ w, name, color }) {
  return (
    <div style={{ flex: w, background: color, position: "relative", borderRight: "1px solid var(--n-0)" }}>
      <span style={{
        position: "absolute", left: 6, top: "50%", transform: "translateY(-50%)",
        fontFamily: "var(--f-mono)", fontSize: 10, color: "var(--n-0)", fontWeight: 600
      }}>{name}</span>
    </div>
  );
}

function CaveRow({ rva, sec, sz, fillPct, target }) {
  return (
    <div className={"pe-row" + (target ? " tgt" : "")} style={{ "--fill": fillPct + "%" }}>
      <div className="nm">{rva} <span style={{ color: "var(--n-7)", fontSize: 10 }}>{sec}</span></div>
      <div className="vis" />
      <div className="sz">{sz} B</div>
    </div>
  );
}

window.FrameBackdoor = FrameBackdoor;
