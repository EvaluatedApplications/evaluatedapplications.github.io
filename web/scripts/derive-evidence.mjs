#!/usr/bin/env node
// Derives the evidence records for EvalApp, AlgFormer, HoloVoxel and Prose from the diagram data that the generators in
// web/generators wrote from the PUBLISHED packages, so every number on an evidence page is read from a recording and none is typed.
//   node web/scripts/derive-evidence.mjs
// Re-run it after any generator is re-run. The records land in web/data/evidence/ next to the hand-kept ones.
import fs from 'node:fs';
import path from 'node:path';
import { DATA, readJson } from './lib/data.mjs';
import * as gate from '../src/assets/diagrams/evalapp-gate.js';
import * as cost from '../src/assets/diagrams/algformer-cost.js';
import * as vox from '../src/assets/diagrams/holovoxel-lod.js';
import * as prose from '../src/assets/diagrams/prose-pipeline.js';
import * as boot from '../src/assets/diagrams/blazier-boot.js';
import * as hg from '../src/assets/diagrams/hologenome-scores.js';

const out = (name, rec) => { fs.writeFileSync(path.join(DATA, 'evidence', name + '.json'), JSON.stringify(rec, null, 2) + '\n', 'utf8'); console.log('wrote evidence/' + name + '.json'); };
const n0 = (n) => Number(n).toLocaleString('en-GB');
const sig = (n) => (n >= 100 ? n0(Math.round(n)) : n >= 10 ? Number(n).toFixed(1) : Number(n).toFixed(2));
const ratio = (a, b) => { const r = a / b; return r >= 10 ? `${Math.round(r)}` : r.toFixed(1); };
const dg = (n) => readJson(path.join(DATA, 'diagrams', n + '.json'));

// ---------------------------------------------------------------- EvalApp: the gate
{
  const d = dg('evalapp-gate'), m = gate.metrics(d), best = m.byBound[m.fastest], one = m.runs[0], wide = m.runs[m.runs.length - 1];
  out('evalapp-gate', {
    id: 'evalapp-gate', product: 'evalapp', slug: '/evalapp/evidence/', scope: 'A resource gate on simulated downloads',
    title: 'What a gate bound does',
    description: `A gate held concurrency at its declared bound in every run, and ${m.n} queued jobs finished in ${n0(best.makespan)} ms at the best bound against ${n0(one.makespan)} ms at 1.`,
    claim: `The gate never admitted more downloads than its bound: in every recorded run the most in flight equalled the bound. The time to finish ${m.n} queued jobs fell from ${n0(one.makespan)} ms at a bound of 1 to ${n0(best.makespan)} ms at ${best.bound}, then rose again to ${n0(wide.makespan)} ms when all ${m.n} ran at once, because the simulated service slows when it is crowded.`,
    headline: [
      { value: `${m.runs.filter((r) => r.peak === r.bound).length} of ${m.runs.length}`, label: 'recorded runs where the most downloads in flight equalled the gate bound' },
      { value: `${n0(one.makespan)} ms to ${n0(best.makespan)} ms`, label: `time to finish ${m.n} jobs at a bound of 1 and at the best bound, ${best.bound}` },
      { value: `${ratio(wide.makespan, best.makespan)} times`, label: `slower with all ${m.n} jobs running at once than at the best bound` },
    ],
    method: {
      workload: `${m.n} jobs are queued at once through a ForEach. Each passes a Network gate and awaits a simulated download of ${m.baseMs} ms. The simulated service stretches the wait once more than ${m.knee} downloads are in flight. ${m.runs.length} fixed gate bounds from ${one.bound} to ${wide.bound}, one recorded run each, after a warm-up run.`,
      engines: ['EvalApp pipeline with Gate and ForEach'],
      verification: 'Every job records its own timestamps, and the number of downloads in flight is counted inside the gated step, so the bound is checked against what actually ran.',
      statistic: 'One recording per bound, in milliseconds. A repeated recording varies by a few percent.',
      memory: 'Nothing is retained between runs.',
    },
    tables: [{
      id: 'bounds', kind: 'matrix', caption: `${m.n} queued jobs, by gate bound`,
      columns: ['Gate bound', 'All jobs finished in (ms)', 'Most running at once', 'Longest wait at the gate (ms)', 'Jobs per second'],
      rows: m.runs.map((r) => [String(r.bound), n0(r.makespan), String(r.peak), n0(r.maxWait), String(r.perSecond)]),
    }],
    losses: [
      `A bound of ${one.bound} took ${n0(one.makespan)} ms, ${ratio(one.makespan, best.makespan)} times the best bound. The gate does not choose its bound for you, and a bound set too low is the slowest result here.`,
      `A bound of ${wide.bound}, the whole batch at once, took ${n0(wide.makespan)} ms, ${ratio(wide.makespan, best.makespan)} times the best bound. A higher bound is not faster when the thing behind the gate slows under load.`,
    ],
    limits: [
      'The work is a simulated wait, not a real network. It shows how the gate and its queue behave, not the speed of any real service.',
      `The best bound here is a property of the simulated service, which slows past ${m.knee} in flight. A real service has its own shape, which is what the tuner exists to find.`,
      'It measures the gate and does not compare EvalApp with another library.',
    ],
    internal: { source: 'web/generators/evalapp-gate, web/scripts/derive-evidence.mjs' },
  });
}

