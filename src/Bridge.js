/* Siberia Connect для Windows: мост между кабинетом и VPN-движком приложения */
(function () {
  if (window.SCNative) return;
  var S = __INIT__;
  function send(cmd, args) {
    try { window.chrome.webview.postMessage(JSON.stringify({ cmd: cmd, args: args || [] })); } catch (e) {}
  }
  window.__scSnap = function (s) { if (!s) return; for (var k in s) S[k] = s[k]; };
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
    setAutoStart: function (on) { S.autoStart = !!on; send('autoStart', [!!on]); },
    advanced: function () { send('advanced'); }
  };
})();
