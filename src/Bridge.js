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

  /* 1.3.0: один вход на приложение и кабинет (localStorage 'sc_tok').
     Нет входа в кабинете, а у приложения есть — кладём до загрузки страницы; кабинет вошёл/вышел — сообщаем приложению. */
  try {
    var cur = localStorage.getItem('sc_tok') || '';
    if (!cur && S.appToken) localStorage.setItem('sc_tok', S.appToken);
    else if (cur && cur !== S.appToken) send('authToken', [cur]);
    var _si = Storage.prototype.setItem, _ri = Storage.prototype.removeItem;
    Storage.prototype.setItem = function (k, v) {
      var r = _si.apply(this, arguments);
      try { if (this === window.localStorage && k === 'sc_tok' && String(v) !== S.appToken) { S.appToken = String(v); send('authToken', [String(v)]); } } catch (e) {}
      return r;
    };
    Storage.prototype.removeItem = function (k) {
      var r = _ri.apply(this, arguments);
      try { if (this === window.localStorage && k === 'sc_tok' && S.appToken) { S.appToken = ''; send('authToken', ['']); } } catch (e) {}
      return r;
    };
  } catch (e) {}
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
    /* 1.4.0: тихое автообновление */
    updateVersion: function () { return S.update || ''; },          // версия, доступная на сервере (подпись уже проверена); '' — обновлений нет
    autoUpdate: function () { return S.autoUpdate !== false; },
    setAutoUpdate: function (on) { S.autoUpdate = !!on; send('autoUpdate', [!!on]); },
    checkUpdate: function () { send('checkUpdate'); },
    /* 1.3.0 */
    account: function () { return S.account || ''; },               // active | expired | blocked | device_limit | ''
    linkTelegram: function () { send('linkTelegram'); },
    /* 1.2.0: «Северное Сияние (Авто)». В servers() первым идёт {guid:'auto', auto:true};
       у серверов есть ping (мс, -1 — нет связи, 0 — не проверяли) и active (через него подключены сейчас) */
    autoMode: function () { return !!S.auto; },
    setAutoMode: function (on) { S.auto = !!on; send('autoMode', [!!on]); },
    activeServer: function () { return S.activeGuid || ''; },
    /* 1.1.0 */
    backdrop: function () { return S.backdrop || 'mica'; },          // mica | acrylic | off
    backdropSupported: function () { return !!S.backdropSupported; },
    glass: function () { return !!S.glass; },
    setBackdrop: function (m) { S.backdrop = String(m || 'mica'); send('backdrop', [S.backdrop]); },
    backupSave: function () { send('backupSave'); },
    backupRestore: function () { send('backupRestore'); }
  };
})();
