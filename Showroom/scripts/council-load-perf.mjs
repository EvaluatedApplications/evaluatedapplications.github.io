// Headless-Edge harness for the "choose what to load" picker on a council page (financial-year tick-list, Select all, Load, Cancel, the loaded years).
// Same server, throttle and long-task measure as council-perf.mjs: serves a PUBLISHED wwwroot (Showroom/dist) under /tools/ and the website-data repo under /website-data/,
// drives the page over the DevTools protocol, prints wall time and the worst stall (long task) of each action, the renderer's working set (MB) at the end of each, and
// writes phone-width (412 px) screenshots to %TEMP%.
// Usage: node council-load-perf.mjs <published wwwroot> <cpu slowdown, 1 = none> <download kbit/s, 0 = none> <rtt ms> [<website-data dir>] [<scenarios: all|big|select|cancel>]
//   (phone bar used so far: 6 4096 100)
// Note: the throttle is switched on only AFTER boot. Needs Node 22+ and Edge at the path below.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn, execFileSync } from 'node:child_process';

const ROOT = process.argv[2];
const CPU = Number(process.argv[3] ?? 1);
const NET_KBPS = Number(process.argv[4] ?? 0);
const RTT = Number(process.argv[5] ?? 0);
const DATA = process.argv[6] ?? 'C:\\Users\\dongy\\website-data';
const WHICH = process.argv[7] ?? 'all';
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

const profileDir = path.join(process.env.TEMP, 'cw-edge-' + DBG);
const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + profileDir, '--no-first-run', '--disable-extensions', '--window-size=412,915', 'about:blank'], { stdio: 'ignore' });
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
  // a Load has finished when its progress bar has gone and the button is back
  window.__waitLoaded = (timeout) => new Promise((res, rej) => { const t0 = performance.now(); const tick = () => { const b = document.querySelector('.loadbtn'); const n = document.querySelectorAll('.loaded .yblock').length; if (b && !b.disabled && !document.querySelector('.loadprog') && n > 0) return res(n); if (document.querySelector('.err')) return rej(new Error('page error: ' + document.querySelector('.err').textContent)); if (performance.now() - t0 > timeout) return rej(new Error('timeout waiting for the load, progress: ' + (document.querySelector('.loadprog')?.innerText ?? ''))); setTimeout(tick, 25); }; tick(); });
  window.__yearItem = (fy) => [...document.querySelectorAll('.loadsec .yitem')].find(x => x.querySelector('.ytitle').textContent === fy);
  window.__tickYear = (fy) => { const li = window.__yearItem(fy); const cb = li.querySelector('.ycheck input'); if (!cb.checked) cb.click(); };
` });

async function waitBoot() { for (let i = 0; i < 600; i++) { if (await ev(`!!document.querySelector('.room')`)) return; await sleep(200); } throw new Error('boot timeout'); }
const rows = [];
function rssMb() {
  try {
    const out = execFileSync('powershell', ['-NoProfile', '-Command', `(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*cw-edge-${DBG}*' } | Measure-Object WorkingSetSize -Sum).Sum`], { encoding: 'utf8' }).trim();
    return Math.round(Number(out) / 1048576);
  } catch { return -1; }
}
let peak = 0, poll = null;
const startPoll = () => { peak = 0; poll = setInterval(() => { try { peak = Math.max(peak, rssMb()); } catch {} }, 4000); };
const stopPoll = () => { clearInterval(poll); };
async function measure(name, js, settleMs = 300, withMem = false) {
  await ev(`window.__lt = []`);
  if (withMem) startPoll();
  const inPage = await ev(`(async () => { const t0 = performance.now(); ${js}; return performance.now() - t0; })()`);
  await sleep(settleMs);
  if (withMem) stopPoll();
  const lt = await ev(`window.__lt`);
  const worst = lt.reduce((a, b) => Math.max(a, b[1]), 0);
  const row = { name, wallMs: Math.round(inPage), worstLongTaskMs: Math.round(worst), longTasks: lt.length };
  if (withMem) { row.edgeMbAfter = rssMb(); row.edgeMbPeakPolled = Math.max(peak, row.edgeMbAfter); }
  rows.push(row); console.log(JSON.stringify(row));
}
const base = `http://127.0.0.1:${PORT}/tools/`;
await send('Emulation.setDeviceMetricsOverride', { width: 412, height: 915, deviceScaleFactor: 2, mobile: true });
await send('Page.navigate', { url: base + 'council-spending' });
await waitBoot();
console.log(JSON.stringify({ bootToHubInteractiveMs: Math.round(await ev(`performance.now()`)) }));
if (CPU > 1) await send('Emulation.setCPUThrottlingRate', { rate: CPU });
if (NET_KBPS > 0) await send('Network.emulateNetworkConditions', { offline: false, latency: RTT, downloadThroughput: NET_KBPS * 1024 / 8, uploadThroughput: NET_KBPS * 1024 / 8 });
const go = slug => measure(`${slug}: council page ready`, `history.pushState({}, '', '/tools/council-spending/${slug}'); window.dispatchEvent(new PopStateEvent('popstate')); await window.__waitFor('.loadsec .yitem', '', 30000)`);
const shot = async (name, selector, maxH = 2400) => {
  await ev(`document.querySelector('${selector}').scrollIntoView()`); await sleep(500);
  const clip = await ev(`(() => { const r = document.querySelector('${selector}').getBoundingClientRect(); return { x: 0, y: r.top + window.scrollY, width: 412, height: Math.min(r.height, ${maxH}), scale: 1 }; })()`);
  const s = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true, clip });
  const f = path.join(process.env.TEMP, `cw-${name}.png`); fs.writeFileSync(f, Buffer.from(s.result.data, 'base64')); console.log('screenshot ' + f + ' ' + JSON.stringify(clip));
};
const info = () => ev(`({ size: document.querySelector('.loadsize')?.innerText, years: document.querySelectorAll('.loadsec .yitem').length, blocks: document.querySelectorAll('.loaded .yblock').length, openBlocks: document.querySelectorAll('.loaded .yblock .gchart').length, domNodes: document.querySelector('.cs').querySelectorAll('*').length, note: document.querySelector('.loadsec .hint')?.innerText })`);

