// Headless-Edge check of Nano Stories and The Cartographer on a PUBLISHED build (2026-10-04 r3,970 checkpoint: ctx 512, 100 boundaries).
// Same one-origin serving as boot-check.mjs. Prints: Stories wall time per variety (nothing should re-prime, so seconds not minutes); Cartographer
// element counts (trajectory points, labels, attention lines, table rows) and writes a screenshot of the result to <shot png> when given.
// Usage: node tool-check.mjs <published wwwroot> <website-data dir> [<cartographer screenshot png>]
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pagesHandler } from './pages-server.mjs';

const ROOT = process.argv[2], DATA = process.argv[3], SHOT = process.argv[4];
const PORT = 8123 + Math.floor(Math.random() * 500), DBG = 9300 + Math.floor(Math.random() * 500);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.dll': 'application/octet-stream', '.csv': 'text/csv', '.txt': 'text/plain', '.gz': 'application/gzip', '.svg': 'image/svg+xml', '.dat': 'application/octet-stream', '.bin': 'application/octet-stream' };
const send404 = res => { res.writeHead(404); res.end(); };
const server = http.createServer(pagesHandler(ROOT, DATA)).listen(PORT);   // Pages-shaped: folder index, 404.html bounce, the Start button pressed for us (see pages-server.mjs)

const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'tc-edge-' + DBG), '--no-first-run', '--disable-extensions', '--window-size=1100,1000', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (let i = 0; i < 60; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const tgt = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find(t => t.type === 'page');
const ws = new WebSocket(tgt.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map(); const problems = [];
ws.addEventListener('message', ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
  if (m.method === 'Runtime.exceptionThrown') problems.push('PAGE-EXC ' + JSON.stringify(m.params.exceptionDetails).slice(0, 400));
});
const sendCdp = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async expr => { const r = await sendCdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); if (r.result.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400)); return r.result.result.value; };
await sendCdp('Runtime.enable'); await sendCdp('Page.enable'); await sendCdp('Network.enable'); await sendCdp('Network.setCacheDisabled', { cacheDisabled: true });
async function waitFor(expr, ms, what) { const t = Date.now(); while (Date.now() - t < ms) { try { if (await ev(expr)) return Date.now() - t; } catch { } await sleep(100); } throw new Error('timeout: ' + what); }
const setInput = (sel, v) => ev(`(() => { const i = document.querySelector(${JSON.stringify(sel)}); const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value'); d.set.call(i, ${JSON.stringify(v)}); i.dispatchEvent(new Event('input', { bubbles: true })); })()`);
const go = async p => { await sendCdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/tools/${p}` }); };

try {
  // ---- Nano Stories: one story per variety; the 448-step cap plus the clamped prompt must never re-prime ----
  await go('stories');
  await waitFor(`!!document.querySelector('.prompt-input') || !!document.querySelector('.err')`, 300000, 'stories load');
  for (const v of ['Focused', 'Balanced', 'Wild']) {
    await ev(`[...document.querySelectorAll('.variety-btn')].find(b => b.textContent.trim() === ${JSON.stringify(v)}).click()`);
    await sleep(150);
    const t = Date.now();
    await ev(`[...document.querySelectorAll('.ns-controls .go')][0].click()`);
    await sleep(300);
    await waitFor(`(document.querySelector('.ns-controls .go')?.textContent || '').trim() !== 'writing…'`, 400000, 'story ' + v);
    const text = await ev(`document.querySelector('.story-text')?.textContent ?? ''`);
    console.log(`stories ${v.padEnd(8)} ${((Date.now() - t) / 1000).toFixed(1).padStart(6)} s  ${String(text.length).padStart(5)} chars  ${JSON.stringify(text.slice(0, 80))}`);
  }
  // ---- Cartographer: 100 boundaries ----
  await go('cartographer');
  await waitFor(`!!document.querySelector('.cg-input') || !!document.querySelector('.err')`, 300000, 'cartographer load');
  console.log('cartographer stats: ' + await ev(`[...document.querySelectorAll('.cg-stats .stat')].map(s => s.innerText.replace(/\\n/g, ' ')).join(' | ')`));
  await setInput('.cg-input', 'Once upon a time there was a little girl named Lily who loved to play with her mom and the');
  await sleep(150);
  const t = Date.now();
  await ev(`document.querySelector('.cg-form .go').click()`);
  await waitFor(`!!document.querySelector('.cg-answer') || !!document.querySelector('.err')`, 300000, 'visualize');
  console.log(`cartographer visualize ${((Date.now() - t) / 1000).toFixed(1)} s`);
  console.log('cartographer counts: ' + JSON.stringify(await ev(`({ answer: document.querySelector('.cg-answer-word')?.textContent, trajectoryPoints: document.querySelectorAll('.cg-traj, .cg-crystal').length, trajLabels: document.querySelectorAll('.cg-traj-label').length, attnLines: document.querySelectorAll('.cg-attn').length, cloudDots: document.querySelectorAll('.cg-dot').length, cloudLabels: document.querySelectorAll('.cg-cloud-label').length, tableRows: document.querySelectorAll('.cg-table tbody tr').length, honesty: document.querySelector('.cg-honesty p')?.textContent.replace(/\\s+/g, ' ').trim().slice(0, 200), truncatedNote: !!document.querySelector('.cg-answer ~ .hint:not(:first-of-type)') })`)));
  if (SHOT) {
    await ev(`document.querySelector('.cg-panel').scrollIntoView()`);
    await sleep(500);
    const r = await sendCdp('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(SHOT, Buffer.from(r.result.data, 'base64'));
    console.log('screenshot ' + SHOT);
  }
} catch (e) { problems.push(String(e)); }
console.log(problems.length ? 'RESULT: PROBLEMS\n' + problems.join('\n') : 'RESULT: OK');
ws.close(); edge.kill(); server.close(); process.exit(problems.length ? 1 : 0);
