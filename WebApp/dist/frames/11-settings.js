function FrameSettings() {
  const [info, setInfo] = React.useState(null);
  React.useEffect(() => {
    if (window.wash && window.wash.invoke) {
      window.wash.invoke('app-info', {}).then(setInfo).catch(() => setInfo({
        version: '—',
        cliAvailable: false
      }));
    }
  }, []);
  const i = info || {};
  return React.createElement(Shell, {
    active: "settings",
    crumbs: ["settings"],
    pipeActive: "",
    wide: true,
    pipeStates: Object.fromEntries(PIPELINE.map(p => [p.id, "skipped"])),
    status: [{
      icon: "info",
      k: "config",
      v: "~/.washmachine"
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
      marginBottom: 22
    }
  }, React.createElement("h1", {
    className: "h1"
  }, "Settings"), React.createElement("span", {
    className: "sub"
  }, "App-level preferences. Per-build options live on the build pages.")), React.createElement(Sec, {
    title: "Appearance"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between",
      marginBottom: 12
    }
  }, React.createElement("div", null, React.createElement("div", {
    className: "h2"
  }, "Theme"), React.createElement("div", {
    className: "sub"
  }, "Washmachine ships with a single dark theme tuned for long sessions.")), React.createElement(Chip, {
    kind: "acc",
    dot: true
  }, "dark \xB7 locked")), React.createElement("div", {
    className: "div"
  }), React.createElement("div", {
    className: "row",
    style: {
      gap: 16
    }
  }, React.createElement(Field, {
    label: "Accent"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "#60CDFF",
    style: {
      width: 140
    }
  })), React.createElement(Field, {
    label: "UI scale",
    hint: "restart required"
  }, React.createElement(Seg, {
    value: "100",
    options: [{
      v: "90",
      l: "90%"
    }, {
      v: "100",
      l: "100%"
    }, {
      v: "110",
      l: "110%"
    }, {
      v: "125",
      l: "125%"
    }]
  }))))), React.createElement(Sec, {
    title: "Paths"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement(Field, {
    label: "Output directory",
    hint: "where built artifacts and session manifests are written"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: i.executableDirectory || "(loading…)",
    style: {
      width: "100%"
    }
  })), React.createElement("div", {
    style: {
      height: 12
    }
  }), React.createElement(Field, {
    label: "Active playbook",
    hint: "YAML catalog used to compose snippets"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: i.playbookPath || "(loading…)",
    style: {
      width: "100%"
    }
  })), React.createElement("div", {
    style: {
      height: 12
    }
  }), React.createElement(Field, {
    label: "Assets directory"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: i.assetsDirectory || "(loading…)",
    style: {
      width: "100%"
    }
  })))), React.createElement(Sec, {
    title: "Build pipeline"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between",
      marginBottom: 12
    }
  }, React.createElement("div", null, React.createElement("div", {
    className: "h2"
  }, "CLI executable"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 4
    }
  }, i.cliPath || "(loading…)")), i.cliAvailable ? React.createElement(Chip, {
    kind: "ok",
    dot: true
  }, "available") : React.createElement(Chip, {
    kind: "warn",
    dot: true
  }, "not built")), React.createElement("div", {
    className: "div"
  }), React.createElement("div", {
    className: "row",
    style: {
      gap: 16
    }
  }, React.createElement(Field, {
    label: "Auto-save sessions"
  }, React.createElement(Toggle, {
    on: true
  })), React.createElement(Field, {
    label: "Verbose CLI output"
  }, React.createElement(Toggle, null)), React.createElement(Field, {
    label: "Auto-open artifact on success"
  }, React.createElement(Toggle, {
    on: true
  }))))), React.createElement(Sec, {
    title: "Keyboard shortcuts"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "col",
    style: {
      gap: 8
    }
  }, React.createElement(ShortcutRow, {
    keys: "Ctrl K",
    label: "Open command palette / search"
  }), React.createElement(ShortcutRow, {
    keys: "Ctrl B",
    label: "Trigger build on the current configuration"
  }), React.createElement(ShortcutRow, {
    keys: "Esc",
    label: "Stop running build / dismiss modal"
  })))), React.createElement(Sec, {
    title: "About"
  }, React.createElement("div", {
    className: "card row",
    style: {
      gap: 16
    }
  }, React.createElement("div", {
    style: {
      width: 44,
      height: 44,
      borderRadius: 10,
      background: "linear-gradient(155deg, var(--acc), var(--acc-dim))",
      display: "grid",
      placeItems: "center",
      color: "var(--n-0)",
      fontFamily: "var(--f-disp)",
      fontWeight: 700,
      fontSize: 20
    }
  }, "W"), React.createElement("div", {
    style: {
      flex: 1
    }
  }, React.createElement("div", {
    className: "h2"
  }, "Washmachine"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, "v", i.version || "—", " \xB7 loader builder"), React.createElement("div", {
    className: "sub",
    style: {
      marginTop: 8
    }
  }, "For authorized security research only. All builds are logged under ", React.createElement("span", {
    className: "mono",
    style: {
      color: "var(--n-9)"
    }
  }, "~/.washmachine/sessions"), ".")), React.createElement("button", {
    className: "btn"
  }, "Check for updates")))));
}
function ShortcutRow({
  keys,
  label
}) {
  const parts = keys.split(" ");
  return React.createElement("div", {
    className: "row",
    style: {
      padding: "6px 0",
      justifyContent: "space-between"
    }
  }, React.createElement("span", {
    style: {
      color: "var(--n-9)",
      fontSize: 13
    }
  }, label), React.createElement("span", {
    style: {
      display: "flex",
      gap: 4
    }
  }, parts.map((k, i) => React.createElement("span", {
    key: i,
    className: "kbd"
  }, k))));
}
window.FrameSettings = FrameSettings;