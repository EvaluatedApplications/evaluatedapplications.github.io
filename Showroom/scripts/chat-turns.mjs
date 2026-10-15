// Headless-Edge timing of a multi-turn Prism chat on a PUBLISHED Showroom build (the 2026-10-04 "turn 7 took 163 s" regression check).
// Same serving layout as boot-check.mjs: <published wwwroot> under /tools/, <website-data dir> under /website-data/, one origin.
// Sends N chat turns on /tools/prism, waits for each reply, prints the wall time and reply length per turn, and exits 1 if any turn is slower than MAXSEC.
// Usage: node chat-turns.mjs <published wwwroot> <website-data dir> [turns=10] [maxSeconds=6] [turnTimeoutSeconds=600]
// Needs Node 22+ and Edge at the path below.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pagesHandler } from './pages-server.mjs';

const ROOT = process.argv[2], DATA = process.argv[3], TURNS = Number(process.argv[4] ?? 10), MAXSEC = Number(process.argv[5] ?? 6), TIMEOUT = Number(process.argv[6] ?? 600) * 1000;
const PORT = 8123 + Math.floor(Math.random() * 500), DBG = 9300 + Math.floor(Math.random() * 500);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.dll': 'application/octet-stream', '.csv': 'text/csv', '.txt': 'text/plain', '.gz': 'application/gzip', '.svg': 'image/svg+xml', '.dat': 'application/octet-stream', '.bin': 'application/octet-stream' };
const send404 = res => { res.writeHead(404); res.end(); };
const server = http.createServer(pagesHandler(ROOT, DATA)).listen(PORT);   // Pages-shaped: folder index, 404.html bounce, the Start button pressed for us (see pages-server.mjs)

const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'ct-edge-' + DBG), '--no-first-run', '--disable-extensions', '--window-size=1000,900', 'about:blank'], { stdio: 'ignore' });
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

// Realistic chat lengths: at ~1.15 chars/token a 70-90 character message is 60-80 tokens, so a handful of turns fills a 512-token window (short
// prompts hide the regression: it needs history + reply to reach Context).
const prompts = ['hello there, my name is Sam and I would like to know who you are and what you can do', 'what is your favourite colour, and can you tell me why you like it so much?',
  'tell me about a small brown dog that lived near a river and loved to chase the ducks', 'where do you live, and do you ever get lonely when nobody comes to talk to you?',
  'what do you like to eat for breakfast when the weather is cold and the morning is dark?', 'can you count from one to five and then tell me what comes after the number five?',
  'why is the sky blue in the day time and why does it turn red when the sun goes down?', 'what did we talk about so far, can you remind me of the first thing I asked you?',
  'do you like stories about brave children who go on a long trip to find a lost toy?', 'what is two plus two, and what is that number plus three more as well?',
  'tell me something about the sea and the big grey boats that sail on it all year long', 'it was nice to talk with you today, is there anything you would like to ask me?'];
// PROMPT_SET=mid: ~95-105 character messages (about 85 tokens). With replies averaging 25-30 tokens, three exchanges plus the new message land the prompt near
// 480 tokens, i.e. inside the band, nearly every turn from turn 4. This is the probe that reproduces the regression.
if (process.env.PROMPT_SET === 'mid') prompts.splice(0, prompts.length,
  'hello there, my name is Sam and I would like to know who you are and what you can do for me today',
  'what is your favourite colour, and can you tell me why you like it so much, and when you first saw it?',
  'tell me about a small brown dog that lived near a river and loved to chase the ducks all day long',
  'where do you live, and do you ever get lonely when nobody comes to talk to you late in the evening?',
  'what do you like to eat for breakfast when the weather is cold and the morning is still very dark?',
  'can you count from one to five and then tell me what comes after the number five in the same way?',
  'why is the sky blue in the day time and why does it turn red when the sun goes down in the west?',
  'what did we talk about so far, can you remind me of the first thing I asked you at the start?',
  'do you like stories about brave children who go on a long trip to find a toy that got lost one day?',
  'what is two plus two, and what is that number plus three more, and then take away one at the end?',
  'tell me something about the sea and the big grey boats that sail on it all year long without rest',
  'it was nice to talk with you today, is there anything you would like to ask me before I go away?');
