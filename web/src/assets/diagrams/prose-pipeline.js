// Prose signature diagram: "From text to new text". Rules parse a sentence, mine its grammar into tables, and recombine only
// what was observed into new sentences that a validity gate re-parses before they are kept.
// Data in: prose-pipeline.json, written by web/generators/prose-pipeline by running the PUBLISHED package: the rules' own parse
// of plain sentences, the tables mined from three public-domain novels, twelve sentences generated from them with the table
// rows each one used, and a larger run of 2,000 requests checked by the package's own gate.
import { hasDom, tablist } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n0 = (n) => Number(n).toLocaleString('en-GB');

// ---------------------------------------------------------------- pure data helpers
export function metrics(d) {
  const gen = d.generated, b = d.bulk;
  return {
    inputs: d.inputs, gen, corpus: d.corpus, bulk: b,
    allGate: gen.every((g) => g.gate), allAttested: gen.every((g) => g.attested),
    bulkAllPass: b.gatePass === b.produced && b.attestedPass === b.produced,
    withObject: gen.filter((g) => g.obj).length,
  };
}

// Numbers the product page quotes in prose, taken from the data so the sentence cannot go stale.
export function limitFacts(m) {
  return {
    sentences: n0(m.corpus.sentences), vocabulary: n0(m.corpus.vocabulary), templates: n0(m.corpus.templates),
    requested: n0(m.bulk.requested), produced: n0(m.bulk.produced), distinct: n0(m.bulk.distinct), gatePass: n0(m.bulk.gatePass), attestedPass: n0(m.bulk.attestedPass),
    books: String(m.corpus.books.length),
  };
}

export function parseLine(s) {
  const bits = [];
  if (s.subject) bits.push(`subject ${s.subject}`);
  if (s.verbs.length) bits.push(`${s.verbs.length > 1 ? 'verbs' : 'verb'} ${s.verbs.join(', ')}`);
  if (s.indirect) bits.push(`indirect object ${s.indirect}`);
  if (s.objects.length) bits.push(`${s.objects.length > 1 ? 'objects' : 'object'} ${s.objects.join(', ')}`);
  if (s.predicate) bits.push(`predicate ${s.predicate}`);
  return bits.join('; ');
}

export function lineageFor(d, g) {
  const lines = [];
  const sv = (d.svTop[g.subjectLemma] || []);
  lines.push(`Subject ${g.subjectLemma} with verb ${g.verbLemma}: seen ${n0(g.svCount)} time${g.svCount === 1 ? '' : 's'} in the novels.`);
  if (g.obj) lines.push(`Verb ${g.verbLemma} with object ${g.objectLemma}: seen ${n0(g.voCount)} time${g.voCount === 1 ? '' : 's'}.`);
  lines.push(`Validity gate: ${g.gate ? 'passed on re-parse' : 'failed'}. Every word observed in the corpus: ${g.attested ? 'yes' : 'no'}.`);
  return { lines, sv };
}

export function readoutFor(d, input, i, gi) {
  const s = d.inputs[input].sentences[i], g = d.generated[gi], l = lineageFor(d, g);
  return `Parsed: "${s.text}" has ${parseLine(s)}. Generated: "${g.text}" ${l.lines.join(' ')}`;
}

// ---------------------------------------------------------------- server-rendered pieces
export function staticSvg(d) {
  const m = metrics(d), W = 640, H = 150, bw = 138, gap = 29;
  const steps = [
    ['Read', 'plain sentences'], ['Parse', 'rules, no model'], ['Mine', `${n0(m.corpus.sentences)} sentences`], ['Recombine', `${n0(m.bulk.produced)} sentences, all valid`],
  ];
  const boxes = steps.map(([t, s], k) => { const x = 8 + k * (bw + gap); return `<g><rect class="bx" x="${x}" y="40" width="${bw}" height="66" rx="10"/><text class="t" x="${x + bw / 2}" y="68" text-anchor="middle">${esc(t)}</text><text class="s" x="${x + bw / 2}" y="88" text-anchor="middle">${esc(s)}</text>${k < 3 ? `<path class="ar" d="M${x + bw + 6} 73 h${gap - 12} m-6 -5 l6 5 l-6 5"/>` : ''}</g>`; }).join('');
  return `<svg class="dia-plan pp-plan" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="pp-t pp-d" preserveAspectRatio="xMidYMid meet"><title id="pp-t">The Prose pipeline</title><desc id="pp-d">Four stages: read plain sentences, parse them with deterministic rules, mine the grammar into tables, and recombine only observed pairs into new sentences that are re-checked before they are kept.</desc>${boxes}</svg>`;
}

