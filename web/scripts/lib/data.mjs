// Loads every data file and derives the facts the templates need (versions, dependency edges,
// reverse edges, evidence headlines). Nothing here is rendered; templates only read the result.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { fmt2 } from './html.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
export const WEB = path.resolve(here, '..', '..');
export const ROOT = path.resolve(WEB, '..');
export const DATA = path.join(WEB, 'data');
export const OUT = path.join(ROOT, 'site-next');
export const MONOREPO = process.env.EA_MONOREPO || path.resolve(ROOT, '..', 'MonoRepo');

export const readJson = (p) => JSON.parse(fs.readFileSync(p, 'utf8'));
const readDir = (d) => (fs.existsSync(d) ? fs.readdirSync(d).filter((f) => f.endsWith('.json')).sort() : []);

// ---------- versions: read from the package csproj so they cannot go stale ----------
export const VERSIONS_FILE = path.join(DATA, 'generated', 'versions.json');

function csprojFacts(file) {
  const x = fs.readFileSync(file, 'utf8');
  const version = (x.match(/<Version>\s*([^<\s]+)\s*<\/Version>/) || [])[1];
  const id = (x.match(/<PackageId>\s*([^<\s]+)\s*<\/PackageId>/) || [])[1];
  const refs = [...x.matchAll(/ProjectReference\s+Include="([^"]+)"/g)].map((m) => path.basename(m[1].replace(/\\/g, '/'), '.csproj'));
  return { version, id, refs };
}

export async function resolveVersions(products, mode = 'auto') {
  const committed = fs.existsSync(VERSIONS_FILE) ? readJson(VERSIONS_FILE) : null;
  const haveMono = fs.existsSync(path.join(MONOREPO, 'Tracer', 'Tracer.csproj'));
  const use = mode === 'auto' ? (haveMono ? 'csproj' : 'committed') : mode;
  if (use === 'committed') {
    if (!committed) throw new Error('No committed versions.json and no MonoRepo to read: run the build on a machine with MonoRepo.');
    return { mode: 'committed', data: committed };
  }
  const packages = {};
  const byAssembly = {};
  for (const p of products) for (const pkg of p.packages) byAssembly[path.basename(pkg.csproj, '.csproj')] = pkg.id;
  for (const p of products) {
    for (const pkg of p.packages) {
      if (use === 'csproj') {
        const f = path.join(MONOREPO, pkg.csproj);
        if (!fs.existsSync(f)) throw new Error('csproj not found: ' + f);
        const c = csprojFacts(f);
        if (c.id && c.id !== pkg.id) throw new Error(`PackageId mismatch for ${pkg.csproj}: ${c.id} vs ${pkg.id}`);
        packages[pkg.id] = { version: c.version, source: pkg.csproj, dependsOn: c.refs.map((r) => byAssembly[r]).filter(Boolean) };
      } else if (use === 'nuget') {
        const url = `https://api.nuget.org/v3-flatcontainer/${pkg.id.toLowerCase()}/index.json`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(`NuGet lookup failed for ${pkg.id}: ${res.status}`);
        const list = (await res.json()).versions.filter((v) => !v.includes('-'));
        packages[pkg.id] = { version: list[list.length - 1], source: 'nuget.org', dependsOn: committed?.packages?.[pkg.id]?.dependsOn ?? [] };
      }
    }
  }
  const data = { schema: 'ea-versions/1', packages };
  fs.mkdirSync(path.dirname(VERSIONS_FILE), { recursive: true });
  fs.writeFileSync(VERSIONS_FILE, JSON.stringify(data, null, 2) + '\n', 'utf8');
  return { mode: use, data };
}

// ---------- evidence: derive counts from the rows, never typed ----------
export const allTables = (ev) => (ev.sections ? ev.sections.flatMap((s) => s.tables || []) : ev.tables || []);
function evidenceFacts(ev) {
  const t = ev.tieBelow ?? 1.15;
  const facts = {};
  for (const tb of allTables(ev)) {
    if (tb.kind === 'matrix') continue;
    const rows = tb.rows;
    // Outcome against the named rival engine, from the numbers (a difference under tieBelow is a tie).
    const outcome = (r) => {
      const a = Math.max(r.values[tb.rival], 0.005), b = Math.max(r.values[tb.focus], 0.005);
      return a / b >= t ? 'win' : b / a >= t ? 'loss' : 'tie';
    };
    const wins = rows.filter((r) => outcome(r) === 'win').length;
    const ties = rows.filter((r) => outcome(r) === 'tie').length;
    const losses = rows.filter((r) => outcome(r) === 'loss').length;
    // First column is the baseline reference engine (SQLite) when present.
    const focus = tb.focus;
    const beatsFirst = rows.filter((r) => r.values[focus] < r.values[0]).length;
    facts[tb.id] = { total: rows.length, wins, ties, losses, beatsFirst, tieBelow: t };
  }
  return facts;
}

