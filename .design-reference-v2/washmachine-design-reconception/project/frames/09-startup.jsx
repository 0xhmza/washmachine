/* Frame 09 — Startup / requirements provisioning */

function FrameStartup() {
  return (
    <div className="app" style={{ gridTemplateColumns: "1fr" }}>
      <div style={{
        display: "grid",
        gridTemplateColumns: "1fr 480px",
        height: "100%",
        background: "var(--n-1)",
      }}>
        {/* Left — hero / brand */}
        <div style={{
          background: `
            radial-gradient(circle at 18% 22%, oklch(0.32 0.08 235 / 0.45) 0%, transparent 45%),
            radial-gradient(circle at 90% 90%, oklch(0.28 0.06 250 / 0.5) 0%, transparent 50%),
            var(--n-0)
          `,
          padding: 64,
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          position: "relative",
          overflow: "hidden",
        }}>
          {/* Mark + brand */}
          <div>
            <div className="row" style={{ gap: 14 }}>
              <div style={{
                width: 44, height: 44, borderRadius: 10,
                background: "linear-gradient(155deg, var(--acc), var(--acc-dim))",
                display: "grid", placeItems: "center",
                color: "var(--n-0)", fontFamily: "var(--f-mono)", fontWeight: 700, fontSize: 18,
                boxShadow: "0 6px 22px oklch(0.5 0.12 235 / 0.35)",
              }}>W</div>
              <div>
                <div style={{ fontSize: 18, fontWeight: 500, color: "var(--n-10)", letterSpacing: -0.01 }}>washmachine</div>
                <div className="mono" style={{ fontSize: 11, color: "var(--n-7)" }}>v2.1.0 · loader builder</div>
              </div>
            </div>
          </div>

          {/* Headline */}
          <div>
            <div style={{
              fontSize: 44, lineHeight: 1.05, letterSpacing: -0.025,
              fontWeight: 500, color: "var(--n-10)", maxWidth: 540,
            }}>
              The last <span style={{ color: "var(--acc)" }}>shellcode loader</span>{" "}
              builder you'll ever need.
            </div>
            <div style={{ fontSize: 15, color: "var(--n-8)", marginTop: 18, maxWidth: 520, lineHeight: 1.55 }}>
              YAML-driven playbooks. Pluggable evasion modules. Auto-discovered toolchains.
              Every build is a session — source, log, manifest, artifact, reproducible.
            </div>

            <div className="row" style={{ marginTop: 28, gap: 10 }}>
              <Chip kind="acc">89 snippets</Chip>
              <Chip kind="acc">10 categories</Chip>
              <Chip kind="acc">6 templates</Chip>
              <Chip kind="acc">5 PE inject methods</Chip>
            </div>
          </div>

          {/* Footer */}
          <div className="mono" style={{ fontSize: 10, color: "var(--n-7)", letterSpacing: 0.04 }}>
            for authorized security research only · all builds logged to <span style={{ color: "var(--n-9)" }}>~/.washmachine/sessions</span>
          </div>
        </div>

        {/* Right — provisioning panel */}
        <div style={{
          background: "var(--n-1)",
          borderLeft: "1px solid var(--n-4)",
          padding: 48,
          display: "flex",
          flexDirection: "column",
          gap: 24,
        }}>
          <div>
            <H3>System check</H3>
            <div style={{ fontSize: 20, color: "var(--n-10)", fontWeight: 500, marginTop: 6, letterSpacing: -0.01 }}>
              Preparing your environment
            </div>
            <div style={{ fontSize: 13, color: "var(--n-8)", marginTop: 6 }}>
              Everything below is fetched once and verified on every launch.
            </div>
          </div>

          {/* Health checks */}
          <div className="card" style={{ padding: 0 }}>
            <Check label=".NET 8 Desktop Runtime"   detail="x64 · 8.0.11"           status="ok" />
            <Check label="Windows App SDK 1.8"      detail="installed system-wide"  status="ok" />
            <Check label="MSVC toolchain"           detail="cl.exe · 19.39.33523"   status="ok" />
            <Check label="Python 3.10+"             detail="3.12.4 · in PATH"       status="ok" />
            <Check label="Bin2Shell"                detail="downloading · 4.2 / 12 MB" status="running" pct={35} />
            <Check label="Shikata Ga Nai"           detail="not provisioned"        status="pending" action="Provision" />
            <Check label="vx_api_snippets.yaml"     detail="89 snippets · sha256 ok" status="ok" />
            <Check label="Output directory"         detail="~/.washmachine/out/"    status="ok" last />
          </div>

          <div className="meter" style={{ width: "100%", height: 4 }}>
            <i style={{ width: "78%" }} />
          </div>
          <div className="row" style={{ justifyContent: "space-between" }}>
            <span className="mono" style={{ fontSize: 11, color: "var(--n-7)" }}>6 of 8 ready · 1 provisioning · 1 optional</span>
            <span className="mono" style={{ fontSize: 11, color: "var(--n-8)" }}>est. 12s remaining</span>
          </div>

          <div style={{ flex: 1 }} />

          <div className="row" style={{ gap: 8 }}>
            <button className="btn ghost"><Icon name="cog" size={12} />Advanced</button>
            <div style={{ flex: 1 }} />
            <button className="btn">Skip optional</button>
            <button className="btn primary" disabled>
              Continue to workspace <Icon name="chev" size={12} />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function Check({ label, detail, status, pct, action, last }) {
  const dot =
    status === "ok"      ? <div style={{ width: 14, height: 14, borderRadius: "50%", background: "var(--ok-bg)", color: "var(--ok)", display: "grid", placeItems: "center" }}><Icon name="check" size={9} sw={3} /></div> :
    status === "running" ? <div style={{ position: "relative", width: 14, height: 14 }}>
                              <div className="pulse" style={{ position: "absolute", inset: 1, background: "var(--acc)", borderRadius: "50%" }} />
                              <div style={{ position: "absolute", inset: 1, background: "var(--acc)", borderRadius: "50%" }} />
                           </div> :
    status === "err"     ? <div style={{ width: 14, height: 14, borderRadius: "50%", background: "var(--err-bg)", color: "var(--err)", display: "grid", placeItems: "center" }}><Icon name="x" size={9} sw={3} /></div> :
                            <div style={{ width: 10, height: 10, margin: 2, borderRadius: "50%", border: "1px dashed var(--n-5)" }} />;

  return (
    <div style={{
      display: "grid",
      gridTemplateColumns: "22px 1fr auto",
      gap: 12,
      padding: "12px 16px",
      borderBottom: last ? "none" : "1px solid var(--n-3)",
      alignItems: "center",
    }}>
      {dot}
      <div>
        <div style={{ fontSize: 13, color: "var(--n-9)" }}>{label}</div>
        <div className="mono" style={{ fontSize: 11, color: status === "running" ? "var(--acc)" : "var(--n-7)", marginTop: 2 }}>{detail}</div>
        {status === "running" && (
          <div className="meter" style={{ width: 180, marginTop: 6, height: 3 }}>
            <i style={{ width: `${pct}%` }} />
          </div>
        )}
      </div>
      {status === "running" && <Chip kind="acc" dot>{pct}%</Chip>}
      {status === "ok" && <Chip kind="ok">ready</Chip>}
      {status === "pending" && action && <button className="btn"><Icon name="dl" size={11} />{action}</button>}
    </div>
  );
}

window.FrameStartup = FrameStartup;
