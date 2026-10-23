import fs from 'node:fs';
import path from 'node:path';
import { html, raw } from '../../scripts/lib/html.mjs';
import { DATA } from '../../scripts/lib/data.mjs';
import { pageHead, secHead, breadcrumbs, crumbsLd } from '../layout.mjs';
import { installLine, codeBlock, evidenceTiles, stat, productSubnav, resolveStats, numbersBlock, toolBand, pointerBand } from '../partials.mjs';
import { diagramFigure } from '../diagram.mjs';

const read = (rel) => fs.readFileSync(path.join(DATA, rel), 'utf8').replace(/\r\n/g, '\n').trimEnd();

// A diagram can carry a small latency block copied from an evidence record, so the numbers it shows are the record's.
function withLatency(ctx, data, spec) {
  if (!spec) return data;
  if (spec.race) {
    const rd = ctx.diagrams['holodb-race'], pick = (labels) => labels.map((l) => { const q = rd.queries.find((x) => x.label === l); if (!q) throw new Error('latency row not found: ' + l); return { label: q.label, values: q.warm }; });
    return { ...data, latency: { columns: rd.arms.map((a) => a.label), title: spec.title, ledger: `${rd.rows.toLocaleString('en-GB')} invoices`, note: spec.note, focus: 0, key: pick(spec.key), aggregate: pick(spec.aggregate) } };
  }
  const tb = ctx.evidence.find((e) => e.id === spec.evidence).sections.flatMap((s) => s.tables).find((t) => t.id === spec.table);
  const pick = (labels) => labels.map((l) => { const r = tb.rows.find((x) => x.label === l); if (!r) throw new Error('latency row not found: ' + l); return { label: r.label, values: r.values }; });
  return { ...data, latency: { columns: tb.columns, qualifiers: tb.qualifiers, note: spec.note, focus: tb.focus, key: pick(spec.key), aggregate: pick(spec.aggregate) } };
}

// A diagram caption written in the product's data. {version} becomes the generated release, kept as a generated value.
function captionHtml(text, data, subst) {
  const [a, b = ''] = text.replace(/\{(\w+)\}/g, (m0, k) => (k === 'version' || !(k in subst) ? m0 : subst[k])).split('{version}');
  return html`${a}<span data-gen="version">${data.package.version}</span>${b}`;
}

