import { html } from '../../scripts/lib/html.mjs';
import { pageHead, crumbsLd } from '../layout.mjs';
import { gameCard } from '../partials.mjs';

export default function gamesIndex(ctx) {
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Games', href: '/games/' }];
  const d = ctx.pages.games;
  const main = html`
<div class="sizzle-page">
${pageHead({ crumbs, eyebrow: d.eyebrow, h1: d.headline, wide: true, lede: d.lede, extra: html`<div class="actions"><a class="btn btn-primary" href="${d.cta.href}">${d.cta.label}</a></div>` })}
<section class="sec" aria-labelledby="h-all"><div class="wrap"><h2 class="sr-only" id="h-all">All games</h2>
  <ul class="cards game-cards">${ctx.games.map((g) => gameCard(g, ctx.byId))}</ul>
</div></section>
</div>`;
  return {
    url: '/games/', type: 'games-index', section: 'games',
    title: d.title, description: d.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}
