// Builds data/diagrams/holodb-scaling.json (the data behind the "scaling curve" diagram and the Scaling evidence section) from the
// owner's two recorded runs. Nothing is typed: every point is parsed from the run files, every slope is re-fitted here from the points
// (least squares of log10 time on log10 size) and checked against the slope the run printed.
//   node web/scripts/migrate/extract-holodb-scaling.mjs [scaling-run.txt] [pleromabench2-run.txt]
import fs from 'node:fs';
import path from 'node:path';
import { MONOREPO, DATA } from '../lib/data.mjs';

const gate = path.join(MONOREPO, 'todo', 'gate-2026-10-15');
const scalingFile = process.argv[2] || path.join(gate, 'run3-scaling.txt');
const benchFile = process.argv[3] || path.join(gate, 'run7-pleromabench2.txt');
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');

// ---- the scaling run: "  label   x:y  x:y ...  unit   slope s" ----
const lines = new Map();
for (const raw of read(scalingFile).split('\n')) {
  const m = raw.match(/^\s*(.*?)\s+((?:\d+:\d+\s+)+)(us|records|passes)\s+slope\s+(\S+)\s*$/);
  if (!m) continue;
  const label = m[1].replace(/\s+/g, ' ').trim();
  const points = m[2].trim().split(/\s+/).map((p) => { const [x, y] = p.split(':').map(Number); return { x, y }; });
  lines.set(label, { points, unit: m[3], recorded: m[4] === 'NaN' ? null : Number(m[4]) });
}
const fit = (pts) => {
  const X = pts.map((p) => Math.log10(p.x)), Y = pts.map((p) => Math.log10(p.y));
  const n = pts.length, mx = X.reduce((a, b) => a + b) / n, my = Y.reduce((a, b) => a + b) / n;
  const sxx = X.reduce((a, x) => a + (x - mx) ** 2, 0);
  return sxx === 0 ? 0 : X.reduce((a, x, i) => a + (x - mx) * (Y[i] - my), 0) / sxx;
};
const r2 = (v) => Math.round(v * 100) / 100;

function series(id, label, role, key, kind) {
  const l = lines.get(key);
  if (!l) throw new Error('line not found in the scaling run: ' + key);
  const f = fit(l.points);
  if (l.recorded != null && Math.abs(f - l.recorded) > 0.011) throw new Error(`slope for "${key}": recorded ${l.recorded}, re-fitted ${f.toFixed(3)}`);
  // a time slope under 0.4 sits inside the noise of the run's own null, so it is shown as nearly flat and never as an exact exponent
  return { id, label, role, points: l.points, slope: r2(f), nearlyFlat: kind === 'time' && f < 0.4 };
}
const nul = lines.get('null: fixed work, size ignored');
if (!nul) throw new Error('null line not found');

// ---- the write-then-read pairs from the benchmark run ----
const bench = read(benchFile);
const toUs = (v, u) => Number(v) * (u === 'ms' ? 1000 : 1);
const pairs = [], placebo = [];
let rows = null;
for (const raw of bench.split('\n')) {
  const h = raw.match(/^=== ([\d,]+) rows ===/);
  if (h) { rows = Number(h[1].replace(/,/g, '')); continue; }
  // the placebo pair runs a query the plane declines, so both arms take the same path: its ratio must sit near 1.00
  const pb = raw.match(/^WRITE\+READ PLACEBO \(1 INSERT then C17, x\d+\): .*\(x([\d.]+) engine\/plane; analytic null 1\.00, valid in \[([\d.]+), ([\d.]+)\]\)/);
  if (pb && rows) { placebo.push({ x: rows, ratio: Number(pb[1]), low: Number(pb[2]), high: Number(pb[3]) }); continue; }
  const m = raw.match(/^WRITE\+READ \(1 INSERT then C04 GROUP BY 2 cols, x(\d+)\): engine ([\d.]+) (ms|us)\/pair\s+plane ([\d.]+) (ms|us)\/pair/);
  if (m && rows) pairs.push({ x: rows, repeats: Number(m[1]), engine: toUs(m[2], m[3]), plane: toUs(m[4], m[5]) });
}
if (pairs.length !== 3) throw new Error('expected 3 write-then-read lines, found ' + pairs.length);
if (placebo.length !== 3) throw new Error('expected 3 placebo lines, found ' + placebo.length);
if (placebo.some((p) => p.ratio < p.low || p.ratio > p.high)) throw new Error('a write-then-read placebo is outside its band: the instrument is biased');
const wr = (key) => { const pts = pairs.map((p) => ({ x: p.x, y: p[key] })); return { points: pts, slope: r2(fit(pts)) }; };
const wrEngine = wr('engine'), wrPlane = wr('plane');

