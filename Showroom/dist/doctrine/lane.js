// Lane battle canvas renderer (the Doctrine page, /tools/doctrine/). It draws what the .NET match hands it and nothing else: every frame the page's Frame() call returns one packet
// (bodies already placed BETWEEN the last two ticks by the service), this file draws the ground, the bases, one picture per body and the effects. It never decides anything about the
// battle. Packet: 8 header floats (tick, stage 0 pause 1 run 2 over, status, your base 0..1, their base 0..1, body count, alpha, spare) then 10 floats a body (id, side, lane, kind,
// x, y, facing x, facing y, hit points 0..1, fighting 0/1). Pictures: terrain and buildings are Kenney CC0 (see NOTICE.txt); the eight soldiers are drawn here with canvas paths.

// The team colours: side 0 is always YOU (blue), side 1 THEM (red), from each player's own view. These are the defaults; the page sends the same two in the layout (LaneTeams) and they win.
// A soldier shows its team three ways: a coloured outline baked into the picture, a coloured disc on the ground under its feet, and the coloured accent of its clothes.
const SIDE = [
  { main: '#3f8cff', dark: '#1d4fa8', light: '#a9ccff' },   // you
  { main: '#e5533d', dark: '#9a2a1c', light: '#ffb7aa' },   // them
];
const OUTLINE = 3;   // px of the team outline, in the 96 px sprite
const ORDER_NAME = ['Advance', 'Hold', 'Flank', 'Fall back', 'Guard', 'Hunt'];   // FieldOrderKind order
const KINDS = 8, SPR = 96;   // sprite canvases are 96 px, drawn in a 64 unit box (scale 1.5)
const INK = '#14110f', SKIN = '#f1c9a0';

let S = null;

// ---- sound: 8-bit effects made in the browser with Web Audio (no files, nothing to download). The audio engine starts only after the first tap or key press, is quiet by default,
// plays at most MAX_PER_TICK sounds per game tick (the most important few) and never the same sound twice inside its gap. The mute choice is kept in localStorage when there is one.

const MAX_PER_TICK = 2, MAX_PER_SECOND = 14, MASTER = 0.1, MUTE_KEY = 'doctrine-sound';
// w: square | pulse | triangle | noise. f: [from, to] in Hz (noise: pitch of the hiss). d: seconds. g: loudness 0..1. at: start delay. pri: which one wins when there are too many. gap: ms before the same sound may repeat.
const SFX = {
  tap:        { pri: 3, gap: 40,  v: [{ w: 'square', f: [900, 1300], d: 0.05, g: 0.5 }] },
  stage:      { pri: 4, gap: 40,  v: [{ w: 'pulse', f: [520, 520], d: 0.05, g: 0.5 }, { w: 'pulse', f: [780, 780], d: 0.07, g: 0.5, at: 0.05 }] },
  orderSet:   { pri: 4, gap: 40,  v: [{ w: 'square', f: [440, 440], d: 0.05, g: 0.45 }, { w: 'square', f: [660, 660], d: 0.07, g: 0.45, at: 0.05 }] },
  orderClear: { pri: 4, gap: 40,  v: [{ w: 'square', f: [660, 660], d: 0.05, g: 0.4 }, { w: 'square', f: [330, 330], d: 0.07, g: 0.4, at: 0.05 }] },
  tick:       { pri: 5, gap: 300, v: [{ w: 'square', f: [1000, 1000], d: 0.04, g: 0.35 }] },
  tickLast:   { pri: 6, gap: 300, v: [{ w: 'square', f: [1600, 1600], d: 0.09, g: 0.5 }, { w: 'square', f: [1600, 1600], d: 0.09, g: 0.5, at: 0.12 }] },
  runStart:   { pri: 9, gap: 500, v: [{ w: 'pulse', f: [330, 330], d: 0.08, g: 0.5 }, { w: 'pulse', f: [440, 440], d: 0.08, g: 0.5, at: 0.08 }, { w: 'pulse', f: [660, 660], d: 0.14, g: 0.5, at: 0.16 }] },
  hit:        { pri: 2, gap: 90,  v: [{ w: 'noise', f: [1400, 1400], d: 0.05, g: 0.45 }, { w: 'square', f: [220, 110], d: 0.05, g: 0.3 }] },
  arrow:      { pri: 2, gap: 120, v: [{ w: 'triangle', f: [1500, 500], d: 0.09, g: 0.4 }] },
  charge:     { pri: 3, gap: 250, v: [{ w: 'pulse', f: [180, 360], d: 0.16, g: 0.4 }, { w: 'noise', f: [600, 600], d: 0.12, g: 0.2 }] },
  thud:       { pri: 5, gap: 250, v: [{ w: 'triangle', f: [140, 45], d: 0.2, g: 0.7 }, { w: 'noise', f: [400, 400], d: 0.08, g: 0.3 }] },
  fall:       { pri: 3, gap: 140, v: [{ w: 'square', f: [440, 90], d: 0.16, g: 0.35 }] },
  castleHit:  { pri: 6, gap: 300, v: [{ w: 'noise', f: [500, 500], d: 0.14, g: 0.5 }, { w: 'triangle', f: [110, 60], d: 0.14, g: 0.5 }] },
  castleFall: { pri: 8, gap: 800, v: [{ w: 'noise', f: [350, 350], d: 0.5, g: 0.55 }, { w: 'triangle', f: [200, 30], d: 0.5, g: 0.6 }] },
  win:        { pri: 10, gap: 1500, v: [523, 659, 784, 1047].map((f, i) => ({ w: 'square', f: [f, f], d: i === 3 ? 0.3 : 0.11, g: 0.45, at: i * 0.12 })) },
  lose:       { pri: 10, gap: 1500, v: [392, 330, 262, 196].map((f, i) => ({ w: i === 3 ? 'triangle' : 'square', f: [f, f], d: i === 3 ? 0.35 : 0.13, g: 0.45, at: i * 0.15 })) },
};
const AUD = { ctx: null, master: null, muted: false, last: {}, tick: -1, count: 0, winStart: 0, winCount: 0, pulse: null, noise: null, unlockOn: false, played: 0 };

function loadMute() { try { AUD.muted = globalThis.localStorage && globalThis.localStorage.getItem(MUTE_KEY) === 'off'; } catch (e) { AUD.muted = false; } }
function saveMute() { try { globalThis.localStorage && globalThis.localStorage.setItem(MUTE_KEY, AUD.muted ? 'off' : 'on'); } catch (e) { /* no storage: the choice lasts for this visit */ } }
function setMuted(m) { AUD.muted = !!m; saveMute(); }

