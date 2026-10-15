// Headless-Edge harness for the declared-spend comparison: the hub summary (GapPanel), and on each council page the three-bar comparison drawn inside every loaded whole year (YearBlock + GapBars); screenshots. Same server, throttle and long-task measure as council-perf.mjs.
// or a publish output) under /tools/ and the website-data repo under /website-data/ (one origin, as on Pages),
// drives the pages over the DevTools protocol with CPU and network throttling, prints wall time and the worst stall (long task) of each action.
// Usage: node gap-perf.mjs <published wwwroot> <cpu slowdown, 1 = none> <download kbit/s, 0 = none> <rtt ms> [<website-data dir, default C:\Users\dongy\website-data>]   (phone bar used so far: 6 4096 100)
// Note: the throttle is switched on only AFTER boot, so the runtime download (now from website-data/_framework) is not part of the timings.
// Needs Node 22+ and Edge at the path below. Publish first: dotnet publish Showroom.csproj -c Release -o <dir>.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pagesHandler } from './pages-server.mjs';

const ROOT = process.argv[2];
const CPU = Number(process.argv[3] ?? 1);
const NET_KBPS = Number(process.argv[4] ?? 0);
const RTT = Number(process.argv[5] ?? 0);
const DATA = process.argv[6] ?? 'C:\\Users\\dongy\\website-data';
const PORT = 8123 + Math.floor(Math.random() * 500), DBG = 9300 + Math.floor(Math.random() * 500);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.dll': 'application/octet-stream', '.csv': 'text/csv', '.txt': 'text/plain', '.gz': 'application/gzip', '.svg': 'image/svg+xml', '.dat': 'application/octet-stream', '.blat': 'application/octet-stream', '.bin': 'application/octet-stream' };
const server = http.createServer(pagesHandler(ROOT, DATA)).listen(PORT);   // Pages-shaped: folder index, 404.html bounce, the Start button pressed for us (see pages-server.mjs)

const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'cw-edge-' + DBG), '--no-first-run', '--disable-extensions', '--window-size=412,915', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
let ver; for (let i = 0; i < 60; i++) { try { ver = await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const targets = await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json();
const tgt = targets.find(t => t.type === 'page');
const ws = new WebSocket(tgt.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map(); const perf = [];
ws.addEventListener('message', ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  else if (m.method === 'Runtime.exceptionThrown') { console.log('PAGE-EXC ' + JSON.stringify(m.params.exceptionDetails).slice(0, 700)); }
  else if (m.method === 'Log.entryAdded') { console.log('PAGE-LOGENTRY ' + m.params.entry.level + ' ' + m.params.entry.text.slice(0, 400)); }
  else if (m.method === 'Runtime.consoleAPICalled') {
    const t = m.params.args.map(a => a.value ?? a.description).join(' ');
    if (t.startsWith('CW-PERF')) perf.push(t); else if (m.params.type === 'error' || m.params.type === 'warning' || /fail|exception|unhandled|handler/i.test(t)) console.log('PAGE-LOG ' + t.slice(0, 600));
  }
});
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async expr => { const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); if (r.result.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400)); return r.result.result.value; };
await send('Runtime.enable'); await send('Log.enable'); await send('Page.enable'); await send('Network.enable');
await send('Page.addScriptToEvaluateOnNewDocument', { source: `
  window.__lt = []; try { new PerformanceObserver(l => { for (const e of l.getEntries()) window.__lt.push([e.startTime, e.duration]); }).observe({ entryTypes: ['longtask'] }); } catch (e) {}
  window.__waitFor = (sel, text, timeout) => new Promise((res, rej) => { const t0 = performance.now(); const tick = () => { const el = document.querySelector(sel); if (el && (!text || el.textContent.includes(text))) return res(performance.now() - t0); if (performance.now() - t0 > timeout) return rej(new Error('timeout waiting for ' + sel + ' ' + (text||''))); setTimeout(tick, 15); }; tick(); });
  window.__waitLoaded = (timeout) => new Promise((res, rej) => { const t0 = performance.now(); const tick = () => { const b = document.querySelector('.loadbtn'); const n = document.querySelectorAll('.loaded .yblock').length; if (b && !b.disabled && !document.querySelector('.loadprog') && n > 0) return res(n); if (document.querySelector('.err')) return rej(new Error('page error: ' + document.querySelector('.err').textContent)); if (performance.now() - t0 > timeout) return rej(new Error('timeout waiting for the load')); setTimeout(tick, 25); }; tick(); });
  window.__setVal = (el, v) => { const proto = Object.getPrototypeOf(el); const d = Object.getOwnPropertyDescriptor(proto, 'value'); d.set.call(el, v); el.dispatchEvent(new Event('change', { bubbles: true })); };
` });

