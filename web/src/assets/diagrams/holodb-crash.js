// Explainer diagram: crash and come back. A timeline of one COMMIT with a cut point the visitor moves; the table that opens afterwards is either the
// table before or the table after, never a mix. The tally is the recorded outcome of 46 recoveries (data/diagrams/holodb-crash.json, from the HoloDb
// customer-lifecycle record). The example rows are invented for the picture and the timeline is schematic; this is a replay of recorded outcomes,
// not a live crash, and the caption says so. Opens on its final frame, animates only on Play. Module contract: metrics / staticSvg / controlsHtml / tablesHtml / verify / mount.
import { hasDom } from './core.js';
import { esc, fmt, clamp, pen, svgOpen, playControls, player, tablesWrap, tbl } from './explain-kit.js';

export function metrics(d) {
  return { ...d.recoveries, customers: d.run.customers, sessions: d.run.sessions, millions: d.run.rowsComparedMillions, durableAt: d.durableAt };
}

export function limitFacts(m) {
  return { recoveries: String(m.total), recoveredBefore: String(m.before), recoveredAfter: String(m.after), recoveredBetween: String(m.between), customers: String(m.customers), sessions: String(m.sessions), rowsComparedMillions: String(m.millions) };
}

export const outcomeAt = (m, cut) => (cut >= m.durableAt ? 'after' : 'before');

export function readoutFor(d, m, cut) {
  const o = outcomeAt(m, cut), torn = cut >= d.segments[1].from && cut < m.durableAt;
  const head = o === 'after' ? 'The power goes after the commit is durable. The table that opens is the table after: both changes are there.'
    : `The power goes before the commit is durable. The table that opens is the table before: neither change is there.${torn ? ' ' + d.tornTail : ''}`;
  return `${head} Recorded across ${m.customers} customers and ${m.sessions} sessions: ${m.total} recoveries, ${m.before} to the table before, ${m.after} to the table after, ${m.between} in between. A replay of recorded outcomes, not a live crash; the timeline is illustrative.`;
}

function panel(p, x, y, w, title, rows, cols, cls, note) {
  p.t(x, y + 11, title, 'hk-lane' + (cls.includes('on') ? '' : ' dim'));
  const rh = 17, top = y + 18;
  p.r(x, top, w, rh * 5 + 6, 'hk-panel ' + cls, 5);
  cols.forEach((c, k) => p.t(x + 8 + k * (w / 2), top + 13, c, 'hk-col'));
  rows.forEach((r, i) => r.forEach((v, k) => p.t(x + 8 + k * (w / 2), top + 13 + rh * (i + 1), String(v), 'hk-cell ' + cls)));
  if (note) p.t(x + w / 2, top + rh * 5 + 18, note, 'hk-note ' + cls, 'middle');
}

