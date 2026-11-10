/* Siberia Connect для Windows: мост между кабинетом и VPN-движком приложения */
(function () {
  if (window.SCNative) return;
  var S = __INIT__;
  function send(cmd, args) {
    try { window.chrome.webview.postMessage(JSON.stringify({ cmd: cmd, args: args || [] })); } catch (e) {}
  }
  /* 1.1.0: стекло Windows 11. Страница получает html.sc-glass и data-sc-backdrop="mica|acrylic|off";
     фон делаем прозрачным с фирменным синим оттенком — под ним Mica/Acrylic окна. */
  var GLASS_CSS = 'html.sc-glass{background:transparent!important}' +
    'html.sc-glass body{background:rgba(6,15,42,var(--sc-glass-tint,.62))!important}' +
    'html.sc-glass[data-sc-backdrop="acrylic"] body{--sc-glass-tint:.5}';
  function glass() {
    try {
      var d = document.documentElement;
      if (!d) return;
      if (!document.getElementById('sc-glass-css')) {
        var st = document.createElement('style');
        st.id = 'sc-glass-css';
        st.textContent = GLASS_CSS;
        (document.head || d).appendChild(st);
      }
      d.classList.toggle('sc-glass', !!S.glass);
      d.setAttribute('data-sc-backdrop', S.glass ? (S.backdrop || 'mica') : 'off');
    } catch (e) {}
  }
  glass();
  document.addEventListener('DOMContentLoaded', glass);
  window.__scSnap = function (s) { if (!s) return; for (var k in s) S[k] = s[k]; if ('glass' in s) glass(); };
  window.SCNative = {
    platform: 'windows',
    version: function () { return S.version || ''; },
    deviceId: function () { return S.deviceId || ''; },
    isRunning: function () { return !!S.running; },
    hasSub: function () { return !!S.hasSub; },
    subUrl: function () { return S.subUrl || ''; },
    setSub: function (u) { send('setSub', [String(u || '')]); },
    connect: function () { send('connect'); },
    disconnect: function () { send('disconnect'); },
    servers: function () { return JSON.stringify(S.servers || []); },
    select: function (g) { (S.servers || []).forEach(function (x) { x.selected = x.guid === g; }); send('select', [String(g)]); },
    openExternal: function (u) { send('open', [String(u || '')]); },
    stats: function () { var s = S.stats || {}; return JSON.stringify({ rx: s.rx || 0, tx: s.tx || 0, since: s.since || 0, now: s.now || Date.now() }); },
    autoStart: function () { return !!S.autoStart; },
    tunMode: function () { return !!S.tun; },
    tunActive: function () { return !!S.tunActive; },
    setTunMode: function (on) { S.tun = !!on; send('tunMode', [!!on]); },
    setAutoStart: function (on) { S.autoStart = !!on; send('autoStart', [!!on]); },
    advanced: function () { send('advanced'); },
    installUpdate: function () { send('installUpdate'); },
    /* 1.1.0 */
    backdrop: function () { return S.backdrop || 'mica'; },          // mica | acrylic | off
    backdropSupported: function () { return !!S.backdropSupported; },
    glass: function () { return !!S.glass; },
    setBackdrop: function (m) { S.backdrop = String(m || 'mica'); send('backdrop', [S.backdrop]); },
    backupSave: function () { send('backupSave'); },
    backupRestore: function () { send('backupRestore'); }
  };
})();