/** Starts the audio engine. Called only from a tap, a click or a key press (browsers refuse it otherwise). */
function unlock() {
  if (AUD.ctx) { try { if (AUD.ctx.state === 'suspended') AUD.ctx.resume(); } catch (e) { } return; }
  try {
    const AC = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!AC) return;
    const c = new AC();
    const m = c.createGain(); m.gain.value = MASTER; m.connect(c.destination);
    AUD.ctx = c; AUD.master = m;
    if (c.state === 'suspended') c.resume();
  } catch (e) { AUD.ctx = null; }
}
function onGesture() { unlock(); }
function listenForGesture() {
  if (AUD.unlockOn || typeof document === 'undefined' || !document.addEventListener) return;
  AUD.unlockOn = true;
  for (const n of ['click', 'touchend', 'keydown']) document.addEventListener(n, onGesture, true);
}
function stopListeningForGesture() {
  if (!AUD.unlockOn) return;
  AUD.unlockOn = false;
  for (const n of ['click', 'touchend', 'keydown']) document.removeEventListener(n, onGesture, true);
}

function voice(c, out, v, t0) {
  const st = t0 + (v.at || 0), en = st + v.d, g = c.createGain();
  g.gain.setValueAtTime(0.0001, st); g.gain.linearRampToValueAtTime(v.g, st + 0.004); g.gain.exponentialRampToValueAtTime(0.0001, en);
  let src;
  if (v.w === 'noise') {
    if (!AUD.noise) { const n = Math.floor(c.sampleRate * 0.25) || 8000, b = c.createBuffer(1, n, c.sampleRate || 32000), d = b.getChannelData(0); for (let i = 0; i < n; i++) d[i] = Math.random() * 2 - 1; AUD.noise = b; }
    src = c.createBufferSource(); src.buffer = AUD.noise; src.loop = true; if (src.playbackRate) src.playbackRate.value = v.f[0] / 1000;
  } else {
    src = c.createOscillator();
    if (v.w === 'pulse') {
      if (!AUD.pulse) { const re = new Float32Array(24), im = new Float32Array(24); for (let n = 1; n < 24; n++) re[n] = (2 / (n * Math.PI)) * Math.sin(n * Math.PI * 0.25); AUD.pulse = c.createPeriodicWave(re, im); }
      src.setPeriodicWave(AUD.pulse);
    } else src.type = v.w;
    src.frequency.setValueAtTime(v.f[0], st);
    if (v.f[1] !== v.f[0]) src.frequency.exponentialRampToValueAtTime(Math.max(20, v.f[1]), en);
  }
  src.connect(g); g.connect(out); src.start(st); src.stop(en + 0.02);
}

/** Plays one named sound now. False when it was not played (unknown, muted, no gesture yet, too soon after the same sound, or too many this second). */
function sfxPlay(name, nowMs) {
  const A = AUD, def = SFX[name];
  if (!def || A.muted || !A.ctx) return false;
  if (nowMs - (A.last[name] === undefined ? -1e9 : A.last[name]) < def.gap) return false;
  if (nowMs - A.winStart >= 1000) { A.winStart = nowMs; A.winCount = 0; }
  if (A.winCount >= MAX_PER_SECOND) return false;
  A.last[name] = nowMs; A.winCount++;
  const t0 = A.ctx.currentTime + 0.005;
  try { for (const v of def.v) voice(A.ctx, A.master, v, t0); A.played++; } catch (e) { return false; }
  return true;
}

/** The sounds of one game tick: the same sound once, the most important MAX_PER_TICK of them, so a big clash is a few clicks and not a wall of noise. Returns how many played. */
function sfxBatch(names, tick, nowMs) {
  const A = AUD;
  if (A.muted || !A.ctx || !names.length) return 0;
  if (tick !== A.tick) { A.tick = tick; A.count = 0; }
  const uniq = [...new Set(names)].filter((n) => SFX[n]).sort((a, b) => SFX[b].pri - SFX[a].pri);
  let n = 0;
  for (const nm of uniq) {
    if (A.count >= MAX_PER_TICK) break;
    if (sfxPlay(nm, nowMs)) { A.count++; n++; }
  }
  return n;
}
const clockMs = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

// ---- small drawing helpers ----------------------------------------------------------------------------------------------------------

function pen(c, w) { c.lineWidth = w || 2; c.strokeStyle = INK; c.lineJoin = 'round'; c.lineCap = 'round'; }
function fillStroke(c, fill) { c.fillStyle = fill; c.fill(); c.stroke(); }
function ell(c, cx, cy, rx, ry, fill) { c.beginPath(); c.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2); fillStroke(c, fill); }
function poly(c, pts, fill) { c.beginPath(); c.moveTo(pts[0][0], pts[0][1]); for (let i = 1; i < pts.length; i++) c.lineTo(pts[i][0], pts[i][1]); c.closePath(); fillStroke(c, fill); }
function rect(c, x, y, w, h, r, fill) {
  c.beginPath();
  if (c.roundRect) c.roundRect(x, y, w, h, r); else c.rect(x, y, w, h);
  fillStroke(c, fill);
}
function seg(c, x1, y1, x2, y2, w, col) {
  c.beginPath(); c.moveTo(x1, y1); c.lineTo(x2, y2);
  c.lineWidth = w + 2; c.strokeStyle = INK; c.stroke();
  c.lineWidth = w; c.strokeStyle = col; c.stroke();
  pen(c, 2);
}
function shadow(c, rx) { c.beginPath(); c.ellipse(32, 59, rx, 4, 0, 0, Math.PI * 2); c.fillStyle = 'rgba(0,0,0,0.32)'; c.fill(); }

// ---- the eight soldiers (facing right, feet near y 58 of 64) -------------------------------------------------------------------------

