// Writes the words and figures of the HoloDb explainer and decision helper:
//   data/diagrams/holodb-how.json  (six scenes: promise, two sentences of how, caption with the measured condition)
//   data/diagrams/holodb-fit.json  (decision helper rules, the five trade-offs, the limits table)
// Run extract-holodb-race.mjs first. Every figure about the benchmark comes from holodb-race.json (which reads the result files);
// the write-cost lines come from the owner's site.md single-row table; the limits and trade-off facts come from the owner's limits audit
// (HoloDb/todo/enterprise-concerns-2026-10-23-report.md, C6 and C7) and are typed here once, with their source named.
//   node web/scripts/migrate/extract-holodb-story.mjs
import fs from 'node:fs';
import path from 'node:path';
import { DATA, MONOREPO, readJson } from '../lib/data.mjs';
import { counts } from '../../src/assets/diagrams/holodb-race.js';

const race = readJson(path.join(DATA, 'diagrams', 'holodb-race.json'));
const grp = (n) => Number(n).toLocaleString('en-GB');
const n2 = (n) => Number(n).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const Q = (label) => { const i = race.queries.findIndex((q) => q.label === label); if (i < 0) throw new Error('query not found: ' + label); return race.queries[i]; };
const A = Object.fromEntries(race.arms.map((a, i) => [a.id, { ...a, i }]));
const warm = (label, arm) => Q(label).warm[A[arm].i];
const cold = (label, arm) => Q(label).cold[A[arm].i];
const gb = (b) => (b / 1e9).toFixed(2);
const mb = (b) => Math.round(b / 1e6);
const cnt = counts(race, 'warm');

// ---- single-row write times, parsed from the owner's table (engine alone, in memory, no log, one run) ----
const md = fs.readFileSync(path.join(MONOREPO, 'HoloDb', 'docs', 'site.md'), 'utf8').replace(/\r\n/g, '\n');
const wr = {};
for (const m of md.matchAll(/^\| single-row (INSERT|UPDATE|DELETE) \| ([\d,]+) \| ([\d.]+) us \| ([\d.]+) us \| ([\d.]+)x \|$/gm)) {
  const k = m[1]; (wr[k] ||= { sql: [], holo: [], ratio: [] }); wr[k].sql.push(+m[3]); wr[k].holo.push(+m[4]); wr[k].ratio.push(+m[5]);
}
for (const k of ['INSERT', 'UPDATE', 'DELETE']) if (!wr[k] || wr[k].sql.length !== 2) throw new Error('single-row write table not found for ' + k);
const span = (a, d = 1) => { const lo = Math.min(...a), hi = Math.max(...a); return lo === hi ? lo.toFixed(d) : `${lo.toFixed(d)} to ${hi.toFixed(d)}`; };
const writeLine = `Single-row INSERT took ${span(wr.INSERT.holo)} microseconds against SQLite's ${span(wr.INSERT.sql)}, UPDATE ${span(wr.UPDATE.holo)} against ${span(wr.UPDATE.sql)}, DELETE ${span(wr.DELETE.holo)} against ${span(wr.DELETE.sql)} (engine alone, in memory, no log, a 9-column table). The maintained totals are updated on every write.`;
const writeRatio = `${span([...wr.INSERT.ratio, ...wr.UPDATE.ratio, ...wr.DELETE.ratio])}`;

// ---- the scan cases where out of the box loses to tuned DuckDB (from the table, not typed) ----
const oobBehind = race.queries.filter((q, qi) => {
  const a = q.warm[A.oob.i], d = q.warm[A.duck.i];
  return a > 1.2 * Math.max(q.warm2[A.duck.i], d) && q.warm2[A.oob.i] > 1.2 * d;
}).map((q) => q.label);
const shortLabel = (l) => l.replace(' (>$5k)', '').replace(' & ', ' and ');
const scanCases = oobBehind.map((l) => `${shortLabel(l)} (${n2(warm(l, 'duck'))} ms tuned DuckDB against ${n2(warm(l, 'oob'))} ms)`);
const listWords = (a) => (a.length < 2 ? a.join('') : `${a.slice(0, -1).join(', ')} and ${a[a.length - 1]}`);

