import { hasDom, prefersReducedMotion, readTokens, rgba, hexToRgb, fitCanvas, loop } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const TAU = Math.PI * 2;
export function metrics(d) {
const byId = Object.fromEntries(d.nodes.map((n) => [n.id, n]));
const builds = (id) => d.edges.filter((e) => e.to === id).map((e) => byId[e.from]);
const usedBy = (id) => d.edges.filter((e) => e.from === id).map((e) => byId[e.to]);
return {
products: d.nodes.length, edges: d.edges.length, tools: d.tools.length,
floors: Math.max(...d.nodes.map((n) => n.level)) + 1,
builds, usedBy, byId,
};
}
export function readoutFor(d, m, id) {
if (!id) return { text: `${m.products} products on ${m.floors} floors. Each line is a project reference: the lower product is what the upper one builds on. Choose a product to see its place in the stack.`, link: null };
const n = m.byId[id];
const b = m.builds(id).map((x) => x.name), u = m.usedBy(id).map((x) => x.name);
const tl = d.tools.filter((t) => t.poweredBy.includes(id)).map((t) => t.name);
const join = (a) => (a.length > 1 ? a.slice(0, -1).join(', ') + ' and ' + a[a.length - 1] : a[0]);
const parts = [`${n.name}: ${n.pitch}`];
parts.push(b.length ? `Builds on ${join(b)}.` : 'Builds on no other product.');
parts.push(u.length ? `${join(u)} ${u.length > 1 ? 'build' : 'builds'} on it.` : 'No other product builds on it.');
if (tl.length) parts.push(`Powers ${join(tl)} in the browser.`);
return { text: parts.join(' '), link: { href: n.href, label: `Open ${n.name}` } };
}
export function staticSvg(d) {
const m = metrics(d);
const W = 360, nodeW = 112, nodeH = 34, rowGap = 78, top = 30;
const rows = [];
for (let l = 0; l < m.floors; l++) {
const ps = d.nodes.filter((n) => n.level === l);
if (!ps.length) continue;
const nRows = Math.ceil(ps.length / 3), per = Math.ceil(ps.length / nRows);
for (let r = 0; r < nRows; r++) rows.push(ps.slice(r * per, (r + 1) * per));
}
const pos = {};
rows.forEach((row, ri) => { const n = row.length, gap = (W - n * nodeW) / (n + 1); row.forEach((p, i) => { pos[p.id] = { x: gap + i * (nodeW + gap) + nodeW / 2, y: top + ri * rowGap }; }); });
const H = top + (rows.length - 1) * rowGap + nodeH / 2 + 24;
const edges = d.edges.map((e) => { const a = pos[e.from], b = pos[e.to], y1 = a.y + nodeH / 2, y2 = b.y - nodeH / 2, mid = (y1 + y2) / 2; return `<path class="edge fill-${m.byId[e.from].cat}" d="M${a.x.toFixed(1)} ${y1} C${a.x.toFixed(1)} ${mid} ${b.x.toFixed(1)} ${mid} ${b.x.toFixed(1)} ${y2}"/>`; }).join('');
const nodes = d.nodes.map((n) => { const q = pos[n.id]; return `<a href="${esc(n.href)}" class="node fill-${n.cat}"><rect x="${(q.x - nodeW / 2).toFixed(1)}" y="${(q.y - nodeH / 2).toFixed(1)}" width="${nodeW}" height="${nodeH}" rx="9"/><text x="${q.x.toFixed(1)}" y="${(q.y + 4.5).toFixed(1)}" text-anchor="middle">${esc(n.name)}</text></a>`; }).join('');
return `<svg class="dia-plan stack-plan" viewBox="0 0 ${W} ${H.toFixed(0)}" role="group" aria-labelledby="sm-t"><title id="sm-t">How the products depend on each other</title>${edges}${nodes}</svg>`;
}
export function controlsHtml(id, m) {
const chips = ['<button type="button" class="chip" data-node="" aria-pressed="true">Overview</button>'].concat(Object.values(m.byId).map((n) => `<button type="button" class="chip" data-node="${esc(n.id)}" aria-pressed="false">${esc(n.name)}</button>`)).join('');
return {
tabs: '',
controls: `<div class="stack-chips dia-ui" role="group" aria-label="Choose a product" hidden>${chips}</div>
<div class="dia-controls dia-ui" hidden>
<label class="dia-field">Turn <input type="range" min="-180" max="180" step="1" value="30" data-ctl="yaw"></label>
<label class="dia-field">Tilt <input type="range" min="0" max="60" step="1" value="22" data-ctl="tilt"></label>
<label class="dia-check"><input type="checkbox" data-ctl="tools" checked> Tools</label>
<button type="button" class="btn btn-ghost btn-sm" data-ctl="play" aria-pressed="true">Pause motion</button>
<button type="button" class="btn btn-ghost btn-sm" data-ctl="reset">Reset view</button>
</div>
<p class="dia-hint dia-ui" hidden>Drag the scene to turn it. Choose a product below, or press the arrow keys while the scene is focused.</p>
<div class="dia-readout stack-readout dia-ui" data-ctl="readout" role="status" hidden><span data-ctl="text" data-gen="count">${esc(readoutFor({ tools: [] }, m, null).text)}</span> <a data-ctl="link" hidden></a></div>`,
};
}
export function tablesHtml(d, m) {
const rows = d.nodes.map((n) => {
const b = m.builds(n.id).map((x) => x.name).join(', ') || 'nothing';
const u = m.usedBy(n.id).map((x) => x.name).join(', ') || 'nothing';
const t = d.tools.filter((x) => x.poweredBy.includes(n.id)).map((x) => x.name).join(', ') || 'none';
return `<tr><th scope="row"><a href="${esc(n.href)}">${esc(n.name)}</a></th><td>${n.level + 1}</td><td>${esc(b)}</td><td>${esc(u)}</td><td>${esc(t)}</td></tr>`;
}).join('');
return `<details class="dia-data"><summary>The stack as a table</summary><div class="tbl-wrap"><table class="tbl"><caption>Each product, its floor, what it builds on, what builds on it and the tools it powers</caption>
<thead><tr><th scope="col">Product</th><th scope="col">Floor</th><th scope="col">Builds on</th><th scope="col">Built on by</th><th scope="col">Powers tools</th></tr></thead><tbody>${rows}</tbody></table></div></details>`;
}
function layout(d) {
const m = metrics(d);
const pos = {};
const ang = {};
for (let l = 0; l < m.floors; l++) {
const ps = d.nodes.filter((n) => n.level === l);
ps.forEach((n, i) => {
const r = ps.length === 1 ? 0 : 1.2 + 0.5 * ps.length;
const a = (i / ps.length) * TAU + l * 0.7 + 0.4;
ang[n.id] = a;
pos[n.id] = [Math.cos(a) * r, (l - (m.floors - 1) / 2) * 2.1, Math.sin(a) * r];
});
}
const tpos = {};
const tl = d.tools.map((t, i) => {
const ps = t.poweredBy.filter((id) => pos[id]);
let a, y;
if (ps.length) {
a = Math.atan2(ps.reduce((s, id) => s + Math.sin(ang[id]), 0), ps.reduce((s, id) => s + Math.cos(ang[id]), 0));
y = ps.reduce((s, id) => s + pos[id][1], 0) / ps.length - 0.9;
} else { a = 1.9 + i * 0.1; y = -2.6; }
return { t, a, y };
}).sort((p, q) => p.a - q.a);
for (let pass = 0; pass < 40; pass++) for (let i = 0; i < tl.length; i++) {
const j = (i + 1) % tl.length; let dd = tl[j].a - tl[i].a; if (dd < 0) dd += TAU;
if (dd < 0.32) { tl[i].a -= (0.32 - dd) / 2; tl[j].a += (0.32 - dd) / 2; }
}
tl.forEach((x) => { tpos[x.t.id] = [Math.cos(x.a) * 4.0, x.y, Math.sin(x.a) * 4.0]; });
return { pos, tpos };
}
export function mount(root, d) {
const m = metrics(d), L = layout(d);
const stage = root.querySelector('.dia-stage');
const reduced = prefersReducedMotion();
const tok = readTokens(root);
const css = getComputedStyle(root);
const FONT = css.fontFamily;
const colorOf = (n) => (css.getPropertyValue('--' + n.color).trim() || tok.accent);
stage.textContent = '';
const canvas = document.createElement('canvas'); canvas.className = 'dia-canvas'; canvas.setAttribute('aria-hidden', 'true'); stage.append(canvas);
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const ctx = canvas.getContext('2d');
const q = (s) => root.querySelector(s);
const ui = { yaw: q('[data-ctl=yaw]'), tilt: q('[data-ctl=tilt]'), tools: q('[data-ctl=tools]'), play: q('[data-ctl=play]'), reset: q('[data-ctl=reset]'), text: q('[data-ctl=text]'), link: q('[data-ctl=link]') };
const chips = [...root.querySelectorAll('.chip')];
const narrow = stage.getBoundingClientRect().width < 560;
const HOME = { yaw: narrow ? 20 : 30, tilt: narrow ? 26 : 22 };
const S = { yaw: HOME.yaw, tilt: HOME.tilt, sel: null, playing: !reduced, userPlay: false, tools: true, drag: null, spin: 0 };
ui.yaw.value = S.yaw; ui.tilt.value = S.tilt;
ui.play.textContent = S.playing ? 'Pause motion' : 'Play motion'; ui.play.setAttribute('aria-pressed', String(S.playing));
const anim = () => S.playing && (!reduced || S.userPlay);
const hits = [];
root._labels = () => (S.rects || []).map((r) => ({ ...r }));
function related(id) {
if (!id) return null;
const set = new Set([id]);
d.edges.forEach((e) => { if (e.from === id) set.add(e.to); if (e.to === id) set.add(e.from); });
return set;
}
function draw(time) {
const { w, h, dpr } = fitCanvas(canvas);
ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, w, h);
if (w < 60 || h < 60) return false;
const yaw = ((S.yaw + S.spin) * Math.PI) / 180, tilt = (S.tilt * Math.PI) / 180;
const cy = Math.cos(yaw), sy = Math.sin(yaw), ct = Math.cos(tilt), st = Math.sin(tilt);
const F = Math.min(h / 7.6, w / 9.4), sx = Math.max(1, Math.min(2.4, w / (9.4 * F)));
const D = 11, cx0 = w / 2, cy0 = h / 2;
const P = (p) => {
const x1 = p[0] * cy + p[2] * sy, z1 = -p[0] * sy + p[2] * cy;
const y2 = p[1] * ct - z1 * st, z2 = p[1] * st + z1 * ct;
const k = D / (D - z2);
return [cx0 + F * sx * x1 * k, cy0 - F * y2 * k, z2, k];
};
const rel = related(S.sel);
const dim = (id) => (rel && !rel.has(id) ? 0.22 : 1);
const nodes = d.nodes.map((n) => ({ n, p: P(L.pos[n.id]), col: colorOf(n) }));
const np = Object.fromEntries(nodes.map((x) => [x.n.id, x]));
for (let l = 0; l < m.floors; l++) {
const y = (l - (m.floors - 1) / 2) * 2.1 - 0.5;
ctx.beginPath();
for (let i = 0; i <= 48; i++) { const a = (i / 48) * TAU, pp = P([Math.cos(a) * 3.9, y, Math.sin(a) * 3.9]); i ? ctx.lineTo(pp[0], pp[1]) : ctx.moveTo(pp[0], pp[1]); }
ctx.strokeStyle = rgba(tok.border2, 0.55); ctx.lineWidth = 1; ctx.stroke();
}
d.edges.forEach((e, i) => {
const a = np[e.from], b = np[e.to], hot = S.sel && (e.from === S.sel || e.to === S.sel);
const al = S.sel ? (hot ? 0.95 : 0.1) : 0.45;
const g = ctx.createLinearGradient(a.p[0], a.p[1], b.p[0], b.p[1]);
g.addColorStop(0, rgba(a.col, al)); g.addColorStop(1, rgba(b.col, al));
ctx.strokeStyle = g; ctx.lineWidth = hot ? 2.6 : 1.4;
ctx.beginPath(); ctx.moveTo(a.p[0], a.p[1]); ctx.lineTo(b.p[0], b.p[1]); ctx.stroke();
if (anim()) {
const t = (time * 0.28 + i * 0.173) % 1;
const px = a.p[0] + (b.p[0] - a.p[0]) * t, py = a.p[1] + (b.p[1] - a.p[1]) * t;
ctx.fillStyle = rgba(tok.ink, hot ? 0.95 : 0.55); ctx.beginPath(); ctx.arc(px, py, hot ? 3 : 2, 0, TAU); ctx.fill();
}
});
const tp = [];
if (S.tools) d.tools.forEach((t) => {
const p = P(L.tpos[t.id]);
const on = !S.sel || t.poweredBy.includes(S.sel);
t.poweredBy.forEach((id) => {
if (!np[id]) return;
ctx.strokeStyle = rgba(tok.faint, on ? 0.45 : 0.08); ctx.lineWidth = 1; ctx.setLineDash([2, 4]);
ctx.beginPath(); ctx.moveTo(p[0], p[1]); ctx.lineTo(np[id].p[0], np[id].p[1]); ctx.stroke(); ctx.setLineDash([]);
});
tp.push({ t, p, on });
});
hits.length = 0; S.rects = [];
ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
const zs = nodes.map((x) => x.p[2]), zmin = Math.min(...zs), zmax = Math.max(...zs);
const labs = nodes.map((x) => {
const k = x.p[3], fs = Math.max(10, 13 * k * (narrow ? 0.95 : 1.05));
ctx.font = `600 ${fs}px ${FONT}`;
const bw = ctx.measureText(x.n.name).width + 22 * k, bh = fs + 14 * k;
return { kind: 'n', id: x.n.id, x, bw, bh, cx: x.p[0], cy: x.p[1], z: x.p[2], fs, k, depth: zmax > zmin ? (x.p[2] - zmin) / (zmax - zmin) : 1 };
});
const placed = [];
const overlap = (r, o) => {
const ix = Math.min(r.x1, o.x1) - Math.max(r.x0, o.x0), iy = Math.min(r.y1, o.y1) - Math.max(r.y0, o.y0);
if (ix <= 0 || iy <= 0) return 0;
return (ix * iy) / Math.min((r.x1 - r.x0) * (r.y1 - r.y0), (o.x1 - o.x0) * (o.y1 - o.y0));
};
const place = (L, optional) => {
const dy = L.bh + 3, dx = L.bw * 0.5;
const offs = [[0, 0], [0, -dy], [0, dy], [dx, 0], [-dx, 0], [0, -2 * dy], [0, 2 * dy], [dx, -dy], [-dx, -dy], [dx, dy], [-dx, dy], [1.5 * dx, 0], [-1.5 * dx, 0], [0, -3 * dy], [0, 3 * dy]];
let best = null;
for (const [ox, oy] of offs) {
const cx = Math.max(L.bw / 2 + 3, Math.min(w - L.bw / 2 - 3, L.cx + ox)), cy = Math.max(L.bh / 2 + 3, Math.min(h - L.bh / 2 - 3, L.cy + oy));
const r = { x0: cx - L.bw / 2, x1: cx + L.bw / 2, y0: cy - L.bh / 2, y1: cy + L.bh / 2, cx, cy };
const worst = placed.reduce((m2, o) => Math.max(m2, overlap(r, o)), 0);
if (!best || worst < best.worst) best = { r, worst };
if (worst < 0.02) break;
}
if (optional && best.worst > 0.04) { L.r = null; return; }
placed.push(best.r); L.r = best.r;
};
[...labs].sort((p, q) => q.z - p.z).forEach(place);
const toolLabs = [];
tp.forEach((x) => {
const show = S.tools && (S.sel ? x.on : S.hoverTool === x.t.id);
if (!show) return;
const k = Math.min(1.2, x.p[3]), fs = 11 * k;
ctx.font = `${fs}px ${FONT}`;
const bw = ctx.measureText(x.t.name).width + 10, bh = fs + 8;
const L = { kind: 't', id: x.t.id, bw, bh, cx: x.p[0], cy: x.p[1] - 14 * x.p[3], z: x.p[2], tool: x };
toolLabs.push(L);
});
toolLabs.sort((p, q) => q.z - p.z).forEach((L) => place(L, true));
const order = [...labs.map((L) => ({ k: 'n', z: L.z, L })), ...tp.map((x) => ({ k: 't', z: x.p[2], x }))].sort((p, q) => p.z - q.z);
for (const it of order) {
if (it.k === 't') {
const { t, p, on } = it.x, r = 4.5 * p[3];
ctx.globalAlpha = on || !S.sel ? 1 : 0.25;
ctx.fillStyle = S.hoverTool === t.id ? tok.accent : tok.surface2; ctx.strokeStyle = tok.soft; ctx.lineWidth = 1.2;
ctx.beginPath(); ctx.arc(p[0], p[1], r, 0, TAU); ctx.fill(); ctx.stroke();
ctx.globalAlpha = 1;
const TL = toolLabs.find((l) => l.id === t.id);
if (TL && TL.r) {
ctx.font = `${11 * Math.min(1.2, p[3])}px ${FONT}`;
ctx.fillStyle = rgba(tok.bg, 0.8); ctx.fillRect(TL.r.x0, TL.r.y0, TL.bw, TL.bh);
ctx.fillStyle = tok.ink; ctx.fillText(t.name, TL.r.cx, TL.r.cy + 0.5);
S.rects.push({ id: 'tool:' + t.id, x0: TL.r.x0, x1: TL.r.x1, y0: TL.r.y0, y1: TL.r.y1 });
}
continue;
}
const L = it.L, { n, p, col } = L.x, sel = S.sel === n.id, r = L.r, k = L.k;
ctx.globalAlpha = dim(n.id) * (0.62 + 0.38 * L.depth);
if (Math.hypot(r.cx - p[0], r.cy - p[1]) > 8) { ctx.strokeStyle = rgba(col, 0.6); ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(p[0], p[1]); ctx.lineTo(r.cx, r.cy); ctx.stroke(); }
const glow = ctx.createRadialGradient(r.cx, r.cy, 0, r.cx, r.cy, L.bw);
glow.addColorStop(0, rgba(col, sel ? 0.35 : 0.14)); glow.addColorStop(1, rgba(col, 0));
ctx.fillStyle = glow; ctx.beginPath(); ctx.arc(r.cx, r.cy, L.bw, 0, TAU); ctx.fill();
ctx.fillStyle = sel ? tok.surface2 : tok.surface; ctx.strokeStyle = col; ctx.lineWidth = sel ? 3 : 1.6;
ctx.beginPath(); if (ctx.roundRect) ctx.roundRect(r.x0, r.y0, L.bw, L.bh, 9 * k); else ctx.rect(r.x0, r.y0, L.bw, L.bh); ctx.fill(); ctx.stroke();
ctx.font = `600 ${L.fs}px ${FONT}`; ctx.fillStyle = tok.ink; ctx.fillText(n.name, r.cx, r.cy + 0.5);
ctx.globalAlpha = 1;
hits.push({ id: n.id, x0: r.x0, x1: r.x1, y0: r.y0, y1: r.y1, z: p[2] });
S.rects.push({ id: n.id, x0: r.x0, x1: r.x1, y0: r.y0, y1: r.y1 });
}
S.toolHits = tp.map((x) => ({ id: x.t.id, x: x.p[0], y: x.p[1], r: 4.5 * x.p[3] + 7 }));
return anim();
}
const lp = loop((time, dt) => {
if (anim() && !S.drag) S.spin += dt * 9;
return draw(time);
});
const changed = () => { lp.redraw(); lp.kick(); };
function choose(id, fromChip) {
S.sel = id || null;
chips.forEach((c) => c.setAttribute('aria-pressed', String((c.dataset.node || null) === S.sel || (!c.dataset.node && !S.sel))));
const r = readoutFor(d, m, S.sel);
ui.text.textContent = r.text;
if (r.link) { ui.link.hidden = false; ui.link.href = r.link.href; ui.link.textContent = r.link.label; } else ui.link.hidden = true;
changed();
}
chips.forEach((c) => c.addEventListener('click', () => choose(c.dataset.node || null, true)));
ui.yaw.addEventListener('input', () => { S.yaw = Number(ui.yaw.value); S.spin = 0; changed(); });
ui.tilt.addEventListener('input', () => { S.tilt = Number(ui.tilt.value); changed(); });
ui.tools.addEventListener('change', () => { S.tools = ui.tools.checked; changed(); });
ui.play.addEventListener('click', () => { S.playing = !S.playing; if (S.playing) S.userPlay = true; ui.play.textContent = S.playing ? 'Pause motion' : 'Play motion'; ui.play.setAttribute('aria-pressed', String(S.playing)); changed(); });
ui.reset.addEventListener('click', () => { S.yaw = HOME.yaw; S.tilt = HOME.tilt; S.spin = 0; ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; choose(null); });
const wrap = (v) => ((v + 540) % 360) - 180;
canvas.addEventListener('pointerdown', (e) => { S.drag = { x: e.clientX, y: e.clientY, moved: false, type: e.pointerType, px: e.clientX, py: e.clientY }; canvas.setPointerCapture(e.pointerId); });
canvas.addEventListener('pointermove', (e) => {
const dr = S.drag;
if (!dr) {
if (e.pointerType === 'mouse' && S.toolHits) { const r0 = canvas.getBoundingClientRect(), x = e.clientX - r0.left, y = e.clientY - r0.top; const hv = S.toolHits.find((th) => Math.hypot(th.x - x, th.y - y) < th.r); const id = hv ? hv.id : null; if (id !== S.hoverTool) { S.hoverTool = id; changed(); } }
return;
}
const dx = e.clientX - dr.x, dy = e.clientY - dr.y;
if (!dr.moved && Math.abs(e.clientX - dr.px) + Math.abs(e.clientY - dr.py) < 5) return;
dr.moved = true; dr.x = e.clientX; dr.y = e.clientY;
S.yaw = wrap(S.yaw + S.spin + dx * 0.4); S.spin = 0;
if (dr.type === 'mouse') S.tilt = Math.max(0, Math.min(60, S.tilt - dy * 0.25));
ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed();
});
const up = (e) => {
const dr = S.drag; S.drag = null; if (!dr) return;
if (!dr.moved) {
const r = canvas.getBoundingClientRect(), x = e.clientX - r.left, y = e.clientY - r.top;
const hit = hits.filter((h) => x >= h.x0 && x <= h.x1 && y >= h.y0 && y <= h.y1).sort((a, b) => b.z - a.z)[0];
choose(hit ? hit.id : null);
}
};
canvas.addEventListener('pointerup', up); canvas.addEventListener('pointercancel', () => { S.drag = null; });
stage.addEventListener('keydown', (e) => {
const k = e.key; let used = true;
const ids = [null, ...d.nodes.map((n) => n.id)], i = ids.indexOf(S.sel);
if (k === 'ArrowRight' || k === 'ArrowDown') choose(ids[(i + 1) % ids.length]);
else if (k === 'ArrowLeft' || k === 'ArrowUp') choose(ids[(i - 1 + ids.length) % ids.length]);
else if (k === 'Escape') choose(null);
else used = false;
if (used) e.preventDefault();
});
new ResizeObserver(() => lp.redraw()).observe(stage);
new IntersectionObserver((es) => lp.setVisible(es[0].isIntersecting)).observe(root);
document.addEventListener('visibilitychange', () => lp.kick());
lp.redraw(); lp.kick();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="stack-map"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export function verify(d, body) {
const out = [];
for (const n of d.nodes) if (!body.includes(`data-node="${n.id}"`)) out.push(`no chip for ${n.id}`);
const rows = (body.match(/<tbody>[\s\S]*?<\/tbody>/) || [''])[0].split('<tr>').length - 1;
if (rows !== d.nodes.length) out.push(`table has ${rows} rows for ${d.nodes.length} products`);
return out;
}
