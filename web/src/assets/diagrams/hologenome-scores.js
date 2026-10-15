// Diagram: "How HoloGenome scores". Four views of the graded runs: the linear readout against its own controls, the fitted and zero-parameter
// models against the best trained network (the losses), the helical period a small phasor model settles on, and the measured twist along one window.
// Data: data/diagrams/hologenome-scores.json, the only place these scores are typed (the helix block is written by web/generators/hologenome-helix
// against the published package). Plain bars and dots in SVG, no libraries.
import { hasDom, tablist } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const f2 = (v) => Number(v).toFixed(2);
const f3 = (v) => Number(v).toFixed(3);
const n0 = (n) => Number(n).toLocaleString('en-GB');
const list = (a) => (a.length < 2 ? a.join('') : a.slice(0, -1).join(', ') + ' and ' + a[a.length - 1]);
const view = (m, id) => m.views.find((v) => v.id === id);

export function metrics(d) {
  const best = d.views.find((v) => v.id === 'best'), period = d.views.find((v) => v.id === 'period');
  const gaps = best.groups.map((g) => g.values.dnacycp - g.values.fitted);
  return {
    views: d.views, modes: d.views.map((v) => v.id), method: d.method, helix: d.helix,
    gaps, ref: d.helix.basesPerTurn, bestGroups: best.groups, period, helixScore: d.helixScore, training: d.training,
  };
}

// Numbers quoted in the page's prose, as strings read from the recordings ({placeholders} in the product data).
export function limitFacts(m) {
  const ctl = view(m, 'controls'), row = (id) => ctl.rows.find((r) => r.id === id), dl = (id) => ctl.deltas.find((r) => r.id === id);
  const P = m.period, hs = m.helixScore.libraries, T = m.training;
  const near = (v) => Math.abs(v - m.ref) < 0.2;
  const off = hs.flatMap((l) => [l.seven, l.fifteen]), on = hs.map((l) => l.helix);
  const nulls = P.libraries.flatMap((l) => [l.order, l.labels]);
  const trained = T.readouts.flatMap((r) => [r.random, r.chrv, r.nuc]);
  const g = m.bestGroups;
  return {
    rShape: f3(row('shape').r), rTwist: f3(row('twist').r), rConstant: f3(row('constant').r), rShuffled: f3(row('shuffled').r), rWords: f3(row('words').r),
    dTwist: f3(dl('twist').d), dTwistLo: f3(dl('twist').lo), dTwistHi: f3(dl('twist').hi),
    dShape: f3(dl('shape').d), dShapeLo: f3(dl('shape').lo), dShapeHi: f3(dl('shape').hi),
    trainSet: m.method.trainSet, testSet: m.method.testSet, overlap: m.method.overlap, measurement: m.method.assay,
    ceilingLo: f2(m.method.ceiling.lo), ceilingHi: f2(m.method.ceiling.hi),
    zeroLo: f2(Math.min(...on)), zeroHi: f2(Math.max(...on)), offNear: f2(Math.max(...off)), offFar: f2(Math.min(...off)),
    periods: list(P.libraries.map((l) => f2(l.real))), periodLo: f2(Math.min(...P.libraries.map((l) => l.real))), periodHi: f2(Math.max(...P.libraries.map((l) => l.real))),
    periodRef: String(m.ref), periodRound: f2(m.ref), startPeriod: String(P.start), nullPeriod: Number(P.start).toFixed(1), libraryCount: String(P.libraries.length),
    basinFound: list(P.basin.filter((b) => near(b.end)).map((b) => String(b.start))).replace(' and ', ' or '),
    basinMissed: list(P.basin.filter((b) => !near(b.end)).map((b) => String(b.start))).replace(' and ', ' or '),
    dnacycp: list(g.map((x) => f2(x.values.dnacycp))), fitted: list(g.map((x) => f2(x.values.fitted))),
    gapLo: f2(Math.min(...m.gaps)), gapHi: f2(Math.max(...m.gaps)),
    dnacycpSize: m.method.dnacycp.size, dnacycpDesc: `an approximately ${m.method.dnacycp.size}-parameter trained neural network`, fittedSize: String(m.method.fitted.size),
    trainLo: f2(Math.min(...trained)), trainHi: f2(Math.max(...trained)),
    stepFactor: String(T.phasor.stepsTo / T.phasor.stepsFrom), stepChange: String(T.phasor.maxChange),
    turns: f2(m.helix.measuredTurns[m.helix.measuredTurns.length - 1]), constantTurns: f2(m.helix.constantTurns[m.helix.constantTurns.length - 1]),
    windowBases: String(m.helix.sequence.length), basesPerTurn: Number(m.helix.basesPerTurn).toFixed(4), rcCosine: f2(m.helix.reverseComplementCosine), dim: String(m.helix.dim),
    constantDegrees: String(m.helix.constantDegrees),
  };
}

