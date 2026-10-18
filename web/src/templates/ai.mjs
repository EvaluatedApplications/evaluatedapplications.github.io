import { html } from '../../scripts/lib/html.mjs';
import { inline } from '../inline.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';

export default function ai(ctx) {
  const d = ctx.pages.ai;
  const crumbs = [{ label: 'Home', href: '/' }, { label: d.navLabel, href: '/ai/' }];
  const main = html`
${pageHead({ crumbs, h1: d.headline, wide: true, lede: inline(d.lede) })}
${d.sections.map((s) => html`<section class="sec" aria-labelledby="h-${s.id}"><div class="wrap">
  ${secHead({ title: s.title, id: 'h-' + s.id })}
  ${s.paragraphs ? html`<div class="prose-block">${s.paragraphs.map((t) => html`<p>${inline(t)}</p>`)}</div>` : ''}
  ${s.items ? html`<ul class="prose-block">${s.items.map((t) => html`<li>${inline(t)}</li>`)}</ul>` : ''}
</div></section>`)}
<section class="sec" aria-labelledby="h-contact"><div class="wrap">
  ${secHead({ title: 'Contact', id: 'h-contact' })}
  <p class="prose-block">Partnership and licence enquiries: <a href="/partner/">Partner with us</a></p>
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
