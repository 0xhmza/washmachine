/* Shell + shared primitives for all frames */

/* ─────────── Icons ─────────── */
// Lucide-style 1px strokes, sized 14 by default
function I({ d, size = 14, sw = 1.5, fill = "none" }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill={fill}
         stroke="currentColor" strokeWidth={sw} strokeLinecap="round" strokeLinejoin="round">
      <path d={d} />
    </svg>
  );
}
const ICONS = {
  bolt:    "M13 3L4 14h7l-1 7 9-11h-7l1-7z",
  inject:  "M14 6l-4 4 4 4M3 12h10M21 6v12",  // arrow-ish
  pkg:     "M3 7l9-4 9 4-9 4-9-4zM3 7v10l9 4 9-4V7M12 11v10",
  finish:  "M5 12l5 5L20 7",
  cog:     "M12 8a4 4 0 100 8 4 4 0 000-8zM19.4 15a1.7 1.7 0 00.3 1.8l.1.1a2 2 0 11-2.8 2.8l-.1-.1a1.7 1.7 0 00-1.8-.3 1.7 1.7 0 00-1 1.5V21a2 2 0 11-4 0v-.1a1.7 1.7 0 00-1-1.5 1.7 1.7 0 00-1.8.3l-.1.1A2 2 0 114.4 17l.1-.1a1.7 1.7 0 00.3-1.8 1.7 1.7 0 00-1.5-1H3a2 2 0 110-4h.1a1.7 1.7 0 001.5-1 1.7 1.7 0 00-.3-1.8l-.1-.1A2 2 0 117 4.4l.1.1a1.7 1.7 0 001.8.3H9a1.7 1.7 0 001-1.5V3a2 2 0 114 0v.1a1.7 1.7 0 001 1.5 1.7 1.7 0 001.8-.3l.1-.1a2 2 0 112.8 2.8l-.1.1a1.7 1.7 0 00-.3 1.8V9a1.7 1.7 0 001.5 1H21a2 2 0 110 4h-.1a1.7 1.7 0 00-1.5 1z",
  history: "M3 12a9 9 0 109-9M3 12V5M3 12h7M12 7v5l4 2",
  doc:     "M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8zM14 2v6h6M9 13h6M9 17h6M9 9h1",
  play:    "M5 3l14 9-14 9V3z",
  pause:   "M6 4h4v16H6zM14 4h4v16h-4z",
  stop:    "M5 5h14v14H5z",
  more:    "M5 12h.01M12 12h.01M19 12h.01",
  search:  "M11 19a8 8 0 100-16 8 8 0 000 16zM21 21l-4.3-4.3",
  cmd:     "M15 6h3a3 3 0 010 6h-3V6zm0 0V3a3 3 0 10-3 3h3zM9 18H6a3 3 0 010-6h3v6zm0 0v3a3 3 0 103-3H9zM9 6v12h6V6H9z",
  copy:    "M9 9h10v10H9zM5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1",
  dl:      "M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3",
  up:      "M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M17 8l-5-5-5 5M12 3v12",
  link:    "M10 13a5 5 0 007 0l4-4a5 5 0 00-7-7l-1 1M14 11a5 5 0 00-7 0l-4 4a5 5 0 007 7l1-1",
  shield:  "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z",
  bug:     "M8 2l2 3M16 2l-2 3M12 22v-7M5 12H2M22 12h-3M6 17l-2 2M18 17l2 2M6 7l-2-2M18 7l2-2M7 12a5 5 0 0110 0v3a5 5 0 01-10 0v-3z",
  file:    "M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8zM14 2v6h6",
  layers:  "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5",
  chev:    "M9 18l6-6-6-6",
  dot:     "M12 12.01",
  check:   "M5 12l5 5L20 7",
  x:       "M18 6L6 18M6 6l12 12",
  alert:   "M12 2L1 21h22L12 2zM12 9v5M12 18v.01",
  info:    "M12 22a10 10 0 100-20 10 10 0 000 20zM12 16v-4M12 8v.01",
  plus:    "M12 5v14M5 12h14",
  filter:  "M3 4h18l-7 9v6l-4 2v-8L3 4z",
  refresh: "M3 12a9 9 0 0115-7l3 3M21 12a9 9 0 01-15 7l-3-3M21 5v4h-4M3 19v-4h4",
  fold:    "M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z",
  term:    "M4 17l6-6-6-6M12 19h8",
  globe:   "M12 22a10 10 0 100-20 10 10 0 000 20zM2 12h20M12 2a15 15 0 010 20M12 2a15 15 0 000 20",
  flame:   "M12 2c2 4 6 6 6 11a6 6 0 11-12 0c0-3 2-4 2-7 2 1 4 2 4-4z",
  upload:  "M4 17v2a2 2 0 002 2h12a2 2 0 002-2v-2M7 9l5-5 5 5M12 4v12",
  beaker:  "M9 3v8L4 21h16L15 11V3M9 3h6M7 17h10",
};

