#!/usr/bin/env node
// Structural checks on the built site (site-next/ by default). Errors fail the run; warnings are reported.
//   node web/scripts/check-site.mjs [dir]
// Links may point at pages that are still on the live site (legacy targets while products migrate); those are
// counted and listed, never silently accepted: a target must exist in the build, in site/, or in Showroom/dist.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { OUT, ROOT, WEB, DATA, readJson } from './lib/data.mjs';
import { walk } from './lint-voice.mjs';

const positional = process.argv.slice(2).filter((a) => !a.startsWith('--'));
const dir = path.resolve(positional[0] || OUT);
const errors = [], warns = [];
const err = (p, m) => errors.push(`${p}: ${m}`);
const warn = (p, m) => warns.push(`${p}: ${m}`);

const site = readJson(path.join(DATA, 'site.json'));
const files = walk(dir);
const rel = (f) => path.relative(dir, f).replace(/\\/g, '/');
const htmlFiles = files.filter((f) => f.endsWith('.html'));
const urlOf = (f) => { const r = rel(f); return r.endsWith('index.html') ? '/' + r.slice(0, -'index.html'.length) : '/' + r; };

const pages = new Map(); // url -> {file, src, stub, noindex}
for (const f of htmlFiles) {
  const src = fs.readFileSync(f, 'utf8');
  pages.set(urlOf(f), { file: f, src, stub: /<body data-stub/.test(src), noindex: /<meta name="robots" content="noindex"/.test(src) });
}
const real = [...pages].filter(([, p]) => !p.stub);

const existsHere = (u) => {
  const clean = u.split('#')[0].split('?')[0];
  if (pages.has(clean)) return true;
  const f = path.join(dir, clean);
  return fs.existsSync(f) && fs.statSync(f).isFile();
};
const legacyRoots = [path.join(ROOT, 'site'), path.join(ROOT, 'Showroom', 'dist')];
function legacyExists(u) {
  const clean = decodeURIComponent(u.split('#')[0].split('?')[0]);
  const cands = [];
  if (clean.startsWith('/tools/')) cands.push(path.join(ROOT, 'Showroom', 'dist', clean.slice('/tools/'.length), clean.endsWith('/') ? 'index.html' : ''));
  const base = path.join(ROOT, 'site', clean);
  cands.push(base, path.join(base, 'index.html'));
  return cands.some((c) => fs.existsSync(c) && fs.statSync(c).isFile());
}

const legacyLinks = new Map();
const graph = new Map();
const idsOf = (src) => [...src.matchAll(/\sid="([^"]+)"/g)].map((m) => m[1]);

