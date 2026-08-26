/* Frame 05 — Backdoor / PE inject */

function FrameBackdoor() {
  const { useField, useFields } = window.washState;
  const [bdEnabled, setBdEnabled] = useField('EnableBackdooringToggle');
  const [targetPath, setTargetPath] = useField('TargetPePath');
  const [injMethod, setInjMethod] = useField('InjectionMethodCombo');
  const [carrierInvoke, setCarrierInvoke] = useField('CarrierInvokeCombo');
  const meta = useFields('pe_analysis', 'pe_caves', 'pe_imports');

  const peInfo = meta.pe_analysis || null;
  const caves  = meta.pe_caves   || [];
  const bdOn = bdEnabled === 'True';

  React.useEffect(() => {
    if (!targetPath || !bdOn) return;
    window.wash.invoke('analyze-pe', { path: targetPath })
      .then(r => {
        if (!r || !r.ok) return;
        window.washState.update({
          pe_analysis: r,
          pe_caves:    r.caves  || [],
          pe_imports:  r.imports || [],
        });
      })
      .catch(() => {});
  }, [targetPath, bdOn]);

  function browsePe() {
    window.wash.invoke('browse-file', { filters: [{ name: 'PE files', patterns: ['.exe', '.dll'] }] })
      .then(r => { if (r && r.ok && r.path) setTargetPath(r.path); })
      .catch(() => {});
  }

  const status = [
    { icon: "info", k: "target", v: peInfo ? (peInfo.fileName || targetPath?.split('\\').pop() || 'none') + ' · ' + (peInfo.fileSizeText || '') : 'none' },
    { icon: "info", k: "method", v: injMethod || 'none' },
  ];

  return (
    <Shell active="backdooring" crumbs={["Backdooring"]} pipeActive="bd"
      pipeStates={{ src: "done", sgn: "done", enc: "done", tpl: "done", cmp: "done", bd: "active" }}
      status={status}>
      <div className="cfg">
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Backdoor PE</h1>
          <span className="sub">Inject the compiled loader into an existing executable.</span>
          <div style={{ flex: 1 }} />
          <Toggle on={bdOn} onChange={v => setBdEnabled(v ? 'True' : 'False')} />
          <span style={{ fontSize: 12, color: bdOn ? "var(--n-9)" : "var(--n-6)" }}>Enable</span>
        </div>

        <div style={{ opacity: bdOn ? 1 : 0.4, pointerEvents: bdOn ? 'auto' : 'none' }}>
          {/* Target picker */}
          <Sec title="Target" action={peInfo && <Chip kind={peInfo.hasAuthenticode ? "warn" : "ok"} dot>{peInfo.is64Bit ? "PE64" : "PE32"}</Chip>}>
            <div className="card">
              <div className="row" style={{ gap: 16, alignItems: "stretch" }}>
                <div style={{ flex: 1 }}>
                  <Field label="Donor executable">
                    <input className="input mono" value={targetPath}
                      onChange={e => setTargetPath(e.target.value)}
                      placeholder="C:\Tools\target.exe" />
                  </Field>
                  <div className="row" style={{ marginTop: 10, gap: 8 }}>
                    <button className="btn" onClick={browsePe}><Icon name="upload" size={12} />Browse…</button>
                    <button className="btn ghost" onClick={() => targetPath && setTargetPath(targetPath + '')}><Icon name="info" size={12} />Re-analyze</button>
                  </div>
                </div>
                {peInfo ? (
                  <div className="mono" style={{ borderLeft: "1px solid var(--n-4)", paddingLeft: 18, fontSize: 11, color: "var(--n-8)", lineHeight: 1.85, minWidth: 220 }}>
                    size       <span style={{ color: "var(--n-10)" }}>{peInfo.fileSizeText}</span>{"\n"}
                    machine    <span style={{ color: "var(--n-10)" }}>{peInfo.architecture}</span>{"\n"}
                    signed     <span style={{ color: peInfo.hasAuthenticode ? "var(--warn)" : "var(--ok)" }}>{peInfo.hasAuthenticode ? "yes" : "no"}</span>{"\n"}
                    sections   <span style={{ color: "var(--n-10)" }}>{peInfo.sectionCount}</span>{"\n"}
                    code caves <span style={{ color: "var(--ok)" }}>{peInfo.codeCaveCount}</span>
                    {peInfo.maxCaveSize ? ` · max ${peInfo.maxCaveSize} B` : ''}
                  </div>
                ) : (
                  <div style={{ borderLeft: "1px solid var(--n-4)", paddingLeft: 18, color: "var(--n-6)", fontSize: 12, minWidth: 180 }}>
                    {targetPath ? 'Analysing…' : 'Select a target PE'}
                  </div>
                )}
              </div>
            </div>
          </Sec>

          {/* Method picker */}
          <Sec title="Injection method">
            <div className="card" style={{ padding: 0, overflow: "hidden" }}>
              {[
                { id: "CodeCave",         name: "Code cave",         sub: "Reuse padding inside existing sections", trait: "zero growth",         traitTone: "ok",   capacity: caves.length > 0 ? `≤ ${Math.max(...caves.map(c => c.size))} B` : "—" },
                { id: "NewSection",       name: "New section",       sub: "Append .wm section with payload",        trait: "unlimited capacity",   traitTone: "acc",  capacity: "any" },
                { id: "SectionExtend",    name: "Section extension", sub: "Extend .text by aligned amount",          trait: "grows file size",       traitTone: "",     capacity: "≤ 64 KB" },
                { id: "TlsCallback",      name: "TLS callback",      sub: "Pre-main execution · x64 only",           trait: "silent execution",     traitTone: "acc",  capacity: "N/A" },
              ].map(m => (
                <MethodRow key={m.id} {...m} selected={injMethod === m.id} onClick={() => setInjMethod(m.id)} />
              ))}
            </div>
          </Sec>

          {caves.length > 0 && (
            <Sec title="Code caves">
              <div className="card" style={{ padding: 0 }}>
                {caves.slice(0, 6).map((c, i) => (
                  <CaveRow key={i} rva={c.rvaHex || c.virtualAddress} sec={c.sectionName} sz={c.size} suitable={c.suitableForInjection} />
                ))}
              </div>
            </Sec>
          )}
        </div>
      </div>

      <PeMapPreview peInfo={peInfo} caves={caves} />
    </Shell>
  );
}

