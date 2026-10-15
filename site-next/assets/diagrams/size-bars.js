import { hasDom, tablist } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');
export const MODES = [{ id: 'size', label: 'Parameters' }, { id: 'spelling', label: 'Spelling' }];
export function metrics(d) {
const own = d.sizes.find((s) => !s.reference), refs = d.sizes.filter((s) => s.reference);
return { own, refs, smallest: refs.reduce((a, b) => (b.value < a.value ? b : a)), largest: refs.reduce((a, b) => (b.value > a.value ? b : a)), spelling: d.spelling, n: { size: d.sizes.length, spelling: d.spelling.length } };
}
export function readoutFor(d, m, mode, i) {
if (mode === 'size') {
const s = d.sizes[i];
if (!s.reference) return `${s.label}: ${s.shown} parameters, which is ${(s.value / m.smallest.value).toFixed(3)} of the smallest reference (${m.smallest.shown}) and ${(s.value / m.largest.value).toFixed(4)} of the largest (${m.largest.shown}).`;
return `${s.label}: ${s.shown} parameters, ${(s.value / m.own.value).toFixed(1)} times this checkpoint.`;
}
const p = d.spelling[i];
return `${p.label}: ${p.value}% of words spelled correctly${p.earlier != null ? `, against ${p.earlier}% at the earlier measurement` : ''}.`;
}
export function chartSvg(d, m, mode, i, narrow = false) {
const W = narrow ? 400 : 700, H = narrow ? 300 : 260, L = narrow ? 116 : 190, R = narrow ? 52 : 70, T = 14, pw = W - L - R;
const nm = (s) => (narrow ? s.replace('TinyStories reference', 'Reference') : s);
if (mode === 'size') {
const lo = Math.log10(m.own.value) - 0.3, hi = Math.log10(m.largest.value) + 0.2, X = (v) => ((Math.log10(v) - lo) / (hi - lo)) * pw, rh = (H - T - 40) / d.sizes.length;
const ticks = [1e5, 1e6, 1e7].filter((t) => Math.log10(t) > lo && Math.log10(t) < hi).map((t) => `<line class="gl" x1="${L + X(t)}" y1="${T}" x2="${L + X(t)}" y2="${H - 34}"/><text class="tk" x="${L + X(t)}" y="${H - 20}" text-anchor="middle">${n0(t)}</text>`).join('');
const bars = d.sizes.map((s, k) => `<g class="${k === i ? 'sel' : ''}"><text class="rl" x="${L - 8}" y="${T + k * rh + rh / 2 + 4}" text-anchor="end">${esc(nm(s.label))}</text><rect class="bar ${s.reference ? 'ref' : 'own'}" x="${L}" y="${T + k * rh + 4}" width="${Math.max(2, X(s.value)).toFixed(1)}" height="${(rh - 8).toFixed(1)}" rx="3"/><text class="val" x="${L + X(s.value) + 6}" y="${T + k * rh + rh / 2 + 4}">${esc(s.shown)}</text></g>`).join('');
return `<svg class="sb-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="sb-t sb-d" preserveAspectRatio="xMidYMid meet"><title id="sb-t">Parameter count against the TinyStories reference sizes</title><desc id="sb-d">${esc(readoutFor(d, m, 'size', 0))}</desc>${ticks}${bars}<text class="ax" x="${L + pw / 2}" y="${H - 4}" text-anchor="middle">parameters, logarithmic scale</text></svg>`;
}
const rh = (H - T - 40) / d.spelling.length, X = (v) => (v / 100) * pw;
const grid = [0, 25, 50, 75, 100].map((t) => `<line class="gl" x1="${L + X(t)}" y1="${T}" x2="${L + X(t)}" y2="${H - 34}"/><text class="tk" x="${L + X(t)}" y="${H - 20}" text-anchor="middle">${t}</text>`).join('');
const bars = d.spelling.map((s, k) => `<g class="${k === i ? 'sel' : ''}"><text class="rl" x="${L - 8}" y="${T + k * rh + rh / 2 + 4}" text-anchor="end">${esc(s.label)}</text><rect class="bar own" x="${L}" y="${T + k * rh + 4}" width="${X(s.value).toFixed(1)}" height="${(rh - 8).toFixed(1)}" rx="3"/>${s.earlier != null ? `<line class="earlier" x1="${L + X(s.earlier)}" y1="${T + k * rh}" x2="${L + X(s.earlier)}" y2="${T + k * rh + rh}"/><text class="tk" x="${L + X(s.earlier)}" y="${T + k * rh - 2}" text-anchor="middle">earlier ${s.earlier}</text>` : ''}<text class="val" x="${L + X(s.value) + 6}" y="${T + k * rh + rh / 2 + 4}">${s.value}%</text></g>`).join('');
return `<svg class="sb-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="sb-t sb-d" preserveAspectRatio="xMidYMid meet"><title id="sb-t">Share of words spelled correctly by word length</title><desc id="sb-d">${esc(readoutFor(d, m, 'spelling', 0))}</desc>${grid}${bars}<text class="ax" x="${L + pw / 2}" y="${H - 4}" text-anchor="middle">percent of words spelled correctly</text></svg>`;
}
export function staticSvg(d) { return chartSvg(d, metrics(d), 'size', 0); }
export function controlsHtml(id, m) {
const tabs = MODES.map((md, k) => `<button type="button" role="tab" id="${id}-tab-${md.id}" data-mode="${md.id}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(md.label)}</button>`).join('');
return { tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="What to compare" hidden>${tabs}</div>`, controls: `<div class="dia-controls dia-ui" hidden><label class="dia-field"><span data-ctl="lab">Row</span> <input type="range" min="0" max="${m.n.size - 1}" step="1" value="0" data-ctl="pos" aria-describedby="${id}-read"></label></div>
<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden></p>` };
}
export function tablesHtml(d, m) {
const row = (cells) => '<tr>' + cells.map((c, k) => (k === 0 ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
return `<details class="dia-data"><summary>Data behind the diagram</summary><div class="tbl-wrap"><table class="tbl"><caption>Parameter count against the TinyStories reference sizes</caption><thead><tr><th scope="col">Model</th><th scope="col">Parameters</th></tr></thead><tbody>${d.sizes.map((s) => row([s.label, s.shown])).join('')}</tbody></table></div><div class="tbl-wrap"><table class="tbl"><caption>Share of words spelled correctly on a held-out sample</caption><thead><tr><th scope="col">Word length</th><th scope="col">Spelled correctly (percent)</th><th scope="col">Earlier measurement (percent)</th></tr></thead><tbody>${d.spelling.map((s) => row([s.label, String(s.value), s.earlier == null ? '' : String(s.earlier)])).join('')}</tbody></table></div></details>`;
}
export function verify(d, body) {
const m = metrics(d), out = [];
if (!(m.own.value < m.smallest.value)) out.push('the checkpoint is not smaller than every reference');
if (m.refs.length < 2) out.push('too few reference sizes');
if (d.spelling.some((s) => s.value < 0 || s.value > 100)) out.push('a spelling rate is outside 0 to 100');
if (!/Spelled correctly/.test(body)) out.push('table missing');
return out;
}
export function mount(root, d) {
const m = metrics(d), stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const q = (s) => root.querySelector(s), slider = q('[data-ctl=pos]');
const S = { mode: 'size', i: 0 };
let narrow = stage.getBoundingClientRect().width < 520;
const render = () => { stage.innerHTML = chartSvg(d, m, S.mode, S.i, narrow); q('[data-ctl=readout]').textContent = readoutFor(d, m, S.mode, S.i); slider.max = String(m.n[S.mode] - 1); slider.value = String(S.i); q('[data-ctl=lab]').textContent = S.mode === 'size' ? 'Model' : 'Word length'; };
tablist(q('[role="tablist"]'), (mode) => { S.mode = mode; S.i = 0; render(); });
slider.addEventListener('input', () => { S.i = Number(slider.value); render(); });
new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; render(); } }).observe(stage);
render();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="size-bars"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export const interactiveStage = false;
