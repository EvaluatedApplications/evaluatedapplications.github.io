// Copies the Phasor owner's current data into the site's diagram data. Nothing is typed here: every number on the Phasor pages is
// read from these files, which are read from Phasor/docs.
//   node web/scripts/migrate/extract-superposition.mjs
// Writes  data/diagrams/pleroma-map.json            the pyramid (rungs, cells, evidence), without the one cell that is not built
//         data/diagrams/superposition-scene.json    the worked example, the kernel, the one pass and the measured cost
//         data/diagrams/coverage-meter.json the coverage meter (headline, class counts, operations of the three scored classes, reads, bricks)
//         data/generated/phasor-ladder.json         the rungs as a ladder table, parsed from site.md
import fs from 'node:fs';
import path from 'node:path';
import { MONOREPO, DATA, readJson } from '../lib/data.mjs';

const docs = path.join(MONOREPO, 'Phasor', 'docs');
const map = readJson(path.join(docs, 'pleroma-map.json'));
const cov = readJson(path.join(docs, 'superposition-coverage.json'));
const siteMd = fs.readFileSync(path.join(docs, 'site.md'), 'utf8').replace(/\r\n/g, '\n');
const versions = readJson(path.join(DATA, 'generated', 'versions.json'));
const pkg = { id: 'EvaluatedApplications.Phasor', version: versions.packages['EvaluatedApplications.Phasor'].version };
const write = (rel, o) => { const f = path.join(DATA, rel); fs.mkdirSync(path.dirname(f), { recursive: true }); fs.writeFileSync(f, JSON.stringify(o) + '\n', 'utf8'); console.log('wrote', rel, fs.statSync(f).size, 'bytes'); };

// ---- the pyramid ----
const built = (c) => c.status !== 'NOT BUILT';
const rungs = map.pyramid.rungs.map((r) => ({
  n: r.rungIndex, name: r.name, caption: r.caption, description: r.description,
  cells: r.cells.filter(built).map((c) => ({ id: c.id, face: c.face, name: c.name, description: c.description, api: c.api, status: c.status, evidence: c.evidence })),
}));
const notBuilt = map.pyramid.rungs.flatMap((r) => r.cells).filter((c) => !built(c)).map((c) => ({ id: c.id, name: c.name, rung: c.id.match(/^R(\d)/)[1] }));
const lid = map.lid.cells[0];
const pyramid = {
  schema: 'ea-diagram/1', kind: 'pleroma-map', package: pkg, title: map.pyramid.title, totalCells: rungs.reduce((a, r) => a + r.cells.length, 0),
  faces: ['Syzygy', 'Monad', 'Ouroboros'],
  faceNotes: { Syzygy: 'the key cycle and its signed counts', Monad: 'the order face, S_k', Ouroboros: 'the flag cube, Z_2^m' },
  rungs, notBuilt,
  lid: { id: lid.id, name: map.lid.name, description: map.lid.description, cell: { id: lid.id, name: lid.name, description: lid.description, api: lid.api, status: lid.status, evidence: lid.evidence } },
};
write('diagrams/pleroma-map.json', pyramid);