// ---------------------------------------------------------------- AlgFormer: what attention costs
{
  const d = dg('algformer-cost'), m = cost.metrics(d), L = m.last;
  out('algformer-cost', {
    id: 'algformer-cost', product: 'algformer', slug: '/algformer/evidence/', scope: 'Two attention cores at one shape',
    title: 'What attention costs',
    description: `At one shape, reading a ${n0(L.tokens)}-token prompt takes ${sig(L.prefillMs.softmax)} ms with softmax attention and ${sig(L.prefillMs.holographic)} ms with holographic attention.`,
    claim: `At the same shape and the same parameter count, reading a ${n0(L.tokens)}-token prompt takes ${sig(L.prefillMs.softmax)} ms with softmax attention and ${sig(L.prefillMs.holographic)} ms with holographic attention. Each further token takes ${sig(L.stepUs.softmax)} µs against ${sig(L.stepUs.holographic)} µs, and the serve cache holds ${n0(Math.round(L.cacheKb.softmax))} KB against ${sig(L.cacheKb.holographic)} KB. Softmax reading grows with the square of the length (exponent ${m.exps.prefill.softmax.toFixed(1)}) and holographic reading grows in step with it (${m.exps.prefill.holographic.toFixed(1)}).`,
    headline: [
      { value: `${ratio(m.prefillRatio, 1)} times`, label: `less time to read a ${n0(L.tokens)}-token prompt with holographic attention` },
      { value: `${ratio(m.stepRatio, 1)} times`, label: 'less time for each further token, which stays flat as the context grows' },
      { value: `${n0(Math.round(m.cacheRatio))} times`, label: 'smaller serve cache, constant in the length of the context' },
    ],
    method: {
      workload: `Both cores are built at ${d.shape.dModel} channels, ${d.shape.shifts} shifts and ${d.shape.layers} layers, which gives them the same parameter count, and driven with the same random tokens. For each context length the run reads the prompt through the serve cache, then produces ${d.method.stepsTimed} more tokens and averages them. Cache memory is the managed memory kept alive by the primed cache.`,
      engines: ['AlgFormer (softmax attention)', 'HoloFormer (holographic attention)'],
      verification: 'Both cores are warmed at every length before timing, so compilation has settled. The two have identical parameter counts.',
      statistic: `Median of ${d.method.reps} runs per length, on one thread.`,
      machine: `${d.method.machineCores} logical processors; the run used one thread.`,
      tie: 'This measures cost only. It says nothing about which core predicts better.',
    },
    tables: [
      {
        id: 'cost', kind: 'matrix', caption: 'Time and memory by context length',
        columns: ['Tokens in context', 'Read the prompt, softmax (ms)', 'Read the prompt, holographic (ms)', 'One more token, softmax (µs)', 'One more token, holographic (µs)', 'Cache, softmax (KB)', 'Cache, holographic (KB)'],
        rows: m.rows.map((r) => [n0(r.tokens), sig(r.prefillMs.softmax), sig(r.prefillMs.holographic), sig(r.stepUs.softmax), sig(r.stepUs.holographic), sig(r.cacheKb.softmax), sig(r.cacheKb.holographic)]),
      },
      {
        id: 'capacity', kind: 'matrix', caption: 'The shape HoloShape derives from a width, and the capacity of a holographic bundle at that shape',
        columns: ['Width', 'Shifts', 'Layers', 'Derived context (tokens)', 'Crosstalk edge (tokens)', 'Reads back 90% clean (tokens)'],
        rows: m.capacity.map((c) => [String(c.dim), String(c.shifts), String(c.layers), n0(c.context), n0(c.bindRank), n0(c.cleanCapacity)]),
      },
    ],
    losses: [
      'Holographic attention holds a limited number of items cleanly in one bundle, and HoloShape reports how many for a shape (the capacity table). Softmax attention has no such bound, at the cost of everything above.',
    ],
    limits: [
      'Absolute times depend on the machine. The way each cost grows with the context length is the finding.',
      'One shape and one random token stream. The growth exponents are fitted between the 128-token and the longest measurement.',
      'Cost only, not quality.',
    ],
    internal: { source: 'web/generators/algformer-cost, web/scripts/derive-evidence.mjs' },
  });
}

