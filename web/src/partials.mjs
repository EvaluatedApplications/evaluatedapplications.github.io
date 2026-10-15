// Component partials: one function per pattern in the design language (card, install, code, stat, table, chart).
import { html, raw, esc, fmt2, fmt0 } from '../scripts/lib/html.mjs';
import { allTables } from '../scripts/lib/data.mjs';

export function productCard(p) {
  const pkgs = p.packages;
  const primary = pkgs.find((x) => x.role === 'primary');
  return html`<li class="card cat-${p.cat}">
  <h3><a href="${p.href}">${p.name}</a></h3>
  <p>${p.pitch}</p>
  <div class="meta"><span class="badge" data-gen="version">${primary.version}</span><span data-gen="count">${pkgs.length > 1 ? `${pkgs.length} packages` : 'NuGet package'}</span></div>
</li>`;
}

export function toolCard(t, byId) {
  return html`<li class="card cat-${t.cat}">
  <h3><a href="${t.href}">${t.name}</a></h3>
  <p>${t.line}</p>
  <div class="meta">${t.poweredBy.length ? html`<span>Powered by</span>${t.poweredBy.map((id) => html`<a href="${byId[id].href}">${byId[id].name}</a>`)}` : html`<span>Opens in your browser</span>`}</div>
</li>`;
}

export function researchCard(r) {
  return html`<li class="card cat-neutral" data-topics="${r.topics.join('|')}">
  <h3><a href="${r.href}">${r.title}</a></h3>
  <p class="finding-line">${r.finding || r.summary}</p>
  <div class="meta">${r.topics.map((t) => html`<span class="pill">${t}</span>`)}</div>
</li>`;
}
export function evidenceCard(e, byId) {
  const p = byId[e.product];
  return html`<li class="card cat-${p.cat}">
  <h3><a href="${e.slug}">${e.title}</a></h3>
  <p>${e.claim}</p>
  <div class="meta"><a href="${p.href}">${p.name}</a><span class="pill">${e.scope === 'engine' ? 'Engine alone' : e.scope}</span></div>
</li>`;
}

export function stat(label, value) {
  return html`<div class="stat"><dt>${label}</dt><dd>${value}</dd></div>`;
}

export function installLine(cmd) {
  return html`<div class="install"><code>${cmd}</code><button class="copy" type="button" aria-label="Copy install command">copy</button></div>`;
}

export function codeBlock({ code, lang = 'csharp', label, out }) {
  return html`<div class="code${out ? ' out' : ''}">${label ? html`<div class="code-bar"><span>${label}</span><button class="copy" type="button" aria-label="Copy ${label}">copy</button></div>` : ''}<pre><code class="language-${lang}">${code}</code></pre></div>`;
}

// ---- evidence: tables derived from the record, plus an SVG bar chart that always sits beside its table ----
const fmtCell = (n) => fmt2(n);

export function evidenceTable(tb) {
  const head = ['Query', ...tb.columns, 'Result'];
  return html`<div class="tbl-wrap"><table class="tbl"><caption>${tb.caption}</caption>
<thead><tr>${head.map((h, i) => html`<th scope="col"${i > 0 && i < head.length - 1 ? raw(' class="num"') : ''}>${h}</th>`)}</tr></thead>
<tbody>${tb.rows.map((r) => html`<tr><th scope="row">${r.label}</th>${r.values.map((v, i) => {
    const cls = i === tb.focus && r.state === 'win' ? 'num is-win' : i === tb.rival && r.state === 'loss' ? 'num is-rival' : 'num';
    return html`<td class="${cls}">${fmtCell(v)}</td>`;
  })}<td class="${r.state === 'loss' ? 'is-loss' : ''}">${r.verdict}</td></tr>`)}</tbody></table></div>`;
}

