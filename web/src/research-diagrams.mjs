// Turns the numbers in a research article's own tables into the data its diagram draws. Nothing is typed here:
// every value is parsed from sections[].tables, or taken from the article's `diagram` block, which restates numbers from its prose.
export function deriveDiagram(r) {
  const d = r.diagram;
  if (!d) return null;
  const tbl = (id) => {
    const t = r.sections.flatMap((s) => s.tables || []).find((x) => x.id === id);
    if (!t) throw new Error(`research ${r.id}: diagram needs table "${id}"`);
    return t;
  };
  const num = (s) => Number(String(s).replace(/,/g, '').match(/-?\d+(\.\d+)?/)[0]);
  const big = (s) => { const m = String(s).replace(/,/g, '').match(/^([\d.]+)\s*([MK]?)/); return Number(m[1]) * (m[2] === 'M' ? 1e6 : m[2] === 'K' ? 1e3 : 1); };
  const base = { schema: 'ea-diagram/1', kind: d.kind, article: r.id };
  if (d.kind === 'round-timeline') return { ...base, end: d.end, battery: d.battery, items: d.items };
  if (d.kind === 'slot-grow') return { ...base, from: d.from, grows: d.grows };
  if (d.kind === 'design-effect') return { ...base, groups: d.groups };
  if (d.kind === 'size-bars') {
    const sz = tbl(d.sizeTable), sp = tbl(d.spellingTable);
    let ref = 0;
    return {
      ...base,
      sizes: sz.rows.map(([label, v]) => ({ label: label === 'TinyStories reference' ? `TinyStories reference ${v}` : label, value: big(v), reference: label !== 'This checkpoint' && ++ref > 0, shown: v })),
      spelling: sp.rows.map(([label, v]) => { const m = String(v).match(/\(([\d.]+)%/); return { label, value: num(v), earlier: m ? Number(m[1]) : null }; }),
    };
  }
  if (d.kind === 'layer-stack') {
    const rd = tbl(d.readoutTable), last = tbl(d.lastTable), ps = tbl(d.passesTable);
    const pair = (s) => { const [c, e] = String(s).split('/').map((x) => Number(x.trim())); return { confidence: c, candidates: e }; };
    return {
      ...base,
      points: rd.rows.map(([label, c, e]) => ({ label, confidence: Number(c), candidates: Number(e) })),
      last: last.rows.map(([pos, four, six]) => ({ position: pos, four: pair(four), six: pair(six) })),
      passes: ps.rows.map(([model, doing, total]) => ({ model, doing: Number(doing), total: Number(total) })),
    };
  }
  throw new Error(`research ${r.id}: unknown diagram kind ${d.kind}`);
}