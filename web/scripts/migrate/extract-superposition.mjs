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
// The page says one thing about reads and read sets: how many answer from the array in one pass, and that nine of them are answered through a
// read set. The owner's longer notes on routes and planners (headline.reads after "Of them", readsInOnePassNote) are not carried over.
const readsCut = cov.headline.reads.indexOf(' Of them, ');
if (readsCut < 0) throw new Error('headline.reads no longer has the "Of them," sentence this script cuts at');
const NUM = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen', 'twenty'];
const nInSet = cov.headline.readsInOnePass;
if (nInSet !== cov.commutation.readsInOneReadSet) throw new Error('the count of reads answered through a read set differs between the headline and the commutation block');
const headline = { ...cov.headline, reads: cov.headline.reads.slice(0, readsCut) + ` ${NUM[nInSet][0].toUpperCase() + NUM[nInSet].slice(1)} of them are answered through a read set.` };
delete headline.readsInOnePassNote;
const coverage = {
  schema: 'ea-diagram/1', kind: 'coverage-meter', package: pkg, source: 'Phasor/docs/superposition-coverage.json',
  headline, summary: cov.summary, classes, reads, readsMeaning: cov.classes.read,
  plumbing: { count: ops.filter((o) => o.class === 'plumbing').length, meaning: cov.classes.plumbing },
  notBuilt: ops.filter((o) => o.class === 'not-built').map(keep),
  construction: cov.construction.map((b) => ({ brick: b.brick, alone: b.alone, pleroma: b.pleroma, proof: b.proof })),
};
const sum = classes.reduce((a, c) => a + c.count, 0);
if (sum !== cov.summary.operatorCount || classes[0].count !== cov.summary.kernelCount || reads.length !== cov.summary.readCount) throw new Error('coverage counts disagree with the operations list');
write('diagrams/coverage-meter.json', coverage);

