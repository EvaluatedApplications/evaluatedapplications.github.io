// Phasor signature diagram: "The map". A 3D pyramid with 3 faces (Syzygy, Monad, Ouroboros), 8 rungs and a lid:
// 25 cells, each a public Pleroma operation with its status, API and evidence.
// Data in: pleroma-map.json (migrated from the Phasor owner's map data; statuses drive the colours, never assumed).
// Module contract: metrics / staticSvg / controlsHtml / tablesHtml / mount / verify (see docs/design-language.md).
import { hasDom, prefersReducedMotion, readTokens, rgba, fitCanvas, loop } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const FACES = ['Syzygy', 'Monad', 'Ouroboros'];
const GLOSS = {
  COVERED: 'A public member of Pleroma, checked by property tests against brute-force or naive implementations.',
  'READ-TIME': 'Not superposable by nature: a choice made once, when the store is read.',
};

export function cells(d) {
  const out = [];
  d.rungs.forEach((r) => r.cells.forEach((c) => out.push({ ...c, rung: r.n, rungName: r.name, rungCaption: r.caption, fi: FACES.indexOf(c.face), lid: false })));
  const l = d.lid.cell;
  out.push({ ...l, rung: 9, rungName: d.lid.name, rungCaption: d.lid.description, face: '', fi: -1, lid: true });
  return out;
}

export function metrics(d) {
  const cs = cells(d);
  const by = {};
  cs.forEach((c) => { by[c.status] = (by[c.status] || 0) + 1; });
  return { cells: cs, total: cs.length, rungs: d.rungs.length, statuses: by, covered: by.COVERED || 0, readTime: by['READ-TIME'] || 0 };
}

// Numbers quoted in prose, taken from the data.
export function limitFacts(m) { return { cells: m.total, rungs: m.rungs }; }

// ---------------------------------------------------------------- server-rendered pieces
export function staticSvg(d) {
  const W = 360, cw = 108, ch = 28, left = 36, top = 34, gap = 4;
  const rows = [...d.rungs].reverse();
  const rects = rows.map((r, ri) => r.cells.map((c, k) => {
    const x = left + FACES.indexOf(c.face) * (cw + gap), y = top + ri * (ch + gap);
    return `<g><title>${esc(`Rung ${r.n}, ${c.face}: ${c.name}`)}</title><rect class="f${FACES.indexOf(c.face)}" x="${x}" y="${y}" width="${cw}" height="${ch}" rx="5"/><text x="${x + cw / 2}" y="${y + ch / 2 + 4}" text-anchor="middle">R${r.n}</text></g>`;
  }).join('') + `<text class="rl" x="${left - 8}" y="${top + ri * (ch + gap) + ch / 2 + 4}" text-anchor="end">${r.n}</text>`).join('');
  const faceLabels = FACES.map((f, i) => `<text class="fl" x="${left + i * (cw + gap) + cw / 2}" y="${top + 8 * (ch + gap) + 12}" text-anchor="middle">${f}</text>`).join('');
  const H = top + 8 * (ch + gap) + 26;
  const lidW = 3 * cw + 2 * gap;
  return `<svg class="dia-plan map-plan" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="pm-t pm-d"><title id="pm-t">The Pleroma map as a grid</title><desc id="pm-d">${d.rungs.length} rungs by ${FACES.length} faces, ${cells(d).length - 1} cells, plus the lid on top. The full list is in the table below.</desc>
<g><title>${esc('Lid: ' + d.lid.cell.name)}</title><rect class="lid" x="${left}" y="6" width="${lidW}" height="22" rx="5"/><text x="${left + lidW / 2}" y="21" text-anchor="middle">Lid</text></g>${rects}${faceLabels}</svg>`;
}

