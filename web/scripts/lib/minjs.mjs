// Deliberately conservative JavaScript shrinker (no dependencies): drops comment-only lines, block comments that
// start a line, trailing "// ..." comments when the code before them has balanced quotes, leading indentation and
// blank lines. It never renames or reorders anything, and the build verifies the result still parses and behaves
// (check-site imports the built module and compares its metrics with the source's).
export function minJs(src) {
  const out = [];
  let inBlock = false;
  for (let line of src.replace(/\r\n/g, '\n').split('\n')) {
    let t = line.trim();
    if (inBlock) { if (t.includes('*/')) { inBlock = false; t = t.slice(t.indexOf('*/') + 2).trim(); } else continue; }
    if (t.startsWith('/*')) { if (!t.includes('*/')) { inBlock = true; continue; } t = t.slice(t.indexOf('*/') + 2).trim(); }
    if (!t || t.startsWith('//')) continue;
    const i = t.search(/\s\/\/\s/);
    if (i > 0) {
      const before = t.slice(0, i);
      const balanced = ["'", '"', '`'].every((q) => (before.split(q).length - 1 - (before.split('\\' + q).length - 1)) % 2 === 0);
      if (balanced) t = before.trimEnd();
    }
    out.push(t);
  }
  return out.join('\n') + '\n';
}
