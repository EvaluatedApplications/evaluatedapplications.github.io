import { html, raw } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';
import { inline, plain } from '../inline.mjs';
import { deriveDiagram } from '../research-diagrams.mjs';
import { diagramFigure } from '../diagram.mjs';

// A research article, written as established findings. Data: research/<id>.json with `finding`, `refutedBy`, `related`, `references`,
// optional `diagram`, and sections[{ id, title, paragraphs[], list?, tables? }]. The sections "finding", "refuted-by" and
// "references" are rendered as their own elements; every other section is rendered in order, with its tables after its text.
const SKIP = new Set(['finding', 'refuted-by', 'references', 'reference']);
const isNum = (c) => /^[~\d][\d.,%\s/~-]*(\s(ms|KB|MB|bytes))?$/.test(String(c).trim()) && String(c).length < 18;

function table(tb) {
  return html`<div class="tbl-wrap"><table class="tbl"><caption>${tb.caption}</caption>
<thead><tr>${tb.columns.map((c, i) => html`<th scope="col"${i > 0 ? raw(' class="num-h"') : ''}>${c}</th>`)}</tr></thead>
<tbody>${tb.rows.map((r) => html`<tr><th scope="row">${r[0]}</th>${r.slice(1).map((c) => html`<td${isNum(c) ? raw(' class="num"') : ''}>${c}</td>`)}</tr>`)}</tbody></table></div>`;
}

export default function researchArticle(ctx, r) {
  const url = `/research/${r.id}/`;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Research', href: '/research/' }, { label: r.title, href: url }];
  const refs = Object.fromEntries(r.references.map((x, i) => [x.id, i + 1]));
  const sections = r.sections.filter((s) => !SKIP.has(s.id));
  const refSec = r.sections.find((s) => s.id === 'refuted-by');
  const words = r.sections.reduce((a, s) => a + (s.paragraphs || []).join(' ').split(/\s+/).length, 0) + r.finding.split(/\s+/).length;
  const minutes = Math.max(1, Math.round(words / 220));
  const tables = r.sections.reduce((a, s) => a + (s.tables || []).length, 0);
  const ddata = deriveDiagram(r);
  const fig = ddata ? diagramFigure({
    id: 'dg', kind: r.diagram.kind, title: r.diagram.title, lede: r.diagram.lede, data: ddata, stageLabel: r.diagram.stageLabel || r.diagram.title,
    caption: html`Drawn from the numbers in this article: the tables below and the figures in its text. Nothing is measured on this page.`,
  }) : null;
  const toc = [{ id: 'finding', t: 'The finding' }, ...(fig ? [{ id: 'diagram', t: r.diagram.title }] : []), ...sections.map((s) => ({ id: s.id, t: s.title })), { id: 'refuted-by', t: 'Refuted by' }, ...(r.relatedArticles.length ? [{ id: 'related', t: 'Related' }] : []), ...(r.references.length ? [{ id: 'references', t: 'References' }] : [])];
  const ld = { '@context': 'https://schema.org', '@type': 'Article', headline: r.title, description: r.summary, url: ctx.site.url + url, keywords: r.topics.join(', '), about: r.topics, author: { '@id': ctx.site.url + '/#org' }, publisher: { '@id': ctx.site.url + '/#org' }, mainEntityOfPage: ctx.site.url + url };
  const main = html`
${pageHead({ crumbs, eyebrow: r.topics.join(', '), h1: r.title, lede: r.summary, wide: true,
    extra: html`<ul class="facts"><li class="fact">${minutes} min read</li><li class="fact">${sections.length} sections</li>${tables ? html`<li class="fact">${tables} table${tables > 1 ? 's' : ''}</li>` : ''}${r.references.length ? html`<li class="fact">${r.references.length} reference${r.references.length > 1 ? 's' : ''}</li>` : ''}</ul>` })}
<section class="sec" id="finding" aria-labelledby="h-finding"><div class="wrap">
  <div class="finding"><h2 id="h-finding" class="finding-label">Finding</h2><p class="finding-text">${inline(r.finding, refs)}</p></div>
</div></section>
${fig ? html`<section class="sec fig-sec" id="diagram" aria-label="${r.diagram.title}"><div class="wrap">${fig.html}</div></section>` : ''}
<section class="sec"><div class="wrap">
  <nav class="toc" aria-labelledby="toc-h"><h2 id="toc-h">On this page</h2><ul>${toc.map((x) => html`<li><a href="#${x.id}">${x.t}</a></li>`)}</ul></nav>
</div></section>
${sections.map((s) => html`<section class="sec" id="${s.id}" aria-labelledby="h-${s.id}"><div class="wrap">
  ${secHead({ title: s.title, id: 'h-' + s.id })}
  <div class="prose-block">${(s.paragraphs || []).map((t) => html`<p>${inline(t, refs)}</p>`)}</div>
  ${s.list ? html`<ul class="limits">${s.list.map((t) => html`<li>${inline(t, refs)}</li>`)}</ul>` : ''}
  ${(s.tables || []).map(table)}
</div></section>`)}
<section class="sec" id="refuted-by" aria-labelledby="h-refuted"><div class="wrap">
  <aside class="callout warn refuted"><h2 id="h-refuted">Refuted by</h2><p>${inline(refSec ? refSec.paragraphs.join(' ') : r.refutedBy, refs)}</p></aside>
</div></section>
${r.relatedArticles.length ? html`<section class="sec" id="related" aria-labelledby="h-related"><div class="wrap">
  ${secHead({ title: 'Related', id: 'h-related' })}
  <ul class="related-list">${r.relatedArticles.map((a) => html`<li><a href="${a.href}">${a.title}</a><span>${a.summary}</span></li>`)}</ul>
</div></section>` : ''}
${r.references.length ? html`<section class="sec" id="references" aria-labelledby="h-references"><div class="wrap">
  ${secHead({ title: 'References', id: 'h-references' })}
  <ol class="refs">${r.references.map((x) => html`<li id="ref-${x.id}">${x.url ? html`<a href="${x.url}" rel="noopener">${x.text}</a>` : x.text}</li>`)}</ol>
</div></section>` : ''}`;
  return {
    url, type: 'research-article', section: 'research',
    title: r.title, description: r.description || r.summary, crumbs, jsonld: [ld, crumbsLd(ctx, crumbs)], main,
    scripts: ddata ? [`/assets/diagrams/${r.diagram.kind}.js`] : [],
  };
}