export function controlsHtml(id, m) {
  return {
    tabs: '',
    controls: `<div class="dia-controls dia-ui" hidden>
<label class="dia-field">Turn <input type="range" min="-180" max="180" step="1" value="-52" data-ctl="yaw"></label>
<label class="dia-field">Tilt <input type="range" min="-8" max="58" step="1" value="24" data-ctl="tilt"></label>
<span class="dia-seg" role="group" aria-label="Colour by"><button type="button" class="chip" data-color="face" aria-pressed="true">Face</button><button type="button" class="chip" data-color="status" aria-pressed="false">Status</button></span>
<button type="button" class="btn btn-ghost btn-sm" data-ctl="play" aria-pressed="true">Pause motion</button>
<button type="button" class="btn btn-ghost btn-sm" data-ctl="reset">Reset view</button>
</div>
<div class="map-legend dia-ui" hidden data-legend="face" aria-label="Colour key: faces"><span class="lg f0">Syzygy: the key cycle and its signed counts</span><span class="lg f1">Monad: the order face</span><span class="lg f2">Ouroboros: the flag cube</span><span class="lg lid">Lid</span></div>
<div class="map-legend dia-ui" hidden data-legend="status" aria-label="Colour key: status"><span class="lg st-ok">COVERED: a public member with property tests</span><span class="lg st-warn">READ-TIME: a choice made once, on reading</span><span class="lg st-other">Dashed edge: any other status</span></div>
<p class="dia-hint dia-ui" hidden>Drag to turn. Select a cell. With the view focused: arrows move between cells, Shift and arrows turn it, space pauses, Escape clears.</p>
<div class="map-panel dia-ui" hidden data-ctl="panel" role="region" aria-label="Selected cell" aria-live="polite"></div>`,
  };
}

