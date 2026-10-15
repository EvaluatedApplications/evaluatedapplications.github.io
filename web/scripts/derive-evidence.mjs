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
