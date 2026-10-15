import { html, raw } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';
import { inline, plain } from '../inline.mjs';
import { deriveDiagram } from '../research-diagrams.mjs';
import { diagramFigure } from '../diagram.mjs';
import { blocksOf, sectionTables, sectionWords } from '../blocks.mjs';

// A research article, written as established findings. Data: research/<id>.json with `finding`, `refutedBy` (optional), `related`,
// `references`, optional `diagram`, and sections[{ id, title, paragraphs[], list?, tables? }] or sections with ordered `blocks`.
// The sections "finding", "refuted-by" and "references" are rendered as their own elements; every other section is rendered in order.
// Optional extras for a sizzle-first article: `headline` + `sub` (hero), `stats` (big number tiles with their conditions in small print),
// `words` (a strip of the terms used), sections flagged `untitled` (no heading) or `disclosure` (a closed "Go deeper" block after the
// main text), and `closing` (a last paragraph with actions).
const SKIP = new Set(['finding', 'refuted-by', 'references', 'reference']);
const isNum = (c) => /^[~\d][\d.,%\s/~-]*(\s(ms|KB|MB|bytes))?$/.test(String(c).trim()) && String(c).length < 18;

function table(tb) {
  return html`<div class="tbl-wrap"><table class="tbl"><caption>${tb.caption}</caption>
<thead><tr>${tb.columns.map((c, i) => html`<th scope="col"${i > 0 ? raw(' class="num-h"') : ''}>${inline(c)}</th>`)}</tr></thead>
<tbody>${tb.rows.map((r) => html`<tr><th scope="row">${inline(r[0])}</th>${r.slice(1).map((c) => html`<td${isNum(c) ? raw(' class="num"') : ''}>${inline(c)}</td>`)}</tr>`)}</tbody></table></div>`;
}

function blocks(sec, refs) {
  const bs = blocksOf(sec);
  const out = [];
  let run = [];
  const flush = () => { if (run.length) { out.push(html`<div class="prose-block">${run}</div>`); run = []; } };
  for (const b of bs) {
    if (b.t === 'p') run.push(html`<p>${inline(b.text, refs)}</p>`);
    else if (b.t === 'quote') run.push(html`<blockquote class="pq"><p>${inline(b.text, refs)}</p></blockquote>`);
    else {
      flush();
      if (b.t === 'sub') out.push(html`<h3 class="sub-h">${inline(b.text, refs)}</h3>`);
      else if (b.t === 'ul') out.push(html`<ul class="limits">${b.items.map((t) => html`<li>${inline(t, refs)}</li>`)}</ul>`);
      else if (b.t === 'table') out.push(table(b));
      else throw new Error(`research section ${sec.id}: unknown block ${b.t}`);
    }
  }
  flush();
  return out;
}

