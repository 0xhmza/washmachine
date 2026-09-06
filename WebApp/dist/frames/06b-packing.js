function FramePacking() {
  const {
    useField,
    useFields
  } = window.washState;
  const [enabled, setEnabled] = useField('EnablePackingToggle');
  const [upxPath, setUpxPath] = useField('UpxPathInput');
  const [compression, setCompression] = useField('UpxCompression');
  const [stripRelocs, setStripRelocs] = useField('UpxStripRelocs');
  const [keepBackup, setKeepBackup] = useField('UpxKeepBackup');
  const meta = useFields('upx_info');
  const isEnabled = enabled === 'True';
  function browseUpx() {
    window.wash.invoke('browse-file', {
      filters: [{
        name: 'UPX executable',
        patterns: ['.exe']
      }]
    }).then(r => {
      if (r && r.ok && r.path) setUpxPath(r.path);
    }).catch(() => {});
  }
  function detectUpx() {
    window.wash.invoke('detect-upx', {}).then(r => {
      window.washState.update({
        upx_info: r
      });
      if (r && r.ok && r.path) setUpxPath(r.path);
    }).catch(() => {});
  }
  const detected = !!upxPath || !!(meta.upx_info && meta.upx_info.ok);
  return React.createElement(Shell, {
    active: "packing",
    crumbs: ["Packing"],
    pipeActive: "pk",
    pipeStates: {
      src: "done",
      sgn: "done",
      enc: "done",
      tpl: "done",
      cmp: "done",
      bd: "done",
      pk: "active"
    },
    status: [{
      icon: 'info',
      k: 'packer',
      v: 'UPX'
    }, {
      icon: 'info',
      k: 'status',
      v: isEnabled ? detected ? 'ready' : 'missing' : 'disabled'
    }]
  }, React.createElement("div", {
    className: "cfg"
  }, React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'baseline',
      gap: 14,
      marginBottom: 22
    }
  }, React.createElement("h1", {
    className: "h1"
  }, "Packing"), React.createElement("span", {
    className: "sub"
  }, "Compress the final executable with the installed UPX tool."), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(Toggle, {
    on: isEnabled,
    onChange: v => setEnabled(v ? 'True' : 'False')
  }), React.createElement("span", {
    style: {
      fontSize: 12,
      color: isEnabled ? 'var(--n-9)' : 'var(--n-6)'
    }
  }, "Enable")), React.createElement("div", {
    style: {
      opacity: isEnabled ? 1 : 0.45,
      pointerEvents: isEnabled ? 'auto' : 'none'
    }
  }, React.createElement(Sec, {
    title: "UPX executable",
    action: React.createElement(Chip, {
      kind: detected ? 'ok' : 'warn',
      dot: true
    }, detected ? 'available' : 'not found')
  }, React.createElement("div", {
    className: "card"
  }, React.createElement(Field, {
    label: "upx.exe path",
    hint: "Detected from Tools, Program Files, LocalAppData, or PATH."
  }, React.createElement("input", {
    className: "input mono",
    value: upxPath || '',
    onChange: e => setUpxPath(e.target.value),
    placeholder: "C:\\\\Tools\\\\upx.exe"
  })), React.createElement("div", {
    className: "row",
    style: {
      gap: 8,
      marginTop: 10
    }
  }, React.createElement("button", {
    className: "btn",
    onClick: browseUpx
  }, React.createElement(Icon, {
    name: "upload",
    size: 12
  }), "Browse\u2026"), React.createElement("button", {
    className: "btn ghost",
    onClick: detectUpx
  }, React.createElement(Icon, {
    name: "refresh",
    size: 12
  }), "Re-detect")))), React.createElement(Sec, {
    title: "Compression"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement(Field, {
    label: "Level"
  }, React.createElement(Seg, {
    value: compression || 'best',
    onChange: setCompression,
    options: [{
      v: 'default',
      l: 'Default'
    }, {
      v: 'best',
      l: 'Best'
    }, {
      v: 'ultra',
      l: 'Ultra brute'
    }]
  })), React.createElement("div", {
    className: "row",
    style: {
      gap: 12,
      marginTop: 16,
      flexWrap: 'wrap'
    }
  }, React.createElement(PackOption, {
    label: "Strip relocations",
    on: stripRelocs === 'True',
    onChange: v => setStripRelocs(v ? 'True' : 'False')
  }), React.createElement(PackOption, {
    label: "Keep .bak backup",
    on: keepBackup === 'True',
    onChange: v => setKeepBackup(v ? 'True' : 'False')
  }), React.createElement(Chip, null, "overlay preserved")))))), React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "pkg",
    size: 11
  }), "UPX")), React.createElement("div", {
    className: "pbody ppad mono",
    style: {
      color: 'var(--n-7)',
      fontSize: 12
    }
  }, isEnabled ? detected ? `Will pack the final artifact with ${upxPath}.` : 'Select upx.exe before building.' : 'Packing is disabled.')));
}
function PackOption({
  label,
  on,
  onChange
}) {
  return React.createElement("div", {
    className: "row",
    style: {
      gap: 8,
      cursor: 'pointer'
    },
    onClick: () => onChange(!on)
  }, React.createElement(Toggle, {
    on: on,
    onChange: onChange
  }), React.createElement("span", {
    style: {
      fontSize: 12
    }
  }, label));
}
window.FramePacking = FramePacking;