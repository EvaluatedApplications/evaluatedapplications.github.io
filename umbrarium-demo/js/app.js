// Small browser helpers the dashboard calls through JS interop. No network access, no storage of data, no telemetry.
window.umbra = {
  // Copy text to the clipboard; falls back to a hidden textarea where the async clipboard is not allowed (plain http on a LAN address).
  copy: async function (text) {
    try {
      if (navigator.clipboard && window.isSecureContext) { await navigator.clipboard.writeText(text); return true; }
    } catch (e) { }
    var t = document.createElement('textarea');
    t.value = text; t.setAttribute('readonly', ''); t.style.position = 'fixed'; t.style.opacity = '0';
    document.body.appendChild(t); t.select();
    var ok = false; try { ok = document.execCommand('copy'); } catch (e) { }
    document.body.removeChild(t);
    return ok;
  },
  // Save text as a file through a temporary link (a Blob in this tab; nothing is sent anywhere).
  download: function (name, text) {
    var url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
    var a = document.createElement('a');
    a.href = url; a.download = name; a.style.display = 'none';
    document.body.appendChild(a); a.click(); document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
  },
  // Minutes to add to UTC for the browser's local time.
  tzOffsetMinutes: function () { return -new Date().getTimezoneOffset(); },
  focus: function (selector) { var e = document.querySelector(selector); if (e) e.focus(); },
  // The SQL console's editor: Tab inserts two spaces and Ctrl/Cmd+Enter asks the page to run the query. Tab must not trap the keyboard: Shift+Tab always moves on, and so does
  // Tab right after Esc. Typing goes through execCommand where the browser allows it (keeps undo); the fallback edits the value and fires an input event so Blazor sees it.
  sqlEditor: function (id, ref) {
    var el = document.getElementById(id);
    if (!el || el.__umbraSql) return;
    el.__umbraSql = true;
    var escaped = false;
    el.addEventListener('blur', function () { escaped = false; });
    el.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') { escaped = true; return; }
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { e.preventDefault(); escaped = false; ref.invokeMethodAsync('RunFromKey'); return; }
      if (e.key === 'Tab' && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.metaKey) {
        if (escaped) { escaped = false; return; }
        e.preventDefault();
        var ok = false; try { ok = document.execCommand('insertText', false, '  '); } catch (x) { }
        if (!ok) {
          var s = el.selectionStart, n = el.selectionEnd;
          el.value = el.value.slice(0, s) + '  ' + el.value.slice(n);
          el.selectionStart = el.selectionEnd = s + 2;
          el.dispatchEvent(new Event('input', { bubbles: true }));
        }
        return;
      }
      escaped = false;
    });
  },
  // The first-run tour: which anchors are on the page and visible, scrolling to one and measuring it ([top, left, width, height, viewport width, viewport height] in viewport pixels),
  // the keys while it runs (Enter or Right = next, Left = back, Esc = skip; typing in a field is left alone) and focus going back where it came from. All of it can fail quietly.
  tour: (function () {
    var ref = null, opener = null, queued = false;
    function find(sel) { var e = document.querySelector(sel); if (!e) return null; var r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 ? e : null; }
    function box(e) { var r = e.getBoundingClientRect(); return [r.top, r.left, r.width, r.height, window.innerWidth, window.innerHeight]; }
    function onKey(e) {
      if (!ref || e.ctrlKey || e.metaKey || e.altKey) return;
      var tag = e.target && e.target.tagName, typing = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
      var act = null;
      if (e.key === 'Escape') act = 'skip';
      else if (typing) return;
      else if (e.key === 'ArrowRight') act = 'next';
      else if (e.key === 'ArrowLeft') act = 'back';
      else if (e.key === 'Enter' && tag !== 'BUTTON' && tag !== 'A' && tag !== 'SUMMARY') act = 'next';
      if (!act) return;
      e.preventDefault();
      ref.invokeMethodAsync('Key', act);
    }
    function again() {
      if (!ref || queued) return;
      queued = true;
      requestAnimationFrame(function () { queued = false; if (ref) ref.invokeMethodAsync('Remeasure'); });
    }
    return {
      present: function (sels) { return sels.map(function (s) { return !!find(s); }); },
      show: function (sel) {
        var e = find(sel); if (!e) return null;
        try { e.scrollIntoView({ block: 'center', inline: 'nearest' }); } catch (x) { }
        return box(e);
      },
      measure: function (sel) { var e = find(sel); return e ? box(e) : null; },
      attach: function (r) {
        ref = r; opener = document.activeElement;
        document.addEventListener('keydown', onKey, true);
        window.addEventListener('resize', again); window.addEventListener('scroll', again, true);
      },
      detach: function () {
        ref = null;
        document.removeEventListener('keydown', onKey, true);
        window.removeEventListener('resize', again); window.removeEventListener('scroll', again, true);
        var o = opener; opener = null;
        if (o && o.focus && document.contains(o)) { try { o.focus(); } catch (x) { } }
      }
    };
  })(),
  // The browser's local storage for one flag (the tour was seen); blocked storage is simply "not seen".
  local: {
    get: function (key) { try { return localStorage.getItem(key); } catch (e) { return null; } },
    set: function (key, value) { try { localStorage.setItem(key, value); } catch (e) { } }
  },
  // The tab's session storage (gone when the tab closes); used for the list of recent queries only.
  session: {
    get: function (key) { try { return sessionStorage.getItem(key); } catch (e) { return null; } },
    set: function (key, value) { try { sessionStorage.setItem(key, value); } catch (e) { } }
  }
};
