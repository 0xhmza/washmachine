function FrameFinalize() {
  const {
    useField
  } = window.washState;
  const [finalizeEnabled, setFinalizeEnabled] = useField('EnableFinalizeToggle');
  const [donorPath, setDonorPath] = useField('DonorPathInput');
  const [cloneIcon, setCloneIcon] = useField('CloneIcon');
  const [cloneVer, setCloneVer] = useField('CloneVersionInfo');
  const [cloneRsrc, setCloneRsrc] = useField('CloneRsrc');
  const [nopPad, setNopPad] = useField('NopPaddingInput');
  const finalizeOn = finalizeEnabled === 'True';
  const nopBytes = parseInt(nopPad) || 0;
  const nopLabel = nopBytes >= 1024 * 1024 ? `= ${(nopBytes / (1024 * 1024)).toFixed(1)} MiB` : nopBytes >= 1024 ? `= ${(nopBytes / 1024).toFixed(1)} KiB` : nopBytes > 0 ? `= ${nopBytes} B` : '';
  function browseDonor() {
    window.wash.invoke('browse-file', {
      filters: [{
        name: 'Executables',
        patterns: ['.exe', '.dll']
      }]
    }).then(r => {
      if (r && r.ok && r.path) setDonorPath(r.path);
    }).catch(() => {});
  }
  const donorName = donorPath ? donorPath.split('\\').pop() : 'none';
  const status = [{
    icon: "info",
    k: "donor",
    v: donorName
  }, {
    icon: "info",
    k: "padding",
    v: nopLabel || 'none'
  }];
  return React.createElement(Shell, {
    active: "finalize",
    crumbs: ["Finalize"],
    pipeActive: "fn",
    pipeStates: {
      src: "done",
      sgn: "done",
      enc: "done",
      tpl: "done",
      cmp: "done",
      bd: "done",
      pk: "done",
      fn: "active"
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
  }, "Finalize output"), React.createElement("span", {
    className: "sub"
  }, "Clone supported PE resources and append NOP overlay padding."), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(Toggle, {
    on: finalizeOn,
    onChange: v => setFinalizeEnabled(v ? 'True' : 'False')
  }), React.createElement("span", {
    style: {
      fontSize: 12,
      color: finalizeOn ? "var(--n-9)" : "var(--n-6)"
    }
  }, "Enable")), React.createElement("div", {
    style: {
      opacity: finalizeOn ? 1 : 0.45,
      pointerEvents: finalizeOn ? 'auto' : 'none'
    }
  }, React.createElement(Sec, {
    title: "Donor metadata"
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
    label: "Donor executable",
    hint: "Source for icon, resources, and version metadata."
  }, React.createElement("input", {
    className: "input mono",
    value: donorPath,
    onChange: e => setDonorPath(e.target.value),
    placeholder: "C:\\Windows\\System32\\OneDrive.exe"
  })), React.createElement("button", {
    className: "btn",
    style: {
      marginTop: 10
    },
    onClick: browseDonor
  }, React.createElement(Icon, {
    name: "upload",
    size: 12
  }), "Browse\u2026"), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 14,
      gap: 12,
      flexWrap: "wrap"
    }
  }, React.createElement(CloneToggle, {
    label: "Icon",
    on: cloneIcon === 'True',
    onChange: v => setCloneIcon(v ? 'True' : 'False')
  }), React.createElement(CloneToggle, {
    label: "Version info",
    on: cloneVer === 'True',
    onChange: v => setCloneVer(v ? 'True' : 'False')
  }), React.createElement(CloneToggle, {
    label: "Other resources",
    on: cloneRsrc === 'True',
    onChange: v => setCloneRsrc(v ? 'True' : 'False')
  }))), React.createElement("div", {
    style: {
      width: 220,
      borderLeft: "1px solid var(--n-4)",
      paddingLeft: 18
    }
  }, React.createElement(H3, null, "Donor"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: donorPath ? "var(--n-9)" : "var(--n-6)",
      marginTop: 10
    }
  }, donorPath ? donorPath.split('\\').pop() : 'No donor selected'))))), React.createElement(Sec, {
    title: "File shaping"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 16
    }
  }, React.createElement(Field, {
    label: "NOP overlay padding (bytes)",
    hint: "Append 0x90 bytes after the PE image."
  }, React.createElement("div", {
    className: "input-wrap"
  }, React.createElement("input", {
    className: "input mono",
    value: nopPad,
    onChange: e => setNopPad(e.target.value),
    style: {
      width: 220
    }
  }), nopLabel && React.createElement("span", {
    style: {
      position: "absolute",
      right: 10,
      top: "50%",
      transform: "translateY(-50%)",
      color: "var(--n-7)",
      fontSize: 11
    }
  }, nopLabel)))))))), React.createElement(FinalizePreview, {
    donorName: donorName
  }));
}
function CloneToggle({
  label,
  on,
  onChange
}) {
  return React.createElement("div", {
    className: "row",
    style: {
      gap: 8,
      padding: "6px 10px",
      borderRadius: 6,
      background: on ? "var(--acc-bg)" : "var(--n-1)",
      border: "1px solid " + (on ? "var(--acc-line)" : "var(--n-4)"),
      cursor: "pointer"
    },
    onClick: () => onChange(!on)
  }, React.createElement(Toggle, {
    on: !!on,
    onChange: onChange
  }), React.createElement("span", {
    style: {
      fontSize: 12,
      color: on ? "var(--n-10)" : "var(--n-8)"
    }
  }, label));
}
function FinalizePreview({
  donorName
}) {
  const meta = window.washState.useFields('build_lastResult');
  const lastResult = meta.build_lastResult;
  function revealOutput() {
    if (lastResult && lastResult.outputPath) {
      window.wash.invoke('reveal-file', {
        path: lastResult.outputPath
      }).catch(() => {});
    }
  }
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "layers",
    size: 11
  }), "artifact"), React.createElement("div", {
    style: {
      flex: 1
    }
  })), React.createElement("div", {
    className: "pbody ppad"
  }, lastResult && lastResult.ok ? React.createElement("div", {
    style: {
      display: "grid",
      gap: 14
    }
  }, React.createElement("div", {
    className: "card flat",
    style: {
      padding: 14
    }
  }, React.createElement(H3, null, "Output"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      color: "var(--n-10)",
      marginTop: 8
    }
  }, lastResult.outputPath || '(path unavailable)'), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 12,
      gap: 8
    }
  }, React.createElement("button", {
    className: "btn primary",
    onClick: revealOutput
  }, React.createElement(Icon, {
    name: "dl",
    size: 12
  }), "Reveal in Explorer"), React.createElement("button", {
    className: "btn",
    onClick: () => navigator.clipboard && navigator.clipboard.writeText(lastResult.outputPath || '')
  }, React.createElement(Icon, {
    name: "copy",
    size: 12
  }), "Copy path")))) : React.createElement("div", {
    style: {
      color: "var(--n-6)",
      fontSize: 12,
      fontFamily: "var(--f-mono)"
    }
  }, "Output details appear here after a successful build.")));
}
window.FrameFinalize = FrameFinalize;