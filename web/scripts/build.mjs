#!/usr/bin/env node
// Static site generator for the staging tree site-next/. No dependencies.
//   node web/scripts/build.mjs [--versions=auto|csproj|nuget|committed] [--out=<dir>]
// Reads web/data, web/src and SiteKit/tokens; writes site-next/. Versions are read from the local
// MonoRepo csproj files when present (and committed to data/generated/versions.json); CI never sees
// MonoRepo, so it builds from the committed copy or with --versions=nuget.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { loadAll, WEB, ROOT, OUT as DEFAULT_OUT } from './lib/data.mjs';
import { document_ } from '../src/layout.mjs';
import home from '../src/templates/home.mjs';
import product from '../src/templates/product.mjs';
import phasorPage from '../src/templates/phasor.mjs';
import productDocs from '../src/templates/product-docs.mjs';
import productsIndex from '../src/templates/products-index.mjs';
import toolsIndex from '../src/templates/tools-index.mjs';
import gamesIndex from '../src/templates/games-index.mjs';
import gamePage from '../src/templates/game.mjs';
import researchIndex from '../src/templates/research-index.mjs';
import researchArticle from '../src/templates/research-article.mjs';
import * as evidenceT from '../src/templates/evidence.mjs';
import partner from '../src/templates/partner.mjs';
import licence from '../src/templates/licence.mjs';
import ai from '../src/templates/ai.mjs';
import about from '../src/templates/about.mjs';
import demo from '../src/templates/demo.mjs';
import notFound from '../src/templates/404.mjs';
import { diagramModule } from '../src/diagram.mjs';
import { minJs } from './lib/minjs.mjs';
import { redirectStub } from './lib/redirects.mjs';
import { composeLlms, composeLlmsFull, composeRobots, composeSitemap } from './lib/machine.mjs';

const args = Object.fromEntries(process.argv.slice(2).map((a) => { const [k, v] = a.replace(/^--/, '').split('='); return [k, v ?? true]; }));
const OUT = args.out ? path.resolve(args.out) : DEFAULT_OUT;
if (path.basename(OUT) !== 'site-next' && !args.out) throw new Error('refusing to clean an unexpected output directory');

const write = (rel, content) => {
  const f = path.join(OUT, rel);
  fs.mkdirSync(path.dirname(f), { recursive: true });
  fs.writeFileSync(f, content, 'utf8');
  return Buffer.byteLength(content);
};
const sha = (s) => crypto.createHash('sha1').update(s).digest('hex').slice(0, 8);
const minCss = (c) => c.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\s+/g, ' ').replace(/\s*([{}:;,>])\s*/g, '$1').replace(/;}/g, '}').trim();

