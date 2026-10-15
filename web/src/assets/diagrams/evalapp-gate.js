// EvalApp signature diagram: "The gate". A batch of jobs is queued at once and every job needs a gated resource.
// The gate admits at most `bound` of them at a time and the rest wait; the bound is a declared number, and the best
// number is not the biggest one.
// Data in: evalapp-gate.json, written by web/generators/evalapp-gate by running the PUBLISHED package: for each gate
// bound, when each of 24 jobs was queued, admitted and finished. The slider only walks through those recordings.
import { hasDom, prefersReducedMotion, readTokens, rgba, mix, makeCamera, fitCanvas, tablist, loop } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');

// ---------------------------------------------------------------- pure data helpers
export function metrics(d) {
  const runs = d.fixedRuns.map((r) => {
    const jobs = r.jobs.map(([id, q, s, f, k]) => ({ id, q, s, f, k, wait: s - q }));
    const order = [...jobs].sort((a, b) => a.s - b.s || a.id - b.id), laneEnd = [];
    for (const j of order) {
      let l = laneEnd.findIndex((e) => e <= j.s);
      if (l < 0) { l = laneEnd.length; laneEnd.push(0); }
      laneEnd[l] = j.f; j.lane = l;
    }
    const waits = jobs.map((j) => j.wait), end = Math.max(...jobs.map((j) => j.f));
    return {
      bound: r.bound, makespan: r.makespanMs, peak: r.peakInFlight, lanes: laneEnd.length, jobs, end,
      maxWait: Math.max(...waits), meanWait: Math.round(waits.reduce((a, b) => a + b, 0) / waits.length),
      perSecond: Math.round((jobs.length / r.makespanMs) * 1000),
    };
  });
  const fastest = runs.reduce((a, b) => (b.makespan < a.makespan ? b : a));
  const slowest = runs.reduce((a, b) => (b.makespan > a.makespan ? b : a));
  const byBound = Object.fromEntries(runs.map((r) => [r.bound, r]));
  const widest = runs[runs.length - 1];
  return {
    runs, n: d.workload.jobs, baseMs: d.workload.baseMs, knee: d.workload.knee,
    fastest: fastest.bound, fastestMs: fastest.makespan, slowestBound: slowest.bound, slowestMs: slowest.makespan,
    firstMs: runs[0].makespan, widestBound: widest.bound, widestMs: widest.makespan,
    defaultIndex: Math.max(0, runs.findIndex((r) => r.bound === 4)), byBound,
  };
}

// Numbers the product page quotes in prose, taken from the data so the sentence cannot go stale.
export function limitFacts(m) {
  return {
    jobs: String(m.n), fastestBound: String(m.fastest), fastestMs: n0(m.fastestMs), oneMs: n0(m.firstMs),
    widestBound: String(m.widestBound), widestMs: n0(m.widestMs), knee: String(m.knee), baseMs: String(m.baseMs),
  };
}

export const waitingAt = (run, t) => run.jobs.filter((j) => j.s > t).length;
export const runningAt = (run, t) => run.jobs.filter((j) => j.s <= t && t < j.f).length;

export function readoutFor(m, i, t) {
  const r = m.runs[i];
  if (t == null || t >= r.end) {
    const rel = r.bound === m.fastest ? ' This is the fastest of the recorded bounds.' : r.bound === m.runs[0].bound ? ' One download at a time: every other job waits its turn.' : '';
    return `Gate bound ${r.bound}: all ${m.n} jobs finished in ${n0(r.makespan)} ms. At most ${r.peak} ran at once; the longest wait at the gate was ${n0(r.maxWait)} ms.${rel}`;
  }
  return `Gate bound ${r.bound}, ${n0(Math.round(t))} ms in: ${runningAt(r, t)} running, ${waitingAt(r, t)} waiting at the gate, ${r.jobs.filter((j) => j.f <= t).length} finished.`;
}