for (const [url, p] of real) {
  const src = p.src;
  const text = src.replace(/<script[\s\S]*?<\/script>/g, '');

  // ---- document basics ----
  const title = (src.match(/<title>([\s\S]*?)<\/title>/) || [])[1]?.trim();
  const desc = (src.match(/<meta name="description" content="([^"]*)"/) || [])[1];
  const canon = (src.match(/<link rel="canonical" href="([^"]*)"/) || [])[1];
  if (!/<html lang="en">/.test(src)) err(url, 'missing html lang');
  if (!/name="viewport"/.test(src)) err(url, 'missing viewport');
  if (!title) err(url, 'missing title'); else { if (title.length > 80) err(url, `title too long (${title.length})`); else if (title.length > 65) warn(url, `title is ${title.length} chars`); }
  if (!desc) err(url, 'missing meta description'); else { if (desc.length > 160) err(url, `description too long (${desc.length})`); if (desc.length < 70) err(url, `description too short (${desc.length})`); }
  if (canon !== site.url + url) err(url, `canonical is ${canon}, expected ${site.url + url}`);
  if ((src.match(/<main[\s>]/g) || []).length !== 1) err(url, 'expected exactly one <main>');
  if (!/class="skip" href="#main"/.test(src)) err(url, 'missing skip link');
  if (/\sstyle="/.test(text)) err(url, 'inline style attribute');
  if (/<style[\s>]/.test(text)) err(url, 'page-local <style> block');
  if (/\son[a-z]+="/.test(text)) err(url, 'inline event handler');
  if ((text.match(/<img\b(?![^>]*\balt=)/g) || []).length) err(url, 'img without alt');
  if (/<!--[\s\S]*?-->/.test(src)) warn(url, 'HTML comment in output');

  // ---- ids ----
  const ids = idsOf(src), seen = new Set();
  for (const i of ids) { if (seen.has(i)) err(url, `duplicate id "${i}"`); seen.add(i); }

  // ---- headings ----
  const hs = [...text.matchAll(/<h([1-6])[\s>]/g)].map((m) => Number(m[1]));
  if (hs.filter((h) => h === 1).length !== 1) err(url, `expected exactly one h1, found ${hs.filter((h) => h === 1).length}`);
  let prev = 0;
  for (const h of hs) { if (prev && h > prev + 1) err(url, `heading level jumps from h${prev} to h${h}`); prev = h; }

  // ---- JSON-LD ----
  for (const m of src.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)) { try { JSON.parse(m[1]); } catch { err(url, 'invalid JSON-LD'); } }

  // ---- links ----
  const targets = new Set();
  for (const m of text.matchAll(/<a\b[^>]*\shref="([^"]*)"/g)) {
    const h = m[1];
    if (/^(https?:|mailto:|tel:|data:)/.test(h)) {
      if (/^https?:/.test(h) && !h.startsWith(site.url)) { if (!/rel="[^"]*noopener/.test(m[0]) && /target="_blank"/.test(m[0])) err(url, `external link without rel=noopener: ${h}`); }
      if (!h.startsWith(site.url)) continue;
    }
    if (h.startsWith('#')) { if (h.length > 1 && !seen.has(h.slice(1))) err(url, `dead anchor ${h}`); continue; }
    const u = h.startsWith(site.url) ? h.slice(site.url.length) || '/' : h;
    if (!u.startsWith('/')) { err(url, `relative link ${h}`); continue; }
    const [pathOnly, hash] = u.split('#');
    if (existsHere(pathOnly)) {
      targets.add(pathOnly);
      if (hash && pages.has(pathOnly) && !idsOf(pages.get(pathOnly).src).includes(hash)) err(url, `dead anchor ${u}`);
      if (pages.get(pathOnly)?.stub) warn(url, `links to a redirect stub: ${u}`);
    } else if (legacyExists(pathOnly)) {
      legacyLinks.set(pathOnly, (legacyLinks.get(pathOnly) || 0) + 1);
    } else err(url, `broken link ${u}`);
    if (pathOnly.startsWith('/tools/') && pathOnly.length > '/tools/'.length && !pathOnly.endsWith('/')) err(url, `tool link without trailing slash: ${u}`);
  }
  graph.set(url, targets);
}

// ---- nav and footer identical on every page ----
const norm = (s) => s.replace(/\saria-current="[^"]*"/g, '').replace(/\s+/g, ' ').trim();
const grab = (src, re) => (src.match(re) || [])[0] || '';
for (const [label, re] of [['nav', /<header class="site-header">[\s\S]*?<\/header>/], ['footer', /<footer class="site-footer">[\s\S]*?<\/footer>/]]) {
  const sigs = new Map();
  for (const [url, p] of real) { const s = norm(grab(p.src, re)); if (!s) err(url, `missing ${label}`); sigs.set(url, s); }
  const first = [...sigs.values()][0];
  for (const [url, s] of sigs) if (s !== first) err(url, `${label} differs from the other pages`);
}

// ---- click depth from home ----
const depth = new Map([['/', 0]]);
const queue = ['/'];
while (queue.length) {
  const u = queue.shift();
  for (const t of graph.get(u) || []) if (pages.has(t) && !pages.get(t).stub && !depth.has(t)) { depth.set(t, depth.get(u) + 1); queue.push(t); }
}
for (const [url, p] of real) {
  if (p.noindex) continue;
  if (!depth.has(url)) err(url, 'not reachable from home');
  else if (depth.get(url) > 2) err(url, `click depth ${depth.get(url)} from home`);
}

