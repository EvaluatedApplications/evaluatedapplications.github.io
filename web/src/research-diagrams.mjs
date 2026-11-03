// Turns the numbers in a research article's own diagram block into the data its diagram draws. Nothing is typed here:
// every value is taken from the article's `diagram` block, which restates numbers from its prose, or from a record the site already holds.
export function deriveDiagram(r, ctx) {
  const d = r.diagram;
  if (!d) return null;
  // the operation grid is the Phasor map record itself (data/diagrams/pleroma-map.json), shown as a grid instead of the pyramid
  if (d.kind === 'pleroma-grid') {
    if (!ctx || !ctx.diagrams['pleroma-map']) throw new Error(`research ${r.id}: pleroma-grid needs the pleroma-map diagram data`);
    return { ...ctx.diagrams['pleroma-map'], kind: 'pleroma-grid', article: r.id };
  }
  const base = { schema: 'ea-diagram/1', kind: d.kind, article: r.id };
  if (d.kind === 'design-effect') return { ...base, groups: d.groups };
  throw new Error(`research ${r.id}: unknown diagram kind ${d.kind}`);
}