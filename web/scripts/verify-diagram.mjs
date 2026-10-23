#!/usr/bin/env node
// Browser checks for the Tracer diagram: keyboard operation, reduced motion, no console errors, no overflow.
//   node web/scripts/verify-diagram.mjs
import fs from 'node:fs';
import path from 'node:path';
import { OUT, DATA } from './lib/data.mjs';
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

  // ================= phasor overview: the superposition scene and the coverage meter =================
  const ph = `http://127.0.0.1:${port}/phasor/`, pl = `http://127.0.0.1:${port}/phasor/pleroma/`;
  const sceneD = JSON.parse(fs.readFileSync(path.join(DATA, 'diagrams', 'superposition-scene.json'), 'utf8'));
  const covD = JSON.parse(fs.readFileSync(path.join(DATA, 'diagrams', 'coverage-meter.json'), 'utf8'));
  const mapD = JSON.parse(fs.readFileSync(path.join(DATA, 'diagrams', 'pleroma-map.json'), 'utf8'));
  const SC = "[data-diagram=superposition-scene]", CM = "[data-diagram=coverage-meter]";
  const PREADY = `document.querySelector('${SC}').dataset.ready==='true' && document.querySelector('${CM}').dataset.ready==='true'`;
  await b.goto(ph, { width: 1280, height: 900, reducedMotion: true, wait: PREADY });
  ok('phasor: scene and meter mount without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('phasor: reduced motion shows the final state and does not autoplay', await b.evaluate(`document.querySelector('${SC}').dataset.view==='after'`));
  await new Promise((r) => setTimeout(r, 1200));
  ok('phasor: the scene is still on its final state after a wait under reduced motion', await b.evaluate(`document.querySelector('${SC}').dataset.view==='after'`));
  ok('phasor: the final total is the recorded total', await b.evaluate(`document.querySelector('${SC} [data-ctl=readout]').textContent.includes('to ${sceneD.result.totalCount} ')`));
  for (const [tab, re] of [['before', 'add to ' + sceneD.store.totalCount], ['kernel', 'one kernel'], ['cost', String(sceneD.cost.applyMs[0])]]) {
    await b.evaluate(`document.querySelector('${SC} [data-mode=${tab}]').click()`);
    ok(`phasor: tab ${tab} selects and the readout follows`, await b.evaluate(`document.querySelector('${SC}').dataset.view==='${tab}' && document.querySelector('${SC} [data-mode=${tab}]').getAttribute('aria-selected')==='true' && document.querySelector('${SC} [data-ctl=readout]').textContent.includes(${JSON.stringify(re)})`));
  }
  ok('phasor: the cost view states its conditions on the chart', await b.evaluate(`document.querySelector('${SC} svg').textContent.includes('GF(13,879)') && document.querySelector('${SC} svg').textContent.includes('warm-up')`));
  await b.evaluate(`document.querySelector('${SC} [data-mode=after]').focus()`);
  await b.key('ArrowLeft', 'ArrowLeft', 37);
  ok('phasor: arrow keys move between the tabs', await b.evaluate(`document.activeElement.dataset.mode==='kernel'`));
  await b.evaluate(`document.querySelector('${SC} [data-ctl=replay]').click()`);
  ok('phasor: Replay under reduced motion lands on the final state', await b.evaluate(`document.querySelector('${SC}').dataset.view==='after'`));
  // normal motion: Replay runs the sweep and ends on the final state
  await b.goto(ph, { width: 1280, height: 900, wait: PREADY });
  await b.evaluate(`document.querySelector('${SC} [data-ctl=replay]').click()`);
  await new Promise((r) => setTimeout(r, 600));
  ok('phasor: Replay shows the sweep part-way', await b.evaluate(`document.querySelector('${SC}').dataset.view==='sweep'`));
  await new Promise((r) => setTimeout(r, 3600));
  ok('phasor: Replay ends on the final state', await b.evaluate(`document.querySelector('${SC}').dataset.view==='after'`));
  // the coverage meter
  const cs = covD.summary;
  ok('phasor: the meter headline is the record\'s text', await b.evaluate(`document.querySelector('${CM} .cm-head').textContent.trim()===${JSON.stringify(covD.headline.text)}`));
  ok('phasor: one square per operator', await b.evaluate(`document.querySelectorAll('${CM} .cm-sq').length===${cs.operatorCount}`));
  ok('phasor: kernel squares match the recorded count', await b.evaluate(`document.querySelectorAll('${CM} .cm-sq.cm-kernel').length===${cs.kernelCount}`));
  ok('phasor: the reads fact sits apart from the score', await b.evaluate(`document.querySelector('${CM} .cm-reads').textContent.includes('${cs.readCount} reads') && document.querySelectorAll('${CM} .cm-sq').length===${cs.operatorCount}`));
  await b.evaluate(`document.querySelector('${CM} .cm-chip[data-class=pass-per-term]').click()`);
  ok('phasor: choosing a class lists its operations with their evidence', await b.evaluate(`document.querySelectorAll('${CM} .cm-ops li').length===${cs.classCounts['pass-per-term']} && document.querySelector('${CM} .cm-chip[data-class=pass-per-term]').getAttribute('aria-pressed')==='true'`));
  await b.evaluate(`document.querySelector('${CM} .cm-sq.cm-write').dispatchEvent(new MouseEvent('click',{bubbles:true}))`);
  ok('phasor: clicking a square chooses its class', await b.evaluate(`document.querySelectorAll('${CM} .cm-ops li').length===${cs.classCounts.write}`));
  ok('phasor: the brick table has one row per brick', await b.evaluate(`document.querySelectorAll('.bricks tbody tr').length===${covD.construction.length}`));
  // the commutation map: one square per operation, five groups, the owner's counts
  const CX = "[data-diagram=commutation-map]", cxD = JSON.parse(fs.readFileSync(path.join(DATA, 'diagrams', 'commutation-map.json'), 'utf8'));
  ok('phasor: the commutation map mounts', await b.evaluate(`document.querySelector('${CX}').dataset.ready==='true'`));
  ok('phasor: one square per operation, and the headline states the commuting count', await b.evaluate(`document.querySelectorAll('${CX} .cm-sq').length===${cxD.members} && document.querySelector('${CX} .cm-head').textContent.includes('${cxD.commuting} of ${cxD.members}')`));
  const cxG = Object.fromEntries(cxD.groups.map((g) => [g.id, g.count]));
  ok('phasor: the group counts are the record\'s', await b.evaluate(`Object.entries(${JSON.stringify(cxG)}).every(([id,n])=>document.querySelectorAll('${CX} .cm-sq.cm-g-'+id).length===n)`));
  await b.evaluate(`document.querySelector('${CX} .cm-chip[data-class=joins-axes]').click()`);
  ok('phasor: choosing the group that joins the kernel\'s axes lists its operations', await b.evaluate(`document.querySelectorAll('${CX} .cm-ops li').length===${cxG['joins-axes']} && document.querySelector('${CX} .cm-chip[data-class=joins-axes]').getAttribute('aria-pressed')==='true'`));
  ok('phasor: the symmetry section states the boundary tiles', await b.evaluate(`(()=>{const t=[...document.querySelectorAll('#symmetry .stat dd')].map(x=>x.textContent.trim());return t.join(',')==='${cxD.commuting},${cxG.linear},${cxG.nonlinear},${cxG.declare}'})()`));
  ok('phasor: the page keeps to "commute", not "universal" or "complete" claims about the kernel', await b.evaluate(`!/\\b(universal|complete)\\b/i.test(document.querySelector('#symmetry').innerText)`));
  ok('phasor: the numbers disclosure is closed above the fold', await b.evaluate(`!document.querySelector('.numbers-more').open`));
  ok('phasor: the hero promise is the first heading and the demo is next', await b.evaluate(`document.querySelector('h1').textContent.startsWith('Do a thousand things') && document.querySelector('main section#demo').getBoundingClientRect().top < 900`));
  ok('phasor: no console errors after use', b.errors.length === 0, JSON.stringify(b.errors));
  await b.goto(ph, { width: 390, height: 844, mobile: true, wait: PREADY });
  const pw = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('phasor phone 390: no horizontal overflow', pw.sw <= pw.cw, JSON.stringify(pw));
  ok('phasor phone 390: the scene uses the narrow layout', await b.evaluate(`document.querySelector('${SC} svg').viewBox.baseVal.width===360`));
  await b.evaluate(`document.querySelector('${SC} [data-mode=cost]').click()`);
  const cr = await b.evaluate(`(()=>{const s=document.querySelector('${SC} svg').getBoundingClientRect();return [...document.querySelectorAll('${SC} svg text')].filter(t=>{const r=t.getBoundingClientRect();return r.right>s.right+1||r.left<s.left-1}).map(t=>t.textContent)})()`);
  ok('phasor phone 390: no chart text is cut off in the cost view', cr.length === 0, JSON.stringify(cr));
  ok('phasor phone 390: tabs are at least 24px tall', await b.evaluate(`[...document.querySelectorAll('${SC} [role=tab]')].every(e=>e.getBoundingClientRect().height>=24)`));

  // ================= the Pleroma page: the pyramid, the replay, the capacity demo =================
  const MREADY = "document.querySelector('[data-diagram=pleroma-map]').dataset.ready==='true'";
  await b.goto(pl, { width: 1280, height: 900, wait: MREADY });
  ok('pleroma: map mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  await b.evaluate("document.querySelector('[data-diagram=pleroma-map] .dia-stage').focus()");
  await b.key('Home', 'Home', 36);
  ok('pleroma: Home selects the first cell and fills the panel', await b.evaluate("/Signed insertion and deletion/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('ArrowUp', 'ArrowUp', 38);
  ok('pleroma: Up moves one rung up on the same face', await b.evaluate("/Binary choice|Rung 2/.test(document.querySelector('[data-ctl=panel]').textContent) || /Key on the cycle/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('ArrowRight', 'ArrowRight', 39);
  ok('pleroma: Right moves to the next face', await b.evaluate("/Monad|One-hot/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('End', 'End', 35);
  ok('pleroma: End selects the lid, with its read-time kind', await b.evaluate("/Read-time/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.key('Escape', 'Escape', 27);
  ok('pleroma: Escape clears the selection', await b.evaluate("/Pick a cell/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  await b.evaluate("document.querySelector('[data-color=status]').click()");
  ok('pleroma: colour by kind toggles', await b.evaluate("document.querySelector('[data-color=status]').getAttribute('aria-pressed')==='true'"));
  ok('pleroma: every cell is in the table alternative', await b.evaluate(`document.querySelectorAll('[data-diagram=pleroma-map] .dia-data tbody tr').length===${mapD.totalCells + 1}`));
  ok('pleroma: the page shows no history or ledger words', await b.evaluate("!/PROOF STATUS|NOT BUILT|COVERED|\\bprogress\\b/i.test(document.querySelector('main').innerText)"));
  await b.goto(pl, { width: 1280, height: 900, reducedMotion: true, wait: MREADY });
  const m1 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(4000,4300)");
  await new Promise((r) => setTimeout(r, 1200));
  const m2 = await b.evaluate("document.querySelector('.dia-canvas').toDataURL().slice(4000,4300)");
  ok('pleroma: reduced motion keeps the pyramid still', m1 === m2);
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
  ok('pleroma: the commutation map mounts in the symmetry section and a group lists its operations', await b.evaluate(`(()=>{const r=document.querySelector('#symmetry')||document.querySelector('[data-diagram=commutation-map]');const f=document.querySelector('[data-diagram=commutation-map]');f.querySelector('.cm-chip[data-class=linear]').click();return f.dataset.ready==='true'&&f.querySelectorAll('.cm-sq').length===${JSON.parse(fs.readFileSync(path.join(DATA, 'diagrams', 'commutation-map.json'), 'utf8')).members}&&f.querySelectorAll('.cm-ops li').length>0})()`));
  ok('pleroma: it points to the coverage meter and the grid map it refers to', await b.evaluate(`!!document.querySelector('a[href="/phasor/#coverage"]') && !!document.querySelector('[data-diagram=pleroma-map]')`));
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
  ok('holodb: Optional plane lists the six queries', await b.evaluate("!document.querySelector('[data-ctl=qlist]').hidden && document.querySelectorAll('[data-diagram=holodb-store] [data-q]').length===6"));
  await b.evaluate("document.querySelector('[data-q=\"3\"]').click()");
  ok('holodb: a declined query says the engine answers and why', await b.evaluate("/Declined/.test(document.querySelector('[data-ctl=panel]').textContent) && /not a plane dimension/.test(document.querySelector('[data-ctl=panel]').textContent)"));
  ok('holodb: table alternative holds the rows, the queries and the plane routes', await b.evaluate("document.querySelectorAll('[data-diagram=holodb-store] .dia-data table').length===3 && document.querySelectorAll('[data-diagram=holodb-store] .dia-data tbody tr').length===24+3+6"));
  await b.goto(hd, { width: 1280, height: 900, reducedMotion: true, wait: DREADY });
  const d1 = await b.evaluate("document.querySelector('[data-diagram=holodb-store] .dia-canvas').toDataURL().slice(4000,4300)");
  await new Promise((r) => setTimeout(r, 1200));
  const d2 = await b.evaluate("document.querySelector('[data-diagram=holodb-store] .dia-canvas').toDataURL().slice(4000,4300)");
  ok('holodb: reduced motion keeps the scene still', d1 === d2);
  await b.goto(hd, { width: 390, height: 844, mobile: true, wait: DREADY });
  const dw2 = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('holodb phone 390: no horizontal overflow', dw2.sw <= dw2.cw, JSON.stringify(dw2));
  // ---- the 100,000,000-invoice record: race chart, explainer, decision helper ----
  const race = await import('../src/assets/diagrams/holodb-race.js');
  const raceData = JSON.parse(fs.readFileSync(path.join(DATA, 'diagrams', 'holodb-race.json'), 'utf8'));
  const raceM = race.metrics(raceData);
  const amberWarm = raceM.per.warm.behind.flat().filter(Boolean).length;
  const RREADY = "document.querySelector('[data-diagram=holodb-race]').dataset.ready==='true' && document.querySelector('[data-diagram=holodb-how]').dataset.ready==='true'";
  await b.goto(he, { width: 1280, height: 900, wait: RREADY });
  ok('evidence: chart and explainer mount without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('evidence: the chart draws 16 queries by 4 engines', await b.evaluate("document.querySelectorAll('[data-diagram=holodb-race] .hr-svg .hr-row').length===16 && document.querySelectorAll('[data-diagram=holodb-race] .hr-svg .bar').length===64"));
  ok('evidence: amber bars are exactly the HoloDb cells with another engine more than 20 percent faster', await b.evaluate("document.querySelectorAll('[data-diagram=holodb-race] .hr-svg .bar.behind').length===" + amberWarm), String(amberWarm));
  ok('evidence: default readout states ahead, equal and behind from the data', await b.evaluate("/HoloDb restored against tuned DuckDB: ahead on \\d+, equal on \\d+, behind on \\d+/.test(document.querySelector('[data-diagram=holodb-race] [data-ctl=readout]').textContent)"));
  await b.evaluate("document.querySelector('[data-diagram=holodb-race] .hr-row[data-i=\"6\"] .hit').dispatchEvent(new PointerEvent('pointermove',{bubbles:true}))");
  ok('evidence: pointing at a row reads the milliseconds of all four engines', await b.evaluate("/overdue outstanding: HoloDb restored [\\d.,]+ ms; HoloDb out of the box [\\d.,]+ ms; DuckDB tuned [\\d.,]+ ms; SQL Server tuned [\\d.,]+ ms/.test(document.querySelector('[data-diagram=holodb-race] [data-ctl=readout]').textContent)"));
  await b.evaluate("document.querySelector('[data-diagram=holodb-race] [data-mode=cold]').click()");
  ok('evidence: the cold toggle redraws and says cold', await b.evaluate("/^Cold/.test(document.querySelector('[data-diagram=holodb-race] [data-ctl=readout]').textContent) && document.querySelector('[data-diagram=holodb-race] [data-mode=cold]').getAttribute('aria-pressed')==='true'"));
  await b.evaluate("document.querySelector('[data-diagram=holodb-race] .dia-stage').focus()");
  await b.key('Home', 'Home', 36);
  ok('evidence: Home reads the first row from the keyboard', await b.evaluate("/invoices by status: HoloDb restored/.test(document.querySelector('[data-diagram=holodb-race] [data-ctl=readout]').textContent)"));
  ok('evidence: the raw numbers are a collapsed block with two tables', await b.evaluate("(()=>{const d=document.querySelector('[data-diagram=holodb-race] details.dia-data');return !!d && !d.open && d.querySelectorAll('table').length===2 && d.querySelectorAll('tbody tr').length===32})()"));
  ok('evidence: explainer has six scenes and each tab changes the promise', await b.evaluate("(()=>{const f=document.querySelector('[data-diagram=holodb-how]');const tabs=[...f.querySelectorAll('[role=tab]')];if(tabs.length!==6)return false;const seen=new Set();for(const t of tabs){t.click();seen.add(f.querySelector('[data-ctl=promise]').textContent)}return seen.size===6})()"));
  const stageHtml = () => b.evaluate("document.querySelector('[data-diagram=holodb-how] .dia-stage').innerHTML");
  await b.evaluate("document.querySelector('[data-diagram=holodb-how] [role=tab]').click()");
  await b.evaluate("document.querySelector('[data-diagram=holodb-how]').scrollIntoView()");
  const hwA = await stageHtml(); await new Promise((r) => setTimeout(r, 900)); const hwB = await stageHtml();
  ok('explainer: a scene plays by itself when it is on screen', hwA !== hwB);
  await b.evaluate("(()=>{const r=document.querySelector('[data-diagram=holodb-how] [data-ctl=time]');r.value=1000;r.dispatchEvent(new Event('input',{bubbles:true}))})()");
  ok('explainer: the scrubber lands on the final frame and pauses', await b.evaluate("document.querySelector('[data-diagram=holodb-how] [data-ctl=play]').getAttribute('aria-pressed')==='false'"));
  ok('explainer: every caption says the motion is illustrative', await b.evaluate("(()=>{const f=document.querySelector('[data-diagram=holodb-how]');const d=JSON.parse(f.querySelector('script[data-diagram-data]').textContent);return d.scenes.every(s=>/illustrative/i.test(s.caption))})()"));
  await b.goto(he, { width: 1280, height: 900, reducedMotion: true, wait: RREADY });
  await b.evaluate("document.querySelector('[data-diagram=holodb-how]').scrollIntoView()");
  const rmA = await stageHtml(); await new Promise((r) => setTimeout(r, 1500)); const rmB = await stageHtml();
  ok('explainer: reduced motion shows the final frame and does not play by itself', rmA === rmB && await b.evaluate("document.querySelector('[data-diagram=holodb-how] [data-ctl=play]').getAttribute('aria-pressed')==='false'"));
  await b.evaluate("document.querySelector('[data-diagram=holodb-how] [data-ctl=play]').click()");
  const rmC = await stageHtml(); await new Promise((r) => setTimeout(r, 700)); const rmD = await stageHtml();
  ok('explainer: under reduced motion it plays only after Play is pressed', rmC !== rmD || rmC !== rmA);
  await b.goto(he, { width: 390, height: 844, mobile: true, wait: RREADY });
  const ew = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('evidence phone 390: no horizontal overflow', ew.sw <= ew.cw, JSON.stringify(ew));
  ok('evidence phone 390: chart labels sit above the bars', await b.evaluate("document.querySelector('[data-diagram=holodb-race] .dia-stage').classList.contains('hr-narrow')"));
  ok('evidence phone 390: no text leaves the explainer scene', await b.evaluate("(()=>{const out=[];for(const t of document.querySelectorAll('[data-diagram=holodb-how] .tab,[data-diagram=holodb-how] [role=tab]')){t.click();const s=document.querySelector('[data-diagram=holodb-how] .hw-svg');const vb=s.viewBox.baseVal;for(const x of s.querySelectorAll('text')){const r=x.getBBox();if(r.x+r.width>vb.width+2||r.y+r.height>vb.height+2)out.push(t.textContent+': '+x.textContent.slice(0,40))}}return out.length===0})()"));
  // ---- the decision helper on /holodb/ ----
  const FREADY = "document.querySelector('[data-diagram=holodb-fit]').dataset.ready==='true'";
  const verdictNow = () => b.evaluate("document.querySelector('[data-diagram=holodb-fit]').dataset.verdict");
  const pickAns = (q, a) => b.evaluate(`document.querySelector('[data-diagram=holodb-fit] [data-fq=${q}][data-fa=${a}]').click()`);
  await b.goto(hd, { width: 1280, height: 900, wait: FREADY });
  ok('fit: the default answers are a good fit', await verdictNow() === 'good');
  await pickAns('work', 'mixed');
  ok('fit: mixed reads and writes is possible and names the write cost', await verdictNow() === 'possible' && await b.evaluate("/Single-row INSERT took/.test(document.querySelector('[data-diagram=holodb-fit] [data-ctl=out]').textContent)"));
  await pickAns('work', 'writes'); await pickAns('queries', 'adhoc'); await pickAns('size', 'm100');
  ok('fit: heavy writes, ad hoc queries and 100M rows point to other tools', await verdictNow() === 'other' && await b.evaluate("/Tuned DuckDB/.test(document.querySelector('[data-diagram=holodb-fit] [data-ctl=out]').textContent) && /SQLite/.test(document.querySelector('[data-diagram=holodb-fit] [data-ctl=out]').textContent)"));
  await pickAns('work', 'reads'); await pickAns('queries', 'known'); await pickAns('size', 'm10'); await pickAns('lang', 'other');
  ok('fit: another language is a hard no and names PostgreSQL or SQL Server', await verdictNow() === 'other' && await b.evaluate("/Does not fit/.test(document.querySelector('[data-diagram=holodb-fit] [data-ctl=out]').textContent) && /PostgreSQL or SQL Server/.test(document.querySelector('[data-diagram=holodb-fit] [data-ctl=out]').textContent)"));
  ok('fit: no console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('holodb: the five trade-offs are on the page', await b.evaluate("document.querySelectorAll('.tradeoffs > li').length===5"));
  await b.goto(hd, { width: 390, height: 844, mobile: true, wait: FREADY });
  const fw = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth})');
  ok('holodb phone 390: no horizontal overflow with the helper', fw.sw <= fw.cw, JSON.stringify(fw));  for (const sp of ['docs/', 'pleroma/', 'client/']) {
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
  // ---- the Pleroma article: the operation grid ----
  const PG = "[data-diagram=pleroma-grid]", pgu = `/research/one-store-many-reads/`;
  await b.goto(`${base}${pgu}`, { width: 1280, height: 900, wait: `document.querySelector('${PG}').dataset.ready==='true'` });
  ok('pleroma article: the grid mounts without console errors', b.errors.length === 0, JSON.stringify(b.errors));
  ok('pleroma article: 24 cells and the lid are buttons, each named with its kind', await b.evaluate(`(()=>{const c=[...document.querySelectorAll('${PG} button.pg-cell')];return c.length===25&&c.every(x=>/Checked\\.|Read-time\\./.test(x.getAttribute('aria-label')))&&document.querySelectorAll('${PG} .pg-cell.k-ok').length===24&&document.querySelectorAll('${PG} .pg-cell.k-warn').length===1})()`));
  await b.evaluate(`document.querySelector('${PG} [data-id=R3_Ouroboros]').click()`);
  ok('pleroma article: selecting a cell shows its API, kind and evidence from the record', await b.evaluate(`(()=>{const t=document.querySelector('${PG} [data-ctl=panel]').textContent;return /Parity: odd-occurrence XOR/.test(t)&&/FlagParity/.test(t)&&/Checked/.test(t)&&/256 parity classes/.test(t)})()`));
  await b.evaluate(`document.querySelector('${PG} [data-id=R3_Ouroboros]').focus()`);
  await b.key('ArrowLeft', 'ArrowLeft', 37);
  ok('pleroma article: the left arrow moves focus to the Monad cell of the same rung', await b.evaluate("document.activeElement.dataset.id==='R3_Monad'"));
  await b.key('ArrowUp', 'ArrowUp', 38);
  ok('pleroma article: the up arrow moves one rung up', await b.evaluate("document.activeElement.dataset.id==='R4_Monad'"));
  await b.key('Home', 'Home', 36);
  ok('pleroma article: Home goes to the lid', await b.evaluate("document.activeElement.dataset.id==='Lid_Sample'"));
  ok('pleroma article: the table alternative lists all 25 cells', await b.evaluate(`document.querySelectorAll('${PG} .dia-data tbody tr').length===25`));
  ok('pleroma article: the three tiles, the words strip and the closed Go deeper blocks are present', await b.evaluate("document.querySelectorAll('.rtiles .stat').length===3&&document.querySelectorAll('.words-list dt').length===7&&document.querySelectorAll('details.deeper').length===7&&[...document.querySelectorAll('details.deeper')].every(d=>!d.open)"));
  await b.goto(`${base}${pgu}`, { width: 1280, height: 900, wait: `document.querySelector('${PG}').dataset.ready==='true'` });
  await b.evaluate("location.hash='#the-gaps-grouped'");
  await new Promise((r) => setTimeout(r, 300));
  ok('pleroma article: a link to a Go deeper block opens it', await b.evaluate("document.getElementById('the-gaps-grouped').open===true"));
  await b.goto(`${base}${pgu}`, { width: 1280, height: 900, reducedMotion: true, wait: `document.querySelector('${PG}').dataset.ready==='true'` });
  ok('pleroma article: reduced motion, still mounts and animates nothing', await b.evaluate(`getComputedStyle(document.querySelector('${PG} .pg-cell')).transitionDuration==='0s'`));
  await b.goto(`${base}${pgu}`, { width: 390, height: 844, mobile: true, wait: `document.querySelector('${PG}').dataset.ready==='true'` });
  ok('pleroma article phone 390: the grid has three columns that fit the screen', await b.evaluate(`(()=>{const g=document.querySelector('${PG} .pg').getBoundingClientRect();const c=[...document.querySelectorAll('${PG} .pg-cell:not(.lid)')].slice(0,3).map(x=>x.getBoundingClientRect());return g.right<=window.innerWidth+1&&c[0].top===c[2].top&&c[0].width>80})()`));
  ok('pleroma article phone 390: cells are at least 40px tall', await b.evaluate(`[...document.querySelectorAll('${PG} .pg-cell')].every(x=>x.getBoundingClientRect().height>=40)`));
  ok('pleroma article phone 390: no console errors', b.errors.length === 0, JSON.stringify(b.errors));
  for (const s of ['context-four', 'context-eight', 'back-to-front', 'smaller-than-tinystories', 'nobody-read-the-warning', 'one-store-many-reads']) {
    ow = await overflow(`/research/${s}/`);
    ok(`research/${s} phone 390: no horizontal overflow`, ow.sw <= ow.cw, JSON.stringify(ow));
  }
  await b.goto(`${base}/research/`, { width: 1280, height: 900 });
  await b.evaluate("document.querySelector('[data-filter] [data-topic=layers]').click()");
  ok('research hub: the topic filter hides articles that do not match', await b.evaluate("[...document.querySelectorAll('.research-cards .card')].filter(c=>!c.hidden).length===1 && /1 of 6 articles about layers/.test(document.querySelector('[data-filter-count]').textContent)"));

  // ================= Blazier: the boot bars =================
  const BB = "document.querySelector('[data-diagram=blazier-boot]').dataset.ready==='true'";
  const bbTab = (mode) => b.evaluate(`document.querySelector('[data-diagram=blazier-boot] [role=tab][data-mode="${mode}"]').click()`);
  const bbRead = () => b.evaluate("document.querySelector('[data-diagram=blazier-boot] [data-ctl=readout]').textContent");
  // every label and value sits inside the drawing, in every view, on desktop and on a phone
  const bbFit = "(()=>{const s=document.querySelector('[data-diagram=blazier-boot] .bb-svg'); const vb=s.viewBox.baseVal; return [...s.querySelectorAll('text')].every(t=>{const r=t.getBBox(); return r.x>=-1 && r.x+r.width<=vb.width+1 && r.y>=-1 && r.y+r.height<=vb.height+1;});})()";
  for (const [label, opts] of [['desktop', { width: 1280, height: 900 }], ['phone 390', { width: 390, height: 844, mobile: true }]]) {
    await b.goto(`${base}/blazier/`, { ...opts, wait: BB });
    ok(`blazier ${label}: bars mount without console errors`, b.errors.length === 0, JSON.stringify(b.errors));
    ok(`blazier ${label}: first view is the bytes before anyone acts`, await bbRead().then((t) => /downloads 6\.8 KB in 6 requests/.test(t) && /2\.8 MB/.test(t)));
    ok(`blazier ${label}: bytes view fits the drawing`, await b.evaluate(bbFit));
    await bbTab('content');
    ok(`blazier ${label}: content view reads both profiles`, await bbRead().then((t) => /72 to 100 ms on desktop and 416 to 484 ms on the phone profile/.test(t)) && await b.evaluate("document.querySelectorAll('[data-diagram=blazier-boot] .bb-svg .gh').length===2"));
    ok(`blazier ${label}: content view fits the drawing`, await b.evaluate(bbFit));
    await bbTab('working');
    ok(`blazier ${label}: working view says the runtime still costs what it costs`, await bbRead().then((t) => /still costs what it costs/.test(t) && /12\.7 to 15\.4 s/.test(t)));
    ok(`blazier ${label}: working view fits the drawing`, await b.evaluate(bbFit));
    ok(`blazier ${label}: table alternative holds three measures and the worker bridge`, await b.evaluate("document.querySelectorAll('[data-diagram=blazier-boot] .dia-data table').length===4"));
  }
  ow = await overflow('/blazier/');
  ok('blazier phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));
  ow = await overflow('/blazier/evidence/');
  ok('blazier evidence phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));
  // ================= HoloGenome: the score views =================
  const HG = "document.querySelector('[data-diagram=hologenome-scores]').dataset.ready==='true'";
  const hgTab = (mode) => b.evaluate(`document.querySelector('[data-diagram=hologenome-scores] [role=tab][data-mode="${mode}"]').click()`);
  const hgRead = () => b.evaluate("document.querySelector('[data-diagram=hologenome-scores] [data-ctl=readout]').textContent");
  // every label and value sits inside the drawing, in every view, on desktop and on a phone
  const hgFit = "(()=>{const s=document.querySelector('[data-diagram=hologenome-scores] .hg-svg'); const vb=s.viewBox.baseVal; return [...s.querySelectorAll('text')].every(t=>{const r=t.getBBox(); return r.x>=-1 && r.x+r.width<=vb.width+1 && r.y>=-1 && r.y+r.height<=vb.height+1;});})()";
  for (const [label, opts] of [['desktop', { width: 1280, height: 900 }], ['phone 390', { width: 390, height: 844, mobile: true }]]) {
    await b.goto(`${base}/hologenome/`, { ...opts, wait: HG });
    ok(`hologenome ${label}: charts mount without console errors`, b.errors.length === 0, JSON.stringify(b.errors));
    ok(`hologenome ${label}: first view reads the measured twist against the constant twist`, await hgRead().then((t) => /0\.529/.test(t) && /0\.483/.test(t)));
    ok(`hologenome ${label}: controls view fits the drawing`, await b.evaluate(hgFit));
    await hgTab('best');
    ok(`hologenome ${label}: best-model view states the gap`, await hgRead().then((t) => /trailing by 0\.28 to 0\.39/.test(t)) && await b.evaluate("document.querySelectorAll('[data-diagram=hologenome-scores] .hg-svg .bar').length===9"));
    ok(`hologenome ${label}: best-model view fits the drawing`, await b.evaluate(hgFit));
    await hgTab('period');
    ok(`hologenome ${label}: period view reads the four libraries`, await hgRead().then((t) => /10\.49, 10\.51, 10\.48 and 10\.51/.test(t)) && await b.evaluate("document.querySelectorAll('[data-diagram=hologenome-scores] .hg-svg .dot.real').length>=4"));
    ok(`hologenome ${label}: period view fits the drawing`, await b.evaluate(hgFit));
    await hgTab('twist');
    ok(`hologenome ${label}: twist view draws one point per base`, await b.evaluate("document.querySelectorAll('[data-diagram=hologenome-scores] .hg-svg .pt').length===50"));
    ok(`hologenome ${label}: twist view fits the drawing`, await b.evaluate(hgFit));
    ok(`hologenome ${label}: table alternative holds every table`, await b.evaluate("document.querySelectorAll('[data-diagram=hologenome-scores] .dia-data table').length===8"));
  }
  ow = await overflow('/hologenome/');
  ok('hologenome phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));
  ow = await overflow('/hologenome/evidence/');
  ok('hologenome evidence phone 390: no horizontal overflow', ow.sw <= ow.cw, JSON.stringify(ow));
} finally { b.close(); server.close(); }
process.exitCode = results.every(Boolean) ? 0 : 1;



