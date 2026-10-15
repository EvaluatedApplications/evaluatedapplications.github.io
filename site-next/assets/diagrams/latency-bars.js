import { hasDom, prefersReducedMotion } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const f2 = (n) => Number(n).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const FLOOR = 0.005;
export function outcome(row, d, rivalIdx) {
const a = Math.max(row.values[rivalIdx], FLOOR), b = Math.max(row.values[d.focus], FLOOR), t = d.tieBelow ?? 1.15;
return a / b >= t ? 'win' : b / a >= t ? 'loss' : 'tie';
}
export function metrics(d) {
const rivals = d.columns.map((c, i) => ({ c, i })).filter((x) => x.i !== d.focus);
const per = {};
rivals.forEach(({ i }) => { const o = d.rows.map((r) => outcome(r, d, i)); per[i] = { wins: o.filter((x) => x === 'win').length, ties: o.filter((x) => x === 'tie').length, losses: o.filter((x) => x === 'loss').length }; });
return { rows: d.rows.length, rivals, per, focus: d.columns[d.focus] };
}
export function summaryFor(d, m, rivalIdx) {
const p = m.per[rivalIdx];
return `${m.focus} against ${d.columns[rivalIdx]}: faster on ${p.wins} of ${m.rows} queries, ${p.ties === 0 ? 'tied on none' : `tied on ${p.ties}`} and slower on ${p.losses}. A difference under ${d.tieBelow ?? 1.15}x counts as a tie. Bars use a logarithmic scale, and shorter is faster.`;
}
export function rowText(d, r, rivalIdx) {
const vals = d.columns.map((c, i) => `${c} ${f2(r.values[i])} ms`).join(', ');
return `${r.label}: ${vals}. ${r.verdict}.`;
}
function order(d, rivalIdx, mode) {
const idx = d.rows.map((_, i) => i);
if (mode === 'gap') idx.sort((a, b) => Math.log10(Math.max(d.rows[b].values[rivalIdx], FLOOR) / Math.max(d.rows[b].values[d.focus], FLOOR)) - Math.log10(Math.max(d.rows[a].values[rivalIdx], FLOOR) / Math.max(d.rows[a].values[d.focus], FLOOR)));
return idx;
}
export function chartSvg(d, { rival = d.rival, mode = 'table', narrow = false, width = 640, active = -1, id = 'lb' } = {}) {
const rows = order(d, rival, mode);
const all = d.rows.flatMap((r) => [r.values[rival], r.values[d.focus]]).map((v) => Math.max(v, FLOOR));
const lo = Math.log10(Math.min(...all)), hi = Math.log10(Math.max(...all));
const left = narrow ? 8 : 210, right = narrow ? 64 : 76, rowH = narrow ? 44 : 24, top = 6;
const span = Math.max(40, width - left - right);
const x = (v) => ((Math.log10(Math.max(v, FLOOR)) - lo) / (hi - lo || 1)) * span;
const H = top + rows.length * rowH + 20;
const parts = rows.map((ri, k) => {
const r = d.rows[ri], y = top + k * rowH, o = outcome(r, d, rival);
const a = Math.max(2, x(r.values[rival])), b = Math.max(2, x(r.values[d.focus]));
const ly = narrow ? y + 11 : y + 13, by = narrow ? y + 16 : y + 2;
return `<g class="lb-row${ri === active ? ' on' : ''}" data-i="${ri}"><rect class="hit" x="0" y="${y}" width="${width}" height="${rowH}"/>
<text class="lab" x="${narrow ? left : left - 8}" y="${ly}" text-anchor="${narrow ? 'start' : 'end'}">${esc(r.label)}</text>
<rect class="bar rival${o === 'loss' ? ' ahead' : ''}" x="${left}" y="${by}" width="${a.toFixed(1)}" height="7" rx="2"/>
<rect class="bar focus" x="${left}" y="${by + 9}" width="${b.toFixed(1)}" height="7" rx="2"/>
<text class="val" x="${left + Math.max(a, b) + 6}" y="${by + 13}">${f2(r.values[d.focus])} ms</text></g>`;
}).join('');
return `<svg class="lb-svg" viewBox="0 0 ${width} ${H}" role="img" aria-labelledby="${id}-t"><title id="${id}-t">${esc(d.caption)}, drawn on a logarithmic scale. ${esc(d.columns[d.focus])} against ${esc(d.columns[rival])}.</title>
<line class="axis" x1="${left}" y1="${top}" x2="${left}" y2="${H - 18}"/>${parts}<text class="foot" x="${left}" y="${H - 4}">logarithmic scale, shorter is faster</text></svg>`;
}
export function staticSvg(d) { return chartSvg(d, { id: 'lb-' + d.id }); }
export function controlsHtml(id, m, d) {
const rivals = m.rivals.map(({ c, i }) => `<button type="button" class="chip" data-rival="${i}" aria-pressed="${i === d.rival}">${esc(c)}</button>`).join('');
return {
tabs: '',
controls: `<p class="lb-key"><span class="sw sw-rival"></span><span data-ctl="rivalname">${esc(d.columns[d.rival])}</span><span class="sw sw-focus"></span>${esc(m.focus)}<span class="sw sw-ahead"></span>where the other engine is ahead</p>
<div class="dia-controls dia-ui" hidden><span class="dia-seg" role="group" aria-label="Compare ${esc(m.focus)} with">${rivals}</span>
<span class="dia-seg" role="group" aria-label="Order"><button type="button" class="chip" data-order="table" aria-pressed="true">Table order</button><button type="button" class="chip" data-order="gap" aria-pressed="false">Biggest gap first</button></span></div>
<p class="dia-hint dia-ui" hidden>Point at a bar, or focus the chart and press the up and down arrows, to read a row.</p>
<p class="dia-readout dia-ui" data-ctl="readout" role="status" hidden>${esc(summaryFor(d, m, d.rival))}</p>`,
};
}
export function tablesHtml(d, m) {
const head = ['Query', ...d.columns, 'Result'];
const rows = d.rows.map((r) => `<tr><th scope="row">${esc(r.label)}</th>${r.values.map((v, i) => `<td class="num${i === d.focus && r.state === 'win' ? ' is-win' : i === d.rival && r.state === 'loss' ? ' is-rival' : ''}">${f2(v)}</td>`).join('')}<td class="${r.state === 'loss' ? 'is-loss' : ''}">${esc(r.verdict)}</td></tr>`).join('');
return `<div class="tbl-wrap"><table class="tbl"><caption>${esc(d.caption)}</caption><thead><tr>${head.map((h, i) => `<th scope="col"${i > 0 && i < head.length - 1 ? ' class="num"' : ''}>${esc(h)}</th>`).join('')}</tr></thead><tbody>${rows}</tbody></table></div>`;
}
export function verify(d, body) {
const out = [], m = metrics(d);
const trs = (body.match(/<tbody>[\s\S]*?<\/tbody>/) || [''])[0].split('<tr>').length - 1;
if (trs !== d.rows.length) out.push(`table has ${trs} rows for ${d.rows.length} data rows`);
if (!/<svg class="lb-svg" [^>]*role="img"/.test(body)) out.push('chart has no accessible name');
const p = m.per[d.rival];
if (p.wins + p.ties + p.losses !== d.rows.length) out.push('outcome counts do not add up');
return out;
}
export const interactiveStage = true;
export function mount(root, d) {
const m = metrics(d), reduced = prefersReducedMotion();
const stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const readout = root.querySelector('[data-ctl=readout]');
const S = { rival: d.rival, mode: 'table', active: -1 };
function render() {
const w = Math.round(stage.getBoundingClientRect().width) || 640, narrow = w < 520;
stage.innerHTML = chartSvg(d, { rival: S.rival, mode: S.mode, narrow, width: Math.max(320, w), active: S.active, id: 'lb-' + d.id });
stage.classList.toggle('lb-narrow', narrow);
if (!reduced) stage.classList.add('lb-in');
}
function say() { readout.textContent = S.active >= 0 ? rowText(d, d.rows[S.active], S.rival) : summaryFor(d, m, S.rival); }
const rows = () => [...stage.querySelectorAll('.lb-row')].map((g) => Number(g.dataset.i));
function setActive(i) { S.active = i; stage.querySelectorAll('.lb-row').forEach((g) => g.classList.toggle('on', Number(g.dataset.i) === i)); say(); }
stage.addEventListener('pointermove', (e) => { const g = e.target.closest && e.target.closest('.lb-row'); if (g) setActive(Number(g.dataset.i)); });
stage.addEventListener('pointerleave', () => setActive(-1));
stage.addEventListener('keydown', (e) => {
const list = rows(), pos = list.indexOf(S.active); let used = true;
if (e.key === 'ArrowDown') setActive(list[Math.min(list.length - 1, pos + 1)]);
else if (e.key === 'ArrowUp') setActive(pos <= 0 ? list[0] : list[pos - 1]);
else if (e.key === 'Home') setActive(list[0]);
else if (e.key === 'End') setActive(list[list.length - 1]);
else if (e.key === 'Escape') setActive(-1);
else used = false;
if (used) e.preventDefault();
});
root.querySelectorAll('[data-rival]').forEach((b) => b.addEventListener('click', () => {
S.rival = Number(b.dataset.rival); root.querySelector('[data-ctl=rivalname]').textContent = d.columns[S.rival]; root.querySelectorAll('[data-rival]').forEach((x) => x.setAttribute('aria-pressed', String(x === b))); render(); say();
}));
root.querySelectorAll('[data-order]').forEach((b) => b.addEventListener('click', () => {
S.mode = b.dataset.order; root.querySelectorAll('[data-order]').forEach((x) => x.setAttribute('aria-pressed', String(x === b))); render(); say();
}));
new ResizeObserver(() => render()).observe(stage);
render(); say();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="latency-bars"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
