// Builds the two HoloDb evidence records:
//   data/evidence/holodb.json            /holodb/evidence/            the 100,000,000-invoice record: headline, the race chart, the explainer,
//                                                                      how it was measured, the limits (layout "story")
//   data/evidence/holodb-deep-dive.json  /holodb/evidence/deep-dive/  the optional plane, constructed workloads, ingest, tenants, scaling,
//                                                                      the 10,000,000-invoice comparison against engines as they ship (layout "deep")
// Run, in order: extract-holodb-scaling.mjs, extract-holodb-race.mjs, extract-holodb-story.mjs, then this file.
// Every number in the story record is read from holodb-race.json / holodb-fit.json (themselves read from the benchmark result files).
// The deep dive parses its tables from the owner's HoloDb/docs/site.md, and its prose is written here in the present tense.
import fs from 'node:fs';
import path from 'node:path';
import { MONOREPO, DATA, readJson } from '../lib/data.mjs';
import { counts, facts as raceFacts } from '../../src/assets/diagrams/holodb-race.js';

const dg = (n) => readJson(path.join(DATA, 'diagrams', n + '.json'));
const sc = dg('holodb-scaling'), race = dg('holodb-race'), fit = dg('holodb-fit');
const panelOf = (id) => sc.panels.find((p) => p.id === id);
const seriesOf = (pid, cid, sid) => panelOf(pid).charts.find((c) => c.id === cid).series.find((s) => s.id === sid);
const grp = (n) => Number(n).toLocaleString('en-GB');
const slope = (s) => s.slope.toFixed(2);
const slopeCell = (s, kind) => (kind === 'time' && s.nearlyFlat ? `${slope(s)} (nearly flat)` : slope(s));
const usText = (v) => (v >= 1000 ? `${(v / 1000).toFixed(2)} ms` : `${Math.round(v)} us`);
const yRange = (s) => [Math.min(...s.points.map((p) => p.y)), Math.max(...s.points.map((p) => p.y))];
const xAt = (s, i) => grp(s.points[i].x);
const lastDecade = (s) => { const a = s.points[s.points.length - 2], b = s.points[s.points.length - 1]; return (Math.log10(b.y / a.y) / Math.log10(b.x / a.x)).toFixed(2); };

// ---- markdown tables from the owner's document ----
const md = fs.readFileSync(process.argv[2] || path.join(MONOREPO, 'HoloDb', 'docs', 'site.md'), 'utf8').replace(/\r\n/g, '\n');
const tables = [];
{
  const lines = md.split('\n');
  for (let i = 0; i < lines.length; i++) {
    if (/^\|.*\|$/.test(lines[i]) && /^\|[\s:|-]+\|$/.test(lines[i + 1] || '')) {
      const cells = (l) => l.trim().slice(1, -1).split('|').map((c) => c.trim());
      const header = cells(lines[i]); const rows = [];
      let j = i + 2; while (j < lines.length && /^\|.*\|$/.test(lines[j])) { rows.push(cells(lines[j])); j++; }
      tables.push({ header, rows }); i = j;
    }
  }
}
const find = (re) => { const t = tables.find((x) => re.test(x.header.join('|'))); if (!t) throw new Error('table not found: ' + re); return t; };
const strip = (s) => s.replace(/\*\*/g, '').replace(/`/g, '').replace(/^[A-Z]\d+[a-z]?\s+/, '').replace(/^C-X\d+\s*/, '').trim();
const num = (s) => Number(s.replace(/[,*]/g, '').replace(/[^\d.\-]/g, ''));
const clean = (s) => s.replace(/not interpreted \(noise\)/gi, 'noisy, no ratio').replace(/\*\*/g, '').replace(/`/g, '').replace(/\s+/g, ' ').trim();

function timing(headerRe, id, caption, focusCol, rivalCol) {
  const t = find(headerRe);
  const cols = t.header.slice(1, -1);
  const rows = t.rows.map((r) => {
    const verdict = clean(r[r.length - 1]).replace(/ vs DuckDB$/, '');
    const state = /^tie/i.test(verdict) ? 'tie' : /^HoloDb/.test(verdict) ? 'win' : 'loss';
    return { label: strip(r[0]), values: r.slice(1, -1).map(num), verdict, state };
  });
  return { id, caption, columns: cols, focus: focusCol, rival: rivalCol, rows };
}
const matrix = (headerRe, id, caption, opts = {}) => {
  const t = find(headerRe);
  return { id, kind: 'matrix', caption, columns: opts.columns || t.header.map(clean), rows: t.rows.map((r) => r.map((c, i) => (i === 0 ? strip(c) : clean(c)))) };
};
const MAINTAINED = new Set(['invoices by status', 'revenue by region', 'total invoiced', 'revenue by month', 'invoice count']);

