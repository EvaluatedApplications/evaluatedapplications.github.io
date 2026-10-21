// Offline smoke test of wwwroot/doctrine/lane.js (no browser): runs the whole canvas script against a stub 2D context, a stub Web Audio and a synthetic packet stream, in the wide and the
// narrow (vertical) layout, and fails on any exception or on a missing effect. It proves the script RUNS, keeps its state straight, hit-tests the order chips on the lane, and makes the
// right sounds under the right limits; it cannot show what the drawing LOOKS like or what the sounds SOUND like.
// Usage: node tools/lane-view-smoke.mjs   (from the Showroom folder). LANE_JS=<file> checks a different copy of the script (the mutation checks use it).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const src = fs.readFileSync(process.env.LANE_JS || new URL('../wwwroot/doctrine/lane.js', import.meta.url), 'utf8');
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'lane-smoke-'));
const file = path.join(tmp, 'lane.mjs');
fs.writeFileSync(file, src);

let calls = 0; const counts = {};
const ctx = () => new Proxy({}, {
  get: (t, k) => (k in t ? t[k] : k === 'measureText' ? () => ({ width: 30 }) : typeof k === 'string' ? (...a) => { calls++; counts[k] = (counts[k] || 0) + 1; } : undefined),
  set: (t, k, v) => { t[k] = v; return true; },
});
function canvas(parentWidth) {
  return { width: 0, height: 0, style: {}, getContext: () => ctx(), toDataURL: () => 'data:image/png;base64,AA==', addEventListener() {}, removeEventListener() {},
    getBoundingClientRect: () => ({ left: 0, top: 0 }), parentElement: { clientWidth: parentWidth } };
}
// a stand-in for Web Audio that only counts what is built, and a stand-in for the page's document that keeps its listeners
const AU = { contexts: 0, voices: 0, closed: 0 };
class FakeAudioContext {
  constructor() { AU.contexts++; this.currentTime = 0; this.sampleRate = 8000; this.state = 'running'; this.destination = {}; }
  createGain() { return { gain: { value: 1, setValueAtTime() {}, linearRampToValueAtTime() {}, exponentialRampToValueAtTime() {} }, connect() {} }; }
  createOscillator() { AU.voices++; return { type: '', frequency: { setValueAtTime() {}, exponentialRampToValueAtTime() {} }, connect() {}, start() {}, stop() {}, setPeriodicWave() {} }; }
  createBufferSource() { AU.voices++; return { playbackRate: { value: 1 }, connect() {}, start() {}, stop() {} }; }
  createBuffer(c, n) { return { getChannelData: () => new Float32Array(n) }; }
  createPeriodicWave() { return {}; }
  resume() {} close() { AU.closed++; }
}
globalThis.AudioContext = FakeAudioContext;
const store = new Map();
const goodStorage = { getItem: (k) => (store.has(k) ? store.get(k) : null), setItem: (k, v) => { store.set(k, v); } };
globalThis.localStorage = goodStorage;
const docListeners = [];
let rafCb = null;
globalThis.window = globalThis; globalThis.innerHeight = 800; globalThis.devicePixelRatio = 2;
globalThis.addEventListener = () => {}; globalThis.removeEventListener = () => {};
globalThis.document = {
  createElement: () => canvas(100),
  addEventListener: (n, f) => { docListeners.push([n, f]); },
  removeEventListener: (n, f) => { const i = docListeners.findIndex((x) => x[0] === n && x[1] === f); if (i >= 0) docListeners.splice(i, 1); },
};
globalThis.Image = class { set src(v) { setTimeout(() => this.onload && this.onload(), 0); } };
globalThis.requestAnimationFrame = (cb) => { rafCb = cb; return 1; };
globalThis.cancelAnimationFrame = () => {};

const lane = await import(pathToFileURL(file).href);
let fail = 0;
const check = (ok, what) => { if (!ok) { fail++; console.log('  FAIL: ' + what); } else console.log('  ok: ' + what); };

