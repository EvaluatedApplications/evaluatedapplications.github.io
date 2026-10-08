// Behavioural check of the static-shell build (Blazier), in headless Edge against a Pages-shaped local server (pages-server.mjs).
//   node shell-check.mjs <published wwwroot (dist)> <website-data dir>
// What it proves, each as PASS/FAIL (exit 1 on any FAIL):
//   1 gallery        /tools/ is real HTML (h1 + 8 cards) and downloads no runtime file at all, even after a scroll and a tap on text
//   2 shell          /tools/prism/ opened directly (no referrer): content + start button, ZERO runtime requests while the visitor only reads/scrolls
//   3 start          pressing the button boots the app over the shell and the tool works (Prism's stats appear)
//   4 in-app nav     from the running Prism, the page's own link to Nano Stories is a client-side navigation: the page is NOT reloaded
//                    (a window marker survives), the checkpoint is NOT fetched again (SessionHost shared), and Stories shows its UI
//   5 hard nav       typing /tools/stories directly is a new page load: marker gone, the checkpoint IS fetched again
//   6 from-site      following a link from another page of the site to a tool shell starts the app without a button press
//   7 bounce         /tools/council-spending/wokingham (no shell) goes through 404.html, keeps its URL, boots, and shows the council page
//   8 not found      an unknown /tools/ path boots the app and shows its not-found page
import http from 'node:http';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pagesHandler } from './pages-server.mjs';

const ROOT = process.argv[2], DATA = process.argv[3];
const PORT = 8100 + Math.floor(Math.random() * 400), DBG = 9300 + Math.floor(Math.random() * 500);
http.createServer(pagesHandler(ROOT, DATA, { autoStart: false })).listen(PORT);
const origin = `http://127.0.0.1:${PORT}`;
const edge = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  ['--headless=new', `--remote-debugging-port=${DBG}`, '--user-data-dir=' + path.join(process.env.TEMP, 'sc-edge-' + DBG), '--no-first-run', '--disable-extensions', '--window-size=412,915', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (let i = 0; i < 80; i++) { try { await (await fetch(`http://127.0.0.1:${DBG}/json/version`)).json(); break; } catch { await sleep(250); } }
const tgt = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find(t => t.type === 'page');
const ws = new WebSocket(tgt.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map(); let reqs = []; const exceptions = [];
ws.addEventListener('message', ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
  if (m.method === 'Network.requestWillBeSent') reqs.push({ url: m.params.request.url, t: Date.now() });
  else if (m.method === 'Runtime.exceptionThrown') exceptions.push(JSON.stringify(m.params.exceptionDetails).slice(0, 300));
});
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async expr => { const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };
await send('Runtime.enable'); await send('Page.enable'); await send('Network.enable');
await send('Network.setCacheDisabled', { cacheDisabled: true });
await send('Emulation.setDeviceMetricsOverride', { width: 412, height: 915, deviceScaleFactor: 2, mobile: true });
await send('Emulation.setTouchEmulationEnabled', { enabled: true });

const poll = async (expr, limit = 240000) => { const s = Date.now(); while (Date.now() - s < limit) { try { if (await ev(`!!(${expr})`)) return Date.now() - s; } catch { } await sleep(100); } return null; };
const runtimeReqs = () => reqs.filter(r => /\/_framework\/(dotnet|System|Microsoft|Showroom|AlgFormer|HoloDb|blazor\.webassembly)/.test(r.url));
const brainFetches = () => reqs.filter(r => r.url.includes('oracle-brain.bin.gz')).length;
let fails = 0;
const check = (name, ok, detail = '') => { console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -  ' + detail : ''}`); if (!ok) fails++; };
const go = async url => { reqs = []; await send('Page.navigate', { url }); await poll(`document.readyState === 'complete'`, 30000); };

// 1 gallery
await go(origin + '/tools/');
await sleep(2500);
const cards = await ev(`document.querySelectorAll('.card.tool').length`), h1 = await ev(`document.querySelector('h1')?.textContent`);
await send('Input.synthesizeScrollGesture', { x: 200, y: 600, yDistance: -400, gestureSourceType: 'touch' });
await send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: 120, y: 300 }] }); await send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
await sleep(1500);
check('1 gallery is real HTML', cards >= 8 && /Touch the tech/.test(h1 ?? ''), `h1="${h1}" cards=${cards}`);
check('1 gallery downloads no runtime', runtimeReqs().length === 0, `${runtimeReqs().length} runtime requests, ${reqs.length} requests in all`);

