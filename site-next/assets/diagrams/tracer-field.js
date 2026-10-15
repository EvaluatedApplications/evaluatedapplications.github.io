import { hasDom, prefersReducedMotion, readTokens, rgba, mix, makeCamera, fitCanvas, tablist, loop } from './core.js';
const SPEED = 3.4;
const WALL_H = 1.5;
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const f1 = (n) => Number(n).toFixed(1);
const f2 = (n) => Number(n).toFixed(2);
const isWall = (g, x, y) => y < 0 || x < 0 || y >= g.height || x >= g.width || g.rows[y][x] !== '.';
function pathLength(p) {
let L = 0;
for (let i = 1; i < p.length; i++) L += Math.hypot(p[i][0] - p[i - 1][0], p[i][1] - p[i - 1][1]);
return L;
}
function cornerCut(g, p) {
let worst = 0;
for (let i = 0; i < p.length - 1; i++) {
const [x0, y0] = p[i], [x1, y1] = p[i + 1];
const n = Math.max(1, Math.ceil(Math.hypot(x1 - x0, y1 - y0) / 0.02));
for (let k = 0; k <= n; k++) {
const x = x0 + ((x1 - x0) * k) / n, y = y0 + ((y1 - y0) * k) / n;
if (!isWall(g, Math.floor(x), Math.floor(y))) continue;
let best = 9;
for (let dx = -2; dx <= 2; dx++) for (let dy = -2; dy <= 2; dy++) {
const cx = Math.floor(x) + dx, cy = Math.floor(y) + dy;
if (isWall(g, cx, cy)) continue;
best = Math.min(best, Math.hypot(x - Math.max(cx, Math.min(cx + 1, x)), y - Math.max(cy, Math.min(cy + 1, y))));
}
if (best > worst) worst = best;
}
}
return worst;
}
function isolatedTriangles(d) {
const g = d.grid, out = [];
const [zx, zy] = d.zone.center, r = d.zone.radius;
for (let y = 0; y < g.height; y++) for (let x = 0; x < g.width; x++) {
if (isWall(g, x, y)) continue;
const cents = [[x + 2 / 3, y + 1 / 3, 0], [x + 1 / 3, y + 2 / 3, 1]];
for (const [cx, cy, k] of cents) if (Math.hypot(cx - zx, cy - zy) <= r) out.push([x, y, k]);
}
return out;
}
const key = (p) => p.map((q) => q[0] + ',' + q[1]).join(';');
export function metrics(d) {
const g = d.grid;
const sw = d.swarm.agents, bl = d.blocked.agents, si = d.singles;
const goals = new Set(sw.map((a) => a.goal.join(','))).size;
const per = sw.map((a, i) => {
const b = bl[i], s = si[i];
return {
id: a.id, from: a.from, goal: a.goal,
swarm: { wp: a.path.length, len: pathLength(a.path), cut: cornerCut(g, a.path) },
blocked: { wp: b.path.length, len: pathLength(b.path), rerouted: key(b.path) !== key(a.path) },
single: { wp: s.path.length, len: pathLength(s.path), cut: cornerCut(g, s.path) },
};
});
const sum = (arr, f) => arr.reduce((t, x) => t + f(x), 0);
const iso = isolatedTriangles(d);
return {
agents: sw.length, goals, triangles: d.mesh.triangles, trianglesAfter: d.mesh.trianglesAfterObstacle,
foundSwarm: sw.filter((a) => a.found).length, foundBlocked: bl.filter((a) => a.found).length,
foundSingle: si.filter((a) => a.found).length,
rerouted: per.filter((p) => p.blocked.rerouted).length,
isolated: iso.length, isolatedCells: iso,
swarmCorner: Math.max(...per.map((p) => p.swarm.cut)),
singleCorner: Math.max(...per.map((p) => p.single.cut)),
totals: {
swarm: { wp: sum(per, (p) => p.swarm.wp) / per.length, len: sum(per, (p) => p.swarm.len) },
blocked: { wp: sum(per, (p) => p.blocked.wp) / per.length, len: sum(per, (p) => p.blocked.len) },
single: { wp: sum(per, (p) => p.single.wp) / per.length, len: sum(per, (p) => p.single.len) },
},
tile: { steps: d.tile.steps, reach: d.tile.reach.length, visible: d.tile.visible.length, cover: d.tile.cover.length, origin: d.tile.origin },
per,
};
}
export function limitFacts(m) {
return { swarmCorner: f2(m.swarmCorner), singleCorner: f2(m.singleCorner) };
}
const MODES = [
{ id: 'swarm', label: 'Swarm tick' },
{ id: 'blocked', label: 'Doorway closed' },
{ id: 'singles', label: 'Single queries' },
{ id: 'tile', label: 'Tile lane' },
];
export function statsFor(mode, m) {
const t = (label, value) => ({ label, value: String(value) });
if (mode === 'blocked') return [t('Triangles isolated', m.isolated), t('Agents re-routed', `${m.rerouted} of ${m.agents}`), t('Paths found', `${m.foundBlocked} of ${m.agents}`), t('Mesh triangles', m.trianglesAfter)];
if (mode === 'singles') return [t('Paths found', `${m.foundSingle} of ${m.agents}`), t('Mean waypoints', f1(m.totals.single.wp)), t('Corner cut, single', f2(m.singleCorner) + ' cell'), t('Corner cut, swarm', f2(m.swarmCorner) + ' cell')];
if (mode === 'tile') return [t('Reachable tiles', m.tile.reach), t('In line of sight', m.tile.visible), t('Beside a wall', m.tile.cover), t('Movement steps', m.tile.steps)];
return [t('Agents', m.agents), t('Paths found', `${m.foundSwarm} of ${m.agents}`), t('Distinct goals', m.goals), t('Mesh triangles', m.triangles)];
}
export function readoutFor(mode, m, sel) {
if (sel >= 0 && mode !== 'tile') {
const p = m.per[sel];
const v = mode === 'blocked' ? p.blocked : mode === 'singles' ? p.single : p.swarm;
return `Agent ${p.id}: ${v.wp} waypoints, ${f1(v.len)} cells long${mode === 'blocked' ? (v.rerouted ? ', route changed' : ', route unchanged') : ''}. From (${p.from.join(', ')}) to (${p.goal.join(', ')}).`;
}
if (mode === 'blocked') return `A circular zone closes the upper doorway in the first wall. The mesh keeps its ${m.trianglesAfter} triangles and isolates ${m.isolated} of them in place. ${m.rerouted} of ${m.agents} agents take a new route; dashed lines show the routes they left.`;
if (mode === 'singles') return `The same ${m.agents} start and goal pairs as single-agent queries. Paths hug corners more tightly: the deepest corner cut is ${f2(m.singleCorner)} of a cell, against ${f2(m.swarmCorner)} for the swarm tick.`;
if (mode === 'tile') return `The bake-free tile lane from tile (${m.tile.origin.join(', ')}): ${m.tile.reach} tiles within ${m.tile.steps} steps, ${m.tile.visible} tiles in line of sight, ${m.tile.cover} reachable tiles beside a wall. Brighter tiles cost more steps.`;
return `${m.agents} agents head for ${m.goals} goals across two walls with two doorways each. Agents that share a goal share one solve. ${m.foundSwarm} of ${m.agents} paths found.`;
}
export function staticSvg(d) {
const g = d.grid, S = 10, W = g.width * S, H = g.height * S;
const walls = [];
for (let y = 0; y < g.height; y++) {
let x = 0;
while (x < g.width) {
if (!isWall(g, x, y)) { x++; continue; }
let x2 = x; while (x2 < g.width && isWall(g, x2, y)) x2++;
walls.push(`<rect x="${x * S}" y="${y * S}" width="${(x2 - x) * S}" height="${S}"/>`);
x = x2;
}
}
const pts = (p) => p.map((q) => `${f1(q[0] * S)},${f1(q[1] * S)}`).join(' ');
const lines = d.swarm.agents.map((a, i) => `<polyline class="a${i % 7}" points="${pts(a.path)}"/>`).join('');
const goals = [...new Set(d.swarm.agents.map((a) => a.goal.join(',')))].map((s) => { const [x, y] = s.split(',').map(Number); return `<circle class="goal" cx="${x * S}" cy="${y * S}" r="${S * 0.45}"/>`; }).join('');
const dots = d.swarm.agents.map((a, i) => `<circle class="a${i % 7}" cx="${a.from[0] * S}" cy="${a.from[1] * S}" r="${S * 0.22}"/>`).join('');
return `<svg class="dia-plan" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="tf-plan-t tf-plan-d" preserveAspectRatio="xMidYMid meet">
<title id="tf-plan-t">Plan view of the level with the swarm paths</title>
<desc id="tf-plan-d">A ${g.width} by ${g.height} level with two walls, each with two doorways. ${d.swarm.agents.length} agents start in the left room and Tracer returns a path for each toward the goals in the right rooms.</desc>
<g class="walls">${walls.join('')}</g><g class="paths">${lines}</g><g class="goals">${goals}</g><g class="starts">${dots}</g></svg>`;
}
export function controlsHtml(id, m) {
const tabs = MODES.map((md, i) => `<button type="button" role="tab" id="${id}-tab-${md.id}" data-mode="${md.id}" aria-selected="${i === 0}" aria-controls="${id}-stage" tabindex="${i === 0 ? 0 : -1}">${esc(md.label)}</button>`).join('');
const opts = ['<option value="-1">All agents</option>'].concat(m.per.map((p, i) => `<option value="${i}">Agent ${p.id}</option>`)).join('');
const stats = statsFor('swarm', m).map((s) => `<div class="stat"><dt>${esc(s.label)}</dt><dd>${esc(s.value)}</dd></div>`).join('');
return {
tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="View" hidden>${tabs}</div>`,
controls: `<div class="dia-controls dia-ui" hidden>
<label class="dia-field">Turn <input type="range" min="-180" max="180" step="1" value="0" data-ctl="yaw"></label>
<label class="dia-field">Tilt <input type="range" min="0" max="65" step="1" value="54" data-ctl="tilt"></label>
<label class="dia-field">Focus <select data-ctl="agent">${opts}</select></label>
<label class="dia-check"><input type="checkbox" data-ctl="mesh" checked> Navmesh</label>
<button type="button" class="btn btn-ghost btn-sm" data-ctl="play" aria-pressed="true">Pause motion</button>
</div>
<p class="dia-hint dia-ui" hidden>Drag the view or use the arrow keys while it is focused to turn and tilt it.</p>
<p class="dia-readout dia-ui" data-ctl="readout" role="status" hidden>${esc(readoutFor('swarm', m, -1))}</p>
<dl class="stats dia-stats dia-ui" data-ctl="stats" hidden>${stats}</dl>`,
};
}
export function tablesHtml(d, m) {
const row = (cells, head) => '<tr>' + cells.map((c, i) => (i === 0 && head ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
const sum = [
['Swarm tick', `${m.foundSwarm} of ${m.agents}`, f1(m.totals.swarm.wp), f1(m.totals.swarm.len), 'none', f2(m.swarmCorner)],
['Doorway closed', `${m.foundBlocked} of ${m.agents}`, f1(m.totals.blocked.wp), f1(m.totals.blocked.len), `${m.rerouted} of ${m.agents}`, ''],
['Single queries', `${m.foundSingle} of ${m.agents}`, f1(m.totals.single.wp), f1(m.totals.single.len), 'none', f2(m.singleCorner)],
];
const agents = m.per.map((p) => [`Agent ${p.id}`, `(${p.from.join(', ')})`, `(${p.goal.join(', ')})`, p.swarm.wp, f1(p.swarm.len), p.blocked.wp, f1(p.blocked.len), p.blocked.rerouted ? 'yes' : 'no', p.single.wp, f1(p.single.len)]);
return `<details class="dia-data"><summary>Data behind the diagram</summary>
<div class="tbl-wrap"><table class="tbl"><caption>Totals by view, from ${m.agents} agents on a level baked to ${m.triangles} triangles</caption>
<thead><tr><th scope="col">View</th><th scope="col">Paths found</th><th scope="col">Mean waypoints</th><th scope="col">Total length (cells)</th><th scope="col">Agents re-routed</th><th scope="col">Deepest corner cut (cells)</th></tr></thead>
<tbody>${sum.map((r) => row(r, true)).join('')}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Per agent: start, goal and the path Tracer returned in each view</caption>
<thead><tr><th scope="col">Agent</th><th scope="col">Start</th><th scope="col">Goal</th><th scope="col">Swarm waypoints</th><th scope="col">Swarm length</th><th scope="col">Closed waypoints</th><th scope="col">Closed length</th><th scope="col">Re-routed</th><th scope="col">Single waypoints</th><th scope="col">Single length</th></tr></thead>
<tbody>${agents.map((r) => row(r, true)).join('')}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Tile lane from tile (${m.tile.origin.join(', ')})</caption>
<thead><tr><th scope="col">Measure</th><th scope="col">Value</th></tr></thead>
<tbody>${row(['Tiles reachable within ' + m.tile.steps + ' steps', m.tile.reach], true)}${row(['Tiles in line of sight', m.tile.visible], true)}${row(['Reachable tiles beside a wall', m.tile.cover], true)}</tbody></table></div>
</details>`;
}
export function mount(root, d) {
const m = metrics(d), g = d.grid;
const stage = root.querySelector('.dia-stage');
const id = root.dataset.diagramId || 'tf';
const reduced = prefersReducedMotion();
const tok = readTokens(root);
const palette = tok.spectrum;
stage.textContent = '';
const canvas = document.createElement('canvas');
canvas.className = 'dia-canvas';
canvas.setAttribute('aria-hidden', 'true');
stage.append(canvas);
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const ctx = canvas.getContext('2d');
const ui = {
yaw: root.querySelector('[data-ctl="yaw"]'), tilt: root.querySelector('[data-ctl="tilt"]'), agent: root.querySelector('[data-ctl="agent"]'),
mesh: root.querySelector('[data-ctl="mesh"]'), play: root.querySelector('[data-ctl="play"]'),
readout: root.querySelector('[data-ctl="readout"]'), stats: root.querySelector('[data-ctl="stats"]'),
};
const narrow = stage.getBoundingClientRect().width < 560;
const S = { mode: 'swarm', yaw: narrow ? 90 : 0, tilt: narrow ? 52 : 54, sel: -1, playing: !reduced, moved: false, mesh: true, userPlay: false };
const anim = () => S.playing && (!reduced || S.userPlay);
ui.yaw.value = S.yaw; ui.tilt.value = S.tilt;
ui.play.textContent = S.playing ? 'Pause motion' : 'Play motion';
ui.play.setAttribute('aria-pressed', String(S.playing));
const floor = [], walls = [];
for (let y = 0; y < g.height; y++) for (let x = 0; x < g.width; x++) (isWall(g, x, y) ? walls : floor).push([x, y]);
const reachCost = new Map(d.tile.reach.map(([x, y, c]) => [x + ',' + y, c]));
const visible = new Set(d.tile.visible.map(([x, y]) => x + ',' + y));
const cover = new Set(d.tile.cover.map(([x, y]) => x + ',' + y));
const isoSet = new Set(m.isolatedCells.map(([x, y, k]) => x + ',' + y + ',' + k));
const lens = {
swarm: d.swarm.agents.map((a) => pathLength(a.path)),
blocked: d.blocked.agents.map((a) => pathLength(a.path)),
singles: d.singles.map((a) => pathLength(a.path)),
};
const setFor = (mode) => (mode === 'blocked' ? d.blocked.agents : mode === 'singles' ? d.singles : d.swarm.agents);
const col = (i) => palette[i % palette.length];
const wallTop = mix(tok.border2, tok.soft, 0.22), wallSideA = mix(tok.surface2, tok.border2, 0.75), wallSideB = mix(tok.surface, tok.border2, 0.55);
const floorCol = mix(tok.bg2, tok.surface2, 0.55);
function along(path, dist) {
let rem = dist;
for (let i = 1; i < path.length; i++) {
const L = Math.hypot(path[i][0] - path[i - 1][0], path[i][1] - path[i - 1][1]);
if (rem <= L || i === path.length - 1) { const t = L ? Math.min(1, rem / L) : 0; return [path[i - 1][0] + (path[i][0] - path[i - 1][0]) * t, path[i - 1][1] + (path[i][1] - path[i - 1][1]) * t]; }
rem -= L;
}
return path[path.length - 1];
}
function draw(time) {
const { w, h, dpr } = fitCanvas(canvas);
ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
ctx.clearRect(0, 0, w, h);
const sway = anim() && !S.moved ? 4 * Math.sin(time * 0.35) : 0;
const cam = makeCamera(g.width / 2, g.height / 2, S.yaw + sway, S.tilt);
let u0 = 1e9, u1 = -1e9, v0 = 1e9, v1 = -1e9;
for (const [x, y] of [[0, 0], [g.width, 0], [0, g.height], [g.width, g.height]]) for (const z of [0, WALL_H]) {
const [u, v] = cam.p(x, y, z); u0 = Math.min(u0, u); u1 = Math.max(u1, u); v0 = Math.min(v0, v); v1 = Math.max(v1, v);
}
const pad = 14, s = Math.min((w - pad * 2) / (u1 - u0), (h - pad * 2) / (v1 - v0));
if (!(s > 4)) return false;
const ox = w / 2 - ((u0 + u1) / 2) * s, oy = h / 2 - ((v0 + v1) / 2) * s;
const P = (x, y, z = 0) => { const q = cam.p(x, y, z); return [ox + q[0] * s, oy + q[1] * s, q[2]]; };
ctx.lineWidth = 1;
const tile = S.mode === 'tile';
for (const [x, y] of floor) {
const a = P(x, y), b = P(x + 1, y), c = P(x + 1, y + 1), e = P(x, y + 1);
let fill = floorCol;
if (tile) {
const cost = reachCost.get(x + ',' + y);
if (cost !== undefined) fill = mix(floorCol, tok.accent, 0.28 + 0.6 * (1 - cost / (d.tile.steps + 1)));
else if (visible.has(x + ',' + y)) fill = mix(floorCol, tok.ok, 0.1);
}
ctx.fillStyle = fill;
ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.lineTo(c[0], c[1]); ctx.lineTo(e[0], e[1]); ctx.closePath(); ctx.fill();
ctx.strokeStyle = rgba(tok.border2, 0.35); ctx.stroke();
if (S.mesh && !tile) {
ctx.strokeStyle = rgba(tok.soft, 0.16); ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(c[0], c[1]); ctx.stroke();
}
if (S.mode === 'blocked') {
for (const k of [0, 1]) if (isoSet.has(x + ',' + y + ',' + k)) {
const tri = k === 0 ? [a, b, c] : [a, c, e];
ctx.fillStyle = rgba(tok.bad, 0.42); ctx.beginPath(); ctx.moveTo(tri[0][0], tri[0][1]); ctx.lineTo(tri[1][0], tri[1][1]); ctx.lineTo(tri[2][0], tri[2][1]); ctx.closePath(); ctx.fill();
}
}
if (tile && visible.has(x + ',' + y)) {
const cx = (a[0] + c[0]) / 2, cy = (a[1] + c[1]) / 2;
ctx.fillStyle = rgba(tok.ok, 0.85); ctx.beginPath(); ctx.arc(cx, cy, Math.max(1.2, s * 0.09), 0, 6.283); ctx.fill();
}
if (tile && cover.has(x + ',' + y)) {
const cx = (a[0] + c[0]) / 2, cy = (a[1] + c[1]) / 2, r = Math.max(2.4, s * 0.22);
ctx.strokeStyle = tok.warn; ctx.lineWidth = 1.6; ctx.strokeRect(cx - r, cy - r, r * 2, r * 2); ctx.lineWidth = 1;
}
}
if (S.mode === 'blocked') {
ctx.strokeStyle = tok.bad; ctx.lineWidth = 2; ctx.setLineDash([5, 4]); ctx.beginPath();
for (let i = 0; i <= 48; i++) { const t = (i / 48) * 6.2832; const q = P(d.zone.center[0] + Math.cos(t) * d.zone.radius, d.zone.center[1] + Math.sin(t) * d.zone.radius, 0.02); i ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]); }
ctx.stroke(); ctx.setLineDash([]); ctx.lineWidth = 1;
}
const set = setFor(S.mode);
if (tile) {
const q = P(d.tile.origin[0] + 0.5, d.tile.origin[1] + 0.5, 0.04);
ctx.fillStyle = tok.ink; ctx.beginPath(); ctx.arc(q[0], q[1], Math.max(3, s * 0.3), 0, 6.283); ctx.fill();
ctx.strokeStyle = tok.accent; ctx.lineWidth = 2; ctx.beginPath(); ctx.arc(q[0], q[1], Math.max(5, s * 0.5), 0, 6.283); ctx.stroke(); ctx.lineWidth = 1;
} else {
if (S.mode === 'blocked') {
ctx.setLineDash([3, 4]); ctx.lineWidth = 1.5;
d.swarm.agents.forEach((a, i) => {
if (!m.per[i].blocked.rerouted || (S.sel >= 0 && S.sel !== i)) return;
ctx.strokeStyle = rgba(tok.faint, 0.7); ctx.beginPath();
a.path.forEach((pt, j) => { const q = P(pt[0], pt[1], 0.02); j ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]); });
ctx.stroke();
});
ctx.setLineDash([]);
}
set.forEach((a, i) => {
const dim = S.sel >= 0 && S.sel !== i;
ctx.strokeStyle = rgba(col(i), dim ? 0.2 : S.sel === i ? 1 : 0.8);
ctx.lineWidth = S.sel === i ? 3.4 : 1.8; ctx.lineJoin = 'round'; ctx.beginPath();
a.path.forEach((pt, j) => { const q = P(pt[0], pt[1], 0.03); j ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]); });
ctx.stroke();
});
ctx.lineWidth = 1;
const seen = new Set();
for (const a of set) {
const k = a.goal.join(','); if (seen.has(k)) continue; seen.add(k);
const q = P(a.goal[0], a.goal[1], 0.04), pulse = anim() ? 0.12 * Math.sin(time * 3) : 0;
ctx.strokeStyle = tok.ink; ctx.lineWidth = 2; ctx.beginPath(); ctx.arc(q[0], q[1], s * (0.42 + pulse), 0, 6.283); ctx.stroke();
ctx.fillStyle = rgba(tok.ink, 0.9); ctx.beginPath(); ctx.arc(q[0], q[1], s * 0.12, 0, 6.283); ctx.fill(); ctx.lineWidth = 1;
}
}
const items = [];
for (const [x, y] of walls) {
const vis = [[0, 1], [0, -1], [1, 0], [-1, 0]].filter(([nx, ny]) => { const ax = x + nx, ay = y + ny; return ax < 0 || ay < 0 || ax >= g.width || ay >= g.height || !isWall(g, ax, ay); });
const q = cam.p(x + 0.5, y + 0.5, 0);
items.push({ k: 'w', x, y, depth: q[2], vis });
}
const agentPos = [];
if (!tile) {
set.forEach((a, i) => {
if (!a.found) return;
const L = lens[S.mode][i], cycle = L + 2.2, tt = S.playing ? (time * SPEED + i * 1.7) % cycle : 0;
const pos = along(a.path, Math.min(tt, L));
const q = cam.p(pos[0], pos[1], 0.28);
items.push({ k: 'a', i, pos, depth: q[2], arrived: tt >= L });
});
}
items.sort((p, q) => p.depth - q.depth);
for (const it of items) {
if (it.k === 'w') {
const { x, y } = it;
for (const [nx, ny] of it.vis) {
const dn = cam.dir(nx, ny);
if (dn[1] <= 0.001 || S.tilt < 1) continue;
let A, B;
if (nx === 0 && ny === 1) { A = [x, y + 1]; B = [x + 1, y + 1]; }
else if (nx === 0 && ny === -1) { A = [x, y]; B = [x + 1, y]; }
else if (nx === 1) { A = [x + 1, y]; B = [x + 1, y + 1]; }
else { A = [x, y]; B = [x, y + 1]; }
const p1 = P(A[0], A[1], 0), p2 = P(B[0], B[1], 0), p3 = P(B[0], B[1], WALL_H), p4 = P(A[0], A[1], WALL_H);
ctx.fillStyle = dn[0] > 0 ? wallSideA : wallSideB;
ctx.beginPath(); ctx.moveTo(p1[0], p1[1]); ctx.lineTo(p2[0], p2[1]); ctx.lineTo(p3[0], p3[1]); ctx.lineTo(p4[0], p4[1]); ctx.closePath(); ctx.fill();
}
const a = P(x, y, WALL_H), b = P(x + 1, y, WALL_H), c = P(x + 1, y + 1, WALL_H), e = P(x, y + 1, WALL_H);
ctx.fillStyle = wallTop; ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.lineTo(c[0], c[1]); ctx.lineTo(e[0], e[1]); ctx.closePath(); ctx.fill();
ctx.strokeStyle = rgba(tok.ink, 0.07); ctx.stroke();
} else {
const q = P(it.pos[0], it.pos[1], 0.28), r = Math.max(2.6, s * 0.2), dim = S.sel >= 0 && S.sel !== it.i;
const sh = P(it.pos[0], it.pos[1], 0.02);
ctx.fillStyle = 'rgba(0,0,0,0.35)'; ctx.beginPath(); ctx.ellipse(sh[0], sh[1], r * 0.9, r * 0.5, 0, 0, 6.283); ctx.fill();
ctx.globalAlpha = dim ? 0.3 : it.arrived ? 0.55 : 1;
ctx.fillStyle = col(it.i); ctx.beginPath(); ctx.arc(q[0], q[1], r, 0, 6.283); ctx.fill();
if (S.sel === it.i) { ctx.strokeStyle = tok.ink; ctx.lineWidth = 2; ctx.stroke(); ctx.lineWidth = 1; }
ctx.globalAlpha = 1;
}
}
return anim();
}
const lp = loop(draw);
function refreshText() {
ui.readout.textContent = readoutFor(S.mode, m, S.sel);
ui.stats.innerHTML = statsFor(S.mode, m).map((x) => `<div class="stat"><dt>${esc(x.label)}</dt><dd>${esc(x.value)}</dd></div>`).join('');
ui.agent.disabled = S.mode === 'tile';
}
function changed() { lp.redraw(); lp.kick(); }
tablist(root.querySelector('[role="tablist"]'), (mode) => { S.mode = mode; refreshText(); changed(); });
ui.yaw.addEventListener('input', () => { S.yaw = Number(ui.yaw.value); S.moved = true; changed(); });
ui.tilt.addEventListener('input', () => { S.tilt = Number(ui.tilt.value); S.moved = true; changed(); });
ui.agent.addEventListener('change', () => { S.sel = Number(ui.agent.value); refreshText(); changed(); });
ui.mesh.addEventListener('change', () => { S.mesh = ui.mesh.checked; changed(); });
ui.play.addEventListener('click', () => {
S.playing = !S.playing; if (S.playing) S.userPlay = true; ui.play.textContent = S.playing ? 'Pause motion' : 'Play motion'; ui.play.setAttribute('aria-pressed', String(S.playing)); changed();
});
let drag = null;
canvas.addEventListener('pointerdown', (e) => { drag = { x: e.clientX, y: e.clientY, type: e.pointerType }; canvas.setPointerCapture(e.pointerId); });
canvas.addEventListener('pointermove', (e) => {
if (!drag) return;
const dx = e.clientX - drag.x, dy = e.clientY - drag.y; drag.x = e.clientX; drag.y = e.clientY;
S.yaw = ((S.yaw + dx * 0.4 + 540) % 360) - 180; S.moved = true;
if (drag.type === 'mouse') S.tilt = Math.max(0, Math.min(65, S.tilt - dy * 0.3));
ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed();
});
const end = () => { drag = null; };
canvas.addEventListener('pointerup', end); canvas.addEventListener('pointercancel', end);
stage.addEventListener('keydown', (e) => {
const k = e.key; let used = true;
if (k === 'ArrowLeft') S.yaw = ((S.yaw - 5 + 540) % 360) - 180;
else if (k === 'ArrowRight') S.yaw = ((S.yaw + 5 + 540) % 360) - 180;
else if (k === 'ArrowUp') S.tilt = Math.min(65, S.tilt + 3);
else if (k === 'ArrowDown') S.tilt = Math.max(0, S.tilt - 3);
else used = false;
if (used) { e.preventDefault(); S.moved = true; ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed(); }
});
new ResizeObserver(() => lp.redraw()).observe(stage);
new IntersectionObserver((es) => lp.setVisible(es[0].isIntersecting)).observe(root);
document.addEventListener('visibilitychange', () => lp.kick());
refreshText();
lp.redraw(); lp.kick();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="tracer-field"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (!blob) return;
mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export function verify(d, body) {
const m = metrics(d), out = [];
if (!/role="tablist"/.test(body)) out.push('no tablist');
if (!body.includes(`${m.foundSwarm} of ${m.agents}`)) out.push('table does not show the swarm result');
return out;
}
