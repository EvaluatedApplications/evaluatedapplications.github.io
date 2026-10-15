// sitemap.xml, robots.txt, llms.txt and llms-full.txt, all composed from the same data the pages use.
// Nothing here is typed per product: counts, versions, install lines, evidence numbers and licence text are generated.
import fs from 'node:fs';
import path from 'node:path';
import { DATA } from './data.mjs';
import { stripTags } from './html.mjs';
import { plain } from '../../src/inline.mjs';

const abs = (ctx, u) => (u.startsWith('http') ? u : ctx.site.url + u);
const readSnip = (rel) => fs.readFileSync(path.join(DATA, rel), 'utf8').replace(/\r\n/g, '\n').trimEnd();

export function composeSitemap(ctx, pages) {
  const urls = pages.filter((p) => !p.noindex && !p.hidden).map((p) => abs(ctx, p.url));
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls.map((u) => `  <url><loc>${u}</loc></url>`).join('\n')}\n</urlset>\n`;
}

const AI_AGENTS = ['GPTBot', 'ChatGPT-User', 'ClaudeBot', 'Claude-Web', 'anthropic-ai', 'PerplexityBot', 'Google-Extended', 'CCBot'];
export function composeRobots(ctx) {
  return `User-agent: *\nAllow: /\n\n# This site is open to be read, indexed and cited by AI systems.\n# /llms.txt is the short summary and /llms-full.txt describes every product.\n${AI_AGENTS.map((a) => `User-agent: ${a}\nAllow: /\n`).join('\n')}\nSitemap: ${ctx.site.url}/sitemap.xml\n`;
}

function productLine(ctx, p) {
  const primary = p.packages.find((x) => x.role === 'primary');
  return `- [${p.name}](${abs(ctx, p.href)}): ${p.pitch} \`dotnet add package ${primary.id}\` (release ${primary.version}).`;
}

export function composeLlms(ctx, stamp) {
  const s = ctx.site;
  const L = [];
  L.push(`# ${s.name}`, '', `> ${s.description} ${ctx.licence.oneLine}`, '', `Updated: ${stamp}`, '');
  L.push('## Products', '', ...ctx.products.map((p) => productLine(ctx, p)), '');
  L.push('## Tools', '', ...ctx.tools.map((t) => `- [${t.name}](${abs(ctx, t.href)}): ${t.line}${t.poweredBy.length ? ` Powered by ${t.poweredBy.map((id) => ctx.byId[id].name).join(' and ')}.` : ''}`), '');
  if (ctx.evidence.length) L.push('## Evidence', '', ...ctx.evidence.map((e) => `- [${e.title}](${abs(ctx, e.slug)}): ${e.claim}`), '');
  L.push('## Research', '', ...ctx.research.map((r) => `- [${r.title}](${abs(ctx, r.href)}): ${r.summary}`), '');
  if (ctx.pageList) L.push('## Every page', '', ...ctx.pageList.filter((p) => !p.noindex && !p.hidden && p.url !== '/').map((p) => `- [${p.title}](${abs(ctx, p.url)})`), '');
  L.push('## Company', '', `- [Partner with us](${s.url}/partner/): ${ctx.pages.partner.lede}`, `- [Licence](${s.url}/licence/): ${ctx.licence.ribbon}`, `- [For AI readers](${s.url}/ai/): a plain summary for AI systems`, `- [NuGet profile](${s.navExternal.href}): every package we publish`, `- Contact: ${s.contactEmail}`, '');
  L.push('## Machine-readable', '', `- [llms-full.txt](${s.url}/llms-full.txt): every product described in plain text`, `- [sitemap.xml](${s.url}/sitemap.xml)`, '');
  return L.join('\n');
}

function table(tb) {
  if (tb.kind === 'matrix') return [tb.caption, '', `| ${tb.columns.join(' | ')} |`, `|${tb.columns.map(() => '---').join('|')}|`, ...tb.rows.map((r) => `| ${r.join(' | ')} |`)].join('\n');
  const cols = ['Query', ...tb.columns, 'Result'];
  const rows = tb.rows.map((r) => [r.label, ...r.values.map((v) => v.toFixed(2)), r.verdict]);
  return [`${tb.caption}`, '', `| ${cols.join(' | ')} |`, `|${cols.map(() => '---').join('|')}|`, ...rows.map((r) => `| ${r.join(' | ')} |`)].join('\n');
}

