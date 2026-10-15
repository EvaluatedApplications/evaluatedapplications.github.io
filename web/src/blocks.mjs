// A research section is either the original shape (paragraphs, then list, then tables) or an ordered list of blocks:
//   { t: 'p', text }   { t: 'quote', text }   { t: 'sub', text }   { t: 'ul', items[] }   { t: 'table', caption, columns[], rows[][] }
// blocksOf() turns both into the ordered form so the page, the word count and the machine-readable files agree.
export function blocksOf(sec) {
  if (sec.blocks) return sec.blocks;
  return [
    ...(sec.paragraphs || []).map((text) => ({ t: 'p', text })),
    ...(sec.list ? [{ t: 'ul', items: sec.list }] : []),
    ...(sec.tables || []).map((tb) => ({ t: 'table', ...tb })),
  ];
}

export function sectionTables(sec) {
  return blocksOf(sec).filter((b) => b.t === 'table');
}

// plain words of a section, for the reading time
export function sectionWords(sec) {
  return blocksOf(sec).reduce((a, b) => a + (b.t === 'ul' ? b.items.join(' ') : b.text || '').split(/\s+/).filter(Boolean).length, 0);
}
