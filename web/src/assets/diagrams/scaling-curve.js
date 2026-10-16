// Evidence diagram: cost against size on logarithmic axes. One tab per scaling question; each tab stacks one or two charts
// (time, and a count such as commit groups or transform passes). Every line is a recorded run; the slope in the key is the fit of
// log time on log size. The same module draws the server-rendered chart and the interactive one, so the picture and the table
// cannot disagree. Data: data/diagrams/holodb-scaling.json, written from the recorded runs by scripts/migrate/extract-holodb-scaling.mjs.
import { hasDom, prefersReducedMotion, tablist } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB', { maximumFractionDigits: 0 });
const SLOPE_TOL = 0.011;   // the runs print slopes to two decimals

export function fit(pts) {
  const X = pts.map((p) => Math.log10(p.x)), Y = pts.map((p) => Math.log10(p.y)), n = pts.length;
  const mx = X.reduce((a, b) => a + b) / n, my = Y.reduce((a, b) => a + b) / n;
  const sxx = X.reduce((a, x) => a + (x - mx) ** 2, 0);
  return sxx === 0 ? 0 : X.reduce((a, x, i) => a + (x - mx) * (Y[i] - my), 0) / sxx;
}

export function metrics(d) {
  const charts = d.panels.reduce((a, p) => a + p.charts.length, 0);
  return { panels: d.panels.length, charts, n: d.panels.map((p) => p.charts[0].series[0].points.length), nullSlope: d.null.slope };
}

const isConstant = (s) => s.points.every((p) => p.y === s.points[0].y);
export function slopeText(s, chart) {
  if (chart.yKind === 'count') return isConstant(s) ? `constant at ${n0(s.points[0].y)}` : `slope ${s.slope.toFixed(2)}`;
  return s.nearlyFlat ? (s.flat ? 'flat' : 'nearly flat') : `slope ${s.slope.toFixed(2)}`;
}

// a time in microseconds, in words
function timeWords(us) {
  if (us < 1000) return `${n0(us)} microseconds`;
  const ms = us / 1000;
  return `${ms < 10 ? ms.toFixed(2) : n0(ms)} milliseconds`;
}
const valueWords = (chart, y) => (chart.yKind === 'time' ? timeWords(y) : n0(y));
function tickText(chart, e) {
  const v = 10 ** e;
  if (chart.yKind === 'count') return n0(v);
  return e < 3 ? `${n0(v)} us` : e < 6 ? `${n0(v / 1000)} ms` : `${n0(v / 1e6)} s`;
}

export function readoutFor(d, pi, i) {
  const p = d.panels[pi], xs = p.charts[0].series[0].points;
  const parts = p.charts.map((c) => `${c.yLabel.replace(/^./, (ch) => ch.toUpperCase())}: ${c.series.map((s) => `${s.label}, ${valueWords(c, s.points[i].y)}`).join('; ')}`);
  return `At ${n0(xs[i].x)} ${p.xLabel}. ${parts.join('. ')}.`;
}

// Geometry shared by the drawing and the pointer, so a pointer position finds the same x as the chart drew.
export function geom(d, pi, narrow = false, width) {
  const W = width || (narrow ? 340 : 700), L = narrow ? 52 : 74, R = narrow ? 36 : 40, LEG = 60, PH = narrow ? 112 : 124, XL = 38;
  const CH = 8 + LEG + PH + XL, p = d.panels[pi], xs = p.charts[0].series[0].points.map((q) => q.x);
  const lx0 = Math.log10(xs[0]), lx1 = Math.log10(xs[xs.length - 1]);
  const X = (x) => L + 8 + ((Math.log10(x) - lx0) / (lx1 - lx0 || 1)) * (W - L - R - 16);
  return { W, L, R, LEG, PH, XL, CH, H: CH * p.charts.length, xs, X };
}

