/* Frame 07 — History / sessions */

function FrameHistory() {
  const { useField, useFields } = window.washState;
  const meta = useFields('history_sessions');
  const [filter, setFilter] = React.useState('all');
  const [search, setSearch] = React.useState('');
  const [sessions, setSessions] = React.useState(meta.history_sessions || []);

  // Load history on mount
  React.useEffect(() => {
    window.wash.invoke('history-list', {})
      .then(r => {
        if (r && r.ok && r.sessions) {
          setSessions(r.sessions);
          window.washState.update({ history_sessions: r.sessions });
        }
      })
      .catch(() => {});
  }, []);

  // Keep in sync with state
  React.useEffect(() => {
    if (meta.history_sessions) setSessions(meta.history_sessions);
  }, [meta.history_sessions]);

  function deleteSession(id) {
    window.wash.invoke('history-delete', { id })
      .then(r => {
        if (r && r.ok) {
          const next = sessions.filter(s => s.id !== id);
          setSessions(next);
          window.washState.update({ history_sessions: next });
        }
      })
      .catch(() => {});
  }

  // Filter + search
  const filtered = sessions.filter(s => {
    if (filter !== 'all' && s.status !== filter) return false;
    if (search && !JSON.stringify(s).toLowerCase().includes(search.toLowerCase())) return false;
    return true;
  });

  const totalCount = sessions.length;

  return (
    <Shell active="history" crumbs={["History"]} pipeActive="" wide
      pipeStates={Object.fromEntries((typeof PIPELINE !== 'undefined' ? PIPELINE : []).map(p => [p.id, "skipped"]))}
      status={[{ icon: "info", k: "sessions", v: String(totalCount) }]}>
      <div className="cfg" style={{ padding: "20px 28px 0" }}>
        <div className="row" style={{ marginBottom: 22, alignItems: "baseline" }}>
          <h1 className="h1" style={{ marginRight: 14 }}>History</h1>
          <span className="sub">Every build leaves a session — source, log, manifest, and the artifact itself.</span>
          <div style={{ flex: 1 }} />
          <div className="row" style={{ gap: 8 }}>
            <div className="input-wrap" style={{ width: 240 }}>
              <span style={{ position: "absolute", left: 10, top: "50%", transform: "translateY(-50%)", color: "var(--n-6)" }}>
                <Icon name="search" size={12} />
              </span>
              <input className="input mono" placeholder="search hash, snippet, donor…"
                style={{ paddingLeft: 32, width: "100%" }}
                value={search} onChange={e => setSearch(e.target.value)} />
            </div>
            <Seg value={filter} onChange={setFilter} options={[
              { v: "all", l: "All" },
              { v: "ok", l: "OK" },
              { v: "warn", l: "Warn" },
              { v: "err", l: "Errors" },
            ]} />
          </div>
        </div>

        {filtered.length === 0 ? (
          <div className="card" style={{ padding: "32px 20px", textAlign: "center", color: "var(--n-6)", fontSize: 13 }}>
            {totalCount === 0 ? 'No build sessions yet. Start a build to create the first one.' : 'No sessions match the current filter.'}
          </div>
        ) : (
          <div className="card" style={{ padding: 0 }}>
            <div style={{ display: "grid", gridTemplateColumns: "100px 220px 140px 120px 100px 80px 24px", padding: "10px 16px", borderBottom: "1px solid var(--n-4)", background: "var(--n-1)" }}>
              {["When", "Session", "Template", "Encoder", "Source", "Size", ""].map((h, i) => (
                <div key={i} className="h3" style={{ fontSize: 10 }}>{h}</div>
              ))}
            </div>
            <div>
              {filtered.map((s, i) => (
                <SessionRow key={s.id || i} {...s} onDelete={() => deleteSession(s.id)} />
              ))}
            </div>
          </div>
        )}
      </div>
    </Shell>
  );
}

function SessionRow({ id, timestamp, date, templateId, encoderName, sourceName, outputSize, status, outputPath, onDelete }) {
  const statusChip = {
    ok:   <Chip kind="ok" dot>ok</Chip>,
    warn: <Chip kind="warn" dot>warn</Chip>,
    err:  <Chip kind="err" dot>fail</Chip>,
  }[status] || <Chip>{status || '?'}</Chip>;

  const timeStr = timestamp ? new Date(timestamp).toLocaleTimeString('en-GB', { hour12: false }) : date || '';

  function reveal() {
    if (outputPath) window.wash.invoke('reveal-file', { path: outputPath }).catch(() => {});
  }

  return (
    <div style={{
      display: "grid",
      gridTemplateColumns: "100px 220px 140px 120px 100px 80px 24px",
      padding: "12px 16px", borderBottom: "1px solid var(--n-3)",
      alignItems: "center", cursor: "pointer",
    }}>
      <div className="mono" style={{ fontSize: 11, color: "var(--n-8)" }}>{timeStr}</div>
      <div>
        <div className="mono" style={{ fontSize: 11, color: "var(--n-10)" }}>{id}</div>
        <div className="row" style={{ gap: 6, marginTop: 3 }}>{statusChip}</div>
      </div>
      <div className="mono" style={{ fontSize: 11, color: "var(--n-9)" }}>{templateId || '—'}</div>
      <div className="mono" style={{ fontSize: 11, color: "var(--n-8)" }}>{encoderName || '—'}</div>
      <div className="mono" style={{ fontSize: 11, color: "var(--n-8)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{sourceName || '—'}</div>
      <div className="mono" style={{ fontSize: 11, color: "var(--n-8)" }}>{outputSize || '—'}</div>
      <div onClick={e => { e.stopPropagation(); onDelete(); }} style={{ cursor: "pointer", color: "var(--n-6)", display: "flex", alignItems: "center" }} title="Delete">
        <Icon name="x" size={13} />
      </div>
    </div>
  );
}

window.FrameHistory = FrameHistory;

