#!/usr/bin/env node
// Proves every code sample shown on the site: each file listed in data/snippets/manifest.json is compiled as a
// complete program against the PUBLISHED NuGet package and run; its real output is written next to it
// (<name>.out.txt) and the build fails if a sample no longer compiles. Needs the .NET SDK and network access to NuGet.
//   node web/scripts/prove-snippets.mjs [name ...]
// A manifest entry may set "env" (extra environment variables for the run), "extra" (more packages) and "run": false (compile only).
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
  fs.writeFileSync(path.join(tmp, 'proof.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><PackageReference Include="${s.package}" Version="${s.version || '*'}" />${(s.extra || []).map((x) => `<PackageReference Include="${x.id}" Version="${x.version}" />`).join('')}</ItemGroup></Project>`);
  fs.copyFileSync(path.join(dir, s.name + '.cs'), path.join(tmp, 'Program.cs'));
  const compileOnly = s.run === false;
  const r = spawnSync('dotnet', compileOnly ? ['build', '-c', 'Release', tmp] : ['run', '-c', 'Release', '--project', tmp], { encoding: 'utf8', env: { ...process.env, ...(s.env || {}) } });
  if (r.status !== 0) { bad++; console.log(`FAIL ${s.name}\n${(r.stdout + r.stderr).split('\n').slice(0, 12).join('\n')}`); }
  else {
    if (compileOnly) { console.log(`ok   ${s.name}  (compiled only: it needs a server)`); fs.rmSync(tmp, { recursive: true, force: true }); continue; }
    const out = r.stdout.replace(/\r\n/g, '\n');
    const f = path.join(dir, s.name + '.out.txt');
    const prev = fs.existsSync(f) ? fs.readFileSync(f, 'utf8') : null;
    fs.writeFileSync(f, out, 'utf8');
    console.log(`ok   ${s.name}${prev !== null && prev !== out ? '  (output changed)' : ''}`);
  }
  fs.rmSync(tmp, { recursive: true, force: true });
}
process.exitCode = bad ? 1 : 0;
