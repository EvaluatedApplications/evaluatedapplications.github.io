import fs from 'node:fs';
import path from 'node:path';
import { html, raw } from '../../scripts/lib/html.mjs';
import { DATA } from '../../scripts/lib/data.mjs';
import { pageHead, secHead, breadcrumbs, crumbsLd } from '../layout.mjs';
import { installLine, codeBlock, evidenceTiles, stat, productSubnav } from '../partials.mjs';
import { diagramFigure } from '../diagram.mjs';

const read = (rel) => fs.readFileSync(path.join(DATA, rel), 'utf8').replace(/\r\n/g, '\n').trimEnd();

// A diagram can carry a small latency block copied from an evidence record, so the numbers it shows are the record's.
function withLatency(ctx, data, spec) {
  if (!spec) return data;
  const tb = ctx.evidence.find((e) => e.id === spec.evidence).sections.flatMap((s) => s.tables).find((t) => t.id === spec.table);
  const pick = (labels) => labels.map((l) => { const r = tb.rows.find((x) => x.label === l); if (!r) throw new Error('latency row not found: ' + l); return { label: r.label, values: r.values }; });
  return { ...data, latency: { columns: tb.columns, focus: tb.focus, key: pick(spec.key), aggregate: pick(spec.aggregate) } };
}

// A diagram caption written in the product's data. {version} becomes the generated release, kept as a generated value.
function captionHtml(text, data, subst) {
  const [a, b = ''] = text.replace(/\{(\w+)\}/g, (m0, k) => (k === 'version' || !(k in subst) ? m0 : subst[k])).split('{version}');
  return html`${a}<span data-gen="version">${data.package.version}</span>${b}`;
}

// Product page template. A product supplies data/products.json (identity, packages, group) and
// data/products/<id>.json (words, diagram, quick start). Sections without data are omitted.
export default function product(ctx, p) {
  const doc = p.doc;
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
    });
  }
  const qs = doc.quickstart;
  const code = qs ? read(qs.source) : null;
  const out = qs ? read(qs.output) : null;


  const tryTool = doc.tryTool ? ctx.tools.find((t) => t.id === doc.tryTool.tool) : null;
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

  const main = html`
${pageHead({
    crumbs, eyebrow: group.label, h1: `${p.name}: ${p.tagline}`, wide: true, lede: doc.headline,
    extra: html`
<ul class="facts"><li class="fact">Release <b data-gen="version">${primary.version}</b></li>${(doc.facts || []).map((f) => html`<li class="fact">${f}</li>`)}</ul>
<div class="install-row">${p.packages.map((pkg) => html`<div>${p.packages.length > 1 ? html`<div class="pkg-label">${pkg.id}</div>` : ''}${installLine(`dotnet add package ${pkg.id}`)}</div>`)}</div>
<div class="actions">${tryTool ? html`<a class="btn btn-primary" href="${tryTool.href}">${doc.tryTool.label}</a>` : ''}${qs ? html`<a class="btn ${tryTool ? 'btn-ghost' : 'btn-primary'}" href="#quick-start">Quick start</a>` : ''}<a class="btn btn-ghost" href="${primary.nuget}" rel="noopener">View on NuGet</a></div>
<p class="licence-ribbon"><span>${ctx.licence.ribbon}</span><a href="/licence/">Licence terms</a></p>`,
  })}
${productSubnav(p, 'overview')}

${fig ? html`<section class="sec fig-sec doc-sec" id="overview" aria-label="Overview"><div class="wrap">${fig.html}</div></section>` : ''}

<section class="sec doc-sec" aria-labelledby="h-what"><div class="wrap">
  ${secHead({ title: `What ${p.name} is`, cat: p.cat, id: 'h-what' })}
  ${[].concat(doc.intro).map((t) => html`<p class="prose-block">${t}</p>`)}
  ${doc.why ? html`<ul class="points">${doc.why.map((w) => html`<li><h3>${w.title}</h3><p>${w.text}</p></li>`)}</ul>` : ''}
</div></section>

${qs ? html`<section class="sec doc-sec" id="quick-start" aria-labelledby="h-qs"><div class="wrap">
  ${secHead({ title: 'Quick start', line: qs.lede, cat: p.cat, id: 'h-qs' })}
  ${codeBlock({ code, label: 'C#' })}
  ${codeBlock({ code: out, label: 'Output', out: true, lang: 'text' })}
  <p class="muted tool-note">This is the complete program, compiled and run against the published package.</p>
  ${(qs.also || []).map((a) => html`${a.lede ? html`<p class="prose-block">${a.lede}</p>` : ''}${codeBlock({ code: read(a.source), label: 'C#' })}${a.output ? codeBlock({ code: read(a.output), label: 'Output', out: true, lang: 'text' }) : ''}`)}
