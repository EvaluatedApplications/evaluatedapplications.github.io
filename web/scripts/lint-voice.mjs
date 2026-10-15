#!/usr/bin/env node
// Voice lint for the built site (site-next/ by default). Classes A to H come from the site audit; every class
// is an error. Code blocks and quoted SQL are exempt, and so is anything generated from data and marked data-gen
// (versions, counts). The licence wording "from the owner" is allow-listed as legal text.
//   node web/scripts/lint-voice.mjs [dir]
import fs from 'node:fs';
import path from 'node:path';
import { OUT } from './lib/data.mjs';
import { stripTags } from './lib/html.mjs';

const MONTHS = 'January|February|March|April|May|June|July|August|September|October|November|December';
export const RULES = [
  ['A', 'dated sentence', [new RegExp(`\\b\\d{1,2}\\s+(${MONTHS})\\s+20\\d\\d\\b`), new RegExp(`\\b(${MONTHS})\\s+\\d{1,2},?\\s+20\\d\\d\\b`), /\b20\d\d-\d\d-\d\d\b/, /\b(this|last) (week|month)\b/i]],
  ['B', 'changelog or revision narration', [/\b(previously|no longer|supersed\w+|re-?runs?|is now|are now|formerly|used to be|earlier run|corrected:|the map said|new in v?\d|added in v?\d)\b/i]],
  ['C', 'internal id, process or role', [/\bEXP-?[A-Z]*\d+\b/, /\b(PB|PS)\d+\b/, /pre-?registered/i, /Genesis\//, /\bMonoRepo\b/, /last verified/i, /prepared for/i, /written down first/i, /experiment id/i, /\bthe doc\b/i, /\bowners?\b/i, /\bcoordinator\b/i, /\bdirective\b/i, /\beditorial\b/i, /site\.md/i, /\btodo\b/i]],
  ['D', 'run-condition boilerplate', [/one run on one machine/i, /not interpreted/i, /idle[- ]machine/i]],
  ['E', 'work-in-progress or hedge', [/\bR&D\b/, /subject to change/i, /early research/i, /work in progress/i, /\bprototype\b/i, /proof of concept/i, /still learning/i, /\bhonest(ly)?\b/i, /limitations included/i, /\bnot yet\b/i, /coming soon/i]],
  ['F', 'hard-coded version, count or size', [/\b\d+\.\d+\.\d+(\.\d+)?\b/, /\b(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve) (libraries|packages|tools|products)\b/i, /\b\d+ (libraries|packages|tools|products)\b/i, /\b\d[\d,]*\s+parameters\b/i, /~\s*\d+\s*MB/]],
  ['G', 'first person singular', [/\b(I|I'm|I've|I'd|my|me)\b/]],
  ['H', 'roadmap', [/what'?s next/i, /\bplanned\b/i, /\bdeferred\b/i, /future (tier|release|version)/i, /\broadmap\b/i, /\bupcoming\b/i, /\bwe plan\b/i]],
  ['L', 'licence wording', [/free to use/i, /\bMIT\b/, /open[- ]source/i, /Pricing: Free/i, /—/]],
];
const ALLOW = [/licen[cs]e from the owner/i, /^Copyright \(c\) 20\d\d/, /^© 20\d\d/];

export function visibleText(htmlSrc) {
  let s = htmlSrc.replace(/<script[\s\S]*?<\/script>/gi, ' ').replace(/<style[\s\S]*?<\/style>/gi, ' ').replace(/<pre[\s\S]*?<\/pre>/gi, ' ').replace(/<code[\s\S]*?<\/code>/gi, ' ').replace(/<svg[\s\S]*?<\/svg>/gi, ' ');
  // generated values: data-gen elements are exempt (a version or count read from data)
  s = s.replace(/<(\w+)[^>]*data-gen[^>]*>[\s\S]*?<\/\1>/gi, ' ');
  s = s.replace(/<\/t[dh]>/gi, ' | ').replace(/<(?:br|\/p|\/li|\/h\d|\/div|\/section|\/dd|\/dt|\/tr|\/caption|\/figcaption)[^>]*>/gi, '\n');
  return stripTags(s).replace(/ /g, ' ');
}

export function lintText(text, label) {
  const hits = [];
  text.split('\n').forEach((line, i) => {
    const l = line.trim();
    if (!l) return;
    let probe = l;
    for (const a of ALLOW) probe = probe.replace(a, ' ');
    for (const [cls, name, res] of RULES) for (const re of res) {
      const m = probe.match(re);
      if (m) hits.push({ cls, name, label, line: i + 1, match: m[0], context: l.slice(Math.max(0, (m.index ?? 0) - 40), (m.index ?? 0) + 60) });
    }
  });
  return hits;
}

export function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out); else out.push(p);
  }
  return out;
}

export function lintDir(dir) {
  const hits = [];
  for (const f of walk(dir)) {
    const rel = path.relative(dir, f).replace(/\\/g, '/');
    if (f.endsWith('.html')) {
      const src = fs.readFileSync(f, 'utf8');
      if (/<body data-stub/.test(src)) continue;
      hits.push(...lintText(visibleText(src), rel));
    } else if (/llms.*\.txt$/.test(f)) {
      // code fences and the machine "Updated:" stamp are exempt
      const t = fs.readFileSync(f, 'utf8').replace(/```[\s\S]*?```/g, '').replace(/^Updated: .*$/m, '').replace(/`[^`]*`/g, ' ').replace(/\(?release \d[\d.]*\)?/g, ' ');
      hits.push(...lintText(t, rel));
    }
  }
  return hits;
}

if (import.meta.url === `file:///${process.argv[1].replace(/\\/g, '/')}` || process.argv[1]?.endsWith('lint-voice.mjs')) {
  const dir = path.resolve(process.argv[2] || OUT);
  const hits = lintDir(dir);
  for (const h of hits) console.log(`${h.label}:${h.line} [${h.cls}] ${h.name}: "${h.match}"  ...${h.context}`);
  console.log(hits.length ? `voice lint: ${hits.length} problem(s)` : 'voice lint: clean');
  process.exit(hits.length ? 1 : 0);
}