// ---- sitemap equals pages ----
const sm = fs.existsSync(path.join(dir, 'sitemap.xml')) ? fs.readFileSync(path.join(dir, 'sitemap.xml'), 'utf8') : '';
const smUrls = new Set([...sm.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1].replace(site.url, '')));
const want = new Set(real.filter(([, p]) => !p.noindex).map(([u]) => u));
for (const u of want) if (!smUrls.has(u)) err('sitemap.xml', `missing ${u}`);
for (const u of smUrls) if (!want.has(u)) err('sitemap.xml', `lists ${u}, which is not an indexable page in the build`);

// ---- redirect stubs ----
for (const [url, p] of pages) if (p.stub) {
  const c = (p.src.match(/<link rel="canonical" href="([^"]*)"/) || [])[1] || '';
  const target = c.replace(site.url, '');
  if (!pages.has(target) || pages.get(target).stub) err(url, `stub target ${target} is not a real page`);
  if (!/http-equiv="refresh"/.test(p.src) || !/noindex/.test(p.src)) err(url, 'stub missing refresh or noindex');
  if (smUrls.has(url)) err(url, 'stub is in the sitemap');
}

// ---- machine files ----
for (const f of ['robots.txt', 'llms.txt', 'llms-full.txt', 'sitemap.xml', '.nojekyll']) if (!fs.existsSync(path.join(dir, f))) err(f, 'missing');
for (const f of ['llms.txt', 'llms-full.txt']) {
  const t = fs.existsSync(path.join(dir, f)) ? fs.readFileSync(path.join(dir, f), 'utf8') : '';
  for (const m of t.matchAll(new RegExp(site.url.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '(/[^\\s)`]*)', 'g'))) {
    const u = m[1].replace(/[.,;:]+$/, '');
    if (!existsHere(u) && !legacyExists(u)) err(f, `dead URL ${u}`);
  }
}

