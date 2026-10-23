import { hasDom, prefersReducedMotion, tablist, loop } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = (n) => Math.round(n).toLocaleString('en-GB');
const clamp = (x) => Math.max(0, Math.min(1, x));
const ph = (t, a, b) => clamp((t - a) / (b - a));
const ease = (x) => x * x * (3 - 2 * x);
const lerp = (a, b, u) => a + (b - a) * u;
const DURATION = 11000;
export function metrics(d) {
return { scenes: d.scenes.length, ids: d.scenes.map((s) => s.id) };
}
function pen() {
const g = [];
const api = {
g,
t: (x, y, s, cls = '', anchor = '') => g.push(`<text x="${(+x).toFixed(1)}" y="${(+y).toFixed(1)}" class="${cls}"${anchor ? ` text-anchor="${anchor}"` : ''}>${esc(s)}</text>`),
r: (x, y, w, h, cls = '', rx = 3, extra = '') => g.push(`<rect x="${(+x).toFixed(1)}" y="${(+y).toFixed(1)}" width="${Math.max(0, +w).toFixed(1)}" height="${Math.max(0, +h).toFixed(1)}" rx="${rx}" class="${cls}"${extra}/>`),
l: (x1, y1, x2, y2, cls = '') => g.push(`<line x1="${(+x1).toFixed(1)}" y1="${(+y1).toFixed(1)}" x2="${(+x2).toFixed(1)}" y2="${(+y2).toFixed(1)}" class="${cls}"/>`),
c: (x, y, r, cls = '') => g.push(`<circle cx="${(+x).toFixed(1)}" cy="${(+y).toFixed(1)}" r="${r}" class="${cls}"/>`),
};
return api;
}
function sceneRead(f, t, L) {
const p = pen(), { W, narrow } = L, pad = 18, N = narrow ? 30 : 48, cw = (W - 2 * pad) / (N + 1);
const second = t >= 0.6, pass = second ? ph(t, 0.64, 0.98) : ph(t, 0.03, 0.4);
const cells = second ? N + 1 : N;
const seen = Math.floor(pass * cells + 1e-9);
p.t(pad, 20, 'A scan: tuned DuckDB adds up every row, every time', 'hw-lane');
for (let i = 0; i < N; i++) p.r(pad + i * cw, 32, cw - 2, 20, 'hw-cell' + (i < seen ? ' seen' : ''), 2);
const drop = ease(ph(t, 0.48, 0.58));
if (t >= 0.48) p.r(pad + N * cw, lerp(-6, 32, drop), cw - 2, 20, 'hw-cell hw-new' + (second && seen > N ? ' seen' : ''), 2);
const rowsRead = pass >= 1 ? f.rows : Math.floor(pass * f.rows);
p.t(pad, 72, `${fmt(rowsRead)} rows read${pass >= 1 ? `, ${f.duckMs.toFixed(2)} ms` : ''}`, 'hw-count');
if (t >= 0.5 && t < 0.62) p.t(W - pad, 72, 'a new invoice lands', 'hw-note', 'end');
const y0 = narrow ? 150 : 128;
p.t(pad, y0 - 14, 'HoloDb: the total is kept up to date as rows are written', 'hw-lane');
const flash = ph(t, 0.55, 0.6) * (1 - ph(t, 0.62, 0.75));
const after = t >= 0.58;
const total = after ? f.totalCents + f.newCents : f.totalCents;
const bw = narrow ? W - 2 * pad : 330;
p.r(pad, y0, bw, 46, 'hw-box' + (flash > 0.05 ? ' flash' : ''), 6);
p.t(pad + 12, y0 + 29, fmt(total), 'hw-num');
p.t(pad + bw - 10, y0 + 29, 'cents', 'hw-note', 'end');
const cx = narrow ? pad : pad + bw + 20, cy = narrow ? y0 + 70 : y0 + 20;
p.t(cx, cy, `reads 1 number: ${f.holoMs.toFixed(2)} ms`, 'hw-count ok');
if (t >= 0.5) {
const a = ph(t, 0.5, 0.6);
p.l(pad + (N + 0.5) * cw, 54, pad + bw / 2, y0, 'hw-wire');
g_opacity(p, a);
}
if (t >= 0.58) p.t(cx, cy + 22, `the write pays: +${f.newCents} cents folded in, about ${f.writeRam} us in memory, ${f.writePaged} us on a paged table`, 'hw-note');
if (t >= 0.72) p.t(cx, cy + (narrow ? 44 : 44), `read again: ${f.holoMs.toFixed(2)} ms, new row included`, 'hw-note ok');
return { h: narrow ? 330 : 250, body: p.g.join('') };
}
function g_opacity(p, a) { const last = p.g.length - 1; p.g[last] = p.g[last].replace('class="hw-wire"', `class="hw-wire" opacity="${a.toFixed(2)}"`); }
function sceneBuild(f, t, L) {
const p = pen(), { W, narrow } = L, pad = 18;
const arrivals = [0.06, 0.16, 0.26, 0.52, 0.6];
const lx = pad, lw = narrow ? W - 2 * pad : 330;
p.t(lx, 20, 'The same question keeps arriving', 'hw-lane');
p.t(lx, 40, 'SELECT status, SUM(amount_cents) ... GROUP BY status', 'hw-mono');
arrivals.forEach((a, i) => {
const x = lx + 16 + i * 44, on = t >= a, scanned = i < 3;
p.c(x, 66, 8, 'hw-dot' + (on ? (scanned ? ' warn' : ' ok') : ''));
if (on) p.t(x, 92, scanned ? 'scan' : 'read', 'hw-tiny ' + (scanned ? 'warn' : 'ok'), 'middle');
});
const spent = [0, 0.3, 0.6, 0.9][arrivals.slice(0, 3).filter((a) => t >= a).length];
p.t(lx, 118, 'time spent scanning', 'hw-note');
p.r(lx, 124, lw, 12, 'hw-track', 6);
p.r(lx, 124, lw * Math.min(1, spent), 12, 'hw-fill warn', 6);
p.l(lx + lw * 0.75, 118, lx + lw * 0.75, 142, 'hw-mark');
p.t(lx + lw * 0.75, 154, 'cost to build', 'hw-tiny', 'middle');
const bp = ph(t, 0.28, 0.46);
const built = t >= 0.46 && t < 0.9, gone = t >= 0.9;
if (t >= 0.28) {
p.t(lx, 178, gone ? 'dropped: the writes cost more than it saves' : built ? 'summary built in the background, answering' : 'summary being built in the background', 'hw-note' + (gone ? ' warn' : built ? ' ok' : ''));
p.r(lx, 186, lw, 12, 'hw-track', 6);
p.r(lx, 186, lw * (gone ? 0 : bp), 12, 'hw-fill ok', 6);
}
if (t >= 0.66) {
const w = ph(t, 0.68, 0.9);
p.t(lx, 222, 'writes arrive: upkeep of the summary', 'hw-note');
p.r(lx, 228, lw, 12, 'hw-track', 6);
p.r(lx, 228, lw * 0.9 * w, 12, 'hw-fill warn', 6);
p.l(lx + lw * 0.6, 222, lx + lw * 0.6, 246, 'hw-mark');
p.t(lx + lw * 0.6, 258, 'what it saves', 'hw-tiny', 'middle');
}
const rx = narrow ? pad : 370, ry = narrow ? 282 : 20, rw = narrow ? W - 2 * pad : W - 370 - pad;
p.t(rx, ry, 'Measured, out of the box, 100,000,000 invoices', 'hw-lane');
p.t(rx, ry + 24, f.status.label, 'hw-mono');
p.t(rx, ry + 44, `first run ${f.status.first} ms, then ${f.status.after} ms`, 'hw-count ok');
p.t(rx, ry + 76, f.overdue.label, 'hw-mono');
p.t(rx, ry + 96, `${f.overdue.oob} ms after four runs: still a scan`, 'hw-count warn');
p.t(rx, ry + 116, `tuned DuckDB ${f.overdue.duck} ms; declared ${f.overdue.restored} ms`, 'hw-note');
return { h: narrow ? 420 : 280, body: p.g.join('') };
}
function sceneJoin(f, t, L) {
const p = pen(), { W, narrow } = L, pad = 18;
const seg = ['A', 'B', 'C'];
const cust = [['Customer 1', 0, 2], ['Customer 2', 0, 1.5], ['Customer 3', 0, 1.5], ['Customer 4', 1, 2], ['Customer 5', 1, 1], ['Customer 6', 2, 2]];
const mv = ease(ph(t, 0.35, 0.62));
const base = [5, 3, 2], moved = 1.5;
const val = [base[0] - moved * mv, base[1] + moved * mv, base[2]];
p.t(pad, 20, f.customers + ' customers, grouped by segment', 'hw-lane');
const rowH = 26, cy0 = 34;
cust.forEach((c, i) => {
const y = cy0 + i * rowH, isMoved = i === 2, segNow = isMoved && mv > 0.5 ? 1 : c[1];
p.r(pad, y, 112, rowH - 4, 'hw-box', 5);
p.t(pad + 8, y + 15, c[0], 'hw-note');
p.r(pad + 118, y, 30, rowH - 4, 'hw-seg s' + segNow + (isMoved && mv > 0 && mv < 1 ? ' moving' : ''), 5);
p.t(pad + 133, y + 15, seg[segNow], 'hw-tiny dark', 'middle');
});
const bx = narrow ? pad + 6 : 300, bw = narrow ? 70 : 90, gap = narrow ? 18 : 40, by = narrow ? 260 : 176, bh = narrow ? 80 : 120;
if (narrow) p.t(pad, 202, 'Totals kept per segment', 'hw-lane'); else p.t(bx, 20, 'Totals kept per segment, already grouped', 'hw-lane');
const top = narrow ? 214 : 34;
seg.forEach((s, i) => {
const x = bx + i * (bw + gap), h = (val[i] / 5) * bh;
p.r(x, by - bh, bw, bh, 'hw-track', 4);
p.r(x, by - h, bw, h, 'hw-bar s' + i, 4);
p.t(x + bw / 2, by + 16, 'segment ' + s, 'hw-note', 'middle');
});
if (mv > 0 && mv < 1) {
const x1 = bx + bw / 2, x2 = bx + (bw + gap) + bw / 2, u = mv;
p.c(lerp(x1, x2, u), by - bh - 10 - Math.sin(u * Math.PI) * 18, 6, 'hw-dot warn');
}
const tx = narrow ? pad : 300, ty = narrow ? 308 + 0 : 232;
void top;
const yy = narrow ? by + 40 : by + 34;
p.t(narrow ? pad : bx, yy, `revenue by segment reads ${seg.length} numbers: ${f.holoMs.toFixed(2)} ms`, 'hw-count ok');
p.t(narrow ? pad : bx, yy + 20, `not the ${fmt(f.rows)} invoice rows`, 'hw-note');
if (mv >= 1) p.t(narrow ? pad : bx, yy + 40, 'one customer moved: only its total moved, exactly', 'hw-note ok');
void tx; void ty;
return { h: narrow ? 400 : 290, body: p.g.join('') };
}
function sceneOpen(f, t, L) {
const p = pen(), { W, narrow } = L, pad = 18, tw = W - 2 * pad;
const fill1 = ease(ph(t, 0.02, 0.45));
p.t(pad, 20, 'Built once, with its structures', 'hw-lane');
p.r(pad, 30, tw, 16, 'hw-track', 8);
p.r(pad, 30, tw * fill1, 16, 'hw-fill', 8);
p.t(pad, 66, `${(f.loadSeconds * fill1).toFixed(1)} s`, 'hw-count');
const files = f.files;
files.forEach((fl, i) => {
const on = t >= 0.46 + i * 0.03, cols = narrow ? 2 : 4, cw = tw / cols, x = pad + (i % cols) * cw, y = 82 + Math.floor(i / cols) * 36;
if (on) { p.r(x, y, cw - 8, 30, 'hw-box', 5); p.t(x + 8, y + 13, fl.name, 'hw-tiny'); p.t(x + 8, y + 25, fl.size, 'hw-tiny ok'); }
});
const y2 = narrow ? 178 : 168;
p.t(pad, y2, 'Opened again from those files', 'hw-lane');
p.r(pad, y2 + 10, tw, 16, 'hw-track', 8);
const fill2 = ease(ph(t, 0.6, 0.66));
p.r(pad, y2 + 10, Math.max(fill2 > 0 ? 3 : 0, tw * (f.openSeconds / f.loadSeconds) * fill2), 16, 'hw-fill ok', 8);
if (t >= 0.66) p.t(pad, y2 + 46, `${f.openSeconds.toFixed(2)} s`, 'hw-count ok');
const checks = ['checksums match', 'same change counter', 'same row count', 'totals cross-checked against the data'];
checks.forEach((c, i) => { if (t >= 0.7 + i * 0.05) p.t(pad + (narrow ? 0 : (i % 2) * (tw / 2)), y2 + 70 + (narrow ? i * 18 : Math.floor(i / 2) * 18), '✓ ' + c, 'hw-note ok'); });
if (t >= 0.92) p.t(pad, y2 + (narrow ? 154 : 126), `then all 16 queries, cold: ${f.coldTotal.toFixed(2)} s in total`, 'hw-count ok');
return { h: narrow ? 400 : 330, body: p.g.join('') };
}
function sceneExact(f, t, L) {
const p = pen(), { W, narrow } = L, pad = 18;
const target = fmt(f.total).split('');
const engines = f.engines;
const cols = narrow ? 1 : 3, cw = (W - 2 * pad) / cols;
p.t(pad, 20, 'Total invoiced, in cents, on 100,000,000 invoices', 'hw-lane');
engines.forEach((e, k) => {
const x = pad + (narrow ? 0 : k * cw), y = narrow ? 36 + k * 40 : 38;
p.t(x, y + 8, e, 'hw-note');
const digits = target.map((ch, i) => {
if (ch === ',') return ',';
const settle = 0.12 + (i / target.length) * 0.3 + k * 0.03;
return t >= settle ? ch : String((Number(ch) + Math.floor(t * 60) + i * 3 + k) % 10);
}).join('');
p.t(x + (narrow ? 118 : 0), narrow ? y + 8 : y + 32, digits, 'hw-num small');
if (t >= 0.5) p.t(x + (narrow ? W - 2 * pad : cw - 14), narrow ? y + 8 : y + 52, '✓', 'hw-ok-mark', 'end');
});
const y0 = narrow ? 168 : 112;
p.t(pad, y0 - 8, 'Result fingerprint of each of the 16 queries', 'hw-lane');
const gc = narrow ? 4 : 8, chw = (W - 2 * pad) / gc;
f.hashes.forEach((h, i) => {
const x = pad + (i % gc) * chw, y = y0 + Math.floor(i / gc) * 30, on = t >= 0.55 + i * 0.025;
p.r(x, y, chw - 6, 24, 'hw-chip' + (on ? ' ok' : ''), 5);
p.t(x + (chw - 6) / 2, y + 16, h.slice(0, 6).toLowerCase(), 'hw-tiny', 'middle');
});
const cnt = f.hashes.filter((_, i) => t >= 0.55 + i * 0.025).length;
p.t(pad, y0 + (narrow ? 4 : 2) * 30 + 22, `${cnt} of ${f.hashes.length} identical on all four engines`, 'hw-count' + (cnt === f.hashes.length ? ' ok' : ''));
return { h: narrow ? 380 : 250, body: p.g.join('') };
}
function sceneDisk(f, t, L) {
const p = pen(), { W, narrow } = L, pad = 18;
const cols = narrow ? 8 : 16, rows = narrow ? 5 : 3, N = cols * rows, cw = (W - 2 * pad) / cols;
p.t(pad, 20, 'A data file in 8 KB pages, each with its own checksum', 'hw-lane');
const flips = (narrow ? [4, 13, 19, 27, 36] : [6, 21, 27, 38, 44]);
const stop = Math.floor(N * 0.62);
const cursor = Math.floor(ph(t, 0.15, 0.62) * stop);
const flipAt = (i) => t >= 0.08 + (flips.indexOf(i) * 0.015);
let reported = null;
for (let i = 0; i < N; i++) {
const x = pad + (i % cols) * cw, y = 32 + Math.floor(i / cols) * 30;
const fl = flips.includes(i) && flipAt(i), read = i < cursor && t > 0.15;
const hit = fl && read;
if (hit && reported === null) reported = i;
p.r(x, y, cw - 4, 24, 'hw-page' + (hit ? ' bad' : fl ? ' flipped' : read ? ' read' : ''), 3);
}
if (t >= 0.15 && t < 0.66) { const x = pad + (cursor % cols) * cw, y = 32 + Math.floor(cursor / cols) * 30; p.r(x - 1, y - 1, cw - 2, 26, 'hw-cursor', 4); }
const y1 = 32 + rows * 30 + 14;
if (reported !== null) p.t(pad, y1, `page ${reported + 1}: checksum mismatch, read refused with an error that names the page`, 'hw-count warn');
else p.t(pad, y1, 'reading...', 'hw-note');
if (t >= 0.7) {
p.t(pad, y1 + 24, `${f.flipped} bits flipped in a ${f.sizeMb} MB data file: ${f.reported} reported by page, ${f.unread} in pages never read, ${f.wrong} wrong answers`, 'hw-count ok');
p.t(pad, y1 + 44, 'amber pages are damaged but were never read; red pages were read and refused', 'hw-note');
}
return { h: narrow ? 300 : 230, body: p.g.join('') };
}
const DRAW = { read: sceneRead, build: sceneBuild, join: sceneJoin, open: sceneOpen, exact: sceneExact, disk: sceneDisk };
export function sceneSvg(d, id, t, narrow = false, uid = 'hw') {
const s = d.scenes.find((x) => x.id === id);
const L = { W: narrow ? 360 : 700, narrow };
const out = DRAW[id](s.fig, clamp(t), L);
return `<svg class="hw-svg" viewBox="0 0 ${L.W} ${out.h}" role="img" aria-labelledby="${uid}-t"><title id="${uid}-t">${esc(s.tab)}: ${esc(s.promise)} Illustrative motion.</title>${out.body}</svg>`;
}
export function staticSvg(d) { return sceneSvg(d, d.scenes[0].id, 1, false, 'hw-' + d.id); }
export function controlsHtml(id, m, d) {
const tabs = d.scenes.map((s, k) => `<button type="button" role="tab" id="${id}-tab-${s.id}" data-mode="${s.id}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(s.tab)}</button>`).join('');
const s0 = d.scenes[0];
return {
tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="How HoloDb gets its wins" hidden>${tabs}</div>`,
controls: `<div class="hw-copy"><p class="hw-promise" data-ctl="promise">${esc(s0.promise)}</p><p class="hw-how" data-ctl="how">${esc(s0.how.join(' '))}</p></div>
<div class="dia-controls dia-ui" hidden><button type="button" class="chip" data-ctl="play" aria-pressed="false">Play</button><label class="dia-field">Scrub <input type="range" data-ctl="time" min="0" max="1000" value="1000" aria-label="Position in the animation"></label></div>
<p class="dia-readout hw-caption" data-ctl="readout" role="status">${esc(s0.caption)}</p>`,
};
}
export function tablesHtml(d) {
const rows = d.scenes.map((s) => `<tr><th scope="row">${esc(s.tab)}</th><td>${esc(s.promise)}</td><td>${esc(s.how.join(' '))}</td><td>${esc(s.caption)}</td></tr>`).join('');
return `<details class="dia-data"><summary>The scenes in words</summary><div class="tbl-wrap"><table class="tbl"><caption>Each scene: the promise, how it works, and the measured condition</caption><thead><tr><th scope="col">Scene</th><th scope="col">Promise</th><th scope="col">How</th><th scope="col">Measured, and the condition</th></tr></thead><tbody>${rows}</tbody></table></div></details>`;
}
export function verify(d, body) {
const out = [];
for (const s of d.scenes) {
if (!DRAW[s.id]) out.push('no drawing for ' + s.id);
if (!s.promise || s.how.length !== 2 || !s.caption) out.push(`${s.id}: needs a promise, two how sentences and a caption`);
if (!/illustrative/i.test(s.caption)) out.push(`${s.id}: caption does not say the motion is illustrative`);
}
if ((body.match(/<tbody>[\s\S]*?<\/tbody>/) || [''])[0].split('<tr>').length - 1 !== d.scenes.length) out.push('scene table row count');
return out;
}
export const interactiveStage = false;
export function mount(root, d) {
const reduced = prefersReducedMotion(), stage = root.querySelector('.dia-stage');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const q = (s) => root.querySelector(s);
const S = { id: d.scenes[0].id, t: (reduced || typeof IntersectionObserver !== 'function') ? 1 : 0, playing: false, start: 0, narrow: stage.getBoundingClientRect().width < 520 };
const play = q('[data-ctl=play]'), range = q('[data-ctl=time]');
const draw = () => { stage.innerHTML = sceneSvg(d, S.id, S.t, S.narrow, 'hw-' + d.id); range.value = String(Math.round(S.t * 1000)); };
const lp = loop((now) => {
if (S.playing) { S.t = clamp((performance.now() - S.start) / DURATION); if (S.t >= 1) { S.playing = false; play.textContent = 'Replay'; play.setAttribute('aria-pressed', 'false'); } }
draw();
return S.playing;
});
function start() { S.t = 0; S.start = performance.now(); S.playing = true; play.setAttribute('aria-pressed', 'true'); play.textContent = 'Pause'; lp.kick(); }
function pick(id) {
const s = d.scenes.find((x) => x.id === id); S.id = id; S.playing = false;
q('[data-ctl=promise]').textContent = s.promise; q('[data-ctl=how]').textContent = s.how.join(' '); q('[data-ctl=readout]').textContent = s.caption;
play.textContent = 'Replay'; play.setAttribute('aria-pressed', 'false');
if (reduced) { S.t = 1; draw(); } else start();
}
tablist(q('[role="tablist"]'), (id) => pick(id));
play.addEventListener('click', () => {
if (S.playing) { S.playing = false; play.textContent = 'Play'; play.setAttribute('aria-pressed', 'false'); } else start();
});
range.addEventListener('input', () => { S.playing = false; S.t = Number(range.value) / 1000; play.textContent = 'Play'; play.setAttribute('aria-pressed', 'false'); draw(); });
if (typeof ResizeObserver === 'function') new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== S.narrow) { S.narrow = n; draw(); } }).observe(stage);
draw();
if (!reduced && typeof IntersectionObserver === 'function') {
let started = false;
new IntersectionObserver((es) => { lp.setVisible(es.some((e) => e.isIntersecting)); if (!started && es.some((e) => e.isIntersecting)) { started = true; start(); } }, { threshold: 0.4 }).observe(stage);
}
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="holodb-how"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
