import { html, raw } from '../../scripts/lib/html.mjs';
import { breadcrumbs, crumbsLd, secHead } from '../layout.mjs';

// A demo page: the demo is an app, so it runs full-bleed. A slim intro strip (breadcrumb, H1, one-line promise and two expandable
// disclosures), then a standalone Blazier shell (its own folder, copied to a path under the page by the workflow) in an iframe that
// fills the viewport below the site header, then the editorial text below the fold. Words: data/pages/umbrarium.json (package owner,
// reviewed by editorial). Styles: 57-demo-embed.css.
export default function demo(ctx) {
  const d = ctx.pages.umbrarium, s = d.demo, m = d.measured;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Tools', href: '/tools/' }, { label: 'Umbrarium', href: '/umbrarium/' }];
  const measured = html`<div class="tbl-wrap"><table class="tbl"><caption>${m.caption}</caption>
    <thead><tr>${m.head.map((h, i) => html`<th scope="col"${i > 1 ? raw(' class="num"') : ''}>${h}</th>`)}</tr></thead>
    <tbody>${m.rows.map((r) => html`<tr>${r.map((c, i) => (i === 0 ? html`<th scope="row">${c}</th>` : html`<td${i > 1 ? raw(' class="num"') : ''}>${c}</td>`))}</tr>`)}</tbody></table></div>`;
  const main = html`
<div class="demo-page">
<section class="demo-strip" aria-labelledby="h-demo-title">
  ${breadcrumbs(crumbs)}
  <h1 id="h-demo-title">${d.headline}</h1>
  <div class="demo-strip-row">
    <p class="lede">${d.lede}</p>
    <div class="demo-more">
      <details class="demo-disc"><summary>${d.looking.summary}</summary>
        <div class="demo-panel"><ul class="demo-list">${d.looking.items.map((t) => html`<li>${t}</li>`)}</ul></div>
      </details>
      <details class="demo-disc"><summary>${m.summary}</summary>
        <div class="demo-panel"><p class="tool-note">${m.intro}</p>${measured}${m.paragraphs.map((t) => html`<p class="prose-block">${t}</p>`)}</div>
      </details>
    </div>
  </div>
</section>
<div class="demo-stage">
  <iframe class="demo-frame" src="${s.src}" title="${s.frameTitle}" allow="clipboard-write" allowfullscreen></iframe>
</div>
<p class="demo-open tool-note"><a href="${s.openHref}">${s.openLabel}</a></p>
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