// ---- diagrams: data, budget, behaviour of the shipped module ----
const versions = readJson(path.join(DATA, 'generated', 'versions.json'));
for (const [url, p] of real) {
  for (const m of p.src.matchAll(/<figure class="fig[^"]*" data-diagram="([\w-]+)"[^>]*>([\s\S]*?)<\/figure>/g)) {
    const kind = m[1], body = m[2];
    const blob = (body.match(/<script type="application\/json" data-diagram-data>([\s\S]*?)<\/script>/) || [])[1];
    if (!blob) { err(url, `diagram ${kind}: no data blob`); continue; }
    let data; try { data = JSON.parse(blob); } catch { err(url, `diagram ${kind}: data is not JSON`); continue; }
    if (!/<table[\s>]/.test(body)) err(url, `diagram ${kind}: no data table alternative`);
    if (!/<svg[^>]*role="(img|group)"/.test(body)) err(url, `diagram ${kind}: static fallback svg has no role`);
    const modFile = path.join(dir, 'assets', 'diagrams', kind + '.js');
    const coreFile = path.join(dir, 'assets', 'diagrams', 'core.js');
    const bytes = fs.statSync(modFile).size + fs.statSync(coreFile).size + Buffer.byteLength(blob);
    if (bytes > 40 * 1024) err(url, `diagram ${kind}: ${bytes} bytes exceeds the 40 KB budget`);
    const js = fs.readFileSync(modFile, 'utf8') + fs.readFileSync(coreFile, 'utf8');
    if (!/prefers-reduced-motion/.test(js)) err(url, `diagram ${kind}: no reduced-motion handling`);
    if (/https?:\/\//.test(js.replace(/schema\.org/g, '').replace(/http:\/\/www\.w3\.org\/2000\/svg/g, ''))) err(url, `diagram ${kind}: external URL in renderer`);
    if (/\bimport\s+[^'"]*from\s+['"](?!\.)/.test(js)) err(url, `diagram ${kind}: non-relative import (libraries are not allowed)`);
    const pkg = data.package && versions.packages[data.package.id]?.version;
    if (pkg && pkg !== data.package.version) warn(url, `diagram ${kind} was generated with ${data.package.version}, current release is ${pkg}: regenerate`);
    // built module behaves like the source module
    const built = await import(pathToFileURL(modFile).href + '?built');
    const srcMod = await import(pathToFileURL(path.join(WEB, 'src', 'assets', 'diagrams', kind + '.js')).href + '?src');
    const a = JSON.stringify(built.metrics(data)), b = JSON.stringify(srcMod.metrics(data));
    if (a !== b) err(url, `diagram ${kind}: shipped module metrics differ from source`);
    for (const msg of (built.verify ? built.verify(data, body) : [])) err(url, `diagram ${kind}: ${msg}`);
    console.log(`diagram ${kind} on ${url}: ${bytes} bytes (renderer + core + data), budget 40960`);
  }
}

// ---- inline code samples must appear in the program that proves them ----
{
  const norm2 = (s) => s.replace(/\s+/g, ' ').replace(/"{2}/g, '"').trim();
  const dirP = path.join(DATA, 'products');
  for (const f of fs.readdirSync(dirP).filter((x) => x.endsWith('.json'))) {
    const doc = readJson(path.join(dirP, f));
    for (const s of doc.sections || []) for (const sn of s.snippets || []) {
      if (!sn.text) continue;
      if (!sn.provenIn) { err(f, `inline sample in "${s.title}" names no proving program (provenIn)`); continue; }
      const prog = fs.readFileSync(path.join(DATA, 'snippets', sn.provenIn + '.cs'), 'utf8');
      if (!norm2(prog).includes(norm2(sn.text))) err(f, `inline sample in "${s.title}" does not appear in ${sn.provenIn}.cs`);
    }
  }
}

// ---- research articles: finding lead, refuted-by callout, contents, and references that match their in-text cites ----
for (const [url, p] of real) {
  if (!/^\/research\/[^/]+\/$/.test(url)) continue;
  if (!/class="finding-text"/.test(p.src)) err(url, 'no finding lead');
  if (!/class="callout warn refuted"/.test(p.src)) err(url, 'no refuted-by callout');
  if (!/<nav class="toc"/.test(p.src)) err(url, 'no contents block');
  if (!/"@type":"Article"/.test(p.src)) err(url, 'no Article JSON-LD');
  const refIds = [...p.src.matchAll(/<li id="ref-([a-z0-9-]+)"/g)].map((m) => m[1]);
  const cites = new Set([...p.src.matchAll(/class="cite" href="#ref-([a-z0-9-]+)"/g)].map((m) => m[1]));
  for (const i of refIds) if (!cites.has(i)) err(url, `reference "${i}" is never cited in the text`);
  for (const i of cites) if (!refIds.includes(i)) err(url, `a citation points at "${i}", which is not in the references`);
  if (/\[@[a-z0-9-]+\]/.test(p.src)) err(url, 'an unresolved [@id] citation marker is left in the text');
}

// ---- report ----
console.log(`checked ${real.length} pages, ${[...pages].filter(([, p]) => p.stub).length} redirect stubs in ${path.relative(ROOT, dir)}`);
console.log(`click depth: ${[...depth].map(([u, d]) => d).reduce((a, b) => Math.max(a, b), 0)} max; legacy link targets still on the live site: ${legacyLinks.size} (${[...legacyLinks.values()].reduce((a, b) => a + b, 0)} links)`);
if (process.argv.includes('--legacy')) for (const [u, n] of legacyLinks) console.log(`  legacy ${u} x${n}`);
for (const w of warns) console.log('warning: ' + w);
for (const e of errors) console.log('ERROR: ' + e);
console.log(errors.length ? `check: ${errors.length} error(s)` : `check: ok${warns.length ? ` (${warns.length} warning(s))` : ''}`);
process.exit(errors.length ? 1 : 0);
