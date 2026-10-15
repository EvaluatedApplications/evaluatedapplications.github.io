// Research diagram: "How many independent judgements a group is worth". The design effect: n members with average correlation rho carry
// n / (1 + (n - 1) rho) independent judgements. Data: the article's two stated illustrations (group size and stated effective size).
import { hasDom } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');
export const effective = (n, rho) => n / (1 + (n - 1) * rho);
export const rhoFor = (n, eff) => (n / eff - 1) / (n - 1);
const RHO_MIN = 1e-6;
const rhoOf = (v) => 10 ** (Math.log10(RHO_MIN) * (1 - v / 1000));   // slider 0..1000 maps to rho from 1e-6 to 1

export function metrics(d) {
  return { groups: d.groups.map((g) => ({ ...g, rho: rhoFor(g.n, g.effective) })) };
}

export function readoutFor(d, m, gi, v) {
  const g = d.groups[gi], rho = rhoOf(v), e = effective(g.n, rho), gm = m.groups[gi];
  return `With ${n0(g.n)} members at an average correlation of ${rho < 0.001 ? rho.toExponential(1) : rho.toFixed(3)}, the group carries about ${e < 10 ? e.toFixed(2) : n0(Math.round(e))} independent judgements. The article's illustration (${g.stated}) corresponds to a correlation of ${gm.rho < 0.001 ? gm.rho.toExponential(1) : gm.rho.toFixed(3)}.`;
}

export function chartSvg(d, m, gi, v, narrow = false) {
  const W = narrow ? 400 : 680, H = narrow ? 300 : 280, L = narrow ? 50 : 62, R = 20, T = 20, B = 46, pw = W - L - R, ph = H - T - B, g = d.groups[gi];
  const X = (rho) => L + ((Math.log10(rho) - Math.log10(RHO_MIN)) / (0 - Math.log10(RHO_MIN))) * pw;
  const Y = (e) => T + ph - (Math.log10(e) / Math.log10(g.n)) * ph;
  const pts = []; for (let k = 0; k <= 120; k++) { const rho = 10 ** (Math.log10(RHO_MIN) * (1 - k / 120)); pts.push(`${X(rho).toFixed(1)},${Y(Math.max(1, effective(g.n, rho))).toFixed(1)}`); }
  const yt = []; for (let e = 1; e <= g.n; e *= 10) yt.push(e);
  const grid = yt.map((e) => `<line class="gl" x1="${L}" y1="${Y(e).toFixed(1)}" x2="${L + pw}" y2="${Y(e).toFixed(1)}"/><text class="tk" x="${L - 8}" y="${(Y(e) + 3).toFixed(1)}" text-anchor="end">${n0(e)}</text>`).join('');
  const xt = [1e-6, 1e-5, 1e-4, 1e-3, 1e-2, 1e-1, 1].map((r) => `<text class="tk" x="${X(r).toFixed(1)}" y="${H - B + 16}" text-anchor="middle">${r < 0.01 ? r.toExponential(0) : r}</text>`).join('');
  const rho = rhoOf(v), e = Math.max(1, effective(g.n, rho)), mk = m.groups[gi];
  return `<svg class="de-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="de-t de-d" preserveAspectRatio="xMidYMid meet"><title id="de-t">Effective size against correlation for ${n0(g.n)} members</title><desc id="de-d">${esc(readoutFor(d, m, gi, v))}</desc>${grid}${xt}<polyline class="ln" points="${pts.join(' ')}"/><circle class="stated" cx="${X(mk.rho).toFixed(1)}" cy="${Y(g.effective).toFixed(1)}" r="6"/><text class="lb" x="${(narrow ? X(mk.rho) - 10 : X(mk.rho) + 10).toFixed(1)}" y="${(Y(g.effective) - 8).toFixed(1)}" text-anchor="${narrow ? 'end' : 'start'}">stated: ${esc(narrow ? g.stated.split(' ').slice(0, 3).join(' ') : g.stated)}</text><line class="mk" x1="${X(rho).toFixed(1)}" y1="${T}" x2="${X(rho).toFixed(1)}" y2="${T + ph}"/><circle class="pt" cx="${X(rho).toFixed(1)}" cy="${Y(e).toFixed(1)}" r="5.5"/><text class="ax" x="${L + pw / 2}" y="${H - 4}" text-anchor="middle">average correlation between members, logarithmic scale</text><text class="ax" transform="translate(14 ${T + ph / 2}) rotate(-90)" text-anchor="middle">independent judgements</text></svg>`;
}

