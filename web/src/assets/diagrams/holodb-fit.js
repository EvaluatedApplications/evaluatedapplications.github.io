// Decision helper: "When HoloDb fits and when it does not". The visitor sets a few things about their workload and gets one of three
// answers (good fit, possible, use something else), the reasons one line each, and the alternative named where it is the better choice.
// Rules and wording live in data/diagrams/holodb-fit.json, written by scripts/migrate/extract-holodb-story.mjs from measured results
// and the limits audit. Without JavaScript the same content is a plain list. Module contract: metrics / staticSvg / controlsHtml /
// tablesHtml / verify / mount.
import { hasDom, prefersReducedMotion } from './core.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

export function defaults(d) { return Object.fromEntries(d.questions.map((q) => [q.id, q.default])); }

const matches = (rule, a) => Object.entries(rule.when).every(([k, vs]) => vs.includes(a[k]));

// score: blocker wins, then the sum of cautions
export function evaluate(d, a) {
  const hit = d.rules.filter((r) => matches(r, a));
  const blockers = hit.filter((r) => r.kind === 'blocker'), cautions = hit.filter((r) => r.kind === 'caution'), pluses = hit.filter((r) => r.kind === 'plus');
  const weight = cautions.reduce((s, r) => s + r.weight, 0);
  const verdict = blockers.length || weight >= d.thresholds.other ? 'other' : weight >= d.thresholds.possible ? 'possible' : 'good';
  const alts = [];
  for (const r of [...blockers, ...cautions]) if (r.alt && !alts.includes(r.alt)) alts.push(r.alt);
  return { verdict, weight, blockers, cautions, pluses, alts };
}

export function metrics(d) {
  const base = defaults(d), e = evaluate(d, base);
  return { questions: d.questions.length, rules: d.rules.length, defaultVerdict: e.verdict };
}

const VERD = { good: 'Good fit', possible: 'Possible', other: 'Use something else' };

function resultHtml(d, a) {
  const e = evaluate(d, a), V = d.verdicts[e.verdict];
  const li = (r, cls) => `<li class="fit-${cls}"><span class="fit-tag">${esc(d.kindLabels[cls])}</span> ${esc(r.line)}</li>`;
  const reasons = [...e.blockers.map((r) => li(r, 'blocker')), ...e.cautions.map((r) => li(r, 'caution')), ...e.pluses.map((r) => li(r, 'plus'))].join('');
  const alts = e.alts.length ? `<div class="fit-alts"><h4>${e.verdict === 'good' ? 'Also worth knowing' : 'What to use instead, or beside it'}</h4><ul>${e.alts.map((id) => `<li><b>${esc(d.alts[id].name)}</b> ${esc(d.alts[id].line)}</li>`).join('')}</ul></div>` : `<p class="fit-none">${esc(d.noAlt)}</p>`;
  return `<div class="fit-card v-${e.verdict}"><p class="fit-verdict"><span class="fit-dot"></span>${esc(VERD[e.verdict])}</p><p class="fit-line">${esc(V.line)}</p><ul class="fit-reasons">${reasons}</ul>${alts}</div>`;
}

export function staticSvg(d) {
  // no-JS fallback: the rules as a list, grouped by question
  const items = d.questions.map((q) => `<li><b>${esc(q.label)}</b><ul>${q.options.map((o) => {
    const rs = d.rules.filter((r) => r.when[q.id] && r.when[q.id].includes(o.id) && Object.keys(r.when).length === 1);
    return `<li>${esc(o.label)}${rs.length ? ': ' + rs.map((r) => esc(r.line)).join(' ') : ''}</li>`;
  }).join('')}</ul></li>`).join('');
  return `<div class="fit-static" data-static-html role="group" aria-label="When HoloDb fits and when it does not"><p>Answers in the questions below decide the verdict. With scripting off, here is each answer and what it means.</p><ul>${items}</ul></div>`;
}