// Product page template. A product supplies data/products.json (identity, packages, group) and
// data/products/<id>.json (words, diagram, quick start). Sections without data are omitted.
// With doc.sizzle the page sells the result first (design-language section 8): one promise, the live tool or demo under it, number tiles
// with the conditions behind a disclosure, what you can build, a proven program, and only then the mechanism, limits and proof.
export default function product(ctx, p) {
  const doc = p.doc;
  const sz = doc.sizzle || null;
  const s = ctx.site;
  const primary = p.packages.find((x) => x.role === 'primary');
  const group = ctx.groups.find((g) => g.id === p.group);
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Products', href: '/products/' }, { label: p.name, href: p.slug }];

  // numbers quoted in prose (corner cuts, cell counts) are filled in from the diagram data, never typed
  const subst = ctx.limitFacts[p.id] || {};
  const fill = (t) => t.replace(/\{(\w+)\}/g, (_, k) => (k in subst ? subst[k] : `{${k}}`));
  let fig = null;
  if (doc.diagram) {
    fig = diagramFigure({
      id: 'dg', kind: doc.diagram.id, title: doc.diagram.title, lede: fill(doc.diagram.lede),
      data: withLatency(ctx, ctx.diagrams[doc.diagram.id], doc.diagram.latency),
      stageLabel: doc.diagram.stageLabel || 'Interactive diagram. Drag, or use the arrow keys.',
      caption: doc.diagram.caption ? captionHtml(doc.diagram.caption, ctx.diagrams[doc.diagram.id], subst) : undefined,
      variant: sz ? 'fig-hero-demo' : undefined,
    });
  }
  const qs = doc.quickstart;
  const code = qs?.source ? read(qs.source) : null;
  const out = qs?.output ? read(qs.output) : null;

  const tryTool = doc.tryTool ? ctx.tools.find((t) => t.id === doc.tryTool.tool) : null;
  const heroTool = sz?.tool ? ctx.tools.find((t) => t.id === sz.tool) : null;
  if (sz?.tool && !heroTool) throw new Error(`${p.id}: sizzle.tool "${sz.tool}" is not a tool`);
  const dependsOn = p.dependsOn.map((id) => ctx.byId[id]);
  const usedBy = p.usedBy.map((id) => ctx.byId[id]);

  const ld = {
    '@context': 'https://schema.org', '@type': 'SoftwareApplication',
    name: primary.id, alternateName: p.name, description: doc.description,
    applicationCategory: 'DeveloperApplication', operatingSystem: '.NET',
    softwareVersion: primary.version, url: s.url + p.slug, downloadUrl: primary.nuget,
    license: s.url + '/licence/',
    author: { '@id': s.url + '/#org' },
  };

  const plainHero = pageHead({
    crumbs, eyebrow: group.label, h1: `${p.name}: ${p.tagline}`, wide: true, lede: doc.headline,
    extra: html`
<ul class="facts"><li class="fact">Release <b data-gen="version">${primary.version}</b></li>${(doc.facts || []).map((f) => html`<li class="fact">${f}</li>`)}</ul>
<div class="install-row">${p.packages.map((pkg) => html`<div>${p.packages.length > 1 ? html`<div class="pkg-label">${pkg.id}</div>` : ''}${installLine(`dotnet add package ${pkg.id}`)}</div>`)}</div>
<div class="actions">${tryTool ? html`<a class="btn btn-primary" href="${tryTool.href}">${doc.tryTool.label}</a>` : ''}${qs ? html`<a class="btn ${tryTool ? 'btn-ghost' : 'btn-primary'}" href="#quick-start">Quick start</a>` : ''}<a class="btn btn-ghost" href="${primary.nuget}" rel="noopener">View on NuGet</a></div>
<p class="licence-ribbon"><span>${ctx.licence.ribbon}</span><a href="/licence/">Licence terms</a></p>`,
  });

  const sizzleHero = sz ? pageHead({
    crumbs, eyebrow: sz.eyebrow || `${p.name}, ${p.tagline}`, h1: sz.headline, wide: true, lede: sz.sub,
    extra: html`
<div class="hero-row"><div class="install-row hero-cta">${installLine(`dotnet add package ${primary.id}`)}</div>
<div class="actions"><a class="btn btn-primary" href="#demo">${sz.cta || 'See it run'}</a>${qs ? html`<a class="btn btn-ghost" href="#quick-start">Get the code</a>` : ''}<a class="btn btn-ghost" href="${primary.nuget}" rel="noopener">View on NuGet</a></div></div>
<p class="licence-ribbon"><span>${ctx.licence.ribbon}</span><a href="/licence/">Licence terms</a></p>`,
  }) : '';

  const figSec = fig ? html`<section class="sec fig-sec doc-sec" id="${sz && !heroTool && !sz.pointer ? 'demo' : 'overview'}" aria-label="Overview"><div class="wrap">${fig.html}</div></section>` : '';
  const bandSec = heroTool ? toolBand(heroTool, p, p.tools.filter((t) => t.id !== heroTool.id)) : sz?.pointer ? pointerBand(sz.pointer, subst) : '';

  const stats = sz ? resolveStats(ctx, sz.stats, subst) : [];
  const numbersSec = sz ? numbersBlock({
    stats, numbers: sz.numbers, subst, links: p.evidence,
    ribbon: html`<p class="licence-ribbon"><span>Release <b data-gen="version">${primary.version}</b></span><span>${(doc.facts || []).join(' · ')}</span></p>`,
  }) : '';

  // further diagrams in the page's own order (the explainer, the decision helper), each with its trade-off strip when it has one
  const figSecs = (sz?.figures || []).map((f) => {
    const g = diagramFigure({ id: f.id, kind: f.kind, title: '', lede: '', level: 3, data: ctx.diagrams[f.kind], stageLabel: f.stageLabel, caption: html`${f.caption}` });
    const to = f.tradeoffs ? ctx.diagrams[f.kind].tradeoffs : null;
    return html`<section class="sec doc-sec" id="${f.secId}" aria-labelledby="h-${f.secId}"><div class="wrap">
  ${secHead({ title: f.title, line: f.line, cat: p.cat, id: 'h-' + f.secId })}
  ${g.html}
  ${to ? html`<h3 class="sub-h" id="trade-offs">${f.tradeoffsTitle || 'Trade-offs'}</h3><ul class="tradeoffs">${to.map((x) => html`<li><h4>${x.title}</h4><div class="to-pair"><div class="to-l"><b>${x.left.label}</b><p>${x.left.text}</p></div><div class="to-r"><b>${x.right.label}</b><p>${x.right.text}</p></div></div></li>`)}</ul>` : ''}
</div></section>`;
  });

  const buildSec = sz?.build ? html`<section class="sec doc-sec" id="build" aria-labelledby="h-build"><div class="wrap">
  ${secHead({ title: sz.build.title || 'What you can build', line: sz.build.line, cat: p.cat, id: 'h-build' })}
  <ul class="points build-points">${sz.build.items.map((c) => html`<li><h3>${c.title}</h3><p>${fill(c.text)}</p>${c.link ? html`<p class="more-link"><a href="${c.link.href}">${c.link.label}</a></p>` : ''}</li>`)}</ul>
</div></section>` : '';

  // Results that went against the product, stated in full right under the numbers (sizzle pages only): the losses are part of the pitch.
  const lossSec = sz && doc.losses ? html`<section class="sec doc-sec" id="losses" aria-labelledby="h-losses"><div class="wrap">
  ${secHead({ title: doc.losses.title, line: doc.losses.line, cat: p.cat, id: 'h-losses' })}
  <ul class="points">${doc.losses.items.map((w) => html`<li><h3>${w.title}</h3><p>${fill(w.text)}</p></li>`)}</ul>
</div></section>` : '';
  const srcSec = doc.sources ? html`<section class="sec doc-sec" id="sources" aria-labelledby="h-src"><div class="wrap">
  ${secHead({ title: doc.sources.title, cat: p.cat, id: 'h-src' })}
  <ul class="limits">${doc.sources.items.map((l) => html`<li>${fill(l)}</li>`)}</ul>
</div></section>` : '';

  const whatSec = html`<section class="sec doc-sec" aria-labelledby="h-what"><div class="wrap">
  ${secHead({ title: sz ? (sz.mechanismTitle || `How ${p.name} works`) : `What ${p.name} is`, cat: p.cat, id: 'h-what' })}
  ${[].concat(doc.intro).map((t) => html`<p class="prose-block">${fill(t)}</p>`)}
  ${doc.why ? html`<ul class="points">${doc.why.map((w) => html`<li><h3>${w.title}</h3><p>${fill(w.text)}</p></li>`)}</ul>` : ''}
</div></section>`;

  const extraInstalls = sz && p.packages.length > 1 ? html`<div class="install-row">${p.packages.map((pkg) => html`<div><div class="pkg-label">${pkg.id}</div>${installLine(`dotnet add package ${pkg.id}`)}</div>`)}</div>` : '';
  const qsSec = qs ? html`<section class="sec doc-sec" id="quick-start" aria-labelledby="h-qs"><div class="wrap">
  ${secHead({ title: sz ? (sz.codeTitle || 'Run it yourself') : 'Quick start', line: qs.lede, cat: p.cat, id: 'h-qs' })}
  ${extraInstalls}
  ${code ? codeBlock({ code, label: 'C#' }) : ''}
  ${out ? codeBlock({ code: out, label: 'Output', out: true, lang: 'text' }) : ''}
  ${(qs.blocks || []).map((b) => html`${b.lede ? html`<p class="prose-block">${fill(b.lede)}</p>` : ''}${codeBlock({ code: read(b.source), label: b.label || 'C#', lang: b.lang || 'csharp', out: b.out })}`)}
  <p class="muted tool-note">${qs.note || 'This is the complete program, compiled and run against the published package.'}</p>
  ${(qs.also || []).map((a) => html`${a.lede ? html`<p class="prose-block">${a.lede}</p>` : ''}${codeBlock({ code: read(a.source), label: 'C#' })}${a.output ? codeBlock({ code: read(a.output), label: 'Output', out: true, lang: 'text' }) : ''}`)}
</div></section>` : '';

  const capSec = doc.capabilities ? html`<section class="sec doc-sec" aria-labelledby="h-cap"><div class="wrap">
  ${secHead({ title: sz ? 'Everything in the package' : 'Capabilities', cat: p.cat, id: 'h-cap' })}
  <ul class="points">${doc.capabilities.map((c) => html`<li><h3>${c.title}</h3><p>${c.text}</p></li>`)}</ul>
</div></section>` : '';

  const extSecs = (doc.extensions || []).map((x) => {
    const pkg = p.packages.find((q) => q.id === x.packageId);
    return html`<section class="sec doc-sec" id="${x.id}" aria-labelledby="h-${x.id}"><div class="wrap">
  ${secHead({ title: x.title, line: x.line, cat: p.cat, id: 'h-' + x.id })}
  ${pkg ? html`<ul class="facts"><li class="fact">${pkg.id}</li><li class="fact">Release <b data-gen="version">${pkg.version}</b></li></ul>` : ''}
  ${(x.paragraphs || []).map((t) => html`<p class="prose-block">${t}</p>`)}
  ${x.points ? html`<ul class="points">${x.points.map((w) => html`<li><h3>${w.title}</h3><p>${w.text}</p></li>`)}</ul>` : ''}
  ${x.snippet ? html`${x.snippet.lede ? html`<p class="prose-block">${x.snippet.lede}</p>` : ''}${codeBlock({ code: read(x.snippet.source), label: 'C#' })}${x.snippet.output ? codeBlock({ code: read(x.snippet.output), label: 'Output', out: true, lang: 'text' }) : ''}` : ''}
  ${x.good ? html`<h3 class="sub-h">Good to know</h3><ul class="limits">${x.good.map((l) => html`<li>${l}</li>`)}</ul>` : ''}
</div></section>`;
  });

  // What exists today against what does not: each item carries a status badge, so an unbuilt piece is never read as present.
  const todaySec = doc.today ? html`<section class="sec doc-sec" id="today" aria-labelledby="h-today"><div class="wrap">
  ${secHead({ title: doc.today.title, line: doc.today.line, cat: p.cat, id: 'h-today' })}
  <ul class="points">${doc.today.items.map((w) => html`<li><h3>${w.title} <span class="badge ${w.built ? 'badge-ok' : 'badge-warn'}">${w.status || (w.built ? 'Built' : 'Not built')}</span></h3><p>${fill(w.text)}</p></li>`)}</ul>
</div></section>` : '';
  const otherSec = doc.otherTools ? html`<section class="sec doc-sec" id="fit" aria-labelledby="h-fit"><div class="wrap">
  ${secHead({ title: doc.otherTools.title, cat: p.cat, id: 'h-fit' })}
  <ul class="limits">${doc.otherTools.items.map((l) => html`<li>${fill(l)}</li>`)}</ul>
</div></section>` : '';

  // the limits table of the decision helper leads the list, so the page and the evidence record say the same thing
  const limitItems = [...(doc.limitsFrom ? ctx.diagrams[doc.limitsFrom].limits.map(([k, v]) => `${k}: ${v}`) : []), ...(doc.limits || [])];
  const limSec = limitItems.length ? html`<section class="sec doc-sec" aria-labelledby="h-lim"><div class="wrap">
  ${secHead({ title: sz ? 'Know before you start' : 'Limits', line: `What ${p.name} does not do, and where to be careful`, cat: p.cat, id: 'h-lim' })}
  <ul class="limits">${limitItems.map((l) => html`<li>${fill(l)}</li>`)}</ul>
  ${doc.reference ? html`<p class="muted tool-note">${doc.reference} <a href="${primary.nuget}" rel="noopener">Open the NuGet page</a>.</p>` : ''}
</div></section>` : '';

  const evSec = p.evidence.length ? html`<section class="sec doc-sec" aria-labelledby="h-ev"><div class="wrap">
  ${secHead({ title: 'Evidence', cat: p.cat, id: 'h-ev', more: { href: p.evidence[0].slug, label: 'Method and every number' } })}
  ${sz ? html`<p class="prose-block">${p.evidence[0].claim}</p>` : html`<dl class="stats">${evidenceTiles(p.evidence[0]).map((t) => stat(t.label, t.value))}</dl>`}
</div></section>` : '';

  const relSec = html`<section class="sec doc-sec" aria-labelledby="h-rel"><div class="wrap">
  ${secHead({ title: 'Related', cat: p.cat, id: 'h-rel' })}
  <div class="related">
    ${dependsOn.length ? html`<div><h3>Builds on</h3><ul>${dependsOn.map((d) => html`<li><a class="pill" href="${d.href}">${d.name}</a></li>`)}</ul></div>` : ''}
    ${usedBy.length ? html`<div><h3>Used by</h3><ul>${usedBy.map((d) => html`<li><a class="pill" href="${d.href}">${d.name}</a></li>`)}</ul></div>` : ''}
    ${p.tools.length ? html`<div><h3>Try it in the browser</h3><ul>${p.tools.map((t) => html`<li><a class="pill" href="${t.href}">${t.name}</a></li>`)}</ul></div>` : ''}
    ${doc.related ? html`<div><h3>${doc.related.title}</h3><ul>${doc.related.links.map((l) => html`<li><a class="pill" href="${l.href}">${l.label}</a></li>`)}</ul></div>` : ''}
    <div><h3>Elsewhere</h3><ul><li><a class="pill" href="/products/">All products</a></li><li><a class="pill" href="${primary.nuget}" rel="noopener">NuGet package</a></li></ul></div>
  </div>
</div></section>`;

  const licSec = html`<section class="sec doc-sec" aria-labelledby="h-lic"><div class="wrap">
  ${secHead({ title: 'Licence', cat: p.cat, id: 'h-lic' })}
  <p class="prose-block">${ctx.licence.oneLine} <a href="/licence/">Read the licence terms</a>.</p>
</div></section>`;

  const main = sz
    ? html`<div class="sizzle-page">
${sizzleHero}
${productSubnav(p, 'overview')}
${bandSec}
${figSec}
${numbersSec}
${figSecs}
${lossSec}
${buildSec}
${qsSec}
${whatSec}
${capSec}
${extSecs}
${todaySec}
${otherSec}
${limSec}
${srcSec}
${evSec}
${relSec}
${licSec}
</div>`
    : html`
${plainHero}
${productSubnav(p, 'overview')}
${fig ? html`<section class="sec fig-sec doc-sec" id="overview" aria-label="Overview"><div class="wrap">${fig.html}</div></section>` : ''}
${whatSec}
${qsSec}
${capSec}
${extSecs}
${limSec}
${evSec}
${relSec}
${licSec}`;

  return {
    url: p.slug, type: 'product', section: 'products', cat: p.cat,
    title: doc.title, description: doc.description,
    crumbs, jsonld: [ld, crumbsLd(ctx, crumbs)],
    scripts: [doc.diagram?.id, ...(sz?.figures || []).map((f) => f.kind)].filter(Boolean).map((k) => '/assets/diagrams/' + k + '.js'),
    main,
  };
}
