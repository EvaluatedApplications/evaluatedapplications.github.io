// Research spike: open the worker bench page in headless Edge, run window.runBench(), print the JSON. Surfaces console output of page and worker.
// Usage: node run-bench.mjs <url> [cpuRate=1]
import { spawn } from 'node:child_process'; import path from 'node:path';
const url = process.argv[2], CPU = Number(process.argv[3] ?? 1);
const DBG = 9300 + Math.floor(Math.random() * 500);
const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'rb-edge-' + DBG), '--no-first-run', '--enable-precise-memory-info', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (let i = 0; i < 60; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const tgt = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find(t => t.type === 'page');
const ws = new WebSocket(tgt.webSocketDebuggerUrl); await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map();
ws.addEventListener('message', ev => { const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type !== 'log') console.log('[console.' + m.params.type + '] ' + m.params.args.map(a => a.value ?? a.description).join(' ').slice(0, 300));
  if (m.method === 'Runtime.exceptionThrown') console.log('[EXC] ' + JSON.stringify(m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails).slice(0, 500)); });
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
await send('Runtime.enable'); await send('Page.enable'); if (CPU > 1) await send('Emulation.setCPUThrottlingRate', { rate: CPU });
await send('Page.navigate', { url });
for (let i = 0; i < 100; i++) { const r = await send('Runtime.evaluate', { expression: 'window.__ready === true', returnByValue: true }); if (r.result?.result?.value) break; await sleep(200); }
const r = await send('Runtime.evaluate', { expression: 'window.runBench().then(x => JSON.stringify(x))', awaitPromise: true, returnByValue: true });
console.log(r.result?.result?.value ?? JSON.stringify(r.result?.exceptionDetails ?? r).slice(0, 800));
ws.close(); edge.kill(); process.exit(0);
