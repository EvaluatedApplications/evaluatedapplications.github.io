// Run by publish-site.ps1 on the published wwwroot, after Blazier has written the static shells (tools/<name>/index.html).
// Blazier sets each shell's <title>; the rest of what a search engine or a link preview reads is per page and is added here:
// meta description, canonical, Open Graph / Twitter, and noindex for any page flagged noindex. Usage: node finish-shells.mjs <wwwroot>
//
// Every page is expected to exist; a missing shell, or a shell this script has no entry for, fails the publish (exit 1), so a new
// [BlazierPage] cannot go out with the gallery's generic description. Add the entry to PAGES below when adding a tool.
import fs from 'node:fs';
import path from 'node:path';

const ORIGIN = 'https://evaluatedapplications.github.io';
// path under /tools/ -> description (plain text, under about 160 characters; the wording follows the page's own lede, never a claim it does not make)
const PAGES = {
  // The boot shell for bounced deep links (/tools/app/?/<path>). The GALLERY is not here: it is generated (web/, site-next/tools/index.html).
  // noindex, and canonical points at the gallery, so a stray visit to /tools/app/ is never a search result of its own.
  'app': { d: 'Starting the in-browser runtime for the tool you asked for.', noindex: true, canonical: '/tools/' },
  // ONE tool, five tabs (Chat, Stories, Map, Creature, Forecaster) on one shared model: one shell, one description.
  'prism': { d: 'A tiny trained language model that runs entirely in your browser: chat with it, have it write a story, watch one of its decisions on a map, then retrain a private copy on a foraging task or a price tape.' },
  'analyst': { d: 'A real SQL database running in your browser tab profiles any CSV, JSON or log you give it, charts it, and answers your SQL. Nothing is uploaded.' },
  'helix': { d: 'Paste DNA sequences and measurements and see the helix in the numbers: a score graded in your browser, with the controls that would expose a false win shown beside it.' },
  'plenum': { d: 'A card game played on one array of exact numbers: every card is one operation on the array, and you see which cells it changed. Play solo or in a two-player duel. Runs in your browser.' },
  'prose': { d: 'Paste in text and a rules-first English grammar mines it in your browser, then recombines what it learned into sentences and question and answer pairs.' },
  'council-spending': { d: "Mechanical checks over English councils' published spending data, the same rules for every council, run in your browser. Nothing is uploaded." },
};

const root = process.argv[2];
if (!root) { console.error('usage: node finish-shells.mjs <wwwroot>'); process.exit(1); }
const esc = s => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
let bad = 0;

// The generated gallery owns /tools/ (web/, site-next/tools/index.html). The publish still copies wwwroot/index.html, the RAW TEMPLATE every
// shell was rendered from (no blazier-scaffold meta): it is not a page, so it is removed here and never reaches dist. A root index.html that
// IS a generated shell would be a second gallery (Home.razor must have no BlazierPage): that fails the publish. The boot shell is app/index.html.
const rootIndex = path.join(root, 'index.html');
if (fs.existsSync(rootIndex)) {
  if (/<meta name="blazier-scaffold"/.test(fs.readFileSync(rootIndex, 'utf8'))) { console.error('finish-shells: the publish wrote a root index.html shell; /tools/ belongs to the generated gallery'); process.exit(1); }
  fs.rmSync(rootIndex);
  for (const ext of ['.gz', '.br']) { const s = rootIndex + ext; if (fs.existsSync(s)) fs.rmSync(s); }
  console.log('finish-shells: removed the raw template index.html (the generated gallery owns /tools/)');
}

// The four tools that became tabs of Prism, and the Dashboard (superseded by the Umbrarium demo), keep their old addresses as redirect stubs (GitHub Pages cannot send a 301): /tools/<old>/ goes
// to /tools/prism/ with the query and hash kept, or the matching tab when there is no hash. Written below, so a republish brings them back;
// publish-site.ps1 asserts they reached dist. Not shells (no blazier-scaffold meta, noindex, canonical on the Prism page).
const STUBS = {
  creature: { to: '/tools/prism/', tab: '#creature' }, forecaster: { to: '/tools/prism/', tab: '#forecaster' }, stories: { to: '/tools/prism/', tab: '#stories' }, cartographer: { to: '/tools/prism/', tab: '#map' },
  dashboard: { to: '/umbrarium/' },   // the Dashboard tool was superseded by the Umbrarium demo
};

