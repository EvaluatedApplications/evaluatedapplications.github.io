// Turns the Phasor owner's article source (Phasor/docs/pleroma-article.md) into the research article data/research/forty-five-reads-one-store.json.
// Nothing about the article is typed here except what the page needs that the source does not carry (headline, sub-line, topics, table captions,
// the three tiles' wording). Every tile figure and every line of small print is checked against the article text, so a re-measured source that no
// longer says it makes this script fail.
//   node web/scripts/migrate/extract-pleroma-article.mjs
// What it strips: the title and byline lines, the Sources section and the "Not cited" line, every bracketed [n] reference marker, the licence
// footer and copyright line (the page ends with the site's own licence sentence). The one wording edit it applies is listed in EDITS.
import fs from 'node:fs';
import path from 'node:path';
import { MONOREPO, DATA } from '../lib/data.mjs';

const SRC = path.join(MONOREPO, 'Phasor', 'docs', 'pleroma-article.md');
const OUTF = path.join(DATA, 'research', 'forty-five-reads-one-store.json');

// wording changes needed to render (each is reported by the build notes; none changes a claim)
const EDITS = [
  ['In a sequence-model prototype, the same exact counts', 'In a sequence-model test, the same exact counts'],
];
const CAPTIONS = [
  'The ladder: eight rungs, each read across three faces',
  'The 24 cells: the public member for each rung and face, and the lid',
  'The same exact counts held two ways: a hash map and one dense Pleroma page per context',
];
const TOPICS = ['exact stores', 'Pleroma'];
const HEADLINE = 'Forty-seven reads, one store.';
const SUB = 'One fixed-size array of exact integers. Counts, order, parity, time and nearest words, each in a single pass. When it is full, it refuses instead of guessing.';
const DESCRIPTION = 'One fixed-size array of exact integers answers forty-seven different questions, each in a single pass, and refuses instead of guessing when it is full.';
// tiles: value and label as the user approved them; `small` is quoted from the article and must appear in it verbatim
const TILES = [
  { value: '64', label: 'items exact, against 4 for the best HRR decoder (200 of 200 trials)',
    small: "At 2,064 and 2,580 bits, the two budgets the benchmark ran, the Pleroma recovered 64 items exactly, in 200 of 200 trials. Plate's holographic memory (HRR), and its phase form (FHRR), with the best decoder tried, recovered 4 items reliably and none from 16 upward." },
  { value: '0', label: 'wrong answers in 12,800 trials past capacity',
    small: "Past the Pleroma's capacity, 65 to 96 items, it refused all 12,800 trials and gave no wrong answer. The baselines answered every time, and every answer was wrong." },
  { value: '0.17 to 0.44 ms', label: 'per kernel apply on a 10,464-cell store',
    small: 'On a 10,464-cell store, `Apply` takes 0.17 to 0.44 ms per call, for a kernel of anything from 1 operator up to 10,368, with no trend across that range. Building the same store from the moved items takes 1,916 ms at 1,000 operators. The figures are a Release build on a loaded machine, two runs.' },
];
const CLOSING_LINKS = [{ label: 'Install the Phasor package', href: '/phasor/' }, { label: 'Walk the grid on the Pleroma page', href: '/phasor/pleroma/' }, { label: 'Partner with us', href: '/partner/' }];

let src = fs.readFileSync(SRC, 'utf8').replace(/\r\n/g, '\n');
src = src.split('\n## Sources')[0];
src = src.replace(/\s*(?:\[\d+\])+(?=\s|$)/g, '');
if (/\[\d+\]/.test(src)) throw new Error('a bracketed reference marker is left in the text');
for (const [from, to] of EDITS) { if (!src.includes(from)) throw new Error('edit target not found: ' + from); src = src.replace(from, to); }

const flat = src.replace(/\s+/g, ' ');
const text = (s) => s.replace(/\s+/g, ' ');
for (const t of TILES) if (!text(flat).includes(text(t.small))) throw new Error('tile small print is not in the article: ' + t.small.slice(0, 60));
// tile figures must be present in their own small print (the label may restate them)
const must = [['64', 0], ['200 of 200', 0], ['12,800', 1], ['0.17 to 0.44', 2], ['10,464', 2]];
for (const [f, i] of must) if (!TILES[i].small.includes(f)) throw new Error(`tile ${i} figure "${f}" is not in its small print`);

