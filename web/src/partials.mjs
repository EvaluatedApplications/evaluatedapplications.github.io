// Component partials: one function per pattern in the design language (card, install, code, stat, table, chart).
import { html, raw, esc, fmt2, fmt0 } from '../scripts/lib/html.mjs';
import { allTables } from '../scripts/lib/data.mjs';
import { inline } from './inline.mjs';

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

// A game card: the sizzle line first, then the Play button, then what it teaches and what powers it. Modes are pills.
// The title links to the game's own page when it has one; the Play button always goes to the tool that runs it.
export function gameCard(g, byId) {
  return html`<li class="card game-card cat-${g.cat}">
  <h3><a href="${g.href}">${g.name}</a></h3>
  <p class="game-sizzle">${g.sizzle}</p>
  <p>${g.line}</p>
  <div class="actions"><a class="btn btn-primary" href="${g.play}">Play ${g.name}</a><a class="btn btn-ghost" href="${g.href}">How it plays</a></div>
  <p>${g.teaches}</p>
  <div class="meta"><span>Modes</span>${g.modes.map((m) => html`<span class="pill">${m}</span>`)}</div>
  <div class="meta"><span>Powered by</span>${g.poweredBy.map((id) => html`<a href="${byId[id].href}">${byId[id].name}</a>`)}</div>
  ${g.phone ? html`<p class="game-phone">${g.phone}</p>` : ''}
</li>`;
}

