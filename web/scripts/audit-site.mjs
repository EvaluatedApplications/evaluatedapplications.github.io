#!/usr/bin/env node
// Whole-site audit of site-next/ (or another built tree). Static crawl of every page plus a browser pass at 390 and 1280.
//   node web/scripts/audit-site.mjs [dir] [--no-browser]
// Asserts: one nav variant and one footer variant, every real page within 2 clicks of home, in the sitemap and in llms.txt, no inline
// style attributes, no heading skips, no duplicate ids, description 70 to 160 characters, canonical correct, valid JSON-LD on every page,
// no version numbers outside generated markers, images with alt; then that no page overflows horizontally or logs a console error.
import fs from 'node:fs';
import path from 'node:path';
import { OUT, ROOT, DATA, readJson } from './lib/data.mjs';
import { walk } from './lint-voice.mjs';
import { serve, launch } from './lib/cdp.mjs';

const dir = path.resolve(process.argv.slice(2).find((a) => !a.startsWith('--')) || OUT);
const noBrowser = process.argv.includes('--no-browser');
const site = readJson(path.join(DATA, 'site.json'));
const errors = [];
const err = (u, m) => errors.push(`${u}: ${m}`);

const files = walk(dir).filter((f) => f.endsWith('.html'));
const urlOf = (f) => { const r = path.relative(dir, f).replace(/\\/g, '/'); return r.endsWith('index.html') ? '/' + r.slice(0, -'index.html'.length) : '/' + r; };
const pages = files.map((f) => ({ url: urlOf(f), src: fs.readFileSync(f, 'utf8') }));
const stubs = pages.filter((p) => /<body data-stub/.test(p.src)), real = pages.filter((p) => !/<body data-stub/.test(p.src));
const indexable = real.filter((p) => !/name="robots" content="noindex"/.test(p.src));

// ---- nav and footer variants ----
const norm = (s) => s.replace(/\saria-current="[^"]*"/g, '').replace(/\s+/g, ' ').trim();
const variants = (re) => new Set(real.map((p) => norm((p.src.match(re) || [''])[0])));
const navV = variants(/<header class="site-header">[\s\S]*?<\/header>/), footV = variants(/<footer class="site-footer">[\s\S]*?<\/footer>/);
if (navV.size !== 1) err('site', `${navV.size} nav variants`);
if (footV.size !== 1) err('site', `${footV.size} footer variants`);

