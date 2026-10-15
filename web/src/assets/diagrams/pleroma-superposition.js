// Phasor diagram: "One pass, every item". Three items sit in one Pleroma; one kernel (1x identity plus 2x flip a flag
// and age a tick) is applied in a single pass, and every read changes at once.
// Data in: pleroma-superposition.json, written by web/generators/pleroma-demos by running the PUBLISHED package
// (the per-item mapping shown here is checked against the package's own joint counts by the generator).
import { hasDom, prefersReducedMotion, readTokens, rgba, fitCanvas } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const bin = (n, bits) => n.toString(2).padStart(bits, '0');
const NS = 'http://www.w3.org/2000/svg';

export function metrics(d) {
  const T = d.shape.timePeriod, FB = d.shape.flagBits, W = d.shape.flagWords;
  const idx = (k) => d.items.findIndex((i) => i.key === k);
  const flagSrc = Array(W).fill(-1), timeSrc = Array(T).fill(-1), jointSrc = Array(W * T).fill(-1);
  d.mapping.forEach((m) => { const i = idx(m.key); if (flagSrc[m.to.flags] < 0) flagSrc[m.to.flags] = i; if (timeSrc[m.to.time] < 0) timeSrc[m.to.time] = i; if (jointSrc[m.to.flags * T + m.to.time] < 0) jointSrc[m.to.flags * T + m.to.time] = i; });
  const befFlag = Array(W).fill(-1), befTime = Array(T).fill(-1), befJoint = Array(W * T).fill(-1);
  d.items.forEach((it, i) => { befFlag[it.flags] = i; befTime[it.time] = i; befJoint[it.flags * T + it.time] = i; });
  const nz = (a) => a.filter((v) => v !== 0).length;
  return {
    T, FB, W, flagSrc, timeSrc, jointSrc, befFlag, befTime, befJoint,
    totalBefore: d.before.total, totalAfter: d.after.total, items: d.items.length,
    jointNonzeroAfter: nz(d.after.joint), maxCount: Math.max(...d.after.flags, ...d.after.times, ...d.before.flags, ...d.before.times),
  };
}

const kernelText = (d) => d.kernel.map((e) => (e.flagMask === 0 && e.timeShift === 0 ? `${e.weight}x identity` : `${e.weight}x (flip flag bit 0, age ${e.timeShift} tick)`)).join(' + ');

export function readoutFor(phase, d, m) {
  if (phase === 'items') return `${m.items} items: each is a key with flags, a time and a count.`;
  if (phase === 'store') return `One store, one array of exact integers: ${d.shape.harmonicsPerKeyLine} harmonics per key line (2K+1 with K = ${d.shape.keyCapacity}), ${d.shape.flagWords} flag words, ${d.shape.timePeriod} time ticks and ${d.shape.jointCells} flags by time cells. Total count ${m.totalBefore}.`;
  if (phase === 'kernel') return `One kernel: ${kernelText(d)}. One pass over the whole store.`;
  if (phase === 'sweep') return 'The pass moves across the store and changes every face at once.';
  return `One pass changed every item at once. Total count ${m.totalBefore} + ${d.kernel[1].weight}*${m.totalBefore} = ${m.totalAfter}. Every flag and time count is exact, and the cost of the pass does not depend on how many items are stored.`;
}

// ---------------------------------------------------------------- server-rendered pieces
export function staticSvg(d) {
  const m = metrics(d), cw = 40;
  const bars = (arr, y0, labels, cls) => arr.map((v, i) => { const h = (v / m.maxCount) * 60; return `<rect class="${cls}" x="${20 + i * cw}" y="${y0 - h}" width="${cw - 8}" height="${Math.max(h, 0.5)}" rx="3"/><text x="${20 + i * cw + (cw - 8) / 2}" y="${y0 + 14}" text-anchor="middle">${labels(i)}</text>${v ? `<text class="val" x="${20 + i * cw + (cw - 8) / 2}" y="${y0 - h - 4}" text-anchor="middle">${v}</text>` : ''}`; }).join('');
  return `<svg class="dia-plan sp-plan" viewBox="0 0 360 230" role="img" aria-labelledby="sp-t sp-d"><title id="sp-t">Counts after the pass</title><desc id="sp-d">Count per flag word: ${d.after.flags.map((v, i) => `${bin(i, m.FB)} is ${v}`).join(', ')}. Count per time tick: ${d.after.times.map((v, i) => `${i} is ${v}`).join(', ')}.</desc>
<text class="lab" x="20" y="14">Count per flag word</text>${bars(d.after.flags, 90, (i) => bin(i, m.FB), 'bar')}
<text class="lab" x="20" y="134">Count per time tick</text>${bars(d.after.times, 210, (i) => i, 'bar t')}</svg>`;
}