export function controlsHtml(id, m) {
  const tabs = m.inputs.map((inp, k) => `<button type="button" role="tab" id="${id}-tab-${inp.id}" data-mode="${k}" aria-selected="${k === 0}" aria-controls="${id}-stage" tabindex="${k === 0 ? 0 : -1}">${esc(inp.label)}</button>`).join('');
  return {
    tabs: `<div class="dia-tabs dia-ui" role="tablist" aria-label="Sentences to parse" hidden>${tabs}</div>`,
    controls: `<p class="dia-hint dia-ui" hidden>Pick a sentence to parse, and a generated sentence to trace.</p>
<p class="dia-readout dia-ui" data-ctl="readout" role="status" hidden>Four stages: read, parse, mine, recombine.</p>
<dl class="stats dia-stats dia-ui" data-ctl="stats" hidden><div class="stat"><dt>Sentences mined</dt><dd>${n0(m.corpus.sentences)}</dd></div><div class="stat"><dt>Distinct words</dt><dd>${n0(m.corpus.vocabulary)}</dd></div><div class="stat"><dt>Sentence shapes</dt><dd>${n0(m.corpus.templates)}</dd></div><div class="stat"><dt>Generated, passed the gate</dt><dd>${n0(m.bulk.gatePass)} of ${n0(m.bulk.produced)}</dd></div></dl>`,
  };
}

export function tablesHtml(d, m) {
  const row = (cells, head) => '<tr>' + cells.map((c, k) => (k === 0 && head ? `<th scope="row">${esc(c)}</th>` : `<td>${esc(c)}</td>`)).join('') + '</tr>';
  const parse = d.inputs.flatMap((inp) => inp.sentences.map((s) => row([s.text, s.subject || '', s.verbs.join(', '), s.indirect || '', s.objects.join(', '), s.predicate || '', s.template], true))).join('');
  const gen = d.generated.map((g) => row([g.text, `${g.subjectLemma} with ${g.verbLemma}: ${n0(g.svCount)}`, g.obj ? `${g.verbLemma} with ${g.objectLemma}: ${n0(g.voCount)}` : 'none', g.gate ? 'passed' : 'failed', g.attested ? 'yes' : 'no'], true)).join('');
  const qa = d.qa.map((q) => row([q.context, q.question, q.answer], true)).join('');
  return `<details class="dia-data"><summary>Data behind the diagram</summary>
<div class="tbl-wrap"><table class="tbl"><caption>How the rules parsed each sentence shown</caption>
<thead><tr><th scope="col">Sentence</th><th scope="col">Subject</th><th scope="col">Verbs</th><th scope="col">Indirect object</th><th scope="col">Objects</th><th scope="col">Predicate</th><th scope="col">Template</th></tr></thead><tbody>${parse}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Twelve generated sentences and the mined counts behind each one</caption>
<thead><tr><th scope="col">Sentence</th><th scope="col">Subject with verb (times seen)</th><th scope="col">Verb with object (times seen)</th><th scope="col">Validity gate</th><th scope="col">Every word observed</th></tr></thead><tbody>${gen}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>Reading-comprehension pairs generated from the same tables</caption>
<thead><tr><th scope="col">Context</th><th scope="col">Question</th><th scope="col">Answer</th></tr></thead><tbody>${qa}</tbody></table></div>
<div class="tbl-wrap"><table class="tbl"><caption>A larger run: ${n0(m.bulk.requested)} sentences requested, each re-checked with the package's own gate</caption>
<thead><tr><th scope="col">Measure</th><th scope="col">Count</th></tr></thead><tbody>${row(['Sentences returned', n0(m.bulk.produced)], true)}${row(['Distinct sentences', n0(m.bulk.distinct)], true)}${row(['Passed the validity gate', n0(m.bulk.gatePass)], true)}${row(['Every word observed in the corpus', n0(m.bulk.attestedPass)], true)}</tbody></table></div>
</details>`;
}

export function verify(d, body) {
  const m = metrics(d), out = [];
  if (!m.allGate) out.push('a generated sentence failed the validity gate');
  if (!m.allAttested) out.push('a generated sentence used an unobserved word');
  if (!m.bulkAllPass) out.push('the bulk run has sentences that failed the gate or the attested check');
  if (m.bulk.distinct !== m.bulk.produced) out.push('the bulk run returned duplicates');
  for (const g of m.gen) {
    if (!d.svTop[g.subjectLemma]) out.push(`no mined row for subject ${g.subjectLemma}`);
    if (g.svCount < 1) out.push(`${g.text}: subject and verb pair was never observed`);
    if (g.obj && g.voCount < 1) out.push(`${g.text}: verb and object pair was never observed`);
  }
  if (!m.corpus.strict) out.push('the corpus is below the size at which generation uses observed pairs only');
  if (!/Validity gate|Every word observed/.test(body)) out.push('generated sentence table missing');
  return out;
}