export function readoutFor(m, mode) {
  const f = limitFacts(m);
  if (mode === 'controls') return `Trained on ${f.trainSet} and tested on ${f.testSet} it never saw, the vectors with the measured twist score r = ${f.rTwist}, against ${f.rConstant} with one constant twist. Adding the local-shape layer lifts it to ${f.rShape}. Counts of short words score ${f.rWords}, and shuffled sequence ${f.rShuffled}.`;
  if (mode === 'best') return `The best trained network, DNAcycP, scores ${f.dnacycp} on three libraries. The ${f.fittedSize}-parameter fit scores ${f.fitted}, trailing by ${f.gapLo} to ${f.gapHi}. Nothing fitted scores less again.`;
  if (mode === 'period') return `Started at ${f.startPeriod} bases and never told the helical period, a small phasor model settled at ${f.periods} bases per turn on ${f.libraryCount} libraries. The crystallographic value is ${f.periodRef}. With the order or the labels scrambled it stayed near ${f.nullPeriod}.`;
  return `Along this ${f.windowBases}-base window the measured twist makes ${f.turns} turns, against ${f.constantTurns} for a constant ${f.constantDegrees} degree twist. The chart shows how far apart the two phases drift, base by base.`;
}

// ---- geometry ----
const geo = (narrow) => (narrow ? { W: 400, L: 124, R: 64 } : { W: 700, L: 170, R: 90 });
const axisTicks = (top, step, X, y0, y1, fmt) => { const out = []; for (let t = 0; t <= top + 1e-9; t += step) out.push(`<line class="gl" x1="${X(t).toFixed(1)}" y1="${y0}" x2="${X(t).toFixed(1)}" y2="${y1}"/><text class="tk" x="${X(t).toFixed(1)}" y="${y1 + 12}" text-anchor="middle">${fmt(t)}</text>`); return out.join(''); };
const wrapSvg = (cls, title, desc, W, H, body) => `<svg class="hg-svg ${cls}" viewBox="0 0 ${W} ${Math.round(H)}" role="img" aria-labelledby="hg-t hg-d" preserveAspectRatio="xMidYMid meet"><title id="hg-t">${esc(title)}</title><desc id="hg-d">${esc(desc)}</desc>${body}</svg>`;

function barsSvg(m, v, narrow) {
  const { W, L, R } = geo(narrow), T = 8, rh = 34, pw = W - L - R, top = 0.8, X = (r) => (r / top) * pw;
  let y = T; const rows = [];
  for (const r of v.rows) {
    const cy = y + rh / 2;
    rows.push(`<g class="row ${r.role}"><text class="rl" x="${L - 8}" y="${cy + 4}" text-anchor="end">${esc(narrow ? r.short : r.label.length > 26 ? r.short : r.label)}</text><rect class="bar ${r.role}" x="${L}" y="${y + 6}" width="${X(r.r).toFixed(1)}" height="${rh - 14}" rx="3"/><line class="ci" x1="${(L + X(r.lo)).toFixed(1)}" y1="${cy}" x2="${(L + X(r.hi)).toFixed(1)}" y2="${cy}"/><line class="ci" x1="${(L + X(r.lo)).toFixed(1)}" y1="${cy - 4}" x2="${(L + X(r.lo)).toFixed(1)}" y2="${cy + 4}"/><line class="ci" x1="${(L + X(r.hi)).toFixed(1)}" y1="${cy - 4}" x2="${(L + X(r.hi)).toFixed(1)}" y2="${cy + 4}"/><text class="val" x="${(L + X(r.hi) + 8).toFixed(1)}" y="${cy + 4}">${f3(r.r)}</text></g>`);
    y += rh;
  }
  const H = y + 44;
  const grid = axisTicks(top, 0.2, (t) => L + X(t), T, H - 32, f2);
  return wrapSvg('hg-bars', v.label, readoutFor(m, 'controls'), W, H, `${grid}${rows.join('')}<text class="ax" x="${L + pw / 2}" y="${H - 4}" text-anchor="middle">${narrow ? 'Correlation r; the line is the 95% interval' : 'Correlation r with the held-out measurement; the line is the 95% interval'}</text>`);
}

