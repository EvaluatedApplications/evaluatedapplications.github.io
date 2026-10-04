// Headless-Edge CONTENT check of the audit-session-52/53 page changes on a PUBLISHED build (no throttle, no timings): prints the text a visitor reads for
// the twins check (groups, extra copies, the rule as the file states it), the rules disclosures (one per check), the within-transaction line for Wokingham, a debt-sink row
// with undated rows, a Schedule A row (gap wording, SmallGap) with its letter line, the budget test prose and sets, and the budget status of Wirral 2022-23 and York.
// Usage: node rules-check.mjs <published wwwroot> [<website-data dir>]     (same server as council-perf.mjs; needs Node 22+ and Edge)
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const ROOT = process.argv[2];
const DATA = process.argv[3] ?? 'C:\\Users\\dongy\\website-data';
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
  res.writeHead(200, { 'Content-Type': types[path.extname(f)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
  fs.createReadStream(f).pipe(res);
}).listen(PORT);

const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'cw-edge-' + DBG), '--no-first-run', '--disable-extensions', '--window-size=412,915', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (let i = 0; i < 60; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const targets = await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json();
const ws = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map();
ws.addEventListener('message', ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  else if (m.method === 'Runtime.exceptionThrown') console.log('PAGE-EXC ' + JSON.stringify(m.params.exceptionDetails).slice(0, 700));
});
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async expr0 => { const expr = /\bawait\b/.test(expr0) && !expr0.trimStart().startsWith('(async') ? '(async () => { ' + expr0 + ' })()' : expr0; const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); if (r.result.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400)); return r.result.result.value; };
await send('Runtime.enable'); await send('Page.enable');
await send('Page.addScriptToEvaluateOnNewDocument', { source: `
  window.__waitFor = (sel, text, timeout) => new Promise((res, rej) => { const t0 = performance.now(); const tick = () => { const el = document.querySelector(sel); if (el && (!text || el.textContent.includes(text))) return res(performance.now() - t0); if (performance.now() - t0 > timeout) return rej(new Error('timeout waiting for ' + sel + ' ' + (text||''))); setTimeout(tick, 15); }; tick(); });
  window.__waitLoaded = (timeout) => new Promise((res, rej) => { const t0 = performance.now(); const tick = () => { const b = document.querySelector('.loadbtn'); const n = document.querySelectorAll('.loaded .yblock').length; if (b && !b.disabled && !document.querySelector('.loadprog') && n > 0) return res(n); if (performance.now() - t0 > timeout) return rej(new Error('timeout loading')); setTimeout(tick, 25); }; tick(); });
  window.__yearItem = (fy) => [...document.querySelectorAll('.loadsec .yitem')].find(x => x.querySelector('.ytitle').textContent === fy);
  window.__tickYear = (fy) => { const li = window.__yearItem(fy); const cb = li.querySelector('.ycheck input'); if (!cb.checked) cb.click(); };
  window.__check = (title) => [...document.querySelectorAll('section.check')].find(s => s.querySelector('h3').textContent.startsWith(title));
` });
const txt = (sel) => ev(`[...document.querySelectorAll(${JSON.stringify(sel)})].map(e => e.innerText.replace(/\\s+/g, ' ').trim()).join('\\n   ')`);
const say = (h, t) => console.log(`\n== ${h}\n   ${t}`);
async function waitBoot() { for (let i = 0; i < 600; i++) { if (await ev(`!!document.querySelector('.room')`)) return; await sleep(200); } throw new Error('boot timeout'); }
const go = async (url, waitSel) => { await ev(`history.pushState({}, '', '/tools/${url}'); window.dispatchEvent(new PopStateEvent('popstate')); await window.__waitFor('${waitSel}', '', 30000)`); };
// open one check (by the start of its title) and its rules disclosure; print what a visitor reads
async function check(title, what = []) {
  await ev(`(async () => { const s = window.__check(${JSON.stringify(title)}); s.querySelector('.check-head').click(); for (let i = 0; i < 2000 && !s.querySelector('.summary'); i++) await new Promise(r => setTimeout(r, 15)); await new Promise(r => setTimeout(r, 200)); })()`);
  const sec = `window.__check(${JSON.stringify(title)})`;
  say(title + ': plain', await ev(`${sec}.querySelector('.plain').innerText`));
  say(title + ': summary + captions', await ev(`[...${sec}.querySelectorAll('.summary, .caption')].map(e => e.innerText.replace(/\\s+/g, ' ').trim()).join('\\n   ')`));
  await ev(`${sec}.querySelector('.rulebtn').click(); await new Promise(r => setTimeout(r, 400))`);
  say(title + ': rules disclosure', await ev(`[...${sec}.querySelectorAll('.rules li')].map(e => e.innerText.replace(/\\s+/g, ' ').trim()).join('\\n   ') || '(none shown) ' + (${sec}.querySelector('.rulebox')?.innerText ?? 'no rulebox')`));
  for (const w of what) say(title + ': ' + w.name, await ev(w.js.replace('SEC', sec)));
}

