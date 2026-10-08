// Research spike: how many foraging episodes does The Creature train in a fixed wall time (live HoloFormer training, the heaviest per-step compute in Showroom)?
// Usage: node creature-rate.mjs <url of /tools/creature> [seconds=20]. Run it against two builds to compare AOT with the interpreter.
import { spawn } from 'node:child_process'; import path from 'node:path';
const url = process.argv[2], SECS = Number(process.argv[3] ?? 20);
const DBG = 9300 + Math.floor(Math.random() * 500);
const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'cr-edge-' + DBG), '--no-first-run', '--window-size=1100,1000', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (let i = 0; i < 60; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const tgt = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find(t => t.type === 'page');
const ws = new WebSocket(tgt.webSocketDebuggerUrl); await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map();
ws.addEventListener('message', ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } });
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async e => (await send('Runtime.evaluate', { expression: e, awaitPromise: true, returnByValue: true })).result?.result?.value;
await send('Runtime.enable'); await send('Page.enable'); await send('Page.navigate', { url });
const t0 = Date.now();
while (Date.now() - t0 < 120000 && !(await ev(`!!document.querySelector('.cr-controls .go')`))) await sleep(200);
console.log('booted in ' + (Date.now() - t0) + ' ms');
await ev(`[...document.querySelectorAll('button')].find(b => /scatter apples/.test(b.textContent))?.click()`);
await sleep(300);
await ev(`(() => { const r = document.querySelector('.speed input'); const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value'); d.set.call(r, '10'); r.dispatchEvent(new Event('input', { bubbles: true })); })()`);
console.log('can run: ' + await ev(`!document.querySelector('.cr-controls .go').disabled`));
await ev(`document.querySelector('.cr-controls .go').click()`);
const t1 = Date.now(); await sleep(SECS * 1000);
const eps = await ev(`document.querySelector('.cr-stats .stat .n')?.textContent`);
console.log(`episodes after ${SECS} s: ${eps}  (${(Number(eps) / SECS).toFixed(2)} per s)`);
ws.close(); edge.kill(); process.exit(0);
