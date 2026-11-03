// Component partials: one function per pattern in the design language (card, install, code, stat, table, chart).
import { html, raw, esc, fmt2, fmt0 } from '../scripts/lib/html.mjs';
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

// Headline tiles are typed in the evidence record.
export function evidenceTiles(e) {
  return e.headline || [];
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