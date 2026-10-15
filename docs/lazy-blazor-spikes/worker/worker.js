// Research spike: the GENERIC half of a worker runtime. Written once by the package; app authors never touch it.
// The runtime is created at module top level (as Microsoft's template does) and the message listener is attached AFTER it: the
// emscripten glue in dotnet.native.js owns self.onmessage while it boots in a worker, so a handler set earlier is lost.
// The page therefore waits for {kind:'ready'} before it posts anything.
// Protocol (internal): {kind:'call', id, method, args} -> progress*/item*/done|error;  {kind:'cancel', id};  {kind:'heap'}.
const q = new URL(self.location.href).searchParams;
try {
  const t0 = performance.now();
  const { dotnet } = await import(q.get('dotnet'));
  const t1 = performance.now();
  const api = await dotnet.withDiagnosticTracing(false).create();
  const t2 = performance.now();
  api.setModuleImports('worker-host.mjs', { emit: (id, kind, json) => postMessage({ kind, id, json }) });
  const ex = (await api.getAssemblyExports(api.getConfig().mainAssemblyName))[q.get('ns') ?? 'Showroom'].Worker.WorkerEntry;
  self.addEventListener('message', async (e) => {
    const m = e.data;
    if (m.kind === 'call') {
      try { const r = await ex.Call(m.id, m.method, m.args); postMessage({ kind: 'done', id: m.id, json: r }); }
      catch (err) { postMessage({ kind: 'error', id: m.id, message: String(err && err.message || err) }); }
    } else if (m.kind === 'cancel') ex.Cancel(m.id);
    else if (m.kind === 'heap') postMessage({ kind: 'heap', heapMB: api.Module.HEAPU8.length / 1048576 });
  });
  postMessage({ kind: 'ready', importMs: Math.round(t1 - t0), createMs: Math.round(t2 - t1), exportsMs: Math.round(performance.now() - t2), heapMB: api.Module.HEAPU8.length / 1048576 });
} catch (err) { postMessage({ kind: 'initfail', message: String(err && err.stack || err) }); }