export function chartSvg(d, m, t, narrow, uid) {
  const p = pen(), W = narrow ? 360 : 700, pad = 10, tw = W - pad * 2, cut = clamp(t), o = outcomeAt(m, cut), X = (u) => pad + u * tw;
  const stm = d.example.statement + ' (example rows)', cut2 = narrow ? stm.indexOf(' and ') : -1;
  if (cut2 > 0) { p.t(pad, 16, stm.slice(0, cut2), 'hk-lane'); p.t(pad, 30, stm.slice(cut2 + 1), 'hk-lane'); } else p.t(pad, 16, stm, 'hk-lane');
  const ty = cut2 > 0 ? 56 : 42;
  d.segments.forEach((s, i) => {
    p.r(X(s.from), ty, (s.to - s.from) * tw - 2, 22, 'hk-seg s' + i + (cut > s.from ? ' done' : ''), 3);
    if (!narrow) p.t(X(s.from) + 6, ty + 15, s.label.length > 30 && s.to - s.from < 0.3 ? s.label.slice(0, 18) + '...' : s.label, 'hk-seglab');
  });
  if (narrow) { d.segments.forEach((s, i) => p.t(X(s.from) + 3, ty + 35 + (i % 2) * 12, ['runs', 'log', 'durable'][i], 'hk-note')); }
  p.l(X(m.durableAt), ty - 6, X(m.durableAt), ty + 28, 'hk-mark');
  p.t(X(m.durableAt), ty + (narrow ? 58 : 44), d.marker, 'hk-note', narrow ? 'middle' : 'middle');
  p.l(X(cut), ty - 10, X(cut), ty + 30, 'hk-cut'); p.c(X(cut), ty - 12, 5, 'hk-cutdot');
  p.t(clamp((X(cut) - pad) / tw) > 0.8 ? X(cut) - 8 : X(cut) + 8, ty - 14, 'power cut', 'hk-cuttxt', clamp((X(cut) - pad) / tw) > 0.8 ? 'end' : 'start');
  const py = ty + (narrow ? 84 : 74), gap = 10, pw = (tw - gap * 2) / 3, e = d.example;
  p.t(pad, py - 6, 'The table that opens after the cut', 'hk-lane');
  panel(p, X(0), py, pw, 'Table before', e.before, e.cols, o === 'before' ? 'on' : 'off', o === 'before' ? 'opens' : '');
  panel(p, X(0) + pw + gap, py, pw, 'Table after', e.after, e.cols, o === 'after' ? 'on' : 'off', o === 'after' ? 'opens' : '');
  panel(p, X(0) + 2 * (pw + gap), py, pw, 'Half applied', e.half, e.cols, 'never', `never seen: ${m.between} of ${m.total}`);
  const by = py + 18 + 17 * 5 + 44, bw = tw;
  p.t(pad, by - 6, `${m.total} recoveries after a hard kill or a cut, recorded`, 'hk-lane');
  const w1 = bw * m.before / m.total, w2 = bw * m.after / m.total;
  p.r(pad, by, w1 - 1, 20, 'hk-tally before' + (o === 'before' ? ' on' : ''), 3);
  p.r(pad + w1, by, w2, 20, 'hk-tally after' + (o === 'after' ? ' on' : ''), 3);
  p.t(pad + w1 / 2, by + 14, `${m.before} to the table before`, 'hk-tlab', 'middle');
  p.t(pad + w1 + w2 / 2, by + 14, narrow ? String(m.after) : `${m.after} after`, 'hk-tlab', 'middle');
  p.t(pad + bw, by + 38, `${m.between} in between`, 'hk-note ok', 'end');
  p.t(pad, by + 38, narrow ? `${m.customers} customers, ${m.sessions} sessions` : `${m.customers} customers, ${m.sessions} sessions, ${m.millions} million rows compared with SQLite`, 'hk-note');
  if (narrow) p.t(pad, by + 52, `${m.millions} million rows compared with SQLite`, 'hk-note');
  return svgOpen('hk-svg', W, by + (narrow ? 64 : 50), uid, `Crash and come back: ${d.rule} Replay of recorded outcomes, illustrative timeline.`) + p.g.join('') + '</svg>';
}

export const staticSvg = (d) => chartSvg(d, metrics(d), 1, false, 'hk-' + d.id);

export function controlsHtml(id, m, d) {
  return {
    tabs: '',
    controls: `<div class="hw-copy"><p class="hw-promise">Cut the power in the middle of a commit. The table that opens is the table before or the table after, with every row and id whole.</p><p class="hw-how">${esc(d.rule)}</p></div>
${playControls('Where the power is cut', 'Cut the power at')}
<p class="dia-readout hw-caption" data-ctl="readout" role="status">${esc(readoutFor(d, m, 1))}</p>`,
  };
}

export function tablesHtml(d, m) {
  const a = tbl('Recorded recoveries after a hard kill or a power cut', ['Outcome', 'Recoveries'], [['Equal to the table before the write', fmt(m.before)], ['Equal to the table after the write', fmt(m.after)], ['In between', fmt(m.between)], ['All recoveries', fmt(m.total)]]);
  const b = tbl('The run behind the tally', ['Measure', 'Recorded'], [['Customers', fmt(m.customers)], ['Sessions', fmt(m.sessions)], ['Rows compared with the SQLite oracle', fmt(m.millions) + ' million']]);
  const c = tbl('Where the cut falls', ['Cut point', 'Table that opens'], d.segments.map((s) => [s.label, outcomeAt(m, s.from) === outcomeAt(m, s.to - 0.001) ? (outcomeAt(m, s.from) === 'after' ? 'Table after' : 'Table before') : 'Table before, then after at the marker']));
  return tablesWrap('Data behind the diagram', a + b + c);
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  if (m.before + m.after + m.between !== m.total) out.push('the three outcomes do not add up to the recoveries');
  if (m.between !== 0) out.push('a recovery landed between');
  const sj = (x) => JSON.stringify(x); if (sj(d.example.half) === sj(d.example.after) || sj(d.example.half) === sj(d.example.before)) out.push('the half-applied example must differ from both tables');
  if ((body.match(/<tbody>/g) || []).length !== 3) out.push('three tables expected');
  return out;
}

export const interactiveStage = false;

export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage'), read = root.querySelector('[data-ctl=readout]');
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  let narrow = stage.getBoundingClientRect().width < 520;
  const pl = player(root, (t) => { stage.innerHTML = chartSvg(d, m, t, narrow, 'hk-' + d.id); read.textContent = readoutFor(d, m, clamp(t)); }, 8000);
  if (typeof ResizeObserver === 'function') new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; pl.paint(); } }).observe(stage);
  pl.paint();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="holodb-crash"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
