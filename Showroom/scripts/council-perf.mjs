// Headless-Edge harness for the Council Spending Scanner (request tray, "See the source rows", month lists). Serves a PUBLISHED wwwroot (Showroom/dist,
// or a publish output) under /tools/ and the website-data repo under /website-data/ (one origin, as on Pages),
// drives the pages over the DevTools protocol with CPU and network throttling, prints wall time and the worst stall (long task) of each action.
// Usage: node council-perf.mjs <published wwwroot> <cpu slowdown, 1 = none> <download kbit/s, 0 = none> <rtt ms> [<website-data dir, default C:\Users\dongy\website-data>]   (phone bar used so far: 6 4096 100)
// Note: the throttle is switched on only AFTER boot, so the runtime download (now from website-data/_framework) is not part of the timings.
// Needs Node 22+ and Edge at the path below. Publish first: dotnet publish Showroom.csproj -c Release -o <dir>.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const ROOT = process.argv[2];
const CPU = Number(process.argv[3] ?? 1);
const NET_KBPS = Number(process.argv[4] ?? 0);
const RTT = Number(process.argv[5] ?? 0);
const DATA = process.argv[6] ?? 'C:\\Users\\dongy\\website-data';
const PORT = 8123 + Math.floor(Math.random() * 500), DBG = 9300 + Math.floor(Math.random() * 500);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.dll': 'application/octet-stream', '.csv': 'text/csv', '.txt': 'text/plain', '.gz': 'application/gzip', '.svg': 'image/svg+xml', '.dat': 'application/octet-stream', '.blat': 'application/octet-stream', '.bin': 'application/octet-stream' };
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]);
  if (p.startsWith('/assets/') || p.startsWith('/SiteKit/')) { const sf = p.startsWith('/assets/') ? path.join('C:\\Users\\dongy\\AboutUs\\site', p) : path.join('C:\\Users\\dongy\\AboutUs', p); if (fs.existsSync(sf)) { res.writeHead(200, { 'Content-Type': types[path.extname(sf)] ?? 'text/plain' }); return fs.createReadStream(sf).pipe(res); } res.writeHead(404); return res.end(); }
  if (p.startsWith('/website-data/')) { const df = path.join(DATA, p.slice('/website-data/'.length)); if (fs.existsSync(df) && fs.statSync(df).isFile()) { res.writeHead(200, { 'Content-Type': types[path.extname(df)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' }); return fs.createReadStream(df).pipe(res); } res.writeHead(404); return res.end(); }
  if (!p.startsWith('/tools/')) { res.writeHead(404); return res.end(); }
  let f = path.join(ROOT, p.slice('/tools/'.length));
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) f = path.join(ROOT, 'index.html');
  const ext = path.extname(f);
  res.writeHead(200, { 'Content-Type': types[ext] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
  fs.createReadStream(f).pipe(res);
}).listen(PORT);

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
  window.__setVal = (el, v) => { const proto = Object.getPrototypeOf(el); const d = Object.getOwnPropertyDescriptor(proto, 'value'); d.set.call(el, v); el.dispatchEvent(new Event('change', { bubbles: true })); };
` });

async function waitBoot() { for (let i = 0; i < 600; i++) { if (await ev(`!!document.querySelector('.room')`)) return; await sleep(200); } throw new Error('boot timeout'); }
const rows = [];
async function measure(name, js, settleMs = 150) {
  await ev(`window.__lt = []`);
  const inPage = await ev(`(async () => { const t0 = performance.now(); ${js}; return performance.now() - t0; })()`);
  await sleep(settleMs);
  const lt = await ev(`window.__lt`);
  const worst = lt.reduce((a, b) => Math.max(a, b[1]), 0);
  const row = { name, wallMs: Math.round(inPage), worstLongTaskMs: Math.round(worst), longTasks: lt.length };
  rows.push(row); console.log(JSON.stringify(row));
}
const letter = () => ev(`document.querySelector('.foi-letter')?.value ?? ''`);

const base = `http://127.0.0.1:${PORT}/tools/`;
await send('Page.navigate', { url: base + 'council-spending' });
await waitBoot();
console.log(JSON.stringify({ bootToHubInteractiveMs: Math.round(await ev(`performance.now()`)) }));
if (CPU > 1) await send('Emulation.setCPUThrottlingRate', { rate: CPU });
if (NET_KBPS > 0) await send('Network.emulateNetworkConditions', { offline: false, latency: RTT, downloadThroughput: NET_KBPS * 1024 / 8, uploadThroughput: NET_KBPS * 1024 / 8 });
const go = slug => measure(`${slug}: council page ready`, `history.pushState({}, '', '/tools/council-spending/${slug}'); window.dispatchEvent(new PopStateEvent('popstate')); await window.__waitFor('.about', '', 30000)`);
// the council page loads by financial year (April to March); to keep these scenarios comparable with the older month-picker numbers, tick ONE month
// (expand its year, tick the month) and Load, which fetches that month's own small file
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const fyOf = m => { const y = +m.slice(0, 4), mo = +m.slice(5); const s = mo >= 4 ? y : y - 1; return `${s}-${String((s + 1) % 100).padStart(2, '0')}`; };
async function month(slug, m) {
  const fy = fyOf(m), title = `${MONTHS[+m.slice(5) - 1]} ${m.slice(0, 4)}`;
  await measure(`${slug} ${m}: tick the month and Load`, `const li = [...document.querySelectorAll('.loadsec .yitem')].find(x => x.querySelector('.ytitle').textContent === '${fy}'); li.querySelector('.ymonths').click(); await window.__waitFor('.loadsec .mrows', '', 5000); const mi = [...li.querySelectorAll('.mrows li')].find(x => x.querySelector('.ytitle').textContent === '${title}'); mi.querySelector('input').click(); await new Promise(r => setTimeout(r, 40)); document.querySelector('.loadbtn').click(); await window.__waitFor('.yblock .summary, .yblock .plain', '', 60000); await new Promise(r => setTimeout(r, 30))`);
}

// ---------- hub: tick rows of a cross-council check, open the tray, read the letter
await measure('hub: open the twins check', `document.querySelector('.cross .check .check-head').click(); await window.__waitFor('.cross .check .summary', '', 30000)`);
await measure('hub: tick one twin row', `document.querySelector('.cross .check .tick').click(); await window.__waitFor('.cross .check .tick.on', '', 5000)`);
await measure('hub: tick 4 more twin rows', `const ts = [...document.querySelectorAll('.cross .check .tick:not(.on)')].slice(0, 4); for (const t of ts) { t.click(); await new Promise(r => setTimeout(r, 20)); }`);
await measure('hub: open the tray from the floating button (profile fetch + letter)', `document.querySelector('.foi-fab').click(); await window.__waitFor('.foi-letter', '', 30000); await new Promise(r => setTimeout(r, 50))`);
await measure('hub: type a name (letter rebuilt)', `window.__setVal(document.querySelector('.foi-fields input[type=text]'), 'Jane Smith'); await new Promise(r => setTimeout(r, 60))`);
console.log('\n=== LETTER (hub, 5 twin rows) ===\n' + await letter() + '\n=== END ===\n');
await measure('hub: open the source rows of a twin row', `const l = document.querySelector('.cross .check .lines li .srcbtn'); l.click(); await window.__waitFor('.cross .check .srcblock .lines li', '', 60000)`); console.log('   SRC: ' + await ev(`[...document.querySelectorAll('.cross .check .srcblock .plain')].map(e => e.innerText).join(' || ')`));

// ---------- one council page, several groups ticked
for (const [slug, m] of [['wokingham', '2020-10']]) {
  await go(slug);
  await month(slug, m);
  await measure(`${slug} ${m}: open the largest group list`, `document.querySelector('.yblock .bucket-head').click(); await window.__waitFor('.yblock .bucket .lines li', '', 30000)`);
  await measure(`${slug} ${m}: tick one group`, `document.querySelector('.yblock .tick').click(); await window.__waitFor('.yblock .tick.on', '', 5000)`);
  await measure(`${slug} ${m}: tick the other 14 listed groups`, `const ts = [...document.querySelectorAll('.yblock .tick:not(.on)')]; for (const t of ts) { t.click(); await new Promise(r => setTimeout(r, 10)); }`);
  await measure(`${slug} ${m}: open the tray with 15 groups + 5 twin rows ticked`, `document.querySelector('.foi-fab').click(); await window.__waitFor('.foi-letter', '', 30000); await new Promise(r => setTimeout(r, 50))`);
  console.log('\n=== LETTER (wokingham, first 2400 chars) ===\n' + (await letter()).slice(0, 2400) + '\n=== END ===\n');
  await measure(`${slug} ${m}: see the source rows of the first group`, `const b = document.querySelector('.yblock .lines li .srcbtn'); b.click(); await window.__waitFor('.yblock .srcblock .lines li', '', 60000)`);
}

// ---------- the heaviest months: source rows of the biggest groups
for (const [slug, m] of [['leeds', '2022-11'], ['sheffield', '2023-03'], ['bradford', '2025-09']]) {
  await go(slug);
  await month(slug, m);
  await measure(`${slug} ${m}: open the largest group list`, `document.querySelector('.yblock .bucket-head').click(); await window.__waitFor('.yblock .bucket .lines li', '', 30000)`);
  await measure(`${slug} ${m}: see the source rows of the first group (fetch + scan the month's transaction file)`, `const b = document.querySelector('.yblock .lines li .srcbtn'); b.click(); await window.__waitFor('.yblock .srcblock', '', 90000)`); console.log('   SRC: ' + await ev(`[...document.querySelectorAll('.yblock .srcblock .plain')].map(e => e.innerText).join(' || ')`) + ' | rows listed: ' + await ev(`document.querySelectorAll('.yblock .srcblock .lines li').length`));
  await measure(`${slug} ${m}: see the source rows of a second group, same month (cached bytes)`, `const bs = document.querySelectorAll('.yblock .lines li .srcbtn'); bs[1].click(); await window.__waitFor('.yblock .lines li:nth-child(2) .srcblock', '', 90000)`);
  await measure(`${slug} ${m}: tick a group, open the tray`, `document.querySelector('.yblock .tick:not(.on)').click(); await new Promise(r => setTimeout(r, 30)); document.querySelector('.foi-fab')?.click(); await window.__waitFor('.foi-letter', '', 30000)`);
}

// ---------- a tray of 100 items: the longest letter
await go('wokingham');
await month('wokingham', '2020-10');
await measure('wokingham 2020-10: open every group list and show all of the largest, ticking 100 groups', `
  const heads = [...document.querySelectorAll('.yblock .bucket-head')]; heads[0].click(); await window.__waitFor('.yblock .bucket .lines li', '', 30000);
  let n = 0;
  while (n < 100) { const more = document.querySelector('.yblock .more'); const ts = [...document.querySelectorAll('.yblock .tick:not(.on)')]; for (const t of ts) { t.click(); n++; if (n >= 100) break; } if (n < 100 && more) { more.click(); await new Promise(r => setTimeout(r, 40)); } else if (n < 100) break; }
  window.__ticked = n`);
console.log(JSON.stringify({ ticked: await ev('window.__ticked') }));
await sleep(1500);
await measure('wokingham: open the tray with the tray full of ticked groups', `const f = document.querySelector('.foi-fab'); const hd = document.querySelector('#foi-tray .check-head'); if (f) f.click(); else if (hd.getAttribute('aria-expanded') !== 'true') hd.click(); await window.__waitFor('.foi-letter', '', 30000); await new Promise(r => setTimeout(r, 50))`);
const L = await letter(); console.log(JSON.stringify({ letterChars: L.length, items: (L.match(/^\d+\. /gm) || []).length }));
await measure('wokingham: type a name with the long letter in place', `window.__setVal(document.querySelector('.foi-fields input[type=text]'), 'Jane Smith'); await new Promise(r => setTimeout(r, 60))`);

console.log('\n--- CW-PERF console lines from the page ---');
perf.forEach(l => console.log(l));
const out = { cpu: CPU, netKbps: NET_KBPS, rtt: RTT, rows };
fs.writeFileSync(path.join(process.env.TEMP, `cw2-result-cpu${CPU}-net${NET_KBPS}.json`), JSON.stringify(out, null, 1));
ws.close(); edge.kill(); server.close(); process.exit(0);