function Icon({ name, size, sw }) {
  const d = ICONS[name] || "";
  return <I d={d} size={size} sw={sw} />;
}

/* ─────────── App shell (rail + title + pipeline + status) ─────────── */

const NAV_PRIMARY = [
  { id: "build", name: "Build",         icon: "bolt",    tag: "" },
  { id: "lib",   name: "Playbooks",     icon: "layers",  tag: "89" },
  { id: "hist",  name: "History",       icon: "history", tag: "127" },
  { id: "lab",   name: "Test lab",      icon: "beaker",  tag: "" },
  { id: "docs",  name: "Documentation", icon: "doc",     tag: "" },
];
const NAV_FOOTER = [
  { id: "set",   name: "Settings",      icon: "cog" },
];

function Rail({ active = "build" }) {
  return (
    <aside className="rail">
      <div className="rail-brand">
        <div className="rail-logo">W</div>
        <div className="rail-brand-text">
          <div className="nm">Washmachine</div>
          <div className="sb">Loader builder</div>
        </div>
      </div>
      <div className="rail-search">
        <Icon name="search" size={14} />
        <span style={{ flex: 1 }}>Search</span>
        <span className="kbd">Ctrl K</span>
      </div>
      <div className="rail-nav">
        {NAV_PRIMARY.map(n => (
          <div key={n.id} className={"rail-item" + (n.id === active ? " active" : "")}>
            <div className="ri-ico"><Icon name={n.icon} size={16} /></div>
            <div>{n.name}</div>
            {n.tag && <div className="ri-tag">{n.tag}</div>}
          </div>
        ))}
      </div>
      <div className="rail-sep" />
      <div className="rail-nav" style={{ paddingBottom: 8 }}>
        {NAV_FOOTER.map(n => (
          <div key={n.id} className={"rail-item" + (n.id === active ? " active" : "")}>
            <div className="ri-ico"><Icon name={n.icon} size={16} /></div>
            <div>{n.name}</div>
          </div>
        ))}
      </div>
      <div className="rail-status">
        <span className="rail-dot" title="all systems ready" />
        <span>All systems ready</span>
        <span style={{ color: "var(--n-7)", fontFamily: "var(--f-mono)", fontSize: 11 }}>v2.1.0</span>
      </div>
    </aside>
  );
}

/* The pipeline spine */
const PIPELINE = [
  { id: "src",  num: "01", name: "Source",   meta: "shellcode" },
  { id: "sgn",  num: "02", name: "SGN",      meta: "optional", optional: true },
  { id: "enc",  num: "03", name: "Encode",   meta: "Bin2Shell" },
  { id: "tpl",  num: "04", name: "Template", meta: "snippets" },
  { id: "cmp",  num: "05", name: "Compile",  meta: "C++ → exe" },
  { id: "bd",   num: "06", name: "Backdoor", meta: "PE inject", optional: true },
  { id: "pk",   num: "07", name: "Pack",     meta: "optional", optional: true },
  { id: "fn",   num: "08", name: "Finalize", meta: "clone / pad", optional: true },
];

function Pipeline({ active = "src", states = {}, runEnabled = true, running = false }) {
  return (
    <div className="pipe">
      {PIPELINE.map((s, i) => {
        const state = states[s.id] || (active === s.id ? "active" : (i < PIPELINE.findIndex(p => p.id === active) ? "done" : ""));
        const cls = ["pipe-stage"];
        if (active === s.id) cls.push("active");
        if (state === "done") cls.push("done");
        if (state === "skipped") cls.push("skipped");
        return (
          <React.Fragment key={s.id}>
            <div className={cls.join(" ")}>
              <span className="pipe-num">
                {s.num}
                {state === "done" && <Icon name="check" size={10} sw={2} />}
                {s.optional && state !== "done" && <span style={{ opacity: 0.5 }}>·opt</span>}
              </span>
              <span className="pipe-name">{s.name}</span>
              <span className="pipe-meta">{s.meta}</span>
            </div>
            {i < PIPELINE.length - 1 && <div className={"pipe-arrow" + (PIPELINE[i+1].optional ? " opt" : "")} />}
          </React.Fragment>
        );
      })}
      <div className="pipe-spacer" />
      <div className="pipe-run">
        {running ? (
          <button className="btn" style={{ borderColor: "rgba(255,153,164,0.35)", color: "var(--err)" }}>
            <Icon name="stop" size={12} /> Stop <span className="sk">Esc</span>
          </button>
        ) : (
          <>
            <button className="btn">Dry run</button>
            <button className="btn primary" disabled={!runEnabled}>
              <Icon name="play" size={12} sw={2} fill="currentColor" /> Build <span className="sk">Ctrl B</span>
            </button>
          </>
        )}
      </div>
    </div>
  );
}