export default function researchArticle(ctx, r) {
  const url = `/research/${r.id}/`;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Research', href: '/research/' }, { label: r.title, href: url }];
  const refs = Object.fromEntries(r.references.map((x, i) => [x.id, i + 1]));
  const all = r.sections.filter((s) => !SKIP.has(s.id));
  const sections = all.filter((s) => !s.disclosure);
  const deeper = all.filter((s) => s.disclosure);
  const titled = sections.filter((s) => !s.untitled);
  const refSec = r.sections.find((s) => s.id === 'refuted-by');
  const refutedBy = refSec ? refSec.paragraphs.join(' ') : r.refutedBy;
  const words = r.sections.reduce((a, s) => a + sectionWords(s), 0) + r.finding.split(/\s+/).length;
  const minutes = Math.max(1, Math.round(words / 220));
  const tables = r.sections.reduce((a, s) => a + sectionTables(s).length, 0);
  const ddata = deriveDiagram(r, ctx);
  const fig = ddata ? diagramFigure({
    id: 'dg', kind: r.diagram.kind, title: r.diagram.title, lede: r.diagram.lede, data: ddata, stageLabel: r.diagram.stageLabel || r.diagram.title,
    caption: r.diagram.caption ? html`${inline(r.diagram.caption)}` : html`Drawn from the numbers in this article: the tables below and the figures in its text. Nothing is measured on this page.`,
  }) : null;
  const toc = [{ id: 'finding', t: 'The finding' }, ...(fig ? [{ id: 'diagram', t: r.diagram.title }] : []), ...titled.map((s) => ({ id: s.id, t: s.title })), ...(deeper.length ? [{ id: 'go-deeper', t: 'Go deeper' }] : []), ...(refutedBy ? [{ id: 'refuted-by', t: 'Refuted by' }] : []), ...(r.relatedArticles.length ? [{ id: 'related', t: 'Related' }] : []), ...(r.references.length ? [{ id: 'references', t: 'References' }] : [])];
  const ld = { '@context': 'https://schema.org', '@type': 'Article', headline: r.title, description: r.summary, url: ctx.site.url + url, keywords: r.topics.join(', '), about: r.topics, author: { '@id': ctx.site.url + '/#org' }, publisher: { '@id': ctx.site.url + '/#org' }, mainEntityOfPage: ctx.site.url + url };
  const section = (s) => html`<section class="sec" id="${s.id}" aria-labelledby="h-${s.id}"><div class="wrap">
  ${s.untitled ? html`<h2 class="sr-only" id="h-${s.id}">${s.title}</h2>` : secHead({ title: s.title, id: 'h-' + s.id })}
  ${blocks(s, refs)}
</div></section>`;
  const body = html`
${pageHead({ crumbs, eyebrow: r.topics.join(', '), h1: r.headline || r.title, lede: r.sub || r.summary, wide: true,
    extra: html`<ul class="facts"><li class="fact">${minutes} min read</li><li class="fact">${titled.length} section${titled.length > 1 ? 's' : ''}</li>${deeper.length ? html`<li class="fact">${deeper.length} to go deeper</li>` : ''}${tables ? html`<li class="fact">${tables} table${tables > 1 ? 's' : ''}</li>` : ''}${r.references.length ? html`<li class="fact">${r.references.length} reference${r.references.length > 1 ? 's' : ''}</li>` : ''}</ul>` })}
${r.stats ? html`<section class="sec" id="numbers" aria-labelledby="h-numbers"><div class="wrap">
  <h2 class="sr-only" id="h-numbers">The numbers</h2>
  <ul class="stats stats-big rtiles">${r.stats.map((x) => html`<li class="stat"><p class="rt-v">${x.value}</p><p class="rt-l">${x.label}</p><p class="rt-s">${inline(x.small)}</p></li>`)}</ul>
</div></section>` : ''}
<section class="sec" id="finding" aria-labelledby="h-finding"><div class="wrap">
  <div class="finding"><h2 id="h-finding" class="finding-label">Finding</h2><p class="finding-text">${inline(r.finding, refs)}</p></div>
</div></section>
${r.words ? html`<section class="sec" id="words" aria-labelledby="h-words"><div class="wrap">
  <div class="words"><h2 id="h-words" class="finding-label">Words</h2><dl class="words-list">${r.words.map((w) => html`<div><dt>${w.term}</dt><dd>${inline(w.text)}</dd></div>`)}</dl></div>
</div></section>` : ''}
${fig ? html`<section class="sec fig-sec" id="diagram" aria-label="${r.diagram.title}"><div class="wrap">${fig.html}</div></section>` : ''}
<section class="sec"><div class="wrap">
  <nav class="toc" aria-labelledby="toc-h"><h2 id="toc-h">On this page</h2><ul>${toc.map((x) => html`<li><a href="#${x.id}">${x.t}</a></li>`)}</ul></nav>
</div></section>
${sections.map(section)}
${deeper.length ? html`<section class="sec" id="go-deeper" aria-labelledby="h-go-deeper"><div class="wrap">
  ${secHead({ title: 'Go deeper', id: 'h-go-deeper', line: `${deeper.length} closed sections: open the ones you want` })}
  ${deeper.map((s) => html`<details class="dia-data deeper" id="${s.id}"><summary>${s.summary || s.title}</summary><div class="deeper-body">${blocks(s, refs)}</div></details>`)}
</div></section>` : ''}
${refutedBy ? html`<section class="sec" id="refuted-by" aria-labelledby="h-refuted"><div class="wrap">
  <aside class="callout warn refuted"><h2 id="h-refuted">Refuted by</h2><p>${inline(refutedBy, refs)}</p></aside>
</div></section>` : ''}
${r.relatedArticles.length ? html`<section class="sec" id="related" aria-labelledby="h-related"><div class="wrap">
  ${secHead({ title: 'Related', id: 'h-related' })}
  <ul class="related-list">${r.relatedArticles.map((a) => html`<li><a href="${a.href}">${a.title}</a><span>${a.summary}</span></li>`)}</ul>
</div></section>` : ''}
${r.references.length ? html`<section class="sec" id="references" aria-labelledby="h-references"><div class="wrap">
  ${secHead({ title: 'References', id: 'h-references' })}
  <ol class="refs">${r.references.map((x) => html`<li id="ref-${x.id}">${x.url ? html`<a href="${x.url}" rel="noopener">${x.text}</a>` : x.text}</li>`)}</ol>
</div></section>` : ''}
${r.closing ? html`<section class="sec" id="closing" aria-label="Get the package"><div class="wrap"><div class="callout closing">
  <p>${inline(r.closing.text)}</p>
  <div class="actions">${r.closing.links.map((l, i) => html`<a class="btn ${i ? 'btn-ghost' : 'btn-primary'}" href="${l.href}">${l.label}</a>`)}</div>
  <p class="muted">${ctx.licence.oneLine} <a href="/licence/">Read the licence terms</a>.</p>
</div></div></section>` : ''}`;
  const main = r.stats ? html`<div class="sizzle-page">${body}</div>` : body;
  return {
    url, type: 'research-article', section: 'research',
    title: r.title, description: r.description || r.summary, crumbs, jsonld: [ld, crumbsLd(ctx, crumbs)], main,
    scripts: ddata ? [`/assets/diagrams/${r.diagram.kind}.js`] : [],
  };
}
