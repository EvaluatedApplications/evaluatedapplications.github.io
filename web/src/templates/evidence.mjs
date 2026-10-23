import { html, raw } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';
import { evidenceCard, evidenceTiles, matrixTable, stat, productSubnav } from '../partials.mjs';
import { diagramFigure } from '../diagram.mjs';

// Evidence template. Hub = every record's card. Record = claim, headline, where we lose (stated once, at the top),
// method stated ONCE, then sections with data-driven charts and tables, and limits. Experiment ids, run names and dates
// stay out of the data entirely; a record may be flat (tables) or sectioned (sections).
export function hub(ctx) {
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Evidence', href: '/evidence/' }];
  const main = html`
${pageHead({ crumbs, h1: 'Evidence', lede: 'What we have measured and how. Every record states its method once, checks that every engine returns the same result before timing, and lists where we lose.' })}
<section class="sec" aria-labelledby="h-all"><div class="wrap"><h2 class="sr-only" id="h-all">All measurements</h2>
  <ul class="cards">${ctx.evidence.map((e) => evidenceCard(e, ctx.byId))}</ul>
</div></section>`;
  return {
    url: '/evidence/', type: 'evidence', section: 'evidence',
    title: 'Evidence', description: 'Every published measurement of our libraries, with the method stated once and the losses shown beside the wins.',
    crumbs, jsonld: [crumbsLd(ctx, [...crumbs])], main,
  };
}

// ---- the HoloDb record: headline, one animated chart, the explainer, how it was measured, what it does not do ----
function recordStory(ctx, e) {
  const p = ctx.byId[e.product], st = e.story;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Evidence', href: '/evidence/' }, { label: e.title, href: e.slug }];
  const tiles = evidenceTiles(e);
  const fig = (id, kind, data, stageLabel, caption) => diagramFigure({ id, kind, title: '', lede: '', level: 3, data, stageLabel, caption }).html;
  const main = html`
${pageHead({ crumbs, eyebrow: `${p.name} evidence`, h1: e.title, lede: e.claim, wide: true,
    extra: html`<ul class="facts"><li class="fact">${e.scope}</li><li class="fact">Losses shown in the chart</li></ul>` })}
${productSubnav(p, 'evidence')}
<section class="sec" aria-labelledby="h-head"><div class="wrap">
  ${secHead({ title: 'Headline', cat: p.cat, id: 'h-head' })}
  <dl class="stats stats-big">${tiles.map((t) => stat(t.label, t.value))}</dl>
  <p class="conditions">${st.conditions}</p>
</div></section>

<section class="sec" id="chart" aria-labelledby="h-chart"><div class="wrap">
  ${secHead({ title: st.chart.title, line: st.chart.line, cat: p.cat, id: 'h-chart' })}
  ${fig('hr', st.chart.fig, ctx.diagrams[st.chart.fig], st.chart.stageLabel, html`${st.chart.caption}`)}
</div></section>

<section class="sec" id="how" aria-labelledby="h-how"><div class="wrap">
  ${secHead({ title: st.how.title, line: st.how.line, cat: p.cat, id: 'h-how' })}
  ${fig('hw', st.how.fig, ctx.diagrams[st.how.fig], st.how.stageLabel, html`The motion is schematic. The figures in each caption are measured, and each is read from the result files.`)}
</div></section>

<section class="sec" aria-labelledby="h-fit"><div class="wrap">
  <div class="callout tool-band"><div class="tb-copy"><h2 id="h-fit">${st.fit.title}</h2><p>${st.fit.line}</p></div><div class="actions"><a class="btn btn-primary" href="${st.fit.href}">${st.fit.label}</a></div></div>
</div></section>

<section class="sec method" aria-labelledby="h-method"><div class="wrap">
  ${secHead({ title: st.method.title, line: st.method.line, cat: p.cat, id: 'h-method' })}
  <dl>${st.method.rows.map(([k, v]) => html`<dt>${k}</dt><dd>${v}</dd>`)}</dl>
  ${matrixTable(st.build)}
</div></section>

<section class="sec" id="limits" aria-labelledby="h-lim"><div class="wrap">
  ${secHead({ title: st.limits.title, line: st.limits.line, cat: p.cat, id: 'h-lim' })}
  ${matrixTable(st.limits.table)}
</div></section>

<section class="sec" aria-labelledby="h-deep"><div class="wrap">
  <div class="callout tool-band"><div class="tb-copy"><h2 id="h-deep">${st.deep.title}</h2><p>${st.deep.line}</p></div><div class="actions"><a class="btn btn-ghost" href="${st.deep.href}">${st.deep.label}</a></div></div>
</div></section>

<section class="sec" aria-labelledby="h-rel"><div class="wrap">
  ${secHead({ title: 'Related', cat: p.cat, id: 'h-rel' })}
  <div class="related"><div><h3>Product</h3><ul><li><a class="pill" href="${p.href}">${p.name}</a></li></ul></div><div><h3>More evidence</h3><ul><li><a class="pill" href="/evidence/">All evidence</a></li></ul></div></div>
</div></section>`;
  return {
    url: e.slug, type: 'evidence', section: 'evidence', cat: p.cat,
    title: e.title, description: e.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
    scripts: [st.chart.fig, st.how.fig].map((k) => `/assets/diagrams/${k}.js`),
  };
}