// ---- the superposition scene: only numbers from the owner's map file ----
const sp = map.superposition, br = map.diagramBrief, ka = map.measurements.kernelApply;
const stack = br.panels.find((p) => p.id === 'stack'), kern = br.panels.find((p) => p.id === 'kernel'), pass = br.panels.find((p) => p.id === 'pass'), cost = br.panels.find((p) => p.id === 'cost');
const harm = Number(kern.structure.match(/(\d+) numbers per cell/)[1]);
const elementsMax = Number(cost.structure.match(/to ([\d,]+) kernel elements/)[1].replace(/,/g, ''));
const applyRange = cost.applyMs.split(' to ').map(Number);
const num = (re) => { const m = sp.codeExample.code.match(re); if (!m) throw new Error('shape value not found in the code example: ' + re); return Number(m[1]); };
const plainMd = fs.readFileSync(path.join(docs, 'pleroma-site.md'), 'utf8');
if (!/mean of 20 calls after one warm-up/.test(plainMd) || !/random kernels/.test(siteMd)) throw new Error('timing conditions not found in the owner docs');
const timing = 'Random kernels, the mean of 20 calls after one warm-up.';
const scene = {
  schema: 'ea-diagram/1', kind: 'superposition-scene', package: pkg, source: 'Phasor/docs/pleroma-map.json (superposition, kernelJoins, measurements.kernelApply, diagramBrief)',
  shape: { keyCapacity: num(/KeyCapacity = (\d+)/), flagBits: num(/FlagBits = (\d+)/), timePeriod: num(/TimePeriod = (\d+)/) },
  store: { capacity: sp.initialStore.capacity, cellCount: sp.initialStore.cellCount, description: sp.initialStore.description, items: sp.initialStore.items, totalCount: sp.initialStore.totalCount },
  reads: { flags: sp.reads.flagSlice, times: sp.reads.timeSlice },
  kernel: { elements: sp.operation.kernel, description: sp.operation.description, cost: sp.operation.cost, numbersPerCell: harm, joins: map.kernelJoins.joins, doesNotJoin: map.kernelJoins.doesNotJoin, rule: map.kernelJoins.rule },
  result: { totalCount: sp.resultCounts.totalCount, description: sp.resultCounts.description, flags: sp.resultCounts.flagCounts, times: sp.resultCounts.timeCounts },
  capacityRule: sp.capacityRule,
  cost: { applyMs: applyRange, elementsMin: 1, elementsMax, bruteForceMs: cost.bruteForceMs, conditions: ka.description + ' ' + timing, source: ka.source, applyText: ka.applyMs, bruteText: ka.bruteForceMs },
  code: sp.codeExample.code,
};
write('diagrams/superposition-scene.json', scene);

// ---- coverage meter ----
const ops = cov.operations;
const keep = (o) => ({ name: o.name, face: o.face, rung: o.rung || '', evidence: o.evidence });
const classes = ['kernel', 'pass-per-term', 'write'].map((c) => ({ id: c, count: ops.filter((o) => o.class === c).length, meaning: cov.classes[c], operations: ops.filter((o) => o.class === c).map(keep) }));
const reads = ops.filter((o) => o.class === 'read').map(keep);
const coverage = {
  schema: 'ea-diagram/1', kind: 'coverage-meter', package: pkg, source: 'Phasor/docs/superposition-coverage.json',
  headline: cov.headline, summary: cov.summary, classes, reads, readsMeaning: cov.classes.read,
  plumbing: { count: ops.filter((o) => o.class === 'plumbing').length, meaning: cov.classes.plumbing },
  notBuilt: ops.filter((o) => o.class === 'not-built').map(keep),
  construction: cov.construction.map((b) => ({ brick: b.brick, alone: b.alone, pleroma: b.pleroma, proof: b.proof })),
};
const sum = classes.reduce((a, c) => a + c.count, 0);
if (sum !== cov.summary.operatorCount || classes[0].count !== cov.summary.kernelCount || reads.length !== cov.summary.readCount) throw new Error('coverage counts disagree with the operations list');
write('diagrams/coverage-meter.json', coverage);

// ---- the ladder table (rungs x what one pass can do), parsed from the owner's table ----
const sec = siteMd.slice(siteMd.indexOf('## The rungs as the ladder'));
const rows = sec.split('\n').filter((l) => /^\| (\d|Lid)/.test(l)).map((l) => l.split('|').slice(1, -1).map((x) => x.trim()));
const ladder = rows.map(([rung, can, superposed, operator, proof]) => ({ rung, can, superposed, operator, checked: proof.replace(/^covered( for)?\s*/, '').replace(/^\((.*)\)$/, '$1') }));
if (ladder.length !== 9) throw new Error('expected 8 rungs and the lid in the ladder table, found ' + ladder.length);
write('generated/phasor-ladder.json', { schema: 'ea-ladder/1', source: 'Phasor/docs/site.md', rungs: ladder });
