// One-off migration helper: copies the owner's operation-grid data into data/diagrams/pleroma-map.json for the site.
// Internal test-class names are dropped from the evidence text (the measured results stay); a few cells carry a
// hand-written override where mechanical cleanup would leave a broken sentence.
//   node web/scripts/migrate/extract-pleroma-map.mjs [path-to-pleroma-map.json]
import fs from 'node:fs';
import path from 'node:path';
import { MONOREPO, DATA } from '../lib/data.mjs';

const src = process.argv[2] || path.join(MONOREPO, 'Phasor', 'docs', 'pleroma-map.json');
const j = JSON.parse(fs.readFileSync(src, 'utf8'));

const IDENT = /\b[A-Za-z][A-Za-z0-9]*(?:Tests)?\.[A-Za-z0-9_]+\*?|\b[A-Za-z0-9]+_[A-Za-z0-9_]*\*|\b[A-Z]{1,3}\d+[a-z]?_\*/g;
function clean(ev) {
  let s = ev.replace(/\b[A-Za-z0-9]+Tests\.[A-Za-z0-9_*]+/g, '').replace(/\b[A-Za-z0-9]+(?:_[A-Za-z0-9]+)*_\*/g, '').replace(/\b[A-Za-z0-9]+_[A-Za-z0-9_]+\b(?=[,:]|$)/g, '');
  s = s.replace(/(^|[ ,])\*+/g, '$1').replace(/\s*:\s*:/g, ':').replace(/\s+,/g, ',').replace(/,\s*,/g, ',').replace(/^[\s,:]+/, '').replace(/[\s,:]+$/, '').replace(/:\s*,/g, ':').replace(/\s{2,}/g, ' ').replace(/\(\s*,\s*/g, '(').replace(/,\s*\)/g, ')');
  return s;
}
const OVERRIDE = {
  R1_Syzygy: 'Adding then removing a count zeroes every cell, on the default field and on three primes.',
  R1_Monad: 'Counts on two arrangements exact, held in superposition through the kernel identity and swap.',
  R1_Ouroboros: 'Each of 15 masks toggled once matches a rebuilt toggled store, and twice is the identity. A kernel composed twice is the identity.',
  R2_Syzygy: 'Keys exact 30/30 at 16 keys, refused 30/30 at 17 and beyond.',
  R2_Monad: 'Arrangement index round-trips exactly, and a one-item store reads as one-hot.',
  R2_Ouroboros: 'Flag counts exact at any load. Hamming retrieval exact.',
  R3_Syzygy: 'Bind equals the store of all pairs, 35/35 across 5 shapes (including continuous and self-bind). 715 multisets of three bindings exact. The three-face read is exact 192/192 on 3 fields.',
  R3_Monad: 'Arrangement unbinding exact 1,000/1,000 in the store. Reversing the order changes the result in 5,800 of 10,000 cases.',
  R3_Ouroboros: 'Bit counts and parity exact 12/12 on 3 fields. 256 parity classes exact.',
  R4_Syzygy: 'Bind with a one-item store is the whole-store shift: 20/20 (key shift, flag relabel, arrangement relabel and tick agree).',
  R4_Monad: 'Time spectrum exact 37/37 (3 fields, signed). One-tick-per-key maps 60/60. Composed time maps 18/18, with bijections undone by their inverse.',
  R4_Ouroboros: 'Predicates exact 1,515/1,515. 1,024/1,024 parity functions read from one spectrum. Weighted predicates 30/30.',
  R5_Syzygy: 'Every unit of 225 permutes (120/120 on two fields). Every non-unit gathers (113/113).',
  R5_Monad: 'All 24 x 24 left-right pairs on S_4 exact.',
  R5_Ouroboros: 'Exact 18/18 (default field, continuous, 3 primes). The eighth power is the identity on x^8+x^4+x^3+x^2+1.',
  R6_Syzygy: 'Exact 12/12 (63 x 55 with a continuous part, and 9 x 25).',
  R6_Monad: 'Independent stores report true 40/40, dependent ones false 40/40, and one added item breaks independence 40/40.',
  R6_Ouroboros: 'Exact 15/15. All 112 single errors of Hamming(7,4) located.',
  R7_Syzygy: 'All 4,096 combinations of shift, diffusion and count modulo 16 exact.',
  R7_Monad: 'Exact expected counts over 600 diffusion steps.',
  R7_Ouroboros: 'Exact 9/9 on 3 primes. Period 255 for x^8+x^4+x^3+x^2+1.',
  R8_Syzygy: 'Loop reads exact 2,176/2,176. Merged and subtracted stores 1,804/1,804.',
  R8_Monad: 'Reversibility read exact 200/200. Perturbed loops detected 120/120.',
  R8_Ouroboros: 'Every loop\'s frustration read exactly.',
  Lid_Sample: 'Every draw in [0, total) lands on its value. Shares uniform on 4/4 views.',
};
const rungs = j.pyramid.rungs.map((r) => ({
  n: r.rungIndex, name: r.name, caption: r.caption, description: r.description,
  cells: r.cells.map((c) => ({ id: c.id, face: c.face, name: c.name, description: c.description, api: c.api, status: c.status, evidence: OVERRIDE[c.id] ?? clean(c.evidence), rawEvidence: undefined })),
}));
const lid = j.lid.cells[0];
const out = {
  schema: 'ea-diagram/1', kind: 'pleroma-map',
  package: { id: 'EvaluatedApplications.Phasor', version: j.pyramid.version },
  title: j.pyramid.title, totalCells: j.pyramid.totalCells,
  faces: ['Syzygy', 'Monad', 'Ouroboros'],
  faceNotes: {
    Syzygy: 'the key cycle and its signed counts', Monad: 'the order face, S_k', Ouroboros: 'the flag cube, Z_2^m',
  },
  rungs,
  lid: { id: lid.id, name: j.lid.name, description: j.lid.description, cell: { id: lid.id, name: lid.name, description: lid.description, api: lid.api, status: lid.status, evidence: OVERRIDE[lid.id] ?? clean(lid.evidence) } },
};
const dest = path.join(DATA, 'diagrams', 'pleroma-map.json');
fs.writeFileSync(dest, JSON.stringify(out) + '\n', 'utf8');
for (const r of rungs) for (const c of r.cells) console.log(c.id.padEnd(14), c.evidence);
console.log(lid.id.padEnd(14), out.lid.cell.evidence);
console.log('wrote', dest, fs.statSync(dest).size, 'bytes');