// ---- the deep dive: the plane and constructed workloads; each section states what it costs in place ----
function recordDeep(ctx, e) {
  const p = ctx.byId[e.product];
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Evidence', href: '/evidence/' }, { label: p.name, href: p.evidence[0].slug }, { label: 'Deep dive', href: e.slug }];
  const m = e.method, kinds = new Set();
  const rows = [['Workload', m.workload], ['Engines', m.engines.join('; ')], ['Check', m.verification], ['Statistic', m.statistic], ['Machine', m.machine], ['Memory and logging', m.memory]].filter(([, v]) => v);
  const timingFigure = (tb) => {
    kinds.add('latency-bars');
    return diagramFigure({
      id: 'lb-' + tb.id, kind: 'latency-bars', title: '', lede: '', level: 3,
      data: { schema: 'ea-diagram/1', kind: 'latency-bars', id: tb.id, caption: tb.caption, columns: tb.columns, focus: tb.focus, rival: tb.rival, tieBelow: e.tieBelow ?? 1.15, resultLabel: tb.resultLabel, qualifiers: tb.qualifiers, rows: tb.rows },
      stageLabel: 'Latency chart. Use the up and down arrows to read each row.', caption: html`Drawn from the table, which holds every value.`,
    }).html;
  };
  const sectionFigure = (s) => {
    if (!s.diagram) return '';
    kinds.add(s.diagram.kind);
    return diagramFigure({ id: 'dg-' + s.id, kind: s.diagram.kind, title: '', lede: '', level: 3, data: ctx.diagrams[s.diagram.id], stageLabel: s.diagram.stageLabel || s.title, caption: html`${s.diagram.caption}` }).html;
  };
  const main = html`
${pageHead({ crumbs, eyebrow: `${p.name} evidence`, h1: e.title, lede: e.claim, wide: true, extra: html`<ul class="facts"><li class="fact">${e.scope}</li></ul>` })}
${productSubnav(p, 'evidence')}
<section class="sec"><div class="wrap">
  <nav class="toc" aria-labelledby="toc-h"><h2 id="toc-h">On this page</h2><ul><li><a href="#h-method">How it was measured</a></li>${e.sections.map((s) => html`<li><a href="#${s.id}">${s.title}</a></li>`)}<li><a href="#h-lim">Limits</a></li></ul></nav>
</div></section>
<section class="sec method" aria-labelledby="h-method"><div class="wrap">
  ${secHead({ title: 'How it was measured', cat: p.cat, id: 'h-method' })}
  <dl>${rows.map(([k, v]) => html`<dt>${k}</dt><dd>${v}</dd>`)}</dl>
</div></section>
${e.sections.map((s) => html`<section class="sec" aria-labelledby="${s.id}"><div class="wrap">
  ${secHead({ title: s.title, cat: p.cat, id: s.id })}
  ${(s.intro || []).length ? html`<div class="prose-block">${s.intro.map((t) => html`<p>${t}</p>`)}</div>` : ''}
  ${sectionFigure(s)}
  ${(s.tables || []).map((tb) => (tb.kind === 'matrix' ? matrixTable(tb) : timingFigure(tb)))}
  ${(s.notes || []).length ? html`<div class="prose-block">${s.notes.map((t) => html`<p>${t}</p>`)}</div>` : ''}
  ${(s.costs || []).length ? html`<h3 class="sub-h">What it costs</h3><ul class="limits">${s.costs.map((l) => html`<li>${l}</li>`)}</ul>` : ''}
</div></section>`)}
<section class="sec" aria-labelledby="h-lim"><div class="wrap">
  ${secHead({ title: 'Limits', cat: p.cat, id: 'h-lim' })}
  <ul class="limits">${e.limits.map((l) => html`<li>${l}</li>`)}</ul>
</div></section>
<section class="sec" aria-labelledby="h-rel"><div class="wrap">
  ${secHead({ title: 'Related', cat: p.cat, id: 'h-rel' })}
  <div class="related"><div><h3>Product</h3><ul><li><a class="pill" href="${p.href}">${p.name}</a></li><li><a class="pill" href="${p.evidence[0].slug}">${p.name} benchmarks</a></li></ul></div><div><h3>More evidence</h3><ul><li><a class="pill" href="/evidence/">All evidence</a></li></ul></div></div>
</div></section>`;
  return {
    url: e.slug, type: 'evidence', section: 'evidence', cat: p.cat,
    title: e.title, description: e.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
    scripts: [...kinds].map((k) => `/assets/diagrams/${k}.js`),
  };
}