// ======================================================================= the story record (100,000,000 invoices)
const F = raceFacts(race), C = counts(race, 'warm');
const arm = Object.fromEntries(race.arms.map((a) => [a.id, a]));
const gb = (b) => (b / 1e9).toFixed(2);
const secs = (s) => (s >= 100 ? Math.round(s) : s >= 10 ? s.toFixed(1) : s.toFixed(2));
const sqlMem = (race.arms.find((a) => a.id === 'sql').version.match(/max server memory (\d+) MB/) || [])[1];
const sqlMemTxt = Number(sqlMem).toLocaleString('en-GB');
const headline = [
  { value: `${F.totalRestored} ms`, label: `to add up ${F.rows} invoices (HoloDb restored, warm median). Tuned DuckDB scanning them: ${F.totalDuck} ms. Tuned SQL Server with an indexed view: ${F.totalSql} ms.` },
  { value: `${F.joinsRestored.replace(' to ', ' to ')} ms`, label: `for the four joins to ${grp(Number((race.fingerprint.match(/cust=(\d+)/) || [])[1]))} customers (HoloDb restored, warm median). Tuned SQL Server: ${F.joinsSql} ms. Tuned DuckDB: ${F.joinsDuck} ms.` },
  { value: `${C.restored.duck.ahead} of ${C.restored.duck.n}`, label: `queries where HoloDb restored is ahead of tuned DuckDB, and ${C.restored.sql.ahead} of ${C.restored.sql.n} where it is ahead of tuned SQL Server. Ahead means more than 20 percent faster in both runs.` },
  { value: `${C.oob.duck.ahead} of ${C.oob.duck.n}`, label: `queries where HoloDb out of the box is ahead of tuned DuckDB. It is behind on ${C.oob.duck.behind}${C.oob.duck.unsure ? ` and too close to call on ${C.oob.duck.unsure}` : ''}: scans that no structure covers. Against tuned SQL Server it is ahead on ${C.oob.sql.ahead} and behind on ${C.oob.sql.behind}.` },
];
const buildTable = {
  id: 'build-size', kind: 'matrix', caption: `Build, size and opening, ${F.rows} invoices`,
  columns: ['Arm', 'Load once', 'On disk', 'Opens in', 'Peak memory while querying'],
  rows: race.arms.map((a) => [a.label, `${secs(a.loadSeconds)} s`, `${gb(a.sizeBytes)} GB`, `${a.openSeconds < 0.1 ? a.openSeconds.toFixed(2) : a.openSeconds.toFixed(2)} s`, `${gb(a.peakBytes)} GB`]),
};
const limitsTable = { id: 'limits', kind: 'matrix', caption: 'What is true today', columns: ['Area', 'What is true today'], rows: fit.limits };

const story = {
  id: 'holodb', product: 'holodb', slug: '/holodb/evidence/', layout: 'story',
  scope: `${F.rows} invoices, tuned rivals`,
  title: 'HoloDb benchmarks',
  description: `HoloDb against tuned DuckDB and SQL Server on ${F.rows} invoices: every query charted, how the wins happen, how it was measured, what it does not do.`,
  claim: `Totals over ${F.rows} invoices in ${F.totalRestored} ms and four joins to two million customers in ${F.joinsRestored} ms, against tuned DuckDB and tuned SQL Server on the same machine and data. Out of the box, scans that no structure covers are slower, and the chart shows each of them as an amber bar.`,
  tieBelow: 1.15,
  headline,
  story: {
    conditions: `One size: ${F.rows} invoices and 2,000,000 customers. One machine: an Intel Core i9-11950H laptop with 16 logical cores. Warm means the median of three runs. Ahead or behind means more than 20 percent in both runs of each engine.`,
    chart: { title: 'Every query, every engine', line: `${F.queries} billing queries on a logarithmic scale. Shorter is faster; an amber HoloDb bar is a loss.`, fig: 'holodb-race', stageLabel: 'Latency chart of the sixteen queries. Use the up and down arrows to read each row.',
      caption: `HoloDb restored ran in a separate session on the same machine and data, the same day as the other three arms. HoloDb restored has its summary, sorted index and keyed summary declared and saved beside the data; the tuned rivals have the matching structures built by hand. Out of the box, nothing is declared and the planner saw each statement four times.` },
    how: { title: 'How the wins happen', line: 'Six short scenes. The motion is schematic; the figures are measured.', fig: 'holodb-how', stageLabel: 'Animated explainer. Choose a scene, then press Play or drag the scrubber.' },
    fit: { title: 'When to use it', line: 'Every answer is a trade-off, so the decision helper names the alternative where another tool is the better choice.', href: '/holodb/#fit', label: 'Open the decision helper' },
    method: {
      title: 'How we measured', line: 'Stated once, for every figure on this page',
      rows: [
        ['Machine', `An 11th Gen Intel Core i9-11950H at 2.60 GHz, 16 logical cores, 34.1 GB of memory, Windows 11, .NET 8.0.27. Each engine ran in its own process.`],
        ['Data and queries', `${F.rows} invoices and 2,000,000 customers; ${F.queries} billing queries: five rollups, a lookup, five scans and five joins to the customers.`],
        ['Tuned DuckDB', 'DuckDB 1.5.5: table written in customer_id order, ANALYZE, 16 threads, 12 GB memory limit.'],
        ['Tuned SQL Server', `SQL Server 2022 Developer: a columnstore index, a descending index on amount_cents and indexed views, max server memory ${sqlMemTxt} MB, in a container.`],
        ['Tuning', 'We chose the rivals\' tuning after a 10,000,000-row trial and fixed it before the 100,000,000-row build. A different tuning is untested.'],
        ['Same answers', `Before any query is timed, its rows are hashed. All four arms return identical results on all ${F.queries} queries, including where HoloDb is slower.`],
        ['Warm and cold', 'Warm is the median of three runs. Cold is the first run after the file cache was cleared by reading a 34 GB file (for SQL Server, dropping its buffers).'],
        ['Repeats', 'Each engine ran the whole set twice. A tilde marks a figure whose repeat differed by more than 20 percent, and a query counts as ahead or behind only when both runs agree.'],
      ],
    },
    build: buildTable,
    limits: { title: 'What HoloDb does not do', line: 'Each row is true today', table: limitsTable },
    deep: { title: 'Go deeper', line: 'The optional plane, constructed workloads, ingest, tenants, scaling slopes, and the 10,000,000-invoice comparison against engines as they ship.', href: '/holodb/evidence/deep-dive/', label: 'Open the deep dive' },
  },
  internal: { source: 'C:\\HoloDbBench\\n100000000 result files and manifest' },
};

