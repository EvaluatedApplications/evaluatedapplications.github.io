// Research spike: the GENERIC client half. call() returns a Promise; onProgress/onItem for streaming; AbortSignal for cancel.
export class WorkerRuntime {
  constructor(workerUrl, dotnetUrl, ns) {
    this.w = new Worker(workerUrl + '?dotnet=' + encodeURIComponent(dotnetUrl) + (ns ? '&ns=' + ns : ''), { type: 'module' }); this.n = 0; this.calls = new Map();
    this.ready = new Promise((res, rej) => { this._res = res; this._rej = rej; });
    this.w.onmessage = (e) => {
      const m = e.data;
      if (m.kind === 'ready') this._res(m); else if (m.kind === 'initfail') this._rej(new Error(m.message));
      else {
        const c = this.calls.get(m.id); if (!c) return;
        if (m.kind === 'progress') c.onProgress?.(Number(m.json)); else if (m.kind === 'item') c.onItem?.(m.json);
        else if (m.kind === 'done') { this.calls.delete(m.id); if (m.json === '\u0001canceled') c.rej(new DOMException('canceled', 'AbortError')); else c.res(m.json); } else if (m.kind === 'error') { this.calls.delete(m.id); c.rej(new Error(m.message)); }
      }
    };
  }
  init() { return this.ready; }
  call(method, args, { onProgress, onItem, signal } = {}) {
    const id = ++this.n;
    return new Promise((res, rej) => {
      this.calls.set(id, { res, rej, onProgress, onItem });
      this.w.postMessage({ kind: 'call', id, method, args: JSON.stringify(args) });
      signal?.addEventListener('abort', () => this.w.postMessage({ kind: 'cancel', id }));
    });
  }
  terminate() { this.w.terminate(); }
}
