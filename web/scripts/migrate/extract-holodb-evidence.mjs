// One-off migration helper: builds data/evidence/holodb.json (ONE evidence set: method stated once, sections, losses)
// from the owner's HoloDb/docs/site.md. Every number in a table is parsed from that file, never retyped; the prose
// around them is written here in the current-state voice (no experiment ids, run numbers or dates).
//   node web/scripts/migrate/extract-holodb-evidence.mjs [path-to-site.md]
import fs from 'node:fs';
import path from 'node:path';
import { MONOREPO, DATA } from '../lib/data.mjs';

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

const billingIn = timing(/^Billing query\|SQLite\|DuckDB\|HoloDb embedded/, 'inprocess', 'In process, in memory: HoloDb embedded against DuckDB and SQLite (median milliseconds)', 2, 1);
const billingNet = timing(/^Billing query\|SQLite\|SQL Server\|HoloDb server/, 'networked', 'Networked: HoloDb server against SQL Server and SQLite (median milliseconds)', 2, 1);

const record = {
  id: 'holodb',
  product: 'holodb',
  slug: '/holodb/evidence/',
  scope: 'engine',
  title: 'HoloDb benchmarks',
  description: 'HoloDb against SQLite, DuckDB and SQL Server on a 10,000,000-invoice ledger, the optional plane, writes, and the checks behind the figures.',
  claim: 'On a 10,000,000-invoice ledger joined to 200,000 customers, the dashboard rollups read in microseconds in the embedded engine, and every result is identical across the engines. The queries where HoloDb is slower are listed beside the wins.',
  tieBelow: 1.15,
  method: {
    workload: 'A billing business\'s invoice ledger (a normalised invoices fact of 10,000,000 rows joined to a customers dimension of 200,000 rows) with sixteen queries: five rollups, a single-invoice lookup, five single-table scans and five joins to the customer dimension. The plane sections use three constructed workloads: a live dashboard, ingest while querying, and multi-tenant slices and joins.',
    engines: ['SQLite (in memory, the external reference, always listed first)', 'DuckDB (in process, in memory)', 'SQL Server 2022 LocalDB (tables in tempdb on disk)', 'HoloDb embedded (in process)', 'HoloDb server (networked over TLS on the loopback interface)', 'HoloDb engine: the same database without a plane. HoloDb plane: a database with the optional Pleroma plane attached.'],
    verification: 'Before any query is timed, its full result set is normalised and compared across the engines. Every timed result is identical, including on the queries where HoloDb is slower. After each plane workload the plane is compared with a rebuild from the columns and found equal.',
    statistic: 'Medians of repeated calls through each engine\'s typed client API, execute plus consume every row. A cell whose own two timing loops disagree by more than 25 percent is marked noisy, and no ratio is drawn from it. A difference under 1.15x counts as a tie.',
    machine: 'One 16-core laptop CPU (Intel Core i9-11950H). The billing comparison ran with no other benchmark or heavy application running. The plane showcase ran with ordinary desktop applications and the SQL Server service open.',
    memory: 'Every HoloDb figure is an in-memory database with no write-ahead log; SQLite and DuckDB ran in memory and SQL Server used tempdb on disk. The single-row write tables therefore compare the engine alone against in-memory SQLite and do not measure durable commits: a database opened with a write-ahead log flushes each committed statement to disk before returning, which adds one flush per commit, set by the disk and not measured here. Bulk-load rates compare unlike modes: typed arrays into memory, an appender, inserts in one transaction, and a bulk copy into tempdb.',
    tie: 'Fresh process means the first statements of a newly started process, which is what an application feels after it starts. The cost of just-in-time compilation is included.',
  },
  sections: [
    {
      id: 'billing',
      title: 'Billing ledger against SQLite, DuckDB and SQL Server',
      intro: [
        'The headline results below are the engine\'s own: the optional Pleroma plane was not attached in these comparisons. The ledger has 200,000 customers, about 50 invoices each, over 24 months, four regions and five statuses, with money held as integer cents.',
        'Bulk load, rows per second: DuckDB 1,159,279; HoloDb server 1,051,473; SQLite 726,050; SQL Server 693,231; HoloDb embedded 679,271.',
      ],
      tables: [billingIn, billingNet],
      notes: [
        'HoloDb embedded is faster than SQLite on 15 of the 16 queries, from 48x (top customers by revenue) to more than 2,700x on the rollups. Against DuckDB, counting a difference under 1.15x as a tie, it is faster on 13 queries, tied on 2 and slower on 1. The rollups and the lookup read a maintained total or the primary-key index, so they take microseconds and do not grow with the ledger. The benchmark prints two decimals, so those cells are shown at that resolution and the ratios are lower bounds.',
        'HoloDb server is faster than SQL Server on 15 of the 16 queries, including all five joins by 21x to 185x. Both are networked services paying a client and server round trip. The server figures are medians of five calls, so they include the first-call cost of the network path: a longer loop through the same client measured the rollups and the lookup at a steady 0.06 to 0.10 ms, and a point query pays a fixed 0.07 ms of client, TLS, socket and result encoding that the same query does not pay embedded (1 to 3 microseconds).',
        'Total invoiced across 10,000,000 invoices (SELECT SUM(amount_cents)): SQLite 414.54 ms, DuckDB 2.56 ms, SQL Server 943.63 ms, HoloDb under 0.01 ms, because a maintained total is read instead of 10 million rows.',
      ],
      losses: [
        'Look up one invoice: SQLite is ahead of embedded HoloDb, 0.01 ms against 0.02 ms at the benchmark\'s two-decimal resolution.',
        'Revenue by signup cohort (a join): DuckDB is 1.25x ahead of embedded HoloDb, a parallel scan of the 10 million invoices at about 80 ns per row per core against DuckDB\'s columnar engine. Two other queries tie with DuckDB (big-ticket invoices and enterprise revenue).',
        'Look up one invoice over the network: SQL Server answers in 0.07 ms and HoloDb server in 0.18 ms, 2.6x behind. About 0.07 ms of HoloDb\'s time is client, TLS and socket cost outside the engine.',
      ],
    },
    {
      id: 'plane-billing',
      title: 'The optional plane on the billing ledger',
      intro: [
        'The plane is opt-in per table and was not on in the comparison above. This is a separate run of the same workload with a plane attached to the invoices table: dimensions region (4 values), status (5) and month (24), a 10-bit joint, and the measure amount_cents. Building it for 10,000,000 rows took 741 ms (749 ms on the server\'s database) and its cells take 34,528 bytes. All 16 results were again identical across the engines.',
      ],
      tables: [matrix(/^Query\|SQLite\|DuckDB\|HoloDb, plane off\|HoloDb, plane on/, 'plane-billing', 'Plane off and plane on, median milliseconds')],
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
        matrix(/^Class \(queries\)\|Plane vs engine, median/, 'dash-classes', 'Plane against the engine and against the best other engine, by class of slice'),
        matrix(/^Query\|DuckDB\|SQL Server\|HoloDb engine\|HoloDb plane$/, 'dash-first', 'First call of each query, median milliseconds'),
      ],
      notes: [
        'The best other engine on the W, G, H and D slices is DuckDB in every row shown. Three of the nine W cells are noisy, because the engine\'s own two timing loops disagreed by more than 25 percent; the class median is as printed.',
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
      intro: ['A 2,000,000-row base table, then 20 rounds of a 10,000-row bulk load, 200 single-row INSERTs and an eight-query dashboard panel, then 300 trickle pairs (one INSERT followed by one group-by query). The panel\'s results matched in every round.'],
      tables: [matrix(/^Per round \(ms\)\|SQLite/, 'ingest-rounds', 'Per round, median milliseconds')],
      notes: [
        'The plane makes the panel 11.95x faster than the engine, the round total 5.78x faster and the trickle pair 7.00x faster. Through the server the round total is 179.34 ms for the engine and 45.65 ms with the plane.',
        'The first panel query after each ingest gains only 1.66x (2.304 ms against 3.835 ms), while the other seven gain 2.87x to 319.60x. The likely cause is the flush of the plane\'s pending changes that the first query after an ingest triggers; the flush was not timed separately. DuckDB takes 6.288 ms on that query, so the plane is still 2.7x ahead of it.',
      ],
      losses: [
        'Writes cost more with the plane attached: the 10,000-row bulk load takes 11.62 ms against 9.85 ms (1.18x), and 200 single-row INSERTs take 1.55 ms against 1.23 ms (1.26x), about 7.8 microseconds against 6.2 per INSERT.',
      ],
    },
    {
      id: 'tenants',
      title: 'Multi-tenant slices and joins',
      intro: ['Five million events spread over six tenant tables (2,000,000 down to 150,000 rows) and a 2,000-product dimension table. A filter with a COUNT and SUM on one tenant is a median 11.70x faster than the engine and 25.0x faster than the best other engine. The largest tenant reads 0.171 ms with the plane, 4.263 ms for the engine, 6.312 ms for DuckDB and 195.195 ms for SQL Server.'],
      tables: [matrix(/^Query\|DuckDB\|SQL Server\|HoloDb engine\|HoloDb plane, embedded/, 'tenant-joins', 'The same join repeated, median milliseconds')],
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
      tables: [matrix(/^Query class\|SQLite\|HoloDb engine\|HoloDb plane\|Plane vs engine/, 'classes', 'Seventeen query classes at 1,000,000 rows')],
      notes: [
        'The engine alone is faster than SQLite on every class, from 9x (the TEXT join) to more than 250x on COUNT(*) and more than 20,000x on SUM, single-column GROUP BY, MIN and MAX and COUNT DISTINCT over the whole table. The plane is faster than SQLite on every class it serves. Against the engine it wins where there is a real scan or join to avoid. Outcome counts for the plane against the engine: 7 classes at least 2x faster, 3 ties, 1 between 1.25x and 2x, 4 slower, 2 noisy.',
        'At 100,000 rows the engine is faster than the plane on seven of the 17 classes: the whole-table classes, the single-column GROUP BY and MIN and MAX (0.33x to 0.51x), the two-column GROUP BY (0.26x), GROUP BY with a WHERE (0.41x) and COUNT DISTINCT with a WHERE (0.58x). At 300,000 rows the plane is faster on every filter and join (24x to 1,887x) and on the two-column GROUP BY (1.77x). Default routing sends GROUP BY with a WHERE to the engine below 140,000 rows.',
        'The plane\'s cost when attached, against the engine alone: single-row INSERT 1.4x to 1.9x, UPDATE 1.6x to 1.8x (these cells are microseconds and move with the order the two arms run in); batch load of the whole table 1.93 s with the plane against 1.80 s without at 1,000,000 rows; rebuilding the plane from the columns 131 ms; a heap of 139.7 bytes per row with the plane against 135.2 without; the plane\'s cells are a flat 0.92 MB joint slice whatever the row count. One INSERT followed by a two-column GROUP BY costs the plane a floor of roughly 3 ms, which the engine beats on small tables and loses to on large ones.',
        'The TEXT-key join is faster than SQLite in the engine alone (39.98 ms against 322.17 ms at 1,000,000 rows, rows identical). It requires both join columns to be TEXT; a join between an INT column and a TEXT column takes the general path and is much slower.',
      ],
      losses: [
        'With the plane attached, whole-table COUNT(*) (0.48x), whole-table SUM (0.47x), single-column GROUP BY (0.43x) and MIN and MAX (0.53x) are slower than the engine alone, because the engine reads a maintained total in microseconds.',
      ],
    },
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
        'Whole-table counts and sums, and GROUP BY on a column with up to 64 distinct values, are served from a snapshot republished after every write, without taking the engine\'s write lock. In one run, 2,000,000 events were ingested in 1,000-row batches at 1.16 million events a second while 4 concurrent readers queried: median read 0.009 to 0.09 ms, p99 0.8 ms (group by region), 1.1 ms (sum) and 6.6 ms (count), worst single read 6.7 ms.',
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
        'Atomicity and crash recovery are covered by a 48-check suite (a failing log append is undone in full, a reopen from the log reproduces the committed state) and a 58-check transaction suite plus about 600 randomised comparisons against SQLite; three deliberate mutations each turned it red.',
      ],
      losses: [],
    },
  ],
  limits: [
    'One run per size on one machine, and constructed workloads for the plane sections: one synthetic table (9 columns, four small-domain dimensions, a 14-bit joint). The plane serves only the columns you attach, lives in RAM and is not persisted.',
    'In the engine alone, GROUP BY with a WHERE is about 3 ms at 1,000,000 rows because the filter and the grouping are separate passes. The dense-code GROUP BY covers INT and TEXT columns whose combined distinct count is at most 65,536, and anything else uses a slower string-key path. REAL aggregates over a filter use the older scan.',
    'The first DDL statement inside a transaction takes a snapshot of every in-memory table, so it costs time proportional to the data. After a ROLLBACK of deleted rows the table holds the same rows but not necessarily in the same internal order.',
    'The plane does not help the billing joins (200,000 join keys against the limit of 4,095) or the top-customers query. A plane join over fewer keys is served, as in the tenant workload.',
    'Page-store throughput is not reported here.',
  ],
  internal: { source: 'HoloDb/docs/site.md (migrated); run ids, dates and experiment names deliberately not carried' },
};

const out = path.join(DATA, 'evidence', 'holodb.json');
fs.writeFileSync(out, JSON.stringify(record, null, 2) + '\n', 'utf8');
let nt = 0, nr = 0; for (const s of record.sections) for (const t of s.tables) { nt++; nr += t.rows.length; }
console.log('wrote', out, `${record.sections.length} sections, ${nt} tables, ${nr} rows`);
