import { html } from '../../scripts/lib/html.mjs';
import { pageHead } from '../layout.mjs';

// Deep links into the Blazor tools app have no static file, so Pages serves this page: bounce them to the app's boot shell, keeping the route.
// The boot shell is /tools/app/ (Showroom publishes it; the generated gallery owns /tools/). Its restore script (Showroom/wwwroot/index.html)
// moves the address bar back to /tools/<rest> and starts the app. Only paths the app can draw are bounced, so a mistyped /tools/ URL stays on
// this page instead of downloading the runtime. APP_DEEP_LINKS = the routes under /tools/ that have no static shell (Showroom: Pages/*.razor
// with a parameter, today /council-spending/{Slug}); add a prefix here in the same change as such a route.
const APP_DEEP_LINKS = ['council-spending/'];
const BOUNCE = `(function (l) { var P = ${JSON.stringify(APP_DEEP_LINKS)}; if (l.pathname.indexOf('/tools/') !== 0) return; var rest = l.pathname.slice('/tools/'.length); if (!P.some(function (p) { return rest.indexOf(p) === 0 && rest.length > p.length; })) return; l.replace(l.protocol + '//' + l.host + '/tools/app/?/' + rest.replace(/&/g, '~and~') + (l.search ? '&' + l.search.slice(1).replace(/&/g, '~and~') : '') + l.hash); }(window.location));`;

export default function notFound(ctx) {
  const links = [...ctx.site.nav, { label: ctx.site.navCta.label, href: ctx.site.navCta.href }];
  const main = html`
${pageHead({ h1: 'That page does not exist', lede: 'The link may be old or mistyped. These are the places most people are looking for.' })}
<section class="sec" aria-label="Where to go"><div class="wrap">
  <div class="err-links">${links.map((l) => html`<a class="btn btn-ghost" href="${l.href}">${l.label}</a>`)}<a class="btn btn-ghost" href="/">Home</a></div>
</div></section>`;
  return {
    url: '/404.html', type: '404', section: null, noindex: true, hidden: true,
    title: 'Page not found', description: 'The page you asked for does not exist. Use the links to find the product, tool, evidence or research page you wanted.',
    jsonld: [], main, headScript: BOUNCE,
  };
}
