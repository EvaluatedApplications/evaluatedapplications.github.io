#!/usr/bin/env node
// One command: build, voice lint, site check.   node web/scripts/run.mjs [--versions=...]   (or: npm run site)
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const extra = process.argv.slice(2);
for (const [name, args] of [['build', extra], ['lint-voice', []], ['check-site', []]]) {
  console.log(`\n== ${name} ==`);
  const r = spawnSync(process.execPath, [path.join(here, name + '.mjs'), ...args], { stdio: 'inherit' });
  if (r.status !== 0) { console.error(`\n${name} failed`); process.exit(r.status || 1); }
}
console.log('\nall steps passed');
