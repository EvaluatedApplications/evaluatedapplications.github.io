// S9 slice 1 page (PREREG_S68_S9_1.md B4, PREREG_S69_S9_1.md). Main thread: pre-flight, the cost line and a click, a module worker for the audit, a ticker, terminate-only cancel, and the list of flagged rows with the council's own transaction numbers.
// Nothing of the audit engine or of the council's files is requested before the click; the main thread never runs engine code. There is NO check of our output on this page (owner decision, session 69): a visitor looks a row up in the council's own file.
// Every value from the data is put on the page with textContent / setAttribute; nothing from the data is ever parsed as HTML.
const q = new URLSearchParams(location.search);
const BASE = new URL(document.querySelector('meta[name="council-live-base"]')?.content ?? './', location.href).href;
const PRECOMPUTED = document.querySelector('meta[name="council-live-precomputed"]')?.content ?? '';
const slug = q.get('slug') ?? 'wokingham';
const log = (s) => { console.log(s); };       // the console only: the harness reads it, the page does not show it
const $ = (id) => document.getElementById(id);
const setStatus = (s) => { $('status').textContent = s; };
const MARGIN = 1.25;       // the allocation probe asks for the measured need times 1.25; the cost line shows the measured wall times 1.25
const el = (tag, text, attrs) => { const e = document.createElement(tag); if (text != null) e.textContent = text; if (attrs) for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v); return e; };
const safeHref = (u) => { try { const x = new URL(u, location.href); return x.protocol === 'https:' || x.protocol === 'http:' ? x.href : null; } catch { return null; } };

// ---- the data the page needs before the click: three small json files (the manifest, the measured estimates, the wording), no audit engine and no council file
const [manifest, estimates, terms] = await Promise.all(['manifest.json', 'estimates.json', 'terms.json'].map((f) => fetch(BASE + f).then((r) => r.json())));
const council = manifest.councils[slug], est = estimates[slug];
const hostedSlugs = Object.keys(manifest.councils).filter((s) => manifest.councils[s].hostable !== false && estimates[s]);
$('title').textContent = council ? `Council spending check: ${council.name}` : 'Council spending check, run in your browser';
for (const s of hostedSlugs) { const a = el('a', manifest.councils[s].name, { href: `?slug=${s}${q.has('retries') ? '&retries=' + q.get('retries') : ''}` }); if (s === slug) a.setAttribute('aria-current', 'page'); $('councils').appendChild(a); }
if (PRECOMPUTED) { $('precomputedLink').setAttribute('href', PRECOMPUTED + slug); $('backScanner').setAttribute('href', PRECOMPUTED.replace(/\/$/, '')); } else { $('precomputedLink').parentElement.hidden = true; $('backScanner').hidden = true; }
$('inputsLink').setAttribute('href', BASE + 'inputs.csv');
$('discrepancy').textContent = terms.discrepancyNote;
// ONE memory figure per council: the most the check was measured holding (the final linear memory of the largest of its measured runs), in GiB to two places. The probe asks for a little more than this; that margin is not stated as a second figure.
const memGiB = (s) => ((estimates[s].measuredLinearMiB ?? estimates[s].estimatedLinearMiB) / 1024).toFixed(2);
$('memFigures').textContent = hostedSlugs.map((s) => `${memGiB(s)} GiB for ${manifest.councils[s].name}`).join(' and ');
{ const host = location.host, gh = /\.github\.io$/i.test(location.hostname) ? ' (hosted by GitHub Pages)' : '';
  $('privacyText').textContent = `Opening this page fetches the page itself and three small files (the list of councils, the timing figures and the wording of the rules) from ${host} (your browser may also ask ${host} for the site's small icon). Nothing else is fetched until you press the button; then the page fetches the audit engine and the council's files, also from ${host}. It requests nothing from any other address: no outside scripts, fonts or analytics. ${host}${gh} can see your device's internet address and your browser's details when you fetch from it, as any web site can for any request. The page does not send the results, or anything you type into the filter, anywhere.`; }