// Log-scale paired bars (rival vs focus engine) for every row of a table. Pure SVG, attributes only.
export function evidenceChart(tb, labels) {
  const rows = tb.rows;
  const W = 640, rowH = 22, left = 210, right = 70, top = 8, H = top + rows.length * rowH + 22;
  const all = rows.flatMap((r) => [r.values[tb.rival], r.values[tb.focus]]).map((v) => Math.max(v, 0.005));
  const lo = Math.log10(Math.min(...all)), hi = Math.log10(Math.max(...all));
  const span = W - left - right;
  const x = (v) => ((Math.log10(Math.max(v, 0.005)) - lo) / (hi - lo)) * span;
  const bars = rows.map((r, i) => {
    const y = top + i * rowH;
    const a = Math.max(2, x(r.values[tb.rival])), b = Math.max(2, x(r.values[tb.focus]));
    return `<g><text x="${left - 8}" y="${y + 13}" text-anchor="end">${esc(r.label)}</text>
<rect class="bar${r.state === 'loss' ? ' rival' : ''}" x="${left}" y="${y + 2}" width="${a.toFixed(1)}" height="7"/>
<rect class="bar focus" x="${left}" y="${y + 11}" width="${b.toFixed(1)}" height="7"/>
<text class="val" x="${left + Math.max(a, b) + 6}" y="${y + 14}">${fmt2(r.values[tb.focus])} ms</text></g>`;
  }).join('');
  return html`<figure class="chart"><svg viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="ch-${tb.id}-t"><title id="ch-${tb.id}-t">${tb.caption}, drawn on a logarithmic scale</title>
<line class="axis" x1="${left}" y1="${top}" x2="${left}" y2="${H - 20}"/>${raw(bars)}
<text class="val" x="${left}" y="${H - 4}">logarithmic scale, shorter is faster</text></svg>
<figcaption class="chart-key"><span><i class="sw sw-rival"></i>${labels.rival}</span><span><i class="sw sw-focus"></i>${labels.focus}</span><span>The table below holds every value.</span></figcaption></figure>`;
}

// Headline tiles come from the table rows, so a re-measured record changes them without anyone retyping.
export function evidenceTiles(e) {
  if (e.headline) return e.headline;
  const tiles = [];
  allTables(e).filter((tb) => tb.kind !== 'matrix').forEach((tb, i) => {
    const f = e.facts[tb.id], focus = tb.columns[tb.focus], rival = tb.columns[tb.rival];
    if (i === 0) tiles.push({ value: `${f.beatsFirst} of ${f.total}`, label: `queries where ${focus} beats ${tb.columns[0]}` });
    if (f.ties > 0 || f.losses > 0) tiles.push({ value: `${f.wins} / ${f.ties} / ${f.losses}`, label: `wins, ties and losses against ${rival}` });
    else tiles.push({ value: `${f.wins} of ${f.total}`, label: `queries where ${focus} beats ${rival}` });
  });
  return tiles;
}

export const nbsp = (s) => String(s).replace(/ /g, ' ');
export { fmt2, fmt0 };

// Sub-navigation shared by a product's overview, its sub-pages and its evidence. A tab for something that does not exist is omitted.
export function productSubnav(p, current) {
  const items = [{ label: 'Overview', href: p.href, key: 'overview' }];
  if (p.doc?.quickstart) items.push({ label: 'Quick start', href: p.slug + '#quick-start', key: 'quick' });
  for (const x of p.doc?.extensions || []) items.push({ label: x.nav || x.title, href: p.slug + '#' + x.id, key: 'ext-' + x.id });
  for (const sp of p.subpages || []) items.push({ label: sp.label, href: p.slug + sp.path + '/', key: sp.path });
  if (p.evidence.length) items.push({ label: 'Evidence', href: p.evidence[0].slug, key: 'evidence' });
  if (items.length < 2) return '';
  return html`<nav class="subnav" aria-label="${p.name} sections"><div class="wrap"><ul>${items.map((n) => html`<li><a href="${n.href}"${n.key === current ? raw(' aria-current="page"') : ''}>${n.label}</a></li>`)}</ul></div></nav>`;
}

// A plain captioned table of strings (evidence records whose rows are not engine timings).
export function matrixTable(tb) {
  return html`<div class="tbl-wrap"><table class="tbl"><caption>${tb.caption}</caption>
<thead><tr>${tb.columns.map((h) => html`<th scope="col">${h}</th>`)}</tr></thead>
<tbody>${tb.rows.map((r) => html`<tr><th scope="row">${r[0]}</th>${r.slice(1).map((c) => html`<td>${c}</td>`)}</tr>`)}</tbody></table></div>`;
}
