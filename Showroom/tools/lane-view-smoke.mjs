// Offline smoke test of wwwroot/doctrine/lane.js (no browser): runs the whole canvas script against a stub 2D context and a synthetic packet stream, in the wide and the
// narrow (vertical) layout, and fails on any exception or on a missing effect. It proves the script RUNS and keeps its state straight; it cannot show what the drawing LOOKS like.
// Usage: node tools/lane-view-smoke.mjs   (from the Showroom folder)
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const src = fs.readFileSync(new URL('../wwwroot/doctrine/lane.js', import.meta.url), 'utf8');
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'lane-smoke-'));
const file = path.join(tmp, 'lane.mjs');
fs.writeFileSync(file, src);

let calls = 0;
const ctx = () => new Proxy({}, {
  get: (t, k) => (k in t ? t[k] : k === 'measureText' ? () => ({ width: 30 }) : typeof k === 'string' ? (...a) => { calls++; } : undefined),
  set: (t, k, v) => { t[k] = v; return true; },
});
function canvas(parentWidth) {
  return { width: 0, height: 0, style: {}, getContext: () => ctx(), toDataURL: () => 'data:image/png;base64,AA==', addEventListener() {}, removeEventListener() {},
    getBoundingClientRect: () => ({ left: 0, top: 0 }), parentElement: { clientWidth: parentWidth } };
}
let rafCb = null;
globalThis.window = globalThis; globalThis.innerHeight = 800; globalThis.devicePixelRatio = 2;
globalThis.addEventListener = () => {}; globalThis.removeEventListener = () => {};
globalThis.document = { createElement: () => canvas(100) };
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

function packet(bodies, stage = 1, pBase = 1, eBase = 1) {
  const f = new Float32Array(8 + bodies.length * 10);
  f.set([100, stage, 0, pBase, eBase, bodies.length, 0.5, 0], 0);
  bodies.forEach((b, i) => f.set([b.id, b.side, b.lane, b.kind, b.x, b.y, b.side ? -1 : 1, 0, b.hp ?? 1, b.fight ? 1 : 0], 8 + i * 10));
  return new Uint8Array(f.buffer);
}

for (const [name, width] of [['wide', 1100], ['narrow (vertical)', 390]]) {
  console.log('== ' + name);
  const frames = []; let tapped = -1;
  const bodies = Array.from({ length: 16 }, (_, i) => ({ id: i, side: i % 2, lane: i % 3, kind: i % 8, x: 20 + i * 8, y: 8 + (i % 3) * 18, hp: 1 }));
  let next = packet(bodies);
  const ref = { invokeMethodAsync: async (m, a) => { if (m === 'Frame') { frames.push(a); const p = next; next = null; return p; } if (m === 'OnLaneTap') tapped = a; } };
  const cv = canvas(width);
  await lane.init(cv, ref, layout, '/tools/doctrine/img/');
  const st = lane._test.state();
  check(st.vertical === (width < 720) && st.cssW === width && st.cssH > 300, `layout fits (vertical ${st.vertical}, ${st.cssW} x ${st.cssH})`);
  check(lane.icons().length === 8, 'eight soldier pictures are made');
  lane.setInfo(JSON.stringify({ selected: 1, stage: 0, orders: [[0, 1, 3], [5, 4, 0]], stagedPacks: [{ lane: 1, kind: 3, count: 4 }, { lane: 1, kind: 7, count: 4 }], stagedOrders: { 2: 3 } }));
  let now = 1000;
  const tick = async () => { now += 16; rafCb(now); await new Promise((r) => setTimeout(r, 0)); };
  await tick(); await tick();
  check(st.bodies.size === 16 && frames.length >= 1, `the first packet put 16 bodies on the field (${st.bodies.size})`);
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
  cv.getBoundingClientRect = () => ({ left: 0, top: 0 });
  lane.dispose();
  check(lane._test.state() === null, 'dispose clears the state');
}
console.log('stub context calls: ' + calls);
fs.rmSync(tmp, { recursive: true, force: true });
console.log(fail === 0 ? 'SMOKE PASS' : fail + ' FAILED');
process.exit(fail === 0 ? 0 : 1);
