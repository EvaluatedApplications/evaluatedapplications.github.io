import { html } from '../../scripts/lib/html.mjs';
import { pageHead, crumbsLd } from '../layout.mjs';
import { toolCard } from '../partials.mjs';

export default function toolsIndex(ctx) {
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Tools', href: '/tools/' }];
  const mb = ctx.toolRuntimeMb;
  const d = ctx.pages.tools;
  const main = html`
<div class="sizzle-page">
${pageHead({ crumbs, eyebrow: d.eyebrow, h1: d.headline, wide: true, lede: d.lede, extra: html`<div class="actions"><a class="btn btn-primary" href="${d.cta.href}">${d.cta.label}</a></div>` })}
<section class="sec" aria-labelledby="h-all"><div class="wrap"><h2 class="sr-only" id="h-all">All tools</h2>
  <ul class="cards">${ctx.tools.map((t) => toolCard(t, ctx.byId))}</ul>
  ${mb ? html`<p class="tool-note">The first tool you start downloads a runtime of about ${mb} MB. Your browser keeps it for the next one.</p>` : ''}
  <p class="tool-note">Looking for something to play? <a href="/games/">The games</a> run the same way.</p>
</div></section>
</div>`;
  return {
    url: '/tools/', type: 'tools-index', section: 'tools',
    title: d.title, description: d.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main, owner: 'showroom',
  };
}
