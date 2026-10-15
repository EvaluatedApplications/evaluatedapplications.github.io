import { html } from '../../scripts/lib/html.mjs';
import { pageHead, crumbsLd } from '../layout.mjs';
import { toolCard } from '../partials.mjs';

export default function toolsIndex(ctx) {
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Tools', href: '/tools/' }];
  const mb = ctx.toolRuntimeMb;
  const main = html`
${pageHead({ crumbs, h1: 'Tools', lede: 'Our libraries running live in your browser. Nothing is uploaded: the work happens on your machine.' })}
<section class="sec" aria-labelledby="h-all"><div class="wrap"><h2 class="sr-only" id="h-all">All tools</h2>
  <ul class="cards">${ctx.tools.map((t) => toolCard(t, ctx.byId))}</ul>
  ${mb ? html`<p class="tool-note">The first tool you start downloads a runtime of about ${mb} MB. Your browser keeps it for the next one.</p>` : ''}
</div></section>`;
  return {
    url: '/tools/', type: 'tools-index', section: 'tools',
    title: 'Tools', description: 'Interactive tools that run our .NET libraries live in your browser: a data profiler, a small language model, a forecaster and more. Nothing is uploaded.',
    crumbs, jsonld: [crumbsLd(ctx, crumbs)], main, owner: 'showroom',
  };
}
