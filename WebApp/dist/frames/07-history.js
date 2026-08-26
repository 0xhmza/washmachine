function FrameHistory() {
  const sessions = [{
    ts: "14:22:08",
    date: "today",
    id: "session_20260519_001a",
    tpl: "full-loader",
    enc: "XOR+B64",
    snips: 5,
    bd: "putty.exe",
    size: "2.21 MB",
    status: "ok",
    current: true
  }, {
    ts: "11:04:51",
    date: "today",
    id: "session_20260519_0019",
    tpl: "minimal",
    enc: "—",
    snips: 1,
    bd: "—",
    size: "12.4 KB",
    status: "ok"
  }, {
    ts: "09:18:33",
    date: "today",
    id: "session_20260519_0018",
    tpl: "staged-http",
    enc: "AES+B91",
    snips: 4,
    bd: "—",
    size: "18.2 KB",
    status: "ok"
  }, {
    ts: "22:51:09",
    date: "yesterday",
    id: "session_20260518_0024",
    tpl: "full-loader",
    enc: "RC4+B64",
    snips: 6,
    bd: "OneDrive.exe",
    size: "4.8 MB",
    status: "ok"
  }, {
    ts: "16:33:07",
    date: "yesterday",
    id: "session_20260518_0021",
    tpl: "full-loader",
    enc: "ChaCha+Hex",
    snips: 5,
    bd: "putty.exe",
    size: "1.92 MB",
    status: "warn"
  }, {
    ts: "14:01:22",
    date: "yesterday",
    id: "session_20260518_001f",
    tpl: "reflective",
    enc: "AES+B64",
    snips: 7,
    bd: "—",
    size: "44 KB",
    status: "err"
  }, {
    ts: "11:20:05",
    date: "yesterday",
    id: "session_20260518_001b",
    tpl: "tls-callback",
    enc: "XOR+B64",
    snips: 3,
    bd: "putty.exe",
    size: "1.21 MB",
    status: "ok"
  }, {
    ts: "18:44:51",
    date: "May 17",
    id: "session_20260517_0030",
    tpl: "minimal",
    enc: "—",
    snips: 1,
    bd: "—",
    size: "11.8 KB",
    status: "ok"
  }, {
    ts: "10:00:11",
    date: "May 17",
    id: "session_20260517_0025",
    tpl: "cobalt-compat",
    enc: "RC4+B64",
    snips: 8,
    bd: "explorer.exe",
    size: "6.7 MB",
    status: "ok"
  }];
  return React.createElement(Shell, {
    active: "history",
    crumbs: ["History"],
    pipeActive: "",
    wide: true,
    pipeStates: Object.fromEntries(PIPELINE.map(p => [p.id, "skipped"])),
    status: [{
      icon: "info",
      k: "sessions",
      v: "127"
    }, {
      icon: "info",
      k: "disk",
      v: "4.2 GB used"
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
  }), React.createElement("div", {
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
    }
  })), React.createElement(Seg, {
    value: "all",
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
  }), React.createElement("button", {
    className: "btn"
  }, React.createElement(Icon, {
    name: "filter",
    size: 12
  }), "Filters"))), React.createElement("div", {
    className: "card flat",
    style: {
      marginBottom: 22,
      padding: 18
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 36
    }
  }, React.createElement(SummaryStat, {
    k: "Sessions \xB7 7d",
    v: "42",
    tail: "+8 vs prev"
  }), React.createElement(SummaryStat, {
    k: "Success rate",
    v: "94 %",
    tail: "2 errors"
  }), React.createElement(SummaryStat, {
    k: "Avg build time",
    v: "4.8s",
    tail: "median"
  }), React.createElement(SummaryStat, {
    k: "Disk",
    v: "4.2 GB",
    tail: "of 50 GB"
  }), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(BuildSpark, null))), React.createElement("div", {
    className: "card",
    style: {
      padding: 0
    }
  }, React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "100px 220px 140px 140px 60px 160px 100px 24px",
      padding: "10px 16px",
      borderBottom: "1px solid var(--n-4)",
      background: "var(--n-1)"
    }
  }, ["When", "Session", "Template", "Encoding", "Snips", "Backdoor target", "Size", ""].map((h, i) => React.createElement("div", {
    key: i,
    className: "h3",
    style: {
      fontSize: 10
    }
  }, h))), React.createElement("div", null, sessions.map((s, i) => {
    const prev = sessions[i - 1];
    const showDate = !prev || prev.date !== s.date;
    return React.createElement(React.Fragment, {
      key: s.id
    }, showDate && React.createElement("div", {
      className: "mono",
      style: {
        padding: "10px 16px 4px",
        fontSize: 10,
        color: "var(--n-7)",
        letterSpacing: 0.06,
        textTransform: "uppercase",
        borderTop: i > 0 ? "1px solid var(--n-3)" : "none"
      }
    }, s.date), React.createElement(SessionRow, s));
  })))));
}
function SessionRow({
  ts,
  id,
  tpl,
  enc,
  snips,
  bd,
  size,
  status,
  current
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
  }[status];
  return React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "100px 220px 140px 140px 60px 160px 100px 24px",
      padding: "12px 16px",
      borderBottom: "1px solid var(--n-3)",
      alignItems: "center",
      cursor: "pointer",
      ...(current ? {
        background: "var(--acc-bg)",
        boxShadow: "inset 2px 0 0 var(--acc)"
      } : {})
    }
  }, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, ts), React.createElement("div", null, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      color: "var(--n-10)"
    }
  }, id), React.createElement("div", {
    className: "row",
    style: {
      gap: 6,
      marginTop: 3
    }
  }, statusChip, current && React.createElement(Chip, {
    kind: "acc"
  }, "current"))), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      color: "var(--n-9)"
    }
  }, tpl), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      color: "var(--n-8)"
    }
  }, enc), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      color: "var(--n-8)",
      textAlign: "center"
    }
  }, snips), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      color: bd === "—" ? "var(--n-6)" : "var(--n-9)"
    }
  }, bd), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      color: "var(--n-9)",
      textAlign: "right"
    }
  }, size), React.createElement(Icon, {
    name: "more",
    size: 14
  }));
}
function SummaryStat({
  k,
  v,
  tail
}) {
  return React.createElement("div", null, React.createElement("div", {
    className: "h3",
    style: {
      marginBottom: 4
    }
  }, k), React.createElement("div", {
    style: {
      fontSize: 22,
      color: "var(--n-10)",
      fontWeight: 500,
      letterSpacing: -0.018,
      fontFamily: "var(--f-mono)"
    }
  }, v), React.createElement("div", {
    style: {
      fontSize: 10,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, tail));
}
function BuildSpark() {
  const days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
  return React.createElement("div", null, React.createElement("div", {
    className: "h3",
    style: {
      marginBottom: 6
    }
  }, "Build cadence \xB7 last 7 days"), React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "flex-end",
      height: 36,
      gap: 3,
      width: 440
    }
  }, Array.from({
    length: 56
  }, (_, i) => {
    const h = 0.2 + Math.abs(Math.sin(i * 0.7) * Math.cos(i * 0.3)) * 0.85;
    const fail = i === 27 || i === 41;
    return React.createElement("div", {
      key: i,
      style: {
        flex: 1,
        height: `${h * 100}%`,
        background: fail ? "var(--err)" : "var(--acc)",
        opacity: fail ? 1 : 0.6 + h * 0.4,
        borderRadius: "2px 2px 0 0"
      }
    });
  })), React.createElement("div", {
    className: "row",
    style: {
      width: 440,
      marginTop: 6
    }
  }, days.map(d => React.createElement("div", {
    key: d,
    style: {
      flex: 1,
      fontFamily: "var(--f-mono)",
      fontSize: 9,
      color: "var(--n-7)",
      textAlign: "center"
    }
  }, d))));
}
window.FrameHistory = FrameHistory;