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
  focus: function (selector) { var e = document.querySelector(selector); if (e) e.focus(); }
};
