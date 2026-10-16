import { hasDom } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const LABEL = { kernel: 'Joins one kernel', 'pass-per-term': 'One pass per term', write: 'Writes the store directly' };
const SHORT = { kernel: 'Kernel', 'pass-per-term': 'Pass per term', write: 'Write' };
const code = (s) => esc(s).replace(/`([^`]+)`/g, '<code>$1</code>');
export function metrics(d) {
const classes = d.classes.map((c) => ({ ...c, label: LABEL[c.id] || c.id, short: SHORT[c.id] || c.id }));
const total = classes.reduce((a, c) => a + c.count, 0);
const squares = classes.flatMap((c, ci) => c.operations.map((o) => ({ class: c.id, ci, name: o.name })));
return { classes, total, squares, kernel: classes[0].count, reads: d.reads.length, bricks: (d.construction || []).length };
}
const TENS = ['', '', 'twenty', 'thirty', 'forty', 'fifty', 'sixty', 'seventy', 'eighty', 'ninety'];
const ONES = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen'];
const words = (n) => (n < 20 ? ONES[n] : TENS[Math.floor(n / 10)] + (n % 10 ? '-' + ONES[n % 10] : ''));
export function limitFacts(m) { return { operators: m.total, kernelOps: m.kernel, bricks: m.bricks, reads: m.reads, readsWord: words(m.reads) }; }
function headlineHtml(d) {
const t = d.headline.text, mt = t.match(/^(\d+ of \d+)(.*)$/);
return mt ? `<b class="cm-num">${esc(mt[1])}</b>${esc(mt[2])}` : esc(t);
}
export function staticSvg(d, opts) {
const m = metrics(d), narrow = opts && opts.narrow, cols = narrow ? 6 : 12, s = narrow ? 44 : 52, gap = 8;
const rows = Math.ceil(m.squares.length / cols), W = cols * (s + gap) - gap, H = rows * (s + gap) - gap;
const sq = m.squares.map((q, i) => {
const x = (i % cols) * (s + gap), y = Math.floor(i / cols) * (s + gap);
return `<rect class="cm-sq cm-${q.class}" data-class="${q.class}" x="${x}" y="${y}" width="${s}" height="${s}" rx="6"><title>${esc(q.name)}: ${esc(LABEL[q.class] || q.class)}</title></rect>`;
}).join('');
return `<svg class="dia-plan cm-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="cm-t cm-d"><title id="cm-t">${m.total} operators that transform the store, by class</title><desc id="cm-d">${esc(d.headline.text)} ${m.classes.map((c) => `${c.short}: ${c.count}.`).join(' ')}</desc>${sq}</svg>`;
}
export function controlsHtml(id, m, d) {
const chips = m.classes.map((c, i) => `<button type="button" class="cm-chip cm-c-${c.id}" data-class="${c.id}" aria-pressed="false"><span class="cm-n">${c.count}</span><span class="cm-l">${esc(c.label)}</span></button>`).join('');
return {
tabs: `<p class="cm-head">${headlineHtml(d)}</p>`,
controls: `<div class="cm-chips dia-ui" hidden role="group" aria-label="Show the operators of one class">${chips}</div>
<div class="cm-panel dia-ui" hidden data-ctl="panel" role="region" aria-live="polite" aria-label="Operators in the chosen class"><p class="muted">${esc(d.headline.beside)} Choose a class to list its operators.</p></div>
<div class="cm-reads"><p><b class="cm-num2">${m.reads} reads</b> ${esc(d.headline.reads)}</p></div>`,
};
}
function opsTable(c, withClass) {
return `<div class="tbl-wrap"><table class="tbl"><caption>${esc(c.label)}: ${c.count} ${c.count === 1 ? 'operation' : 'operations'}. ${esc(c.meaning || '')}</caption><thead><tr><th scope="col">Operation</th><th scope="col">Face</th><th scope="col">Rung</th><th scope="col">What was checked</th></tr></thead><tbody>${c.operations.map((o) => `<tr><th scope="row"><code>${esc(o.name)}</code></th><td>${esc(o.face)}</td><td>${esc(o.rung || '')}</td><td>${esc(o.evidence)}</td></tr>`).join('')}</tbody></table></div>`;
}
export function tablesHtml(d, m) {
const reads = { id: 'read', label: 'Reads', count: d.reads.length, meaning: d.readsMeaning, operations: d.reads };
return `<details class="dia-data"><summary>Every operation, with what was checked</summary>
${m.classes.map((c) => `<h3 class="sub-h">${esc(c.label)}</h3>${opsTable(c)}`).join('')}
<h3 class="sub-h">Reads (not part of the score)</h3><p class="muted">${esc(d.headline.why)}</p>${opsTable(reads)}
</details>`;
}
export function verify(d, body) {
const m = metrics(d), out = [];
const s = d.summary;
if (m.total !== s.operatorCount) out.push(`classes add to ${m.total}, record says ${s.operatorCount}`);
if (m.kernel !== s.kernelCount) out.push('kernel count differs from the record');
m.classes.forEach((c) => { if (c.count !== s.classCounts[c.id]) out.push(`class ${c.id} has ${c.count}, record says ${s.classCounts[c.id]}`); if (c.operations.length !== c.count) out.push('operation list differs from the count for ' + c.id); });
if (d.reads.length !== s.readCount) out.push('read count differs from the record');
if (!d.headline.text.startsWith(`${s.kernelCount} of ${s.operatorCount} `)) out.push('headline does not state the recorded counts');
const squares = (body.match(/class="cm-sq /g) || []).length;
if (squares !== 0 && squares !== m.total) out.push(`${squares} squares for ${m.total} operators`);
return out;
}
export function mount(root, d) {
const m = metrics(d), stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const panel = root.querySelector('[data-ctl=panel]'), chips = [...root.querySelectorAll('.cm-chip')];
let sel = null, narrow = null;
function draw() {
const n = stage.clientWidth < 560;
if (n === narrow) return;
narrow = n; stage.innerHTML = staticSvg(d, { narrow: n });
stage.querySelectorAll('.cm-sq').forEach((q) => q.addEventListener('click', () => choose(q.dataset.class)));
paint();
}
function paint() {
stage.querySelectorAll('.cm-sq').forEach((q) => q.classList.toggle('cm-dim', !!sel && q.dataset.class !== sel));
chips.forEach((c) => c.setAttribute('aria-pressed', String(c.dataset.class === sel)));
}
function choose(id) {
sel = sel === id ? null : id; paint(); panel.textContent = '';
const el = (tag, cls, txt) => { const e = document.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; };
if (!sel) { panel.append(el('p', 'muted', d.headline.beside + ' Choose a class to list its operators.')); return; }
const c = m.classes.find((x) => x.id === sel);
panel.append(el('h3', null, `${c.label}: ${c.count}`), el('p', 'muted', c.meaning));
const ul = el('ul', 'cm-ops');
c.operations.forEach((o) => { const li = el('li'); li.append(el('code', null, o.name), el('span', null, ' ' + o.evidence)); ul.append(li); });
panel.append(ul);
}
chips.forEach((c) => c.addEventListener('click', () => choose(c.dataset.class)));
draw();
if (typeof ResizeObserver === 'function') new ResizeObserver(draw).observe(stage);
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="coverage-meter"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export const interactiveStage = false;
