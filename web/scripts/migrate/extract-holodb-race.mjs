// Builds data/diagrams/holodb-race.json from the 100,000,000-invoice result files. Nothing in the file is typed:
// every cell is a warm median or a first (cold) run read from a result file, the repeat run comes from a second file of the same engine,
// and the load, size and open figures come from the build manifest of the same data set.
//   node web/scripts/migrate/extract-holodb-race.mjs [results-dir]     (default C:\HoloDbBench\n100000000, or $EA_BENCH)
import fs from 'node:fs';
import path from 'node:path';
import { DATA } from '../lib/data.mjs';
import { counts, cell } from '../../src/assets/diagrams/holodb-race.js';

const root = process.argv[2] || process.env.EA_BENCH || 'C:\\HoloDbBench\\n100000000';
const rj = (name) => JSON.parse(fs.readFileSync(path.join(root, 'results', name + '.json'), 'utf8'));
const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json'), 'utf8'));

// arm, the file the figures come from, and the second run of the same engine (the repeat the noise marks are read from)
const ARMS = [
  { id: 'restored', label: 'HoloDb restored', use: 'holodeclared-s4', again: 'holodeclared-s3', build: 'holodeclared', line: 'structures declared and saved beside the data, opened from them' },
  { id: 'oob', label: 'HoloDb out of the box', use: 'holopaged-s2', again: 'holopaged', build: 'holopaged', line: 'nothing declared, nothing saved; the planner builds what repeats earn' },
  { id: 'duck', label: 'DuckDB tuned', use: 'duckdbtuned', again: 'duckdbtuned-r2', build: 'duckdbtuned', line: 'table sorted by customer_id, ANALYZE, 16 threads, 12 GB limit' },
  { id: 'sql', label: 'SQL Server tuned', use: 'sqlservertuned', again: 'sqlservertuned-r2', build: 'sqlservertuned', line: 'columnstore index, descending index on amount_cents, indexed views' },
];
const runs = ARMS.map((a) => ({ a, r1: rj(a.use), r2: rj(a.again) }));
const labels = runs[0].r1.Queries.map((q) => q.Label);
for (const x of runs) {
  if (x.r1.Rows !== 100000000) throw new Error(x.a.use + ' is not a 100,000,000-row run');
  x.r1.Queries.forEach((q, i) => {
    if (q.Label !== labels[i] || x.r2.Queries[i].Label !== labels[i]) throw new Error('query order differs in ' + x.a.use);
    if (q.Status !== 'OK' || x.r2.Queries[i].Status !== 'OK') throw new Error(`${x.a.use}: ${q.Label} not OK`);
    if (q.Hash !== runs[0].r1.Queries[i].Hash || x.r2.Queries[i].Hash !== q.Hash) throw new Error(`${x.a.use}: ${q.Label} hash differs`);
  });
}
const GROUP = { 'invoices by status': 'rollup', 'revenue by month': 'rollup', 'revenue by region': 'rollup', 'total invoiced': 'rollup', 'invoice count': 'rollup', 'look up one invoice': 'lookup',
  'overdue outstanding': 'scan', 'big-ticket invoices (>$5k)': 'scan', 'largest invoices': 'scan', 'top customers by revenue': 'scan', 'revenue by region & status': 'scan' };
const r4 = (n) => Math.round(n * 10000) / 10000;
const queries = labels.map((label, i) => ({
  label, group: GROUP[label] || 'join', hash: runs[0].r1.Queries[i].Hash, rows: runs[0].r1.Queries[i].RowCount, first: runs[0].r1.Queries[i].Rows[0],
  warm: runs.map((x) => r4(x.r1.Queries[i].WarmMedianMs)), warm2: runs.map((x) => r4(x.r2.Queries[i].WarmMedianMs)),
  cold: runs.map((x) => r4(x.r1.Queries[i].ColdMs)), cold2: runs.map((x) => r4(x.r2.Queries[i].ColdMs)),
}));
const M = manifest.Engines;
const arms = ARMS.map((a, k) => ({
  id: a.id, label: a.label, line: a.line, source: a.use,
  loadSeconds: r4(M[a.build].LoadSeconds), sizeBytes: M[a.build].SizeBytes, openSeconds: r4(runs[k].r1.OpenSeconds ?? 0), peakBytes: runs[k].r1.PeakWorkingSetBytes, coldTotalSeconds: r4(runs[k].r1.TotalColdSeconds),
  version: runs[k].r1.Version,
}));
const sc = runs[0].r1;
const data = {
  schema: 'ea-diagram/1', kind: 'holodb-race', id: 'holodb-race', generator: 'web/scripts/migrate/extract-holodb-race.mjs',
  caption: 'Warm medians at 100,000,000 invoices for the 16 billing queries: HoloDb restored, HoloDb out of the box, tuned DuckDB and tuned SQL Server.',
  rows: manifest.Rows, machine: sc.Machine, utc: sc.Utc.slice(0, 10), fingerprint: manifest.Fingerprint,
  arms, queries,
  extra: { restoredBuild: M.holodeclared.Files, restoredFiles: Object.fromEntries(Object.entries(M.holodeclared.Files).map(([k, v]) => [k.split('\\').slice(-2).join('/'), v])) },
};
fs.writeFileSync(path.join(DATA, 'diagrams', 'holodb-race.json'), JSON.stringify(data, null, 1) + '\n', 'utf8');

const show = (mode) => { const c = counts(data, mode); console.log(mode, JSON.stringify(c)); };
show('warm'); show('cold');
queries.forEach((q, qi) => console.log(q.label.padEnd(36), data.arms.map((_, ai) => { const c = cell(data, qi, ai, 'warm'); return (c.v.toFixed(2) + (c.noisy ? '~' : '')).padStart(9); }).join('')));
console.log('wrote holodb-race.json', queries.length, 'queries');
