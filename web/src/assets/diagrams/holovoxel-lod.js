// HoloVoxel signature diagram: "Detail is a property of the read". A strip of terrain chunks, each stored once as a single
// hologram, is decoded at three budgets according to its distance from the viewer: crisp near, blocky and soft far.
// Data in: holovoxel-lod.json, written by web/generators/holovoxel-lod by running the PUBLISHED package: the decoded colour
// field of every chunk at each level, and how accuracy, stored size and decode cost fall with the component budget.
import { hasDom, prefersReducedMotion, readTokens, rgba, makeCamera, fitCanvas, loop } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');
const f1 = (n) => Number(n).toFixed(1);
const ZS = 0.55;                      // vertical exaggeration of the terrain
const LEVEL_NAMES = ['Near', 'Middle', 'Far'];

// ---------------------------------------------------------------- pure data helpers
const hexRgb = (h) => [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];

export function decode(d, chunk, level) {
  const L = d.chunks[chunk].levels[level], n = L.n, hb = d.world.heightBase, cells = [];
  for (let i = 0; i < n * n; i++) {
    const ci = ((L.colours.charCodeAt(2 * i) - 48) << 6) | (L.colours.charCodeAt(2 * i + 1) - 48);
    cells.push({ lx: (i % n) * L.step, lz: Math.floor(i / n) * L.step, size: L.step, y: L.heights.charCodeAt(i) - hb, ci });
  }
  return cells;
}

export function levelAt(d, units) {
  for (const b of d.bands) if (b.maxUnits == null || units <= b.maxUnits) return b.level;
  return d.bands[d.bands.length - 1].level;
}

export function metrics(d) {
  const ladder = Object.fromEntries(d.ladder.map((r) => [r.components, r]));
  const levels = d.levels.map((lv, i) => ({ ...lv, i, cells: (d.world.chunkSize / lv.step) ** 2, ladder: ladder[lv.components], name: LEVEL_NAMES[i] }));
  const size = d.world.chunkSize, chunks = d.chunks.length, fullCells = levels[0].cells * chunks;
  const counts = (v) => {
    const c = levels.map(() => []);
    for (let k = 0; k < chunks; k++) c[levelAt(d, Math.abs(k - v) * size)].push(k);
    return c;
  };
  return { levels, counts, chunks, size, fullCells, ladder: d.ladder, full: levels[0], far: levels[levels.length - 1], defaultViewer: 2 };
}

// Numbers the product page quotes in prose, taken from the data so the sentence cannot go stale.
export function limitFacts(m) {
  return {
    nearComps: n0(m.full.components), midComps: n0(m.levels[1].components), farComps: n0(m.far.components),
    nearAcc: f1(m.full.ladder.accuracy), midAcc: f1(m.levels[1].ladder.accuracy), farAcc: f1(m.far.ladder.accuracy),
    nearBytes: n0(m.full.ladder.storedBytes), farBytes: n0(m.far.ladder.storedBytes),
    nearMs: f1(m.full.ladder.decodeMs), farMs: m.far.ladder.decodeMs.toFixed(2),
    nearCells: n0(m.full.cells), midCells: n0(m.levels[1].cells), farCells: n0(m.far.cells),
    smallest: n0(m.ladder[0].components), smallestAcc: f1(m.ladder[0].accuracy), chunks: String(m.chunks),
  };
}

export function readoutFor(d, m, v) {
  const c = m.counts(v), where = (list) => (list.length ? `chunk${list.length > 1 ? 's' : ''} ${list[0]}${list.length > 1 ? ` to ${list[list.length - 1]}` : ''}` : null);
  const parts = m.levels.map((lv, i) => {
    const w = where(c[i]);
    return w ? `${w} read at ${n0(lv.components)} components (${lv.cells} cells each, ${f1(lv.ladder.accuracy)} percent accurate)` : null;
  }).filter(Boolean);
  const cells = c.reduce((a, list, i) => a + list.length * m.levels[i].cells, 0);
  return `Viewer at chunk ${Number(v).toFixed(1)}: ${parts.join('; ')}. ${n0(cells)} cells are decoded instead of ${n0(m.fullCells)}.`;
}