function groupsSvg(m, v, narrow) {
  const { W, L, R } = geo(narrow), T = 8, rh = 26, pw = W - L - R, top = 1, X = (r) => (r / top) * pw;
  const name = (s) => (s.id === 'fitted' ? `${m.method.fitted.size}-parameter fit` : s.short);
  let y = T; const rows = [];
  for (const g of v.groups) {
    rows.push(`<text class="gh" x="${narrow ? 4 : L - 8}" y="${y + 11}" text-anchor="${narrow ? 'start' : 'end'}">${esc(g.label)}</text>`); y += 18;
    for (const s of v.series) {
      const val = g.values[s.id];
      rows.push(`<g class="row ${s.id}"><text class="rl" x="${L - 8}" y="${y + rh / 2 + 3}" text-anchor="end">${esc(name(s))}</text><rect class="bar s-${s.id}" x="${L}" y="${y + 4}" width="${X(val).toFixed(1)}" height="${rh - 8}" rx="3"/><text class="val" x="${(L + X(val) + 8).toFixed(1)}" y="${y + rh / 2 + 3}">${f2(val)}</text></g>`);
      y += rh;
    }
    y += 8;
  }
  const H = y + 40;
  const grid = axisTicks(top, 0.2, (t) => L + X(t), T, H - 30, f2);
  return wrapSvg('hg-groups', v.label, readoutFor(m, 'best'), W, H, `${grid}${rows.join('')}<text class="ax" x="${L + pw / 2}" y="${H - 4}" text-anchor="middle">${narrow ? 'Correlation r with the measurement' : 'Correlation r with the held-out measurement; 1.0 would be a perfect match'}</text>`);
}

function periodSvg(m, v, narrow) {
  const { W, L } = geo(narrow), R = narrow ? 14 : 112, T = 30, rh = 34, pw = W - L - R, a = v.axis, X = (p) => L + ((p - a.lo) / (a.hi - a.lo)) * pw;
  let y = T; const rows = [];
  for (const l of v.libraries) {
    const cy = y + rh / 2, xr = X(l.real);
    rows.push(`<g class="row"><text class="rl" x="${L - 8}" y="${cy + 4}" text-anchor="end">${esc(l.label)}</text><circle class="dot real" cx="${xr.toFixed(1)}" cy="${cy}" r="6"/><text class="val" x="${(xr - 11).toFixed(1)}" y="${cy + 4}" text-anchor="end">${f2(l.real)}</text><circle class="dot order" cx="${X(l.order).toFixed(1)}" cy="${cy}" r="5.5"/><rect class="dot labels" x="${(X(l.labels) - 4.5).toFixed(1)}" y="${cy - 4.5}" width="9" height="9" transform="rotate(45 ${X(l.labels).toFixed(1)} ${cy})"/>${narrow ? '' : `<text class="val faint" x="${W - R + 12}" y="${cy + 4}">${f2(l.order)} ${f2(l.labels)}</text>`}</g>`);
    y += rh;
  }
  const H = y + 62, ticks = [];
  for (let t = a.lo; t <= a.hi + 1e-9; t += a.step) ticks.push(`<line class="gl" x1="${X(t).toFixed(1)}" y1="${T - 4}" x2="${X(t).toFixed(1)}" y2="${y}"/><text class="tk" x="${X(t).toFixed(1)}" y="${y + 13}" text-anchor="middle">${Number(t.toFixed(2))}</text>`);
  const ref = `<line class="refline" x1="${X(m.ref).toFixed(1)}" y1="${T - 12}" x2="${X(m.ref).toFixed(1)}" y2="${y}"/><text class="refl" x="${X(m.ref).toFixed(1)}" y="${T - 16}" text-anchor="middle">${m.ref} published</text>`;
  const st = `<line class="refline start" x1="${X(v.start).toFixed(1)}" y1="${T - 12}" x2="${X(v.start).toFixed(1)}" y2="${y}"/><text class="refl" x="${X(v.start).toFixed(1)}" y="${T - 16}" text-anchor="middle">start ${v.start}</text>`;
  const lg = `<g class="lgd"><circle class="dot real" cx="${L + 6}" cy="${H - 22}" r="5"/><text class="lt" x="${L + 16}" y="${H - 18}">Found from real data</text><circle class="dot order" cx="${L + 150}" cy="${H - 22}" r="5"/><text class="lt" x="${L + 160}" y="${H - 18}">Order scrambled</text><rect class="dot labels" x="${L + 261}" y="${H - 26.5}" width="8" height="8" transform="rotate(45 ${L + 265} ${H - 22.5})"/><text class="lt" x="${L + 276}" y="${H - 18}">Labels scrambled</text></g>`;
  const lgNarrow = `<g class="lgd"><circle class="dot real" cx="${8}" cy="${H - 34}" r="5"/><text class="lt" x="${18}" y="${H - 30}">Found from real data</text><circle class="dot order" cx="${150}" cy="${H - 34}" r="5"/><text class="lt" x="${160}" y="${H - 30}">Order scrambled</text><rect class="dot labels" x="${8 - 4}" y="${H - 21 - 4}" width="8" height="8" transform="rotate(45 8 ${H - 21})"/><text class="lt" x="${18}" y="${H - 17}">Labels scrambled</text></g>`;
  return wrapSvg('hg-period', v.label, readoutFor(m, 'period'), W, H + (narrow ? 14 : 0), `${ticks.join('')}${ref}${st}${rows.join('')}<text class="ax" x="${L + pw / 2}" y="${y + 28}" text-anchor="middle">Bases per turn</text>${narrow ? lgNarrow : lg}`);
}

