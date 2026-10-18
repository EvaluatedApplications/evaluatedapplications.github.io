import { html } from '../../scripts/lib/html.mjs';
import { inline } from '../inline.mjs';
import { pageHead, secHead, crumbsLd } from '../layout.mjs';

// One game's own page, rendered from data/games/<id>.json: sizzle, Play button, then how it plays, section by section.
export default function game(ctx, g) {
  const d = g.doc;
  const crumbs = [{ label: 'Home', href: '/' }, { label: 'Games', href: '/games/' }, { label: g.name, href: g.href }];
  const modes = g.modes.map((m) => html`<span class="pill">${m}</span>`);
  const extra = html`<div class="actions"><a class="btn btn-primary" href="${g.play}">${d.playLabel}</a><a class="btn btn-ghost" href="#${d.sections[0].id}">How it plays</a></div>
  <p class="game-note">${d.playNote}</p>`;
  const main = html`
<div class="sizzle-page">
${pageHead({ crumbs, eyebrow: d.eyebrow, h1: d.headline, wide: true, lede: d.sub, extra })}
${d.sections.map((s) => html`<section class="sec" id="${s.id}" aria-labelledby="h-${s.id}"><div class="wrap">
  ${secHead({ title: s.title, id: 'h-' + s.id })}
  ${s.paragraphs ? html`<div class="prose-block">${s.paragraphs.map((t) => html`<p>${inline(t)}</p>`)}</div>` : ''}
  ${s.terms ? html`<dl class="term-list">${s.terms.map((x) => html`<dt>${x.term}</dt><dd>${inline(x.text)}</dd>`)}</dl>` : ''}
</div></section>`)}
<section class="sec" aria-labelledby="h-play"><div class="wrap"><div class="callout closing">
  <h2 id="h-play">${d.closing.title}</h2>
  <p>${d.closing.text}</p>
  <div class="actions"><a class="btn btn-primary" href="${g.play}">${d.playLabel}</a><a class="btn btn-ghost" href="/games/">All games</a></div>
  <div class="meta game-meta"><span>Modes</span>${modes}</div>
</div></div></section>
</div>`;
  const ld = {
    '@context': 'https://schema.org', '@type': 'VideoGame', name: g.name, url: ctx.site.url + g.href,
    description: d.description, applicationCategory: 'Game', operatingSystem: 'Any modern web browser',
    gamePlatform: 'Web browser', playMode: ['SinglePlayer', 'MultiPlayer'],
    publisher: { '@id': ctx.site.url + '/#org' },
  };
  return {
    url: g.slug, type: 'game', section: 'games', cat: g.cat,
    title: d.title, description: d.description,
    crumbs, jsonld: [crumbsLd(ctx, crumbs), ld], main,
  };
}
