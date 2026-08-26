/* Frame — Pipeline (overview / dashboard of all stages, matches original PipelinePage) */

function FramePipeline() {
  const stages = [
    { id: "src",  num: "01", name: "Source",     icon: "file",   tone: "ok",   detail: "calc_x64.bin · 327 B · sha256 4f7b…a9d2", time: "0.01s" },
    { id: "sgn",  num: "02", name: "SGN",        icon: "shield", tone: "ok",   detail: "2 passes · pre · max 64 B", time: "0.71s", optional: true },
    { id: "enc",  num: "03", name: "Encode",     icon: "bolt",   tone: "ok",   detail: "XOR (key 9fa24cd7) → Base64 · 789 B", time: "0.17s" },
    { id: "tpl",  num: "04", name: "Template",   icon: "doc",    tone: "ok",   detail: "full-loader · 5 snippets composed", time: "0.04s" },
    { id: "cmp",  num: "05", name: "Compile",    icon: "play",   tone: "ok",   detail: "cl.exe /O2 /MT → 20260519-4f7ba9d2.exe", time: "3.21s" },
    { id: "bd",   num: "06", name: "Backdoor",   icon: "inject", tone: "ok",   detail: "putty.exe · code cave @ 0x004f3a18", time: "0.48s", optional: true },
    { id: "pk",   num: "07", name: "Pack",       icon: "pkg",    tone: "ok",   detail: "Custom RC4 · 2.04 MB · entropy 7.84", time: "0.92s", optional: true },
    { id: "fn",   num: "08", name: "Finalize",   icon: "finish", tone: "acc",  detail: "Clone OneDrive.exe · +1.0 MiB NOP pad", time: "0.31s", optional: true, running: true },
  ];

  return (
    <Shell active="pipeline" crumbs={["Pipeline"]} pipeActive="" wide
      pipeStates={Object.fromEntries(PIPELINE.map(p => [p.id, "skipped"]))}
      status={[
        { icon: "info", k: "session", v: "session_20260519_001a" },
        { icon: "info", k: "elapsed", v: "00:05.8" },
        { icon: "info", k: "remaining", v: "Finalize · ~3s" },
      ]}>
      <div className="cfg" style={{ padding: "22px 28px" }}>
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 4 }}>
          <h1 className="h1">Pipeline</h1>
          <span className="sub">Full build at a glance — every stage, its config, status, and output artifact.</span>
        </div>

        {/* Run header */}
        <div className="card flat" style={{ marginTop: 18, marginBottom: 22, padding: "16px 18px" }}>
          <div className="row" style={{ gap: 18 }}>
            <div>
              <div className="h3">Active session</div>
              <div className="mono" style={{ fontSize: 14, color: "var(--n-10)", marginTop: 4 }}>session_20260519_001a</div>
            </div>
            <Divider />
            <div>
              <div className="h3">Status</div>
              <div className="row" style={{ marginTop: 4, gap: 6 }}>
                <span style={{ width: 8, height: 8, borderRadius: "50%", background: "var(--acc)" }} className="pulse" />
                <span style={{ fontSize: 14, color: "var(--n-10)", fontWeight: 500 }}>Running</span>
              </div>
            </div>
            <Divider />
            <div>
              <div className="h3">Elapsed</div>
              <div className="mono" style={{ fontSize: 14, color: "var(--n-10)", marginTop: 4 }}>00:05.8</div>
            </div>
            <Divider />
            <div style={{ flex: 1 }}>
              <div className="h3">Progress</div>
              <div style={{ marginTop: 8 }}>
                <div className="meter" style={{ height: 6 }}>
                  <i style={{ width: "82%" }} />
                </div>
                <div className="row" style={{ justifyContent: "space-between", marginTop: 6, fontSize: 11, color: "var(--n-7)" }}>
                  <span>7 of 8 done · Finalize in progress</span>
                  <span>est. 3s remaining</span>
                </div>
              </div>
            </div>
            <button className="btn"><Icon name="copy" size={12} />Copy manifest</button>
            <button className="btn ghost"><Icon name="dl" size={12} />Export</button>
          </div>
        </div>

        {/* Stages — vertical timeline */}
        <div style={{ position: "relative", paddingLeft: 24 }}>
          <div style={{ position: "absolute", top: 14, bottom: 14, left: 31, width: 1, background: "var(--n-4)" }} />
          {stages.map((s, i) => <StageRow key={s.id} {...s} last={i === stages.length - 1} />)}
        </div>
      </div>
    </Shell>
  );
}

function Divider() {
  return <div style={{ width: 1, height: 30, background: "var(--n-4)" }} />;
}

function StageRow({ num, name, icon, tone, detail, time, optional, running, last }) {
  const dotColor = running ? "var(--acc)" : tone === "ok" ? "var(--ok)" : tone === "warn" ? "var(--warn)" : tone === "err" ? "var(--err)" : "var(--n-5)";
  const bg = running ? "var(--acc-bg)" : tone === "ok" ? "var(--ok-bg)" : "var(--n-2)";
  return (
    <div style={{
      display: "grid",
      gridTemplateColumns: "16px 1fr",
      gap: 18,
      paddingBottom: last ? 0 : 18,
      position: "relative",
    }}>
      <div style={{
        width: 16, height: 16, borderRadius: "50%",
        background: bg,
        border: "1px solid " + dotColor,
        display: "grid", placeItems: "center",
        marginTop: 14,
        position: "relative",
        zIndex: 1,
      }}>
        {running ? (
          <span className="pulse" style={{ width: 8, height: 8, borderRadius: "50%", background: "var(--acc)" }} />
        ) : (
          <span style={{ width: 6, height: 6, borderRadius: "50%", background: dotColor }} />
        )}
      </div>
      <div style={{
        background: "var(--n-2)",
        border: "1px solid " + (running ? "var(--acc-line)" : "var(--n-4)"),
        borderRadius: 10,
        padding: "12px 16px",
        display: "grid",
        gridTemplateColumns: "32px 1fr auto auto auto",
        gap: 14,
        alignItems: "center",
      }}>
        <div style={{ width: 32, height: 32, borderRadius: 6, background: "var(--n-3)", display: "grid", placeItems: "center", color: running ? "var(--acc)" : "var(--n-8)" }}>
          <Icon name={icon} size={15} />
        </div>
        <div>
          <div className="row" style={{ gap: 8 }}>
            <span className="mono" style={{ fontSize: 11, color: "var(--n-7)" }}>{num}</span>
            <span style={{ fontSize: 14, color: "var(--n-10)", fontWeight: 500 }}>{name}</span>
            {optional && <span style={{ fontSize: 11, color: "var(--n-7)" }}>optional</span>}
            {running && <Chip kind="acc" dot>running</Chip>}
          </div>
          <div className="mono" style={{ fontSize: 11, color: "var(--n-7)", marginTop: 3 }}>{detail}</div>
        </div>
        <div className="mono" style={{ fontSize: 11, color: running ? "var(--acc)" : "var(--n-7)", textAlign: "right" }}>
          {running ? "…" : time}
        </div>
        <button className="btn ghost" style={{ height: 26, padding: "0 10px" }}>
          <Icon name="doc" size={11} />Log
        </button>
        <button className="btn ghost" style={{ height: 26, padding: "0 10px" }}>
          <Icon name="cog" size={11} />Edit
        </button>
      </div>
    </div>
  );
}

window.FramePipeline = FramePipeline;