export async function loadAll(opts = {}) {
  const site = readJson(path.join(DATA, 'site.json'));
  const licence = readJson(path.join(DATA, 'licence.json'));
  const productsFile = readJson(path.join(DATA, 'products.json'));
  const toolsFile = readJson(path.join(DATA, 'tools.json'));
  const redirects = readJson(path.join(DATA, 'redirects.json'));
  // one file per page type: partner, ai, and the copy for the home, products, tools and research index pages
  const pages = Object.fromEntries(readDir(path.join(DATA, 'pages')).map((f) => [f.replace(/\.json$/, ''), readJson(path.join(DATA, 'pages', f))]));
  const versions = await resolveVersions(productsFile.products, opts.versions || 'auto');

  const products = productsFile.products.map((p) => ({ ...p }));
  const byId = Object.fromEntries(products.map((p) => [p.id, p]));
  const pkgToProduct = {};
  for (const p of products) for (const pkg of p.packages) pkgToProduct[pkg.id] = p.id;

  // Packages carry their version; product-level edges come from PRIMARY packages only
  // (an extension package may depend back on its own product's neighbours).
  for (const p of products) {
    p.packages = p.packages.map((pkg) => ({ ...pkg, version: versions.data.packages[pkg.id]?.version ?? null, nuget: `https://www.nuget.org/packages/${pkg.id}` }));
    const primary = p.packages.find((x) => x.role === 'primary');
    const deps = new Set();
    for (const d of versions.data.packages[primary.id]?.dependsOn ?? []) {
      const pid = pkgToProduct[d];
      if (pid && pid !== p.id) deps.add(pid);
    }
    p.dependsOn = [...deps];
    p.usedBy = [];
    p.href = p.status === 'migrated' ? p.slug : p.legacyUrl;
  }
  for (const p of products) for (const d of p.dependsOn) byId[d].usedBy.push(p.id);

  const tools = toolsFile.tools.map((t) => ({ ...t }));
  for (const t of tools) {
    t.href = t.slug;
    for (const pid of t.poweredBy) if (!byId[pid]) throw new Error(`tool ${t.id} references unknown product ${pid}`);
  }
  for (const p of products) p.tools = tools.filter((t) => t.poweredBy.includes(p.id));
  // The platform product (Blazier) hosts every tool as a static shell, so its page lists the whole set.
  if (toolsFile.platform) {
    if (!byId[toolsFile.platform]) throw new Error('tools.json platform references unknown product ' + toolsFile.platform);
    byId[toolsFile.platform].tools = tools;
  }

  // Games: one card per game (games.json); a game with a `content` file also gets its own page.
  const games = readJson(path.join(DATA, 'games.json')).games.map((g) => ({ ...g }));
  for (const g of games) {
    g.href = g.slug;
    for (const pid of g.poweredBy) if (!byId[pid]) throw new Error(`game ${g.id} references unknown product ${pid}`);
    if (g.tool && !tools.some((t) => t.id === g.tool)) throw new Error(`game ${g.id} references unknown tool ${g.tool}`);
    if (g.content) g.doc = readJson(path.join(DATA, g.content));
  }

  // Content blocks authored per product (only migrated products have one).
  for (const p of products) {
    if (p.content) p.doc = readJson(path.join(DATA, p.content));
    p.subpages = (p.subpages || []).map((sp) => ({ ...sp, doc: readJson(path.join(DATA, sp.file)) }));
  }

  const evidence = readDir(path.join(DATA, 'evidence')).map((f) => {
    const e = readJson(path.join(DATA, 'evidence', f));
    e.facts = evidenceFacts(e);
    return e;
  });
  for (const p of products) p.evidence = evidence.filter((e) => e.product === p.id);

  const research = readDir(path.join(DATA, 'research')).map((f) => readJson(path.join(DATA, 'research', f))).sort((a, b) => a.order - b.order);
  for (const r of research) r.href = r.status === 'migrated' ? `/research/${r.id}/` : r.legacyUrl;
  // references are always an array; `related` names an article by id or by its old file name
  const byKey = {};
  for (const r of research) { byKey[r.id] = r; const m = (r.legacyUrl || '').match(/([^/]+)\.html$/); if (m) byKey[m[1]] = r; }
  for (const r of research) {
    r.references = [].concat(r.references || []);
    r.relatedArticles = (r.related || []).map((k) => { if (!byKey[k]) throw new Error(`research ${r.id}: related "${k}" is not an article`); return byKey[k]; });
  }

  // Diagram data referenced by product docs.
  const diagrams = {};
  for (const f of readDir(path.join(DATA, 'diagrams'))) diagrams[f.replace(/\.json$/, '')] = readJson(path.join(DATA, 'diagrams', f));

  // The single typed runtime-size constant, read from the Showroom source rather than copied.
  let toolRuntimeMb = null;
  const ts = path.join(ROOT, 'Showroom', 'Components', 'ToolStart.razor');
  if (fs.existsSync(ts)) {
    const m = fs.readFileSync(ts, 'utf8').match(/RuntimeSize\s*=\s*"(\d+)\s*MB"/);
    if (m) toolRuntimeMb = Number(m[1]);
  }
  const factsFile = path.join(DATA, 'generated', 'facts.json');
  if (toolRuntimeMb != null) {
    fs.mkdirSync(path.dirname(factsFile), { recursive: true });
    fs.writeFileSync(factsFile, JSON.stringify({ schema: 'ea-facts/1', toolRuntimeMb, source: 'Showroom/Components/ToolStart.razor RuntimeSize' }, null, 2) + '\n', 'utf8');
  } else if (fs.existsSync(factsFile)) {
    toolRuntimeMb = readJson(factsFile).toolRuntimeMb;
  }

  const groups = productsFile.groups.map((g) => ({ ...g, products: products.filter((p) => p.group === g.id) }));
  const year = Number(process.env.SOURCE_DATE_EPOCH ? new Date(Number(process.env.SOURCE_DATE_EPOCH) * 1000).getUTCFullYear() : new Date().getFullYear());

  return { site, licence, products, groups, byId, tools, games, evidence, research, redirects, pages, diagrams, versions, toolRuntimeMb, year, fmt2 };
}
