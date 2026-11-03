#!/usr/bin/env node
// Derives the evidence records for HoloVoxel, Prose and HoloGenome from the diagram data that the generators in
// web/generators wrote from the PUBLISHED packages, so every number on an evidence page is read from a recording and none is typed.
//   node web/scripts/derive-evidence.mjs
// Re-run it after any generator is re-run. The records land in web/data/evidence/ next to the hand-kept ones.
import fs from 'node:fs';
import path from 'node:path';
import { DATA, readJson } from './lib/data.mjs';
import * as vox from '../src/assets/diagrams/holovoxel-lod.js';
import * as prose from '../src/assets/diagrams/prose-pipeline.js';
import * as hg from '../src/assets/diagrams/hologenome-scores.js';

const out = (name, rec) => { fs.writeFileSync(path.join(DATA, 'evidence', name + '.json'), JSON.stringify(rec, null, 2) + '\n', 'utf8'); console.log('wrote evidence/' + name + '.json'); };
const n0 = (n) => Number(n).toLocaleString('en-GB');
const ratio = (a, b) => { const r = a / b; return r >= 10 ? `${Math.round(r)}` : r.toFixed(1); };
const dg = (n) => readJson(path.join(DATA, 'diagrams', n + '.json'));

// ---------------------------------------------------------------- HoloVoxel: accuracy against component budget
{
  const d = dg('holovoxel-lod'), m = vox.metrics(d), lad = m.ladder, lo = lad[0], hi = lad[lad.length - 1];
  out('holovoxel-lod', {
    id: 'holovoxel-lod', product: 'holovoxel', slug: '/holovoxel/evidence/', scope: 'One chunk decoded at several budgets',
    title: 'Detail against components read',
    description: `One chunk stored as a single hologram decodes ${hi.accuracy.toFixed(1)} percent correct at the full budget, and the hologram read shrinks as the budget falls.`,
    claim: `One 16 by 16 chunk stored as a single hologram decodes ${hi.accuracy.toFixed(1)} percent of its cells correctly at the full budget of ${n0(hi.components)} components and ${n0(hi.storedBytes)} bytes. Reading fewer components is smaller and fuzzier: ${lad.find((r) => r.components === m.levels[1].components).accuracy.toFixed(1)} percent at ${n0(m.levels[1].components)} components, and ${lo.accuracy.toFixed(1)} percent at ${n0(lo.components)}, which takes ${n0(lo.storedBytes)} bytes.`,
    headline: [
      { value: `${hi.accuracy.toFixed(1)}%`, label: `of cells decoded correctly with all ${n0(hi.components)} components` },
      { value: `${ratio(hi.storedBytes, lo.storedBytes)} times`, label: `less hologram read at ${n0(lo.components)} components than at the full budget` },
    ],
    method: {
      workload: `A chunk of a fixed procedural world is built at full fidelity. For each component budget it is decoded as a colour field and checked cell by cell against the stored block (Chunk.Accuracy). The hologram size is the number of bytes CapacityProbe reports for that budget.`,
      engines: ['HoloVoxel Chunk.DecodeField at ten component budgets'],
      verification: 'Accuracy is the share of surface cells whose strongest decoded block equals the block that was stored.',
      statistic: 'Accuracy is one measurement of one chunk.',
      memory: 'Hologram size is sixteen bytes per component read.',
      tie: 'Eight blocks are possible, so a random guess is right 12.5 percent of the time.',
    },
    tables: [{
      id: 'ladder', kind: 'matrix', caption: 'One chunk, by component budget',
      columns: ['Components read', 'Cells correct (percent)', 'Hologram size (bytes)'],
      rows: lad.map((r) => [n0(r.components), r.accuracy.toFixed(1), n0(r.storedBytes)]),
    }],
    losses: [
      `Accuracy is low at small budgets by design: ${lo.accuracy.toFixed(1)} percent at ${n0(lo.components)} components, against 12.5 percent for a random guess. A far read is a soft blend, not a smaller copy of the near one.`,
    ],
    limits: [
      'One chunk of one world. Terrain with other block mixes will score differently.',
      'Accuracy is measured at full resolution for each budget. The scene on the product page also coarsens the grid with distance, which this table does not measure.',
    ],
    internal: { source: 'web/generators/holovoxel-lod, web/scripts/derive-evidence.mjs' },
  });
}

