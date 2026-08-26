function FrameCompile() {
  return React.createElement(Shell, {
    active: "compile",
    crumbs: ["Compile"],
    pipeActive: "cmp",
    running: true,
    pipeStates: {
      src: "done",
      sgn: "done",
      enc: "done",
      tpl: "done",
      cmp: "active"
    },
    status: [{
      icon: "info",
      k: "compiler",
      v: "cl.exe · MSVC 19.39"
    }, {
      icon: "info",
      k: "stage",
      v: "linking"
    }, {
      icon: "info",
      k: "elapsed",
      v: "00:04.2"
    }]
  }, React.createElement("div", {
    className: "cfg"
  }, React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "baseline",
      gap: 14,
      marginBottom: 22
    }
  }, React.createElement("h1", {
    className: "h1"
  }, "Compile"), React.createElement("span", {
    className: "sub"
  }, "Render template \u2192 invoke compiler \u2192 produce binary.")), React.createElement(Sec, {
    title: "Output",
    action: React.createElement(Chip, {
      kind: "acc"
    }, `{ts}-{sha8}.exe`)
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 16,
      alignItems: "flex-end"
    }
  }, React.createElement(Field, {
    label: "Filename pattern",
    hint: "{ts} = build time \xB7 {sha8} = first 8 of source hash"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "{ts}-{sha8}.exe",
    style: {
      width: 240
    }
  })), React.createElement(Field, {
    label: "Directory",
    hint: "resolved from AppPaths.OutputDirectory"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "C:\\\\Users\\\\hmza\\\\washmachine\\\\out\\\\",
    style: {
      width: 320
    }
  })), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(Field, {
    label: "Save session artifacts",
    hint: "source.cpp \xB7 build_log \xB7 UIData"
  }, React.createElement(Toggle, {
    on: true
  }))))), React.createElement(Sec, {
    title: "Toolchain",
    action: React.createElement("button", {
      className: "btn ghost"
    }, React.createElement(Icon, {
      name: "refresh",
      size: 12
    }), "Re-detect")
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 16,
      alignItems: "stretch"
    }
  }, React.createElement(CompCard, {
    name: "MSVC",
    sub: "cl.exe \xB7 19.39.33523",
    path: "C:\\\\\u2026\\\\VC\\\\Tools\\\\14.39.33519\\\\bin\\\\Hostx64\\\\x64",
    selected: true
  }), React.createElement(CompCard, {
    name: "MinGW",
    sub: "g++.exe \xB7 13.2.0",
    path: "C:\\\\msys64\\\\mingw64\\\\bin\\\\g++.exe"
  }), React.createElement(CompCard, {
    name: "Clang",
    sub: "clang++.exe \xB7 18.1.4",
    path: "C:\\\\Program Files\\\\LLVM\\\\bin\\\\clang++.exe"
  })), React.createElement("div", {
    className: "div"
  }), React.createElement("div", {
    className: "row",
    style: {
      gap: 16
    }
  }, React.createElement(Field, {
    label: "Optimization"
  }, React.createElement(Seg, {
    value: "O2",
    options: [{
      v: "Od",
      l: "Od"
    }, {
      v: "O1",
      l: "O1"
    }, {
      v: "O2",
      l: "O2"
    }, {
      v: "Os",
      l: "Os"
    }]
  })), React.createElement(Field, {
    label: "Subsystem"
  }, React.createElement(Seg, {
    value: "console",
    options: [{
      v: "console",
      l: "Console"
    }, {
      v: "windows",
      l: "Windows"
    }]
  })), React.createElement(Field, {
    label: "Strip symbols"
  }, React.createElement(Toggle, {
    on: true
  })), React.createElement(Field, {
    label: "Static CRT"
  }, React.createElement(Toggle, {
    on: true
  }))))), React.createElement(Sec, {
    title: "Build",
    action: React.createElement(Chip, {
      kind: "acc",
      dot: true
    }, "running")
  }, React.createElement("div", {
    className: "card"
  }, React.createElement(BuildStep, {
    done: true,
    label: "Render template",
    detail: "247 lines \xB7 4.1 KB",
    time: "0.04s"
  }), React.createElement(BuildStep, {
    done: true,
    label: "Write source.cpp",
    detail: "logging/session_20260519_001a/source.cpp",
    time: "0.01s"
  }), React.createElement(BuildStep, {
    done: true,
    label: "Resolve includes",
    detail: "windows.h \xB7 tlhelp32.h \xB7 intrin.h",
    time: "0.12s"
  }), React.createElement(BuildStep, {
    done: true,
    label: "Preprocess + compile",
    detail: "cl.exe /c /O2 /MT /GS- source.cpp",
    time: "3.21s"
  }), React.createElement(BuildStep, {
    running: true,
    label: "Link",
    detail: "cl.exe /Fe:20260519-4f7ba9d2.exe source.obj kernel32.lib user32.lib"
  }), React.createElement(BuildStep, {
    pending: true,
    label: "Sign artifacts",
    detail: "sha256 \xB7 session manifest"
  }), React.createElement(BuildStep, {
    pending: true,
    label: "Post-compile (optional)",
    detail: "clone donor resources \xB7 NOP pad"
  })))), React.createElement(BuildLogPreview, null));
}
function CompCard({
  name,
  sub,
  path,
  selected
}) {
  return React.createElement("div", {
    style: {
      flex: 1,
      padding: 14,
      borderRadius: 10,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-1)"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between"
    }
  }, React.createElement("div", {
    className: "h2"
  }, name), selected && React.createElement(Chip, {
    kind: "acc",
    dot: true
  }, "active")), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)",
      marginTop: 4
    }
  }, sub), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-6)",
      marginTop: 8,
      wordBreak: "break-all"
    }
  }, path));
}
function BuildStep({
  done,
  running,
  pending,
  label,
  detail,
  time
}) {
  const dot = React.createElement("div", {
    style: {
      width: 16,
      height: 16,
      position: "relative"
    }
  }, done && React.createElement("div", {
    style: {
      width: 14,
      height: 14,
      borderRadius: "50%",
      background: "var(--ok-bg)",
      color: "var(--ok)",
      display: "grid",
      placeItems: "center"
    }
  }, React.createElement(Icon, {
    name: "check",
    size: 9,
    sw: 3
  })), running && React.createElement(React.Fragment, null, React.createElement("div", {
    className: "pulse",
    style: {
      position: "absolute",
      inset: 1,
      background: "var(--acc)",
      borderRadius: "50%"
    }
  }), React.createElement("div", {
    style: {
      position: "absolute",
      inset: 1,
      background: "var(--acc)",
      borderRadius: "50%"
    }
  })), pending && React.createElement("div", {
    style: {
      width: 10,
      height: 10,
      margin: 2,
      borderRadius: "50%",
      border: "1px dashed var(--n-5)"
    }
  }));
  return React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "20px 1fr auto",
      gap: 14,
      padding: "10px 0",
      borderBottom: "1px solid var(--n-3)",
      opacity: pending ? 0.5 : 1
    }
  }, dot, React.createElement("div", null, React.createElement("div", {
    className: "h2",
    style: {
      fontSize: 13
    }
  }, label), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, detail)), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: running ? "var(--acc)" : "var(--n-7)"
    }
  }, time || (running ? "…" : pending ? "—" : "")));
}
function BuildLogPreview() {
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "source.cpp"), React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "term",
    size: 11
  }), "build_log.txt ", React.createElement("span", {
    className: "pulse",
    style: {
      width: 6,
      height: 6,
      background: "var(--acc)",
      borderRadius: "50%",
      marginLeft: 4
    }
  })), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "layers",
    size: 11
  }), "artifact"), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "dl",
    size: 11
  }))), React.createElement("div", {
    className: "pbody",
    style: {
      background: "var(--n-0)"
    }
  }, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      lineHeight: 1.7,
      padding: "14px 16px",
      color: "var(--n-8)"
    }
  }, React.createElement(LogLine, {
    ts: "14:22:01.082",
    lvl: "info",
    msg: "\u203A compile session_20260519_001a started"
  }), React.createElement(LogLine, {
    ts: "14:22:01.103",
    lvl: "dbg",
    msg: "catalog: Assets/vx_api_snippets.yaml (1 file, 248 KB)"
  }), React.createElement(LogLine, {
    ts: "14:22:01.110",
    lvl: "dbg",
    msg: "template: full-loader \xB7 placeholders=5"
  }), React.createElement(LogLine, {
    ts: "14:22:01.142",
    lvl: "info",
    msg: "rendered source.cpp \xB7 247 lines \xB7 4.1 KB \xB7 sha256=2c9e\u2026b1"
  }), React.createElement(LogLine, {
    ts: "14:22:01.150",
    lvl: "info",
    msg: "SGN preprocess: 2 passes \xB7 max=64 (327 B \u2192 481 B)"
  }), React.createElement(LogLine, {
    ts: "14:22:01.842",
    lvl: "dbg",
    msg: "Bin2Shell: encoder=1 envelope=1 key=9fa24cd7"
  }), React.createElement(LogLine, {
    ts: "14:22:02.011",
    lvl: "info",
    msg: "encoded: 481 B \u2192 789 B (Base64)"
  }), React.createElement(LogLine, {
    ts: "14:22:02.014",
    lvl: "info",
    msg: "spawn cl.exe /c /O2 /MT /GS- /GR- /EHs-c- source.cpp"
  }), React.createElement(LogLine, {
    ts: "14:22:03.418",
    lvl: "cl",
    msg: "source.cpp"
  }), React.createElement(LogLine, {
    ts: "14:22:05.221",
    lvl: "cl",
    msg: "Generating code\u2026"
  }), React.createElement(LogLine, {
    ts: "14:22:05.224",
    lvl: "info",
    msg: "compiled source.obj \xB7 12.4 KB"
  }), React.createElement(LogLine, {
    ts: "14:22:05.230",
    lvl: "info",
    msg: "spawn cl.exe link \u2192 20260519-4f7ba9d2.exe"
  }), React.createElement(LogLine, {
    ts: "14:22:05.231",
    lvl: "cl",
    msg: "Microsoft (R) Incremental Linker Version 14.39.33523.0",
    running: true
  }), React.createElement(LogLine, {
    ts: "",
    lvl: "",
    msg: "",
    cursor: true
  }))));
}
function LogLine({
  ts,
  lvl,
  msg,
  running,
  cursor
}) {
  if (cursor) return React.createElement("div", {
    style: {
      marginTop: 6
    }
  }, React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "\u258D"));
  const lvlColor = {
    info: "var(--acc)",
    dbg: "var(--n-7)",
    cl: "var(--n-8)",
    warn: "var(--warn)",
    err: "var(--err)"
  }[lvl] || "var(--n-7)";
  return React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "90px 36px 1fr",
      gap: 8
    }
  }, React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, ts), React.createElement("span", {
    style: {
      color: lvlColor,
      textTransform: "uppercase",
      fontSize: 10,
      letterSpacing: 0.06
    }
  }, lvl), React.createElement("span", {
    style: {
      color: running ? "var(--n-10)" : "var(--n-9)"
    }
  }, msg));
}
window.FrameCompile = FrameCompile;