// ---------------------------------------------------------------- browser
const chip = (t) => `<span class="pp-chip r-${t[2]}" title="${esc(t[1])}${t[2] !== 'None' ? ', ' + esc(t[2]) : ''}"><b>${esc(t[0])}</b><i>${esc(t[1])}</i></span>`;

export function mount(root, d) {
  const m = metrics(d), stage = root.querySelector('.dia-stage');
  stage.textContent = '';
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const q = (s) => root.querySelector(s);
  const readout = q('[data-ctl=readout]');
  const S = { input: 0, i: 0, g: 0 };
  const col = (n, title, body, note) => `<section class="pp-col" aria-labelledby="pp-h${n}"><h3 id="pp-h${n}"><span class="pp-n" aria-hidden="true">${n}</span>${esc(title)}</h3>${body}${note ? `<p class="pp-note">${esc(note)}</p>` : ''}</section>`;
  function render() {
    const inp = d.inputs[S.input], s = inp.sentences[S.i], g = d.generated[S.g], l = lineageFor(d, g);
    const read = `<ul class="pp-list">${inp.sentences.map((x, k) => `<li><button type="button" class="pp-pick" data-s="${k}" aria-pressed="${k === S.i}">${esc(x.text)}</button></li>`).join('')}</ul>`;
    const parse = `<div class="pp-chips" role="group" aria-label="Tagged words">${s.toks.map(chip).join('')}</div><p class="pp-template">${esc(s.template)}</p><p class="pp-sum">${esc(parseLine(s))}${s.copular ? ' (copular)' : ''}.</p><p class="pp-legend"><span class="r-Subject">subject</span><span class="r-Verb">verb</span><span class="r-IndObject">indirect object</span><span class="r-Object">object</span><span class="r-Predicate">predicate</span></p>`;
    const svRows = (d.svTop[g.subjectLemma] || []).map(([v, c]) => `<tr${v === g.verbLemma ? ' class="on"' : ''}><td>${esc(g.subjectLemma)} ${esc(v)}</td><td class="num">${n0(c)}</td></tr>`).join('');
    const voRows = g.obj ? (d.voTop[g.verbLemma] || []).map(([o, c]) => `<tr${o === g.objectLemma ? ' class="on"' : ''}><td>${esc(g.verbLemma)} ${esc(o)}</td><td class="num">${n0(c)}</td></tr>`).join('') : '';
    const mine = `<p class="pp-sum">${n0(m.corpus.sentences)} sentences from ${m.corpus.books.length} novels gave ${n0(m.corpus.vocabulary)} distinct words and ${n0(m.corpus.templates)} sentence shapes.</p><table class="pp-tbl"><caption>What the tables hold for the subject of the selected generated sentence</caption><tbody>${svRows}</tbody></table>${voRows ? `<table class="pp-tbl"><caption>And for its verb</caption><tbody>${voRows}</tbody></table>` : ''}`;
    const gen = `<ul class="pp-list">${d.generated.map((x, k) => `<li><button type="button" class="pp-pick" data-g="${k}" aria-pressed="${k === S.g}">${esc(x.text)}</button></li>`).join('')}</ul><div class="pp-chips" role="group" aria-label="Tagged words">${g.toks.map(chip).join('')}</div><ul class="pp-lin">${l.lines.map((x) => `<li>${esc(x)}</li>`).join('')}</ul>`;
    stage.innerHTML = `<div class="pp">${col(1, 'Read', read, 'Plain text in. No model is involved.')}${col(2, 'Parse', parse)}${col(3, 'Mine', mine)}${col(4, 'Recombine', gen)}</div>`;
    readout.textContent = readoutFor(d, S.input, S.i, S.g);
  }
  stage.addEventListener('click', (e) => {
    const b = e.target.closest('button.pp-pick'); if (!b) return;
    if (b.dataset.s != null) S.i = Number(b.dataset.s); else S.g = Number(b.dataset.g);
    const top = stage.scrollTop; render(); stage.scrollTop = top;
    const again = stage.querySelector(`button[data-${b.dataset.s != null ? 's' : 'g'}="${b.dataset.s ?? b.dataset.g}"]`); if (again) again.focus();
  });
  tablist(q('[role="tablist"]'), (mode) => { S.input = Number(mode); S.i = 0; render(); });
  render();
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="prose-pipeline"]').forEach((root) => {
    const blob = root.querySelector('script[type="application/json"]');
    if (blob) mount(root, JSON.parse(blob.textContent));
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}

export const interactiveStage = false;
