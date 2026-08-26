function FrameEncode() {
  return React.createElement(Shell, {
    active: "payload",
    crumbs: ["Payload", "Encoding"],
    pipeActive: "enc",
    pipeStates: {
      src: "done",
      sgn: "done",
      enc: "active"
    },
    status: [{
      icon: "info",
      k: "encoder",
      v: "XOR · 4-byte"
    }, {
      icon: "info",
      k: "envelope",
      v: "Base64"
    }, {
      icon: "info",
      k: "sgn",
      v: "2 passes · pre"
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
  }, "Encoding"), React.createElement("span", {
    className: "sub"
  }, "Transform raw shellcode bytes before they're embedded.")), React.createElement(Sec, {
    title: "Shikata Ga Nai \xB7 preprocessor",
    action: React.createElement("div", {
      className: "row",
      style: {
        gap: 8
      }
    }, React.createElement(Chip, {
      kind: "acc",
      dot: true
    }, "provisioned"), React.createElement(Toggle, {
      on: true
    }))
  }, React.createElement("div", {
    className: "card"
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 16
    }
  }, React.createElement(Field, {
    label: "Encode count",
    hint: "Each pass adds a decoder stub."
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "2",
    style: {
      width: 100
    }
  })), React.createElement(Field, {
    label: "Decoder max bytes",
    hint: "Obfuscation budget per pass."
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "64",
    style: {
      width: 100
    }
  })), React.createElement(Field, {
    label: "Placement"
  }, React.createElement(Seg, {
    value: "pre",
    options: [{
      v: "pre",
      l: "Pre-Bin2Shell"
    }, {
      v: "post",
      l: "Post"
    }]
  })), React.createElement("div", {
    style: {
      flex: 1
    }
  })), React.createElement("div", {
    className: "div"
  }), React.createElement("div", {
    className: "row",
    style: {
      gap: 12
    }
  }, React.createElement("div", {
    style: {
      flex: 1
    }
  }, React.createElement(H3, null, "Effect"), React.createElement("div", {
    className: "row",
    style: {
      gap: 14,
      marginTop: 8
    }
  }, React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, "327 B ", React.createElement("span", {
    className: "o"
  }, "\u2192"), " ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "~512 B")), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, "entropy 7.42 ", React.createElement("span", {
    className: "o"
  }, "\u2192"), " ", React.createElement("span", {
    style: {
      color: "var(--ok)"
    }
  }, "7.96")))), React.createElement("button", {
    className: "btn ghost"
  }, React.createElement(Icon, {
    name: "info",
    size: 12
  }), "SGN reference")))), React.createElement(Sec, {
    title: "Bin2Shell",
    action: React.createElement("button", {
      className: "btn ghost"
    }, React.createElement(Icon, {
      name: "refresh",
      size: 12
    }), "Reload catalog")
  }, React.createElement("div", {
    className: "card",
    style: {
      padding: 0,
      overflow: "hidden"
    }
  }, React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "1fr 1fr"
    }
  }, React.createElement("div", {
    style: {
      padding: 18,
      borderRight: "1px solid var(--n-4)"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between",
      marginBottom: 10
    }
  }, React.createElement(H3, null, "Encoder"), React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-7)"
    }
  }, "algos.yaml")), React.createElement("div", {
    className: "list"
  }, React.createElement(EncRow, {
    name: "None",
    id: "0",
    desc: "Pass-through bytes"
  }), React.createElement(EncRow, {
    name: "XOR",
    id: "1",
    desc: "4-byte rolling key",
    selected: true
  }), React.createElement(EncRow, {
    name: "RC4",
    id: "2",
    desc: "Stream cipher \xB7 128-bit"
  }), React.createElement(EncRow, {
    name: "AES-128-CBC",
    id: "3",
    desc: "Block cipher \xB7 16B key + IV"
  }), React.createElement(EncRow, {
    name: "ChaCha20",
    id: "4",
    desc: "Stream \xB7 256-bit key"
  })), React.createElement("div", {
    className: "div"
  }), React.createElement(Field, {
    label: "Key",
    hint: "hex \xB7 empty = autogen"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "9f a2 4c d7"
  }))), React.createElement("div", {
    style: {
      padding: 18
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between",
      marginBottom: 10
    }
  }, React.createElement(H3, null, "Envelope"), React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-7)"
    }
  }, "algos.yaml")), React.createElement("div", {
    className: "list"
  }, React.createElement(EncRow, {
    name: "None",
    id: "0",
    desc: "Raw byte array"
  }), React.createElement(EncRow, {
    name: "Base64",
    id: "1",
    desc: "Standard alphabet",
    selected: true
  }), React.createElement(EncRow, {
    name: "Base32",
    id: "2",
    desc: "Padded \xB7 A-Z 2-7"
  }), React.createElement(EncRow, {
    name: "Base91",
    id: "3",
    desc: "Higher density"
  }), React.createElement(EncRow, {
    name: "Hex",
    id: "4",
    desc: "0x.. comma-separated"
  })))))), React.createElement(Sec, {
    title: "Byte distribution",
    action: React.createElement("div", {
      className: "row",
      style: {
        gap: 6
      }
    }, React.createElement(Chip, null, "raw"), React.createElement(Chip, {
      kind: "acc"
    }, "encoded"))
  }, React.createElement("div", {
    className: "card"
  }, React.createElement(Histogram, null), React.createElement("div", {
    className: "row",
    style: {
      gap: 14,
      marginTop: 10,
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, React.createElement("span", null, "0x00"), React.createElement("span", {
    style: {
      flex: 1
    }
  }), React.createElement("span", null, "0x40"), React.createElement("span", {
    style: {
      flex: 1
    }
  }), React.createElement("span", null, "0x80"), React.createElement("span", {
    style: {
      flex: 1
    }
  }), React.createElement("span", null, "0xC0"), React.createElement("span", {
    style: {
      flex: 1
    }
  }), React.createElement("span", null, "0xFF"))))), React.createElement(EncodedPreview, null));
}
function EncRow({
  name,
  id,
  desc,
  selected
}) {
  return React.createElement("div", {
    className: "list-item" + (selected ? " sel" : "")
  }, React.createElement("div", {
    style: {
      width: 14,
      height: 14,
      borderRadius: "50%",
      border: "1px solid " + (selected ? "var(--acc)" : "var(--n-5)"),
      background: selected ? "var(--acc)" : "transparent",
      boxShadow: selected ? "inset 0 0 0 3px var(--n-3)" : "none"
    }
  }), React.createElement("div", null, React.createElement("div", {
    className: "ttl"
  }, name), React.createElement("div", {
    className: "meta"
  }, "id ", id, " \xB7 ", desc)));
}
function Histogram() {
  const bars = Array.from({
    length: 48
  }, (_, i) => {
    const raw = 0.2 + Math.abs(Math.sin(i * 0.4)) * 0.6 + (i % 7 === 0 ? 0.2 : 0);
    const enc = 0.4 + i * 31 % 17 / 30;
    return {
      raw,
      enc
    };
  });
  return React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "flex-end",
      height: 90,
      gap: 3
    }
  }, bars.map((b, i) => React.createElement("div", {
    key: i,
    style: {
      flex: 1,
      height: "100%",
      position: "relative"
    }
  }, React.createElement("div", {
    style: {
      position: "absolute",
      bottom: 0,
      left: 0,
      right: 0,
      height: `${b.raw * 100}%`,
      background: "var(--n-5)",
      borderRadius: "2px 2px 0 0"
    }
  }), React.createElement("div", {
    style: {
      position: "absolute",
      bottom: 0,
      left: 0,
      right: 0,
      height: `${b.enc * 100}%`,
      background: "var(--acc)",
      opacity: 0.85,
      borderRadius: "2px 2px 0 0",
      mixBlendMode: "screen"
    }
  }))));
}
function EncodedPreview() {
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "term",
    size: 11
  }), "encoded.b64"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "invocation"), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "copy",
    size: 11
  }))), React.createElement("div", {
    className: "pbody ppad"
  }, React.createElement("div", {
    className: "row",
    style: {
      marginBottom: 10,
      gap: 10
    }
  }, React.createElement(Chip, {
    kind: "acc"
  }, "789 B"), React.createElement(Chip, null, "+241 % vs raw"), React.createElement(Chip, {
    kind: "ok",
    dot: true
  }, "printable")), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 12,
      lineHeight: 1.7,
      color: "var(--n-9)",
      wordBreak: "break-all"
    }
  }, React.createElement("span", {
    style: {
      color: "var(--n-6)"
    }
  }, "// XOR(key=9fa24cd7) \u2192 Base64"), React.createElement("br", null), "/EiD5PDowAAAAEFRQVBSUVZIMdJlSItSYEiLUhhIi1IgSItyUEgPt0pKTTHJSDHA", React.createElement("br", null), "rDxhfAIsIEHByQ1BAcHi7VJBUUiLUiBLizQKSDHASIvSrAxA8MtT8MtIAcdK4u9I", React.createElement("br", null), "M8BLizR2SDHASLqAAQAAQQEAAEH/0EH/0FNQUEhB/9CDxCBoBwAAAGgEAAAAaQEA", React.createElement("br", null), "AGgFAAAAaAYAAAA1bGFKAVNQUEH/0Ej//8hI/zP/Q/8z/0OD7FBT/3RkEDU="), React.createElement("div", {
    className: "div"
  }), React.createElement(H3, null, "Bin2Shell invocation"), React.createElement("div", {
    className: "mono",
    style: {
      marginTop: 8,
      fontSize: 11,
      color: "var(--n-8)",
      lineHeight: 1.7
    }
  }, React.createElement("span", {
    className: "o"
  }, "$"), " python ", React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "main.py"), "  \\", React.createElement("br", null), "    ", "-e ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "1"), "  ", "-x ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "1"), "  \\", React.createElement("br", null), "    ", "-i ./payload.bin", "  \\", React.createElement("br", null), "    ", "-o ./encoded.b64", "  \\", React.createElement("br", null), "    ", "--key 9fa24cd7")));
}
window.FrameEncode = FrameEncode;