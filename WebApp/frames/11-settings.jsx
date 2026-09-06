/* Frame — Settings (app preferences + about) */

function FrameSettings() {
  const { useField } = window.washState;
  const [info, setInfo] = React.useState(null);
  const [verboseBuild, setVerboseBuild] = useField('VerboseBuildCheck');
  const [openAfter, setOpenAfter] = useField('OpenFolderAfterCompile');

  React.useEffect(() => {
    if (window.wash && window.wash.invoke) {
      window.wash.invoke('app-info', {})
        .then(setInfo)
        .catch(() => setInfo({ version: '—', cliAvailable: false }));
    }
  }, []);

  const i = info || {};

  return (
    <Shell active="settings" crumbs={["settings"]} pipeActive="" wide
      pipeStates={Object.fromEntries((typeof PIPELINE !== 'undefined' ? PIPELINE : []).map(p => [p.id, "skipped"]))}
      status={[
        { icon: "info", k: "mode", v: "local WebView2" },
      ]}>
      <div className="cfg" style={{ padding: "22px 28px" }}>
        <div style={{ display: "flex", alignItems: "baseline", gap: 14, marginBottom: 22 }}>
          <h1 className="h1">Settings</h1>
          <span className="sub">App-level preferences. Per-build options live on the build pages.</span>
        </div>

        <Sec title="Appearance">
          <div className="card">
            <div className="row" style={{ justifyContent: "space-between", marginBottom: 12 }}>
              <div>
                <div className="h2">Theme</div>
                <div className="sub">Washmachine ships with a single dark theme tuned for long sessions.</div>
              </div>
              <Chip kind="acc" dot>dark · locked</Chip>
            </div>
          </div>
        </Sec>

        <Sec title="Paths">
          <div className="card">
            <Field label="Output directory" hint="where built artifacts and session manifests are written">
              <input className="input mono"
                     value={i.executableDirectory || "(loading…)"}
                     style={{ width: "100%" }} readOnly />
            </Field>
            <div style={{ height: 12 }} />
            <Field label="Active playbook" hint="YAML catalog used to compose snippets">
              <input className="input mono"
                     value={i.playbookPath || "(loading…)"}
                     style={{ width: "100%" }} readOnly />
            </Field>
            <div style={{ height: 12 }} />
            <Field label="Assets directory">
              <input className="input mono"
                     value={i.assetsDirectory || "(loading…)"}
                     style={{ width: "100%" }} readOnly />
            </Field>
          </div>
        </Sec>

        <Sec title="Build pipeline">
          <div className="card">
            <div className="row" style={{ justifyContent: "space-between", marginBottom: 12 }}>
              <div>
                <div className="h2">CLI executable</div>
                <div className="mono" style={{ fontSize: 11, color: "var(--n-7)", marginTop: 4 }}>
                  {i.cliPath || "(loading…)"}
                </div>
              </div>
              {i.cliAvailable
                ? <Chip kind="ok" dot>available</Chip>
                : <Chip kind="warn" dot>not built</Chip>}
            </div>
            <div className="div" />
            <div className="row" style={{ gap: 24 }}>
              <Field label="Verbose CLI output" hint="Include debug-level log lines in the build panel">
                <Toggle
                  on={verboseBuild === 'True'}
                  onChange={v => setVerboseBuild(v ? 'True' : 'False')}
                />
              </Field>
              <Field label="Auto-open artifact on success" hint="Reveal output file in Explorer after a successful build">
                <Toggle
                  on={openAfter !== 'False'}
                  onChange={v => setOpenAfter(v ? 'True' : 'False')}
                />
              </Field>
            </div>
          </div>
        </Sec>

        <Sec title="Keyboard shortcuts">
          <div className="card">
            <div className="col" style={{ gap: 8 }}>
              <ShortcutRow keys="Ctrl B" label="Trigger build on the current configuration" />
              <ShortcutRow keys="Esc"    label="Stop running build / dismiss modal" />
            </div>
          </div>
        </Sec>

        <Sec title="About">
          <div className="card row" style={{ gap: 16 }}>
            <div style={{
              width: 44, height: 44, borderRadius: 10,
              background: "linear-gradient(155deg, var(--acc), var(--acc-dim))",
              display: "grid", placeItems: "center",
              color: "var(--n-0)", fontFamily: "var(--f-disp)", fontWeight: 700, fontSize: 20,
            }}>W</div>
            <div style={{ flex: 1 }}>
              <div className="h2">Washmachine</div>
              <div className="mono" style={{ fontSize: 11, color: "var(--n-7)", marginTop: 2 }}>
                v{i.version || "—"} · loader builder
              </div>
              <div className="sub" style={{ marginTop: 8 }}>
                For authorized security research only. Build and backdoor sessions are logged under the application's <span className="mono" style={{ color: "var(--n-9)" }}>logging</span> directory.
              </div>
            </div>
          </div>
        </Sec>
      </div>
    </Shell>
  );
}

function ShortcutRow({ keys, label }) {
  const parts = keys.split(" ");
  return (
    <div className="row" style={{ padding: "6px 0", justifyContent: "space-between" }}>
      <span style={{ color: "var(--n-9)", fontSize: 13 }}>{label}</span>
      <span style={{ display: "flex", gap: 4 }}>
        {parts.map((k, i) => <span key={i} className="kbd">{k}</span>)}
      </span>
    </div>
  );
}

window.FrameSettings = FrameSettings;