// ======================================================================= the deep dive (HoloDb/docs/site.md tables, present-tense prose)
const billingIn = timing(/^Billing query\|SQLite\|DuckDB\|HoloDb embedded/, 'inprocess', 'In process, 10,000,000 invoices: HoloDb embedded against DuckDB and SQLite (median milliseconds, in memory, no index or summary added to any engine)', 2, 1);
billingIn.qualifiers = ['in memory', 'out of the box', ''];
billingIn.resultLabel = 'Result against DuckDB (out of the box)';
for (const r of billingIn.rows) {
  if (MAINTAINED.has(r.label) && r.state === 'win') r.verdict += ' (reads a maintained total)';
  if (r.state === 'loss') r.verdict = r.verdict.replace(/^DuckDB /, 'DuckDB out of the box, ').replace(/^SQLite ahead/, 'SQLite (in memory) ahead');
}
const billingNet = timing(/^Billing query\|SQLite\|SQL Server\|HoloDb server/, 'networked', 'Networked, 10,000,000 invoices: HoloDb server against SQL Server LocalDB and SQLite (median milliseconds, SQL Server with primary keys only)', 2, 1);
billingNet.qualifiers = ['in memory', 'primary keys only', ''];
billingNet.resultLabel = 'Result against SQL Server (primary keys only)';
for (const r of billingNet.rows) {
  if (MAINTAINED.has(r.label) && r.state === 'win') r.verdict += ' (reads a maintained total)';
  if (r.state === 'loss') r.verdict = r.verdict.replace(/^SQL Server,/, 'SQL Server (primary keys only),');
}