const data = {
  schema: 'ea-diagram/1', kind: 'scaling-curve', id: 'holodb-scaling',
  source: { scaling: 'todo/gate-2026-10-15/run3-scaling.txt', writeThenRead: 'todo/gate-2026-10-15/run7-pleromabench2.txt' },
  null: { label: 'Fixed work, size ignored', points: nul.points, slope: r2(fit(nul.points)), firstPointIsStartUp: nul.points[0].y > 3 * Math.max(...nul.points.slice(1).map((p) => p.y)) },
  repeats: { writeThenRead: pairs[0].repeats },
  placebo,
  panels: [
    {
      id: 'write-read', label: 'Write, then read', title: 'One insert, then one group-by',
      xLabel: 'rows in the table', xFmt: 'count',
      charts: [{
        id: 'write-read-time', yLabel: 'time per pair', yKind: 'time',
        series: [
          { id: 'engine', label: 'engine alone', role: 'base', points: wrEngine.points, slope: wrEngine.slope, nearlyFlat: wrEngine.slope < 0.4 },
          { id: 'plane', label: 'with the plane', role: 'new', points: wrPlane.points, slope: wrPlane.slope, nearlyFlat: wrPlane.slope < 0.4, flat: true },
        ],
      }],
    },
    {
      id: 'batch', label: 'Changes in a batch', title: 'B changes applied to a table',
      xLabel: 'changes in the batch', xFmt: 'count',
      charts: [
        { id: 'batch-time', yLabel: 'time', yKind: 'time', series: [
          series('sequential', 'B statements, one at a time', 'base', 'S1 B sequential statements (time)', 'time'),
          series('apply', 'one ApplyChanges call', 'new', 'S1 B in one ApplyChanges (time)', 'time'),
        ] },
        { id: 'batch-commits', yLabel: 'commit groups written', yKind: 'count', series: [
          series('sequential', 'B statements, one at a time', 'base', 'S1 WAL commit groups, sequential', 'count'),
          series('apply', 'one ApplyChanges call', 'new', 'S1 WAL commit groups, ApplyChanges', 'count'),
        ] },
      ],
    },
    {
      id: 'flags', label: 'Flag words', title: 'k flag words added to a plane',
      xLabel: 'flag words added', xFmt: 'count',
      charts: [{ id: 'flags-time', yLabel: 'time', yKind: 'time', series: [
        series('adds', 'k separate adds', 'base', 'S2 k separate Add calls', 'time'),
        series('words', 'one AddFlagWords call', 'new', 'S2 one AddFlagWords', 'time'),
      ] }],
    },
    {
      id: 'measures', label: 'Measures in a group-by', title: 'A group-by that sums M measures',
      xLabel: 'measures summed', xFmt: 'count',
      charts: [
        { id: 'measures-passes', yLabel: 'transform passes per query', yKind: 'count', series: [
          series('per-plane', 'one read per plane', 'base', 'S3 projected counted passes per query, per-plane Flags', 'count'),
          series('many', 'one multi-plane read', 'new', 'S3 projected counted passes per query, FlagsMany', 'count'),
        ] },
        { id: 'measures-time', yLabel: 'time per query', yKind: 'time', series: [
          series('per-plane', 'one read per plane', 'base', 'S3 projected (6 of 9 bits) GROUP BY, M measures, per-plane Flags (time)', 'time'),
          series('many', 'one multi-plane read', 'new', 'S3 projected (6 of 9 bits) GROUP BY, M measures, FlagsMany (time)', 'time'),
        ] },
      ],
      resultBuild: series('result', 'result build only, repeat with no transform', 'other', 'S5 memo-hit repeat (no transform): result build only', 'time'),
    },
    {
      id: 'groupbys', label: 'Group-bys in one refresh', title: 'G group-bys refreshed together (four measures)',
      xLabel: 'group-bys in the refresh', xFmt: 'count',
      charts: [
        { id: 'groupbys-passes', yLabel: 'transform passes per refresh', yKind: 'count', series: [
          series('per-plane', 'one read per plane', 'base', 'S4 transform passes per refresh, per-plane Flags', 'count'),
          series('many', 'one multi-plane read', 'new', 'S4 transform passes per refresh, FlagsMany', 'count'),
        ] },
        { id: 'groupbys-time', yLabel: 'time per refresh', yKind: 'time', series: [
          series('per-plane', 'one read per plane', 'base', 'S4 G group-bys, M=4, per-plane Flags (time)', 'time'),
          series('many', 'one multi-plane read', 'new', 'S4 G group-bys, M=4, FlagsMany (time)', 'time'),
        ] },
      ],
    },
  ],
};

const out = path.join(DATA, 'diagrams', 'holodb-scaling.json');
fs.writeFileSync(out, JSON.stringify(data, null, 2) + '\n', 'utf8');
console.log('wrote', out, `${data.panels.length} panels, null slope ${data.null.slope}, write-then-read engine ${wrEngine.slope} plane ${wrPlane.slope}`);