export function controlsHtml(id, m, d) {
  const qs = d.questions.map((q) => `<fieldset class="fit-q"><legend>${esc(q.label)}</legend><span class="dia-seg" role="group" aria-label="${esc(q.label)}">${q.options.map((o) => `<button type="button" class="chip" data-fq="${q.id}" data-fa="${o.id}" aria-pressed="${o.id === q.default}">${esc(o.label)}</button>`).join('')}</span></fieldset>`).join('');
  return { tabs: '', controls: `<div class="fit-ui dia-ui" hidden><div class="fit-form">${qs}</div><div class="fit-out" data-ctl="out" role="status" aria-live="polite">${resultHtml(d, defaults(d))}</div></div>` };
}

export function tablesHtml(d) {
  const rows = d.rules.map((r) => `<tr><th scope="row">${esc(Object.entries(r.when).map(([k, vs]) => { const q = d.questions.find((x) => x.id === k); return `${q.short}: ${vs.map((v) => q.options.find((o) => o.id === v).label).join(' or ')}`; }).join(', and '))}</th><td>${esc(d.kindLabels[r.kind])}</td><td>${esc(r.line)}</td></tr>`).join('');
  return `<details class="dia-data"><summary>Every rule behind the answer</summary><div class="tbl-wrap"><table class="tbl"><caption>The rules: your answer, how it counts, and why</caption><thead><tr><th scope="col">If</th><th scope="col">Counts as</th><th scope="col">Why</th></tr></thead><tbody>${rows}</tbody></table></div><p class="muted">${esc(d.rule)}</p></details>`;
}

export function verify(d, body) {
  const out = [];
  for (const r of d.rules) {
    for (const [k, vs] of Object.entries(r.when)) { const q = d.questions.find((x) => x.id === k); if (!q) out.push(`rule on unknown question ${k}`); else for (const v of vs) if (!q.options.some((o) => o.id === v)) out.push(`rule on unknown answer ${k}=${v}`); }
    if (r.alt && !d.alts[r.alt]) out.push('unknown alternative ' + r.alt);
    if (r.kind === 'caution' && !(r.weight >= 1)) out.push('a caution needs a weight');
  }
  for (const q of d.questions) if (!q.options.some((o) => o.id === q.default)) out.push(`default answer missing for ${q.id}`);
  // the three verdicts must be reachable from the data
  const seen = new Set();
  const walk = (i, a) => { if (i === d.questions.length) { seen.add(evaluate(d, a).verdict); return; } for (const o of d.questions[i].options) walk(i + 1, { ...a, [d.questions[i].id]: o.id }); };
  if (d.questions.reduce((n, q) => n * q.options.length, 1) <= 200000) walk(0, {});
  for (const v of ['good', 'possible', 'other']) if (!seen.has(v)) out.push('verdict never reached: ' + v);
  if ((body.match(/<tbody>[\s\S]*?<\/tbody>/) || [''])[0].split('<tr>').length - 1 !== d.rules.length) out.push('rule table row count');
  return out;
}

export const interactiveStage = false;

export function mount(root, d) {
  void prefersReducedMotion();
  root.querySelectorAll('.dia-ui').forEach((el) => { el.hidden = false; });
  const stage = root.querySelector('.dia-stage'); if (stage) stage.hidden = true;
  const A = defaults(d), out = root.querySelector('[data-ctl=out]');
  root.querySelectorAll('[data-fq]').forEach((b) => b.addEventListener('click', () => {
    A[b.dataset.fq] = b.dataset.fa;
    root.querySelectorAll(`[data-fq="${b.dataset.fq}"]`).forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    out.innerHTML = resultHtml(d, A);
    root.dataset.verdict = evaluate(d, A).verdict;
  }));
  root.dataset.verdict = evaluate(d, A).verdict;
  root.dataset.ready = 'true';
}

if (hasDom) {
  const start = () => document.querySelectorAll('[data-diagram="holodb-fit"]').forEach((root) => {
    const blob = root.querySelector('script[type="application/json"]');
    if (blob) mount(root, JSON.parse(blob.textContent));
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
}
