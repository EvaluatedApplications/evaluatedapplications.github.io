#!/usr/bin/env node
// Screenshots of built pages at desktop (1280) and phone (390). Dev tooling.
//   node web/scripts/shots.mjs [outDir] [path ...]     default paths: / /tracer/
import path from 'node:path';
import { OUT, WEB } from './lib/data.mjs';
import { serve, launch } from './lib/cdp.mjs';

const outDir = path.resolve(process.argv[2] || path.join(WEB, 'shots'));
const paths = process.argv.slice(3).length ? process.argv.slice(3) : ['/', '/tracer/'];
const { server, port } = await serve(OUT);
const b = await launch();
try {
  for (const p of paths) {
    const slug = p === '/' ? 'home' : p.replace(/^\/|\/$/g, '').replace(/\//g, '-');
    for (const [name, opt] of [['desktop-1280', { width: 1280, height: 900 }], ['phone-390', { width: 390, height: 844, mobile: true }]]) {
      await b.goto(`http://127.0.0.1:${port}${p}`, { ...opt, wait: "!document.querySelector('[data-diagram]') || document.querySelector('[data-diagram]').dataset.ready === 'true'" });
      await new Promise((r) => setTimeout(r, 400));
      const file = path.join(outDir, `${slug}-${name}.png`);
      await b.shot(file);
      const w = await b.evaluate('({sw:document.documentElement.scrollWidth,cw:document.documentElement.clientWidth,h:document.documentElement.scrollHeight})');
      console.log(file, JSON.stringify(w), b.errors.length ? 'ERRORS ' + JSON.stringify(b.errors) : '');
    }
  }
} finally { b.close(); server.close(); }
