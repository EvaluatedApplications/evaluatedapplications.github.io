// AlgFormer signature diagram: "Two attention cores, one shape". The same model shape is built with softmax attention
// and with holographic attention; the chart shows what each costs to read a prompt, to produce each further token and to
// keep in memory as the context grows, plus the capacity numbers HoloShape derives for several widths.
// Data in: algformer-cost.json, written by web/generators/algformer-cost by running the PUBLISHED package.
import { hasDom, tablist } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');
const sig = (n) => (n >= 100 ? n0(Math.round(n)) : n >= 10 ? Number(n).toFixed(1) : Number(n).toFixed(2)).replace(/\.0+$/, '');

export const MODES = [
  { id: 'prefill', label: 'Reading a prompt', key: 'prefillMs', unit: 'ms', title: 'Time to read the whole prompt', y: 'milliseconds' },
  { id: 'step', label: 'Each new token', key: 'stepUs', unit: 'µs', title: 'Time to produce one more token', y: 'microseconds' },
  { id: 'cache', label: 'Memory held', key: 'cacheKb', unit: 'KB', title: 'Memory the serve cache holds', y: 'kilobytes' },
  { id: 'capacity', label: 'Capacity', key: null, unit: '', title: 'What the holographic bundle can hold', y: 'tokens' },
];

// ---------------------------------------------------------------- pure data helpers
// growth exponent: the slope of log(cost) against log(tokens) between the 128 and the longest measured prompt
function exponent(rows, key, core) {
  const a = rows.find((r) => r.tokens >= 128), b = rows[rows.length - 1];
  const va = a[key][core], vb = b[key][core];
  return Math.log(vb / va) / Math.log(b.tokens / a.tokens);
}

export function metrics(d) {
  const rows = d.rows, last = rows[rows.length - 1];
  const exps = {};
  for (const md of MODES.filter((x) => x.key)) exps[md.id] = { softmax: exponent(rows, md.key, 'softmax'), holographic: exponent(rows, md.key, 'holographic') };
  const at = (n) => rows.find((r) => r.tokens === n);
  return {
    rows, last, exps, shape: d.shape, capacity: d.capacity, cleanFrac: d.cleanFrac,
    samePar: d.shape.softmaxParams === d.shape.holographicParams, params: d.shape.softmaxParams,
    prefillRatio: last.prefillMs.softmax / last.prefillMs.holographic, stepRatio: last.stepUs.softmax / last.stepUs.holographic,
    cacheRatio: last.cacheKb.softmax / last.cacheKb.holographic, longest: last.tokens, at,
    flatKb: rows.every((r) => Math.abs(r.cacheKb.holographic - rows[0].cacheKb.holographic) < 0.5) ? rows[0].cacheKb.holographic : null,
  };
}

// Numbers the product page quotes in prose, taken from the data so the sentence cannot go stale.
export function limitFacts(m) {
  return {
    tokens: n0(m.longest), prefillSoftMs: sig(m.last.prefillMs.softmax), prefillHoloMs: sig(m.last.prefillMs.holographic),
    stepSoftUs: sig(m.last.stepUs.softmax), stepHoloUs: sig(m.last.stepUs.holographic),
    cacheSoftKb: n0(Math.round(m.last.cacheKb.softmax)), cacheHoloKb: sig(m.last.cacheKb.holographic),
    prefillExpSoft: m.exps.prefill.softmax.toFixed(1), prefillExpHolo: m.exps.prefill.holographic.toFixed(1),
    params: n0(m.params), dim: String(m.shape.dModel), shifts: String(m.shape.shifts), layers: String(m.shape.layers),
    cleanFrac: String(Math.round(m.cleanFrac * 100)), ctx128: n0(m.capacity.find((c) => c.dim === 128).context), edge128: n0(m.capacity.find((c) => c.dim === 128).bindRank), clean128: n0(m.capacity.find((c) => c.dim === 128).cleanCapacity),
  };
}

