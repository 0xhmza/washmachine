function FrameCompile() {
  const {
    useField,
    useFields
  } = window.washState;
  const [backend, setBackend] = useField('compilationBackend');
  const [llvmPasses, setLlvmPasses] = useField('llvmObfuscationPasses');
  const meta = useFields('catalog_compilers', 'catalog_llvmPasses', 'build_running', 'build_log', 'build_lastResult');
  const compilers = meta.catalog_compilers || [];
  const running = !!meta.build_running;
  const logLines = meta.build_log || [];
  const lastResult = meta.build_lastResult || null;
  const selComp = compilers[0];
  const passes = meta.catalog_llvmPasses || [];
  const selectedPasses = llvmPasses || [];
  function togglePass(id) {
    setLlvmPasses(selectedPasses.includes(id) ? selectedPasses.filter(x => x !== id) : [...selectedPasses, id]);
  }
  const status = [{
    icon: "info",
    k: "compiler",
    v: selComp ? selComp.name : 'none'
  }, {
    icon: "info",
    k: "stage",
    v: running ? 'building' : lastResult ? lastResult.ok ? 'done' : 'failed' : 'idle'
  }];
  return React.createElement(Shell, {
    active: "compile",
    crumbs: ["Compile"],
    pipeActive: "cmp",
    running: running,
    pipeStates: {
      src: "done",
      sgn: "done",
      enc: "done",
      tpl: "done",
      cmp: "active"
    },
    status: status
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
    title: "Toolchain",
    action: React.createElement("button", {
      className: "btn ghost",
      onClick: () => window.washState && window.washState.loadCatalogs()
    }, React.createElement(Icon, {
      name: "refresh",
      size: 12
    }), "Re-detect")
  }, React.createElement("div", {
    className: "card"
  }, compilers.length === 0 ? React.createElement("div", {
    style: {
      color: "var(--n-6)",
      fontSize: 12
    }
  }, "Detecting compilers\u2026") : React.createElement("div", {
    className: "row",
    style: {
      gap: 16,
      alignItems: "stretch"
    }
  }, compilers.map((c, index) => React.createElement(CompCard, {
    key: c.index,
    name: c.name,
    sub: c.version || '',
    path: c.path || '',
    selected: index === 0
  }))), React.createElement("div", {
    className: "div"
  }), React.createElement("div", {
    className: "row",
    style: {
      gap: 16
    }
  }, React.createElement(Field, {
    label: "Backend"
  }, React.createElement(Seg, {
    value: backend || 'Deterministic',
    onChange: setBackend,
    options: [{
      v: "Deterministic",
      l: "Deterministic"
    }, {
      v: "LlvmObfuscated",
      l: "LLVM Obfuscated"
    }]
  }))), backend === 'LlvmObfuscated' && React.createElement("div", {
    style: {
      marginTop: 16
    }
  }, React.createElement(Field, {
    label: "LLVM passes",
    hint: "Only registered passes exposed by the core registry are shown."
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 8,
      flexWrap: 'wrap',
      marginTop: 8
    }
  }, passes.map(p => React.createElement("button", {
    key: p.id,
    className: 'btn' + (selectedPasses.includes(p.id) ? ' primary' : ''),
    disabled: !p.available,
    title: p.available ? p.description : 'Pass runner metadata is unavailable',
    onClick: () => togglePass(p.id)
  }, p.name)), passes.length === 0 && React.createElement("span", {
    className: "sub"
  }, "No LLVM passes registered.")))))), React.createElement(Sec, {
    title: "Build",
    action: running ? React.createElement(Chip, {
      kind: "acc",
      dot: true
    }, "running") : lastResult ? React.createElement(Chip, {
      kind: lastResult.ok ? "ok" : "err",
      dot: true
    }, lastResult.ok ? "success" : "failed") : null
  }, running || logLines.length > 0 ? React.createElement("div", {
    className: "card"
  }, logLines.map((entry, i) => React.createElement(LogLine, {
    key: i,
    msg: typeof entry === 'string' ? entry : entry.line
  })), running && React.createElement("div", null, React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "\u258D"))) : React.createElement("div", {
    className: "card",
    style: {
      color: "var(--n-6)",
      fontSize: 12,
      fontFamily: "var(--f-mono)"
    }
  }, "No build started yet. Press Ctrl+B or click Build."))), React.createElement(BuildLogPreview, {
    logLines: logLines,
    running: running,
    lastResult: lastResult
  }));
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
  }, "auto-selected")), sub && React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)",
      marginTop: 4
    }
  }, sub), path && React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-6)",
      marginTop: 8,
      wordBreak: "break-all"
    }
  }, path));
}
function BuildLogPreview({
  logLines,
  running,
  lastResult
}) {
  const logRef = React.useRef(null);
  React.useEffect(() => {
    if (logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight;
  }, [logLines]);
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "term",
    size: 11
  }), "build_log.txt", running && React.createElement("span", {
    className: "pulse",
    style: {
      width: 6,
      height: 6,
      background: "var(--acc)",
      borderRadius: "50%",
      marginLeft: 4,
      display: "inline-block"
    }
  })), React.createElement("div", {
    style: {
      flex: 1
    }
  })), React.createElement("div", {
    className: "pbody",
    style: {
      background: "var(--n-0)"
    },
    ref: logRef
  }, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      lineHeight: 1.7,
      padding: "14px 16px",
      color: "var(--n-8)"
    }
  }, logLines.length > 0 ? logLines.map((entry, i) => React.createElement(LogLine, {
    key: i,
    msg: typeof entry === 'string' ? entry : entry.line
  })) : React.createElement("span", {
    style: {
      color: "var(--n-5)"
    }
  }, "Build output will stream here\u2026"), running && React.createElement("div", null, React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "\u258D")), lastResult && !running && React.createElement("div", {
    style: {
      marginTop: 10,
      color: lastResult.ok ? "var(--ok)" : "var(--err)"
    }
  }, lastResult.ok ? `✓ Build succeeded · ${lastResult.outputPath || ''}` : `✗ Build failed · ${lastResult.error || ''}`))));
}
function LogLine({
  msg
}) {
  const m = msg && msg.match(/^(\d{2}:\d{2}:\d{2}\.\d{3})\s+(\w+)\s+(.*)/s);
  if (m) {
    const lvlColor = {
      info: "var(--acc)",
      dbg: "var(--n-7)",
      warn: "var(--warn)",
      err: "var(--err)"
    }[m[2].toLowerCase()] || "var(--n-7)";
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
    }, m[1]), React.createElement("span", {
      style: {
        color: lvlColor,
        textTransform: "uppercase",
        fontSize: 10
      }
    }, m[2]), React.createElement("span", {
      style: {
        color: "var(--n-9)"
      }
    }, m[3]));
  }
  return React.createElement("div", {
    style: {
      color: "var(--n-9)"
    }
  }, msg);
}
window.FrameCompile = FrameCompile;