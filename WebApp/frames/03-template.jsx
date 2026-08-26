/* Frame 03 — Template & Snippets */

function FrameTemplate() {
  const { useField, useFields } = window.washState;
  const [templateId, setTemplateId] = useField('templateCombo');
  const [activePlaybook, setActivePlaybook] = useField('activePlaybook');
  const [snippetSelections, setSnippetSelections] = useField('snippetSelections');
  const [snippetInputs, setSnippetInputs] = useField('snippetInputs');
  const meta = useFields('catalog_templates', 'catalog_snippets', 'catalog_playbooks');

  const templates  = meta.catalog_templates  || [];
  const sections   = meta.catalog_snippets   || [];
  const playbooks  = meta.catalog_playbooks  || [];
  const selections = snippetSelections || {};
  const inputs     = snippetInputs || {};

  const activeSnippetCount = Object.values(selections).flat().length;

  function toggleSnippet(sectionTemplate, itemId, allowMultiple) {
    const current = selections[sectionTemplate] || [];
    let next;
    if (allowMultiple) {
      next = current.includes(itemId)
        ? current.filter(x => x !== itemId)
        : [...current, itemId];
    } else {
      next = current.includes(itemId) ? [] : [itemId];
    }
    setSnippetSelections({ ...selections, [sectionTemplate]: next });
  }

  function setSnippetInput(key, value) {
    setSnippetInputs({ ...inputs, [key]: value });
  }

  function openPlaybook() {
    if (activePlaybook) window.wash.invoke('open-file', { path: activePlaybook }).catch(() => {});
  }

  function reloadPlaybook() {
    if (window.washState) window.washState.loadCatalogs();
  }

  const selTpl = templates.find(t => t.id === templateId);
  const status = [
    { icon: "info", k: "template", v: templateId || 'none' },
    { icon: "info", k: "snippets", v: `${activeSnippetCount} selected` },
  ];

  return (
    <Shell active="payload" crumbs={["Payload", "Template options"]} pipeActive="tpl"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "active" }}
      status={status}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Template &amp; snippets</h1>
          <span className="sub">Composed at render time from the active playbook YAML.</span>
        </div>

        <Sec title="Playbook" action={<button className="btn ghost" onClick={openPlaybook}><Icon name="dl" size={12} />Open in editor</button>}>
          <div className="card row" style={{ justifyContent: "space-between" }}>
            <div className="row" style={{ gap: 12 }}>
              <div style={{ width: 36, height: 36, borderRadius: 8, background: "var(--n-3)", display: "grid", placeItems: "center" }}>
                <Icon name="doc" size={16} />
              </div>
              <div>
                <div className="h2">{activePlaybook ? activePlaybook.split('\\').pop() : 'default.yaml'}</div>
                <div className="mono" style={{ fontSize: 11, color: "var(--n-7)" }}>
                  {templates.length} templates · {sections.length} sections
                </div>
              </div>
            </div>
            <div className="row" style={{ gap: 8 }}>
              <select className="select" style={{ width: 260 }}
                value={activePlaybook}
                onChange={e => {
                  setActivePlaybook(e.target.value);
                  window.wash.invoke('set-playbook', { path: e.target.value }).catch(() => {});
                }}>
                {playbooks.map(p => <option key={p} value={p}>{p}</option>)}
                {playbooks.length === 0 && <option value={activePlaybook || ''}>{activePlaybook || 'default.yaml'}</option>}
              </select>
              <button className="btn" onClick={reloadPlaybook}><Icon name="refresh" size={12} />Reload</button>
            </div>
          </div>
        </Sec>

        <Sec title="Template">
          <div className="card" style={{ padding: 0 }}>
            {templates.length === 0 ? (
              <div style={{ padding: "14px 16px", color: "var(--n-6)", fontSize: 12 }}>Loading templates…</div>
            ) : templates.map(tpl => (
              <TplRow key={tpl.id} name={tpl.id} desc={tpl.description || ''} placeholders={tpl.placeholderCount}
                selected={templateId === tpl.id}
                onClick={() => setTemplateId(tpl.id)} />
            ))}
          </div>
        </Sec>

        <Sec title="Snippets" action={<div className="row" style={{ gap: 8 }}>
          {activeSnippetCount > 0 && <Chip kind="acc" dot>{activeSnippetCount} active</Chip>}
        </div>}>
          <div className="card" style={{ padding: 0 }}>
            {sections.length === 0 ? (
              <div style={{ padding: "14px 16px", color: "var(--n-6)", fontSize: 12 }}>Loading snippets…</div>
            ) : sections.map(sec => (
              <SnipGroup key={sec.template}
                name={sec.name}
                template={sec.template}
                allowMultiple={sec.allowMultiple}
                items={sec.items || []}
                selected={selections[sec.template] || []}
                inputs={inputs}
                onToggle={(id) => toggleSnippet(sec.template, id, sec.allowMultiple)}
                onInput={setSnippetInput}
              />
            ))}
          </div>
        </Sec>
      </div>

      <SnippetDiffPreview />
    </Shell>
  );
}

