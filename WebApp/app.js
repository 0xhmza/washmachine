// Washmachine — web shell entry
// Router + rail interactivity + native bridge + global click delegate that
// makes the design's static mockup components feel alive (toggles, selections,
// browse buttons, preview tabs, segmented controls).
//
// Runs after shell.js + frames have populated `window.*`.

(function () {
  'use strict';

  const e = React.createElement;

  /* ═════════════════════════════ Native bridge ═════════════════════════════
     window.chrome.webview is injected by WebView2. We expose a tiny RPC wrapper
     as window.wash that returns promises. Messages from C# arrive as JSON via
     `addEventListener("message", ...)`. */

  const _pending = new Map();
  let _seq = 0;

  function invoke(method, args) {
    return new Promise((resolve, reject) => {
      if (!window.chrome || !window.chrome.webview) {
        return reject(new Error('Native bridge unavailable (running outside WebView2 host)'));
      }
      const id = ++_seq;
      _pending.set(id, { resolve, reject });
      window.chrome.webview.postMessage(JSON.stringify({ id, method, args: args || {} }));
    });
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (ev) => {
      let msg;
      try { msg = typeof ev.data === 'string' ? JSON.parse(ev.data) : ev.data; }
      catch { return; }

      if (msg.id && _pending.has(msg.id)) {
        const { resolve, reject } = _pending.get(msg.id);
        _pending.delete(msg.id);
        if (msg.error) reject(new Error(msg.error));
        else resolve(msg.result);
        return;
      }

      if (msg.event) {
        window.dispatchEvent(new CustomEvent('wash:' + msg.event, { detail: msg.payload }));
      }
    });
  }

  window.wash = { invoke };

  /* ═════════════════════════════ Routing ═════════════════════════════
     Map of rail item id → frame component. Frame names match window globals. */

  const ROUTES = {
    payload:     () => window.FrameWorkspace,
    backdooring: () => window.FrameBackdoor,
    packing:     () => window.FramePacking,
    finalize:    () => window.FrameFinalize,
    compile:     () => window.FrameCompile,
    pipeline:    () => window.FramePipeline,
    history:     () => window.FrameHistory,
    settings:    () => window.FrameSettings,
  };

  const SUBROUTES = {
    encode:   () => window.FrameEncode,
    template: () => window.FrameTemplate,
    startup:  () => window.FrameStartup,
    wizard:   () => window.FrameWebWizard,
  };

  let _route = 'payload';
  const _routeListeners = new Set();

  function setRoute(r) {
    if (!ROUTES[r] && !SUBROUTES[r]) return;
    if (r === _route) return;
    _route = r;
    _routeListeners.forEach(fn => { try { fn(r); } catch {} });
  }

  function onRoute(fn) {
    _routeListeners.add(fn);
    return () => _routeListeners.delete(fn);
  }

  /* ═════════════════════════════ Click delegate ═════════════════════════════
     A single capture-phase listener on document.body handles all the
     interactivity the static mockup frames lack. Cheap and survives React
     re-renders without us needing to monkey-patch every component. */

  document.addEventListener('click', handleGlobalClick, true);

  function handleGlobalClick(ev) {
    const t = ev.target;

    // 1) Rail item → route navigation
    const railItem = climb(t, '.rail-item');
    if (railItem && !railItem.dataset.wired) {
      // Map by visible label text
      const label = textOf(railItem).toLowerCase();
      const id = ({
        'payload': 'payload', 'backdooring': 'backdooring', 'packing': 'packing',
        'finalize': 'finalize', 'compile': 'compile',
        'pipeline': 'pipeline', 'history': 'history', 'settings': 'settings',
      })[label];
      if (id) { ev.stopPropagation(); setRoute(id); }
      return;
    }

    // 2) Pipeline stage → route to its sub-frame
    const pipeStage = climb(t, '.pipe-stage');
    if (pipeStage) {
      const nameEl = pipeStage.querySelector('.pipe-name');
      if (nameEl) {
        const name = (nameEl.textContent || '').trim().toLowerCase();
        const mapping = {
          'source':   'payload',
          'sgn':      'payload',
          'encode':   'encode',
          'template': 'template',
          'compile':  'compile',
          'backdoor': 'backdooring',
          'pack':     'packing',
          'finalize': 'finalize',
        };
        if (mapping[name]) { ev.stopPropagation(); setRoute(mapping[name]); }
      }
      return;
    }

    // 3) Buttons in the pipeline run area + page action buttons
    const btn = climb(t, '.btn');
    if (btn) {
      const txt = textOf(btn).toLowerCase();
      // Build / Stop / Dry run
      if (txt.includes('build')   && !btn.disabled) { ev.stopPropagation(); invoke('build', window.washState ? window.washState.getRecipe() : {}).catch(noop); return; }
      if (txt.includes('stop'))                     { ev.stopPropagation(); invoke('stop', {}).catch(noop); return; }
      if (txt === 'dry run' || txt.startsWith('dry')) { ev.stopPropagation(); invoke('dry-run', window.washState ? window.washState.getRecipe() : {}).catch(noop); return; }
      // Browse → opens file picker, writes result to the nearest preceding text input
      if (txt.startsWith('browse')) {
        ev.stopPropagation();
        const card = btn.closest('.card, .modal-bd, .cfg, .pemap, .row, .field');
        const tx = findNearestTextInput(btn, card);
        const ext = guessExtensionFilter(tx);
        invoke('browse-file', { filters: ext ? [{ name: ext.name, patterns: ext.patterns }] : [] })
          .then(res => {
            if (res && res.ok && res.path && tx) {
              setReactInputValue(tx, res.path);
            }
          })
          .catch(err => console.error('browse-file:', err));
        return;
      }
      // Re-detect / Reload / Refresh
      if (txt.includes('re-detect') || txt.includes('reload') || txt.includes('refresh') || txt.includes('recompute')) {
        ev.stopPropagation();
        flashButton(btn);
        return;
      }
      // Open in editor / Reveal etc — just flash for now
      if (txt.includes('open') || txt.includes('reveal') || txt.includes('copy') || txt.includes('save')) {
        ev.stopPropagation();
        flashButton(btn);
        return;
      }
    }

    // 4) Toggle switches → flip .on state
    const tog = climb(t, '.tog');
    if (tog) {
      ev.stopPropagation();
      tog.classList.toggle('on');
      return;
    }

    // 5) List rows (templates, snippets, methods, encoder/envelope etc.)
    //    Find row + its group; clear sibling selection; mark this row.
    const row = climb(t, '.list-item, .pe-row');
    if (row) {
      const list = row.closest('.list, .pemap, .card');
      if (list) {
        list.querySelectorAll('.list-item.sel, .pe-row.tgt').forEach(el => {
          if (el !== row) {
            el.classList.remove('sel');
            el.classList.remove('tgt');
            // also reset the radio dot if any
            const dot = el.querySelector('div[style*="border-radius"]');
          }
        });
      }
      row.classList.add(row.classList.contains('pe-row') ? 'tgt' : 'sel');
      ev.stopPropagation();
      return;
    }

    // 6) Segmented control buttons inside .seg
    const segBtn = climb(t, '.seg > button');
    if (segBtn) {
      const seg = segBtn.parentElement;
      seg.querySelectorAll('button.on').forEach(b => b.classList.remove('on'));
      segBtn.classList.add('on');
      ev.stopPropagation();
      return;
    }

    // 7) Preview tabs (.ptab)
    const ptab = climb(t, '.ptab');
    if (ptab) {
      const bar = ptab.parentElement;
      bar.querySelectorAll('.ptab.on').forEach(b => b.classList.remove('on'));
      ptab.classList.add('on');
      ev.stopPropagation();
      return;
    }

    // 8) Modal close (X button at top of modal)
    if (t.closest('.modal-hd .btn.ghost') && t.closest('svg')) {
      // future: dismiss modal
      return;
    }
  }

  function climb(el, selector) {
    while (el && el !== document.body) {
      if (el.matches && el.matches(selector)) return el;
      el = el.parentElement;
    }
    return null;
  }

  function textOf(el) {
    return (el.textContent || '').trim();
  }

  function noop() {}

  function flashButton(btn) {
    btn.style.transition = 'background 120ms ease';
    const orig = btn.style.background;
    btn.style.background = 'var(--n-3)';
    setTimeout(() => { btn.style.background = orig; }, 180);
  }

  function findNearestTextInput(start, scope) {
    // Walk previous siblings + parents within scope looking for an .input or input element
    let cur = start;
    while (cur && cur !== document.body) {
      // 1) Look in previous siblings
      let sib = cur.previousElementSibling;
      while (sib) {
        const inp = sib.matches && (sib.matches('input.input') || sib.matches('input')) ? sib : sib.querySelector && sib.querySelector('input.input, input.mono, input');
        if (inp) return inp;
        sib = sib.previousElementSibling;
      }
      // 2) Check parent's earlier descendants
      if (cur.parentElement === scope) break;
      cur = cur.parentElement;
    }
    return scope ? scope.querySelector('input.input, input.mono, input') : null;
  }

  function guessExtensionFilter(input) {
    if (!input) return null;
    const ph = (input.placeholder || '').toLowerCase();
    const val = (input.value || '').toLowerCase();
    if (ph.includes('.bin') || val.endsWith('.bin')) return { name: 'Shellcode binary', patterns: ['.bin'] };
    if (ph.includes('.exe') || val.endsWith('.exe')) return { name: 'Executable', patterns: ['.exe'] };
    if (ph.includes('.dll') || val.endsWith('.dll')) return { name: 'Dynamic library', patterns: ['.dll'] };
    if (ph.includes('yaml') || val.endsWith('.yaml')) return { name: 'YAML', patterns: ['.yaml', '.yml'] };
    return null;
  }

  // React preserves input values via its own state machine. To update a value
  // and trigger any onChange, we have to call the native setter then dispatch
  // an 'input' event. (Standard React-controlled-input workaround.)
  function setReactInputValue(input, value) {
    const proto = Object.getPrototypeOf(input);
    const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
    if (setter) setter.call(input, value);
    else input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  }

  /* ═════════════════════════════ Keyboard shortcuts ═════════════════════════════ */

  window.addEventListener('keydown', (ev) => {
    // Ignore when an input/textbox has focus and the key isn't a global shortcut
    const inForm = ev.target.tagName === 'INPUT' || ev.target.tagName === 'TEXTAREA';

    if (ev.ctrlKey && ev.key.toLowerCase() === 'b') {
      ev.preventDefault();
      invoke('build', window.washState ? window.washState.getRecipe() : {}).catch(noop);
    } else if (ev.ctrlKey && ev.key.toLowerCase() === 'k') {
      ev.preventDefault();
      // future: command palette
    } else if (ev.key === 'Escape' && !inForm) {
      invoke('stop', {}).catch(noop);
    }
  });

  /* ═════════════════════════════ App component ═════════════════════════════ */

  function App() {
    const [route, setLocalRoute] = React.useState(_route);
    React.useEffect(() => onRoute(setLocalRoute), []);

    const Frame =
      (ROUTES[route] && ROUTES[route]()) ||
      (SUBROUTES[route] && SUBROUTES[route]()) ||
      window.FrameWorkspace;

    return e(Frame, null);
  }

  /* ═════════════════════════════ Bootstrap ═════════════════════════════ */

  function hideBoot() {
    const boot = document.getElementById('boot');
    if (boot) {
      boot.classList.add('hidden');
      setTimeout(() => { try { boot.remove(); } catch {} }, 400);
    }
  }

  function mount() {
    const root = document.getElementById('root');
    const r = ReactDOM.createRoot(root);
    r.render(e(App));
    // Load catalogs from the native bridge after render
    if (window.washState) window.washState.loadCatalogs();
    requestAnimationFrame(() => requestAnimationFrame(hideBoot));
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', mount, { once: true });
  } else {
    mount();
  }

  /* ═════════════════════════════ Public API ═════════════════════════════ */

  window.washmachine = {
    setRoute,
    getRoute: () => _route,
    invoke: window.wash.invoke,
  };
})();