// the ground of the default setup: 3 lanes of 8 rows with a wall row between, 5 base cells each end, 40 lane cells each half
const rows = [];
for (let r = 0; r < 26; r++) {
  const wall = r % 9 === 8; let s = '';
  for (let c = 0; c < 90; c++) s += wall && c >= 5 && c < 85 ? '#' : (r % 9 === 3 && c === 60 ? ',' : '.');
  rows.push(s);
}
const layout = JSON.stringify({ cell: 2, width: 180, height: 52, playerEdge: 10, enemyEdge: 170, laneHalf: 8, holdPct: 35, laneCentreY: [8, 26, 44], rows });
const MENU = [{ order: 0, name: 'Advance' }, { order: 1, name: 'Hold' }, { order: 3, name: 'Fall back' }, { order: 5, name: 'Hunt' }, { order: 4, name: 'Guard' }];

function packet(bodies, stage = 1, pBase = 1, eBase = 1, status = 0, tick = 100) {
  const f = new Float32Array(8 + bodies.length * 10);
  f.set([tick, stage, status, pBase, eBase, bodies.length, 0.5, 0], 0);
  bodies.forEach((b, i) => f.set([b.id, b.side, b.lane, b.kind, b.x, b.y, b.side ? -1 : 1, 0, b.hp ?? 1, b.fight ? 1 : 0], 8 + i * 10));
  return new Uint8Array(f.buffer);
}
const LIM_TICK = 2, LIM_SECOND = 14;   // the limits the design promises: the script's own numbers must not be above them
const names = () => Object.keys(lane._test.sfx.audio.last).sort();

