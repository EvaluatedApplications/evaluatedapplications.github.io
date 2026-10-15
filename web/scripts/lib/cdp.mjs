// Minimal headless-browser driver over the DevTools protocol (Edge or Chrome, no dependencies).
// Used by shots.mjs and verify-diagram.mjs for screenshots and interaction checks. Dev tooling only.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { spawn } from 'node:child_process';

const CANDIDATES = [
  process.env.EA_BROWSER,
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
].filter(Boolean);

export function findBrowser() {
  const f = CANDIDATES.find((p) => fs.existsSync(p));
  if (!f) throw new Error('No Edge or Chrome found; set EA_BROWSER');
  return f;
}

export function serve(dir, port = 0) {
  const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.json': 'application/json', '.txt': 'text/plain; charset=utf-8', '.xml': 'application/xml', '.svg': 'image/svg+xml' };
  const server = http.createServer((req, res) => {
    let p = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    let f = path.join(dir, p);
    if (!f.startsWith(dir)) { res.writeHead(403).end(); return; }
    if (fs.existsSync(f) && fs.statSync(f).isDirectory()) f = path.join(f, 'index.html');
    if (!fs.existsSync(f)) { const nf = path.join(dir, '404.html'); res.writeHead(404, { 'content-type': types['.html'] }); res.end(fs.existsSync(nf) ? fs.readFileSync(nf) : 'not found'); return; }
    res.writeHead(200, { 'content-type': types[path.extname(f)] || 'application/octet-stream' });
    fs.createReadStream(f).pipe(res);
  });
  return new Promise((resolve) => server.listen(port, '127.0.0.1', () => resolve({ server, port: server.address().port })));
}

export async function launch(debugPort = 9333) {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'ea-cdp-'));
  const proc = spawn(findBrowser(), ['--headless=new', `--remote-debugging-port=${debugPort}`, `--user-data-dir=${profile}`, '--no-first-run', '--disable-gpu', '--hide-scrollbars', 'about:blank'], { stdio: 'ignore' });
  let ws;
  for (let i = 0; i < 80 && !ws; i++) {
    await new Promise((r) => setTimeout(r, 150));
    try {
      const list = await (await fetch(`http://127.0.0.1:${debugPort}/json`)).json();
      ws = list.find((t) => t.type === 'page')?.webSocketDebuggerUrl;
    } catch { /* not up yet */ }
  }
  if (!ws) { proc.kill(); throw new Error('browser did not start'); }
  const sock = new WebSocket(ws);
  await new Promise((r, j) => { sock.onopen = r; sock.onerror = j; });
  let id = 0; const pending = new Map(); const listeners = [];
  sock.onmessage = (m) => {
    const msg = JSON.parse(m.data);
    if (msg.id && pending.has(msg.id)) { const { r, j } = pending.get(msg.id); pending.delete(msg.id); msg.error ? j(new Error(JSON.stringify(msg.error))) : r(msg.result); }
    else listeners.forEach((l) => l(msg));
  };
  const send = (method, params = {}) => new Promise((r, j) => { const i = ++id; pending.set(i, { r, j }); sock.send(JSON.stringify({ id: i, method, params })); });
  const on = (fn) => listeners.push(fn);
  const evaluate = async (expr) => {
    const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };
  await send('Page.enable'); await send('Runtime.enable');
  const errors = [];
  on((m) => {
    if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text);
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errors.push(m.params.args.map((a) => a.value ?? a.description).join(' '));
  });
  async function goto(url, { width = 1280, height = 900, mobile = false, reducedMotion = false, wait = null } = {}) {
    await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: mobile ? 2 : 1, mobile });
    await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: reducedMotion ? 'reduce' : 'no-preference' }] });
    errors.length = 0;
    await send('Emulation.setFocusEmulationEnabled', { enabled: true });
    await send('Page.bringToFront');
    await send('Page.navigate', { url });
    await new Promise((r) => { const h = (m) => { if (m.method === 'Page.loadEventFired') { listeners.splice(listeners.indexOf(h), 1); r(); } }; on(h); });
    if (wait) for (let i = 0; i < 100; i++) { if (await evaluate(wait)) break; await new Promise((r) => setTimeout(r, 100)); }
  }
  async function shot(file, { full = true } = {}) {
    let clip;
    if (full) {
      const m = await send('Page.getLayoutMetrics');
      const cs = m.cssContentSize || m.contentSize;
      clip = { x: 0, y: 0, width: cs.width, height: Math.min(cs.height, 9000), scale: 1 };
    }
    const r = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: !!full, ...(clip ? { clip } : {}) });
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, Buffer.from(r.data, 'base64'));
  }
  async function key(k, code = k, vk = 0) {
    await send('Input.dispatchKeyEvent', { type: 'keyDown', key: k, code, windowsVirtualKeyCode: vk });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key: k, code, windowsVirtualKeyCode: vk });
  }
  const close = () => { try { sock.close(); } catch { /* ignore */ } proc.kill(); try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* ignore */ } };
  return { send, evaluate, goto, shot, key, close, errors, on };
}
