function I({
  d,
  size = 14,
  sw = 1.5,
  fill = "none"
}) {
  return React.createElement("svg", {
    width: size,
    height: size,
    viewBox: "0 0 24 24",
    fill: fill,
    stroke: "currentColor",
    strokeWidth: sw,
    strokeLinecap: "round",
    strokeLinejoin: "round"
  }, React.createElement("path", {
    d: d
  }));
}
const ICONS = {
  bolt: "M13 3L4 14h7l-1 7 9-11h-7l1-7z",
  inject: "M14 6l-4 4 4 4M3 12h10M21 6v12",
  pkg: "M3 7l9-4 9 4-9 4-9-4zM3 7v10l9 4 9-4V7M12 11v10",
  finish: "M5 12l5 5L20 7",
  cog: "M12 8a4 4 0 100 8 4 4 0 000-8zM19.4 15a1.7 1.7 0 00.3 1.8l.1.1a2 2 0 11-2.8 2.8l-.1-.1a1.7 1.7 0 00-1.8-.3 1.7 1.7 0 00-1 1.5V21a2 2 0 11-4 0v-.1a1.7 1.7 0 00-1-1.5 1.7 1.7 0 00-1.8.3l-.1.1A2 2 0 114.4 17l.1-.1a1.7 1.7 0 00.3-1.8 1.7 1.7 0 00-1.5-1H3a2 2 0 110-4h.1a1.7 1.7 0 001.5-1 1.7 1.7 0 00-.3-1.8l-.1-.1A2 2 0 117 4.4l.1.1a1.7 1.7 0 001.8.3H9a1.7 1.7 0 001-1.5V3a2 2 0 114 0v.1a1.7 1.7 0 001 1.5 1.7 1.7 0 001.8-.3l.1-.1a2 2 0 112.8 2.8l-.1.1a1.7 1.7 0 00-.3 1.8V9a1.7 1.7 0 001.5 1H21a2 2 0 110 4h-.1a1.7 1.7 0 00-1.5 1z",
  history: "M3 12a9 9 0 109-9M3 12V5M3 12h7M12 7v5l4 2",
  doc: "M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8zM14 2v6h6M9 13h6M9 17h6M9 9h1",
  play: "M5 3l14 9-14 9V3z",
  pause: "M6 4h4v16H6zM14 4h4v16h-4z",
  stop: "M5 5h14v14H5z",
  more: "M5 12h.01M12 12h.01M19 12h.01",
  search: "M11 19a8 8 0 100-16 8 8 0 000 16zM21 21l-4.3-4.3",
  cmd: "M15 6h3a3 3 0 010 6h-3V6zm0 0V3a3 3 0 10-3 3h3zM9 18H6a3 3 0 010-6h3v6zm0 0v3a3 3 0 103-3H9zM9 6v12h6V6H9z",
  copy: "M9 9h10v10H9zM5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1",
  dl: "M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3",
  up: "M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M17 8l-5-5-5 5M12 3v12",
  link: "M10 13a5 5 0 007 0l4-4a5 5 0 00-7-7l-1 1M14 11a5 5 0 00-7 0l-4 4a5 5 0 007 7l1-1",
  shield: "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z",
  bug: "M8 2l2 3M16 2l-2 3M12 22v-7M5 12H2M22 12h-3M6 17l-2 2M18 17l2 2M6 7l-2-2M18 7l2-2M7 12a5 5 0 0110 0v3a5 5 0 01-10 0v-3z",
  file: "M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8zM14 2v6h6",
  layers: "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5",
  chev: "M9 18l6-6-6-6",
  dot: "M12 12.01",
  check: "M5 12l5 5L20 7",
  x: "M18 6L6 18M6 6l12 12",
  alert: "M12 2L1 21h22L12 2zM12 9v5M12 18v.01",
  info: "M12 22a10 10 0 100-20 10 10 0 000 20zM12 16v-4M12 8v.01",
  plus: "M12 5v14M5 12h14",
  filter: "M3 4h18l-7 9v6l-4 2v-8L3 4z",
  refresh: "M3 12a9 9 0 0115-7l3 3M21 12a9 9 0 01-15 7l-3-3M21 5v4h-4M3 19v-4h4",
  fold: "M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z",
  term: "M4 17l6-6-6-6M12 19h8",
  globe: "M12 22a10 10 0 100-20 10 10 0 000 20zM2 12h20M12 2a15 15 0 010 20M12 2a15 15 0 000 20",
  flame: "M12 2c2 4 6 6 6 11a6 6 0 11-12 0c0-3 2-4 2-7 2 1 4 2 4-4z",
  upload: "M4 17v2a2 2 0 002 2h12a2 2 0 002-2v-2M7 9l5-5 5 5M12 4v12",
  beaker: "M9 3v8L4 21h16L15 11V3M9 3h6M7 17h10"
};
function Icon({
  name,
  size,
  sw
}) {
  const d = ICONS[name] || "";
  return React.createElement(I, {
    d: d,
    size: size,
    sw: sw
  });
}
const NAV_PRIMARY = [{
  id: "payload",
  name: "Payload",
  icon: "bolt"
}, {
  id: "scanner",
  name: "PE scanner",
  icon: "search"
}, {
  id: "backdooring",
  name: "Backdooring",
  icon: "inject"
}, {
  id: "packing",
  name: "Packing",
  icon: "pkg"
}, {
  id: "finalize",
  name: "Finalize",
  icon: "finish"
}, {
  id: "compile",
  name: "Compile",
  icon: "play"
}];
const NAV_FOOTER = [{
  id: "pipeline",
  name: "Pipeline",
  icon: "layers"
}, {
  id: "history",
  name: "History",
  icon: "history"
}, {
  id: "settings",
  name: "Settings",
  icon: "cog"
}];
function Rail({
  active = "payload"
}) {
  const info = window.washState ? window.washState.useFields('app_info').app_info : null;
  const connected = !!(info && info.version);
  return React.createElement("aside", {
    className: "rail"
  }, React.createElement("div", {
    className: "rail-brand"
  }, React.createElement("div", {
    className: "rail-logo"
  }, "W"), React.createElement("div", {
    className: "rail-brand-text"
  }, React.createElement("div", {
    className: "nm"
  }, "Washmachine"), React.createElement("div", {
    className: "sb"
  }, "Loader builder"))), React.createElement("div", {
    className: "rail-nav"
  }, NAV_PRIMARY.map(n => React.createElement("div", {
    key: n.id,
    className: "rail-item" + (n.id === active ? " active" : "")
  }, React.createElement("div", {
    className: "ri-ico"
  }, React.createElement(Icon, {
    name: n.icon,
    size: 15
  })), React.createElement("div", null, n.name)))), React.createElement("div", {
    className: "rail-sep"
  }), React.createElement("div", {
    className: "rail-nav",
    style: {
      paddingBottom: 8
    }
  }, NAV_FOOTER.map(n => React.createElement("div", {
    key: n.id,
    className: "rail-item" + (n.id === active ? " active" : "")
  }, React.createElement("div", {
    className: "ri-ico"
  }, React.createElement(Icon, {
    name: n.icon,
    size: 15
  })), React.createElement("div", null, n.name)))), React.createElement("div", {
    className: "rail-status"
  }, React.createElement("span", {
    className: "rail-dot",
    style: {
      opacity: connected ? 1 : 0.45
    },
    title: connected ? "native bridge connected" : "connecting to native bridge"
  }), React.createElement("span", null, connected ? "Local core connected" : "Connecting to local core…"), React.createElement("span", {
    style: {
      color: "var(--n-7)",
      fontFamily: "var(--f-mono)",
      fontSize: 10
    }
  }, info && info.version ? `v${info.version}` : '')));
}
const PIPELINE = [{
  id: "src",
  num: "01",
  name: "Source",
  meta: "shellcode"
}, {
  id: "sgn",
  num: "02",
  name: "SGN",
  meta: "optional",
  optional: true
}, {
  id: "enc",
  num: "03",
  name: "Encode",
  meta: "Bin2Shell"
}, {
  id: "tpl",
  num: "04",
  name: "Template",
  meta: "snippets"
}, {
  id: "cmp",
  num: "05",
  name: "Compile",
  meta: "C++ → exe"
}, {
  id: "bd",
  num: "06",
  name: "Backdoor",
  meta: "PE inject",
  optional: true
}, {
  id: "pk",
  num: "07",
  name: "Pack",
  meta: "optional",
  optional: true
}, {
  id: "fn",
  num: "08",
  name: "Finalize",
  meta: "clone / pad",
  optional: true
}];
function Pipeline({
  active = "src",
  states = {},
  runEnabled = true,
  running = false
}) {
  const recipeSource = window.washState.useFields('sourceKind', 'shellcodeFileInput', 'shellcodeRawInput', 'shellcodeUrlValue');
  const hasSource = recipeSource.sourceKind === 'raw' ? !!(recipeSource.shellcodeRawInput || '').trim() : recipeSource.sourceKind === 'url' ? !!(recipeSource.shellcodeUrlValue || '').trim() : !!(recipeSource.shellcodeFileInput || '').trim();
  const canBuild = runEnabled && hasSource;
  return React.createElement("div", {
    className: "pipe"
  }, PIPELINE.map((s, i) => {
    const state = states[s.id] || (active === s.id ? "active" : i < PIPELINE.findIndex(p => p.id === active) ? "done" : "");
    const cls = ["pipe-stage"];
    if (active === s.id) cls.push("active");
    if (state === "done") cls.push("done");
    if (state === "skipped") cls.push("skipped");
    return React.createElement(React.Fragment, {
      key: s.id
    }, React.createElement("div", {
      className: cls.join(" ")
    }, React.createElement("span", {
      className: "pipe-num"
    }, s.num, state === "done" && React.createElement(Icon, {
      name: "check",
      size: 10,
      sw: 2
    }), s.optional && state !== "done" && React.createElement("span", {
      style: {
        opacity: 0.5
      }
    }, "\xB7opt")), React.createElement("span", {
      className: "pipe-name"
    }, s.name), React.createElement("span", {
      className: "pipe-meta"
    }, s.meta)), i < PIPELINE.length - 1 && React.createElement("div", {
      className: "pipe-arrow" + (PIPELINE[i + 1].optional ? " opt" : "")
    }));
  }), React.createElement("div", {
    className: "pipe-spacer"
  }), React.createElement("div", {
    className: "pipe-run"
  }, running ? React.createElement("button", {
    className: "btn",
    style: {
      borderColor: "oklch(0.55 0.14 25 / 0.35)",
      color: "var(--err)"
    }
  }, React.createElement(Icon, {
    name: "stop",
    size: 12
  }), " Stop ", React.createElement("span", {
    className: "sk"
  }, "Esc")) : React.createElement(React.Fragment, null, React.createElement("button", {
    className: "btn"
  }, "Dry run"), React.createElement("button", {
    className: "btn primary",
    disabled: !canBuild,
    title: canBuild ? 'Build current recipe' : 'Select a payload source first'
  }, React.createElement(Icon, {
    name: "play",
    size: 12,
    sw: 2,
    fill: "currentColor"
  }), " Build ", React.createElement("span", {
    className: "sk"
  }, "Ctrl B")))));
}
function TitleBar({
  crumbs = []
}) {
  const result = window.washState ? window.washState.useFields('build_lastResult').build_lastResult : null;
  const session = result && result.sessionId ? result.sessionId : 'no active session';
  return React.createElement("div", {
    className: "tbar"
  }, React.createElement("div", {
    className: "tbar-crumbs"
  }, React.createElement("b", null, "Washmachine"), crumbs.map((c, i) => React.createElement(React.Fragment, {
    key: i
  }, React.createElement("span", {
    className: "sep"
  }, "\u203A"), React.createElement("span", {
    style: {
      color: i === crumbs.length - 1 ? "var(--n-10)" : "var(--n-8)"
    }
  }, c)))), React.createElement("div", {
    className: "tbar-spacer"
  }), React.createElement("div", {
    className: "tbar-quick"
  }, React.createElement(Icon, {
    name: "history",
    size: 13
  }), React.createElement("span", null, "Session"), React.createElement("b", {
    className: "mono",
    style: {
      color: "var(--n-10)"
    }
  }, session)));
}
function StatusBar({
  items = []
}) {
  const info = window.washState ? window.washState.useFields('app_info').app_info : null;
  return React.createElement("div", {
    className: "sbar"
  }, items.map((it, i) => React.createElement("div", {
    key: i,
    className: "grp mono"
  }, it.icon && React.createElement(Icon, {
    name: it.icon,
    size: 12
  }), React.createElement("span", null, it.k), React.createElement("b", null, it.v))), React.createElement("div", {
    className: "spacer"
  }), React.createElement("div", {
    className: "grp mono"
  }, React.createElement("span", null, "local"), React.createElement("b", null, info && info.version ? `v${info.version}` : 'connecting')));
}
function Shell({
  active = "payload",
  crumbs,
  pipeActive = "src",
  pipeStates = {},
  running = false,
  status = [],
  wide = false,
  readOnly = false,
  children,
  modal = null
}) {
  return React.createElement("div", {
    className: "app"
  }, React.createElement(Rail, {
    active: active
  }), React.createElement("div", {
    className: "main"
  }, React.createElement(TitleBar, {
    crumbs: crumbs
  }), readOnly ? React.createElement("div", {
    className: "row",
    style: {
      padding: '0 28px',
      color: 'var(--n-7)'
    }
  }, "Read-only analysis \xB7 selected files are never executed or modified") : React.createElement(Pipeline, {
    active: pipeActive,
    states: pipeStates,
    running: running
  }), React.createElement("div", {
    className: "work" + (wide ? " wide" : "")
  }, children), React.createElement(StatusBar, {
    items: status
  })), modal);
}
function NoticeHost() {
  const [notice, setNotice] = React.useState(null);
  React.useEffect(() => {
    let timer;
    const receive = ev => {
      const next = ev.detail || null;
      setNotice(next);
      clearTimeout(timer);
      timer = setTimeout(() => setNotice(null), next && next.kind === 'err' ? 9000 : 4500);
    };
    window.addEventListener('wash:notice', receive);
    return () => {
      clearTimeout(timer);
      window.removeEventListener('wash:notice', receive);
    };
  }, []);
  if (!notice) return null;
  return React.createElement("div", {
    className: 'notice ' + (notice.kind || 'err'),
    role: "status"
  }, React.createElement(Icon, {
    name: notice.kind === 'ok' ? 'check' : 'alert',
    size: 14
  }), React.createElement("span", null, notice.message), React.createElement("button", {
    type: "button",
    "aria-label": "Dismiss notification",
    onClick: () => setNotice(null)
  }, React.createElement(Icon, {
    name: "x",
    size: 12
  })));
}
function H3({
  children
}) {
  return React.createElement("div", {
    className: "h3"
  }, children);
}
function Sec({
  title,
  action,
  children
}) {
  return React.createElement("div", {
    className: "sec"
  }, React.createElement("div", {
    className: "sec-hd"
  }, React.createElement(H3, null, title), React.createElement("div", {
    style: {
      flex: 1
    }
  }), action), children);
}
function Field({
  label,
  hint,
  children
}) {
  return React.createElement("div", {
    className: "field"
  }, label && React.createElement("label", null, label), children, hint && React.createElement("span", {
    className: "hint"
  }, hint));
}
function Chip({
  kind = "",
  dot = false,
  children
}) {
  return React.createElement("span", {
    className: "chip " + (kind ? kind : "") + (dot ? " dot" : "")
  }, children);
}
function Toggle({
  on,
  onChange,
  disabled = false
}) {
  return React.createElement("span", {
    className: "tog" + (on ? " on" : ""),
    "data-wired": "true",
    role: "switch",
    "aria-checked": !!on,
    "aria-disabled": disabled,
    tabIndex: disabled ? -1 : 0,
    onClick: e => {
      e.stopPropagation();
      if (!disabled && onChange) onChange(!on);
    },
    onKeyDown: e => {
      if (!disabled && onChange && (e.key === 'Enter' || e.key === ' ')) {
        e.preventDefault();
        onChange(!on);
      }
    }
  });
}
function Seg({
  value,
  options,
  onChange
}) {
  return React.createElement("div", {
    className: "seg",
    "data-wired": "true"
  }, options.map(o => React.createElement("button", {
    key: o.v,
    className: o.v === value ? "on" : "",
    onClick: e => {
      e.stopPropagation();
      if (onChange) onChange(o.v);
    }
  }, o.icon && React.createElement(Icon, {
    name: o.icon,
    size: 11
  }), o.l)));
}
function CodeBlock({
  lines,
  startLine = 1,
  highlight = []
}) {
  return React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "40px 1fr",
      padding: "12px 0"
    }
  }, React.createElement("div", {
    className: "gutter"
  }, lines.map((_, i) => React.createElement("div", {
    key: i
  }, startLine + i))), React.createElement("pre", {
    className: "code",
    style: {
      margin: 0,
      paddingRight: 14
    }
  }, lines.map((toks, i) => React.createElement("div", {
    key: i,
    style: highlight.includes(i) ? {
      background: "oklch(0.4 0.08 235 / 0.12)"
    } : null
  }, toks.map(([c, t], j) => React.createElement("span", {
    key: j,
    className: c
  }, t)), "\n"))));
}
Object.assign(window, {
  Icon,
  Shell,
  Rail,
  Pipeline,
  TitleBar,
  StatusBar,
  Sec,
  Field,
  Chip,
  Toggle,
  Seg,
  CodeBlock,
  H3,
  NoticeHost,
  PIPELINE
});