function helixSvg(m, v, narrow) {
  const W = narrow ? 400 : 700, L = narrow ? 44 : 56, R = 14, T = 14, ph = narrow ? 150 : 170, pw = W - L - R;
  const a = m.helix.measuredTurns, b = m.helix.constantTurns, n = a.length;
  const dev = a.map((x, i) => (x - b[i]) * 360), mx = Math.max(2, Math.ceil(Math.max(...dev.map(Math.abs)) / 2) * 2);
  const X = (i) => L + (i / (n - 1)) * pw, Y = (d) => T + ph / 2 - (d / mx) * (ph / 2);
  const grid = [-mx, -mx / 2, 0, mx / 2, mx].map((t) => `<line class="gl${t === 0 ? ' zero' : ''}" x1="${L}" y1="${Y(t).toFixed(1)}" x2="${W - R}" y2="${Y(t).toFixed(1)}"/><text class="tk" x="${L - 6}" y="${(Y(t) + 3.5).toFixed(1)}" text-anchor="end">${t > 0 ? '+' : ''}${t}</text>`).join('');
  const xt = [0, 10, 20, 30, 40, n - 1].map((i) => `<text class="tk" x="${X(i).toFixed(1)}" y="${T + ph + 14}" text-anchor="middle">${i}</text>`).join('');
  const line = dev.map((d, i) => `${i ? 'L' : 'M'}${X(i).toFixed(1)} ${Y(d).toFixed(1)}`).join('');
  const dots = dev.map((d, i) => `<circle class="pt" cx="${X(i).toFixed(1)}" cy="${Y(d).toFixed(1)}" r="2.2"/>`).join('');
  const H = T + ph + 52;
  return wrapSvg('hg-helix', v.label, readoutFor(m, 'twist'), W, H, `${grid}<path class="curve" d="${line}"/>${dots}${xt}<text class="ax" x="${L + pw / 2}" y="${H - 6}" text-anchor="middle">Base along the ${n}-base window</text><text class="ax" x="${L}" y="${T - 3}" text-anchor="start">Degrees ahead (+) or behind (-) a constant ${m.helix.constantDegrees} degree twist</text>`);
}

export function chartSvg(d, m, mode, narrow = false) {
  const v = view(m, mode);
  if (v.kind === 'bars') return barsSvg(m, v, narrow);
  if (v.kind === 'groups') return groupsSvg(m, v, narrow);
  if (v.kind === 'period') return periodSvg(m, v, narrow);
  return helixSvg(m, v, narrow);
}

export function staticSvg(d) { return chartSvg(d, metrics(d), 'controls'); }

