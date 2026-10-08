// Research spike (no site changes): loads a URL in headless Edge with a COLD cache, over an emulated network and CPU,
// and prints bytes on the wire per file group plus first-paint / app-ready times.
// Usage: node measure-live.mjs <url> <readyExpr> <label> [downKbps upKbps rttMs cpuRate]
//   readyExpr: JS expression that becomes truthy when the page is interactive (e.g. the tool's first real element).
// Needs Node 22+ and Edge. Read-only against the live site (normal GETs, one page load).
import { spawn } from 'node:child_process';
import path from 'node:path';
const [url, readyExpr, label = 'run', down = '0', up = '0', rtt = '0', cpu = '1'] = process.argv.slice(2);
const DBG = 9300 + Math.floor(Math.random() * 500);
const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'ml-edge-' + DBG), '--no-first-run', '--disable-extensions', '--window-size=412,915', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (let i = 0; i < 60; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const tgt = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find(t => t.type === 'page');
const ws = new WebSocket(tgt.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map(); const reqs = new Map();
ws.addEventListener('message', ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
  if (m.method === 'Network.requestWillBeSent') reqs.set(m.params.requestId, { url: m.params.request.url, t: m.params.timestamp });
  else if (m.method === 'Network.responseReceived') { const r = reqs.get(m.params.requestId); if (r) { r.status = m.params.response.status; r.enc = m.params.response.headers['content-encoding'] ?? m.params.response.headers['Content-Encoding'] ?? ''; } }
  else if (m.method === 'Network.loadingFinished') { const r = reqs.get(m.params.requestId); if (r) { r.bytes = m.params.encodedDataLength; r.end = m.params.timestamp; } }
});
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async expr => { const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };
await send('Runtime.enable'); await send('Page.enable'); await send('Network.enable');
await send('Network.setCacheDisabled', { cacheDisabled: true });
await send('Emulation.setDeviceMetricsOverride', { width: 412, height: 915, deviceScaleFactor: 2, mobile: true });
if (Number(down) > 0) await send('Network.emulateNetworkConditions', { offline: false, latency: Number(rtt), downloadThroughput: Number(down) * 1024 / 8, uploadThroughput: Number(up) * 1024 / 8 });
if (Number(cpu) > 1) await send('Emulation.setCPUThrottlingRate', { rate: Number(cpu) });
await send('Page.addScriptToEvaluateOnNewDocument', { source: `
  window.__lb = { fcp: null, lcp: null };
  new PerformanceObserver(l => { for (const e of l.getEntries()) if (e.name === 'first-contentful-paint') window.__lb.fcp = e.startTime; }).observe({ type: 'paint', buffered: true });
  new PerformanceObserver(l => { for (const e of l.getEntries()) window.__lb.lcp = e.startTime; }).observe({ type: 'largest-contentful-paint', buffered: true });` });
const t0 = Date.now();
await send('Page.navigate', { url });
let ready = null;
while (Date.now() - t0 < 300000) { try { if (await ev(`!!(${readyExpr})`)) { ready = Date.now() - t0; break; } } catch { } await sleep(100); }
await sleep(1500);
const lb = await ev('JSON.stringify(window.__lb)'); const tt = await ev('JSON.stringify(window.__t||null)');
const nav = await ev(`JSON.stringify((()=>{const n=performance.getEntriesByType('navigation')[0];return {dcl:n.domContentLoadedEventEnd,load:n.loadEventEnd,resp:n.responseStart}})())`);
const groups = {};
const g = u => { const p = new URL(u).pathname; const f = p.split('/').pop();
  if (/dotnet\.native.*\.wasm$/.test(f)) return '1 dotnet.native.wasm (AOT)';
  if (/^dotnet.*\.js$/.test(f) || /blazor\.webassembly.*\.js$/.test(f)) return '2 runtime/loader js';
  if (/^icudt/.test(f)) return '4 ICU data';
  if (p.includes('/_framework/') && /\.wasm$/.test(f)) return /^(System|Microsoft|netstandard|mscorlib)/.test(f) ? '3 BCL/MS assemblies' : '5 app+EA assemblies';
  if (p.includes('/website-data/') && !p.includes('_framework')) return '6 data (' + p.split('/').slice(2, 3)[0] + ')';
  if (p.includes('/assets/') || p.endsWith('.css')) return '7 css/site';
  return '8 html/other'; };
let total = 0;
for (const r of reqs.values()) { if (!r.url.startsWith('http')) continue; const k = g(r.url); const o = groups[k] ??= { n: 0, bytes: 0, enc: new Set() }; o.n++; o.bytes += r.bytes ?? 0; if (r.enc) o.enc.add(r.enc); total += r.bytes ?? 0; }
console.log(`== ${label} ${url}  net=${down}kbps rtt=${rtt}ms cpu=${cpu}x`);
console.log('custom marks: ' + tt);
console.log(`ready(${readyExpr.slice(0, 40)}): ${ready} ms   paint/lcp: ${lb}   nav: ${nav}`);
for (const k of Object.keys(groups).sort()) console.log(`  ${k.padEnd(30)} ${String(groups[k].n).padStart(3)} req ${(groups[k].bytes / 1048576).toFixed(2).padStart(7)} MB wire  enc=${[...groups[k].enc].join(',') || 'none'}`);
console.log(`  ${'TOTAL'.padEnd(30)} ${(total / 1048576).toFixed(2).padStart(7)} MB wire`);
ws.close(); edge.kill(); process.exit(0);
