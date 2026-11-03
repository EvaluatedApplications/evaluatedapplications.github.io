// Explainer diagram: rent or buy. The ledger behind every structure the engine builds or moves: rent while a question keeps paying for blocks it did
// not need, buy once the rent paid reaches the price. The three costs (about 200 microseconds per moved row, about 12 page deltas, about 100 uses to
// pay back) are recorded in data/diagrams/holodb-rent.json with their file; the three lines are the rule, drawn as a rule, and the caption says so.
// The figure opens on its final frame and animates only on Play. Module contract: metrics / staticSvg / controlsHtml / tablesHtml / verify / mount.
import { hasDom } from './core.js';
import { esc, fmt, clamp, pen, svgOpen, playControls, player, tablesWrap, tbl } from './explain-kit.js';

export function metrics(d) {
  const c = d.cost;
  return { rowMicros: c.rowMicros, pageDeltas: c.pageDeltas, payback: c.paybackUses, uses: d.axis.uses, pct: d.axis.percent };
}

export function limitFacts(m) {
  return { rowMicros: String(m.rowMicros), pageDeltas: String(m.pageDeltas), paybackUses: String(m.payback) };
}

// Cost so far, as a percent of the price of the move, after n uses.
export const costs = (m, n) => ({ rent: n, buy: n > 0 ? 100 : 0, rule: n >= m.payback ? 2 * m.payback : n });

export function readoutFor(m, n) {
  const c = costs(m, n);
  const tail = n >= m.payback ? ' The ledger bought the move at ' + m.payback + ' uses and pays nothing more; it has spent twice the price, the most it can ever spend over the cheaper choice.' : ' The ledger is still renting, the same cost as renting.';
  return `After ${fmt(n)} uses, renting every time has cost ${fmt(c.rent)}% of the price of the move, buying at the first use ${fmt(c.buy)}%, and the ledger ${fmt(c.rule)}%.${tail} Recorded costs; the curves are the rule drawn as a rule, so the motion is illustrative.`;
}

export function chartSvg(d, m, t, narrow, uid) {
  const p = pen(), W = narrow ? 360 : 700, H = narrow ? 338 : 320, L = 46, R = 14, T = 16, B = narrow ? 114 : 46, pw = W - L - R, ph_ = H - T - B;
  const X = (u) => L + (u / m.uses) * pw, Y = (v) => T + ph_ - (v / m.pct) * ph_;
  const n = Math.round(clamp(t) * m.uses), pay = m.payback;
  for (const v of [0, 50, 100, 150, 200]) { p.l(L, Y(v), W - R, Y(v), 'rn-grid' + (v === 100 ? ' price' : '')); p.t(L - 6, Y(v) + 4, v + '%', 'rn-tick', 'end'); }
  for (const u of [0, 100, 200, 300]) p.t(X(u), H - B + 16, String(u), 'rn-tick', 'middle');
  p.t(L + pw / 2, narrow ? H - B + 34 : H - 8, 'Uses of the question (the price of moving the rows is 100%)', 'rn-ax', 'middle');
  p.t(W - R, Y(100) - 5, 'price of the move', 'rn-price', 'end');
  p.l(X(pay), T, X(pay), H - B, 'rn-pay'); p.t(X(pay) + 5, T + 12, `pays back in about ${pay} uses`, 'rn-note');
  const ne = Math.min(n, m.pct), rent = `M${X(0).toFixed(1)} ${Y(0).toFixed(1)}L${X(ne).toFixed(1)} ${Y(ne).toFixed(1)}`;
  p.p(rent, 'rn-line rent');
  if (n > 0) p.p(`M${X(Math.min(1, n)).toFixed(1)} ${Y(100).toFixed(1)}L${X(n).toFixed(1)} ${Y(100).toFixed(1)}`, 'rn-line buy');
  const rp = n < pay ? `M${X(0).toFixed(1)} ${Y(0).toFixed(1)}L${X(n).toFixed(1)} ${Y(n).toFixed(1)}` : `M${X(0).toFixed(1)} ${Y(0).toFixed(1)}L${X(pay).toFixed(1)} ${Y(pay).toFixed(1)}L${X(pay).toFixed(1)} ${Y(2 * pay).toFixed(1)}L${X(n).toFixed(1)} ${Y(2 * pay).toFixed(1)}`;
  p.p(rp, 'rn-line rule');
  if (n >= pay) p.t(X(pay) + 6, Y(2 * pay) + 14, 'bought here', 'rn-note ok');
  const c = costs(m, n);
  p.l(X(n), T, X(n), H - B, 'rn-now');
  p.c(X(ne), Y(ne), 4, 'rn-dot rent'); p.c(X(n), Y(c.buy), 4, 'rn-dot buy'); p.c(X(n), Y(c.rule), 5, 'rn-dot rule');
  const kx = narrow ? L : X(125), ky = narrow ? H - B + 58 : Y(70);
  [['rent', d.lines[0].label], ['buy', d.lines[1].label], ['rule', d.lines[2].label]].forEach(([k, lab], i) => { p.c(kx + 4, ky + i * 18 - 3, 4, 'rn-dot ' + k); p.t(kx + 14, ky + i * 18, lab, 'rn-key'); });
  return svgOpen('rn-ledger', W, H, uid, `Rent or buy: ${d.rule} Recorded costs, illustrative curves.`) + p.g.join('') + '</svg>';
}

