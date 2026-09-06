// Washmachine — web shell entry
// Router, native bridge, and the small global delegate used for shell-level
// navigation and Build / Dry run / Stop actions.
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

  function notify(message, kind) {
    if (!message) return;
    window.dispatchEvent(new CustomEvent('wash:notice', {
      detail: { message: String(message), kind: kind || 'err' },
    }));
  }

  function invoke(method, args) {
    return new Promise((resolve, reject) => {
      if (!window.chrome || !window.chrome.webview) {
        return reject(new Error('Native bridge unavailable (running outside WebView2 host)'));
      }
      const id = ++_seq;
      const timeoutMs = method === 'build'
        ? 30 * 60 * 1000
        : method === 'browse-file' || method === 'browse-folder'
          ? 10 * 60 * 1000
          : 60 * 1000;
      const timer = setTimeout(() => {
        if (!_pending.delete(id)) return;
        const error = new Error(`${method} timed out waiting for the native host.`);
        notify(error.message, 'err');
        reject(error);
      }, timeoutMs);
      _pending.set(id, { resolve, reject, timer });
      window.chrome.webview.postMessage(JSON.stringify({ id, method, args: args || {} }));
    });
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (ev) => {
      let msg;
      try { msg = typeof ev.data === 'string' ? JSON.parse(ev.data) : ev.data; }
      catch { return; }

      if (msg.id && _pending.has(msg.id)) {
        const { resolve, reject, timer } = _pending.get(msg.id);
        _pending.delete(msg.id);
        clearTimeout(timer);
        if (msg.error) {
          notify(msg.error, 'err');
          reject(new Error(msg.error));
        }
        else resolve(msg.result);
        return;
      }

      if (msg.event) {
        window.dispatchEvent(new CustomEvent('wash:' + msg.event, { detail: msg.payload }));
      }
    });
  }

  window.wash = { invoke, notify };

  async function runRecipe(method) {
    setRoute('compile');
    try {
      const result = await invoke(method, window.washState ? window.washState.getRecipe() : {});
      if (result && result.ok === false) {
        const details = Array.isArray(result.errors) && result.errors.length
          ? result.errors.join(' ')
          : (result.message || result.error || `${method} failed.`);
        notify(details, 'err');
      } else if (method === 'dry-run' && result && result.ok) {
        notify('Recipe validation passed.', 'ok');
      }
      return result;
    } catch {
      return null;
    }
  }

  /* ═════════════════════════════ Routing ═════════════════════════════
     Map of rail item id → frame component. Frame names match window globals. */

  const ROUTES = {
    payload:     () => window.FrameWorkspace,
    scanner:     () => window.FramePeScanner,
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
     Shell-level navigation and run actions survive React frame re-renders. */

  document.addEventListener('click', handleGlobalClick, true);

  function handleGlobalClick(ev) {
    const t = ev.target;

    // 1) Rail item → route navigation
    const railItem = climb(t, '.rail-item');
    if (railItem && !railItem.dataset.wired) {
      // Map by visible label text
      const label = textOf(railItem).toLowerCase();
      const id = ({
        'payload': 'payload', 'pe scanner': 'scanner', 'backdooring': 'backdooring', 'packing': 'packing',
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
          'sgn':      'encode',
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
      if (txt.includes('build')   && !btn.disabled) { ev.stopPropagation(); runRecipe('build'); return; }
      if (txt.includes('stop'))                     { ev.stopPropagation(); invoke('stop', {}).catch(noop); return; }
      if (txt === 'dry run' || txt.startsWith('dry')) { ev.stopPropagation(); runRecipe('dry-run'); return; }
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

  /* ═════════════════════════════ Keyboard shortcuts ═════════════════════════════ */

  window.addEventListener('keydown', (ev) => {
    // Ignore when an input/textbox has focus and the key isn't a global shortcut
    const inForm = ev.target.tagName === 'INPUT' || ev.target.tagName === 'TEXTAREA';

    if (ev.ctrlKey && ev.key.toLowerCase() === 'b') {
      ev.preventDefault();
      if (_route === 'scanner') return;
      runRecipe('build');
    } else if (ev.key === 'Escape' && !inForm) {
      if (_route === 'scanner') return;
      invoke('stop', {}).catch(noop);
    }
  });

  /* ═════════════════════════════ App component ═════════════════════════════ */

  function App() {
    const [route, setLocalRoute] = React.useState(_route);
    React.useEffect(() => {
      const unsubscribe = onRoute(setLocalRoute);
      setLocalRoute(_route);
      return unsubscribe;
    }, []);

    const Frame =
      (ROUTES[route] && ROUTES[route]()) ||
      (SUBROUTES[route] && SUBROUTES[route]()) ||
      window.FrameWorkspace;

    return e(React.Fragment, null, e(Frame, null), e(window.NoticeHost));
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
    notify,
  };
})();
