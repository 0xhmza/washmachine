function FrameWebWizard() {
  const modal = React.createElement("div", {
    className: "modal-scrim"
  }, React.createElement("div", {
    className: "modal",
    style: {
      width: 820
    }
  }, React.createElement("div", {
    className: "modal-hd"
  }, React.createElement("div", {
    style: {
      width: 32,
      height: 32,
      borderRadius: 8,
      background: "var(--acc-bg)",
      border: "1px solid var(--acc-line)",
      display: "grid",
      placeItems: "center",
      color: "var(--acc)"
    }
  }, React.createElement(Icon, {
    name: "globe",
    size: 16
  })), React.createElement("div", null, React.createElement("div", {
    className: "h2"
  }, "Web payload wizard"), React.createElement("div", {
    className: "sub"
  }, "Encode \u2192 host \u2192 fetch. Generates the C++ retrieval stub that gets stitched into the template.")), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement("button", {
    className: "btn ghost"
  }, React.createElement(Icon, {
    name: "x",
    size: 14
  }))), React.createElement("div", {
    className: "row",
    style: {
      padding: "14px 22px 0",
      gap: 0
    }
  }, React.createElement(Step, {
    n: "1",
    name: "Encoder & envelope",
    done: true
  }), React.createElement(StepArrow, {
    done: true
  }), React.createElement(Step, {
    n: "2",
    name: "Fetch helper",
    active: true
  }), React.createElement(StepArrow, null), React.createElement(Step, {
    n: "3",
    name: "Verify URL"
  }), React.createElement(StepArrow, null), React.createElement(Step, {
    n: "4",
    name: "Generate"
  })), React.createElement("div", {
    className: "modal-bd"
  }, React.createElement(H3, null, "Step 2 of 4 \xB7 Fetch helper"), React.createElement("div", {
    style: {
      marginTop: 6,
      marginBottom: 18,
      fontSize: 13,
      color: "var(--n-8)"
    }
  }, "Choose how the loader retrieves and decodes the payload at runtime. Heavier helpers carry more dependencies; lighter ones surface in fewer signatures."), React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "1fr 1fr",
      gap: 12
    }
  }, React.createElement(FetchOption, {
    name: "WinInet",
    sub: "InternetOpenA \u2192 HttpOpenRequest \u2192 InternetReadFile",
    tag: "\u2248 +6 KB",
    weight: "standard"
  }), React.createElement(FetchOption, {
    name: "WinHttp",
    sub: "WinHttpOpen \u2192 WinHttpSendRequest",
    tag: "\u2248 +4 KB",
    weight: "recommended",
    selected: true
  }), React.createElement(FetchOption, {
    name: "URLDownloadToFile",
    sub: "urlmon.dll \xB7 simplest, most flagged",
    tag: "\u2248 +1 KB",
    weight: "loud",
    warn: true
  }), React.createElement(FetchOption, {
    name: "Raw socket TLS",
    sub: "ws2_32 + manual TLS handshake",
    tag: "\u2248 +18 KB",
    weight: "quiet"
  })), React.createElement("div", {
    className: "div"
  }), React.createElement(Field, {
    label: "User-Agent override",
    hint: "Empty = use default Windows UA."
  }, React.createElement("input", {
    className: "input mono",
    defaultValue: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
  })), React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "1fr 1fr",
      gap: 16,
      marginTop: 14
    }
  }, React.createElement(Field, {
    label: "Retry policy"
  }, React.createElement(Seg, {
    value: "3x",
    options: [{
      v: "off",
      l: "Off"
    }, {
      v: "3x",
      l: "3× backoff"
    }, {
      v: "inf",
      l: "Infinite"
    }]
  })), React.createElement("div", {
    className: "field"
  }, React.createElement("label", null, "Verification"), React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      gap: 12,
      padding: "6px 12px",
      height: 34,
      border: "1px solid var(--n-4)",
      borderBottom: "1px solid var(--n-5)",
      borderRadius: "var(--r-2)",
      background: "var(--n-1)"
    }
  }, React.createElement(Toggle, {
    on: true
  }), React.createElement("div", {
    style: {
      flex: 1
    }
  }, React.createElement("div", {
    style: {
      fontSize: 13,
      color: "var(--n-10)",
      lineHeight: 1.2
    }
  }, "Pin server certificate"), React.createElement("div", {
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 2
    }
  }, "SHA-256 of leaf cert \xB7 embedded in stub")))))), React.createElement("div", {
    className: "modal-ft"
  }, React.createElement("button", {
    className: "btn ghost"
  }, "Cancel"), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginRight: 8
    }
  }, "est. stub size: 4.1 KB \xB7 payload: 789 B"), React.createElement("button", {
    className: "btn"
  }, "\u2190 Back"), React.createElement("button", {
    className: "btn primary"
  }, "Next \xB7 verify URL ", React.createElement("span", {
    className: "sk"
  }, "\u23CE")))));
  return React.createElement(Shell, {
    active: "payload",
    crumbs: ["Payload", "Web payload wizard"],
    pipeActive: "src",
    modal: modal
  }, React.createElement("div", {
    className: "cfg"
  }), React.createElement("div", {
    className: "preview"
  }));
}
function Step({
  n,
  name,
  done,
  active
}) {
  return React.createElement("div", {
    className: "row",
    style: {
      gap: 8
    }
  }, React.createElement("div", {
    style: {
      width: 22,
      height: 22,
      borderRadius: "50%",
      background: done ? "var(--ok-bg)" : active ? "var(--acc-bg)" : "var(--n-2)",
      color: done ? "var(--ok)" : active ? "var(--acc)" : "var(--n-7)",
      border: "1px solid " + (done ? "oklch(0.55 0.12 155 / 0.45)" : active ? "var(--acc-line)" : "var(--n-4)"),
      display: "grid",
      placeItems: "center",
      fontFamily: "var(--f-mono)",
      fontSize: 11,
      fontWeight: 500
    }
  }, done ? React.createElement(Icon, {
    name: "check",
    size: 11,
    sw: 3
  }) : n), React.createElement("div", {
    style: {
      fontSize: 12,
      color: active ? "var(--n-10)" : "var(--n-7)",
      fontWeight: active ? 500 : 400
    }
  }, name));
}
function StepArrow({
  done
}) {
  return React.createElement("div", {
    style: {
      flex: 1,
      height: 1,
      background: done ? "var(--ok)" : "var(--n-5)",
      margin: "0 12px",
      opacity: done ? 0.5 : 1
    }
  });
}
function FetchOption({
  name,
  sub,
  tag,
  weight,
  selected,
  warn
}) {
  return React.createElement("div", {
    style: {
      padding: 14,
      borderRadius: 10,
      border: "1px solid " + (selected ? "var(--acc-line)" : "var(--n-4)"),
      background: selected ? "var(--acc-bg)" : "var(--n-1)",
      cursor: "pointer"
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
  }, name), selected ? React.createElement(Chip, {
    kind: "acc",
    dot: true
  }, "selected") : warn ? React.createElement(Chip, {
    kind: "warn"
  }, weight) : React.createElement(Chip, null, weight)), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)",
      marginTop: 6
    }
  }, sub), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-6)",
      marginTop: 4
    }
  }, tag));
}
window.FrameWebWizard = FrameWebWizard;