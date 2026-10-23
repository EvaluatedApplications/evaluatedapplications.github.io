// One-off migration helper: builds data/evidence/holodb.json (ONE evidence set: method stated once, sections, losses)
// from the owner's HoloDb/docs/site.md. Every number in a table is parsed from that file, never retyped; the prose
// around them is written here in the current-state voice (no experiment ids, run numbers or dates).
//   node web/scripts/migrate/extract-holodb-evidence.mjs [path-to-site.md]
import fs from 'node:fs';
import path from 'node:path';
import { MONOREPO, DATA, readJson } from '../lib/data.mjs';

// The Scaling section is written from data/diagrams/holodb-scaling.json (run node web/scripts/migrate/extract-holodb-scaling.mjs first),
// so no slope or recorded point is typed here.
const sc = readJson(path.join(DATA, 'diagrams', 'holodb-scaling.json'));
const panelOf = (id) => sc.panels.find((p) => p.id === id);
const seriesOf = (pid, cid, sid) => panelOf(pid).charts.find((c) => c.id === cid).series.find((s) => s.id === sid);
const grp = (n) => Number(n).toLocaleString('en-GB');
const slope = (s) => s.slope.toFixed(2);
const slopeCell = (s, kind) => (kind === 'time' && s.nearlyFlat ? `${slope(s)} (nearly flat)` : slope(s));
const usText = (v) => (v >= 1000 ? `${(v / 1000).toFixed(2)} ms` : `${Math.round(v)} us`);
const yRange = (s) => [Math.min(...s.points.map((p) => p.y)), Math.max(...s.points.map((p) => p.y))];
const xAt = (s, i) => grp(s.points[i].x);
const lastDecade = (s) => { const a = s.points[s.points.length - 2], b = s.points[s.points.length - 1]; return (Math.log10(b.y / a.y) / Math.log10(b.x / a.x)).toFixed(2); };

const src = process.argv[2] || path.join(MONOREPO, 'HoloDb', 'docs', 'site.md');
const md = fs.readFileSync(src, 'utf8').replace(/\r\n/g, '\n');

// ---- markdown tables ----
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

// Every verdict carries the rival's setup: the column qualifiers and the Result heading say which DuckDB and which SQL Server these are
// (neither is tuned here; the tuned runs are in the 100,000,000-row section), and a ratio on a row that reads a maintained total says so.
const MAINTAINED = new Set(['invoices by status', 'revenue by region', 'total invoiced', 'revenue by month', 'invoice count']);
const billingIn = timing(/^Billing query\|SQLite\|DuckDB\|HoloDb embedded/, 'inprocess', 'In process, 10,000,000 invoices: HoloDb embedded against DuckDB and SQLite (median milliseconds, in memory, no index or summary added to any engine, one run on one laptop)', 2, 1);
billingIn.qualifiers = ['in memory', 'out of the box', ''];
billingIn.resultLabel = 'Result against DuckDB (out of the box)';
for (const r of billingIn.rows) {
  if (MAINTAINED.has(r.label) && r.state === 'win') r.verdict += ' (reads a maintained total)';
  if (r.state === 'loss') r.verdict = r.verdict.replace(/^DuckDB /, 'DuckDB out of the box, ').replace(/^SQLite ahead/, 'SQLite (in memory) ahead');
}
const billingNet = timing(/^Billing query\|SQLite\|SQL Server\|HoloDb server/, 'networked', 'Networked, 10,000,000 invoices: HoloDb server against SQL Server LocalDB and SQLite (median milliseconds, SQL Server not tuned, one run on one laptop)', 2, 1);
billingNet.qualifiers = ['in memory', 'primary keys only', ''];
billingNet.resultLabel = 'Result against SQL Server (primary keys only)';
for (const r of billingNet.rows) {
  if (MAINTAINED.has(r.label) && r.state === 'win') r.verdict += ' (reads a maintained total)';
  if (r.state === 'loss') r.verdict = r.verdict.replace(/^SQL Server,/, 'SQL Server (primary keys only),');
}

