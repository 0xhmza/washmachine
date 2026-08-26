function FrameStartup() {
  return React.createElement("div", {
    className: "app",
    style: {
      gridTemplateColumns: "1fr"
    }
  }, React.createElement("div", {
    style: {
      display: "grid",
      gridTemplateColumns: "1fr 480px",
      height: "100%",
      background: "var(--n-1)"
    }
  }, React.createElement("div", {
    style: {
      background: `
            radial-gradient(circle at 18% 22%, oklch(0.32 0.08 235 / 0.45) 0%, transparent 45%),
            radial-gradient(circle at 90% 90%, oklch(0.28 0.06 250 / 0.5) 0%, transparent 50%),
            var(--n-0)
          `,
      padding: 64,
      display: "flex",
      flexDirection: "column",
      justifyContent: "space-between",
      position: "relative",
      overflow: "hidden"
    }
  }, React.createElement("div", null, React.createElement("div", {
    className: "row",
    style: {
      gap: 14
    }
  }, React.createElement("div", {
    style: {
      width: 44,
      height: 44,
      borderRadius: 10,
      background: "linear-gradient(155deg, var(--acc), var(--acc-dim))",
      display: "grid",
      placeItems: "center",
      color: "var(--n-0)",
      fontFamily: "var(--f-mono)",
      fontWeight: 700,
      fontSize: 18,
      boxShadow: "0 6px 22px oklch(0.5 0.12 235 / 0.35)"
    }
  }, "W"), React.createElement("div", null, React.createElement("div", {
    style: {
      fontSize: 18,
      fontWeight: 500,
      color: "var(--n-10)",
      letterSpacing: -0.01
    }
  }, "washmachine"), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, "v2.1.0 \xB7 loader builder")))), React.createElement("div", null, React.createElement("div", {
    style: {
      fontSize: 44,
      lineHeight: 1.05,
      letterSpacing: -0.025,
      fontWeight: 500,
      color: "var(--n-10)",
      maxWidth: 540
    }
  }, "The last ", React.createElement("span", {
    style: {
      color: "var(--acc)"
    }
  }, "shellcode loader"), " ", "builder you'll ever need."), React.createElement("div", {
    style: {
      fontSize: 15,
      color: "var(--n-8)",
      marginTop: 18,
      maxWidth: 520,
      lineHeight: 1.55
    }
  }, "YAML-driven playbooks. Pluggable evasion modules. Auto-discovered toolchains. Every build is a session \u2014 source, log, manifest, artifact, reproducible."), React.createElement("div", {
    className: "row",
    style: {
      marginTop: 28,
      gap: 10
    }
  }, React.createElement(Chip, {
    kind: "acc"
  }, "89 snippets"), React.createElement(Chip, {
    kind: "acc"
  }, "10 categories"), React.createElement(Chip, {
    kind: "acc"
  }, "6 templates"), React.createElement(Chip, {
    kind: "acc"
  }, "5 PE inject methods"))), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-7)",
      letterSpacing: 0.04
    }
  }, "for authorized security research only \xB7 all builds logged to ", React.createElement("span", {
    style: {
      color: "var(--n-9)"
    }
  }, "~/.washmachine/sessions"))), React.createElement("div", {
    style: {
      background: "var(--n-1)",
      borderLeft: "1px solid var(--n-4)",
      padding: 48,
      display: "flex",
      flexDirection: "column",
      gap: 24
    }
  }, React.createElement("div", null, React.createElement(H3, null, "System check"), React.createElement("div", {
    style: {
      fontSize: 20,
      color: "var(--n-10)",
      fontWeight: 500,
      marginTop: 6,
      letterSpacing: -0.01
    }
  }, "Preparing your environment"), React.createElement("div", {
    style: {
      fontSize: 13,
      color: "var(--n-8)",
      marginTop: 6
    }
  }, "Everything below is fetched once and verified on every launch.")), React.createElement("div", {
    className: "card",
    style: {
      padding: 0
    }
  }, React.createElement(Check, {
    label: ".NET 8 Desktop Runtime",
    detail: "x64 \xB7 8.0.11",
    status: "ok"
  }), React.createElement(Check, {
    label: "Windows App SDK 1.8",
    detail: "installed system-wide",
    status: "ok"
  }), React.createElement(Check, {
    label: "MSVC toolchain",
    detail: "cl.exe \xB7 19.39.33523",
    status: "ok"
  }), React.createElement(Check, {
    label: "Python 3.10+",
    detail: "3.12.4 \xB7 in PATH",
    status: "ok"
  }), React.createElement(Check, {
    label: "Bin2Shell",
    detail: "downloading \xB7 4.2 / 12 MB",
    status: "running",
    pct: 35
  }), React.createElement(Check, {
    label: "Shikata Ga Nai",
    detail: "not provisioned",
    status: "pending",
    action: "Provision"
  }), React.createElement(Check, {
    label: "vx_api_snippets.yaml",
    detail: "89 snippets \xB7 sha256 ok",
    status: "ok"
  }), React.createElement(Check, {
    label: "Output directory",
    detail: "~/.washmachine/out/",
    status: "ok",
    last: true
  })), React.createElement("div", {
    className: "meter",
    style: {
      width: "100%",
      height: 4
    }
  }, React.createElement("i", {
    style: {
      width: "78%"
    }
  })), React.createElement("div", {
    className: "row",
    style: {
      justifyContent: "space-between"
    }
  }, React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, "6 of 8 ready \xB7 1 provisioning \xB7 1 optional"), React.createElement("span", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-8)"
    }
  }, "est. 12s remaining")), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement("div", {
    className: "row",
    style: {
      gap: 8
    }
  }, React.createElement("button", {
    className: "btn ghost"
  }, React.createElement(Icon, {
    name: "cog",
    size: 12
  }), "Advanced"), React.createElement("div", {
    style: {
      flex: 1
    }
  }), React.createElement("button", {
    className: "btn"
  }, "Skip optional"), React.createElement("button", {
    className: "btn primary",
    disabled: true
  }, "Continue to workspace ", React.createElement(Icon, {
    name: "chev",
    size: 12
  }))))));
}
function Check({
  label,
  detail,
  status,
  pct,
  action,
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
  })) : status === "running" ? React.createElement("div", {
    style: {
      position: "relative",
      width: 14,
      height: 14
    }
  }, React.createElement("div", {
    className: "pulse",
    style: {
      position: "absolute",
      inset: 1,
      background: "var(--acc)",
      borderRadius: "50%"
    }
  }), React.createElement("div", {
    style: {
      position: "absolute",
      inset: 1,
      background: "var(--acc)",
      borderRadius: "50%"
    }
  })) : status === "err" ? React.createElement("div", {
    style: {
      width: 14,
      height: 14,
      borderRadius: "50%",
      background: "var(--err-bg)",
      color: "var(--err)",
      display: "grid",
      placeItems: "center"
    }
  }, React.createElement(Icon, {
    name: "x",
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
      padding: "12px 16px",
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
      color: status === "running" ? "var(--acc)" : "var(--n-7)",
      marginTop: 2
    }
  }, detail), status === "running" && React.createElement("div", {
    className: "meter",
    style: {
      width: 180,
      marginTop: 6,
      height: 3
    }
  }, React.createElement("i", {
    style: {
      width: `${pct}%`
    }
  }))), status === "running" && React.createElement(Chip, {
    kind: "acc",
    dot: true
  }, pct, "%"), status === "ok" && React.createElement(Chip, {
    kind: "ok"
  }, "ready"), status === "pending" && action && React.createElement("button", {
    className: "btn"
  }, React.createElement(Icon, {
    name: "dl",
    size: 11
  }), action));
}
window.FrameStartup = FrameStartup;