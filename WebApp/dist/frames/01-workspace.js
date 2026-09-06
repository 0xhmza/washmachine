function FrameWorkspace() {
  const {
    useField
  } = window.washState;
  const [sourceKind, setSourceKind] = useField('sourceKind');
  const [shellcodeFile, setShellcodeFile] = useField('shellcodeFileInput');
  const [shellcodeRaw, setShellcodeRaw] = useField('shellcodeRawInput');
  const [shellcodeUrl, setShellcodeUrl] = useField('shellcodeUrlValue');
  const [analysisNonce, setAnalysisNonce] = React.useState(0);
  const hasFile = !!(shellcodeFile || '').trim();
  const {
    useFields
  } = window.washState;
  const analysis = useFields('analysis_size', 'analysis_sizeText', 'analysis_arch', 'analysis_entropy', 'analysis_sha256', 'analysis_bytesPreview', 'analysis_running', 'analysis_error');
  React.useEffect(() => {
    let active = true;
    if (sourceKind !== 'file' || !hasFile) {
      window.washState.update({
        analysis_size: null,
        analysis_sizeText: '—',
        analysis_arch: '—',
        analysis_entropy: '—',
        analysis_sha256: '—',
        analysis_bytesPreview: '',
        analysis_running: false,
        analysis_error: ''
      });
      return () => {
        active = false;
      };
    }
    window.washState.update({
      analysis_running: true,
      analysis_error: ''
    });
    window.wash.invoke('analyze-shellcode', {
      path: shellcodeFile
    }).then(r => {
      if (!active) return;
      if (!r || !r.ok) {
        window.washState.update({
          analysis_running: false,
          analysis_error: r?.message || 'Analysis failed.'
        });
        return;
      }
      window.washState.update({
        analysis_size: r.size,
        analysis_sizeText: r.sizeText,
        analysis_arch: r.arch,
        analysis_entropy: String(r.entropy),
        analysis_sha256: r.sha256,
        analysis_bytesPreview: r.bytesPreview || '',
        analysis_running: false,
        analysis_error: ''
      });
    }).catch(e => {
      if (active) window.washState.update({
        analysis_running: false,
        analysis_error: e.message || 'Analysis failed.'
      });
    });
    return () => {
      active = false;
    };
  }, [shellcodeFile, sourceKind, analysisNonce]);
  const statusArch = analysis.analysis_arch || '—';
  const statusSize = analysis.analysis_sizeText || '—';
  const status = [{
    icon: "info",
    k: "src",
    v: sourceKind === 'file' ? hasFile ? (shellcodeFile.split('\\').pop() || shellcodeFile) + (statusSize !== '—' ? ' · ' + statusSize : '') : 'none' : sourceKind
  }, {
    icon: "info",
    k: "arch",
    v: statusArch
  }];
  const entropyVal = analysis.analysis_entropy ? `${analysis.analysis_entropy} / 8.0` : '—';
  const entropyHigh = parseFloat(analysis.analysis_entropy) > 7.0;
  const sha256Short = analysis.analysis_sha256 ? analysis.analysis_sha256.slice(0, 4) + '…' + analysis.analysis_sha256.slice(-4) : '—';
  function handleBrowse() {
    window.wash.invoke('browse-file', {
      filters: [{
        name: 'Shellcode binary',
        patterns: ['.bin', '.raw', '.dat']
      }]
    }).then(r => {
      if (r && r.ok && r.path) setShellcodeFile(r.path);
    }).catch(() => {});
  }
  function handleRecompute() {
    if (hasFile) setAnalysisNonce(n => n + 1);
  }
  const hexLines = React.useMemo(() => {
    const raw = analysis.analysis_bytesPreview || '';
    return raw.split('\n').filter(Boolean);
  }, [analysis.analysis_bytesPreview]);
  return React.createElement(Shell, {
    active: "payload",
    crumbs: ["Payload"],
    pipeActive: "src",
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
  }, "Shellcode source"), React.createElement("span", {
    className: "sub"
  }, "Pick one input. Everything downstream rebuilds when this changes.")), React.createElement(Sec, {
    title: "Input mode",
    action: React.createElement(Seg, {
      value: sourceKind,
      onChange: setSourceKind,
      options: [{
        v: "file",
        l: "File",
        icon: "file"
      }, {
        v: "raw",
        l: "Raw",
        icon: "term"
      }, {
        v: "url",
        l: "URL",
        icon: "globe"
      }]
    })
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      alignItems: "stretch",
      gap: 16
    }
  }, React.createElement("div", {
    style: {
      flex: "1.4 1 0",
      display: "flex",
      flexDirection: "column",
      gap: 10
    }
  }, sourceKind === 'file' && React.createElement(React.Fragment, null, React.createElement(Field, {
    label: "Shellcode .bin path"
  }, React.createElement("input", {
    className: "input mono",
    value: shellcodeFile,
    onChange: e => setShellcodeFile(e.target.value),
    placeholder: "C:\\payloads\\shellcode.bin"
  })), React.createElement("div", {
    className: "row",
    style: {
      gap: 8
    }
  }, React.createElement("button", {
    className: "btn",
    onClick: handleBrowse
  }, React.createElement(Icon, {
    name: "upload",
    size: 12
  }), "Browse\u2026"), React.createElement("button", {
    className: "btn ghost",
    disabled: !hasFile || analysis.analysis_running,
    onClick: handleRecompute
  }, React.createElement(Icon, {
    name: "refresh",
    size: 12
  }), "Recompute hash"), React.createElement("div", {
    style: {
      flex: 1
    }
  }), analysis.analysis_size != null && React.createElement(Chip, {
    kind: "ok",
    dot: true
  }, "analysed"))), sourceKind === 'raw' && React.createElement(React.Fragment, null, React.createElement(Field, {
    label: "Shellcode hex",
    hint: "space-separated or continuous hex bytes"
  }, React.createElement("textarea", {
    className: "input mono",
    rows: 4,
    value: shellcodeRaw,
    onChange: e => setShellcodeRaw(e.target.value),
    placeholder: "fc 48 83 e4 f0 e8 c0 00 ...",
    style: {
      resize: "vertical",
      fontFamily: "var(--f-mono)",
      fontSize: 11
    }
  }))), sourceKind === 'url' && React.createElement(React.Fragment, null, React.createElement(Field, {
    label: "Shellcode URL",
    hint: "http(s) \u2014 fetched at build time"
  }, React.createElement("input", {
    className: "input mono",
    value: shellcodeUrl,
    onChange: e => setShellcodeUrl(e.target.value),
    placeholder: "https://your-server.com/shell.bin"
  })))), React.createElement("div", {
    style: {
      flex: 1,
      borderLeft: "1px solid var(--n-4)",
      paddingLeft: 16,
      display: "flex",
      flexDirection: "column",
      gap: 8
    }
  }, React.createElement(H3, null, "Detected"), analysis.analysis_size != null ? React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)",
      lineHeight: 1.85
    }
  }, "size      ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, analysis.analysis_sizeText), "\n", "arch      ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, analysis.analysis_arch), "\n", "entropy   ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, entropyVal), entropyHigh && React.createElement("span", {
    style: {
      color: "var(--warn)"
    }
  }, " high"), "\n", "sha256    ", React.createElement("span", {
    style: {
      color: "var(--n-9)"
    }
  }, sha256Short)) : React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-6)",
      lineHeight: 1.85
    }
  }, sourceKind === 'file' ? analysis.analysis_error || (analysis.analysis_running ? 'analysing…' : hasFile ? 'waiting for analysis' : 'no file selected') : 'analysis available for file mode'))), hexLines.length > 0 && React.createElement(React.Fragment, null, React.createElement("div", {
    className: "div"
  }), React.createElement("div", null, React.createElement(H3, null, "First 64 bytes"), React.createElement("div", {
    className: "bytes",
    style: {
      marginTop: 8
    }
  }, hexLines.map((line, i) => {
    const m = line.match(/^([0-9a-f]+)\s+(.*)/);
    if (!m) return React.createElement("div", {
      key: i
    }, line);
    return React.createElement("div", {
      key: i
    }, React.createElement("span", {
      className: "o"
    }, m[1], "  "), React.createElement("span", null, m[2]));
  }))))))), React.createElement(PreviewPane, null));
}
function PreviewPane() {
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "info",
    size: 11
  }), "workflow")), React.createElement("div", {
    className: "pbody"
  }, React.createElement("div", {
    style: {
      padding: "10px 14px 4px",
      display: "flex",
      alignItems: "center",
      gap: 8
    }
  }, React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, "Configure each stage, then run Dry run or Build.")), React.createElement("div", {
    style: {
      padding: "20px 14px",
      color: "var(--n-6)",
      fontSize: 12,
      fontFamily: "var(--f-mono)"
    }
  }, "File analysis is calculated locally. Build output streams on the Compile and Pipeline pages.")));
}
window.FrameWorkspace = FrameWorkspace;
window.PreviewPane = PreviewPane;