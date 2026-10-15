import { hasDom, prefersReducedMotion } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const bin = (n, bits) => n.toString(2).padStart(bits, '0');
const fmt = (n) => (n >= 1000 ? n.toLocaleString('en-GB') : String(n));
const VIEWS = [
{ id: 'before', label: '1 Stack' },
{ id: 'kernel', label: '2 Kernel' },
{ id: 'after', label: '3 One pass' },
{ id: 'cost', label: '4 Cost' },
];
export function metrics(d) {
const FB = d.shape.flagBits, T = d.shape.timePeriod, W = 1 << FB;
const move = d.kernel.elements.find((e) => e.flagMask);
const mask = parseInt(move.flagMask, 2), shift = move.timeShift, wMove = move.weight, wKeep = d.kernel.elements.find((e) => e.element === 'identity').weight;
const items = d.store.items.map((it, i) => ({ i, key: it.key, flags: parseInt(it.flags, 2), time: it.time, count: it.count }));
const flagStack = Array.from({ length: W }, () => items.map(() => ({ keep: 0, move: 0 })));
const timeStack = Array.from({ length: T }, () => items.map(() => ({ keep: 0, move: 0 })));
items.forEach((it) => {
flagStack[it.flags][it.i].keep += wKeep * it.count; flagStack[it.flags ^ mask][it.i].move += wMove * it.count;
timeStack[it.time][it.i].keep += wKeep * it.count; timeStack[(it.time + shift) % T][it.i].move += wMove * it.count;
});
const sum = (st, f) => st.map((cell) => cell.reduce((a, s) => a + f(s), 0));
const flagsAfter = sum(flagStack, (s) => s.keep + s.move), timesAfter = sum(timeStack, (s) => s.keep + s.move);
const flagsBefore = sum(flagStack, (s) => s.keep), timesBefore = sum(timeStack, (s) => s.keep);
const total = items.reduce((a, it) => a + it.count, 0);
const maxBar = Math.max(...flagsAfter, ...timesAfter);
const c = d.cost;
return {
FB, T, W, items, mask, shift, wMove, wKeep, flagStack, timeStack, flagsAfter, timesAfter, flagsBefore, timesBefore, total,
totalAfter: total * (wKeep + wMove), maxBar, jointCells: W * T,
applyLo: c.applyMs[0], applyHi: c.applyMs[1], elementsMax: c.elementsMax,
brute1: c.bruteForceMs['1'], brute1000: c.bruteForceMs['1000'],
};
}
export function limitFacts(m) {
return {
superItems: m.items.length, superTotal: m.total, superTotalAfter: m.totalAfter, superCells: m.jointCells,
applyLo: m.applyLo, applyHi: m.applyHi, elementsMax: fmt(m.elementsMax), brute1: m.brute1, brute1000: fmt(m.brute1000),
};
}
const kernelLine = (m) => `${m.wKeep} × identity plus ${m.wMove} × (flip flag bit 0, age ${m.shift} tick)`;
export function readoutFor(view, d, m) {
const sentenceCounts = (a) => a.join(' ');
if (view === 'before') return `${m.items.length} items sit in one store of ${d.store.cellCount} cells (key capacity ${d.shape.keyCapacity}). Their counts add to ${m.total} (${m.items.map((i) => i.count).join(' + ')}). Counts per flag word: ${sentenceCounts(m.flagsBefore)}. Counts per tick: ${sentenceCounts(m.timesBefore)}.`;
if (view === 'kernel') return `Two operators are summed into one kernel: ${kernelLine(m)}. The kernel is one table of ${d.kernel.numbersPerCell} numbers (${m.W} flag words by ${m.T} ticks), whatever the elements are.`;
if (view === 'after') return `One pass over the ${d.store.cellCount} cells moved every item by both operators. The total went from ${m.total} to ${m.totalAfter} (${m.total} + ${m.wMove} × ${m.total}). Counts per flag word: ${sentenceCounts(m.flagsAfter)}. Counts per tick: ${sentenceCounts(m.timesAfter)}. All of them are exact.`;
return `Apply took ${m.applyLo} to ${m.applyHi} ms per call from 1 to ${fmt(m.elementsMax)} kernel elements, with no trend. Building the same store from the moved items took ${m.brute1} ms at 1 element and ${fmt(m.brute1000)} ms at 1,000.`;
}
function wrap(text, n) {
const out = []; let line = '';
text.split(' ').forEach((w) => { if ((line + ' ' + w).trim().length > n) { out.push(line); line = w; } else line = (line + ' ' + w).trim(); });
if (line) out.push(line);
return out;
}
export function sceneSvg(d, m, o) {
const narrow = !!o.narrow, view = o.view;
const W = narrow ? 360 : 760;
const sweeping = view === 'sweep', p = sweeping ? o.p : view === 'after' ? 1 : 0;
const blend = view === 'after' ? 1 : sweeping ? Math.max(0, Math.min(1, (p - 0.5) / 0.5)) : 0;
const showKernel = view !== 'before';
const g = [];
const t = (x, y, s, cls, anchor) => g.push(`<text x="${x}" y="${y}" class="${cls || ''}"${anchor ? ` text-anchor="${anchor}"` : ''}>${esc(s)}</text>`);
const r = (x, y, w, h, cls, rx = 3, extra = '') => g.push(`<rect x="${+x.toFixed(1)}" y="${+y.toFixed(1)}" width="${+w.toFixed(1)}" height="${+Math.max(h, 0).toFixed(1)}" rx="${rx}" class="${cls}"${extra}/>`);
let H;
if (view === 'cost') {
H = narrow ? 470 : 340;
const L = narrow ? 44 : 64, R = W - (narrow ? 30 : 22), Tp = 14, B = narrow ? 250 : 236;
const lx = (v) => L + (Math.log10(v) / Math.log10(m.elementsMax)) * (R - L);
const lo = 0.1, hi = 3000;
const ly = (v) => B - ((Math.log10(v) - Math.log10(lo)) / (Math.log10(hi) - Math.log10(lo))) * (B - Tp);
[0.1, 1, 10, 100, 1000].forEach((v) => { r(L, ly(v) - 0.5, R - L, 1, 'ss-grid', 0); t(L - 6, ly(v) + 3, String(v), 'ss-tick', 'end'); });
[1, 10, 100, 1000, 10000].forEach((v) => { if (v <= m.elementsMax) { r(lx(v) - 0.5, Tp, 1, B - Tp, 'ss-grid', 0); t(lx(v), B + 14, fmt(v), 'ss-tick', 'middle'); } });
t((L + R) / 2, B + 30, 'Kernel elements (log scale)', 'ss-lab', 'middle');
t(L, 10, 'ms per call (log scale)', 'ss-lab');
r(lx(1), ly(m.applyHi), lx(m.elementsMax) - lx(1), ly(m.applyLo) - ly(m.applyHi), 'ss-apply', 2);
t(lx(m.elementsMax) - 6, ly(m.applyHi) - 6, `Apply: ${m.applyLo} to ${m.applyHi} ms${narrow ? `` : `, whatever the element count`}`, 'ss-val', 'end');
[[1, m.brute1], [1000, m.brute1000]].forEach(([e, ms]) => {
g.push(`<circle cx="${+lx(e).toFixed(1)}" cy="${+ly(ms).toFixed(1)}" r="5" class="ss-brute"/>`);
t(lx(e) + (e === 1 ? 10 : -10), ly(ms) + 4, `${fmt(ms)} ms at ${fmt(e)} ${e === 1 ? 'element' : 'elements'}`, 'ss-val', e === 1 ? 'start' : 'end');
});
t(narrow ? 10 : L - 30, B + 50, 'Dots: building the same store from the moved items', 'ss-soft');
const lines = wrap(d.cost.conditions, narrow ? 54 : 120);
lines.forEach((ln, i) => t(narrow ? 10 : L - 30, B + 70 + i * 15, ln, 'ss-soft'));
H = B + 70 + lines.length * 15 + 8;
return `<svg class="dia-plan ss-svg" viewBox="0 0 ${W} ${H}" data-h="${H}" role="img" aria-labelledby="ss-t ss-d"><title id="ss-t">Measured cost of one kernel pass</title><desc id="ss-d">${esc(readoutFor('cost', d, m))}</desc>${g.join('')}</svg>`;
}
const B1 = narrow ? { x: 10, y: 0, w: 340 } : { x: 0, y: 0, w: 220 };
const B2 = narrow ? { x: 10, y: 150, w: 340 } : { x: 0, y: 168, w: 220 };
const B3 = narrow ? { x: 10, y: 282, w: 340 } : { x: 246, y: 0, w: 250 };
const B4 = narrow ? { x: 10, y: 530, w: 340 } : { x: 520, y: 0, w: 240 };
H = narrow ? 800 : 330;
t(B1.x, B1.y + 14, 'Items', 'ss-lab');
m.items.forEach((it, i) => {
const y = B1.y + 24 + i * 38;
r(B1.x, y, 5, 32, `ss-i${i} ss-fill`, 2);
t(B1.x + 14, y + 13, `key ${it.key}, count ${it.count}`, 'ss-ink');
t(B1.x + 14, y + 27, `flags ${bin(it.flags, m.FB)}, tick ${it.time}`, 'ss-soft');
});
if (showKernel) {
const hh = 118;
g.push(`<rect x="${B2.x}" y="${B2.y}" width="${B2.w}" height="${hh}" rx="8" class="ss-kbox"/>`);
t(B2.x + 12, B2.y + 20, 'One kernel, two operators', 'ss-lab');
t(B2.x + 12, B2.y + 44, `${m.wKeep} ×  identity`, 'ss-ink');
t(B2.x + 12, B2.y + 64, `${m.wMove} ×  flip flag bit 0, age ${m.shift} tick`, 'ss-ink');
r(B2.x + 12, B2.y + 76, B2.w - 24, 1, 'ss-grid', 0);
t(B2.x + 12, B2.y + 96, `${d.kernel.numbersPerCell} numbers (${m.W} flag words by ${m.T} ticks)`, 'ss-acc');
t(B2.x + 12, B2.y + 110, 'one table, whatever the elements', 'ss-soft');
}
t(B3.x, B3.y + 14, `One store: ${d.store.cellCount} cells`, 'ss-lab');
const cols = 12, rows = d.store.cellCount / cols;
const cw = (B3.w - 6) / cols, ch = narrow ? 14 : 13, gy = B3.y + 24;
const swept = sweeping ? p / 0.6 : view === 'after' ? 2 : 0;
for (let rr = 0; rr < rows; rr++) for (let cc = 0; cc < cols; cc++) {
const lit = swept * rows > rr + 0.5;
r(B3.x + cc * cw, gy + rr * (ch + 3), cw - 3, ch, lit ? 'ss-cell ss-lit' : 'ss-cell', 2);
}
if (sweeping && p < 0.6) g.push(`<rect x="${B3.x - 4}" y="${+(gy + Math.min(1, p / 0.6) * rows * (ch + 3) - 2).toFixed(1)}" width="${+(B3.w + 4).toFixed(1)}" height="3" class="ss-sweep"/>`);
t(B3.x, gy + rows * (ch + 3) + 14, showKernel ? 'one pass across all cells' : `capacity ${d.shape.keyCapacity} keys`, 'ss-soft');
const ty = gy + rows * (ch + 3) + 30, scale = (B3.w - 6) / m.totalAfter;
t(B3.x, ty, 'Total count', 'ss-lab');
let x = B3.x;
m.items.forEach((it, i) => { const w = it.count * m.wKeep * scale; r(x, ty + 8, w - 1, 20, `ss-i${i} ss-fill`, 2); x += w; });
m.items.forEach((it, i) => { const w = it.count * m.wMove * scale * blend; if (w > 0.5) { r(x, ty + 8, w - 1, 20, `ss-i${i} ss-fill ss-copy`, 2); x += w; } });
const shown = Math.round(m.total + (m.totalAfter - m.total) * blend);
t(B3.x + B3.w - 6, ty, String(shown), 'ss-big', 'end');
t(B3.x, ty + 44, blend >= 1 ? `${m.total} kept + ${m.totalAfter - m.total} moved copies` : `${m.items.map((i) => i.count).join(' + ')}`, 'ss-soft');
const bars = (title, stack, labelOf, y0, n) => {
t(B4.x, y0 - 94, title, 'ss-lab');
const bw = B4.w / n, base = y0, hmax = 66;
stack.forEach((cell, j) => {
let y = base;
cell.forEach((s, i) => {
const hk = (s.keep / m.maxBar) * hmax, hm = ((s.move * blend) / m.maxBar) * hmax;
if (hk > 0) { y -= hk; r(B4.x + j * bw + 2, y, bw - 5, hk, `ss-i${i} ss-fill`, 2); }
if (hm > 0.2) { y -= hm; r(B4.x + j * bw + 2, y, bw - 5, hm, `ss-i${i} ss-fill ss-copy`, 2); }
});
const v = cell.reduce((a, s) => a + s.keep + s.move * blend, 0);
if (v > 0) t(B4.x + j * bw + bw / 2 - 0.5, y - 4, String(Math.round(v)), 'ss-val', 'middle');
t(B4.x + j * bw + bw / 2 - 0.5, base + 12, labelOf(j), 'ss-tick', 'middle');
});
r(B4.x, base + 0.5, B4.w, 1, 'ss-grid', 0);
};
bars('Count per flag word', m.flagStack, (j) => bin(j, m.FB), B4.y + 112, m.W);
bars('Count per tick', m.timeStack, (j) => String(j), B4.y + 250, m.T);
t(B4.x, B4.y + 290, blend > 0 ? 'bright: kept; dim: moved copies' : 'each colour is one item', 'ss-soft');
return `<svg class="dia-plan ss-svg" viewBox="0 0 ${W} ${H}" data-h="${H}" role="img" aria-labelledby="ss-t ss-d"><title id="ss-t">${esc(view === 'before' ? 'Three items stacked in one store' : 'One kernel applied to the store in one pass')}</title><desc id="ss-d">${esc(readoutFor(view === 'sweep' ? 'after' : view, d, m))}</desc>${g.join('')}</svg>`;
}
export function staticSvg(d) { return sceneSvg(d, metrics(d), { view: 'after', narrow: false }); }
export function controlsHtml(id, m, d) {
const tabs = VIEWS.map((v, i) => `<button type="button" role="tab" id="${id}-tab-${v.id}" data-mode="${v.id}" aria-selected="${i === 2}" aria-controls="${id}-stage" tabindex="${i === 2 ? 0 : -1}">${esc(v.label)}</button>`).join('');
return {
tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="Step" hidden>${tabs}</div>`,
controls: `<p class="dia-readout" data-ctl="readout" role="status">${esc(readoutFor('after', d, m))}</p>
<div class="dia-controls dia-ui" hidden><button type="button" class="btn btn-ghost btn-sm" data-ctl="replay">Replay the pass</button></div>`,
};
}
export function tablesHtml(d, m) {
const row = (cells) => '<tr>' + cells.map((c, i) => (i === 0 ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
return `<details class="dia-data"><summary>The same numbers as tables</summary>
<div class="tbl-wrap"><table class="tbl"><caption>Count per flag word, before and after the pass</caption><thead><tr><th scope="col">Flag word</th><th scope="col">Before</th><th scope="col">After</th></tr></thead><tbody>${m.flagsAfter.map((v, j) => row([bin(j, m.FB), m.flagsBefore[j], v])).join('')}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Count per tick, before and after the pass</caption><thead><tr><th scope="col">Tick</th><th scope="col">Before</th><th scope="col">After</th></tr></thead><tbody>${m.timesAfter.map((v, j) => row([j, m.timesBefore[j], v])).join('')}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Recorded cost. ${esc(d.cost.conditions)}</caption><thead><tr><th scope="col">Kernel elements</th><th scope="col">Apply, ms per call</th><th scope="col">Building the store from the moved items, ms</th></tr></thead><tbody>${row(['1 to ' + fmt(m.elementsMax), `${m.applyLo} to ${m.applyHi}`, ''])}${row(['1', '', m.brute1])}${row(['1,000', '', fmt(m.brute1000)])}</tbody></table></div>
</details>`;
}
export function verify(d, body) {
const m = metrics(d), out = [];
const same = (a, rec, label) => { a.forEach((v, j) => { const key = label === 'f' ? bin(j, m.FB) : String(j); const want = rec[key] || 0; if (v !== want) out.push(`derived ${label === 'f' ? 'flag ' + key : 'tick ' + key} count ${v} differs from the recorded ${want}`); }); };
same(m.flagsAfter, d.result.flags, 'f'); same(m.timesAfter, d.result.times, 't');
if (m.totalAfter !== d.result.totalCount) out.push(`total after ${m.totalAfter} differs from the recorded ${d.result.totalCount}`);
if (m.total !== d.store.totalCount) out.push('item counts do not add to the recorded total');
if (m.jointCells !== d.kernel.numbersPerCell) out.push(`joint cells ${m.jointCells} differ from the kernel's ${d.kernel.numbersPerCell} numbers`);
m.items.forEach((it) => { if ((d.reads.flags[bin(it.flags, m.FB)] || 0) < it.count) out.push('a recorded flag read is below an item count'); });
m.flagsBefore.forEach((v, j) => { if (v !== (d.reads.flags[bin(j, m.FB)] || 0)) out.push('derived flag counts before differ from the recorded read'); });
m.timesBefore.forEach((v, j) => { if (v !== (d.reads.times[String(j)] || 0)) out.push('derived tick counts before differ from the recorded read'); });
if (!(m.applyLo < m.applyHi) || !(m.brute1 < m.brute1000)) out.push('cost record is out of order');
if (!body.includes(d.cost.conditions.slice(0, 40).replace(/&/g, '&amp;'))) out.push('conditions are not stated beside the cost');
return out;
}
export function mount(root, d) {
const m = metrics(d), reduced = prefersReducedMotion();
const stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const q = (s) => root.querySelector(s);
const readout = q('[data-ctl=readout]');
const tabs = [...root.querySelectorAll('[role=tab]')];
const S = { view: 'after', p: 0, timer: 0, narrow: false };
function draw() {
S.narrow = stage.clientWidth < 560;
stage.innerHTML = sceneSvg(d, m, { view: S.view, p: S.p, narrow: S.narrow });
const h = Number(stage.firstElementChild.dataset.h), w = S.narrow ? 360 : 760;
stage.style.aspectRatio = `${w} / ${h}`;
root.dataset.view = S.view;
}
function select(view, play) {
clearTimeout(S.timer);
S.view = view; S.p = 0;
tabs.forEach((tb) => { const on = tb.dataset.mode === view; tb.setAttribute('aria-selected', String(on)); tb.tabIndex = on ? 0 : -1; });
readout.textContent = readoutFor(view, d, m);
draw();
}
function replay() {
clearTimeout(S.timer);
if (reduced) { select('after'); return; }
tabs.forEach((tb) => { const on = tb.dataset.mode === 'after'; tb.setAttribute('aria-selected', String(on)); tb.tabIndex = on ? 0 : -1; });
S.view = 'sweep'; readout.textContent = 'One pass moves every item by both operators at once.';
const t0 = performance.now(), DUR = 2600;
const tick = () => { S.p = Math.min(1, (performance.now() - t0) / DUR); draw(); if (S.p < 1) S.timer = setTimeout(tick, 40); else select('after'); };
tick();
}
tabs.forEach((tb, i) => {
tb.addEventListener('click', () => select(tb.dataset.mode));
tb.addEventListener('keydown', (e) => {
const k = e.key, go = (j) => { e.preventDefault(); tabs[j].focus(); select(tabs[j].dataset.mode); };
if (k === 'ArrowRight' || k === 'ArrowDown') go((i + 1) % tabs.length); else if (k === 'ArrowLeft' || k === 'ArrowUp') go((i - 1 + tabs.length) % tabs.length);
else if (k === 'Home') go(0); else if (k === 'End') go(tabs.length - 1);
});
});
q('[data-ctl=replay]').addEventListener('click', replay);
let width = stage.clientWidth;
if (typeof ResizeObserver === 'function') new ResizeObserver(() => { if (Math.abs(stage.clientWidth - width) > 8) { width = stage.clientWidth; if (S.view !== 'sweep') draw(); } }).observe(stage);
select('after');
if (!reduced && typeof IntersectionObserver === 'function') {
const io = new IntersectionObserver((es) => { if (es[0].isIntersecting) { io.disconnect(); replay(); } }, { threshold: 0.5 });
io.observe(root);
}
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="superposition-scene"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export const interactiveStage = false;