const DRAW = [
  function spearmen(c, p) {   // plain foot with a long spear
    shadow(c, 13);
    rect(c, 25, 50, 5, 8, 2, '#5b4636'); rect(c, 34, 50, 5, 8, 2, '#5b4636');
    rect(c, 23, 30, 18, 23, 6, p.main);
    rect(c, 23, 44, 18, 4, 1, p.dark);
    ell(c, 32, 23, 8, 8, SKIN);
    c.beginPath(); c.arc(32, 22, 8.5, Math.PI, 2 * Math.PI); fillStroke(c, '#6b4a2b');
    seg(c, 44, 58, 48, 6, 3, '#9a6b3a');
    poly(c, [[48, 2], [44.5, 13], [51.5, 13]], '#dfe5ea');
  },
  function shieldwall(c, p) {   // heavy, helmet, a big shield in front
    shadow(c, 14);
    rect(c, 24, 50, 6, 8, 2, '#4a4f58'); rect(c, 33, 50, 6, 8, 2, '#4a4f58');
    rect(c, 21, 29, 22, 24, 6, '#a3adbb');
    rect(c, 21, 39, 22, 5, 1, p.main);
    ell(c, 31, 22, 9, 8.5, '#b9c2ce');
    rect(c, 25, 21, 14, 3, 1, '#2b2f36');
    poly(c, [[36, 26], [57, 26], [57, 43], [46.5, 58], [36, 43]], p.main);
    poly(c, [[39, 29], [54, 29], [54, 42], [46.5, 53], [39, 42]], p.light);
    ell(c, 46.5, 38, 4.5, 4.5, '#e9edf2');
    seg(c, 40, 40, 36, 26, 3, '#dfe5ea');
  },
  function riders(c, p) {   // a horse and a rider with a lance
    shadow(c, 20);
    seg(c, 18, 47, 16, 58, 3.5, '#5e3b1f'); seg(c, 24, 48, 24, 58, 3.5, '#5e3b1f');
    seg(c, 38, 48, 40, 58, 3.5, '#5e3b1f'); seg(c, 44, 47, 47, 58, 3.5, '#5e3b1f');
    ell(c, 31, 43, 18, 9, '#8e5b34');
    poly(c, [[42, 40], [50, 22], [58, 26], [50, 44]], '#8e5b34');
    ell(c, 57, 26, 6, 4.5, '#9a6640');
    poly(c, [[48, 22], [50, 15], [53, 22]], '#8e5b34');
    seg(c, 14, 40, 5, 50, 3.5, '#4a2d18');
    rect(c, 26, 22, 11, 17, 4, p.main);
    ell(c, 31, 16, 6.5, 6.5, SKIN);
    c.beginPath(); c.arc(31, 15, 7, Math.PI, 2 * Math.PI); fillStroke(c, '#b9c2ce');
    seg(c, 30, 32, 62, 8, 3, '#9a6b3a');
    poly(c, [[62, 8], [54, 6], [56, 13]], p.light);
  },
  function archers(c, p) {   // hooded, a bow drawn
    shadow(c, 12);
    rect(c, 26, 50, 5, 8, 2, '#4a3b2a'); rect(c, 34, 50, 5, 8, 2, '#4a3b2a');
    rect(c, 24, 30, 16, 22, 6, '#58803e');
    rect(c, 24, 36, 16, 4, 1, p.main);
    ell(c, 32, 23, 8, 8, SKIN);
    poly(c, [[23, 26], [32, 11], [41, 26], [36, 22], [28, 22]], '#3f6430');
    c.beginPath(); c.arc(48, 34, 17, -1.15, 1.15); c.lineWidth = 5; c.strokeStyle = INK; c.stroke();
    c.lineWidth = 3; c.strokeStyle = '#a86a2e'; c.stroke(); pen(c, 2);
    const bx = 48 + 17 * Math.cos(1.15), by1 = 34 - 17 * Math.sin(1.15), by2 = 34 + 17 * Math.sin(1.15);
    seg(c, bx, by1, bx, by2, 1, '#f1ead7');
    seg(c, 30, 34, 60, 34, 1.6, '#e6dcc2');
    poly(c, [[62, 34], [57, 31], [57, 37]], '#dfe5ea');
  },
  function priests(c, p) {   // a long white robe, a halo, a glowing staff
    shadow(c, 13);
    poly(c, [[20, 58], [44, 58], [39, 29], [25, 29]], '#f4f0e2');
    poly(c, [[20, 58], [44, 58], [43, 51], [21, 51]], p.main);
    rect(c, 28, 29, 8, 20, 3, p.light);
    ell(c, 32, 22, 7.5, 7.5, SKIN);
    c.beginPath(); c.ellipse(32, 11, 10, 3.2, 0, 0, Math.PI * 2); c.lineWidth = 4; c.strokeStyle = INK; c.stroke(); c.lineWidth = 2.4; c.strokeStyle = '#ffd95a'; c.stroke(); pen(c, 2);
    seg(c, 48, 58, 48, 14, 3, '#d8b04a');
    c.shadowColor = '#fff3a0'; c.shadowBlur = 8; ell(c, 48, 11, 5, 5, '#fff6b8'); c.shadowBlur = 0;
  },
  function wights(c, p) {   // gaunt, ragged, green eyes
    shadow(c, 12);
    poly(c, [[22, 58], [25, 52], [29, 58], [33, 51], [37, 58], [41, 52], [43, 58], [41, 30], [25, 30]], '#34453f');
    rect(c, 25, 30, 14, 5, 2, p.main);
    ell(c, 32, 23, 7.5, 8, '#d7dfcf');
    ell(c, 29.5, 22.5, 1.8, 2.2, '#10150f'); ell(c, 35, 22.5, 1.8, 2.2, '#10150f');
    c.shadowColor = '#6dff95'; c.shadowBlur = 6; ell(c, 29.5, 22.5, 1, 1.2, '#7dff9a'); ell(c, 35, 22.5, 1, 1.2, '#7dff9a'); c.shadowBlur = 0;
    c.beginPath(); c.moveTo(40, 52); c.quadraticCurveTo(58, 40, 50, 14); c.lineWidth = 6; c.strokeStyle = INK; c.stroke();
    c.lineWidth = 3; c.strokeStyle = '#9db3a6'; c.stroke(); pen(c, 2);
  },
  function wolves(c, p) {   // low and fast on four legs
    shadow(c, 20);
    seg(c, 16, 48, 14, 58, 3.5, '#5c6068'); seg(c, 23, 49, 23, 58, 3.5, '#5c6068');
    seg(c, 38, 49, 40, 58, 3.5, '#5c6068'); seg(c, 44, 48, 48, 58, 3.5, '#5c6068');
    c.beginPath(); c.moveTo(15, 42); c.quadraticCurveTo(4, 40, 3, 28); c.lineWidth = 7; c.strokeStyle = INK; c.stroke();
    c.lineWidth = 4; c.strokeStyle = '#7c8088'; c.stroke(); pen(c, 2);
    ell(c, 30, 43, 17, 8.5, '#80848c');
    ell(c, 50, 36, 8, 6.5, '#8d9199');
    poly(c, [[55, 34], [64, 38], [56, 41]], '#8d9199');
    poly(c, [[46, 31], [47, 22], [52, 30]], '#6a6e76');
    ell(c, 53, 34, 1.6, 1.6, '#ff3b30');
    rect(c, 41, 38, 5, 9, 2, p.main);
  },
  function catapults(c, p) {   // a wooden frame, wheels, a throwing arm
    shadow(c, 24);
    poly(c, [[8, 50], [56, 50], [52, 40], [14, 40]], '#8a5a2e');
    seg(c, 16, 41, 44, 14, 5, '#a8733a');
    ell(c, 47, 13, 6, 5, '#7a4a22');
    ell(c, 47, 10.5, 4, 3.6, '#9aa0a8');
    ell(c, 15, 45, 5, 5, '#5a5f68');
    ell(c, 18, 55, 7.5, 7.5, '#5e3b1f'); ell(c, 46, 55, 7.5, 7.5, '#5e3b1f');
    ell(c, 18, 55, 2.6, 2.6, '#d8c19a'); ell(c, 46, 55, 2.6, 2.6, '#d8c19a');
    seg(c, 10, 44, 10, 22, 2, '#4a2d18');
    poly(c, [[10, 22], [22, 26], [10, 31]], p.main);
  },
];

