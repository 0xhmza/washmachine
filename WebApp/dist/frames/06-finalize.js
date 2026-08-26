function FrameFinalize() {
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
    status: [{
      icon: "info",
      k: "donor",
      v: "OneDrive.exe"
    }, {
      icon: "info",
      k: "padding",
      v: "1.0 MiB NOP"
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
  }, "Finalize output"), React.createElement("span", {
    className: "sub"
  }, "Clone metadata from a benign donor and adjust the file shape.")), React.createElement(Sec, {
    title: "Donor metadata",
    action: React.createElement(Chip, {
      kind: "acc"
    }, "authenticode unsigned")
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
    hint: "Used as source for icon, resources, and version metadata."
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "C:\\Users\\hmza\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe"
  })), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 12,
      gap: 12,
      flexWrap: "wrap"
    }
  }, React.createElement(CloneToggle, {
    label: "Icon",
    on: true
  }), React.createElement(CloneToggle, {
    label: "Version info",
    on: true
  }), React.createElement(CloneToggle, {
    label: "Manifest",
    on: true
  }), React.createElement(CloneToggle, {
    label: ".rsrc tree",
    on: true
  }), React.createElement(CloneToggle, {
    label: "Authenticode signature"
  }), React.createElement(CloneToggle, {
    label: "Original file name"
  }))), React.createElement("div", {
    style: {
      width: 240,
      borderLeft: "1px solid var(--n-4)",
      paddingLeft: 18
    }
  }, React.createElement(H3, null, "Preview \xB7 version info"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)",
      marginTop: 10,
      lineHeight: 1.85
    }
  }, "CompanyName       ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "Microsoft Corporation"), "\n", "ProductName       ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "Microsoft OneDrive"), "\n", "FileVersion       ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "24.176.0901.0002"), "\n", "Copyright         \xA9 Microsoft", "\n", "OriginalFilename  OneDrive.exe"))))), React.createElement(Sec, {
    title: "File shaping"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 16
    }
  }, React.createElement(Field, {
    label: "NOP padding (bytes)",
    hint: "Append zero-effect bytes to alter hash & shape."
  }, React.createElement("div", {
    className: "input-wrap"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "1048576",
    style: {
      width: 220
    }
  }), React.createElement("span", {
    style: {
      position: "absolute",
      right: 10,
      top: "50%",
      transform: "translateY(-50%)",
      color: "var(--n-7)",
      fontSize: 11
    }
  }, "= 1.0 MiB"))), React.createElement(Field, {
    label: "Pattern"
  }, React.createElement(Seg, {
    value: "nop",
    options: [{
      v: "nop",
      l: "0x90"
    }, {
      v: "zero",
      l: "0x00"
    }, {
      v: "rand",
      l: "Random"
    }]
  })), React.createElement(Field, {
    label: "Append location"
  }, React.createElement(Seg, {
    value: "overlay",
    options: [{
      v: "overlay",
      l: "Overlay"
    }, {
      v: "section",
      l: "New section"
    }]
  }))), React.createElement("div", {
    className: "div"
  }), React.createElement("div", null, React.createElement(H3, null, "Result"), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 10,
      gap: 24
    }
  }, React.createElement(Stat, {
    k: "size",
    v: "2.21 MB",
    subk: "was",
    subv: "1.21 MB"
  }), React.createElement(Stat, {
    k: "sha256",
    v: "9c4f\u20262e10",
    subk: "was",
    subv: "4f7b\u2026a9d2"
  }), React.createElement(Stat, {
    k: "entropy",
    v: "6.21",
    subk: "was",
    subv: "7.42",
    sublabel: "dropped \u2014 good",
    ok: true
  }), React.createElement(Stat, {
    k: "age stamp",
    v: "2024-08-12",
    subk: "from",
    subv: "OneDrive.exe"
  }))))), React.createElement(Sec, {
    title: "Packing",
    action: React.createElement(Chip, null, "configured on Packing screen")
  }, React.createElement("div", {
    className: "card flat row",
    style: {
      gap: 12
    }
  }, React.createElement("div", {
    style: {
      width: 36,
      height: 36,
      borderRadius: 8,
      background: "var(--n-2)",
      border: "1px solid var(--n-4)",
      display: "grid",
      placeItems: "center"
    }
  }, React.createElement(Icon, {
    name: "pkg",
    size: 16
  })), React.createElement("div", {
    style: {
      flex: 1
    }
  }, React.createElement("div", {
    className: "h2"
  }, "Packing pass"), React.createElement("div", {
    className: "sub"
  }, "None \u2014 ship binary as-is.")), React.createElement("button", {
    className: "btn ghost"
  }, "Configure ", React.createElement(Icon, {
    name: "chev",
    size: 12
  }))))), React.createElement(FinalizePreview, null));
}
function CloneToggle({
  label,
  on
}) {
  return React.createElement("div", {
    className: "row",
    style: {
      gap: 8,
      padding: "6px 10px",
      borderRadius: 6,
      background: on ? "var(--acc-bg)" : "var(--n-1)",
      border: "1px solid " + (on ? "var(--acc-line)" : "var(--n-4)")
    }
  }, React.createElement(Toggle, {
    on: !!on
  }), React.createElement("span", {
    style: {
      fontSize: 12,
      color: on ? "var(--n-10)" : "var(--n-8)"
    }
  }, label));
}
function Stat({
  k,
  v,
  subk,
  subv,
  sublabel,
  ok
}) {
  return React.createElement("div", null, React.createElement("div", {
    className: "h3",
    style: {
      marginBottom: 4
    }
  }, k), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 18,
      color: "var(--n-10)",
      fontWeight: 500,
      letterSpacing: -0.01
    }
  }, v), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-7)",
      marginTop: 3
    }
  }, subk, " ", React.createElement("span", {
    style: {
      color: "var(--n-8)"
    }
  }, subv), " ", sublabel && React.createElement("span", {
    style: {
      color: ok ? "var(--ok)" : "var(--n-7)"
    }
  }, "\xB7 ", sublabel)));
}
function PackCard({
  name,
  sub,
  selected,
  warn
}) {
  return React.createElement("div", {
    style: {
      flex: 1,
      padding: 12,
      borderRadius: 8,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-1)"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between"
    }
  }, React.createElement("div", {
    className: "h2",
    style: {
      fontSize: 13
    }
  }, name), warn && React.createElement(Chip, {
    kind: "warn"
  }, "flagged")), React.createElement("div", {
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 4
    }
  }, sub));
}
function FinalizePreview() {
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
    className: "ptab"
  }, React.createElement(Icon, {
    name: "bug",
    size: 11
  }), "before/after"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "manifest")), React.createElement("div", {
    className: "pbody ppad"
  }, React.createElement("div", {
    style: {
      display: "grid",
      gap: 16
    }
  }, React.createElement("div", {
    className: "card flat",
    style: {
      padding: 14
    }
  }, React.createElement(H3, null, "Surface \xB7 file explorer"), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 12,
      gap: 12
    }
  }, React.createElement("div", {
    style: {
      width: 48,
      height: 48,
      borderRadius: 6,
      background: "var(--n-2)",
      border: "1px solid var(--n-4)",
      display: "grid",
      placeItems: "center",
      color: "var(--acc)",
      fontSize: 22,
      fontWeight: 700
    }
  }, "\u2601"), React.createElement("div", {
    style: {
      flex: 1
    }
  }, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      color: "var(--n-10)"
    }
  }, "20260519-4f7ba9d2.exe"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, "Microsoft OneDrive \xB7 2.21 MB")), React.createElement(Chip, {
    kind: "ok",
    dot: true
  }, "icon cloned"))), React.createElement("div", {
    className: "card flat",
    style: {
      padding: 14
    }
  }, React.createElement(H3, null, "Output manifest"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)",
      marginTop: 10,
      lineHeight: 1.85
    }
  }, React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "session"), "      ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "session_20260519_001a"), "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "source"), "       ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "calc_x64.bin"), "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "template"), "     full-loader", "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "encoder"), "      XOR + Base64 (key=9fa24cd7)", "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "snippets"), "     5 selected", "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "backdoor"), "     putty.exe \xB7 code-cave @ 0x004f3a18", "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "donor"), "        OneDrive.exe", "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "output"), "       ", React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "out/20260519-4f7ba9d2.exe"), "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "sha256"), "       9c4f8d2a1bce7e10\u2026", "\n", React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "size"), "         2.21 MB"), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 14,
      gap: 8
    }
  }, React.createElement("button", {
    className: "btn primary"
  }, React.createElement(Icon, {
    name: "dl",
    size: 12
  }), "Reveal in Explorer"), React.createElement("button", {
    className: "btn"
  }, React.createElement(Icon, {
    name: "copy",
    size: 12
  }), "Copy hash"), React.createElement("button", {
    className: "btn ghost"
  }, React.createElement(Icon, {
    name: "upload",
    size: 12
  }), "Save preset"))), React.createElement("div", {
    className: "card flat",
    style: {
      padding: 14
    }
  }, React.createElement(H3, null, "Recommended next"), React.createElement("div", {
    className: "col",
    style: {
      marginTop: 10,
      gap: 8
    }
  }, React.createElement(NextRow, {
    icon: "beaker",
    label: "Test in detonation lab",
    sub: "run against your VM matrix"
  }), React.createElement(NextRow, {
    icon: "history",
    label: "Save as preset",
    sub: "re-run this exact composition"
  }))))));
}
function NextRow({
  icon,
  label,
  sub
}) {
  return React.createElement("div", {
    className: "row",
    style: {
      padding: "8px 0",
      borderTop: "1px solid var(--n-3)",
      gap: 10
    }
  }, React.createElement("div", {
    style: {
      width: 28,
      height: 28,
      borderRadius: 6,
      background: "var(--n-2)",
      border: "1px solid var(--n-4)",
      display: "grid",
      placeItems: "center"
    }
  }, React.createElement(Icon, {
    name: icon,
    size: 13
  })), React.createElement("div", {
    style: {
      flex: 1
    }
  }, React.createElement("div", {
    style: {
      fontSize: 12,
      color: "var(--n-9)"
    }
  }, label), React.createElement("div", {
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, sub)), React.createElement(Icon, {
    name: "chev",
    size: 12
  }));
}
window.FrameFinalize = FrameFinalize;