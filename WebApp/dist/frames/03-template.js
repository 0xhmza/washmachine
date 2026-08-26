function FrameTemplate() {
  return React.createElement(Shell, {
    active: "payload",
    crumbs: ["Payload", "Template options"],
    pipeActive: "tpl",
    pipeStates: {
      src: "done",
      sgn: "done",
      enc: "done",
      tpl: "active"
    },
    status: [{
      icon: "info",
      k: "template",
      v: "full-loader"
    }, {
      icon: "info",
      k: "snippets",
      v: "5 selected"
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
  }, "Template & snippets"), React.createElement("span", {
    className: "sub"
  }, "Composed at render time from ", React.createElement("span", {
    className: "mono",
    style: {
      color: "var(--n-9)"
    }
  }, "vx_api_snippets.yaml"))), React.createElement(Sec, {
    title: "Playbook",
    action: React.createElement("button", {
      className: "btn ghost"
    }, React.createElement(Icon, {
      name: "dl",
      size: 12
    }), "Open in editor")
  }, React.createElement("div", {
    className: "card row",
    style: {
      justifyContent: "space-between"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      gap: 12
    }
  }, React.createElement("div", {
    style: {
      width: 36,
      height: 36,
      borderRadius: 8,
      background: "var(--n-3)",
      display: "grid",
      placeItems: "center"
    }
  }, React.createElement(Icon, {
    name: "doc",
    size: 16
  })), React.createElement("div", null, React.createElement("div", {
    className: "h2"
  }, "vx_api_snippets.yaml"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, "89 snippets \xB7 10 categories \xB7 6 templates"))), React.createElement("div", {
    className: "row",
    style: {
      gap: 8
    }
  }, React.createElement("select", {
    className: "select",
    style: {
      width: 220
    }
  }, React.createElement("option", null, "Assets/vx_api_snippets.yaml")), React.createElement("button", {
    className: "btn"
  }, React.createElement(Icon, {
    name: "refresh",
    size: 12
  }), "Reload")))), React.createElement(Sec, {
    title: "Template"
  }, React.createElement("div", {
    className: "card",
    style: {
      padding: 0
    }
  }, React.createElement(TplRow, {
    name: "full-loader",
    desc: "All feature placeholders wired",
    selected: true
  }), React.createElement(TplRow, {
    name: "minimal",
    desc: "Shellcode source + one execution snippet"
  }), React.createElement(TplRow, {
    name: "reflective",
    desc: "Reflective DLL injection scaffold"
  }), React.createElement(TplRow, {
    name: "staged-http",
    desc: "HTTPS stager with cert-pin envelope"
  }), React.createElement(TplRow, {
    name: "cobalt-compat",
    desc: "Cobalt-strike beacon-shaped"
  }), React.createElement(TplRow, {
    name: "tls-callback",
    desc: "Pre-main execution via TLS callback"
  }))), React.createElement(Sec, {
    title: "Snippets",
    action: React.createElement("div", {
      className: "row",
      style: {
        gap: 8
      }
    }, React.createElement(Chip, {
      kind: "acc",
      dot: true
    }, "5 active"), React.createElement("button", {
      className: "btn ghost"
    }, React.createElement(Icon, {
      name: "filter",
      size: 12
    }), "Filter"))
  }, React.createElement("div", {
    className: "card",
    style: {
      padding: 0
    }
  }, React.createElement(SnipGroup, {
    name: "Anti-debugging",
    template: "ANTIDEBUGGING",
    stack: true,
    items: [{
      name: "CloseHandle on invalid address",
      id: "CloseHandleAntiDebug",
      on: true,
      inputs: []
    }, {
      name: "IsDebuggerPresent",
      id: "IsDebuggerPresentCheck",
      on: true
    }, {
      name: "NtQueryInformationProcess · ProcessDebugPort",
      id: "NtQueryDebugPort",
      on: false
    }, {
      name: "RDTSC timing delta",
      id: "RdtscTiming",
      on: false
    }]
  }), React.createElement(SnipGroup, {
    name: "Guardrail",
    template: "GUARDRAIL",
    items: [{
      name: "Require environment variable",
      id: "EnvVarGuardrail",
      on: true,
      input: {
        label: "Condition",
        value: "USERDOMAIN#equals#CORP"
      }
    }, {
      name: "Require domain join",
      id: "DomainGuardrail",
      on: false
    }]
  }), React.createElement(SnipGroup, {
    name: "Process injection",
    template: "PSINJECTION",
    items: [{
      name: "WriteProcessMemory + CreateRemoteThread",
      id: "WPMCRT",
      on: true,
      input: {
        label: "Target process",
        value: "explorer.exe"
      }
    }, {
      name: "QueueUserAPC into ALERTABLE thread",
      id: "QueueAPC",
      on: false
    }, {
      name: "Map → SetThreadContext (Ghosting)",
      id: "Ghost",
      on: false
    }]
  }), React.createElement(SnipGroup, {
    name: "Shellcode execution",
    template: "SHELLCODEEXECUTION",
    items: [{
      name: "VirtualAlloc + function pointer",
      id: "VirtualAllocFuncPtr",
      on: true
    }, {
      name: "CreateThread",
      id: "CreateThreadExec",
      on: false
    }, {
      name: "EnumWindows callback",
      id: "EnumWindowsExec",
      on: false
    }]
  })))), React.createElement(SnippetDiffPreview, null));
}
function TplRow({
  name,
  desc,
  selected
}) {
  return React.createElement("div", {
    className: "list-item",
    style: {
      gridTemplateColumns: "20px 1fr auto auto",
      padding: "12px 16px",
      borderBottom: "1px solid var(--n-3)",
      ...(selected ? {
        background: "var(--acc-bg)",
        boxShadow: "inset 2px 0 0 var(--acc)"
      } : {})
    }
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
    className: "ttl mono",
    style: {
      fontSize: 13
    }
  }, name), React.createElement("div", {
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, desc)), React.createElement(Chip, null, name === "full-loader" ? "5 placeholders" : name === "minimal" ? "2 placeholders" : "4 placeholders"), React.createElement(Icon, {
    name: "chev",
    size: 12
  }));
}
function SnipGroup({
  name,
  template,
  stack,
  items
}) {
  const activeCount = items.filter(i => i.on).length;
  return React.createElement("div", {
    style: {
      borderBottom: "1px solid var(--n-3)"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      padding: "12px 16px",
      background: "var(--n-1)",
      gap: 10
    }
  }, React.createElement(Icon, {
    name: "chev",
    size: 11,
    sw: 2
  }), React.createElement("div", null, React.createElement("div", {
    className: "h2"
  }, name), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-7)"
    }
  }, "template: ", template, stack ? "  ·  allowMultiple" : "")), React.createElement("div", {
    style: {
      flex: 1
    }
  }), activeCount > 0 && React.createElement(Chip, {
    kind: "acc"
  }, activeCount, " active")), React.createElement("div", {
    style: {
      padding: "6px 8px"
    }
  }, items.map((it, i) => React.createElement("div", {
    key: i,
    className: "list-item" + (it.on ? " sel" : ""),
    style: {
      gridTemplateColumns: "16px 1fr auto"
    }
  }, React.createElement("div", {
    style: {
      width: 12,
      height: 12,
      borderRadius: 3,
      border: "1px solid " + (it.on ? "var(--acc)" : "var(--n-5)"),
      background: it.on ? "var(--acc)" : "transparent",
      display: "grid",
      placeItems: "center"
    }
  }, it.on && React.createElement(Icon, {
    name: "check",
    size: 9,
    sw: 3
  })), React.createElement("div", null, React.createElement("div", {
    className: "ttl"
  }, it.name), React.createElement("div", {
    className: "meta"
  }, "id ", React.createElement("span", {
    style: {
      color: "var(--n-8)"
    }
  }, it.id)), it.input && it.on && React.createElement("div", {
    className: "row",
    style: {
      marginTop: 6,
      gap: 8
    }
  }, React.createElement("span", {
    className: "hint mono",
    style: {
      fontSize: 10
    }
  }, it.input.label), React.createElement("input", {
    className: "input mono",
    style: {
      padding: "4px 8px",
      fontSize: 11,
      width: 260
    },
    defaultValue: it.input.value
  }))), React.createElement(Icon, {
    name: "more",
    size: 12
  })))));
}
function SnippetDiffPreview() {
  return React.createElement("div", {
    className: "preview"
  }, React.createElement("div", {
    className: "ptabs"
  }, React.createElement("div", {
    className: "ptab on"
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "render diff"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "layers",
    size: 11
  }), "placeholders"), React.createElement("div", {
    className: "ptab"
  }, React.createElement(Icon, {
    name: "doc",
    size: 11
  }), "yaml")), React.createElement("div", {
    className: "pbody"
  }, React.createElement("div", {
    className: "ppad",
    style: {
      paddingBottom: 0
    }
  }, React.createElement(H3, null, "What this composition expands into")), React.createElement(CodeBlock, {
    startLine: 42,
    highlight: [],
    lines: [[["cm", "// {{SNIPPET_INCLUDES}}"]], [["ins", "#include <intrin.h>"]], [["ins", "#include <tlhelp32.h>"]], [["", ""]], [["cm", "// {{SNIPPET_IMPLEMENTATIONS}}"]], [["ins", "static BOOL AntiDbg_CloseHandle() { /* ... */ }"]], [["ins", "static BOOL InjectWPMCRT(PBYTE, DWORD, DWORD) { /* ... */ }"]], [["ins", "static DWORD GetProcessOrThreadId(LPCWSTR, BOOL) { /* ... */ }"]], [["", ""]], [["cm", "// — in main() —"]], [["", ""]], [["cm", "// {{GUARDRAILS}}"]], [["ins", "if (GetEnvironmentVariableW(L\"USERDOMAIN\", buf, ...)"]], [["ins", "    || wcscmp(buf, L\"CORP\") != 0) return 0;"]], [["", ""]], [["cm", "// {{ANTI_DEBUGGING}}"]], [["ins", "if (AntiDbg_CloseHandle()) return 0;"]], [["ins", "if (IsDebuggerPresent()) return 0;"]], [["", ""]], [["cm", "// {{PROCESS_INJECTION}}"]], [["ins", "if (InjectWPMCRT((PBYTE)code_blob, dwSize,"]], [["ins", "    GetProcessOrThreadId(L\"explorer.exe\", true))) return 1;"]], [["", ""]], [["cm", "// {{SHELLCODE_EXECUTION}}"]], [["del", "// CreateThread((LPTHREAD_START_ROUTINE)mem, ...);"]], [["ins", "void* mem = VirtualAlloc(NULL, dwSize, MEM_COMMIT|MEM_RESERVE, PAGE_RW);"]], [["ins", "memcpy(mem, code_blob, dwSize); ((void(*)())mem)();"]]]
  })));
}
window.FrameTemplate = FrameTemplate;