const total = Q('total invoiced');
const seg = Q('revenue by segment (JOIN)');
const status = Q('invoices by status');
const overdue = Q('overdue outstanding');
const lookup = Q('look up one invoice');
const newCents = Number(lookup.first.split('|')[3]);
const custCount = Number((race.fingerprint.match(/cust=(\d+)/) || [])[1]);
const F = race.extra.restoredFiles;
const dataBytes = F['customers/data.pages'] + F['invoices/data.pages'];
const files = [
  { name: 'the data pages', size: `${gb(dataBytes)} GB` },
  { name: 'sorted index', size: `${gb(F['invoices/sorted.amount_cents.idx'])} GB` },
  { name: 'keyed totals', size: `${mb(F['invoices/keyed.ix_customer.pks'])} MB` },
  { name: 'summary', size: `${Math.round(F['invoices/summary.decl'] / 1000)} KB` },
];
const R = A.restored;
const WRITE_RAM = 6.5, WRITE_PAGED = 90;           // microseconds per INSERT with a summary kept: owner's limits audit, C7 point 2
const FLIP = { flipped: 61, reported: 36, unread: 25, wrong: 0, sizeMb: 91 };   // owner's page-checksum test, on the page before this rewrite

// ======================================================================= how
const scenes = [
  {
    id: 'read', tab: 'Instant totals', promise: 'A total that is already added up.',
    how: ['HoloDb keeps the running total of an integer column next to the data, so a sum, a count or a group-by reads a few numbers instead of scanning rows.', 'Each insert pays a few microseconds to keep that total current, so the cost moves from every read to every write.'],
    caption: `Illustrative motion. Measured at ${grp(race.rows)} invoices, warm: total invoiced ${n2(warm('total invoiced', 'restored'))} ms for HoloDb restored, ${n2(warm('total invoiced', 'duck'))} ms for tuned DuckDB scanning and ${n2(warm('total invoiced', 'sql'))} ms for tuned SQL Server with an indexed view. Keeping the summary current cost ${WRITE_RAM} microseconds per INSERT in memory and about ${WRITE_PAGED} on a paged table.`,
    fig: { rows: race.rows, totalCents: Number(total.first), newCents, holoMs: warm('total invoiced', 'restored'), duckMs: warm('total invoiced', 'duck'), sqlMs: warm('total invoiced', 'sql'), writeRam: WRITE_RAM, writePaged: WRITE_PAGED },
  },
  {
    id: 'build', tab: 'Builds its own', promise: 'It notices what you keep asking, and builds the answer.',
    how: ['When a query shape repeats and the scans have cost as much as a summary or index would cost to build, the planner builds it in the background.', 'If writes later make it cost more than it saves, the planner drops it again. A DBA can declare structures instead, and those are saved and kept.'],
    caption: `Illustrative motion. Measured out of the box at ${grp(race.rows)} invoices: invoices by status took ${grp(Math.round(cold('invoices by status', 'oob') * 10) / 10)} ms on its first run, then ${n2(warm('invoices by status', 'oob'))} ms. Overdue outstanding was still a scan after four runs, ${n2(warm('overdue outstanding', 'oob'))} ms against ${n2(warm('overdue outstanding', 'duck'))} ms for tuned DuckDB. Declared, it read in ${n2(warm('overdue outstanding', 'restored'))} ms.`,
    fig: { status: { label: 'invoices by status', first: grp(Math.round(cold('invoices by status', 'oob') * 10) / 10), after: n2(warm('invoices by status', 'oob')) }, overdue: { label: 'overdue outstanding', oob: n2(warm('overdue outstanding', 'oob')), duck: n2(warm('overdue outstanding', 'duck')), restored: n2(warm('overdue outstanding', 'restored')) } },
  },
  {
    id: 'join', tab: 'Joins without joining', promise: 'Joins that read a handful of numbers.',
    how: ['Declare a keyed summary and HoloDb keeps the totals of every customer segment already grouped, so a join reads the groups instead of the invoices.', 'Move a customer to another segment and only that customer\'s total moves, exactly, from one group to the other.'],
    caption: `Illustrative motion. Measured at ${grp(race.rows)} invoices joined to ${grp(custCount)} customers, warm: revenue by segment ${n2(warm('revenue by segment (JOIN)', 'restored'))} ms for HoloDb restored, ${n2(warm('revenue by segment (JOIN)', 'sql'))} ms for tuned SQL Server, ${n2(warm('revenue by segment (JOIN)', 'duck'))} ms for tuned DuckDB. Changing an invoice row, not a customer, makes the pre-grouped totals stale and those joins fall back to a scan.`,
    fig: { rows: race.rows, customers: grp(custCount), holoMs: warm('revenue by segment (JOIN)', 'restored') },
  },
  {
    id: 'open', tab: 'Start in a second', promise: 'Close it, open it, ask.',
    how: ['Declared structures are saved beside the data with checksums. On open, HoloDb trusts them only if they match the data\'s change counter, its row count and its exact totals.', 'A structure that does not match is discarded or rebuilt, so a restored database answers from its first query without rebuilding.'],
    caption: `Illustrative motion. Measured at ${grp(race.rows)} invoices on one NVMe laptop: built once in ${R.loadSeconds.toFixed(1)} s, ${gb(R.sizeBytes)} GB with its structures; reopened in ${R.openSeconds.toFixed(2)} s; all 16 queries cold (file cache cleared first) in ${race.arms[0].coldTotalSeconds.toFixed(2)} s in total. Tuned DuckDB opens its file in ${A.duck.openSeconds.toFixed(2)} s and the SQL Server service was already running. The bars are drawn to one scale.`,
    fig: { loadSeconds: R.loadSeconds, openSeconds: R.openSeconds, coldTotal: R.coldTotalSeconds, files },
  },
  {
    id: 'exact', tab: 'Exact', promise: 'Exact to the last digit.',
    how: ['Integer sums are held in 128 bits, never rounded and never wrapped: a sum that would pass 64 bits throws an error instead of returning a wrong number.', 'Before any query is timed, its rows are fingerprinted and compared across HoloDb, DuckDB and SQL Server.'],
    caption: `Illustrative motion. Measured: the fingerprint of all 16 queries is identical on all four arms at ${grp(race.rows)} invoices, including the queries where HoloDb is slower. Money is held as integer cents; DECIMAL is a double.`,
    fig: { total: Number(total.first), engines: ['HoloDb restored', 'DuckDB tuned', 'SQL Server tuned'], hashes: race.queries.map((q) => q.hash) },
  },
  {
    id: 'disk', tab: 'Checksums', promise: 'It tells you when the disk lies.',
    how: ['Every 8 KB data page carries a checksum, and every read checks it.', 'A page that fails is refused with an error that names the page, so a flipped bit is reported and never read as an answer.'],
    caption: `Illustrative layout. Measured: ${FLIP.flipped} bits flipped in the data file of a ${FLIP.sizeMb} MB paged table; ${FLIP.reported} were reported with an error naming the page, ${FLIP.unread} were in pages never read, and none changed an answer. The damage is simulated; a real power cut and a full disk are not tested.`,
    fig: FLIP,
  },
];
const how = { schema: 'ea-diagram/1', kind: 'holodb-how', id: 'holodb-how', generator: 'web/scripts/migrate/extract-holodb-story.mjs', scenes };
fs.writeFileSync(path.join(DATA, 'diagrams', 'holodb-how.json'), JSON.stringify(how, null, 1) + '\n', 'utf8');

