// current year
(function(){ var y = document.getElementById('yr'); if(y) y.textContent = new Date().getFullYear(); })();

// phasor-dial hero animation (only if a #scope canvas is present)
(function(){
  var cv = document.getElementById('scope'); if(!cv) return;
  var ctx = cv.getContext('2d');
  var reduce = matchMedia('(prefers-reduced-motion:reduce)').matches;
  var dials = [], W = 0, H = 0, DPR = Math.min(window.devicePixelRatio || 1, 2);
  var CY = 'rgba(51,206,219,', AM = 'rgba(230,162,76,', VI = 'rgba(144,130,238,';
  function build(){
    var r = cv.getBoundingClientRect(); W = r.width; H = r.height;
    cv.width = Math.round(W * DPR); cv.height = Math.round(H * DPR);
    ctx.setTransform(DPR, 0, 0, DPR, 0, 0); dials = [];
    var cols = Math.max(5, Math.round(W / 150)), rows = Math.max(3, Math.round(H / 150));
    var gx = W / cols, gy = H / rows;
    for(var j = 0; j < rows; j++) for(var i = 0; i < cols; i++){
      var jx = (Math.sin(i * 12.9 + j * 3.1) * 0.5) * gx * 0.4, jy = (Math.cos(i * 4.7 + j * 7.3) * 0.5) * gy * 0.4;
      var rad = Math.min(gx, gy) * (0.14 + ((i * j) % 5) * 0.03), freq = 0.15 + ((i * 7 + j * 13) % 17) * 0.055;
      var dir = ((i + j) % 2) ? 1 : -1, band = ((i * 3 + j) % 9 === 0) ? 'a' : (((i + j * 2) % 13 === 0) ? 'v' : 'c');
      dials.push({ x: gx * (i + 0.5) + jx, y: gy * (j + 0.5) + jy, r: rad, f: freq * dir, p: (i * 1.7 + j * 2.3), band: band, a: 0.10 + ((i + j) % 4) * 0.05 });
    }
  }
  var raf;
  function frame(t){
    ctx.clearRect(0, 0, W, H); var time = t * 0.0009;
    for(var k = 0; k < dials.length; k++){
      var d = dials[k], col = d.band === 'a' ? AM : d.band === 'v' ? VI : CY;
      ctx.beginPath(); ctx.arc(d.x, d.y, d.r, 0, Math.PI * 2); ctx.strokeStyle = col + (d.a * 0.5) + ')'; ctx.lineWidth = 1; ctx.stroke();
      var ang = reduce ? d.p : (d.p + time * d.f * 6.0), hx = d.x + Math.cos(ang) * d.r, hy = d.y + Math.sin(ang) * d.r;
      ctx.beginPath(); ctx.moveTo(d.x, d.y); ctx.lineTo(hx, hy); ctx.strokeStyle = col + (d.a + 0.14) + ')'; ctx.lineWidth = 1.4; ctx.stroke();
      ctx.beginPath(); ctx.arc(hx, hy, 1.7, 0, Math.PI * 2); ctx.fillStyle = col + (d.a + 0.4) + ')'; ctx.fill();
    }
    if(!reduce) raf = requestAnimationFrame(frame);
  }
  function start(){ cancelAnimationFrame(raf); build(); if(reduce){ frame(0); } else { raf = requestAnimationFrame(frame); } }
  var to; window.addEventListener('resize', function(){ clearTimeout(to); to = setTimeout(start, 150); }); start();
})();