export function chartSvg(d, pi, { narrow = false, i = -1, id = 'sc' } = {}) {
  const g = geom(d, pi, narrow), p = d.panels[pi];
  const body = p.charts.map((c, k) => {
    const y0 = k * g.CH, top = y0 + 8 + g.LEG;
    const all = c.series.flatMap((s) => s.points.map((q) => q.y));
    const lo = Math.floor(Math.log10(Math.min(...all)) + 1e-9);
    let hi = Math.ceil(Math.log10(Math.max(...all)) - 1e-9);
    if (hi <= lo) hi = lo + 1;
    const Y = (v) => top + g.PH * (1 - (Math.log10(v) - lo) / (hi - lo));
    let grid = '';
    for (let e = lo; e <= hi; e++) grid += `<line class="gl" x1="${g.L}" y1="${Y(10 ** e).toFixed(1)}" x2="${g.W - g.R}" y2="${Y(10 ** e).toFixed(1)}"/><text class="tk" x="${g.L - 6}" y="${(Y(10 ** e) + 3.5).toFixed(1)}" text-anchor="end">${esc(tickText(c, e))}</text>`;
    const xt = g.xs.map((x) => `<text class="tk" x="${g.X(x).toFixed(1)}" y="${top + g.PH + 15}" text-anchor="middle">${n0(x)}</text>`).join('');
    const lines = c.series.map((s) => `<polyline class="ln ${s.role}" points="${s.points.map((q) => `${g.X(q.x).toFixed(1)},${Y(q.y).toFixed(1)}`).join(' ')}"/>${s.points.map((q, j) => `<circle class="pt ${s.role}${j === i ? ' on' : ''}" cx="${g.X(q.x).toFixed(1)}" cy="${Y(q.y).toFixed(1)}" r="${j === i ? 5 : 3.2}"/>`).join('')}`).join('');
    const guide = i >= 0 ? `<line class="guide" x1="${g.X(g.xs[i]).toFixed(1)}" y1="${top}" x2="${g.X(g.xs[i]).toFixed(1)}" y2="${top + g.PH}"/>` : '';
    const key = c.series.map((s, j) => `<line class="sw ${s.role}" x1="${g.L}" y1="${y0 + 8 + 30 + j * 16}" x2="${g.L + 18}" y2="${y0 + 8 + 30 + j * 16}"/><text class="kt" x="${g.L + 25}" y="${y0 + 8 + 34 + j * 16}">${esc(s.label)}: ${esc(slopeText(s, c))}</text>`).join('');
    return `<g class="ch">${`<text class="yl" x="${g.L}" y="${y0 + 8 + 12}">${esc(c.yLabel)}, both axes logarithmic</text>`}${key}<line class="axis" x1="${g.L}" y1="${top}" x2="${g.L}" y2="${top + g.PH}"/>${grid}${guide}${lines}${xt}<text class="ax" x="${(g.L + g.W - g.R) / 2}" y="${top + g.PH + 32}" text-anchor="middle">${esc(p.xLabel)}</text></g>`;
  }).join('');
  return `<svg class="sc-svg" viewBox="0 0 ${g.W} ${g.H}" role="img" aria-labelledby="${id}-t ${id}-d"><title id="${id}-t">${esc(p.title)}: cost against size on logarithmic axes</title><desc id="${id}-d">${esc(readoutFor(d, pi, p.charts[0].series[0].points.length - 1))}</desc>${body}</svg>`;
}

// ---------------------------------------------------------------- server-rendered pieces
export function staticSvg(d) { return chartSvg(d, 0, { id: 'sc-' + d.id }); }