// ---------------------------------------------------------------- HoloVoxel: accuracy against component budget
{
  const d = dg('holovoxel-lod'), m = vox.metrics(d), lad = m.ladder, lo = lad[0], hi = lad[lad.length - 1];
  out('holovoxel-lod', {
    id: 'holovoxel-lod', product: 'holovoxel', slug: '/holovoxel/evidence/', scope: 'One chunk decoded at several budgets',
    title: 'Detail against components read',
    description: `One chunk stored as a single hologram decodes ${hi.accuracy.toFixed(1)} percent correct at the full budget, and the read costs less as the budget falls.`,
    claim: `One 16 by 16 chunk stored as a single hologram decodes ${hi.accuracy.toFixed(1)} percent of its cells correctly at the full budget of ${n0(hi.components)} components, ${n0(hi.storedBytes)} bytes and ${hi.decodeMs.toFixed(1)} ms. Reading fewer components is cheaper and fuzzier: ${lad.find((r) => r.components === m.levels[1].components).accuracy.toFixed(1)} percent at ${n0(m.levels[1].components)} components, and ${lo.accuracy.toFixed(1)} percent at ${n0(lo.components)}, which takes ${n0(lo.storedBytes)} bytes and ${lo.decodeMs.toFixed(2)} ms.`,
    headline: [
      { value: `${hi.accuracy.toFixed(1)}%`, label: `of cells decoded correctly with all ${n0(hi.components)} components` },
      { value: `${ratio(hi.storedBytes, lo.storedBytes)} times`, label: `less hologram read at ${n0(lo.components)} components than at the full budget` },
      { value: `${ratio(hi.decodeMs, lo.decodeMs)} times`, label: 'less decode time at the smallest budget than at the full one' },
    ],
    method: {
      workload: `A chunk of a fixed procedural world is built at full fidelity. For each component budget it is decoded as a colour field and checked cell by cell against the stored block (Chunk.Accuracy). The hologram size is the number of bytes CapacityProbe reports for that budget.`,
      engines: ['HoloVoxel Chunk.DecodeField at ten component budgets'],
      verification: 'Accuracy is the share of surface cells whose strongest decoded block equals the block that was stored.',
      statistic: 'Accuracy is one measurement of one chunk. Decode time is the median of 15 runs after warm-up.',
      memory: 'Hologram size is sixteen bytes per component read.',
      tie: 'Eight blocks are possible, so a random guess is right 12.5 percent of the time.',
    },
    tables: [{
      id: 'ladder', kind: 'matrix', caption: 'One chunk, by component budget',
      columns: ['Components read', 'Cells correct (percent)', 'Hologram size (bytes)', 'Decode time (ms)'],
      rows: lad.map((r) => [n0(r.components), r.accuracy.toFixed(1), n0(r.storedBytes), r.decodeMs.toFixed(2)]),
    }],
    losses: [
      `Accuracy is low at small budgets by design: ${lo.accuracy.toFixed(1)} percent at ${n0(lo.components)} components, against 12.5 percent for a random guess. A far read is a soft blend, not a smaller copy of the near one.`,
    ],
    limits: [
      'One chunk of one world. Terrain with other block mixes will score differently.',
      'Accuracy is measured at full resolution for each budget. The scene on the product page also coarsens the grid with distance, which this table does not measure.',
      'Decode time is for one chunk on one machine.',
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
      `The package returned ${n0(b.produced)} of the ${n0(b.requested)} requested sentences. It returns up to the number asked for and stops when it cannot find more different valid ones.`,
    ],
    limits: [
      'The gate checks form, using the same rules that produced the sentences. It does not check meaning, and a valid sentence can still say something odd.',
      'One corpus, one seed.',
    ],
    internal: { source: 'web/generators/prose-pipeline, web/scripts/derive-evidence.mjs' },
  });
}