for (const [name, width] of [['wide', 1100], ['narrow (vertical)', 390]]) {
  console.log('== ' + name);
  const frames = []; const calls2 = [];
  const bodies = Array.from({ length: 16 }, (_, i) => ({ id: i, side: i % 2, lane: i % 3, kind: i % 8, x: 20 + i * 8, y: 8 + (i % 3) * 18, hp: 1 }));
  let next = packet(bodies);
  const ref = { invokeMethodAsync: async (m, ...a) => { if (m === 'Frame') { frames.push(a[0]); const p = next; next = null; return p; } calls2.push([m, ...a]); } };
  const cv = canvas(width);
  const ctxBefore = AU.contexts, voxBefore = AU.voices;
  await lane.init(cv, ref, layout, '/tools/doctrine/img/');
  const st = lane._test.state();
  check(st.vertical === (width < 720) && st.cssW === width && st.cssH > 300, `layout fits (vertical ${st.vertical}, ${st.cssW} x ${st.cssH})`);
  check(lane.icons().length === 8, 'eight soldier pictures are made');
  const info = (o) => lane.setInfo(JSON.stringify({ selected: 1, stage: 0, human: true, left: 30, menu: MENU, orders: [[0, 1, 3], [5, 4, 0]], stagedPacks: [{ lane: 1, kind: 3, count: 4 }, { lane: 1, kind: 7, count: 4 }], stagedOrders: { 2: 3 }, work: {}, ...o }));
  info({});
  let now = 1000;
  const tick = async () => { now += 16; rafCb(now); await new Promise((r) => setTimeout(r, 0)); };
  await tick(); await tick();
  check(st.bodies.size === 16 && frames.length >= 1, `the first packet put 16 bodies on the field (${st.bodies.size})`);
  // ---- team colours: two different colours, and every body on the field gets its team disc under its feet
  const tm = lane._test.teams();
  check(tm.length === 2 && /^#[0-9a-f]{6}$/i.test(tm[0].main) && /^#[0-9a-f]{6}$/i.test(tm[1].main) && tm[0].main.toLowerCase() !== tm[1].main.toLowerCase() && tm[0].light !== tm[1].light && tm[0].dark !== tm[1].dark, `the two teams have different colours (${tm[0].main} you, ${tm[1].main} them)`);
  const e0 = counts.ellipse || 0; lane._test.draw(3); const e1 = counts.ellipse || 0;
  const discs = e1 - e0;
  lane._test.draw(3); const perDraw = (counts.ellipse || 0) - e1;
  check(perDraw === discs && discs >= st.bodies.size, `each body is drawn with a team disc under its feet (${discs} ellipses for ${st.bodies.size} bodies)`);
  // a hit, then a death, then a new arrival
  next = packet(bodies.map((b, i) => (i === 2 ? { ...b, hp: 0.6, fight: true } : b)).filter((b) => b.id !== 5).concat([{ id: 40, side: 0, lane: 1, kind: 4, x: 8, y: 26, hp: 1 }]));
  await tick(); await tick();
  check(st.floats.length >= 1, 'a hit raised a small damage number');
  check(st.corpses.length === 1 && !st.bodies.has(5), 'a body that left the packet fell (one corpse, gone from the field)');
  check(st.bodies.has(40) && st.rings.length >= 1, 'a new body arrived with a ring');
  for (let i = 0; i < 100; i++) await tick();   // 1.6 s: effects expire
  check(st.corpses.length === 0 && st.floats.length === 0 && st.rings.length === 0, 'effects expire');
  next = packet(bodies, 2, 0, 0.4);
  await tick(); await tick();
  check(st.head.stage === 2 && st.head.pBase === 0, 'the over packet is read (your base down)');

  // ---- sound: nothing starts before a tap (hits, a fall, a castle and the end all happened above)
  const A = lane._test.sfx.audio;
  check(AU.contexts === ctxBefore && A.ctx === null && A.played === 0, 'no audio before a first tap: no audio context built, no sound made, though hits, a fall, a castle and the end have all happened');
  check(docListeners.some((l) => l[0] === 'click') && docListeners.some((l) => l[0] === 'touchend') && docListeners.some((l) => l[0] === 'keydown'), 'the script waits for a tap or a key (listeners on the page)');
  check(lane._test.sfx.play('tap', 99999) === false && AU.voices === voxBefore, 'a sound asked for before the first tap is dropped (not queued, nothing built)');
  docListeners.filter((l) => l[0] === 'click').forEach((l) => l[1]({}));
  check(AU.contexts === ctxBefore + 1 && A.ctx !== null, 'the first tap anywhere starts the audio (one context)');
  docListeners.filter((l) => l[0] === 'click').forEach((l) => l[1]({}));
  check(AU.contexts === ctxBefore + 1, 'more taps do not build a second context');

  // ---- the sound table covers every event, short and quiet
  const T = lane._test.sfx.table;
  check(lane._test.sfx.maxPerTick <= LIM_TICK && lane._test.sfx.maxPerSecond <= LIM_SECOND, 'the limits in the script are no higher than 2 a tick and 14 a second');
  const EVENTS = ['tap', 'stage', 'orderSet', 'orderClear', 'tick', 'tickLast', 'runStart', 'hit', 'arrow', 'charge', 'thud', 'fall', 'castleHit', 'castleFall', 'win', 'lose'];
  check(EVENTS.every((e) => T[e] && T[e].v.length > 0 && T[e].v.every((v) => ['square', 'pulse', 'triangle', 'noise'].includes(v.w))), 'every event has a sound made of square, pulse, triangle or noise voices');
  const len = (e) => Math.max(...T[e].v.map((v) => (v.at || 0) + v.d));
  check(EVENTS.every((e) => len(e) <= 1e-9 + (e === 'win' || e === 'lose' ? 0.8 : e === 'castleFall' ? 0.6 : 0.3)) && EVENTS.every((e) => T[e].v.every((v) => v.g <= 0.8)), 'every sound is short (fights 0.3 s, a castle falling 0.6 s, jingles 0.8 s) and none is loud');
  check(Object.keys(T).every((e) => EVENTS.includes(e)), 'the table holds no sound without an event');
  check(T.tick.v[0].f[0] !== T.tickLast.v[0].f[0] && T.tickLast.v.length > T.tick.v.length, 'the last second of the countdown sounds different from the others');

  // ---- each event makes its own sound (the event-to-sound mapping, end to end)
  let clock = 100000, tk = 500;
  const cur = [
    { id: 100, side: 0, lane: 1, kind: 0, x: 60, y: 26 }, { id: 101, side: 1, lane: 1, kind: 3, x: 70, y: 26 }, { id: 102, side: 1, lane: 0, kind: 7, x: 70, y: 8 },
    { id: 103, side: 0, lane: 0, kind: 0, x: 60, y: 8 }, { id: 104, side: 0, lane: 2, kind: 2, x: 60, y: 44 }, { id: 105, side: 1, lane: 2, kind: 0, x: 70, y: 44 },
  ];
  let pB = 1, eB = 1;
  const feed = (mut, stage = 1, status = 0) => { A.last = {}; clock += 5000; tk += 10; const list = cur.map((b) => ({ ...b })); mut(list); lane._test.applyPacket(packet(list, stage, pB, eB, status, tk), clock); return list; };
  const same = (a, b) => a.length === b.length && a.every((x, i) => x === b[i]);
  feed(() => {});   // the new soldiers replace the old ones: the old ones fall, that is not what is tested here
  feed(() => {});
  check(same(names(), []), 'a quiet packet makes no sound');
  feed((l) => { l[0].hp = 0.7; });
  check(same(names(), ['hit']), 'a soldier hurt by a plain fighter: a hit');
  const f1 = feed((l) => { l[0].hp = 0.5; l[1].fight = true; }); cur[1].fight = true; cur[0].hp = 0.5;
  check(same(names(), ['arrow']), 'hurt while an archer of the other side fights in the lane: an arrow');
  feed((l) => { l[3].hp = 0.5; l[2].fight = true; }); cur[2].fight = true; cur[3].hp = 0.5;
  check(same(names(), ['thud']), 'hurt while a catapult of the other side fights in the lane: a thud');
  feed((l) => { l[4].fight = true; }); cur[4].fight = true;
  check(same(names(), ['charge']), 'a rider that starts to fight: a charge');
  cur.splice(1, 1);
  feed(() => {});
  check(same(names(), ['fall']), 'a soldier that is gone from the packet: a fall');
  pB = 0.6; feed(() => {});
  check(same(names(), ['castleHit']), 'your castle takes damage: castleHit');
  pB = 0; feed(() => {});
  check(same(names(), ['castleFall']), 'a castle at nothing: castleFall');
  pB = 1; feed(() => {}, 0);
  check(same(names(), []), 'the run ending in a pause makes no sound of its own');
  feed(() => {}, 1);
  check(same(names(), ['runStart']), 'the pause ending and the run starting: runStart');
  eB = 0; feed(() => {}, 2, 1);
  check(same(names(), ['castleFall', 'win']), 'their castle falling and the win together: both play (two sounds is the cap)');
  eB = 1; feed(() => {}, 1);
  feed(() => {}, 2, 2);
  check(same(names(), ['lose']), 'a lost match: the lose jingle');

  // ---- the limiter: a big clash is a few sounds
  const big = [];
  for (let i = 0; i < 24; i++) big.push({ id: 200 + i, side: i % 2, lane: i % 3, kind: i % 8, x: 40 + i, y: 8 + (i % 3) * 18, hp: 1, fight: false });
  feed((l) => { l.length = 0; l.push(...big); });
  const before = A.played;
  A.last = {}; clock += 5000; tk += 10;
  lane._test.applyPacket(packet(big.map((b, i) => ({ ...b, hp: 0.4, fight: true })).slice(0, 18), 1, 0.3, 0.3, 0, tk), clock);
  const made = A.played - before;
  check(made >= 1 && made <= LIM_TICK, `a clash of 18 hurt, 6 fallen and both castles hit in ONE tick makes at most ${LIM_TICK} sounds (made ${made})`);
  const kept = names();
  check(kept.length === made && !kept.includes('hit'), `the loudest news is kept (${kept.join(', ')}), not the many small hits`);
  A.last = {}; A.tick = -1; A.count = 0;
  const n1 = lane._test.sfx.batch(['hit', 'arrow', 'thud', 'fall', 'castleHit', 'charge'], 7, 900000);
  const n2 = lane._test.sfx.batch(['hit', 'arrow', 'thud', 'fall', 'castleHit', 'charge'], 7, 900500);
  check(n1 <= LIM_TICK && n1 + n2 <= LIM_TICK, `asking again inside the same tick adds nothing past the cap (${n1} + ${n2})`);
  check(lane._test.sfx.batch(['hit'], 8, 905000) === 1, 'the next tick has its own allowance');
  A.last = {}; A.winStart = 0; A.winCount = 0;
  let played = 0; for (let i = 0; i < 49; i++) if (lane._test.sfx.play(EVENTS[i % EVENTS.length], 2000000 + i * 20)) played++;
  check(played >= 8 && played <= LIM_SECOND, `no more than ${LIM_SECOND} sounds in one second however fast the events come (49 different asks in 0.98 s, ${played} played)`);
  A.last = {}; A.winStart = 0; A.winCount = 0;
  check(lane._test.sfx.play('hit', 3000000) === true && lane._test.sfx.play('hit', 3000010) === false && lane._test.sfx.play('hit', 3000200) === true, 'the same sound is not repeated inside its gap');

  // ---- the countdown
  const fresh = () => { A.last = {}; A.winStart = -1e9; A.winCount = 0; };
  info({});
  const say = (left) => { fresh(); info({ left }); return names(); };
  const seq = [6, 5, 4, 3, 2, 1].map(say);
  check(same(seq[0], []) && same(seq[1], ['tick']) && same(seq[2], ['tick']) && same(seq[3], ['tick']) && same(seq[4], ['tick']) && same(seq[5], ['tickLast']), 'the last five seconds tick and the last one has its own tone; before that, silence');
  check(same(say(1), []), 'the same second asked again does not tick twice');
  check(same((() => { fresh(); lane.setInfo(JSON.stringify({ selected: 1, stage: 1, human: true, left: 3, menu: MENU })); return names(); })(), []), 'no ticking outside the pause');
  info({});   // back to a pause with lane 2 picked

  // ---- taps: order chips on the lane
  const boxes = lane._test.menu(1);
  check(boxes.length === 5 && boxes.every((b) => b.h >= 44 && b.w >= 44), `five order chips on the picked lane, each at least 44 px both ways (${boxes.map((b) => Math.round(b.w) + 'x' + Math.round(b.h)).join(' ')})`);
  const overlap = boxes.some((a, i) => boxes.some((b, j) => i < j && a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h));
  check(!overlap && boxes.every((b) => b.x >= 0 && b.y >= 0 && b.x + b.w <= st.cssW && b.y + b.h <= st.cssH), 'the chips do not overlap each other and all sit on the board');
  check(boxes.map((b) => b.name).join() === 'Advance,Hold,Fall back,Hunt,Guard', 'the chips are Advance, Hold, Fall back, Hunt and Guard');
  const laneTop = (l) => { const cy = JSON.parse(layout).laneCentreY[l]; return st.vertical ? [cy * st.v, (180 - 100) * st.u] : [100 * st.u, cy * st.v]; };
  for (const b of boxes) {
    calls2.length = 0; fresh();
    const h = lane._test.tap(b.x + b.w / 2, b.y + b.h / 2);
    check(h && h.kind === 'order' && h.lane === 1 && h.order === b.order && calls2.length === 1 && calls2[0][0] === 'OnOrderTap' && calls2[0][1] === 1 && calls2[0][2] === b.order, `a tap on the ${b.name} chip sets order ${b.order} on lane 2 (and nothing else)`);
  }
  info({ stagedOrders: { 1: 1 } });
  fresh(); lane._test.tap(boxes[1].x + 5, boxes[1].y + 5);
  check(same(names(), ['orderClear']), 'tapping the order that is already staged sounds as a clear');
  info({ stagedOrders: {} });
  fresh(); lane._test.tap(boxes[1].x + 5, boxes[1].y + 5);
  check(same(names(), ['orderSet']), 'tapping a chip with nothing staged sounds as a set');
  // a lane
  calls2.length = 0; fresh();
  const [lx, ly] = laneTop(0);
  const hl = lane._test.tap(lx, ly);
  check(hl && hl.kind === 'lane' && hl.lane === 0 && st.selected === 0 && calls2.length === 1 && calls2[0][0] === 'OnLaneTap' && calls2[0][1] === 0 && same(names(), ['tap']), 'a tap on another lane picks it (a tap sound, the page is told)');
  info({ selected: 0 });
  // putting the lane down by its number
  const cy0 = JSON.parse(layout).laneCentreY[0], bx = st.vertical ? cy0 * st.v : 12.2 * st.u, by = st.vertical ? (180 - 12.2) * st.u : cy0 * st.v;
  calls2.length = 0;
  const hd = lane._test.tap(bx, by);
  check(hd && hd.kind === 'deselect' && st.selected === -1 && calls2.length === 1 && calls2[0][1] === -1, "a tap on the picked lane's number puts it down (the page is told -1)");
  // no lane picked: where a chip would be, a tap picks the lane and does not set an order
  info({ selected: -1 });
  const ghost = lane._test.menu(1)[2];
  calls2.length = 0;
  const hn = lane._test.tap(ghost.x + ghost.w / 2, ghost.y + ghost.h / 2);
  check(hn && hn.kind === 'lane' && !calls2.some((c) => c[0] === 'OnOrderTap'), 'with no lane picked the chips are not there: that tap picks a lane and sets no order');
  // what is DRAWN: the five chips show on the picked lane in a pause, and nowhere else
  const rr = (o) => { info(o); const c0 = counts.roundRect || 0; lane._test.draw(2); return (counts.roundRect || 0) - c0; };
  check(rr({ selected: 1 }) - rr({ selected: -1 }) === 5, 'a picked lane in a pause is drawn with five order chips more than no lane');
  check(rr({ stage: 1, selected: 1 }) - rr({ stage: 1, selected: -1 }) === 0 && rr({ human: false, selected: 1 }) - rr({ human: false, selected: -1 }) === 0, 'no chips are drawn in the run or at a bots-only table');
  // the run and the bots: no picking, no orders
  for (const [label, o] of [['the run', { stage: 1, selected: 1 }], ['a bots-only table', { human: false, selected: 1 }]]) {
    info(o); calls2.length = 0;
    const b2 = lane._test.menu(1)[1];
    const hr = lane._test.tap(b2.x + b2.w / 2, b2.y + b2.h / 2), hr2 = lane._test.tap(lx, ly);
    check(hr === null && hr2 === null && calls2.length === 0, `during ${label} a tap on the board picks no lane and sets no order`);
  }
  // the mute button
  info({});
  const mb = lane._test.muteBox();
  check(mb.h >= 44 && mb.w >= 44 && mb.x + mb.w <= st.cssW && mb.y >= 0, `the Sound button is on the board in the corner and at least 44 px (${mb.w} x ${mb.h})`);
  const played0 = A.played, vox0 = AU.voices;
  const hm = lane._test.tap(mb.x + 5, mb.y + 5);
  check(hm && hm.kind === 'mute' && A.muted === true && store.get('doctrine-sound') === 'off' && A.played === played0, 'Sound button: muted, remembered, and the tap itself makes no sound');
  // everything is silent while muted
  const mute0 = AU.voices;
  info({ left: 2 }); info({ left: 1 }); info({ selected: 1, left: 30 });
  lane._test.tap(lx, ly); lane._test.tap(boxes[0].x + 5, boxes[0].y + 5); lane.sfx('stage'); lane.sfx('tap');
  feed((l) => { l[0].hp = 0.1; l[3].hp = 0.1; l.pop(); });
  pB = 0.2; feed(() => {}); pB = 0; eB = 0; feed(() => {}, 2, 1); pB = 1; eB = 1; feed(() => {}, 1);
  lane._test.sfx.batch(EVENTS, 77, 5000000);
  check(AU.voices === mute0 && A.played === played0, `muted: nothing at all is made (ticks, taps, page sounds, fights, castles, jingles, 16 events at once): ${AU.voices - mute0} voices`);
  lane._test.tap(mb.x + 5, mb.y + 5);
  fresh();
  check(A.muted === false && store.get('doctrine-sound') === 'on', 'Sound button again: sound is back and remembered');
  const v1 = AU.voices; fresh(); lane._test.sfx.play('stage', 8000000);
  check(AU.voices > v1, 'unmuted: sounds are made again');
  // the page asks for a sound by name; an unknown name is ignored
  check(lane._test.sfx.play('nothing', 9000000) === false, 'an unknown sound name is ignored');

  cv.getBoundingClientRect = () => ({ left: 0, top: 0 });
  lane.dispose();
  check(lane._test.state() === null, 'dispose clears the state');
  check(docListeners.length === 0 && AU.closed >= 1, 'dispose takes the page listeners off and closes the audio');
}

console.log('== sound at start-up');
{
  // a remembered mute is honoured before the first tap, and a page with no storage still works
  store.set('doctrine-sound', 'off');
  const ref = { invokeMethodAsync: async () => null };
  await lane.init(canvas(390), ref, layout, '/tools/doctrine/img/');
  let A = lane._test.sfx.audio;
  check(A.muted === true, 'a remembered "off" is read at start (muted from the first moment)');
  lane._test.sfx.gesture(); const v = AU.voices; lane._test.sfx.play('tap', 1);
  check(AU.voices === v, 'muted from the start: the first tap starts the audio but no sound is made');
  lane.dispose();
  store.set('doctrine-sound', 'on');
  await lane.init(canvas(390), ref, layout, '/tools/doctrine/img/');
  A = lane._test.sfx.audio;
  check(A.muted === false, 'a remembered "on" starts with sound');
  lane.dispose();
  globalThis.localStorage = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
  let ok = true;
  try {
    await lane.init(canvas(390), ref, layout, '/tools/doctrine/img/');
    A = lane._test.sfx.audio;
    lane._test.sfx.gesture();
    lane.setInfo(JSON.stringify({ selected: 1, stage: 0, human: true, left: 30, menu: MENU }));
    const mb = lane._test.muteBox(); lane._test.tap(mb.x + 5, mb.y + 5);
    check(A.muted === true, 'with storage blocked the Sound button still works (for this visit)');
    lane._test.tap(mb.x + 5, mb.y + 5);
    check(A.muted === false, 'and switches back');
  } catch (e) { ok = false; console.log(e); }
  check(ok, 'with storage blocked nothing throws');
  lane.dispose();
  delete globalThis.AudioContext;
  try {
    await lane.init(canvas(390), ref, layout, '/tools/doctrine/img/');
    lane._test.sfx.gesture(); lane._test.sfx.play('tap', 1);
    check(true, 'a browser with no Web Audio: the game goes on, silently');
  } catch (e) { check(false, 'no Web Audio must not throw: ' + e); }
  lane.dispose();
  globalThis.AudioContext = FakeAudioContext; globalThis.localStorage = goodStorage;
}
console.log('stub context calls: ' + calls);
fs.rmSync(tmp, { recursive: true, force: true });
console.log(fail === 0 ? 'SMOKE PASS' : fail + ' FAILED');
process.exit(fail === 0 ? 0 : 1);
