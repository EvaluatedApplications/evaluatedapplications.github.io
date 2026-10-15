import { hasDom, tablist } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');
const num = (v) => (Number.isInteger(v) ? n0(v) : String(v));
export function fmtRange(lo, hi, unit) {
const span = (a, b, u) => (a === b ? `${a}${u}` : `${a} to ${b}${u}`);
if (unit === 'KB') { const f = (v) => (v >= 1000 ? `${v / 1000} MB` : `${v} KB`); return lo === hi ? f(lo) : `${f(lo)} to ${f(hi)}`; }
if (unit === 'ms') {
if (lo >= 10000) return span((lo / 1000).toFixed(1), (hi / 1000).toFixed(1), ' s');
return span(num(lo), num(hi), ' ms');
}
if (unit === 'MiB') return span(num(lo), num(hi), ' MiB');
return span(num(lo), num(hi), '');
}
export function metrics(d) {
const me = Object.fromEntries(d.measures.map((x) => [x.id, x]));
return { ...me, W: Object.fromEntries(d.worker.map((w) => [w.id, w])), method: d.method, modes: d.measures.map((x) => x.id), series: d.series };
}
const grp = (me, id) => me.groups.find((g) => g.id === id);
const rng = (r, unit) => fmtRange(r.lo, r.hi, unit);
export function limitFacts(m) {
const b = grp(m.bytes, 'page'), cd = grp(m.content, 'desktop'), cp = grp(m.content, 'phone'), wd = grp(m.working, 'desktop'), wp = grp(m.working, 'phone');
const w = (id) => rng(m.W[id], m.W[id].unit);
return {
bytesClassic: rng(b.classic, m.bytes.unit), bytesShell: rng(b.shell, m.bytes.unit), requestsShell: String(b.shell.requests),
contentDesktopClassic: rng(cd.classic, m.content.unit), contentDesktopShell: rng(cd.shell, m.content.unit),
contentPhoneClassic: rng(cp.classic, m.content.unit), contentPhoneShell: rng(cp.shell, m.content.unit),
workingDesktopShell: rng(wd.shell, m.working.unit), workingPhoneShell: rng(wp.shell, m.working.unit),
workerStart: w('start'), callRt: w('call'), gap: w('gap'), longTasks: w('longTasks'), coopCancel: w('coop'), hardCancel: w('hard'),
restart: w('restart'), streamFirst: w('streamFirst'), streamAll: w('streamAll'), workerMemory: w('memory'), workerNetwork: m.W.network.text,
workerJob: m.method.workerJob, phoneProfile: m.method.phone, desktopRuns: String(m.method.desktopRuns), phoneRuns: String(m.method.phoneRuns),
machine: m.method.machine, gestureRequests: String(m.method.gestureRequests),
};
}
export function readoutFor(m, mode) {
const f = limitFacts(m);
if (mode === 'bytes') return `Before the visitor does anything, the Blazier static shell downloads ${f.bytesShell} in ${f.requestsShell} requests. The same app booted the usual way downloads ${f.bytesClassic}.`;
if (mode === 'content') return `Real content is on screen in ${f.contentDesktopShell} on desktop and ${f.contentPhoneShell} on the phone profile. Booted the usual way it takes ${f.contentDesktopClassic} on desktop and ${f.contentPhoneClassic} on the phone profile.`;
return `The runtime still costs what it costs. From pressing Start to a working page takes ${f.workingDesktopShell} on desktop and ${f.workingPhoneShell} on the phone profile. The usual boot takes ${f.contentDesktopClassic} and ${f.contentPhoneClassic} from loading the page. Blazier moves the wait behind a readable page.`;
}
function ticks(maxV) {
const p = 10 ** Math.floor(Math.log10(maxV));
for (const k of [0.1, 0.2, 0.5, 1, 2, 5, 10]) { const s = p * k; if (maxV / s <= 5) return { step: s, top: Math.ceil(maxV / s - 1e-9) * s }; }
return { step: p * 10, top: Math.ceil(maxV / (p * 10)) * p * 10 };
}
export function chartSvg(d, m, mode, narrow = false) {
const me = m[mode], W = narrow ? 400 : 700, L = narrow ? 112 : 170, R = narrow ? 100 : 124, T = 10, rh = narrow ? 32 : 34, pw = W - L - R, multi = me.groups.length > 1;
const per = me.axis.per, tk = ticks(Math.max(...me.groups.flatMap((g) => d.series.map((s) => g[s.id].hi))) / per);
const X = (v) => (v / per / tk.top) * pw;
let y = T; const rows = [];
for (const g of me.groups) {
if (multi) { rows.push(`<text class="gh" x="${L - 8}" y="${y + 11}" text-anchor="end">${esc(g.label)}</text>`); y += 18; }
for (const s of d.series) {
const r = g[s.id], lo = Math.max(2, X(r.lo)), hi = Math.max(lo, X(r.hi));
rows.push(`<g class="row ${s.id}"><text class="rl" x="${L - 8}" y="${y + rh / 2 + 1}" text-anchor="end">${esc(s.short)}</text><rect class="bar ${s.id}" x="${L}" y="${y + 4}" width="${lo.toFixed(1)}" height="${rh - 10}" rx="3"/>${hi > lo + 1 ? `<rect class="bar tail ${s.id}" x="${(L + lo).toFixed(1)}" y="${y + 4}" width="${(hi - lo).toFixed(1)}" height="${rh - 10}" rx="3"/>` : ''}<text class="val" x="${(L + hi + 6).toFixed(1)}" y="${y + rh / 2 + 1}">${esc(fmtRange(r.lo, r.hi, me.unit))}</text></g>`);
y += rh;
}
y += 8;
}
const H = y + 30, grid = [];
for (let t = 0; t <= tk.top + 1e-9; t += tk.step) grid.push(`<line class="gl" x1="${(L + (t / tk.top) * pw).toFixed(1)}" y1="${T}" x2="${(L + (t / tk.top) * pw).toFixed(1)}" y2="${H - 28}"/><text class="tk" x="${(L + (t / tk.top) * pw).toFixed(1)}" y="${H - 16}" text-anchor="middle">${Number(t.toFixed(3))}</text>`);
return `<svg class="bb-svg" viewBox="0 0 ${W} ${H.toFixed(0)}" role="img" aria-labelledby="bb-t bb-d" preserveAspectRatio="xMidYMid meet"><title id="bb-t">${esc(me.label)}, usual boot against Blazier static shell</title><desc id="bb-d">${esc(readoutFor(m, mode))}</desc>${grid.join('')}${rows.join('')}<text class="ax" x="${L + pw / 2}" y="${H - 3}" text-anchor="middle">${esc(me.axis.unit)}, linear scale; the lighter end of a bar is the slowest recorded run</text></svg>`;
}
export function staticSvg(d) { return chartSvg(d, metrics(d), 'bytes'); }
export function controlsHtml(id, m, d) {
const tabs = d.measures.map((x, k) => `<button type="button" role="tab" id="${id}-tab-${x.id}" data-mode="${x.id}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(x.tab)}</button>`).join('');
return { tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="What to compare" hidden>${tabs}</div>`, controls: `<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden></p>` };
}
export function tablesHtml(d, m) {
const row = (cells) => '<tr>' + cells.map((c, k) => (k === 0 ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
const head = (cols) => `<thead><tr>${cols.map((c) => `<th scope="col">${esc(c)}</th>`).join('')}</tr></thead>`;
const measure = (me) => `<div class="tbl-wrap"><table class="tbl"><caption>${esc(me.label)}${me.note ? '. ' + esc(me.note) : ''}</caption>${head(['Profile', ...d.series.map((s) => s.label)])}<tbody>${me.groups.map((g) => row([g.label, ...d.series.map((s) => fmtRange(g[s.id].lo, g[s.id].hi, me.unit))])).join('')}</tbody></table></div>`;
const worker = `<div class="tbl-wrap"><table class="tbl"><caption>Worker bridge, desktop</caption>${head(['Measure', 'Recorded'])}<tbody>${d.worker.map((w) => row([w.label, w.text || fmtRange(w.lo, w.hi, w.unit)])).join('')}</tbody></table></div>`;
return `<details class="dia-data"><summary>Data behind the diagram</summary>${d.measures.map(measure).join('')}${worker}</details>`;
}
export function verify(d, body) {
const m = metrics(d), out = [];
for (const id of ['bytes', 'content']) for (const g of m[id].groups) if (!(g.shell.hi < g.classic.lo)) out.push(`${id}: the static shell is not below the usual boot for ${g.id}`);
for (const me of d.measures) for (const g of me.groups) for (const s of d.series) if (g[s.id].lo > g[s.id].hi) out.push(`${me.id}: ${g.id} ${s.id} has lo above hi`);
if (!/Worker bridge, desktop/.test(body)) out.push('worker table missing');
if (d.measures.some((x) => !new RegExp(esc(x.label)).test(body))) out.push('a measure table is missing');
return out;
}
export function mount(root, d) {
const m = metrics(d), stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const q = (s) => root.querySelector(s), S = { mode: 'bytes' };
let narrow = stage.getBoundingClientRect().width < 520;
const render = () => { stage.innerHTML = chartSvg(d, m, S.mode, narrow); q('[data-ctl=readout]').textContent = readoutFor(m, S.mode); };
tablist(q('[role="tablist"]'), (mode) => { S.mode = mode; render(); });
if (typeof ResizeObserver === 'function') new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; render(); } }).observe(stage);
render();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="blazier-boot"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export const interactiveStage = false;
