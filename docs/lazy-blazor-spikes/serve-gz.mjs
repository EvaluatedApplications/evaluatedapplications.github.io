// Research spike: a one-origin static server shaped like GitHub Pages for a scratch build.
// /tools/**  -> <wwwroot> (SPA fallback to index.html), /website-data/** -> <data dir>, /assets/** and /SiteKit/** -> a site copy.
// Responses are gzip-encoded (zlib level 9, cached) when the client accepts it, like Pages (no Brotli), Cache-Control max-age=600.
// Usage: node serve-gz.mjs <wwwroot> <dataDir> <siteRoot> <port>
import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path'; import zlib from 'node:zlib';
const [ROOT, DATA, SITE, PORT] = process.argv.slice(2);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm', '.txt': 'text/plain', '.svg': 'image/svg+xml', '.png': 'image/png', '.csv': 'text/csv' };
const cache = new Map();
const compressible = ext => ['.html', '.js', '.mjs', '.css', '.json', '.wasm', '.txt', '.svg', '.csv', '.dat'].includes(ext) || ext === '';
http.createServer((req, res) => {
  if (process.env.REQLOG) fs.appendFileSync(process.env.REQLOG, req.method + ' ' + req.url + '\n');
  const p = decodeURIComponent(req.url.split('?')[0]); let f = null;
  if (p.startsWith('/assets/') || p.startsWith('/SiteKit/')) f = path.join(SITE, p);
  else if (p.startsWith('/website-data/')) f = path.join(DATA, p.slice(14));
  else if (p.startsWith('/tools/')) { f = path.join(ROOT, p.slice(7)); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) f = path.join(ROOT, 'index.html'); }
  else if (p === '/' || p.endsWith('.html')) f = path.join(SITE, '..', 'site', p === '/' ? 'index.html' : p);
  if (!f || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); return res.end(); }
  const ext = path.extname(f); const hdr = { 'Content-Type': types[ext] ?? 'application/octet-stream', 'Cache-Control': 'max-age=600', 'Access-Control-Allow-Origin': '*' };
  const gz = /gzip/.test(req.headers['accept-encoding'] ?? '') && compressible(ext) && !f.endsWith('.gz');
  if (!gz) { res.writeHead(200, hdr); return fs.createReadStream(f).pipe(res); }
  let b = cache.get(f); if (!b) { b = zlib.gzipSync(fs.readFileSync(f), { level: 9 }); cache.set(f, b); }
  res.writeHead(200, { ...hdr, 'Content-Encoding': 'gzip', 'Content-Length': b.length }); res.end(b);
}).listen(Number(PORT), () => console.log('listening ' + PORT));