function TplRow({ name, desc, placeholders, selected, onClick }) {
  return (
    <div className="list-item" onClick={onClick} style={{
      gridTemplateColumns: "20px 1fr auto auto", padding: "12px 16px",
      borderBottom: "1px solid var(--n-3)", cursor: "pointer",
      ...(selected ? { background: "var(--acc-bg)", boxShadow: "inset 2px 0 0 var(--acc)" } : {}),
    }}>
      <div style={{
        width: 14, height: 14, borderRadius: "50%",
        border: "1px solid " + (selected ? "var(--acc)" : "var(--n-5)"),
        background: selected ? "var(--acc)" : "transparent",
        boxShadow: selected ? "inset 0 0 0 3px var(--n-3)" : "none",
      }} />
      <div>
        <div className="ttl mono" style={{ fontSize: 13 }}>{name}</div>
        <div style={{ fontSize: 11, color: "var(--n-7)", marginTop: 2 }}>{desc}</div>
      </div>
      {placeholders != null && <Chip>{placeholders} placeholders</Chip>}
      <Icon name="chev" size={12} />
    </div>
  );
}

function SnipGroup({ name, template, allowMultiple, items, selected, inputs, onToggle, onInput }) {
  const [open, setOpen] = React.useState(true);
  const activeCount = selected.length;
  return (
    <div style={{ borderBottom: "1px solid var(--n-3)" }}>
      <div className="row" style={{ padding: "12px 16px", background: "var(--n-1)", gap: 10, cursor: "pointer" }}
        onClick={() => setOpen(o => !o)}>
        <Icon name="chev" size={11} sw={2} style={{ transform: open ? "none" : "rotate(-90deg)", transition: "0.15s" }} />
        <div>
          <div className="h2">{name}</div>
          <div className="mono" style={{ fontSize: 10, color: "var(--n-7)" }}>
            template: {template}{allowMultiple ? "  ·  allowMultiple" : ""}
          </div>
        </div>
        <div style={{ flex: 1 }} />
        {activeCount > 0 && <Chip kind="acc">{activeCount} active</Chip>}
      </div>
      {open && (
        <div style={{ padding: "6px 8px" }}>
          {items.map(it => {
            const isOn = selected.includes(it.id);
            const inputKey = `${template}:${it.id}`;
            return (
              <div key={it.id} className={"list-item" + (isOn ? " sel" : "")}
                style={{ gridTemplateColumns: "16px 1fr" }}>
                <div onClick={() => onToggle(it.id)} style={{
                  width: 12, height: 12, borderRadius: 3, cursor: "pointer", flexShrink: 0,
                  border: "1px solid " + (isOn ? "var(--acc)" : "var(--n-5)"),
                  background: isOn ? "var(--acc)" : "transparent",
                  display: "grid", placeItems: "center",
                }}>
                  {isOn && <Icon name="check" size={9} sw={3} />}
                </div>
                <div onClick={() => onToggle(it.id)} style={{ cursor: "pointer" }}>
                  <div className="ttl">{it.display || it.id}</div>
                  <div className="meta">id <span style={{ color: "var(--n-8)" }}>{it.id}</span></div>
                  {it.hasTextInput && isOn && (
                    <div className="row" style={{ marginTop: 6, gap: 8 }} onClick={e => e.stopPropagation()}>
                      <span className="hint mono" style={{ fontSize: 10 }}>{it.textInputLabel || 'Value'}</span>
                      <input className="input mono"
                        style={{ padding: "4px 8px", fontSize: 11, width: 260 }}
                        value={inputs[inputKey] || ''}
                        onChange={e => onInput(inputKey, e.target.value)}
                        placeholder={it.textInputPlaceholder || ''} />
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function SnippetDiffPreview() {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="doc" size={11} />render diff</div>
        <div style={{ flex: 1 }} />
      </div>
      <div className="pbody ppad">
        <div style={{ color: "var(--n-6)", fontSize: 12, fontFamily: "var(--f-mono)" }}>
          Snippet expansion preview appears after a build.
        </div>
      </div>
    </div>
  );
}

window.FrameTemplate = FrameTemplate;