export function researchCard(r) {
  return html`<li class="card cat-neutral" data-topics="${r.topics.join('|')}">
  <h3><a href="${r.href}">${r.title}</a></h3>
  <p class="finding-line">${inline(r.finding || r.summary)}</p>
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
  const head = ['Query', ...tb.columns.map((c, k) => c + (tb.qualifiers && tb.qualifiers[k] ? ` (${tb.qualifiers[k]})` : '')), tb.resultLabel || 'Result'];
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
    const q = (k) => tb.columns[k] + (tb.qualifiers && tb.qualifiers[k] ? ` (${tb.qualifiers[k]})` : '');
    const f = e.facts[tb.id], focus = q(tb.focus), rival = q(tb.rival);
    if (i === 0) tiles.push({ value: `${f.beatsFirst} of ${f.total}`, label: `queries where ${focus} beats ${q(0)}` });
    if (f.ties > 0 || f.losses > 0) tiles.push({ value: `${f.wins} / ${f.ties} / ${f.losses}`, label: `wins, ties and losses against ${rival}` });
    else tiles.push({ value: `${f.wins} of ${f.total}`, label: `queries where ${focus} beats ${rival}` });
  });
  return [...tiles, ...(e.extraTiles || [])];
}

// ---- sizzle-first pieces (design-language section 8): number tiles with the conditions behind a disclosure, and the tool entry ----
export const fillWith = (subst) => (t) => String(t).replace(/\{(\w+)\}/g, (_, k) => (k in subst ? subst[k] : `{${k}}`));

// A stat spec is { value, label } typed from a cited record, or { evidence, tile } read from an evidence record's headline tiles
// (optionally with its own label). Placeholders {name} come from the product's diagram facts. Nothing else is accepted.
export function resolveStats(ctx, specs, subst = {}) {
  const fill = fillWith(subst);
  return specs.map((s) => {
    if (s.evidence) {
      const e = ctx.evidence.find((x) => x.id === s.evidence);
      const t = e && evidenceTiles(e)[s.tile];
      if (!t) throw new Error(`stat refers to evidence ${s.evidence} tile ${s.tile}, which does not exist`);
      return { value: s.value ?? t.value, label: s.label ?? t.label, evidence: e };
    }
    if (!s.cite) throw new Error(`stat "${s.value}" has no cite (a source record it can be checked against)`);
    return { value: fill(s.value), label: fill(s.label) };
  });
}

export function numbersBlock({ stats, numbers, links = [], subst = {}, ribbon, id = 'numbers' }) {
  const fill = fillWith(subst);
  return html`<section class="sec doc-sec" id="${id}" aria-labelledby="${id}-h"><div class="wrap">
  <h2 class="sr-only" id="${id}-h">The numbers</h2>
  <dl class="stats stats-big">${stats.map((x) => stat(x.label, x.value))}</dl>
  ${numbers ? html`<details class="dia-data numbers-more"><summary>${numbers.summary || 'See the numbers'}</summary>
    <ul class="numbers-list">${numbers.items.map((n) => html`<li><h3>${fill(n.claim)}</h3><p>${fill(n.text)}</p></li>`)}</ul>
    ${links.length ? html`<p class="more-link">${links.map((e, i) => html`${i ? ' · ' : ''}<a href="${e.slug}">${e.title}</a>`)}</p>` : ''}
  </details>` : ''}
  ${ribbon || ''}
</div></section>`;
}

// The live tool, straight under the hero.
export function toolBand(tool, p, others = []) {
  return html`<section class="sec doc-sec" id="demo" aria-label="Try it now"><div class="wrap"><div class="callout tool-band">
  <div class="tb-copy"><p class="eyebrow">Try it now, in your browser</p><h2>${tool.name}</h2><p>${tool.line}</p>${others.length ? html`<p class="tb-also">Also built on ${p.name}: ${others.map((t, i) => html`${i ? ', ' : ''}<a href="${t.href}">${t.name}</a>`)}</p>` : ''}</div>
  <div class="actions"><a class="btn btn-primary" href="${tool.href}">Open ${tool.name}</a></div>
</div></div></section>`;
}

// A product with no tool of its own can point at the tools that run on it: the same band, one button, words from the product's data.
export function pointerBand(pt, subst = {}) {
  const fill = fillWith(subst);
  return html`<section class="sec doc-sec" id="demo" aria-label="Try it now"><div class="wrap"><div class="callout tool-band">
  <div class="tb-copy"><p class="eyebrow">${pt.eyebrow || 'Try it now, in your browser'}</p><h2>${pt.title}</h2><p>${fill(pt.line)}</p></div>
  <div class="actions"><a class="btn btn-primary" href="${pt.cta.href}">${pt.cta.label}</a></div>
</div></div></section>`;
}

export const nbsp =(s) => String(s).replace(/ /g, ' ');
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

// Text with `code` spans (backticks), escaped first.
export const codeSpans = (s) => raw(esc(s).replace(/`([^`]+)`/g, '<code>$1</code>'));

// The eight rungs as a ladder: what one pass can do, the thing superposed, the operator and what it was checked against.
export function ladderTable(rows) {
  return html`<div class="tbl-wrap"><table class="tbl ladder"><caption>Each rung adds one thing a single pass can do</caption>
<thead><tr><th scope="col">Rung</th><th scope="col">What one pass can do</th><th scope="col">Superposed thing</th><th scope="col">Operator</th><th scope="col">Checked by</th></tr></thead>
<tbody>${rows.map((r) => html`<tr><th scope="row">${r.rung}</th><td>${codeSpans(r.can)}</td><td>${codeSpans(r.superposed)}</td><td>${codeSpans(r.operator)}</td><td>${codeSpans(r.checked)}</td></tr>`)}</tbody></table></div>`;
}

// A table of credited bricks: what each offers alone, the Pleroma operation that carries it, and the check.
export function brickTable(rows) {
  return html`<div class="tbl-wrap"><table class="tbl bricks"><caption>Established ideas, and the operation of the Pleroma that carries each</caption>
<thead><tr><th scope="col">Brick (credited)</th><th scope="col">Alone it offers</th><th scope="col">In the Pleroma</th><th scope="col">Proof</th></tr></thead>
<tbody>${rows.map((r) => html`<tr><th scope="row">${r.brick}</th><td>${codeSpans(r.alone)}</td><td>${codeSpans(r.pleroma)}</td><td>${codeSpans(r.proof)}</td></tr>`)}</tbody></table></div>`;
}