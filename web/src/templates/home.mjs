import { html, raw } from '../../scripts/lib/html.mjs';
import { secHead } from '../layout.mjs';
import { productCard, toolCard, researchCard, stat, resolveStats } from '../partials.mjs';
import { diagramFigure } from '../diagram.mjs';

// The home tiles are other pages' own tiles (a product's sizzle stats, or Phasor's), so a number is typed in one place only.
function tileFor(ctx, spec) {
  const p = ctx.byId[spec.product];
  const doc = p.doc, subst = ctx.limitFacts[p.id] || {};
  const all = resolveStats(ctx, doc.sizzle ? doc.sizzle.stats : doc.stats.map((x) => ({ ...x, cite: ['owner record'] })), subst);
  const t = all[spec.tile];
  if (!t) throw new Error(`home tile ${spec.product}[${spec.tile}] does not exist`);
  return stat(html`${spec.label || t.label} <a href="${p.href}">${p.name}</a>`, t.value);
}

export default function home(ctx) {
  const s = ctx.site, d = ctx.pages.home;
  const feat = ctx.evidence[0];
  const fig = diagramFigure({
    id: 'sm', kind: 'stack-map', title: '', lede: '', data: ctx.diagrams['stack-map'], variant: 'fig-hero',
    stageLabel: 'Interactive 3D view of how the products depend on each other. Drag to turn it, or use the arrow keys to move between products.',
    caption: raw('Every line is a project reference read from a package, so the picture changes only when the real dependencies do. Small circles are the browser tools each product powers.'),
  });
  const org = {
    '@context': 'https://schema.org', '@graph': [
      { '@type': 'Organization', '@id': s.url + '/#org', name: s.name, url: s.url + '/', sameAs: [s.navExternal.href], description: s.description },
      { '@type': 'WebSite', '@id': s.url + '/#site', url: s.url + '/', name: s.name, publisher: { '@id': s.url + '/#org' } },
    ],
  };
  const main = html`
<header class="page-head home-hero"><div class="wrap hero-grid">
  <div class="hero-copy">
  <p class="eyebrow">${d.eyebrow}</p>
  <h1>${s.pitch}</h1>
  <p class="lede">${s.lede}</p>
  <div class="actions"><a class="btn btn-primary" href="${d.cta.href}">${d.cta.label}</a><a class="btn btn-ghost" href="${d.ghost.href}">${d.ghost.label}</a></div>
  <ul class="facts"><li class="fact" data-gen="count"><b>${ctx.products.length}</b> products</li><li class="fact" data-gen="count"><b>${ctx.tools.length}</b> tools</li><li class="fact">on <b>NuGet</b></li></ul>
  </div>
  <div class="hero-fig">${fig.html}</div>
</div></header>

<section class="sec" id="numbers" aria-label="The numbers"><div class="wrap">
  <dl class="stats stats-big">${d.tiles.map((t) => tileFor(ctx, t))}</dl>
  <p class="more-link"><a href="${d.tilesLink.href}">${d.tilesLink.label}</a></p>
</div></section>

<section class="sec" aria-labelledby="h-tools"><div class="wrap">
  ${secHead({ title: d.toolsTitle, line: d.toolsLine, more: { href: '/tools/', label: 'All tools' }, id: 'h-tools' })}
  <ul class="cards">${ctx.tools.map((t) => toolCard(t, ctx.byId))}</ul>
</div></section>

<section class="sec" aria-labelledby="h-products"><div class="wrap">
  ${secHead({ title: d.productsTitle, line: d.productsLine, more: { href: '/products/', label: 'All products' }, id: 'h-products' })}
  <ul class="cards">${ctx.products.map(productCard)}</ul>
</div></section>

${feat ? html`<section class="sec" aria-labelledby="h-evidence"><div class="wrap">
  ${secHead({ title: d.evidenceTitle, line: d.evidenceLine, more: { href: '/evidence/', label: 'All evidence' }, id: 'h-evidence' })}
  <p class="prose-block">${feat.claim}</p>
  <p class="prose-block evidence-more"><a href="${feat.slug}">Read the method and every number</a></p>
</div></section>` : ''}

<section class="sec" aria-labelledby="h-research"><div class="wrap">
  ${secHead({ title: 'Research', line: 'What we have found about small models', more: { href: '/research/', label: 'All research' }, id: 'h-research' })}
  <ul class="cards">${ctx.research.slice(0, 3).map(researchCard)}</ul>
</div></section>

<section class="sec" aria-labelledby="h-partner"><div class="wrap">
  <div class="callout partner-band">
    <h2 id="h-partner">Build something on this stack</h2>
    <p>${ctx.pages.partner.lede}</p>
    <div class="actions"><a class="btn btn-ghost" href="/partner/">Partner with us</a><a class="btn btn-ghost" href="/about/">About</a><a class="btn btn-ghost" href="/licence/">Licence terms</a></div>
  </div>
</div></section>

<section class="sec" aria-label="For agents"><div class="wrap">
  <p class="muted for-ai">Reading as an agent? <a href="/ai/">A plain summary is here</a>, with <a href="/llms.txt">llms.txt</a> and <a href="/llms-full.txt">llms-full.txt</a>.</p>
</div></section>`;
  return {
    url: '/', type: 'home', section: null,
    fullTitle: `${s.name}: .NET libraries and in-browser tools`,
    description: s.description,
    jsonld: [org], scripts: ['/assets/diagrams/stack-map.js'], main, hashForward: ctx.redirects.homeHashes,
  };
}