export const staticSvg = (d) => chartSvg(d, metrics(d), 1, false, 'rn-' + d.id);

export function controlsHtml(id, m, d) {
  return {
    tabs: '',
    controls: `<div class="hw-copy"><p class="hw-promise">Moving one row costs about ${m.rowMicros} microseconds, and a scattered question pays that back in about ${m.payback} uses.</p><p class="hw-how">${esc(d.rule)} The price is about ${m.pageDeltas} page deltas per moved row.</p></div>
${playControls('Uses of the question', 'Uses')}
<p class="dia-readout hw-caption" data-ctl="readout" role="status">${esc(readoutFor(m, m.uses))}</p>`,
  };
}

export function tablesHtml(d, m) {
  const c = tbl('The recorded costs', ['Cost', 'Recorded'], [
    ['One moved row', `about ${m.rowMicros} microseconds`], ['One moved row, in page deltas', `about ${m.pageDeltas}`], ['A scattered question pays the move back in', `about ${m.payback} uses`]]);
  const pts = [0, 50, 100, 150, 300].map((n) => { const k = costs(m, n); return [`${n} uses`, `${k.rent}%`, `${k.buy}%`, `${k.rule}%`]; });
  const t = tbl('Cost so far as a percent of the price of the move (the rule)', ['Uses', d.lines[0].label, d.lines[1].label, d.lines[2].label], pts);
  const notes = `<ul class="dia-notes">${d.notes.map((x) => `<li>${esc(x)}</li>`).join('')}</ul>`;
  return tablesWrap('Data behind the diagram', c + t + notes);
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  if (m.uses <= m.payback) out.push('the axis must run past the payback point');
  const a = costs(m, m.uses);
  if (!(a.rule <= 2 * Math.min(a.rent, a.buy))) out.push('the ledger costs more than twice the cheaper choice');
  if (!/Data behind the diagram/.test(body) || (body.match(/<tbody>/g) || []).length !== 2) out.push('two tables expected');
  return out;
}

export const interactiveStage = false;

export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage'), read = root.querySelector('[data-ctl=readout]');
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  let narrow = stage.getBoundingClientRect().width < 520;
  const pl = player(root, (t) => { stage.innerHTML = chartSvg(d, m, t, narrow, 'rn-' + d.id); read.textContent = readoutFor(m, Math.round(clamp(t) * m.uses)); }, 9000);
  if (typeof ResizeObserver === 'function') new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; pl.paint(); } }).observe(stage);
  pl.paint();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="holodb-rent"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