// ---------------------------------------------------------------- Prose: the validity gate
{
  const d = dg('prose-pipeline'), m = prose.metrics(d), b = m.bulk;
  out('prose-gate', {
    id: 'prose-gate', product: 'prose', slug: '/prose/evidence/', scope: 'Generated sentences re-checked by the package gate',
    title: 'Generated sentences pass the gate',
    description: `Of ${n0(b.requested)} sentences requested in one run, ${n0(b.produced)} different ones came back and every one passed the validity gate.`,
    claim: `Of ${n0(b.requested)} sentences requested in one run, ${n0(b.produced)} different ones came back. Every one passed the package's validity gate on a fresh parse, and every word in every one was observed in the corpus.`,
    headline: [
      { value: `${n0(b.gatePass)} of ${n0(b.produced)}`, label: 'generated sentences that passed the validity gate on re-parse' },
      { value: `${n0(b.attestedPass)} of ${n0(b.produced)}`, label: 'sentences in which every word was observed in the corpus' },
      { value: `${n0(b.distinct)} of ${n0(b.produced)}`, label: 'sentences that were different from every other' },
    ],
    method: {
      workload: `${n0(m.corpus.sentences)} sentences from ${m.corpus.books.length} public-domain novels (${m.corpus.books.join('; ')}) are mined, then ${n0(b.requested)} sentences are generated with a fixed seed.`,
      engines: ['Prose ProseEngine.Generate, checked with ValidityGate.Check and Attested.All'],
      verification: 'Every sentence is parsed again by the same rules that mined the text and must have a real subject and a finite verb, with subject and verb agreement and article and noun agreement. A separate check confirms every open-class word was observed in the corpus.',
      statistic: 'One run with one seed.',
      tie: 'The corpus is large enough for generation to use observed pairings only.',
    },
    tables: [{
      id: 'bulk', kind: 'matrix', caption: `${n0(b.requested)} sentences requested in one run`,
      columns: ['Measure', 'Count'],
      rows: [['Sentences requested', n0(b.requested)], ['Sentences returned', n0(b.produced)], ['Different from every other', n0(b.distinct)], ['Passed the validity gate', n0(b.gatePass)], ['Every word observed in the corpus', n0(b.attestedPass)]],
    }],
    losses: [
      b.produced < b.requested
        ? `The package returned ${n0(b.produced)} of the ${n0(b.requested)} requested sentences. It returns up to the number asked for and stops when it cannot find more different valid ones.`
        : 'The package returns up to the number asked for and stops when it cannot find more different valid sentences.',
    ],
    limits: [
      'The gate checks form, using the same rules that produced the sentences. It does not check meaning, and a valid sentence can still say something odd.',
      'One corpus, one seed.',
    ],
    internal: { source: 'web/generators/prose-pipeline, web/scripts/derive-evidence.mjs' },
  });
}

