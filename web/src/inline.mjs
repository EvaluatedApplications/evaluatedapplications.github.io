// A small safe markup subset for article prose. Everything is escaped first, then three constructs are allowed:
//   [text](/path or https://url)   a link         **bold**   emphasis        [@id]   a numbered citation of a reference
import { raw, esc } from '../scripts/lib/html.mjs';

export function inline(text, refs = {}) {
  let s = esc(text);
  s = s.replace(/\[@([a-z0-9-]+)\]/g, (m, id) => (refs[id] ? `<a class="cite" href="#ref-${id}" aria-label="Reference ${refs[id]}">[${refs[id]}]</a>` : m));
  s = s.replace(/\[([^\]]+)\]\(((?:\/|https:\/\/)[^)\s]*)\)/g, (m, t, u) => `<a href="${u}"${u.startsWith('http') ? ' rel="noopener"' : ''}>${t}</a>`);
  s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  return raw(s);
}

// plain text of a paragraph (markup removed), for word counts and machine-readable files
export const plain = (text) => String(text).replace(/\[@[a-z0-9-]+\]/g, '').replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').replace(/\*\*([^*]+)\*\*/g, '$1');