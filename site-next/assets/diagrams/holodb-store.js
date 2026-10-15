import { hasDom, prefersReducedMotion, readTokens, rgba, makeCamera, fitCanvas, tablist, loop } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const NS = 'http://www.w3.org/2000/svg';
const f2 = (n) => Number(n).toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const n0 = (n) => Number(n).toLocaleString('en-GB');
const MODES = [{ id: 'key', label: 'By key' }, { id: 'content', label: 'By content' }, { id: 'aggregate', label: 'By aggregate' }, { id: 'plane', label: 'Optional plane' }];
const REGIONS = ['north', 'south', 'east', 'west'];
export function metrics(d) {
const rows = d.table.rows, ci = (c) => d.table.columns.indexOf(c);
const q = Object.fromEntries(d.queries.map((x) => [x.key, x]));
const scores = Object.fromEntries(q.content.result.map((r) => [r.id, r.score]));
const totals = q.aggregate.result.map((r) => ({ region: r.region, count: r['count(*)'], sum: r['sum(amount_cents)'] }));
const keyId = q.key.result[0].id;
const rank = q.content.result.map((r) => r.id);
return { rows: rows.length, ci, q, scores, totals, keyId, rank, planeServed: d.plane.queries.filter((p) => p.served).length, planeTotal: d.plane.queries.length };
}
const route = (x) => x.explain.filter((e) => e.kind !== 'decline').map((e) => e.route).join(' then ');
export function panelFor(mode, d, m, sel) {
if (mode === 'plane') {
const p = d.plane.queries[sel];
const reason = (p.explain.find((e) => e.kind === 'decline') || {}).route;
return {
title: p.served ? 'Served by the plane' : 'Declined: the engine answers',
sql: p.sql,
lines: [p.served ? 'Route: ' + p.explain[0].route : 'Route: ' + p.explain.filter((e) => e.kind !== 'decline').map((e) => e.route).join(', then ') + (reason ? '. Why not the plane: ' + reason : '.'), `The plane served ${m.planeServed} of these ${m.planeTotal} queries.`],
};
}
const x = m.q[mode];
const res = mode === 'key' ? `Row ${m.keyId}: ${Object.entries(x.result[0]).map(([k, v]) => `${k} ${v}`).join(', ')}.`
: mode === 'content' ? `Closest: ${x.result.slice(0, 3).map((r, i) => `${i + 1}. id ${r.id} (${r.region}, ${n0(r.amount_cents)}) score ${Number(r.score).toFixed(3)}`).join('; ')}.`
: `${m.totals.map((t) => `${t.region} ${n0(t.sum)} over ${t.count} rows`).join('; ')}.`;
return {
title: MODES.find((k) => k.id === mode).label,
sql: x.sql,
lines: [`Route: ${route(x)}.`, `Rows scanned: ${x.rowsScanned} of ${x.tableRows}.`, res],
};
}
function latencySvg(d, mode) {
const L = d.latency; if (!L) return '';
const rows = mode === 'key' ? L.key : mode === 'aggregate' ? L.aggregate : null;
const W = 330, rowH = 52;
if (!rows) {
const txt = mode === 'content' ? 'Similarity ranking is not part of the billing comparison, so there is no latency figure for it here.' : 'The plane is measured on its own workloads: see the evidence.';
return `<p class="muted hd-note">${esc(txt)}</p>`;
}
const cols = L.columns, all = rows.flatMap((r) => r.values).map((v) => Math.max(v, 0.005));
const lo = Math.log10(Math.min(...all)), hi = Math.log10(Math.max(...all)), span = 150, left = 100;
const x = (v) => 4 + ((Math.log10(Math.max(v, 0.005)) - lo) / (hi - lo || 1)) * span;
const g = rows.map((r, i) => {
const y = 8 + i * rowH;
return `<text class="lab" x="0" y="${y + 10}">${esc(r.label)}</text>` + cols.map((c, k) => `<text class="eng" x="${left - 6}" y="${y + 21 + k * 11}" text-anchor="end">${esc(c)}</text><rect class="b${k === L.focus ? ' focus' : ''}" x="${left}" y="${y + 14 + k * 11}" width="${x(r.values[k]).toFixed(1)}" height="7" rx="2"/><text class="val" x="${left + x(r.values[k]) + 6}" y="${y + 21 + k * 11}">${f2(r.values[k])} ms</text>`).join('');
}).join('');
return `<svg class="hd-lat-svg" viewBox="0 0 ${W} ${rows.length * rowH + 12}" role="img" aria-label="Median latency in milliseconds on a ledger of 10,000,000 invoices, logarithmic scale: ${esc(rows.map((r) => `${r.label}: ${cols.map((c, k) => `${c} ${f2(r.values[k])}`).join(', ')}`).join('. '))}">${g}</svg>`;
}
export function staticSvg(d) {
const m = metrics(d), col = { north: 'n', south: 's', east: 'e', west: 'w' };
const rows = d.table.rows, cw = 56, ch = 38;
const cells = rows.map((r, i) => { const x = 8 + (i % 6) * (cw + 4), y = 8 + Math.floor(i / 6) * (ch + 4); return `<g><title>${esc(`id ${r[0]}, ${r[1]}, ${r[2]}, ${r[3]}, ${n0(r[4])}`)}</title><rect class="t ${col[r[2]]}" x="${x}" y="${y}" width="${cw}" height="${ch}" rx="5"/><text x="${x + cw / 2}" y="${y + 17}" text-anchor="middle">${r[0]}</text><text class="s" x="${x + cw / 2}" y="${y + 30}" text-anchor="middle">${esc(r[2])}</text></g>`; }).join('');
return `<svg class="dia-plan hd-plan" viewBox="0 0 372 178" role="img" aria-labelledby="hd-t hd-d"><title id="hd-t">The invoices table</title><desc id="hd-d">${m.rows} rows in four regions. The same rows answer a key lookup, a similarity ranking and an aggregate; the tables below list each query and its result.</desc>${cells}</svg>`;
}
export function controlsHtml(id, m, d) {
const tabs = MODES.map((md, i) => `<button type="button" role="tab" id="${id}-tab-${md.id}" data-mode="${md.id}" aria-selected="${i === 0}" aria-controls="${id}-stage" tabindex="${i === 0 ? 0 : -1}">${esc(md.label)}</button>`).join('');
const p0 = panelFor('key', d, m, 0);
const planeBtns = d.plane.queries.map((p, i) => `<button type="button" class="chip" data-q="${i}" aria-pressed="${i === 0}">${p.served ? 'served' : 'engine'}: ${esc(p.sql.length > 46 ? p.sql.slice(0, 44) + '...' : p.sql)}</button>`).join('');
return {
tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="Way to query" hidden>${tabs}</div>`,
controls: `<div class="dia-controls dia-ui" hidden>
<label class="dia-field">Turn <input type="range" min="-180" max="180" step="1" value="-24" data-ctl="yaw"></label>
<label class="dia-field">Tilt <input type="range" min="10" max="70" step="1" value="46" data-ctl="tilt"></label>
<button type="button" class="btn btn-ghost btn-sm" data-ctl="play" aria-pressed="true">Pause motion</button></div>
<div class="hd-qlist dia-ui" data-ctl="qlist" hidden role="group" aria-label="Queries to explain">${planeBtns}</div>
<div class="hd-panel" data-ctl="panel" role="status"><h3 class="hd-h">${esc(p0.title)}</h3><pre class="hd-sql"><code>${esc(p0.sql)}</code></pre>${p0.lines.map((l) => `<p>${esc(l)}</p>`).join('')}</div>
<div class="hd-latency" data-ctl="latency"><h3 class="hd-h">Same question on a 10,000,000-invoice ledger</h3><div data-ctl="latbody">${latencySvg(d, 'key')}</div></div>`,
};
}
export function tablesHtml(d, m) {
const row = (cells, head) => '<tr>' + cells.map((c, i) => (i === 0 && head ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
const tbl = (cap, cols, rows) => `<div class="tbl-wrap"><table class="tbl"><caption>${esc(cap)}</caption><thead><tr>${cols.map((c) => `<th scope="col">${esc(c)}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>`;
const qs = d.queries.map((x) => row([x.label, x.sql, route(x), `${x.rowsScanned} of ${x.tableRows}`, panelFor(x.key, d, m, 0).lines[2]], true));
const pl = d.plane.queries.map((p) => row([p.sql, p.served ? 'plane' : 'engine', p.explain.filter((e) => e.kind !== 'decline').map((e) => e.route).join(', then '), (p.explain.find((e) => e.kind === 'decline') || {}).route || ''], true));
return `<details class="dia-data"><summary>Data behind the diagram</summary>
${tbl(`The ${m.rows} rows`, d.table.columns, d.table.rows.map((r) => row(r.map(String), true)))}
${tbl('The three queries as run, with the engine\'s own route and the usage meter', ['Way', 'SQL', 'Route', 'Rows scanned', 'Result'], qs)}
${tbl('Where the optional plane sends six queries (from EXPLAIN)', ['SQL', 'Answered by', 'Route', 'Why the plane declined'], pl)}
</details>`;
}
export function verify(d, body) {
const m = metrics(d), out = [];
if (d.table.rows.length !== 24) out.push('expected 24 rows');
if (m.q.key.rowsScanned !== 0) out.push('key lookup scanned rows');
if (m.q.aggregate.rowsScanned !== 0) out.push('aggregate scanned rows');
if (m.totals.reduce((a, t) => a + t.count, 0) !== m.rows) out.push('aggregate counts do not add up to the table');
if (m.totals.reduce((a, t) => a + t.sum, 0) !== d.table.rows.reduce((a, r) => a + r[4], 0)) out.push('aggregate sums do not add up to the table');
if (!/Rows scanned/.test(body)) out.push('no usage-meter line');
if (d.latency && !d.latency.key.length) out.push('latency block empty');
return out;
}
export function mount(root, d) {
const m = metrics(d), reduced = prefersReducedMotion(), tok = readTokens(root);
const FONT = getComputedStyle(root).fontFamily;
const regCol = { north: tok.spectrum[4], south: tok.spectrum[3], east: tok.spectrum[1], west: tok.spectrum[6] };
const stage = root.querySelector('.dia-stage');
stage.textContent = '';
const canvas = document.createElement('canvas'); canvas.className = 'dia-canvas'; canvas.setAttribute('aria-hidden', 'true'); stage.append(canvas);
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const ctx = canvas.getContext('2d');
const q = (s) => root.querySelector(s);
const ui = { yaw: q('[data-ctl=yaw]'), tilt: q('[data-ctl=tilt]'), play: q('[data-ctl=play]'), panel: q('[data-ctl=panel]'), qlist: q('[data-ctl=qlist]'), lat: q('[data-ctl=latbody]') };
const S = { mode: 'key', yaw: -24, tilt: 46, sel: 0, playing: !reduced, userPlay: false, moved: false, drag: null };
ui.qlist.hidden = true;
const anim = () => S.playing && (!reduced || S.userPlay);
ui.play.textContent = S.playing ? 'Pause motion' : 'Play motion'; ui.play.setAttribute('aria-pressed', String(S.playing));
const rows = d.table.rows.map((r, i) => ({ id: r[0], customer: r[1], region: r[2], status: r[3], cents: r[4], gx: (i % 6) * 1.25, gy: Math.floor(i / 6) * 1.25 }));
const CX = 2.5 * 1.25, CY = 1.5 * 1.25;
const maxSum = Math.max(...m.totals.map((t) => t.sum));
const towers = m.totals.map((t, i) => ({ ...t, gx: 0.9 + i * 1.6, gy: -1.9, h: 0.3 + 2.6 * (t.sum / maxSum) }));
const towerOf = Object.fromEntries(towers.map((t) => [t.region, t]));
function box(cam, P, gx, gy, w, hgt, col, alpha, sideCol) {
const x0 = gx - w / 2, x1 = gx + w / 2, y0 = gy - w / 2, y1 = gy + w / 2;
const quad = (pts, fill) => { ctx.beginPath(); pts.forEach((p, i) => (i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]))); ctx.closePath(); ctx.fillStyle = fill; ctx.fill(); };
ctx.globalAlpha = alpha;
const sides = [[0, 1, [x0, y1], [x1, y1]], [0, -1, [x0, y0], [x1, y0]], [1, 0, [x1, y0], [x1, y1]], [-1, 0, [x0, y0], [x0, y1]]];
for (const [nx, ny, a, b] of sides) {
const dn = cam.dir(nx, ny);
if (dn[1] <= 0.001) continue;
quad([P(a[0], a[1], 0), P(b[0], b[1], 0), P(b[0], b[1], hgt), P(a[0], a[1], hgt)], dn[0] > 0 ? sideCol[0] : sideCol[1]);
}
quad([P(x0, y0, hgt), P(x1, y0, hgt), P(x1, y1, hgt), P(x0, y1, hgt)], col);
ctx.globalAlpha = 1;
}
function draw(time) {
const { w, h, dpr } = fitCanvas(canvas);
ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, w, h);
if (w < 80 || h < 80) return false;
const narrow = w < 560;
const sway = anim() && !S.moved ? 5 * Math.sin(time * 0.3) : 0;
const cam = makeCamera(CX, CY + (S.mode === 'aggregate' ? -0.6 : 0), S.yaw + sway + (narrow ? 90 : 0), S.tilt);
const Rc = S.mode === 'aggregate' ? 5.2 : 4.4;
const tr = (S.tilt * Math.PI) / 180, F = Math.min(w / (2 * Rc * 1.15), h / (2 * (Rc * Math.cos(tr) * 0.9 + 3.2 * Math.sin(tr))));
const P = (x, y, z = 0) => { const p = cam.p(x, y, z); return [w / 2 + p[0] * F, h / 2 + p[1] * F + 6, p[2]]; };
const dimA = 0.3;
const items = rows.map((r) => {
const rc = regCol[r.region];
let hgt = 0.16, alpha = 1;
if (S.mode === 'key') { if (r.id === m.keyId) hgt = 1.1; else alpha = dimA; }
else if (S.mode === 'content') hgt = 0.16 + 1.7 * (m.scores[r.id] || 0);
else if (S.mode === 'aggregate') alpha = 0.45;
else alpha = 0.35;
const rnk = m.rank.indexOf(r.id);
return { r, rc, hgt, alpha, rnk, depth: cam.p(r.gx, r.gy, 0)[2] };
});
if (S.mode === 'aggregate') {
towers.forEach((t) => {
const base = P(t.gx, t.gy, 0);
ctx.fillStyle = rgba(regCol[t.region], 0.18); ctx.strokeStyle = rgba(regCol[t.region], 0.5);
const pts = [[-0.5, -0.5], [0.5, -0.5], [0.5, 0.5], [-0.5, 0.5]].map(([a, b]) => P(t.gx + a * 0.9, t.gy + b * 0.9, 0));
ctx.beginPath(); pts.forEach((p, i) => (i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]))); ctx.closePath(); ctx.fill(); ctx.stroke();
void base;
});
rows.forEach((r, i) => {
const t = towerOf[r.region], a = P(r.gx, r.gy, 0.16), b = P(t.gx, t.gy, 0.5);
ctx.strokeStyle = rgba(regCol[r.region], 0.16); ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke();
if (anim()) { const p = (time * 0.35 + i * 0.137) % 1, x = a[0] + (b[0] - a[0]) * p, y = a[1] + (b[1] - a[1]) * p - Math.sin(p * Math.PI) * 12; ctx.fillStyle = rgba(regCol[r.region], 0.9); ctx.beginPath(); ctx.arc(x, y, 2.2, 0, 6.2832); ctx.fill(); }
});
}
const all = items.map((it) => ({ k: 'tile', depth: it.depth, it }));
if (S.mode === 'aggregate') towers.forEach((t) => all.push({ k: 'tower', depth: cam.p(t.gx, t.gy, 0)[2], t }));
all.sort((a, b) => a.depth - b.depth);
ctx.font = `600 ${narrow ? 10 : 11}px ${FONT}`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
const labels = [];
for (const o of all) {
if (o.k === 'tower') {
const t = o.t, col = regCol[t.region];
box(cam, P, t.gx, t.gy, 0.8, t.h, col, 1, [rgba(col, 0.75), rgba(col, 0.55)]);
const top = P(t.gx, t.gy, t.h + 0.25);
labels.push({ x: top[0], y: top[1] - 8, text: `${t.region} ${n0(t.sum)}`, col });
continue;
}
const { r, rc, hgt, alpha, rnk } = o.it;
const hot = (S.mode === 'key' && r.id === m.keyId) || (S.mode === 'content' && rnk >= 0 && rnk < 3);
const base = S.mode === 'content' ? rgba(rc, 0.35 + 0.65 * (m.scores[r.id] || 0)) : rc;
box(cam, P, r.gx, r.gy, 0.9, hgt, base, alpha, [rgba(rc, 0.7), rgba(rc, 0.5)]);
const top = P(r.gx, r.gy, hgt);
if (hot) {
ctx.strokeStyle = tok.ink; ctx.lineWidth = 2; ctx.globalAlpha = 0.9; ctx.beginPath(); ctx.arc(top[0], top[1], 11, 0, 6.2832); ctx.stroke(); ctx.globalAlpha = 1;
labels.push({ x: top[0], y: top[1] - 17, text: S.mode === 'key' ? `id ${r.id}` : `${rnk + 1}: ${Number(m.scores[r.id]).toFixed(2)}`, col: tok.ink });
} else if (!narrow || S.mode === 'plane') {
ctx.fillStyle = rgba(tok.ink, alpha > 0.5 ? 0.75 : 0.35); ctx.fillText(String(r.id), top[0], top[1]);
}
}
if (S.mode === 'key') {
const row = rows.find((r) => r.id === m.keyId), top = P(row.gx, row.gy, 1.1);
const g = ctx.createLinearGradient(top[0], 0, top[0], top[1]); g.addColorStop(0, rgba(tok.accentInk, 0)); g.addColorStop(1, rgba(tok.accentInk, 0.8));
ctx.strokeStyle = g; ctx.lineWidth = 3; ctx.beginPath(); ctx.moveTo(top[0], 6); ctx.lineTo(top[0], top[1] - 14); ctx.stroke();
}
if (S.mode === 'plane') drawPlane(w, h, narrow, time);
ctx.font = `700 ${narrow ? 11 : 12}px ${FONT}`;
for (const l of labels) { const tw = ctx.measureText(l.text).width + 10, x = Math.max(tw / 2 + 2, Math.min(w - tw / 2 - 2, l.x)); ctx.fillStyle = rgba(tok.bg, 0.85); ctx.fillRect(x - tw / 2, l.y - 9, tw, 18); ctx.fillStyle = l.col; ctx.fillText(l.text, x, l.y); }
return anim();
}
function drawPlane(w, h, narrow, time) {
const p = d.plane.queries[S.sel], served = p.served;
const bw = Math.min(190, w * 0.36), bh = 58, lx = 14, rx = w - bw - 14;
const boxes = { plane: { x: rx, y: h * 0.16, t: 'Plane', s: 'derived summary' }, engine: { x: rx, y: h * 0.62, t: 'Engine', s: 'columns' } };
ctx.globalAlpha = 1; ctx.fillStyle = rgba(tok.bg, 0.55); ctx.fillRect(0, 0, w, h);
for (const [k, b] of Object.entries(boxes)) {
const on = (k === 'plane') === served;
ctx.fillStyle = on ? rgba(tok.accent, 0.22) : tok.surface; ctx.strokeStyle = on ? tok.accentInk : tok.border2; ctx.lineWidth = on ? 3 : 1.2;
ctx.beginPath(); if (ctx.roundRect) ctx.roundRect(b.x, b.y, bw, bh, 10); else ctx.rect(b.x, b.y, bw, bh); ctx.fill(); ctx.stroke();
ctx.fillStyle = tok.ink; ctx.font = `700 14px ${FONT}`; ctx.fillText(b.t, b.x + bw / 2, b.y + 22); ctx.fillStyle = tok.faint; ctx.font = `12px ${FONT}`; ctx.fillText(b.s, b.x + bw / 2, b.y + 41);
}
const qy = h * 0.42, target = served ? boxes.plane : boxes.engine;
ctx.fillStyle = tok.surface2; ctx.strokeStyle = tok.soft; ctx.lineWidth = 1.2;
ctx.beginPath(); if (ctx.roundRect) ctx.roundRect(lx, qy - 24, bw * 0.8, 48, 10); else ctx.rect(lx, qy - 24, bw * 0.8, 48); ctx.fill(); ctx.stroke();
ctx.fillStyle = tok.ink; ctx.font = `600 13px ${FONT}`; ctx.fillText('query', lx + bw * 0.4, qy);
const ax = lx + bw * 0.8, ay = qy, bx = target.x, by = target.y + bh / 2;
ctx.strokeStyle = rgba(tok.accentInk, 0.9); ctx.lineWidth = 2.5; ctx.beginPath(); ctx.moveTo(ax, ay); ctx.bezierCurveTo(ax + 50, ay, bx - 50, by, bx, by); ctx.stroke();
if (anim()) { const t = (time * 0.5) % 1, x = (1 - t) ** 3 * ax + 3 * (1 - t) ** 2 * t * (ax + 50) + 3 * (1 - t) * t * t * (bx - 50) + t ** 3 * bx, y = (1 - t) ** 3 * ay + 3 * (1 - t) ** 2 * t * ay + 3 * (1 - t) * t * t * by + t ** 3 * by; ctx.fillStyle = tok.ink; ctx.beginPath(); ctx.arc(x, y, 5, 0, 6.2832); ctx.fill(); }
}
const lp = loop((time) => draw(time));
const changed = () => { lp.redraw(); lp.kick(); };
function refresh() {
const pl = panelFor(S.mode, d, m, S.sel);
const pn = ui.panel; pn.textContent = '';
const h3 = document.createElement('h3'); h3.className = 'hd-h'; h3.textContent = pl.title;
const pre = document.createElement('pre'); pre.className = 'hd-sql'; const code = document.createElement('code'); code.textContent = pl.sql; pre.append(code);
pn.append(h3, pre); pl.lines.forEach((l) => { const p = document.createElement('p'); p.textContent = l; pn.append(p); });
ui.qlist.hidden = S.mode !== 'plane';
ui.lat.innerHTML = latencySvg(d, S.mode);
root.querySelectorAll('[data-q]').forEach((b) => b.setAttribute('aria-pressed', String(Number(b.dataset.q) === S.sel)));
root.dataset.mode = S.mode;
}
tablist(root.querySelector('[role="tablist"]'), (mode) => { S.mode = mode; refresh(); changed(); });
root.querySelectorAll('[data-q]').forEach((b) => b.addEventListener('click', () => { S.sel = Number(b.dataset.q); refresh(); changed(); }));
ui.yaw.addEventListener('input', () => { S.yaw = Number(ui.yaw.value); S.moved = true; changed(); });
ui.tilt.addEventListener('input', () => { S.tilt = Number(ui.tilt.value); S.moved = true; changed(); });
ui.play.addEventListener('click', () => { S.playing = !S.playing; if (S.playing) S.userPlay = true; ui.play.textContent = S.playing ? 'Pause motion' : 'Play motion'; ui.play.setAttribute('aria-pressed', String(S.playing)); changed(); });
const wrap = (v) => ((v + 540) % 360) - 180;
canvas.addEventListener('pointerdown', (e) => { S.drag = { x: e.clientX, y: e.clientY, type: e.pointerType }; canvas.setPointerCapture(e.pointerId); });
canvas.addEventListener('pointermove', (e) => {
if (!S.drag) return;
const dx = e.clientX - S.drag.x, dy = e.clientY - S.drag.y; S.drag.x = e.clientX; S.drag.y = e.clientY;
S.yaw = wrap(S.yaw + dx * 0.4); S.moved = true; if (S.drag.type === 'mouse') S.tilt = Math.max(10, Math.min(70, S.tilt - dy * 0.3));
ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed();
});
const end = () => { S.drag = null; }; canvas.addEventListener('pointerup', end); canvas.addEventListener('pointercancel', end);
stage.addEventListener('keydown', (e) => {
let used = true;
if (e.key === 'ArrowLeft') S.yaw = wrap(S.yaw - 5); else if (e.key === 'ArrowRight') S.yaw = wrap(S.yaw + 5);
else if (e.key === 'ArrowUp') S.tilt = Math.min(70, S.tilt + 3); else if (e.key === 'ArrowDown') S.tilt = Math.max(10, S.tilt - 3);
else used = false;
if (used) { e.preventDefault(); S.moved = true; ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed(); }
});
new ResizeObserver(() => lp.redraw()).observe(stage);
new IntersectionObserver((es) => lp.setVisible(es[0].isIntersecting)).observe(root);
document.addEventListener('visibilitychange', () => lp.kick());
refresh(); lp.redraw(); lp.kick();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="holodb-store"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