function makeSprites() {
  const out = [[], []], flash = [[], []];
  for (let side = 0; side < 2; side++) {
    for (let k = 0; k < KINDS; k++) {
      let cv = document.createElement('canvas'); cv.width = SPR; cv.height = SPR;
      const c = cv.getContext('2d'); c.scale(SPR / 64, SPR / 64); pen(c, 2);
      DRAW[k](c, SIDE[side]);
      // the team outline: the picture stamped round its edge in the team colour, then the picture itself on top
      const ov = document.createElement('canvas'); ov.width = SPR; ov.height = SPR;
      const o = ov.getContext('2d');
      for (let a = 0; a < 8; a++) o.drawImage(cv, Math.cos(a * Math.PI / 4) * OUTLINE, Math.sin(a * Math.PI / 4) * OUTLINE);
      o.globalCompositeOperation = 'source-in'; o.fillStyle = SIDE[side].main; o.fillRect(0, 0, SPR, SPR);
      o.globalCompositeOperation = 'source-over'; o.drawImage(cv, 0, 0);
      cv = ov;
      out[side][k] = cv;
      const fv = document.createElement('canvas'); fv.width = SPR; fv.height = SPR;
      const f = fv.getContext('2d'); f.drawImage(cv, 0, 0); f.globalCompositeOperation = 'source-atop'; f.fillStyle = '#ffffff'; f.fillRect(0, 0, SPR, SPR);
      flash[side][k] = fv;
    }
  }
  return { out, flash };
}

// ---- assets ----------------------------------------------------------------------------------------------------------------------------

function loadImg(url) {
  return new Promise((resolve) => {
    const im = new Image();
    im.onload = () => resolve(im);
    im.onerror = () => resolve(null);   // a missing picture is drawn as a flat colour, never an error
    im.src = url;
  });
}

// ---- the ground -----------------------------------------------------------------------------------------------------------------------

const sx = (x, y) => (S.vertical ? y * S.v : x * S.u);
const sy = (x, y) => (S.vertical ? (S.L.width - x) * S.u : y * S.v);
function clamp(x, a, b) { return Math.max(a, Math.min(b, x)); }

function cellRect(col, row) {
  const c = S.L.cell, x0 = col * c, y0 = row * c;
  const ax = sx(x0, y0), ay = sy(x0, y0), bx = sx(x0 + c, y0 + c), by = sy(x0 + c, y0 + c);
  return [Math.min(ax, bx), Math.min(ay, by), Math.abs(bx - ax), Math.abs(by - ay)];
}

function buildTerrain() {
  const L = S.L, dpr = S.dpr;
  const cv = document.createElement('canvas'); cv.width = Math.round(S.cssW * dpr); cv.height = Math.round(S.cssH * dpr);
  const c = cv.getContext('2d'); c.scale(dpr, dpr);
  c.fillStyle = '#2f8a4a'; c.fillRect(0, 0, S.cssW, S.cssH);
  const I = S.imgs;
  for (let r = 0; r < L.rows.length; r++) {
    const row = L.rows[r];
    for (let col = 0; col < row.length; col++) {
      const [x, y, w, h] = cellRect(col, r), ch = row[col];
      const g = ((col * 7 + r * 13) % 5 === 0) ? I.grass2 : I.grass1;
      if (g) c.drawImage(g, x, y, w + 0.6, h + 0.6); else { c.fillStyle = '#2f8a4a'; c.fillRect(x, y, w + 0.6, h + 0.6); }
      const worldX = col * L.cell;
      if (worldX < L.playerEdge) { c.fillStyle = 'rgba(63,140,255,0.13)'; c.fillRect(x, y, w + 0.6, h + 0.6); }
      else if (worldX >= L.enemyEdge) { c.fillStyle = 'rgba(229,83,61,0.13)'; c.fillRect(x, y, w + 0.6, h + 0.6); }
      if (ch === '#') {
        if (I.stone) c.drawImage(I.stone, x, y, w + 0.6, h + 0.6); else { c.fillStyle = '#8b929b'; c.fillRect(x, y, w, h); }
        c.fillStyle = 'rgba(255,255,255,0.18)'; c.fillRect(x, y, w, Math.max(2, h * 0.12));
        c.fillStyle = 'rgba(0,0,0,0.30)'; c.fillRect(x, y + h - Math.max(2, h * 0.14), w, Math.max(2, h * 0.14));
        c.strokeStyle = 'rgba(20,17,15,0.55)'; c.lineWidth = 1; c.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
      } else if (ch === ',') {
        c.fillStyle = 'rgba(20,70,30,0.35)'; c.fillRect(x, y, w, h);
        if (I.bush) { const s = Math.min(w, h) * 1.5; c.drawImage(I.bush, x + w / 2 - s / 2, y + h / 2 - s / 2, s, s); }
      }
    }
  }
  // a few pines along the far edge of each base, behind the castle
  if (I.pine) {
    for (let r = 0; r < L.rows.length; r += 3) {
      for (const colSide of [0, 1]) {
        const col = colSide === 0 ? 0 : L.rows[r].length - 1;
        const [x, y, w, h] = cellRect(col, r);
        const s = Math.min(S.sz * 1.3, 60);
        c.drawImage(I.pine, x + w / 2 - s / 2, y + h / 2 - s / 2, s, s);
      }
    }
  }
  // the line where each base zone ends
  c.setLineDash([6, 5]); c.lineWidth = 2;
  for (let s = 0; s < 2; s++) {
    const wx = s === 0 ? L.playerEdge : L.enemyEdge;
    c.strokeStyle = SIDE[s].light + '88';
    c.beginPath(); c.moveTo(sx(wx, 0), sy(wx, 0)); c.lineTo(sx(wx, L.height), sy(wx, L.height)); c.stroke();
  }
  c.setLineDash([]);
  S.terrain = cv;
}

function fit() {
  const parent = S.canvas.parentElement || S.canvas;
  const w = Math.max(280, Math.floor(parent.clientWidth || 360));
  const vh = (typeof window !== 'undefined' && window.innerHeight) || 800;
  S.vertical = w < 720;
  S.cssW = w;
  // the board leaves room for the tray under it, so the whole game fits one screen
  S.cssH = S.vertical ? Math.round(clamp(vh * 0.64, 440, 860)) : Math.round(clamp(w * 0.46, 330, Math.max(330, vh * 0.68)));
  S.dpr = Math.min((typeof window !== 'undefined' && window.devicePixelRatio) || 1, 2);
  if (S.vertical) { S.v = S.cssW / S.L.height; S.u = S.cssH / S.L.width; }
  else { S.u = S.cssW / S.L.width; S.v = S.cssH / S.L.height; }
  S.sz = clamp(Math.sqrt(S.u * S.v) * 3.4, 26, 46);
  S.canvas.style.width = S.cssW + 'px'; S.canvas.style.height = S.cssH + 'px';
  S.canvas.width = Math.round(S.cssW * S.dpr); S.canvas.height = Math.round(S.cssH * S.dpr);
  buildTerrain();
}

// ---- packets ----------------------------------------------------------------------------------------------------------------------------