export function composeLlmsFull(ctx, stamp) {
  const s = ctx.site;
  const L = [];
  L.push(`# ${s.name}: products, tools and evidence in full`, '', `${s.description}`, '', `Updated: ${stamp}`, '', '## Licence', '', ...ctx.licence.paragraphs.flatMap((t) => [t, '']));
  L.push('## Products', '');
  for (const p of ctx.products) {
    const primary = p.packages.find((x) => x.role === 'primary');
    L.push(`### ${p.name}: ${p.tagline}`, '', p.pitch, '');
    L.push(`Page: ${abs(ctx, p.href)}`);
    for (const pkg of p.packages) L.push(`- Package ${pkg.id}, release ${pkg.version}: \`dotnet add package ${pkg.id}\``);
    if (p.dependsOn.length) L.push(`- Builds on: ${p.dependsOn.map((d) => ctx.byId[d].name).join(', ')}`);
    if (p.usedBy.length) L.push(`- Used by: ${p.usedBy.map((d) => ctx.byId[d].name).join(', ')}`);
    if (p.tools.length) L.push(`- Tools: ${p.tools.map((t) => t.name).join(', ')}`);
    L.push('');
    const d = p.doc;
    if (d) {
      L.push([].concat(d.intro).join('\n\n'), '');
      if (d.why) L.push('Why use it:', ...d.why.map((w) => `- ${w.title}. ${w.text}`), '');
      if (d.capabilities) L.push('Capabilities:', ...d.capabilities.map((c) => `- ${c.title}. ${c.text}`), '');
      if (d.quickstart) L.push('Quick start:', '', '```csharp', readSnip(d.quickstart.source), '```', '', 'Output:', '', '```text', readSnip(d.quickstart.output), '```', '');
      if (d.limits) L.push('Limits:', ...d.limits.map((l) => `- ${l.replace(/\{(\w+)\}/g, (_, k) => (ctx.limitFacts[p.id] || {})[k] ?? `{${k}}`)}`), '');
    }
    for (const sp of p.subpages || []) {
      const sd = sp.doc, fact = ctx.limitFacts[p.id] || {};
      L.push(`#### ${sd.h1 || sd.title} (${abs(ctx, p.slug + sp.path + '/')})`, '', sd.intro, '');
      for (const sec of sd.sections) {
        L.push(`##### ${sec.title}`, '');
        for (const t of sec.paragraphs || []) L.push(t.replace(/\{(\w+)\}/g, (_, k) => fact[k] ?? `{${k}}`), '');
        for (const x of sec.list || []) L.push(`- ${x.term}: ${x.text}`);
        if (sec.list) L.push('');
        for (const t of sec.paragraphsAfter || []) L.push(t, '');
        for (const sn of sec.snippets || []) { if (sn.text) { L.push('```' + (sn.lang || 'sql'), sn.text.trim(), '```', ''); continue; } L.push('```csharp', readSnip(sn.source), '```', ''); if (sn.output) L.push('Output:', '', '```text', readSnip(sn.output), '```', ''); }
      }
    }
    L.push(`Licence: ${ctx.licence.ribbon} Terms at ${s.url}/licence/`, '');
  }
  L.push('## Tools', '');
  for (const t of ctx.tools) L.push(`### ${t.name}`, '', t.line, '', `Page: ${abs(ctx, t.href)}`, t.poweredBy.length ? `Powered by ${t.poweredBy.map((id) => ctx.byId[id].name).join(' and ')}.` : '', '');
  if (ctx.evidence.length) {
    L.push('## Evidence', '');
    for (const e of ctx.evidence) {
      L.push(`### ${e.title}`, '', e.claim, '', `Page: ${abs(ctx, e.slug)}`, '', 'Method:');
      const m = e.method;
      L.push(...[['Workload', m.workload], ['Engines', m.engines.join('; ')], ['Check', m.verification], ['Statistic', m.statistic], ['Machine', m.machine], ['Memory and logging', m.memory], ['Ties', m.tie]].filter(([, v]) => v).map(([k, v]) => `- ${k}: ${v}`), '');
      if (e.losses?.length) L.push('Where it loses:', ...e.losses.map((l) => `- ${l}`), '');
      for (const s of e.sections || [{ title: 'Results', intro: [], tables: e.tables, notes: [], losses: [] }]) {
        L.push('#### ' + s.title, '', ...(s.intro || []).flatMap((t) => [t, '']));
        for (const tb of s.tables || []) L.push(table(tb), '');
        for (const t of s.notes || []) L.push(t, '');
        if (s.losses?.length) L.push('Where it is slower:', ...s.losses.map((l) => `- ${l}`), '');
      }
      L.push('Limits:', ...e.limits.map((l) => `- ${l}`), '');
    }
  }
  L.push('## Research', '');
  for (const r of ctx.research) {
    L.push(`### ${r.title}`, '', `Page: ${abs(ctx, r.href)}`, `Topics: ${r.topics.join(', ')}`, '', `Finding: ${plain(r.finding)}`, '');
    for (const sec of r.sections) {
      if (['finding', 'references', 'reference'].includes(sec.id)) continue;
      L.push(`#### ${sec.title}`, '', ...(sec.paragraphs || []).flatMap((t) => [plain(t), '']));
      for (const tb of sec.tables || []) L.push(table({ kind: 'matrix', ...tb }), '');
    }
    if (r.references.length) L.push('References:', ...r.references.map((x, i) => `${i + 1}. ${x.text}${x.url ? ' ' + x.url : ''}`), '');
    if (r.relatedArticles.length) L.push('Related: ' + r.relatedArticles.map((a) => `${a.title} ${abs(ctx, a.href)}`).join('; '), '');
  }
  return L.join('\n');
}
