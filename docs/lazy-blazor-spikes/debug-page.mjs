// Research spike: open a URL in headless Edge, print console/exception output and evaluate expressions after N ms.
// Usage: node debug-page.mjs <url> <waitMs> [expr ...]
import { spawn } from 'node:child_process'; import path from 'node:path';
const [url, waitMs, ...exprs] = process.argv.slice(2);
const DBG = 9300 + Math.floor(Math.random() * 500);
const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'dbg-edge-' + DBG), '--no-first-run', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (let i = 0; i < 60; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const tgt = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find(t => t.type === 'page');
const ws = new WebSocket(tgt.webSocketDebuggerUrl); await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map();
ws.addEventListener('message', ev => { const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
  if (m.method === 'Runtime.consoleAPICalled') console.log('[console.' + m.params.type + '] ' + m.params.args.map(a => a.value ?? a.description).join(' ').slice(0, 400));
  if (m.method === 'Runtime.exceptionThrown') console.log('[EXC] ' + JSON.stringify(m.params.exceptionDetails.exception ?? m.params.exceptionDetails).slice(0, 900)); });
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
await send('Runtime.enable'); await send('Page.enable');
await send('Page.navigate', { url }); await sleep(Number(waitMs));
for (const e of exprs) { const r = await send('Runtime.evaluate', { expression: e, awaitPromise: true, returnByValue: true }); console.log('> ' + e + '\n  ' + JSON.stringify(r.result?.result?.value ?? r.result)); }
ws.close(); edge.kill(); process.exit(0);
