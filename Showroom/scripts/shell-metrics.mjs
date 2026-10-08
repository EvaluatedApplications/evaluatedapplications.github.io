// Per-page load measurements for the static-shell build against the live site, in headless Edge (cold cache, one fresh browser per run).
//   node shell-metrics.mjs local <published wwwroot (dist)> <website-data dir> <desktop|phone> [pages csv] [runs]
//   node shell-metrics.mjs live  https://evaluatedapplications.github.io/tools/ <desktop|phone> [pages csv] [runs]
//   LEGACY=1 node shell-metrics.mjs local <a pre-Blazier dist> ...   measures an old build like the live site: from navigation start, no click
// Profiles: desktop = no throttling, 1280x800. phone = 412x915 mobile, 4096/1024 kbps, 100 ms RTT, CPU 6x (the profile every Showroom perf script uses).
// Local serving is shaped like GitHub Pages (pages-server.mjs: gzip level 9, folder index, real 404.html); the app is NOT auto-started.
// For each page it prints:
//   bytes before intent   wire bytes (encodedDataLength) once the page has settled with nobody touching it. A static shell is settled at once;
//                         the live site boots itself, so for it this is the whole boot (it has no "before intent").
//   first paint           first-contentful-paint, ms from navigation start
//   content (h1)          ms from navigation start until the page's own <h1> text is in the DOM (the live site draws it only after the runtime boots)
//   click -> working      shell: ms from pressing the start button until the tool's own UI is on screen.  live: ms from navigation start (no click).
//   bytes after click     shell only: wire bytes the start added
// Runs are medians. "working" is a per-page selector that only exists once the tool has drawn its real UI (see PAGES).
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pagesHandler } from './pages-server.mjs';

const mode = process.argv[2];
let ROOT, DATA, LIVE, profile, pagesArg, runsArg;
if (mode === 'local') [, , , ROOT, DATA, profile, pagesArg, runsArg] = process.argv;
else if (mode === 'live') [, , , LIVE, profile, pagesArg, runsArg] = process.argv;
else { console.error('usage: see the header'); process.exit(2); }
const RUNS = Number(runsArg ?? 1);
const liveStyle = mode === 'live' || !!process.env.LEGACY;

// page key -> route under /tools/, the selector that means "the tool is working"
const PAGES = {
  gallery: ['', '.card.tool'],
  prism: ['prism', '.cr-stats .stat .n'],
  stories: ['stories', '.ns-panel .prompt-input'],
  cartographer: ['cartographer', '.cg-form .cg-input'],
  creature: ['creature', '.cr-stage .cr-grid'],
  forecaster: ['forecaster', '.fc-stage'],
  analyst: ['analyst', '.dropzone'],
  prose: ['prose', '.dropzone'],
  'council-spending': ['council-spending', '.picker-grid .pick-card'],
  'recycledao-demo': ['recycledao-demo', '.mk-shell'],
};
const keys = (pagesArg && pagesArg !== 'all') ? pagesArg.split(',') : Object.keys(PAGES);

const PORT = 8600 + Math.floor(Math.random() * 300);
let base;
if (mode === 'local') {
  http.createServer(pagesHandler(ROOT, DATA, { gzip: true, cache: 'max-age=600', autoStart: false })).listen(PORT);
  base = `http://127.0.0.1:${PORT}/tools/`;
} else base = LIVE.endsWith('/') ? LIVE : LIVE + '/';

const sleep = ms => new Promise(r => setTimeout(r, ms));
const median = a => { const s = a.filter(x => x != null).sort((x, y) => x - y); return s.length ? s[Math.floor(s.length / 2)] : null; };