// ---- click depth ----
const links = new Map();
for (const p of real) links.set(p.url, new Set([...p.src.replace(/<script[\s\S]*?<\/script>/g, '').matchAll(/<a\b[^>]*\shref="([^"#?]*)/g)].map((m) => m[1].replace(site.url, '')).filter((h) => h.startsWith('/'))));
const depth = new Map([['/', 0]]), q = ['/'];
while (q.length) { const u = q.shift(); for (const t of links.get(u) || []) if (!depth.has(t) && links.has(t)) { depth.set(t, depth.get(u) + 1); q.push(t); } }
let maxDepth = 0;
for (const p of indexable) { if (!depth.has(p.url)) err(p.url, 'not reachable from home'); else { maxDepth = Math.max(maxDepth, depth.get(p.url)); if (depth.get(p.url) > 2) err(p.url, `${depth.get(p.url)} clicks from home`); } }

// ---- sitemap and llms.txt ----
const sm = fs.readFileSync(path.join(dir, 'sitemap.xml'), 'utf8'), llms = fs.readFileSync(path.join(dir, 'llms.txt'), 'utf8');
for (const p of indexable) {
  if (!sm.includes(`<loc>${site.url}${p.url}</loc>`)) err(p.url, 'not in sitemap.xml');
  if (p.url !== '/' && !llms.includes(site.url + p.url)) err(p.url, 'not in llms.txt');
}

// ---- per page ----
let jsonld = 0, ids = 0, styleAttrs = 0, imgs = 0, versions = 0;
for (const p of real) {
  const src = p.src, text = src.replace(/<script[\s\S]*?<\/script>/g, '');
  const desc = (src.match(/<meta name="description" content="([^"]*)"/) || [])[1];
  if (!desc || desc.length < 70 || desc.length > 160) err(p.url, `description is ${desc ? desc.length : 0} characters`);
  const canon = (src.match(/<link rel="canonical" href="([^"]*)"/) || [])[1];
  if (canon !== site.url + p.url) err(p.url, `canonical ${canon}`);
  const lds = [...src.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)];
  if (!lds.length && !/name="robots" content="noindex"/.test(src)) err(p.url, 'no JSON-LD');
  for (const m of lds) { try { JSON.parse(m[1]); jsonld++; } catch { err(p.url, 'invalid JSON-LD'); } }
  const idl = [...text.matchAll(/\sid="([^"]+)"/g)].map((m) => m[1]); ids += idl.length;
  if (new Set(idl).size !== idl.length) err(p.url, 'duplicate ids');
  const hs = [...text.matchAll(/<h([1-6])[\s>]/g)].map((m) => Number(m[1]));
  if (hs.filter((h) => h === 1).length !== 1) err(p.url, 'not exactly one h1');
  hs.reduce((a, h) => { if (a && h > a + 1) err(p.url, `heading skips h${a} to h${h}`); return h; }, 0);
  const sa = (text.match(/\sstyle="/g) || []).length; styleAttrs += sa; if (sa) err(p.url, `${sa} inline style attribute(s)`);
  for (const m of text.matchAll(/<img\b[^>]*>/g)) { imgs++; if (!/\balt=/.test(m[0])) err(p.url, 'img without alt'); }
  // version numbers in visible text, outside code, generated markers and the generator's own prose
  const vis = text.replace(/<pre[\s\S]*?<\/pre>/g, '').replace(/<(\w+)[^>]*data-gen[^>]*>[\s\S]*?<\/\1>/g, '').replace(/<[^>]+>/g, ' ');
  for (const m of vis.matchAll(/\b(?:v|version |release )?\d+\.\d+\.\d+\b/gi)) { if (/\b\d+\.\d+\.\d+\.\d+\b/.test(m[0])) continue; versions++; err(p.url, `hard-coded version "${m[0]}"`); }
}
console.log(`static: ${real.length} real pages + ${stubs.length} redirect stubs; nav variants ${navV.size}, footer variants ${footV.size}; max click depth ${maxDepth}; ${indexable.length} indexable pages all in sitemap.xml and llms.txt; JSON-LD blocks ${jsonld}; ids ${ids}; inline style attributes ${styleAttrs}; images ${imgs}; hard-coded versions ${versions}`);

// ---- browser pass ----
let browserRuns = 0, overflow = 0, consoleErrors = 0;
if (!noBrowser) {
  const { server, port } = await serve(dir);
  const b = await launch();
  try {
    for (const p of real) {
      for (const [w, h, mobile] of [[390, 844, true], [1280, 900, false]]) {
        try { await b.goto(`http://127.0.0.1:${port}${p.url}`, { width: w, height: h, mobile }); } catch (e) { err(p.url, `${w}: failed to load (${e.message})`); continue; }
        await new Promise((r) => setTimeout(r, 250));
        const m = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
        browserRuns++;
        if (m.sw > m.cw) { overflow++; err(p.url, `${w}: horizontal overflow ${m.sw} > ${m.cw}`); }
        if (b.errors.length) { consoleErrors += b.errors.length; err(p.url, `${w}: console errors ${JSON.stringify(b.errors).slice(0, 200)}`); }
      }
    }
  } finally { b.close(); server.close(); }
  console.log(`browser: ${browserRuns} page loads (${real.length} pages at 390 and 1280); horizontal overflows ${overflow}; console errors ${consoleErrors}`);
}
for (const e of errors) console.log('ERROR: ' + e);
console.log(errors.length ? `audit: ${errors.length} error(s)` : 'audit: clean');
process.exit(errors.length ? 1 : 0);