// ---- 100,000,000 invoices: HoloDb out of the box, HoloDb declared, tuned DuckDB and tuned SQL Server ----
function hundredMillion() {
  const t = find(/^Warm median, ms\|DuckDB tuned\|SQL Server tuned\|HoloDb out of the box\|HoloDb declared/);
  const COLS = { duck: 0, sql: 1, oob: 2, decl: 3 };
  const rows = t.rows.map((r) => ({ label: strip(r[0]), s: r.slice(1).map((c) => c.trim()), v: r.slice(1).map(num) }));
  const row = (label) => { const x = rows.find((r) => r.label === label); if (!x) throw new Error('100M row not found: ' + label); return x; };
  const cells = (labels, col) => labels.map((l) => row(l).s[COLS[col]]);
  const list = (a) => (a.length < 2 ? a.join('') : `${a.slice(0, -1).join(', ')} and ${a[a.length - 1]}`);
  // a difference of 20 percent or less is equal; the two sub-millisecond cells the repeat did not confirm are left out
  const SKIP = new Set(['invoice count', 'look up one invoice']);
  const tally = (arm, rival) => {
    let w = 0, e = 0, l = 0;
    for (const r of rows) {
      if (SKIP.has(r.label)) continue;
      const mine = r.v[COLS[arm]], theirs = r.v[COLS[rival]];
      if (theirs > 1.2 * mine) w++; else if (mine > 1.2 * theirs) l++; else e++;
    }
    return { w, e, l, n: w + e + l };
  };
  const dd = tally('decl', 'duck'), ds = tally('decl', 'sql'), od = tally('oob', 'duck'), os = tally('oob', 'sql');
  const sqlAheadDeclared = ['revenue by segment (JOIN)', 'enterprise revenue (JOIN)', 'revenue by signup cohort (JOIN)', 'revenue by segment and status (JOIN)'];
  const oobBehindDuck = ['overdue outstanding', 'big-ticket invoices', 'largest invoices', 'top customers by revenue', 'revenue by region and status'];
  const near = 'revenue by segment and status (JOIN)';
  const sqlRange = (labels, col) => { const v = labels.map((l) => row(l).v[COLS[col]]); return [Math.min(...v), Math.max(...v)]; };
  const [sLo, sHi] = sqlRange(sqlAheadDeclared, 'sql'), [dLo, dHi] = sqlRange(sqlAheadDeclared, 'decl');
  const f2s = (n) => n.toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const rollups = ['invoices by status', 'revenue by month', 'revenue by region', 'total invoiced'];
  const [rLoO, rHiO] = sqlRange(rollups, 'oob'), [rLoD, rHiD] = sqlRange(rollups, 'duck');
  const table = matrix(/^Warm median, ms\|DuckDB tuned/, 'hundred-million', 'Warm median milliseconds at 100,000,000 invoices: tuned DuckDB, tuned SQL Server, HoloDb out of the box and HoloDb declared (one run, one session, one 16-core laptop)', { columns: ['Query', ...t.header.slice(1).map(clean)] });
  return {
    counts: { dd, ds, od, os },
    id: 'hundred-million',
    title: '100,000,000 invoices: HoloDb out of the box, HoloDb declared, and tuned competitors',
    intro: [
      'The same 16 billing queries on 100,000,000 invoices (2,000,000 customers), one session on one machine: an 11th Gen Intel Core i9-11950H at 2.60 GHz, 16 logical cores, 34.1 GB of memory, Windows 11, .NET 8.0.27. Each engine ran in its own process. Before any timing, the result of every query was hashed, and all engines returned identical results on all 16 queries.',
      'The four arms. HoloDb out of the box: a paged table with nothing declared and nothing saved beside it; the planner may earn structures from repeated scans (here it saw each statement four times: the cold run and three warm runs). HoloDb declared: a paged table with the summary, sorted index and keyed summary declared and saved beside the data, opened from those files (a restored state). DuckDB 1.5.5 tuned: the table written in customer_id order, ANALYZE, explicit thread and memory settings. SQL Server 2022 Developer tuned: a nonclustered columnstore index, a descending index on amount_cents, and indexed views over region, status and month, customer, segment, cohort and customer name, with max server memory 5,120 MB in a container. We chose the tuning after a 10,000,000-row trial and fixed it before the 100,000,000-row build. Cold means the first run after the operating system\'s file cache was evicted by reading a 34 GB file (and, for SQL Server, dropping its buffers); warm is the median of three runs, and the table shows warm.',
    ],
    tables: [table],
    notes: [
      `Counting a difference of 20 percent or less as equal, and leaving out the invoice count and the lookup (both tuned competitors moved more than 20 percent between the two runs of the same arm there, so no ratio is drawn from those cells), the ${dd.n} remaining queries come out as follows. HoloDb declared is ahead of tuned DuckDB on ${dd.w}, equal on ${dd.e} and behind on ${dd.l}; against tuned SQL Server it is ahead on ${ds.w}, equal on ${ds.e} and behind on ${ds.l}. HoloDb out of the box is ahead of tuned DuckDB on ${od.w}, equal on ${od.e} and behind on ${od.l}; against tuned SQL Server it is ahead on ${os.w}, equal on ${os.e} and behind on ${os.l}.`,
      `Why out of the box loses where it does: its first touches of a query are scans, and it earns a structure only after repeats. The four rollups it answers from a maintained total (${list(rollups)}) read in ${f2s(rLoO)} to ${f2s(rHiO)} ms, against ${f2s(rLoD)} to ${f2s(rHiD)} ms for tuned DuckDB scanning 100,000,000 rows. That gap is a maintained total against a scan, and it says nothing about queries that scan.`,
      'In a later run on the same machine, with the pre-grouped join totals present in the declared database (warm medians of three, one run, the competitors were not timed again), the four dimension joins read 0.19, 0.20, 0.23 and 0.25 ms, top customers 0.58 ms and top named customers 0.93 ms. The SQL Server figures to compare with are the ones in the table above.',
      `Load once and size: HoloDb out of the box 165 s and 4.94 GB, HoloDb declared 192 s and 6.84 GB, DuckDB tuned 168 s and 2.46 GB, SQL Server tuned 484 s and 8.88 GB.`,
    ],
    losses: [
      `Tuned SQL Server is ahead of HoloDb declared on four of the five dimension joins (${list(sqlAheadDeclared.map((l) => l.replace(' (JOIN)', '')))}): ${f2s(sLo)} to ${f2s(sHi)} ms against ${f2s(dLo)} to ${f2s(dHi)} ms. It is also ahead on top customers by revenue, ${row('top customers by revenue').s[COLS.sql]} ms against ${row('top customers by revenue').s[COLS.decl]} ms. Both are warm medians from the same session.`,
      `HoloDb out of the box is slower than tuned DuckDB on ${list(oobBehindDuck.map((l) => l.replace(' (JOIN)', '')))}: ${list(cells(oobBehindDuck, 'oob'))} ms against ${list(cells(oobBehindDuck, 'duck'))} ms. On revenue by segment and status it reads ${row(near).s[COLS.oob]} ms against ${row(near).s[COLS.duck]} ms, which is inside the 20 percent counted as equal.`,
      `HoloDb out of the box is behind tuned SQL Server on ${os.l} of the ${os.n} compared queries.`,
    ],
  };
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
    id: 'scaling',
    title: 'Scaling',
    intro: [
      `This section measures cost against size instead of at one size. Each line is a recorded run on logarithmic axes and its slope says how the cost grows: a slope of 1.00 is one step for every item, and a slope near zero is a cost that does not depend on how many items there are. Only the engine and the plane appear here; no other database is timed in this section.`,
      `How to read a slope. The run's own control, a fixed amount of work repeated at each size, fitted a slope of ${sc.null.slope.toFixed(2)} because its first point includes start-up compilation (${nLo} microseconds against ${rest[0]} to ${rest[1]} for the other four). The first point of every time series carries the same start-up cost, so a time slope under about 0.4 is shown as nearly flat and is not read as an exact exponent. Counts (commit groups, transform passes) carry no such noise and are exact. The write-then-read pairs are ${sc.repeats.writeThenRead} repetitions of one insert followed by the same two-column group-by at each table size, one run per size, with a placebo pair beside them.`,
    ],
    diagram: { id: 'holodb-scaling', kind: 'scaling-curve', stageLabel: 'Scaling curves. Choose what grows, then use the left and right arrows to read each size.', caption: 'Drawn from the recorded runs, which the tables below hold. Both axes are logarithmic.' },
    tables: [slopes, writeRead],
    notes: [
      `Write, then read. With the plane attached, one insert followed by one two-column group-by costs ${usText(pl.points[0].y)}, ${usText(pl.points[1].y)} and ${usText(pl.points[2].y)} per pair at ${grp(pl.points[0].x)}, ${grp(pl.points[1].x)} and ${grp(pl.points[2].x)} rows: flat across a tenfold growth in the table. The engine alone costs ${usText(eng.points[0].y)}, ${usText(eng.points[1].y)} and ${usText(eng.points[2].y)} for the same pair, a slope of ${slope(eng)}, because the group-by scans the table. The group-by is the same statement in every pair, and a placebo pair that runs a query the plane declines (so both arms take the same path) read ${sc.placebo.map((p) => p.ratio.toFixed(2)).join(', ')}, inside its band.`,
      `Changes in a batch. B changes applied one statement at a time cost one lock, one log commit and one plane update each: the time slope is ${slope(seq)}, and the commit groups written equal the changes made, ${xAt(seqC, 0)} groups for ${xAt(seqC, 0)} changes up to ${lastX(seqC)} for ${lastX(seqC)} (slope ${slope(seqC)}). One ApplyChanges call writes exactly one commit group at every B (slope ${slope(appC)}), and its time grows far more slowly than the statements do. It does not stop growing: from ${xAt(app, 3)} to ${xAt(app, 4)} changes its slope is ${lastDecade(app)}, because the per-row work on the table remains and only the fixed costs are shared.`,
      `Flag words. Adding k flag words to a 14-bit plane with k separate Add calls has a slope of ${slope(adds)}. One AddFlagWords call, which is a Phasor Pleroma operation, takes ${Math.round(wLo)} to ${Math.round(wHi)} microseconds from k = ${xAt(words, 0)} to k = ${lastX(words)}, nearly flat.`,
      `Measures in a group-by. A group-by that sums M measures reads 1 + 3M planes, because each measure is held as three limbs beside the count plane: ${mp.points[0].y} planes at M = ${xAt(mp, 0)} and ${mp.points[mp.points.length - 1].y} at M = ${lastX(mp)}. Read plane by plane, the transform passes grow with a slope of ${slope(mp)}; read by one multi-plane call (Pleroma FlagsMany) they are ${mm.points.map((p) => p.y)[0]} at every M (slope ${slope(mm)}). Time falls from a slope of ${slope(mtp)} to ${slope(mtm)}, and no lower, because the answer itself grows with M: a repeat that skips the transform and only builds the result takes ${Math.round(bLo)} to ${Math.round(bHi)} microseconds, so what remains is bound by the size of the output.`,
      `Group-bys in one refresh. With four measures, refreshing G group-bys together still costs one transform per group-by: the passes grow with a slope of ${slope(gp)} read plane by plane and ${slope(gm)} read by one multi-plane call, and the time with ${slope(gtp)} and ${slope(gtm)}. Each distinct group-by is its own read, so this cost still grows in proportion to the number of group-bys. That is the open item in this section.`,
    ],
    losses: [
      `Group-bys in one refresh: each distinct group-by is its own read, so the time and the transform passes still grow in proportion to the number of group-bys (slope ${slope(gtm)} in time with the multi-plane read).`,
      `Measures in a group-by: the transform passes stay at one, but the time still grows (slope ${slope(mtm)}), because the answer grows with the number of measures.`,
      `Changes in a batch: a bigger batch still costs more (slope ${lastDecade(app)} from ${xAt(app, 3)} to ${xAt(app, 4)} changes); only the fixed costs, one lock, one commit and one plane pass, are shared.`,
    ],
  };
}