export function controlsHtml(id, m, d) {
  const tabs = d.panels.map((p, k) => `<button type="button" role="tab" id="${id}-tab-${p.id}" data-mode="${p.id}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(p.label)}</button>`).join('');
  return {
    tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="What grows" hidden>${tabs}</div>`,
    controls: `<div class="dia-controls dia-ui" hidden><label class="dia-field"><span data-ctl="lab">Size</span> <input type="range" min="0" max="${m.n[0] - 1}" step="1" value="${m.n[0] - 1}" data-ctl="pos" aria-describedby="${id}-read"></label></div>
<p class="dia-hint dia-ui" hidden>Drag the slider, or focus the chart and press the left and right arrows, to read each size. Lines are recorded runs; both axes are logarithmic.</p>
<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden></p>`,
  };
}

export function tablesHtml(d) {
  const tbl = (p, c) => {
    const xs = c.series[0].points;
    const head = [p.xLabel, ...c.series.map((s) => `${s.label}${c.yKind === 'time' ? ' (microseconds)' : ''}`)];
    const rows = xs.map((q, j) => `<tr><th scope="row">${n0(q.x)}</th>${c.series.map((s) => `<td class="num">${n0(s.points[j].y)}</td>`).join('')}</tr>`).join('');
    const slopes = `<tr><th scope="row">Fitted slope</th>${c.series.map((s) => `<td class="num">${s.slope.toFixed(2)}${c.yKind === 'time' && s.nearlyFlat ? ' (under 0.4, read as nearly flat)' : ''}</td>`).join('')}</tr>`;
    return `<div class="tbl-wrap"><table class="tbl"><caption>${esc(p.title)}: ${esc(c.yLabel)}</caption><thead><tr>${head.map((h, j) => `<th scope="col"${j ? ' class="num"' : ''}>${esc(h)}</th>`).join('')}</tr></thead><tbody>${rows}${slopes}</tbody></table></div>`;
  };
  return `<details class="dia-data"><summary>Data behind the diagram</summary>${d.panels.map((p) => p.charts.map((c) => tbl(p, c)).join('')).join('')}</details>`;
}

export function verify(d, body) {
  const out = [], m = metrics(d);
  const trs = (body.match(/<tbody>[\s\S]*?<\/tbody>/g) || []).length;
  if (trs !== m.charts) out.push(`${trs} data tables for ${m.charts} charts`);
  if (!/<svg class="sc-svg" [^>]*role="img"/.test(body)) out.push('chart has no accessible name');
  for (const p of d.panels) for (const c of p.charts) {
    const xs = c.series[0].points.map((q) => q.x).join();
    for (const s of c.series) {
      if (s.points.map((q) => q.x).join() !== xs) out.push(`${c.id}: series ${s.id} is on different sizes`);
      if (s.points.some((q) => !(q.y > 0))) out.push(`${c.id}: ${s.id} has a value that cannot be drawn on a log axis`);
      if (Math.abs(fit(s.points) - s.slope) > SLOPE_TOL) out.push(`${c.id}: ${s.id} slope ${s.slope} is not the fit of its points`);
      if (c.yKind === 'time' && s.nearlyFlat !== (s.slope < 0.4)) out.push(`${c.id}: ${s.id} is marked wrongly against the 0.4 floor`);
    }
  }
  // the words around this chart say the engine rises and the plane stays flat: hold the data to that
  const wr = d.panels.find((p) => p.id === 'write-read');
  if (wr) {
    const [eng, pl] = wr.charts[0].series, ys = pl.points.map((q) => q.y);
    if (!(eng.points[eng.points.length - 1].y > 2 * eng.points[0].y)) out.push('write-read: the engine no longer rises');
    if (!(Math.max(...ys) / Math.min(...ys) < 1.3)) out.push('write-read: the plane no longer stays flat');
  }
  if (!(d.null.slope < 0 && d.null.firstPointIsStartUp)) out.push('the null no longer shows a start-up first point, so the caveat in the text is wrong');
  return out;
}

export const interactiveStage = true;

// ---------------------------------------------------------------- interactive chart (browser only)
export function mount(root, d) {
  const stage = root.querySelector('.dia-stage'), reduced = prefersReducedMotion();
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const q = (s) => root.querySelector(s), slider = q('[data-ctl=pos]');
  const S = { pi: 0, i: d.panels[0].charts[0].series[0].points.length - 1 };
  let narrow = stage.getBoundingClientRect().width < 520;
  const last = () => d.panels[S.pi].charts[0].series[0].points.length - 1;
  function render() {
    stage.innerHTML = chartSvg(d, S.pi, { narrow, i: S.i, id: 'sc-' + d.id });
    if (!reduced) stage.classList.add('sc-in');
    q('[data-ctl=readout]').textContent = readoutFor(d, S.pi, S.i);
    slider.max = String(last()); slider.value = String(S.i);
    q('[data-ctl=lab]').textContent = d.panels[S.pi].xLabel.replace(/^./, (c) => c.toUpperCase());
  }
  tablist(q('[role="tablist"]'), (mode) => { S.pi = d.panels.findIndex((p) => p.id === mode); S.i = last(); render(); });
  slider.addEventListener('input', () => { S.i = Number(slider.value); render(); });
  stage.addEventListener('pointermove', (e) => {
    const svg = stage.querySelector('svg'); if (!svg) return;
    const r = svg.getBoundingClientRect(), g = geom(d, S.pi, narrow), vx = ((e.clientX - r.left) / (r.width || 1)) * g.W;
    let best = 0; g.xs.forEach((x, j) => { if (Math.abs(g.X(x) - vx) < Math.abs(g.X(g.xs[best]) - vx)) best = j; });
    if (best !== S.i) { S.i = best; render(); }
  });
  stage.addEventListener('keydown', (e) => {
    let used = true;
    if (e.key === 'ArrowRight' || e.key === 'ArrowUp') S.i = Math.min(last(), S.i + 1);
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') S.i = Math.max(0, S.i - 1);
    else if (e.key === 'Home') S.i = 0;
    else if (e.key === 'End') S.i = last();
    else used = false;
    if (used) { e.preventDefault(); render(); }
  });
  new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; render(); } }).observe(stage);
  render();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="scaling-curve"]').forEach((root) => {
    const blob = root.querySelector('script[type="application/json"]');
    if (blob) mount(root, JSON.parse(blob.textContent));
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