// ---------------------------------------------------------------- HoloGenome: how the vectors score against held-out measurements
{
  const d = dg('hologenome-scores'), m = hg.metrics(d), F = hg.limitFacts(m);
  const f2 = (v) => Number(v).toFixed(2), f3 = (v) => Number(v).toFixed(3);
  const ctl = m.views.find((v) => v.id === 'controls'), per = m.views.find((v) => v.id === 'period');
  out('hologenome-scores', {
    id: 'hologenome-scores', product: 'hologenome', slug: '/hologenome/evidence/', scope: 'Held-out Loop-seq bendability measurements',
    title: 'How HoloGenome scores against the lab',
    lossTitle: 'Where it did not win',
    description: `Measured helix twist scores r = ${F.rTwist} against held-out bendability measurements, against ${F.rConstant} for a constant twist.`,
    claim: `A linear readout trained on ${F.trainSet} and tested on ${F.testSet} (${F.overlap}) scores r = ${F.rTwist} with vectors that use the measured twist, against ${F.rConstant} with one constant twist (difference ${F.dTwist}, 95% interval ${F.dTwistLo} to ${F.dTwistHi}). Adding the local-shape layer lifts it to ${F.rShape}, a further ${F.dShape}. A small phasor model that chose its own period, starting from ${F.startPeriod} bases, settled at ${F.periods} bases per turn on four independent libraries, against the crystallographic ${F.periodRef}.`,
    headline: [
      { value: `${F.periodLo} to ${F.periodHi}`, label: `bases per turn found by a small model that was never told the helical period, on four independent libraries (the crystallographic value is ${F.periodRef})` },
      { value: `${F.rTwist} vs ${F.rConstant}`, label: 'correlation with held-out bendability using the measured twist of each base pair step, against one constant twist' },
      { value: F.rShape, label: `correlation with held-out bendability once the local-shape layer is added, against ${F.rWords} for counts of short words` },
    ],
    method: {
      workload: `${F.measurement}. Each vector is one window of sequence encoded by HoloGenome. A linear readout is trained on ${F.trainSet} and tested on ${F.testSet} with ${F.overlap}. The period search fits a small phasor model to one library's measurements, starting from ${F.startPeriod} bases per turn, and is repeated on independent libraries.`,
      engines: ['HoloGenome vectors with a linear readout', 'A fitted phasor model', 'A zero-parameter score'],
      verification: 'Each score is a correlation on sequences the model had not seen. Label-permutation checks land at zero, as designed, and scrambled-order controls fall to the level of base counts alone. The period search is repeated with the base order scrambled and with the labels scrambled.',
      statistic: `${m.method.statistic}, with a 95% interval from resampling whole gene blocks.`,
      memory: 'The Loop-seq measurements are not redistributed.',
    },
    tables: [
      { id: 'controls', kind: 'matrix', caption: ctl.label + '. ' + ctl.note, columns: ['Vectors built with', 'Correlation r', '95% interval'], rows: ctl.rows.map((r) => [r.label, f3(r.r), `${f3(r.lo)} to ${f3(r.hi)}`]) },
      { id: 'deltas', kind: 'matrix', caption: 'Differences between those readouts', columns: ['Contrast', 'Difference in r', '95% interval'], rows: ctl.deltas.map((r) => [r.label, f3(r.d), `${f3(r.lo)} to ${f3(r.hi)}`]) },
      { id: 'period', kind: 'matrix', caption: per.label + '. ' + per.note, columns: ['Library', 'Found from real data', 'Order scrambled', 'Labels scrambled'], rows: per.libraries.map((l) => [l.label, f2(l.real), f2(l.order), f2(l.labels)]) },
      { id: 'basin', kind: 'matrix', caption: 'Where the period search started, yeast tiling library', columns: ['Started at (bases per turn)', 'Settled at (bases per turn)'], rows: per.basin.map((b) => [String(b.start), f2(b.end)]) },
      { id: 'helix-score', kind: 'matrix', caption: m.helixScore.label, columns: ['Library', ...m.helixScore.periods.map((p) => p.label)], rows: m.helixScore.libraries.map((l) => [l.label, ...m.helixScore.periods.map((p) => f3(l[p.id]))]) },
      { id: 'training', kind: 'matrix', caption: `${m.training.label}: held-out correlation after training the readouts with iterative optimisation`, columns: ['Vectors built with', 'Random sequence', 'Chromosome V tiling', 'Nucleosome'], rows: m.training.readouts.map((r) => [r.label, f3(r.random), f3(r.chrv), f3(r.nuc)]) },
    ],
    losses: [
      `More training did not help. Training the vector readouts with iterative optimisation left accuracy where it was (r ${F.trainLo} to ${F.trainHi} across the libraries tested), and running the phasor models for ${F.stepFactor} times as many steps moved their scores by at most ${F.stepChange}.`,
      `The measurement itself has a ceiling. Loop-seq replicates agree at roughly r = ${F.ceilingLo} to ${F.ceilingHi}, so no model can score near 1.0 on it.`,
      `The simplest zero-parameter score is modest. At r = ${F.zeroLo} to ${F.zeroHi} it is well below any trained readout. Its value is that it needs no fitting and matches the known periodicity of the double helix.`,
    ],
    limits: [
      `The period search has a narrow basin. Runs started at ${F.basinFound} bases found the helical period, and runs started at ${F.basinMissed} bases found nothing.`,
      'All grading uses one kind of measurement: Loop-seq bendability in yeast-derived and random sequence. Nothing is claimed for other organisms, other assays or chromatin in the living cell.',
      'The graded readouts (the linear model, the zero-parameter helical score and the fitted phasor models) run in the research harness and are not in the shipped package. The package ships the encoder, the twist tables and the window store.',
      'The shape layer was graded with a pentamer table from the DNAshapeR project. That table is not shipped because its licence does not allow it, so you load your own.',
      'The correlations are Pearson r. The intervals come from resampling whole gene blocks, because neighbouring windows overlap.',
    ],
    internal: { source: 'web/data/diagrams/hologenome-scores.json, web/scripts/derive-evidence.mjs' },
  });
}