export function staticSvg(d) { return chartSvg(d, metrics(d), 0, 600); }

export function controlsHtml(id, m, d) {
  const opts = d.groups.map((g, k) => `<option value="${k}">${esc(g.label)}</option>`).join('');
  return { tabs: '', controls: `<div class="dia-controls dia-ui" hidden><label class="dia-field">Group <select data-ctl="group">${opts}</select></label><label class="dia-field">Correlation <input type="range" min="0" max="1000" step="1" value="600" data-ctl="rho" aria-describedby="${id}-read"></label></div>
<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden>${esc(readoutFor(d, m, 0, 600))}</p>` };
}

export function tablesHtml(d, m) {
  const rhos = [1e-4, 1e-3, 1e-2, 1e-1, 0.5, 1];
  const rows = rhos.map((r) => `<tr><th scope="row">${r}</th>${d.groups.map((g) => `<td>${effective(g.n, r) < 10 ? effective(g.n, r).toFixed(2) : n0(Math.round(effective(g.n, r)))}</td>`).join('')}</tr>`).join('');
  const st = m.groups.map((g) => `<tr><th scope="row">${esc(g.label)}</th><td>${n0(g.n)}</td><td>${esc(g.stated)}</td><td>${g.rho < 0.001 ? g.rho.toExponential(1) : g.rho.toFixed(3)}</td></tr>`).join('');
  return `<details class="dia-data"><summary>Data behind the diagram</summary><div class="tbl-wrap"><table class="tbl"><caption>The two illustrations in the article, and the correlation each one implies</caption><thead><tr><th scope="col">Group</th><th scope="col">Members</th><th scope="col">Stated effective size</th><th scope="col">Implied correlation</th></tr></thead><tbody>${st}</tbody></table></div><div class="tbl-wrap"><table class="tbl"><caption>Independent judgements by correlation, from the design effect</caption><thead><tr><th scope="col">Average correlation</th>${d.groups.map((g) => `<th scope="col">${n0(g.n)} members</th>`).join('')}</tr></thead><tbody>${rows}</tbody></table></div></details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  for (const g of m.groups) {
    const e = effective(g.n, g.rho);
    if (Math.abs(e - g.effective) > 0.01 * Math.max(1, g.effective)) out.push(`${g.n}: formula gives ${e}, stated ${g.effective}`);
    if (g.rho < 0 || g.rho > 1) out.push(`${g.n}: implied correlation outside 0 to 1`);
  }
  if (!/Implied correlation/.test(body)) out.push('table missing');
  return out;
}

export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage');
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const q = (s) => root.querySelector(s), S = { g: 0, v: 600 };
  let narrow = stage.getBoundingClientRect().width < 520;
  const render = () => { stage.innerHTML = chartSvg(d, m, S.g, S.v, narrow); q('[data-ctl=readout]').textContent = readoutFor(d, m, S.g, S.v); };
  q('[data-ctl=group]').addEventListener('change', (e) => { S.g = Number(e.target.value); render(); });
  q('[data-ctl=rho]').addEventListener('input', (e) => { S.v = Number(e.target.value); render(); });
  new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== narrow) { narrow = n; render(); } }).observe(stage);
  render();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="design-effect"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}

export const interactiveStage = false;