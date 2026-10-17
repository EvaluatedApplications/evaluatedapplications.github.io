import { html } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';

// A demo page: a standalone Blazier shell (its own folder, copied to a path under the page by the workflow) embedded right under the
// headline, then the words around it. The words come from data/pages/umbrarium.json, authored by the package owner and reviewed by editorial.
export default function demo(ctx) {
  const d = ctx.pages.umbrarium, s = d.demo;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Tools', href: '/tools/' }, { label: 'Umbrarium', href: '/umbrarium/' }];
  const main = html`
<div class="sizzle-page">
${pageHead({ crumbs, eyebrow: d.eyebrow, h1: d.headline, wide: true })}
<section class="sec" aria-labelledby="h-demo"><div class="wrap">
  <h2 class="sr-only" id="h-demo">The demo</h2>
  <iframe class="demo-frame" src="${s.src}" title="${s.frameTitle}"></iframe>
  <p class="tool-note"><a href="${s.src}">${s.openLabel}</a></p>
</div></section>
<section class="sec" aria-labelledby="h-look"><div class="wrap">
  ${secHead({ title: 'About the demo', id: 'h-look' })}
  <p class="prose-block">${d.lede}</p>
  <details class="dia-data numbers-more"><summary>${d.looking.summary}</summary>
    <ul class="demo-list">${d.looking.items.map((t) => html`<li>${t}</li>`)}</ul>
    <p class="tool-note">${d.looking.reference}</p>
  </details>
</div></section>
<section class="sec" aria-labelledby="h-adds"><div class="wrap">
  ${secHead({ title: d.adds.title, id: 'h-adds' })}
  <div class="prose-block">${d.adds.paragraphs.map((t) => html`<p>${t}</p>`)}</div>
  <p class="prose-block tool-note">${d.licence} <a href="/licence/">Licence terms</a>.</p>
</div></section>
</div>`;
  const url = '/umbrarium/';
  const page = { '@context': 'https://schema.org', '@type': 'WebPage', name: d.title, description: d.description, url: ctx.site.url + url, isPartOf: { '@type': 'WebSite', name: ctx.site.name, url: ctx.site.url } };
  return {
    url, type: 'demo', section: 'tools',
    title: d.title, description: d.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs), page], main,
  };
}