export function tablesHtml(d, m) {
  const rows = m.cells.map((c) => `<tr><th scope="row">${c.lid ? 'Lid' : 'R' + c.rung}</th><td>${esc(c.face || 'Lid')}</td><td>${esc(c.name)}</td><td>${esc(c.description)}</td><td><code>${esc(c.api)}</code></td><td>${esc(c.status)}</td><td>${esc(c.evidence)}</td></tr>`).join('');
  return `<details class="dia-data"><summary>All ${m.total} cells as a table</summary><div class="tbl-wrap"><table class="tbl"><caption>The operation grid: ${m.rungs} rungs by ${FACES.length} faces, plus the lid</caption>
<thead><tr><th scope="col">Rung</th><th scope="col">Face</th><th scope="col">Cell</th><th scope="col">What it does</th><th scope="col">API</th><th scope="col">Status</th><th scope="col">Evidence</th></tr></thead><tbody>${rows}</tbody></table></div></details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  const trs = (body.match(/<tbody>[\s\S]*?<\/tbody>/) || [''])[0].split('<tr>').length - 1;
  if (trs !== m.total) out.push(`table has ${trs} rows for ${m.total} cells`);
  const ids = new Set(); m.cells.forEach((c) => { if (ids.has(c.id)) out.push('duplicate cell id ' + c.id); ids.add(c.id); });
  if (m.total !== d.totalCells + 1) out.push(`data says ${d.totalCells} cells plus the lid, found ${m.total}`);
  return out;
}

// ---------------------------------------------------------------- 3D pyramid (browser only)
const HH = 1.5, BODY = 0.8, LIDT = 0.86, YC = 0.62, DIST = 6.5, RIN = 0.5, INSET = 0.9;
const AN = [-60, 60, 180].map((d) => (d * Math.PI) / 180);
const V = (k, t) => { const s = 1 - t; return [Math.sin(AN[k]) * s, t * HH, Math.cos(AN[k]) * s]; };
const SIDEMID = [0, 1, 2].map((k) => { const a = AN[k], b = k === 2 ? AN[0] + 2 * Math.PI : AN[k + 1]; return (a + b) / 2; });
const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);

function inset(pts, k) {
  const n = pts.length; let cx = 0, cy = 0; pts.forEach((p) => { cx += p[0]; cy += p[1]; }); cx /= n; cy /= n;
  return pts.map((p) => [cx + (p[0] - cx) * k, cy + (p[1] - cy) * k]);
}
function inPoly(x, y, pts) {
  let c = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) if ((pts[i][1] > y) !== (pts[j][1] > y) && x < ((pts[j][0] - pts[i][0]) * (y - pts[i][1])) / (pts[j][1] - pts[i][1]) + pts[i][0]) c = !c;
  return c;
}

export function mount(root, d) {
  const m = metrics(d), CS = m.cells;
  const byId = Object.fromEntries(CS.map((c) => [c.id, c]));
  const stage = root.querySelector('.dia-stage');
  const reduced = prefersReducedMotion();
  const tok = readTokens(root);
  const FONT = getComputedStyle(root).fontFamily;
  root._labels = () => (S.rects || []).map((r) => ({ ...r }));
  const faceCol = [tok.spectrum[4], tok.spectrum[6], tok.spectrum[0]];
  stage.textContent = '';
  const canvas = document.createElement('canvas'); canvas.className = 'dia-canvas'; canvas.setAttribute('aria-hidden', 'true'); stage.append(canvas);
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const legends = [...root.querySelectorAll('[data-legend]')];
  const showLegend = (mode) => legends.forEach((l) => { l.hidden = l.dataset.legend !== mode; });
  showLegend('face');
  const ctx = canvas.getContext('2d');
  const q = (s) => root.querySelector(s);
  const ui = { yaw: q('[data-ctl=yaw]'), tilt: q('[data-ctl=tilt]'), play: q('[data-ctl=play]'), reset: q('[data-ctl=reset]'), panel: q('[data-ctl=panel]') };
  const colorBtns = [...root.querySelectorAll('[data-color]')];
  const RING = []; for (let i = 0; i <= 8; i++) RING.push([0, 1, 2].map((k) => V(k, (BODY * i) / 8)));
  const HOME = { yaw: -52, tilt: 24 };
  const S = { yaw: HOME.yaw, tilt: HOME.tilt, sel: null, mode: 'face', playing: !reduced, userPlay: false, spin: 0, tween: null, drag: null, time: 0 };
  ui.play.textContent = S.playing ? 'Pause motion' : 'Play motion'; ui.play.setAttribute('aria-pressed', String(S.playing));
  const anim = () => S.playing && (!reduced || S.userPlay);
  const polys = [];

  function statusColor(c) { return c.status === 'COVERED' ? tok.ok : c.status === 'READ-TIME' ? tok.warn : null; }

  function draw(time) {
    const { w, h, dpr } = fitCanvas(canvas);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, w, h);
    if (w < 80 || h < 80) return false;
    S.time = time;
    const narrow = w < 520;
    const yaw = ((S.yaw + S.spin) * Math.PI) / 180, pitch = (S.tilt * Math.PI) / 180;
    const cy = Math.cos(yaw), sy = Math.sin(yaw), cp = Math.cos(pitch), sp = Math.sin(pitch);
    const F = Math.min(w * (narrow ? 0.46 : 0.4), h * 0.46), CX = w * 0.54, CY = h * 0.5;
    const R = (p) => { const x = p[0] * cy + p[2] * sy, z = -p[0] * sy + p[2] * cy, y = p[1] - YC; return [x, y * cp - z * sp, y * sp + z * cp]; };
    const Pj = (r) => { const k = DIST / (DIST - r[2]); return [CX + F * r[0] * k, CY - F * r[1] * k]; };
    const bob = anim() ? 0.025 * Math.sin(time * 1.8) : 0;
    const raw = [], scr = [];
    for (let i = 0; i <= 8; i++) { raw[i] = []; scr[i] = []; for (let k = 0; k < 3; k++) { raw[i][k] = R(RING[i][k]); scr[i][k] = Pj(raw[i][k]); } }
    const lidLo = [0, 1, 2].map((k) => { const v = V(k, LIDT); v[1] += bob; return Pj(R(v)); });
    const apex = Pj(R([0, HH + bob, 0]));
    const vis = [], lit = [];
    for (let s = 0; s < 3; s++) {
      const mm = SIDEMID[s], n = [HH * Math.sin(mm), RIN, HH * Math.cos(mm)];
      const nx = n[0] * cy + n[2] * sy, nz = -n[0] * sy + n[2] * cy, ny = n[1];
      const ny2 = ny * cp - nz * sp, nz2 = ny * sp + nz * cp, len = Math.hypot(nx, ny2, nz2);
      const a = nx / len, b = ny2 / len, c = nz2 / len, p0 = raw[0][s];
      vis[s] = a * (0 - p0[0]) + b * (0 - p0[1]) + c * (DIST - p0[2]) > 0;
      lit[s] = 0.62 + 0.38 * clamp(a * -0.35 + b * 0.55 + c * 0.75, 0, 1);
    }
    const shim = anim() ? (time * 0.45) % 1.7 - 0.35 : -9;
    const boost = (frac) => (shim > -9 ? 0.3 * Math.exp(-Math.pow(frac - shim, 2) / 0.012) : 0);

    // base outline
    ctx.beginPath(); ctx.moveTo(scr[0][0][0], scr[0][0][1]); ctx.lineTo(scr[0][1][0], scr[0][1][1]); ctx.lineTo(scr[0][2][0], scr[0][2][1]); ctx.closePath();
    ctx.strokeStyle = rgba(tok.border2, 0.9); ctx.lineWidth = 1; ctx.stroke();
    polys.length = 0;
    const paint = (c, pts, fi, op, k) => {
      const base = c.lid ? tok.soft : faceCol[fi];
      const sc = S.mode === 'status' ? statusColor(c) : null;
      const col = sc || (S.mode === 'status' ? tok.faint : base);
      const sel = S.sel === c.id;
      ctx.beginPath(); pts.forEach((p, i) => (i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]))); ctx.closePath();
      ctx.globalAlpha = clamp(op, 0, 1);
      ctx.fillStyle = col; ctx.fill();
      ctx.globalAlpha = 1;
      ctx.lineWidth = sel ? 2.6 : 1; ctx.strokeStyle = sel ? tok.ink : rgba(tok.ink, 0.22);
      if (S.mode === 'status' && !sc) ctx.setLineDash([3, 3]);
      ctx.stroke(); ctx.setLineDash([]);
      polys.push({ id: c.id, pts, z: k });
    };
    CS.forEach((c) => {
      if (c.lid) return;
      const k = c.fi, k2 = (k + 1) % 3, r = c.rung - 1;
      if (!vis[k]) return;
      const quad = [scr[r][k], scr[r][k2], scr[r + 1][k2], scr[r + 1][k]];
      const pts = inset(quad, INSET);
      paint(c, pts, k, lit[k] * (0.68 + (0.32 * r) / 7) + boost((r + 0.5) / 8), k);
      const wq = Math.abs(quad[1][0] - quad[0][0]), hq = Math.abs(quad[3][1] - quad[0][1]);
      if (wq > 26 && hq > 13) {
        ctx.fillStyle = tok.bg; ctx.globalAlpha = 0.9; ctx.font = '600 11px ' + FONT; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        ctx.fillText('R' + c.rung, (quad[0][0] + quad[1][0] + quad[2][0] + quad[3][0]) / 4, (quad[0][1] + quad[1][1] + quad[2][1] + quad[3][1]) / 4); ctx.globalAlpha = 1;
      }
    });
    // lid
    const lid = CS[CS.length - 1];
    for (let k = 0; k < 3; k++) if (vis[k]) {
      const k2 = (k + 1) % 3;
      paint(lid, inset([lidLo[k], lidLo[k2], apex], 0.86), -1, lit[k] + boost(1.02), 9);
    }
    // labels: rung numbers on the left axis, faces below the base
    ctx.fillStyle = tok.faint; ctx.font = '11px ' + FONT; ctx.textAlign = 'left'; ctx.textBaseline = 'alphabetic';
    d.rungs.forEach((r, i) => {
      const a = Pj(R([0, (HH * BODY * (i + 0.5)) / 8, 0]));
      ctx.fillStyle = S.sel && !byId[S.sel].lid && byId[S.sel].rung === r.n ? tok.ink : tok.faint;
      ctx.fillText(narrow ? 'R' + r.n : `R${r.n}  ${r.name.split(' (')[0]}`, 10, a[1] + 4);
    });
    const bc = [(scr[0][0][0] + scr[0][1][0] + scr[0][2][0]) / 3, (scr[0][0][1] + scr[0][1][1] + scr[0][2][1]) / 3];
    // face names: beside the edge for faces turned toward you, and always listed top right (with "behind" for the far side)
    S.rects = [];
    ctx.textAlign = 'center';
    for (let k = 0; k < 3; k++) if (vis[k]) {
      const k2 = (k + 1) % 3, ma = [(scr[0][k][0] + scr[0][k2][0]) / 2, (scr[0][k][1] + scr[0][k2][1]) / 2];
      const dx = ma[0] - bc[0], dy = ma[1] - bc[1], dl = Math.hypot(dx, dy) || 1;
      const tx = clamp(ma[0] + (dx / dl) * 40, 50, w - 50), ty = clamp(ma[1] + (dy / dl) * 28 + 8, 16, h - 12), tw = ctx.measureText(FACES[k]).width;
      ctx.fillStyle = faceCol[k]; ctx.fillText(FACES[k], tx, ty);
      S.rects.push({ id: 'edge:' + FACES[k], x0: tx - tw / 2, x1: tx + tw / 2, y0: ty - 11, y1: ty + 3 });
    }
    ctx.textAlign = 'right';
    FACES.forEach((f, k) => {
      const label = vis[k] ? f : f + ' (behind)', y = 18 + k * 17, tw = ctx.measureText(label).width;
      ctx.fillStyle = faceCol[k]; ctx.globalAlpha = vis[k] ? 1 : 0.7;
      ctx.beginPath(); ctx.arc(w - 12, y - 4, 4, 0, 6.2832); ctx.fill();
      ctx.fillText(label, w - 22, y); ctx.globalAlpha = 1;
      S.rects.push({ id: 'key:' + f, x0: w - 22 - tw, x1: w - 8, y0: y - 12, y1: y + 3 });
    });
    d.rungs.forEach((r, i) => {
      const a = Pj(R([0, (HH * BODY * (i + 0.5)) / 8, 0])), label = narrow ? 'R' + r.n : `R${r.n}  ${r.name.split(' (')[0]}`;
      ctx.textAlign = 'left'; S.rects.push({ id: 'rung:' + r.n, x0: 10, x1: 10 + ctx.measureText(label).width, y0: a[1] - 7, y1: a[1] + 7 });
    });    return anim() || !!S.tween;
  }

  const lp = loop((time, dt) => {
    if (S.tween) {
      const t = S.tween, k = Math.min(1, dt * 6 + (reduced ? 1 : 0));
      S.yaw += (t.yaw - S.yaw) * k; S.tilt += (t.tilt - S.tilt) * k;
      if (Math.abs(t.yaw - S.yaw) < 0.2 && Math.abs(t.tilt - S.tilt) < 0.2) { S.yaw = t.yaw; S.tilt = t.tilt; S.tween = null; }
      ui.yaw.value = S.yaw; ui.tilt.value = S.tilt;
    }
    if (anim() && !S.drag && !S.sel) S.spin += dt * 10;
    return draw(time);
  });
  const changed = () => { lp.redraw(); lp.kick(); };
  const wrap = (v) => ((v + 540) % 360) - 180;

  function panel(c) {
    const p = ui.panel; p.textContent = '';
    const el = (tag, cls, txt) => { const e = document.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; };
    if (!c) {
      p.append(el('h3', null, 'Pick a cell'), el('p', 'muted', `Select a cell on the pyramid, or focus it and use the arrow keys. The panel shows the cell's name, what it does, its API, its status and the evidence. ${m.rungs} rungs by ${FACES.length} faces is ${m.total - 1} cells, plus the lid.`));
      return;
    }
    p.append(el('p', 'eyebrow', c.lid ? 'Lid' : `Rung ${c.rung} · ${c.rungName}`));
    p.append(el('h3', null, c.name));
    const chips = el('div', 'map-chips');
    chips.append(el('span', 'pill ' + (c.lid ? '' : 'f' + c.fi), c.lid ? 'Lid' : c.face), el('span', 'pill st-' + (c.status === 'COVERED' ? 'ok' : c.status === 'READ-TIME' ? 'warn' : 'other'), c.status));
    p.append(chips);
    const dl = el('dl', 'map-dl');
    [[c.lid ? 'About the lid' : 'Rung', c.rungCaption], ['What it does', c.description], ['API', c.api], ['Status', GLOSS[c.status] || `Status as listed: ${c.status}.`], ['Evidence', c.evidence]].forEach(([k, v]) => {
      dl.append(el('dt', null, k)); dl.append(k === 'API' ? (() => { const dd = el('dd'); dd.append(el('code', null, v)); return dd; })() : el('dd', null, v));
    });
    p.append(dl);
    const nav = el('div', 'map-nav');
    const prev = el('button', 'btn btn-ghost btn-sm', 'Previous cell'), next = el('button', 'btn btn-ghost btn-sm', 'Next cell');
    prev.type = next.type = 'button'; prev.onclick = () => step(-1); next.onclick = () => step(1);
    nav.append(prev, next); p.append(nav);
  }
  function select(c, rotate) {
    S.sel = c ? c.id : null; panel(c);
    if (c && rotate && !c.lid) { S.tween = { yaw: wrap(((-SIDEMID[c.fi] * 180) / Math.PI)), tilt: clamp(S.tilt, 12, 40) }; const d0 = ((S.tween.yaw - S.yaw + 540) % 360) - 180; S.tween.yaw = S.yaw + d0; }
    changed();
  }
  const step = (dlt) => { const i = S.sel ? CS.findIndex((c) => c.id === S.sel) : dlt > 0 ? -1 : 0; select(CS[(i + dlt + CS.length) % CS.length], true); };
  function move(dx, dy) {
    const c = S.sel ? byId[S.sel] : null;
    if (!c) return select(CS[0], true);
    if (c.lid) { if (dy < 0) select(byId['R8_' + FACES[0]], true); return; }
    let f = c.fi, r = c.rung;
    if (dx) return select(byId[`R${r}_${FACES[(f + dx + 3) % 3]}`], true);
    if (dy > 0) return select(r === 8 ? CS[CS.length - 1] : byId[`R${r + 1}_${FACES[f]}`], true);
    if (dy < 0 && r > 1) select(byId[`R${r - 1}_${FACES[f]}`], true);
  }
  panel(null);

  ui.yaw.addEventListener('input', () => { S.yaw = Number(ui.yaw.value); S.spin = 0; S.tween = null; changed(); });
  ui.tilt.addEventListener('input', () => { S.tilt = Number(ui.tilt.value); S.tween = null; changed(); });
  colorBtns.forEach((b) => b.addEventListener('click', () => { S.mode = b.dataset.color; showLegend(S.mode); colorBtns.forEach((x) => x.setAttribute('aria-pressed', String(x === b))); changed(); }));
  ui.play.addEventListener('click', () => { S.playing = !S.playing; if (S.playing) S.userPlay = true; ui.play.textContent = S.playing ? 'Pause motion' : 'Play motion'; ui.play.setAttribute('aria-pressed', String(S.playing)); changed(); });
  ui.reset.addEventListener('click', () => { S.sel = null; S.spin = 0; S.tween = { yaw: HOME.yaw, tilt: HOME.tilt }; const d0 = ((HOME.yaw - S.yaw + 540) % 360) - 180; S.tween.yaw = S.yaw + d0; panel(null); changed(); });

  canvas.addEventListener('pointerdown', (e) => { S.drag = { x: e.clientX, y: e.clientY, px: e.clientX, py: e.clientY, moved: false, type: e.pointerType }; canvas.setPointerCapture(e.pointerId); stage.focus({ preventScroll: true }); });
  canvas.addEventListener('pointermove', (e) => {
    const dr = S.drag; if (!dr) return;
    if (!dr.moved && Math.abs(e.clientX - dr.px) + Math.abs(e.clientY - dr.py) < 5) return;
    dr.moved = true; S.tween = null;
    S.yaw = wrap(S.yaw + S.spin + (e.clientX - dr.x) * 0.5); S.spin = 0;
    if (dr.type === 'mouse') S.tilt = clamp(S.tilt - (e.clientY - dr.y) * 0.3, -8, 58);
    dr.x = e.clientX; dr.y = e.clientY; ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed();
  });
  const up = (e) => {
    const dr = S.drag; S.drag = null; if (!dr || dr.moved) return;
    const r = canvas.getBoundingClientRect(), x = e.clientX - r.left, y = e.clientY - r.top;
    const hit = polys.filter((p) => inPoly(x, y, p.pts)).sort((a, b) => b.z - a.z)[0];
    select(hit ? byId[hit.id] : null, false);
  };
  canvas.addEventListener('pointerup', up); canvas.addEventListener('pointercancel', () => { S.drag = null; });
  stage.addEventListener('keydown', (e) => {
    const k = e.key; let used = true;
    if (k.startsWith('Arrow')) {
      if (e.shiftKey) {
        S.tween = null; S.spin = 0;
        if (k === 'ArrowLeft') S.yaw = wrap(S.yaw - 10); else if (k === 'ArrowRight') S.yaw = wrap(S.yaw + 10);
        else if (k === 'ArrowUp') S.tilt = clamp(S.tilt + 5, -8, 58); else S.tilt = clamp(S.tilt - 5, -8, 58);
        ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed();
      } else if (k === 'ArrowLeft') move(-1, 0); else if (k === 'ArrowRight') move(1, 0); else if (k === 'ArrowUp') move(0, 1); else move(0, -1);
    } else if (k === 'Home') select(CS[0], true);
    else if (k === 'End') select(CS[CS.length - 1], true);
    else if (k === ' ') ui.play.click();
    else if (k === 'Escape') select(null);
    else used = false;
    if (used) e.preventDefault();
  });
  new ResizeObserver(() => lp.redraw()).observe(stage);
  new IntersectionObserver((es) => lp.setVisible(es[0].isIntersecting)).observe(root);
  document.addEventListener('visibilitychange', () => lp.kick());
  lp.redraw(); lp.kick();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="pleroma-map"]').forEach((root) => {
    const blob = root.querySelector('script[type="application/json"]');
    if (blob) mount(root, JSON.parse(blob.textContent));
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