function showPrecomputed(reason) {
  $('reason').textContent = reason ?? ''; $('cancel').hidden = true; $('run').disabled = false;
}
const hostable = !!council && council.hostable !== false && !!est;
if (!hostable) { showPrecomputed('This council is shown as prepared results only; it cannot be run in the browser here.'); $('tested').hidden = true; log('RESULT ' + JSON.stringify({ slug, verdict: 'not-hosted' })); }
else {
  const dataMiB0 = council.inputs.reduce((a, f) => a + f.bytes, 0) / 1048576;
  $('what').textContent = `Clicking the button downloads the audit engine and ${council.inputs.length} files of ${council.name}'s published spending data from ${location.host}, then runs the checks on your device. The files are copies of what the council published, saved on ${council.inputs.map((f) => f.saved).filter(Boolean).sort().pop() ?? 'an earlier date'}; the council may have published newer ones since.`;
  // ---- pre-flight: feature tests, a real allocation probe, hints that only warn
  const moduleWorkerSupported = () => { let ok = false; try { const u = URL.createObjectURL(new Blob([''], { type: 'text/javascript' })); const w = new Worker(u, { get type() { ok = true; return 'module'; } }); w.terminate(); URL.revokeObjectURL(u); } catch { /* not supported */ } return ok; };
  const missing = [];
  if (typeof Worker === 'undefined') missing.push('web workers'); else if (!moduleWorkerSupported()) missing.push('module workers');
  if (typeof WebAssembly === 'undefined' || typeof WebAssembly.instantiateStreaming !== 'function') missing.push('streaming WebAssembly');
  if (!(typeof Response !== 'undefined' && 'body' in Response.prototype)) missing.push('fetch body streams');
  if (!(typeof crypto !== 'undefined' && crypto.subtle && isSecureContext)) missing.push('crypto.subtle (needs a secure context)');
  const needMiB = Number(q.get('probeNeed') ?? Math.ceil((est.measuredLinearMiB ?? est.estimatedLinearMiB) * MARGIN));       // probeNeed: a test switch that asks for a different size
  const maxMiB = manifest.bundle.maxHeapMiB;
  let probe = { ok: false, error: 'not run (a feature is missing)' };
  if (missing.length === 0) {
    try { const pages = (mib) => Math.ceil(mib * 16); const m = new WebAssembly.Memory({ initial: 1, maximum: pages(maxMiB) }); m.grow(pages(needMiB)); probe = { ok: true, gotMiB: Math.round(m.buffer.byteLength / 1048576) }; }
    catch (e) { probe = { ok: false, error: String(e) }; }
  }
  const warnings = [];
  if (navigator.deviceMemory != null && navigator.deviceMemory * 1024 < needMiB * 2) warnings.push(`this device reports ${navigator.deviceMemory} GiB of memory`);
  if (navigator.connection?.saveData) warnings.push('data saver is on');
  if (/Android|iPhone|iPad|Mobile/i.test(navigator.userAgent)) warnings.push('this looks like a phone, and phones have not been tested');
  log('PREFLIGHT ' + JSON.stringify({ slug, missing, needMiB, maxMiB, probe, warnings, deviceMemory: navigator.deviceMemory ?? null, secure: isSecureContext }));

  if (missing.length > 0 || !probe.ok) {
    showPrecomputed(missing.length > 0 ? `This browser lacks: ${missing.join(', ')}. The prepared results are the way to see this council here.` : `This browser would not set aside the memory the check needs (about ${memGiB(slug)} GiB; ${probe.error}). The prepared results are the way to see this council here.`);
    log('RESULT ' + JSON.stringify({ slug, verdict: 'preflight-refused', missing, needMiB, probe }));
  } else {
    $('cost').textContent = `Before you run it: this downloads the audit engine once (about ${manifest.bundle.wireMiB} MiB over the network) and ${council.name}'s published files (${dataMiB0.toFixed(1)} MiB of files, about ${council.inputsWireMiB} MiB over the network from this site); on the desktop computer we tested it took ${est.wallRangeS[0]} to ${est.wallRangeS[1]} s, and a laptop or a phone may be slower; it needs up to about ${memGiB(slug)} GiB of memory (the most we measured).${est.coldNote ? ' ' + est.coldNote : ''}${warnings.length ? ' Note: ' + warnings.join('; ') + '.' : ''}`;
    $('runbox').hidden = false; $('run').disabled = false;
    log('READY ' + JSON.stringify({ slug, dataMiB: Math.round(dataMiB0 * 10) / 10, estSeconds: Math.round(est.wallS * MARGIN) }));
    $('run').onclick = () => run();
    if (q.get('autoclick') === '1') setTimeout(() => $('run').click(), Number(q.get('autoclickDelay') ?? 500));       // a test switch: the harness clicks through the DOM when it wants a click; this one is for a plain page load
  }
}

