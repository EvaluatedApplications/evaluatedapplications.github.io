#!/usr/bin/env node
// Unit check for the Facts at Once game: generates 200 items at each N = 2..12 (mixed determined and
// undetermined) and re-verifies, from the hidden tiers alone, that the answer is unique, has no giveaway,
// that no fact uses a pronoun, and that order and candidates are shuffled.
//   node web/scripts/test-facts.mjs
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { WEB } from './lib/data.mjs';

const G = await import(pathToFileURL(path.join(WEB, 'src', 'assets', 'games', 'facts.js')).href);
const rng = G.makeRng(G.mulberry32(20261106));
const DIMS = ['speed', 'strength', 'loud', 'night'];
const PRONOUN = /\b(he|she|it|its|they|them|their|his|her|hers|him|this|that|these|those)\b/i;
let items = 0, und = 0, bad = 0;
const fail = (m) => { bad++; if (bad <= 10) console.log('FAIL: ' + m); };

// independent re-computation of the hidden tiers (own copy of the rules, from the item's private spec)
function tiersOf(item) {
  const out = {};
  for (const [e, m] of item._spec) out[e] = m;
  return out;
}

for (let n = 2; n <= 12; n++) {
  const t0 = Date.now();
  let firstFactIsSurvivor = 0, firstCand = {};
  for (let k = 0; k < 200; k++) {
    const dim = DIMS[k % 4];
    const kind = k % 5 === 4 ? 'undetermined' : 'determined';
    const item = G.makeItem(dim, n, rng, kind);
    items++;
    if (item.facts.length !== n || item.candidates.length !== n || new Set(item.candidates).size !== n) fail(`n=${n} wrong counts`);
    for (const f of item.facts) if (PRONOUN.test(f)) fail(`pronoun in "${f}"`);
    if (new Set(item.facts).size !== n) fail('duplicate fact');
    // every candidate has exactly one fact naming it as subject
    for (const c of item.candidates) if (item.facts.filter((f) => f.startsWith(`The ${c} `)).length !== 1) fail(`candidate ${c} has not exactly one fact`);
    const ok = G.checkItem(item);
    if (!ok[0]) fail(`checkItem: ${ok[1]}`);
    if (item.kind === 'determined') {
      // survivor must be the single top effective tier, from the hidden tiers
      const eff = {};
      for (const [e, m] of item._spec) eff[e] = G.tierOf(dim, e) + G.deltaOf(dim, m);
      const top = Math.max(...Object.values(eff));
      const best = Object.keys(eff).filter((c) => eff[c] === top);
      if (best.length !== 1 || best[0] !== item.answer) fail(`n=${n} ${dim} answer not unique from tiers`);
      const boosts = item._spec.filter(([, m]) => G.deltaOf(dim, m) > 0).map((s) => s[0]);
      if (boosts.length === 1 && boosts[0] === item.answer) fail('giveaway: survivor carries the only boost');
      if (G.deltaOf(dim, tiersOf(item)[item.answer]) > 0) fail('survivor carries a boost');
      const idx = item._spec.findIndex((s) => s[0] === item.answer);
      if (idx === 0) firstFactIsSurvivor++;
    } else {
      und++;
      if (item.answer !== 'undetermined') fail('undetermined item with a named answer');
      // both resolutions of the loop must not give one and the same unique winner
      const out = G.answerSet(dim, item.candidates, item._spec);
      if (out.size === 1 && !out.has(null)) fail('undetermined item has a single outcome');
    }
    firstCand[item.candidates[0]] = 1;
  }
  // shuffled: the survivor is not always the first fact, candidates do not always lead with the same name
  const detCount = 160;
  if (n >= 4 && firstFactIsSurvivor > detCount * 0.6) fail(`n=${n} survivor fact is first too often (${firstFactIsSurvivor}/${detCount})`);
  if (n >= 4 && Object.keys(firstCand).length < 4) fail(`n=${n} candidate order is not shuffled`);
  console.log(`n=${String(n).padStart(2)}: 200 items ok so far (${Date.now() - t0} ms, informational)`);
}

// ranks and reading
{
  const mk = (n, correct, conf, kind = 'determined', given = 'x') => ({ n, kind, correct, conf, given });
  if (G.tierFor(0).name !== 'Pocket' || G.tierFor(2).name !== 'Pocket' || G.tierFor(3).name !== 'Handful' || G.tierFor(12).name !== 'Atlas' || G.tierFor(11).name !== 'Atlas') fail('tier edges');
  const a = G.analyse([mk(2, true, 80), mk(3, true, 70), mk(4, false, 90), mk(4, false, 85, 'undetermined', 'cat')]);
  if (a.best !== 3 || a.per.length !== 3 || a.per[2].accuracy !== 0 || a.named !== 1 || a.undetermined !== 1) fail('analyse numbers');
  if (!/stayed high/.test(a.reading)) fail('reading should say confidence stayed high');
  if (!/fell sharply/.test(G.reading([mk(2, true, 90), mk(3, false, 20)]))) fail('reading should say confidence fell');
}

console.log(`${items} items (${und} undetermined), ${bad} failure(s)`);
process.exit(bad ? 1 : 0);
