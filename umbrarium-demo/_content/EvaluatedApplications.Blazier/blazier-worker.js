// EvaluatedApplications.Blazier: the generic worker script (module worker). Boots a second .NET runtime from the
// page's own _framework (or a dedicated bundle) and relays messages to Blazier.Workers.WorkerEntry.
// Order matters: the runtime is created FIRST and the message listener attached AFTER it. While dotnet.js boots in a
// worker its glue owns self.onmessage, so a handler set earlier is silently lost and the worker hangs. The page
// queues every message until this script posts `ready`.
const q = new URL(self.location.href).searchParams;
const post = (m) => self.postMessage(m);
const t0 = performance.now();
let entry = null;

try {
  const { dotnet } = await import(q.get('dotnet'));
  const t1 = performance.now();
  let b = dotnet.withDiagnosticTracing(false);
  const env = JSON.parse(q.get('env') || '{}');
  for (const k of Object.keys(env)) b = b.withEnvironmentVariable(k, env[k]);
  b = b.withModuleConfig({
    onAbort: (reason) => post({ k: 'abort', id: 0, j: String(reason) }),
    onExit: (code) => post({ k: 'abort', id: 0, j: 'runtime exited with code ' + code }),
  });
  const api = await b.create();
  const t2 = performance.now();
  api.setModuleImports('blazier-worker', { emit: (kind, id, json) => post({ k: kind, id, j: json }) });
  let exports;
  try { exports = await api.getAssemblyExports('Blazier'); } catch (e) { exports = await api.getAssemblyExports('Blazier.dll'); }
  entry = exports.Blazier.Workers.WorkerEntry;
  const err = entry.Boot(q.get('entry'));
  if (err) throw new Error('Blazier worker boot: ' + err);
  self.addEventListener('message', (e) => {
    const m = e.data;
    if (m.k === 'call') entry.Call(m.id, m.s, m.m, m.j == null ? '[]' : m.j, m.n | 0);
    else if (m.k === 'cancel') entry.Cancel(m.id);
    else if (m.k === 'ack') entry.Ack(m.id, m.n | 0);
  });
  const heap = api.Module && api.Module.HEAPU8 ? api.Module.HEAPU8.length / 1048576 : 0;
  post({ k: 'ready', t: { importMs: Math.round(t1 - t0), createMs: Math.round(t2 - t1), bootMs: Math.round(performance.now() - t2), heapMB: heap } });
} catch (err) {
  post({ k: 'initfail', id: 0, j: String((err && err.stack) || err) });
}