// ---- the commutation map: how each of the 89 operations moves when the store is moved ----
// Classes come from the coverage record (each operation's commutation.class, probe-backed rows taken by the owner's tests from
// superposition-classification.json); the generators and the per-generator "breaks" counts are read from the classification file.
const cls = readJson(path.join(docs, 'superposition-classification.json'));
const com = cov.commutation;
const members = ops.filter((o) => o.commutation);
if (members.length !== com.members) throw new Error(`commutation members: ${members.length} operations carry a class, the record says ${com.members}`);
// "joins" splits by the owner's definition: it commutes with the group AND lies on an axis the kernel carries, which the record shows as a
// kernel operation, a store sum (Add or Subtract of another store) or a read answered through a read set. The other commuting operations,
// Hadamard and the key shift among them, lie off those axes (a key shift joins them only through a keyed kernel).
const isStoreSum = (o) => o.class === 'write' && /^(Add|Subtract)\(Pleroma\)$/.test(o.name);
const onAxes = (o) => o.commutation.class === 'joins' && (o.class === 'kernel' || isStoreSum(o) || o.readSet === 'joined');
const how = (o) => (o.class === 'kernel' ? 'kernel operation' : isStoreSum(o) ? 'store sum' : o.readSet === 'joined' ? 'read through a read set' : o.class === 'read' ? 'read' : 'operator');
const note = (o) => (o.commutation.note || o.commutation.basis || '').replace(/^probe: /, 'probed as ').replace(/^not probed$/, '').replace(/\s*\(op-\d+,\s*EXP-\w+\)/g, '').replace(/,\s*EXP-\w+\)/g, ')').trim(); // internal ids stay out of public prose
const pick = (f) => members.filter(f).map((o) => ({ name: o.name, class: o.class, how: how(o), face: o.face, note: note(o) }));
const GROUPS = [
  { id: 'joins-axes', label: "Join the kernel's axes", meaning: 'Commute with every generator of the group and lie on an axis the kernel carries: the flag face, the left arrangement or the tick.', f: onAxes },
  { id: 'joins-off', label: 'Commute, off the axes', meaning: 'Commute with every generator but do not lie on an axis the kernel carries, such as the key shift of an unkeyed kernel.', f: (o) => o.commutation.class === 'joins' && !onAxes(o) },
  { id: 'linear', label: 'Linear, break a symmetry', meaning: 'Linear or affine, and break at least one named generator: a bit permutation of the flags, a key scaling, a Frobenius squaring, a bind.', f: (o) => o.commutation.class === 'linear-not-commuting' },
  { id: 'nonlinear', label: 'Not linear', meaning: 'Choices, decodes, order statistics, draws and predicates.', f: (o) => o.commutation.class === 'non-linear' },
  { id: 'declare', label: 'Need a declaration', meaning: 'Need an output-action declaration before a probe can classify them. The record gives the reason for each.', f: (o) => o.commutation.class === 'needs-declaration' },
].map((g) => ({ id: g.id, label: g.label, meaning: g.meaning, operations: pick(g.f) })).map((g) => ({ ...g, count: g.operations.length }));
if (GROUPS.reduce((a, g) => a + g.count, 0) !== com.members) throw new Error('commutation groups do not add up to the members');
const byCount = (id) => GROUPS.find((g) => g.id === id).count;
if (byCount('joins-axes') + byCount('joins-off') !== com.classCounts.joins || byCount('linear') !== com.classCounts['linear-not-commuting'] || byCount('nonlinear') !== com.classCounts['non-linear'] || byCount('declare') !== com.classCounts['needs-declaration']) throw new Error('commutation groups differ from the record\'s class counts');
// the generators, parsed from the classification file's own scope line
const genText = cls.scope.replace(/^.*?Generators:\s*/, '').replace(/\.$/, '').split(';').map((s) => s.trim());
const generators = genText.map((s) => { const m = s.match(/^(.+?) \((.+)\)$/); if (!m) throw new Error('generator group not understood: ' + s); return m[1].split(',').map((x) => x.trim()).map((id) => ({ id, group: m[2] })); }).flat();
const movesOf = (id) => {
  let m;
  if ((m = id.match(/^key(\d+)$/))) return `shift every key by ${m[1]}`;
  if ((m = id.match(/^flag(\d+)$/))) return `XOR every flag word with mask ${m[1]}`;
  if ((m = id.match(/^arrR(\d+)$/))) return `relabel the arrangements from the right, generator ${m[1]}`;
  if ((m = id.match(/^arrL(\d+)$/))) return `relabel the arrangements from the left, generator ${m[1]}`;
  if ((m = id.match(/^time(\d+)$/))) return `advance every tick by ${m[1]}`;
  throw new Error('generator not understood: ' + id);
};
for (const g of generators) { g.moves = movesOf(g.id); delete g.group; }
for (const g of generators) g.breaks =cls.probes.filter((p) => p.commutes && p.commutes[g.id] === 'breaks').length;
const probeIds = new Set(cls.probes.flatMap((p) => Object.keys(p.commutes || {})));
if (generators.some((g) => !probeIds.has(g.id))) throw new Error('a parsed generator is not a key of the probe rows');
// the owner's prose and the data must say the same numbers (the pages quote these)
const word = (n) => NUM[n][0].toUpperCase() + NUM[n].slice(1);
if (!siteMd.includes(`${com.classCounts.joins} commute with every generator of the group. ${word(byCount('joins-axes'))} of them join the kernel's axes.`)) throw new Error('site.md no longer states the commutation split this data produces');
const commutation = {
  schema: 'ea-diagram/1', kind: 'commutation-map', package: pkg, source: 'Phasor/docs/superposition-coverage.json (commutation) and Phasor/docs/superposition-classification.json',
  members: com.members, commuting: com.classCounts.joins, probeBacked: com.probeBacked, notProbed: com.notProbed, probeRows: com.probeRows,
  probeSummary: cls.summary, groups: GROUPS, generators,
};
write('diagrams/commutation-map.json', commutation);

// ---- the ladder table (rungs x what one pass can do), parsed from the owner's table ----
const sec = siteMd.slice(siteMd.indexOf('## The rungs as the ladder'));
const rows = sec.split('\n').filter((l) => /^\| (\d|Lid)/.test(l)).map((l) => l.split('|').slice(1, -1).map((x) => x.trim()));
const ladder = rows.map(([rung, can, superposed, operator, proof]) => ({ rung, can, superposed, operator, checked: proof.replace(/^covered( for)?\s*/, '').replace(/^\((.*)\)$/, '$1') }));
if (ladder.length !== 9) throw new Error('expected 8 rungs and the lid in the ladder table, found ' + ladder.length);
write('generated/phasor-ladder.json', { schema: 'ea-ladder/1', source: 'Phasor/docs/site.md', rungs: ladder });
