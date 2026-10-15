#!/usr/bin/env node
// Cutover readiness: every file under the live site/ must either exist in site-next/ (as a page, a redirect stub or an asset)
// or be on the explicit keep/drop list below. Not part of run.mjs: it is expected to fail until every product is migrated.
//   node web/scripts/check-cutover.mjs
import fs from 'node:fs';
import path from 'node:path';
import { OUT, ROOT } from './lib/data.mjs';
import { walk } from './lint-voice.mjs';

const LIVE = path.join(ROOT, 'site');
// Deliberate decisions, each one recorded in web/CUTOVER.md.
const KEEP_AS_IS = [/^council-live\//];                               // unlinked client page, copied unchanged at cutover
const DROP = [/^recycledao-preview\.html$/, /^articles\/_example\.html$/];   // proposal D5 and the template page
const LEGACY_ASSETS = [/^assets\//];                                   // see CUTOVER.md: the tool shells still link /assets/site.css

const missing = [], ok = { page: 0, asset: 0, kept: 0, dropped: 0 };
for (const f of walk(LIVE)) {
  const rel = path.relative(LIVE, f).replace(/\\/g, '/');
  if (KEEP_AS_IS.some((r) => r.test(rel))) { ok.kept++; continue; }
  if (DROP.some((r) => r.test(rel))) { ok.dropped++; continue; }
  if (LEGACY_ASSETS.some((r) => r.test(rel))) { ok.asset++; continue; }
  const inOut = fs.existsSync(path.join(OUT, rel));
  // /holodb/index.html style directory pages
  if (inOut) { ok.page++; continue; }
  missing.push(rel);
}
console.log(`live files covered by site-next: ${ok.page}; kept as is: ${ok.kept}; dropped: ${ok.dropped}; assets (see CUTOVER.md): ${ok.asset}`);
if (missing.length) { console.log(`NOT YET COVERED (${missing.length}):`); missing.forEach((m) => console.log('  ' + m)); }
console.log(missing.length ? 'cutover: not ready' : 'cutover: every live URL resolves in site-next');
process.exitCode = missing.length ? 1 : 0;
