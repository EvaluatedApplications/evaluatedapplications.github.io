// Explainer diagram: the engine learns what the workload reads. Three views of recorded counters (blocks read by a repeated set of questions
// after writes, the cost of keeping an index through writes, the blocks a priced move saves). Every figure drawn or quoted is typed once in
// data/diagrams/holodb-learns.json with the file it came from. The figure opens on its final frame and animates only on Play; the motion is
// schematic and the caption says so. Module contract: metrics / staticSvg / controlsHtml / tablesHtml / verify / mount.
import { hasDom, tablist } from './core.js';
import { esc, fmt, clamp, ph, ease, pen, svgOpen, playControls, player, tablesWrap, tbl } from './explain-kit.js';

const range = (r) => (r.lo === r.hi ? fmt(r.lo) : `${fmt(r.lo)} to ${fmt(r.hi)}`);
const view = (d, id) => d.views.find((v) => v.id === id);

export function metrics(d) {
  const rec = view(d, 'records'), up = view(d, 'upkeep'), ga = view(d, 'gather');
  const arm = (id) => rec.arms.find((a) => a.id === id), me = (id) => up.metrics.find((m) => m.id === id), bar = (id) => ga.bars.find((b) => b.id === id);
  return { views: d.views.map((v) => v.id), rec, up, ga, arm, me, bar };
}

// Numbers quoted in page prose, read from the recorded file.
export function limitFacts(m) {
  const on = m.arm('on'), off = m.arm('off');
  return {
    blocksAdjust: String(on.blocks[0]), blocksDropUpdate: String(off.blocks[0]), blocksDropDelete: String(off.blocks[1]), blocksDropConsolidate: String(off.blocks[2]),
    pinsBefore: range(m.me('pins').before), pinsAfter: range(m.me('pins').after),
    gatherRecords: String(m.bar('records').value), gatherNew: String(m.bar('new').value),
  };
}

export function captionFor(m, id) {
  const v = { records: m.rec, upkeep: m.up, gather: m.ga }[id];
  return `${v.setup} ${v.note} Recorded counters; the motion is illustrative.`;
}

function bar(p, x, y, w, h, frac, cls, tailFrac) {
  const fw = Math.max(frac > 0 ? 2 : 0, w * frac);
  p.r(x, y, w, h, 'hl-track', 3);
  p.r(x, y, fw, h, 'hl-bar ' + cls, 3);
  if (tailFrac && tailFrac > frac) p.r(x + fw, y, w * tailFrac - fw, h, 'hl-bar tail ' + cls, 3);
}

function drawRecords(v, t, W, narrow) {
  const p = pen(), pad = 10, L = narrow ? 0 : 168, R = 54, bw = W - pad * 2 - L - R, max = 370;
  let y = 8;
  v.phases.forEach((ph_, i) => {
    const a = ease(ph(t, i / 3, (i + 0.8) / 3)), shown = t >= i / 3 - 1e-9 || a > 0;
    p.t(pad, y + 12, ph_.label, 'hl-lane' + (shown ? '' : ' dim')); y += 20;
    v.arms.forEach((arm) => {
      const val = arm.blocks[i], cls = arm.id;
      if (narrow) { p.t(pad, y + 10, arm.label, 'hl-note'); y += 13; }
      else p.t(pad + L - 8, y + 11, arm.label, 'hl-note', 'end');
      bar(p, pad + L, y, bw, 14, (val / max) * a, cls);
      if (a > 0.02) p.t(pad + L + bw * (val / max) * a + 6, y + 11, fmt(val * a >= val - 0.5 ? val : val * a), 'hl-val');
      y += narrow ? 22 : 20;
    });
    y += 8;
  });
  p.t(pad, y + 10, 'Blocks read by the next run of the six questions, linear scale from 0 to ' + max, 'hl-ax');
  return { h: y + 20, body: p.g.join('') };
}

function drawUpkeep(v, t, W, narrow) {
  const p = pen(), pad = 10, bw = W - pad * 2 - (narrow ? 96 : 150);
  let y = 8;
  v.metrics.forEach((m, i) => {
    const n = v.metrics.length, a = ease(ph(t, i / n, (i + 0.8) / n)), max = Math.max(m.before.hi, m.after.hi);
    p.t(pad, y + 12, m.label, 'hl-lane' + (a > 0 || t >= i / n ? '' : ' dim')); y += 20;
    [['before', 'before'], ['after', 'after']].forEach(([k, cls]) => {
      const r = m[k];
      p.t(pad, y + 10, v.series[k], 'hl-note'); y += 13;
      bar(p, pad, y, bw, 14, (r.lo / max) * a, cls, (r.hi / max) * a);
      if (a > 0.02) p.t(pad + bw + 8, y + 11, a >= 0.999 ? range(r) : fmt(r.hi * a), 'hl-val');
      y += 22;
    });
    y += 6;
  });
  return { h: y + 4, body: p.g.join('') };
}

