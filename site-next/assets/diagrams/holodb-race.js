import { hasDom, prefersReducedMotion } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const FLOOR = 0.005;
const LIMIT = 1.2;
export const ms = (v) => {
if (v >= 1000) return Number(v).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
return Number(v).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
};
const MODES = [{ id: 'warm', label: 'Warm', note: 'median of three runs' }, { id: 'cold', label: 'Cold', note: 'first run after the file cache was cleared' }];
export function cell(d, qi, ai, mode) {
const q = d.queries[qi], v = q[mode][ai], r = q[mode + '2'][ai];
const lo = Math.max(Math.min(v, r), FLOOR), hi = Math.max(v, r, FLOOR);
return { v, r, lo, hi, noisy: hi / lo > LIMIT };
}
export function verdict(a, b) {
if (b.lo > LIMIT * a.hi) return 'ahead';
if (a.lo > LIMIT * b.hi) return 'behind';
if (a.hi <= LIMIT * b.lo && b.hi <= LIMIT * a.lo) return 'equal';
return 'unsure';
}
export function counts(d, mode) {
const out = {};
for (const ai of [0, 1]) {
out[d.arms[ai].id] = {};
for (const bi of [2, 3]) {
const t = { ahead: 0, equal: 0, behind: 0, unsure: 0, n: 0 };
d.queries.forEach((_, qi) => { t[verdict(cell(d, qi, ai, mode), cell(d, qi, bi, mode))]++; t.n++; });
t.compared = t.n - t.unsure;
out[d.arms[ai].id][d.arms[bi].id] = t;
}
}
return out;
}
export function behindAt(d, qi, ai, mode) {
return [2, 3].some((bi) => verdict(cell(d, qi, ai, mode), cell(d, qi, bi, mode)) === 'behind');
}
const JOINS = ['revenue by segment (JOIN)', 'enterprise revenue (JOIN)', 'revenue by signup cohort (JOIN)', 'revenue by segment and status (JOIN)'];
const f2 = (v) => Number(v).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const rangeOf = (vals) => { const lo = Math.min(...vals), hi = Math.max(...vals); return `${f2(lo)} to ${f2(hi)}`; };
const qIndex = (d, label) => { const i = d.queries.findIndex((q) => q.label === label.replace(' and status', ' & status')); if (i < 0) throw new Error('query not found: ' + label); return i; };
export function facts(d) {
const arm = (id) => d.arms.findIndex((a) => a.id === id);
const R = arm('restored'), O = arm('oob'), U = arm('duck'), S = arm('sql');
const w = (label, ai) => d.queries[qIndex(d, label)].warm[ai];
const j = (ai) => JOINS.map((l) => w(l, ai));
const c = counts(d, 'warm');
return {
rows: d.rows.toLocaleString('en-GB'), queries: String(d.queries.length),
totalRestored: f2(w('total invoiced', R)), totalDuck: f2(w('total invoiced', U)), totalSql: f2(w('total invoiced', S)),
joinsRestored: rangeOf(j(R)), joinsSql: rangeOf(j(S)), joinsDuck: rangeOf(j(U)),
restoredDuckAhead: String(c.restored.duck.ahead), restoredSqlAhead: String(c.restored.sql.ahead),
oobDuckAhead: String(c.oob.duck.ahead), oobDuckBehind: String(c.oob.duck.behind), oobDuckUnsure: String(c.oob.duck.unsure),
oobSqlAhead: String(c.oob.sql.ahead), oobSqlBehind: String(c.oob.sql.behind),
topCustomersRestored: f2(w('top customers by revenue', R)), topNamedRestored: f2(w('top named customers (JOIN)', R)),
};
}
export const limitFacts = (m) => m.facts;
export function metrics(d) {
const per = {};
for (const m of MODES) per[m.id] = { counts: counts(d, m.id), behind: d.queries.map((_, qi) => [0, 1].map((ai) => behindAt(d, qi, ai, m.id))), noisy: d.queries.map((_, qi) => d.arms.map((__, ai) => cell(d, qi, ai, m.id).noisy)) };
return { rows: d.queries.length, arms: d.arms.length, per, facts: facts(d) };
}
export function summaryFor(d, m, mode) {
const c = m.per[mode].counts, r = c.restored, o = c.oob, M = MODES.find((x) => x.id === mode);
const part = (x) => `ahead on ${x.ahead}, equal on ${x.equal}, behind on ${x.behind}${x.unsure ? `, too close to call on ${x.unsure}` : ''}`;
return `${M.label} (${M.note}). HoloDb restored against tuned DuckDB: ${part(r.duck)}; against tuned SQL Server: ${part(r.sql)}. HoloDb out of the box against tuned DuckDB: ${part(o.duck)}; against tuned SQL Server: ${part(o.sql)}. A difference of 20 percent or less is equal. Point at a row to read it.`;
}
export function rowText(d, qi, mode) {
const q = d.queries[qi];
const vals = d.arms.map((a, ai) => { const c = cell(d, qi, ai, mode); return `${a.label} ${ms(c.v)} ms${c.noisy ? ' (the repeat run differed by more than 20 percent)' : ''}`; }).join('; ');
return `${q.label}: ${vals}.`;
}
const CLASS = ['restored', 'oob', 'duck', 'sql'];
export function chartSvg(d, { mode = 'warm', narrow = false, width = 700, active = -1, id = 'hr' } = {}) {
const m = metrics(d);
const all = d.queries.flatMap((_, qi) => d.arms.map((__, ai) => Math.max(cell(d, qi, ai, mode).v, FLOOR)));
const lo = Math.floor(Math.log10(Math.min(...all))), hi = Math.ceil(Math.log10(Math.max(...all)));
const left = narrow ? 22 : 190, right = narrow ? 22 : 24, bh = 7, gap = 3, top = 18;
const rowH = narrow ? 14 + 4 * (bh + gap) + 8 : 4 * (bh + gap) + 10;
const span = Math.max(60, width - left - right);
const x = (v) => ((Math.log10(Math.max(v, FLOOR)) - lo) / (hi - lo || 1)) * span;
const H = top + d.queries.length * rowH + 8;
const grid = [];
for (let e = lo; e <= hi; e++) {
const gx = left + ((e - lo) / (hi - lo || 1)) * span, val = 10 ** e;
grid.push(`<line class="gl" x1="${gx.toFixed(1)}" y1="${top - 4}" x2="${gx.toFixed(1)}" y2="${H - 4}"/><text class="tk" x="${gx.toFixed(1)}" y="10" text-anchor="middle">${val >= 1000 ? val / 1000 + ' s' : (val < 1 ? val : val) + ' ms'}</text>`);
}
const rows = d.queries.map((q, qi) => {
const y = top + qi * rowH, on = qi === active;
const bars = d.arms.map((a, ai) => {
const c = cell(d, qi, ai, mode), w = Math.max(2, x(c.v));
const behind = ai < 2 && m.per[mode].behind[qi][ai];
const by = y + (narrow ? 14 : 4) + ai * (bh + gap);
return `<rect class="bar ${CLASS[ai]}${behind ? ' behind' : ''}${c.noisy ? ' noisy' : ''}" x="${left}" y="${by}" width="${w.toFixed(1)}" height="${bh}" rx="2"/>${on ? `<text class="val" x="${(left + w + 5).toFixed(1)}" y="${by + bh - 0.5}">${ms(c.v)} ms</text>` : ''}`;
}).join('');
return `<g class="hr-row${on ? ' on' : ''}" data-i="${qi}"><rect class="hit" x="0" y="${y}" width="${width}" height="${rowH}"/><text class="lab" x="${narrow ? left : left - 8}" y="${narrow ? y + 10 : y + 20}" text-anchor="${narrow ? 'start' : 'end'}">${esc(q.label)}</text>${bars}</g>`;
}).join('');
return `<svg class="hr-svg" viewBox="0 0 ${width} ${H}" role="img" aria-labelledby="${id}-t"><title id="${id}-t">${esc(d.caption)} Logarithmic scale, shorter is faster. ${MODES.find((x) => x.id === mode).label}.</title>${grid.join('')}${rows}</svg>`;
}
export function staticSvg(d) { return chartSvg(d, { id: 'hr-' + d.id }); }
export function controlsHtml(id, m, d) {
const modes = MODES.map((x) => `<button type="button" class="chip" data-mode="${x.id}" aria-pressed="${x.id === 'warm'}">${x.label}</button>`).join('');
const key = d.arms.map((a, ai) => `<span class="hr-k"><i class="sw sw-${CLASS[ai]}"></i>${esc(a.label)}</span>`).join('');
return {
tabs: '',
controls: `<p class="hr-key">${key}<span class="hr-k"><i class="sw sw-behind"></i>HoloDb bar in amber: another engine is more than 20 percent faster</span></p>
<div class="dia-controls dia-ui" hidden><span class="dia-seg" role="group" aria-label="Warm or cold">${modes}</span></div>
<p class="dia-hint dia-ui" hidden>Point at a row, or focus the chart and press the up and down arrows, to read the milliseconds. Shorter is faster.</p>
<p class="dia-readout dia-ui" data-ctl="readout" role="status" hidden>${esc(summaryFor(d, m, 'warm'))}</p>`,
};
}
function rawTable(d, m, mode) {
const M = MODES.find((x) => x.id === mode);
const head = ['Query', ...d.arms.map((a) => a.label)];
const body = d.queries.map((q, qi) => `<tr><th scope="row">${esc(q.label)}</th>${d.arms.map((a, ai) => {
const c = cell(d, qi, ai, mode), behind = ai < 2 && m.per[mode].behind[qi][ai];
return `<td class="num${behind ? ' is-behind' : ''}">${ms(c.v)}${c.noisy ? '<span class="noisy-mark" title="the repeat run differed by more than 20 percent">~</span>' : ''}</td>`;
}).join('')}</tr>`).join('');
return `<div class="tbl-wrap"><table class="tbl"><caption>${esc(M.label)} milliseconds, ${esc(M.note)}</caption><thead><tr>${head.map((h, i) => `<th scope="col"${i ? ' class="num"' : ''}>${esc(h)}</th>`).join('')}</tr></thead><tbody>${body}</tbody></table></div>`;
}
export function tablesHtml(d, m) {
return `<details class="dia-data"><summary>Raw numbers: every cell, warm and cold</summary>${rawTable(d, m, 'warm')}${rawTable(d, m, 'cold')}<p class="muted">Shaded cell: a HoloDb figure with another engine more than 20 percent faster. A tilde marks a figure whose repeat run differed by more than 20 percent. Load, size and open times are in the method section.</p></details>`;
}
export function verify(d, body) {
const out = [];
const trs = (body.match(/<tbody>[\s\S]*?<\/tbody>/g) || []).reduce((a, t) => a + t.split('<tr>').length - 1, 0);
if (trs !== 2 * d.queries.length) out.push(`tables have ${trs} rows for ${2 * d.queries.length} cells rows`);
if (!/<svg class="hr-svg" [^>]*role="img"/.test(body)) out.push('chart has no accessible name');
for (const q of d.queries) for (const k of ['warm', 'warm2', 'cold', 'cold2']) if (q[k].length !== d.arms.length || q[k].some((v) => !(v >= 0))) out.push(`${q.label}: bad ${k}`);
return out;
}
export const interactiveStage = true;
export function mount(root, d) {
const m = metrics(d), reduced = prefersReducedMotion();
const stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const readout = root.querySelector('[data-ctl=readout]');
const S = { mode: 'warm', active: -1 };
function render() {
const w = Math.round(stage.getBoundingClientRect().width) || 700, narrow = w < 560;
stage.innerHTML = chartSvg(d, { mode: S.mode, narrow, width: Math.max(320, w), active: S.active, id: 'hr-' + d.id });
stage.classList.toggle('hr-narrow', narrow);
if (!reduced) stage.classList.add('hr-in');
}
const say = () => { readout.textContent = S.active >= 0 ? rowText(d, S.active, S.mode) : summaryFor(d, m, S.mode); };
const set = (i) => { if (i === S.active) return; S.active = i; render(); say(); };
stage.addEventListener('pointermove', (e) => { const g = e.target.closest && e.target.closest('.hr-row'); if (g) set(Number(g.dataset.i)); });
stage.addEventListener('click', (e) => { const g = e.target.closest && e.target.closest('.hr-row'); if (g) set(Number(g.dataset.i)); });
stage.addEventListener('pointerleave', (e) => { if (e.pointerType === 'mouse') set(-1); });
stage.addEventListener('keydown', (e) => {
const n = d.queries.length; let used = true;
if (e.key === 'ArrowDown') set(Math.min(n - 1, S.active + 1));
else if (e.key === 'ArrowUp') set(S.active <= 0 ? 0 : S.active - 1);
else if (e.key === 'Home') set(0);
else if (e.key === 'End') set(n - 1);
else if (e.key === 'Escape') set(-1);
else used = false;
if (used) e.preventDefault();
});
root.querySelectorAll('[data-mode]').forEach((b) => b.addEventListener('click', () => {
S.mode = b.dataset.mode; root.querySelectorAll('[data-mode]').forEach((x) => x.setAttribute('aria-pressed', String(x === b))); S.active = -1; render(); say();
}));
if (typeof ResizeObserver === 'function') { let last = stage.getBoundingClientRect().width; new ResizeObserver(() => { const w = stage.getBoundingClientRect().width; if (Math.abs(w - last) > 1) { last = w; render(); } }).observe(stage); }
render(); say();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="holodb-race"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