function applyPacket(u8, nowMs) {
  const buf = u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength);   // a copy, so the floats are aligned
  const f = new Float32Array(buf);
  const t = nowMs / 1000, n = f[5] | 0;
  const prevStage = S.head.stage, prevP = S.head.pBase, prevE = S.head.eBase;
  S.head = { tick: f[0], stage: f[1], status: f[2], pBase: f[3], eBase: f[4], alpha: f[6] };
  const seen = new Set(), ev = [];
  const fighting = {};   // side*8+lane -> bit mask of the kinds fighting there (who is hitting whom, roughly, for the sound)
  for (let i = 0; i < n; i++) { const o = 8 + i * 10; if (f[o + 9] > 0.5) { const k = (f[o + 1] | 0) * 8 + (f[o + 2] | 0); fighting[k] = (fighting[k] || 0) | (1 << (f[o + 3] | 0)); } }
  for (let i = 0; i < n; i++) {
    const o = 8 + i * 10, id = f[o] | 0;
    let b = S.bodies.get(id);
    const x = f[o + 4], y = f[o + 5], hp = f[o + 8];
    if (!b) {
      b = { id, side: f[o + 1] | 0, lane: f[o + 2] | 0, kind: f[o + 3] | 0, x, y, px: x, py: y, fx: f[o + 6], fy: f[o + 7], flip: (f[o + 1] | 0) === 1, hp, hitAt: -9, floatAt: -9, born: t, mv: 0, fight: false, phase: (id * 2.399) % 6.28 };
      S.bodies.set(id, b);
      if (S.bodies.size > 0 && S.ready) S.rings.push({ x, y, t0: t, side: b.side });
    } else {
      b.px = b.x; b.py = b.y; b.x = x; b.y = y;
      if (hp < b.hp - 0.002) {
        const mask = fighting[(1 - b.side) * 8 + b.lane] || 0;   // the other side's kinds fighting in this lane
        ev.push(mask & (1 << 7) ? 'thud' : mask & (1 << 3) ? 'arrow' : 'hit');
        b.hitAt = t;
        if (t - b.floatAt > 0.15 && S.floats.length < 60) { S.floats.push({ x, y, t0: t, text: '-' + Math.max(1, Math.round((b.hp - hp) * 100)) }); b.floatAt = t; }
      }
      b.hp = hp; b.fx = f[o + 6]; b.fy = f[o + 7];
    }
    if (S.ready && !b.fight && f[o + 9] > 0.5 && b.kind === 2) ev.push('charge');
    b.fight = f[o + 9] > 0.5;
    const moved = Math.abs(b.x - b.px) + Math.abs(b.y - b.py) > 0.004;
    b.mv = b.mv * 0.7 + (moved ? 0.3 : 0);
    const dirx = S.vertical ? b.fy : b.fx;
    if (Math.abs(dirx) > 0.25) b.flip = dirx < 0;
    seen.add(id);
  }
  for (const [id, b] of S.bodies) {
    if (!seen.has(id)) { S.corpses.push({ b, t0: t }); S.bodies.delete(id); ev.push('fall'); }
  }
  if (S.ready) {
    if (f[3] < prevP - 0.001) ev.push(f[3] <= 0 ? 'castleFall' : 'castleHit');
    if (f[4] < prevE - 0.001) ev.push(f[4] <= 0 ? 'castleFall' : 'castleHit');
    if (prevStage === 0 && S.head.stage === 1) ev.push('runStart');
    if (prevStage !== 2 && S.head.stage === 2) ev.push(S.head.status === 1 ? 'win' : 'lose');
  }
  S.ready = true;
  if (prevStage !== S.head.stage) S.stageAt = t;
  if (ev.length) sfxBatch(ev, f[0], nowMs);
}

// ---- drawing ------------------------------------------------------------------------------------------------------------------------------

function laneBand(lane) {
  const L = S.L, half = L.laneHalf, cy = L.laneCentreY[lane];
  return [cy - half, cy + half];
}

function chip(c, text, x, y, fill, align) {
  c.font = '600 11px system-ui, sans-serif';
  const w = c.measureText(text).width + 12, h = 17;
  const left = align === 'right' ? x - w : align === 'center' ? x - w / 2 : x;
  c.fillStyle = fill; c.strokeStyle = 'rgba(0,0,0,0.5)'; c.lineWidth = 1;
  c.beginPath(); if (c.roundRect) c.roundRect(left, y, w, h, 8); else c.rect(left, y, w, h); c.fill(); c.stroke();
  c.fillStyle = '#fff'; c.textBaseline = 'middle'; c.textAlign = 'left'; c.fillText(text, left + 6, y + h / 2 + 0.5);
}

function drawBase(c, side, frac) {
  const L = S.L, I = S.imgs;
  const edge = side === 0 ? L.playerEdge : L.width - L.enemyEdge;
  const cxw = side === 0 ? L.playerEdge / 2 : L.enemyEdge + (L.width - L.enemyEdge) / 2, cyw = L.height / 2;
  const x = sx(cxw, cyw), y = sy(cxw, cyw);
  const zoneW = S.vertical ? edge * S.u : edge * S.u;
  const size = clamp(Math.max(zoneW * 1.5, 56), 56, 110);
  const img = I.castle || I.keep;
  c.save();
  if (frac <= 0) c.globalAlpha = 0.35;
  c.fillStyle = 'rgba(0,0,0,0.28)'; c.beginPath(); c.ellipse(x, y + size * 0.4, size * 0.5, size * 0.14, 0, 0, Math.PI * 2); c.fill();
  if (img) c.drawImage(img, x - size / 2, y - size / 2, size, size);
  else { c.fillStyle = '#8b929b'; c.fillRect(x - size / 2, y - size / 2, size, size); }
  // banner in the side's colour
  c.strokeStyle = INK; c.lineWidth = 2; c.beginPath(); c.moveTo(x, y - size / 2 - 22); c.lineTo(x, y - size / 2 + 4); c.stroke();
  c.fillStyle = SIDE[side].main; c.beginPath(); c.moveTo(x, y - size / 2 - 22); c.lineTo(x + 18, y - size / 2 - 16); c.lineTo(x, y - size / 2 - 9); c.closePath(); c.fill(); c.stroke();
  c.restore();
  // base health: a thin bar, no number
  const bw = Math.max(56, Math.min(size * 1.1, 110)), bh = 6, bx = x - bw / 2, by = y + size / 2 + 6;
  c.fillStyle = 'rgba(0,0,0,0.65)'; c.fillRect(bx - 1, by - 1, bw + 2, bh + 2);
  c.fillStyle = frac > 0.5 ? '#5ed17a' : frac > 0.25 ? '#e6c450' : '#ef5a4a';
  c.fillRect(bx, by, bw * clamp(frac, 0, 1), bh);
  c.font = '700 10px system-ui, sans-serif'; c.textAlign = 'center'; c.textBaseline = 'top'; c.fillStyle = '#fff'; c.strokeStyle = 'rgba(0,0,0,0.7)'; c.lineWidth = 3;
  const label = side === 0 ? 'Your base' : 'Their base';
  c.strokeText(label, x, by + bh + 3); c.fillText(label, x, by + bh + 3);
}

