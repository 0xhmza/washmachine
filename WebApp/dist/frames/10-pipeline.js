function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function FramePipeline() {
  const stages = [{
    id: "src",
    num: "01",
    name: "Source",
    icon: "file",
    tone: "ok",
    detail: "calc_x64.bin · 327 B · sha256 4f7b…a9d2",
    time: "0.01s"
  }, {
    id: "sgn",
    num: "02",
    name: "SGN",
    icon: "shield",
    tone: "ok",
    detail: "2 passes · pre · max 64 B",
    time: "0.71s",
    optional: true
  }, {
    id: "enc",
    num: "03",
    name: "Encode",
    icon: "bolt",
    tone: "ok",
    detail: "XOR (key 9fa24cd7) → Base64 · 789 B",
    time: "0.17s"
  }, {
    id: "tpl",
    num: "04",
    name: "Template",
    icon: "doc",
    tone: "ok",
    detail: "full-loader · 5 snippets composed",
    time: "0.04s"
  }, {
    id: "cmp",
    num: "05",
    name: "Compile",
    icon: "play",
    tone: "ok",
    detail: "cl.exe /O2 /MT → 20260519-4f7ba9d2.exe",
    time: "3.21s"
  }, {
    id: "bd",
    num: "06",
    name: "Backdoor",
    icon: "inject",
    tone: "ok",
    detail: "putty.exe · code cave @ 0x004f3a18",
    time: "0.48s",
    optional: true
  }, {
    id: "pk",
    num: "07",
    name: "Pack",
    icon: "pkg",
    tone: "ok",
    detail: "Custom RC4 · 2.04 MB · entropy 7.84",
    time: "0.92s",
    optional: true
  }, {
    id: "fn",
    num: "08",
    name: "Finalize",
    icon: "finish",
    tone: "acc",
    detail: "Clone OneDrive.exe · +1.0 MiB NOP pad",
    time: "0.31s",
    optional: true,
    running: true
  }];
  return React.createElement(Shell, {
    active: "pipeline",
    crumbs: ["Pipeline"],
    pipeActive: "",
    wide: true,
    pipeStates: Object.fromEntries(PIPELINE.map(p => [p.id, "skipped"])),
    status: [{
      icon: "info",
      k: "session",
      v: "session_20260519_001a"
    }, {
      icon: "info",
      k: "elapsed",
      v: "00:05.8"
    }, {
      icon: "info",
      k: "remaining",
      v: "Finalize · ~3s"
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
  }, "Active session"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 14,
      color: "var(--n-10)",
      marginTop: 4
    }
  }, "session_20260519_001a")), React.createElement(Divider, null), React.createElement("div", null, React.createElement("div", {
    className: "h3"
  }, "Status"), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 4,
      gap: 6
    }
  }, React.createElement("span", {
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
  }, "Running"))), React.createElement(Divider, null), React.createElement("div", null, React.createElement("div", {
    className: "h3"
  }, "Elapsed"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 14,
      color: "var(--n-10)",
      marginTop: 4
    }
  }, "00:05.8")), React.createElement(Divider, null), React.createElement("div", {
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
      width: "82%"
    }
  })), React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between",
      marginTop: 6,
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, React.createElement("span", null, "7 of 8 done \xB7 Finalize in progress"), React.createElement("span", null, "est. 3s remaining")))), React.createElement("button", {
    className: "btn"
  }, React.createElement(Icon, {
    name: "copy",
    size: 12
  }), "Copy manifest"), React.createElement("button", {
    className: "btn ghost"
  }, React.createElement(Icon, {
    name: "dl",
    size: 12
  }), "Export"))), React.createElement("div", {
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
  }), stages.map((s, i) => React.createElement(StageRow, _extends({
    key: s.id
  }, s, {
    last: i === stages.length - 1
  }))))));
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
  time,
  optional,
  running,
  last
}) {
  const dotColor = running ? "var(--acc)" : tone === "ok" ? "var(--ok)" : tone === "warn" ? "var(--warn)" : tone === "err" ? "var(--err)" : "var(--n-5)";
  const bg = running ? "var(--acc-bg)" : tone === "ok" ? "var(--ok-bg)" : "var(--n-2)";
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
      gridTemplateColumns: "32px 1fr auto auto auto",
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
  }, detail)), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: running ? "var(--acc)" : "var(--n-7)",
      textAlign: "right"
    }
  }, running ? "…" : time), React.createElement("button", {
    className: "btn ghost",
    style: {
      height: 26,
      padding: "0 10px"
    }
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "Log"), React.createElement("button", {
    className: "btn ghost",
    style: {
      height: 26,
      padding: "0 10px"
    }
  }, React.createElement(Icon, {
    name: "cog",
    size: 11
  }), "Edit")));
}
window.FramePipeline = FramePipeline;