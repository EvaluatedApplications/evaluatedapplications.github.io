import { hasDom, tablist } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
export const MODES = [{ id: 'six', label: 'Six layers, point by point' }, { id: 'last', label: 'Four layers against six' }, { id: 'passes', label: 'Passes doing work' }];
export const kindOf = (p) => (p.confidence >= 0.99995 ? 'copy' : p.candidates < 2 ? 'pinned' : 'weighing');
const KIND_TEXT = { copy: 'an exact copy of its input', pinned: 'pinned to a single token', weighing: 'weighing several tokens' };
export function metrics(d) {
const maxC = Math.max(...d.points.map((p) => p.candidates));
const counts = { copy: 0, pinned: 0, weighing: 0 };
d.points.forEach((p) => counts[kindOf(p)]++);
return { points: d.points, maxC, counts, firstWeighing: d.points.findIndex((p) => kindOf(p) === 'weighing'), last: d.last, passes: d.passes, size: { six: d.points.length, last: d.last.length, passes: d.passes.length } };
}
export function readoutFor(d, m, mode, i) {
if (mode === 'six') { const p = d.points[i]; return `${p.label}: confidence ${p.confidence.toFixed(4)}, ${p.candidates.toFixed(2)} effective candidates, ${KIND_TEXT[kindOf(p)]}. ${m.counts.copy} of ${d.points.length} read points are exact copies and ${m.counts.weighing} weigh several tokens.`; }
if (mode === 'last') { const r = d.last[i]; return `${r.position}: four-layer ${r.four.confidence.toFixed(4)} confidence and ${r.four.candidates.toFixed(2)} candidates, six-layer ${r.six.confidence.toFixed(4)} and ${r.six.candidates.toFixed(2)}.`; }
const p = d.passes[i]; return `${p.model}: ${p.doing} of ${p.total} passes carry real information.`;
}
function six(d, m, i, narrow) {
const W = narrow ? 400 : 700, H = 250, base = 190, bw = narrow ? 15 : 28, gap = narrow ? 9 : 22, x0 = narrow ? 14 : 26, n = d.points.length, hOf = (c) => 12 + 120 * (Math.log2(c) / Math.log2(m.maxC));
const blocks = d.points.map((p, k) => {
const x = x0 + k * (bw + gap), h = hOf(p.candidates), kd = kindOf(p), sel = k === i, dx = narrow ? 6 : 10, dy = narrow ? 4 : 6;
const front = `<rect class="f ${kd}" x="${x}" y="${base - h}" width="${bw}" height="${h.toFixed(1)}"/>`;
const side = `<polygon class="s ${kd}" points="${x + bw},${base - h} ${x + bw + dx},${base - h - dy} ${x + bw + dx},${base - dy} ${x + bw},${base}"/>`;
const top = `<polygon class="t ${kd}" points="${x},${base - h} ${x + dx},${base - h - dy} ${x + bw + dx},${base - h - dy} ${x + bw},${base - h}"/>`;
const lab = p.label.replace('layer ', 'L').replace(', pass ', '.p').replace('embedding', 'emb').replace('final', 'out');
return `<g class="blk${sel ? ' sel' : ''}">${side}${top}${front}${sel ? `<rect class="selbox" x="${x - 3}" y="${base - h - dy - 3}" width="${bw + dx + 6}" height="${h + dy + 6}" rx="3"/>` : ''}${!narrow || k % 2 === 0 || sel ? `<text class="tk" x="${x + bw / 2}" y="${base + 16}" text-anchor="middle">${esc(lab)}</text>` : ''}</g>`;
}).join('');
const leg = [['copy', 'exact copy'], ['pinned', 'one token'], ['weighing', 'weighing several']].map(([k, t], j) => `<g transform="translate(${x0 + j * (narrow ? 120 : 170)} ${H - 14})"><rect class="f ${k}" width="10" height="10" rx="2"/><text class="tk" x="15" y="9">${t}</text></g>`).join('');
return `<svg class="ls-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="ls-t ls-d" preserveAspectRatio="xMidYMid meet"><title id="ls-t">Read points of a six-layer model</title><desc id="ls-d">${esc(readoutFor(d, m, 'six', d.points.length - 1))}</desc><text class="ax" x="${x0}" y="22">Height is effective candidates on a log scale. Left is the input, right is the output.</text>${blocks}${leg}</svg>`;
}
function pairs(rows, W, H, getA, getB, la, lb, labelOf, max, i) {
const L = 150, pw = W - L - 40, rh = (H - 50) / rows.length;
const g = rows.map((r, k) => { const y = 24 + k * rh, a = (getA(r) / max) * pw, b = (getB(r) / max) * pw; return `<g class="${k === i ? 'sel' : ''}"><text class="rl" x="${L - 8}" y="${y + 16}" text-anchor="end">${esc(labelOf(r))}</text><rect class="pa" x="${L}" y="${y}" width="${Math.max(2, a).toFixed(1)}" height="12" rx="3"/><text class="val" x="${L + a + 6}" y="${y + 10}">${getA(r)}</text><rect class="pb" x="${L}" y="${y + 15}" width="${Math.max(2, b).toFixed(1)}" height="12" rx="3"/><text class="val" x="${L + b + 6}" y="${y + 25}">${getB(r)}</text></g>`; }).join('');
return `${g}<g transform="translate(${L} ${H - 12})"><rect class="pa" width="10" height="10" rx="2"/><text class="tk" x="15" y="9">${la}</text><rect class="pb" x="150" width="10" height="10" rx="2"/><text class="tk" x="165" y="9">${lb}</text></g>`;
}
export function chartSvg(d, m, mode, i, narrow = false) {
if (mode === 'six') return six(d, m, i, narrow);
const W = narrow ? 420 : 700, H = 230;
const inner = mode === 'last'
? pairs(d.last, W, H, (r) => r.four.candidates, (r) => r.six.candidates, 'four-layer candidates', 'six-layer candidates', (r) => r.position, Math.max(...d.last.flatMap((r) => [r.four.candidates, r.six.candidates])), i)
: pairs(d.passes, W, H, (r) => r.doing, (r) => r.total, 'passes doing work', 'total passes', (r) => r.model, Math.max(...d.passes.map((r) => r.total)), i);
return `<svg class="ls-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="ls-t ls-d" preserveAspectRatio="xMidYMid meet"><title id="ls-t">${mode === 'last' ? 'Last four read points, four layers against six' : 'Passes that carry real information'}</title><desc id="ls-d">${esc(readoutFor(d, m, mode, 0))}</desc>${inner}</svg>`;
}
export function staticSvg(d) { return chartSvg(d, metrics(d), 'six', d.points.length - 1); }
export function controlsHtml(id, m) {
const tabs = MODES.map((md, k) => `<button type="button" role="tab" id="${id}-tab-${md.id}" data-mode="${md.id}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(md.label)}</button>`).join('');
return { tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="View" hidden>${tabs}</div>`, controls: `<div class="dia-controls dia-ui" hidden><label class="dia-field"><span data-ctl="lab">Read point</span> <input type="range" min="0" max="${m.size.six - 1}" step="1" value="${m.size.six - 1}" data-ctl="pos" aria-describedby="${id}-read"></label></div>
<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden></p>` };
}
export function tablesHtml(d, m) {
const row = (cells) => '<tr>' + cells.map((c, k) => (k === 0 ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
const t = (cap, cols, rows) => `<div class="tbl-wrap"><table class="tbl"><caption>${esc(cap)}</caption><thead><tr>${cols.map((c) => `<th scope="col">${esc(c)}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>`;
return `<details class="dia-data"><summary>Data behind the diagram</summary>${t('Six-layer snapshot, read at each point', ['Point', 'Confidence', 'Effective candidates', 'State'], d.points.map((p) => row([p.label, p.confidence.toFixed(4), p.candidates.toFixed(2), KIND_TEXT[kindOf(p)]])))}${t('Last four read points', ['Position from the end', 'Four-layer', 'Six-layer'], d.last.map((r) => row([r.position, `${r.four.confidence.toFixed(4)} / ${r.four.candidates.toFixed(2)}`, `${r.six.confidence.toFixed(4)} / ${r.six.candidates.toFixed(2)}`])))}${t('Passes that carry real information', ['Model', 'Passes doing work', 'Total passes'], d.passes.map((p) => row([p.model, String(p.doing), String(p.total)])))}</details>`;
}
export function verify(d, body) {
const m = metrics(d), out = [];
if (m.firstWeighing < 0) out.push('no read point weighs several tokens');
if (d.points.slice(m.firstWeighing).some((p) => kindOf(p) !== 'weighing')) out.push('a copy follows a weighing point');
if (d.points[0].candidates !== 1) out.push('the embedding should pin one token');
if (!/Effective candidates/.test(body)) out.push('table missing');
return out;
}
export function mount(root, d) {
const m = metrics(d), stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const q = (s) => root.querySelector(s), slider = q('[data-ctl=pos]');
const S = { mode: 'six', i: m.size.six - 1 };
let narrow = stage.getBoundingClientRect().width < 520;
const render = () => { stage.innerHTML = chartSvg(d, m, S.mode, S.i, narrow); q('[data-ctl=readout]').textContent = readoutFor(d, m, S.mode, S.i); q('[data-ctl=lab]').textContent = S.mode === 'six' ? 'Read point' : S.mode === 'last' ? 'Position' : 'Model'; slider.max = String(m.size[S.mode] - 1); slider.value = String(S.i); };
tablist(q('[role="tablist"]'), (mode) => { S.mode = mode; S.i = m.size[mode] - 1; render(); });
slider.addEventListener('input', () => { S.i = Number(slider.value); render(); });
new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; render(); } }).observe(stage);
render();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="layer-stack"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export const interactiveStage = false;