// every shell Blazier wrote must have an entry
const found = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const f = path.join(dir, e.name);
    if (e.isDirectory()) { if (e.name !== '_framework' && e.name !== '_content' && e.name !== 'data' && e.name !== 'css') walk(f); }
    else if (e.name === 'index.html') found.push(path.relative(root, path.dirname(f)).split(path.sep).join('/'));
  }
})(root);
for (const k of found) if (!(k in PAGES) && !(k in STUBS)) { console.error(`finish-shells: tools/${k}/ has a shell but no entry in PAGES`); bad++; }

for (const [key, page] of Object.entries(PAGES)) {
  const file = path.join(root, key, 'index.html');
  if (!fs.existsSync(file)) { console.error(`finish-shells: missing shell ${file}`); bad++; continue; }
  let html = fs.readFileSync(file, 'utf8');
  if (!/<meta name="blazier-scaffold"/.test(html)) { console.error(`finish-shells: ${file} is not a generated shell`); bad++; continue; }
  const title = (/<title>([\s\S]*?)<\/title>/.exec(html) ?? [])[1] ?? '';
  const url = ORIGIN + (page.canonical ?? `/tools/${key}/`);
  const desc = esc(page.d);
  const tags = [
    `<link rel="canonical" href="${url}" />`,
    `<meta property="og:type" content="website" />`,
    `<meta property="og:site_name" content="Evaluated Applications" />`,
    `<meta property="og:title" content="${title}" />`,
    `<meta property="og:description" content="${desc}" />`,
    `<meta property="og:url" content="${url}" />`,
    `<meta name="twitter:card" content="summary" />`,
    `<meta name="twitter:title" content="${title}" />`,
    `<meta name="twitter:description" content="${desc}" />`,
  ];
  if (page.noindex) tags.unshift(`<meta name="robots" content="noindex,nofollow" />`);
  const d = /<meta name="description"[^>]*>/;
  if (!d.test(html)) { console.error(`finish-shells: ${file} has no description meta to replace`); bad++; continue; }
  html = html.replace(d, `<meta name="description" content="${desc}" />\n    ` + tags.join('\n    '));
  fs.writeFileSync(file, html, 'utf8');
  // Blazier left .gz/.br copies of the shell it wrote; they are stale now and GitHub Pages never serves them
  for (const ext of ['.gz', '.br']) { const s = file + ext; if (fs.existsSync(s)) fs.rmSync(s); }
  console.log(`finish-shells: tools/${key}/${(Buffer.byteLength(html) / 1024).toFixed(1)} KB${page.noindex ? '  noindex' : ''}`);
}
for (const [key, stub] of Object.entries(STUBS)) {
  const dir = path.join(root, key);
  if (fs.existsSync(path.join(dir, 'index.html')) && /<meta name="blazier-scaffold"/.test(fs.readFileSync(path.join(dir, 'index.html'), 'utf8'))) {
    console.error('finish-shells: tools/' + key + '/ is a generated shell but is a redirect stub now; remove its [BlazierPage]'); bad++; continue;
  }
  const target = stub.to + (stub.tab ?? '');
  const html = [
    '<!DOCTYPE html>',
    '<html lang="en">',
    '<head>',
    '<meta charset="utf-8">',
    '<meta name="viewport" content="width=device-width, initial-scale=1">',
    '<title>Moved | Evaluated Applications</title>',
    '<meta name="description" content="This tool has moved to ' + ORIGIN + target + '.">',
    '<meta name="robots" content="noindex">',
    '<link rel="canonical" href="' + ORIGIN + stub.to + '">',
    '<meta http-equiv="refresh" content="0; url=' + target + '">',
    // keep whatever query and hash the visitor arrived with; with no hash, open the tab this tool became
    stub.tab ? '<script>(function(){location.replace("' + stub.to + '"+location.search+(location.hash||"' + stub.tab + '"));})();</script>' : '<script>location.replace("' + stub.to + '");</script>',
    '<meta name="generator" content="redirect-stub">',
    '</head>',
    '<body data-stub>',
    '<p>This tool has moved to <a href="' + target + '">' + (stub.tab ? 'Prism' : 'the Umbrarium demo') + '</a>.</p>',
    '</body>',
    '</html>',
    '',
  ].join('\n');
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'index.html'), html, 'utf8');
  for (const ext of ['.gz', '.br']) { const s = path.join(dir, 'index.html') + ext; if (fs.existsSync(s)) fs.rmSync(s); }
  console.log('finish-shells: tools/' + key + '/ redirect stub -> ' + target);
}
process.exit(bad ? 1 : 0);
