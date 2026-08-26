function FrameBackdoor() {
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
    status: [{
      icon: "info",
      k: "target",
      v: "putty.exe · 1.2 MB"
    }, {
      icon: "info",
      k: "method",
      v: "code cave"
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
  }, "Backdoor PE"), React.createElement("span", {
    className: "sub"
  }, "Inject the compiled loader into an existing executable.")), React.createElement(Sec, {
    title: "Target",
    action: React.createElement(Chip, {
      kind: "ok",
      dot: true
    }, "verified PE32+")
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
    defaultValue: "C:\\\\Tools\\\\PuTTY\\\\putty.exe"
  })), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 10,
      gap: 8
    }
  }, React.createElement("button", {
    className: "btn"
  }, React.createElement(Icon, {
    name: "upload",
    size: 12
  }), "Browse\u2026"), React.createElement("button", {
    className: "btn ghost"
  }, React.createElement(Icon, {
    name: "info",
    size: 12
  }), "Re-analyze"))), React.createElement("div", {
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
  }, "1.21 MB"), "\n", "machine    ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "AMD64"), "\n", "signed     ", React.createElement("span", {
    style: {
      color: "var(--warn)"
    }
  }, "yes"), " \xB7 expires 2027", "\n", "sections   ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "6"), "\n", "code caves ", React.createElement("span", {
    style: {
      color: "var(--ok)"
    }
  }, "14"), " \xB7 max 1,184 B")))), React.createElement(Sec, {
    title: "Injection method"
  }, React.createElement("div", {
    className: "card",
    style: {
      padding: 0,
      overflow: "hidden"
    }
  }, React.createElement(MethodRow, {
    name: "Code cave",
    sub: "Reuse padding inside existing sections",
    trait: "zero growth",
    traitTone: "ok",
    capacity: "\u2264 1,184 B",
    selected: true
  }), React.createElement(MethodRow, {
    name: "New section",
    sub: "Append .wm section with payload",
    trait: "unlimited capacity",
    traitTone: "acc",
    capacity: "any"
  }), React.createElement(MethodRow, {
    name: "Section extension",
    sub: "Extend .text by aligned amount",
    trait: "grows file size",
    traitTone: "",
    capacity: "\u2264 64 KB"
  }), React.createElement(MethodRow, {
    name: "Text padding",
    sub: "Use .text alignment padding",
    trait: "zero growth \xB7 fragile",
    traitTone: "warn",
    capacity: "\u2264 312 B"
  }), React.createElement(MethodRow, {
    name: "TLS callback",
    sub: "Pre-main execution \xB7 x64 only",
    trait: "silent execution",
    traitTone: "acc",
    capacity: "N/A"
  }))), React.createElement(Sec, {
    title: "Hijack",
    action: React.createElement(Chip, null, "entry-point patch")
  }, React.createElement("div", {
    className: "card row",
    style: {
      gap: 16,
      alignItems: "stretch"
    }
  }, React.createElement(Field, {
    label: "Patch strategy"
  }, React.createElement(Seg, {
    value: "ep",
    options: [{
      v: "ep",
      l: "Entry point"
    }, {
      v: "import",
      l: "Import"
    }, {
      v: "tls",
      l: "TLS"
    }]
  })), React.createElement(Field, {
    label: "Resume after exec"
  }, React.createElement(Toggle, {
    on: true
  })), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(Field, {
    label: "Output path"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "putty.patched.exe",
    style: {
      width: 240
    }
  }))))), React.createElement(PeMapPreview, null));
}
function MethodRow({
  name,
  sub,
  trait,
  traitTone,
  capacity,
  selected
}) {
  const toneColor = {
    ok: "var(--ok)",
    warn: "var(--warn)",
    acc: "var(--acc)",
    "": "var(--n-7)"
  }[traitTone || ""];
  return React.createElement("div", {
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
function PeMapPreview() {
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
    className: "ptab"
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "headers"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "term",
    size: 11
  }), "imports"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "bug",
    size: 11
  }), "diff")), React.createElement("div", {
    className: "pbody ppad"
  }, React.createElement("div", {
    className: "row",
    style: {
      marginBottom: 14,
      gap: 10
    }
  }, React.createElement(Chip, {
    kind: "acc"
  }, "putty.exe"), React.createElement(Chip, null, "6 sections"), React.createElement(Chip, {
    kind: "ok",
    dot: true
  }, "14 code caves")), React.createElement("div", {
    style: {
      marginBottom: 14
    }
  }, React.createElement("div", {
    style: {
      display: "flex",
      height: 32,
      borderRadius: 6,
      overflow: "hidden",
      border: "1px solid var(--n-4)"
    }
  }, React.createElement(SecBar, {
    w: 32,
    name: ".text",
    color: "var(--n-5)"
  }), React.createElement(SecBar, {
    w: 6,
    name: ".rdata",
    color: "var(--n-6)"
  }), React.createElement(SecBar, {
    w: 3,
    name: ".data",
    color: "var(--n-5)"
  }), React.createElement(SecBar, {
    w: 1,
    name: ".pdata",
    color: "var(--n-6)"
  }), React.createElement(SecBar, {
    w: 3,
    name: ".rsrc",
    color: "var(--n-5)"
  }), React.createElement(SecBar, {
    w: 1,
    name: ".reloc",
    color: "var(--n-6)"
  })), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 8,
      justifyContent: "space-between"
    }
  }, React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-7)"
    }
  }, "0x00401000"), React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-7)"
    }
  }, "0x0053c000"))), React.createElement(H3, null, "Candidate code caves"), React.createElement("div", {
    className: "pemap",
    style: {
      marginTop: 8
    }
  }, React.createElement(CaveRow, {
    rva: "0x004f3a18",
    sec: ".text",
    sz: 1184,
    fillPct: 92,
    target: true
  }), React.createElement(CaveRow, {
    rva: "0x004e8c20",
    sec: ".text",
    sz: 812,
    fillPct: 64
  }), React.createElement(CaveRow, {
    rva: "0x004a9b00",
    sec: ".text",
    sz: 640,
    fillPct: 50
  }), React.createElement(CaveRow, {
    rva: "0x00521c40",
    sec: ".rdata",
    sz: 512,
    fillPct: 40
  }), React.createElement(CaveRow, {
    rva: "0x004b1280",
    sec: ".text",
    sz: 384,
    fillPct: 30
  }), React.createElement(CaveRow, {
    rva: "0x004f9100",
    sec: ".text",
    sz: 296,
    fillPct: 23
  })), React.createElement("div", {
    className: "div"
  }), React.createElement(H3, null, "Patch preview"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      lineHeight: 1.8,
      color: "var(--n-8)",
      marginTop: 8
    }
  }, React.createElement("div", null, React.createElement("span", {
    className: "o"
  }, "0x004f3a18"), "  ", React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, "cc cc cc cc cc cc cc cc"), "  ", React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, "// before")), React.createElement("div", null, React.createElement("span", {
    className: "o"
  }, "0x004f3a18"), "  ", React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "e8 a3 12 00 00 90 90 90"), "  ", React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, "// after (call \u2192 cave)")), React.createElement("div", {
    style: {
      marginTop: 6
    }
  }, React.createElement("span", {
    className: "o"
  }, "EP"), "          ", React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, "48 83 ec 28 e8 d7 04 00"), "  ", React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, "// before")), React.createElement("div", null, React.createElement("span", {
    className: "o"
  }, "EP"), "          ", React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "e9 13 3a 4f 00"), React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, " 90 90 90"), "  ", React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, "// after (jmp \u2192 loader)")))));
}
function SecBar({
  w,
  name,
  color
}) {
  return React.createElement("div", {
    style: {
      flex: w,
      background: color,
      position: "relative",
      borderRight: "1px solid var(--n-0)"
    }
  }, React.createElement("span", {
    style: {
      position: "absolute",
      left: 6,
      top: "50%",
      transform: "translateY(-50%)",
      fontFamily: "var(--f-mono)",
      fontSize: 10,
      color: "var(--n-0)",
      fontWeight: 600
    }
  }, name));
}
function CaveRow({
  rva,
  sec,
  sz,
  fillPct,
  target
}) {
  return React.createElement("div", {
    className: "pe-row" + (target ? " tgt" : ""),
    style: {
      "--fill": fillPct + "%"
    }
  }, React.createElement("div", {
    className: "nm"
  }, rva, " ", React.createElement("span", {
    style: {
      color: "var(--n-7)",
      fontSize: 10
    }
  }, sec)), React.createElement("div", {
    className: "vis"
  }), React.createElement("div", {
    className: "sz"
  }, sz, " B"));
}
window.FrameBackdoor = FrameBackdoor;