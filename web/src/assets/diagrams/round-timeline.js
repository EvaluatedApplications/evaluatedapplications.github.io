// Research diagram: "What stopped moving, and when". Rows are the measures of one training run; the bar is solid while the measure
// was still changing and dashed once it had stopped. Data: the article's own `diagram` block (rounds restated from its prose).
import { hasDom } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');

// where a row changes: [startOfChange, stopOfChange] in rounds; stop = end means it never stopped
const span = (it, end) => ({ from: it.from ?? 0, stop: it.state === 'flat' ? 0 : it.state === 'moving' ? end : it.stop });

export function statusAt(it, R, end) {
  const s = span(it, end);
  if (it.state === 'flat') return 'flat';
  if (R < s.from) return 'not yet';
  if (it.capFrom != null && R >= it.capFrom && R < s.stop) return 'capping';
  return R < s.stop ? 'changing' : it.state === 'moving' ? 'changing' : 'flat';
}

export function metrics(d) {
  const flatAt = (R) => d.items.filter((it) => statusAt(it, R, d.end) === 'flat');
  return { items: d.items, end: d.end, battery: d.battery, flatAt, stopped: d.items.filter((i) => i.state === 'stopped'), moving: d.items.filter((i) => i.state === 'moving') };
}

export function readoutFor(d, m, R) {
  const flat = m.flatAt(R).map((i) => i.label), ch = d.items.filter((i) => ['changing', 'capping'].includes(statusAt(i, R, d.end))).map((i) => i.label);
  return `At round ${n0(R)}: ${flat.length ? `flat or capped: ${flat.join('; ')}` : 'nothing has stopped yet'}. ${ch.length ? `Still changing: ${ch.join('; ')}.` : ''}`;
}

export function chartSvg(d, m, R, narrow) {
  const rowH = 44, L = narrow ? 8 : 262, T = 26, W = narrow ? 400 : 760, rows = d.items.length, labelAbove = narrow, pw = W - L - 40;
  const H = T + rows * (labelAbove ? rowH + 18 : rowH) + 34;
  const X = (v) => L + (v / d.end) * pw;
  const step = d.end / 4, ticks = [0, 1, 2, 3, 4].map((k) => k * step);
  const grid = ticks.map((t) => `<line class="gl" x1="${X(t).toFixed(1)}" y1="${T - 6}" x2="${X(t).toFixed(1)}" y2="${H - 28}"/><text class="tk" x="${X(t).toFixed(1)}" y="${H - 14}" text-anchor="middle">${n0(t)}</text>`).join('');
  const bars = d.items.map((it, k) => {
    const y = T + k * (labelAbove ? rowH + 18 : rowH), by = y + (labelAbove ? 20 : 12), s = span(it, d.end), st = statusAt(it, R, d.end);
    const solidFrom = X(s.from), solidTo = it.state === 'flat' ? solidFrom : X(s.stop), dashTo = X(d.end);
    const label = `<text class="rl" x="${labelAbove ? L : L - 10}" y="${labelAbove ? y + 10 : by + 14}" text-anchor="${labelAbove ? 'start' : 'end'}">${esc(it.label)}</text>`;
    const solid = it.state === 'flat' ? '' : `<rect class="bar ${it.state}" x="${solidFrom.toFixed(1)}" y="${by}" width="${Math.max(2, solidTo - solidFrom).toFixed(1)}" height="14" rx="3"/>`;
    const cap = it.capFrom != null ? `<rect class="bar cap" x="${X(it.capFrom).toFixed(1)}" y="${by}" width="${(X(it.stop) - X(it.capFrom)).toFixed(1)}" height="14" rx="3"/>` : '';
    const dash = it.state === 'moving' ? '' : `<line class="dash" x1="${solidTo.toFixed(1)}" y1="${by + 7}" x2="${dashTo.toFixed(1)}" y2="${by + 7}"/>`;
    const pill = `<text class="st ${st.replace(' ', '-')}" x="${(X(Math.min(R, d.end)) + 6).toFixed(1)}" y="${by - 3}">${st}</text>`;
    return `<g>${label}${solid}${cap}${dash}${pill}</g>`;
  }).join('');
  const bat = `<text class="ax" x="${L}" y="${T - 12}">A battery of tests ran on a fresh checkpoint every ${n0(d.battery)} rounds</text>`;
  const now = `<line class="mk" x1="${X(R).toFixed(1)}" y1="${T - 6}" x2="${X(R).toFixed(1)}" y2="${H - 28}"/>`;
  return `<svg class="rt-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="rt-t rt-d" preserveAspectRatio="xMidYMid meet"><title id="rt-t">What stopped moving, and when</title><desc id="rt-d">${esc(readoutFor(d, m, d.end))} The table below lists each measure with its rounds.</desc>${grid}${bat}${bars}${now}<text class="ax" x="${L + pw / 2}" y="${H - 1}" text-anchor="middle">training rounds</text></svg>`;
}

export function staticSvg(d) { const m = metrics(d); return chartSvg(d, m, d.end, false); }

export function controlsHtml(id, m) {
  return {
    tabs: '',
    controls: `<div class="dia-controls dia-ui" hidden><label class="dia-field">Round <input type="range" min="0" max="${m.end}" step="1000" value="${m.end}" data-ctl="round" aria-describedby="${id}-read"></label><output class="dia-field" data-ctl="rv">${n0(m.end)}</output></div>
<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden>${esc(readoutFor({ items: m.items, end: m.end }, m, m.end))}</p>`,
  };
}

export function tablesHtml(d, m) {
  const rows = d.items.map((it) => { const s = span(it, d.end); return `<tr><th scope="row">${esc(it.label)}</th><td>${it.state === 'flat' ? 'Flat throughout' : it.state === 'moving' ? `Still changing at round ${n0(d.end)}` : `Stopped at about round ${n0(s.stop)}`}</td><td>${esc(it.note)}</td></tr>`; }).join('');
  return `<details class="dia-data"><summary>Data behind the diagram</summary><div class="tbl-wrap"><table class="tbl"><caption>Each measure and the round at which it stopped changing, restated from the article</caption><thead><tr><th scope="col">Measure</th><th scope="col">When</th><th scope="col">Note</th></tr></thead><tbody>${rows}</tbody></table></div></details>`;
}

export function verify(d, body) {
  const out = [];
  if (!d.items.length) out.push('no rows');
  for (const it of d.items) if (it.state === 'stopped' && !(it.stop > 0 && it.stop <= d.end)) out.push(`${it.label}: stop outside the run`);
  if (!/Stopped at about round/.test(body)) out.push('table missing');
  return out;
}

export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage');
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const q = (s) => root.querySelector(s);
  let R = d.end;
  const render = () => {
    stage.innerHTML = chartSvg(d, m, R, stage.getBoundingClientRect().width < 520);
    q('[data-ctl=readout]').textContent = readoutFor(d, m, R); q('[data-ctl=rv]').textContent = n0(R);
  };
  q('[data-ctl=round]').addEventListener('input', (e) => { R = Number(e.target.value); render(); });
  let narrow = stage.getBoundingClientRect().width < 520;
  new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; render(); } }).observe(stage);
  render();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="round-timeline"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}

export const interactiveStage = false;
