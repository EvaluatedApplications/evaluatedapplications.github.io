// The one document shell: head, meta, JSON-LD, skip link, global nav, footer.
// Every page type renders only its <main> content; nothing else may emit a nav or footer.
import { html, raw, esc, jsonScript } from '../scripts/lib/html.mjs';

const MARK = raw(`<svg class="mark" viewBox="0 0 32 32" aria-hidden="true"><defs><linearGradient id="m" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f0796a"/><stop offset=".166" stop-color="#f0a15a"/><stop offset=".333" stop-color="#e6c450"/><stop offset=".5" stop-color="#7bd86a"/><stop offset=".666" stop-color="#4aa3ff"/><stop offset=".833" stop-color="#7d7dff"/><stop offset="1" stop-color="#c07dff"/></linearGradient></defs><path d="M16 5L27 26H5Z" fill="none" stroke="url(#m)" stroke-width="2" stroke-linejoin="round"/></svg>`);

const ICON = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'%3E%3Cdefs%3E%3ClinearGradient id='g' x1='0' y1='0' x2='1' y2='1'%3E%3Cstop offset='0' stop-color='%23f0796a'/%3E%3Cstop offset='.5' stop-color='%237bd86a'/%3E%3Cstop offset='1' stop-color='%23c07dff'/%3E%3C/linearGradient%3E%3C/defs%3E%3Crect width='32' height='32' rx='7' fill='%23050608'/%3E%3Cpath d='M16 5L27 26H5Z' fill='none' stroke='url(%23g)' stroke-width='2' stroke-linejoin='round'/%3E%3C/svg%3E";

export function nav(ctx, current) {
  const s = ctx.site;
  const item = (n) => html`<li><a href="${n.href}"${n.section === current ? raw(' aria-current="page"') : ''}>${n.label}</a></li>`;
  return html`<header class="site-header"><div class="wrap nav" data-nav>
  <a class="brand" href="/">${MARK}${s.shortName}</a>
  <button class="nav-toggle" type="button" aria-expanded="false" aria-controls="site-links">Menu</button>
  <nav aria-label="Main"><ul class="nav-links" id="site-links">
    ${s.nav.map(item)}
    <li><a class="nav-cta" href="${s.navCta.href}"${s.navCta.section === current ? raw(' aria-current="page"') : ''}>${s.navCta.label}</a></li>
    <li><a href="${s.navExternal.href}" rel="noopener">${s.navExternal.label}<span class="sr-only"> (external)</span></a></li>
  </ul></nav>
</div></header>`;
}

export function footer(ctx) {
  const s = ctx.site;
  return html`<footer class="site-footer"><div class="wrap">
  <div class="foot-grid">
    <div><h2>Products</h2><ul>${ctx.products.map((p) => html`<li><a href="${p.href}">${p.name}</a></li>`)}</ul></div>
    <div><h2>Tools</h2><ul>${ctx.tools.map((t) => html`<li><a href="${t.href}">${t.name}</a></li>`)}</ul></div>
    <div><h2>Learn</h2><ul><li><a href="/evidence/">Evidence</a></li><li><a href="/research/">Research</a></li><li><a href="/products/">All products</a></li><li><a href="/tools/">All tools</a></li></ul></div>
    <div><h2>Company</h2><ul><li><a href="/partner/">Partner with us</a></li><li><a href="/licence/">Licence</a></li><li><a href="/ai/">For AI readers</a></li><li><a href="${s.navExternal.href}" rel="noopener">NuGet</a></li></ul></div>
    <div><h2>Machine-readable</h2><ul><li><a href="/llms.txt">llms.txt</a></li><li><a href="/llms-full.txt">llms-full.txt</a></li><li><a href="/sitemap.xml">sitemap.xml</a></li></ul></div>
  </div>
  <div class="foot-base"><span>${s.name} · .NET libraries · ${s.place}</span><span class="mono">© ${ctx.year} ${s.name}</span></div>
</div></footer>`;
}

export function document_(ctx, page, main) {
  const s = ctx.site;
  const canonical = s.url + page.url;
  const title = page.fullTitle ?? `${page.title} | ${s.name}`;
  const ld = (page.jsonld || []).map((o) => html`<script type="application/ld+json">${raw(jsonScript(o))}</script>`);
  const body = html`<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${title}</title>
<meta name="description" content="${page.description}">
<link rel="canonical" href="${canonical}">
${page.noindex ? raw('<meta name="robots" content="noindex">') : ''}
<meta property="og:type" content="${page.url === '/' ? 'website' : 'article'}">
<meta property="og:site_name" content="${s.name}">
<meta property="og:title" content="${title}">
<meta property="og:description" content="${page.description}">
<meta property="og:url" content="${canonical}">
<meta name="twitter:card" content="summary">
<link rel="icon" href="${raw(ICON)}">
<script>document.documentElement.classList.add('js')</script>
<link rel="stylesheet" href="/assets/ea.css?v=${ctx.assets.css}">
${ld}
${page.headScript ? html`<script>${raw(page.headScript)}</script>` : ''}
</head>
<body class="${page.cat ? 'cat-' + page.cat : ''}" data-page="${page.type}">
<a class="skip" href="#main">Skip to content</a>
<div class="beam" aria-hidden="true"></div>
${nav(ctx, page.section)}
<main id="main" tabindex="-1">
${main}
</main>
${footer(ctx)}
${page.hashForward ? html`<script type="application/json" id="hash-forward">${raw(jsonScript(page.hashForward))}</script>` : ''}
<script src="/assets/site.js?v=${ctx.assets.js}" defer></script>
${(page.scripts || []).map((src) => html`<script type="module" src="${src}"></script>`)}
</body>
</html>
`;
  return body.toString();
}

export function breadcrumbs(items) {
  return html`<nav class="crumbs" aria-label="Breadcrumb"><ol>${items.map((c, i) => (i === items.length - 1 ? html`<li aria-current="page">${c.label}</li>` : html`<li><a href="${c.href}">${c.label}</a></li>`))}</ol></nav>`;
}

export function crumbsLd(ctx, items) {
  return {
    '@context': 'https://schema.org', '@type': 'BreadcrumbList',
    itemListElement: items.map((c, i) => ({ '@type': 'ListItem', position: i + 1, name: c.label, item: ctx.site.url + c.href })),
  };
}

export function pageHead({ crumbs, eyebrow, h1, lede, wide, extra }) {
  return html`<header class="page-head"><div class="wrap">
${crumbs ? breadcrumbs(crumbs) : ''}
${eyebrow ? html`<p class="eyebrow">${eyebrow}</p>` : ''}
<h1${wide ? raw(' class="wide"') : ''}>${h1}</h1>
${lede ? html`<p class="lede">${lede}</p>` : ''}
${extra || ''}
</div></header>`;
}

export function secHead({ title, line, more, cat, id }) {
  return html`<div class="sec-head">${cat ? html`<span class="sec-dot cat-${cat}" aria-hidden="true"></span>` : ''}<h2${id ? raw(` id="${esc(id)}"`) : ''}>${title}</h2>${line ? html`<span class="sec-line">${line}</span>` : ''}${more ? html`<a class="sec-more" href="${more.href}">${more.label}</a>` : ''}</div>`;
}