function MethodRow({ name, sub, trait, traitTone, capacity, selected, onClick }) {
  const toneColor = { ok: "var(--ok)", warn: "var(--warn)", acc: "var(--acc)", "": "var(--n-7)" }[traitTone || ""];
  return (
    <div onClick={onClick} style={{
      display: "grid", gridTemplateColumns: "20px 1fr auto 14px",
      gap: 18, padding: "14px 18px", alignItems: "center",
      borderBottom: "1px solid var(--n-3)", cursor: "pointer",
      ...(selected ? { background: "var(--acc-bg)", boxShadow: "inset 3px 0 0 var(--acc)" } : {}),
    }}>
      <div style={{
        width: 16, height: 16, borderRadius: "50%",
        border: "1px solid " + (selected ? "var(--acc)" : "var(--n-6)"),
        background: selected ? "var(--acc)" : "transparent",
        boxShadow: selected ? "inset 0 0 0 4px var(--n-2)" : "none",
      }} />
      <div style={{ minWidth: 0 }}>
        <div className="h2" style={{ fontSize: 14 }}>{name}</div>
        <div style={{ fontSize: 12, color: "var(--n-7)", marginTop: 2 }}>{sub}</div>
      </div>
      <div className="row" style={{ gap: 18, fontSize: 12, whiteSpace: "nowrap" }}>
        <span style={{ display: "flex", alignItems: "center", gap: 7, color: toneColor }}>
          <span style={{ width: 7, height: 7, borderRadius: "50%", background: toneColor }} />{trait}
        </span>
        <span style={{ width: 1, height: 14, background: "var(--n-4)" }} />
        <span className="mono" style={{ color: "var(--n-8)", minWidth: 64, textAlign: "right" }}>{capacity}</span>
      </div>
      <Icon name="chev" size={12} />
    </div>
  );
}

function CaveRow({ rva, sec, sz, suitable }) {
  return (
    <div style={{
      display: "grid", gridTemplateColumns: "1fr auto auto",
      padding: "10px 16px", borderBottom: "1px solid var(--n-3)", alignItems: "center",
    }}>
      <div className="mono" style={{ fontSize: 11 }}>{typeof rva === 'number' ? '0x' + rva.toString(16).padStart(8, '0') : rva} <span style={{ color: "var(--n-7)", fontSize: 10 }}>{sec}</span></div>
      {suitable && <Chip kind="ok" dot>suitable</Chip>}
      <span className="mono" style={{ fontSize: 11, color: "var(--n-8)", marginLeft: 12 }}>{sz} B</span>
    </div>
  );
}

function PeMapPreview({ peInfo, caves }) {
  return (
    <div className="preview">
      <div className="ptabs">
        <div className="ptab on"><Icon name="layers" size={11} />PE map</div>
        <div style={{ flex: 1 }} />
      </div>
      <div className="pbody ppad">
        {peInfo ? (
          <>
            <div className="row" style={{ marginBottom: 14, gap: 10 }}>
              <Chip kind="acc">{peInfo.fileName || 'target.exe'}</Chip>
              <Chip>{peInfo.sectionCount} sections</Chip>
              {peInfo.codeCaveCount > 0 && <Chip kind="ok" dot>{peInfo.codeCaveCount} code caves</Chip>}
            </div>
            {caves.length > 0 && <>
              <H3>Candidate code caves</H3>
              <div style={{ marginTop: 8 }}>
                {caves.slice(0, 6).map((c, i) => (
                  <CaveRow key={i} rva={c.rvaHex || c.virtualAddress} sec={c.sectionName} sz={c.size} suitable={c.suitableForInjection} />
                ))}
              </div>
            </>}
          </>
        ) : (
          <div style={{ color: "var(--n-6)", fontSize: 12, fontFamily: "var(--f-mono)" }}>
            PE analysis appears here after selecting a target.
          </div>
        )}
      </div>
    </div>
  );
}

window.FrameBackdoor = FrameBackdoor;