// the static shell already has a .room, so wait for the RUNNING app: Blazier sets data-blazier-state="live" on #app once it has mounted over the shell (a pre-Blazier build, which has no shell, is ready at the first .room)
async function waitBoot() { for (let i = 0; i < 600; i++) { if (await ev(`!!(document.querySelector('#app[data-blazier-state="live"] .room') || (!document.querySelector('meta[name=blazier-scaffold]') && document.querySelector('.room')))`)) return; await sleep(200); } throw new Error('boot timeout'); }
const rows = [];
async function measure(name, js, settleMs = 200) {
  await ev(`window.__lt = []`);
  const inPage = await ev(`(async () => { const t0 = performance.now(); ${js}; return performance.now() - t0; })()`);
  await sleep(settleMs);
  const lt = await ev(`window.__lt`);
  const worst = lt.reduce((a, b) => Math.max(a, b[1]), 0);
  const row = { name, wallMs: Math.round(inPage), worstLongTaskMs: Math.round(worst), longTasks: lt.length };
  rows.push(row); console.log(JSON.stringify(row));
}
const base = `http://127.0.0.1:${PORT}/tools/`;
await send('Emulation.setDeviceMetricsOverride', { width: 412, height: 915, deviceScaleFactor: 2, mobile: true });
const COLD = process.argv[7] === 'cold';   // CPU throttle from the start (network is still switched on after boot), so the first load of budget_units.csv is measured at phone CPU speed
if (COLD && CPU > 1) await send('Emulation.setCPUThrottlingRate', { rate: CPU });
await send('Page.navigate', { url: base + 'council-spending' });
await waitBoot();
console.log(JSON.stringify({ bootToHubInteractiveMs: Math.round(await ev(`performance.now()`)) }));
if (CPU > 1) await send('Emulation.setCPUThrottlingRate', { rate: CPU });
if (NET_KBPS > 0) await send('Network.emulateNetworkConditions', { offline: false, latency: RTT, downloadThroughput: NET_KBPS * 1024 / 8, uploadThroughput: NET_KBPS * 1024 / 8 });
await measure('hub: open the declared-spend summary (first gz file of the session)', `document.querySelector('.gappanel .check-head').click(); await window.__waitFor('.gappanel .gaptable tbody tr', '', 60000)`);
console.log('hub rows: ' + await ev(`document.querySelectorAll('.gappanel .gaptable tbody tr').length`) + ' | ' + await ev(`document.querySelector('.gappanel .summary').innerText`));
const go = slug => measure(`${slug}: council page ready`, `history.pushState({}, '', '/tools/council-spending/${slug}'); window.dispatchEvent(new PopStateEvent('popstate')); await window.__waitFor('.loadsec .yitem', '', 30000)`);
// Select all and Load on the smaller councils (the big ones are measured in council-load-perf.mjs); the bars are drawn inside each loaded whole year
const loadAll = slug => measure(`${slug}: Select all + Load (declared-spend bars drawn inside each year)`, `document.querySelector('.selall').click(); await new Promise(r => setTimeout(r, 40)); document.querySelector('.loadbtn').click(); await window.__waitLoaded(240000)`, 400);
const open = () => ev(`[...document.querySelectorAll('.loaded .yblock .yhead')].forEach(h => { if (h.getAttribute('aria-expanded') !== 'true') h.click(); })`);
for (const slug of ['wokingham', 'merton', 'coventry', 'westberkshire']) {
  await go(slug);
  await loadAll(slug);
  await measure(`${slug}: open every year`, `[...document.querySelectorAll('.loaded .yblock .yhead')].forEach(h => { if (h.getAttribute('aria-expanded') !== 'true') h.click(); }); await new Promise(r => setTimeout(r, 100))`);
  console.log(`   ${slug}: year blocks ${await ev(`document.querySelectorAll('.loaded .yblock').length`)}, with the three bars ${await ev(`document.querySelectorAll('.loaded .yblock .gchart').length`)}, DOM nodes ${await ev(`document.querySelector('.loaded').querySelectorAll('*').length`)} | ${await ev(`[...document.querySelectorAll('.loaded .summary')].map(e => e.innerText).join(' || ')`)}`);
}
await go('wokingham');
await loadAll('wokingham');
await ev(`[...document.querySelectorAll('.loaded .yblock .yhead')].forEach(h => { if (h.getAttribute('aria-expanded') !== 'true') h.click(); })`); await sleep(500);
const clip = await ev(`(() => { document.querySelector('.loaded .yblock').scrollIntoView(); const r = document.querySelector('.loaded').getBoundingClientRect(); return { x: 0, y: r.top + window.scrollY, width: 412, height: Math.min(r.height, 3200), scale: 1 }; })()`);
await sleep(400);
const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true, clip });
fs.writeFileSync(path.join(process.env.TEMP, 'gap-wokingham.png'), Buffer.from(shot.result.data, 'base64'));
console.log('screenshot: ' + path.join(process.env.TEMP, 'gap-wokingham.png') + ' ' + JSON.stringify(clip));console.log('\n--- CW-PERF console lines from the page ---');
perf.filter(l => /gap|budget|units|profiles|load /.test(l)).forEach(l => console.log(l));
ws.close(); edge.kill(); server.close(); process.exit(0);