export function statsFor(m, i) {
  const r = m.runs[i], t = (label, value) => ({ label, value: String(value) });
  return [t('Gate bound', r.bound), t('All jobs finished in', `${n0(r.makespan)} ms`), t('Most running at once', r.peak), t('Longest wait', `${n0(r.maxWait)} ms`)];
}

// ---------------------------------------------------------------- server-rendered pieces
export function staticSvg(d) {
  const m = metrics(d), r = m.runs[m.defaultIndex], W = 360, H = 170, left = 26, top = 10, bottom = 26, right = 8;
  const pw = W - left - right, ph = H - top - bottom, T = r.end * 1.04, rowH = ph / r.lanes, bh = Math.min(16, rowH * 0.72);
  const x = (t) => left + (t / T) * pw;
  const bars = r.jobs.map((j) => `<rect class="job" x="${x(j.s).toFixed(1)}" y="${(top + j.lane * rowH + (rowH - bh) / 2).toFixed(1)}" width="${Math.max(1, x(j.f) - x(j.s)).toFixed(1)}" height="${bh.toFixed(1)}" rx="2"/>`).join('');
  const lanes = Array.from({ length: r.lanes }, (_, i) => `<text class="tick" x="${left - 6}" y="${(top + i * rowH + rowH / 2 + 3).toFixed(1)}" text-anchor="end">${i + 1}</text>`).join('');
  const step = [10, 20, 50, 100, 200, 500].find((s) => T / s <= 7) || 500;
  const ticks = []; for (let t = 0; t <= T; t += step) ticks.push(`<line class="grid" x1="${x(t).toFixed(1)}" y1="${top}" x2="${x(t).toFixed(1)}" y2="${top + ph}"/><text class="tick" x="${x(t).toFixed(1)}" y="${H - 10}" text-anchor="middle">${t}</text>`);
  return `<svg class="dia-plan ga-plan" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="ga-t ga-d" preserveAspectRatio="xMidYMid meet"><title id="ga-t">When each job ran with a gate bound of ${r.bound}</title><desc id="ga-d">${m.n} jobs queued at once pass through a gate that admits ${r.bound} at a time. All ${m.n} finish in ${n0(r.makespan)} milliseconds. The tables below give every bound.</desc>${ticks.join('')}${bars}${lanes}<text class="tick" x="${left}" y="${H - 1}">milliseconds from the start of the run</text></svg>`;
}

