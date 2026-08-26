function FrameWorkspace() {
  const status = [{
    icon: "info",
    k: "src",
    v: "calc_x64.bin · 327 B"
  }, {
    icon: "info",
    k: "arch",
    v: "x86_64"
  }];
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
      value: "file",
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
      }, {
        v: "test",
        l: "Test",
        icon: "beaker"
      }]
    })
  }, "          ", React.createElement("div", {
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
  }, React.createElement(Field, {
    label: "Shellcode .bin path"
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "C:\\\\Users\\\\hmza\\\\payloads\\\\calc_x64.bin"
  })), React.createElement("div", {
    className: "row",
    style: {
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
    name: "refresh",
    size: 12
  }), "Recompute hash"), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(Chip, {
    kind: "ok",
    dot: true
  }, "valid PE-stripped"))), React.createElement("div", {
    style: {
      flex: 1,
      borderLeft: "1px solid var(--n-4)",
      paddingLeft: 16,
      display: "flex",
      flexDirection: "column",
      gap: 8
    }
  }, React.createElement(H3, null, "Detected"), React.createElement("div", {
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
  }, "327 B"), "\n", "arch      ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "x86_64"), "\n", "entropy   ", React.createElement("span", {
    style: {
      color: "var(--n-10)"
    }
  }, "7.42 / 8.0"), " ", React.createElement("span", {
    style: {
      color: "var(--warn)"
    }
  }, "high"), "\n", "sha256    ", React.createElement("span", {
    style: {
      color: "var(--n-9)"
    }
  }, "4f7b\u2026a9d2")))), React.createElement("div", {
    className: "div"
  }), React.createElement("div", null, React.createElement(H3, null, "First 64 bytes"), React.createElement("div", {
    className: "bytes",
    style: {
      marginTop: 8
    }
  }, React.createElement("div", null, React.createElement("span", {
    className: "o"
  }, "00000000  "), React.createElement("span", {
    className: "a"
  }, "fc 48 83 e4"), " f0 e8 c0 00 00 00 41 51 41 50 52 51"), React.createElement("div", null, React.createElement("span", {
    className: "o"
  }, "00000010  "), "56 48 31 d2 65 48 8b 52 60 48 8b 52 18 48 8b 52"), React.createElement("div", null, React.createElement("span", {
    className: "o"
  }, "00000020  "), "20 48 8b 72 50 48 0f b7 4a 4a 4d 31 c9 48 31 c0"), React.createElement("div", null, React.createElement("span", {
    className: "o"
  }, "00000030  "), "ac 3c 61 7c 02 2c 20 41 c1 c9 0d 41 01 c1 e2 ed")))))), React.createElement(PreviewPane, null));
}
function PreviewPane() {
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "source.cpp"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "layers",
    size: 11
  }), "pipeline"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "term",
    size: 11
  }), "console"), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "copy",
    size: 11
  })), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "dl",
    size: 11
  }))), React.createElement("div", {
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
  }, "temp/cpp/session_20260519_001a/source.cpp"), React.createElement(Chip, null, "4.1 KB"), React.createElement(Chip, null, "247 lines"), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement(Chip, {
    kind: "acc",
    dot: true
  }, "auto-rebuild")), React.createElement(CodeBlock, {
    startLine: 1,
    highlight: [10, 11, 12],
    lines: [[["pp", "#define"], ["", " "], ["kw", "WIN32_LEAN_AND_MEAN"]], [["pp", "#include"], ["", " "], ["str", "<windows.h>"]], [["pp", "#include"], ["", " "], ["str", "<tlhelp32.h>"]], [["", ""]], [["cm", "// ── snippet includes ─────────────────"]], [["pp", "#include"], ["", " "], ["str", "<intrin.h>"]], [["", ""]], [["cm", "// ── implementations (anti-debug, ps-inject) ─"]], [["kw", "static"], ["", " "], ["ty", "BOOL"], ["", " "], ["fn", "AntiDbg_CloseHandle"], ["", "()"]], [["", "{ "], ["kw", "__try"], ["", " { "], ["fn", "CloseHandle"], ["", "(("], ["ty", "HANDLE"], ["", ")"], ["num", "0xDEADBEEF"], ["", "); "], ["kw", "return"], ["", " "], ["num", "FALSE"], ["", "; }"]], [["", "  "], ["kw", "__except"], ["", "("], ["num", "EXCEPTION_INVALID_HANDLE"], ["", " == "], ["fn", "GetExceptionCode"], ["", "()"]], [["", "    ? "], ["num", "EXCEPTION_EXECUTE_HANDLER"], ["", " : "], ["num", "EXCEPTION_CONTINUE_SEARCH"], ["", ")"]], [["", "  { "], ["kw", "return"], ["", " "], ["num", "TRUE"], ["", "; } }"]], [["", ""]], [["ty", "INT"], ["", " "], ["fn", "main"], ["", "("], ["ty", "VOID"], ["", ")"]], [["", "{"]], [["", "  "], ["cm", "// {{SHELLCODE_SOURCE}}"]], [["", "  "], ["kw", "static"], ["", " "], ["ty", "unsigned char"], ["", " code_blob[] = {"]], [["", "    "], ["num", "0xfc"], ["", ", "], ["num", "0x48"], ["", ", "], ["num", "0x83"], ["", ", "], ["num", "0xe4"], ["", ", "], ["num", "0xf0"], ["", ", "], ["num", "0xe8"], ["", ", "], ["num", "0xc0"], ["", ", "], ["num", "0x00"], ["", ", "], ["num", "0x00"], ["", ", "], ["num", "0x00"], ["", ", "], ["num", "0x41"], ["", ", "], ["num", "0x51"], ["", ","]], [["", "    "], ["fold", "771 bytes folded · click to expand"]], [["", "  };  "], ["cm", "// sizeof = 789"]], [["", "  "], ["ty", "DWORD"], ["", " dwSize = "], ["kw", "sizeof"], ["", "(code_blob);"]], [["", ""]], [["", "  "], ["cm", "// {{GUARDRAILS}}"]], [["", "  "], ["kw", "if"], ["", " ("], ["fn", "GetEnvironmentVariableW"], ["", "("], ["str", "L\"USERDOMAIN\""], ["", ", ..."]], [["", "    .. != "], ["str", "L\"CORP\""], ["", ") "], ["kw", "return"], ["", " "], ["num", "0"], ["", ";"]], [["", ""]], [["", "  "], ["cm", "// {{ANTI_DEBUGGING}}"]], [["", "  "], ["kw", "if"], ["", " ("], ["fn", "AntiDbg_CloseHandle"], ["", "()) "], ["kw", "return"], ["", " "], ["num", "0"], ["", ";"]], [["", ""]], [["", "  "], ["cm", "// {{SHELLCODE_EXECUTION}}"]], [["", "  "], ["kw", "void"], ["", "* mem = "], ["fn", "VirtualAlloc"], ["", "("], ["num", "NULL"], ["", ", dwSize, ..."], ["", ");"]], [["", "  "], ["fn", "memcpy"], ["", "(mem, code_blob, dwSize);"]], [["", "  (("], ["kw", "void"], ["", "(*)())mem)();"]], [["", "  "], ["kw", "return"], ["", " "], ["num", "0"], ["", ";"]], [["", "}"]]]
  })));
}
window.FrameWorkspace = FrameWorkspace;
window.PreviewPane = PreviewPane;