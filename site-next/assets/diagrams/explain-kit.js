import { prefersReducedMotion, loop } from './core.js';
export const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
export const fmt = (n) => Math.round(n).toLocaleString('en-GB');
export const clamp = (x) => Math.max(0, Math.min(1, x));
export const ph = (t, a, b) => clamp((t - a) / (b - a));
export const ease = (x) => x * x * (3 - 2 * x);
export const lerp = (a, b, u) => a + (b - a) * u;
export function pen() {
const g = [], n = (v) => (+v).toFixed(1);
return {
g,
t: (x, y, s, cls = '', anchor = '') => g.push(`<text x="${n(x)}" y="${n(y)}" class="${cls}"${anchor ? ` text-anchor="${anchor}"` : ''}>${esc(s)}</text>`),
r: (x, y, w, h, cls = '', rx = 3) => g.push(`<rect x="${n(x)}" y="${n(y)}" width="${Math.max(0, +w).toFixed(1)}" height="${Math.max(0, +h).toFixed(1)}" rx="${rx}" class="${cls}"/>`),
l: (x1, y1, x2, y2, cls = '') => g.push(`<line x1="${n(x1)}" y1="${n(y1)}" x2="${n(x2)}" y2="${n(y2)}" class="${cls}"/>`),
c: (x, y, r, cls = '', extra = '') => g.push(`<circle cx="${n(x)}" cy="${n(y)}" r="${r}" class="${cls}"${extra}/>`),
p: (d, cls = '') => g.push(`<path d="${d}" class="${cls}"/>`),
};
}
export const svgOpen = (cls, W, H, id, title) => `<svg class="${cls}" viewBox="0 0 ${W} ${Math.round(H)}" role="img" aria-labelledby="${id}-t"><title id="${id}-t">${esc(title)}</title>`;
export function playControls(label = 'Position in the animation', field = 'Scrub') {
return `<div class="dia-controls dia-ui" hidden><button type="button" class="chip" data-ctl="play" aria-pressed="false">Play</button><label class="dia-field">${esc(field)} <input type="range" data-ctl="time" min="0" max="1000" value="1000" aria-label="${esc(label)}"></label></div>`;
}
export function rangeField(name, label, min, max, step, value) {
return `<label class="dia-field">${esc(label)} <input type="range" data-ctl="${name}" min="${min}" max="${max}" step="${step}" value="${value}" aria-label="${esc(label)}"><output data-out="${name}"></output></label>`;
}
export function player(root, draw, duration, initial = 1) {
const q = (s) => root.querySelector(s), play = q('[data-ctl=play]'), range = q('[data-ctl=time]');
const S = { t: initial, playing: false, start: 0 };
const label = (txt, on) => { play.textContent = txt; play.setAttribute('aria-pressed', String(on)); };
const paint = () => { draw(S.t); range.value = String(Math.round(S.t * 1000)); };
const lp = loop(() => {
if (S.playing) { S.t = clamp((performance.now() - S.start) / duration); if (S.t >= 1) { S.playing = false; label('Replay', false); } }
paint();
return S.playing;
});
play.addEventListener('click', () => {
if (S.playing) { S.playing = false; label('Play', false); return; }
S.t = 0; S.start = performance.now(); S.playing = true; label('Pause', true);
if (prefersReducedMotion()) { S.t = 1; S.playing = false; label('Replay', false); paint(); return; }
lp.kick();
});
range.addEventListener('input', () => { S.playing = false; S.t = Number(range.value) / 1000; label('Play', false); paint(); });
return { paint, reset() { S.playing = false; S.t = initial; label('Play', false); paint(); }, get t() { return S.t; }, set t(v) { S.t = v; } };
}
export function tablesWrap(summary, tables) {
return `<details class="dia-data"><summary>${esc(summary)}</summary>${tables}</details>`;
}
export const tbl = (caption, cols, rows) => `<div class="tbl-wrap"><table class="tbl"><caption>${esc(caption)}</caption><thead><tr>${cols.map((c) => `<th scope="col">${esc(c)}</th>`).join('')}</tr></thead><tbody>${rows.map((r) => '<tr>' + r.map((c, k) => (k === 0 ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>').join('')}</tbody></table></div>`;
