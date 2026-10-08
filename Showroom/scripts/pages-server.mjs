// Shared by the headless-Edge scripts: a request handler that serves a PUBLISHED Showroom build the way GitHub Pages serves it.
//   /tools/**         <published wwwroot>: a folder with an index.html is served at its path (a path without the trailing slash gets Pages'
//                     301 to the slash); anything with no file is Pages' 404, whose body is the site's real 404.html (so the deep-link
//                     bounce to /tools/?/<path> runs for real, e.g. /tools/council-spending/wokingham, which has no static shell)
//   /website-data/**  <website-data dir>, also under /tools/website-data/ (what the localhost junction gives)
//   /assets/**, /SiteKit/**  the static site's shared CSS
// Options: gzip (compress like Pages: gzip level 9, no Brotli; the wire figures are then real), cache ('no-store' by default, or e.g.
//   'max-age=600' as Pages sends), autoStart (default true: a page that has a tool start panel gets a script that presses it, because
//   the test scripts exercise the TOOLS, not the shells. Pass false when the shell itself is what is being measured).
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const SITE = 'C:\\Users\\dongy\\AboutUs\\site';
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.dll': 'application/octet-stream', '.csv': 'text/csv', '.txt': 'text/plain', '.gz': 'application/gzip', '.svg': 'image/svg+xml', '.png': 'image/png',
  '.dat': 'application/octet-stream', '.blat': 'application/octet-stream', '.bin': 'application/octet-stream' };
const compressible = ext => ['.html', '.js', '.mjs', '.css', '.json', '.wasm', '.txt', '.svg', '.csv', '.dat'].includes(ext);
const AUTOSTART = '<script>document.addEventListener("DOMContentLoaded",function(){var b=document.querySelector("[data-blazier-go]");if(b)b.click();});</script>';

export function pagesHandler(ROOT, DATA, { gzip = false, cache = 'no-store', autoStart = true, log = null } = {}) {
  const gzCache = new Map();
  return (req, res) => {
    const url = req.url.split('?')[0];
    const p = decodeURIComponent(url);
    if (log) log.push(req.method + ' ' + req.url);
    const send = (status, f, extra = {}) => {
      const ext = path.extname(f);
      let body = null;
      if (ext === '.html' && autoStart && status === 200) {
        body = fs.readFileSync(f, 'utf8');
        if (body.includes('data-blazier-go')) body = body.replace('</body>', AUTOSTART + '</body>');
        body = Buffer.from(body, 'utf8');
      }
      const hdr = { 'Content-Type': types[ext] ?? 'application/octet-stream', 'Cache-Control': cache, 'Access-Control-Allow-Origin': '*', ...extra };
      const wantGz = gzip && /gzip/.test(req.headers['accept-encoding'] ?? '') && compressible(ext);
      if (!wantGz) {
        if (body) { hdr['Content-Length'] = body.length; res.writeHead(status, hdr); return res.end(body); }
        res.writeHead(status, hdr); return fs.createReadStream(f).pipe(res);
      }
      const key = f + (body ? ':as' : '');
      let b = gzCache.get(key);
      if (!b) { b = zlib.gzipSync(body ?? fs.readFileSync(f), { level: 9 }); gzCache.set(key, b); }
      res.writeHead(status, { ...hdr, 'Content-Encoding': 'gzip', 'Content-Length': b.length });
      res.end(b);
    };
    const isFile = f => fs.existsSync(f) && fs.statSync(f).isFile();
    const notFound = () => { const nf = path.join(SITE, '404.html'); return isFile(nf) ? send(404, nf) : (res.writeHead(404), res.end()); };

    if (p.startsWith('/assets/') || p.startsWith('/SiteKit/')) {
      const sf = p.startsWith('/assets/') ? path.join(SITE, p) : path.join('C:\\Users\\dongy\\AboutUs', p);
      return isFile(sf) ? send(200, sf) : notFound();
    }
    for (const prefix of ['/tools/website-data/', '/website-data/']) {
      if (p.startsWith(prefix)) { const f = path.join(DATA, p.slice(prefix.length)); return isFile(f) ? send(200, f) : (res.writeHead(404), res.end()); }
    }
    if (p === '/tools') { res.writeHead(301, { Location: '/tools/' }); return res.end(); }
    if (!p.startsWith('/tools/')) return notFound();
    const rel = p.slice('/tools/'.length);
    let f = path.join(ROOT, rel);
    if (fs.existsSync(f) && fs.statSync(f).isDirectory()) {
      if (!p.endsWith('/')) { res.writeHead(301, { Location: p + '/' + req.url.slice(url.length) }); return res.end(); }
      f = path.join(f, 'index.html');
    }
    return isFile(f) ? send(200, f) : notFound();
  };
}
