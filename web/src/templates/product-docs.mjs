import fs from 'node:fs';
import path from 'node:path';
import { html, raw } from '../../scripts/lib/html.mjs';
import { DATA, readJson } from '../../scripts/lib/data.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';
import { codeBlock, productSubnav, matrixTable, ladderTable, resolveStats, numbersBlock } from '../partials.mjs';
import { diagramFigure } from '../diagram.mjs';

const read = (rel) => fs.readFileSync(path.join(DATA, rel), 'utf8').replace(/\r\n/g, '\n').trimEnd();

// The operation grid as a plain table: rungs down, faces across (the same cells the 3D map draws).
function gridTable(d) {
  const faces = d.faces;
  return html`<div class="tbl-wrap"><table class="tbl"><caption>Each rung is a family of operations, one per face</caption>
<thead><tr><th scope="col">Rung</th>${faces.map((f) => html`<th scope="col">${f}</th>`)}</tr></thead>
<tbody>${d.rungs.map((r) => html`<tr><th scope="row">${r.n}. ${r.name}<br><span class="muted">${r.caption}</span></th>${faces.map((f) => html`<td>${(r.cells.find((c) => c.face === f) || {}).name || ''}</td>`)}</tr>`)}</tbody></table></div>`;
}

// Sub-page of a product (docs, or a deep dive such as the Pleroma). Data: a product's subpages[] entry whose doc is
// { title, description, h1?, intro, maturity?, sections: [{ id, title, paragraphs?, list?, grid?, table?, diagram?, snippets?, paragraphsAfter?, listAfter?, more? }] }.
export default function productSubpage(ctx, p, sp) {
  const d = sp.doc, url = p.slug + sp.path + '/';
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Products', href: '/products/' }, { label: p.name, href: p.slug }, { label: sp.label, href: url }];
  const subst = ctx.limitFacts[p.id] || {};
  const fill = (t) => t.replace(/\{(\w+)\}/g, (_, k) => (k in subst ? subst[k] : `{${k}}`));
  const kinds = new Set();
  const section = (s) => {
    let fig = '';
    if (s.diagram) {
      kinds.add(s.diagram.id);
      fig = diagramFigure({ id: 'dg-' + s.id, kind: s.diagram.id, title: s.diagram.title, lede: fill(s.diagram.lede), data: ctx.diagrams[s.diagram.id], level: 3, stageLabel: s.diagram.stageLabel || s.diagram.title }).html;
    }
    return html`<section class="sec doc-sec" aria-labelledby="${s.id}"><div class="wrap">
  ${secHead({ title: s.title, cat: p.cat, id: s.id })}
  ${(s.paragraphs || []).length ? html`<div class="prose-block">${s.paragraphs.map((t) => html`<p>${fill(t)}</p>`)}</div>` : ''}
  ${s.list ? html`<dl class="term-list">${s.list.map((x) => html`<dt>${fill(x.term)}</dt><dd>${fill(x.text)}</dd>`)}</dl>` : ''}
  ${s.table ? matrixTable(s.table) : ''}
  ${s.ladder ? ladderTable(readJson(path.join(DATA, 'generated', 'phasor-ladder.json')).rungs) : ''}
  ${fig}
  ${s.grid ? gridTable(ctx.diagrams[s.grid]) : ''}
  ${(s.paragraphsAfter || []).length ? html`<div class="prose-block">${s.paragraphsAfter.map((t) => html`<p>${fill(t)}</p>`)}</div>` : ''}
  ${(s.snippets || []).map((sn) => (sn.text ? codeBlock({ code: sn.text.trim(), lang: sn.lang || 'sql', label: sn.label || 'SQL' }) : html`${codeBlock({ code: read(sn.source), label: sn.label || 'C#' })}${sn.output ? codeBlock({ code: read(sn.output), label: 'Output', out: true, lang: 'text' }) : ''}`))}
  ${s.listAfter ? html`<dl class="term-list">${s.listAfter.map((x) => html`<dt>${fill(x.term)}</dt><dd>${fill(x.text)}</dd>`)}</dl>` : ''}
  ${s.more ? html`<p class="more-link"><a href="${s.more.href}">${s.more.label}</a></p>` : ''}
</div></section>`;
  };
  const body = d.sections.map(section);
  // sizzle-first sub-page (design-language section 8): a promise headline, number tiles with the conditions behind a disclosure, then the explainer
  const sz = d.sizzle;
  const stats = sz ? resolveStats(ctx, sz.stats, subst) : [];
  const links = sz ? (sz.evidence || []).map((id) => ctx.evidence.find((e) => e.id === id)).filter(Boolean) : [];
  const head = sz
    ? pageHead({ crumbs, eyebrow: sz.eyebrow, h1: sz.headline, lede: sz.sub, wide: true, extra: html`<div class="actions"><a class="btn btn-primary" href="#numbers">${sz.cta || 'See the numbers'}</a><a class="btn btn-ghost" href="${p.slug}">${p.name} overview</a></div>` })
    : pageHead({ crumbs, eyebrow: `${p.name} ${sp.label.toLowerCase()}`, h1: d.h1 || d.title, lede: d.intro, wide: true, extra: d.maturity ? html`<ul class="facts"><li class="fact">${d.maturity}</li></ul>` : '' });
  const inner = html`${head}
${productSubnav(p, sp.path)}
${sz ? numbersBlock({ stats, numbers: sz.numbers, subst, links }) : ''}
${sz ? html`<section class="sec doc-sec" aria-labelledby="h-brief"><div class="wrap">${secHead({ title: sz.briefTitle || 'In brief', cat: p.cat, id: 'h-brief' })}<p class="prose-block">${d.intro}</p>${d.maturity ? html`<ul class="facts"><li class="fact">${d.maturity}</li></ul>` : ''}</div></section>` : ''}
<section class="sec"><div class="wrap">
  <nav class="toc" aria-labelledby="toc-h"><h2 id="toc-h">On this page</h2><ul>${d.sections.map((s) => html`<li><a href="#${s.id}">${s.title}</a></li>`)}</ul></nav>
</div></section>
${body}`;
  const main = sz ? html`<div class="sizzle-page">${inner}</div>` : inner;
  return {
    url, type: 'product-docs', section: 'products', cat: p.cat,
    title: d.h1 && d.title.length < 40 ? d.title : d.title, description: d.description, crumbs, jsonld: [crumbsLd(ctx, crumbs)],
    scripts: [...kinds].map((k) => `/assets/diagrams/${k}.js`), main,
  };
}
