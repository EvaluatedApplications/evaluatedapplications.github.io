#!/usr/bin/env node
// Dry run of the cutover: assembles the final deploy tree in a temp folder exactly as the new deploy.yml would (see web/CUTOVER.md,
// section 1), serves it, and checks the old site against it.
//   node web/scripts/cutover-dry-run.mjs [--keep]
// Assembly order (later steps win):  Showroom/dist -> _site/tools   then   site-next -> _site   then   legacy assets and council-live.
// Checks: every URL in the OLD sitemap resolves (200, or a redirect stub whose target is 200); every internal link in every OLD page
// resolves; every tool shell loads in Edge with no console error and shows its static HTML; the legacy stylesheet and the token
// imports the tool shells need are served; the boot shell /tools/app/ is noindex and starts nothing by itself; /tools/nonexistent/ stays on the
// 404 page; and a BOOT TEST: /tools/council-spending/<first slug in website-data/council-web/index.csv> (no file) is bounced through 404.html to
// /tools/app/?/..., the app boots in Edge from the runtime in C:\Users\dongy\website-data (override: WEBSITE_DATA=<dir>), the address bar is
// restored to the original path and the council page is live with no console error. Exit code 1 on any failure.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { ROOT, OUT } from './lib/data.mjs';
import { walk } from './lint-voice.mjs';
import { launch } from './lib/cdp.mjs';

const SITE = path.join(ROOT, 'site'), DIST = path.join(ROOT, 'Showroom', 'dist'), TOKENS = path.join(ROOT, 'SiteKit', 'tokens');
const T = fs.mkdtempSync(path.join(os.tmpdir(), 'ea-deploy-'));
const cp = (from, to) => fs.cpSync(from, to, { recursive: true });

// ---- 1. assemble (mirrors the deploy workflow) ----
fs.mkdirSync(path.join(T, 'tools'), { recursive: true });
cp(DIST, path.join(T, 'tools'));                                             // Showroom's pre-built dist under /tools
cp(OUT, T);                                                                  // the generated site wins at the root, including /tools/index.html (the gallery)
cp(path.join(SITE, 'council-live'), path.join(T, 'council-live'));           // unlinked client page, unchanged
fs.mkdirSync(path.join(T, 'assets'), { recursive: true });
fs.copyFileSync(path.join(SITE, 'assets', 'site.css'), path.join(T, 'assets', 'site.css'));   // legacy stylesheet the tool shells still link
cp(TOKENS, path.join(T, 'SiteKit', 'tokens'));                              // imported by that stylesheet
const kept = fs.existsSync(path.join(T, '.nojekyll'));
console.log(`assembled ${walk(T).length} files in ${T} (.nojekyll ${kept ? 'present' : 'MISSING'})`);

const fails = [], warns = [];
const fail = (m) => fails.push(m), warn = (m) => warns.push(m);
if (!kept) fail('.nojekyll missing from the deploy tree');
if (fs.existsSync(path.join(DIST, 'index.html'))) fail('Showroom/dist/index.html exists: /tools/ belongs to the generated gallery (Showroom\'s boot shell is dist/app/index.html)');
if (!fs.existsSync(path.join(DIST, 'app', 'index.html'))) fail('Showroom/dist/app/index.html (the boot shell for bounced deep links) is missing');

// The deploy tree plus the second origin path the real site has: /website-data/ (the website-data repo's own Pages, same origin) which holds
// the Blazor runtime (_framework) and the council data. Shaped like Pages: a folder without its slash gets a 301, a miss gets the tree's 404.html with status 404.
const DATA = process.env.WEBSITE_DATA || 'C:\\Users\\dongy\\website-data';
const haveData = fs.existsSync(path.join(DATA, '_framework')) && fs.existsSync(path.join(DATA, 'council-web', 'index.csv'));
const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.txt': 'text/plain; charset=utf-8',
  '.xml': 'application/xml', '.svg': 'image/svg+xml', '.png': 'image/png', '.wasm': 'application/wasm', '.csv': 'text/csv', '.gz': 'application/gzip', '.dll': 'application/octet-stream' };
