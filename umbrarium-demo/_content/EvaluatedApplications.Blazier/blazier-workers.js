// EvaluatedApplications.Blazier: page-side worker host (ES module, imported by Blazier.Workers via JSHost.ImportAsync).
// Owns the Worker objects. Messages posted before the worker reports `ready` are queued: the worker attaches its
// message listener only after its .NET runtime exists (a listener set earlier is lost while the runtime boots).
const hosts = new Map();
let seq = 0;

export function resolveUrl(url) { return new URL(url, document.baseURI).href; }

function isDotnetJs(name) {
  const file = name.split('?')[0].substring(name.lastIndexOf('/') + 1);
  return /^dotnet(\.[a-z0-9]+)?\.js$/i.test(file) && !/^dotnet\.(native|runtime)\b/i.test(file);
}

// The page's own dotnet.js: the URL it was really loaded from (honours any loadBootResource redirect), else the
// import map entry (module workers do not read the page's import map, so the worker needs an absolute URL).
export function resolveDotnetUrl(frameworkBase) {
  let mapped = null;
  const el = document.querySelector('script[type="importmap"]');
  if (el) { try { mapped = JSON.parse(el.textContent).imports['./_framework/dotnet.js'] || null; } catch (e) { } }
  if (frameworkBase) {
    const file = mapped ? mapped.substring(mapped.lastIndexOf('/') + 1) : 'dotnet.js';
    return new URL(frameworkBase + file, document.baseURI).href;
  }
  const loaded = performance.getEntriesByType('resource').map(r => r.name).find(isDotnetJs);
  if (loaded) return loaded;
  return new URL(mapped || './_framework/dotnet.js', document.baseURI).href;
}

export function create(workerUrl, dotnetUrl, entry, envJson, onMessage) {
  const id = ++seq;
  const u = new URL(workerUrl);
  u.searchParams.set('dotnet', dotnetUrl);
  u.searchParams.set('entry', entry);
  u.searchParams.set('env', envJson || '{}');
  const w = new Worker(u.href, { type: 'module', name: 'blazier-' + id });
  const h = { w, ready: false, queue: [] };
  w.onmessage = (e) => {
    const m = e.data;
    if (m.k === 'ready') {
      h.ready = true;
      for (const q of h.queue) w.postMessage(q);
      h.queue.length = 0;
      onMessage('ready', 0, JSON.stringify(m.t || {}));
      return;
    }
    onMessage(m.k, m.id | 0, m.j == null ? null : String(m.j));
  };
  w.onerror = (e) => {
    if (e && e.preventDefault) e.preventDefault();
    onMessage('abort', 0, String((e && e.message) || 'worker error'));
  };
  hosts.set(id, h);
  return id;
}

export function post(host, kind, id, service, method, json, n) {
  const h = hosts.get(host);
  if (!h) return;
  const m = { k: kind, id, s: service, m: method, j: json, n };
  if (h.ready) h.w.postMessage(m); else h.queue.push(m);
}

export function terminate(host) {
  const h = hosts.get(host);
  if (!h) return;
  h.w.terminate();
  hosts.delete(host);
}
