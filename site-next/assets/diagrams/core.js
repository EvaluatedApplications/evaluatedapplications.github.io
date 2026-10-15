export const hasDom = typeof document !== 'undefined';
export function prefersReducedMotion() {
return hasDom && typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}
export function readTokens(el) {
const cs = getComputedStyle(el);
const v = (n, d) => (cs.getPropertyValue(n).trim() || d);
return {
bg: v('--bg', '#050608'), bg2: v('--bg-2', '#090a0e'), surface: v('--surface', '#0d0f16'), surface2: v('--surface-2', '#12151d'),
border: v('--border', '#1e2430'), border2: v('--border-2', '#2c3444'),
ink: v('--ink', '#e9ecf3'), soft: v('--ink-soft', '#98a2b3'), faint: v('--ink-faint', '#7c869c'),
accent: v('--accent', '#8b7dff'), accentInk: v('--accent-ink', '#b3a9ff'),
ok: v('--ok', '#7bd86a'), warn: v('--warn', '#e6b450'), bad: v('--bad', '#f0796a'),
spectrum: [1, 2, 3, 4, 5, 6, 7].map((i) => v('--spectrum-' + i, '#8b7dff')),
cat: v('--c-tracer', '#f0796a'),
};
}
export function hexToRgb(h) {
const s = String(h).trim();
if (s[0] === '#') {
const n = s.length === 4 ? s.slice(1).split('').map((c) => c + c).join('') : s.slice(1);
return [parseInt(n.slice(0, 2), 16), parseInt(n.slice(2, 4), 16), parseInt(n.slice(4, 6), 16)];
}
const m = s.match(/rgba?\(([^)]+)\)/);
if (m) return m[1].split(',').slice(0, 3).map((x) => Number(x));
return [128, 128, 128];
}
export const rgba = (c, a) => { const [r, g, b] = hexToRgb(c); return `rgba(${r},${g},${b},${a})`; };
export function mix(a, b, t) {
const A = hexToRgb(a), B = hexToRgb(b);
return `rgb(${Math.round(A[0] + (B[0] - A[0]) * t)},${Math.round(A[1] + (B[1] - A[1]) * t)},${Math.round(A[2] + (B[2] - A[2]) * t)})`;
}
export function makeCamera(cx, cy, yawDeg, tiltDeg) {
const yaw = (yawDeg * Math.PI) / 180, tilt = (tiltDeg * Math.PI) / 180;
const cy_ = Math.cos(yaw), sy = Math.sin(yaw), ct = Math.cos(tilt), st = Math.sin(tilt);
return {
yaw, tilt,
p(x, y, z = 0) {
const dx = x - cx, dy = y - cy;
const rx = dx * cy_ - dy * sy, ry = dx * sy + dy * cy_;
return [rx, ry * ct - z * st, ry * st + z * ct];
},
dir(nx, ny) { return [nx * cy_ - ny * sy, nx * sy + ny * cy_]; },
};
}
export function fitCanvas(canvas, maxDpr = 2) {
const r = canvas.getBoundingClientRect();
const dpr = Math.min(window.devicePixelRatio || 1, maxDpr);
const w = Math.max(1, Math.round(r.width)), h = Math.max(1, Math.round(r.height));
if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
canvas.width = Math.round(w * dpr);
canvas.height = Math.round(h * dpr);
}
return { w, h, dpr };
}
export function tablist(list, onSelect) {
const tabs = [...list.querySelectorAll('[role="tab"]')];
function select(i, focus) {
tabs.forEach((t, j) => { t.setAttribute('aria-selected', String(i === j)); t.tabIndex = i === j ? 0 : -1; });
if (focus) tabs[i].focus();
onSelect(tabs[i].dataset.mode, i);
}
tabs.forEach((t, i) => {
t.addEventListener('click', () => select(i, false));
t.addEventListener('keydown', (e) => {
const k = e.key;
if (k === 'ArrowRight' || k === 'ArrowDown') { e.preventDefault(); select((i + 1) % tabs.length, true); }
else if (k === 'ArrowLeft' || k === 'ArrowUp') { e.preventDefault(); select((i - 1 + tabs.length) % tabs.length, true); }
else if (k === 'Home') { e.preventDefault(); select(0, true); }
else if (k === 'End') { e.preventDefault(); select(tabs.length - 1, true); }
});
});
return { select: (i) => select(i, false), tabs };
}
export function loop(draw) {
let raf = 0, want = false, visible = true, last = 0;
function frame(t) {
raf = 0;
const dt = last ? Math.min(0.05, (t - last) / 1000) : 0;
last = t;
const keep = draw(t / 1000, dt);
if (keep && visible && !document.hidden) raf = requestAnimationFrame(frame); else last = 0;
}
return {
kick() { if (!raf && visible && !document.hidden) raf = requestAnimationFrame(frame); },
redraw() { draw(performance.now() / 1000, 0); },
setVisible(v) { visible = v; if (v) this.kick(); },
stop() { if (raf) cancelAnimationFrame(raf); raf = 0; },
};
}
