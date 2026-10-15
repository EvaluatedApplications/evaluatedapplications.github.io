import { html } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';

export default function partner(ctx) {
  const d = ctx.pages.partner, email = ctx.site.contactEmail;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Partner with us', href: '/partner/' }];
  const main = html`
${pageHead({ crumbs, h1: d.headline, wide: true, lede: d.lede })}
<section class="sec" aria-labelledby="h-offer"><div class="wrap">
  ${secHead({ title: 'The offer', id: 'h-offer' })}
  <ul class="points">${d.offers.map((o) => html`<li><h3>${o.title}</h3><p>${o.text}</p></li>`)}</ul>
</div></section>
<section class="sec" aria-labelledby="h-contact"><div class="wrap">
  ${secHead({ title: 'Get in touch', id: 'h-contact' })}
  <p class="contact"><a href="mailto:${email}">${email}</a></p>
  <div class="actions"><a class="btn btn-primary" href="mailto:${email}">Email us</a><a class="btn btn-ghost" href="/products/">See what is on offer</a></div>
  ${d.closing.map((c) => html`<p class="prose-block tool-note">${c}</p>`)}
</div></section>`;
  return {
    url: '/partner/', type: 'partner', section: 'partner',
    title: d.title, description: d.description, crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}