export function statsFor(d, m, v) {
  const c = m.counts(v), t = (label, value) => ({ label, value: String(value) });
  const cells = c.reduce((a, list, i) => a + list.length * m.levels[i].cells, 0);
  const bytes = c.reduce((a, list, i) => a + list.length * m.levels[i].ladder.storedBytes, 0);
  return [t('Cells decoded', `${n0(cells)} of ${n0(m.fullCells)}`), t('Hologram bytes read', `${n0(bytes)} of ${n0(m.chunks * m.full.ladder.storedBytes)}`), t('Chunks at full detail', c[0].length), t('Chunks at the coarsest', c[2].length)];
}

// ---------------------------------------------------------------- server-rendered pieces
function ladderSvg(d, m, W = 640, H = 210) {
  const L = 44, R = 12, T = 22, B = 44, pw = W - L - R, ph = H - T - B, n = m.ladder.length, gap = 8, bw = (pw - gap * (n - 1)) / n;
  const used = Object.fromEntries(m.levels.map((lv) => [lv.components, lv.i]));
  const grid = [0, 25, 50, 75, 100].map((v) => `<line class="gl" x1="${L}" y1="${(T + ph - (v / 100) * ph).toFixed(1)}" x2="${L + pw}" y2="${(T + ph - (v / 100) * ph).toFixed(1)}"/><text class="tk" x="${L - 6}" y="${(T + ph - (v / 100) * ph + 3).toFixed(1)}" text-anchor="end">${v}</text>`).join('');
  const bars = m.ladder.map((r, k) => {
    const x = L + k * (bw + gap), h = (r.accuracy / 100) * ph, lv = used[r.components];
    return `<g><rect class="bar${lv != null ? ' l' + lv : ''}" x="${x.toFixed(1)}" y="${(T + ph - h).toFixed(1)}" width="${bw.toFixed(1)}" height="${h.toFixed(1)}" rx="3"/><text class="tk${lv != null ? ' on' : ''}" x="${(x + bw / 2).toFixed(1)}" y="${H - B + 14}" text-anchor="middle">${n0(r.components)}</text>${lv != null ? `<text class="val" x="${(x + bw / 2).toFixed(1)}" y="${(T + ph - h - 5).toFixed(1)}" text-anchor="middle">${f1(r.accuracy)}</text>` : ''}</g>`;
  }).join('');
  return `<svg class="hv-svg" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="hv-t hv-d" preserveAspectRatio="xMidYMid meet"><title id="hv-t">Accuracy against component budget</title><desc id="hv-d">One chunk stored as a single hologram is decoded at ${m.ladder.length} component budgets from ${n0(m.ladder[0].components)} to ${n0(m.ladder[n - 1].components)}. Accuracy rises from ${f1(m.ladder[0].accuracy)} to ${f1(m.ladder[n - 1].accuracy)} percent. The tables below list every budget with its stored size and decode time.</desc>${grid}${bars}<text class="ax" x="${L + pw / 2}" y="${H - 4}" text-anchor="middle">components read from the hologram</text><text class="ax" transform="translate(11 ${T + ph / 2}) rotate(-90)" text-anchor="middle">percent of cells correct</text></svg>`;
}

export function staticSvg(d) { return ladderSvg(d, metrics(d)); }

export function controlsHtml(id, m) {
  const stats = statsFor(null, m, m.defaultViewer).map((s) => `<div class="stat"><dt>${esc(s.label)}</dt><dd>${esc(s.value)}</dd></div>`).join('');
  return {
    tabs: '',
    controls: `<div class="dia-controls dia-ui" hidden>
<label class="dia-field">Viewer <input type="range" min="0" max="${m.chunks - 1}" step="0.1" value="${m.defaultViewer}" data-ctl="viewer" aria-describedby="${id}-read"></label>
<label class="dia-field">Turn <input type="range" min="-180" max="180" step="1" value="-28" data-ctl="yaw"></label>
<label class="dia-field">Tilt <input type="range" min="5" max="75" step="1" value="40" data-ctl="tilt"></label>
<label class="dia-check"><input type="checkbox" data-ctl="bands" checked> Show detail bands</label>
<button type="button" class="btn btn-ghost btn-sm" data-ctl="walk" aria-pressed="false">Walk the viewer</button>
</div>
<p class="dia-hint dia-ui" hidden>Move the viewer along the strip and watch the detail follow it. Drag the scene or use the arrow keys while it is focused to turn and tilt it.</p>
<p class="dia-readout dia-ui" id="${id}-read" data-ctl="readout" role="status" hidden>${esc(readoutFor(null, m, m.defaultViewer))}</p>
<dl class="stats dia-stats dia-ui" data-ctl="stats" hidden>${stats}</dl>`,
  };
}

