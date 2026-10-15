import { html, raw } from '../../scripts/lib/html.mjs';
import { pageHead, crumbsLd } from '../layout.mjs';
import { researchCard } from '../partials.mjs';

export default function researchIndex(ctx) {
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Research', href: '/research/' }];
  const topics = [...new Set(ctx.research.flatMap((r) => r.topics))].sort();
  const main = html`
${pageHead({ crumbs, h1: 'Research', lede: 'Findings about small holographic models: how they use context, how they grow and how they compare. Each article leads with its finding and says what would refute it.' })}
<section class="sec" aria-labelledby="h-all"><div class="wrap"><h2 class="sr-only" id="h-all">All articles</h2>
  <div class="filter dia-ui" data-filter role="group" aria-label="Filter by topic" hidden><button type="button" class="chip" data-topic="" aria-pressed="true">All</button>${topics.map((t) => html`<button type="button" class="chip" data-topic="${t}" aria-pressed="false">${t}</button>`)}</div>
  <p class="muted filter-count" data-filter-count role="status" aria-live="polite" hidden></p>
  <ul class="cards research-cards">${ctx.research.map(researchCard)}</ul>
</div></section>`;
  return {
    url: '/research/', type: 'research-index', section: 'research',
    title: 'Research', description: 'Findings about small holographic language models: context length, growth, layer use and size, each written up as a separate article.',
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}