function scalingSection() {
  const seq = seriesOf('batch', 'batch-time', 'sequential'), app = seriesOf('batch', 'batch-time', 'apply');
  const seqC = seriesOf('batch', 'batch-commits', 'sequential'), appC = seriesOf('batch', 'batch-commits', 'apply');
  const adds = seriesOf('flags', 'flags-time', 'adds'), words = seriesOf('flags', 'flags-time', 'words');
  const mp = seriesOf('measures', 'measures-passes', 'per-plane'), mm = seriesOf('measures', 'measures-passes', 'many');
  const mtp = seriesOf('measures', 'measures-time', 'per-plane'), mtm = seriesOf('measures', 'measures-time', 'many');
  const build = panelOf('measures').resultBuild;
  const gp = seriesOf('groupbys', 'groupbys-passes', 'per-plane'), gm = seriesOf('groupbys', 'groupbys-passes', 'many');
  const gtp = seriesOf('groupbys', 'groupbys-time', 'per-plane'), gtm = seriesOf('groupbys', 'groupbys-time', 'many');
  const eng = seriesOf('write-read', 'write-read-time', 'engine'), pl = seriesOf('write-read', 'write-read-time', 'plane');
  const [wLo, wHi] = yRange(words), [bLo, bHi] = yRange(build), [nLo] = yRange({ points: sc.null.points.slice(0, 1) }), rest = yRange({ points: sc.null.points.slice(1) });
  const lastX = (s) => grp(s.points[s.points.length - 1].x);
  const wrRows = eng.points.map((p, i) => [grp(p.x), usText(p.y), usText(pl.points[i].y), sc.placebo[i].ratio.toFixed(2)]);
  const slopes = {
    id: 'scaling-slopes', kind: 'matrix', caption: 'Fitted slope of log cost on log size: one step per item, and one step for the whole set',
    columns: ['What grows, and what is measured', 'One step per item', 'One step for the whole set'],
    rows: [
      ['Changes in a batch, time', slopeCell(seq, 'time'), slopeCell(app, 'time')],
      ['Changes in a batch, commit groups written', slope(seqC), slope(appC)],
      ['Flag words added to a plane, time', slopeCell(adds, 'time'), slopeCell(words, 'time')],
      ['Measures in a group-by, transform passes', slope(mp), slope(mm)],
      ['Measures in a group-by, time', slopeCell(mtp, 'time'), slopeCell(mtm, 'time')],
      ['Group-bys in one refresh, transform passes', slope(gp), slope(gm)],
      ['Group-bys in one refresh, time', slopeCell(gtp, 'time'), slopeCell(gtm, 'time')],
    ],
  };
  const writeRead = {
    id: 'scaling-write-read', kind: 'matrix', caption: 'One insert, then the same two-column group-by, per pair, as the table grows',
    columns: ['Rows in the table', 'Engine alone', 'With the plane', 'Placebo ratio (a query the plane declines; the band is 0.8 to 1.25)'],
    rows: wrRows,
  };
  return {
    id: 'scaling', title: 'Scaling',
    intro: [
      `Each line is a recorded run on logarithmic axes, and its slope says how the cost grows: a slope of 1.00 is one step for every item, and a slope near zero is a cost that does not depend on how many items there are. Only the engine and the plane appear here; no other database is timed in this section.`,
      `The control, a fixed amount of work repeated at each size, fits a slope of ${sc.null.slope.toFixed(2)} because its first point includes start-up compilation (${nLo} microseconds against ${rest[0]} to ${rest[1]} for the other four). The first point of every time series carries the same start-up cost, so a time slope under about 0.4 is shown as nearly flat and is not read as an exact exponent. Counts (commit groups, transform passes) carry no such noise and are exact. The write-then-read pairs are ${sc.repeats.writeThenRead} repetitions of one insert followed by the same two-column group-by at each table size, one run per size, with a placebo pair beside them.`,
    ],
    diagram: { id: 'holodb-scaling', kind: 'scaling-curve', stageLabel: 'Scaling curves. Choose what grows, then use the left and right arrows to read each size.', caption: 'Drawn from the recorded runs, which the tables below hold. Both axes are logarithmic.' },
    tables: [slopes, writeRead],
    notes: [
      `Write, then read. With the plane attached, one insert followed by one two-column group-by costs ${usText(pl.points[0].y)}, ${usText(pl.points[1].y)} and ${usText(pl.points[2].y)} per pair at ${grp(pl.points[0].x)}, ${grp(pl.points[1].x)} and ${grp(pl.points[2].x)} rows: flat across a tenfold growth in the table. The engine alone costs ${usText(eng.points[0].y)}, ${usText(eng.points[1].y)} and ${usText(eng.points[2].y)} for the same pair, a slope of ${slope(eng)}, because the group-by scans the table. A placebo pair that runs a query the plane declines (so both arms take the same path) reads ${sc.placebo.map((p) => p.ratio.toFixed(2)).join(', ')}, inside its band.`,
      `Changes in a batch. B changes applied one statement at a time cost one lock, one log commit and one plane update each: the time slope is ${slope(seq)}, and the commit groups written equal the changes made (slope ${slope(seqC)}). One ApplyChanges call writes exactly one commit group at every B (slope ${slope(appC)}), and its time grows far more slowly than the statements do. It keeps growing: from ${xAt(app, 3)} to ${xAt(app, 4)} changes its slope is ${lastDecade(app)}, because the per-row work on the table remains and only the fixed costs are shared.`,
      `Flag words. Adding k flag words to a 14-bit plane with k separate Add calls has a slope of ${slope(adds)}. One AddFlagWords call, which is a Phasor Pleroma operation, takes ${Math.round(wLo)} to ${Math.round(wHi)} microseconds from k = ${xAt(words, 0)} to k = ${lastX(words)}, nearly flat.`,
      `Measures in a group-by. A group-by that sums M measures reads 1 + 3M planes, because each measure is held as three limbs beside the count plane: ${mp.points[0].y} planes at M = ${xAt(mp, 0)} and ${mp.points[mp.points.length - 1].y} at M = ${lastX(mp)}. Read plane by plane, the transform passes grow with a slope of ${slope(mp)}; read by one multi-plane call (Pleroma FlagsMany) they are ${mm.points.map((p) => p.y)[0]} at every M (slope ${slope(mm)}). Time falls from a slope of ${slope(mtp)} to ${slope(mtm)}, and no lower, because the answer itself grows with M: a repeat that skips the transform and only builds the result takes ${Math.round(bLo)} to ${Math.round(bHi)} microseconds.`,
      `Group-bys in one refresh. With four measures, refreshing G group-bys together still costs one transform per group-by: the passes grow with a slope of ${slope(gp)} read plane by plane and ${slope(gm)} read by one multi-plane call, and the time with ${slope(gtp)} and ${slope(gtm)}. Each distinct group-by is its own read.`,
    ],
    costs: [
      `Group-bys in one refresh: each distinct group-by is its own read, so the time and the transform passes grow in proportion to the number of group-bys (time slope ${slope(gtm)} with the multi-plane read).`,
      `Measures in a group-by: the transform passes stay at one, but the time still grows (slope ${slope(mtm)}), because the answer grows with the number of measures.`,
      `Changes in a batch: a bigger batch costs more (slope ${lastDecade(app)} from ${xAt(app, 3)} to ${xAt(app, 4)} changes); only the fixed costs, one lock, one commit and one plane pass, are shared.`,
    ],
  };
}
const ingestTable = matrix(/^Per round \(ms\)\|SQLite/, 'ingest-rounds', 'Per round, median milliseconds (SQLite, DuckDB and SQL Server out of the box)');
ingestTable.rows = ingestTable.rows.filter((r) => !/^trickle pair/.test(r[0]));
if (ingestTable.rows.length !== 2) throw new Error('ingest table: expected panel and round total rows');

