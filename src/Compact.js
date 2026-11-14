/* Siberia Connect для Windows 1.4.0: компактное окно 360×640 — «всё на одном экране».
   Скрипт добавляется к Bridge.js и перекраивает страницу кабинета только внутри приложения:
   своя верхняя полоса (перетаскивание + ×), нижняя навигация VPN / Настройки / Профиль,
   главный экран без прокрутки, вкладка «Настройки», реферальная карточка внутри «Профиля». */
(function () {
  if (window.top !== window || !window.SCNative || window.SCNative.platform !== 'windows' || location.protocol !== 'https:') return;

  var CSS = [
    /* каркас окна */
    'html.win.scc,html.win.scc body{height:100vh;overflow:hidden!important;background:#060F2A}',
    'html.win.scc #wside,html.win.scc .nav,html.win.scc #apphead{display:none!important}',
    'html.win.scc .wrap{position:fixed;top:34px;bottom:58px;left:0;right:0;max-width:none;margin:0;padding:4px 14px 8px!important;overflow-x:hidden;overflow-y:auto;scrollbar-width:none}',
    'html.win.scc .wrap::-webkit-scrollbar{display:none}',
    'html.win.scc .wrap:has(#s-home.on),html.win.scc .wrap:has(#s-profile.on){overflow-y:hidden}',
    'html.win.scc.nonav .wrap{bottom:0;overflow-y:auto!important}',
    'html.win.scc .screen{max-width:none}',
    /* верхняя полоса: перетаскивание и × */
    '.sc-top{position:fixed;left:0;right:0;top:0;height:34px;z-index:10;display:flex;align-items:center;justify-content:space-between;padding:0 8px 0 16px;-webkit-user-select:none;user-select:none}',
    '.sc-brand{font-style:normal;font-size:10.5px;letter-spacing:.16em;text-transform:uppercase;color:#6F8DB0;font-weight:700}',
    '.sc-x{width:26px;height:26px;border:0;border-radius:50%;background:rgba(127,211,255,.08);color:#9FB8D6;font:inherit;font-size:18px;line-height:24px;text-align:center;cursor:pointer;padding:0;transition:background .2s,color .2s}',
    '.sc-x:hover{background:rgba(255,157,176,.22);color:#FF9DB0}',
    /* нижняя навигация */
    '.sc-nav{position:fixed;left:0;right:0;bottom:0;height:58px;z-index:10;display:grid;grid-template-columns:repeat(3,1fr);background:rgba(4,13,33,.97);border-top:1px solid rgba(159,184,214,.13)}',
    'html.win.nonav .sc-nav{display:none}',
    '.sc-nav button{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;border:0;background:transparent;color:#6F8DB0;font:inherit;font-size:11px;font-weight:650;cursor:pointer;padding:0;transition:color .2s}',
    '.sc-nav button svg{width:20px;height:20px}',
    '.sc-nav button:hover{color:#fff}',
    '.sc-nav button.on{color:#3DE0C2}',
    /* главный экран: ничего не прокручивается */
    'html.win.scc #s-home{max-width:none;height:100%}',
    'html.win.scc.nonav #s-home{height:auto;margin:0 auto;padding:18px}',
    'html.win.scc .wtop,html.win.scc .wdesc{display:none}',
    'html.win.scc #wupd:empty{display:none}',
    'html.win.scc #wupd{margin-bottom:6px}',
    'html.win.scc .wdash{display:flex;flex-direction:column;gap:0;height:100%}',
    'html.win.scc .wcenter{display:flex;flex-direction:column;align-items:stretch;flex:1 1 auto;min-height:0;text-align:center}',
    'html.win.scc .wcenter:before,html.win.scc .wcenter:after{content:"";flex:1 1 0;min-height:0}',
    'html.win.scc .wcenter:before{order:3}html.win.scc .wcenter:after{order:7}',
    'html.win.scc .wstats{order:1}html.win.scc .wsrv{order:2}html.win.scc .worbw{order:4}html.win.scc .wtitle{order:5}html.win.scc .wact{order:6}',
    /* три мини-плашки */
    'html.win.scc .wstats{max-width:none;gap:6px}',
    'html.win.scc .wstat{padding:8px 9px 9px;border-radius:12px;overflow:hidden}',
    'html.win.scc .wstat small{font-size:10px;gap:4px;overflow:hidden;white-space:nowrap}',
    'html.win.scc .wstat small svg{width:12px;height:12px;flex:none}',
    'html.win.scc .wstat:nth-child(1) small{font-size:0}',
    'html.win.scc .wstat:nth-child(1) small:after{content:"Время";font-size:10px}',
    'html.win.scc .wstat b{font-size:16px;margin-top:4px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
    'html.win.scc #wt2{font-size:0!important}',
    'html.win.scc #wt2:after{content:"∞";font-size:20px;line-height:16px;display:block}',
    /* выбор локации одной строкой */
    'html.win.scc .wsrv{max-width:none;margin-top:6px;padding:9px 12px;border-radius:13px;gap:10px}',
    'html.win.scc .wsrv small{display:none}',
    'html.win.scc .wsrv b{font-size:13px}',
    /* главная кнопка */
    'html.win.scc .worbw{width:min(250px,calc(100vh - 400px));height:min(250px,calc(100vh - 400px));aspect-ratio:auto;flex:none;margin:14px auto}',
    'html.win.scc .wtitle{font-size:22px;margin:0;line-height:1.2}',
    'html.win.scc .wact{margin:8px 0 0;min-height:0}',
    'html.win.scc .wact:empty{display:none}',
    'html.win.scc .wact .wbtn{padding:9px 16px;font-size:13px}',
    /* «Мой IP» одной строкой под кнопкой */
    'html.win.scc .wright{display:block!important;padding:0!important;margin:0}',
    'html.win.scc .wright .wcard:not(:first-child){display:none}',
    'html.win.scc .wright .wcard{padding:9px 12px;border-radius:12px}',
    'html.win.scc .wright .wcard h4{display:none}',
    'html.win.scc .wip{margin:0;font-size:13px;font-weight:600;word-break:normal;display:flex;align-items:baseline;gap:6px;flex-wrap:wrap;justify-content:center}',
    'html.win.scc .wip small{display:inline!important;margin:0;font-size:12px;font-weight:500}',
    /* плашка «Доступно обновление» */
    '.sc-upd{display:none;position:fixed;left:14px;right:14px;top:38px;z-index:9;height:40px;align-items:center;gap:8px;padding:0 8px 0 12px;border:1px solid rgba(61,224,194,.35);border-radius:12px;background:linear-gradient(110deg,rgba(61,224,194,.16),rgba(127,211,255,.08));font-size:12.5px;font-weight:600}',
    '.sc-upd.on{display:flex}',
    '.sc-upd span{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
    '.sc-upd button{border:0;border-radius:9px;padding:6px 11px;font:inherit;font-weight:700;font-size:12px;cursor:pointer;color:#062138;background:#3DE0C2}',
    '.sc-upd button.g{background:transparent;color:#9FB8D6;font-weight:600;padding:6px 6px}',
    'html.win.scc.scu .wrap{top:84px}',
    'html.win.scc.scu .sc-set{top:84px}',
    /* вкладка «Настройки» */
    '.sc-set{display:none;position:fixed;left:0;right:0;top:34px;bottom:58px;z-index:8;padding:6px 14px 10px;overflow-y:auto;overflow-x:hidden;scrollbar-width:none;background:#060F2A}',
    '.sc-set::-webkit-scrollbar{display:none}',
    '.sc-set.on{display:block}',
    '.sc-set .scdis{opacity:.5;cursor:default}',
    '.sc-set .scft{margin-top:14px;text-align:center;font-size:11px;color:#6F8DB0}',
    /* «Профиль» на одном экране: карточка статуса и четыре плитки */
    'html.win.scc #s-profile{height:100%}',
    '.scp{height:100%;display:flex;flex-direction:column;gap:10px}',
    '.scp-top{padding:14px;border:1px solid rgba(127,211,255,.18);border-radius:18px;background:linear-gradient(145deg,rgba(21,46,86,.75),rgba(8,23,52,.8))}',
    '.scp-me{display:flex;align-items:center;gap:12px;min-width:0}',
    '.scp-me>div{min-width:0}',
    '.scp-ava{width:42px;height:42px;flex:none;border-radius:14px;display:grid;place-items:center;font-style:normal;font-weight:800;font-size:18px;color:#062138;background:linear-gradient(135deg,#3DE0C2,#7FD3FF)}',
    '.scp-me b{display:block;font-size:15px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
    '.scp-pill{display:inline-block;margin-top:3px;font-size:11px;font-weight:700;color:#3DE0C2}',
    '.scp-pill.bad{color:#FF9DB0}',
    '.scp-bar{height:6px;border-radius:6px;background:rgba(127,211,255,.12);overflow:hidden;margin:12px 0 6px}',
    '.scp-bar i{display:block;height:100%;background:linear-gradient(90deg,#3DE0C2,#7FD3FF)}',
    '.scp-until{display:flex;justify-content:space-between;gap:8px;font-size:12px;color:#9FB8D6}',
    '.scp-renew{width:100%;height:44px;margin-top:12px;border:0;border-radius:13px;font:inherit;font-weight:750;font-size:14.5px;color:#062138;background:linear-gradient(110deg,#3DE0C2,#8DEBD9);box-shadow:0 8px 24px rgba(61,224,194,.22);cursor:pointer}',
    '.scp-renew:hover{filter:brightness(1.06)}',
    '.scp-grid{flex:1;min-height:0;display:grid;grid-template-columns:1fr 1fr;grid-template-rows:1fr 1fr;gap:10px}',
    '.scp-tile{display:flex;flex-direction:column;justify-content:space-between;align-items:flex-start;padding:13px;border:1px solid rgba(159,184,214,.13);border-radius:16px;background:linear-gradient(145deg,rgba(17,39,78,.72),rgba(8,22,51,.72));color:#fff;font:inherit;text-align:left;cursor:pointer;min-width:0;overflow:hidden}',
    '.scp-tile:hover{border-color:rgba(127,211,255,.42)}',
    '.scp-tile.dis{opacity:.5;cursor:default}',
    '.scp-tile i{width:34px;height:34px;border-radius:11px;display:grid;place-items:center;background:rgba(127,211,255,.1);color:#7FD3FF}',
    '.scp-tile i svg{width:18px;height:18px}',
    '.scp-tile span{min-width:0;max-width:100%}',
    '.scp-tile b{display:block;font-size:14px}',
    '.scp-tile small{display:block;margin-top:2px;font-size:11.5px;color:#9FB8D6;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}'
  ].join('\n');

  var ICON = {
    vpn: '<path d="M12 3 4.5 6v5.5c0 4.6 3.1 8.2 7.5 9.5 4.4-1.3 7.5-4.9 7.5-9.5V6z"/><path d="m9 12 2 2 4-4"/>',
    settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
    profile: '<circle cx="12" cy="8" r="3.5"/><path d="M5 20c.7-3.5 3.1-5.3 7-5.3s6.3 1.8 7 5.3"/>'
  };
  var NAMES = { vpn: 'VPN', settings: 'Настройки', profile: 'Профиль' };

  function $(id) { return document.getElementById(id); }
  function send(cmd, args) { try { window.chrome.webview.postMessage(JSON.stringify({ cmd: cmd, args: args || [] })); } catch (e) {} }
  function svg(d) { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' + d + '</svg>'; }
  function ico(n) { try { return typeof ic === 'function' ? ic(n) : ''; } catch (e) { return ''; } }

  try { document.documentElement.classList.add('scc'); } catch (e) {}

  /* ───── верхняя полоса и навигация ───── */
  var nav, set;

  function buildChrome() {
    var top = document.createElement('div');
    top.className = 'sc-top';
    top.innerHTML = '<i class="sc-brand">Siberia Connect</i><button class="sc-x" id="sc-x" type="button" aria-label="Свернуть в трей" title="Свернуть в трей">×</button>';
    top.addEventListener('mousedown', function (e) {
      if (e.button === 0 && !(e.target.closest && e.target.closest('.sc-x'))) send('drag');
    });
    top.querySelector('.sc-x').addEventListener('click', function () { send('hide'); });
    document.body.appendChild(top);

    var ub = document.createElement('div');
    ub.className = 'sc-upd';
    ub.id = 'sc-upd';
    document.body.appendChild(ub);

    nav = document.createElement('nav');
    nav.className = 'sc-nav';
    nav.innerHTML = ['vpn', 'settings', 'profile'].map(function (k) {
      return '<button type="button" data-k="' + k + '">' + svg(ICON[k]) + '<span>' + NAMES[k] + '</span></button>';
    }).join('');
    nav.addEventListener('click', function (e) {
      var b = e.target.closest && e.target.closest('button');
      if (!b) return;
      var k = b.getAttribute('data-k');
      if (k === 'settings') { drawSettings(); set.classList.add('on'); }
      else { hideSettings(); try { go(k === 'vpn' ? 'home' : 'profile'); } catch (x) {} }
      sync();
    });
    document.body.appendChild(nav);

    set = document.createElement('section');
    set.className = 'sc-set';
    set.id = 'sc-settings';
    document.body.appendChild(set);
  }

  function hideSettings() { if (set) set.classList.remove('on'); }

  function sync() {
    if (!nav) return;
    var k = '';
    if (set.classList.contains('on')) k = 'settings';
    else if (document.querySelector('#s-profile.on')) k = 'profile';
    else if (document.querySelector('#s-home.on')) k = 'vpn';
    var bs = nav.querySelectorAll('button');
    for (var i = 0; i < bs.length; i++) bs[i].classList.toggle('on', bs[i].getAttribute('data-k') === k);
    updBar();
  }

  /* ───── плашка обновления: данные приходят из приложения (версия уже прошла проверку подписи) ───── */
  var upd = { stage: '', pct: 0, skip: '' };

  function updVer() {
    var v = '';
    try { v = window.SCNative.updateVersion ? String(window.SCNative.updateVersion() || '') : ''; } catch (e) {}
    return v.replace(/[^0-9A-Za-z._-]/g, '');
  }

  function updBar() {
    var el = $('sc-upd');
    if (!el) return;
    var v = updVer(), show = !!v && upd.skip !== v && !document.documentElement.classList.contains('nonav'), html = '';
    if (show) {
      if (upd.stage === 'download') html = '<span>Скачиваю обновление v' + v + '… ' + upd.pct + '%</span>';
      else if (upd.stage === 'install') html = '<span>Устанавливаю v' + v + '…</span>';
      else if (upd.stage === 'ready') html = '<span>Обновление v' + v + ' готово</span><button type="button" onclick="scUpdGo2()">Перезапустить</button>';
      else html = '<span>Доступно обновление v' + v + '</span><button type="button" onclick="scUpdGo2()">Обновить</button><button class="g" type="button" onclick="scUpdLater2()">Позже</button>';
    }
    if (el.getAttribute('data-h') !== html) { el.setAttribute('data-h', html); el.innerHTML = html; }
    el.classList.toggle('on', show);
    document.documentElement.classList.toggle('scu', show);
  }

  function onUpd(ev) {
    var st = ev.stage;
    if (st === 'download') { upd.stage = 'download'; upd.pct = ev.pct | 0; }
    else if (st === 'ready' || st === 'install' || st === 'available') upd.stage = st;
    else if (st === 'error') upd.stage = '';
    else if (st === 'none') { try { toast('У тебя последняя версия ✓'); } catch (e) {} }
    updBar();
  }

  window.scUpdGo2 = function () { upd.stage = 'download'; upd.pct = 0; updBar(); try { window.SCNative.installUpdate(); } catch (e) {} };
  window.scUpdLater2 = function () { upd.skip = updVer(); updBar(); };

  /* ───── вкладка «Настройки» ───── */
  function row(i, cls, title, sub, onclick, right, dis) {
    return '<div class="nxrow' + (dis ? ' scdis' : '') + '"' + (onclick ? ' onclick="' + onclick + '"' : '') + '><i class="nxi ' + cls + '">' + ico(i) + '</i>' +
      '<div><b>' + title + '</b>' + (sub ? '<small>' + sub + '</small>' : '') + '</div>' + (right || '') + '</div>';
  }
  function tg(on) { return '<i class="nxtg' + (on ? ' on' : '') + '"></i>'; }

  function drawSettings() {
    var S = window.SCNative, auto = false, tun = false, ver = '', au = true;
    try { au = !!S.autoUpdate(); } catch (e) {}
    try { auto = !!S.autoStart(); } catch (e) {}
    try { tun = !!S.tunMode(); } catch (e) {}
    try { ver = S.version(); } catch (e) {}
    set.innerHTML = '<div class="nxp"><div class="nxsl">НАСТРОЙКИ</div><div class="nxlist">' +
      row('bolt', 'c4', 'Запускать с Windows', 'и сразу включать VPN', 'scSetAuto()', tg(auto)) +
      (S.tunMode ? row('shield', 'c2', 'Весь компьютер', tun ? 'игры и все программы — через VPN' : 'сейчас — браузеры и большинство программ', 'scTun()', tg(tun)) : '') +
      row('phone', 'c2', 'Исключения', 'домены и приложения напрямую · скоро. Российские сайты и банки уже идут напрямую', '', '', true) +
      row('clock', 'c5', 'Сворачивание в трей', 'кнопка × прячет окно, VPN продолжает работать. Выход — значок у часов → «Выход»', '', '', true) +
      row('bolt', 'c4', 'Автообновление', 'скачивать и ставить новые версии само', 'scSetAu()', tg(au)) +
      row('clock', 'c5', 'Проверить обновления', 'сейчас v' + ver, 'scCheckUpd()') +
      row('gear', 'c5', 'Отправить логи', 'архив ляжет на рабочий стол — отправьте его в поддержку', 'SCNative.advanced()') +
      row('ticket', 'c3', 'Код для входа на другом устройстве', '', 'myCode()') +
      (typeof logout === 'function' ? row('user', 'c6', 'Выйти из аккаунта', '', 'logout()') : '') +
      '</div><div class="scft">Siberia Connect' + (ver ? ' · v' + ver : '') + '</div></div>';
  }

  window.scSetAuto = function () {
    var S = window.SCNative, on = !S.autoStart();
    try { S.setAutoStart(on); } catch (e) {}
    try { toast(on ? '✅ Будет запускаться с Windows и включать VPN' : 'Автозапуск выключен'); } catch (e) {}
    setTimeout(function () { if (set.classList.contains('on')) drawSettings(); }, 150);
  };

  window.scSetAu = function () {
    var S = window.SCNative, on = !S.autoUpdate();
    try { S.setAutoUpdate(on); } catch (e) {}
    try { toast(on ? 'Автообновление включено' : 'Автообновление выключено'); } catch (e) {}
    setTimeout(function () { if (set.classList.contains('on')) drawSettings(); }, 150);
  };

  window.scCheckUpd = function () {
    try { toast('Проверяю обновления…'); window.SCNative.checkUpdate(); } catch (e) {}
  };

  /* ───── правки текстов и реферальная карточка ───── */
  var TITLE = { 'Защищено': 'Подключено', 'Нажми на снежинку': 'Отключено' };

  function tidy() {
    var t = $('wtitle');
    if (t && TITLE[t.textContent]) t.textContent = TITLE[t.textContent];

    var ip = $('wip');
    if (ip && !ip.querySelector('.scip')) {
      var sm = ip.querySelector('small'), addr = '';
      for (var n = ip.firstChild; n; n = n.nextSibling) if (n.nodeType === 3) addr += n.textContent;
      addr = addr.trim() || '—';
      var tx = sm ? sm.textContent : '', ok = !!(sm && sm.classList.contains('ok'));
      var tag = ok ? 'Через VPN' : /настоящий/i.test(tx) ? 'Настоящий адрес' : /напрямую/i.test(tx) ? 'Напрямую' : /провер/i.test(tx) ? 'проверяю…' : tx;
      ip.textContent = '';
      var a = document.createElement('span'); a.className = 'scip'; a.appendChild(document.createTextNode('Мой IP: '));
      var b = document.createElement('b'); b.textContent = addr; a.appendChild(b);
      var s2 = document.createElement('small'); if (ok) s2.className = 'ok'; s2.textContent = '· ' + tag;
      ip.appendChild(a); ip.appendChild(s2);
    }

    var prof = $('s-profile');
    if (prof && prof.querySelector('.nxp')) compactProfile(prof);
  }

  /* ───── «Профиль»: статус подписки и четыре плитки вместо длинного списка ───── */
  var SITE = 'https://sibir-cloud-api.site/account';
  var PI = {
    dev: '<rect x="6" y="3" width="12" height="18" rx="2.5"/><path d="M11 17h2"/>',
    gift: '<path d="M20 12v9H4v-9M2.5 7h19v5h-19zM12 7v14"/><path d="M12 7H7.5a2.5 2.5 0 1 1 2.4-3.2C10.4 5.4 12 7 12 7Zm0 0h4.5a2.5 2.5 0 1 0-2.4-3.2C13.6 5.4 12 7 12 7Z"/>',
    cab: '<rect x="3.5" y="5" width="17" height="14" rx="2.5"/><path d="M3.5 9.5h17M7 14h4"/>',
    sup: '<path d="M4 13v-2a8 8 0 0 1 16 0v2M4 12H3v5h4v-5H4Zm16 0h1v5h-4v-5h3ZM17 20h-5"/>'
  };
  function tile(k, title, sub, onclick, dis) {
    return '<button type="button" class="scp-tile' + (dis ? ' dis' : '') + '"' + (onclick && !dis ? ' onclick="' + onclick + '"' : '') + '><i>' + svg(PI[k]) + '</i>' +
      '<span><b>' + title + '</b><small>' + sub + '</small></span></button>';
  }
  function compactProfile(prof) {
    var d = null;
    try { d = D; } catch (e) {}
    if (!d) return;
    var act = d.status === 'active';
    var v = d.devices || {}, lim = v.limit || d.devices_std || 2;
    var pill = act ? '● Доступ активен' : d.status === 'expired' ? '● Доступ закончился' : d.status === 'blocked' ? '● Приостановлен' : '● Без подписки';
    var pct = act ? Math.max(4, Math.min(100, Math.round(d.days_left / (d.period || 30) * 100))) : 0;
    var until = String(d.until || '').split(' ')[0];
    var left = '';
    try { left = 'осталось ' + d.days_left + ' ' + dword(d.days_left); } catch (e) {}
    prof.innerHTML = '<div class="scp"><div class="scp-top">' +
      '<div class="scp-me"><i class="scp-ava">' + H((d.name || '❄').charAt(0).toUpperCase()) + '</i><div><b>' + H(d.name || 'Мой профиль') + '</b>' +
      '<span class="scp-pill' + (act ? '' : ' bad') + '">' + pill + '</span></div></div>' +
      (act ? '<div class="scp-bar"><i style="width:' + pct + '%"></i></div><div class="scp-until"><span>до ' + H(until) + '</span><span>' + left + '</span></div>' : '') +
      (d.status === 'blocked' ? '' : '<button class="scp-renew" type="button" onclick="scRenew()">' + (act ? 'Продлить подписку' : 'Выбрать тариф') + '</button>') +
      '</div><div class="scp-grid">' +
      tile('dev', 'Устройства', act ? (v.registered || 0) + ' из ' + lim : '—', 'nxDevices()', !act) +
      tile('gift', 'Пригласить друга', 'другу +6 дней · тебе +30', 'scRef()') +
      tile('cab', 'Личный кабинет', 'sibir-cloud-api.site', 'scSite()') +
      tile('sup', 'Поддержка', 'ответим в приложении', "openPage('support','profile')") +
      '</div></div>';
  }

  window.scSite = window.scRenew = function () {
    try { openUrl(SITE); } catch (e) { try { window.SCNative.openExternal(SITE); } catch (x) {} }
  };

  window.scRef = async function () {
    var head = '<div class="shi">🎁</div><b>Пригласи друга</b>';
    try { sheet(head + '<div class="lwait"><i></i>Загружаю…</div>'); } catch (e) { return; }
    var r = null;
    try { r = await act('ref'); } catch (e) {}
    if (!r || !r.ok) {
      sheet(head + '<p>' + H((r && r.error) || 'Не удалось загрузить ссылку') + '</p><button class="btn ghost" onclick="closeSheet()">Закрыть</button>');
      return;
    }
    window.__scLink = r.link; window.__scShare = r.share;
    sheet(head + '<p>Другу — <b>' + r.friend_days + ' ' + dword(r.friend_days) + '</b> бесплатно вместо ' + r.trial_days + '.<br>' +
      'Тебе — <b>+' + r.bonus + ' дней</b>, когда друг оплатит тариф.<br>' +
      'Каждые ' + r.goal + ' оплативших — ещё <b>+' + r.goal_days + ' дней</b>.</p>' +
      '<div class="keybox" onclick="copyText(__scLink,\'✅ Ссылка скопирована\')">' + H(r.link) + '</div>' +
      '<button class="btn" onclick="openTg(__scShare)">📤 Отправить другу</button>' +
      '<button class="btn ghost" onclick="closeSheet()">Закрыть</button>');
  };

  function boot() {
    try {
      var st = document.createElement('style');
      st.id = 'scc-css'; st.textContent = CSS;
      (document.head || document.documentElement).appendChild(st);
      document.documentElement.classList.add('scc');
      buildChrome();
      if (typeof go === 'function') {
        var _go = go;
        go = function () { hideSettings(); var r = _go.apply(this, arguments); setTimeout(sync, 0); return r; };
      }
      /* события обновления из приложения → плашка */
      var _sn = window.scNative;
      window.scNative = function (ev) {
        try { if (ev && ev.type === 'update') onUpd(ev); } catch (e) {}
        return _sn ? _sn.apply(this, arguments) : undefined;
      };
      /* окно «Новая версия» самой страницы заменено плашкой; принудительное обновление (min.json) по-прежнему блокирует экран */
      if (typeof nxUpdCheck === 'function') {
        var _nuc = nxUpdCheck;
        nxUpdCheck = async function () {
          try {
            var r = await fetch('dl/win.json?' + Date.now(), { cache: 'no-store' });
            var w = r.ok ? await r.json() : null;
            if (w && w.version) localStorage.setItem('upd_skip', JSON.stringify({ v: w.version, t: Date.now() }));
          } catch (e) {}
          return _nuc.apply(this, arguments);
        };
      }
      new MutationObserver(tidy).observe(document.body, { childList: true, subtree: true, characterData: true });
      setInterval(sync, 300);
      tidy(); sync();
    } catch (e) {}
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