</div></section>` : ''}

${doc.capabilities ? html`<section class="sec doc-sec" aria-labelledby="h-cap"><div class="wrap">
  ${secHead({ title: 'Capabilities', cat: p.cat, id: 'h-cap' })}
  <ul class="points">${doc.capabilities.map((c) => html`<li><h3>${c.title}</h3><p>${c.text}</p></li>`)}</ul>
</div></section>` : ''}

${(doc.extensions || []).map((x) => {
    const pkg = p.packages.find((q) => q.id === x.packageId);
    return html`<section class="sec doc-sec" id="${x.id}" aria-labelledby="h-${x.id}"><div class="wrap">
  ${secHead({ title: x.title, line: x.line, cat: p.cat, id: 'h-' + x.id })}
  ${pkg ? html`<ul class="facts"><li class="fact">${pkg.id}</li><li class="fact">Release <b data-gen="version">${pkg.version}</b></li></ul>` : ''}
  ${(x.paragraphs || []).map((t) => html`<p class="prose-block">${t}</p>`)}
  ${x.points ? html`<ul class="points">${x.points.map((w) => html`<li><h3>${w.title}</h3><p>${w.text}</p></li>`)}</ul>` : ''}
  ${x.snippet ? html`${x.snippet.lede ? html`<p class="prose-block">${x.snippet.lede}</p>` : ''}${codeBlock({ code: read(x.snippet.source), label: 'C#' })}${x.snippet.output ? codeBlock({ code: read(x.snippet.output), label: 'Output', out: true, lang: 'text' }) : ''}` : ''}
  ${x.good ? html`<h3 class="sub-h">Good to know</h3><ul class="limits">${x.good.map((l) => html`<li>${l}</li>`)}</ul>` : ''}
</div></section>`;
  })}

${doc.limits ? html`<section class="sec doc-sec" aria-labelledby="h-lim"><div class="wrap">
  ${secHead({ title: 'Limits', line: `What ${p.name} does not do, and where to be careful`, cat: p.cat, id: 'h-lim' })}
  <ul class="limits">${doc.limits.map((l) => html`<li>${fill(l)}</li>`)}</ul>
  ${doc.reference ? html`<p class="muted tool-note">${doc.reference} <a href="${primary.nuget}" rel="noopener">Open the NuGet page</a>.</p>` : ''}
</div></section>` : ''}

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
    <div><h3>Elsewhere</h3><ul><li><a class="pill" href="/products/">All products</a></li><li><a class="pill" href="${primary.nuget}" rel="noopener">NuGet package</a></li></ul></div>
  </div>
</div></section>

<section class="sec doc-sec" aria-labelledby="h-lic"><div class="wrap">
  ${secHead({ title: 'Licence', cat: p.cat, id: 'h-lic' })}
  <p class="prose-block">${ctx.licence.oneLine} <a href="/licence/">Read the licence terms</a>.</p>
</div></section>`;

  return {
    url: p.slug, type: 'product', section: 'products', cat: p.cat,
    title: doc.title, description: doc.description,
    crumbs, jsonld: [ld, crumbsLd(ctx, crumbs)],
    scripts: doc.diagram ? ['/assets/diagrams/' + doc.diagram.id + '.js'] : [],
    main,
  };
}