const chunks = src.split(/\n{2,}/).map((c) => c.trim()).filter(Boolean);
const slug = (t) => t.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const kindOf = (c) => {
  const ls = c.split('\n');
  if (/^\|/.test(ls[0])) return 'table';
  if (/^- /.test(ls[0])) return 'ul';
  if (/^> /.test(ls[0])) return 'quote';
  if (/^\*\*[^*]+\*\*$/.test(c)) return 'sub';
  return 'p';
};
const parseTable = (c, caption) => {
  const rows = c.split('\n').map((l) => l.replace(/^\||\|$/g, '').split('|').map((x) => x.trim()));
  return { t: 'table', caption, columns: rows[0], rows: rows.slice(2) };
};
const blockOf = (c, ti) => {
  const k = kindOf(c);
  if (k === 'table') return parseTable(c, CAPTIONS[ti.n++]);
  if (k === 'ul') { const items = c.split('\n'); if (items.some((l) => !/^- /.test(l))) throw new Error('mixed list chunk: ' + c.slice(0, 60)); return { t: 'ul', items: items.map((l) => l.replace(/^- /, '')) }; }
  if (k === 'quote') return { t: 'quote', text: c.replace(/^> ?/gm, '').trim() };
  if (k === 'sub') return { t: 'sub', text: c.replace(/^\*\*|\*\*$/g, '') };
  if (c.includes('\n')) throw new Error('multi-line paragraph chunk: ' + c.slice(0, 60));
  return { t: 'p', text: c };
};

let i = 0;
if (!/^# /.test(chunks[i])) throw new Error('no title line'); i++;
i++; // byline: "The Pleroma - Evaluated Applications - <month year>"; no byline or date is carried over
const intro = [];
while (!/^\*\*Words\*\*$/.test(chunks[i])) intro.push(chunks[i++]);
if (intro.length !== 3) throw new Error('expected three opening paragraphs, found ' + intro.length);
i++;
const words = chunks[i++].split('\n').map((l) => { const m = l.match(/^- \*\*([^*]+?):\*\* (.*)$/); if (!m) throw new Error('word line: ' + l); return { term: m[1], text: m[2] }; });

const sections = [{ id: 'in-brief', title: 'In brief', untitled: true, blocks: [{ t: 'p', text: intro[1] }, { t: 'p', text: intro[2] }] }];
const ti = { n: 0 };
let cur = null, mode = 'body', closing = [];
for (; i < chunks.length; i++) {
  const c = chunks[i];
  if (c === '---') { if (mode === 'body') mode = 'tail'; continue; }
  const h = c.match(/^## (.*)$/);
  if (h) {
    const dm = h[1].match(/^Go deeper: (.*)$/);
    if (dm) { const title = dm[1][0].toUpperCase() + dm[1].slice(1); cur = { id: slug(title), title, summary: 'Go deeper: ' + dm[1], disclosure: true, blocks: [] }; mode = 'deeper'; }
    else { cur = { id: slug(h[1]), title: h[1], blocks: [] }; mode = 'body'; }
    sections.push(cur); continue;
  }
  if (mode === 'tail') {
    if (/^\*Proprietary/.test(c) || /^Copyright \(c\)/.test(c)) continue;
    closing.push(c); continue;
  }
  cur.blocks.push(blockOf(c, ti));
}
if (ti.n !== CAPTIONS.length) throw new Error(`found ${ti.n} tables, ${CAPTIONS.length} captions`);
if (closing.length !== 1) throw new Error('expected one closing paragraph, found ' + closing.length);
const ids = sections.map((s) => s.id); if (new Set(ids).size !== ids.length) throw new Error('duplicate section id');

const out = {
  id: 'forty-five-reads-one-store',
  title: 'Forty-seven reads, one store',
  headline: HEADLINE,
  sub: SUB,
  summary: SUB,
  description: DESCRIPTION,
  topics: TOPICS,
  order: 0,
  status: 'migrated',
  voice: 'owner-authored',
  finding: intro[0],
  stats: TILES,
  words,
  related: [],
  references: [],
  sections,
  closing: { text: closing[0], links: CLOSING_LINKS },
  diagram: {
    kind: 'pleroma-grid',
    title: 'The grid: every cell, with its kind',
    lede: 'Eight rungs by three faces, and the lid. Each cell is one public operation of the Pleroma. Select a cell for its API, what it does and the check behind it.',
    stageLabel: 'The Pleroma operation grid. Select a cell, or use the arrow keys to move between cells.',
    caption: 'Drawn from the Pleroma map record: the cells, their kind and their evidence are that record, unchanged. Nothing is measured on this page.',
  },
};
fs.writeFileSync(OUTF, JSON.stringify(out, null, 2) + '\n', 'utf8');
console.log('wrote', path.relative(DATA, OUTF), fs.statSync(OUTF).size, 'bytes;', sections.length, 'sections (' + sections.filter((s) => s.disclosure).length + ' go-deeper), ' + words.length + ' words, ' + ti.n + ' tables');
console.log('wording edits applied:', EDITS.map((e) => `"${e[0]}" -> "${e[1]}"`).join('; '));
