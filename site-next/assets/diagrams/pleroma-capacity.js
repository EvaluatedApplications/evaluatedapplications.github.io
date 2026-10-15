import { hasDom, prefersReducedMotion, readTokens } from './core.js';
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const NS = 'http://www.w3.org/2000/svg';
export function metrics(d) {
const K = d.capacity, rows = d.rows, max = rows.length - 1;
const lastOk = Math.max(...rows.filter((r) => r.decoded && r.exact).map((r) => r.keys));
const firstRefuse = Math.min(...rows.filter((r) => !r.decoded).map((r) => r.keys));
return { K, rows, max, lastOk, firstRefuse, harmonics: d.harmonicsPerKeyLine };
}
export function verdictFor(d, m, n) {
const r = m.rows[n];
if (n === 0) return `Empty store. Add keys to see what the 2K+1 rule does.`;
if (r.decoded) return `TryKeys returns true: all ${n} distinct key${n > 1 ? 's are' : ' is'} decoded exactly (${n} of K = ${m.K}).`;
return `TryKeys returns false: ${n} keys is past capacity K = ${m.K}. The store refuses instead of guessing, so it never returns a wrong answer.`;
}
export function staticSvg(d) {
const m = metrics(d), W = 360, cw = 36;
const chips = m.rows.slice(1).map((r, i) => `<g><rect class="${r.decoded ? 'ok' : 'no'}" x="${10 + i * (cw + 2)}" y="20" width="${cw}" height="26" rx="5"/><text x="${10 + i * (cw + 2) + cw / 2}" y="37" text-anchor="middle">${r.keys}</text></g>`).join('');
return `<svg class="dia-plan cap-plan" viewBox="0 0 ${W} 70" role="img" aria-labelledby="cp-t cp-d"><title id="cp-t">TryKeys result for 1 to ${m.max} distinct keys</title><desc id="cp-d">With key capacity ${m.K}, TryKeys decodes exactly up to ${m.lastOk} keys and refuses from ${m.firstRefuse} keys.</desc>${chips}</svg>`;
}
export function controlsHtml(id, m, d) {
return {
tabs: '',
controls: `<div class="cap-box dia-ui" hidden data-over="0">
<h3 class="sp-h">K = ${m.K}: <span data-ctl="n">3</span> keys in the store, 2K+1 = ${m.harmonics} harmonics per key line</h3>
<div class="cap-keys" data-ctl="keys"></div>
<p class="dia-readout" data-ctl="verdict" role="status"></p>
<div class="dia-controls"><button type="button" class="btn btn-ghost btn-sm" data-ctl="add">Add a key</button><button type="button" class="btn btn-ghost btn-sm" data-ctl="remove">Remove a key</button><button type="button" class="btn btn-ghost btn-sm" data-ctl="reset">Reset</button></div>
</div>`,
};
}
export function tablesHtml(d, m) {
const rows = m.rows.map((r) => `<tr><th scope="row">${r.keys}</th><td>${r.decoded ? 'true' : 'false'}</td><td>${r.returned}</td></tr>`).join('');
return `<details class="dia-data"><summary>What TryKeys returned for each number of keys</summary><div class="tbl-wrap"><table class="tbl"><caption>Key capacity ${m.K}: distinct keys added and the result of TryKeys</caption><thead><tr><th scope="col">Distinct keys</th><th scope="col">TryKeys returned</th><th scope="col">Keys decoded</th></tr></thead><tbody>${rows}</tbody></table></div></details>`;
}
export function verify(d, body) {
const m = metrics(d), out = [];
if (m.lastOk !== m.K) out.push(`data says exact up to ${m.lastOk} keys, capacity is ${m.K}`);
if (m.firstRefuse !== m.K + 1) out.push(`data first refuses at ${m.firstRefuse}, expected ${m.K + 1}`);
if (m.rows.some((r) => r.decoded && !r.exact)) out.push('a decoded result was not exact');
return out;
}
export function mount(root, d) {
const m = metrics(d), stage = root.querySelector('.dia-stage');
stage.textContent = '';
root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
const q = (s) => root.querySelector(s);
const keys = q('[data-ctl=keys]'), verdict = q('[data-ctl=verdict]'), num = q('[data-ctl=n]'), box = q('.cap-box');
const add = q('[data-ctl=add]'), rm = q('[data-ctl=remove]');
const svg = document.createElementNS(NS, 'svg'); svg.setAttribute('class', 'cap-svg'); svg.setAttribute('viewBox', `0 0 ${m.harmonics * 38 + 10} 56`); svg.setAttribute('role', 'img');
svg.setAttribute('aria-label', `The store holds ${m.harmonics} harmonics per key line. It is exact for up to ${m.K} distinct keys.`);
stage.append(svg);
let n = 3;
function render() {
const r = m.rows[n];
num.textContent = n; keys.textContent = '';
for (let i = 1; i <= n; i++) { const s = document.createElement('span'); s.className = 'cap-key' + (i > m.K ? ' over' : ''); s.textContent = 'key ' + i; keys.append(s); }
box.dataset.over = r.decoded ? '0' : '1';
verdict.textContent = verdictFor(d, m, n);
add.disabled = n >= m.max; rm.disabled = n === 0;
svg.textContent = '';
for (let i = 0; i < m.harmonics; i++) {
const rect = document.createElementNS(NS, 'rect');
rect.setAttribute('x', 5 + i * 38); rect.setAttribute('y', 8); rect.setAttribute('width', 34); rect.setAttribute('height', 34); rect.setAttribute('rx', 5);
rect.setAttribute('class', 'cap-cell' + (n > 0 ? (r.decoded ? ' ok' : ' no') : ''));
svg.append(rect);
}
}
add.addEventListener('click', () => { if (n < m.max) n++; render(); });
rm.addEventListener('click', () => { if (n > 0) n--; render(); });
q('[data-ctl=reset]').addEventListener('click', () => { n = 0; render(); });
render();
root.dataset.ready = 'true';
}
if (hasDom) {
const start = () => document.querySelectorAll('[data-diagram="pleroma-capacity"]').forEach((root) => {
const blob = root.querySelector('script[type="application/json"]');
if (blob) mount(root, JSON.parse(blob.textContent));
});
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
export const interactiveStage = false;
