function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function FrameHistory() {
  const {
    useField,
    useFields
  } = window.washState;
  const meta = useFields('history_sessions');
  const [filter, setFilter] = React.useState('all');
  const [search, setSearch] = React.useState('');
  const [sessions, setSessions] = React.useState(meta.history_sessions || []);
  React.useEffect(() => {
    window.wash.invoke('history-list', {}).then(r => {
      if (r && r.ok && r.sessions) {
        setSessions(r.sessions);
        window.washState.update({
          history_sessions: r.sessions
        });
      }
    }).catch(() => {});
  }, []);
  React.useEffect(() => {
    if (meta.history_sessions) setSessions(meta.history_sessions);
  }, [meta.history_sessions]);
  function deleteSession(id) {
    if (!window.confirm('Delete this history entry and its session files?')) return;
    window.wash.invoke('history-delete', {
      id
    }).then(r => {
      if (r && r.ok) {
        const next = sessions.filter(s => s.id !== id);
        setSessions(next);
        window.washState.update({
          history_sessions: next
        });
      }
    }).catch(() => {});
  }
  function clearHistory() {
    if (!window.confirm('Clear all payload history and session logs? This cannot be undone.')) return;
    window.wash.invoke('history-clear', {}).then(r => {
      if (r && r.ok) {
        setSessions([]);
        window.washState.update({
          history_sessions: []
        });
      }
    }).catch(() => {});
  }
  const filtered = sessions.filter(s => {
    if (filter !== 'all' && s.status !== filter) return false;
    if (search && !JSON.stringify(s).toLowerCase().includes(search.toLowerCase())) return false;
    return true;
  });
  const totalCount = sessions.length;
  return React.createElement(Shell, {
    active: "history",
    crumbs: ["History"],
    pipeActive: "",
    wide: true,
    pipeStates: Object.fromEntries((typeof PIPELINE !== 'undefined' ? PIPELINE : []).map(p => [p.id, "skipped"])),
    status: [{
      icon: "info",
      k: "sessions",
      v: String(totalCount)
    }]
  }, React.createElement("div", {
    className: "cfg",
    style: {
      padding: "20px 28px 0"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      marginBottom: 22,
      alignItems: "baseline"
    }
  }, React.createElement("h1", {
    className: "h1",
    style: {
      marginRight: 14
    }
  }, "History"), React.createElement("span", {
    className: "sub"
  }, "Every build leaves a session \u2014 source, log, manifest, and the artifact itself."), React.createElement("div", {
    style: {
      flex: 1
    }
  }), totalCount > 0 && React.createElement("button", {
    className: "btn",
    onClick: clearHistory
  }, React.createElement(Icon, {
    name: "x",
    size: 12
  }), "Clear all"), React.createElement("div", {
    className: "row",
    style: {
      gap: 8
    }
  }, React.createElement("div", {
    className: "input-wrap",
    style: {
      width: 240
    }
  }, React.createElement("span", {
    style: {
      position: "absolute",
      left: 10,
      top: "50%",
      transform: "translateY(-50%)",
      color: "var(--n-6)"
    }
  }, React.createElement(Icon, {
    name: "search",
    size: 12
  })), React.createElement("input", {
    className: "input mono",
    placeholder: "search hash, snippet, donor\u2026",
    style: {
      paddingLeft: 32,
      width: "100%"
    },
    value: search,
    onChange: e => setSearch(e.target.value)
  })), React.createElement(Seg, {
    value: filter,
    onChange: setFilter,
    options: [{
      v: "all",
      l: "All"
    }, {
      v: "ok",
      l: "OK"
    }, {
      v: "warn",
      l: "Warn"
    }, {
      v: "err",
      l: "Errors"
    }]
  }))), filtered.length === 0 ? React.createElement("div", {
    className: "card",
    style: {
      padding: "32px 20px",
      textAlign: "center",
      color: "var(--n-6)",
      fontSize: 13
    }
  }, totalCount === 0 ? 'No build sessions yet. Start a build to create the first one.' : 'No sessions match the current filter.') : React.createElement("div", {
    className: "card",
    style: {
      padding: 0
    }
  }, React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "100px 220px 140px 120px 100px 80px 24px",
      padding: "10px 16px",
      borderBottom: "1px solid var(--n-4)",
      background: "var(--n-1)"
    }
  }, ["When", "Session", "Template", "Encoder", "Source", "Size", ""].map((h, i) => React.createElement("div", {
    key: i,
    className: "h3",
    style: {
      fontSize: 10
    }
  }, h))), React.createElement("div", null, filtered.map((s, i) => React.createElement(SessionRow, _extends({
    key: s.id || i
  }, s, {
    onDelete: () => deleteSession(s.id)
  })))))));
}
function SessionRow({
  id,
  timestamp,
  date,
  templateId,
  encoderName,
  sourceName,
  outputSize,
  status,
  outputPath,
  onDelete
}) {
  const statusChip = {
    ok: React.createElement(Chip, {
      kind: "ok",
      dot: true
    }, "ok"),
    warn: React.createElement(Chip, {
      kind: "warn",
      dot: true
    }, "warn"),
    err: React.createElement(Chip, {
      kind: "err",
      dot: true
    }, "fail")
  }[status] || React.createElement(Chip, null, status || '?');
  const timeStr = timestamp ? new Date(timestamp).toLocaleTimeString('en-GB', {
    hour12: false
  }) : date || '';
  function reveal() {
    if (outputPath) window.wash.invoke('reveal-file', {
      path: outputPath
    }).catch(() => {});
  }
  return React.createElement("div", {
    onClick: reveal,
    title: outputPath ? 'Reveal output in Explorer' : '',
    style: {
      display: "grid",
      gridTemplateColumns: "100px 220px 140px 120px 100px 80px 24px",
      padding: "12px 16px",
      borderBottom: "1px solid var(--n-3)",
      alignItems: "center",
      cursor: outputPath ? "pointer" : "default"
    }
  }, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, timeStr), React.createElement("div", null, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-10)"
    }
  }, id), React.createElement("div", {
    className: "row",
    style: {
      gap: 6,
      marginTop: 3
    }
  }, statusChip)), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-9)"
    }
  }, templateId || '—'), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, encoderName || '—'), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)",
      overflow: "hidden",
      textOverflow: "ellipsis",
      whiteSpace: "nowrap"
    }
  }, sourceName || '—'), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, outputSize || '—'), React.createElement("div", {
    onClick: e => {
      e.stopPropagation();
      onDelete();
    },
    style: {
      cursor: "pointer",
      color: "var(--n-6)",
      display: "flex",
      alignItems: "center"
    },
    title: "Delete"
  }, React.createElement(Icon, {
    name: "x",
    size: 13
  })));
}
window.FrameHistory = FrameHistory;