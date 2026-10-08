// Run by publish-site.ps1 on the published wwwroot, after Blazier has written the static shells (tools/<name>/index.html).
// Blazier sets each shell's <title>; the rest of what a search engine or a link preview reads is per page and is added here:
// meta description, canonical, Open Graph / Twitter, and noindex for the unlisted client preview. Usage: node finish-shells.mjs <wwwroot>
//
// Every page is expected to exist; a missing shell, or a shell this script has no entry for, fails the publish (exit 1), so a new
// [BlazierPage] cannot go out with the gallery's generic description. Add the entry to PAGES below when adding a tool.
import fs from 'node:fs';
import path from 'node:path';

const ORIGIN = 'https://evaluatedapplications.github.io';
// path under /tools/ -> description (plain text, under about 160 characters; the wording follows the page's own lede, never a claim it does not make)
const PAGES = {
  '': { d: "Interactive tools that run Evaluated Applications' .NET libraries entirely in your browser: a small language model, a SQL analyst, a learning creature, a forecaster, a grammar miner and a council spending scanner." },
  'prism': { d: 'Talk to a tiny trained language model that runs entirely in your browser, computed live on your own device. It is still learning, so the replies are rough.' },
  'stories': { d: 'The same tiny model as Prism, asked to write a story: give it an opening line and watch it continue, live, one word piece at a time, in your browser.' },
  'cartographer': { d: "Watch one next-word decision of a tiny language model take shape as a path through its representation space, decoded live from the model's own forward pass." },
  'creature': { d: "Draw a world and watch a small holographic-transformer brain learn to forage in it, live in your browser, sensing food through Tracer's distance field." },
  'forecaster': { d: 'Train a small holographic transformer on real historical price ticks and watch its next-move accuracy climb, live in your browser. A demo, not investment advice.' },
  'analyst': { d: 'A real SQL database running in your browser tab profiles any CSV, JSON or log you give it, charts it, and answers your SQL. Nothing is uploaded.' },
  'prose': { d: 'Paste in text and a rules-first English grammar mines it in your browser, then recombines what it learned into sentences and question and answer pairs.' },
  'council-spending': { d: "Mechanical checks over English councils' published spending data, the same rules for every council, run in your browser. Nothing is uploaded." },
  'recycledao-demo': { d: 'RecycleDAO marketplace prototype: a private, browser-only simulation.', noindex: true },
};

const root = process.argv[2];
if (!root) { console.error('usage: node finish-shells.mjs <wwwroot>'); process.exit(1); }
const esc = s => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
let bad = 0;

// every shell Blazier wrote must have an entry
const found = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const f = path.join(dir, e.name);
    if (e.isDirectory()) { if (e.name !== '_framework' && e.name !== '_content' && e.name !== 'data' && e.name !== 'css') walk(f); }
    else if (e.name === 'index.html') found.push(path.relative(root, path.dirname(f)).split(path.sep).join('/'));
  }
})(root);
for (const k of found) if (!(k in PAGES)) { console.error(`finish-shells: tools/${k}/ has a shell but no entry in PAGES`); bad++; }

for (const [key, page] of Object.entries(PAGES)) {
  const file = path.join(root, key, 'index.html');
  if (!fs.existsSync(file)) { console.error(`finish-shells: missing shell ${file}`); bad++; continue; }
  let html = fs.readFileSync(file, 'utf8');
  if (!/<meta name="blazier-scaffold"/.test(html)) { console.error(`finish-shells: ${file} is not a generated shell`); bad++; continue; }
  const title = (/<title>([\s\S]*?)<\/title>/.exec(html) ?? [])[1] ?? '';
  const url = `${ORIGIN}/tools/${key ? key + '/' : ''}`;
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
  console.log(`finish-shells: tools/${key}${key ? '/' : ''}  ${(Buffer.byteLength(html) / 1024).toFixed(1)} KB${page.noindex ? '  noindex' : ''}`);
}
process.exit(bad ? 1 : 0);