export function readoutFor(mode, m, i, log) {
  const md = MODES.find((x) => x.id === mode);
  if (mode === 'capacity') {
    const c = m.capacity[i];
    return `Width ${c.dim}: HoloShape derives ${c.shifts} shifts, ${c.layers} layer${c.layers > 1 ? 's' : ''} and a context of ${n0(c.context)} tokens. The bundle's crosstalk edge is ${n0(c.bindRank)} tokens, and ${n0(c.cleanCapacity)} tokens read back at least 90 percent clean.`;
  }
  const r = m.rows[i], a = r[md.key].softmax, h = r[md.key].holographic, e = m.exps[mode];
  const ratio = a / h;
  const what = mode === 'prefill' ? 'reading the prompt takes' : mode === 'step' ? 'each further token takes' : 'the serve cache holds';
  const grow = (x) => (Math.abs(x) < 0.15 ? 'stays flat' : x > 1.5 ? 'grows with the square of the length' : 'grows in step with the length');
  return `At ${n0(r.tokens)} tokens ${what} ${sig(a)} ${md.unit} with softmax attention and ${sig(h)} ${md.unit} with holographic attention, ${ratio >= 1 ? `${sig(ratio)} times` : `${sig(1 / ratio)} times less`} as much. Across the measured lengths the softmax cost ${grow(e.softmax)} and the holographic cost ${grow(e.holographic)}.`;
}