function drawLaneMarks(c, t) {
  const L = S.L, info = S.info;
  for (let lane = 0; lane < L.laneCentreY.length; lane++) {
    const [y0, y1] = laneBand(lane), cy = L.laneCentreY[lane];
    if (lane === S.selected) {
      const pulse = 0.5 + 0.5 * Math.sin(t * 4);
      const ax = sx(L.playerEdge, y0), ay = sy(L.playerEdge, y0), bx = sx(L.enemyEdge, y1), by = sy(L.enemyEdge, y1);
      c.fillStyle = 'rgba(255,255,255,0.07)'; c.fillRect(Math.min(ax, bx), Math.min(ay, by), Math.abs(bx - ax), Math.abs(by - ay));
      c.strokeStyle = 'rgba(255,224,130,' + (0.55 + 0.35 * pulse).toFixed(2) + ')'; c.lineWidth = 3;
      c.strokeRect(Math.min(ax, bx) + 1.5, Math.min(ay, by) + 1.5, Math.abs(bx - ax) - 3, Math.abs(by - ay) - 3);
    }
    // the lane's number, where you tap
    const nx = sx(L.playerEdge + 2.2, cy), ny = sy(L.playerEdge + 2.2, cy);
    c.beginPath(); c.arc(nx, ny, 14, 0, Math.PI * 2);
    c.fillStyle = lane === S.selected ? '#ffd95a' : 'rgba(20,17,15,0.78)'; c.fill(); c.lineWidth = 2; c.strokeStyle = '#fff'; c.stroke();
    c.fillStyle = lane === S.selected ? '#14110f' : '#fff'; c.font = '700 14px system-ui, sans-serif'; c.textAlign = 'center'; c.textBaseline = 'middle'; c.fillText(String(lane + 1), nx, ny + 0.5);
    if (!info) continue;
    for (let side = 0; side < 2; side++) {
      const cur = (info.orders && info.orders[side] ? info.orders[side][lane] : 0) | 0;
      const staged = side === 0 && info.stagedOrders ? info.stagedOrders[lane] : undefined;
      const text = (staged !== undefined ? 'Next: ' + ORDER_NAME[staged] : ORDER_NAME[cur]);
      const wx = side === 0 ? L.playerEdge + 5 : L.enemyEdge - 1;
      const ay0 = S.vertical ? cy : y0 + 0.6;   // on a phone the chip is centred on the lane
      const px = sx(wx, ay0), py = (S.vertical && side === 0) ? sy(L.playerEdge + 2.2, cy) - 38 : sy(wx, ay0);   // on a phone, just above the lane's number
      const col = staged !== undefined ? 'rgba(180,120,0,0.92)' : (side === 0 ? 'rgba(29,79,168,0.9)' : 'rgba(154,42,28,0.9)');
      const al = S.vertical ? 'center' : (side === 0 ? 'left' : 'right');
      chip(c, text, px, py, col, al);
      // your plays at work: what was sent into this lane at the last pause, a few seconds into the run
      if (side === 0 && info.stage === 1 && info.work && info.work[lane] && S.head.stage === 1) {
        const fade = clamp(1 - (t - S.stageAt - 10) / 3, 0, 1);
        if (fade > 0) {
          c.globalAlpha = fade;
          info.work[lane].forEach((txt, k) => chip(c, txt, px, S.vertical ? py - 19 * (k + 1) : py + 19 * (k + 1), 'rgba(40,90,170,0.92)', al));
          c.globalAlpha = 1;
        }
      }
      // a held line is drawn where the bodies will stop
      if (cur === 1) {
        const len = L.enemyEdge - L.playerEdge, hx = side === 0 ? L.playerEdge + len * L.holdPct / 100 : L.enemyEdge - len * L.holdPct / 100;
        c.setLineDash([5, 5]); c.lineWidth = 2; c.strokeStyle = SIDE[side].light;
        c.beginPath(); c.moveTo(sx(hx, y0), sy(hx, y0)); c.lineTo(sx(hx, y1), sy(hx, y1)); c.stroke(); c.setLineDash([]);
      }
    }
  }
}

// ---- the orders drawn ON the selected lane, the mute button, and what a tap hits --------------------------------------------------------

const TAP = 44;   // the smallest tap target, in CSS pixels

function laneRect(lane) {
  const L = S.L, [y0, y1] = laneBand(lane);
  const ax = sx(L.playerEdge, y0), ay = sy(L.playerEdge, y0), bx = sx(L.enemyEdge, y1), by = sy(L.enemyEdge, y1);
  return [Math.min(ax, bx), Math.min(ay, by), Math.abs(bx - ax), Math.abs(by - ay)];
}
/** The order chips of a lane: a column down the middle of a tall lane (phone), a row along the middle of a wide one. Each is at least 44 px tall. */
function menuBoxes(lane) {
  const menu = (S.info && S.info.menu) || [], n = menu.length, gap = 6, out = [];
  if (!n) return out;
  const [x, y, w, h] = laneRect(lane);
  if (S.vertical) {
    const bw = clamp(w - 8, 64, 130), top = y + (h - (n * TAP + (n - 1) * gap)) / 2;
    menu.forEach((m, i) => out.push({ x: x + (w - bw) / 2, y: top + i * (TAP + gap), w: bw, h: TAP, order: m.order, name: m.name }));
  } else {
    const bw = clamp((w * 0.7 - (n - 1) * gap) / n, 64, 124), left = x + (w - (n * bw + (n - 1) * gap)) / 2;
    menu.forEach((m, i) => out.push({ x: left + i * (bw + gap), y: y + (h - TAP) / 2, w: bw, h: TAP, order: m.order, name: m.name }));
  }
  return out;
}
const menuOn = () => !!(S.info && S.info.human && S.info.stage === 0 && S.selected >= 0);
const pausePick = () => !!(S.info && S.info.human && S.info.stage === 0);
function muteBox() { return { x: S.cssW - 92 - 8, y: 8, w: 92, h: TAP }; }
const inBox = (b, px, py) => px >= b.x && px <= b.x + b.w && py >= b.y && py <= b.y + b.h;

/** What a tap at (px, py) on the canvas means: the mute button, an order chip of the selected lane, the selected lane's number (puts the lane down), or a lane. Null when nothing. */
function hitAt(px, py) {
  if (inBox(muteBox(), px, py)) return { kind: 'mute' };
  if (!pausePick()) return null;
  if (menuOn()) for (const b of menuBoxes(S.selected)) if (inBox(b, px, py)) return { kind: 'order', lane: S.selected, order: b.order };
  if (S.selected >= 0) {
    const cy = S.L.laneCentreY[S.selected], nx = sx(S.L.playerEdge + 2.2, cy), ny = sy(S.L.playerEdge + 2.2, cy);
    if (Math.hypot(px - nx, py - ny) <= 22) return { kind: 'deselect' };
  }
  const wy = S.vertical ? px / S.v : py / S.v;
  const pitch = S.L.laneCentreY.length > 1 ? S.L.laneCentreY[1] - S.L.laneCentreY[0] : S.L.height;
  return { kind: 'lane', lane: clamp(Math.floor(wy / pitch), 0, S.L.laneCentreY.length - 1) };
}

