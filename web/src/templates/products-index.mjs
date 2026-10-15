import { html } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';
import { productCard } from '../partials.mjs';

export default function productsIndex(ctx) {
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Products', href: '/products/' }];
  const main = html`
${pageHead({ crumbs, h1: 'Products', lede: 'High-performance .NET libraries. Each one is a NuGet package, and most build on the same two foundations.' })}
${ctx.groups.map((g) => html`<section class="sec" aria-labelledby="g-${g.id}"><div class="wrap">
  ${secHead({ title: g.label, line: g.line, id: 'g-' + g.id })}
  <ul class="cards">${g.products.map(productCard)}</ul>
</div></section>`)}
<section class="sec" aria-labelledby="h-licence-note"><div class="wrap">
  <div class="callout"><h2 id="h-licence-note">Licence</h2><p>${ctx.licence.ribbon} <a href="/licence/">Read the licence terms</a>.</p></div>
</div></section>`;
  return {
    url: '/products/', type: 'products-index', section: 'products',
    title: 'Products', description: 'Every Evaluated Applications library: a SQL database, a transformer engine, pathfinding, a pipeline runtime and more, each one a NuGet package.',
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}
