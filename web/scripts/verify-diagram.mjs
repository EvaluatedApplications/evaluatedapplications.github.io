#!/usr/bin/env node
// Browser checks for the Tracer diagram: keyboard operation, reduced motion, no console errors, no overflow.
//   node web/scripts/verify-diagram.mjs
import { OUT } from './lib/data.mjs';
import { serve, launch } from './lib/cdp.mjs';

const { server, port } = await serve(OUT);
const b = await launch();
const results = [];
const ok = (name, cond, detail = '') => { results.push(cond); console.log(`${cond ? 'PASS' : 'FAIL'}  ${name}${detail ? '  ' + detail : ''}`); };
const url = `http://127.0.0.1:${port}/tracer/`;
const READY = "document.querySelector('[data-diagram]').dataset.ready==='true'";
try {
  // ---- normal motion, desktop ----
  await b.goto(url, { width: 1280, height: 900, wait: READY });
  ok('diagram mounts without console errors', (await b.evaluate(READY)) && b.errors.length === 0, JSON.stringify(b.errors));
  ok('controls are revealed and the static plan is replaced by a canvas', await b.evaluate("!document.querySelector('.dia-controls').hidden && !!document.querySelector('.dia-canvas') && !document.querySelector('.dia-plan')"));
  ok('animation is playing by default (button says Pause)', await b.evaluate("document.querySelector('[data-ctl=play]').textContent.trim()==='Pause motion'"));

  // tabs: focus the first tab and use the arrow keys
  await b.evaluate("document.querySelector('[role=tab][aria-selected=true]').focus()");
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('Right arrow moves to the next view and selects it', await b.evaluate("document.activeElement.dataset.mode==='blocked' && document.activeElement.getAttribute('aria-selected')==='true'"));
  ok('readout (live region) describes the new view', await b.evaluate("/doorway/i.test(document.querySelector('[data-ctl=readout]').textContent) && document.querySelector('[data-ctl=readout]').getAttribute('role')==='status'"));
  await b.key('End', 'End', 35);
  ok('End selects the last view (tile lane) and disables the agent picker', await b.evaluate("document.activeElement.dataset.mode==='tile' && document.querySelector('[data-ctl=agent]').disabled"));
  await b.key('Home', 'Home', 36);
  ok('Home returns to the swarm view', await b.evaluate("document.activeElement.dataset.mode==='swarm'"));

  // stage keyboard: arrows turn and tilt, sliders follow
  const before = await b.evaluate("[document.querySelector('[data-ctl=yaw]').value, document.querySelector('[data-ctl=tilt]').value].join(',')");
  await b.evaluate("document.querySelector('.dia-stage').focus()");
  await b.key('ArrowRight', 'ArrowRight', 39); await b.key('ArrowUp', 'ArrowUp', 38);
  const after = await b.evaluate("[document.querySelector('[data-ctl=yaw]').value, document.querySelector('[data-ctl=tilt]').value].join(',')");
  ok('arrow keys on the focused stage change turn and tilt', before !== after, `${before} -> ${after}`);

  // sliders are native inputs (keyboard operable) and the focus select updates the readout
  await b.evaluate("(()=>{const s=document.querySelector('[data-ctl=agent]'); s.value='4'; s.dispatchEvent(new Event('change'));})()");
  ok('choosing an agent updates the readout', await b.evaluate("/Agent 5:/.test(document.querySelector('[data-ctl=readout]').textContent)"));

  // pause toggles
  await b.evaluate("document.querySelector('[data-ctl=play]').click()");
  ok('Pause motion stops the animation', await b.evaluate("document.querySelector('[data-ctl=play]').getAttribute('aria-pressed')==='false'"));

  // canvas actually draws different frames while playing
  await b.evaluate("document.querySelector('[data-ctl=play]').click()");
  const f1 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().length + ':' + document.querySelector('.dia-canvas').toDataURL().slice(5000,5200)");
  await new Promise((r) => setTimeout(r, 900));
  const f2 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().length + ':' + document.querySelector('.dia-canvas').toDataURL().slice(5000,5200)");
  ok('canvas frames change over time while playing', f1 !== f2);
  ok('data table alternative is present with captions and scoped headers', await b.evaluate("document.querySelectorAll('.dia-data table caption').length===3 && document.querySelectorAll('.dia-data th[scope=col]').length>10"));

  // ---- reduced motion ----
  await b.goto(url, { width: 1280, height: 900, reducedMotion: true, wait: READY });
  ok('reduced motion: not playing on load', await b.evaluate("document.querySelector('[data-ctl=play]').getAttribute('aria-pressed')==='false' && document.querySelector('[data-ctl=play]').textContent.trim()==='Play motion'"));
  const r1 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(3000,3300)");
  await new Promise((r) => setTimeout(r, 1200));
  const r2 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(3000,3300)");
  ok('reduced motion: canvas is static (no sway, no moving agents)', r1 === r2);
  ok('reduced motion: CSS transitions and animations are off', await b.evaluate("getComputedStyle(document.querySelector('.card') || document.body).transitionDuration==='0s'"));
  await b.evaluate("document.querySelector('[data-ctl=play]').click()");
  const r3a = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(3000,3300)");
  await new Promise((r) => setTimeout(r, 900));
  const r3b = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(3000,3300)");
  ok('reduced motion: motion starts only when the visitor presses Play', r3a !== r3b);

  // ---- phone ----
  await b.goto(url, { width: 390, height: 844, mobile: true, wait: READY });
  const w = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('phone 390: no horizontal overflow', w.sw <= w.cw, JSON.stringify(w));
  ok('phone 390: stage is taller than wide (portrait layout)', await b.evaluate("(()=>{const r=document.querySelector('.dia-stage').getBoundingClientRect();return r.height>r.width})()"));
  ok('phone 390: touch targets in the diagram are at least 24px tall', await b.evaluate("[...document.querySelectorAll('.dia-tabs [role=tab], .dia-controls button, .dia-controls select')].every(e=>e.getBoundingClientRect().height>=24)"));
  ok('no console errors on phone', b.errors.length === 0, JSON.stringify(b.errors));

  // ---- no-JS fallback: static plan and table present in markup ----
  const html = await (await fetch(url)).text();
  ok('without JS the page still carries a plan (svg role=img) and the data tables', /<svg class="dia-plan" [^>]*role="img"/.test(html) && /<details class="dia-data">/.test(html));
  // ================= home: the stack scene =================
  const home = `http://127.0.0.1:${port}/`;
  const HREADY = "document.querySelector('[data-diagram=stack-map]').dataset.ready==='true'";
  await b.goto(home, { width: 1280, height: 900, wait: HREADY });
  ok('home: scene mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('home: scene is above the fold at 1280x900', await b.evaluate("document.querySelector('.dia-canvas').getBoundingClientRect().top < 400"));
  await b.evaluate("document.querySelector('[data-node=algformer]').click()");
  ok('home: choosing a product fills the readout from the data and links to its page', await b.evaluate("/Builds on EvalApp and Phasor/.test(document.querySelector('[data-ctl=text]').textContent) && !document.querySelector('[data-ctl=link]').hidden"));
  await b.evaluate("document.querySelector('.dia-stage').focus()");
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('home: arrow keys on the focused scene move to the next product', await b.evaluate("document.querySelector('[data-node=prism]').getAttribute('aria-pressed')==='true'"));
  await b.key('Escape', 'Escape', 27);
  ok('home: Escape returns to the overview', await b.evaluate("document.querySelector('[data-node=\"\"]').getAttribute('aria-pressed')==='true'"));
  const h1 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(4000,4300)");
  await new Promise((r) => setTimeout(r, 900));
  const h2 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(4000,4300)");
  ok('home: scene rotates and pulses while playing', h1 !== h2);
  ok('home: table alternative lists every product', await b.evaluate("document.querySelectorAll('.dia-data tbody tr').length===document.querySelectorAll('.stack-chips .chip').length-1"));
  await b.goto(home, { width: 1280, height: 900, reducedMotion: true, wait: HREADY });
  const hr1 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(4000,4300)");
  await new Promise((r) => setTimeout(r, 1200));
  const hr2 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(4000,4300)");
  ok('home: reduced motion keeps the scene still until Play is pressed', hr1 === hr2 && (await b.evaluate("document.querySelector('[data-ctl=play]').getAttribute('aria-pressed')==='false'")));
  await b.goto(home, { width: 390, height: 844, mobile: true, wait: HREADY });
  const hw = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('home phone 390: no horizontal overflow', hw.sw <= hw.cw, JSON.stringify(hw));
  ok('home phone 390: chips scroll inside their row and are at least 24px tall', await b.evaluate("[...document.querySelectorAll('.stack-chips .chip')].every(e=>e.getBoundingClientRect().height>=24)"));

  // ================= phasor: the map, the replay, the capacity demo =================
  const ph = `http://127.0.0.1:${port}/phasor/`, pl = `http://127.0.0.1:${port}/phasor/pleroma/`;
  const MREADY = "document.querySelector('[data-diagram=pleroma-map]').dataset.ready==='true'";
  await b.goto(ph, { width: 1280, height: 900, wait: MREADY });
  ok('phasor: map mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  await b.evaluate("document.querySelector('.dia-stage').focus()");
  await b.key('Home', 'Home', 36);
  ok('phasor: Home selects the first cell and fills the panel', await b.evaluate("/Signed insertion and deletion/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('ArrowUp', 'ArrowUp', 38);
  ok('phasor: Up moves one rung up on the same face', await b.evaluate("/Binary choice|Rung 2/.test(document.querySelector('[data-ctl=panel]').textContent) || /Key on the cycle/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('phasor: Right moves to the next face', await b.evaluate("/Monad|One-hot/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('End', 'End', 35);
  ok('phasor: End selects the lid, with its read-time status', await b.evaluate("/READ-TIME/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('Escape', 'Escape', 27);
  ok('phasor: Escape clears the selection', await b.evaluate("/Pick a cell/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.evaluate("document.querySelector('[data-color=status]').click()");
  ok('phasor: colour by status toggles', await b.evaluate("document.querySelector('[data-color=status]').getAttribute('aria-pressed')==='true'"));
  ok('phasor: all cells are in the table alternative', await b.evaluate("document.querySelectorAll('.dia-data tbody tr').length===25"));
  await b.goto(ph, { width: 1280, height: 900, reducedMotion: true, wait: MREADY });
  const m1 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(4000,4300)");
  await new Promise((r) => setTimeout(r, 1200));
  const m2 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(4000,4300)");
  ok('phasor: reduced motion keeps the pyramid still', m1 === m2);
  await b.goto(ph, { width: 390, height: 844, mobile: true, wait: MREADY });
  const pw = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('phasor phone 390: no horizontal overflow', pw.sw <= pw.cw, JSON.stringify(pw));

  const SREADY = "document.querySelector('[data-diagram=pleroma-superposition]').dataset.ready==='true'";
  await b.goto(pl, { width: 1280, height: 900, reducedMotion: true, wait: SREADY });
  ok('pleroma: reduced motion shows the final state and does not autoplay', await b.evaluate("document.querySelector('[data-diagram=pleroma-superposition]').dataset.state==='after'"));
  await new Promise((r) => setTimeout(r, 1500));
  ok('pleroma: replay does not start by itself under reduced motion', await b.evaluate("document.querySelector('[data-diagram=pleroma-superposition]').dataset.state==='after' && document.querySelectorAll('.sp-item.in').length===3"));
  await b.evaluate("document.querySelector('[data-state=before]').click()");
  ok('pleroma: Before the pass shows the original counts', await b.evaluate("document.querySelector('[data-diagram=pleroma-superposition]').dataset.state==='before' && /Total count 6/.test(document.querySelector('[data-diagram=pleroma-superposition] [data-ctl=readout]').textContent)"));
  await b.evaluate("document.querySelector('[data-state=after]').click()");
  ok('pleroma: After the pass shows the final total', await b.evaluate("/= 18/.test(document.querySelector('[data-diagram=pleroma-superposition] [data-ctl=readout]').textContent)"));
  await b.evaluate("document.querySelector('[data-ctl=replay]').click()");
  await new Promise((r) => setTimeout(r, 10000));
    ok('pleroma: Replay runs to the end state', await b.evaluate("document.querySelector('[data-diagram=pleroma-superposition]').dataset.state==='after'"));
  // capacity demo
  for (let i = 0; i < 2; i++) await b.evaluate("document.querySelector('[data-diagram=pleroma-capacity] [data-ctl=add]').click()");
  ok('pleroma: 5 keys is past capacity and TryKeys refuses', await b.evaluate("/returns false/.test(document.querySelector('[data-diagram=pleroma-capacity] [data-ctl=verdict]').textContent) && document.querySelector('.cap-box').dataset.over==='1'"));
  await b.evaluate("document.querySelector('[data-diagram=pleroma-capacity] [data-ctl=remove]').click()");
  ok('pleroma: 4 keys is exact', await b.evaluate("/decoded exactly \\(4 of K = 4\\)/.test(document.querySelector('[data-diagram=pleroma-capacity] [data-ctl=verdict]').textContent)"));
  ok('pleroma: no console errors', b.errors.length === 0, JSON.stringify(b.errors));
  await b.goto(pl, { width: 390, height: 844, mobile: true, wait: SREADY });
  const lw = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('pleroma phone 390: no horizontal overflow', lw.sw <= lw.cw, JSON.stringify(lw));
  const dw = `http://127.0.0.1:${port}/phasor/docs/`;
  await b.goto(dw, { width: 390, height: 844, mobile: true });
  const dd = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('phasor docs phone 390: no horizontal overflow', dd.sw <= dd.cw, JSON.stringify(dd));
  // ================= holodb: the store scene and the evidence bars =================
  const hd = `http://127.0.0.1:${port}/holodb/`, he = `http://127.0.0.1:${port}/holodb/evidence/`;
  const DREADY = "document.querySelector('[data-diagram=holodb-store]').dataset.ready==='true'";
  await b.goto(hd, { width: 1280, height: 900, wait: DREADY });
  ok('holodb: scene mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('holodb: key lookup reports zero rows scanned from the real meter', await b.evaluate("/Rows scanned: 0 of 24/.test(document.querySelector('[data-ctl=panel]').textContent) && /primary-key point lookup/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.evaluate("document.querySelector('[role=tab][aria-selected=true]').focus()");
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('holodb: arrow keys switch to By content with the NEAREST route and scores', await b.evaluate("document.activeElement.dataset.mode==='content' && /NEAREST similarity search/.test(document.querySelector('[data-ctl=panel]').textContent) && /score 0\\.6/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('holodb: By aggregate shows maintained accumulators, zero rows scanned and the totals', await b.evaluate("/maintained accumulators/.test(document.querySelector('[data-ctl=panel]').textContent) && /Rows scanned: 0 of 24/.test(document.querySelector('[data-ctl=panel]').textContent) && /east 89,600/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  ok('holodb: aggregate mode shows the latency bars from the evidence record', await b.evaluate("!!document.querySelector('[data-ctl=latbody] svg')"));
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('holodb: Optional plane lists the six queries', await b.evaluate("!document.querySelector('[data-ctl=qlist]').hidden && document.querySelectorAll('[data-q]').length===6"));
  await b.evaluate("document.querySelector('[data-q=\"3\"]').click()");
  ok('holodb: a declined query says the engine answers and why', await b.evaluate("/Declined/.test(document.querySelector('[data-ctl=panel]').textContent) && /not a plane dimension/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  ok('holodb: table alternative holds the rows, the queries and the plane routes', await b.evaluate("document.querySelectorAll('.dia-data table').length===3 && document.querySelectorAll('.dia-data tbody tr').length===24+3+6"));
  await b.goto(hd, { width: 1280, height: 900, reducedMotion: true, wait: DREADY });
  const d1 = await b.evaluate("document.querySelector('[data-diagram=holodb-store] .dia-canvas').toDataURL().slice(4000,4300)");
  await new Promise((r) => setTimeout(r, 1200));
  const d2 = await b.evaluate("document.querySelector('[data-diagram=holodb-store] .dia-canvas').toDataURL().slice(4000,4300)");
  ok('holodb: reduced motion keeps the scene still', d1 === d2);
  await b.goto(hd, { width: 390, height: 844, mobile: true, wait: DREADY });
  const dw2 = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('holodb phone 390: no horizontal overflow', dw2.sw <= dw2.cw, JSON.stringify(dw2));
  const BREADY = "document.querySelectorAll('[data-diagram=latency-bars]')[0].dataset.ready==='true'";
  await b.goto(he, { width: 1280, height: 900, wait: BREADY });
  ok('evidence: bars mount without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('evidence: default readout states wins, ties and losses from the data', await b.evaluate("/faster on 13 of 16 queries, tied on 2 and slower on 1/.test(document.querySelector('[data-diagram=latency-bars] [data-ctl=readout]').textContent)"));
  await b.evaluate("document.querySelector('[data-diagram=latency-bars] [data-rival=\"0\"]').click()");
  ok('evidence: comparing with SQLite changes the summary', await b.evaluate("/against SQLite: faster on 15 of 16/.test(document.querySelector('[data-diagram=latency-bars] [data-ctl=readout]').textContent)"));
  await b.evaluate("document.querySelector('[data-diagram=latency-bars] [data-order=gap]').click()");
  ok('evidence: ordering by gap puts the biggest speed-up first', await b.evaluate("(()=>{const g=document.querySelector('[data-diagram=latency-bars] .lb-row text.lab');return /invoices by status|revenue by region|total invoiced|revenue by month/.test(g.textContent)})()"));
  await b.evaluate("document.querySelector('[data-diagram=latency-bars] .dia-stage').focus()");
  await b.key('Home', 'Home', 36);
  ok('evidence: Home reads the first row from the keyboard', await b.evaluate("/ms\\./.test(document.querySelector('[data-diagram=latency-bars] [data-ctl=readout]').textContent) && !!document.querySelector('[data-diagram=latency-bars] .lb-row.on')"));
  ok('evidence: every chart has its table beside it', await b.evaluate("[...document.querySelectorAll('[data-diagram=latency-bars]')].every(f=>f.querySelector('table'))"));
  await b.goto(he, { width: 390, height: 844, mobile: true, wait: BREADY });
  const ew = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('evidence phone 390: no horizontal overflow', ew.sw <= ew.cw, JSON.stringify(ew));
  ok('evidence phone 390: labels sit above the bars', await b.evaluate("document.querySelector('[data-diagram=latency-bars] .dia-stage').classList.contains('lb-narrow')"));
  for (const sp of ['docs/', 'pleroma/', 'client/']) {
    await b.goto(`http://127.0.0.1:${port}/holodb/${sp}`, { width: 390, height: 844, mobile: true });
    const sw = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
    ok(`holodb/${sp} phone 390: no horizontal overflow`, sw.sw <= sw.cw, JSON.stringify(sw));
  }

  // ================= batch 4: EvalApp gate, AlgFormer cost, HoloVoxel LOD, Prose pipeline =================
  const base = `http://127.0.0.1:${port}`;
  const overflow = async (p, w = 390) => { await b.goto(base + p, { width: w, height: 844, mobile: w < 600 }); return b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})'); };
  const EA = "document.querySelector('[data-diagram=evalapp-gate]').dataset.ready==='true'";
  await b.goto(base + '/evalapp/', { width: 1280, height: 900, wait: EA });
  ok('evalapp: gate scene mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('evalapp: one tab per recorded gate bound, the default selected', await b.evaluate("document.querySelectorAll('[data-diagram=evalapp-gate] [role=tab]').length>=5 && document.querySelectorAll('[data-diagram=evalapp-gate] [role=tab][aria-selected=true]').length===1"));
  await b.evaluate("document.querySelector('[data-diagram=evalapp-gate] [role=tab][aria-selected=true]').focus()");
  const t0 = await b.evaluate("document.activeElement.textContent");
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('evalapp: Right arrow selects the next gate bound', await b.evaluate("document.activeElement.textContent") !== t0 && await b.evaluate("document.activeElement.getAttribute('aria-selected')==='true'"));
  await new Promise((r) => setTimeout(r, 7500));
  ok('evalapp: the replay finishes and the readout reports the whole run', await b.evaluate("/all \\d+ jobs finished in [\\d,]+ ms/.test(document.querySelector('[data-diagram=evalapp-gate] [data-ctl=readout]').textContent)"));
  await b.evaluate("(()=>{const s=document.querySelector('[data-diagram=evalapp-gate] [data-ctl=time]'); s.value='300'; s.dispatchEvent(new Event('input'));})()");
  ok('evalapp: scrubbing to the middle of the run counts running, waiting and finished jobs that add up', await b.evaluate("(()=>{const m=document.querySelector('[data-diagram=evalapp-gate] [data-ctl=readout]').textContent.match(/(\\d+) running, (\\d+) waiting at the gate, (\\d+) finished/); return !!m && Number(m[1])+Number(m[2])+Number(m[3])===24})()"));
  ok('evalapp: the declared bound is never exceeded in the scrubbed state', await b.evaluate("(()=>{const m=document.querySelector('[data-diagram=evalapp-gate] [data-ctl=readout]').textContent.match(/Gate bound (\\d+)[^:]*: (\\d+) running/); return !!m && Number(m[2])<=Number(m[1])})()"));
  ok('evalapp: table alternative holds the summary and per-job times', await b.evaluate("document.querySelectorAll('[data-diagram=evalapp-gate] .dia-data table').length===2 && document.querySelectorAll('[data-diagram=evalapp-gate] .dia-data tbody tr').length>=24"));
  await b.goto(base + '/evalapp/', { width: 1280, height: 900, reducedMotion: true, wait: EA });
  ok('evalapp: reduced motion shows the finished run and does not replay by itself', await b.evaluate("document.querySelector('[data-diagram=evalapp-gate] [data-ctl=time]').value==='1000'"));
  const g1 = await b.evaluate("document.querySelector('[data-diagram=evalapp-gate] .dia-canvas').toDataURL().slice(4000,4300)");
  await new Promise((r) => setTimeout(r, 1200));
  const g2 = await b.evaluate("document.querySelector('[data-diagram=evalapp-gate] .dia-canvas').toDataURL().slice(4000,4300)");
  ok('evalapp: reduced motion keeps the scene still', g1 === g2);
  await b.evaluate("document.querySelector('[data-diagram=evalapp-gate] [data-ctl=play]').click()");
  await new Promise((r) => setTimeout(r, 600));
  ok('evalapp: Replay starts motion only when the visitor presses it', await b.evaluate("document.querySelector('[data-diagram=evalapp-gate] [data-ctl=play]').getAttribute('aria-pressed')==='true' || document.querySelector('[data-diagram=evalapp-gate] [data-ctl=time]').value!=='1000'"));
  let ow = await overflow('/evalapp/');
  ok('evalapp phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));
  ow = await overflow('/evalapp/docs/');
  ok('evalapp/docs phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));

  const AC = "document.querySelector('[data-diagram=algformer-cost]').dataset.ready==='true'";
  await b.goto(base + '/algformer/', { width: 1280, height: 900, wait: AC });
  ok('algformer: chart mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('algformer: the static chart is replaced by the interactive one with four tabs', await b.evaluate("document.querySelectorAll('[data-diagram=algformer-cost] [role=tab]').length===4 && !!document.querySelector('[data-diagram=algformer-cost] .ac-svg')"));
  await b.evaluate("document.querySelector('[data-diagram=algformer-cost] [role=tab][aria-selected=true]').focus()");
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('algformer: Right arrow selects Each new token and the readout says so', await b.evaluate("document.activeElement.dataset.mode==='step' && /each further token takes/.test(document.querySelector('[data-diagram=algformer-cost] [data-ctl=readout]').textContent)"));
  await b.evaluate("(()=>{const s=document.querySelector('[data-diagram=algformer-cost] [data-ctl=pos]'); s.value='2'; s.dispatchEvent(new Event('input'));})()");
  ok('algformer: moving the length slider changes the readout to a shorter context', await b.evaluate("/At 256 tokens/.test(document.querySelector('[data-diagram=algformer-cost] [data-ctl=readout]').textContent)"));
  await b.evaluate("(()=>{const s=document.querySelector('[data-diagram=algformer-cost] [data-ctl=scale]'); s.value='log'; s.dispatchEvent(new Event('change'));})()");
  ok('algformer: the logarithmic scale is announced in the chart', await b.evaluate("/logarithmic scale on both axes/.test(document.querySelector('[data-diagram=algformer-cost] .ac-svg').textContent)"));
  await b.key('End', 'End', 35);
  await b.evaluate("document.querySelector('[data-diagram=algformer-cost] [role=tab][data-mode=capacity]').click()");
  ok('algformer: the capacity tab reports the derived shape and disables the scale', await b.evaluate("/HoloShape derives/.test(document.querySelector('[data-diagram=algformer-cost] [data-ctl=readout]').textContent) && document.querySelector('[data-diagram=algformer-cost] [data-ctl=scale]').disabled"));
  ok('algformer: table alternative holds the cost table and the capacity table', await b.evaluate("document.querySelectorAll('[data-diagram=algformer-cost] .dia-data table').length===2"));
  ow = await overflow('/algformer/');
  ok('algformer phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));
  ow = await overflow('/algformer/holoformer/');
  ok('algformer/holoformer phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));

  const HV = "document.querySelector('[data-diagram=holovoxel-lod]').dataset.ready==='true'";
  await b.goto(base + '/holovoxel/', { width: 1280, height: 900, wait: HV });
  ok('holovoxel: scene mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  const hv0 = await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=readout]').textContent");
  await b.evaluate("(()=>{const s=document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=viewer]'); s.value='0'; s.dispatchEvent(new Event('input'));})()");
  const hv1 = await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=readout]').textContent");
  ok('holovoxel: moving the viewer changes which chunks are read at which budget', hv0 !== hv1 && /Viewer at chunk 0\.0/.test(hv1));
  ok('holovoxel: at the end of the strip the far chunks use the coarsest level', await b.evaluate("/read at \\d+ components \\(\\d+ cells each/.test(document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=readout]').textContent) && document.querySelectorAll('[data-diagram=holovoxel-lod] .stat').length===4"));
  await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=bands]').click()");
  ok('holovoxel: detail bands can be switched off', await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=bands]').checked===false"));
  await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] .dia-stage').focus()");
  const v1 = await b.evaluate("[document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=yaw]').value, document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=tilt]').value].join(',')");
  await b.key('ArrowRight', 'ArrowRight', 39); await b.key('ArrowUp', 'ArrowUp', 38);
  ok('holovoxel: arrow keys on the focused scene turn and tilt it', v1 !== await b.evaluate("[document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=yaw]').value, document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=tilt]').value].join(',')"));
  ok('holovoxel: table alternative holds the levels, the ladder and the distances', await b.evaluate("document.querySelectorAll('[data-diagram=holovoxel-lod] .dia-data table').length===3"));
  await b.goto(base + '/holovoxel/', { width: 1280, height: 900, reducedMotion: true, wait: HV });
  const w1 = await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] .dia-canvas').toDataURL().slice(4000,4300)");
  await new Promise((r) => setTimeout(r, 1200));
  ok('holovoxel: reduced motion keeps the scene still and the walk off until pressed', w1 === await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] .dia-canvas').toDataURL().slice(4000,4300)") && await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=walk]').getAttribute('aria-pressed')==='false'"));
  await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=walk]').click()");
  await new Promise((r) => setTimeout(r, 900));
  ok('holovoxel: pressing Walk moves the viewer', await b.evaluate("document.querySelector('[data-diagram=holovoxel-lod] [data-ctl=viewer]').value!=='2'"));
  ow = await overflow('/holovoxel/');
  ok('holovoxel phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));

  const PP = "document.querySelector('[data-diagram=prose-pipeline]').dataset.ready==='true'";
  await b.goto(base + '/prose/', { width: 1280, height: 1000, wait: PP });
  ok('prose: pipeline mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('prose: four stages are rendered as labelled columns', await b.evaluate("document.querySelectorAll('[data-diagram=prose-pipeline] .pp-col').length===4 && [...document.querySelectorAll('[data-diagram=prose-pipeline] .pp-col h3')].map(h=>h.textContent.replace(/^\\d/,'')).join()==='Read,Parse,Mine,Recombine'"));
  await b.evaluate("document.querySelectorAll('[data-diagram=prose-pipeline] .pp-pick[data-s]')[1].click()");
  ok('prose: choosing a sentence shows its roles, including an indirect object', await b.evaluate("/indirect object man/.test(document.querySelector('[data-diagram=prose-pipeline] .pp-col:nth-of-type(2)').textContent) && !!document.querySelector('[data-diagram=prose-pipeline] .r-IndObject')"));
  await b.evaluate("document.querySelectorAll('[data-diagram=prose-pipeline] .pp-pick[data-g]')[3].focus(); document.querySelectorAll('[data-diagram=prose-pipeline] .pp-pick[data-g]')[3].click()");
  ok('prose: choosing a generated sentence traces it to table counts and keeps keyboard focus on it', await b.evaluate("/seen \\d+ time/.test(document.querySelector('[data-diagram=prose-pipeline] .pp-lin').textContent) && document.activeElement.dataset.g==='3' && /Validity gate: passed/.test(document.querySelector('[data-diagram=prose-pipeline] .pp-lin').textContent)"));
  await b.evaluate("document.querySelector('[data-diagram=prose-pipeline] [role=tab][data-mode=\"1\"]').click()");
  ok('prose: the panda tab shows shoots as a verb and, on the other sentence, as a noun', await b.evaluate("(()=>{const q=(n)=>{document.querySelectorAll('[data-diagram=prose-pipeline] .pp-pick[data-s]')[n].click(); return [...document.querySelectorAll('[data-diagram=prose-pipeline] .pp-col:nth-of-type(2) .pp-chip')].find(c=>c.querySelector('b').textContent==='shoots').querySelector('i').textContent}; return q(0)==='Verb' && q(1)==='Noun'})()"));
  ok('prose: table alternative holds parses, generated sentences, question pairs and the larger run', await b.evaluate("document.querySelectorAll('[data-diagram=prose-pipeline] .dia-data table').length===4"));
  ow = await overflow('/prose/');
  ok('prose phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));
  for (const p of ['/evalapp/evidence/', '/algformer/evidence/', '/holovoxel/evidence/', '/prose/evidence/']) {
    ow = await overflow(p);
    ok(`${p} phone 390: no horizontal overflow`, ow.sw <= ow.cw, JSON.stringify(ow));
  }

  // ================= batch 5: research diagrams =================
  const setRange = (sel, v, ev = 'input') => `(()=>{const s=document.querySelector(${JSON.stringify(sel)}); s.value=${JSON.stringify(String(v))}; s.dispatchEvent(new Event(${JSON.stringify(ev)}));})()`;
  const rd = async (slug, kind) => { await b.goto(`${base}/research/${slug}/`, { width: 1280, height: 900, wait: `document.querySelector('[data-diagram=${kind}]').dataset.ready==='true'` }); };
  const RO = (kind) => `document.querySelector('[data-diagram=${kind}] [data-ctl=readout]').textContent`;
  await rd('context-four', 'round-timeline');
  ok('research context-four: timeline mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  const rt0 = await b.evaluate(RO('round-timeline'));
  await b.evaluate(setRange('[data-diagram=round-timeline] [data-ctl=round]', 10000));
  ok('research context-four: moving the round changes which measures are flat', rt0 !== await b.evaluate(RO('round-timeline')) && /At round 10,000/.test(await b.evaluate(RO('round-timeline'))));
  ok('research context-four: table alternative lists every measure', await b.evaluate("document.querySelectorAll('[data-diagram=round-timeline] .dia-data tbody tr').length===5"));
  await rd('context-eight', 'slot-grow');
  await b.evaluate(setRange('[data-diagram=slot-grow] [data-ctl=grows]', 5));
  ok('research context-eight: appending leaves a new slot last and inserting keeps the original', await b.evaluate(RO('slot-grow')).then((t) => /After 5 grows/.test(t) && /new and has not been trained/.test(t) && /original one/.test(t)));
  await rd('back-to-front', 'layer-stack');
  ok('research back-to-front: the last read point weighs several tokens and earlier ones are copies', await b.evaluate(RO('layer-stack')).then((t) => /weighing several tokens/.test(t) && /exact copies/.test(t)));
  await b.evaluate("document.querySelector('[data-diagram=layer-stack] [role=tab][data-mode=passes]').click()");
  ok('research back-to-front: the passes tab reports passes doing work', await b.evaluate(RO('layer-stack')).then((t) => /passes carry real information/.test(t)));
  await rd('smaller-than-tinystories', 'size-bars');
  await b.evaluate(setRange('[data-diagram=size-bars] [data-ctl=pos]', 1));
  ok('research tinystories: a reference row reads as a multiple of the checkpoint', await b.evaluate(RO('size-bars')).then((t) => /times this checkpoint/.test(t)));
  await rd('nobody-read-the-warning', 'design-effect');
  await b.evaluate(setRange('[data-diagram=design-effect] [data-ctl=rho]', 1000));
  ok('research nobody-read: full correlation leaves about one independent judgement', await b.evaluate(RO('design-effect')).then((t) => /about 1\.00 independent judgements/.test(t)));
  for (const s of ['context-four', 'context-eight', 'back-to-front', 'smaller-than-tinystories', 'nobody-read-the-warning']) {
    ow = await overflow(`/research/${s}/`);
    ok(`research/${s} phone 390: no horizontal overflow`, ow.sw <= ow.cw, JSON.stringify(ow));
  }
  await b.goto(`${base}/research/`, { width: 1280, height: 900 });
  await b.evaluate("document.querySelector('[data-filter] [data-topic=layers]').click()");
  ok('research hub: the topic filter hides articles that do not match', await b.evaluate("[...document.querySelectorAll('.research-cards .card')].filter(c=>!c.hidden).length===1 && /1 of 5 articles about layers/.test(document.querySelector('[data-filter-count]').textContent)"));
} finally { b.close(); server.close(); }
process.exitCode = results.every(Boolean) ? 0 : 1;



