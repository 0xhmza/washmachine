function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function FramePipeline() {
  const {
    useFields
  } = window.washState;
  const state = useFields('build_running', 'build_log', 'build_lastResult');
  const running = state.build_running;
  const log = state.build_log || [];
  const lastResult = state.build_lastResult;
  const stageStatus = React.useMemo(() => {
    const out = {};
    for (const entry of log) {
      const line = typeof entry === 'string' ? entry : entry.line;
      if (!line) continue;
      const m = line.match(/\[stage:(\w+)\]\s*(done|warn|err|running)/i);
      if (m) out[m[1]] = m[2].toLowerCase();
    }
    return out;
  }, [log]);
  const STAGE_DEFS = [{
    id: "src",
    num: "01",
    name: "Source",
    icon: "file",
    optional: false
  }, {
    id: "sgn",
    num: "02",
    name: "SGN",
    icon: "shield",
    optional: true
  }, {
    id: "enc",
    num: "03",
    name: "Encode",
    icon: "bolt",
    optional: false
  }, {
    id: "tpl",
    num: "04",
    name: "Template",
    icon: "doc",
    optional: false
  }, {
    id: "cmp",
    num: "05",
    name: "Compile",
    icon: "play",
    optional: false
  }, {
    id: "bd",
    num: "06",
    name: "Backdoor",
    icon: "inject",
    optional: true
  }, {
    id: "pk",
    num: "07",
    name: "Pack",
    icon: "pkg",
    optional: true
  }, {
    id: "fn",
    num: "08",
    name: "Finalize",
    icon: "finish",
    optional: true
  }];
  const done = STAGE_DEFS.filter(s => stageStatus[s.id] === 'done').length;
  const total = STAGE_DEFS.length;
  const frac = running && total > 0 ? done / total : lastResult ? 1 : 0;
  const sessionId = lastResult?.sessionId || (log.length > 0 ? 'building…' : '—');
  const statusLabel = running ? 'Running' : lastResult ? lastResult.ok ? 'Success' : 'Failed' : 'Idle';
  const statusKind = running ? 'acc' : lastResult ? lastResult.ok ? 'ok' : 'err' : null;
  return React.createElement(Shell, {
    active: "pipeline",
    crumbs: ["Pipeline"],
    pipeActive: "",
    wide: true,
    pipeStates: Object.fromEntries((typeof PIPELINE !== 'undefined' ? PIPELINE : []).map(p => [p.id, stageStatus[p.id] || "skipped"])),
    status: [{
      icon: "info",
      k: "session",
      v: sessionId
    }, {
      icon: "info",
      k: "status",
      v: statusLabel
    }]
  }, React.createElement("div", {
    className: "cfg",
    style: {
      padding: "22px 28px"
    }
  }, React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "baseline",
      gap: 14,
      marginBottom: 4
    }
  }, React.createElement("h1", {
    className: "h1"
  }, "Pipeline"), React.createElement("span", {
    className: "sub"
  }, "Full build at a glance \u2014 every stage, its config, status, and output artifact.")), React.createElement("div", {
    className: "card flat",
    style: {
      marginTop: 18,
      marginBottom: 22,
      padding: "16px 18px"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 18
    }
  }, React.createElement("div", null, React.createElement("div", {
    className: "h3"
  }, "Session"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 14,
      color: "var(--n-10)",
      marginTop: 4
    }
  }, sessionId)), React.createElement(Divider, null), React.createElement("div", null, React.createElement("div", {
    className: "h3"
  }, "Status"), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 4,
      gap: 6
    }
  }, running && React.createElement("span", {
    style: {
      width: 8,
      height: 8,
      borderRadius: "50%",
      background: "var(--acc)"
    },
    className: "pulse"
  }), React.createElement("span", {
    style: {
      fontSize: 14,
      color: "var(--n-10)",
      fontWeight: 500
    }
  }, statusLabel))), React.createElement(Divider, null), React.createElement("div", {
    style: {
      flex: 1
    }
  }, React.createElement("div", {
    className: "h3"
  }, "Progress"), React.createElement("div", {
    style: {
      marginTop: 8
    }
  }, React.createElement("div", {
    className: "meter",
    style: {
      height: 6
    }
  }, React.createElement("i", {
    style: {
      width: `${frac * 100}%`
    }
  })), React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between",
      marginTop: 6,
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, React.createElement("span", null, done, " of ", total, " stages done")))))), React.createElement("div", {
    style: {
      position: "relative",
      paddingLeft: 24
    }
  }, React.createElement("div", {
    style: {
      position: "absolute",
      top: 14,
      bottom: 14,
      left: 31,
      width: 1,
      background: "var(--n-4)"
    }
  }), STAGE_DEFS.map((s, i) => {
    const st = stageStatus[s.id];
    const tone = st === 'done' ? 'ok' : st === 'warn' ? 'warn' : st === 'err' ? 'err' : st === 'running' ? 'acc' : null;
    const isRunning = st === 'running';
    const detail = lastResult?.stageDetails?.[s.id] || null;
    return React.createElement(StageRow, _extends({
      key: s.id
    }, s, {
      tone: tone,
      running: isRunning,
      detail: detail,
      last: i === STAGE_DEFS.length - 1
    }));
  })), log.length > 0 && React.createElement("div", {
    className: "card",
    style: {
      marginTop: 22,
      padding: 0
    }
  }, React.createElement("div", {
    className: "h3",
    style: {
      padding: "12px 16px",
      borderBottom: "1px solid var(--n-4)"
    }
  }, "Last build log"), React.createElement("div", {
    style: {
      padding: 12,
      maxHeight: 200,
      overflowY: "auto",
      fontFamily: "var(--f-mono)",
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, log.slice(-30).map((entry, i) => React.createElement("div", {
    key: i
  }, typeof entry === 'string' ? entry : entry.line))))));
}
function Divider() {
  return React.createElement("div", {
    style: {
      width: 1,
      height: 30,
      background: "var(--n-4)"
    }
  });
}
function StageRow({
  num,
  name,
  icon,
  tone,
  detail,
  optional,
  running,
  last
}) {
  const dotColor = running ? "var(--acc)" : tone === "ok" ? "var(--ok)" : tone === "warn" ? "var(--warn)" : tone === "err" ? "var(--err)" : "var(--n-5)";
  const bg = running ? "var(--acc-bg)" : tone === "ok" ? "var(--ok-bg)" : "var(--n-2)";
  const detailStr = detail || (tone === 'ok' ? 'completed' : tone ? tone : 'pending');
  return React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "16px 1fr",
      gap: 18,
      paddingBottom: last ? 0 : 18,
      position: "relative"
    }
  }, React.createElement("div", {
    style: {
      width: 16,
      height: 16,
      borderRadius: "50%",
      background: bg,
      border: "1px solid " + dotColor,
      display: "grid",
      placeItems: "center",
      marginTop: 14,
      position: "relative",
      zIndex: 1
    }
  }, running ? React.createElement("span", {
    className: "pulse",
    style: {
      width: 8,
      height: 8,
      borderRadius: "50%",
      background: "var(--acc)"
    }
  }) : React.createElement("span", {
    style: {
      width: 6,
      height: 6,
      borderRadius: "50%",
      background: dotColor
    }
  })), React.createElement("div", {
    style: {
      background: "var(--n-2)",
      border: "1px solid " + (running ? "var(--acc-line)" : "var(--n-4)"),
      borderRadius: 10,
      padding: "12px 16px",
      display: "grid",
      gridTemplateColumns: "32px 1fr auto",
      gap: 14,
      alignItems: "center"
    }
  }, React.createElement("div", {
    style: {
      width: 32,
      height: 32,
      borderRadius: 6,
      background: "var(--n-3)",
      display: "grid",
      placeItems: "center",
      color: running ? "var(--acc)" : "var(--n-8)"
    }
  }, React.createElement(Icon, {
    name: icon,
    size: 15
  })), React.createElement("div", null, React.createElement("div", {
    className: "row",
    style: {
      gap: 8
    }
  }, React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, num), React.createElement("span", {
    style: {
      fontSize: 14,
      color: "var(--n-10)",
      fontWeight: 500
    }
  }, name), optional && React.createElement("span", {
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, "optional"), running && React.createElement(Chip, {
    kind: "acc",
    dot: true
  }, "running")), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 3
    }
  }, detailStr)), tone && React.createElement(Chip, {
    kind: tone === 'ok' ? 'ok' : tone === 'err' ? 'err' : tone === 'warn' ? 'warn' : 'acc'
  }, tone)));
}
window.FramePipeline = FramePipeline;