// ---------------------------------------------------------------- chart (SVG string, shared by the server and the browser)
function ticksLinear(max) {
  const raw = max / 4, mag = 10 ** Math.floor(Math.log10(raw)), f = raw / mag;
  const step = (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * mag;
  const out = []; for (let v = 0; v <= max * 1.001; v += step) out.push(v); return out;
}
function ticksLog(min, max) {
  const out = []; for (let e = Math.floor(Math.log10(min)); e <= Math.ceil(Math.log10(max)); e++) out.push(10 ** e); return out;
}
const tickLabel = (v) => (v >= 1000 ? n0(v) : String(v < 1 ? Number(v.toFixed(2)) : v));

export function chartSvg(mode, d, m, opt = {}) {
  const { log = false, i = m.rows.length - 1, narrow = false } = opt;
  const [W, H, L, R] = narrow ? [400, 330, 52, 92] : [640, 310, 62, 112], T = 18, B = 46, pw = W - L - R, ph = H - T - B;
  if (mode === 'capacity') return capacitySvg(d, m, i, W, H, narrow);
  const md = MODES.find((x) => x.id === mode), rows = m.rows;
  const xs = rows.map((r) => Math.log2(r.tokens)), x0 = Math.log2(rows[0].tokens), x1 = Math.log2(rows[rows.length - 1].tokens);
  const X = (n) => L + ((Math.log2(n) - x0) / (x1 - x0)) * pw;
  const vals = rows.flatMap((r) => [r[md.key].softmax, r[md.key].holographic]);
  const vmax = Math.max(...vals), vmin = Math.min(...vals);
  const ymax = log ? 10 ** Math.ceil(Math.log10(vmax * 1.2)) : vmax * 1.08, ymin = log ? 10 ** Math.floor(Math.log10(vmin * 0.8)) : 0;
  const Y = (v) => T + ph - (log ? (Math.log10(Math.max(v, ymin)) - Math.log10(ymin)) / (Math.log10(ymax) - Math.log10(ymin)) : v / ymax) * ph;
  const yt = log ? ticksLog(ymin, ymax) : ticksLinear(vmax);
  const grid = yt.map((v) => `<line class="gl" x1="${L}" y1="${Y(v).toFixed(1)}" x2="${L + pw}" y2="${Y(v).toFixed(1)}"/><text class="tk" x="${L - 8}" y="${(Y(v) + 3).toFixed(1)}" text-anchor="end">${tickLabel(v)}</text>`).join('');
  const xt = rows.map((r) => `<line class="gl v" x1="${X(r.tokens).toFixed(1)}" y1="${T}" x2="${X(r.tokens).toFixed(1)}" y2="${T + ph}"/><text class="tk" x="${X(r.tokens).toFixed(1)}" y="${H - B + 16}" text-anchor="middle">${n0(r.tokens)}</text>`).join('');
  const line = (core) => rows.map((r) => `${X(r.tokens).toFixed(1)},${Y(r[md.key][core]).toFixed(1)}`).join(' ');
  const dots = (core) => rows.map((r, k) => `<circle class="pt ${core}${k === i ? ' sel' : ''}" cx="${X(r.tokens).toFixed(1)}" cy="${Y(r[md.key][core]).toFixed(1)}" r="${k === i ? 5.5 : 3.4}"/>`).join('');
  const lastR = rows[rows.length - 1], sel = rows[i];
  const lab = (core, name) => `<text class="lb ${core}" x="${(X(lastR.tokens) + 8).toFixed(1)}" y="${(Y(lastR[md.key][core]) - 18).toFixed(1)}">${name}</text><text class="lv ${core}" x="${(X(lastR.tokens) + 8).toFixed(1)}" y="${(Y(lastR[md.key][core]) - 6).toFixed(1)}">${sig(lastR[md.key][core])} ${md.unit}</text>`;
  const mark = `<line class="mk" x1="${X(sel.tokens).toFixed(1)}" y1="${T}" x2="${X(sel.tokens).toFixed(1)}" y2="${T + ph}"/>`;
  return `<svg class="ac-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="ac-t ac-d" preserveAspectRatio="xMidYMid meet"><title id="ac-t">${esc(md.title)}, by context length</title><desc id="ac-d">${esc(`Softmax attention and holographic attention at the same shape, measured at ${rows.map((r) => n0(r.tokens)).join(', ')} tokens. ${readoutFor(mode, m, rows.length - 1)}`)}</desc>${grid}${xt}${mark}<polyline class="ln softmax" points="${line('softmax')}"/><polyline class="ln holographic" points="${line('holographic')}"/>${dots('softmax')}${dots('holographic')}${lab('softmax', 'Softmax')}${lab('holographic', 'Holographic')}<text class="ax" x="${L + pw / 2}" y="${H - 6}" text-anchor="middle">tokens in context${log ? ', logarithmic scale on both axes' : ''}</text><text class="ax" transform="translate(14 ${T + ph / 2}) rotate(-90)" text-anchor="middle">${md.y}</text></svg>`;
}

function capacitySvg(d, m, i, W, H, narrow) {
  const caps = m.capacity, L = narrow ? 52 : 64, R = 16, T = 22, B = 50, pw = W - L - R, ph = H - T - B;
  const vmax = Math.max(...caps.map((c) => c.bindRank)), vmin = Math.min(...caps.map((c) => c.cleanCapacity));
  const lo = 10 ** Math.floor(Math.log10(vmin * 0.8)), hi = 10 ** Math.ceil(Math.log10(vmax * 1.2));
  const Y = (v) => T + ph - ((Math.log10(v) - Math.log10(lo)) / (Math.log10(hi) - Math.log10(lo))) * ph;
  const grid = ticksLog(lo, hi).map((v) => `<line class="gl" x1="${L}" y1="${Y(v).toFixed(1)}" x2="${L + pw}" y2="${Y(v).toFixed(1)}"/><text class="tk" x="${L - 8}" y="${(Y(v) + 3).toFixed(1)}" text-anchor="end">${tickLabel(v)}</text>`).join('');
  const gw = pw / caps.length, bw = Math.min(30, gw / 4.4);
  const bars = caps.map((c, k) => {
    const cx = L + gw * k + gw / 2, series = [['edge', c.bindRank], ['ctx', c.context], ['clean', c.cleanCapacity]];
    const sel = k === i;
    return `<g class="grp${sel ? ' sel' : ''}">${series.map(([cl, v], s) => `<rect class="bar ${cl}" x="${(cx + (s - 1.5) * (bw + 3)).toFixed(1)}" y="${Y(v).toFixed(1)}" width="${bw.toFixed(1)}" height="${(T + ph - Y(v)).toFixed(1)}" rx="3"/>`).join('')}<text class="tk" x="${cx.toFixed(1)}" y="${H - B + 16}" text-anchor="middle">width ${c.dim}</text></g>`;
  }).join('');
  const leg = [['edge', 'crosstalk edge'], ['ctx', 'derived context'], ['clean', narrow ? 'reads clean' : 'reads back 90% clean']].map(([cl, t], k) => `<g transform="translate(${L + k * ((W - L) / 3)} ${H - 14})"><rect class="bar ${cl}" width="10" height="10" rx="2"/><text class="tk" x="15" y="9">${t}</text></g>`).join('');
  return `<svg class="ac-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="ac-t ac-d" preserveAspectRatio="xMidYMid meet"><title id="ac-t">Capacity of the holographic bundle, by model width</title><desc id="ac-d">${esc(readoutFor('capacity', m, caps.length - 1))}</desc>${grid}${bars}${leg}</svg>`;
}

// ---------------------------------------------------------------- server-rendered pieces
export function staticSvg(d) {
  const m = metrics(d);
  return chartSvg('prefill', d, m, { log: false, i: m.rows.length - 1 });
}

export function controlsHtml(id, m) {
  const tabs = MODES.map((md, k) => `<button type="button" role="tab" id="${id}-tab-${md.id}" data-mode="${md.id}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(md.label)}</button>`).join('');
  return {
    tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="What to compare" hidden>${tabs}</div>`,
    controls: `<div class="dia-controls dia-ui" hidden>
<label class="dia-field"><span data-ctl="poslabel">Context length</span> <input type="range" min="0" max="${m.rows.length - 1}" step="1" value="${m.rows.length - 1}" data-ctl="pos" aria-describedby="${id}-read"></label>
<label class="dia-field">Scale <select data-ctl="scale"><option value="linear">Linear</option><option value="log">Logarithmic</option></select></label>
</div>
<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden>${esc(readoutFor('prefill', m, m.rows.length - 1))}</p>
<dl class="stats dia-stats dia-ui" data-ctl="stats" hidden>${statsFor('prefill', m).map((s) => `<div class="stat"><dt>${esc(s.label)}</dt><dd>${esc(s.value)}</dd></div>`).join('')}</dl>`,
  };
}

export function statsFor(mode, m) {
  const t = (label, value) => ({ label, value: String(value) }), e = m.exps[mode];
  if (mode === 'capacity') { const c = m.capacity[Math.min(2, m.capacity.length - 1)]; return [t('Width', c.dim), t('Derived context', n0(c.context)), t('Crosstalk edge', n0(c.bindRank)), t('Reads back clean', n0(c.cleanCapacity))]; }
  const md = MODES.find((x) => x.id === mode), r = m.last;
  return [t('Longest prompt', `${n0(r.tokens)} tokens`), t('Softmax', `${sig(r[md.key].softmax)} ${md.unit}`), t('Holographic', `${sig(r[md.key].holographic)} ${md.unit}`), t('Growth exponent', `${e.softmax.toFixed(1)} against ${e.holographic.toFixed(1)}`)];
}

export function tablesHtml(d, m) {
  const row = (cells, head) => '<tr>' + cells.map((c, k) => (k === 0 && head ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
  const cost = m.rows.map((r) => row([n0(r.tokens), sig(r.prefillMs.softmax), sig(r.prefillMs.holographic), sig(r.stepUs.softmax), sig(r.stepUs.holographic), sig(r.cacheKb.softmax), sig(r.cacheKb.holographic)], true)).join('');
  const cap = m.capacity.map((c) => row([String(c.dim), String(c.shifts), String(c.layers), n0(c.context), n0(c.bindRank), n0(c.cleanCapacity)], true)).join('');
  return `<details class="dia-data"><summary>Data behind the diagram</summary>
<div class="tbl-wrap"><table class="tbl"><caption>Both cores at ${m.shape.dModel} channels, ${m.shape.shifts} shifts and ${m.shape.layers} layers, which gives them the same parameter count, driven with the same random tokens. Medians of ${d.method.reps} runs on one thread.</caption>
<thead><tr><th scope="col">Tokens in context</th><th scope="col">Read the prompt, softmax (ms)</th><th scope="col">Read the prompt, holographic (ms)</th><th scope="col">One more token, softmax (µs)</th><th scope="col">One more token, holographic (µs)</th><th scope="col">Cache, softmax (KB)</th><th scope="col">Cache, holographic (KB)</th></tr></thead>
<tbody>${cost}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>The shape HoloShape derives from a width, and the capacity it reports for that shape</caption>
<thead><tr><th scope="col">Width</th><th scope="col">Shifts</th><th scope="col">Layers</th><th scope="col">Derived context (tokens)</th><th scope="col">Crosstalk edge (tokens)</th><th scope="col">Reads back 90% clean (tokens)</th></tr></thead>
<tbody>${cap}</tbody></table></div>
</details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  if (!m.samePar) out.push('the two cores do not have the same parameter count');
  if (m.exps.prefill.softmax < 1.5) out.push(`softmax prefill exponent ${m.exps.prefill.softmax.toFixed(2)} is not quadratic`);
  if (m.exps.prefill.holographic > 1.3) out.push(`holographic prefill exponent ${m.exps.prefill.holographic.toFixed(2)} is not linear`);
  if (Math.abs(m.exps.step.holographic) > 0.25) out.push('holographic per-token cost is not flat');
  if (m.exps.step.softmax < 0.6) out.push('softmax per-token cost does not grow with context');
  if (m.flatKb == null) out.push('holographic cache size is not constant');
  if (m.exps.cache.softmax < 0.8) out.push('softmax cache does not grow with context');
  if (!/Memory the serve cache|Cache, softmax/.test(body)) out.push('cost table missing');
  return out;
}

// ---------------------------------------------------------------- browser
export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage');
  stage.textContent = '';
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const q = (s) => root.querySelector(s);
  const ui = { pos: q('[data-ctl=pos]'), scale: q('[data-ctl=scale]'), label: q('[data-ctl=poslabel]'), readout: q('[data-ctl=readout]'), stats: q('[data-ctl=stats]') };
  const S = { mode: 'prefill', i: m.rows.length - 1, log: false, narrow: false };
  function render() {
    const cap = S.mode === 'capacity';
    S.narrow = stage.getBoundingClientRect().width < 520;
    stage.innerHTML = chartSvg(S.mode, d, m, { log: S.log, i: S.i, narrow: S.narrow });
    ui.readout.textContent = readoutFor(S.mode, m, S.i, S.log);
    ui.label.textContent = cap ? 'Model width' : 'Context length';
    ui.pos.max = String((cap ? m.capacity.length : m.rows.length) - 1);
    ui.pos.value = String(S.i);
    ui.scale.disabled = cap;
    ui.stats.innerHTML = statsFor(S.mode, m).map((x) => `<div class="stat"><dt>${esc(x.label)}</dt><dd>${esc(x.value)}</dd></div>`).join('');
  }
  new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== S.narrow) render(); }).observe(stage);
  tablist(q('[role="tablist"]'), (mode) => { S.mode = mode; S.i = mode === 'capacity' ? 2 : m.rows.length - 1; render(); });
  ui.pos.addEventListener('input', () => { S.i = Number(ui.pos.value); render(); });
  ui.scale.addEventListener('change', () => { S.log = ui.scale.value === 'log'; render(); });
  render();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="algformer-cost"]').forEach((root) => {
    const blob = root.querySelector('script[type="application/json"]');
    if (blob) mount(root, JSON.parse(blob.textContent));
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}

export const interactiveStage = false;