export function tablesHtml(d, m) {
  const row = (cells, head) => '<tr>' + cells.map((c, k) => (k === 0 && head ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
  const lv = m.levels.map((l) => row([`${l.name}, level ${l.i + 1}`, `${l.step}`, n0(l.components), String(l.cells), f1(l.ladder.accuracy), n0(l.ladder.storedBytes), l.ladder.decodeMs.toFixed(2)], true)).join('');
  const lad = m.ladder.map((r) => row([n0(r.components), f1(r.accuracy), n0(r.storedBytes), r.decodeMs.toFixed(2)], true)).join('');
  const dist = d.distances.map(([k, units, comps, level]) => row([String(k), n0(units), n0(comps), `${m.levels[level].name}, level ${level + 1}`], true)).join('');
  return `<details class="dia-data"><summary>Data behind the diagram</summary>
<div class="tbl-wrap"><table class="tbl"><caption>The three levels of detail the scene uses, for one 16 by 16 chunk</caption>
<thead><tr><th scope="col">Level</th><th scope="col">Cell size</th><th scope="col">Components read</th><th scope="col">Cells decoded</th><th scope="col">Correct (percent)</th><th scope="col">Hologram size (bytes)</th><th scope="col">Decode time (ms)</th></tr></thead><tbody>${lv}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Accuracy, stored size and decode time of one chunk against the component budget</caption>
<thead><tr><th scope="col">Components read</th><th scope="col">Correct (percent)</th><th scope="col">Hologram size (bytes)</th><th scope="col">Decode time (ms)</th></tr></thead><tbody>${lad}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>What Lod.Components returns for a chunk at each distance, and the band the scene puts it in</caption>
<thead><tr><th scope="col">Chunks from the viewer</th><th scope="col">World units</th><th scope="col">Components suggested</th><th scope="col">Band</th></tr></thead><tbody>${dist}</tbody></table></div>
</details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  for (let c = 0; c < d.chunks.length; c++) for (let l = 0; l < d.levels.length; l++) {
    const cells = decode(d, c, l);
    if (cells.length !== m.levels[l].cells) out.push(`chunk ${c} level ${l}: ${cells.length} cells, expected ${m.levels[l].cells}`);
    if (cells.some((q) => q.ci >= d.palette.length)) out.push(`chunk ${c} level ${l}: colour index outside the palette`);
  }
  const acc = m.ladder.map((r) => r.accuracy);
  if (!(acc[acc.length - 1] > acc[0] + 40)) out.push('accuracy does not rise with the component budget');
  for (let k = 1; k < m.ladder.length; k++) if (!(m.ladder[k].storedBytes > m.ladder[k - 1].storedBytes)) out.push('stored size does not grow with the budget');
  if (m.levels.some((l) => !l.ladder)) out.push('a level has no matching ladder row');
  for (let i = 1; i < m.levels.length; i++) if (!(m.levels[i].components < m.levels[i - 1].components)) out.push('levels are not ordered from detailed to coarse');
  if (!/Hologram size/.test(body)) out.push('level table missing');
  return out;
}

// ---------------------------------------------------------------- interactive scene (browser only)
export function mount(root, d) {
  const m = metrics(d), reduced = prefersReducedMotion(), tok = readTokens(root);
  const cs = getComputedStyle(root), FONT = cs.fontFamily, MONO = getComputedStyle(document.body).getPropertyValue('--mono').trim() || 'monospace';
  const stage = root.querySelector('.dia-stage');
  stage.textContent = '';
  const canvas = document.createElement('canvas'); canvas.className = 'dia-canvas'; canvas.setAttribute('aria-hidden', 'true'); stage.append(canvas);
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const c2 = canvas.getContext('2d');
  const q = (s) => root.querySelector(s);
  const ui = { viewer: q('[data-ctl=viewer]'), yaw: q('[data-ctl=yaw]'), tilt: q('[data-ctl=tilt]'), bands: q('[data-ctl=bands]'), walk: q('[data-ctl=walk]'), readout: q('[data-ctl=readout]'), stats: q('[data-ctl=stats]') };
  const narrow = stage.getBoundingClientRect().width < 560;
  const S = { v: m.defaultViewer, yaw: narrow ? 72 : -28, tilt: narrow ? 46 : 40, bands: true, walking: false, dir: 1, moved: false, last: '' };
  ui.yaw.value = S.yaw; ui.tilt.value = S.tilt;
  const size = m.size, sea = d.world.seaLevel;

  // colours per palette entry: top and the two shaded sides
  const shade = (rgb, k) => `rgb(${Math.round(rgb[0] * k)},${Math.round(rgb[1] * k)},${Math.round(rgb[2] * k)})`;
  const pal = d.palette.map((h) => { const c = hexRgb(h); return { top: shade(c, 1), a: shade(c, 0.66), b: shade(c, 0.46) }; });
  const cells = d.chunks.map((_, c) => d.levels.map((__, l) => decode(d, c, l)));
  const bandCol = [tok.ok, tok.warn, tok.bad];

  function draw(time, dt) {
    const { w, h, dpr } = fitCanvas(canvas);
    c2.setTransform(dpr, 0, 0, dpr, 0, 0); c2.clearRect(0, 0, w, h);
    if (S.walking && dt) {
      S.v += S.dir * dt * 1.5;
      if (S.v >= m.chunks - 1) { S.v = m.chunks - 1; S.dir = -1; } else if (S.v <= 0) { S.v = 0; S.dir = 1; }
      ui.viewer.value = S.v; refresh();
    }
    const split = h * (w < 560 ? 0.7 : 0.74);
    c2.save(); c2.beginPath(); c2.rect(0, 0, w, split); c2.clip(); scene(0, 0, w, split, time); c2.restore();
    ladder(0, split + 4, w, h - split - 4);
    return S.walking;
  }

  function scene(x, y, w, h, time) {
    const lenX = m.chunks * size, topZ = 70 * ZS;
    const sway = !reduced && S.walking && !S.moved ? 3 * Math.sin(time * 0.35) : 0;
    const cam = makeCamera(lenX / 2, size / 2, S.yaw + sway, S.tilt);
    const corners = []; for (const px of [0, lenX]) for (const py of [-6, size]) for (const pz of [0, topZ]) corners.push(cam.p(px, py, pz));
    const u0 = Math.min(...corners.map((c) => c[0])), u1 = Math.max(...corners.map((c) => c[0])), v0 = Math.min(...corners.map((c) => c[1])), v1 = Math.max(...corners.map((c) => c[1]));
    const pad = 12, s = Math.min((w - pad * 2) / (u1 - u0), (h - pad * 2) / (v1 - v0));
    if (!(s > 0.3)) return;
    const ox = x + w / 2 - ((u0 + u1) / 2) * s, oy = y + h / 2 - ((v0 + v1) / 2) * s;
    const P = (px, py, pz = 0) => { const p = cam.p(px, py, pz); return [ox + p[0] * s, oy + p[1] * s, p[2]]; };
    const poly = (pts, fill) => { c2.beginPath(); c2.moveTo(pts[0][0], pts[0][1]); for (let k = 1; k < pts.length; k++) c2.lineTo(pts[k][0], pts[k][1]); c2.closePath(); c2.fillStyle = fill; c2.fill(); };

    // floor
    poly([P(0, 0), P(lenX, 0), P(lenX, size), P(0, size)], tok.surface2);
    // collect visible cells at the level each chunk is read
    const items = [];
    // each chunk uses its continuous distance, so a level changes exactly where a band edge is crossed
    const lvOf = Array.from({ length: m.chunks }, (_, k) => levelAt(d, Math.abs(k - S.v) * size));
    for (let k = 0; k < m.chunks; k++) {
      for (const c of cells[k][lvOf[k]]) {
        const x0 = k * size + c.lx, y0 = c.lz, top = Math.max(c.y, sea) * ZS;
        items.push({ x0, y0, sz: c.size, top, ci: c.ci, depth: cam.p(x0 + c.size / 2, y0 + c.size / 2, top / 2)[2] });
      }
    }
    items.sort((a, b) => a.depth - b.depth);
    const sides = [[0, 1, 0, 1], [0, -1, 0, 0], [1, 0, 1, 0], [-1, 0, 0, 0]];
    for (const it of items) {
      const col = pal[it.ci], x1 = it.x0 + it.sz, y1 = it.y0 + it.sz;
      for (const [nx, ny] of sides) {
        const dn = cam.dir(nx, ny); if (dn[1] <= 0.001 || S.tilt < 1) continue;
        let A, B;
        if (nx === 0 && ny === 1) { A = [it.x0, y1]; B = [x1, y1]; } else if (nx === 0) { A = [it.x0, it.y0]; B = [x1, it.y0]; } else if (nx === 1) { A = [x1, it.y0]; B = [x1, y1]; } else { A = [it.x0, it.y0]; B = [it.x0, y1]; }
        poly([P(A[0], A[1], 0), P(B[0], B[1], 0), P(B[0], B[1], it.top), P(A[0], A[1], it.top)], dn[0] > 0 ? col.a : col.b);
      }
      poly([P(it.x0, it.y0, it.top), P(x1, it.y0, it.top), P(x1, y1, it.top), P(it.x0, y1, it.top)], col.top);
    }
    // detail bands: a coloured frame and a label for each chunk
    if (S.bands) {
      c2.font = `600 11px ${FONT}`; c2.textAlign = 'center';
      for (let k = 0; k < m.chunks; k++) {
        const l = lvOf[k], a = P(k * size, 0, 0), b = P(k * size + size, 0, 0), e = P(k * size + size, size, 0), g = P(k * size, size, 0);
        c2.strokeStyle = bandCol[l]; c2.lineWidth = 2; c2.beginPath(); c2.moveTo(a[0], a[1]); c2.lineTo(b[0], b[1]); c2.lineTo(e[0], e[1]); c2.lineTo(g[0], g[1]); c2.closePath(); c2.stroke();
      }
      const labels = [];
      for (let k = 0; k < m.chunks; k++) { const p = P(k * size + size / 2, size / 2, 62 * ZS); labels.push([k, p]); }
      c2.font = `10px ${MONO}`;
      for (const [k, p] of labels) { const l = lvOf[k]; c2.fillStyle = bandCol[l]; c2.fillText(n0(m.levels[l].components), p[0], p[1]); }
    }
    // the viewer
    const vx = S.v * size + size / 2, base = P(vx, -3, 0), head = P(vx, -3, 78 * ZS);
    c2.strokeStyle = tok.ink; c2.lineWidth = 2; c2.beginPath(); c2.moveTo(base[0], base[1]); c2.lineTo(head[0], head[1]); c2.stroke();
    c2.fillStyle = tok.ink; c2.beginPath(); c2.arc(head[0], head[1], 5, 0, 6.283); c2.fill();
    c2.font = `600 11px ${FONT}`; c2.textAlign = 'left'; c2.fillText('Viewer', head[0] + 9, head[1] + 4);
  }

  function ladder(x, y, w, h) {
    const L = 40, R = 10, T = 14, B = 32, n = m.ladder.length, gap = 6, pw = w - L - R, ph = h - T - B, bw = (pw - gap * (n - 1)) / n;
    const used = Object.fromEntries(m.levels.map((lv) => [lv.components, lv.i]));
    c2.font = `10px ${MONO}`; c2.textAlign = 'right'; c2.fillStyle = tok.faint; c2.strokeStyle = rgba(tok.soft, 0.22); c2.lineWidth = 1;
    for (const v of [0, 50, 100]) { const yy = y + T + ph - (v / 100) * ph; c2.beginPath(); c2.moveTo(x + L, yy); c2.lineTo(x + L + pw, yy); c2.stroke(); c2.fillText(String(v), x + L - 5, yy + 3); }
    m.ladder.forEach((r, k) => {
      const bx = x + L + k * (bw + gap), bh = (r.accuracy / 100) * ph, lv = used[r.components];
      c2.fillStyle = lv != null ? bandCol[lv] : rgba(tok.soft, 0.3); c2.globalAlpha = lv != null ? 0.9 : 1;
      c2.beginPath(); c2.roundRect ? c2.roundRect(bx, y + T + ph - bh, bw, bh, 3) : c2.rect(bx, y + T + ph - bh, bw, bh); c2.fill(); c2.globalAlpha = 1;
      c2.textAlign = 'center'; c2.fillStyle = lv != null ? tok.ink : tok.faint; c2.font = `10px ${MONO}`;
      if (bw >= 38 || lv != null) c2.fillText(n0(r.components), bx + bw / 2, y + h - 15);
      if (lv != null) c2.fillText(f1(r.accuracy), bx + bw / 2, y + T + ph - bh - 4);
    });
    c2.textAlign = 'left'; c2.fillStyle = tok.ink; c2.font = `600 11px ${FONT}`; c2.fillText('Percent of cells correct, by components read', x + 4, y + 9);
    c2.textAlign = 'center'; c2.fillStyle = tok.faint; c2.font = `10px ${FONT}`; c2.fillText('components read from the hologram', x + L + pw / 2, y + h - 2);
  }

  const lp = loop(draw);
  function refresh() {
    const text = readoutFor(d, m, S.v);
    if (text !== S.last) {
      S.last = text; ui.readout.textContent = text;
      ui.stats.innerHTML = statsFor(d, m, S.v).map((x) => `<div class="stat"><dt>${esc(x.label)}</dt><dd>${esc(x.value)}</dd></div>`).join('');
    }
  }
  function changed() { refresh(); lp.redraw(); lp.kick(); }
  ui.viewer.addEventListener('input', () => { S.v = Number(ui.viewer.value); S.walking = false; ui.walk.setAttribute('aria-pressed', 'false'); ui.walk.textContent = 'Walk the viewer'; changed(); });
  ui.yaw.addEventListener('input', () => { S.yaw = Number(ui.yaw.value); S.moved = true; changed(); });
  ui.tilt.addEventListener('input', () => { S.tilt = Number(ui.tilt.value); S.moved = true; changed(); });
  ui.bands.addEventListener('change', () => { S.bands = ui.bands.checked; changed(); });
  ui.walk.addEventListener('click', () => { S.walking = !S.walking; ui.walk.setAttribute('aria-pressed', String(S.walking)); ui.walk.textContent = S.walking ? 'Stop walking' : 'Walk the viewer'; changed(); });

  let drag = null;
  canvas.addEventListener('pointerdown', (e) => { drag = { x: e.clientX, y: e.clientY, type: e.pointerType }; canvas.setPointerCapture(e.pointerId); });
  canvas.addEventListener('pointermove', (e) => {
    if (!drag) return;
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y; drag.x = e.clientX; drag.y = e.clientY;
    S.yaw = ((S.yaw + dx * 0.4 + 540) % 360) - 180; S.moved = true;
    if (drag.type === 'mouse') S.tilt = Math.max(5, Math.min(75, S.tilt - dy * 0.3));
    ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed();
  });
  const end = () => { drag = null; };
  canvas.addEventListener('pointerup', end); canvas.addEventListener('pointercancel', end);
  stage.addEventListener('keydown', (e) => {
    const k = e.key; let used = true;
    if (k === 'ArrowLeft') S.yaw = ((S.yaw - 5 + 540) % 360) - 180;
    else if (k === 'ArrowRight') S.yaw = ((S.yaw + 5 + 540) % 360) - 180;
    else if (k === 'ArrowUp') S.tilt = Math.min(75, S.tilt + 3);
    else if (k === 'ArrowDown') S.tilt = Math.max(5, S.tilt - 3);
    else used = false;
    if (used) { e.preventDefault(); S.moved = true; ui.yaw.value = S.yaw; ui.tilt.value = S.tilt; changed(); }
  });
  new ResizeObserver(() => lp.redraw()).observe(stage);
  new IntersectionObserver((es) => lp.setVisible(es[0].isIntersecting)).observe(root);
  document.addEventListener('visibilitychange', () => lp.kick());
  refresh(); lp.redraw();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="holovoxel-lod"]').forEach((root) => {
    const blob = root.querySelector('script[type="application/json"]');
    if (blob) mount(root, JSON.parse(blob.textContent));
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