export function controlsHtml(id, m, d) {
  const items = d.items.map((it, i) => `<div class="sp-item i${i} in done"><b>Key ${it.key}</b>, flags ${bin(it.flags, m.FB)}, time ${it.time}, count ${it.count}<span class="sp-after">${d.mapping.filter((x) => x.key === it.key).map((x) => `<span>${x.weight}x to key ${x.key}, flags ${bin(x.to.flags, m.FB)}, time ${x.to.time}, count ${x.to.count}</span>`).join('')}</span></div>`).join('');
  return {
    tabs: '',
    controls: `<div class="sp-side"><h3 class="sp-h">Three items</h3><div class="sp-items">${items}</div>
<p class="sp-kernel">kernel = ${esc(kernelText(d))}</p>
<div class="dia-controls dia-ui" hidden><span class="dia-seg" role="group" aria-label="Show"><button type="button" class="chip" data-state="before" aria-pressed="false">Before the pass</button><button type="button" class="chip" data-state="after" aria-pressed="true">After the pass</button></span><button type="button" class="btn btn-ghost btn-sm" data-ctl="replay">Replay</button></div>
<p class="dia-readout dia-ui" data-ctl="readout" role="status" hidden>${esc(readoutFor('done', d, m))}</p></div>`,
  };
}

