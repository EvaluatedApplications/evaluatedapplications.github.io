import fs from 'node:fs';
import path from 'node:path';
import { html, raw } from '../../scripts/lib/html.mjs';
import { DATA, readJson } from '../../scripts/lib/data.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';
import { installLine, codeBlock, evidenceTiles, stat, productSubnav, ladderTable, brickTable, codeSpans } from '../partials.mjs';
import { diagramFigure } from '../diagram.mjs';

const read = (rel) => fs.readFileSync(path.join(DATA, rel), 'utf8').replace(/\r\n/g, '\n').trimEnd();

// The Phasor overview. Same shell as every product page (head, subnav, evidence, related, licence), but it sells the result first:
// one promise, the live demo, the numbers as tiles, what you can build, and only then the mechanism, the coverage and the proof.
export default function phasorPage(ctx, p) {
  const doc = p.doc, s = ctx.site;
  const primary = p.packages.find((x) => x.role === 'primary');
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Products', href: '/products/' }, { label: p.name, href: p.slug }];
  const subst = ctx.limitFacts[p.id] || {};
  const fill = (t) => t.replace(/\{(\w+)\}/g, (_, k) => (k in subst ? subst[k] : `{${k}}`));
  const ladder = readJson(path.join(DATA, 'generated', 'phasor-ladder.json')).rungs;
  const scene = ctx.diagrams['superposition-scene'], cov = ctx.diagrams['coverage-meter'];

  const demo = diagramFigure({ id: 'dg', kind: 'superposition-scene', title: doc.demo.title, lede: doc.demo.lede, data: scene, stageLabel: doc.demo.stageLabel, level: 2, variant: 'fig-hero-demo' });
  const meter = diagramFigure({ id: 'cm', kind: 'coverage-meter', title: null, lede: null, data: cov, stageLabel: 'Each square is one operator, coloured by class.', level: 3, variant: 'fig-quiet',
    caption: html`From the coverage record for release <span data-gen="version">${primary.version}</span>. Every count is the record's.` });

  const w = doc.where;
  const ld = {
    '@context': 'https://schema.org', '@type': 'SoftwareApplication', name: primary.id, alternateName: p.name, description: doc.description,
    applicationCategory: 'DeveloperApplication', operatingSystem: '.NET', softwareVersion: primary.version, url: s.url + p.slug, downloadUrl: primary.nuget,
    license: s.url + '/licence/', author: { '@id': s.url + '/#org' },
  };
  const dependsOn = p.dependsOn.map((id) => ctx.byId[id]), usedBy = p.usedBy.map((id) => ctx.byId[id]);

  const main = html`<div class="phasor-page">
${pageHead({
    crumbs, eyebrow: 'Phasor for .NET', h1: doc.headline, wide: true, lede: doc.sub,
    extra: html`
<div class="hero-row"><div class="install-row hero-cta">${installLine(`dotnet add package ${primary.id}`)}</div>
<div class="actions"><a class="btn btn-primary" href="#demo">See it run</a><a class="btn btn-ghost" href="#code">Get the code</a><a class="btn btn-ghost" href="${primary.nuget}" rel="noopener">View on NuGet</a></div></div>
<p class="licence-ribbon"><span>${ctx.licence.ribbon}</span><a href="/licence/">Licence terms</a></p>`,
  })}
${productSubnav(p, 'overview')}

<section class="sec fig-sec doc-sec" id="demo" aria-label="Live demo"><div class="wrap">${demo.html}</div></section>

<section class="sec doc-sec" id="numbers" aria-label="The numbers"><div class="wrap">
  <dl class="stats stats-big">${doc.stats.map((x) => stat(fill(x.label), fill(x.value)))}</dl>
  <details class="dia-data numbers-more"><summary>${doc.numbers.summary}</summary>
    <ul class="numbers-list">${doc.numbers.items.map((n) => html`<li><h3>${fill(n.claim)}</h3><p>${fill(n.text)}</p></li>`)}</ul>
    <p class="more-link">${p.evidence.map((e, i) => html`${i ? ' · ' : ''}<a href="${e.slug}">${e.title}</a>`)}</p>
  </details>
  <p class="licence-ribbon"><span>Release <b data-gen="version">${primary.version}</b></span><span>${doc.facts.join(' \u00b7 ')}</span></p>
</div></section>

<section class="sec doc-sec" id="build" aria-labelledby="h-build"><div class="wrap">
  ${secHead({ title: doc.build.title, line: doc.build.line, cat: p.cat, id: 'h-build' })}
  <ul class="points build-points">${doc.build.items.map((c) => html`<li><h3>${c.title}</h3><p>${c.text}</p>${c.link ? html`<p class="more-link"><a href="${c.link.href}">${c.link.label}</a></p>` : ''}</li>`)}</ul>
</div></section>

<section class="sec doc-sec" id="code" aria-labelledby="h-code"><div class="wrap">
  ${secHead({ title: doc.code.title, line: doc.code.line, cat: p.cat, id: 'h-code' })}
  <p class="prose-block">${doc.code.lede}</p>
  ${codeBlock({ code: read(doc.code.source), label: 'C#' })}
  ${codeBlock({ code: read(doc.code.output), label: 'Output', out: true, lang: 'text' })}
</div></section>

<section class="sec doc-sec" id="superposition" aria-labelledby="h-where"><div class="wrap">
  ${secHead({ title: w.title, line: w.line, cat: p.cat, id: 'h-where' })}
  <p class="prose-block">${w.lede}</p>
  <div class="where-grid">
    <div class="where-col"><h3>${w.data.title}</h3><p class="muted">${w.data.text}</p>
      <dl class="term-list term-tight">${w.data.list.map((x) => html`<dt>${x.term}</dt><dd>${x.text}</dd>`)}</dl></div>
    <div class="where-col"><h3>${w.operators.title}</h3><p class="muted">${w.operators.text}</p>
      <h4>${w.operators.joinsTitle}</h4><ul class="limits">${scene.kernel.joins.map((j) => html`<li>${j}</li>`)}</ul>
      <h4>${w.operators.notTitle}</h4><ul class="limits">${scene.kernel.doesNotJoin.map((j) => html`<li>${j}</li>`)}</ul>
      <p class="muted">${scene.kernel.rule}</p></div>
    <div class="where-col"><h3>${w.exact.title}</h3><p class="muted">${w.exact.text}</p></div>
  </div>
  <details class="dia-data"><summary>${w.ladder.summary}</summary>
    <p class="prose-block">${w.ladder.lede}</p>
    ${ladderTable(ladder)}
    <p class="more-link"><a href="${w.ladder.more.href}">${w.ladder.more.label}</a></p>
  </details>
</div></section>

<section class="sec doc-sec quiet" id="coverage" aria-labelledby="h-meter"><div class="wrap">
  ${secHead({ title: doc.meter.title, line: doc.meter.line, cat: p.cat, id: 'h-meter' })}
  <p class="prose-block">${doc.meter.lede}</p>
  ${meter.html}
</div></section>

<section class="sec doc-sec quiet" id="construction" aria-labelledby="h-bricks"><div class="wrap">
  ${secHead({ title: fill(doc.bricks.title), line: doc.bricks.line, cat: p.cat, id: 'h-bricks' })}
  <p class="prose-block">${doc.bricks.opening}</p>
  ${brickTable(cov.construction)}
  <p class="prose-block">${doc.bricks.claim}</p>
</div></section>

<section class="sec doc-sec" id="uses" aria-labelledby="h-uses"><div class="wrap">
  ${secHead({ title: doc.uses.title, line: doc.uses.line, cat: p.cat, id: 'h-uses' })}
  <ul class="points uses-points">${doc.uses.items.map((u) => html`<li><h3>${u.title}</h3><p>${u.text}</p>${u.link ? html`<p class="more-link"><a href="${u.link.href}">${u.link.label}</a></p>` : ''}${u.code ? html`${codeBlock({ code: read(u.code.source), label: 'C#' })}${codeBlock({ code: read(u.code.output), label: 'Output', out: true, lang: 'text' })}` : ''}</li>`)}</ul>
</div></section>

<section class="sec doc-sec" id="fit" aria-labelledby="h-fit"><div class="wrap">
  ${secHead({ title: doc.wrongTool.title, cat: p.cat, id: 'h-fit' })}
  <ul class="limits">${doc.wrongTool.items.map((l) => html`<li>${l}</li>`)}</ul>
</div></section>

<section class="sec doc-sec" id="limits" aria-labelledby="h-lim"><div class="wrap">
  ${secHead({ title: doc.limits.title, cat: p.cat, id: 'h-lim' })}
  <ul class="limits">${doc.limits.items.map((l) => html`<li>${l}</li>`)}</ul>
  ${doc.reference ? html`<p class="muted tool-note">${doc.reference} <a href="${primary.nuget}" rel="noopener">Open the NuGet page</a>.</p>` : ''}
</div></section>

<section class="sec doc-sec" id="codec" aria-labelledby="h-codec"><div class="wrap">
  ${secHead({ title: doc.codec.title, line: doc.codec.line, cat: p.cat, id: 'h-codec', more: doc.codec.more })}
  ${doc.codec.paragraphs.map((t) => html`<p class="prose-block">${t}</p>`)}
  ${codeBlock({ code: read(doc.codec.source), label: 'C#' })}
  ${codeBlock({ code: read(doc.codec.output), label: 'Output', out: true, lang: 'text' })}
</div></section>

${p.evidence.length ? html`<section class="sec doc-sec" aria-labelledby="h-ev"><div class="wrap">
  ${secHead({ title: 'Evidence', cat: p.cat, id: 'h-ev', more: { href: p.evidence[0].slug, label: 'Method and every number' } })}
  <dl class="stats">${evidenceTiles(p.evidence[0]).map((t) => stat(t.label, t.value))}</dl>
</div></section>` : ''}

<section class="sec doc-sec" aria-labelledby="h-rel"><div class="wrap">
  ${secHead({ title: 'Related', cat: p.cat, id: 'h-rel' })}
  <div class="related">
    ${dependsOn.length ? html`<div><h3>Builds on</h3><ul>${dependsOn.map((d) => html`<li><a class="pill" href="${d.href}">${d.name}</a></li>`)}</ul></div>` : ''}
    ${usedBy.length ? html`<div><h3>Used by</h3><ul>${usedBy.map((d) => html`<li><a class="pill" href="${d.href}">${d.name}</a></li>`)}</ul></div>` : ''}
    ${p.tools.length ? html`<div><h3>Try it in the browser</h3><ul>${p.tools.map((t) => html`<li><a class="pill" href="${t.href}">${t.name}</a></li>`)}</ul></div>` : ''}
    ${(doc.research || []).length ? html`<div><h3>Research</h3><ul>${doc.research.map((x) => { const a = ctx.research.find((r) => r.id === x.id); if (!a) throw new Error('phasor research link: no article ' + x.id); return html`<li><a class="pill" href="${a.href}">${x.label}</a></li>`; })}</ul></div>` : ''}
    <div><h3>Elsewhere</h3><ul><li><a class="pill" href="/products/">All products</a></li><li><a class="pill" href="${primary.nuget}" rel="noopener">NuGet package</a></li></ul></div>
  </div>
</div></section>

<section class="sec doc-sec" aria-labelledby="h-lic"><div class="wrap">
  ${secHead({ title: 'Licence', cat: p.cat, id: 'h-lic' })}
  <p class="prose-block">${ctx.licence.oneLine} <a href="/licence/">Read the licence terms</a>.</p>
</div></section>
</div>`;

  return {
    url: p.slug, type: 'product', section: 'products', cat: p.cat,
    title: doc.title, description: doc.description,
    crumbs, jsonld: [ld, crumbsLd(ctx, crumbs)],
    scripts: ['/assets/diagrams/superposition-scene.js', '/assets/diagrams/coverage-meter.js'],
    main,
  };
}
