import { html, raw } from '../../scripts/lib/html.mjs';
import { pageHead, crumbsLd } from '../layout.mjs';
import { researchCard } from '../partials.mjs';

export default function researchIndex(ctx) {
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Research', href: '/research/' }];
  const d = ctx.pages.research, first = ctx.research.find((r) => r.status === 'migrated');
  const topics = [...new Set(ctx.research.flatMap((r) => r.topics))].sort();
  const main = html`
<div class="sizzle-page">
${pageHead({ crumbs, eyebrow: d.eyebrow, h1: d.headline, wide: true, lede: d.lede, extra: first ? html`<div class="actions"><a class="btn btn-primary" href="${first.href}">${d.ctaLead} ${first.title}</a></div>` : '' })}
<section class="sec" aria-labelledby="h-all"><div class="wrap"><h2 class="sr-only" id="h-all">All articles</h2>
  <div class="filter dia-ui" data-filter role="group" aria-label="Filter by topic" hidden><button type="button" class="chip" data-topic="" aria-pressed="true">All</button>${topics.map((t) => html`<button type="button" class="chip" data-topic="${t}" aria-pressed="false">${t}</button>`)}</div>
  <p class="muted filter-count" data-filter-count role="status" aria-live="polite" hidden></p>
  <ul class="cards research-cards">${ctx.research.map(researchCard)}</ul>
</div></section>
</div>`;
  return {
    url: '/research/', type: 'research-index', section: 'research',
    title: d.title, description: d.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}