export function record(ctx, e) {
  if (e.layout === 'story') return recordStory(ctx, e);
  if (e.layout === 'deep') return recordDeep(ctx, e);
  const p = ctx.byId[e.product];
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Evidence', href: '/evidence/' }, { label: e.title, href: e.slug }];
  const tiles = evidenceTiles(e);
  const m = e.method;
  const sections = e.sections || [{ id: 'results', title: 'Results', intro: [], tables: e.tables, notes: [], losses: [] }];
  const kinds = new Set();
  const rows = [['Workload', m.workload], ['Engines', m.engines.join('; ')], ['Check', m.verification], ['Statistic', m.statistic], ['Machine', m.machine], ['Memory and logging', m.memory], ['Ties', m.tie]].filter(([, v]) => v);
  // losses: derived from timing rows where another engine is ahead, plus the ones each section states
  const losses = [];
  for (const s of sections) {
    for (const tb of s.tables || []) if (tb.kind !== 'matrix') for (const r of tb.rows) if (r.state === 'loss') losses.push({ sec: s, text: `${r.label} (${tb.columns[tb.focus]}): ${r.verdict}.` });
    for (const l of s.losses || []) losses.push({ sec: s, text: l });
  }
  for (const l of e.losses || []) losses.push({ sec: null, text: l });
  const timingFigure = (s, tb) => {
    kinds.add('latency-bars');
    return diagramFigure({
      id: 'lb-' + tb.id, kind: 'latency-bars', title: '', lede: '', level: 3,
      data: { schema: 'ea-diagram/1', kind: 'latency-bars', id: tb.id, caption: tb.caption, columns: tb.columns, focus: tb.focus, rival: tb.rival, tieBelow: e.tieBelow ?? 1.15, resultLabel: tb.resultLabel, qualifiers: tb.qualifiers, rows: tb.rows },
      stageLabel: 'Latency chart. Use the up and down arrows to read each row.',
      caption: html`Drawn from the table, which holds every value.`,
    }).html;
  };
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
  <nav class="toc" aria-labelledby="toc-h"><h2 id="toc-h">On this page</h2><ul><li><a href="#h-head">Headline</a></li><li><a href="#h-loss">${e.lossTitle || 'Where it is slower'}</a></li><li><a href="#h-method">How it was measured</a></li>${sections.map((s) => html`<li><a href="#${s.id}">${s.title}</a></li>`)}<li><a href="#h-lim">Limits</a></li></ul></nav>
</div></section>` : ''}

<section class="sec" aria-labelledby="h-head"><div class="wrap">
  ${secHead({ title: 'Headline', cat: p.cat, id: 'h-head' })}
  <dl class="stats">${tiles.map((t) => stat(t.label, t.value))}</dl>
</div></section>

<section class="sec" aria-labelledby="h-loss"><div class="wrap">
  ${secHead({ title: e.lossTitle || 'Where it is slower', line: 'Every loss in this record, stated once', cat: p.cat, id: 'h-loss' })}
  ${losses.length ? html`<ul class="limits">${losses.map((l) => html`<li>${l.sec && (e.sections || []).length ? html`<a href="#${l.sec.id}">${l.sec.title}</a>: ` : ''}${l.text}</li>`)}</ul>` : html`<p class="prose-block">This record measures one implementation, so no other engine is faster in it.</p>`}
</div></section>

<section class="sec method" aria-labelledby="h-method"><div class="wrap">
  ${secHead({ title: 'How it was measured', line: 'Stated once, for every figure on this page', cat: p.cat, id: 'h-method' })}
  <dl>${rows.map(([k, v]) => html`<dt>${k}</dt><dd>${v}</dd>`)}</dl>
</div></section>

${sections.map((s) => html`<section class="sec" aria-labelledby="${s.id}"><div class="wrap">
  ${secHead({ title: s.title, cat: p.cat, id: s.id })}
  ${(s.intro || []).length ? html`<div class="prose-block">${s.intro.map((t) => html`<p>${t}</p>`)}</div>` : ''}
  ${sectionFigure(s)}
  ${(s.tables || []).map((tb) => (tb.kind === 'matrix' ? matrixTable(tb) : timingFigure(s, tb)))}
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
