// Shared nav behaviour: the "Packages" mega-menu is a native <details>/<summary> disclosure
// (opens/closes with no JS at all), this just adds the polish of closing it when a link inside
// is activated, when a click lands outside it, or on Escape. Safe no-op on pages with no .nav-drop
// (e.g. 404.html).
document.addEventListener('DOMContentLoaded', function () {
  var drops = document.querySelectorAll('.nav-drop');
  if (!drops.length) return;

  drops.forEach(function (drop) {
    drop.addEventListener('click', function (e) {
      if (e.target.closest('a')) drop.removeAttribute('open');
    });
  });

  document.addEventListener('click', function (e) {
    drops.forEach(function (drop) {
      if (drop.hasAttribute('open') && !drop.contains(e.target)) drop.removeAttribute('open');
    });
  });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
      drops.forEach(function (drop) { drop.removeAttribute('open'); });
    }
  });
});
