import { html } from '../../scripts/lib/html.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';

export default function ai(ctx) {
  const d = ctx.pages.ai, email = ctx.site.contactEmail;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'For AI readers', href: '/ai/' }];
  const main = html`
${pageHead({ crumbs, h1: d.headline, wide: true, lede: d.lede })}
<section class="sec" aria-labelledby="h-sum"><div class="wrap">
  ${secHead({ title: 'Summary', id: 'h-sum' })}
  <div class="prose-block">${d.paragraphs.map((t) => html`<p>${t}</p>`)}</div>
  <p class="prose-block tool-note">${ctx.licence.oneLine} <a href="/licence/">Licence terms</a>. Contact: <a href="mailto:${email}">${email}</a>.</p>
</div></section>
<section class="sec" aria-labelledby="h-files"><div class="wrap">
  ${secHead({ title: 'Machine-readable files', id: 'h-files' })}
  <ul class="file-list">${d.files.map((f) => html`<li><a href="${f.href}">${f.label}</a><p class="muted">${f.text}</p></li>`)}</ul>
</div></section>`;
  return {
    url: '/ai/', type: 'ai', section: null,
    title: d.title, description: d.description, crumbs, jsonld: [crumbsLd(ctx, crumbs)], main,
  };
}