// ---------------------------------------------------------------- Blazier: what the visitor waits for
{
  const d = dg('blazier-boot'), m = boot.metrics(d), F = boot.limitFacts(m), me = d.method;
  const fr = boot.fmtRange;
  const rowsFor = (x) => x.groups.map((g) => [x.label, g.label, fr(g.classic.lo, g.classic.hi, x.unit), fr(g.shell.lo, g.shell.hi, x.unit)]);
  out('blazier-boot', {
    id: 'blazier-boot', product: 'blazier', slug: '/blazier/evidence/', scope: 'The sample app in headless Edge',
    title: 'What a visitor waits for',
    description: `A Blazier static shell downloads ${F.bytesShell} before the visitor acts, against ${F.bytesClassic} for the usual boot, and shows content in ${F.contentPhoneShell} on a phone profile.`,
    claim: `On the sample app the static page downloads ${F.bytesShell} before the visitor acts, against ${F.bytesClassic} when the same app boots the usual way. Real content is on screen in ${F.contentDesktopShell} on desktop and ${F.contentPhoneShell} on the phone profile, against ${F.contentDesktopClassic} and ${F.contentPhoneClassic}. After the visitor presses Start the runtime takes ${F.workingDesktopShell} on desktop and ${F.workingPhoneShell} on the phone profile to reach a working page, the same wait as the usual boot.`,
    headline: [
      { value: F.bytesShell, label: `downloaded before the visitor does anything, against ${F.bytesClassic} when the same app boots the usual way` },
      { value: F.contentPhoneShell, label: `until real content is on screen on the phone profile, against ${F.contentPhoneClassic} booting the usual way` },
      { value: F.gap, label: `worst gap on the page's timers while a worker ran a long job, with ${F.longTasks} long tasks` },
      { value: F.callRt, label: 'for one call to a worker service, round trip' },
    ],
    method: {
      workload: `The Blazier sample app, ${me.build}, is loaded in ${me.browser} from ${me.server}. The same page is loaded two ways: booted the usual way, and as a Blazier static shell that starts the runtime when the visitor presses Start. The worker rows run ${me.workerJob} in a worker service.`,
      engines: ['Blazier static shell', 'Blazor WebAssembly booted the usual way'],
      verification: `On a page with a click target, a touch scroll, a tap on text and a Tab key press make ${F.gestureRequests} requests for the runtime, while the control page starts it.`,
      statistic: `Desktop is unthrottled, ${F.desktopRuns} runs. The phone profile is ${me.phone}, ${F.phoneRuns} runs. A range runs from the fastest to the slowest run.`,
      machine: `${me.machine[0].toUpperCase()}${me.machine.slice(1)}.`,
    },
    tables: [
      {
        id: 'boot', kind: 'matrix', caption: 'Booted the usual way and as a Blazier static shell',
        columns: ['Measure', 'Profile', ...d.series.map((s) => s.label)],
        rows: [...rowsFor(m.bytes).map((r) => [r[0], 'Both', r[2], `${r[3]} in ${F.requestsShell} requests`]), ...rowsFor(m.content), ...rowsFor(m.working).map((r) => [r[0] + ' (usual boot from loading the page, Blazier from pressing Start)', r[1], r[2], r[3]])],
      },
      {
        id: 'worker', kind: 'matrix', caption: 'Worker bridge, desktop', columns: ['Measure', 'Recorded'],
        rows: d.worker.map((w) => [w.label, w.text || fr(w.lo, w.hi, w.unit)]),
      },
    ],
    losses: [
      `Pressing Start does not shorten the wait for the runtime: a working page takes ${F.workingPhoneShell} after the press on the phone profile, against ${F.contentPhoneClassic} for the usual boot. Blazier moves the wait behind a readable page and does not remove it.`,
    ],
    limits: [
      'One sample app on one machine. The figures describe that app, not a forecast for another.',
      'The sample app uses the trimmed interpreter build. In an ahead-of-time build the lazy-assembly saving is small, because that native code lives in the main runtime file either way.',
      `The worker rows are desktop only. The phone profile slows the page and not the worker, so a phone worker figure would describe the page. A worker runtime starts at about ${F.workerMemory} of memory.`,
      'Headless Edge only. Firefox and Safari have not been run, and the memory and battery cost on a phone are not measured.',
      'The worker bridge was measured with a shared bundle. A separate worker bundle has been built but not run in a browser.',
      'The Showroom tool pages are built this way, but their shell sizes and timings are not part of this record.',
    ],
    internal: { source: 'web/data/diagrams/blazier-boot.json, web/scripts/derive-evidence.mjs' },
  });
}

