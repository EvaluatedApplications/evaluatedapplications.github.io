#!/usr/bin/env node
// Unit check for the Intelligence Test page (/games/facts/): generates 200 items at each N = 2..12 (mixed determined and
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

// undetermined items: loop members share one base tier, every other candidate sits strictly below it, no single winner
{
  let made = 0;
  for (let k = 0; k < 500; k++) {
    const dim = DIMS[k % 4];
    const n = 2 + (k % 11);
    const item = G.makeItem(dim, n, rng, 'undetermined');
    made++;
    const ring = item._spec.filter((s) => s[1] === 'link').map((s) => s[0]);
    if (ring.length < 2 || ring.length > 3) fail(`loop size ${ring.length}`);
    const tiers = new Set(ring.map((c) => G.tierOf(dim, c)));
    if (tiers.size !== 1) fail(`${dim} loop members differ in tier: ${ring.join(',')}`);
    const shared = [...tiers][0];
    for (const [e, m] of item._spec) {
      if (m === 'link') continue;
      if (G.tierOf(dim, e) + G.deltaOf(dim, m) >= shared) fail(`${dim} non-member ${e} not strictly below the loop`);
    }
    for (const o of G.answerSet(dim, item.candidates, item._spec)) if (o !== null) fail(`${dim} undetermined item can end with winner ${o}`);
  }
  if (made !== 500) fail('undetermined count');

  // regression: a car is faster than a goat, so a car/goat loop with the rest knocked out decides nothing the item may call undetermined
  const bad = {
    kind: 'undetermined', dimension: 'speed', n: 3, answer: 'undetermined',
    candidates: ['goat', 'car', 'train'],
    _spec: [['goat', 'link', 'car'], ['car', 'link', 'goat'], ['train', 'seized_engine', null]],
  };
  if (G.checkItem(bad)[0]) fail('item checker accepted the car/goat loop with different tiers');
  const bad2 = { ...bad, candidates: ['goat', 'car', 'bicycle'], _spec: [['goat', 'link', 'car'], ['car', 'link', 'goat'], ['bicycle', 'flat_tyre', null]] };
  if (G.checkItem(bad2)[0]) fail('item checker accepted the car/goat loop with a bicycle below');
  // same-tier loop with a knocked-out train is accepted, and a loop member that tops the rest never wins by default
  const good = { ...bad, candidates: ['goat', 'sheep', 'train'], _spec: [['goat', 'link', 'sheep'], ['sheep', 'link', 'goat'], ['train', 'seized_engine', null]] };
  if (!G.checkItem(good)[0]) fail('item checker rejected a same-tier loop: ' + G.checkItem(good)[1]);
  for (const o of G.answerSet('speed', good.candidates, good._spec)) if (o !== null) fail('idle loop produced a winner');
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

// the rendered round: all N facts at once before the hide step, none after (tiny fake DOM, page script loaded fresh)
{
  class N {
    constructor(tag) { this.tag = tag; this.kids = []; this.attrs = {}; this.listeners = {}; this.nodeType = 1; this.textContent = ''; }
    setAttribute(k, v) { this.attrs[k] = v; }
    append(...k) { for (const x of k) this.kids.push(x); }
    replaceChildren(...k) { this.kids = k; }
    addEventListener(t, f) { this.listeners[t] = f; }
    focus() {}
    click() { this.listeners.click(); }
    text() { return this.kids.map((k) => (typeof k.text === 'function' ? k.text() : k.value)).join(' '); }
    all(f, out = []) { if (f(this)) out.push(this); for (const k of this.kids) if (k.all) k.all(f, out); return out; }
    querySelector(sel) {
      if (sel === '[data-facts-start]') return this.start || null;
      if (sel === 'h2') return this.all((n) => n.tag === 'h2')[0] || null;
      return null;
    }
  }
  const root = new N('div');
  root.start = new N('button');
  globalThis.document = {
    readyState: 'complete',
    createElement: (t) => new N(t),
    createElementNS: (_, t) => new N(t),
    createTextNode: (v) => ({ nodeType: 3, value: v, text: () => v }),
    querySelector: (s) => (s === '[data-facts]' ? root : null),
    addEventListener() {},
  };
  await import(pathToFileURL(path.join(WEB, 'src', 'assets', 'games', 'facts.js')).href + '?dom');
  root.start.click();
  const lis = () => root.all((n) => n.tag === 'li' && root.all((m) => m.tag === 'ol').some((o) => o.kids.includes(n)));
  const hideBtn = () => root.all((n) => n.tag === 'button' && /Hide the facts/.test(n.text()))[0];
  if (lis().length !== 2) fail(`round 1 should show all 2 facts at once, saw ${lis().length}`);
  if (!hideBtn()) fail('hide button missing');
  else {
    hideBtn().click();
    if (lis().length !== 0) fail('facts still on screen after the hide step');
    if (root.all((n) => n.tag === 'input').length !== 1) fail('confidence slider missing after hide');
    if (root.all((n) => n.attrs.class && /fa-choice/.test(n.attrs.class)).length < 3) fail('candidate buttons missing after hide');
  }
  delete globalThis.document;
}

console.log(`${items} items (${und} undetermined), ${bad} failure(s)`);
process.exit(bad ? 1 : 0);