// The per-round table without its trickle-pair row: that row's ratio is replaced by the write-then-read curve under Scaling.
const ingestTable = matrix(/^Per round \(ms\)\|SQLite/, 'ingest-rounds', 'Per round, median milliseconds (SQLite, DuckDB and SQL Server out of the box, one run)');
ingestTable.rows = ingestTable.rows.filter((r) => !/^trickle pair/.test(r[0]));
if (ingestTable.rows.length !== 2) throw new Error('ingest table: expected panel and round total rows');

const HM = hundredMillion();
const { counts: _hmCounts, ...HM_SECTION } = HM;
const record = {
  id: 'holodb',
  product: 'holodb',
  slug: '/holodb/evidence/',
  scope: 'Rivals as shipped and tuned, one run each',
  title: 'HoloDb benchmarks',
  description: 'HoloDb against SQLite, DuckDB and SQL Server at 10,000,000 invoices, then against tuned DuckDB and SQL Server at 100,000,000, with the plane, writes and checks.',
  claim: `On a 10,000,000-invoice ledger joined to 200,000 customers, the dashboard rollups read in microseconds in the embedded engine because they read a maintained total, and every result is identical across the engines. That comparison sets DuckDB and SQL Server up as they ship. At 100,000,000 invoices against tuned DuckDB and tuned SQL Server, HoloDb declared is behind tuned SQL Server on ${HM.counts.ds.l} of ${HM.counts.ds.n} compared queries and HoloDb out of the box is behind tuned DuckDB on ${HM.counts.od.l} of ${HM.counts.od.n}. Every query where HoloDb is slower is listed beside the wins.`,
  tieBelow: 1.15,
  extraTiles: [
    { value: `${HM.counts.ds.w} / ${HM.counts.ds.e} / ${HM.counts.ds.l}`, label: `HoloDb declared against tuned SQL Server at 100,000,000 invoices: ahead, equal, behind, on ${HM.counts.ds.n} compared queries` },
    { value: `${HM.counts.od.w} / ${HM.counts.od.e} / ${HM.counts.od.l}`, label: `HoloDb out of the box against tuned DuckDB at 100,000,000 invoices: ahead, equal, behind, on ${HM.counts.od.n} compared queries` },
  ],
  method: {
    workload: 'A billing business\'s invoice ledger (a normalised invoices fact joined to a customers dimension) with sixteen queries: five rollups, a single-invoice lookup, five single-table scans and five joins to the customer dimension. It runs at two sizes. At 10,000,000 invoices every engine ran as it ships, with default settings, no indexes added (primary keys only) and no summary or rollup tables built for the queries, so DuckDB and SQL Server are untuned there. At 100,000,000 invoices (2,000,000 customers) DuckDB and SQL Server are tuned, and HoloDb appears twice: out of the box (nothing declared, nothing saved beside the table) and declared (summary, sorted index and keyed summary saved beside the data, a restored state). The plane sections use three constructed workloads: a live dashboard, ingest while querying, and multi-tenant slices and joins, with every other engine out of the box.',
    engines: ['SQLite (in memory, the external reference, always listed first)', 'DuckDB (in process, in memory; untuned at 10,000,000 invoices, tuned DuckDB 1.5.5 at 100,000,000)', 'SQL Server 2022 LocalDB (tables in tempdb on disk, primary keys only; at 100,000,000 invoices SQL Server 2022 Developer in a container with a columnstore index, a descending index on amount_cents and indexed views)', 'HoloDb embedded (in process)', 'HoloDb server (networked over TLS on the loopback interface)', 'HoloDb engine: the same database without a plane; HoloDb plane: a database with the optional Pleroma plane attached', 'HoloDb out of the box and HoloDb declared: the two paged-table arms of the 100,000,000-invoice comparison.'],
    verification: 'Before any query is timed, its full result set is normalised (at 100,000,000 invoices, hashed) and compared across the engines. Every timed result is identical, including on the queries where HoloDb is slower. After each plane workload the plane is compared with a rebuild from the columns and found equal.',
    statistic: 'Medians of repeated calls through each engine\'s typed client API, execute plus consume every row. A cell whose own two timing loops disagree by more than 25 percent is marked noisy, and no ratio is drawn from it. A difference under 1.15x counts as a tie. In the 100,000,000-invoice table a difference of 20 percent or less is equal, cold means the first run after the operating system\'s file cache was evicted, and warm is the median of three runs.',
    machine: 'One 16-core laptop CPU (Intel Core i9-11950H). The billing comparison at 10,000,000 invoices ran with no other benchmark or heavy application running. The plane showcase ran with ordinary desktop applications and the SQL Server service open. The 100,000,000-invoice comparison was one session with each engine in its own process (34.1 GB of memory, Windows 11, .NET 8.0.27). The scaling runs used a Release build with no other heavy application running (our Prism trainer was switched off), and each point is the median of three repetitions.',
    memory: 'The 10,000,000-invoice comparison, the plane runs, the million-row classes and the single-row write tables use in-memory HoloDb databases with no write-ahead log; SQLite and DuckDB ran in memory and SQL Server LocalDB used tempdb on disk. The single-row write tables therefore compare the engine alone against in-memory SQLite and do not measure durable commits: a database opened with a write-ahead log flushes each committed statement to disk before returning, which adds one flush per commit, set by the disk and not measured here. Bulk-load rates compare unlike modes: typed arrays into memory, an appender, inserts in one transaction, and a bulk copy into tempdb. The 100,000,000-invoice comparison is the exception: HoloDb used paged tables in files on disk. The opening and live-ingest figures say which mode they used.',
    tie: 'Fresh process means the first statements of a newly started process, which is what an application feels after it starts. The cost of just-in-time compilation is included.',
  },
  sections: [
    {
      id: 'billing',
      title: 'Billing ledger against SQLite, DuckDB and SQL Server, all out of the box',
      intro: [
        'The headline results below are the engine\'s own: the optional Pleroma plane was not attached in these comparisons. The ledger has 10,000,000 invoices and 200,000 customers, about 50 invoices each, over 24 months, four regions and five statuses, with money held as integer cents. In this section every engine ran as it ships: SQLite and DuckDB in memory with default settings, SQL Server 2022 LocalDB with primary keys only, and no index or summary table added for the queries. The next section repeats the queries at 100,000,000 invoices against tuned DuckDB and tuned SQL Server.',
        'Bulk load, rows per second: DuckDB 1,159,279; HoloDb server 1,051,473; SQLite 726,050; SQL Server 693,231; HoloDb embedded 679,271.',
      ],
      tables: [billingIn, billingNet],
      notes: [
        'HoloDb embedded is faster than in-memory SQLite on 15 of the 16 queries, from 48x (top customers by revenue) to more than 2,700x on the rollups. Against DuckDB out of the box, counting a difference under 1.15x as a tie, it is faster on 13 queries, tied on 2 and slower on 1. The rollups and the lookup read a maintained total or the primary-key index, so they take microseconds and do not grow with the ledger, while the other engines scan. The benchmark prints two decimals, so those cells are shown at that resolution and the ratios are lower bounds. A ratio on a rollup compares a maintained total with a scan; the ratios on the joins and scans, 1.4x to 3.3x, are the ones that compare the engines doing the same work.',
        'HoloDb server is faster than SQL Server with primary keys only on 15 of the 16 queries, including all five joins by 21x to 185x. Both are networked services paying a client and server round trip. With a columnstore index and indexed views added, SQL Server answers four of the five dimension joins faster than HoloDb at 100,000,000 invoices (next section). The server figures are medians of five calls, so they include the first-call cost of the network path: a longer loop through the same client measured the rollups and the lookup at a steady 0.06 to 0.10 ms, and a point query pays a fixed 0.07 ms of client, TLS, socket and result encoding that the same query does not pay embedded (1 to 3 microseconds).',
        'Total invoiced across 10,000,000 invoices (SELECT SUM(amount_cents)): SQLite 414.54 ms, DuckDB 2.56 ms, SQL Server 943.63 ms, HoloDb under 0.01 ms, because a maintained total is read instead of 10 million rows.',
      ],
      losses: [
        'Look up one invoice: SQLite is ahead of embedded HoloDb, 0.01 ms against 0.02 ms at the benchmark\'s two-decimal resolution.',
        'Revenue by signup cohort (a join): DuckDB is 1.25x ahead of embedded HoloDb, a parallel scan of the 10 million invoices at about 80 ns per row per core against DuckDB\'s columnar engine. Two other queries tie with DuckDB (big-ticket invoices and enterprise revenue).',
        'Look up one invoice over the network: SQL Server answers in 0.07 ms and HoloDb server in 0.18 ms, 2.6x behind. About 0.07 ms of HoloDb\'s time is client, TLS and socket cost outside the engine.',
      ],
    },
    HM_SECTION,
    {
      id: 'plane-billing',
      title: 'The optional plane on the billing ledger',
      intro: [
        'The plane is opt-in per table and was not on in the 10,000,000-invoice comparison above. This is a separate run of the same workload with a plane attached to the invoices table: dimensions region (4 values), status (5) and month (24), a 10-bit joint, and the measure amount_cents. Building it for 10,000,000 rows took 741 ms (749 ms on the server\'s database) and its cells take 34,528 bytes. All 16 results were again identical across the engines.',
      ],
      tables: [matrix(/^Query\|SQLite\|DuckDB\|HoloDb, plane off\|HoloDb, plane on/, 'plane-billing', 'Plane off and plane on, 10,000,000 invoices, median milliseconds (SQLite and DuckDB out of the box, in memory, one run)')],
      notes: [
        'Through the server the same two queries take 6.30 ms and 34.58 ms with the plane off and 0.11 ms and 0.45 ms with it on (SQL Server: 611.59 ms and 2,898.13 ms).',
        'What the plane serves: filters and multi-column GROUP BY over its dimension columns. What it declines, and why: the big-ticket count (the filter is on a measure, not a dimension), top customers (200,000 distinct customer ids, far over the 1,024-value dimension cap), the five joins (200,000 join keys against a limit of 4,095) and the lookup and largest-invoices queries (no aggregate). The five rollups are left to the engine on purpose, because it reads a maintained total there in microseconds and the plane is slower. Per database, 12 calls were served by the plane and 42 declined.',
        'The cells the plane does not touch run the identical engine path in both columns and differed by 1 to 13 percent typically and by up to 33 percent (big-ticket: 3.01 ms off, 2.26 ms on), so differences of that size are noise.',
      ],
      losses: ['The plane is not used for the rollups: whole-table counts and sums, single-column GROUP BY and MIN and MAX, where the engine already reads a maintained total in microseconds.'],
    },
    {
      id: 'dashboard',
      title: 'Plane on constructed workloads: dashboards, ingest and tenants',
      intro: [
        'These workloads were built to suit the plane and are measured separately from the engine-only comparisons above. What they share: the filter combinations cannot be pre-indexed because nobody knows in advance which dimensions a person will cut by, the answers must be exact, and the table keeps changing while it is queried.',
        'A live dashboard: 10,000,000 events and 32 ad-hoc slices over six small-domain dimensions (a 14-bit joint) and the measures. Building the plane took 1,759 ms and its cells take 920,248 bytes. The slices are scalar filters (W), group-bys with and without a filter (G), HAVING (H), COUNT DISTINCT and MIN and MAX with a filter (D), queries outside the plane (X) and whole-table queries the engine already answers from a maintained total.',
      ],
      tables: [
        matrix(/^Class \(queries\)\|Plane vs engine, median/, 'dash-classes', 'Plane against the engine and against the best other engine (SQLite, DuckDB and SQL Server, each out of the box), by class of slice; one run on a laptop with ordinary applications open'),
        matrix(/^Query\|DuckDB\|SQL Server\|HoloDb engine\|HoloDb plane$/, 'dash-first', 'First call of each query, median milliseconds (DuckDB and SQL Server out of the box, one run)'),
      ],
      notes: [
        'The best other engine on the W, G, H and D slices is DuckDB in every row shown, out of the box: no indexes added and no summary table built. Three of the nine W cells are noisy, because the engine\'s own two timing loops disagreed by more than 25 percent; the class median is as printed.',
        'The first call of each query, which pays any one-time work, is also faster with the plane: the median first-call speed-up over the engine is 16.99x for W, 10.90x for G, 13.97x for H and 10.17x for D.',
        'The queries the plane declines or leaves to the engine (the X class: a filter on a non-dimension column, COUNT(DISTINCT user_id), a sum of a non-measure column, a top-10 by user, a filter on a measure, a row list and a point lookup by event id) run the engine\'s own path and tie with it: the seven X queries read 0.95x to 1.04x warm. On the first call five of the seven were slower with the plane attached, at 0.42x to 0.84x of the engine\'s first call; these are single samples.',
      ],
      losses: [
        'X-class queries gain nothing from the plane: they tie with the engine warm, and five of seven first calls were slower with the plane attached.',
      ],
    },
    {
      id: 'ingest',
      title: 'Ingest while querying',
      intro: ['A 2,000,000-row base table, then 20 rounds of a 10,000-row bulk load, 200 single-row INSERTs and an eight-query dashboard panel. The panel\'s results matched in every round. This one ran on a desktop with ordinary applications open, as stated above, so read the cells as the medians of one run. How an insert followed by a query scales with the size of the table is under Scaling.'],
      tables: [ingestTable],
      notes: [],
      losses: [
        'Writes cost more with the plane attached: the 10,000-row bulk load takes 11.62 ms against 9.85 ms (1.18x), and 200 single-row INSERTs take 1.55 ms against 1.23 ms (1.26x), about 7.8 microseconds against 6.2 per INSERT.',
      ],
    },
    {
      id: 'tenants',
      title: 'Multi-tenant slices and joins',
      intro: ['Five million events spread over six tenant tables (2,000,000 down to 150,000 rows) and a 2,000-product dimension table. A filter with a COUNT and SUM on one tenant is a median 11.70x faster than the engine and 25.0x faster than the best other engine (DuckDB, SQLite and SQL Server each out of the box). The largest tenant reads 0.171 ms with the plane, 4.263 ms for the engine, 6.312 ms for DuckDB and 195.195 ms for SQL Server.'],
      tables: [matrix(/^Query\|DuckDB\|SQL Server\|HoloDb engine\|HoloDb plane, embedded/, 'tenant-joins', 'The same join repeated, median milliseconds (DuckDB and SQL Server out of the box, one run)')],
      notes: [
        'The join in these tests is one statement repeated: tenant events joined to the products table on the product key (about 2,000 keys), with COUNT(*) alone or with two SUMs. The plane serves a repeat from a memo of the exact answer, valid until either of the two tables is written, so the very low figures are for a repeated identical statement with no write in between. A memo hit takes 0.001 to 0.003 ms embedded and 0.071 to 0.084 ms through the server.',
        'Joins with a WHERE or a GROUP BY on the dimension table are not served by the plane at all; they run the engine\'s path and tie with it (0.93x to 1.02x).',
      ],
      losses: [
        'A join that is not a memo hit usually costs more through the plane than the engine\'s own join. The first call of a statement costs more through the plane than through the engine in 11 of the 12 join cells: join COUNT 1.016 to 2.921 ms through the plane on the five later tenants against 0.179 to 0.573 ms for the engine, and join COUNT with two SUMs 2.348 to 8.134 ms against 1.364 to 3.076 ms. A call after a write to either table was not timed. If the tables are written between nearly every read, expect the plane join to cost more than the engine\'s own join; the plane\'s value on such a workload comes from the filters and group-bys.',
      ],
    },
    {
      id: 'million',
      title: 'The plane against the engine at 1,000,000 rows',
      intro: [
        'Seventeen query classes on a 1,000,000-row table, with the engine alone, the engine with the plane, and SQLite. The control query (one the plane declines, so both sides run the same engine path) read 0.81, inside the band of 0.8 to 1.25. The join rows use a very small dimension table: 40 distinct join keys for the two INT joins and 4 for the TEXT join, far below the 2,000 product keys of the tenant workload and the 200,000 join keys of the billing ledger, so they say little about joins over many keys.',
      ],
      tables: [matrix(/^Query class\|SQLite\|HoloDb engine\|HoloDb plane\|Plane vs engine/, 'classes', 'Seventeen query classes at 1,000,000 rows (SQLite in memory; the two INT joins have 40 distinct join keys and the TEXT join has 4; one run)')],
      notes: [
        'The engine alone is faster than in-memory SQLite on every class, from 9x (the TEXT join) to more than 250x on COUNT(*) and more than 20,000x on SUM, single-column GROUP BY, MIN and MAX and COUNT DISTINCT over the whole table; those last classes compare a maintained total with a scan. The plane is faster than SQLite on every class it serves. Against the engine it wins where there is a real scan or join to avoid. Outcome counts for the plane against the engine: 7 classes at least 2x faster, 3 ties, 1 between 1.25x and 2x, 4 slower, 2 noisy.',
        'At 100,000 rows the engine is faster than the plane on seven of the 17 classes: the whole-table classes, the single-column GROUP BY and MIN and MAX (0.33x to 0.51x), the two-column GROUP BY (0.26x), GROUP BY with a WHERE (0.41x) and COUNT DISTINCT with a WHERE (0.58x). At 300,000 rows the plane is faster on every filter and join (24x to 1,887x) and on the two-column GROUP BY (1.77x). Default routing sends GROUP BY with a WHERE to the engine below 140,000 rows.',
        'The plane\'s cost when attached, against the engine alone: single-row INSERT 1.4x to 1.9x, UPDATE 1.6x to 1.8x (these cells are microseconds and move with the order the two arms run in); batch load of the whole table 1.93 s with the plane against 1.80 s without at 1,000,000 rows; rebuilding the plane from the columns 131 ms; a heap of 139.7 bytes per row with the plane against 135.2 without; the plane\'s cells are a flat 0.92 MB joint slice whatever the row count.',
        'The TEXT-key join is faster than SQLite in the engine alone (39.98 ms against 322.17 ms at 1,000,000 rows, rows identical). It requires both join columns to be TEXT; a join between an INT column and a TEXT column takes the general path and is much slower.',
      ],
      losses: [
        'With the plane attached, whole-table COUNT(*) (0.48x), whole-table SUM (0.47x), single-column GROUP BY (0.43x) and MIN and MAX (0.53x) are slower than the engine alone, because the engine reads a maintained total in microseconds.',
      ],
    },
    scalingSection(),
    {
      id: 'writes',
      title: 'Single-row writes and transactions, engine alone against SQLite',
      intro: ['Autocommit statements on a table of the benchmark shape (9 columns), 205 statements per cell with the first 5 dropped, median, each size in its own newly started process.'],
      tables: [
        matrix(/^Statement\|Rows\|SQLite\|HoloDb engine\|Engine vs SQLite/, 'writes', 'Single-row statements, fresh process'),
        matrix(/^Operation\|SQLite\|HoloDb engine$/, 'txn', 'Transactions on a 1,000,000-row table'),
      ],
      notes: [
        'BEGIN costs the same at any table size because rows are journaled as they change instead of the table being copied, and ROLLBACK replays the journal. The transaction figures are an upper bound for the engine, measured before a later compilation setting was added, and were not re-measured; the 100,000-row figures follow the same pattern.',
      ],
      losses: [
        'The engine alone is slower than SQLite on single-row writes: INSERT 1.5x to 1.6x, UPDATE 2.0x to 2.1x and DELETE 1.7x to 1.8x. Point SELECT by key ties.',
        'A 1,000-statement transaction costs 4 to 5 times SQLite\'s: 28.18 ms against 6.44 ms, and 33.35 ms against 6.54 ms with a ROLLBACK.',
      ],
    },
    {
      id: 'live',
      title: 'Dashboard reads during live ingest, and opening a database',
      intro: [
        'Whole-table counts and sums, and GROUP BY on a column with up to 64 distinct values, are served from a snapshot republished after every write, without taking the engine\'s write lock. In one run on a 16-core laptop that was not idle, 2,000,000 events were ingested in 1,000-row batches at 1.16 million events a second while 4 concurrent readers queried: median read 0.009 to 0.09 ms, p99 0.8 ms (group by region), 1.1 ms (sum) and 6.6 ms (count), worst single read 6.7 ms.',
        'Opening a database of 1,000,000 rows (a 43 MB log) takes about 2.0 s by replaying its write-ahead log (1.95 to 2.05 s across two runs) and about 1.1 s from a checkpoint (1.09 to 1.11 s), with the loaded state checked correct and a heap of about 110 bytes per row, in a newly started process. This is the engine\'s own recovery and checkpoint; it does not use the plane or the page store.',
      ],
      tables: [],
      notes: [],
      losses: ['A GROUP BY with more than 64 distinct values (the per-minute series in the same test has 167) takes the normal locked path and waited behind the ingest for up to 0.9 s (p99 0.9 s).'],
    },
    {
      id: 'checks',
      title: 'Checks behind the figures',
      intro: ['Every release is measured against the previous one before it ships: each benchmark cell is run twice on each build, and a cell counts as a regression only if it is worse than 25 percent (or the baseline\'s own spread) in both pairs. The last such check covered the 10-million-invoice comparison, the plane benchmark at 100,000, 300,000 and 1,000,000 rows, single-row writes and transactions, and opening a 1,000,000-row database from its log and from a checkpoint, and found no cell slower.'],
      tables: [],
      notes: [
        'Queries the plane declines or leaves to the engine stay within noise: of the 16 queries that run the same path in both arms, none fell outside a band of 0.8x to 1.25x, with class medians of 0.97 and 0.99. A deliberately wrong plane (the measure column shuffled at 1,000,000 rows) was caught on every query it changes and matched on every query it does not: 0 failures of 32.',
        'The aggregate plane returns row-for-row the same answers as the engine and SQLite across the parity suite (2,853 checks at 2,000 rows, and the suite also passes at 5,000 and 100,000 rows), including a random insert, update, delete, rollback and reopen log, and sums past 64 bits. Nine deliberate mutations (a dropped carry, a missed delete, a stale join limb and others) each turned the suite red. Sums past 64 bits are never wrapped: the plane raises an error and PleromaSumExact returns the exact 128-bit total.',
        'The page store passes 72 of 72 checks: exactly-once replay after a crash, a torn journal tail discarded, restore points that rewind without recounting, and repair of a lost page from its parity group. Eight deliberate mutations and a tiny-cache case each turned the suite red.',
        'A paged table checks every 8 KB data page against a checksum kept in a small side file. In a test that flipped 61 bits in the data file of a 91 MB paged table, on the laptop described above, none gave a silently different answer: 36 were reported with an error that names the page, and 25 were in pages never read.',
        'Atomicity and crash recovery are covered by a 48-check suite (a failing log append is undone in full, a reopen from the log reproduces the committed state) and a 58-check transaction suite plus about 600 randomised comparisons against SQLite; three deliberate mutations each turned it red.',
      ],
      losses: [],
    },
  ],
  limits: [
    'Each figure comes from a single run on a single machine, and the plane sections use constructed workloads: one synthetic table (9 columns, four small-domain dimensions, a 14-bit joint). The plane serves only the columns you attach. A plane made with EnablePleroma lives in RAM and is not persisted; a plane made with SQL, and the summaries the planner builds by itself, are saved with the table.',
    'The 100,000,000-invoice comparison is one session on one laptop. We chose the tuning of DuckDB and SQL Server after a 10,000,000-row trial and fixed it before the 100,000,000-row build, and a different tuning is untested. HoloDb out of the box saw each statement four times (the cold run and three warm runs) and earns a structure only after repeats. The cold runs are not on this page.',
    'In the engine alone, GROUP BY with a WHERE is about 3 ms at 1,000,000 rows because the filter and the grouping are separate passes. The dense-code GROUP BY covers INT and TEXT columns whose combined distinct count is at most 65,536, and anything else uses a slower string-key path. REAL aggregates over a filter use the older scan.',
    'The first DDL statement inside a transaction takes a snapshot of every in-memory table, so it costs time proportional to the data. After a ROLLBACK of deleted rows the table holds the same rows but not necessarily in the same internal order.',
    'The aggregate plane does not serve the billing joins at 10,000,000 invoices (200,000 join keys against its limit of 4,095) or the top-customers query; the keyed summary and the pre-grouped join totals in the 100,000,000-invoice section do. A plane join over fewer keys is served, as in the tenant workload.',
    'The scaling lines are fits over four or five sizes on one machine, and the write-then-read pairs are one run per table size of one repeated group-by. Time slopes under about 0.4 are not read as exact exponents.',
    'Page-store throughput is not reported here.',
  ],
  internal: { source: 'HoloDb/docs/site.md (migrated); run ids, dates and experiment names deliberately not carried' },
};

const out = path.join(DATA, 'evidence', 'holodb.json');
fs.writeFileSync(out, JSON.stringify(record, null, 2) + '\n', 'utf8');
let nt = 0, nr = 0; for (const s of record.sections) for (const t of s.tables) { nt++; nr += t.rows.length; }
console.log('wrote', out, `${record.sections.length} sections, ${nt} tables, ${nr} rows`);
