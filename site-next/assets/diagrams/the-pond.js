import { hasDom } from './core.js';
import { esc, clamp, pen, svgOpen, playControls, rangeField, player, tablesWrap, tbl } from './explain-kit.js';
const GOLD = 2.399963229728653;
function rng(seed) {
let a = seed >>> 0;
return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
const gauss = (r) => Math.sqrt(-2 * Math.log(1 - r())) * Math.cos(2 * Math.PI * r());
export function metrics(d) {
return { ...d.defaults, bells: d.bells.length, timeMax: d.timeMax };
}
export function limitFacts(m) {
return { pondAtoms: String(m.atoms), pondPatterns: String(2 ** m.patternsPow), pondBells: String(m.bells) };
}
export function scene(d, S) {
const N = S.atoms, M = 2 ** S.patternsPow, w = S.earWidth, r = rng(S.seed);
const pos = Array.from({ length: N }, (_, k) => { const rad = 1.5 * Math.sqrt(k + 0.5), a = k * GOLD; return [rad * Math.cos(a), rad * Math.sin(a)]; });
const kap = Array.from({ length: M }, () => [gauss(r) / w, gauss(r) / w]);
const time = S.t * d.timeMax, env = d.bells.map((b) => Math.exp(-time / b.tau)), A = env.reduce((s, x) => s + x, 0) / env.length;
const j = Math.min(S.source, N - 1), hear = pos.map((q, k) => {
const dx = q[0] - pos[j][0], dy = q[1] - pos[j][1], dist = Math.hypot(dx, dy);
let s = 0; for (const kk of kap) s += Math.cos(kk[0] * dx + kk[1] * dy);
return { k, dist, K: s / M, G: Math.exp(-(dist * dist) / (2 * w * w)) };
});
const others = hear.filter((h) => h.k !== j), far = others.filter((h) => h.dist > 2 * w), near = others.filter((h) => h.dist <= 1.5 * w);
const rms = (a, f) => (a.length ? Math.sqrt(a.reduce((s, h) => s + f(h) ** 2, 0) / a.length) : 0);
return { N, M, w, pos, hear, j, A, env, time, far, near, farHiss: rms(far, (h) => h.K), allHiss: rms(others, (h) => h.K - h.G), nearMean: near.length ? near.reduce((s, h) => s + h.K, 0) / near.length : 0, dmax: Math.max(1, ...others.map((h) => h.dist)) };
}
export function readoutFor(d, S) {
const sc = scene(d, S), f2 = (x) => x.toFixed(2);
return `Atom ${sc.j + 1} writes. ${sc.near.length} atoms within one and a half ear widths hear it at a mean ${f2(sc.nearMean * sc.A)} of full strength; ${sc.far.length} atoms beyond two ear widths hear hiss of about ${f2(sc.farHiss * sc.A)}. With ${sc.M} wave pattern${sc.M === 1 ? '' : 's'} the scatter around the ear is ${f2(sc.allHiss)}; it falls with the square root of the patterns. A toy computed in the page, no model; illustrative.`;
}
function bellPath(b, x0, y0, w, h, tmax) {
const pts = [], n = 90;
for (let i = 0; i <= n; i++) { const t = (i / n) * tmax, v = Math.exp(-t / b.tau) * Math.cos(b.omega * t * 2.2); pts.push(`${i ? 'L' : 'M'}${(x0 + (t / tmax) * w).toFixed(1)} ${(y0 + h / 2 - v * (h / 2 - 1)).toFixed(1)}`); }
return pts.join('');
}
export function sceneSvg(d, S, narrow = false, uid = 'pd') {
const sc = scene(d, S), p = pen(), W = narrow ? 360 : 700, sp = narrow ? W - 20 : 300, spH = narrow ? 250 : 300;
const R = 1.5 * Math.sqrt(sc.N - 0.5) + 0.7, cx = 10 + sp / 2, cy = spH / 2 + 4, k0 = (Math.min(sp, spH) / 2 - 12) / R;
p.t(10, 14, 'Atoms on a golden spiral. Choose one to make it write.', 'pd-lane');
sc.pos.forEach((q, k) => {
const h = sc.hear[k], v = k === sc.j ? 0 : h.K * sc.A, x = cx + q[0] * k0, y = cy + q[1] * k0 + 8, mag = clamp(Math.abs(v));
const fill = v >= 0 ? `rgba(139,125,255,${(0.12 + 0.88 * mag).toFixed(2)})` : `rgba(240,170,90,${(0.12 + 0.88 * mag).toFixed(2)})`;
p.g.push(`<g class="pd-atom${k === sc.j ? ' src' : ''}" data-atom="${k}" tabindex="0" role="button" aria-label="Atom ${k + 1}${k === sc.j ? ', writing' : ', hears ' + v.toFixed(2)}" aria-pressed="${k === sc.j}"><circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="${narrow ? 11 : 13}" fill="${fill}"/><text x="${x.toFixed(1)}" y="${(y + 4).toFixed(1)}" text-anchor="middle" class="pd-n">${k + 1}</text></g>`);
});
if (sc.hear.length) { const c = sc.pos[sc.j]; p.c(cx + c[0] * k0, cy + c[1] * k0 + 8, narrow ? 15 : 18, 'pd-ring'); }
const gx = narrow ? 10 : sp + 28, gy = narrow ? spH + 16 : 24, gw = narrow ? W - 20 : W - gx - 10, gh = narrow ? 170 : 170, pl = 30;
p.t(gx, gy - 8, 'What each atom hears, by its distance from the writer', 'pd-lane');
const X = (u) => gx + pl + (u / sc.dmax) * (gw - pl - 4), Y = (v) => gy + 6 + (gh - 28) / 2 - (v / 1.1) * ((gh - 28) / 2);
p.l(gx + pl, Y(0), gx + gw, Y(0), 'pd-zero'); p.t(gx + pl - 4, Y(1) + 4, '1', 'pd-tick', 'end'); p.t(gx + pl - 4, Y(0) + 4, '0', 'pd-tick', 'end'); p.t(gx + pl - 4, Y(-1) + 4, '-1', 'pd-tick', 'end');
const curve = []; for (let i = 0; i <= 60; i++) { const u = (i / 60) * sc.dmax; curve.push(`${i ? 'L' : 'M'}${X(u).toFixed(1)} ${Y(Math.exp(-(u * u) / (2 * sc.w * sc.w)) * sc.A).toFixed(1)}`); }
p.p(curve.join(''), 'pd-ear');
sc.hear.forEach((h) => { if (h.k !== sc.j) p.c(X(h.dist), Y(h.K * sc.A), 3.4, 'pd-dot', ` data-k="${h.k}"`); });
p.t(gx + pl + (gw - pl) / 2, gy + gh - 8, 'distance between writer and listener', 'pd-ax', 'middle');
const by = narrow ? gy + gh + 14 : gy + gh + 12, bh = 30, bw = narrow ? W - 20 : W - gx - 10;
p.t(gx, by, `Bell bank: time ${sc.time.toFixed(0)} of ${d.timeMax}`, 'pd-lane');
d.bells.forEach((b, i) => {
const y = by + 8 + i * (bh + 4);
p.r(gx, y, bw - 116, bh, 'pd-bellbox', 3);
p.p(bellPath(b, gx, y, bw - 116, bh, d.timeMax), 'pd-bell b' + i);
const xt = gx + (sc.time / d.timeMax) * (bw - 116); p.l(xt, y, xt, y + bh, 'pd-now'); p.c(xt, y + bh / 2 - sc.env[i] * Math.cos(b.omega * sc.time * 2.2) * (bh / 2 - 1), 3.5, 'pd-bd b' + i);
p.t(gx + bw - 110, y + bh / 2 + 4, `${b.label} ${(sc.env[i] * 100).toFixed(0)}%`, 'pd-note');
});
const H = by + 8 + d.bells.length * (bh + 4) + 6;
return svgOpen('pd-svg', W, Math.max(H, spH + 10), uid, 'The pond: atoms on a golden spiral share one wave medium. A toy computed in the page, no model; illustrative.') + p.g.join('') + '</svg>';
}
export const staticSvg = (d) => sceneSvg(d, { ...d.defaults, t: 0 }, false, 'pd-' + d.id);
export function controlsHtml(id, m, d) {
const L = d.limits, D = d.defaults;
return {
tabs: '',
controls: `<div class="hw-copy"><p class="hw-promise">Drop a signal into the pond. Near atoms hear it clearly and far atoms hear hiss.</p><p class="hw-how">${esc(d.equations[2])} ${esc(d.equations[3])}</p></div>
<div class="dia-controls dia-ui pd-sliders" hidden>${rangeField('atoms', 'Atoms', L.atoms[0], L.atoms[1], 1, D.atoms)}${rangeField('ear', 'Ear width', L.earWidth[0], L.earWidth[1], 0.1, D.earWidth)}${rangeField('pat', 'Wave patterns', L.patternsPow[0], L.patternsPow[1], 1, D.patternsPow)}<button type="button" class="chip" data-ctl="reroll">New patterns</button></div>
${playControls('Time since the write', 'Time')}
<p class="dia-readout hw-caption" data-ctl="readout" role="status">${esc(readoutFor(d, { ...D, t: 0 }))}</p>`,
};
}
export function tablesHtml(d) {
const sc = scene(d, { ...d.defaults, t: 0 });
const a = tbl(`What each atom hears from atom ${sc.j + 1} at time zero (defaults: ${sc.M} patterns, ear width ${d.defaults.earWidth})`, ['Listener', 'Distance', 'Ear (Gaussian)', 'Heard'], sc.hear.filter((h) => h.k !== sc.j).map((h) => [`Atom ${h.k + 1}`, h.dist.toFixed(2), h.G.toFixed(2), h.K.toFixed(2)]));
const b = tbl('The bell bank', ['Bell', 'Fade time', 'Left at time 10', 'Left at time 30'], d.bells.map((x) => [x.label, String(x.tau), `${(Math.exp(-10 / x.tau) * 100).toFixed(0)}%`, `${(Math.exp(-30 / x.tau) * 100).toFixed(0)}%`]));
const eq = `<ol class="dia-notes">${d.equations.map((x) => `<li>${esc(x)}</li>`).join('')}</ol>`;
return tablesWrap('Data behind the diagram', a + b + eq);
}
export function verify(d, body) {
const out = [], sc = scene(d, { ...d.defaults, t: 0 });
if (!(sc.far.length > 0 && sc.near.length > 0)) out.push('the default scene needs both near and far atoms');
const lo = scene(d, { ...d.defaults, patternsPow: 0, t: 0 }), hi = scene(d, { ...d.defaults, patternsPow: 8, t: 0 });
if (!(hi.allHiss < lo.allHiss)) out.push('more patterns must shrink the hiss');
if (!(scene(d, { ...d.defaults, t: 1 }).A < sc.A)) out.push('time must fade the bank');
if ((body.match(/<tbody>/g) || []).length !== 2) out.push('two tables expected');
return out;
}
export const interactiveStage = false;
export function mount(root, d) {
const q = (s) => root.querySelector(s), stage = q('.dia-stage'), read = q('[data-ctl=readout]');
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const S = { ...d.defaults, t: 0 }; let narrow = stage.getBoundingClientRect().width < 560, focusAtom = null;
const out = (n, v) => { const o = q(`[data-out=${n}]`); if (o) o.textContent = v; };
const pl = player(root, (t) => {
S.t = t; stage.innerHTML = sceneSvg(d, S, narrow, 'pd-' + d.id); read.textContent = readoutFor(d, S);
out('atoms', S.atoms); out('ear', S.earWidth.toFixed(1)); out('pat', 2 ** S.patternsPow);
if (focusAtom !== null) { const el = stage.querySelector(`[data-atom="${focusAtom}"]`); if (el) el.focus(); }
}, 10000, 0);
const pick = (el, keep) => { const g = el && el.closest('[data-atom]'); if (!g) return; S.source = Number(g.dataset.atom); focusAtom = keep ? S.source : null; pl.paint(); };
stage.addEventListener('click', (e) => pick(e.target, false));
stage.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(e.target, true); } });
root.addEventListener('pointerdown', () => { focusAtom = null; });
const bind = (name, fn) => q(`[data-ctl=${name}]`).addEventListener('input', (e) => { fn(Number(e.target.value)); pl.paint(); });
bind('atoms', (v) => { S.atoms = v; if (S.source >= v) S.source = 0; });
bind('ear', (v) => { S.earWidth = v; });
bind('pat', (v) => { S.patternsPow = v; });
q('[data-ctl=reroll]').addEventListener('click', () => { S.seed += 1; pl.paint(); });
if (typeof ResizeObserver === 'function') new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 560; if (n !== narrow) { narrow = n; pl.paint(); } }).observe(stage);
pl.paint();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="the-pond"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
