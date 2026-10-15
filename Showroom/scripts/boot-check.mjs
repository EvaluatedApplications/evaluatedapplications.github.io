// Headless-Edge check that a PUBLISHED Showroom build boots and loads its data from the website-data origin.
// Serves the published wwwroot under /tools/ and the website-data repo under /website-data/ on ONE origin (as GitHub Pages does),
// opens /tools/prism, waits for the checkpoint to load, sends one message and prints the reply, then lists every request by area
// (framework / site / website-data) and any non-200. Exits 1 on a page error, a 4xx/5xx, or a request that left the origin.
// Usage: node boot-check.mjs <published wwwroot> <website-data dir> [<framework dir>]
//   <framework dir>: when the build was published with the framework split out into website-data/_framework, nothing extra is
//   needed (it is inside <website-data dir>); the argument exists only to serve a framework folder from somewhere else.
// Needs Node 22+ and Edge at the path below. Publish first: dotnet publish Showroom.csproj -c Release -o <dir>.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pagesHandler } from './pages-server.mjs';

const ROOT = process.argv[2], DATA = process.argv[3];
const PORT = 8123 + Math.floor(Math.random() * 500), DBG = 9300 + Math.floor(Math.random() * 500);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.dll': 'application/octet-stream', '.csv': 'text/csv', '.txt': 'text/plain', '.gz': 'application/gzip', '.svg': 'image/svg+xml', '.dat': 'application/octet-stream', '.bin': 'application/octet-stream' };
const send404 = res => { res.writeHead(404); res.end(); };
const server = http.createServer(pagesHandler(ROOT, DATA)).listen(PORT);   // Pages-shaped: folder index, 404.html bounce, the Start button pressed for us (see pages-server.mjs)

const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'bc-edge-' + DBG), '--no-first-run', '--disable-extensions', '--window-size=1000,900', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (let i = 0; i < 60; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const tgt = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find(t => t.type === 'page');
const ws = new WebSocket(tgt.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map(); const reqs = new Map(); const problems = [], notes = [];
ws.addEventListener('message', ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
  if (m.method === 'Network.requestWillBeSent') reqs.set(m.params.requestId, { url: m.params.request.url });
  else if (m.method === 'Network.responseReceived') { const r = reqs.get(m.params.requestId); if (r) { r.status = m.params.response.status; r.len = Number(m.params.response.headers['content-length'] ?? 0); } }
  else if (m.method === 'Network.loadingFinished') { const r = reqs.get(m.params.requestId); if (r) { r.bytes = m.params.encodedDataLength; r.finished = true; } }
  else if (m.method === 'Network.loadingFailed') { const r = reqs.get(m.params.requestId); if (process.env.BC_LBR) console.log('LOADFAIL ' + JSON.stringify(m.params) + ' finishedBefore=' + !!r?.finished + ' status=' + r?.status + ' t=' + (Date.now() - t0)); if (r) r.failed = m.params.errorText; if (m.params.canceled && r?.status === 200) notes.push('cancelled after a 200 (the page aborts a fetch it has finished reading): ' + r.url); else problems.push('FAILED ' + (r?.url ?? m.params.requestId) + ' ' + m.params.errorText); }
  else if (m.method === 'Runtime.exceptionThrown') problems.push('PAGE-EXC ' + JSON.stringify(m.params.exceptionDetails).slice(0, 500));
  else if (m.method === 'Runtime.consoleAPICalled' && process.env.BC_LBR && m.params.args[0]?.value?.startsWith?.('LBR ')) console.log(m.params.args[0].value.slice(0, 220));
  else if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') problems.push('CONSOLE-ERR ' + m.params.args.map(a => a.value ?? a.description).join(' ').slice(0, 400));
});
const sendCdp = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async expr => { const r = await sendCdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); if (r.result.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400)); return r.result.result.value; };
await sendCdp('Runtime.enable'); await sendCdp('Page.enable'); await sendCdp('Network.enable');
await sendCdp('Network.setCacheDisabled', { cacheDisabled: true });

const origin = `http://${process.env.BC_HOST ?? '127.0.0.1'}:${PORT}`;
const t0 = Date.now();
await sendCdp('Page.navigate', { url: origin + '/tools/prism' });
async function waitFor(expr, ms, what) { const t = Date.now(); while (Date.now() - t < ms) { try { if (await ev(expr)) return Date.now() - t; } catch { } await sleep(250); } throw new Error('timeout: ' + what); }
let reply = '', stats = '', ok = true;
try {
  const loadMs = await waitFor(`!!document.querySelector('.cr-stats .stat .n') || !!document.querySelector('.err')`, 240000, 'checkpoint load');
  if (await ev(`!!document.querySelector('.err')`)) { ok = false; problems.push('PAGE SHOWS ERROR: ' + await ev(`document.querySelector('.err').textContent`)); }
  stats = await ev(`[...document.querySelectorAll('.cr-stats .stat')].map(s => s.innerText.replace(/\\n/g, ' ')).join(' | ')`);
  console.log(`prism loaded in ${Date.now() - t0} ms (page ready ${loadMs} ms after navigation settled): ${stats}`);
  if (ok) {
    await ev(`(() => { const i = document.querySelector('.or-input'); const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value'); d.set.call(i, 'hello, who are you?'); i.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await sleep(200);
    await ev(`document.querySelector('.or-input-row .go').click()`);
    await waitFor(`(document.querySelector('.or-input-row .go')?.textContent || '').trim() === 'Send' && !!document.querySelector('.or-turn.prism:not(.pending) .or-text')?.textContent.trim()`, 120000, 'a reply');
    reply = await ev(`document.querySelector('.or-turn.prism:not(.pending) .or-text').textContent`);
    console.log('prism reply: ' + JSON.stringify(reply.slice(0, 200)));
  }
} catch (e) { ok = false; problems.push(String(e)); }

const areas = {};
for (const r of reqs.values()) {
  if (!r.url.startsWith('http')) continue;
  const u = new URL(r.url); const sameOrigin = u.origin === origin;
  const area = !sameOrigin ? 'OFF-ORIGIN' : u.pathname.startsWith('/website-data/_framework/') ? 'website-data/_framework' : u.pathname.startsWith('/website-data/') ? 'website-data/data' : u.pathname.startsWith('/tools/_framework/') ? 'site/_framework' : u.pathname.startsWith('/tools/') ? 'site/other' : 'site/assets';
  const a = areas[area] ??= { n: 0, bytes: 0, bad: [] }; a.n++; a.bytes += r.bytes ?? 0;
  if (r.status && r.status >= 400) a.bad.push(r.status + ' ' + u.pathname);
  if (area === 'OFF-ORIGIN') problems.push('left the origin: ' + r.url);
}
for (const [k, v] of Object.entries(areas)) console.log(`${k.padEnd(26)} ${String(v.n).padStart(4)} requests ${(v.bytes / 1048576).toFixed(1).padStart(6)} MB on the wire` + (v.bad.length ? `   NON-2xx: ${v.bad.join(', ')}` : ''));
for (const r of reqs.values()) if (r.url.includes('/website-data/') && !r.url.includes('_framework')) console.log('  data: ' + new URL(r.url).pathname + ' ' + (r.status ?? '?'));
for (const r of reqs.values()) if (r.status >= 400) problems.push(r.status + ' ' + r.url);
for (const n of notes) console.log('note: ' + n);
console.log(problems.length || !ok ? 'RESULT: PROBLEMS\n' + problems.join('\n') : 'RESULT: OK');
ws.close(); edge.kill(); server.close(); process.exit(problems.length || !ok ? 1 : 0);