// ======================================================================= fit
const questions = [
  { id: 'work', short: 'Workload', label: 'What does your app mostly do?', default: 'reads', options: [{ id: 'reads', label: 'Reads' }, { id: 'mixed', label: 'Reads and writes' }, { id: 'writes', label: 'Writes' }] },
  { id: 'queries', short: 'Queries', label: 'Do you know the queries ahead?', default: 'known', options: [{ id: 'known', label: 'Known: dashboards, reports' }, { id: 'adhoc', label: 'Ad hoc: any filter, any day' }] },
  { id: 'size', short: 'Data', label: 'How much data?', default: 'm10', options: [{ id: 'xs', label: 'Under 1M rows' }, { id: 'm1', label: '1M to 10M' }, { id: 'm10', label: '10M to 100M' }, { id: 'm100', label: '100M or more' }] },
  { id: 'writers', short: 'Writers', label: 'Who writes?', default: 'one', options: [{ id: 'one', label: 'One process' }, { id: 'many', label: 'Several processes' }] },
  { id: 'lang', short: 'Language', label: 'Which language?', default: 'net', options: [{ id: 'net', label: '.NET' }, { id: 'other', label: 'Something else' }] },
  { id: 'exact', short: 'Exact sums', label: 'Need exact sums?', default: 'yes', options: [{ id: 'yes', label: 'Yes' }, { id: 'no', label: 'No' }] },
  { id: 'sim', short: 'Similarity', label: 'Need similarity search?', default: 'no', options: [{ id: 'no', label: 'No' }, { id: 'yes', label: 'Yes' }] },
  { id: 'where', short: 'Runs', label: 'Where does it run?', default: 'embedded', options: [{ id: 'embedded', label: 'Inside the app' }, { id: 'browser', label: 'In the browser' }, { id: 'server', label: 'As a server' }] },
  { id: 'sql', short: 'SQL', label: 'How much SQL?', default: 'basic', options: [{ id: 'basic', label: 'Select, join, group by' }, { id: 'wide', label: 'CASE, UNION, outer joins, aliases' }] },
];
const rules = [
  { when: { work: ['reads'] }, kind: 'plus', line: `Reads become microseconds because totals are kept up to date as rows are written: ${n2(warm('total invoiced', 'restored'))} ms for a total over ${grp(race.rows)} invoices, against ${n2(warm('total invoiced', 'duck'))} ms for tuned DuckDB scanning.` },
  { when: { work: ['mixed'] }, kind: 'caution', weight: 1, line: writeLine },
  { when: { work: ['writes'] }, kind: 'caution', weight: 2, alt: 'sqlite', line: `${writeLine} Each durable commit is one fsync with no group commit, and its cost against other databases is not measured.` },
  { when: { queries: ['known'] }, kind: 'plus', line: 'Known dashboards and reports can be declared as summaries and indexes, saved beside the data and read in microseconds. The planner also builds them once repeats pay for them.' },
  { when: { queries: ['adhoc'], size: ['xs', 'm1'] }, kind: 'note', line: 'Ad hoc questions scan, and a scan over a few million rows is quick in any engine. Questions that repeat earn a structure.' },
  { when: { queries: ['adhoc'], size: ['m10'] }, kind: 'caution', weight: 1, alt: 'duckdb', line: `Scans that no structure covers are slower than tuned DuckDB until the planner has built one: out of the box at ${grp(race.rows)} invoices, ${listWords(scanCases)}.` },
  { when: { queries: ['adhoc'], size: ['m100'] }, kind: 'caution', weight: 2, alt: 'duckdb', line: `Scans that no structure covers are slower than tuned DuckDB until the planner has built one: out of the box at ${grp(race.rows)} invoices, ${listWords(scanCases)}.` },
  { when: { size: ['xs'] }, kind: 'caution', weight: 1, alt: 'sqlite', line: 'Under a million rows every engine answers quickly, so maintained totals save little. SQLite is one file with the widest tooling.' },
  { when: { size: ['m100'] }, kind: 'caution', weight: 1, line: `Tested to ${grp(race.rows)} rows (${gb(A.oob.sizeBytes)} GB paged, ${Math.round(A.oob.loadSeconds)} s to load). Beyond that is untested.` },
  { when: { writers: ['one'] }, kind: 'note', line: 'One process owns a database and one writer runs at a time, which suits an embedded store.' },
  { when: { writers: ['many'] }, kind: 'blocker', alt: 'pg', line: 'One process owns a database\'s files and one writer runs at a time, with no MVCC. A second process cannot open the files, and a long transaction blocks every other writer.' },
  { when: { lang: ['net'] }, kind: 'plus', line: 'A .NET package: call it in process from ordinary C#.' },
  { when: { lang: ['other'] }, kind: 'blocker', alt: 'pg', line: 'The engine is a .NET package, and the server\'s only client is .NET. There is no JDBC, ODBC, Python, Node or Go client and no PostgreSQL wire compatibility.' },
  { when: { exact: ['yes'] }, kind: 'plus', line: `Integer sums are exact: held in 128 bits, never wrapped, and identical to DuckDB and SQL Server on all 16 queries. Money is held as integer cents, because DECIMAL is a double.` },
  { when: { sim: ['yes'] }, kind: 'plus', line: 'NEAREST ranks rows by closeness in SQL with no separate vector index. From 8,000 rows an approximate index ranks the candidates; with a WHERE it is an exact scan.' },
  { when: { where: ['embedded'] }, kind: 'plus', line: 'Embedded, a point query takes 1 to 3 microseconds, with no client or network in the way.' },
  { when: { where: ['browser'] }, kind: 'plus', line: 'It runs in a browser tab: the same managed code, which is how The Analyst works.' },
  { when: { where: ['server'] }, kind: 'caution', weight: 1, line: 'As a server it speaks TCP and TLS to a .NET client, and a point query pays about 0.07 ms of client, TLS and socket cost that the embedded engine does not.' },
  { when: { sql: ['basic'] }, kind: 'note', line: 'SELECT, WHERE, GROUP BY, HAVING, ORDER BY, LIMIT and INNER JOIN are supported: several joins on in-memory tables, one on a paged table.' },
  { when: { sql: ['wide'] }, kind: 'blocker', alt: 'pg', line: 'The SQL is narrower than PostgreSQL\'s: no AS aliases, ALTER TABLE ADD COLUMN only, no CASE, OFFSET, UNION or outer joins, no expressions in the select list, and one join on a paged table.' },
];
const alts = {
  duckdb: { name: 'Tuned DuckDB', line: `for ad hoc analytics scans, where it needs no declared structure. At ${grp(race.rows)} invoices it beat HoloDb out of the box on ${oobBehind.length} of ${race.queries.length} queries, the ones that scan.` },
  pg: { name: 'PostgreSQL or SQL Server', line: 'for several writers or processes, MVCC, a wide SQL dialect, and drivers in every language.' },
  sqlite: { name: 'SQLite', line: `for a small single-file store with the widest tooling, and for write-heavy single-row work: its single-row INSERT took ${span(wr.INSERT.sql)} microseconds against HoloDb's ${span(wr.INSERT.holo)} (engine alone, in memory).` },
};
const tradeoffs = [
  { title: 'Read speed against write cost', left: { label: 'Reads', text: `A total over ${grp(race.rows)} invoices reads in ${n2(warm('total invoiced', 'restored'))} ms, because totals are kept up to date as rows are written.` }, right: { label: 'Writes and space', text: `Single-row INSERT, UPDATE and DELETE took ${writeRatio} times SQLite's time (engine alone, in memory). The structures take space: ${gb(A.restored.sizeBytes)} GB restored, ${gb(A.oob.sizeBytes)} GB out of the box, ${gb(A.duck.sizeBytes)} GB for tuned DuckDB.` } },
  { title: 'Exact against approximate', left: { label: 'Exact', text: 'Integer sums are 128-bit and identical on all four engines for all 16 queries. A sum past 64 bits throws; it never wraps.' }, right: { label: 'Approximate', text: 'Only similarity search: from 8,000 rows an approximate index picks the candidates. Money is integer cents, because DECIMAL is a double.' } },
  { title: 'Automatic against hand-tuned', left: { label: 'Automatic', text: 'The planner builds a summary or index when repeats pay for it, and drops it when writes cost more than it saves.' }, right: { label: 'Hand-tuned', text: `A DBA can declare structures, which are saved and never dropped. Without them, scans no structure covers are slower than tuned DuckDB: ${cnt.oob.duck.behind} of ${cnt.oob.duck.n} queries at ${grp(race.rows)} invoices.` } },
  { title: 'Embedded against server', left: { label: 'Embedded', text: 'In process: a point query takes 1 to 3 microseconds. One process owns the files and one writer runs at a time.' }, right: { label: 'Server', text: 'TCP and TLS with a typed .NET client: about 0.07 ms more per point query. No JDBC, ODBC or PostgreSQL wire.' } },
  { title: 'Memory against disk', left: { label: 'In memory', text: 'The fastest path, bounded by RAM at about 110 bytes per row.' }, right: { label: 'Paged', text: `Tables bigger than memory through an 8 KB buffer pool: ${grp(race.rows)} invoices in ${gb(A.oob.sizeBytes)} GB, ${Math.round(A.oob.loadSeconds)} s to load, a ${gb(A.oob.peakBytes)} GB peak working set out of the box and ${gb(A.restored.peakBytes)} GB restored.` } },
];
const limits = [
  ['Joins', 'INNER JOIN only. Several in one statement on in-memory tables, one on a paged table. LEFT, RIGHT, FULL, CROSS, NATURAL, USING and comma joins are refused.'],
  ['Aliases', 'No AS on columns or tables. Aggregate columns are named agg(col), such as count(*).'],
  ['ALTER TABLE', 'ADD COLUMN only, with a DEFAULT or NULL. No DROP COLUMN, RENAME COLUMN or RENAME TO.'],
  ['Expressions', 'No CASE, LENGTH(), OFFSET or UNION, and no arithmetic in the select list or inside SUM(v * 2). COUNT(DISTINCT col), LIKE and IN (SELECT ...) work on in-memory tables.'],
  ['Money', 'Constant-time totals are for INT columns. REAL SUM is a SIMD scan, and DECIMAL and NUMERIC are REAL (a double), so hold money as integer cents. There is no fixed-point type.'],
  ['Saved summaries', 'A summary made with CREATE INDEX ... USING SUMMARY, or built by the planner, is saved with the table and restored on open. A plane attached with the EnablePleroma call on an in-memory table is not saved: call it again after Open.'],
  ['Summary limits', 'A declared summary takes up to 4,095 distinct values per column (1,024 for one the planner builds) and a joint of 12 bits. A column over its limit goes to a scan and is never approximated.'],
  ['Joins on paged tables', `The keyed summary holds up to 4,194,304 consecutive INT keys. The ${grp(race.rows)}-invoice run joins ${grp(custCount)} customers.`],
  ['Languages', 'A .NET library. The server speaks a TCP and TLS binary protocol whose only client is .NET. No JDBC, ODBC, Python, Node or Go client, and no PostgreSQL or MySQL wire.'],
  ['Concurrency', 'One process per database and one writer at a time. Simple aggregate reads run off a snapshot without the lock. A second process cannot open a paged database\'s files, and there is no MVCC: a long transaction blocks other writers.'],
  ['Table size', `Tested to ${grp(race.rows)} rows (${gb(A.oob.sizeBytes)} GB paged, ${Math.round(A.oob.loadSeconds)} s to load). In-memory tables are bounded by RAM at about 110 bytes per row. A paged primary key loaded out of order keeps a RAM dictionary of about 35 bytes per row. Beyond ${grp(race.rows)} rows is untested.`],
  ['Backup', 'Offline: copy the files of a closed database. There is no online backup and no point-in-time recovery.'],
  ['Crash safety', 'One fsync per commit. Tests cut the log at every record boundary, flip bits in the log and the data file, and tear a page during a checkpoint. A real power cut, a disk that lies about fsync, a full disk and several processes are not tested.'],
];
const fit = {
  schema: 'ea-diagram/1', kind: 'holodb-fit', id: 'holodb-fit', generator: 'web/scripts/migrate/extract-holodb-story.mjs',
  thresholds: { possible: 1, other: 4 },
  kindLabels: { plus: 'Fits', note: 'Note', caution: 'Trade-off', blocker: 'Does not fit' },
  verdicts: { good: { line: 'Your answers sit where HoloDb is strongest.' }, possible: { line: 'It can work. Weigh the trade-offs below before you commit.' }, other: { line: 'At least one answer points to a tool built for it.' } },
  noAlt: 'Nothing in your answers points to another database.',
  rule: 'A thing HoloDb does not do is a hard no. Trade-offs add up: one makes it possible, several make another tool the better choice.',
  questions, rules, alts, tradeoffs, limits,
  sources: { writeRatio },
};
fs.writeFileSync(path.join(DATA, 'diagrams', 'holodb-fit.json'), JSON.stringify(fit, null, 1) + '\n', 'utf8');
console.log('wrote holodb-how.json (6 scenes) and holodb-fit.json', rules.length, 'rules');
console.log('scan cases:', scanCases.join(' | '));
console.log('write line:', writeLine);
