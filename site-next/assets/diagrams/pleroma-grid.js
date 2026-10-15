import { hasDom } from './core.js';
import { FACES, KIND, GLOSS, metrics as mapMetrics, tablesHtml as mapTables } from './pleroma-map.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const kindClass = (c) => (c.status === 'COVERED' ? 'ok' : c.status === 'READ-TIME' ? 'warn' : 'other');
export function metrics(d) { return mapMetrics(d); }
export function limitFacts(m) { return { cells: m.total - 1, rungs: m.rungs }; }
export const tablesHtml = mapTables;
export const interactiveStage = false;
function cellHtml(c, interactive, tab) {
const label = `${c.lid ? 'Lid' : `Rung ${c.rung}, ${c.face}`}: ${c.name}. ${KIND[c.status] || c.status}.`;
const inner = `<span class="pg-n">${esc(c.name)}</span><span class="pg-k"><i class="pg-dot" aria-hidden="true"></i><span class="pg-kt">${esc(KIND[c.status] || c.status)}</span></span>`;
const cls = `pg-cell ${c.lid ? 'lid' : 'f' + c.fi} k-${kindClass(c)}`;
return interactive
? `<button type="button" class="${cls}" data-id="${esc(c.id)}" aria-pressed="false" aria-label="${esc(label)}" tabindex="${tab ? 0 : -1}">${inner}</button>`
: `<div class="${cls}" data-id="${esc(c.id)}" role="listitem" aria-label="${esc(label)}">${inner}</div>`;
}
export function gridHtml(d, m, interactive) {
const by = Object.fromEntries(m.cells.map((c) => [c.id, c]));
const lid = m.cells[m.cells.length - 1];
const rows = [...d.rungs].reverse().map((r) => `<div class="pg-rung" role="presentation"><b>${r.n}</b><span>${esc(r.name.split(' (')[0])}</span></div>${FACES.map((f) => cellHtml(by[`R${r.n}_${f}`], interactive, false)).join('')}`).join('');
const heads = FACES.map((f, i) => `<div class="pg-face f${i}" role="presentation"><b>${f}</b><span>${esc((d.faceNotes || {})[f] || '')}</span></div>`).join('');
return `<div class="pg" data-static-html role="${interactive ? 'group' : 'list'}" aria-label="The Pleroma operation grid: ${d.rungs.length} rungs by ${FACES.length} faces, and the lid">
<div class="pg-corner" role="presentation"></div>${heads}
<div class="pg-lidrow" role="presentation"><div class="pg-rung" role="presentation"><b>Lid</b><span>read time</span></div>${cellHtml(lid, interactive, false)}</div>
${rows}</div>`;
}
export function staticSvg(d) { return gridHtml(d, metrics(d), false); }
export function controlsHtml() {
return {
tabs: '',
controls: `<div class="map-legend pg-legend dia-ui" hidden aria-label="Colour key: kind"><span class="lg st-ok">Checked: a public member checked against a brute-force rebuild</span><span class="lg st-warn">Read-time: a choice made once, on reading</span></div>
<p class="dia-hint dia-ui" hidden>Select a cell. Arrow keys move between cells, Enter selects, Escape clears.</p>
<div class="map-panel pg-panel dia-ui" hidden data-ctl="panel" role="region" aria-label="Selected cell" aria-live="polite"></div>`,
};
}
export function verify(d, body) {
const m = metrics(d), out = [];
const cells = (body.match(/class="pg-cell /g) || []).length;
if (cells !== m.total) out.push(`grid draws ${cells} cells for ${m.total}`);
const ids = new Set(); m.cells.forEach((c) => { if (ids.has(c.id)) out.push('duplicate cell id ' + c.id); ids.add(c.id); });
if (m.total !== d.totalCells + 1) out.push(`data says ${d.totalCells} cells plus the lid, found ${m.total}`);
if (m.total - 1 !== d.rungs.length * FACES.length) out.push('cells are not rungs times faces');
const unknown = m.cells.filter((c) => !KIND[c.status]); if (unknown.length) out.push('cells with a kind the grid does not name: ' + unknown.map((c) => c.id).join(', '));
const trs = ((body.match(/<tbody>[\s\S]*?<\/tbody>/) || [''])[0].match(/<tr>/g) || []).length;
if (trs !== m.total) out.push(`table has ${trs} rows for ${m.total} cells`);
return out;
}
export function mount(root, d) {
const m = metrics(d), CS = m.cells, byId = Object.fromEntries(CS.map((c) => [c.id, c]));
const stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
stage.innerHTML = gridHtml(d, m, true);
const panel = root.querySelector('[data-ctl=panel]');
const btn = (id) => stage.querySelector(`[data-id="${id}"]`);
const all = [...stage.querySelectorAll('.pg-cell')];
let sel = null, cur = null;
const el = (tag, cls, txt) => { const e = document.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; };
function show(c) {
panel.textContent = '';
if (!c) {
panel.append(el('h3', null, 'Pick a cell'), el('p', 'muted', `Select a cell in the grid. The panel shows its name, what it does, its API, its kind and the evidence. ${m.rungs} rungs by ${FACES.length} faces is ${m.total - 1} cells, plus the lid.`));
return;
}
panel.append(el('p', 'eyebrow', c.lid ? 'Lid' : `Rung ${c.rung} · ${c.rungName}`), el('h3', null, c.name));
const chips = el('div', 'map-chips');
chips.append(el('span', 'pill ' + (c.lid ? '' : 'f' + c.fi), c.lid ? 'Lid' : c.face), el('span', 'pill st-' + kindClass(c), KIND[c.status] || c.status));
panel.append(chips);
const dl = el('dl', 'map-dl');
[[c.lid ? 'About the lid' : 'Rung', c.rungCaption], ['What it does', c.description], ['API', c.api], ['Kind', GLOSS[c.status] || `Kind as listed: ${c.status}.`], ['Evidence', c.evidence]].forEach(([k, v]) => {
dl.append(el('dt', null, k));
if (k === 'API') { const dd = el('dd'); dd.append(el('code', null, v)); dl.append(dd); } else dl.append(el('dd', null, v));
});
panel.append(dl);
}
function setTab(id) { all.forEach((b) => { b.tabIndex = b.dataset.id === id ? 0 : -1; }); cur = id; }
function select(id, scroll) {
sel = id === sel ? null : id;
all.forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.id === sel)));
show(sel ? byId[sel] : null);
if (sel) setTab(sel);
if (sel && scroll && matchMedia('(max-width:900px)').matches) panel.scrollIntoView({ block: 'nearest' });
}
const pos = (c) => (c.lid ? { r: 0, f: 1 } : { r: 9 - c.rung, f: c.fi });
const at = (r, f) => CS.find((c) => { const p = pos(c); return p.r === r && p.f === f; });
function move(c, dr, df) {
const p = pos(c); let r = p.r + dr, f = p.f + df;
if (r < 0 || r > 8) return c;
if (r === 0) return CS[CS.length - 1];
f = (f + 3) % 3;
return at(r, f) || c;
}
stage.addEventListener('click', (e) => { const b = e.target.closest('.pg-cell'); if (b) select(b.dataset.id, true); });
stage.addEventListener('keydown', (e) => {
const b = e.target.closest('.pg-cell'); if (!b) return;
const c = byId[b.dataset.id]; let n = null;
if (e.key === 'ArrowLeft') n = move(c, 0, -1); else if (e.key === 'ArrowRight') n = move(c, 0, 1);
else if (e.key === 'ArrowUp') n = move(c, -1, 0); else if (e.key === 'ArrowDown') n = move(c, 1, 0);
else if (e.key === 'Home') n = CS[CS.length - 1]; else if (e.key === 'End') n = at(8, 1);
else if (e.key === 'Escape') { if (sel) select(sel, false); return; }
if (!n) return;
e.preventDefault(); setTab(n.id); btn(n.id).focus();
});
setTab(CS[CS.length - 1].id);
show(null);
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="pleroma-grid"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
