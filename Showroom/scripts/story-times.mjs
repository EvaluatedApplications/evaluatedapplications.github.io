// Headless-Edge timing of Nano Stories on a PUBLISHED Showroom build: wall time and length per story, per Variety preset.
// Same serving layout as chat-turns.mjs / boot-check.mjs: <published wwwroot> under /tools/, <website-data dir> under /website-data/, one origin.
// Sound is off (the default), so this is the unpaced path a visitor gets; "Skip to the end" is not pressed.
// Also records the worst gap between page repaints while a story streams (a long gap = the tab was frozen that long).
// Usage: node story-times.mjs <published wwwroot> <website-data dir> [storiesPerPreset=3] [timeoutSeconds=300] [prompt]
// Needs Node 22+ and Edge at the path below.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pagesHandler } from './pages-server.mjs';

const ROOT = process.argv[2], DATA = process.argv[3], PER = Number(process.argv[4] ?? 3), TIMEOUT = Number(process.argv[5] ?? 300) * 1000, PROMPT = process.argv[6] ?? 'Once upon a time,';
const PORT = 8123 + Math.floor(Math.random() * 500), DBG = 9300 + Math.floor(Math.random() * 500);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.dll': 'application/octet-stream', '.csv': 'text/csv', '.txt': 'text/plain', '.gz': 'application/gzip', '.svg': 'image/svg+xml', '.dat': 'application/octet-stream', '.bin': 'application/octet-stream' };
const send404 = res => { res.writeHead(404); res.end(); };
const server = http.createServer(pagesHandler(ROOT, DATA)).listen(PORT);   // Pages-shaped: folder index, 404.html bounce, the Start button pressed for us (see pages-server.mjs)

const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'st-edge-' + DBG), '--no-first-run', '--disable-extensions', '--window-size=1000,900', 'about:blank'], { stdio: 'ignore' });
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
await sendCdp('Runtime.enable'); await sendCdp('Page.enable'); await sendCdp('Network.enable');
await sendCdp('Network.setCacheDisabled', { cacheDisabled: true });
async function waitFor(expr, ms, what) { const t = Date.now(); while (Date.now() - t < ms) { try { if (await ev(expr)) return Date.now() - t; } catch { } await sleep(100); } throw new Error('timeout: ' + what); }

const rows = [];
try {
  await sendCdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/tools/stories` });
  await waitFor(`!!document.querySelector('.cr-stats .stat .n') || !!document.querySelector('.err')`, 300000, 'checkpoint load');
  if (await ev(`!!document.querySelector('.err')`)) throw new Error('page shows error: ' + await ev(`document.querySelector('.err').textContent`));
  console.log('loaded: ' + await ev(`[...document.querySelectorAll('.cr-stats .stat')].map(s => s.innerText.replace(/\\n/g, ' ')).join(' | ')`));
  // a page-side watchdog: requestAnimationFrame gaps while a story streams (the largest gap is the longest the tab was unresponsive)
  await ev(`(() => { window.__gap = 0; let last = performance.now(); const tick = t => { window.__gap = Math.max(window.__gap, t - last); last = t; requestAnimationFrame(tick); }; requestAnimationFrame(tick); })()`);
  await ev(`(() => { const i = document.querySelector('.prompt-input'); const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value'); d.set.call(i, ${JSON.stringify(PROMPT)}); i.dispatchEvent(new Event('input', { bubbles: true })); })()`);
  await sleep(150);
  for (const preset of ['Focused', 'Balanced', 'Wild']) {
    await ev(`[...document.querySelectorAll('.variety-btn')].find(b => b.textContent.trim() === ${JSON.stringify(preset)}).click()`);
    await sleep(150);
    for (let n = 1; n <= PER; n++) {
      await ev(`window.__gap = 0`);
      const t = Date.now();
      await ev(`document.querySelector('.ns-controls .go').click()`);
      await waitFor(`/writing/.test(document.querySelector('.ns-controls .go')?.textContent || '')`, 10000, `story ${preset} #${n} start`);
      await waitFor(`/^Tell me another$/.test((document.querySelector('.ns-controls .go')?.textContent || '').trim())`, TIMEOUT, `story ${preset} #${n}`);
      const secs = (Date.now() - t) / 1000;
      const body = await ev(`document.querySelector('.story-body')?.textContent ?? ''`);
      const paras = body.split('\n\n').length;
      const seedLine = await ev(`document.querySelector('.seed-line')?.textContent ?? ''`);
      const gap = await ev(`window.__gap`);
      rows.push({ preset, n, secs, chars: body.length, paras, gap });
      console.log(`${preset.padEnd(8)} #${n}  ${secs.toFixed(1).padStart(6)} s  ${String(body.length).padStart(4)} chars  ${paras} para  worst frame gap ${Math.round(gap)} ms  [${seedLine}]`);
      console.log('    ' + JSON.stringify(PROMPT + body).slice(0, 400));
    }
  }
} catch (e) { problems.push(String(e)); }
if (rows.length) {
  const m = rows.reduce((a, r) => a + r.secs, 0) / rows.length;
  console.log(`stories ${rows.length}: wall mean ${m.toFixed(1)} s, max ${Math.max(...rows.map(r => r.secs)).toFixed(1)} s; mean ${Math.round(rows.reduce((a, r) => a + r.chars, 0) / rows.length)} chars; worst frame gap ${Math.round(Math.max(...rows.map(r => r.gap)))} ms`);
}
console.log(problems.length ? 'RESULT: PROBLEMS\n' + problems.join('\n') : 'RESULT: OK');
ws.close(); edge.kill(); server.close(); process.exit(problems.length ? 1 : 0);
