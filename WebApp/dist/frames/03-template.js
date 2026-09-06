function FrameTemplate() {
  const {
    useField,
    useFields
  } = window.washState;
  const [templateId, setTemplateId] = useField('templateCombo');
  const [activePlaybook, setActivePlaybook] = useField('activePlaybook');
  const [snippetSelections, setSnippetSelections] = useField('snippetSelections');
  const [snippetInputs, setSnippetInputs] = useField('snippetInputs');
  const meta = useFields('catalog_templates', 'catalog_snippets', 'catalog_playbooks');
  const templates = meta.catalog_templates || [];
  const sections = meta.catalog_snippets || [];
  const playbooks = meta.catalog_playbooks || [];
  const selections = snippetSelections || {};
  const inputs = snippetInputs || {};
  const activeSnippetCount = Object.values(selections).flat().length;
  function toggleSnippet(sectionTemplate, itemId, allowMultiple) {
    const current = selections[sectionTemplate] || [];
    let next;
    if (allowMultiple) {
      next = current.includes(itemId) ? current.filter(x => x !== itemId) : [...current, itemId];
    } else {
      next = current.includes(itemId) ? [] : [itemId];
    }
    setSnippetSelections({
      ...selections,
      [sectionTemplate]: next
    });
  }
  function setSnippetInput(key, value) {
    setSnippetInputs({
      ...inputs,
      [key]: value
    });
  }
  function openPlaybook() {
    if (activePlaybook) window.wash.invoke('open-file', {
      path: activePlaybook
    }).catch(() => {});
  }
  function reloadPlaybook() {
    if (window.washState) window.washState.loadCatalogs();
  }
  const selTpl = templates.find(t => t.id === templateId);
  const status = [{
    icon: "info",
    k: "template",
    v: templateId || 'none'
  }, {
    icon: "info",
    k: "snippets",
    v: `${activeSnippetCount} selected`
  }];
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
  }, "Template & snippets"), React.createElement("span", {
    className: "sub"
  }, "Composed at render time from the active playbook YAML.")), React.createElement(Sec, {
    title: "Playbook",
    action: React.createElement("button", {
      className: "btn ghost",
      disabled: !activePlaybook,
      onClick: openPlaybook
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
  }, activePlaybook ? activePlaybook.split('\\').pop() : 'default.yaml'), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 11,
      color: "var(--n-7)"
    }
  }, templates.length, " templates \xB7 ", sections.length, " sections"))), React.createElement("div", {
    className: "row",
    style: {
      gap: 8
    }
  }, React.createElement("select", {
    className: "select",
    style: {
      width: 260
    },
    value: activePlaybook,
    onChange: async e => {
      const nextPath = e.target.value;
      try {
        const r = await window.wash.invoke('set-playbook', {
          path: nextPath
        });
        if (!r || !r.ok) {
          window.wash.notify(r?.message || 'Could not activate that playbook.', 'err');
          return;
        }
        setActivePlaybook(r.active || nextPath);
        await window.washState.loadCatalogs();
        window.wash.notify('Playbook activated and catalogs reloaded.', 'ok');
      } catch {}
    }
  }, playbooks.map(p => React.createElement("option", {
    key: p,
    value: p
  }, p)), playbooks.length === 0 && React.createElement("option", {
    value: activePlaybook || ''
  }, activePlaybook || 'default.yaml')), React.createElement("button", {
    className: "btn",
    onClick: reloadPlaybook
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
  }, templates.length === 0 ? React.createElement("div", {
    style: {
      padding: "14px 16px",
      color: "var(--n-6)",
      fontSize: 12
    }
  }, "Loading templates\u2026") : templates.map(tpl => React.createElement(TplRow, {
    key: tpl.id,
    name: tpl.id,
    desc: tpl.description || '',
    placeholders: tpl.placeholderCount,
    selected: templateId === tpl.id,
    onClick: () => setTemplateId(tpl.id)
  })))), React.createElement(Sec, {
    title: "Snippets",
    action: React.createElement("div", {
      className: "row",
      style: {
        gap: 8
      }
    }, activeSnippetCount > 0 && React.createElement(Chip, {
      kind: "acc",
      dot: true
    }, activeSnippetCount, " active"))
  }, React.createElement("div", {
    className: "card",
    style: {
      padding: 0
    }
  }, sections.length === 0 ? React.createElement("div", {
    style: {
      padding: "14px 16px",
      color: "var(--n-6)",
      fontSize: 12
    }
  }, "Loading snippets\u2026") : sections.map(sec => React.createElement(SnipGroup, {
    key: sec.template,
    name: sec.name,
    template: sec.template,
    allowMultiple: sec.allowMultiple,
    items: sec.items || [],
    selected: selections[sec.template] || [],
    inputs: inputs,
    onToggle: id => toggleSnippet(sec.template, id, sec.allowMultiple),
    onInput: setSnippetInput
  }))))), React.createElement(SnippetDiffPreview, null));
}
function TplRow({
  name,
  desc,
  placeholders,
  selected,
  onClick
}) {
  return React.createElement("div", {
    className: "list-item",
    onClick: onClick,
    style: {
      gridTemplateColumns: "20px 1fr auto auto",
      padding: "12px 16px",
      borderBottom: "1px solid var(--n-3)",
      cursor: "pointer",
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
  }, desc)), placeholders != null && React.createElement(Chip, null, placeholders, " placeholders"), React.createElement(Icon, {
    name: "chev",
    size: 12
  }));
}
function SnipGroup({
  name,
  template,
  allowMultiple,
  items,
  selected,
  inputs,
  onToggle,
  onInput
}) {
  const [open, setOpen] = React.useState(true);
  const activeCount = selected.length;
  return React.createElement("div", {
    style: {
      borderBottom: "1px solid var(--n-3)"
    }
  }, React.createElement("div", {
    className: "row",
    style: {
      padding: "12px 16px",
      background: "var(--n-1)",
      gap: 10,
      cursor: "pointer"
    },
    onClick: () => setOpen(o => !o)
  }, React.createElement(Icon, {
    name: "chev",
    size: 11,
    sw: 2,
    style: {
      transform: open ? "none" : "rotate(-90deg)",
      transition: "0.15s"
    }
  }), React.createElement("div", null, React.createElement("div", {
    className: "h2"
  }, name), React.createElement("div", {
    className: "mono",
    style: {
      fontSize: 10,
      color: "var(--n-7)"
    }
  }, "template: ", template, allowMultiple ? "  ·  allowMultiple" : "")), React.createElement("div", {
    style: {
      flex: 1
    }
  }), activeCount > 0 && React.createElement(Chip, {
    kind: "acc"
  }, activeCount, " active")), open && React.createElement("div", {
    style: {
      padding: "6px 8px"
    }
  }, items.map(it => {
    const isOn = selected.includes(it.id);
    return React.createElement("div", {
      key: it.id,
      className: "list-item" + (isOn ? " sel" : ""),
      style: {
        gridTemplateColumns: "16px 1fr"
      }
    }, React.createElement("div", {
      onClick: () => onToggle(it.id),
      style: {
        width: 12,
        height: 12,
        borderRadius: 3,
        cursor: "pointer",
        flexShrink: 0,
        border: "1px solid " + (isOn ? "var(--acc)" : "var(--n-5)"),
        background: isOn ? "var(--acc)" : "transparent",
        display: "grid",
        placeItems: "center"
      }
    }, isOn && React.createElement(Icon, {
      name: "check",
      size: 9,
      sw: 3
    })), React.createElement("div", {
      onClick: () => onToggle(it.id),
      style: {
        cursor: "pointer"
      }
    }, React.createElement("div", {
      className: "ttl"
    }, it.display || it.id), React.createElement("div", {
      className: "meta"
    }, "id ", React.createElement("span", {
      style: {
        color: "var(--n-8)"
      }
    }, it.id)), isOn && (it.inputs || []).map(inp => {
      const inputKey = `${template}_${it.id}_${inp.id}`;
      return React.createElement("div", {
        key: inp.id,
        className: "row",
        style: {
          marginTop: 6,
          gap: 8
        },
        onClick: e => e.stopPropagation()
      }, React.createElement("span", {
        className: "hint mono",
        style: {
          fontSize: 10
        }
      }, inp.label || inp.id), React.createElement("input", {
        className: "input mono",
        style: {
          padding: "4px 8px",
          fontSize: 11,
          width: 260
        },
        value: inputs[inputKey] != null ? inputs[inputKey] : inp.defaultValue || '',
        onChange: e => onInput(inputKey, e.target.value),
        placeholder: inp.placeholder || ''
      }));
    })));
  })));
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
    style: {
      flex: 1
    }
  })), React.createElement("div", {
    className: "pbody ppad"
  }, React.createElement("div", {
    style: {
      color: "var(--n-6)",
      fontSize: 12,
      fontFamily: "var(--f-mono)"
    }
  }, "Selected snippets and their scoped input values are passed to the active playbook during Build.")));
}
window.FrameTemplate = FrameTemplate;