function drawGather(v, t, W, narrow) {
  const p = pen(), pad = 10, bw = W - pad * 2 - 54, max = 300;
  let y = 8;
  v.bars.forEach((b, i) => {
    const n = v.bars.length, a = ease(ph(t, i / n, (i + 0.8) / n));
    p.t(pad, y + 12, b.label, 'hl-lane'); y += 18;
    bar(p, pad, y, bw, 18, (b.value / max) * a, b.id === 'new' ? 'after' : 'before');
    if (a > 0.02) p.t(pad + bw * (b.value / max) * a + 6, y + 14, fmt(b.value * a >= b.value - 0.5 ? b.value : b.value * a), 'hl-val');
    y += 34;
  });
  p.t(pad, y, 'Blocks read per round, linear scale from 0 to ' + max, 'hl-ax');
  return { h: y + 10, body: p.g.join('') };
}

const DRAW = { records: drawRecords, upkeep: drawUpkeep, gather: drawGather };

export function viewSvg(d, id, t, narrow = false, uid = 'hl') {
  const v = view(d, id), W = narrow ? 360 : 700, out = DRAW[id](v, clamp(t), W, narrow);
  return svgOpen('hl-svg', W, out.h, uid, `${v.tab}: ${v.promise} Recorded counters, illustrative motion.`) + out.body + '</svg>';
}

export const staticSvg = (d) => viewSvg(d, d.views[0].id, 1, false, 'hl-' + d.id);

export function controlsHtml(id, m, d) {
  const tabs = d.views.map((v, k) => `<button type="button" role="tab" id="${id}-tab-${v.id}" data-mode="${v.id}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(v.tab)}</button>`).join('');
  const v0 = d.views[0];
  return {
    tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="What the engine learns" hidden>${tabs}</div>`,
    controls: `<div class="hw-copy"><p class="hw-promise" data-ctl="promise">${esc(v0.promise)}</p><p class="hw-how" data-ctl="how">${esc(v0.how)}</p></div>
${playControls()}
<p class="dia-readout hw-caption" data-ctl="readout" role="status">${esc(captionFor(m, v0.id))}</p>`,
  };
}

export function tablesHtml(d, m) {
  const rec = m.rec, up = m.up, ga = m.ga;
  const t1 = tbl(`${rec.tab}: ${rec.unit}`, ['Arm', ...rec.phases.map((p) => p.label)], rec.arms.map((a) => [a.label, ...a.blocks.map(fmt)]));
  const t2 = tbl(`${up.tab}: ${up.setup}`, ['Measure', 'Charged as a rebuild per write', 'Priced from the measured delta cost'], up.metrics.map((x) => [`${x.label} (${x.unit})`, range(x.before), range(x.after)]));
  const t3 = tbl(`${ga.tab}: ${ga.setup}`, ['Arm', 'Blocks read per round'], ga.bars.map((b) => [b.label, fmt(b.value)]));
  return tablesWrap('Data behind the diagram', t1 + t2 + t3);
}

export function verify(d, body) {
  const out = [], m = metrics(d);
  const on = m.arm('on'), off = m.arm('off');
  on.blocks.forEach((b, i) => { if (!(b < off.blocks[i])) out.push(`records: adjusting does not read fewer blocks than dropping at phase ${i}`); });
  const pins = m.me('pins');
  if (!(pins.after.hi < pins.before.lo)) out.push('upkeep: priced upkeep does not use fewer pins');
  if (!(m.bar('new').value < m.bar('records').value)) out.push('gather: the bars are not in falling order');
  for (const v of d.views) { if (!v.file || !v.promise || !v.how || !v.setup) out.push(`${v.id}: needs a file, promise, how and setup`); }
  if ((body.match(/<tbody>/g) || []).length !== 3) out.push('table alternative needs three tables');
  return out;
}

export const interactiveStage = false;

export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage'), q = (s) => root.querySelector(s);
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const S = { id: d.views[0].id, narrow: stage.getBoundingClientRect().width < 520 };
  const pl = player(root, (t) => { stage.innerHTML = viewSvg(d, S.id, t, S.narrow, 'hl-' + d.id); }, 6500);
  tablist(q('[role="tablist"]'), (id) => {
    const v = view(d, id); S.id = id;
    q('[data-ctl=promise]').textContent = v.promise; q('[data-ctl=how]').textContent = v.how; q('[data-ctl=readout]').textContent = captionFor(m, id);
    pl.reset();
  });
  if (typeof ResizeObserver === 'function') new ResizeObserver(() => { const n = stage.getBoundingClientRect().width < 520; if (n !== S.narrow) { S.narrow = n; pl.paint(); } }).observe(stage);
  pl.paint();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="holodb-learns"]').forEach((root) => { const b = root.querySelector('script[type="application/json"]'); if (b) mount(root, JSON.parse(b.textContent)); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
