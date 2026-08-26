// Global state store for Washmachine web shell.
//
// Two-tier model:
//   recipe[k]      — the build configuration (paths, selections, toggles).
//                    Mirrors UiDataKeys (Washmachine.Core/Models/UiDataKeys.cs).
//                    This is what gets serialized + sent to the C# bridge for
//                    a compile. Keys match the C# side exactly so the bridge
//                    can pass them straight to UiData without translation.
//   meta[k]        — derived/cached data (catalogs from backend, current
//                    session, analysis results, live build progress).
//                    Not sent to the compiler; just powers the UI.
//
// Pub/sub: subscribe(field, cb) → cb(value) on change, returns unsubscribe.
//          useField(field) → React hook for use inside components.
//
// No external deps. Plain JS for fast cold start.

(function () {
  'use strict';

  const RECIPE_DEFAULTS = {
    // ── Source ──────────────────────────────────────────────────────────
    sourceKind:               'file',       // 'file' | 'raw' | 'url' | 'generic'
    shellcodeFileInput:       '',
    shellcodeRawInput:        '',
    shellcodeUrlValue:        '',
    shellcodeUrlFileInput:    '',
    genericShellcodeCombo:    '',
    peStripModeCombo:         'ep',
    peStripSectionInput:      '.text',
    peStripTrim:              'True',       // CompilerService reads as bool.TrueString
    donutArchCombo:           '3',
    donutClassInput:          '',
    donutMethodInput:         '',
    donutParamsInput:         '',

    // ── SGN ─────────────────────────────────────────────────────────────
    shikataGaNaiEnabledCheckBox:    'False',
    shikataGaNaiEncodeCountInput:   '1',
    shikataGaNaiMaxBytesInput:      '50',
    shikataGaNaiPlacement:          'pre',  // 'pre' | 'post'

    // ── Encode (Bin2Shell) ──────────────────────────────────────────────
    encoderCombo:             '',           // numeric index (string)
    envelopeCombo:             '',           // numeric index (string)

    // ── Template + playbook ─────────────────────────────────────────────
    activePlaybook:           '',
    templateCombo:            '',
    snippetSelections:        {},           // { sectionTemplate: [itemId, ...] }
    snippetInputs:            {},           // { "section.item.input": value }

    // ── Compile ─────────────────────────────────────────────────────────
    compilerCombo:            '',
    compilationBackend:       'Deterministic', // or 'LlvmObfuscated'
    llvmObfuscationPasses:    [],
    OutputPath:               '',
    OpenFolderAfterCompile:   'True',
    GenerateDebugInfo:        'False',
    StripToBinCheck:          'False',
    VerboseBuildCheck:        'False',

    // ── Backdoor ────────────────────────────────────────────────────────
    EnableBackdooringToggle:  'False',
    TargetPePath:             '',
    InjectionMethodCombo:     'code-cave', // code-cave | new-section | section-ext | text-pad | tls-callback
    CarrierInvokeCombo:       'entry-point',
    PreserveEntryCheck:       'True',
    PatchIatCheck:            'True',
    RemoveSignatureCheck:     'True',
    PatchSubsystemCheck:      'True',
    PatchExitCheck:           'True',
    DryRunCheck:              'False',
    SectionNameInput:         '.extra',
    CaveMinSizeBox:           '64',

    // ── Pack ────────────────────────────────────────────────────────────
    EnablePackingToggle:      'False',
    PackerCombo:              'None',

    // ── Finalize ────────────────────────────────────────────────────────
    EnableFinalizeToggle:     'False',
    DonorPathInput:           '',
    CloneIcon:                'True',
    CloneVersionInfo:         'True',
    CloneManifest:            'True',
    CloneRsrc:                'True',
    CloneAuthenticode:        'False',
    CloneOriginalFilename:    'False',
    NopPaddingInput:          '0',
    NopPattern:               'nop',
    AppendLocation:           'overlay',
  };

  const META_DEFAULTS = {
    // catalogs (loaded once at startup)
    catalog_encoders:   [],        // [{ index, name, description }]
    catalog_envelopes:  [],
    catalog_templates:  [],        // [{ id, display, description }]
    catalog_snippets:   [],        // [{ header, template, display, items: [...] }]
    catalog_compilers:  [],        // [{ kind, path, version }]
    catalog_playbooks:  [],

    // current shellcode analysis (computed on file change)
    analysis_size:      null,
    analysis_sizeText:  '—',
    analysis_arch:      '—',
    analysis_entropy:   '—',
    analysis_sha256:    '—',
    analysis_bytesPreview: '',

    // current PE target analysis
    pe_filename:        '',
    pe_arch:            '',
    pe_type:            '',
    pe_sections:        0,
    pe_entry:           '—',
    pe_size:            '—',
    pe_entropy:         '—',
    pe_caves:           [],
    pe_imports:         [],

    // build state
    build_running:      false,
    build_lastResult:   null,      // CompilerResult JSON
    build_log:          [],        // accumulator (capped)
    build_lastSession:  '',

    // history
    history_sessions:   [],        // populated by history-list

    // app info
    app_info:           null,      // populated by app-info
  };

  // Persisted recipe keys (saved to localStorage so user state survives refresh)
  const PERSISTED = new Set([
    'sourceKind', 'shellcodeFileInput', 'peStripModeCombo', 'peStripSectionInput',
    'donutArchCombo', 'donutClassInput', 'donutMethodInput', 'donutParamsInput',
    'shikataGaNaiEnabledCheckBox', 'shikataGaNaiEncodeCountInput', 'shikataGaNaiMaxBytesInput', 'shikataGaNaiPlacement',
    'encoderCombo', 'envelopeCombo',
    'activePlaybook', 'templateCombo', 'snippetSelections', 'snippetInputs',
    'compilerCombo', 'compilationBackend', 'llvmObfuscationPasses', 'OutputPath',
    'OpenFolderAfterCompile', 'GenerateDebugInfo', 'StripToBinCheck', 'VerboseBuildCheck',
    'EnableBackdooringToggle', 'TargetPePath', 'InjectionMethodCombo', 'CarrierInvokeCombo',
    'PreserveEntryCheck', 'PatchIatCheck', 'RemoveSignatureCheck', 'PatchSubsystemCheck', 'PatchExitCheck',
    'DryRunCheck', 'SectionNameInput', 'CaveMinSizeBox',
    'EnablePackingToggle', 'PackerCombo',
    'EnableFinalizeToggle', 'DonorPathInput', 'CloneIcon', 'CloneVersionInfo',
    'CloneManifest', 'CloneRsrc', 'CloneAuthenticode', 'CloneOriginalFilename',
    'NopPaddingInput', 'NopPattern', 'AppendLocation',
  ]);
  const PERSIST_KEY = 'washmachine:recipe:v1';

  // ── Store ──────────────────────────────────────────────────────────────

  const recipe = Object.assign({}, RECIPE_DEFAULTS);
  const meta   = Object.assign({}, META_DEFAULTS);

  const listeners = new Map();   // field → Set<cb>

  function notify(field, value) {
    const subs = listeners.get(field);
    if (subs) subs.forEach(fn => { try { fn(value); } catch (e) { console.error(e); } });
    // Wildcard listeners on '*' fire for every change
    const wild = listeners.get('*');
    if (wild) wild.forEach(fn => { try { fn(field, value); } catch (e) { console.error(e); } });
  }

  function get(field) {
    if (Object.prototype.hasOwnProperty.call(recipe, field)) return recipe[field];
    if (Object.prototype.hasOwnProperty.call(meta, field))   return meta[field];
    return undefined;
  }

  function set(field, value) {
    let target;
    if (Object.prototype.hasOwnProperty.call(recipe, field))      target = recipe;
    else if (Object.prototype.hasOwnProperty.call(meta, field))   target = meta;
    else target = recipe; // accept new recipe keys silently

    if (target[field] === value) return;  // no-op
    target[field] = value;
    if (target === recipe && PERSISTED.has(field)) persist();
    notify(field, value);
  }

  function update(patch) {
    if (!patch || typeof patch !== 'object') return;
    let dirtyRecipe = false;
    for (const k of Object.keys(patch)) {
      const v = patch[k];
      const target = Object.prototype.hasOwnProperty.call(recipe, k) ? recipe :
                     Object.prototype.hasOwnProperty.call(meta, k)   ? meta   : recipe;
      if (target[k] === v) continue;
      target[k] = v;
      if (target === recipe && PERSISTED.has(k)) dirtyRecipe = true;
      notify(k, v);
    }
    if (dirtyRecipe) persist();
  }

  function subscribe(field, cb) {
    if (!listeners.has(field)) listeners.set(field, new Set());
    listeners.get(field).add(cb);
    return () => { const s = listeners.get(field); if (s) s.delete(cb); };
  }

  function snapshot() {
    return { recipe: Object.assign({}, recipe), meta: Object.assign({}, meta) };
  }

  function getRecipe() {
    // Deep-ish copy so consumers can't mutate
    return JSON.parse(JSON.stringify(recipe));
  }

  function reset() {
    Object.assign(recipe, RECIPE_DEFAULTS);
    Object.assign(meta,   META_DEFAULTS);
    try { localStorage.removeItem(PERSIST_KEY); } catch {}
    notify('*', null);
  }

  // ── Persistence ─────────────────────────────────────────────────────────

  function persist() {
    try {
      const persisted = {};
      for (const k of PERSISTED) persisted[k] = recipe[k];
      localStorage.setItem(PERSIST_KEY, JSON.stringify(persisted));
    } catch (e) {
      // localStorage may be unavailable; silent fail is fine.
    }
  }

  function hydrate() {
    try {
      const raw = localStorage.getItem(PERSIST_KEY);
      if (!raw) return;
      const obj = JSON.parse(raw);
      if (obj && typeof obj === 'object') {
        for (const k of Object.keys(obj)) {
          if (PERSISTED.has(k)) recipe[k] = obj[k];
        }
      }
    } catch {}
  }
  hydrate();

  // ── React helpers ───────────────────────────────────────────────────────

  function useField(field) {
    const initial = get(field);
    const [v, setV] = React.useState(initial);
    React.useEffect(() => subscribe(field, setV), [field]);
    return [v, (newVal) => set(field, newVal)];
  }

  function useFields(...fields) {
    const init = {}; fields.forEach(f => { init[f] = get(f); });
    const [v, setV] = React.useState(init);
    React.useEffect(() => {
      const unsubs = fields.map(f => subscribe(f, () => {
        setV(prev => {
          const next = {}; fields.forEach(k => { next[k] = get(k); });
          return next;
        });
      }));
      return () => unsubs.forEach(u => u && u());
    }, fields);
    return v;
  }

  // ── Bootstrap catalogs from the native bridge ───────────────────────────

  async function loadCatalogs() {
    if (!window.wash || !window.wash.invoke) return;
    try {
      const [encs, envs, comps, snips, info, pbs] = await Promise.all([
        window.wash.invoke('catalog-encoders',  {}).catch(_ => ({ items: [] })),
        window.wash.invoke('catalog-envelopes', {}).catch(_ => ({ items: [] })),
        window.wash.invoke('catalog-compilers', {}).catch(_ => ({ items: [] })),
        window.wash.invoke('catalog-snippets',  {}).catch(_ => ({ templates: [], sections: [] })),
        window.wash.invoke('app-info',          {}).catch(_ => ({})),
        window.wash.invoke('list-playbooks',    {}).catch(_ => ({ active: '', files: [] })),
      ]);
      update({
        catalog_encoders:  encs.items || [],
        catalog_envelopes: envs.items || [],
        catalog_compilers: comps.items || [],
        catalog_templates: snips.templates || [],
        catalog_snippets:  snips.sections  || [],
        catalog_playbooks: pbs.files || [],
        app_info:          info || null,
        activePlaybook:    recipe.activePlaybook || pbs.active || '',
      });

      // Auto-pick sensible defaults if nothing chosen yet
      if (!recipe.encoderCombo && (encs.items || []).length > 1) {
        // index 1 is the first non-"none" encoder
        set('encoderCombo', String((encs.items[1] || encs.items[0]).index));
      }
      if (!recipe.envelopeCombo && (envs.items || []).length > 1) {
        set('envelopeCombo', String((envs.items[1] || envs.items[0]).index));
      }
      if (!recipe.templateCombo && (snips.templates || []).length > 0) {
        set('templateCombo', snips.templates[0].id);
      }
      if (!recipe.compilerCombo && (comps.items || []).length > 0) {
        set('compilerCombo', comps.items[0].path || comps.items[0].kind || '');
      }
    } catch (e) {
      console.error('loadCatalogs:', e);
    }
  }

  // ── Build log event handling (server-pushed) ────────────────────────────

  window.addEventListener('wash:build-log', (ev) => {
    const line = ev.detail && ev.detail.line ? ev.detail.line : '';
    if (!line) return;
    const log = meta.build_log.slice();
    log.push({ ts: Date.now(), line });
    // Cap at 2000 lines to avoid unbounded growth
    while (log.length > 2000) log.shift();
    meta.build_log = log;
    notify('build_log', log);
  });

  window.addEventListener('wash:build-started', (ev) => {
    meta.build_log = [];
    meta.build_running = true;
    notify('build_running', true);
    notify('build_log', []);
  });

  window.addEventListener('wash:build-finished', (ev) => {
    meta.build_running = false;
    meta.build_lastResult = ev.detail || null;
    notify('build_running', false);
    notify('build_lastResult', meta.build_lastResult);
  });

  // ── Public API ──────────────────────────────────────────────────────────

  window.washState = {
    get, set, update, subscribe, snapshot, getRecipe, reset,
    useField, useFields,
    loadCatalogs,
  };
})();