function tapAt(px, py) {
  unlock();   // a tap is a gesture: the sound may start now
  const h = hitAt(px, py), now = clockMs();
  if (!h) return null;
  if (h.kind === 'mute') { setMuted(!AUD.muted); sfxPlay('tap', now); }
  else if (h.kind === 'order') {
    const staged = S.info.stagedOrders ? S.info.stagedOrders[h.lane] : undefined;
    sfxPlay(staged === h.order ? 'orderClear' : 'orderSet', now);
    S.ref.invokeMethodAsync('OnOrderTap', h.lane, h.order);
  } else if (h.kind === 'deselect') { S.selected = -1; sfxPlay('tap', now); S.ref.invokeMethodAsync('OnLaneTap', -1); }
  else { S.selected = h.lane; sfxPlay('tap', now); S.ref.invokeMethodAsync('OnLaneTap', h.lane); }
  return h;
}

function drawMenu(c) {
  if (!menuOn()) return;
  const lane = S.selected, info = S.info;
  const inForce = info.orders && info.orders[0] ? info.orders[0][lane] | 0 : -1;
  const staged = info.stagedOrders ? info.stagedOrders[lane] : undefined;
  c.font = '700 14px system-ui, sans-serif'; c.textAlign = 'center'; c.textBaseline = 'middle';
  for (const b of menuBoxes(lane)) {
    const on = staged === b.order, now = staged === undefined && inForce === b.order;
    c.beginPath(); if (c.roundRect) c.roundRect(b.x, b.y, b.w, b.h, 10); else c.rect(b.x, b.y, b.w, b.h);
    c.fillStyle = on ? '#ffd95a' : 'rgba(20,17,15,0.84)'; c.fill();
    c.lineWidth = now ? 3 : 2; c.strokeStyle = on ? '#ffffff' : now ? '#a9ccff' : 'rgba(255,255,255,0.7)'; c.stroke();
    c.fillStyle = on ? '#14110f' : '#ffffff'; c.fillText(b.name, b.x + b.w / 2, b.y + b.h / 2 + 0.5);
  }
}

function drawMute(c) {
  const b = muteBox();
  c.beginPath(); if (c.roundRect) c.roundRect(b.x, b.y, b.w, b.h, 10); else c.rect(b.x, b.y, b.w, b.h);
  c.fillStyle = 'rgba(20,17,15,0.7)'; c.fill(); c.lineWidth = 2; c.strokeStyle = 'rgba(255,255,255,0.6)'; c.stroke();
  c.fillStyle = AUD.muted ? '#ffb7aa' : '#ffffff'; c.font = '700 13px system-ui, sans-serif'; c.textAlign = 'center'; c.textBaseline = 'middle';
  c.fillText(AUD.muted ? 'Sound off' : 'Sound on', b.x + b.w / 2, b.y + b.h / 2 + 0.5);
}

function drawGhosts(c, t) {
  const info = S.info, L = S.L;
  if (!info || !info.stagedPacks || info.stage !== 0) return;
  const perLane = {};
  for (const p of info.stagedPacks) { (perLane[p.lane] = perLane[p.lane] || []).push(p); }
  for (const lane of Object.keys(perLane)) {
    const list = perLane[lane], cy = L.laneCentreY[lane];
    list.forEach((p, i) => {
      const wx = L.playerEdge - 2.2 - (i % 4) * 2.2, wy = cy + (Math.floor(i / 4) - 0.5) * 3 * 1.2;
      const x = sx(wx, wy), y = sy(wx, wy) + Math.sin(t * 3 + i) * 1.5;
      const s = S.sz * 1.05;
      c.globalAlpha = 0.6; c.drawImage(S.sprites.out[0][p.kind], x - s / 2, y - s * 0.86, s, s); c.globalAlpha = 1;
      chip(c, '+' + p.count, x, y + 1, 'rgba(20,17,15,0.8)', 'center');
    });
  }
}

function drawBody(c, b, t, run) {
  const s = S.sz, side = b.side, spr = S.sprites.out[side][b.kind];
  const x = sx(b.x, b.y), y = sy(b.x, b.y);
  let dx = 0, dy = 0, rot = 0;
  const ph = t * 10 + b.phase;
  if (run && b.fight) { const l = Math.max(0, Math.sin(t * 7 + b.phase)); dx = (b.flip ? -1 : 1) * l * s * 0.12; dy = -l * s * 0.04; rot = (b.flip ? -1 : 1) * l * 0.08; }
  else if (run && b.mv > 0.12) { dy = -Math.abs(Math.sin(ph)) * s * 0.09; rot = Math.sin(ph) * 0.05; }
  else dy = Math.sin(t * 2 + b.phase) * s * 0.015;
  const age = t - b.born, grow = age < 0.35 ? 0.4 + 0.6 * (age / 0.35) : 1;
  // the team disc on the ground under the feet (ellipses only, so a crowd stays cheap)
  c.beginPath(); c.ellipse(x, y + s * 0.02, s * 0.36 * grow, s * 0.13 * grow, 0, 0, Math.PI * 2);
  c.globalAlpha = 0.55; c.fillStyle = SIDE[side].main; c.fill(); c.globalAlpha = 1; c.lineWidth = 2; c.strokeStyle = SIDE[side].light; c.stroke();
  c.save();
  c.translate(x + dx, y + dy);
  c.rotate(rot);
  c.scale((b.flip ? -1 : 1) * grow, grow);
  c.drawImage(spr, -s / 2, -s * 0.9, s, s);
  const hitAge = t - b.hitAt;
  if (hitAge < 0.16) { c.globalAlpha = 0.75 * (1 - hitAge / 0.16); c.drawImage(S.sprites.flash[side][b.kind], -s / 2, -s * 0.9, s, s); c.globalAlpha = 1; }
  c.restore();
  // a thin health bar, no number
  const bw = s * 0.62, bh = 3, bx = x - bw / 2, by = y - s * 0.98;
  c.fillStyle = 'rgba(0,0,0,0.6)'; c.fillRect(bx - 0.5, by - 0.5, bw + 1, bh + 1);
  c.fillStyle = b.hp > 0.5 ? '#5ed17a' : b.hp > 0.25 ? '#e6c450' : '#ef5a4a';
  c.globalAlpha = b.hp > 0.995 ? 0.55 : 1; c.fillRect(bx, by, bw * clamp(b.hp, 0, 1), bh); c.globalAlpha = 1;
}