function TitleBar({ crumbs = [], session = "session_20260519_001a" }) {
  return (
    <div className="tbar">
      <div className="tbar-crumbs">
        <b>Washmachine</b>
        {crumbs.map((c, i) => (
          <React.Fragment key={i}>
            <span className="sep">›</span>
            <span style={{ color: i === crumbs.length - 1 ? "var(--n-10)" : "var(--n-8)" }}>{c}</span>
          </React.Fragment>
        ))}
      </div>
      <div className="tbar-spacer" />
      <div className="tbar-quick">
        <Icon name="history" size={13} />
        <span>Session</span><b className="mono" style={{ color: "var(--n-10)" }}>{session}</b>
      </div>
      <button className="btn ghost" style={{ height: 28 }}>
        <Icon name="copy" size={13} />
      </button>
      <button className="btn ghost" style={{ height: 28 }}>
        <Icon name="info" size={13} />
      </button>
    </div>
  );
}

function StatusBar({ items = [] }) {
  return (
    <div className="sbar">
      {items.map((it, i) => (
        <div key={i} className="grp mono">
          {it.icon && <Icon name={it.icon} size={12} />}
          <span>{it.k}</span><b>{it.v}</b>
        </div>
      ))}
      <div className="spacer" />
      <div className="grp mono"><span>cl.exe</span><b>19.39.33523</b></div>
      <div className="grp mono"><span>x64</span></div>
      <div className="grp mono"><span>UTC</span><b>14:22:08</b></div>
    </div>
  );
}

/* ─── Generic Shell wrapper ─── */
function Shell({ active = "build", crumbs, pipeActive = "src", pipeStates = {}, running = false, status = [], wide = false, children, modal = null }) {
  return (
    <div className="app">
      <Rail active={active} />
      <div className="main">
        <TitleBar crumbs={crumbs} />
        <Pipeline active={pipeActive} states={pipeStates} running={running} />
        <div className={"work" + (wide ? " wide" : "")}>{children}</div>
        <StatusBar items={status} />
      </div>
      {modal}
    </div>
  );
}

/* ─── small bits ─── */
function H3({ children }) { return <div className="h3">{children}</div>; }
function Sec({ title, action, children }) {
  return (
    <div className="sec">
      <div className="sec-hd">
        <H3>{title}</H3>
        <div style={{ flex: 1 }} />
        {action}
      </div>
      {children}
    </div>
  );
}
function Field({ label, hint, children }) {
  return (
    <div className="field">
      {label && <label>{label}</label>}
      {children}
      {hint && <span className="hint">{hint}</span>}
    </div>
  );
}
function Chip({ kind = "", dot = false, children }) {
  return <span className={"chip " + (kind ? kind : "") + (dot ? " dot" : "")}>{children}</span>;
}
function Toggle({ on }) { return <span className={"tog" + (on ? " on" : "")} />; }
function Seg({ value, options }) {
  return (
    <div className="seg">
      {options.map(o => <button key={o.v} className={o.v === value ? "on" : ""}>{o.icon && <Icon name={o.icon} size={11} />}{o.l}</button>)}
    </div>
  );
}

/* Pretty C++ snippet rendering: array of [class, text] tokens, line-by-line */
function CodeBlock({ lines, startLine = 1, highlight = [] }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "40px 1fr", padding: "12px 0" }}>
      <div className="gutter">
        {lines.map((_, i) => <div key={i}>{startLine + i}</div>)}
      </div>
      <pre className="code" style={{ margin: 0, paddingRight: 14 }}>
        {lines.map((toks, i) => (
          <div key={i} style={highlight.includes(i) ? { background: "oklch(0.4 0.08 235 / 0.12)" } : null}>
            {toks.map(([c, t], j) => <span key={j} className={c}>{t}</span>)}
            {"\n"}
          </div>
        ))}
      </pre>
    </div>
  );
}

Object.assign(window, { Icon, Shell, Rail, Pipeline, TitleBar, StatusBar, Sec, Field, Chip, Toggle, Seg, CodeBlock, H3, PIPELINE });
