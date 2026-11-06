const WORLD = {
dimensions: {
speed: 'Which of these is the fastest?',
strength: 'Which of these is the strongest?',
loud: 'Which of these is the loudest?',
night: 'Which of these sees best in the dark?',
},
order: ['speed', 'strength', 'loud', 'night'],
entities: [
['cat', 'L q', 3, 1, 2, 5],
['dog', 'L q', 3, 2, 3, 3],
['rabbit', 'L q', 3, 0, 0, 3],
['fox', 'L q', 3, 1, 2, 4],
['man', 'L g p', 2, 2, 2, 1],
['woman', 'L g p', 2, 2, 2, 1],
['horse', 'L q', 5, 4, 3, 2],
['tortoise', 'L q', 0, 1, 0, 1],
['snail', 'L', 0, 0, 0, 1],
['mouse', 'L q', 2, 0, 1, 4],
['owl', 'L g w', 3, 1, 2, 5],
['bear', 'L q', 3, 5, 4, 3],
['wolf', 'L q', 4, 3, 4, 4],
['lion', 'L q', 4, 5, 5, 4],
['elephant', 'L q', 2, 5, 5, 2],
['cow', 'L q', 2, 4, 3, 2],
['sheep', 'L q', 2, 2, 2, 2],
['pig', 'L q', 2, 2, 4, 2],
['goat', 'L q', 2, 2, 3, 3],
['deer', 'L q', 4, 2, 1, 3],
['hare', 'L q', 4, 0, 1, 3],
['rat', 'L q', 2, 0, 1, 4],
['bat', 'L w', 3, 0, 1, 5],
['eagle', 'L g w', 5, 2, 3, 3],
['donkey', 'L q', 3, 3, 5, 2],
['frog', 'L q', 1, 0, 3, 3],
['duck', 'L g w', 2, 1, 3, 2],
['bicycle', 'v h', 2, 0, 0, null],
['car', 'v h e', 4, 3, 3, null],
['bus', 'v h e', 3, 4, 4, null],
['tractor', 'v h e', 1, 5, 4, null],
['motorbike', 'v h e', 4, 2, 5, null],
['train', 'v e', 5, 5, 5, null],
],
modifiers: {
speed: [
['slow_stroll', 'The {x} is out for a slow, easy stroll.', -1, 'L'],
['sore_foot', 'The {x} has a sore foot.', -1, 'g q'],
['heavy_load', 'The {x} is carrying a heavy load.', -1, ''],
['very_old', 'The {x} is very old.', -1, 'L'],
['worn_tyres', 'The {x} has worn tyres.', -1, 'h'],
['tired_engine', 'The {x} has a tired engine.', -1, 'e'],
['three_legs', 'The {x} has three legs.', -2, 'q'],
['broken_leg', 'The {x} has a broken leg.', -2, 'g q'],
['deep_mud', 'The {x} is stuck in deep mud.', -2, 'q h p'],
['flat_tyre', 'The {x} has a flat tyre.', -2, 'h'],
['asleep', 'The {x} is asleep.', -3, 'L'],
['tied_post', 'The {x} is tied to a post.', -3, 'L'],
['no_fuel', 'The {x} is out of fuel.', -3, 'e'],
['brakes_on', 'The {x} has the brakes jammed on.', -3, 'h'],
['seized_engine', 'The {x} has a seized engine.', -4, 'e'],
['paralysed', 'The {x} is paralysed.', -5, 'L'],
['no_wheels', 'The {x} has every wheel removed.', -5, 'h'],
['hunting', 'The {x} is hunting.', 1, 'q g w'],
['fleeing', 'The {x} is running from danger.', 2, 'L'],
['rested', 'The {x} is well rested.', 1, 'L'],
['fresh_tyres', 'The {x} has brand new tyres.', 1, 'h'],
],
strength: [
['sore_back', 'The {x} has a sore back.', -1, 'L'],
['exhausted', 'The {x} is exhausted.', -1, 'L'],
['stiff_shoulder', 'The {x} has a stiff shoulder.', -1, 'L'],
['rusty_frame', 'The {x} has a rusty frame.', -1, 'v'],
['tired_engine', 'The {x} has a tired engine.', -1, 'e'],
['fever', 'The {x} is sick with a fever.', -2, 'L'],
['very_young', 'The {x} is very young.', -2, 'L'],
['starving', 'The {x} has not eaten for days.', -2, 'L'],
['torn_muscle', 'The {x} has a torn muscle.', -2, 'L'],
['missing_wheel', 'The {x} is missing a wheel.', -2, 'h'],
['cracked_frame', 'The {x} has a cracked frame.', -2, 'v'],
['unconscious', 'The {x} is unconscious.', -3, 'L'],
['broken_engine', 'The {x} has a broken engine.', -3, 'e'],
['long_illness', 'The {x} has been weakened by a long illness.', -4, 'L'],
['snapped_frame', 'The {x} has a snapped frame.', -4, 'v'],
['coma', 'The {x} is in a deep coma.', -5, 'L'],
['no_engine', 'The {x} has no engine fitted.', -5, 'e'],
['fed_rested', 'The {x} is well fed and rested.', 1, 'L'],
['defending', 'The {x} is protecting a newborn.', 2, 'L'],
['training', 'The {x} has been training for months.', 1, 'L'],
['tuned', 'The {x} has a freshly tuned engine.', 1, 'e'],
['prime', 'The {x} is in the prime of life.', 1, 'L'],
],
loud: [
['sleepy', 'The {x} is very sleepy.', -1, 'L'],
['dry_throat', 'The {x} has a dry throat.', -1, 'L'],
['calm_quiet', 'The {x} is calm and content.', -1, 'L'],
['idling', 'The {x} is idling gently.', -1, 'e'],
['sore_throat', 'The {x} has a sore throat.', -2, 'L'],
['padding', 'The {x} is wrapped in thick padding.', -2, ''],
['hiding', 'The {x} is hiding quietly.', -2, 'L'],
['cloth_mouth', 'The {x} has a thick cloth tied over the mouth.', -2, 'L'],
['silencer', 'The {x} has a silencer fitted.', -2, 'e'],
['lost_voice', 'The {x} has no voice left.', -3, 'L'],
['sedated', 'The {x} is deeply sedated.', -3, 'L'],
['engine_off', 'The {x} has the engine switched off.', -3, 'e'],
['anaesthetic', 'The {x} is under heavy anaesthetic.', -4, 'L'],
['born_silent', 'The {x} was born unable to make any sound.', -5, 'L'],
['no_engine', 'The {x} has no engine fitted.', -5, 'e'],
['frightened', 'The {x} is frightened.', 2, 'L'],
['calling_mate', 'The {x} is calling out to a mate.', 2, 'L'],
['hungry_calling', 'The {x} is hungry and calling for food.', 1, 'L'],
['horn', 'The {x} is sounding the horn.', 2, 'v'],
],
night: [
['squinting', 'The {x} is squinting in the glare.', -1, 'L'],
['drowsy', 'The {x} is drowsy.', -1, 'L'],
['short_sighted', 'The {x} is a little short-sighted.', -1, 'L'],
['tired_eyes', 'The {x} has tired eyes.', -1, 'L'],
['cloudy_eyes', 'The {x} has cloudy eyes.', -2, 'L'],
['eye_infection', 'The {x} has an eye infection.', -2, 'L'],
['dazzled', 'The {x} has just been dazzled by a bright lamp.', -2, 'L'],
['dust_eyes', 'The {x} has dust in the eyes.', -2, 'L'],
['blindfold', 'The {x} has a cloth tied over the eyes.', -3, 'L'],
['nearly_blind', 'The {x} is old and nearly blind.', -3, 'L'],
['eyes_shut', 'The {x} has the eyes shut tight.', -3, 'L'],
['totally_blind', 'The {x} is totally blind.', -5, 'L'],
['wide_awake', 'The {x} is wide awake after dark.', 1, 'L'],
['adjusted', 'The {x} has had an hour for the eyes to adjust to the dark.', 2, 'L'],
['hunts_night', 'The {x} hunts at night.', 1, 'L'],
['wide_pupils', 'The {x} has wide open pupils.', 1, 'L'],
],
},
links: {
speed: ['link', 'The {x} only moves when the {y} moves.', 0, ''],
strength: ['link', 'The {x} only pulls hard when the {y} pulls hard.', 0, ''],
loud: ['link', 'The {x} only makes a noise when the {y} makes a noise.', 0, ''],
night: ['link', 'The {x} only goes out into the dark when the {y} does.', 0, 'L'],
},
};
const UNDETERMINED = 'undetermined';
const DIMS = WORLD.order;
const tagSet = (s) => new Set(s ? s.split(' ') : []);
const ENT = {};
for (const r of WORLD.entities) {
ENT[r[0]] = { name: r[0], tags: tagSet(r[1]), tiers: { speed: r[2], strength: r[3], loud: r[4], night: r[5] } };
}
const mkMod = (a) => ({ id: a[0], template: a[1], delta: a[2], anyOf: tagSet(a[3]) });
const MODS = {};
const MODBY = {};
const LINK = {};
for (const d of DIMS) {
MODS[d] = WORLD.modifiers[d].map(mkMod);
LINK[d] = mkMod(WORLD.links[d]);
MODBY[d] = Object.fromEntries(MODS[d].map((m) => [m.id, m]));
MODBY[d].link = LINK[d];
}
const base = (dim, name) => ENT[name].tiers[dim];
const applies = (mod, ent) => mod.anyOf.size === 0 || [...mod.anyOf].some((t) => ent.tags.has(t));
const namesFor = (dim) => Object.keys(ENT).filter((k) => ENT[k].tiers[dim] !== null);
export const tierOf = base;
export const deltaOf = (dim, id) => MODBY[dim][id].delta;
export function mulberry32(seed) {
let a = seed | 0;
return () => {
a = (a + 0x6D2B79F5) | 0;
let t = Math.imul(a ^ (a >>> 15), 1 | a);
t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};
}
export function makeRng(f) {
const r = {
random: f,
choice: (a) => a[Math.floor(f() * a.length)],
shuffle(a) { for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(f() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; },
sample: (a, k) => r.shuffle(a.slice()).slice(0, k),
};
return r;
}
function cycles(f) {
const on = new Map();
for (const s of f.keys()) {
if (f.get(s)[0] !== 'link') continue;
const seen = [];
let c = s;
while (f.has(c) && f.get(c)[0] === 'link' && !seen.includes(c)) { seen.push(c); c = f.get(c)[1]; }
if (c === s) { const key = seen.slice().sort().join('+'); for (const node of seen) on.set(node, key); }
}
return on;
}
export function answerSet(dim, cands, spec) {
const f = new Map(spec.map(([e, m, t]) => [e, [m, t]]));
const mods = MODBY[dim];
const on = cycles(f);
const keys = [...new Set(on.values())].sort();
const outcomes = new Set();
for (let mask = 0; mask < (1 << keys.length); mask++) {
const state = new Map();
for (const [n, key] of on) state.set(n, ((mask >> keys.indexOf(key)) & 1) === 1);
const up = (c) => {
if (!f.has(c)) return true;
const [m, t] = f.get(c);
if (m !== 'link') return mods[m].delta >= 0;
if (state.has(c)) return state.get(c);
return up(t);
};
const eff = {};
const idle = new Set();
for (const c of cands) {
if (f.has(c) && f.get(c)[0] !== 'link') eff[c] = base(dim, c) + mods[f.get(c)[0]].delta;
else { eff[c] = base(dim, c); if (!up(c)) idle.add(c); }
}
const top = topOf(eff);
outcomes.add(top !== null && !idle.has(top) ? top : null);
}
return outcomes;
}
function topOf(eff) {
const vals = Object.values(eff);
const top = Math.max(...vals);
const best = Object.keys(eff).filter((c) => eff[c] === top);
return best.length === 1 ? best[0] : null;
}
export function answerOf(dim, cands, spec) {
const out = answerSet(dim, cands, spec);
return out.size === 1 ? [...out][0] : null;
}
function blurs(dim, cands, spec, i, ans) {
if (answerOf(dim, cands, spec.slice(0, i).concat(spec.slice(i + 1))) !== ans) return true;
const e = spec[i][0];
const eff = {};
for (const [c, m] of spec) eff[c] = base(dim, c) + MODBY[dim][m].delta;
const deltas = new Set([0]);
for (const m of MODS[dim]) if (applies(m, ENT[e])) deltas.add(m.delta);
for (const d of deltas) {
eff[e] = base(dim, e) + d;
if (topOf(eff) !== ans) return true;
}
return false;
}
export function checkItem(item) {
const { dimension: dim, _spec: spec, kind } = item;
const cands = item.candidates.slice();
const ents = spec.map((s) => s[0]);
if (new Set(ents).size !== ents.length) return [false, 'two facts about the same candidate'];
if (ents.slice().sort().join() !== cands.slice().sort().join() || cands.length !== item.n) return [false, 'facts and candidates differ'];
for (const [e, m, t] of spec) {
const mod = MODBY[dim][m];
if (!applies(mod, ENT[e])) return [false, 'modifier does not apply'];
if ((m === 'link') !== (t !== null) || (m !== 'link' && mod.delta === 0)) return [false, 'bad modifier'];
if (t !== null && (t === e || !cands.includes(t))) return [false, 'bad link target'];
}
if (kind === 'determined') {
if (spec.some((s) => s[1] === 'link')) return [false, 'link in determined item'];
const ans = answerOf(dim, cands, spec);
if (ans === null || ans !== item.answer) return [false, 'answer not unique or not as recorded'];
const pos = spec.filter((s) => MODBY[dim][s[1]].delta > 0).map((s) => s[0]);
if (pos.length === 1 && pos[0] === ans) return [false, 'the answer fact is the only boost'];
for (let i = 0; i < spec.length; i++) if (!blurs(dim, cands, spec, i, ans)) return [false, `fact ${i} is not load-bearing`];
return [true, 'ok'];
}
if (kind === 'undetermined') {
if (item.answer !== UNDETERMINED) return [false, 'answer must be undetermined'];
if (!spec.some((s) => s[1] === 'link')) return [false, 'no circular fact'];
const f = new Map(spec.map(([e, m, t]) => [e, [m, t]]));
const ring = cycles(f);
if (!ring.size) return [false, 'no cycle'];
const bases = new Set([...ring.keys()].map((c) => base(dim, c)));
if (bases.size !== 1) return [false, 'cycle members differ in tier'];
const shared = [...bases][0];
for (const [e, m] of spec) {
if (!ring.has(e) && base(dim, e) + MODBY[dim][m].delta >= shared) return [false, 'a knocked-out candidate is not below the cycle'];
}
if ([...answerSet(dim, cands, spec)].some((o) => o !== null)) return [false, 'a single answer follows'];
for (let i = 0; i < spec.length; i++) {
const rest = spec.slice(0, i).concat(spec.slice(i + 1));
if (cycles(new Map(rest.map(([e, m, t]) => [e, [m, t]]))).size && answerOf(dim, cands, rest) === null) return [false, `fact ${i} does not break the loop or decide`];
}
return [true, 'ok'];
}
return [false, 'unknown kind'];
}
function attemptDetermined(dim, n, rng, strict) {
const names = namesFor(dim);
const w = rng.choice(names);
const milds = MODS[dim].filter((m) => m.delta === -1 && applies(m, ENT[w]));
if (!milds.length) return null;
const mw = rng.choice(milds);
const target = base(dim, w) + mw.delta;
const kos = MODS[dim].filter((m) => m.delta < 0);
const options = {};
for (const o of names) {
if (o === w || base(dim, o) < target) continue;
const ok = kos.filter((m) => applies(m, ENT[o]) && base(dim, o) + m.delta < target);
if (ok.length) options[o] = ok;
}
const keys = Object.keys(options).sort();
if (keys.length < n - 1) return null;
const spec = [[w, mw.id, null]];
const used = { [mw.id]: 1 };
for (const o of rng.sample(keys, n - 1)) {
let fresh = options[o].filter((m) => !used[m.id]);
if (!fresh.length) fresh = options[o];
const m = rng.choice(fresh);
used[m.id] = (used[m.id] || 0) + 1;
spec.push([o, m.id, null]);
}
if (strict) {
const ds = spec.slice(1).map((s) => MODBY[dim][s[1]].delta);
if (n >= 3 && !ds.some((d) => d >= mw.delta)) return null;
if (n >= 4 && !ds.some((d) => d <= -3)) return null;
}
return [w, spec];
}
function attemptUndetermined(dim, n, rng) {
const names = namesFor(dim);
const k = Math.min(n, rng.choice([2, 3]));
const pool = names.filter((x) => applies(LINK[dim], ENT[x]));
const tiers = [...new Set(pool.map((x) => base(dim, x)))]
.filter((t) => pool.filter((y) => base(dim, y) === t).length >= k).sort((a, b) => a - b);
if (!tiers.length) return null;
const lo = rng.choice(tiers);
const ring = rng.sample(pool.filter((x) => base(dim, x) === lo), k);
const kos = MODS[dim].filter((m) => m.delta < 0);
const options = {};
for (const o of names) {
if (ring.includes(o) || base(dim, o) <= lo) continue;
const ok = kos.filter((m) => applies(m, ENT[o]) && base(dim, o) + m.delta < lo);
if (ok.length) options[o] = ok;
}
const keys = Object.keys(options).sort();
if (keys.length < n - k) return null;
const spec = ring.map((x, i) => [x, 'link', ring[(i + 1) % k]]);
const used = {};
for (const o of rng.sample(keys, n - k)) {
let fresh = options[o].filter((m) => !used[m.id]);
if (!fresh.length) fresh = options[o];
const m = rng.choice(fresh);
used[m.id] = (used[m.id] || 0) + 1;
spec.push([o, m.id, null]);
}
return [UNDETERMINED, spec];
}
export function makeItem(dimension, n, rng, kind = 'determined') {
if (!DIMS.includes(dimension)) throw new Error('unknown dimension ' + dimension);
if (n < 2) throw new Error('n must be at least 2');
for (let k = 0; k < 30000; k++) {
const got = kind === 'determined' ? attemptDetermined(dimension, n, rng, k < 6000) : attemptUndetermined(dimension, n, rng);
if (!got) continue;
const [ans, spec] = got;
const cands = rng.shuffle(spec.map((s) => s[0]));
const shuffled = rng.shuffle(spec.slice());
const item = {
kind, dimension, n,
facts: shuffled.map(([e, m, t]) => MODBY[dimension][m].template.replace('{x}', e).replace('{y}', t)),
question: WORLD.dimensions[dimension],
candidates: cands,
answer: ans,
_spec: shuffled,
};
if (checkItem(item)[0]) return item;
}
throw new Error(`no valid ${kind} item for ${dimension} n=${n}`);
}
export const MAX_N = 12;
export const TIERS = [
{ min: 0, name: 'Pocket', line: 'Two facts or fewer held cleanly.' },
{ min: 3, name: 'Handful', line: 'Three or four facts held.' },
{ min: 5, name: 'Juggler', line: 'Five or six facts held.' },
{ min: 7, name: 'Ledger', line: 'Seven or eight facts held.' },
{ min: 9, name: 'Archivist', line: 'Nine or ten facts held.' },
{ min: 11, name: 'Atlas', line: 'Eleven or twelve facts held.' },
];
export const tierFor = (best) => TIERS.reduce((t, x) => (best >= x.min ? x : t), TIERS[0]);
const mean = (a) => (a.length ? a.reduce((x, y) => x + y, 0) / a.length : 0);
export function analyse(results) {
const best = Math.max(0, ...results.filter((r) => r.correct).map((r) => r.n));
const per = [];
for (const n of [...new Set(results.map((r) => r.n))].sort((a, b) => a - b)) {
const rs = results.filter((r) => r.n === n);
per.push({ n, rounds: rs.length, correct: rs.filter((r) => r.correct).length,
accuracy: Math.round(100 * rs.filter((r) => r.correct).length / rs.length), conf: Math.round(mean(rs.map((r) => r.conf))) });
}
const und = results.filter((r) => r.kind === 'undetermined');
const named = und.filter((r) => r.given !== UNDETERMINED).length;
return { best, tier: tierFor(best), per, undetermined: und.length, named, reading: reading(results) };
}
export function reading(results) {
if (!results.length) return '';
const right = results.filter((r) => r.correct).map((r) => r.conf);
const wrong = results.filter((r) => !r.correct).map((r) => r.conf);
const all = Math.round(mean(results.map((r) => r.conf)));
if (!wrong.length) return `Every round was right. Your confidence averaged ${all} out of 100.`;
if (!right.length) return `No round was right, and your confidence averaged ${all} out of 100.`;
const r = Math.round(mean(right)), w = Math.round(mean(wrong));
const drop = r - w;
if (drop < 10) return `Your confidence stayed high when your answers failed: ${r} on the rounds you got right, ${w} on the rounds you got wrong. The feeling of knowing was not tracking the facts.`;
if (drop < 25) return `Your confidence dipped when your answers failed: ${r} on the rounds you got right, ${w} on the rounds you got wrong. You sensed some of the slips.`;
return `Your confidence fell sharply when your answers failed: ${r} on the rounds you got right, ${w} on the rounds you got wrong. You could tell when you were unsure.`;
}
function el(tag, attrs, ...kids) {
const node = document.createElement(tag);
for (const [k, v] of Object.entries(attrs || {})) { if (v !== null && v !== undefined && v !== false) node.setAttribute(k, v === true ? '' : v); }
for (const kid of kids.flat()) { if (kid !== null && kid !== undefined && kid !== false) node.append(kid.nodeType ? kid : document.createTextNode(String(kid))); }
return node;
}
const cap = (s) => s.charAt(0).toUpperCase() + s.slice(1);
const SVGNS = 'http://www.w3.org/2000/svg';
function svg(tag, attrs, ...kids) {
const node = document.createElementNS(SVGNS, tag);
for (const [k, v] of Object.entries(attrs || {})) node.setAttribute(k, v);
for (const kid of kids) node.append(kid.nodeType ? kid : document.createTextNode(String(kid)));
return node;
}
function chart(per) {
const W = 640, H = 300, L = 44, R = 12, T = 16, B = 44;
const pw = W - L - R, ph = H - T - B;
const y = (v) => T + ph * (1 - v / 100);
const s = svg('svg', { viewBox: `0 0 ${W} ${H}`, class: 'fa-chart', role: 'img',
'aria-label': 'Accuracy and confidence for each number of facts you faced, on a scale of 0 to 100. The table above has the same numbers.' });
for (const v of [0, 25, 50, 75, 100]) {
s.append(svg('line', { x1: L, x2: W - R, y1: y(v), y2: y(v), class: 'fa-grid' }));
s.append(svg('text', { x: L - 6, y: y(v) + 4, class: 'fa-tick', 'text-anchor': 'end' }, v));
}
const gw = pw / per.length, bw = Math.min(24, gw / 2.6);
per.forEach((p, i) => {
const cx = L + gw * (i + 0.5);
for (const [val, cls, dx] of [[p.accuracy, 'fa-bar-acc', -bw - 1], [p.conf, 'fa-bar-conf', 1]]) {
s.append(svg('rect', { x: cx + dx, y: y(val), width: bw, height: Math.max(0, y(0) - y(val)), class: cls }));
s.append(svg('text', { x: cx + dx + bw / 2, y: y(val) - 4, class: 'fa-val', 'text-anchor': 'middle' }, val));
}
s.append(svg('text', { x: cx, y: H - B + 18, class: 'fa-tick', 'text-anchor': 'middle' }, `N=${p.n}`));
});
s.append(svg('text', { x: L + pw / 2, y: H - 6, class: 'fa-tick', 'text-anchor': 'middle' }, 'Facts held at once'));
return s;
}
function boot(root) {
const rng = makeRng(Math.random);
let st;
const live = el('p', { class: 'sr-only', role: 'status', 'aria-live': 'polite' });
const show = (...nodes) => { root.replaceChildren(live, ...nodes); const h = root.querySelector('h2'); if (h) { h.setAttribute('tabindex', '-1'); h.focus({ preventScroll: false }); } };
function startRun() {
st = { n: 2, streak: 0, results: [], round: 0, lastDim: null, lastKind: null };
nextRound();
}
function nextRound() {
st.round += 1;
let dim; do { dim = rng.choice(DIMS); } while (dim === st.lastDim);
const kind = st.lastKind !== 'undetermined' && rng.random() < 0.2 ? 'undetermined' : 'determined';
st.lastDim = dim; st.lastKind = kind;
st.item = makeItem(dim, st.n, rng, kind);
showFacts();
}
function showFacts() {
const it = st.item;
const hide = el('button', { type: 'button', class: 'btn btn-primary fa-next', 'aria-describedby': 'fa-facts-list' }, 'Hide the facts and ask');
hide.addEventListener('click', showQuestion);
show(
el('p', { class: 'fa-meta' }, `Round ${st.round}. Holding ${it.n} facts.`),
el('h2', { class: 'fa-h' }, `All ${it.n} facts`),
el('ol', { class: 'fa-list', id: 'fa-facts-list' }, it.facts.map((f) => el('li', {}, f))),
el('div', { class: 'actions' }, hide),
el('p', { class: 'fa-hint' }, 'Take your time. There is no timer. Once you hide the facts they do not come back in this round.'),
);
hide.focus();
}
function showQuestion() {
const it = st.item;
let pick = null;
const out = el('output', { id: 'fa-conf-out', for: 'fa-conf' }, '50');
const slider = el('input', { type: 'range', id: 'fa-conf', min: 0, max: 100, step: 1, value: 50 });
slider.addEventListener('input', () => { out.textContent = slider.value; });
const submit = el('button', { type: 'button', class: 'btn btn-primary', disabled: true }, 'Lock in answer');
const options = [...it.candidates.slice().sort(), UNDETERMINED];
const buttons = options.map((o) => {
const b = el('button', { type: 'button', class: 'fa-choice' + (o === UNDETERMINED ? ' fa-und' : ''), 'aria-pressed': 'false' }, o === UNDETERMINED ? 'Undetermined' : cap(o));
b.addEventListener('click', () => {
pick = o;
buttons.forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
submit.disabled = false;
});
return b;
});
submit.addEventListener('click', () => answer(pick, Number(slider.value)));
show(
el('p', { class: 'fa-meta' }, `Round ${st.round}. The ${it.n} facts are hidden.`),
el('h2', { class: 'fa-h' }, it.question),
el('p', { class: 'fa-hint' }, 'Pick one. Choose Undetermined if the facts do not settle it.'),
el('div', { class: 'fa-choices', role: 'group', 'aria-label': it.question }, buttons),
el('div', { class: 'fa-conf' }, el('label', { for: 'fa-conf' }, 'How sure are you? ', out, ' out of 100'), slider,
el('div', { class: 'fa-scale', 'aria-hidden': 'true' }, el('span', {}, 'Guessing'), el('span', {}, 'Certain'))),
el('div', { class: 'actions' }, submit),
);
buttons[0].focus();
}
function answer(pick, conf) {
const it = st.item;
const correct = pick === it.answer;
st.results.push({ n: it.n, kind: it.kind, correct, conf, given: pick });
let end = false;
if (correct) { st.streak = 0; if (it.n >= MAX_N) end = true; else st.n += 1; } else { st.streak += 1; if (st.streak >= 2) end = true; }
const go = el('button', { type: 'button', class: 'btn btn-primary' }, end ? 'See your results' : 'Next round');
go.addEventListener('click', () => (end ? showEnd() : nextRound()));
const said = it.answer === UNDETERMINED ? 'The facts did not settle it. The answer was undetermined.' : `The answer was ${it.answer}.`;
show(
el('p', { class: 'fa-meta' }, `Round ${st.round}.`),
el('h2', { class: 'fa-h' }, correct ? 'Correct.' : 'Not this time.'),
el('p', { class: 'fa-fact' }, said),
el('p', { class: 'fa-hint' }, end ? (correct ? 'That was the top of the ladder.' : 'Two misses in a row at the same size end the run.') : correct ? `Next round holds ${st.n} facts.` : 'The next round holds the same number of facts, with new ones.'),
el('div', { class: 'actions' }, go),
);
live.textContent = correct ? 'Correct.' : 'Not this time.';
go.focus();
}
function showEnd() {
const a = analyse(st.results);
const again = el('button', { type: 'button', class: 'btn btn-primary' }, 'Play again');
again.addEventListener('click', startRun);
const rows = a.per.map((p) => el('tr', {}, el('th', { scope: 'row' }, `${p.n} facts`), el('td', { class: 'num' }, p.rounds), el('td', { class: 'num' }, `${p.correct} of ${p.rounds}`), el('td', { class: 'num' }, `${p.accuracy}%`), el('td', { class: 'num' }, `${p.conf}`)));
const table = el('div', { class: 'tbl-wrap' }, el('table', { class: 'tbl' },
el('caption', {}, 'Accuracy and mean confidence for each number of facts you faced'),
el('thead', {}, el('tr', {}, el('th', { scope: 'col' }, 'Facts'), el('th', { scope: 'col', class: 'num' }, 'Rounds'), el('th', { scope: 'col', class: 'num' }, 'Correct'), el('th', { scope: 'col', class: 'num' }, 'Accuracy'), el('th', { scope: 'col', class: 'num' }, 'Mean confidence'))),
el('tbody', {}, rows)));
const legend = el('p', { class: 'fa-legend' }, el('span', { class: 'fa-key fa-key-acc' }), ' Accuracy (percent right)  ', el('span', { class: 'fa-key fa-key-conf' }), ' Mean confidence (out of 100)');
const over = a.undetermined === 0
? 'No undetermined rounds came up in this run.'
: `${a.undetermined} undetermined ${a.undetermined === 1 ? 'round' : 'rounds'} came up. You named a candidate in ${a.named} of ${a.undetermined}${a.named ? ', so the facts were read as settling something they did not' : ', so you saw where the facts ran out every time'}.`;
const ladder = el('ol', { class: 'fa-ranks' }, TIERS.map((t) => el('li', { 'aria-current': t === a.tier ? 'true' : null }, el('strong', {}, t.name), ' ', t.line)));
show(
el('p', { class: 'fa-meta' }, 'Your run'),
el('h2', { class: 'fa-h' }, 'Rank: ', a.tier.name),
el('p', { class: 'fa-fact' }, a.best ? `Largest number of facts answered correctly: ${a.best}.` : 'No round was answered correctly.'),
el('p', { class: 'fa-small' }, 'The rank is set by the facts you held, not by a comparison with other people. We hold no data on anyone else, and nothing you do here leaves your device.'),
el('h3', {}, 'Accuracy for each size'), table,
el('h3', {}, 'Confidence beside accuracy'), chart(a.per), legend,
el('h3', {}, 'Undetermined rounds'), el('p', {}, over),
el('h3', {}, 'Reading'), el('p', {}, a.reading),
el('h3', {}, 'All ranks'), ladder,
el('div', { class: 'actions' }, again),
);
}
const startBtn = root.querySelector('[data-facts-start]');
if (startBtn) startBtn.addEventListener('click', startRun);
}
if (typeof document !== 'undefined') {
const go = () => { const r = document.querySelector('[data-facts]'); if (r) boot(r); };
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', go); else go();
}