function draw(t) {
  const c = S.ctx;
  c.setTransform(S.dpr, 0, 0, S.dpr, 0, 0);
  if (S.terrain) c.drawImage(S.terrain, 0, 0, S.cssW, S.cssH);
  else { c.fillStyle = '#2f8a4a'; c.fillRect(0, 0, S.cssW, S.cssH); }
  const run = S.head.stage === 1;
  drawLaneMarks(c, t);
  drawBase(c, 0, S.head.pBase);
  drawBase(c, 1, S.head.eBase);
  drawGhosts(c, t);

  // the fallen, under the living
  S.corpses = S.corpses.filter((k) => t - k.t0 < 1.5);
  for (const k of S.corpses) {
    const a = 1 - (t - k.t0) / 1.5, b = k.b, s = S.sz, x = sx(b.x, b.y), y = sy(b.x, b.y);
    c.save(); c.globalAlpha = Math.max(0, a) * 0.85; c.translate(x, y); c.rotate((b.flip ? -1 : 1) * Math.min(1, (t - k.t0) * 5) * 1.45); c.scale(b.flip ? -1 : 1, 1);
    c.drawImage(S.sprites.out[b.side][b.kind], -s / 2, -s * 0.9, s, s); c.restore();
  }
  // the living, back to front
  const list = Array.from(S.bodies.values());
  list.sort((p, q) => sy(p.x, p.y) - sy(q.x, q.y));
  for (const b of list) drawBody(c, b, t, run);

  // rings where soldiers just arrived (your plays at work)
  S.rings = S.rings.filter((r) => t - r.t0 < 0.8);
  for (const r of S.rings) {
    const k = (t - r.t0) / 0.8; c.strokeStyle = SIDE[r.side].light; c.globalAlpha = 1 - k; c.lineWidth = 2;
    c.beginPath(); c.ellipse(sx(r.x, r.y), sy(r.x, r.y), S.sz * (0.3 + 0.7 * k), S.sz * (0.15 + 0.35 * k), 0, 0, Math.PI * 2); c.stroke(); c.globalAlpha = 1;
  }
  // small damage numbers
  S.floats = S.floats.filter((f) => t - f.t0 < 0.75);
  c.font = '700 10px system-ui, sans-serif'; c.textAlign = 'center'; c.textBaseline = 'middle';
  for (const f of S.floats) {
    const k = (t - f.t0) / 0.75, x = sx(f.x, f.y), y = sy(f.x, f.y) - S.sz * 0.9 - k * 16;
    c.globalAlpha = 1 - k; c.strokeStyle = 'rgba(0,0,0,0.8)'; c.lineWidth = 3; c.strokeText(f.text, x, y); c.fillStyle = '#fff4c2'; c.fillText(f.text, x, y); c.globalAlpha = 1;
  }
  drawMenu(c);
  drawMute(c);
}

// ---- public ---------------------------------------------------------------------------------------------------------------------------

export async function init(canvas, ref, layout, imgBase) {
  dispose();
  const L0 = typeof layout === 'string' ? JSON.parse(layout) : layout;
  if (Array.isArray(L0.teams) && L0.teams.length === 2) for (let i = 0; i < 2; i++) SIDE[i] = { ...SIDE[i], ...L0.teams[i] };   // the page's two team colours win over the defaults
  S = {
    canvas, ctx: canvas.getContext('2d'), ref, L: L0, imgs: {}, sprites: makeSprites(), terrain: null, vertical: false, cssW: 0, cssH: 0, dpr: 1, u: 1, v: 1, sz: 30,
    bodies: new Map(), corpses: [], floats: [], rings: [], head: { tick: 0, stage: 0, status: 0, pBase: 1, eBase: 1, alpha: 1 }, info: null, selected: -1, lastLeft: -1,
    raf: 0, busy: false, last: 0, lastCall: 0, ready: false, stageAt: 0, ro: null, onTap: null, onResize: null,
  };
  const names = ['grass1', 'grass2', 'stone', 'bush', 'pine', 'keep', 'castle'];
  const loaded = await Promise.all(names.map((n) => loadImg(imgBase + n + '.png')));
  names.forEach((n, i) => { S.imgs[n] = loaded[i]; });
  fit();
  // a click (a finished tap, so a swipe that scrolls the page picks nothing)
  S.onTap = (e) => {
    const r = S.canvas.getBoundingClientRect();
    tapAt(e.clientX - r.left, e.clientY - r.top);
  };
  canvas.addEventListener('click', S.onTap);
  loadMute();
  listenForGesture();
  S.onResize = () => { if (S) fit(); };
  window.addEventListener('resize', S.onResize);
  if (typeof ResizeObserver !== 'undefined' && canvas.parentElement) { S.ro = new ResizeObserver(S.onResize); S.ro.observe(canvas.parentElement); }
  const loop = (now) => {
    if (!S) return;
    S.raf = requestAnimationFrame(loop);
    draw(now / 1000);
    if (!S.busy) {
      S.busy = true;
      const dt = S.lastCall ? Math.min(0.25, (now - S.lastCall) / 1000) : 0;
      S.lastCall = now;
      S.ref.invokeMethodAsync('Frame', dt).then((bytes) => { if (S && bytes) applyPacket(bytes, now); if (S) S.busy = false; }, () => { if (S) S.busy = false; });
    }
  };
  S.raf = requestAnimationFrame(loop);
}

/** What the page knows that the packet does not: the selected lane, each side's current order per lane, and what you have staged. */
export function setInfo(info) {
  if (!S) return;
  if (typeof info === 'string') info = JSON.parse(info);
  S.info = info;
  if (typeof info.selected === 'number') S.selected = info.selected;
  // the last seconds of the pause tick, and the last one sounds different
  if (info.human && info.stage === 0 && typeof info.left === 'number') {
    if (info.left !== S.lastLeft) {
      S.lastLeft = info.left;
      if (info.left >= 1 && info.left <= 5) sfxPlay(info.left === 1 ? 'tickLast' : 'tick', clockMs());
    }
  } else S.lastLeft = -1;
}

/** A sound the page asks for (staging a pack, a refused tap). Silent before the first tap or when muted. */
export function sfx(name) { sfxPlay(name, clockMs()); }

/** One picture of each soldier in your colour, as data URLs, for the buttons. */
export function icons() {
  if (!S) return [];
  return S.sprites.out[0].map((cv) => cv.toDataURL('image/png'));
}

export function dispose() {
  if (!S) return;
  if (S.raf) cancelAnimationFrame(S.raf);
  if (S.canvas && S.onTap) S.canvas.removeEventListener('click', S.onTap);
  if (S.onResize) window.removeEventListener('resize', S.onResize);
  if (S.ro) S.ro.disconnect();
  stopListeningForGesture();
  if (AUD.ctx) { try { AUD.ctx.close(); } catch (e) { } }
  AUD.ctx = null; AUD.master = null; AUD.pulse = null; AUD.noise = null; AUD.last = {}; AUD.tick = -1; AUD.count = 0; AUD.winStart = 0; AUD.winCount = 0; AUD.played = 0;
  S = null;
}

// for the offline smoke test only
export const _test = {
  teams: () => SIDE.map((s) => ({ ...s })), outline: OUTLINE,
  applyPacket: (u8, ms) => applyPacket(u8, ms), draw: (t) => draw(t), state: () => S,
  tap: (x, y) => tapAt(x, y), hit: (x, y) => hitAt(x, y), menu: (lane) => menuBoxes(lane), muteBox: () => muteBox(),
  sfx: { table: SFX, audio: AUD, play: sfxPlay, batch: sfxBatch, unlock, gesture: onGesture, setMuted, maxPerTick: MAX_PER_TICK, maxPerSecond: MAX_PER_SECOND },
};
