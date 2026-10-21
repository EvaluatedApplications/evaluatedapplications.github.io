import { html } from '../../scripts/lib/html.mjs';
import { inline } from '../inline.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';

// A quiet legal page: linked only from /ai/, kept out of the sitemap, llms*.txt and the nav (hidden + noindex).
export default function aiDeclaration(ctx) {
  const d = ctx.pages['ai-declaration'];
  const crumbs = [{ label: 'Home', href: '/' }, { label: ctx.pages.ai.navLabel, href: '/ai/' }, { label: 'Declaration', href: '/ai/declaration/' }];
  const main = html`
${pageHead({ crumbs, h1: d.headline })}
<section class="sec" aria-labelledby="h-declaration"><div class="wrap">
  ${secHead({ title: d.sectionTitle, id: 'h-declaration' })}
  <div class="prose-block">${d.paragraphs.map((t) => html`<p>${inline(t)}</p>`)}</div>
</div></section>`;
  return {
    url: '/ai/declaration/', type: 'ai', section: null, noindex: true, hidden: true,
    title: d.title, description: d.description, crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}
