import { html } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';

// The only page where licence terms are written out. Every other page links here.
export default function licence(ctx) {
  const l = ctx.licence, email = ctx.site.contactEmail;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Licence', href: '/licence/' }];
  const main = html`
${pageHead({ crumbs, h1: 'Licence', lede: l.ribbon })}
<section class="sec" aria-labelledby="h-terms"><div class="wrap">
  ${secHead({ title: 'The terms in brief', id: 'h-terms' })}
  <div class="prose-block">${l.paragraphs.map((t) => html`<p>${t}</p>`)}</div>
  <div class="actions"><a class="btn btn-primary" href="mailto:${email}">Ask for a commercial licence</a><a class="btn btn-ghost" href="/partner/">Partner with us</a></div>
  <p class="muted tool-note">${l.copyright}</p>
</div></section>`;
  return {
    url: '/licence/', type: 'licence', section: null,
    title: 'Licence', description: 'Every package is proprietary and ships compiled. Evaluation and non-commercial use are permitted; commercial use needs a licence, available on request.',
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}
