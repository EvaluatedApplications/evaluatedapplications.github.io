#!/usr/bin/env node
// Sourcing check for the number tiles on sizzle-first pages (design-language section 8): every figure on a tile must appear in a record
// it cites, so nothing is invented. Tiles that read straight from an evidence record carry no typed figure and need no check.
//   node web/scripts/check-sizzle.mjs
// A cite is "evidence/<id>.json" (data/evidence), "diagrams/<id>.json" (generated: the value is all {placeholders}, checked at build),
// or "monorepo:<path>" (an owner's own document; skipped with a note when MonoRepo is not on this machine, e.g. in CI).
// A tile may carry "proof": ["121.40"] to check the exact figure behind a rounded display value. The figures in the "See the numbers"
// text are checked against the page's cited and evidence records and reported as warnings.
import fs from 'node:fs';
import path from 'node:path';
import { DATA, MONOREPO, readJson } from './lib/data.mjs';

const errors = [], warns = [], notes = [];
const norm = (t) => t.replace(/(\d),(?=\d{3}(?!\d))/g, '$1').replace(/\r/g, '');
const numbersIn = (s) => [...new Set((String(s).replace(/\{\w+\}/g, ' ').match(/\d+(?:,\d{3})*(?:\.\d+)?/g) || []).map((x) => x.replace(/,/g, '')))];
const has = (corpus, tok) => new RegExp(`(?<![\\d.])${tok.replace(/\./g, '\\.')}(?!\\d)`).test(corpus);

const cache = new Map();
function source(cite) {
  if (cache.has(cite)) return cache.get(cite);
  let text = null;
  if (cite.startsWith('monorepo:')) {
    const f = path.join(MONOREPO, cite.slice('monorepo:'.length));
    text = fs.existsSync(f) ? norm(fs.readFileSync(f, 'utf8')) : null;
    if (text == null) notes.push(`${cite} is not on this machine, so its figures were not checked`);
  } else {
    const f = path.join(DATA, cite);
    if (!fs.existsSync(f)) throw new Error(`cite ${cite} does not exist`);
    text = norm(fs.readFileSync(f, 'utf8'));
  }
  cache.set(cite, text);
  return text;
}

function checkBlock(where, sz, extraCites = []) {
  const cites = new Set(extraCites);
  for (const s of sz.stats || []) for (const c of s.cite || []) cites.add(c);
  for (const id of sz.evidence || []) cites.add(`evidence/${id}.json`);
  for (const s of sz.stats || []) {
    if (s.evidence) { cites.add(`evidence/${s.evidence}.json`); continue; }
    if (!s.cite?.length) { errors.push(`${where}: tile "${s.value}" cites no source`); continue; }
    const corpora = s.cite.map(source).filter((c) => c != null);
    if (!corpora.length) continue;
    const toks = [...new Set([...(s.proof || numbersIn(s.value)), ...numbersIn(s.label)])];
    for (const t of toks) if (!corpora.some((c) => has(c, t))) errors.push(`${where}: figure ${t} in tile "${s.value}" is not in ${s.cite.join(', ')}`);
  }
  const all = [...cites].map(source).filter((c) => c != null).join('\n');
  if (all) for (const n of sz.numbers?.items || []) {
    for (const t of numbersIn(n.claim + ' ' + n.text)) if (!has(all, t)) warns.push(`${where}: figure ${t} in "${n.claim}" is not in the cited records`);
  }
  return cites.size;
}

const evidenceOf = new Map();
for (const f of fs.readdirSync(path.join(DATA, 'evidence'))) { const e = readJson(path.join(DATA, 'evidence', f)); (evidenceOf.get(e.product) || evidenceOf.set(e.product, []).get(e.product)).push(`evidence/${f}`); }

let blocks = 0;
for (const f of fs.readdirSync(path.join(DATA, 'products')).filter((x) => x.endsWith('.json'))) {
  const doc = readJson(path.join(DATA, 'products', f));
  const id = f.replace(/\.json$/, '');
  if (doc.sizzle) { blocks++; checkBlock(f, doc.sizzle, evidenceOf.get(id) || []); }
}
// Phasor's tiles are typed from the owner's own documents
{
  const doc = readJson(path.join(DATA, 'products', 'phasor.json'));
  const cites = ['monorepo:Phasor/docs/site.md', 'monorepo:Phasor/docs/superposition-coverage.json', 'monorepo:Phasor/docs/pleroma-map.json'];
  checkBlock('phasor.json', { stats: doc.stats.map((s) => ({ ...s, cite: cites })), numbers: { items: [] } });
  blocks++;
  // the flag reads, widening and tenants tiles and cards: typed from site.md, so every figure must be in it
  checkBlock('phasor.json flags', { stats: doc.flags.tiles.map((s) => ({ ...s, cite: ['monorepo:Phasor/docs/site.md'] })), numbers: { items: doc.flags.items.map((i) => ({ claim: i.title, text: i.text })) } }, ['monorepo:Phasor/docs/site.md']);
  blocks++;
}
for (const n of [...new Set(notes)]) console.log('note: ' + n);
for (const w of warns) console.log('warning: ' + w);
for (const e of errors) console.log('ERROR: ' + e);
console.log(errors.length ? `check-sizzle: ${errors.length} unsourced figure(s)` : `check-sizzle: ok (${blocks} pages with number tiles${warns.length ? `, ${warns.length} warning(s)` : ''})`);
process.exit(errors.length ? 1 : 0);
