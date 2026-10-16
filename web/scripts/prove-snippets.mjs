#!/usr/bin/env node
// Proves every code sample shown on the site: each file listed in data/snippets/manifest.json is compiled as a
// complete program against the PUBLISHED NuGet package and run; its real output is written next to it
// (<name>.out.txt) and the build fails if a sample no longer compiles. Needs the .NET SDK and network access to NuGet.
//   node web/scripts/prove-snippets.mjs [name ...]
// A manifest entry may set "env" (extra environment variables for the run), "extra" (more packages) and "run": false (compile only).
// Browser-only samples (a Blazor page, a Web Worker service) are compile-only: "sdk", "props", "fragment", "files" and "entry" shape the proving project.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { DATA } from './lib/data.mjs';

const dir = path.join(DATA, 'snippets');
const manifest = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
const only = process.argv.slice(2);
let bad = 0;
for (const s of manifest.samples) {
  if (only.length && !only.includes(s.name)) continue;
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'ea-snip-'));
  // "sdk" swaps the project SDK (a Blazor WebAssembly app), "props" adds properties that are glue and not shown on the page, "fragment" is a
  // project file block shown on the page and spliced in verbatim, "files" copies more files into the project ({ "path in project": "file in snippets" }),
  // and "entry" appends an entry point so a library-style sample compiles as a program.
  const sdk = s.sdk || 'Microsoft.NET.Sdk';
  const fragment = s.fragment ? fs.readFileSync(path.join(dir, s.fragment), 'utf8') : '';
  fs.writeFileSync(path.join(tmp, 'proof.csproj'), `<Project Sdk="${sdk}"><PropertyGroup>${s.sdk ? '' : '<OutputType>Exe</OutputType>'}<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>${s.props || ''}</PropertyGroup>${fragment}<ItemGroup><PackageReference Include="${s.package}" Version="${s.version || '*'}" />${(s.extra || []).map((x) => `<PackageReference Include="${x.id}" Version="${x.version}" />`).join('')}</ItemGroup></Project>`);
  fs.writeFileSync(path.join(tmp, 'Program.cs'), fs.readFileSync(path.join(dir, s.name + '.cs'), 'utf8') + (s.entry ? '\n' + s.entry + '\n' : ''));
  for (const [dest, src] of Object.entries(s.files || {})) { fs.mkdirSync(path.dirname(path.join(tmp, dest)), { recursive: true }); fs.copyFileSync(path.join(dir, src), path.join(tmp, dest)); }
  const compileOnly = s.run === false;
  const r = spawnSync('dotnet', compileOnly ? ['build', '-c', 'Release', tmp] : ['run', '-c', 'Release', '--project', tmp], { encoding: 'utf8', env: { ...process.env, ...(s.env || {}) } });
  // "hold": the sample uses members the published package does not have yet. It is tried every run: while it fails it is reported as held
  // (its .out.txt is then the owner's recorded output, not a run of ours); the first run it compiles it is proven normally, so remove the hold.
  if (r.status !== 0 && s.hold) { console.log(`held ${s.name}  (${s.hold}; output is the owner's record, not yet proven here)`); fs.rmSync(tmp, { recursive: true, force: true }); continue; }
  if (r.status !== 0) { bad++; console.log(`FAIL ${s.name}\n${(r.stdout + r.stderr).split('\n').slice(0, 12).join('\n')}`); }
  else {
    if (s.hold) console.log(`note ${s.name} now compiles against the published package: remove its "hold" in manifest.json`);
    if (compileOnly) { console.log(`ok   ${s.name}  (compiled only: it needs a server or a browser)`); fs.rmSync(tmp, { recursive: true, force: true }); continue; }
    const out = r.stdout.replace(/\r\n/g, '\n');
    const f = path.join(dir, s.name + '.out.txt');
    const prev = fs.existsSync(f) ? fs.readFileSync(f, 'utf8') : null;
    fs.writeFileSync(f, out, 'utf8');
    console.log(`ok   ${s.name}${prev !== null && prev !== out ? '  (output changed)' : ''}`);
  }
  fs.rmSync(tmp, { recursive: true, force: true });
}
process.exitCode = bad ? 1 : 0;
