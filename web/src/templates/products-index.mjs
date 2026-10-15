import { html } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';
import { productCard } from '../partials.mjs';

export default function productsIndex(ctx) {
  const d = ctx.pages.products;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Products', href: '/products/' }];
  const main = html`
<div class="sizzle-page">
${pageHead({ crumbs, eyebrow: d.eyebrow, h1: d.headline, wide: true, lede: d.lede, extra: html`<div class="actions"><a class="btn btn-primary" href="${d.cta.href}">${d.cta.label}</a></div>` })}
${ctx.groups.map((g) => html`<section class="sec" aria-labelledby="g-${g.id}"><div class="wrap">
  ${secHead({ title: g.label, line: g.line, id: 'g-' + g.id })}
  <ul class="cards">${g.products.map(productCard)}</ul>
</div></section>`)}
<section class="sec" aria-labelledby="h-licence-note"><div class="wrap">
  <div class="callout"><h2 id="h-licence-note">Licence</h2><p>${ctx.licence.ribbon} <a href="/licence/">Read the licence terms</a>.</p></div>
</div></section>
</div>`;
  return {
    url: '/products/', type: 'products-index', section: 'products',
    title: d.title, description: d.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}