// ---- the list of flagged rows
const C = { year: 0, schedule: 1, group: 2, id: 3, supplier: 4, net: 5, gross: 6, gap: 7, detail: 8, cls: 9, by: 10, meaning: 11 };       // the worker's column order (KEEP in worker.js)
const money = (s) => { const n = Number(s); return s !== '' && Number.isFinite(n) ? n.toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : s; };
const PAGE = 50;
let all = [], classes = new Map(), order = [], view = [], pageNo = 0, fileOf = new Map();
const classKey = (r) => r[C.schedule] + '|' + r[C.cls];
// Plain-English status labels. They are this page's own short names for the rules; the rule's own name stays beside each one, and the public scanner's wording of what the rule means is shown in the table (terms.json).
const PLAIN = { Unreconciled: 'Does not add up, no pattern found', StandingScheduleSurplus: 'Monthly amount paid more often than monthly', Unclear: 'Repeated payment, no invoice number to check', Unmatched: 'Credit with no matching charge found', SmallGap: 'Small gap, 5p to under £1', ReversedSameDay: 'Repeat cancelled by a same-day reversal', MigrationControlLabel: 'Council ledger-migration control account',
  DoubleListing: 'Possibly published more than once', DebtCharge: 'Looks like a loan or debt-recovery payment', EarlyPaymentProgramme: 'Early-payment discount scheme', VatRoundingNoise: 'VAT rounding, 5p or less', NegativeNetSignFlip: 'Net figure has the opposite sign', LikelyDuplicate: 'Same invoice number and amount repeated', LikelyRecurring: 'Same amount, different invoice numbers', MatchedOffsettingCharge: 'Credit with a matching charge nearby', StandingScheduleCatchUp: 'Monthly amount, a catch-up of missed months', StandingPaymentUnderRefundType: 'Monthly amount under a refund label', NSquaredListing: 'Publication quirk: lines printed several times' };
const classCode = (k) => k.split('|')[1];
const classLabel = (k) => { const [s, c] = k.split('|'); return c ? (PLAIN[c] ?? c) : `One transaction number, several payees or dates (Schedule ${s})`; };
const firstSentence = (t) => { const m = /^.*?[.!?](?=\s|$)/s.exec(t ?? ''); return m ? m[0] : (t ?? ''); };
const rankOf = (c) => terms.rank[c] ?? 5;

