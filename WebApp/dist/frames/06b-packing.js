function FramePacking() {
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
      icon: "info",
      k: "method",
      v: "custom RC4"
    }, {
      icon: "info",
      k: "stub",
      v: "in-memory"
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
  }, "Packing"), React.createElement("span", {
    className: "sub"
  }, "Compress and obfuscate the compiled binary before final delivery.")), React.createElement(Sec, {
    title: "Method"
  }, React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "repeat(2, 1fr)",
      gap: 10
    }
  }, React.createElement(PackTile, {
    name: "None",
    tagline: "Ship binary as-is",
    sub: "No transformation. Lowest risk of post-pack breakage; largest output.",
    capacity: "\u2014"
  }), React.createElement(PackTile, {
    name: "UPX",
    tagline: "Generic compressor",
    sub: "Industry-standard. Signature-flagged by most EDR \u2014 use only if entropy is masked downstream.",
    capacity: "\u2248 0.55\xD7 size",
    warn: true
  }), React.createElement(PackTile, {
    name: "Custom RC4",
    tagline: "Stub decrypts at runtime",
    sub: "Stage-0 stub maps decrypted payload into RWX and jumps. Higher CPU cost on launch.",
    capacity: "\u2248 0.92\xD7 size",
    selected: true
  }), React.createElement(PackTile, {
    name: "Reflective",
    tagline: "In-memory unpack",
    sub: "No payload on disk; entire decode happens in process memory. Largest stub.",
    capacity: "\u2248 1.05\xD7 size"
  }))), React.createElement(Sec, {
    title: "Stub options"
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 16,
      alignItems: "stretch"
    }
  }, React.createElement(Field, {
    label: "Key (hex)",
    hint: "Empty = autogen at build time"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "9f a2 4c d7 21 0b 5e 78",
    style: {
      width: 280
    }
  })), React.createElement(Field, {
    label: "Decoder placement"
  }, React.createElement(Seg, {
    value: "prepend",
    options: [{
      v: "prepend",
      l: "Prepend"
    }, {
      v: "tls",
      l: "TLS callback"
    }, {
      v: "section",
      l: "New section"
    }]
  })), React.createElement(Field, {
    label: "Self-erase decoder",
    hint: "Zero the decoder after first use"
  }, React.createElement(Toggle, {
    on: true
  }))))), React.createElement(Sec, {
    title: "Verify pass",
    action: React.createElement(Chip, {
      kind: "acc",
      dot: true
    }, "ready")
  }, React.createElement("div", {
    className: "card"
  }, React.createElement(VerifyRow, {
    label: "Round-trip integrity",
    detail: "Unpacked SHA-256 matches input",
    status: "ok"
  }), React.createElement(VerifyRow, {
    label: "Entropy floor",
    detail: "Target \u2264 7.0 after final pad \xB7 current 6.21",
    status: "ok"
  }), React.createElement(VerifyRow, {
    label: "Static signature scan",
    detail: "UPX / MPRESS / ASPack patterns",
    status: "ok"
  }), React.createElement(VerifyRow, {
    label: "Stub footprint",
    detail: "2.4 KB \xB7 within 16 KB limit",
    status: "ok",
    last: true
  })))), React.createElement(PackingPreview, null));
}
function PackTile({
  name,
  tagline,
  sub,
  capacity,
  selected,
  warn
}) {
  return React.createElement("div", {
    style: {
      padding: 16,
      borderRadius: 10,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-2)",
      cursor: "pointer",
      position: "relative"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between",
      marginBottom: 6
    }
  }, React.createElement("div", null, React.createElement("div", {
    className: "h2",
    style: {
      fontSize: 14
    }
  }, name), React.createElement("div", {
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, tagline)), selected && React.createElement(Chip, {
    kind: "acc",
    dot: true
  }, "selected"), warn && !selected && React.createElement(Chip, {
    kind: "warn"
  }, "flagged")), React.createElement("div", {
    style: {
      fontSize: 12,
      color: "var(--n-8)",
      lineHeight: 1.5,
      marginTop: 10
    }
  }, sub), React.createElement("div", {
    className: "div"
  }), React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between"
    }
  }, React.createElement("span", {
    className: "h3"
  }, "Size factor"), React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 12,
      color: selected ? "var(--acc)" : "var(--n-8)"
    }
  }, capacity)));
}
function VerifyRow({
  label,
  detail,
  status,
  last
}) {
  const dot = status === "ok" ? React.createElement("div", {
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
  })) : React.createElement("div", {
    style: {
      width: 10,
      height: 10,
      margin: 2,
      borderRadius: "50%",
      border: "1px dashed var(--n-5)"
    }
  });
  return React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "22px 1fr auto",
      gap: 12,
      padding: "10px 0",
      borderBottom: last ? "none" : "1px solid var(--n-3)",
      alignItems: "center"
    }
  }, dot, React.createElement("div", null, React.createElement("div", {
    style: {
      fontSize: 13,
      color: "var(--n-9)"
    }
  }, label), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, detail)), React.createElement(Chip, {
    kind: "ok"
  }, "pass"));
}
function PackingPreview() {
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "layers",
    size: 11
  }), "before / after"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "term",
    size: 11
  }), "stub source"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "invocation")), React.createElement("div", {
    className: "pbody ppad"
  }, React.createElement(H3, null, "Compression visualization"), React.createElement("div", {
    style: {
      marginTop: 12,
      display: "grid",
      gap: 14
    }
  }, React.createElement(Bar, {
    label: "Input",
    size: "2.21 MB",
    frac: 1.0,
    color: "var(--n-5)"
  }), React.createElement(Bar, {
    label: "After RC4 + stub",
    size: "2.04 MB",
    frac: 0.92,
    color: "var(--acc)"
  }), React.createElement(Bar, {
    label: "Net savings",
    size: "\u2212168 KB \xB7 \u22127.7%",
    frac: 0.077,
    color: "var(--ok)",
    hollow: true
  })), React.createElement("div", {
    className: "div"
  }), React.createElement(H3, null, "Layout"), React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "auto 1fr auto",
      gap: 10,
      padding: 12,
      marginTop: 10,
      background: "var(--n-2)",
      border: "1px solid var(--n-4)",
      borderRadius: 8,
      fontFamily: "var(--f-mono)",
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "0x00000000"), React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "\u250C\u2500 stub (decoder + jmp)  2.4 KB"), React.createElement("span", null), React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "0x00000970"), React.createElement("span", {
    style: {
      color: "var(--n-9)"
    }
  }, "\u251C\u2500 encrypted body  2.04 MB"), React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "RC4"), React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "0x00208000"), React.createElement("span", {
    style: {
      color: "var(--n-9)"
    }
  }, "\u251C\u2500 original headers  rebuilt at runtime"), React.createElement("span", null), React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "0x002080a0"), React.createElement("span", {
    style: {
      color: "var(--n-9)"
    }
  }, "\u2514\u2500 overlay (NOP pad)  0 B"), React.createElement("span", {
    style: {
      color: "var(--n-7)"
    }
  }, "\u2014")), React.createElement("div", {
    className: "div"
  }), React.createElement(H3, null, "Entropy delta"), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 12,
      gap: 20
    }
  }, React.createElement("div", null, React.createElement("div", {
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginBottom: 4
    }
  }, "Before"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 22,
      color: "var(--n-9)",
      fontWeight: 500
    }
  }, "6.21")), React.createElement(Icon, {
    name: "chev",
    size: 16
  }), React.createElement("div", null, React.createElement("div", {
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginBottom: 4
    }
  }, "After"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 22,
      color: "var(--warn)",
      fontWeight: 500
    }
  }, "7.84")), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(Chip, {
    kind: "warn"
  }, "approaching ceiling"))));
}
function Bar({
  label,
  size,
  frac,
  color,
  hollow
}) {
  return React.createElement("div", null, React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between",
      marginBottom: 5
    }
  }, React.createElement("span", {
    style: {
      fontSize: 12,
      color: "var(--n-9)"
    }
  }, label), React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, size)), React.createElement("div", {
    style: {
      height: 8,
      background: "var(--n-2)",
      borderRadius: 4,
      position: "relative",
      overflow: "hidden"
    }
  }, React.createElement("div", {
    style: {
      position: "absolute",
      top: 0,
      left: 0,
      bottom: 0,
      width: `${frac * 100}%`,
      background: hollow ? "transparent" : color,
      border: hollow ? `1px solid ${color}` : "none",
      borderRadius: 4,
      transition: "width .2s"
    }
  })));
}
window.FramePacking = FramePacking;