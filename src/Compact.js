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
    /* v-fix: «снежинка» не двигается при смене состояния (Connected/Connecting/Off).
       Кнопка жёстко центрируется внутри контейнера фиксированного размера (inset, а не flow/transform),
       место под заголовок и кнопки действий зарезервировано — высота группы не меняется,
       поэтому flex-распорки не сдвигают кнопку; полоса прокрутки не может «уехать» вбок. */
    'html.win.scc,html.win.scc body{overflow-x:hidden;scrollbar-gutter:stable both-edges}',
    /* корень бага: .wdash{align-items:start} + flex-колонка → .wcenter сжималась по ширине содержимого
       (заголовок/кнопки/таймер), и кнопка, центрированная в ней, «ехала» вправо при смене состояния */
    'html.win.scc .wdash{align-items:stretch;width:100%}',
    'html.win.scc .wcenter{align-items:center;align-self:stretch;width:100%;min-width:0}',
    'html.win.scc .wcenter>.wstats,html.win.scc .wcenter>.wsrv{align-self:stretch}',
    'html.win.scc .worbw{display:block;position:relative;align-self:center;margin-left:auto;margin-right:auto;contain:layout}',
    'html.win.scc .worb{position:absolute;inset:12%;width:auto;height:auto;margin:0;display:block}',
    'html.win.scc .worb>svg{position:absolute;inset:21%;width:58%;height:58%;transform-origin:50% 50%}',
    'html.win.scc .wtitle{height:27px;line-height:27px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:100%}',
    'html.win.scc .wact{height:40px;min-height:40px;margin:8px 0 0;align-items:center}',
    'html.win.scc .wact:empty{display:flex}',
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
  ,"html.win.scc,html.win.scc body,.sc-set{background:#0B1017!important}\nhtml.win.scc .wrap{top:42px;bottom:82px;padding:0 18px!important}\n.sc-top{height:42px;padding:0 14px 0 20px}\n.sc-brand{font-size:11px;letter-spacing:.18em;color:#8E9BAE}\n.sc-x{width:28px;height:28px;background:rgba(255,255,255,.07);color:#CBD5E1;font-size:16px;line-height:28px}\n.sc-nav{left:14px;right:14px;bottom:14px;height:58px;border-radius:18px;background:#101722!important;border:1px solid rgba(255,255,255,.08)!important;padding:4px;gap:4px}\n.sc-nav button{border-radius:13px;color:#7C8B9E;font-size:11px;gap:4px}\n.sc-nav button.on{background:rgba(56,189,248,.12);color:#38BDF8!important}\nhtml.win.scc .wstats,html.win.scc .wright{display:none!important}\nhtml.win.scc .wcenter{justify-content:space-between;padding:8px 0 6px}\nhtml.win.scc .wcenter:before,html.win.scc .wcenter:after{display:none}\nhtml.win.scc .worbw{order:1;width:214px;height:214px;margin:6px auto 0}\nhtml.win.scc .worbw:before,html.win.scc .worbw:after{content:\"\";position:absolute;border-radius:50%;pointer-events:none;border:1px solid rgba(56,189,248,.22)}\nhtml.win.scc .worbw:before{inset:0}\nhtml.win.scc .worbw:after{inset:18px;border-color:rgba(56,189,248,.13)}\nhtml.win.scc .wtitle{order:2;font-size:18px;font-weight:800;letter-spacing:.22em;text-transform:uppercase;color:#CBD5E1;margin-top:8px}\nhtml.win.scc .sc-tmr{order:3;font-family:ui-monospace,Consolas,monospace;font-size:21px;font-weight:700;letter-spacing:.14em;color:#64748B;margin-top:2px}\nhtml.win.scc .wact{order:4;margin:2px 0 0;min-height:28px;height:auto}\nhtml.win.scc .wsrv{order:5;background:#111822!important;border:1px solid rgba(255,255,255,.08)!important;border-radius:16px;padding:12px 15px;margin-top:8px}\nhtml.win.scc .wsrv small{display:block!important;font-size:10px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:#38BDF8;margin-bottom:2px}\nhtml.win.scc .wsrv b{font-size:13.5px;font-weight:700;color:#F1F5F9}"].join('\n');

  var ICON = {
    vpn: '<path d="M12 3 4.5 6v5.5c0 4.6 3.1 8.2 7.5 9.5 4.4-1.3 7.5-4.9 7.5-9.5V6z"/><path d="m9 12 2 2 4-4"/>',
    settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
    profile: '<circle cx="12" cy="8" r="3.5"/><path d="M5 20c.7-3.5 3.1-5.3 7-5.3s6.3 1.8 7 5.3"/>'
  };
  var NAMES = { vpn: 'Подключение', settings: 'Настройки', profile: 'Профиль' };

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
    top.innerHTML = '<i class="sc-brand">Siberia Connect</i><button class="sc-x" id="sc-x" type="button" aria-label="Свернуть в трей" >×</button>';
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
    unfreeze();
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
  var upd = { stage: '', pct: 0, skip: '', note: '', manual: false };

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
    if (st === 'checking') upd.note = 'Сверяю версию с сервером…';
    else if (st === 'available') { upd.note = 'Доступна новая версия v' + (ev.version || updVer()); if (upd.manual) { upd.manual = false; try { toast(upd.note); } catch (e) {} } }
    else if (st === 'ready') { upd.note = 'Обновление v' + (ev.version || updVer()) + ' готово к установке'; if (upd.manual) { upd.manual = false; try { toast(upd.note); } catch (e) {} } }
    else if (st === 'download') upd.note = 'Загрузка обновления… ' + upd.pct + '%';
    else if (st === 'error') { upd.stage = ''; upd.note = (ev.text || 'Нет связи с сервером') + ' · повторить'; if (upd.manual) { upd.manual = false; try { toast('⚠️ ' + (ev.text || 'Ошибка проверки')); } catch (e) {} } }
    else if (st === 'none') { upd.stage = ''; var cv = ''; try { cv = window.SCNative.version(); } catch (e) {} upd.note = 'Установлена последняя версия' + (cv ? ' v' + cv : '') + ' ✓'; if (upd.manual) { upd.manual = false; try { toast(upd.note); } catch (e) {} } }
    updBar(); var ss = $('sc-set'); if (ss && ss.classList.contains('on')) drawSettings();
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
      (function(){var v=updVer(),b=upd.stage==='checking';return row('clock','c5',b?'Проверяю обновления…':(v?'Обновить до v'+v:'Проверить обновления'),upd.note||('Текущая версия v'+ver),b?'':(v?'scUpdGo2()':'scCheckUpd()'),'',b);})() +
      row('gear', 'c5', 'Отправить логи', 'архив ляжет на рабочий стол — отправьте его в поддержку', 'SCNative.advanced()') +
      row('ticket', 'c3', 'Ввести ключ, код или номер', 'vless://…, VPN-XXXX-XXXX, SC-XXXXX-XXXXX или номер подписки', 'scRedeemOpen()') +
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
    if (upd.stage === 'checking') return;
    upd.manual = true; upd.stage = 'checking'; upd.note = 'Сверяю версию с сервером…';
    var ss = $('sc-set'); if (ss && ss.classList.contains('on')) drawSettings();
    try { window.SCNative.checkUpdate(); } catch (e) { upd.manual = false; upd.stage = ''; upd.note = 'Ошибка запуска проверки'; }
  };

  /* ───── 1.5.0: одно поле для ключа, кода и номера подписки (тип определяет сервер) ───── */
  var rdBusy = false;
  function esc(s) { return String(s || '').replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function rdMsg(t, cls) { ['sc-rd-msg', 'sc-au-msg'].forEach(function (id) { var m = $(id); if (m) { m.textContent = t || ''; m.className = 'sc-rd-msg ' + (cls || ''); } }); }
  function rdState(b) {
    rdBusy = b;
    var g = $('sc-rd-go'), i = $('sc-rd-in');
    if (g) { g.disabled = b; g.textContent = b ? 'Проверяю…' : 'Войти'; }
    if (i) i.disabled = b;
    var g2 = $('sc-au-go'), i2 = $('sc-au-in'), p2 = $('sc-au-paste');
    if (g2) { g2.disabled = b; g2.textContent = b ? 'Проверяю…' : 'Войти по ключу'; }
    if (i2) i2.disabled = b;
    if (p2) p2.disabled = b;
  }
  window.scRedeemOpen = function () {
    hideSettings();
    var html = '<div class="shi">🔑</div><b>Ключ, код или номер</b>' +
      '<p class="sc-rd-p">Вставь любое из этого — приложение само поймёт, что это:<br>' +
      '• ключ <code>vless://…</code> или ссылку подписки<br>• код <code>VPN-XXXX-XXXX</code> — из приглашения или с сайта<br>' +
      '• код восстановления <code>SC-XXXXX-XXXXX</code><br>• номер подписки <code>SC-12345</code> — вход подтвердишь в Telegram</p>' +
      '<input id="sc-rd-in" class="sc-rd-in" autocomplete="off" spellcheck="false" aria-label="Ключ, код или номер подписки" placeholder="Вставь сюда (Ctrl+V)">' +
      '<div id="sc-rd-msg" class="sc-rd-msg" role="status" aria-live="polite"></div>' +
      '<button class="btn" id="sc-rd-go" type="button" onclick="scRedeemGo()">Войти</button>' +
      '<button class="btn ghost" type="button" onclick="closeSheet()">Отмена</button>';
    try { sheet(html); } catch (e) { return; }
    rdState(false);
    var i = $('sc-rd-in');
    if (i) {
      i.addEventListener('keydown', function (e) { if (e.key === 'Enter') window.scRedeemGo(); });
      i.addEventListener('paste', function () { setTimeout(function () { i.value = i.value.trim(); }, 0); });
      setTimeout(function () { try { i.focus(); } catch (e) {} }, 50);
    }
  };
  window.scRedeemGo = function () {
    var i = $('sc-rd-in');
    if (!i || rdBusy) return;
    var v = i.value.replace(/[\u200B-\u200F\u2060\uFEFF]/g, '').trim();
    if (!v) { rdMsg('Вставь ключ, код или номер подписки', 'er'); i.focus(); return; }
    rdState(true); rdMsg('');
    try { window.SCNative.redeem(v); } catch (e) { rdState(false); rdMsg('Обнови приложение', 'er'); }
  };
  function onRedeem(ev) {
    if (ev.stage === 'busy') { rdState(true); return; }
    if (ev.stage === 'wait_tg') { rdMsg('📨 ' + (ev.message || 'Подтверди вход в Telegram — запрос уже в боте'), 'wt'); return; }
    rdState(false);
    if (ev.stage === 'ok') { try { closeSheet(); } catch (e) {} try { toast('✅ ' + (ev.message || 'Вход выполнен')); } catch (e) {} return; }
    rdMsg('❌ ' + (ev.message || 'Не получилось'), 'er');
  }
  /* ───── 1.6.0: ключ привязан к другому устройству (409 DEVICE_LOCKED / REBIND_AVAILABLE) ───── */
  var rbBusy = false;
  function rbMsg(t, cls) { var m = $('sc-rb-msg'); if (m) { m.textContent = t || ''; m.className = 'sc-rd-msg ' + (cls || ''); } }
  function rbState(b) {
    rbBusy = b;
    var g = $('sc-rb-go');
    if (g) { g.disabled = b; g.textContent = b ? 'Переношу…' : 'Перенести ключ на этот ПК'; }
  }
  function onLock(ev) {
    rbBusy = false;
    var can = ev.canRebind === true;
    var msg = ev.message || (can ? 'Ключ привязан к другому устройству. Его можно перенести на этот ПК.'
      : 'Ключ привязан к другому устройству. Перенос на этот ПК станет доступен позже');
    var html = '<div class="shi">🔒</div><b>Ключ привязан к другому устройству</b>' +
      '<p class="sc-rd-p">' + esc(msg) + '</p>' +
      (ev.deviceName ? '<p class="sc-rd-p">Сейчас он на устройстве: <b>' + esc(ev.deviceName) + '</b></p>' : '') +
      (can ? '<p class="sc-rd-p">После переноса на старом устройстве ключ перестанет работать.</p>' : '') +
      '<div id="sc-rb-msg" class="sc-rd-msg" role="status" aria-live="polite"></div>' +
      (can ? '<button class="btn" id="sc-rb-go" type="button" onclick="scRebindGo()">Перенести ключ на этот ПК</button>' : '') +
      '<button class="btn ghost" type="button" onclick="closeSheet()">' + (can ? 'Отмена' : 'Понятно') + '</button>';
    try { sheet(html); } catch (e) {}
  }
  window.scRebindGo = function () {
    if (rbBusy) return;
    rbState(true); rbMsg('');
    try { window.SCNative.rebind(); } catch (e) { rbState(false); rbMsg('Обнови приложение', 'er'); }
  };
  function onRebind(ev) {
    if (ev.stage === 'busy') { rbState(true); return; }
    rbState(false);
    if (ev.stage === 'ok') { try { closeSheet(); } catch (e) {} try { toast('✅ ' + (ev.message || 'Ключ перенесён') + ' — подключаю'); } catch (e) {} return; }
    rbMsg('❌ ' + (ev.message || 'Не получилось'), 'er');
  }

  /* ───── 1.7.0: единый экран входа — QR / короткий код (device flow) + Мастер-ключ ─────
     Показывается поверх страницы, пока нет входа. Опрос /tv/pair/poll идёт только когда экран виден и окно не скрыто;
     при подтверждении токен кладётся в localStorage('sc_tok') — Bridge.js сам передаёт его приложению (AdoptToken). */
  var API = location.origin + '/app/api/v1/';
  var AU = { pair: null, ends: 0, timer: 0, phase: 'idle', lastTry: 0 };

  /* QR (байтовый режим, коррекция M, версии 1–6; ISO 18004). Возвращает матрицу true/false или null. */
  var QR = (function () {
    var EXP = [], LOG = [];
    (function () { var x = 1; for (var i = 0; i < 255; i++) { EXP[i] = x; LOG[x] = i; x <<= 1; if (x & 256) x ^= 0x11D; } })();
    function mul(a, b) { return a && b ? EXP[(LOG[a] + LOG[b]) % 255] : 0; }
    function rsGen(n) { var g = [1]; for (var i = 0; i < n; i++) { var r = new Array(g.length + 1).fill(0); for (var j = 0; j < g.length; j++) { r[j] ^= g[j]; r[j + 1] ^= mul(g[j], EXP[i]); } g = r; } return g; }
    function rsRem(data, n) { var g = rsGen(n), res = new Array(n).fill(0); data.forEach(function (b) { var f = b ^ res.shift(); res.push(0); for (var i = 0; i < n; i++) res[i] ^= mul(g[i + 1], f); }); return res; }
    /* версия → [блоков, данных в блоке, EC на блок] для уровня M */
    var T = { 1: [1, 16, 10], 2: [1, 28, 16], 3: [1, 44, 26], 4: [2, 32, 18], 5: [2, 43, 24], 6: [4, 27, 16] };
    var AL = { 1: [], 2: [6, 18], 3: [6, 22], 4: [6, 26], 5: [6, 30], 6: [6, 34] };
    function utf8(s) { return Array.prototype.slice.call(new TextEncoder().encode(s)); }
    function make(text) {
      var bytes = utf8(text), v = 0;
      for (var k = 1; k <= 6; k++) if (bytes.length <= T[k][0] * T[k][1] - 2) { v = k; break; }
      if (!v) return null;
      var nb = T[v][0], dl = T[v][1], ec = T[v][2], cap = nb * dl, bits = [];
      function put(val, len) { for (var i = len - 1; i >= 0; i--) bits.push((val >>> i) & 1); }
      put(4, 4); put(bytes.length, 8); bytes.forEach(function (b) { put(b, 8); });
      put(0, Math.min(4, cap * 8 - bits.length)); while (bits.length % 8) bits.push(0);
      var cw = []; for (var i = 0; i < bits.length; i += 8) { var b = 0; for (var j = 0; j < 8; j++) b = (b << 1) | bits[i + j]; cw.push(b); }
      for (var pad = 0xEC; cw.length < cap; pad ^= 0xEC ^ 0x11) cw.push(pad);
      var blocks = [], ecs = [];
      for (var q = 0; q < nb; q++) { var d = cw.slice(q * dl, (q + 1) * dl); blocks.push(d); ecs.push(rsRem(d, ec)); }
      var all = [];
      for (var c = 0; c < dl; c++) blocks.forEach(function (d) { all.push(d[c]); });
      for (var c2 = 0; c2 < ec; c2++) ecs.forEach(function (e) { all.push(e[c2]); });
      var n = 17 + 4 * v, M = [], F = [];
      for (var y = 0; y < n; y++) { M.push(new Array(n).fill(false)); F.push(new Array(n).fill(false)); }
      function fn(x, y2, dark) { M[y2][x] = !!dark; F[y2][x] = true; }
      function finder(cx, cy) { for (var dy = -4; dy <= 4; dy++) for (var dx = -4; dx <= 4; dx++) { var x = cx + dx, y2 = cy + dy; if (x < 0 || y2 < 0 || x >= n || y2 >= n) continue; var di = Math.max(Math.abs(dx), Math.abs(dy)); fn(x, y2, di !== 2 && di !== 4); } }
      finder(3, 3); finder(n - 4, 3); finder(3, n - 4);
      for (var t = 0; t < n; t++) { fn(6, t, t % 2 === 0); fn(t, 6, t % 2 === 0); }
      var ap = AL[v];
      ap.forEach(function (ay) { ap.forEach(function (ax) {
        if ((ax === 6 && ay === 6) || (ax === 6 && ay === ap[ap.length - 1]) || (ax === ap[ap.length - 1] && ay === 6)) return;
        for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++) fn(ax + dx, ay + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
      }); });
      function fmt(mask) {
        var data = mask, rem = data;                              /* уровень M = 00, затем 3 бита маски */
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
        var b = ((data << 10) | rem) ^ 0x5412;
        function g(i2) { return ((b >>> i2) & 1) === 1; }
        for (var i3 = 0; i3 <= 5; i3++) fn(8, i3, g(i3));
        fn(8, 7, g(6)); fn(8, 8, g(7)); fn(7, 8, g(8));
        for (var i4 = 9; i4 < 15; i4++) fn(14 - i4, 8, g(i4));
        for (var i5 = 0; i5 < 8; i5++) fn(n - 1 - i5, 8, g(i5));
        for (var i6 = 8; i6 < 15; i6++) fn(8, n - 15 + i6, g(i6));
        fn(8, n - 8, true);
      }
      fmt(0);                                                     /* резервируем области формата */
      var p = 0;
      for (var right = n - 1; right >= 1; right -= 2) {
        if (right === 6) right = 5;
        for (var vert = 0; vert < n; vert++) for (var j2 = 0; j2 < 2; j2++) {
          var x2 = right - j2, up = ((right + 1) & 2) === 0, y3 = up ? n - 1 - vert : vert;
          if (!F[y3][x2] && p < all.length * 8) { M[y3][x2] = ((all[p >>> 3] >>> (7 - (p & 7))) & 1) === 1; p++; }
        }
      }
      var MASK = [function (x, y) { return (x + y) % 2 === 0; }, function (x, y) { return y % 2 === 0; }, function (x) { return x % 3 === 0; },
        function (x, y) { return (x + y) % 3 === 0; }, function (x, y) { return (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0; },
        function (x, y) { return x * y % 2 + x * y % 3 === 0; }, function (x, y) { return (x * y % 2 + x * y % 3) % 2 === 0; },
        function (x, y) { return ((x + y) % 2 + x * y % 3) % 2 === 0; }];
      function applyMask(m) { for (var y4 = 0; y4 < n; y4++) for (var x3 = 0; x3 < n; x3++) if (!F[y4][x3] && MASK[m](x3, y4)) M[y4][x3] = !M[y4][x3]; }
      function penalty() {
        var pn = 0, dark = 0;
        for (var a = 0; a < n; a++) for (var dir = 0; dir < 2; dir++) {
          var run = 1;
          for (var b2 = 1; b2 < n; b2++) {
            var cur = dir ? M[b2][a] : M[a][b2], prev = dir ? M[b2 - 1][a] : M[a][b2 - 1];
            if (cur === prev) { run++; if (run === 5) pn += 3; else if (run > 5) pn++; } else run = 1;
          }
        }
        for (var y5 = 0; y5 < n; y5++) for (var x4 = 0; x4 < n; x4++) {
          if (M[y5][x4]) dark++;
          if (x4 + 1 < n && y5 + 1 < n && M[y5][x4] === M[y5][x4 + 1] && M[y5][x4] === M[y5 + 1][x4] && M[y5][x4] === M[y5 + 1][x4 + 1]) pn += 3;
        }
        return pn + Math.floor(Math.abs(dark * 20 - n * n * 10) / (n * n)) * 10;
      }
      var best = 0, bestP = 1e9;
      for (var m = 0; m < 8; m++) { applyMask(m); fmt(m); var sc = penalty(); if (sc < bestP) { bestP = sc; best = m; } applyMask(m); }
      applyMask(best); fmt(best);
      return M;
    }
    return { make: make };
  })();
  window.__scQr = QR.make;

  function qrSvg(text) {
    var M = QR.make(text);
    if (!M) return '';
    var n = M.length, q = 3, d = '';
    for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) if (M[y][x]) d += 'M' + (x + q) + ' ' + (y + q) + 'h1v1h-1z';
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + (n + 2 * q) + ' ' + (n + 2 * q) + '" shape-rendering="crispEdges" role="img" ' +
      'aria-label="QR-код для входа"><rect width="100%" height="100%" fill="#fff"/><path d="' + d + '" fill="#050D1A"/></svg>';
  }

  var AU_CSS = '#sc-auth{position:fixed;left:0;right:0;top:34px;bottom:0;z-index:50;overflow-y:auto;background:#060F2A;padding:6px 14px 18px;box-sizing:border-box;display:none;color:#DFF1FF}' +
    '#sc-auth.on{display:block}#sc-auth.hold{display:none}' +
    '#sc-auth h1{font-size:19px;margin:6px 0 12px;text-align:center}' +
    '.sc-au-g{display:grid;grid-template-columns:minmax(0,1fr);gap:14px;max-width:900px;margin:0 auto}' +
    '@media (min-width:720px){.sc-au-g{grid-template-columns:minmax(0,1fr) minmax(0,1fr)}}' +
    '.sc-au-c{background:#102444;border:1px solid #1C5A80;border-radius:18px;padding:16px;display:flex;flex-direction:column;align-items:center;gap:10px;text-align:center}' +
    '.sc-au-c h2{font-size:15px;margin:0}.sc-au-s{font-size:12.5px;line-height:1.5;color:#9CC3E8;margin:0}' +
    '.sc-au-qr{position:relative;width:184px;height:184px;border-radius:14px;background:#fff;overflow:hidden;display:flex;align-items:center;justify-content:center}' +
    '.sc-au-qr svg{width:100%;height:100%;display:block}#sc-au-img{width:100%;height:100%}' +
    '.sc-au-ov{position:absolute;left:0;right:0;top:0;bottom:0;background:rgba(255,255,255,.94);display:none;flex-direction:column;align-items:center;justify-content:center;gap:10px;color:#1B2B44;font-size:13px;padding:8px}' +
    '.sc-au-qr.ov .sc-au-ov{display:flex}.sc-au-qr.ov svg{visibility:hidden}' +
    '.sc-au-code{font:700 28px/1 Consolas,monospace;letter-spacing:.14em;color:#00D2FF}' +
    '.sc-au-in{width:100%;box-sizing:border-box;min-height:46px;border-radius:12px;border:1px solid rgba(140,200,255,.3);background:rgba(0,0,0,.25);color:#fff;font-size:14px;padding:0 12px}' +
    '.sc-au-in:focus{outline:none;border-color:#7fd3ff;box-shadow:0 0 0 3px rgba(127,211,255,.2)}' +
    '.sc-au-row{display:flex;gap:8px;width:100%}.sc-au-row>*{flex:1}.sc-au-row>.sc-au-b2{flex:2}' +
    '.sc-au-b{min-height:44px;border-radius:12px;border:1px solid rgba(127,211,255,.4);background:rgba(10,30,70,.9);color:#cfeaff;font:inherit;font-size:14px;cursor:pointer;padding:0 12px}' +
    '.sc-au-b.p{background:linear-gradient(90deg,#00D2FF,#0080FF);color:#03121F;border:0;font-weight:700}' +
    '.sc-au-b:disabled{opacity:.55;cursor:default}.sc-au-b:focus-visible,.sc-au-in:focus-visible{outline:3px solid #fff;outline-offset:2px}' +
    '#sc-au-help{position:fixed;left:0;right:0;top:0;bottom:0;z-index:60;background:rgba(2,8,20,.78);display:none;align-items:center;justify-content:center;padding:18px}' +
    '#sc-au-help.on{display:flex}#sc-au-help .box{background:#102444;border:1px solid #1C5A80;border-radius:18px;padding:18px;max-width:420px;text-align:left;font-size:13.5px;line-height:1.6}' +
    '#sc-au-help .t{display:block;font-size:16px;font-weight:700;margin-bottom:8px}#sc-au-help p{margin:0 0 8px}';

  function auBuild() {
    var st = document.createElement('style'); st.id = 'scc-au-css'; st.textContent = AU_CSS;
    (document.head || document.documentElement).appendChild(st);
    var el = document.createElement('div'); el.id = 'sc-auth';
    el.innerHTML = '<h1>Вход в Siberia Connect</h1><div class="sc-au-g">' +
      '<section class="sc-au-c" aria-labelledby="sc-au-h2"><h2 id="sc-au-h2">Войдите по мастер-ключу</h2>' +
      '<input id="sc-au-in" class="sc-au-in" autocomplete="off" spellcheck="false" aria-label="Мастер-ключ" placeholder="Мастер-ключ (Ctrl+V)">' +
      '<div id="sc-au-msg" class="sc-rd-msg" role="status" aria-live="polite" style="width:100%;text-align:left"></div>' +
      '<div class="sc-au-row"><button type="button" class="sc-au-b" id="sc-au-paste">Вставить</button>' +
      '<button type="button" class="sc-au-b p sc-au-b2" id="sc-au-go">Войти по ключу</button></div>' +
      '<p class="sc-au-s">Мастер-ключ доступен в личном кабинете на сайте или в Telegram-боте</p></section></div>';
    document.body.appendChild(el);
    var inp = $('sc-au-in');
    inp.addEventListener('keydown', function (e) { if (e.key === 'Enter') auGo(); });
    inp.addEventListener('paste', function () { setTimeout(function () { inp.value = inp.value.trim(); }, 0); });
    $('sc-au-go').addEventListener('click', auGo);
    $('sc-au-paste').addEventListener('click', function () {
      if (rdBusy) return;
      var done = function (t) { inp.value = String(t || '').trim(); inp.focus(); };
      try { navigator.clipboard.readText().then(done, function () { inp.focus(); rdMsg('Нажмите Ctrl+V — браузер не дал прочитать буфер', 'er'); }); }
      catch (e) { inp.focus(); rdMsg('Нажмите Ctrl+V', 'er'); }
    });
    return el;
  }
  function auGo() {
    var i = $('sc-au-in');
    if (!i || rdBusy) return;
    var v = i.value.replace(/[\u200B-\u200F\u2060\uFEFF]/g, '').trim();
    i.value = v;
    if (!v) { rdMsg('Вставьте мастер-ключ', 'er'); i.focus(); return; }
    rdState(true); rdMsg('');
    try { window.SCNative.redeem(v); } catch (e) { rdState(false); rdMsg('Обновите приложение', 'er'); }
  }
  function auOv(txt, showBtn) {
    var q = $('sc-au-qr'); if (!q) return;
    q.classList.toggle('ov', !!txt);
    var t = $('sc-au-ovt'), b = $('sc-au-new');
    if (t) t.textContent = txt || '';
    if (b) b.style.display = showBtn ? '' : 'none';
    if (showBtn && b) { try { b.focus(); } catch (e) {} }
  }
  function auStop() { if (AU.timer) { clearTimeout(AU.timer); AU.timer = 0; } }
  function auPost(path, body) {
    return fetch(API + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), cache: 'no-store' })
      .then(function (r) { return r.json().catch(function () { return {}; }); });
  }
  function auNew() {
    auStop(); AU.pair = null; AU.phase = 'loading'; AU.lastTry = Date.now();
    $('sc-au-img').innerHTML = ''; $('sc-au-code').textContent = ''; $('sc-au-cap').style.display = 'none'; $('sc-au-open').style.display = 'none';
    auOv('Готовим код…', false);
    var did = '';
    try { did = window.SCNative.deviceId() || ''; } catch (e) {}
    if (!did) { AU.phase = 'error'; auOv('Обновите приложение, чтобы войти по QR', false); return; }
    auPost('tv/pair/start', { did: did, device_name: 'Windows · ' + (window.SCNative.version ? 'Siberia Connect ' + window.SCNative.version() : 'ПК') }).then(function (d) {
      if (!d || !d.ok || !d.pair_id) { AU.phase = 'error'; auOv((d && d.error) || 'Не удалось получить код. Проверьте интернет.', true); return; }
      AU.pair = d; AU.ends = Date.now() + (d.expires_in || 600) * 1000; AU.phase = 'wait';
      $('sc-au-img').innerHTML = qrSvg(d.qr_url);
      $('sc-au-code').textContent = d.user_code || ''; $('sc-au-cap').style.display = ''; $('sc-au-open').style.display = '';
      auOv('', false); auSchedule();
    }).catch(function () { AU.phase = 'error'; auOv('Не удалось получить код. Проверьте интернет.', true); });
  }
  function auSchedule() {
    auStop();
    if (AU.phase !== 'wait' || !AU.pair) return;
    AU.timer = setTimeout(auPoll, Math.max(2, Math.min(10, AU.pair.interval || 3)) * 1000);
  }
  function auPoll() {
    AU.timer = 0;
    if (AU.phase !== 'wait' || !AU.pair) return;
    if (Date.now() >= AU.ends) { AU.phase = 'expired'; AU.pair = null; auOv('Время действия кода вышло', true); return; }
    var p = AU.pair;
    auPost('tv/pair/poll', { pair_id: p.pair_id, device_secret: p.device_secret }).then(function (d) {
      if (AU.pair !== p || AU.phase !== 'wait') return;                      // за время запроса экран закрыли / код обновили
      if (d && d.ok && d.state === 'approved' && d.token) {
        AU.phase = 'done'; AU.pair = null; auStop(); auOv('Подтверждено! Входим…', false);
        try { localStorage.setItem('sc_tok', String(d.token)); } catch (e) {}
        setTimeout(function () { location.reload(); }, 900);
        return;
      }
      if (d && d.ok && d.state && d.state !== 'pending') { AU.phase = 'expired'; AU.pair = null; auOv('Время действия кода вышло', true); return; }
      auSchedule();
    }).catch(function () { if (AU.pair === p) auSchedule(); });             // нет связи — повторим
  }
  /* раз в 0,5 с: показать/скрыть экран и включить/остановить опрос (окно свёрнуто или экран закрыт — поллинг стоит) */
  function authTick() {
    var need = false;
    try { need = !window.SCNative.loggedIn(); } catch (e) {}
    var el = $('sc-auth') || (need ? auBuild() : null);
    if (!el) return;
    var sh = document.getElementById('wsheet');
    el.classList.toggle('on', need);
    el.classList.toggle('hold', need && !!sh && sh.classList.contains('on'));
  }
  document.addEventListener('visibilitychange', function () { if (document.hidden) auStop(); else setTimeout(authTick, 0); });

  /* ───── 1.6.0: «снежинка» ─────
     1) Страница ставит html.paused (все анимации замирают), когда document.hidden, и снимает по visibilitychange. После сворачивания
        в трей / сна / смены окна WebView2 может не прислать обратное событие — кнопка так и остаётся «замороженной».
        Поэтому видимое окно само снимает паузу и перезапускает анимации кнопки (раз в 300 мс — вместе с sync, и по событиям).
     2) Кольца и волна лежат над/рядом с кнопкой: им нельзя ловить клики (иначе кольцо с анимацией перехватывает нажатие). */
  var FLAKE_CSS = 'html.win.scc .wzring,html.win.scc .wzring2,html.win.scc .wtick,html.win.scc .wwave{pointer-events:none}' +
    'html.win.scc .worb{z-index:1;touch-action:manipulation;-webkit-tap-highlight-color:transparent}' +
    'html.win.scc .worb:focus{outline:none}html.win.scc .worb:focus-visible{outline:2px solid #7FD3FF;outline-offset:6px}';
  function kickFlake() {
    var o = $('worb');
    if (!o) return;
    try {
      o.style.animation = 'none';
      var s = o.firstElementChild;
      if (s && s.style) s.style.animation = 'none';
      void o.offsetWidth;
      o.style.animation = '';
      if (s && s.style) s.style.animation = '';
    } catch (e) {}
  }
  function unfreeze() {
    var d = document.documentElement;
    if (!document.hidden && d.classList.contains('paused')) { d.classList.remove('paused'); kickFlake(); }
  }

  var RD_CSS = '.sc-rd-in{width:100%;box-sizing:border-box;min-height:48px;border-radius:14px;border:1px solid rgba(140,200,255,.3);' +
    'background:rgba(0,0,0,.25);color:#fff;font-size:15px;padding:0 14px;margin:6px 0 4px}.sc-rd-in:focus{outline:none;border-color:#7fd3ff;' +
    'box-shadow:0 0 0 3px rgba(127,211,255,.2)}.sc-rd-p{text-align:left;font-size:13px;line-height:1.55}.sc-rd-p code{font-size:12px}' +
    '.sc-rd-msg{min-height:20px;font-size:13px;margin:4px 0 8px}.sc-rd-msg.er{color:#ff8a8a}.sc-rd-msg.wt{color:#7fd3ff}' +
    '.sc-rd-fab{position:fixed;left:50%;transform:translateX(-50%);bottom:84px;z-index:60;border:1px solid rgba(127,211,255,.35);' +
    'background:rgba(10,30,70,.92);color:#cfeaff;border-radius:22px;padding:10px 18px;font-size:14px;cursor:pointer;display:none}' +
    '.sc-rd-fab.on{display:block}.sc-rd-fab:focus-visible{outline:2px solid #7fd3ff;outline-offset:2px}';
  function rdFab() {
    var f = $('sc-rd-fab');
    if (!f) {
      f = document.createElement('button');
      f.id = 'sc-rd-fab'; f.type = 'button'; f.className = 'sc-rd-fab'; f.textContent = '🔑 Ключ или код';
      f.addEventListener('click', function () { window.scRedeemOpen(); });
      document.body.appendChild(f);
    }
    var on = false;
    try { on = !window.SCNative.loggedIn() && !document.getElementById('wsheet')?.classList.contains('on'); } catch (e) {}
    f.classList.toggle('on', on);
  }

  /* ───── правки текстов и реферальная карточка ───── */
  var TITLE = { 'Защищено': 'Подключено', 'Нажми на снежинку': 'Отключено' };

  function tidy() {var t=$("wtitle");if(t){var wc=document.querySelector(".wcenter");if(wc&&!document.querySelector(".sc-tmr")){var tm=document.createElement("div");tm.className="sc-tmr";tm.id="sc-tmr";tm.textContent="00:00:00";t.after(tm);}var tm=$("sc-tmr"),w1=$("wt1");if(tm&&w1){var v=(w1.textContent||"").trim();tm.textContent=/\d/.test(v)?v:"00:00:00";tm.style.color=/\d/.test(v)?"#E2E8F0":"#64748B";}}var sr=document.querySelector(".wsrv small");if(sr&&sr.textContent!=="УЗЕЛ")sr.textContent="УЗЕЛ";
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
      tile('gift', 'Пригласить друга', 'бонус, когда друг оплатит', 'scRef()') +
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
    /* ref_v4 (19.11.2026): другу — пробный + бонус (по умолчанию 2 + 1 дня, лимит ГБ); тебе — дни ТОЛЬКО за первую оплату друга. Цифры — с сервера. */
    var pay = r.days_pay || r.bonus || 7;
    sheet(head + '<p>Другу — <b>' + r.friend_days + ' ' + dword(r.friend_days) + '</b> бесплатно' + (r.trial_gb ? ' (' + r.trial_gb + ' ГБ на тест)' : '') + '.<br>' +
      'Тебе — <b>+' + pay + ' ' + dword(pay) + '</b>, когда друг оформит первую подписку.</p>' +
      '<div class="keybox" onclick="copyText(__scLink,\'✅ Ссылка скопирована\')">' + H(r.link) + '</div>' +
      '<button class="btn" onclick="openTg(__scShare)">📤 Отправить другу</button>' +
      '<button class="btn ghost" onclick="closeSheet()">Закрыть</button>');
  };

  /* ───── 1.7.0: спокойный статус сбоя и «Написать в поддержку» с кодом обращения ─────
     Плашка появляется ТОЛЬКО когда сервер принял отчёт о реальном сбое (событие incident с кодом INC-XXXXXX).
     Никаких ручных кнопок диагностики: проверки идут в фоне, в приложении — одна короткая фраза и один выход. */
  var INC_CSS = '#sc-inc{position:fixed;left:12px;right:12px;bottom:66px;z-index:11;display:none;flex-wrap:wrap;align-items:center;gap:8px;padding:10px 34px 10px 12px;' +
    'border-radius:14px;background:rgba(14,32,72,.96);border:1px solid rgba(140,200,255,.22);color:#DCEBFF;font-size:12.5px;line-height:1.4}' +
    '#sc-inc.on{display:flex}#sc-inc span{flex:1 1 100%}#sc-inc button{border:0;border-radius:10px;padding:8px 12px;background:linear-gradient(90deg,#2563eb,#38bdf8);color:#fff;font:inherit;font-weight:700;cursor:pointer}' +
    '#sc-inc small{opacity:.6;font-size:11px}#sc-inc i{position:absolute;right:10px;top:8px;font-style:normal;cursor:pointer;opacity:.6;font-size:16px}#sc-inc i:hover{opacity:1}';
  var incT = 0;
  function incHide() { clearTimeout(incT); var b = $('sc-inc'); if (b) b.classList.remove('on'); }
  function onIncident(ev) {
    var code = String(ev && ev.code || '').replace(/[^A-Z0-9-]/g, '').slice(0, 12);
    if (!code) return;
    var b = $('sc-inc');
    if (!b) { b = document.createElement('div'); b.id = 'sc-inc'; b.setAttribute('role', 'status'); document.body.appendChild(b); }
    b.innerHTML = '<span>Не удалось подключиться. Попробуйте сменить локацию</span>' +
      '<button type="button" id="sc-inc-go">Написать в поддержку</button><small>Код обращения ' + code + '</small><i id="sc-inc-x" aria-label="Закрыть">×</i>';
    $('sc-inc-go').addEventListener('click', function () {
      try { navigator.clipboard.writeText(code); } catch (e) {}
      try { window.SCNative.openExternal('https://t.me/SibirHelpBot?start=' + code); } catch (e) {}
      try { toast('Код ' + code + ' скопирован — вставьте его в сообщение'); } catch (e) {}
    });
    $('sc-inc-x').addEventListener('click', incHide);
    b.classList.add('on');
    clearTimeout(incT); incT = setTimeout(incHide, 10 * 60 * 1000);
  }

  function boot() {
    try {
      var ist = document.createElement('style');
      ist.id = 'scc-inc-css'; ist.textContent = INC_CSS;
      (document.head || document.documentElement).appendChild(ist);
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
        try { if (ev && ev.type === 'incident') { onIncident(ev); return; } } catch (e) {}
        try { if (ev && ev.type === 'state' && (ev.state === 'on' || ev.state === 'connecting')) incHide(); } catch (e) {}
        try { if (ev && ev.type === 'redeem') { onRedeem(ev); return; } } catch (e) {}
        try { if (ev && ev.type === 'rebind') { onRebind(ev); return; } } catch (e) {}
        try { if (ev && ev.type === 'account' && ev.state === 'device_locked') onLock(ev); } catch (e) {}   // страница тоже получит событие
        return _sn ? _sn.apply(this, arguments) : undefined;
      };
      var rst = document.createElement('style');
      rst.id = 'scc-rd-css'; rst.textContent = RD_CSS;
      (document.head || document.documentElement).appendChild(rst);
      setInterval(authTick, 500); authTick();
      var fst = document.createElement('style');
      fst.id = 'scc-flake-css'; fst.textContent = FLAKE_CSS;
      (document.head || document.documentElement).appendChild(fst);
      ['visibilitychange', 'focus', 'resize', 'pageshow'].forEach(function (n) {
        (n === 'visibilitychange' ? document : window).addEventListener(n, function () { setTimeout(unfreeze, 50); });
      });
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
