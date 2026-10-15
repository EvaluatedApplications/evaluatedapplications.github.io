#!/usr/bin/env node
// Label declutter check for the home scene: at 8 turn angles, on desktop and phone, with nothing selected and with each
// product selected (which shows its tools' labels), no two labels may overlap by more than a small share of the smaller
// one, and every label must lie inside the stage.   node web/scripts/verify-labels.mjs
import { OUT } from './lib/data.mjs';
import { serve, launch } from './lib/cdp.mjs';

const MAX_OVERLAP = 0.12;
const { server, port } = await serve(OUT);
const b = await launch();
let worst = 0, bad = 0, checks = 0;
try {
  for (const [name, opt] of [['desktop 1280', { width: 1280, height: 900 }], ['phone 390', { width: 390, height: 844, mobile: true }]]) {
    await b.goto(`http://127.0.0.1:${port}/`, { ...opt, wait: "document.querySelector('[data-diagram=stack-map]').dataset.ready==='true'" });
    await b.evaluate("document.querySelector('[data-ctl=play]').click()");   // pause the spin so the angle is the slider's
    const ids = await b.evaluate("[...document.querySelectorAll('.stack-chips .chip')].map(c=>c.dataset.node)");
    for (let a = 0; a < 8; a++) {
      const yaw = -180 + a * 45;
      for (const sel of ids) {
        const rects = await b.evaluate(`(()=>{const r=document.querySelector('[data-diagram=stack-map]');const y=r.querySelector('[data-ctl=yaw]');y.value=${yaw};y.dispatchEvent(new Event('input'));
          const c=r.querySelector('[data-node="${sel}"]');c.click();const st=r.querySelector('.dia-stage').getBoundingClientRect();return {st:{w:st.width,h:st.height},l:r._labels()}})()`);
        const L = rects.l; checks++;
        for (const l of L) if (l.x0 < -0.5 || l.y0 < -0.5 || l.x1 > rects.st.w + 0.5 || l.y1 > rects.st.h + 0.5) { bad++; console.log(`OUT  ${name} yaw ${yaw} sel "${sel}": ${l.id}`); }
        for (let i = 0; i < L.length; i++) for (let j = i + 1; j < L.length; j++) {
          const p = L[i], q = L[j];
          const ix = Math.min(p.x1, q.x1) - Math.max(p.x0, q.x0), iy = Math.min(p.y1, q.y1) - Math.max(p.y0, q.y0);
          if (ix > 0 && iy > 0) {
            const ratio = (ix * iy) / Math.min((p.x1 - p.x0) * (p.y1 - p.y0), (q.x1 - q.x0) * (q.y1 - q.y0));
            worst = Math.max(worst, ratio);
            if (ratio > MAX_OVERLAP) { bad++; console.log(`OVERLAP ${ratio.toFixed(2)}  ${name} yaw ${yaw} sel "${sel}": ${p.id} x ${q.id}`); }
          }
        }
      }
    }
    console.log(`${name}: checked ${checks} frames so far`);
  }
  // ---- the Pleroma pyramid: face names stay readable at every turn, the legend follows the colour mode ----
  for (const [name, opt] of [['desktop 1280', { width: 1280, height: 900 }], ['phone 390', { width: 390, height: 844, mobile: true }]]) {
    await b.goto(`http://127.0.0.1:${port}/phasor/pleroma/`, { ...opt, wait: "document.querySelector('[data-diagram=pleroma-map]').dataset.ready==='true'" });
    await b.evaluate("document.querySelector('[data-diagram=pleroma-map] [data-ctl=play]').click()");
    for (const mode of ['face', 'status']) {
      await b.evaluate(`document.querySelector('[data-color=${mode}]').click()`);
      const legend = await b.evaluate("[...document.querySelectorAll('[data-legend]')].filter(l=>!l.hidden).map(l=>l.dataset.legend+':'+l.textContent).join('|')");
      checks++;
      if (mode === 'face' && !(/face:/.test(legend) && /Syzygy/.test(legend) && /Monad/.test(legend) && /Ouroboros/.test(legend))) { bad++; console.log(`LEGEND ${name}: face legend missing a face: ${legend}`); }
      if (mode === 'status' && !(/status:/.test(legend) && /Checked/.test(legend) && /Read-time/.test(legend) && !/face:/.test(legend))) { bad++; console.log(`LEGEND ${name}: status legend wrong: ${legend}`); }
      for (let a = 0; a < 8; a++) {
        const yaw = -180 + a * 45;
        const res = await b.evaluate(`(()=>{const r=document.querySelector('[data-diagram=pleroma-map]');const y=r.querySelector('[data-ctl=yaw]');y.value=${yaw};y.dispatchEvent(new Event('input'));const st=r.querySelector('.dia-stage').getBoundingClientRect();return {st:{w:st.width,h:st.height},l:r._labels()}})()`);
        checks++;
        const keys = res.l.filter((x) => x.id.startsWith('key:')).length;
        if (keys !== 3) { bad++; console.log(`FACES ${name} ${mode} yaw ${yaw}: ${keys} of 3 face names listed`); }
        for (const l of res.l) if (l.x0 < -0.5 || l.x1 > res.st.w + 0.5 || l.y0 < -0.5 || l.y1 > res.st.h + 0.5) { bad++; console.log(`OUT  ${name} ${mode} yaw ${yaw}: ${l.id}`); }
        const L = res.l;
        for (let i = 0; i < L.length; i++) for (let j = i + 1; j < L.length; j++) {
          const p = L[i], q = L[j], ix = Math.min(p.x1, q.x1) - Math.max(p.x0, q.x0), iy = Math.min(p.y1, q.y1) - Math.max(p.y0, q.y0);
          if (ix > 0 && iy > 0) { const ratio = (ix * iy) / Math.min((p.x1 - p.x0) * (p.y1 - p.y0), (q.x1 - q.x0) * (q.y1 - q.y0)); worst = Math.max(worst, ratio); if (ratio > MAX_OVERLAP) { bad++; console.log(`OVERLAP ${ratio.toFixed(2)} ${name} ${mode} yaw ${yaw}: ${p.id} x ${q.id}`); } }
        }
      }
    }
  }
} finally { b.close(); server.close(); }
console.log(`labels: ${checks} frames (8 angles x selections x 2 viewports), worst overlap ${(worst * 100).toFixed(1)}% of the smaller label, ${bad} violation(s)`);
process.exitCode = bad ? 1 : 0;