async function oneRun(key) {
  const [route, ready] = PAGES[key];
  const DBG = 9300 + Math.floor(Math.random() * 600);
  const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'sm-edge-' + DBG), '--no-first-run', '--disable-extensions',
      profile === 'phone' ? '--window-size=412,915' : '--window-size=1280,800', 'about:blank'], { stdio: 'ignore' });
  for (let i = 0; i < 80; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
  const tgt = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find(t => t.type === 'page');
  const ws = new WebSocket(tgt.webSocketDebuggerUrl);
  await new Promise(r => ws.addEventListener('open', r));
  let id = 0; const pending = new Map(); const reqs = new Map(); let lastNet = Date.now(), loadFired = false;
  ws.addEventListener('message', ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
    if (m.method === 'Network.requestWillBeSent') { reqs.set(m.params.requestId, { url: m.params.request.url, done: false }); lastNet = Date.now(); }
    else if (m.method === 'Network.responseReceived') { const r = reqs.get(m.params.requestId); if (r) r.status = m.params.response.status; }
    else if (m.method === 'Network.loadingFinished') { const r = reqs.get(m.params.requestId); if (r) { r.bytes = m.params.encodedDataLength; r.done = true; } lastNet = Date.now(); }
    else if (m.method === 'Network.loadingFailed') { const r = reqs.get(m.params.requestId); if (r) r.done = true; lastNet = Date.now(); }
    else if (m.method === 'Page.loadEventFired') loadFired = true;
  });
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const ev = async expr => { const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };
  await send('Runtime.enable'); await send('Page.enable'); await send('Network.enable');
  await send('Network.setCacheDisabled', { cacheDisabled: true });
  if (profile === 'phone') {
    await send('Emulation.setDeviceMetricsOverride', { width: 412, height: 915, deviceScaleFactor: 2, mobile: true });
    await send('Network.emulateNetworkConditions', { offline: false, latency: 100, downloadThroughput: 4096 * 1024 / 8, uploadThroughput: 1024 * 1024 / 8 });
    await send('Emulation.setCPUThrottlingRate', { rate: 6 });
  } else await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });
  await send('Page.addScriptToEvaluateOnNewDocument', { source: `
    window.__m = { fcp: null, h1: null };
    new PerformanceObserver(l => { for (const e of l.getEntries()) if (e.name === 'first-contentful-paint') window.__m.fcp = e.startTime; }).observe({ type: 'paint', buffered: true });
    new MutationObserver(() => { if (window.__m.h1 == null) { const h = document.querySelector('h1'); if (h && h.textContent.trim()) window.__m.h1 = performance.now(); } })
      .observe(document, { childList: true, subtree: true, characterData: true });` });
  const total = () => [...reqs.values()].reduce((s, r) => s + (r.bytes ?? 0), 0);
  const t0 = Date.now();
  await send('Page.navigate', { url: base + route + (process.env.TRAILING && route ? '/' : '') });   // no trailing slash, as every link on the site writes it: Pages answers a folder with a 301 to the slash
  const out = { key, bytesBefore: null, fcp: null, h1: null, working: null, bytesAfter: null, requestsBefore: null, errors: [] };
  const poll = async (expr, limit) => { const s = Date.now(); while (Date.now() - s < limit) { try { if (await ev(`!!(${expr})`)) return Date.now() - s; } catch { } await sleep(50); } return null; };
  if (mode === 'live' || process.env.LEGACY) {
    // no intent step: the live site boots itself (LEGACY=1: a pre-Blazier build served locally behaves the same). "working" is measured from navigation start.
    const tw = await poll(`document.querySelector('${ready}')`, 300000);
    out.working = tw == null ? null : Date.now() - t0;
    await sleep(1500);
    out.bytesBefore = total(); out.requestsBefore = reqs.size;
  } else {
    // settle: load fired and the network quiet for 1.5 s
    while (!(loadFired && Date.now() - lastNet > 1500) && Date.now() - t0 < 120000) await sleep(100);
    out.bytesBefore = total(); out.requestsBefore = [...reqs.values()].filter(r => r.url.startsWith('http')).length;
    const hasGo = await ev(`!!document.querySelector('[data-blazier-go]')`);
    if (hasGo) {
      const startBytes = total(); const tc = Date.now();
      await ev(`document.querySelector('[data-blazier-go]').click()`);
      const tw = await poll(`document.querySelector('${ready}')`, 300000);
      out.working = tw == null ? null : Date.now() - tc;
      await sleep(1200);
      out.bytesAfter = total() - startBytes;
    } else out.working = 0;   // the gallery is static: working as soon as it is settled
  }
  const m = JSON.parse(await ev(`JSON.stringify(window.__m)`) ?? '{}');
  out.fcp = m.fcp; out.h1 = m.h1;
  ws.close(); edge.kill();
  await sleep(300);
  return out;
}

const table = [];
for (const key of keys) {
  const runs = [];
  for (let i = 0; i < RUNS; i++) { try { runs.push(await oneRun(key)); } catch (e) { console.error(key, 'run failed:', e.message); } }
  const row = { page: key, bytesBefore: median(runs.map(r => r.bytesBefore)), reqs: median(runs.map(r => r.requestsBefore)), fcp: median(runs.map(r => r.fcp)), h1: median(runs.map(r => r.h1)), working: median(runs.map(r => r.working)), bytesAfter: median(runs.map(r => r.bytesAfter)), n: runs.length };
  table.push(row);
  console.log(JSON.stringify(row));
}
const f1 = x => x == null ? '-' : x.toFixed(0);
const kb = x => x == null ? '-' : (x / 1024).toFixed(x > 1048576 ? 0 : 1) + ' KB';
console.log(`\n${mode} ${mode === 'live' ? base : '(local, Pages-shaped)'}  profile=${profile}  runs=${RUNS}`);
console.log('page'.padEnd(18) + 'bytes before'.padStart(14) + 'reqs'.padStart(6) + 'first paint'.padStart(13) + 'content h1'.padStart(12) + (liveStyle ? 'ready (nav->)'.padStart(15) : 'click->working'.padStart(16)) + 'bytes after'.padStart(14));
for (const r of table) console.log(r.page.padEnd(18) + kb(r.bytesBefore).padStart(14) + String(r.reqs ?? '-').padStart(6) + (f1(r.fcp) + ' ms').padStart(13) + (f1(r.h1) + ' ms').padStart(12) + (f1(r.working) + ' ms').padStart(liveStyle ? 15 : 16) + kb(r.bytesAfter).padStart(14));
process.exit(0);