function buildResults(wallS) {
  classes = new Map(); all.forEach((r, i) => { const k = classKey(r); let a = classes.get(k); if (!a) classes.set(k, a = []); a.push(i); });
  const keys = [...classes.keys()].sort((a, b) => rankOf(a.split('|')[1]) - rankOf(b.split('|')[1]) || classes.get(b).length - classes.get(a).length || (a < b ? -1 : 1));
  order = keys.flatMap((k) => classes.get(k));
  fileOf = new Map(council.inputs.map((f) => [f.tag, f]));
  { const read = est.rows, nf = all.length, sizes = keys.map((k) => [k, classes.get(k).length]).sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1)), two = sizes.slice(0, 2), twoSum = two.reduce((a, x) => a + x[1], 0), pc = (x, y) => (100 * x / y).toFixed(1), n = (x) => x.toLocaleString('en-GB');
    const [tk] = two[0], tmean = firstSentence(classCode(tk) ? terms.rules[classCode(tk)] : terms.schedules[tk.split('|')[0]]?.definition);
    $('resultsLead').textContent = `${n(nf)} of the ${n(read)} lines read in ${council.inputs.length} files of ${council.name} are flagged (${pc(nf, read)} percent), found in ${wallS.toFixed(0)} s on this device. ${two.length > 1 ? `The two largest groups hold ${n(twoSum)} of the flagged rows (${pc(twoSum, nf)} percent): "${classLabel(two[0][0])}" (${n(two[0][1])}, ${pc(two[0][1], nf)} percent) and "${classLabel(two[1][0])}" (${n(two[1][1])}, ${pc(two[1][1], nf)} percent). ` : ''}The largest group is described like this: "${tmean}" Read what each group means in the table below before taking the total as a count of problems. A flagged row is a fact about the published numbers, not an accusation.`; }
  const tb = $('summary').tBodies[0]; tb.replaceChildren();
  for (const k of keys) {
    const [s, c] = k.split('|'); const tr = el('tr'); const a = el('a', classLabel(k), { href: '#' }); a.onclick = (e) => { e.preventDefault(); $('fRule').value = k; apply(); $('flagged').scrollIntoView(); };
    const td = el('td'); td.appendChild(a); if (c) td.appendChild(el('div', `rule name: ${c}`, { class: 'detail' })); tr.appendChild(td); tr.appendChild(el('td', s)); tr.appendChild(el('td', classes.get(k).length.toLocaleString('en-GB'), { class: 'num' }));
    tr.appendChild(el('td', (c ? terms.rules[c] : terms.schedules[s]?.definition) ?? '')); tb.appendChild(tr);
  }
  const fr = $('fRule'); fr.replaceChildren(el('option', 'All rules', { value: '' })); for (const k of keys) fr.appendChild(el('option', `${classLabel(k)} (${classes.get(k).length.toLocaleString('en-GB')})`, { value: k }));
  const ff = $('fFile'); ff.replaceChildren(el('option', 'All files', { value: '' })); for (const f of council.inputs) ff.appendChild(el('option', `${f.period}: ${f.file}`, { value: f.tag }));
  const landing = council.inputs.find((f) => f.sourceKind === 'landing-page');
  $('fileHead').textContent = landing ? 'File (the name we saved it under)' : 'File (named in the council\'s web address; links to it)';
  $('howto').textContent = `To check a row, open the council's file named in its last column and find the transaction number in the column the council uses for it (TransNo). ${landing ? `${council.name} publishes its files from one web page (${landing.source}), so the file name shown is the name we saved the file under, not a name taken from the council's web address; the period of the file (for example "${landing.period}") says which file it is. ` : `Each file name is the one at the end of the web address recorded for that file when we saved it, and it links to that address. `}The period of the file is the period the council gives that file, not the date of each payment: a file can hold payment dates outside its stated period. Councils change their web addresses from time to time, so a link may no longer work; "copy" is the same file as we saved it, kept on this site. Where a rule lists several rows with the same number, the council's file shows all of those lines.`;
  $('results').hidden = false; pageNo = 0; $('fRule').value = ''; $('fFile').value = ''; $('fSearch').value = ''; apply();
  log('FLAGGED ' + JSON.stringify({ slug, rows: all.length, classes: Object.fromEntries([...classes].map(([k, v]) => [k, v.length])) }));
}

function apply() {
  const rule = $('fRule').value, tag = $('fFile').value, text = $('fSearch').value.trim().toLowerCase();
  let base = rule ? classes.get(rule) ?? [] : order;
  if (tag) base = base.filter((i) => all[i][C.year] === tag);
  if (text) base = base.filter((i) => all[i][C.id].toLowerCase().includes(text) || all[i][C.supplier].toLowerCase().includes(text));
  view = base; pageNo = 0; draw();
}
function draw() {
  const pages = Math.max(1, Math.ceil(view.length / PAGE)); pageNo = Math.min(pageNo, pages - 1);
  $('count').textContent = `${view.length.toLocaleString('en-GB')} rows; page ${pageNo + 1} of ${pages.toLocaleString('en-GB')}`;
  $('prev').disabled = pageNo === 0; $('next').disabled = pageNo >= pages - 1;
  const tb = $('flagged').tBodies[0]; tb.replaceChildren();
  for (const i of view.slice(pageNo * PAGE, pageNo * PAGE + PAGE)) {
    const r = all[i], tr = el('tr'), f = fileOf.get(r[C.year]);
    const rule = el('td', classLabel(classKey(r))); if (r[C.cls]) rule.appendChild(el('div', r[C.cls], { class: 'detail' })); if (r[C.group]) rule.appendChild(el('div', `group ${r[C.group]}`, { class: 'detail' })); tr.appendChild(rule);
    tr.appendChild(el('td', r[C.id], { class: 'id' }));
    const sup = el('td', r[C.supplier]); const note = [r[C.detail], r[C.by] ? `pattern reading: ${r[C.by]}` : ''].filter(Boolean).join('; '); if (note) sup.appendChild(el('div', note, { class: 'detail' })); tr.appendChild(sup);
    tr.appendChild(el('td', money(r[C.net]), { class: 'num' })); tr.appendChild(el('td', money(r[C.gross]), { class: 'num' })); tr.appendChild(el('td', money(r[C.gap]), { class: 'num' }));
    const ft = el('td'); if (f) {
      const href = f.sourceKind === 'direct-file' ? safeHref(f.source) : null;
      if (href) ft.appendChild(el('a', f.file, { href, target: '_blank', rel: 'noopener noreferrer' })); else ft.appendChild(document.createTextNode(f.file));
      ft.appendChild(el('div', `period of the file: ${f.period}`, { class: 'detail' }));
      const copy = el('a', 'copy', { href: BASE + f.hosted, target: '_blank', rel: 'noopener' }); ft.appendChild(document.createTextNode(' (')); ft.appendChild(copy); ft.appendChild(document.createTextNode(')'));
    } else ft.textContent = r[C.year];
    tr.appendChild(ft); tb.appendChild(tr);
  }
}
$('fRule').onchange = apply; $('fFile').onchange = apply; $('prev').onclick = () => { pageNo--; draw(); }; $('next').onclick = () => { pageNo++; draw(); };
let searchTimer = 0; $('fSearch').oninput = () => { clearTimeout(searchTimer); searchTimer = setTimeout(apply, 200); };

// ---- the run
let runNo = 0;
function run() {
  runNo++; const myRun = runNo;
  $('run').disabled = true; $('cancel').hidden = false; $('reason').textContent = ''; $('result').textContent = ''; $('results').hidden = true; all = [];
  const FRIENDLY = { 'Engine/StandingIndex': 'building the standing-payment index', 'Engine/FindScheduleAGroups': 'grouping payments by transaction and supplier', 'Engine/ScheduleD': 'looking for transaction numbers shared by several payees', 'Engine/ScheduleBGroups': 'matching repeated payments', 'Engine/TransactionAggregate': 'summing each transaction', 'Engine/FindRepeatGroups': 'finding repeated payments', 'ExportFile/End': 'finishing an export file' };
  const steps = est.steps ?? []; let lastLabel = '';
  const stageNote = (msg, silentS, elapsedS) => {
    if (!msg) return elapsedS > 1 ? 'starting the engine' : '';
    if (silentS < 2) return '';
    const i = steps.findIndex((s) => s[0] === msg.stage); let name = null;
    if (msg.running && i >= 0) name = msg.stage; else { const nx = steps.slice(i + 1).find((s) => s[1] >= 2000); if (nx) name = nx[0]; }
    if (!name) return '';
    const s = steps.find((x) => x[0] === name), text = `${FRIENDLY[name] ?? name}: typically about ${Math.max(1, Math.round(s[1] / 1000))} s on this council (measured run)`;
    if (text !== lastLabel) { lastLabel = text; log(`STAGE ${name}; silent ${silentS.toFixed(1)} s`); }
    return text;
  };
  let lastTick = performance.now(), maxBlock = 0;
  const blockMeter = setInterval(() => { const n = performance.now(); maxBlock = Math.max(maxBlock, n - lastTick - 25); lastTick = n; }, 25);
  const w = new Worker(BASE + 'worker.js', { type: 'module' }); const t0 = performance.now();
  let progress = 0, lastProg = t0, last = null, finished = false, cancelledAt = null, rowsEnd = null, rowBatches = 0;
  const ticker = setInterval(() => {
    const now = performance.now(), elapsed = (now - t0) / 1000, silent = (now - lastProg) / 1000, what = stageNote(last, silent, elapsed);
    setStatus(`${Math.floor(elapsed / 60)}:${String(Math.floor(elapsed % 60)).padStart(2, '0')}  ${last ? last.stage : 'starting the engine'}  files ${last?.files ?? 0}  memory ${last?.linearMB ?? 0} MiB  worker last spoke ${silent.toFixed(1)} s ago${what ? '\n' + what : ''}`);
    if (silent > 180 && !finished) { result({ verdict: 'worker-silent-180s', last }, 'The check stopped answering. The prepared results are the way to see this council here.'); }
  }, 250);
  const stop = () => { finished = true; clearInterval(ticker); clearInterval(blockMeter); w.terminate(); };
  const result = (r, message, ok) => { if (finished) return; stop(); const wallMs = Math.round(performance.now() - t0); log('RESULT ' + JSON.stringify({ slug, runNo: myRun, wallMs, mainThreadMaxBlockMs: Math.round(maxBlock), ...r })); $('result').textContent = ''; setStatus(''); $('cancel').hidden = true; $('run').disabled = false; $('run').textContent = 'Run this check again'; if (!ok) showPrecomputed(message ?? ''); };
  w.onmessage = (e) => {
    if (finished) return;
    const m = e.data, now = performance.now();
    if (m.type === 'progress') {
      progress++; lastProg = now; last = m;
      if (progress % 40 === 1) log(`PROGRESS ${(now - t0).toFixed(0)} ms: ${m.stage} files ${m.files} linear ${m.linearMB} MiB`);
      const cw = q.get('cancelWhen');       // a test switch: press Cancel when the first progress message of that stage arrives
      if (cw && cancelledAt === null && myRun === 1 && String(m.stage).includes(cw)) { cancelledAt = now; log(`CANCELWHEN ${cw} at ${Math.round(now - t0)} ms (stage ${m.stage})`); $('cancel').click(); }
    }
    else if (m.type === 'log') log('WORKERLOG ' + m.text);
    else if (m.type === 'rows') { rowBatches++; for (const r of m.rows) all.push(r); }
    else if (m.type === 'rowsEnd') rowsEnd = m;
    else if (m.type === 'failed') {
      let d = {}; try { d = JSON.parse(m.detail); } catch { d = { error: String(m.detail) }; }
      const verdict = d.error === 'runtime-abort' ? 'runtime-abort' : String(d.error).startsWith('input-') ? d.error : 'failed-in-worker';
      const fl = council.inputs.find((f) => f.path === d.file); const fname = fl ? `${fl.file} (${fl.period})` : (d.file ?? 'a file');
      const message = verdict === 'runtime-abort' ? 'The browser ran out of memory inside the check and the runtime stopped. The prepared results are the way to see this council here.'
        : verdict === 'input-digest-mismatch' ? `The downloaded file ${fname} is not the file we hold (its fingerprint differs), so the run was stopped before it was used; this usually means a broken download, so trying again may work.`
        : verdict === 'input-missing' || verdict === 'input-not-in-manifest' ? `The file ${fname} could not be fetched (${d.detail ?? d.error}), so the run was stopped.`
        : `The check stopped (${d.error ?? 'error'}${d.lastStep ? ' after ' + d.lastStep : ''}). The prepared results are the way to see this council here.`;
      result({ verdict, detail: m.detail, last }, message);
    }
    else if (m.type === 'done') {
      const ok = rowsEnd !== null && rowsEnd.rows === all.length;
      if (!ok) { result({ verdict: 'completed-rows-missing', received: all.length, announced: rowsEnd?.rows ?? null, last }, 'The check finished but the list of flagged rows did not arrive complete, so it is not shown.'); return; }
      const wallMs = performance.now() - t0; const t1 = performance.now();
      buildResults(wallMs / 1000);
      result({ verdict: 'completed', rows: all.length, rowBytes: rowsEnd.bytes, rowBatches, buildMs: Math.round(performance.now() - t1), progressEvents: progress, last, summary: m.summary }, '', true);
    }
    else if (m.type === 'error') result({ verdict: 'failed', error: m.error, last }, `The check stopped (${m.error}). The prepared results are the way to see this council here.`);
  };
  w.onerror = (e) => result({ verdict: 'worker-error', error: e.message, last }, 'The check could not start. The prepared results are the way to see this council here.');
  $('cancel').onclick = () => {
    if (finished) return; const t = performance.now(); stop();
    const returned = Math.round(performance.now() - t);
    $('cancel').hidden = true; $('run').disabled = false; setStatus(''); showPrecomputed('Cancelled. The prepared results are the way to see this council here.');
    setTimeout(() => { log('RESULT ' + JSON.stringify({ slug, runNo: myRun, verdict: 'cancelled', cancelRequestedAtMs: Math.round(t - t0), terminateReturnedAfterMs: returned, viewShownAfterMs: Math.round(performance.now() - t), mainThreadMaxBlockMs: Math.round(maxBlock), last })); if (q.get('then') === 'again' && myRun === 1) setTimeout(() => $('run').click(), 1000); });
  };
  // the worker gets exactly what it needs: the council's input list with digests, and the measured settings; the files are fetched from the hosted paths (relative to the worker's own address), one at a time as the engine asks
  w.postMessage({ slug, inputs: council.inputs, window: 4, softHeapLimit: est.softHeapLimit ?? null, retries: q.has('retries') ? Number(q.get('retries')) : 3, crash: q.get('crash'), throwAfter: q.get('throwAfter') ?? '', checkDigests: q.get('nodigest') !== '1' });
  log('CLICKRUN ' + JSON.stringify({ slug, runNo: myRun, atMs: Math.round(t0) }));
}
