// GitHub Pages cannot send a 301, so a retired URL becomes a small static stub:
// canonical to the new URL, an immediate meta refresh, noindex, and a visible link.
// A stub may also forward legacy fragments (for example packages.html#license) with a few lines of script.
import { esc, jsonScript } from './html.mjs';

export function redirectStub(site, r) {
  const target = r.to.startsWith('http') ? r.to : r.to;
  const abs = site.url + target.split('#')[0];
  const hashes = r.hash ? `<script>(function(){var m=${jsonScript(r.hash)};var t=m[location.hash];if(t)location.replace(t);})();</script>\n` : '';
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Moved | ${esc(site.name)}</title>
<meta name="description" content="This page has moved to ${esc(site.url + target)}.">
<meta name="robots" content="noindex">
<link rel="canonical" href="${esc(abs)}">
<meta http-equiv="refresh" content="0; url=${esc(target)}">
<meta name="generator" content="redirect-stub">
${hashes}</head>
<body data-stub>
<p>This page has moved to <a href="${esc(target)}">${esc(site.url + target)}</a>.</p>
</body>
</html>
`;
}
