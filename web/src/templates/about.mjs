import { html } from '../../scripts/lib/html.mjs';
import { inline } from '../inline.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';

// The About page describes the work only: no names, no people, no personal contact.
export default function about(ctx) {
  const d = ctx.pages.about;
  const crumbs = [{ label: 'Home', href: '/' }, { label: d.navLabel, href: '/about/' }];
  const main = html`
${pageHead({ crumbs, h1: d.headline, lede: inline(d.lede) })}
${d.sections.map((s) => html`<section class="sec" aria-labelledby="h-${s.id}"><div class="wrap">
  ${secHead({ title: s.title, id: 'h-' + s.id })}
  ${s.items ? html`<ul class="prose-block">${s.items.map((t) => html`<li>${inline(t)}</li>`)}</ul>` : ''}
  ${s.paragraphs ? html`<div class="prose-block">${s.paragraphs.map((t) => html`<p>${inline(t)}</p>`)}</div>` : ''}
</div></section>`)}`;
  return {
    url: '/about/', type: 'about', section: null,
    title: d.title, description: d.description, crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}
