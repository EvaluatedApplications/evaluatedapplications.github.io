import { hasDom } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
export function metrics(d) {
const groups = d.groups;
const total = groups.reduce((a, g) => a + g.count, 0);
const squares = groups.flatMap((g) => g.operations.map((o) => ({ group: g.id, name: o.name })));
const g = (id) => groups.find((x) => x.id === id).count;
return { groups, total, squares, commuting: g('joins-axes') + g('joins-off'), joinsAxes: g('joins-axes'), joinsOff: g('joins-off'), linear: g('linear'), nonlinear: g('nonlinear'), declare: g('declare'), probeBacked: d.probeBacked };
}
export function limitFacts(m) {
const on = m.groups.find((x) => x.id === 'joins-axes').operations, n = (how) => on.filter((o) => o.how === how).length;
return { members: m.total, commuting: m.commuting, joinsAxes: m.joinsAxes, joinsOff: m.joinsOff, linearBreak: m.linear, nonlinear: m.nonlinear, needDeclaration: m.declare, probeBacked: m.probeBacked,
axesKernel: n('kernel operation'), axesSums: n('store sum'), axesReads: n('read through a read set') };
}
function headlineHtml(m) {
return `<b class="cm-num">${m.commuting} of ${m.total}</b> operations commute with every generator of the group. ${m.joinsAxes} of them join the kernel&#39;s axes.`;
}
export function staticSvg(d, opts) {
const m = metrics(d), narrow = opts && opts.narrow, cols = narrow ? 6 : 12, s = narrow ? 44 : 52, gap = 8;
const rows = Math.ceil(m.squares.length / cols), W = cols * (s + gap) - gap, H = rows * (s + gap) - gap;
const label = (id) => m.groups.find((x) => x.id === id).label;
const sq = m.squares.map((q, i) => {
const x = (i % cols) * (s + gap), y = Math.floor(i / cols) * (s + gap);
return `<rect class="cm-sq cm-g-${q.group}" data-class="${q.group}" x="${x}" y="${y}" width="${s}" height="${s}" rx="6"><title>${esc(q.name)}: ${esc(label(q.group))}</title></rect>`;
}).join('');
return `<svg class="dia-plan cm-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="cx-t cx-d"><title id="cx-t">${m.total} operations, by how they commute with the symmetry group</title><desc id="cx-d">${m.commuting} of ${m.total} commute with every generator; ${m.joinsAxes} of them join the kernel's axes. ${m.groups.map((g) => `${g.label}: ${g.count}.`).join(' ')}</desc>${sq}</svg>`;
}
export function controlsHtml(id, m) {
const chips = m.groups.map((g) => `<button type="button" class="cm-chip cm-c-${g.id}" data-class="${g.id}" aria-pressed="false"><span class="cm-n">${g.count}</span><span class="cm-l">${esc(g.label)}</span></button>`).join('');
return {
tabs: `<p class="cm-head">${headlineHtml(m)}</p>`,
controls: `<div class="cm-chips dia-ui" hidden role="group" aria-label="Show the operations of one group">${chips}</div>
<div class="cm-panel dia-ui" hidden data-ctl="panel" role="region" aria-live="polite" aria-label="Operations in the chosen group"><p class="muted">Choose a group to list its operations.</p></div>`,
};
}
function opsTable(g) {
return `<div class="tbl-wrap"><table class="tbl"><caption>${esc(g.label)}: ${g.count} operations. ${esc(g.meaning)}</caption><thead><tr><th scope="col">Operation</th><th scope="col">Kind</th><th scope="col">Face</th><th scope="col">What the record says</th></tr></thead><tbody>${g.operations.map((o) => `<tr><th scope="row"><code>${esc(o.name)}</code></th><td>${esc(o.how)}</td><td>${esc(o.face)}</td><td>${esc(o.note)}</td></tr>`).join('')}</tbody></table></div>`;
}
export function tablesHtml(d, m) {
const gens = `<div class="tbl-wrap"><table class="tbl"><caption>The nine generators: each probe row moves the store by one of them and compares</caption><thead><tr><th scope="col">Generator</th><th scope="col">Moves the store by</th><th scope="col" class="num-h">Probe rows that break it, of ${d.probeRows}</th></tr></thead><tbody>${d.generators.map((g) => `<tr><th scope="row"><code>${esc(g.id)}</code></th><td>${esc(g.moves)}</td><td class="num">${g.breaks}</td></tr>`).join('')}</tbody></table></div>`;
const ps = Object.entries(d.probeSummary);
const probes = `<div class="tbl-wrap"><table class="tbl"><caption>The ${d.probeRows} probe rows by outcome (an operation can have more than one row)</caption><thead><tr><th scope="col">Outcome</th><th scope="col" class="num-h">Rows</th></tr></thead><tbody>${ps.map(([k, v]) => `<tr><th scope="row">${esc(k)}</th><td class="num">${v}</td></tr>`).join('')}</tbody></table></div>`;
return `<details class="dia-data"><summary>Every operation, with what the record says</summary>
${m.groups.map((g) => `<h3 class="sub-h">${esc(g.label)}</h3>${opsTable(g)}`).join('')}
<h3 class="sub-h">The generators and the probe rows</h3>
<p class="muted">${d.probeBacked} of the ${d.members} operations are checked by a probe run. The other ${d.notProbed} carry their reason in the record.</p>${gens}${probes}
</details>`;
}
export function verify(d, body) {
const m = metrics(d), out = [];
if (m.total !== d.members) out.push(`groups add to ${m.total}, record says ${d.members}`);
if (m.commuting !== d.commuting) out.push('commuting count differs from the record');
m.groups.forEach((g) => { if (g.operations.length !== g.count) out.push('operation list differs from the count for ' + g.id); });
if (d.probeBacked + d.notProbed !== d.members) out.push('probe-backed and not-probed do not add to the members');
const squares = (body.match(/class="cm-sq /g) || []).length;
if (squares !== 0 && squares !== m.total) out.push(`${squares} squares for ${m.total} operations`);
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
if (!sel) { panel.append(el('p', 'muted', 'Choose a group to list its operations.')); return; }
const g = m.groups.find((x) => x.id === sel);
panel.append(el('h3', null, `${g.label}: ${g.count}`), el('p', 'muted', g.meaning));
const ul = el('ul', 'cm-ops');
g.operations.forEach((o) => { const li = el('li'); li.append(el('code', null, o.name), el('span', null, ' ' + o.how + (o.note ? '. ' + o.note : ''))); ul.append(li); });
panel.append(ul);
}
chips.forEach((c) => c.addEventListener('click', () => choose(c.dataset.class)));
draw();
if (typeof ResizeObserver === 'function') new ResizeObserver(draw).observe(stage);
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="commutation-map"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export const interactiveStage = false;
