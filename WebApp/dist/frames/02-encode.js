function FrameEncode() {
  const {
    useField,
    useFields
  } = window.washState;
  const [sgnEnabled, setSgnEnabled] = useField('shikataGaNaiEnabledCheckBox');
  const [sgnCount, setSgnCount] = useField('shikataGaNaiEncodeCountInput');
  const [sgnMax, setSgnMax] = useField('shikataGaNaiMaxBytesInput');
  const [sgnPlace, setSgnPlace] = useField('shikataGaNaiPlacement');
  const [encoderIdx, setEncoderIdx] = useField('encoderCombo');
  const [envelopeIdx, setEnvelopeIdx] = useField('envelopeCombo');
  const meta = useFields('catalog_encoders', 'catalog_envelopes');
  const encoders = meta.catalog_encoders || [];
  const envelopes = meta.catalog_envelopes || [];
  function reloadCatalog() {
    if (window.washState) window.washState.loadCatalogs();
  }
  const sgnOn = sgnEnabled === 'True';
  const selEnc = encoders.find(e => String(e.index) === String(encoderIdx));
  const selEnv = envelopes.find(e => String(e.index) === String(envelopeIdx));
  const status = [{
    icon: "info",
    k: "encoder",
    v: selEnc ? selEnc.name : encoderIdx || 'none'
  }, {
    icon: "info",
    k: "envelope",
    v: selEnv ? selEnv.name : envelopeIdx || 'none'
  }, {
    icon: "info",
    k: "sgn",
    v: sgnOn ? `${sgnCount} passes · ${sgnPlace}` : 'off'
  }];
  return React.createElement(Shell, {
    active: "payload",
    crumbs: ["Payload", "Encoding"],
    pipeActive: "enc",
    pipeStates: {
      src: "done",
      sgn: "done",
      enc: "active"
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
  }, "Encoding"), React.createElement("span", {
    className: "sub"
  }, "Transform raw shellcode bytes before they're embedded.")), React.createElement(Sec, {
    title: "Shikata Ga Nai \xB7 preprocessor",
    action: React.createElement("div", {
      className: "row",
      style: {
        gap: 8
      }
    }, React.createElement(Toggle, {
      on: sgnOn,
      onChange: v => setSgnEnabled(v ? 'True' : 'False')
    }))
  }, React.createElement("div", {
    className: "card",
    style: {
      opacity: sgnOn ? 1 : 0.55,
      pointerEvents: sgnOn ? 'auto' : 'none'
    }
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
    value: sgnCount,
    onChange: e => setSgnCount(e.target.value),
    style: {
      width: 100
    }
  })), React.createElement(Field, {
    label: "Decoder max bytes",
    hint: "Obfuscation budget per pass."
  }, React.createElement("input", {
    className: "input mono",
    value: sgnMax,
    onChange: e => setSgnMax(e.target.value),
    style: {
      width: 100
    }
  })), React.createElement(Field, {
    label: "Placement"
  }, React.createElement(Seg, {
    value: sgnPlace,
    onChange: setSgnPlace,
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
  })))), React.createElement(Sec, {
    title: "Bin2Shell",
    action: React.createElement("button", {
      className: "btn ghost",
      onClick: reloadCatalog
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
  }, "algos.yaml")), encoders.length === 0 ? React.createElement("div", {
    style: {
      color: "var(--n-6)",
      fontSize: 12,
      padding: "8px 0"
    }
  }, "Provisioning Bin2Shell\u2026 or catalog unavailable.") : React.createElement("div", {
    className: "list"
  }, encoders.map(enc => React.createElement(EncRow, {
    key: enc.index,
    name: enc.name,
    id: String(enc.index),
    desc: enc.description,
    selected: String(encoderIdx) === String(enc.index),
    onClick: () => setEncoderIdx(String(enc.index))
  })))), React.createElement("div", {
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
  }, "algos.yaml")), envelopes.length === 0 ? React.createElement("div", {
    style: {
      color: "var(--n-6)",
      fontSize: 12,
      padding: "8px 0"
    }
  }, "Catalog unavailable.") : React.createElement("div", {
    className: "list"
  }, envelopes.map(env => React.createElement(EncRow, {
    key: env.index,
    name: env.name,
    id: String(env.index),
    desc: env.description,
    selected: String(envelopeIdx) === String(env.index),
    onClick: () => setEnvelopeIdx(String(env.index))
  })))))))), React.createElement(EncodedPreview, null));
}
function EncRow({
  name,
  id,
  desc,
  selected,
  onClick
}) {
  return React.createElement("div", {
    className: "list-item" + (selected ? " sel" : ""),
    onClick: onClick,
    style: {
      cursor: "pointer"
    }
  }, React.createElement("div", {
    style: {
      width: 14,
      height: 14,
      borderRadius: "50%",
      border: "1px solid " + (selected ? "var(--acc)" : "var(--n-5)"),
      background: selected ? "var(--acc)" : "transparent",
      boxShadow: selected ? "inset 0 0 0 3px var(--n-3)" : "none",
      flexShrink: 0
    }
  }), React.createElement("div", null, React.createElement("div", {
    className: "ttl"
  }, name), React.createElement("div", {
    className: "meta"
  }, "id ", id, desc ? " · " + desc : "")));
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
  }), "encoding")), React.createElement("div", {
    className: "pbody ppad"
  }, React.createElement("div", {
    style: {
      color: "var(--n-6)",
      fontSize: 12,
      fontFamily: "var(--f-mono)"
    }
  }, "The selected encoder, envelope, and optional SGN step are applied during Build.")));
}
window.FrameEncode = FrameEncode;