// the biggest single year of each of the three biggest councils: size line, load, open the biggest list, source rows of its first group
if (WHICH === 'all' || WHICH === 'big') {
  for (const [slug, fy] of [['cornwall', '2025-26'], ['leeds', '2022-23'], ['sheffield', '2025-26'], ['wokingham', '2020-21']]) {
    await go(slug);
    console.log('   ' + JSON.stringify(await info()));
    await measure(`${slug} ${fy}: tick the year (size line updates)`, `window.__tickYear('${fy}'); await new Promise(r => setTimeout(r, 40))`);
    console.log('   size line: ' + await ev(`document.querySelector('.loadsize').innerText`));
    await measure(`${slug} ${fy}: Load (download + inflate + scan + draw)`, `document.querySelector('.loadbtn').click(); await window.__waitLoaded(180000)`, 400, true);
    console.log('   ' + JSON.stringify(await info()) + ' | ' + await ev(`document.querySelector('.loaded .yblock .yhead .n')?.innerText`));
    await measure(`${slug} ${fy}: open the largest group list`, `document.querySelector('.yblock .bucket-head').click(); await window.__waitFor('.yblock .bucket .lines li', '', 30000)`);
    await measure(`${slug} ${fy}: show 15 more`, `const m = document.querySelector('.yblock .more'); if (m) { m.click(); await new Promise(r => setTimeout(r, 60)); }`);
    await measure(`${slug} ${fy}: see the source rows of the first group`, `document.querySelector('.yblock .lines li .srcbtn').click(); await window.__waitFor('.yblock .srcblock', '', 90000)`);
    if (slug === 'cornwall') { await shot('cornwall-loaded-year', '.loaded', 2600); }
  }
}

// Select all on the big councils: one Load for every year; then open a year's list (its bytes may have been let go), then Cancel part way
if (WHICH === 'all' || WHICH === 'select') {
  for (const slug of ['cornwall', 'leeds', 'sheffield']) {
    await go(slug);
    await measure(`${slug}: Select all`, `document.querySelector('.selall').click(); await new Promise(r => setTimeout(r, 60))`);
    console.log('   size line: ' + await ev(`document.querySelector('.loadsize').innerText`));
    if (slug === 'sheffield') await shot('sheffield-select-all', '.loadsec', 3000);
    await measure(`${slug}: Load everything ticked`, `document.querySelector('.loadbtn').click(); await window.__waitLoaded(600000)`, 600, true);
    console.log('   ' + JSON.stringify(await info()));
    if (slug === 'sheffield') await shot('sheffield-loaded-all', '.loaded', 1700);
    await measure(`${slug}: open the oldest year (bytes may need reading back)`, `const bl = [...document.querySelectorAll('.loaded .yblock')].filter(b => !b.querySelector('.yhead .n').textContent.startsWith('0 ')); bl[bl.length - 1].querySelector('.yhead').click(); await new Promise(r => setTimeout(r, 80))`);
    await measure(`${slug}: open its largest group list`, `const bs = [...document.querySelectorAll('.loaded .yblock')].filter(b => !b.querySelector('.yhead .n').textContent.startsWith('0 ')); const b = bs[bs.length - 1].querySelector('.bucket-head'); b.click(); await new Promise(res => { const t = () => bs[bs.length - 1].querySelector('.bucket .lines li') ? res() : setTimeout(t, 20); t(); })`);
    await measure(`${slug}: open the newest year and its largest list`, `const hs = [...document.querySelectorAll('.loaded .yblock .yhead')]; hs[0].click(); await new Promise(r => setTimeout(r, 80)); document.querySelector('.loaded .yblock .bucket-head').click(); await window.__waitFor('.loaded .yblock .bucket .lines li', '', 60000)`);
  }
}