export function controlsHtml(id, m, d) {
  const tabs = d.views.map((x, k) => `<button type="button" role="tab" id="${id}-tab-${x.id}" data-mode="${x.id}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(x.tab)}</button>`).join('');
  return { tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="What to compare" hidden>${tabs}</div>`, controls: `<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden></p>` };
}

export function tablesHtml(d, m) {
  const row = (cells) => '<tr>' + cells.map((c, k) => (k === 0 ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
  const head = (cols) => `<thead><tr>${cols.map((c) => `<th scope="col">${esc(c)}</th>`).join('')}</tr></thead>`;
  const tbl = (cap, cols, rows) => `<div class="tbl-wrap"><table class="tbl"><caption>${esc(cap)}</caption>${head(cols)}<tbody>${rows.map(row).join('')}</tbody></table></div>`;
  const ctl = view(m, 'controls'), best = view(m, 'best'), per = view(m, 'period');
  const name = (s) => (s.id === 'fitted' ? `${m.method.fitted.size}-parameter fit` : s.label);
  const T = m.training, f = limitFacts(m);
  const parts = [
    tbl(`${ctl.label}. ${ctl.note}`, ['Vectors built with', 'Correlation r', '95% interval'], ctl.rows.map((r) => [r.label, f3(r.r), `${f3(r.lo)} to ${f3(r.hi)}`])),
    tbl('Differences between those readouts', ['Contrast', 'Difference in r', '95% interval'], ctl.deltas.map((r) => [r.label, f3(r.d), `${f3(r.lo)} to ${f3(r.hi)}`])),
    tbl(`${best.label}. ${best.note}`, ['Library', ...best.series.map(name)], best.groups.map((g) => [g.label, ...best.series.map((s) => f2(g.values[s.id]))])),
    tbl(`${per.label}. ${per.note}`, ['Library', 'Found from real data', 'Order scrambled', 'Labels scrambled'], per.libraries.map((l) => [l.label, f2(l.real), f2(l.order), f2(l.labels)])),
    tbl('Where the period search started, yeast tiling library', ['Started at (bases per turn)', 'Settled at (bases per turn)'], per.basin.map((b) => [String(b.start), f2(b.end)])),
    tbl(m.helixScore.label, ['Library', ...m.helixScore.periods.map((p) => p.label)], m.helixScore.libraries.map((l) => [l.label, ...m.helixScore.periods.map((p) => f3(l[p.id]))])),
    tbl(`${T.label}: held-out correlation after training the readouts with iterative optimisation`, ['Vectors built with', 'Random sequence', 'Chromosome V tiling', 'Nucleosome'], T.readouts.map((r) => [r.label, f3(r.random), f3(r.chrv), f3(r.nuc)])),
    tbl(`The ${f.windowBases}-base window, cumulative helix turns`, ['Base', 'Letter', 'Measured twist', `Constant ${f.constantDegrees} degrees`], m.helix.measuredTurns.map((t, i) => [String(i + 1), m.helix.sequence[i], t.toFixed(3), m.helix.constantTurns[i].toFixed(3)])),
  ];
  return `<details class="dia-data"><summary>Data behind the diagram</summary>${parts.join('')}</details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [], ctl = view(m, 'controls'), r = (id) => ctl.rows.find((x) => x.id === id).r;
  if (!(r('shape') > r('twist') && r('twist') > r('constant') && r('constant') > r('words'))) out.push('controls: the readouts are not in the order the page states');
  for (const g of m.bestGroups) if (!(g.values.dnacycp > g.values.fitted && g.values.fitted > g.values.zero)) out.push(`best: ${g.id} is not ordered DNAcycP, fitted, zero-parameter`);
  for (const l of m.period.libraries) {
    if (Math.abs(l.real - m.ref) > 0.2) out.push(`period: ${l.id} did not land within 0.2 of the published period`);
    if (Math.abs(l.order - m.period.start) > 0.2 || Math.abs(l.labels - m.period.start) > 0.2) out.push(`period: ${l.id} scrambled runs left the start`);
  }
  if (m.helix.measuredTurns.length !== m.helix.sequence.length || m.helix.constantTurns.length !== m.helix.sequence.length) out.push('helix: a phase per base is missing');
  if (m.helixScore.libraries.some((l) => !(l.helix > 0 && l.seven < 0 && l.fifteen < 0))) out.push('helix score: the off-period scores are not all below zero');
  if (!/<caption>/.test(body) || !/Differences between those readouts/.test(body)) out.push('a table is missing');
  return out;
}

export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage');
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const q = (s) => root.querySelector(s), S = { mode: 'controls' };
  let narrow = stage.getBoundingClientRect().width < 520;
  const render = () => { stage.innerHTML = chartSvg(d, m, S.mode, narrow); q('[data-ctl=readout]').textContent = readoutFor(m, S.mode); };
  tablist(q('[role="tablist"]'), (mode) => { S.mode = mode; render(); });
  if (typeof ResizeObserver === 'function') new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; render(); } }).observe(stage);
  render();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="hologenome-scores"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}

export const interactiveStage = false;