const deep = {
  id: 'holodb-deep-dive', product: 'holodb', slug: '/holodb/evidence/deep-dive/', layout: 'deep',
  scope: 'The plane and constructed workloads',
  title: 'HoloDb deep dive',
  description: 'The optional plane, constructed workloads, ingest, tenants and scaling slopes, and the 10,000,000-invoice comparison against engines as they ship.',
  claim: 'The numbers behind the plane: what it serves, what it declines, how its cost grows with size, and the 10,000,000-invoice comparison against engines as they ship. These workloads are built to suit the plane, so they sit apart from the 100,000,000-invoice comparison.',
  tieBelow: 1.15,
  method: {
    workload: 'A billing ledger (invoices joined to customers, sixteen queries) at 10,000,000 invoices with every engine out of the box, and three constructed workloads for the plane: a live dashboard, ingest while querying, and multi-tenant slices and joins.',
    engines: ['SQLite (in memory, always listed first)', 'DuckDB (in process, in memory, default settings)', 'SQL Server 2022 LocalDB (tables in tempdb on disk, primary keys only)', 'HoloDb embedded (in process)', 'HoloDb server (networked over TLS on the loopback interface)', 'HoloDb engine: the same database without a plane; HoloDb plane: a database with the optional plane attached'],
    verification: 'Before any query is timed, its full result set is normalised and compared across the engines. Every timed result is identical. After each plane workload the plane is compared with a rebuild from the columns and found equal.',
    statistic: 'Medians of repeated calls through each engine\'s typed client API, execute plus consume every row. A cell whose own two timing loops disagree by more than 25 percent is marked noisy, and no ratio is drawn from it. A difference under 1.15x counts as a tie.',
    machine: 'One 16-core laptop CPU (Intel Core i9-11950H). The billing comparison ran with nothing else heavy open. The plane workloads ran with ordinary desktop applications and the SQL Server service open. The scaling runs used a Release build, and each point is the median of three repetitions.',
    memory: 'Every figure here is an in-memory HoloDb database with no write-ahead log; SQLite and DuckDB ran in memory and SQL Server LocalDB used tempdb on disk. A database opened with a write-ahead log flushes each committed statement to disk, which adds one flush per commit, set by the disk. Bulk-load rates compare unlike modes: typed arrays into memory, an appender, inserts in one transaction, and a bulk copy into tempdb.',
  },
  sections: [
    {
      id: 'billing', title: 'Billing ledger at 10,000,000 invoices, every engine out of the box',
      intro: [
        'The ledger has 10,000,000 invoices and 200,000 customers, about 50 invoices each, over 24 months, four regions and five statuses, with money held as integer cents. SQLite and DuckDB run in memory with default settings, SQL Server 2022 LocalDB with primary keys only, and no index or summary table is added for the queries. The optional plane is not attached.',
        'Bulk load, rows per second: DuckDB 1,159,279; HoloDb server 1,051,473; SQLite 726,050; SQL Server 693,231; HoloDb embedded 679,271.',
      ],
      tables: [billingIn, billingNet],
      notes: [
        'HoloDb embedded is faster than in-memory SQLite on 15 of the 16 queries. Against DuckDB out of the box, counting a difference under 1.15x as a tie, it is faster on 13 queries, tied on 2 and slower on 1. The rollups and the lookup read a maintained total or the primary-key index, so they take microseconds and do not grow with the ledger, while the other engines scan. The benchmark prints two decimals, so those cells are lower bounds. The joins and scans are the rows where the engines do the same work.',
        'HoloDb server is faster than SQL Server with primary keys only on 15 of the 16 queries, including all five joins. Both are networked services paying a client and server round trip. The server figures are medians of five calls, so they include the first-call cost of the network path: a longer loop through the same client measures the rollups and the lookup at a steady 0.06 to 0.10 ms, and a point query pays a fixed 0.07 ms of client, TLS, socket and result encoding that the same query does not pay embedded (1 to 3 microseconds).',
        'Total invoiced across 10,000,000 invoices (SELECT SUM(amount_cents)): SQLite 414.54 ms, DuckDB 2.56 ms, SQL Server 943.63 ms, HoloDb under 0.01 ms, because a maintained total is read instead of 10 million rows.',
      ],
      costs: [
        'Look up one invoice: SQLite is ahead of embedded HoloDb, 0.01 ms against 0.02 ms at the benchmark\'s two-decimal resolution.',
        'Revenue by signup cohort (a join): DuckDB is ahead of embedded HoloDb by 1.25x, a parallel scan of the 10 million invoices against DuckDB\'s columnar engine. Two other queries tie with DuckDB (big-ticket invoices and enterprise revenue).',
        'Look up one invoice over the network: SQL Server answers in 0.07 ms and HoloDb server in 0.18 ms. About 0.07 ms of HoloDb\'s time is client, TLS and socket cost outside the engine.',
      ],
    },
    {
      id: 'plane-billing', title: 'The optional plane on the billing ledger',
      intro: ['The plane is opt-in per table. This run attaches one to the invoices table: dimensions region (4 values), status (5) and month (24), a 10-bit joint, and the measure amount_cents. Building it for 10,000,000 rows takes 741 ms (749 ms on the server\'s database) and its cells take 34,528 bytes. All 16 results are identical across the engines.'],
      tables: [matrix(/^Query\|SQLite\|DuckDB\|HoloDb, plane off\|HoloDb, plane on/, 'plane-billing', 'Plane off and plane on, 10,000,000 invoices, median milliseconds (SQLite and DuckDB out of the box, in memory)')],
      notes: [
        'Through the server the same two queries take 6.30 ms and 34.58 ms with the plane off and 0.11 ms and 0.45 ms with it on (SQL Server: 611.59 ms and 2,898.13 ms).',
        'What the plane serves: filters and multi-column GROUP BY over its dimension columns. What it declines, and why: the big-ticket count (the filter is on a measure, not a dimension), top customers (200,000 distinct customer ids, far over the 1,024-value dimension limit), the five joins (200,000 join keys against a limit of 4,095) and the lookup and largest-invoices queries (no aggregate). The five rollups are left to the engine on purpose, because it reads a maintained total there in microseconds and the plane is slower. Per database, 12 calls were served by the plane and 42 declined.',
        'The cells the plane does not touch run the identical engine path in both columns and differ by 1 to 13 percent typically and by up to 33 percent (big-ticket: 3.01 ms off, 2.26 ms on), so differences of that size are noise.',
      ],
      costs: ['The plane is not used for the rollups: whole-table counts and sums, single-column GROUP BY and MIN and MAX, where the engine already reads a maintained total in microseconds.'],
    },
    {
      id: 'dashboard', title: 'Constructed workload: a live dashboard',
      intro: [
        'The filter combinations cannot be pre-indexed because nobody knows in advance which dimensions a person will cut by, the answers must be exact, and the table keeps changing while it is queried. The workload is 10,000,000 events and 32 ad-hoc slices over six small-domain dimensions (a 14-bit joint) and the measures. Building the plane takes 1,759 ms and its cells take 920,248 bytes. The slices are scalar filters (W), group-bys with and without a filter (G), HAVING (H), COUNT DISTINCT and MIN and MAX with a filter (D), queries outside the plane (X) and whole-table queries the engine already answers from a maintained total.',
      ],
      tables: [
        matrix(/^Class \(queries\)\|Plane vs engine, median/, 'dash-classes', 'Plane against the engine and against the best other engine (SQLite, DuckDB and SQL Server, each out of the box), by class of slice'),
        matrix(/^Query\|DuckDB\|SQL Server\|HoloDb engine\|HoloDb plane$/, 'dash-first', 'First call of each query, median milliseconds (DuckDB and SQL Server out of the box)'),
      ],
      notes: [
        'The best other engine on the W, G, H and D slices is DuckDB in every row shown, out of the box: no indexes added and no summary table built. Three of the nine W cells are noisy, because the engine\'s own two timing loops disagree by more than 25 percent; the class median is as printed.',
        'The first call of each query, which pays any one-time work, is also faster with the plane: the median first-call speed-up over the engine is 16.99x for W, 10.90x for G, 13.97x for H and 10.17x for D.',
      ],
      costs: [
        'The queries the plane declines or leaves to the engine (the X class: a filter on a non-dimension column, COUNT(DISTINCT user_id), a sum of a non-measure column, a top-10 by user, a filter on a measure, a row list and a point lookup by event id) run the engine\'s own path and tie with it: the seven X queries read 0.95x to 1.04x warm. On the first call five of the seven are slower with the plane attached, at 0.42x to 0.84x of the engine\'s first call (single samples).',
      ],
    },
    {
      id: 'ingest', title: 'Constructed workload: ingest while querying',
      intro: ['A 2,000,000-row base table, then 20 rounds of a 10,000-row bulk load, 200 single-row INSERTs and an eight-query dashboard panel. The panel\'s results match in every round. How an insert followed by a query scales with the size of the table is under Scaling.'],
      tables: [ingestTable],
      notes: [],
      costs: ['Writes cost more with the plane attached: the 10,000-row bulk load takes 11.62 ms against 9.85 ms (1.18x), and 200 single-row INSERTs take 1.55 ms against 1.23 ms (1.26x), about 7.8 microseconds against 6.2 per INSERT.'],
    },
    {
      id: 'tenants', title: 'Constructed workload: multi-tenant slices and joins',
      intro: ['Five million events spread over six tenant tables (2,000,000 down to 150,000 rows) and a 2,000-product dimension table. A filter with a COUNT and SUM on one tenant is a median 11.70x faster than the engine and 25.0x faster than the best other engine (DuckDB, SQLite and SQL Server each out of the box). The largest tenant reads 0.171 ms with the plane, 4.263 ms for the engine, 6.312 ms for DuckDB and 195.195 ms for SQL Server.'],
      tables: [matrix(/^Query\|DuckDB\|SQL Server\|HoloDb engine\|HoloDb plane, embedded/, 'tenant-joins', 'The same join repeated, median milliseconds (DuckDB and SQL Server out of the box)')],
      notes: [
        'The join is one statement repeated: tenant events joined to the products table on the product key (about 2,000 keys), with COUNT(*) alone or with two SUMs. The plane serves a repeat from a memo of the exact answer, valid until either of the two tables is written, so the very low figures are for a repeated identical statement with no write in between. A memo hit takes 0.001 to 0.003 ms embedded and 0.071 to 0.084 ms through the server.',
        'Joins with a WHERE or a GROUP BY on the dimension table are not served by the plane; they run the engine\'s path and tie with it (0.93x to 1.02x).',
      ],
      costs: ['A join that is not a memo hit usually costs more through the plane than the engine\'s own join. The first call of a statement costs more through the plane than through the engine in 11 of the 12 join cells: join COUNT 1.016 to 2.921 ms through the plane on the five smaller tenants against 0.179 to 0.573 ms for the engine, and join COUNT with two SUMs 2.348 to 8.134 ms against 1.364 to 3.076 ms. If the tables are written between nearly every read, expect the plane join to cost more than the engine\'s own join; the plane\'s value on such a workload comes from the filters and group-bys.'],
    },
    {
      id: 'million', title: 'The plane against the engine at 1,000,000 rows',
      intro: ['Seventeen query classes on a 1,000,000-row table, with the engine alone, the engine with the plane, and SQLite. The control query (one the plane declines, so both sides run the same engine path) reads 0.81, inside the band of 0.8 to 1.25. The join rows use a very small dimension table: 40 distinct join keys for the two INT joins and 4 for the TEXT join, so they say little about joins over many keys.'],
      tables: [matrix(/^Query class\|SQLite\|HoloDb engine\|HoloDb plane\|Plane vs engine/, 'classes', 'Seventeen query classes at 1,000,000 rows (SQLite in memory; the two INT joins have 40 distinct join keys and the TEXT join has 4)')],
      notes: [
        'The engine alone is faster than in-memory SQLite on every class; the whole-table classes compare a maintained total with a scan. The plane is faster than SQLite on every class it serves. Against the engine it wins where there is a real scan or join to avoid. Outcome counts for the plane against the engine: 7 classes at least 2x faster, 3 ties, 1 between 1.25x and 2x, 4 slower, 2 noisy.',
        'At 100,000 rows the engine is faster than the plane on seven of the 17 classes: the whole-table classes, the single-column GROUP BY and MIN and MAX (0.33x to 0.51x), the two-column GROUP BY (0.26x), GROUP BY with a WHERE (0.41x) and COUNT DISTINCT with a WHERE (0.58x). At 300,000 rows the plane is faster on every filter and join (24x to 1,887x) and on the two-column GROUP BY (1.77x). Default routing sends GROUP BY with a WHERE to the engine below 140,000 rows.',
        'The plane\'s cost when attached, against the engine alone: single-row INSERT 1.4x to 1.9x, UPDATE 1.6x to 1.8x (microseconds that move with the order the two arms run in); batch load of the whole table 1.93 s with the plane against 1.80 s without at 1,000,000 rows; rebuilding the plane from the columns 131 ms; a heap of 139.7 bytes per row with the plane against 135.2 without; the plane\'s cells are a flat 0.92 MB joint slice whatever the row count.',
        'The TEXT-key join is faster than SQLite in the engine alone (39.98 ms against 322.17 ms at 1,000,000 rows, rows identical). It requires both join columns to be TEXT; a join between an INT column and a TEXT column takes the general path and is much slower.',
      ],
      costs: ['With the plane attached, whole-table COUNT(*) (0.48x), whole-table SUM (0.47x), single-column GROUP BY (0.43x) and MIN and MAX (0.53x) are slower than the engine alone, because the engine reads a maintained total in microseconds.'],
    },
    scalingSection(),
    {
      id: 'live', title: 'Dashboard reads during live ingest, and opening a database',
      intro: [
        'Whole-table counts and sums, and GROUP BY on a column with up to 64 distinct values, are served from a snapshot republished after every write, without taking the engine\'s write lock. On a 16-core laptop that was not idle, 2,000,000 events ingested in 1,000-row batches at 1.16 million events a second while 4 concurrent readers queried: median read 0.009 to 0.09 ms, p99 0.8 ms (group by region), 1.1 ms (sum) and 6.6 ms (count), worst single read 6.7 ms.',
        'Opening a database of 1,000,000 rows (a 43 MB log) takes about 2.0 s by replaying its write-ahead log (1.95 to 2.05 s across two runs) and about 1.1 s from a checkpoint (1.09 to 1.11 s), with the loaded state checked correct and a heap of about 110 bytes per row, in a newly started process. This is the engine\'s own recovery and checkpoint; it does not use the plane or the page store.',
      ],
      tables: [],
      notes: [],
      costs: ['A GROUP BY with more than 64 distinct values (the per-minute series in the same test has 167) takes the normal locked path and waits behind the ingest for up to 0.9 s (p99 0.9 s).'],
    },
    {
      id: 'checks', title: 'Checks behind the figures',
      intro: ['Every release is measured against the one before it: each benchmark cell is run twice on each build, and a cell counts as a regression only if it is worse than 25 percent (or the baseline\'s own spread) in both pairs. The check covers the 10,000,000-invoice comparison, the plane benchmark at 100,000, 300,000 and 1,000,000 rows, and opening a 1,000,000-row database from its log and from a checkpoint, and finds no cell slower.'],
      tables: [],
      notes: [
        'Queries the plane declines or leaves to the engine stay within noise: of the 16 queries that run the same path in both arms, none falls outside a band of 0.8x to 1.25x, with class medians of 0.97 and 0.99. A deliberately wrong plane (the measure column shuffled at 1,000,000 rows) is caught on every query it changes and matches on every query it does not: 0 failures of 32.',
        'The aggregate plane returns row-for-row the same answers as the engine and SQLite across the parity suite (2,853 checks at 2,000 rows, and the suite also passes at 5,000 and 100,000 rows), including a random insert, update, delete, rollback and reopen log, and sums past 64 bits. Nine deliberate mutations (a dropped carry, a missed delete, a stale join limb and others) each turn the suite red. Sums past 64 bits are never wrapped: the plane raises an error and PleromaSumExact returns the exact 128-bit total.',
        'The page store passes 72 of 72 checks: exactly-once replay after a crash, a torn journal tail discarded, restore points that rewind without recounting, and repair of a lost page from its parity group. Eight deliberate mutations and a tiny-cache case each turn the suite red.',
        'A paged table checks every 8 KB data page against a checksum kept in a small side file. In a test that flipped 61 bits in the data file of a 91 MB paged table, none gave a silently different answer: 36 were reported with an error that names the page, and 25 were in pages never read.',
        'Atomicity and crash recovery are covered by a 48-check suite (a failing log append is undone in full, a reopen from the log reproduces the committed state) and a 58-check transaction suite plus about 600 randomised comparisons against SQLite; three deliberate mutations each turn it red.',
      ],
      costs: [],
    },
  ],
  limits: [
    'Each figure comes from a single run on a single machine, and the plane sections use constructed workloads: one synthetic table (9 columns, four small-domain dimensions, a 14-bit joint). The plane serves only the columns you attach. A plane made with EnablePleroma lives in RAM and is not persisted; a plane made with SQL, and the summaries the planner builds by itself, are saved with the table.',
    'In the engine alone, GROUP BY with a WHERE is about 3 ms at 1,000,000 rows because the filter and the grouping are separate passes. The dense-code GROUP BY covers INT and TEXT columns whose combined distinct count is at most 65,536, and anything else uses a slower string-key path. REAL aggregates over a filter use a scan.',
    'The first DDL statement inside a transaction takes a snapshot of every in-memory table, so it costs time proportional to the data. After a ROLLBACK of deleted rows the table holds the same rows but not necessarily in the same internal order.',
    'The aggregate plane does not serve the billing joins at 10,000,000 invoices (200,000 join keys against its limit of 4,095) or the top-customers query; the keyed summary and the pre-grouped join totals of paged tables do. A plane join over fewer keys is served, as in the tenant workload.',
    'The scaling lines are fits over four or five sizes on one machine, and the write-then-read pairs are one run per table size of one repeated group-by. Time slopes under about 0.4 are not read as exact exponents.',
  ],
  internal: { source: 'HoloDb/docs/site.md (parsed tables); run ids, dates and experiment names deliberately not carried' },
};

fs.writeFileSync(path.join(DATA, 'evidence', 'holodb.json'), JSON.stringify(story, null, 2) + '\n', 'utf8');
fs.writeFileSync(path.join(DATA, 'evidence', 'holodb-deep-dive.json'), JSON.stringify(deep, null, 2) + '\n', 'utf8');
console.log('wrote holodb.json (story) and holodb-deep-dive.json', deep.sections.length, 'sections');