// Cancel in the middle of a Select all
if (WHICH === 'all' || WHICH === 'cancel') {
  await go('leeds');
  await ev(`document.querySelector('.selall').click()`); await sleep(100);
  await measure('leeds: Load, then Cancel after the first year is shown', `document.querySelector('.loadbtn').click(); await window.__waitFor('.loaded .yblock', '', 120000); document.querySelector('.cancelbtn').click(); await window.__waitFor('.loadsec .hint', 'Stopped', 60000)`, 800);
  console.log('   ' + JSON.stringify(await info()));
  await measure('leeds: Load again carries on with the rest', `document.querySelector('.loadbtn').click(); await window.__waitLoaded(600000)`, 600);
  console.log('   ' + JSON.stringify(await info()));
  // months: choose months of one year
  await go('wokingham');
  await measure('wokingham: expand a year and tick three months', `const li = window.__yearItem('2021-22'); li.querySelector('.ymonths').click(); await window.__waitFor('.loadsec .mrows', '', 5000); const cbs = [...li.querySelectorAll('.mrows input')].slice(0, 3); for (const c of cbs) c.click(); await new Promise(r => setTimeout(r, 60))`);
  console.log('   size line: ' + await ev(`document.querySelector('.loadsize').innerText`));
  await measure('wokingham: Load the three months', `document.querySelector('.loadbtn').click(); await window.__waitLoaded(60000)`);
  console.log('   ' + JSON.stringify(await info()) + ' | ' + await ev(`document.querySelector('.loaded .yblock .yhead h3')?.innerText`));
  await shot('wokingham-months', '.loadsec', 2000);
  await go('wokingham');
  await ev(`window.__tickYear('2021-22'); window.__tickYear('2022-23')`); await sleep(100);
  await measure('wokingham: tick two whole years and Load', `document.querySelector('.loadbtn').click(); await window.__waitLoaded(60000)`);
  await shot('wokingham-two-years', '.loaded', 3400);
}

// Wokingham: the n-squared payment runs (2022-23 holds the group, 2023-24 the Schedule A row), the care caption on a repeat group, and the social care panel
if (WHICH === 'all' || WHICH === 'care') {
  await go('wokingham');
  await ev(`window.__tickYear('2022-23'); window.__tickYear('2023-24')`); await sleep(100);
  await measure('wokingham 2022-23 + 2023-24: Load', `document.querySelector('.loadbtn').click(); await window.__waitLoaded(120000)`, 400, true);
  await ev(`[...document.querySelectorAll('.yblock .yhead')].forEach(h => { if (h.getAttribute('aria-expanded') !== 'true') h.click(); })`); await sleep(300);
  // open every bucket of both years so the quirk rows and the care captions are in the DOM, then read them back
  await measure('wokingham: open every group list of the two years', `for (const b of document.querySelectorAll('.yblock .bucket-head')) { if (b.getAttribute('aria-expanded') !== 'true') b.click(); } await new Promise(r => setTimeout(r, 600))`);
  console.log('   n-squared rows: ' + JSON.stringify(await ev(`[...document.querySelectorAll('.yblock .lines li')].filter(li => /payment run/.test(li.textContent)).map(li => li.innerText.replace(/\\s+/g, ' ').slice(0, 900))`)));
  console.log('   care captions on screen: ' + await ev(`document.querySelectorAll('.yblock .care').length`) + ', bucket heads: ' + JSON.stringify(await ev(`[...document.querySelectorAll('.yblock .bucket-head')].map(b => b.innerText.replace(/\\s+/g, ' ')).filter(t => /Publication quirk|Unclear|Standing|Unreconciled/.test(t)).slice(0, 8)`)));
  await shot('wokingham-quirk', '.yblock .bucket', 1800);
  await measure('wokingham: open the social care panel (4 small files)', `document.querySelector('.scare .check-head').click(); await window.__waitFor('.scare .lines li', '', 60000)`);
  console.log('   ' + await ev(`document.querySelector('.scare .summary').innerText.slice(0, 400)`));
  await shot('wokingham-socialcare', '.scare', 3200);
  await measure('wokingham: choose another year in the provider list', `const s = document.querySelectorAll('.scare select')[1]; s.value = s.options[0].value; s.dispatchEvent(new Event('change', { bubbles: true })); await new Promise(r => setTimeout(r, 120))`);
  await measure('wokingham: open a provider company record', `document.querySelector('.scare .srcbtn').click(); await window.__waitFor('.scare .chrec', '', 5000)`);
  console.log('   company record: ' + await ev(`document.querySelector('.scare .chrec').innerText.slice(0, 400)`));
}

console.log('\n--- CW-PERF console lines from the page ---');
perf.filter(l => /load |reread/.test(l)).forEach(l => console.log(l));
fs.writeFileSync(path.join(process.env.TEMP, `cw-load-result-cpu${CPU}-net${NET_KBPS}.json`), JSON.stringify({ cpu: CPU, netKbps: NET_KBPS, rtt: RTT, rows }, null, 1));
ws.close(); edge.kill(); server.close(); process.exit(0);