// 2 shell opened directly
await go(origin + '/tools/prism');
await sleep(2500);
const url2 = await ev('location.pathname'), go2 = await ev(`!!document.querySelector('[data-blazier-go]')`), h12 = await ev(`document.querySelector('h1')?.textContent`);
await send('Input.synthesizeScrollGesture', { x: 200, y: 600, yDistance: -300, gestureSourceType: 'touch' });
await send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: 200, y: 200 }] }); await send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
await ev(`document.querySelector('.lede')?.click()`);
await sleep(2500);
check('2 shell shows content and a start button', url2 === '/tools/prism/' && go2 && h12 === 'Prism', `path=${url2} h1="${h12}" start button=${go2}`);
check('2 reading, scrolling and tapping text start nothing', runtimeReqs().length === 0, `${runtimeReqs().length} runtime requests`);

// 3 start
const tStart = Date.now();
await ev(`window.__marker = 1; document.querySelector('[data-blazier-go]').click()`);
const worked = await poll(`document.querySelector('.cr-stats .stat .n')`);
check('3 start boots the app and Prism works', worked != null, worked != null ? `${worked} ms after the press` : 'timed out');
check('3 the app is mounted over the shell', await ev(`document.querySelector('[data-blazier-island="app"]')?.getAttribute('data-blazier-state')`) === 'live');
const n1 = brainFetches();

// 4 in-app navigation
await ev(`document.querySelector('a[href="/tools/stories"]').click()`);
const stories = await poll(`document.querySelector('.ns-panel .prompt-input')`, 60000);
check('4 in-app link is client-side navigation', (await ev('window.__marker')) === 1 && (await ev('location.pathname')) === '/tools/stories', `marker=${await ev('window.__marker')} path=${await ev('location.pathname')}`);
check('4 Stories reused the loaded model', stories != null && brainFetches() === n1 && n1 === 1, `checkpoint fetches: ${n1} before, ${brainFetches()} after; Stories UI after ${stories} ms`);

// 5 hard navigation
await go(origin + '/tools/stories');
await ev(`document.querySelector('[data-blazier-go]').click()`);
const stories2 = await poll(`document.querySelector('.ns-panel .prompt-input')`);
check('5 hard navigation is a fresh page load', (await ev('window.__marker')) === undefined && stories2 != null && brainFetches() === 1, `marker=${await ev('window.__marker')} checkpoint fetched again: ${brainFetches()}x; working ${stories2} ms after the press`);

// 6 from another page of the site: the referrer is this origin
await go(origin + '/tools/');
reqs = [];
await ev(`document.querySelector('a[href^="/tools/forecaster"]').click()`);
const fc = await poll(`document.querySelector('.fc-stage')`);
check('6 a link from the site starts the app without a button press', fc != null && (await ev('location.pathname')) === '/tools/forecaster/', `Forecaster working ${fc} ms after the click`);

// 7 bounce for a path with no shell
await go(origin + '/tools/council-spending/wokingham');
const council = await poll(`document.querySelector('.room.cs h1') && /Wokingham/i.test(document.querySelector('.room.cs h1').textContent)`);
check('7 deep link without a shell boots and keeps its URL', council != null && (await ev('location.pathname')) === '/tools/council-spending/wokingham', `path=${await ev('location.pathname')} h1="${await ev(`document.querySelector('.room.cs h1')?.textContent`)}"`);

// 8 unknown path
await go(origin + '/tools/no-such-tool');
const nf = await poll(`/Not Found/i.test(document.body.innerText)`, 120000);
check('8 unknown path shows the not-found page', nf != null, `path=${await ev('location.pathname')}`);

check('no uncaught page exceptions', exceptions.length === 0, exceptions.slice(0, 2).join(' | '));
console.log(fails ? `RESULT: ${fails} FAILED` : 'RESULT: ALL PASS');
ws.close(); edge.kill(); process.exit(fails ? 1 : 0);