async function main() {
  const ctx = await loadAll({ versions: args.versions || 'auto' });

  // home signature diagram data: products, edges (from the csproj references) and the tools they power
  {
    const level = {}; const lv = (p) => (level[p.id] ??= p.dependsOn.length ? 1 + Math.max(...p.dependsOn.map((d) => lv(ctx.byId[d]))) : 0);
    ctx.products.forEach(lv);
    const colorOf = { foundation: 'c-foundation-solid', 'chord-holodb-algformer': 'c-holodb', 'chord-algformer-tracer': 'c-algformer', neutral: 'ink-faint' };
    const sm = {
      schema: 'ea-diagram/1', kind: 'stack-map', source: 'csproj ProjectReference edges (data/generated/versions.json)',
      nodes: ctx.products.map((p) => ({ id: p.id, name: p.name, cat: p.cat, color: colorOf[p.cat] || 'c-' + p.cat, level: level[p.id], href: p.href, pitch: p.pitch })),
      edges: ctx.products.flatMap((p) => p.dependsOn.map((d) => ({ from: d, to: p.id }))),
      tools: ctx.tools.map((t) => ({ id: t.id, name: t.name, href: t.href, poweredBy: t.poweredBy })),
    };
    ctx.diagrams['stack-map'] = sm;
    fs.mkdirSync(path.join(WEB, 'data', 'generated'), { recursive: true });
    fs.writeFileSync(path.join(WEB, 'data', 'generated', 'stack-map.json'), JSON.stringify(sm, null, 2) + '\n', 'utf8');
  }

  // numbers quoted in prose that come from diagram data
  ctx.limitFacts = {};
  for (const p of ctx.products) {
    const ids = [p.doc?.diagram?.id, ...(p.doc?.factDiagrams || [])].filter(Boolean);
    ctx.limitFacts[p.id] = {};
    for (const id of ids) { const mod = diagramModule(id); if (mod.limitFacts) Object.assign(ctx.limitFacts[p.id], mod.limitFacts(mod.metrics(ctx.diagrams[id]))); }
    // the one site-wide runtime size, read from the Showroom source (data/generated/facts.json), is available to any product's prose
    if (ctx.toolRuntimeMb != null) ctx.limitFacts[p.id].toolRuntimeMb = String(ctx.toolRuntimeMb);
  }

  // ---- clean output ----
  fs.rmSync(OUT, { recursive: true, force: true });
  fs.mkdirSync(OUT, { recursive: true });

  // ---- assets ----
  // brand-ea.css ends with a comment that contains the text "**/", which closes it early and leaves prose after
  // the last rule. Strip comments, then cut after the final closing brace so that prose cannot reach a selector.
  const tokens = ['core.css', 'brand-ea.css'].map((f) => {
    const c = fs.readFileSync(path.join(ROOT, 'SiteKit', 'tokens', f), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
    return c.slice(0, c.lastIndexOf('}') + 1);
  });
  const stylesDir = path.join(WEB, 'src', 'styles');
  const sheets = fs.readdirSync(stylesDir).filter((f) => f.endsWith('.css')).sort().map((f) => fs.readFileSync(path.join(stylesDir, f), 'utf8'));
  const css = minCss([...tokens, ...sheets].join('\n'));
  const siteJs = fs.readFileSync(path.join(WEB, 'src', 'assets', 'site.js'), 'utf8');
  ctx.assets = { css: sha(css), js: sha(siteJs) };
  const sizes = {};
  sizes['assets/ea.css'] = write('assets/ea.css', css);   // the legacy /assets/site.css stays for the tool shells (CUTOVER.md)
  sizes['assets/site.js'] = write('assets/site.js', minJs(siteJs));
  const diaDir = path.join(WEB, 'src', 'assets', 'diagrams');
  for (const f of fs.readdirSync(diaDir)) sizes['assets/diagrams/' + f] = write('assets/diagrams/' + f, minJs(fs.readFileSync(path.join(diaDir, f), 'utf8')));
  write('.nojekyll', '');

  // ---- pages ----
  const pages = [];
  pages.push(home(ctx));
  pages.push(productsIndex(ctx));
  for (const p of ctx.products) if (p.status === 'migrated') {
    pages.push((p.doc?.template === 'phasor' ? phasorPage : product)(ctx, p));
    for (const sp of p.subpages) pages.push(productDocs(ctx, p, sp));
  }
  pages.push(toolsIndex(ctx));
  pages.push(demo(ctx));
  pages.push(gamesIndex(ctx));
  for (const g of ctx.games) if (g.doc) pages.push(gamePage(ctx, g));
  pages.push(evidenceT.hub(ctx));
  for (const e of ctx.evidence) pages.push(evidenceT.record(ctx, e));
  pages.push(researchIndex(ctx));
  for (const r of ctx.research) if (r.status === 'migrated') pages.push(researchArticle(ctx, r));
  pages.push(partner(ctx), about(ctx), licence(ctx), ai(ctx), notFound(ctx));

  const urls = new Set();
  for (const pg of pages) {
    if (urls.has(pg.url)) throw new Error('duplicate page url ' + pg.url);
    urls.add(pg.url);
    const rel = pg.url.endsWith('/') ? pg.url.slice(1) + 'index.html' : pg.url.slice(1);
    sizes[rel] = write(rel, document_(ctx, pg, pg.main));
  }
  ctx.pageList = pages;

  // ---- redirect stubs (Pages cannot send a 301) ----
  const stubs = [], pending = [];
  const articleStubs = ctx.research.filter((x) => x.status === 'migrated' && x.legacyUrl).map((x) => ({ from: x.legacyUrl, to: x.href }));
  for (const r of [...ctx.redirects.redirects, ...articleStubs]) {
    const target = r.to.split('#')[0];
    if (urls.has(target)) {
      if (urls.has(r.from)) throw new Error('redirect source collides with a real page: ' + r.from);
      const rel = r.from.endsWith('/') ? r.from.slice(1) + 'index.html' : r.from.slice(1);
      sizes[rel] = write(rel, redirectStub(ctx.site, r));
      stubs.push(r);
    } else pending.push(r);
  }
  ctx.stubs = stubs;

  // ---- machine-readable files ----
  const stamp = args.date || new Date(process.env.SOURCE_DATE_EPOCH ? Number(process.env.SOURCE_DATE_EPOCH) * 1000 : Date.now()).toISOString().slice(0, 10);
  sizes['sitemap.xml'] = write('sitemap.xml', composeSitemap(ctx, pages));
  sizes['robots.txt'] = write('robots.txt', composeRobots(ctx));
  sizes['llms.txt'] = write('llms.txt', composeLlms(ctx, stamp));
  sizes['llms-full.txt'] = write('llms-full.txt', composeLlmsFull(ctx, stamp));

  // ---- report ----
  const total = Object.values(sizes).reduce((a, b) => a + b, 0);
  console.log(`built ${pages.length} pages, ${stubs.length} redirect stubs -> ${path.relative(ROOT, OUT)} (${(total / 1024).toFixed(1)} KB total)`);
  console.log(`versions: ${ctx.versions.mode}; products: ${ctx.products.length} (${ctx.products.filter((p) => p.status === 'migrated').length} migrated); tools: ${ctx.tools.length}; evidence: ${ctx.evidence.length}; research: ${ctx.research.length}`);
  if (pending.length) console.log(`redirects pending (target page not built yet): ${pending.length}`);
  if (args.sizes) for (const [k, v] of Object.entries(sizes).sort((a, b) => b[1] - a[1])) console.log(String(v).padStart(8), k);
}

main().catch((e) => { console.error(e); process.exit(1); });
