import { html, raw } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';
import { evidenceCard, evidenceTiles, matrixTable, stat, productSubnav } from '../partials.mjs';
import { diagramFigure } from '../diagram.mjs';

// Evidence template. Hub = every record's card. Record = claim, headline, where we lose or what it costs (stated once, at the top),
// method stated ONCE, then sections with data-driven charts and tables, and limits. Experiment ids, run names and dates
// stay out of the data entirely; a record may be flat (tables) or sectioned (sections).
export function hub(ctx) {
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Evidence', href: '/evidence/' }];
  const main = html`
${pageHead({ crumbs, h1: 'Evidence', lede: 'What we have tested and measured, and how. Every record states its method once and lists what went against the library.' })}
<section class="sec" aria-labelledby="h-all"><div class="wrap"><h2 class="sr-only" id="h-all">All measurements</h2>
  <ul class="cards">${ctx.evidence.map((e) => evidenceCard(e, ctx.byId))}</ul>
</div></section>`;
  return {
    url: '/evidence/', type: 'evidence', section: 'evidence',
    title: 'Evidence', description: 'Every published test and measurement of our libraries, with the method stated once and the losses shown beside the results.',
    crumbs, jsonld: [crumbsLd(ctx, [...crumbs])], main,
  };
}

export function record(ctx, e) {
  const p = ctx.byId[e.product];
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Evidence', href: '/evidence/' }, { label: e.title, href: e.slug }];
  const tiles = evidenceTiles(e);
  const m = e.method;
  const sections = e.sections || [{ id: 'results', title: 'Results', intro: [], tables: e.tables, notes: [], losses: [] }];
  const kinds = new Set();
  const rows = [['Workload', m.workload], ['Engines', m.engines.join('; ')], ['Check', m.verification], ['Statistic', m.statistic], ['Machine', m.machine], ['Memory and logging', m.memory], ['Ties', m.tie]].filter(([, v]) => v);
  // losses: the ones each section states, then the record's own
  const losses = [];
  for (const s of sections) {
    for (const l of s.losses || []) losses.push({ sec: s, text: l });
  }
  for (const l of e.losses || []) losses.push({ sec: null, text: l });
  // a section may carry a diagram drawn from a generated data file ({ id, kind, stageLabel, caption }); its table alternative sits beside it
  const sectionFigure = (s) => {
    if (!s.diagram) return '';
    kinds.add(s.diagram.kind);
    return diagramFigure({
      id: 'dg-' + s.id, kind: s.diagram.kind, title: '', lede: '', level: 3,
      data: ctx.diagrams[s.diagram.id], stageLabel: s.diagram.stageLabel || s.title,
      caption: html`${s.diagram.caption}`,
    }).html;
  };
  const main = html`
${pageHead({ crumbs, eyebrow: `${p.name} evidence`, h1: e.title, lede: e.claim, wide: true,
    extra: html`<ul class="facts"><li class="fact">${e.scope === 'engine' ? 'Engine alone, without the optional plane' : e.scope}</li><li class="fact">Losses shown</li></ul>` })}
${productSubnav(p, e.slug === p.evidence[0]?.slug ? 'evidence' : 'evidence')}
${e.sections && e.sections.length > 2 ? html`<section class="sec"><div class="wrap">
  <nav class="toc" aria-labelledby="toc-h"><h2 id="toc-h">On this page</h2><ul><li><a href="#h-head">Headline</a></li><li><a href="#h-loss">${e.lossTitle || 'What it costs'}</a></li><li><a href="#h-method">How it was measured</a></li>${sections.map((s) => html`<li><a href="#${s.id}">${s.title}</a></li>`)}<li><a href="#h-lim">Limits</a></li></ul></nav>
</div></section>` : ''}

<section class="sec" aria-labelledby="h-head"><div class="wrap">
  ${secHead({ title: 'Headline', cat: p.cat, id: 'h-head' })}
  <dl class="stats">${tiles.map((t) => stat(t.label, t.value))}</dl>
</div></section>

<section class="sec" aria-labelledby="h-loss"><div class="wrap">
  ${secHead({ title: e.lossTitle || 'What it costs', line: 'Every loss in this record, stated once', cat: p.cat, id: 'h-loss' })}
  ${losses.length ? html`<ul class="limits">${losses.map((l) => html`<li>${l.sec && (e.sections || []).length ? html`<a href="#${l.sec.id}">${l.sec.title}</a>: ` : ''}${l.text}</li>`)}</ul>` : html`<p class="prose-block">Nothing in this record went against the library.</p>`}
</div></section>

<section class="sec method" aria-labelledby="h-method"><div class="wrap">
  ${secHead({ title: 'How it was measured', line: 'Stated once, for every figure on this page', cat: p.cat, id: 'h-method' })}
  <dl>${rows.map(([k, v]) => html`<dt>${k}</dt><dd>${v}</dd>`)}</dl>
</div></section>

${sections.map((s) => html`<section class="sec" aria-labelledby="${s.id}"><div class="wrap">
  ${secHead({ title: s.title, cat: p.cat, id: s.id })}
  ${(s.intro || []).length ? html`<div class="prose-block">${s.intro.map((t) => html`<p>${t}</p>`)}</div>` : ''}
  ${sectionFigure(s)}
  ${(s.tables || []).map((tb) => matrixTable(tb))}
  ${(s.notes || []).length ? html`<div class="prose-block">${s.notes.map((t) => html`<p>${t}</p>`)}</div>` : ''}
</div></section>`)}

<section class="sec" aria-labelledby="h-lim"><div class="wrap">
  ${secHead({ title: 'Limits', cat: p.cat, id: 'h-lim' })}
  <ul class="limits">${e.limits.map((l) => html`<li>${l}</li>`)}</ul>
</div></section>

<section class="sec" aria-labelledby="h-rel"><div class="wrap">
  ${secHead({ title: 'Related', cat: p.cat, id: 'h-rel' })}
  <div class="related"><div><h3>Product</h3><ul><li><a class="pill" href="${p.href}">${p.name}</a></li></ul></div><div><h3>More evidence</h3><ul><li><a class="pill" href="/evidence/">All evidence</a></li></ul></div></div>
</div></section>`;
  return {
    url: e.slug, type: 'evidence', section: 'evidence', cat: p.cat,
    title: e.title, description: e.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
    scripts: [...kinds].map((k) => `/assets/diagrams/${k}.js`),
  };
}