// PROMPT_SET=mix: seeded pseudo-random messages of 60-138 characters (the input's maxlength is 140), so the prompt size sweeps the whole 350-512 range over many
// turns and some turn lands in the band whatever the replies do. Use with 16+ turns.
if (process.env.PROMPT_SET === 'mix') {
  const words = 'the a small big brown dog cat river sea boat sky blue red sun moon tell me about you can and what why where do like eat play said went home friend toy lost found long short day night happy sad run jump'.split(' ');
  let seed = 12345; const rnd = () => (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
  prompts.length = 0;
  for (let i = 0; i < 40; i++) { const target = 60 + Math.floor(rnd() * 78); let s = ''; while (s.length < target) s += (s ? ' ' : '') + words[Math.floor(rnd() * words.length)]; prompts.push(s.slice(0, 138)); }
}
// PROMPT_SET=long: ~110-130 character messages. Three such exchanges (message + reply + tags ~ 160 tokens each) leave the prompt at 440-512 tokens, the band where the
// pre-fix page filled the cache mid-reply and re-primed every remaining step. Which turn lands in that band depends on reply lengths, so this is a probe, not a guarantee.
if (process.env.PROMPT_SET === 'long') prompts.splice(0, prompts.length,
  'hello there, my name is Sam and I would like to know who you are, what you can do, and where you come from before we begin to talk',
  'what is your favourite colour, and can you tell me why you like it so much, and whether it is the same colour as the sky today?',
  'tell me about a small brown dog that lived near a river and loved to chase the ducks, and what happened to him on the very last day',
  'where do you live, and do you ever get lonely when nobody comes to talk to you, and what do you do all day while you are waiting?',
  'what do you like to eat for breakfast when the weather is cold and the morning is dark, and do you ever share it with a friend?',
  'can you count from one to five and then tell me what comes after the number five, and what comes after that number as well?',
  'why is the sky blue in the day time and why does it turn red when the sun goes down, and where does all the blue go at night?',
  'what did we talk about so far, can you remind me of the first thing I asked you, and the second thing, and the third one too?',
  'do you like stories about brave children who go on a long trip to find a lost toy, and do they ever manage to bring it home safe?',
  'what is two plus two, and what is that number plus three more as well, and then if you take away one how many are left in the end?');
const times = [];
try {
  await sendCdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/tools/prism` });
  await waitFor(`!!document.querySelector('.cr-stats .stat .n') || !!document.querySelector('.err')`, 300000, 'checkpoint load');
  if (await ev(`!!document.querySelector('.err')`)) throw new Error('page shows error: ' + await ev(`document.querySelector('.err').textContent`));
  console.log('loaded: ' + await ev(`[...document.querySelectorAll('.cr-stats .stat')].map(s => s.innerText.replace(/\\n/g, ' ')).join(' | ')`));
  for (let n = 1; n <= TURNS; n++) {
    const before = await ev(`document.querySelectorAll('.or-turn.prism:not(.pending)').length`);
    const msg = prompts[(n - 1) % prompts.length];
    await ev(`(() => { const i = document.querySelector('.or-input'); const d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value'); d.set.call(i, ${JSON.stringify(msg)}); i.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await sleep(150);
    const t = Date.now();
    await ev(`document.querySelector('.or-input-row .go').click()`);
    await sleep(100);
    await waitFor(`(document.querySelector('.or-input-row .go')?.textContent || '').trim() === 'Send' && document.querySelectorAll('.or-turn.prism:not(.pending)').length > ${before}`, TIMEOUT, `reply to turn ${n}`);
    const secs = (Date.now() - t) / 1000;
    const turns = await ev(`[...document.querySelectorAll('.or-turn.prism:not(.pending) .or-text')].map(e => e.textContent)`);
    const reply = turns[turns.length - 1] ?? '';
    times.push(secs);
    console.log(`turn ${String(n).padStart(2)}  ${secs.toFixed(1).padStart(7)} s  ${String(reply.length).padStart(4)} chars  you: ${JSON.stringify(msg)}  prism: ${JSON.stringify(reply.slice(0, 90))}`);
  }
} catch (e) { problems.push(String(e)); }
console.log(`max turn ${Math.max(0, ...times).toFixed(1)} s, mean ${(times.reduce((a, b) => a + b, 0) / Math.max(1, times.length)).toFixed(1)} s over ${times.length} turns`);
const slow = times.map((s, i) => [s, i + 1]).filter(([s]) => s > MAXSEC);
if (slow.length) problems.push('turns over ' + MAXSEC + ' s: ' + slow.map(([s, i]) => `#${i} ${s.toFixed(1)} s`).join(', '));
console.log(problems.length ? 'RESULT: PROBLEMS\n' + problems.join('\n') : 'RESULT: OK');
ws.close(); edge.kill(); server.close(); process.exit(problems.length ? 1 : 0);
