function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function FrameBackdoor() {
  const {
    useField,
    useFields
  } = window.washState;
  const [bdEnabled, setBdEnabled] = useField('EnableBackdooringToggle');
  const [targetPath, setTargetPath] = useField('TargetPePath');
  const [injMethod, setInjMethod] = useField('InjectionMethodCombo');
  const [carrierInvoke, setCarrierInvoke] = useField('CarrierInvokeCombo');
  const [patchIat, setPatchIat] = useField('PatchIatCheck');
  const [removeSignature, setRemoveSignature] = useField('RemoveSignatureCheck');
  const [patchSubsystem, setPatchSubsystem] = useField('PatchSubsystemCheck');
  const [patchExit, setPatchExit] = useField('PatchExitCheck');
  const [sectionName, setSectionName] = useField('SectionNameInput');
  const [caveMin, setCaveMin] = useField('CaveMinSizeBox');
  const [analysisNonce, setAnalysisNonce] = React.useState(0);
  const meta = useFields('pe_analysis', 'pe_analysis_running', 'pe_analysis_error', 'pe_caves', 'pe_imports');
  const peInfo = meta.pe_analysis || null;
  const caves = meta.pe_caves || [];
  const bdOn = bdEnabled === 'True';
  const hasTarget = !!(targetPath || '').trim();
  React.useEffect(() => {
    if (peInfo && !peInfo.isDll && carrierInvoke === 'dll-main') setCarrierInvoke('entry-point');
  }, [peInfo, carrierInvoke]);
  React.useEffect(() => {
    let active = true;
    window.washState.set('PreserveEntryCheck', 'True');
    if (!hasTarget) {
      window.washState.update({
        pe_analysis: null,
        pe_analysis_running: false,
        pe_analysis_error: '',
        pe_caves: [],
        pe_imports: []
      });
      return () => {
        active = false;
      };
    }
    const timer = setTimeout(() => {
      window.washState.update({
        pe_analysis: null,
        pe_analysis_running: true,
        pe_analysis_error: '',
        pe_caves: [],
        pe_imports: []
      });
      window.wash.invoke('analyze-pe', {
        path: targetPath
      }).then(r => {
        if (!active) return;
        if (!r || !r.ok) {
          window.washState.update({
            pe_analysis_running: false,
            pe_analysis_error: r?.message || 'PE analysis failed.'
          });
          return;
        }
        window.washState.update({
          pe_analysis: r,
          pe_analysis_running: false,
          pe_analysis_error: '',
          pe_caves: r.caves || [],
          pe_imports: r.imports || []
        });
      }).catch(e => {
        if (active) window.washState.update({
          pe_analysis_running: false,
          pe_analysis_error: e.message || 'PE analysis failed.'
        });
      });
    }, 300);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [targetPath, analysisNonce]);
  function browsePe() {
    window.wash.invoke('browse-file', {
      filters: [{
        name: 'PE files',
        patterns: ['.exe', '.dll']
      }]
    }).then(r => {
      if (r && r.ok && r.path) setTargetPath(r.path);else if (r && !r.cancelled && r.message) window.wash.notify(r.message, 'err');
    }).catch(() => {});
  }
  const status = [{
    icon: "info",
    k: "target",
    v: peInfo ? (peInfo.fileName || targetPath?.split('\\').pop() || 'none') + ' · ' + (peInfo.fileSizeText || '') : 'none'
  }, {
    icon: "info",
    k: "method",
    v: injMethod || 'none'
  }];
  return React.createElement(Shell, {
    active: "backdooring",
    crumbs: ["Backdooring"],
    pipeActive: "bd",
    pipeStates: {
      src: "done",
      sgn: "done",
      enc: "done",
      tpl: "done",
      cmp: "done",
      bd: "active"
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
  }, "Backdoor PE"), React.createElement("span", {
    className: "sub"
  }, "Inject the compiled loader into an existing executable."), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(Toggle, {
    on: bdOn,
    onChange: v => setBdEnabled(v ? 'True' : 'False')
  }), React.createElement("span", {
    style: {
      fontSize: 12,
      color: bdOn ? "var(--n-9)" : "var(--n-6)"
    }
  }, "Enable")), React.createElement("div", null, React.createElement(Sec, {
    title: "Target",
    action: peInfo && React.createElement(Chip, {
      kind: peInfo.hasAuthenticode ? "warn" : "ok",
      dot: true
    }, peInfo.is64Bit ? "PE64" : "PE32")
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 16,
      alignItems: "stretch"
    }
  }, React.createElement("div", {
    style: {
      flex: 1
    }
  }, React.createElement(Field, {
    label: "Donor executable"
  }, React.createElement("input", {
    className: "input mono",
    value: targetPath,
    onChange: e => setTargetPath(e.target.value),
    placeholder: "C:\\Tools\\target.exe"
  })), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 10,
      gap: 8
    }
  }, React.createElement("button", {
    className: "btn",
    onClick: browsePe
  }, React.createElement(Icon, {
    name: "upload",
    size: 12
  }), "Browse\u2026"), React.createElement("button", {
    className: "btn ghost",
    disabled: !hasTarget || meta.pe_analysis_running,
    onClick: () => hasTarget && setAnalysisNonce(n => n + 1)
  }, React.createElement(Icon, {
    name: "info",
    size: 12
  }), meta.pe_analysis_running ? 'Analysing…' : 'Re-analyze'))), peInfo ? React.createElement("div", {
    className: "mono",
    style: {
      borderLeft: "1px solid var(--n-4)",
      paddingLeft: 18,
      fontSize: 11,
      color: "var(--n-8)",
      lineHeight: 1.85,
      minWidth: 220
    }
  }, "size       ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, peInfo.fileSizeText), "\n", "machine    ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, peInfo.architecture), "\n", "signed     ", React.createElement("span", {
    style: {
      color: peInfo.hasAuthenticode ? "var(--warn)" : "var(--ok)"
    }
  }, peInfo.hasAuthenticode ? "yes" : "no"), "\n", "sections   ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, peInfo.sectionCount), "\n", "code caves ", React.createElement("span", {
    style: {
      color: "var(--ok)"
    }
  }, peInfo.codeCaveCount), peInfo.maxCaveSize ? ` · max ${peInfo.maxCaveSize} B` : '') : React.createElement("div", {
    style: {
      borderLeft: "1px solid var(--n-4)",
      paddingLeft: 18,
      color: "var(--n-6)",
      fontSize: 12,
      minWidth: 180
    }
  }, meta.pe_analysis_error || (meta.pe_analysis_running ? 'Analysing…' : hasTarget ? 'Waiting for analysis' : 'Select a target PE'))))), React.createElement(Sec, {
    title: "Injection method"
  }, React.createElement("div", {
    className: "card",
    style: {
      padding: 0,
      overflow: "hidden"
    }
  }, [{
    id: "code-cave",
    name: "Code cave",
    sub: "Reuse padding inside existing sections",
    trait: "zero growth",
    traitTone: "ok",
    capacity: caves.length > 0 ? `≤ ${Math.max(...caves.map(c => c.size))} B` : "—"
  }, {
    id: "new-section",
    name: "New section",
    sub: "Append a section with the payload",
    trait: "predictable capacity",
    traitTone: "acc",
    capacity: "any"
  }, {
    id: "section-ext",
    name: "Section extension",
    sub: "Extend an existing section",
    trait: "grows file size",
    traitTone: "",
    capacity: "≤ 64 KB"
  }, {
    id: "text-pad",
    name: "Text padding",
    sub: "Use padding at the end of .text",
    trait: "limited capacity",
    traitTone: "warn",
    capacity: "target dependent"
  }, {
    id: "tls-callback",
    name: "TLS callback",
    sub: "Execute through a TLS callback",
    trait: "target dependent",
    traitTone: "acc",
    capacity: "target dependent"
  }].map(m => React.createElement(MethodRow, _extends({
    key: m.id
  }, m, {
    selected: injMethod === m.id,
    onClick: () => setInjMethod(m.id)
  }))))), React.createElement(Sec, {
    title: "Invocation and patching"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 18,
      alignItems: 'flex-start',
      flexWrap: 'wrap'
    }
  }, React.createElement(Field, {
    label: "Carrier invocation"
  }, React.createElement(Seg, {
    value: carrierInvoke || 'entry-point',
    onChange: setCarrierInvoke,
    options: [{
      v: 'entry-point',
      l: 'Entry point'
    }, ...(peInfo?.isDll ? [{
      v: 'dll-main',
      l: 'DLL main'
    }] : [])]
  })), React.createElement(Field, {
    label: "Section name"
  }, React.createElement("input", {
    className: "input mono",
    value: sectionName || '.extra',
    onChange: e => setSectionName(e.target.value),
    style: {
      width: 130
    }
  })), React.createElement(Field, {
    label: "Minimum cave bytes"
  }, React.createElement("input", {
    className: "input mono",
    value: caveMin || '64',
    onChange: e => setCaveMin(e.target.value),
    style: {
      width: 130
    }
  }))), React.createElement("div", {
    className: "div"
  }), React.createElement("div", {
    className: "row",
    style: {
      gap: 12,
      flexWrap: 'wrap'
    }
  }, React.createElement(BackdoorOption, {
    label: "Patch missing imports",
    value: patchIat,
    setValue: setPatchIat
  }), React.createElement(BackdoorOption, {
    label: "Remove invalid signature",
    value: removeSignature,
    setValue: setRemoveSignature
  }), React.createElement(BackdoorOption, {
    label: "Use GUI subsystem",
    value: patchSubsystem,
    setValue: setPatchSubsystem
  }), React.createElement(BackdoorOption, {
    label: "Redirect process exit",
    value: patchExit,
    setValue: setPatchExit
  })))), caves.length > 0 && React.createElement(Sec, {
    title: "Code caves"
  }, React.createElement("div", {
    className: "card",
    style: {
      padding: 0
    }
  }, caves.slice(0, 6).map((c, i) => React.createElement(CaveRow, {
    key: i,
    rva: c.rvaHex || c.virtualAddress,
    sec: c.sectionName,
    sz: c.size,
    suitable: c.suitableForInjection
  })))))), React.createElement(PeMapPreview, {
    peInfo: peInfo,
    caves: caves
  }));
}
function BackdoorOption({
  label,
  value,
  setValue
}) {
  const on = value !== 'False';
  return React.createElement("div", {
    className: "row",
    style: {
      gap: 8,
      cursor: 'pointer'
    },
    onClick: () => setValue(on ? 'False' : 'True')
  }, React.createElement(Toggle, {
    on: on,
    onChange: v => setValue(v ? 'True' : 'False')
  }), React.createElement("span", {
    style: {
      fontSize: 12
    }
  }, label));
}
function MethodRow({
  name,
  sub,
  trait,
  traitTone,
  capacity,
  selected,
  onClick
}) {
  const toneColor = {
    ok: "var(--ok)",
    warn: "var(--warn)",
    acc: "var(--acc)",
    "": "var(--n-7)"
  }[traitTone || ""];
  return React.createElement("div", {
    onClick: onClick,
    style: {
      display: "grid",
      gridTemplateColumns: "20px 1fr auto 14px",
      gap: 18,
      padding: "14px 18px",
      alignItems: "center",
      borderBottom: "1px solid var(--n-3)",
      cursor: "pointer",
      ...(selected ? {
        background: "var(--acc-bg)",
        boxShadow: "inset 3px 0 0 var(--acc)"
      } : {})
    }
  }, React.createElement("div", {
    style: {
      width: 16,
      height: 16,
      borderRadius: "50%",
      border: "1px solid " + (selected ? "var(--acc)" : "var(--n-6)"),
      background: selected ? "var(--acc)" : "transparent",
      boxShadow: selected ? "inset 0 0 0 4px var(--n-2)" : "none"
    }
  }), React.createElement("div", {
    style: {
      minWidth: 0
    }
  }, React.createElement("div", {
    className: "h2",
    style: {
      fontSize: 14
    }
  }, name), React.createElement("div", {
    style: {
      fontSize: 12,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, sub)), React.createElement("div", {
    className: "row",
    style: {
      gap: 18,
      fontSize: 12,
      whiteSpace: "nowrap"
    }
  }, React.createElement("span", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 7,
      color: toneColor
    }
  }, React.createElement("span", {
    style: {
      width: 7,
      height: 7,
      borderRadius: "50%",
      background: toneColor
    }
  }), trait), React.createElement("span", {
    style: {
      width: 1,
      height: 14,
      background: "var(--n-4)"
    }
  }), React.createElement("span", {
    className: "mono",
    style: {
      color: "var(--n-8)",
      minWidth: 64,
      textAlign: "right"
    }
  }, capacity)), React.createElement(Icon, {
    name: "chev",
    size: 12
  }));
}
function CaveRow({
  rva,
  sec,
  sz,
  suitable
}) {
  return React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "1fr auto auto",
      padding: "10px 16px",
      borderBottom: "1px solid var(--n-3)",
      alignItems: "center"
    }
  }, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11
    }
  }, typeof rva === 'number' ? '0x' + rva.toString(16).padStart(8, '0') : rva, " ", React.createElement("span", {
    style: {
      color: "var(--n-7)",
      fontSize: 10
    }
  }, sec)), suitable && React.createElement(Chip, {
    kind: "ok",
    dot: true
  }, "suitable"), React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)",
      marginLeft: 12
    }
  }, sz, " B"));
}
function PeMapPreview({
  peInfo,
  caves
}) {
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "layers",
    size: 11
  }), "PE map"), React.createElement("div", {
    style: {
      flex: 1
    }
  })), React.createElement("div", {
    className: "pbody ppad"
  }, peInfo ? React.createElement(React.Fragment, null, React.createElement("div", {
    className: "row",
    style: {
      marginBottom: 14,
      gap: 10
    }
  }, React.createElement(Chip, {
    kind: "acc"
  }, peInfo.fileName || 'target.exe'), React.createElement(Chip, null, peInfo.sectionCount, " sections"), peInfo.codeCaveCount > 0 && React.createElement(Chip, {
    kind: "ok",
    dot: true
  }, peInfo.codeCaveCount, " code caves")), caves.length > 0 && React.createElement(React.Fragment, null, React.createElement(H3, null, "Candidate code caves"), React.createElement("div", {
    style: {
      marginTop: 8
    }
  }, caves.slice(0, 6).map((c, i) => React.createElement(CaveRow, {
    key: i,
    rva: c.rvaHex || c.virtualAddress,
    sec: c.sectionName,
    sz: c.size,
    suitable: c.suitableForInjection
  }))))) : React.createElement("div", {
    style: {
      color: "var(--n-6)",
      fontSize: 12,
      fontFamily: "var(--f-mono)"
    }
  }, "PE analysis appears here after selecting a target.")));
}
window.FrameBackdoor = FrameBackdoor;