await send('Page.navigate', { url: `http://127.0.0.1:${PORT}/tools/council-spending` });
await waitBoot();
await ev(`window.__waitFor('.cross .check', '', 30000)`);

// ---- hub: twins (all councils), within-transaction (the not-run line), the budget test panel
await check('Identical lines under two transaction numbers', [{ name: 'first two rows', js: `[...SEC.querySelectorAll('.lines li')].slice(0, 2).map(e => e.innerText.replace(/\\s+/g, ' ').trim()).join('\\n   ')` }]);
await check('The same line repeated inside one transaction');
say('hub: budget test panel', await ev(`(async () => { const b = [...document.querySelectorAll('.cross .budget')].find(s => s.querySelector('h2')?.textContent.startsWith('Did the test')); b.querySelector('.check-head').click(); await window.__waitFor('.cross .budget table.tests', '', 30000); return b.innerText.replace(/\\s+/g, ' '); })()`));

// ---- Wokingham: twins + within-transaction + debt sink, Schedule A, the tray letter
await go('council-spending/wokingham', '.about');
say('wokingham: within-transaction (single council)', await ev(`(async () => { const s = window.__check('The same line repeated inside one transaction'); s.querySelector('.check-head').click(); await window.__waitFor('section.check .summary', 'not run', 30000).catch(() => {}); await new Promise(r => setTimeout(r, 300)); return s.innerText.replace(/\\s+/g, ' '); })()`));
say('wokingham: debt sink rows with undated lines', await ev(`(async () => { const s = window.__check('Money paid out to other councils'); s.querySelector('.check-head').click(); for (let i = 0; i < 2000 && !s.querySelector('.summary'); i++) await new Promise(r => setTimeout(r, 15)); await new Promise(r => setTimeout(r, 300)); return [...s.querySelectorAll('.lines li')].filter(l => l.innerText.includes('no published payment date')).slice(0, 2).map(l => l.innerText.replace(/\\s+/g, ' ')).join('\\n   ') || '(no row mentions undated payments)'; })()`));
await ev(`window.__tickYear('2023-24'); document.querySelector('.loadbtn').click(); await window.__waitLoaded(120000)`);
say('wokingham 2023-24: Schedule A block', await ev(`(async () => { const s = [...document.querySelectorAll('.yblock .sched')].find(x => x.querySelector('h4').textContent.startsWith('A payment')); s.querySelector('.rulebtn').click(); await new Promise(r => setTimeout(r, 400)); return s.querySelector('.plain').innerText + '\\n   RULES: ' + [...s.querySelectorAll('.rules li')].map(e => e.innerText.replace(/\\s+/g, ' ')).join(' | ') + '\\n   BUCKETS: ' + [...s.querySelectorAll('.bucket-head')].map(b => b.innerText.replace(/\\s+/g, ' ')).join(' | '); })()`));
say('wokingham 2023-24: first rows of the first Schedule A bucket + its letter line', await ev(`(async () => { const s = [...document.querySelectorAll('.yblock .sched')].find(x => x.querySelector('h4').textContent.startsWith('A payment')); s.querySelector('.bucket-head').click(); await window.__waitFor('.yblock .sched .lines li', '', 30000); const li = s.querySelector('.lines li'); const t = li.innerText.replace(/\\s+/g, ' '); li.querySelector('.tick').click(); await new Promise(r => setTimeout(r, 200)); document.querySelector('.foi-fab')?.click(); await window.__waitFor('.foi-letter', '', 30000); const L = document.querySelector('.foi-letter').value; const i = L.indexOf('gross'); return t + '\\n   LETTER: ' + L.slice(Math.max(0, i - 220), i + 260).replace(/\\s+/g, ' '); })()`));

// ---- budget: Wirral 2022-23 (eligible after its freeze) and York (government figures now held)
for (const [slug, yr] of [['wirral', '2022-23'], ['york', '2024-25']]) {
  await go('council-spending/' + slug, '.about');
  say(slug + ': what cannot be checked', await ev(`[...document.querySelectorAll('.about .notelist li')].map(e => e.innerText).filter(t => /declared-spend|Revenue Outturn/.test(t)).join('\\n   ') || '(no budget limit stated)'`));
  say(slug + ' ' + yr + ': budget status', await ev(`(async () => { const b = [...document.querySelectorAll('section.budget')].find(s => s.querySelector('h2')?.textContent.startsWith('Declared spend against')); b.querySelector('.check-head').click(); await window.__waitFor('section.budget .year', '', 30000); await new Promise(r => setTimeout(r, 300)); const y = [...b.querySelectorAll('.year')].find(e => e.innerText.includes('${yr}')); y.querySelector('.bucket-head').click(); await new Promise(r => setTimeout(r, 200)); return y.querySelector('.plain').innerText.replace(/\\s+/g, ' ') + '\\n   RULES BUTTON: ' + (b.querySelector('.rulebtn')?.innerText ?? 'none'); })()`));
}
edge.kill(); server.close(); process.exit(0);
