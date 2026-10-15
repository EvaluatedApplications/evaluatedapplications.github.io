// Research diagram: "Which slot reads what was just said". A context window grows by doubling from `from` slots. Each slot has its own
// trained representation, and the last slot is the one that means "the token just before the prediction". Appending puts a fresh slot
// there on every grow; inserting before the oldest slot keeps the trained one there. Ages are counted in grows (structure, not rounds).
import { hasDom } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');

// slots as a list of cohorts: cohort c was created at grow c (0 = the original window)
export function layout(d, mode, g) {
  const cohorts = [{ c: 0, n: d.from }];
  for (let k = 1; k <= g; k++) cohorts.push({ c: k, n: d.from * 2 ** (k - 1) });
  const ordered = mode === 'append' ? cohorts : [...cohorts].reverse();
  const slots = []; for (const co of ordered) for (let i = 0; i < co.n; i++) slots.push(co.c);
  return slots;
}

export function metrics(d) {
  const info = (mode, g) => {
    const slots = layout(d, mode, g), last = slots[slots.length - 1], firstOriginal = slots.indexOf(0), lastOriginal = slots.lastIndexOf(0);
    return { n: slots.length, lastCohort: last, lastAge: g - last, originalAt: [firstOriginal + 1, lastOriginal + 1] };
  };
  return { info, from: d.from, grows: d.grows, sizeAt: (g) => d.from * 2 ** g };
}

export function readoutFor(d, m, g) {
  const a = m.info('append', g), i = m.info('insert', g);
  const age = (x) => (x === 0 ? 'has not been trained at all since the last grow' : `has been trained for ${x} grow${x > 1 ? 's' : ''}`);
  return `After ${g} grow${g === 1 ? '' : 's'} the window has ${n0(a.n)} slots. Appending: the slot that reads what was just said is new and ${age(a.lastAge)}, and the most trained slots sit at positions ${a.originalAt[0]} to ${a.originalAt[1]}. Inserting before the oldest: that slot is the original one and ${age(i.lastAge)}, and the most trained slots stay at the end.`;
}

function row(slots, W, y, h, g, label, ring) {
  const n = slots.length, w = (W - 16) / n;
  const cells = slots.map((c, k) => { const o = g === 0 ? 1 : 1 - 0.75 * (c / g); return `<rect class="sl" x="${(8 + k * w).toFixed(2)}" y="${y}" width="${Math.max(0.4, w - (n > 64 ? 0 : 1.2)).toFixed(2)}" height="${h}" fill-opacity="${o.toFixed(2)}"/>`; }).join('');
  const rx = 8 + (n - 1) * w;
  return `<g><text class="rl" x="8" y="${y - 8}">${esc(label)}</text>${cells}<rect class="ring" x="${(rx - 2).toFixed(2)}" y="${y - 3}" width="${Math.max(4, w + 4).toFixed(2)}" height="${h + 6}" rx="3"/><text class="tk" x="${W - 8}" y="${y + h + 16}" text-anchor="end">${ring}</text></g>`;
}

export function chartSvg(d, m, g) {
  const W = 640, H = 220;
  const ap = layout(d, 'append', g), ins = layout(d, 'insert', g);
  const ai = m.info('append', g), ii = m.info('insert', g);
  return `<svg class="sg-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="sg-t sg-d" preserveAspectRatio="xMidYMid meet"><title id="sg-t">Position slots after ${g} grows</title><desc id="sg-d">${esc(readoutFor(d, m, g))}</desc>${row(ap, W, 34, 34, g, `Append new slots after the last one (${n0(ai.n)} slots)`, `last slot: new, age ${ai.lastAge} grows`)}${row(ins, W, 128, 34, g, `Insert new slots before the oldest (${n0(ii.n)} slots)`, `last slot: original, age ${ii.lastAge} grows`)}<text class="ax" x="8" y="${H - 6}">Darker slots are older. The outlined slot is the one just before the prediction. Window grows by doubling from ${d.from} slots.</text></svg>`;
}

export function staticSvg(d) { return chartSvg(d, metrics(d), 1); }

export function controlsHtml(id, m) {
  return { tabs: '', controls: `<div class="dia-controls dia-ui" hidden><label class="dia-field">Grows <input type="range" min="0" max="${m.grows}" step="1" value="1" data-ctl="grows" aria-describedby="${id}-read"></label><output class="dia-field" data-ctl="gv">1</output></div>
<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden>${esc(readoutFor({ from: 0 }, { info: m.info }, 1))}</p>` };
}

export function tablesHtml(d, m) {
  const rows = Array.from({ length: d.grows + 1 }, (_, g) => { const a = m.info('append', g), i = m.info('insert', g); return `<tr><th scope="row">${g}</th><td>${n0(a.n)}</td><td>${a.lastAge}</td><td>${a.originalAt[0]} to ${a.originalAt[1]}</td><td>${i.lastAge}</td></tr>`; }).join('');
  return `<details class="dia-data"><summary>Data behind the diagram</summary><div class="tbl-wrap"><table class="tbl"><caption>The slot just before the prediction, by number of grows, when the window doubles from ${d.from} slots. Ages are in grows.</caption><thead><tr><th scope="col">Grows</th><th scope="col">Slots</th><th scope="col">Appending: age of the last slot</th><th scope="col">Appending: positions of the original slots</th><th scope="col">Inserting: age of the last slot</th></tr></thead><tbody>${rows}</tbody></table></div></details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  for (let g = 1; g <= d.grows; g++) {
    if (m.info('append', g).lastAge !== 0) out.push(`grow ${g}: appending should leave a new slot last`);
    if (m.info('insert', g).lastAge !== g) out.push(`grow ${g}: inserting should keep the original slot last`);
    if (layout(d, 'append', g).length !== layout(d, 'insert', g).length) out.push(`grow ${g}: the two windows differ in size`);
  }
  if (!/positions of the original slots/.test(body)) out.push('table missing');
  return out;
}

export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage');
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const q = (s) => root.querySelector(s);
  const render = (g) => { stage.innerHTML = chartSvg(d, m, g); q('[data-ctl=readout]').textContent = readoutFor(d, m, g); q('[data-ctl=gv]').textContent = g; };
  q('[data-ctl=grows]').addEventListener('input', (e) => render(Number(e.target.value)));
  render(1);
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="slot-grow"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}

export const interactiveStage = false;