export function controlsHtml(id, m) {
  const tabs = m.runs.map((r, i) => `<button type="button" role="tab" id="${id}-tab-${r.bound}" data-mode="${i}" aria-selected="${i === m.defaultIndex}" aria-controls="${id}-stage" tabindex="${i === m.defaultIndex ? 0 : -1}">Bound ${r.bound}</button>`).join('');
  const stats = statsFor(m, m.defaultIndex).map((s) => `<div class="stat"><dt>${esc(s.label)}</dt><dd>${esc(s.value)}</dd></div>`).join('');
  return {
    tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="Gate bound" hidden>${tabs}</div>`,
    controls: `<div class="dia-controls dia-ui" hidden>
<label class="dia-field">Time <input type="range" min="0" max="1000" step="1" value="1000" data-ctl="time" aria-label="Time into the run"></label>
<label class="dia-field">Turn <input type="range" min="-180" max="180" step="1" value="-12" data-ctl="yaw"></label>
<label class="dia-field">Tilt <input type="range" min="10" max="70" step="1" value="52" data-ctl="tilt"></label>
<button type="button" class="btn btn-ghost btn-sm" data-ctl="play" aria-pressed="false">Replay</button>
</div>
<p class="dia-hint dia-ui" hidden>Pick a gate bound, then replay the run. Drag the scene or use the arrow keys while it is focused to turn and tilt it.</p>
<p class="dia-readout dia-ui" data-ctl="readout" role="status" hidden>${esc(readoutFor(m, m.defaultIndex, null))}</p>
<dl class="stats dia-stats dia-ui" data-ctl="stats" hidden>${stats}</dl>`,
  };
}

export function tablesHtml(d, m) {
  const row = (cells, head) => '<tr>' + cells.map((c, i) => (i === 0 && head ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
  const sum = m.runs.map((r) => row([`Bound ${r.bound}`, n0(r.makespan), r.peak, n0(r.maxWait), n0(r.meanWait), r.perSecond], true)).join('');
  const per = Array.from({ length: m.n }, (_, k) => row([`Job ${k + 1}`, ...m.runs.map((r) => { const j = r.jobs.find((x) => x.id === k + 1); return `${j.s} to ${j.f}`; })], true)).join('');
  return `<details class="dia-data"><summary>Data behind the diagram</summary>
<div class="tbl-wrap"><table class="tbl"><caption>${m.n} jobs queued at once, by gate bound. Each job waits for the gate, then runs a simulated download of ${m.baseMs} ms that slows once more than ${m.knee} are in flight.</caption>
<thead><tr><th scope="col">Gate bound</th><th scope="col">All jobs finished in (ms)</th><th scope="col">Most running at once</th><th scope="col">Longest wait (ms)</th><th scope="col">Mean wait (ms)</th><th scope="col">Jobs per second</th></tr></thead>
<tbody>${sum}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>When each job ran, in milliseconds from the start of the run (admitted to finished)</caption>
<thead><tr><th scope="col">Job</th>${m.runs.map((r) => `<th scope="col">Bound ${r.bound}</th>`).join('')}</tr></thead>
<tbody>${per}</tbody></table></div>
</details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  for (const r of m.runs) {
    if (r.peak > r.bound) out.push(`bound ${r.bound}: peak in flight ${r.peak} exceeds the bound`);
    if (r.lanes > r.bound) out.push(`bound ${r.bound}: timestamps show ${r.lanes} overlapping jobs`);
    if (r.jobs.length !== m.n) out.push(`bound ${r.bound}: expected ${m.n} jobs`);
    if (r.jobs.some((j) => !(j.q <= j.s && j.s < j.f))) out.push(`bound ${r.bound}: a job has out-of-order timestamps`);
  }
  if (m.fastest === m.runs[0].bound || m.fastest === m.runs[m.runs.length - 1].bound) out.push('the fastest bound is at an end of the range, so the "bigger is not better" story would be false');
  if (!/Longest wait/.test(body)) out.push('summary table missing');
  return out;
}

// ---------------------------------------------------------------- interactive scene (browser only)
const LEN = 34, SP = 1.5, LANE_W = 1.15, BOX_H = 1.0;

export function mount(root, d) {
  const m = metrics(d), reduced = prefersReducedMotion(), tok = readTokens(root);
  const cs = getComputedStyle(root), catRaw = cs.getPropertyValue('--cat-solid').trim();
  const cat = /^#|^rgb/.test(catRaw) ? catRaw : tok.accentInk;
  const FONT = cs.fontFamily, MONO = getComputedStyle(document.body).getPropertyValue('--mono').trim() || 'monospace';
  const stage = root.querySelector('.dia-stage');
  stage.textContent = '';
  const canvas = document.createElement('canvas'); canvas.className = 'dia-canvas'; canvas.setAttribute('aria-hidden', 'true'); stage.append(canvas);
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const c2 = canvas.getContext('2d');
  const q = (s) => root.querySelector(s);
  const ui = { time: q('[data-ctl=time]'), yaw: q('[data-ctl=yaw]'), tilt: q('[data-ctl=tilt]'), play: q('[data-ctl=play]'), readout: q('[data-ctl=readout]'), stats: q('[data-ctl=stats]') };

  const narrowStage = stage.getBoundingClientRect().width < 560;
  const S = { i: m.defaultIndex, t: null, yaw: narrowStage ? 84 : -12, tilt: narrowStage ? 46 : 52, playing: false, userPlay: false, moved: false };
  ui.yaw.value = S.yaw; ui.tilt.value = S.tilt;
  const run = () => m.runs[S.i];
  const tNow = () => (S.t == null ? run().end : S.t);
  const hold = 0.35;                                   // seconds the finished scene rests before it stops
  const DUR = 5.5;                                     // seconds to play the longest recording
  const rate = () => (Math.max(...m.runs.map((r) => r.end)) / DUR);   // recorded ms per second of playback
  ui.time.value = 1000;

  const palette = { run: cat, done: mix(cat, tok.bg2, 0.62), wait: tok.warn, post: mix(tok.border2, tok.soft, 0.35), floor: mix(tok.bg2, tok.surface2, 0.6) };

  function polygon(pts, fill, stroke) {
    c2.beginPath(); c2.moveTo(pts[0][0], pts[0][1]); for (let k = 1; k < pts.length; k++) c2.lineTo(pts[k][0], pts[k][1]); c2.closePath();
    c2.fillStyle = fill; c2.fill(); if (stroke) { c2.strokeStyle = stroke; c2.lineWidth = 1; c2.stroke(); }
  }
  // an axis-aligned box in world space, drawn with its visible faces
  function box(cam, P, x0, x1, y0, y1, z0, z1, col, alpha = 1) {
    c2.globalAlpha = alpha;
    const sides = [[0, 1, [x0, y1], [x1, y1]], [0, -1, [x0, y0], [x1, y0]], [1, 0, [x1, y0], [x1, y1]], [-1, 0, [x0, y0], [x0, y1]]];
    for (const [nx, ny, A, B] of sides) {
      const dn = cam.dir(nx, ny); if (dn[1] <= 0.001 || S.tilt < 1) continue;
      polygon([P(A[0], A[1], z0), P(B[0], B[1], z0), P(B[0], B[1], z1), P(A[0], A[1], z1)], mix(col, '#000000', dn[0] > 0 ? 0.42 : 0.58));
    }
    polygon([P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)], mix(col, '#ffffff', 0.12), rgba(tok.ink, 0.08));
    c2.globalAlpha = 1;
  }

  function scene(x, y, w, h, time) {
    const r = run(), t = tNow(), Tspan = r.end * 1.03;
    const X = (ms) => (ms / Tspan) * LEN;
    const depthY = Math.max(r.lanes * SP, 8.4);
    const cy = depthY / 2 - 0.4, cx = (LEN - 7) / 2;
    const sway = S.playing && !S.moved && !reduced ? 3 * Math.sin(time * 0.4) : 0;
    const cam = makeCamera(cx, cy, S.yaw + sway, S.tilt);
    const corners = []; for (const px of [-7, LEN]) for (const py of [-1.4, depthY + 0.2]) for (const pz of [0, 2.4]) corners.push(cam.p(px, py, pz));
    const u0 = Math.min(...corners.map((c) => c[0])), u1 = Math.max(...corners.map((c) => c[0])), v0 = Math.min(...corners.map((c) => c[1])), v1 = Math.max(...corners.map((c) => c[1]));
    const pad = 14, s = Math.min((w - pad * 2) / (u1 - u0), (h - pad * 2 - 14) / (v1 - v0));
    if (!(s > 3)) return;
    const ox = x + w / 2 - ((u0 + u1) / 2) * s, oy = y + (h - 14) / 2 - ((v0 + v1) / 2) * s;
    const P = (px, py, pz = 0) => { const p = cam.p(px, py, pz); return [ox + p[0] * s, oy + p[1] * s, p[2]]; };

    // floor under the lanes and the waiting room
    polygon([P(-7, -0.6), P(LEN, -0.6), P(LEN, depthY + 0.2), P(-7, depthY + 0.2)], palette.floor, rgba(tok.border2, 0.5));
    for (let k = 0; k < r.lanes; k++) {
      const ly = k * SP + (SP - LANE_W) / 2;
      polygon([P(0, ly), P(LEN, ly), P(LEN, ly + LANE_W), P(0, ly + LANE_W)], rgba(tok.ink, 0.045));
    }
    // time ticks along the front edge of the lanes
    const step = [10, 20, 50, 100, 200, 500].find((v) => Tspan / v <= 7) || 500;
    c2.font = `10px ${MONO}`; c2.fillStyle = tok.faint; c2.textAlign = 'center'; c2.strokeStyle = rgba(tok.soft, 0.28); c2.lineWidth = 1;
    for (let tt = 0; tt <= r.end; tt += step) { const a = P(X(tt), -0.6), b = P(X(tt), depthY + 0.2); c2.beginPath(); c2.moveTo(a[0], a[1]); c2.lineTo(b[0], b[1]); c2.stroke(); }
    const sorted = [];
    // the gate: one post between neighbouring lanes
    for (let k = 0; k <= r.lanes; k++) { const py = k * SP - (SP - LANE_W) / 2 - 0.15; sorted.push({ kind: 'post', x0: -0.18, x1: 0.18, y0: py, y1: py + 0.3, z1: 2.1, depth: cam.p(0, py, 1)[2] }); }
    sorted.push({ kind: 'lintel', x0: -0.18, x1: 0.18, y0: -(SP - LANE_W) / 2 - 0.15, y1: r.lanes * SP - (SP - LANE_W) / 2 + 0.15, z0: 2.1, z1: 2.35, depth: cam.p(0, depthY / 2, 2.2)[2] + 0.001 });
    // waiting jobs in the room before the gate
    const waiting = r.jobs.filter((j) => j.s > t).sort((a, b) => a.s - b.s);
    const cols = 4;
    waiting.forEach((j, n) => {
      const col = n % cols, row = Math.floor(n / cols), gx = -1.6 - col * 1.5, gy = depthY / 2 - ((Math.ceil(m.n / cols)) * 1.35) / 2 + row * 1.35 + 0.2;
      sorted.push({ kind: 'wait', x0: gx - 0.55, x1: gx + 0.55, y0: gy, y1: gy + 0.9, z1: 0.9, depth: cam.p(gx, gy, 0.4)[2] });
    });
    // jobs admitted so far
    for (const j of r.jobs) {
      if (j.s > t) continue;
      const end = Math.min(t, j.f), y0 = j.lane * SP + (SP - LANE_W) / 2;
      const gx0 = X(j.s) + 0.07, gx1 = Math.max(gx0 + 0.05, X(end) - 0.07);
      sorted.push({ kind: j.f <= t ? 'done' : 'run', x0: gx0, x1: gx1, y0, y1: y0 + LANE_W, z1: BOX_H, depth: cam.p((gx0 + gx1) / 2, y0 + LANE_W / 2, 0.5)[2] });
    }
    sorted.sort((a, b) => a.depth - b.depth);
    for (const it of sorted) {
      if (it.kind === 'post') box(cam, P, it.x0, it.x1, it.y0, it.y1, 0, it.z1, palette.post, 0.9);
      else if (it.kind === 'lintel') box(cam, P, it.x0, it.x1, it.y0, it.y1, it.z0, it.z1, palette.post, 0.55);
      else if (it.kind === 'wait') box(cam, P, it.x0, it.x1, it.y0, it.y1, 0, it.z1, palette.wait, 0.92);
      else box(cam, P, it.x0, it.x1, it.y0, it.y1, 0, it.z1, it.kind === 'run' ? palette.run : palette.done);
    }
    // labels
    const gl = P(0, -1.0, 2.5), wl = P(-4.2, depthY + 0.0, 0.2);
    c2.font = `600 11px ${FONT}`; c2.fillStyle = tok.ink;
    if (w < 560) {
      c2.textAlign = 'left'; c2.fillText(`Gate: ${r.bound} at a time`, x + 10, y + 16);
      c2.fillStyle = tok.warn; c2.font = `11px ${FONT}`; c2.fillText(`Waiting: ${waiting.length}`, x + 10, y + 31);
    } else {
      c2.textAlign = 'center';
      c2.fillText(`Gate: ${r.bound} at a time`, Math.max(x + 70, Math.min(x + w - 70, gl[0])), Math.max(y + 12, gl[1] - 4));
      c2.fillStyle = tok.warn; c2.font = `11px ${FONT}`;
      c2.fillText(`Waiting: ${waiting.length}`, Math.max(x + 40, Math.min(x + w - 40, wl[0])), Math.min(y + h - 16, wl[1] + 14));
    }
    c2.fillStyle = tok.faint; c2.font = `10px ${MONO}`; c2.textAlign = 'right';
    c2.fillText(`${n0(Math.round(Math.min(t, r.end)))} of ${n0(r.end)} ms`, x + w - 8, y + h - 3);
  }

  function chart(x, y, w, h) {
    const r = run(), t = tNow(), T = r.end * 1.03, L = 30, B = 20, Tp = 22, R = 8;
    const pw = w - L - R, ph = h - B - Tp, X = (ms) => x + L + (ms / T) * pw;
    const top = Math.max(m.n, ...m.runs.map((q2) => q2.bound)), Y = (v) => y + Tp + ph - (v / m.n) * ph;
    c2.font = `600 11px ${FONT}`; c2.textAlign = 'left'; c2.fillStyle = tok.ink; c2.fillText('Waiting and running over time', x + 4, y + 11);
    c2.strokeStyle = rgba(tok.soft, 0.25); c2.lineWidth = 1; c2.fillStyle = tok.faint; c2.font = `10px ${MONO}`; c2.textAlign = 'right';
    for (const v of [0, 8, 16, 24]) { c2.beginPath(); c2.moveTo(x + L, Y(v)); c2.lineTo(x + L + pw, Y(v)); c2.stroke(); c2.fillText(String(v), x + L - 5, Y(v) + 3); }
    c2.textAlign = 'center';
    const step = [10, 20, 50, 100, 200, 500].find((v) => T / v <= 6) || 500;
    for (let tt = 0; tt <= r.end; tt += step) c2.fillText(String(tt), X(tt), y + h - 6);
    // step functions from the recorded times
    const pts = [...new Set([0, ...r.jobs.flatMap((j) => [j.s, j.f]), r.end])].sort((a, b) => a - b);
    const line = (fn, col, fill) => {
      c2.beginPath(); let first = true, prevV = 0;
      for (const p of pts) { if (p > t) break; const v = fn(p); if (first) { c2.moveTo(X(p), Y(v)); first = false; } else { c2.lineTo(X(p), Y(prevV)); c2.lineTo(X(p), Y(v)); } prevV = v; }
      const endT = Math.min(t, r.end); c2.lineTo(X(endT), Y(prevV));
      c2.strokeStyle = col; c2.lineWidth = 2; c2.lineJoin = 'round'; c2.stroke();
      if (fill) { c2.lineTo(X(endT), Y(0)); c2.lineTo(X(0), Y(0)); c2.closePath(); c2.fillStyle = fill; c2.fill(); }
    };
    line((p) => waitingAt(r, p - 0.001), tok.warn, rgba(tok.warn, 0.12));
    line((p) => runningAt(r, p + 0.001), cat, rgba(cat, 0.14));
    // the declared bound is a ceiling
    c2.setLineDash([5, 4]); c2.strokeStyle = tok.ink; c2.lineWidth = 1.2; c2.beginPath(); c2.moveTo(x + L, Y(r.bound)); c2.lineTo(x + L + pw, Y(r.bound)); c2.stroke(); c2.setLineDash([]);
    c2.fillStyle = tok.ink; c2.textAlign = 'right'; c2.font = `10px ${MONO}`; c2.fillText(`bound ${r.bound}`, x + L + pw, Y(r.bound) - 4);
    c2.strokeStyle = tok.ink; c2.lineWidth = 1; c2.beginPath(); c2.moveTo(X(Math.min(t, r.end)), y + Tp); c2.lineTo(X(Math.min(t, r.end)), y + Tp + ph); c2.stroke();
    c2.textAlign = 'left'; c2.font = `10px ${FONT}`;
    c2.textAlign = 'right'; c2.fillStyle = cat; c2.fillText('running', x + w - 6, y + 11); c2.fillStyle = tok.warn; c2.fillText('waiting', x + w - 56, y + 11);
  }

  const hits = [];
  function bars(x, y, w, h) {
    hits.length = 0;
    const L = 8, B = 30, Tp = 22, n = m.runs.length, gap = 6, bw = (w - L * 2 - gap * (n - 1)) / n, ph = h - B - Tp;
    const maxMs = Math.max(...m.runs.map((r) => r.makespan));
    c2.font = `600 11px ${FONT}`; c2.textAlign = 'left'; c2.fillStyle = tok.ink; c2.fillText('Time to finish all jobs, by bound', x + 4, y + 11);
    m.runs.forEach((r, k) => {
      const bx = x + L + k * (bw + gap), bh = Math.max(2, (r.makespan / maxMs) * ph), by = y + Tp + ph - bh, sel = k === S.i;
      c2.fillStyle = sel ? cat : rgba(cat, 0.32); c2.beginPath(); c2.roundRect ? c2.roundRect(bx, by, bw, bh, 3) : c2.rect(bx, by, bw, bh); c2.fill();
      if (r.bound === m.fastest) { c2.strokeStyle = tok.ok; c2.lineWidth = 1.6; c2.stroke(); }
      c2.textAlign = 'center'; c2.fillStyle = sel ? tok.ink : tok.faint; c2.font = `10px ${MONO}`;
      c2.fillText(String(r.bound), bx + bw / 2, y + h - 16);
      if (sel || r.bound === m.fastest) c2.fillText(`${n0(r.makespan)}`, bx + bw / 2, by - 4);
      hits.push({ x0: bx, x1: bx + bw + gap, y0: y, y1: y + h, i: k });
    });
    c2.fillStyle = tok.faint; c2.font = `10px ${FONT}`; c2.textAlign = 'center'; c2.fillText('gate bound, with the selected and the fastest labelled in ms', x + w / 2, y + h - 3);
  }

  function draw(time, dt) {
    const { w, h, dpr } = fitCanvas(canvas);
    c2.setTransform(dpr, 0, 0, dpr, 0, 0); c2.clearRect(0, 0, w, h);
    if (S.playing && dt) {
      const r = run(), nt = (S.t == null ? 0 : S.t) + dt * rate();
      if (nt >= r.end + hold * rate()) { S.t = null; S.playing = false; ui.play.textContent = 'Replay'; ui.play.setAttribute('aria-pressed', 'false'); ui.time.value = 1000; refreshText(); }
      else { S.t = nt; ui.time.value = Math.round(Math.min(1, nt / r.end) * 1000); refreshText(); }
    }
    const narrow = w < 560;
    c2.save(); c2.beginPath();
    if (narrow) {
      const sh = h * 0.5; c2.rect(0, 0, w, sh); c2.clip(); scene(0, 0, w, sh, time); c2.restore();
      chart(0, sh + 6, w, h * 0.26 - 6); bars(0, h * 0.78, w, h * 0.22);
    } else {
      const sh = h * 0.6; c2.rect(0, 0, w, sh); c2.clip(); scene(0, 0, w, sh, time); c2.restore();
      chart(0, sh + 6, w * 0.6 - 6, h - sh - 6); bars(w * 0.62, sh + 6, w * 0.38, h - sh - 6);
    }
    return S.playing;
  }

  const lp = loop(draw);
  function refreshText() {
    ui.readout.textContent = readoutFor(m, S.i, S.t);
    ui.stats.innerHTML = statsFor(m, S.i).map((x) => `<div class="stat"><dt>${esc(x.label)}</dt><dd>${esc(x.value)}</dd></div>`).join('');
  }
  function changed() { lp.redraw(); lp.kick(); }
  function pick(i, play) {
    S.i = i; S.t = null; S.playing = false; ui.time.value = 1000;
    if (play && !reduced) { S.t = 0; S.playing = true; S.userPlay = true; ui.time.value = 0; }
    ui.play.textContent = S.playing ? 'Pause' : 'Replay'; ui.play.setAttribute('aria-pressed', String(S.playing));
    refreshText(); changed();
  }

  const tabs = tablist(root.querySelector('[role="tablist"]'), (mode) => { const i = Number(mode); if (i !== S.i) pick(i, true); });
  ui.time.addEventListener('input', () => { S.playing = false; ui.play.textContent = 'Replay'; ui.play.setAttribute('aria-pressed', 'false'); const v = Number(ui.time.value) / 1000; S.t = v >= 1 ? null : v * run().end; refreshText(); changed(); });
  ui.yaw.addEventListener('input', () => { S.yaw = Number(ui.yaw.value); S.moved = true; changed(); });
  ui.tilt.addEventListener('input', () => { S.tilt = Number(ui.tilt.value); S.moved = true; changed(); });
  ui.play.addEventListener('click', () => {
    if (S.playing) { S.playing = false; ui.play.textContent = 'Resume'; ui.play.setAttribute('aria-pressed', 'false'); }
    else { if (S.t == null || S.t >= run().end) S.t = 0; S.playing = true; S.userPlay = true; ui.play.textContent = 'Pause'; ui.play.setAttribute('aria-pressed', 'true'); }
    changed();
  });

  let drag = null;
  canvas.addEventListener('pointerdown', (e) => {
    const r = canvas.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top;
    const hit = hits.find((hh) => px >= hh.x0 && px <= hh.x1 && py >= hh.y0 && py <= hh.y1);
    if (hit) { tabs.select(hit.i); return; }
    drag = { x: e.clientX, y: e.clientY, type: e.pointerType }; canvas.setPointerCapture(e.pointerId);
  });
  canvas.addEventListener('pointermove', (e) => {
    if (!drag) return;
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y; drag.x = e.clientX; drag.y = e.clientY;
    S.yaw = ((S.yaw + dx * 0.4 + 540) % 360) - 180; S.moved = true;
    if (drag.type === 'mouse') S.tilt = Math.max(10, Math.min(70, S.tilt - dy * 0.3));
    ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed();
  });
  const end = () => { drag = null; };
  canvas.addEventListener('pointerup', end); canvas.addEventListener('pointercancel', end);
  stage.addEventListener('keydown', (e) => {
    const k = e.key; let used = true;
    if (k === 'ArrowLeft') S.yaw = ((S.yaw - 5 + 540) % 360) - 180;
    else if (k === 'ArrowRight') S.yaw = ((S.yaw + 5 + 540) % 360) - 180;
    else if (k === 'ArrowUp') S.tilt = Math.min(70, S.tilt + 3);
    else if (k === 'ArrowDown') S.tilt = Math.max(10, S.tilt - 3);
    else used = false;
    if (used) { e.preventDefault(); S.moved = true; ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed(); }
  });
  new ResizeObserver(() => lp.redraw()).observe(stage);
  new IntersectionObserver((es) => lp.setVisible(es[0].isIntersecting)).observe(root);
  document.addEventListener('visibilitychange', () => lp.kick());
  refreshText();
  lp.redraw();
  root.dataset.ready = 'true';
  // the first view replays once as the figure scrolls into sight, unless the visitor asked for reduced motion
  if (!reduced) {
    const io = new IntersectionObserver((es) => { if (es[0].isIntersecting) { io.disconnect(); pick(S.i, true); } }, { threshold: 0.4 });
    io.observe(root);
  }
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="evalapp-gate"]').forEach((root) => {
    const blob = root.querySelector('script[type="application/json"]');
    if (blob) mount(root, JSON.parse(blob.textContent));
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