// ---------------------------------------------------------------- HoloGenome: how the vectors score against held-out measurements
{
  const d = dg('hologenome-scores'), m = hg.metrics(d), F = hg.limitFacts(m);
  const f2 = (v) => Number(v).toFixed(2), f3 = (v) => Number(v).toFixed(3);
  const ctl = m.views.find((v) => v.id === 'controls'), best = m.views.find((v) => v.id === 'best'), per = m.views.find((v) => v.id === 'period');
  const fitName = `${m.method.fitted.size}-parameter fit`;
  out('hologenome-scores', {
    id: 'hologenome-scores', product: 'hologenome', slug: '/hologenome/evidence/', scope: 'Held-out Loop-seq bendability measurements',
    title: 'How HoloGenome scores against the lab',
    lossTitle: 'Where it did not win',
    description: `Measured helix twist scores r = ${F.rTwist} against held-out bendability measurements, against ${F.rConstant} for a constant twist. The best trained network scores higher.`,
    claim: `A linear readout trained on ${F.trainSet} and tested on ${F.testSet} (${F.overlap}) scores r = ${F.rTwist} with vectors that use the measured twist, against ${F.rConstant} with one constant twist (difference ${F.dTwist}, 95% interval ${F.dTwistLo} to ${F.dTwistHi}). Adding the local-shape layer lifts it to ${F.rShape}, a further ${F.dShape}. A small phasor model that chose its own period, starting from ${F.startPeriod} bases, settled at ${F.periods} bases per turn on four independent libraries, against the crystallographic ${F.periodRef}. The best trained network scores higher: the ${F.fittedSize}-parameter fit trails it by ${F.gapLo} to ${F.gapHi}.`,
    headline: [
      { value: `${F.periodLo} to ${F.periodHi}`, label: `bases per turn found by a small model that was never told the helical period, on four independent libraries (the crystallographic value is ${F.periodRef})` },
      { value: `${F.rTwist} vs ${F.rConstant}`, label: 'correlation with held-out bendability using the measured twist of each base pair step, against one constant twist' },
      { value: F.rShape, label: `correlation with held-out bendability once the local-shape layer is added, against ${F.rWords} for counts of short words` },
      { value: `${F.gapLo} to ${F.gapHi}`, label: `behind the best trained network, DNAcycP, on every library where it has published scores (${F.dnacycp} against ${F.fitted})` },
    ],
    method: {
      workload: `${F.measurement}. Each vector is one window of sequence encoded by HoloGenome. A linear readout is trained on ${F.trainSet} and tested on ${F.testSet} with ${F.overlap}. The period search fits a small phasor model to one library's measurements, starting from ${F.startPeriod} bases per turn, and is repeated on independent libraries.`,
      engines: ['HoloGenome vectors with a linear readout', 'A fitted phasor model', 'A zero-parameter score', `DNAcycP, ${F.dnacycpDesc} (published scores)`],
      verification: 'Each score is a correlation on sequences the model had not seen. Label-permutation checks land at zero, as designed, and scrambled-order controls fall to the level of base counts alone. The period search is repeated with the base order scrambled and with the labels scrambled.',
      statistic: `${m.method.statistic}, with a 95% interval from resampling whole gene blocks. The DNAcycP figures are published.`,
      memory: 'The Loop-seq measurements are not redistributed.',
    },
    tables: [
      { id: 'controls', kind: 'matrix', caption: ctl.label + '. ' + ctl.note, columns: ['Vectors built with', 'Correlation r', '95% interval'], rows: ctl.rows.map((r) => [r.label, f3(r.r), `${f3(r.lo)} to ${f3(r.hi)}`]) },
      { id: 'deltas', kind: 'matrix', caption: 'Differences between those readouts', columns: ['Contrast', 'Difference in r', '95% interval'], rows: ctl.deltas.map((r) => [r.label, f3(r.d), `${f3(r.lo)} to ${f3(r.hi)}`]) },
      { id: 'best', kind: 'matrix', caption: best.label + '. ' + best.note, columns: ['Library', ...best.series.map((s) => (s.id === 'fitted' ? fitName : s.label))], rows: best.groups.map((g) => [g.label, ...best.series.map((s) => f2(g.values[s.id]))]) },
      { id: 'period', kind: 'matrix', caption: per.label + '. ' + per.note, columns: ['Library', 'Found from real data', 'Order scrambled', 'Labels scrambled'], rows: per.libraries.map((l) => [l.label, f2(l.real), f2(l.order), f2(l.labels)]) },
      { id: 'basin', kind: 'matrix', caption: 'Where the period search started, yeast tiling library', columns: ['Started at (bases per turn)', 'Settled at (bases per turn)'], rows: per.basin.map((b) => [String(b.start), f2(b.end)]) },
      { id: 'helix-score', kind: 'matrix', caption: m.helixScore.label, columns: ['Library', ...m.helixScore.periods.map((p) => p.label)], rows: m.helixScore.libraries.map((l) => [l.label, ...m.helixScore.periods.map((p) => f3(l[p.id]))]) },
      { id: 'training', kind: 'matrix', caption: `${m.training.label}: held-out correlation after training the readouts with iterative optimisation`, columns: ['Vectors built with', 'Random sequence', 'Chromosome V tiling', 'Nucleosome'], rows: m.training.readouts.map((r) => [r.label, f3(r.random), f3(r.chrv), f3(r.nuc)]) },
    ],
    losses: [
      `It does not beat the best trained model. DNAcycP, ${F.dnacycpDesc}, reaches r = ${F.dnacycp} on the libraries where it has published scores. The best ${F.fittedSize}-parameter model here reached ${F.fitted} on the same libraries, trailing by ${F.gapLo} to ${F.gapHi}.`,
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