const reqLog = [];
async function serveTree(dir) {
  const http = await import('node:http');
  const server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x'), p = decodeURIComponent(u.pathname);
    reqLog.push(p);
    const root = p.startsWith('/website-data/') ? DATA : dir, rel = p.startsWith('/website-data/') ? p.slice('/website-data'.length) : p;
    let f = path.join(root, rel);
    if (!f.startsWith(root)) { res.writeHead(403).end(); return; }
    if (fs.existsSync(f) && fs.statSync(f).isDirectory()) {
      if (!p.endsWith('/')) { res.writeHead(301, { Location: p + '/' + u.search }); res.end(); return; }
      f = path.join(f, 'index.html');
    }
    if (!fs.existsSync(f) || !fs.statSync(f).isFile()) {
      if (p.startsWith('/website-data/')) { res.writeHead(404).end(); return; }
      const nf = path.join(dir, '404.html');
      res.writeHead(404, { 'content-type': MIME['.html'] }); res.end(fs.existsSync(nf) ? fs.readFileSync(nf) : 'not found'); return;
    }
    res.writeHead(200, { 'content-type': MIME[path.extname(f)] || 'application/octet-stream', 'cache-control': 'no-store' });
    fs.createReadStream(f).pipe(res);
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve({ server, port: server.address().port })));
}
const { server, port } = await serveTree(T);
const base = `http://127.0.0.1:${port}`;

// ---- resolution through the real server, following one redirect stub ----
const cache = new Map();
async function resolveUrl(p) {
  const key = p.split('#')[0].split('?')[0];
  if (cache.has(key)) return cache.get(key);
  const r = await fetch(base + key);
  let out = { status: r.status, via: null };
  if (r.status === 200 && /text\/html/.test(r.headers.get('content-type') || '')) {
    const t = await r.text();
    const m = t.match(/<body data-stub/) && t.match(/http-equiv="refresh" content="0; url=([^"]+)"/);
    if (m) { const tr = await fetch(base + m[1].split('#')[0]); out = { status: tr.status, via: m[1] }; }
  } else await r.arrayBuffer();
  cache.set(key, out);
  return out;
}

// ---- 2. the old sitemap ----
const smUrls = [...fs.readFileSync(path.join(SITE, 'sitemap.xml'), 'utf8').matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => new URL(m[1]).pathname);
let direct = 0, viaStub = 0;
for (const u of smUrls) {
  const r = await resolveUrl(u);
  if (r.status !== 200) fail(`old sitemap URL ${u} -> ${r.status}${r.via ? ' via ' + r.via : ''}`); else if (r.via) viaStub++; else direct++;
}
console.log(`old sitemap: ${smUrls.length} URLs; ${direct} resolve directly, ${viaStub} through a redirect stub, ${smUrls.length - direct - viaStub} fail`);

