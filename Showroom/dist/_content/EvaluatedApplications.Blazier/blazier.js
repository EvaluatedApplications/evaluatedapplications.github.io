/* EvaluatedApplications.Blazier loader: boots Blazor WebAssembly on intent and mounts named islands.
   Classic script, no dependencies. Include it with `defer`; it does nothing until an island's trigger fires.
   Markup:  <div data-blazier-island="counter" data-blazier-start="click|visible|idle|immediate"
                 data-blazier-params='{"start":5}'> static shell </div>
   Islands are registered in C# with builder.UseBlazier(b => b.Island<Counter>("counter")). */
(function () {
  'use strict';
  if (window.Blazier && window.Blazier.__loader) return;

  var me = document.currentScript;
  var cfg = {
    blazorScript: (me && me.getAttribute('data-blazor-script')) || null,
    frameworkBase: (me && me.getAttribute('data-framework-base')) || null,
    loadBootResource: null,       // optional passthrough, same contract as Blazor's
    startOptions: null,           // extra Blazor.start options
    readyTimeoutMs: 15000,        // after this, mount falls back to retrying add() without the handshake
    addRetryMs: 50,
    addRetryLimit: 200
  };
  var timing = { intent: null, scriptRequested: null, scriptLoaded: null, runtimeStarted: null, registered: null, mounted: {} , addAttempts: {} };
  var registered = new Map();     // island name -> parameter info Blazor passed to the initializer
  var readyResolve, ready = new Promise(function (r) { readyResolve = r; });
  var mountWaiters = new Map();   // mount token -> resolve (lazy islands report through __mounted)
  var started = null, tokenSeq = 0;
  var progress = { requested: 0, loaded: 0, bytes: 0 };

  function mark(name) { try { performance.mark('blazier:' + name); } catch (e) { } return performance.now(); }
  function emit(type, detail) { document.dispatchEvent(new CustomEvent('blazier:' + type, { detail: detail })); }
  function readImportMap() {
    var el = document.querySelector('script[type="importmap"]');
    if (!el) return null;
    try { return JSON.parse(el.textContent).imports || null; } catch (e) { return null; }
  }
  function resolveBlazorScript() {
    if (cfg.blazorScript) return cfg.blazorScript;
    var map = readImportMap();
    var m = map && map['./_framework/blazor.webassembly.js'];
    return m || '_framework/blazor.webassembly.js';
  }
  function fileName(url) { return url.substring(url.lastIndexOf('/') + 1); }

  function watchResources() {
    if (!('PerformanceObserver' in window)) return;
    try {
      new PerformanceObserver(function (list) {
        list.getEntries().forEach(function (e) {
          if (e.name.indexOf('/_framework/') < 0) return;
          progress.loaded++; progress.bytes += e.transferSize || 0;
          emit('progress', { requested: progress.requested, loaded: progress.loaded, bytes: progress.bytes });
        });
      }).observe({ type: 'resource', buffered: false });
    } catch (e) { }
  }

  function loadBootResource(type, name, defaultUri, integrity, behavior) {
    progress.requested++;
    if (cfg.loadBootResource) {
      var r = cfg.loadBootResource(type, name, defaultUri, integrity, behavior);
      if (r !== undefined && r !== null) return r;
    }
    var fw = cfg.frameworkBase;
    if (fw && type !== 'manifest') {
      var file = name;
      if (name === 'dotnet.js') {
        var map = readImportMap(), mapped = map && map['./_framework/dotnet.js'];
        if (mapped) file = fileName(mapped);
      }
      return fw + file;
    }
    return undefined;
  }

  function loadScript(src) {
    return new Promise(function (resolve, reject) {
      var s = document.createElement('script');
      s.src = src; s.setAttribute('autostart', 'false');
      s.onload = resolve;
      s.onerror = function () { reject(new Error('could not load ' + src)); };
      document.head.appendChild(s);
    });
  }

  /** Boots the runtime once. Safe to call any number of times. */
  function start() {
    if (started) return started;
    timing.intent = timing.intent || mark('intent');
    watchResources();
    started = (async function () {
      if (!window.Blazor || typeof window.Blazor.start !== 'function') {
        timing.scriptRequested = mark('script-requested');
        await loadScript(resolveBlazorScript());
        timing.scriptLoaded = mark('script-loaded');
      }
      var opts = Object.assign({}, cfg.startOptions || {}, { loadBootResource: loadBootResource });
      await window.Blazor.start(opts);
      timing.runtimeStarted = mark('runtime-started');
      emit('started', { ms: timing.runtimeStarted - timing.intent });
    })();
    started.catch(function (err) { emit('error', { stage: 'start', message: String(err && err.message || err) }); });
    return started;
  }

  function withTimeout(p, ms) {
    return Promise.race([p, new Promise(function (r) { setTimeout(function () { r('timeout'); }, ms); })]);
  }

  function isNotEnabled(err) { return /Dynamic root components have not been enabled/.test(String(err && err.message || err)); }

  async function addWithRetry(target, name, params) {
    for (var i = 0; ; i++) {
      try {
        var c = await window.Blazor.rootComponents.add(target, name, params);
        timing.addAttempts[name] = i + 1;
        return c;
      } catch (err) {
        if (!isNotEnabled(err) || i >= cfg.addRetryLimit) throw err;
        await new Promise(function (r) { setTimeout(r, cfg.addRetryMs); });
      }
    }
  }

  function hasParam(info, name) {
    if (!info) return false;
    for (var i = 0; i < info.length; i++) if (info[i] && info[i].name === name) return true;
    return false;
  }

  function setState(el, state) {
    el.setAttribute('data-blazier-state', state);
    if (state === 'booting' || state === 'mounting') el.setAttribute('aria-busy', 'true'); else el.removeAttribute('aria-busy');
  }

  /** Mounts island `name` over element `el` (the static shell stays visible until the component has rendered). */
  async function mount(el, name, params) {
    if (typeof el === 'string') el = document.querySelector(el);
    if (!el) throw new Error('Blazier: mount target not found');
    if (el.__blazier) return el.__blazier;
    var job = (async function () {
      setState(el, 'booting');
      await start();
      setState(el, 'mounting');
      var r = await withTimeout(ready, cfg.readyTimeoutMs);
      if (r !== 'timeout' && !registered.has(name))
        throw new Error("Blazier: no island named '" + name + "' (register it with UseBlazier(b => b.Island<T>(\"" + name + "\")))");
      var info = registered.get(name);
      var user = params || {};
      var p = user, token = null, waitMounted = null;
      if (hasParam(info, 'BlazierIsland')) {           // a lazy island: the host loads assemblies, then reports back
        token = 'm' + (++tokenSeq);
        waitMounted = new Promise(function (res) { mountWaiters.set(token, res); });
        p = { BlazierIsland: name, BlazierMount: token, BlazierParams: Object.keys(user).length ? JSON.stringify(user) : null };
      }
      var shadow = document.createElement(el.tagName);
      shadow.hidden = true;
      el.parentNode.insertBefore(shadow, el.nextSibling);
      var component = await addWithRetry(shadow, name, p);
      if (waitMounted) await waitMounted;
      for (var i = 0; i < el.attributes.length; i++) {
        var a = el.attributes[i];
        if (a.name.indexOf('data-blazier-') !== 0 && a.name !== 'aria-busy' && a.name !== 'hidden') shadow.setAttribute(a.name, a.value);
      }
      shadow.setAttribute('data-blazier-island', name);
      el.replaceWith(shadow);
      shadow.hidden = false;
      setState(shadow, 'live');
      timing.mounted[name] = mark('mounted:' + name);
      emit('mounted', { name: name, element: shadow, ms: timing.mounted[name] - (timing.intent || 0) });
      shadow.__blazier = job;
      return component;
    })();
    el.__blazier = job;
    job.catch(function (err) {
      setState(el, 'failed');
      emit('error', { stage: 'mount', island: name, message: String(err && err.message || err) });
      var slot = el.querySelector('[data-blazier-error]');
      if (slot) { slot.hidden = false; return; }
      var p = document.createElement('p');
      p.className = 'blazier-error'; p.setAttribute('role', 'alert');
      p.textContent = 'This part of the page could not start. ';
      var b = document.createElement('button'); b.type = 'button'; b.textContent = 'Reload';
      b.onclick = function () { location.reload(); };
      p.appendChild(b); el.appendChild(p);
    });
    return job;
  }

  function parseParams(el) {
    var raw = el.getAttribute('data-blazier-params');
    if (!raw) return {};
    try { return JSON.parse(raw); } catch (e) { console.error('Blazier: bad data-blazier-params on', el, e); return {}; }
  }

  function arm(el) {
    if (el.__blazierArmed) return;
    el.__blazierArmed = true;
    var name = el.getAttribute('data-blazier-island');
    var mode = (el.getAttribute('data-blazier-start') || 'click').toLowerCase();
    setState(el, 'idle');
    var fire = function () { if (!timing.intent) timing.intent = mark('intent'); mount(el, name, parseParams(el)); };
    if (mode === 'immediate') return fire();
    if (mode === 'idle') {
      var ric = window.requestIdleCallback || function (f) { return setTimeout(f, 1); };
      return ric(fire, { timeout: 2000 });
    }
    if (mode === 'visible' && 'IntersectionObserver' in window) {
      var io = new IntersectionObserver(function (entries) {
        if (entries.some(function (e) { return e.isIntersecting; })) { io.disconnect(); fire(); }
      }, { rootMargin: '200px' });
      return io.observe(el);
    }
    // click (and visible without IntersectionObserver): the first real interaction inside the island
    var events = ['pointerdown', 'keydown', 'focusin', 'touchstart'];
    var once = function () { events.forEach(function (t) { el.removeEventListener(t, once, true); }); fire(); };
    events.forEach(function (t) { el.addEventListener(t, once, { capture: true, passive: true }); });
  }

  function scan(root) {
    (root || document).querySelectorAll('[data-blazier-island]').forEach(function (el) {
      if (!el.getAttribute('data-blazier-state')) arm(el);
    });
  }

  window.Blazier = {
    __loader: true,
    timing: timing,
    progress: progress,
    configure: function (o) { Object.assign(cfg, o || {}); return cfg; },
    start: start,
    mount: mount,
    scan: scan,
    get registered() { return Array.from(registered.keys()); },
    // Called by Blazor (RegisterForJavaScript initializer) once per island, right after dynamic roots are enabled.
    __registered: function (name, parameterInfo) {
      if (!registered.size) { timing.registered = mark('registered'); queueMicrotask(function () { readyResolve('ok'); }); }
      registered.set(name, parameterInfo);
    },
    // Called by LazyIslandHost once the lazy component (or its error) has rendered.
    __mounted: function (token) { var r = mountWaiters.get(token); if (r) { mountWaiters.delete(token); r(); } }
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { scan(); });
  else scan();
})();
