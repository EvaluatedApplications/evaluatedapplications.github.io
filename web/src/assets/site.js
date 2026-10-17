// Site behaviour: menu toggle, copy buttons, product-map highlighting, legacy home-anchor forwarding.
// No dependencies. Every feature degrades to working HTML without it.
(function () {
  'use strict';

  var nav = document.querySelector('[data-nav]');
  var toggle = nav && nav.querySelector('.nav-toggle');
  if (toggle) {
    toggle.addEventListener('click', function () {
      var open = nav.hasAttribute('data-open');
      if (open) nav.removeAttribute('data-open'); else nav.setAttribute('data-open', '');
      toggle.setAttribute('aria-expanded', String(!open));
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && nav.hasAttribute('data-open')) { nav.removeAttribute('data-open'); toggle.setAttribute('aria-expanded', 'false'); toggle.focus(); }
    });
  }

  document.querySelectorAll('.copy').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var box = btn.closest('.install, .code');
      var el = box && (box.querySelector('pre code') || box.querySelector('code'));
      if (!el || !navigator.clipboard) return;
      navigator.clipboard.writeText(el.textContent).then(function () {
        var old = btn.textContent; btn.textContent = 'copied'; btn.classList.add('done');
        setTimeout(function () { btn.textContent = old; btn.classList.remove('done'); }, 1400);
      });
    });
  });

  var filter = document.querySelector('[data-filter]');
  if (filter) {
    var cards = Array.prototype.slice.call(document.querySelectorAll('[data-topics]'));
    var count = document.querySelector('[data-filter-count]');
    filter.hidden = false;
    filter.addEventListener('click', function (e) {
      var b = e.target.closest('button[data-topic]'); if (!b) return;
      var t = b.getAttribute('data-topic'), shown = 0;
      filter.querySelectorAll('button').forEach(function (x) { x.setAttribute('aria-pressed', String(x === b)); });
      cards.forEach(function (c) { var on = !t || c.getAttribute('data-topics').split('|').indexOf(t) >= 0; c.hidden = !on; if (on) shown++; });
      if (count) { count.hidden = !t; count.textContent = t ? shown + ' of ' + cards.length + ' articles about ' + t : ''; }
    });
  }
  // a link to a closed disclosure (or to anything inside one) opens it
  function openTarget() {
    var el = location.hash.length > 1 && document.getElementById(decodeURIComponent(location.hash.slice(1)));
    var d = el && el.closest('details');
    if (d && !d.open) { d.open = true; el.scrollIntoView(); }
  }
  window.addEventListener('hashchange', openTarget);
  openTarget();
  // disclosures in a .demo-more group behave as one: opening one closes the other (their panels share a place on wide screens)
  document.querySelectorAll('.demo-more').forEach(function (g) {
    var ds = g.querySelectorAll('details');
    ds.forEach(function (d) { d.addEventListener('toggle', function () { if (d.open) ds.forEach(function (o) { if (o !== d) o.open = false; }); }); });
  });
  var fwd = document.getElementById('hash-forward');
  if (fwd && location.hash) {
    try { var to = JSON.parse(fwd.textContent)[location.hash]; if (to) location.replace(to); } catch (e) { /* ignore */ }
  }
})();