export function tablesHtml(d, m) {
  const row = (cells) => '<tr>' + cells.map((c, i) => (i === 0 ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
  const joint = [];
  d.after.joint.forEach((v, i) => { if (v) joint.push([`flags ${bin(Math.floor(i / m.T), m.FB)}, tick ${i % m.T}`, d.before.joint[i], v]); });
  return `<details class="dia-data"><summary>Counts before and after the pass</summary>
<div class="tbl-wrap"><table class="tbl"><caption>Count per flag word</caption><thead><tr><th scope="col">Flag word</th><th scope="col">Before</th><th scope="col">After</th></tr></thead><tbody>${d.after.flags.map((v, i) => row([bin(i, m.FB), d.before.flags[i], v])).join('')}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Count per time tick</caption><thead><tr><th scope="col">Tick</th><th scope="col">Before</th><th scope="col">After</th></tr></thead><tbody>${d.after.times.map((v, i) => row([i, d.before.times[i], v])).join('')}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Flags by time joint cells that are not zero after the pass</caption><thead><tr><th scope="col">Cell</th><th scope="col">Before</th><th scope="col">After</th></tr></thead><tbody>${joint.map((r) => row(r)).join('')}</tbody></table></div>
</details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  if (d.after.total !== d.after.flags.reduce((a, b) => a + b, 0)) out.push('after total does not equal the flag counts');
  if (d.after.total !== d.after.times.reduce((a, b) => a + b, 0)) out.push('after total does not equal the time counts');
  if (d.after.joint.reduce((a, b) => a + b, 0) !== d.after.total) out.push('joint cells do not sum to the total');
  if (!body.includes(`Count per flag word`)) out.push('no flag table');
  return out;
}

// ---------------------------------------------------------------- animation (browser only)
export function mount(root, d) {
  const m = metrics(d), tok = readTokens(root), reduced = prefersReducedMotion();
  const stage = root.querySelector('.dia-stage');
  stage.textContent = '';
  const svg = document.createElementNS(NS, 'svg');
  const VW = 360, VH = 530;
  svg.setAttribute('viewBox', `0 0 ${VW} ${VH}`); svg.setAttribute('class', 'sp-svg'); svg.setAttribute('role', 'img');
  svg.setAttribute('aria-label', 'The store as bands: key line, count per flag word, count per time tick, and the flags by time cells. A line sweeps through them when the pass is applied.');
  stage.append(svg);
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const cols = [tok.spectrum[0], tok.spectrum[3], tok.spectrum[4]];
  const q = (s) => root.querySelector(s);
  const readout = q('[data-ctl=readout]');
  const stateBtns = [...root.querySelectorAll('[data-state]')];
  const itemEls = [...root.querySelectorAll('.sp-item')];

  const el = (name, attrs, parent) => { const e = document.createElementNS(NS, name); for (const k in attrs) e.setAttribute(k, attrs[k]); (parent || svg).append(e); return e; };
  const text = (x, y, s, cls, anchor) => { const e = el('text', { x, y, class: cls || '', 'text-anchor': anchor || 'start' }); e.textContent = s; return e; };
  const cw = 40, flagBase = 190, timeBase = 322, gridY = 372, gcw = 40, gch = 14;
  text(10, 14, `Key line: ${d.shape.harmonicsPerKeyLine} harmonics (2K+1, K = ${d.shape.keyCapacity})`, 'lab');
  const keyCells = Array.from({ length: d.shape.harmonicsPerKeyLine }, (_, i) => el('rect', { x: 10 + i * 38, y: 22, width: 34, height: 22, rx: 4, class: 'cell' }));
  text(10, 76, 'Count per flag word', 'lab');
  const fb = Array.from({ length: m.W }, (_, i) => ({ r: el('rect', { x: 10 + i * cw, y: flagBase, width: cw - 8, height: 0.5, rx: 3 }), v: text(10 + i * cw + (cw - 8) / 2, flagBase - 4, '', 'val', 'middle') }));
  Array.from({ length: m.W }, (_, i) => text(10 + i * cw + (cw - 8) / 2, flagBase + 14, bin(i, m.FB), 'tick', 'middle'));
  text(10, 226, 'Count per time tick', 'lab');
  const tb = Array.from({ length: m.T }, (_, i) => ({ r: el('rect', { x: 10 + i * cw, y: timeBase, width: cw - 8, height: 0.5, rx: 3 }), v: text(10 + i * cw + (cw - 8) / 2, timeBase - 4, '', 'val', 'middle') }));
  Array.from({ length: m.T }, (_, i) => text(10 + i * cw + (cw - 8) / 2, timeBase + 14, i, 'tick', 'middle'));
  text(10, 362, `Flags by time cells: ${m.W} x ${m.T} = ${d.shape.jointCells}`, 'lab');
  const jc = [];
  for (let t = 0; t < m.T; t++) for (let f = 0; f < m.W; f++) {
    const x = 10 + f * gcw, y = gridY + 6 + t * (gch + 2);
    jc[f * m.T + t] = { r: el('rect', { x, y, width: gcw - 4, height: gch, rx: 2, class: 'cell' }), v: text(x + (gcw - 4) / 2, y + gch - 3, '', 'gv', 'middle') };
  }
  const sweep = el('rect', { x: 0, y: 0, width: VW, height: 3, class: 'sweep', opacity: 0 });
  const ys = { flags: flagBase - 70, time: timeBase - 70, joint: gridY, key: 22 };

  const S = { n: m.items, sweepY: -1, done: true, timer: null, raf: 0 };
  const blend = (before, after, y) => { if (S.done) return 1; if (S.sweepY < 0) return 0; return Math.max(0, Math.min(1, (S.sweepY - y) / 40)); };
  const colorOf = (i, fallback) => (i >= 0 ? cols[i % cols.length] : fallback);

  function render() {
    const lit = S.n > 0;
    keyCells.forEach((c) => { c.setAttribute('fill', rgba(tok.accent, lit ? 0.5 : 0.08)); });
    const maxC = m.maxCount;
    const bars = (arr, base, bef, aft, befSrc, aftSrc, y) => arr.forEach((b, i) => {
      const p = blend(0, 1, y), v = bef[i] + (aft[i] - bef[i]) * p;
      const show = S.n >= (befSrc[i] >= 0 ? befSrc[i] + 1 : 0);
      const src = p > 0.5 ? aftSrc[i] : befSrc[i];
      const val = show || p > 0.5 ? v : 0;
      const h = (val / maxC) * 70;
      b.r.setAttribute('y', base - Math.max(h, 0.5)); b.r.setAttribute('height', Math.max(h, 0.5));
      b.r.setAttribute('fill', val > 0 ? colorOf(src, tok.faint) : rgba(tok.border2, 0.6));
      b.v.setAttribute('y', base - Math.max(h, 0.5) - 4); b.v.textContent = val > 0 ? Math.round(val) : '';
    });
    bars(fb, flagBase, d.before.flags, d.after.flags, m.befFlag, m.flagSrc, ys.flags);
    bars(tb, timeBase, d.before.times, d.after.times, m.befTime, m.timeSrc, ys.time);
    jc.forEach((c, i) => {
      const p = blend(0, 1, ys.joint), bv = d.before.joint[i], av = d.after.joint[i], v = bv + (av - bv) * p;
      const show = S.n >= (m.befJoint[i] >= 0 ? m.befJoint[i] + 1 : 0);
      const val = show || p > 0.5 ? v : 0, src = p > 0.5 ? m.jointSrc[i] : m.befJoint[i];
      c.r.setAttribute('fill', val > 0 ? rgba(colorOf(src, tok.faint), 0.25 + 0.6 * Math.min(1, val / maxC)) : rgba(tok.border2, 0.35));
      c.v.textContent = val > 0 ? Math.round(val) : '';
    });
    if (S.sweepY >= 0) { sweep.setAttribute('y', S.sweepY); sweep.setAttribute('opacity', '0.9'); sweep.setAttribute('fill', tok.accentInk); } else sweep.setAttribute('opacity', '0');
    itemEls.forEach((e, i) => { e.classList.toggle('in', S.n > i); e.classList.toggle('done', S.done); });
    root.dataset.state = S.done ? 'after' : 'before';
  }
  function say(phase) { readout.textContent = readoutFor(phase, d, m); }
  function setBtns() { stateBtns.forEach((b) => b.setAttribute('aria-pressed', String((b.dataset.state === 'after') === S.done))); }
  function stop() { clearTimeout(S.timer); clearTimeout(S.raf); S.raf = 0; }
  function show(state) { stop(); S.n = m.items; S.sweepY = -1; S.done = state === 'after'; say(S.done ? 'done' : 'store'); setBtns(); render(); }
  function play() {
    stop(); S.n = 0; S.sweepY = -1; S.done = false; setBtns(); render();
    const at = (ms, fn) => { S.timer = setTimeout(fn, ms); };
    let t = 0;
    const seq = [];
    for (let i = 0; i < m.items; i++) seq.push([i === 0 ? 200 : 450, () => { S.n = i + 1; if (i === 0) say('items'); render(); }]);
    seq.push([1500, () => { say('store'); render(); }]);
    seq.push([1800, () => { say('kernel'); }]);
    seq.push([1000, () => { say('sweep'); const t0 = performance.now(), DUR = 2600; const tick = () => { const p = Math.min(1, (performance.now() - t0) / DUR); S.sweepY = p * (VH + 20) - 10; render(); if (p < 1) S.raf = setTimeout(tick, 16); else { S.raf = 0; show('after'); } }; tick(); }]);
    const run = (i) => { if (i >= seq.length) return; at(seq[i][0], () => { seq[i][1](); run(i + 1); }); };
    run(0);
  }
  stateBtns.forEach((b) => b.addEventListener('click', () => show(b.dataset.state)));
  q('[data-ctl=replay]').addEventListener('click', play);
  show('after');
  if (!reduced && typeof IntersectionObserver === 'function') {
    const io = new IntersectionObserver((es) => { if (es[0].isIntersecting) { io.disconnect(); play(); } }, { threshold: 0.4 });
    io.observe(root);
  }
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="pleroma-superposition"]').forEach((root) => {
    const blob = root.querySelector('script[type="application/json"]');
    if (blob) mount(root, JSON.parse(blob.textContent));
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}

export const interactiveStage = false;