// ---- 3. every internal link and asset reference in every old page ----
const oldPages = walk(SITE).filter((f) => f.endsWith('.html'));
let linkCount = 0; const dead = new Map();
for (const f of oldPages) {
  const rel = path.relative(SITE, f).replace(/\\/g, '/');
  const pageUrl = '/' + (rel.endsWith('index.html') ? rel.slice(0, -'index.html'.length) : rel);
  const src = fs.readFileSync(f, 'utf8').replace(/<script[\s\S]*?<\/script>/g, '');
  for (const m of src.matchAll(/(?:href|src)="([^"]+)"/g)) {
    const h = m[1];
    if (/^(https?:|mailto:|tel:|data:|#|javascript:)/.test(h)) continue;
    const target = new URL(h, base + pageUrl).pathname;
    linkCount++;
    const r = await resolveUrl(target);
    if (r.status !== 200) { const k = target; if (!dead.has(k)) dead.set(k, []); dead.get(k).push(rel); }
  }
}
const oldExists = (target) => { const f = path.join(SITE, target); return (fs.existsSync(f) && fs.statSync(f).isFile()) || fs.existsSync(path.join(f, 'index.html')) || (target.startsWith('/tools/') && fs.existsSync(path.join(DIST, target.slice('/tools/'.length)))); };
const replaced = (rel) => fs.existsSync(path.join(OUT, rel));      // the new site has a page or a stub at that path
let alreadyBroken = 0, moot = 0;
for (const [t, from] of dead) {
  const pages = [...new Set(from)];
  if (!oldExists(t)) { alreadyBroken++; warn(`already dead on the live site: ${t} (from ${pages.slice(0, 2).join(', ')})`); }
  else if (pages.every(replaced)) moot++;                 // an asset used only by pages the cutover replaces
  else fail(`old link target ${t} no longer resolves and is used by a page that survives (${pages.join(', ')})`);
}
console.log(`old pages: ${oldPages.length} pages, ${linkCount} internal links and asset references checked, ${dead.size} unresolved targets (${alreadyBroken} already dead on the live site, ${moot} used only by pages the cutover replaces)`);

// ---- 4. tool shells in a browser ----
// the tool shells are Showroom's dist folders with an index.html, except app/, the boot shell for bounced deep links (checked on its own below)
const shells = fs.readdirSync(DIST, { withFileTypes: true }).filter((d) => d.isDirectory() && d.name !== 'app' && fs.existsSync(path.join(DIST, d.name, 'index.html'))).map((d) => d.name);
const css = await fetch(base + '/assets/site.css'), tok = await fetch(base + '/SiteKit/tokens/core.css');
if (css.status !== 200) fail('/assets/site.css (legacy stylesheet for the tool shells) is not served');
if (tok.status !== 200) fail('/SiteKit/tokens/core.css (imported by the legacy stylesheet) is not served');
const b = await launch();
let booted = 0;
try {
  for (const s of shells) {
    await b.goto(`${base}/tools/${s}/`, { width: 1280, height: 900 });
    await new Promise((r) => setTimeout(r, 400));
    const info = await b.evaluate('({h1:(document.querySelector("h1")||{}).textContent||"",len:document.body.innerText.trim().length,sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
    if (b.errors.length) fail(`tool shell /tools/${s}/ logged console errors: ${JSON.stringify(b.errors).slice(0, 160)}`);
    if (info.len < 150) fail(`tool shell /tools/${s}/ has almost no static text (${info.len} characters)`);
    if (info.sw > info.cw) fail(`tool shell /tools/${s}/ overflows horizontally`);
    if (info.len >= 150 && !b.errors.length) booted++;
  }
  // the generated gallery wins at /tools/
  await b.goto(`${base}/tools/`, { width: 1280, height: 900 });
  const gal = await b.evaluate('({gen:!!document.querySelector("meta[name=generator]")||!!document.querySelector("header.site-header"),h1:(document.querySelector("h1")||{}).textContent})');
  if (!gal.gen) fail('/tools/ is not the generated gallery (Showroom index.html won the copy)');
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const poll = async (expr, limit) => { const s = Date.now(); while (Date.now() - s < limit) { try { if (await b.evaluate(`!!(${expr})`)) return Date.now() - s; } catch { /* page navigating */ } await sleep(250); } return null; };

  // the boot shell opened directly: real static HTML, noindex, canonical to the gallery, nothing started
  await b.goto(`${base}/tools/app/`, { width: 1280, height: 900 });
  await sleep(800);
  const app = await b.evaluate('({h1:(document.querySelector("h1")||{}).textContent||"",robots:(document.querySelector("meta[name=robots]")||{}).content||"",canon:(document.querySelector("link[rel=canonical]")||{}).href||"",bounced:!!window.__eaBounced,live:!!document.querySelector("[data-blazier-state=live]")})');
  if (!/Starting the tool/.test(app.h1) || !/noindex/.test(app.robots) || !/\/tools\/$/.test(app.canon)) fail(`/tools/app/ is not the noindex boot shell: ${JSON.stringify(app)}`);
  if (app.bounced || app.live) fail('/tools/app/ opened directly started the app (it must wait for a bounce)');
  if (b.errors.length) fail(`/tools/app/ logged console errors: ${JSON.stringify(b.errors).slice(0, 160)}`);

  // a miss under /tools/ that the app cannot draw stays on the site's 404 page: status 404, no bounce, no runtime download
  const miss = await fetch(`${base}/tools/nonexistent/`);
  if (miss.status !== 404) fail(`/tools/nonexistent/ answered ${miss.status}, expected 404`);
  const before = reqLog.length;
  await b.goto(`${base}/tools/nonexistent/`, { width: 1280, height: 900 });
  await sleep(1200);
  const nf = await b.evaluate('({path:location.pathname+location.search,h1:(document.querySelector("h1")||{}).textContent||"",bounced:!!window.__eaBounced,live:!!document.querySelector("[data-blazier-state]")})');
  if (nf.path !== '/tools/nonexistent/' || !/does not exist/.test(nf.h1) || nf.bounced || nf.live) fail(`/tools/nonexistent/ did not land on the 404 page: ${JSON.stringify(nf)}`);
  if (reqLog.slice(before).some((r) => r.startsWith('/website-data/_framework/') || r.startsWith('/tools/app/'))) fail('/tools/nonexistent/ fetched the app boot shell or runtime');
  if (b.errors.length) fail(`/tools/nonexistent/ logged console errors: ${JSON.stringify(b.errors).slice(0, 160)}`);

  // the bounce itself: /tools/council-spending/<real slug> has no file -> 404.html -> /tools/app/?/... -> the app boots, URL restored, route resolved
  if (!haveData) warn(`boot test skipped: ${DATA} has no _framework or council-web/index.csv`);
  else {
    const rows = fs.readFileSync(path.join(DATA, 'council-web', 'index.csv'), 'utf8').trim().split(/\r?\n/);
    const head = rows[0].split(','), iShort = head.indexOf('Short');
    const [slug, short] = [rows[1].split(',')[0], rows[1].split(',')[iShort]];
    const deepPath = `/tools/council-spending/${slug}`;
    const mark = reqLog.length;
    await b.goto(`${base}${deepPath}`, { width: 1280, height: 900 });
    const t0 = Date.now();
    // wait for the live tool: the council page's own h1 (the council's name) and its year picker, which exist only once the app has booted and read the data
    const ready = await poll(`document.querySelector('#app[data-blazier-state=live] .room.cs h1') && /${short.replace(/[^\w ]/g, '.')}/i.test(document.querySelector('.room.cs h1').textContent) && document.querySelector('.loadsec')`, 300000);
    const st = ready == null ? await b.evaluate('({path:location.pathname+location.search,h1:(document.querySelector("h1")||{}).textContent,state:(document.querySelector("#app")||{}).getAttribute&&document.querySelector("#app").getAttribute("data-blazier-state"),text:document.body.innerText.slice(0,200)})').catch((e) => String(e)) : null;
    if (ready == null) fail(`deep link ${deepPath} did not boot the app within 300 s: ${JSON.stringify(st)}`);
    else {
      const fin = await b.evaluate('({path:location.pathname+location.search,bounced:!!window.__eaBounced,go:!!document.querySelector("[data-blazier-go]"),boot:!!document.querySelector(".boot-crt"),h1:document.querySelector("h1").textContent})');
      if (fin.path !== deepPath) fail(`deep link ${deepPath} ended at ${fin.path} (the original path was not restored)`);
      if (!fin.bounced) fail(`deep link ${deepPath} booted without passing the bounce (window.__eaBounced unset)`);
      if (fin.go || fin.boot) fail(`deep link ${deepPath}: the shell's start panel or boot terminal is still on screen after the app went live`);
      const seen = reqLog.slice(mark);
      if (!seen.includes('/tools/app/')) fail(`deep link ${deepPath} never requested /tools/app/ (the bounce target)`);
      if (!seen.some((r) => r.startsWith('/website-data/_framework/'))) fail(`deep link ${deepPath}: no runtime file requested from /website-data/_framework/`);
      if (!seen.some((r) => r.startsWith('/website-data/council-web/'))) fail(`deep link ${deepPath}: no council data requested from /website-data/council-web/ (EA_DATA_BASE)`);
      if (seen.some((r) => r.startsWith('/tools/_framework/') && !/blazor\.webassembly/.test(r))) fail(`deep link ${deepPath}: runtime requested from the site instead of website-data`);
      if (b.errors.length) fail(`deep link ${deepPath} logged console errors: ${JSON.stringify(b.errors).slice(0, 300)}`);
      console.log(`boot test: ${deepPath} -> 404 -> /tools/app/?/... -> live in ${((Date.now() - t0) / 1000).toFixed(1)} s, h1 "${fin.h1}", ${seen.length} requests, ${seen.filter((r) => r.startsWith('/website-data/_framework/')).length} runtime files, ${b.errors.length} console errors`);
    }
  }
} finally { b.close(); server.close(); }
console.log(`tool shells: ${shells.length} found (${shells.join(', ')}); ${booted} load with static text and no console errors`);

// ---- 5. what the cutover removes ----
const gone = walk(SITE).map((f) => path.relative(SITE, f).replace(/\\/g, '/')).filter((r) => !fs.existsSync(path.join(T, r)));
console.log(`live site/ files with no file of the same name in the deploy tree: ${gone.length} (each is an old URL that resolves through a stub, or an asset the new site does not use)`);
if (process.argv.includes('--list')) gone.forEach((g) => console.log('  ' + g));
for (const w of warns) console.log('WARN: ' + w);
for (const f of fails) console.log('FAIL: ' + f);
console.log(fails.length ? `dry run: ${fails.length} failure(s)` : `dry run: ok${warns.length ? ` (${warns.length} warning)` : ''}`);
if (!process.argv.includes('--keep')) fs.rmSync(T, { recursive: true, force: true }); else console.log('kept ' + T);
process.exit(fails.length ? 1 : 0);
