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
    'html.win.scc .wrap:has(#s-home.on){overflow-y:hidden}',
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
    /* вкладка «Настройки» */
    '.sc-set{display:none;position:fixed;left:0;right:0;top:34px;bottom:58px;z-index:8;padding:6px 14px 10px;overflow-y:auto;overflow-x:hidden;scrollbar-width:none;background:#060F2A}',
    '.sc-set::-webkit-scrollbar{display:none}',
    '.sc-set.on{display:block}',
    '.sc-set .scdis{opacity:.5;cursor:default}',
    '.sc-set .scft{margin-top:14px;text-align:center;font-size:11px;color:#6F8DB0}',
    /* реферальная карточка в «Профиле» */
    '.scref{margin:12px 0;padding:14px;border:1px solid rgba(138,124,255,.28);border-radius:16px;background:linear-gradient(135deg,rgba(81,68,153,.3),rgba(13,34,72,.86));text-align:left}',
    '.scref b{font-size:14px}',
    '.scref p{margin:6px 0 10px;font-size:12px;line-height:1.5;color:#9FB8D6}'
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
  }

  /* ───── вкладка «Настройки» ───── */
  function row(i, cls, title, sub, onclick, right, dis) {
    return '<div class="nxrow' + (dis ? ' scdis' : '') + '"' + (onclick ? ' onclick="' + onclick + '"' : '') + '><i class="nxi ' + cls + '">' + ico(i) + '</i>' +
      '<div><b>' + title + '</b>' + (sub ? '<small>' + sub + '</small>' : '') + '</div>' + (right || '') + '</div>';
  }
  function tg(on) { return '<i class="nxtg' + (on ? ' on' : '') + '"></i>'; }

  function drawSettings() {
    var S = window.SCNative, auto = false, tun = false, ver = '';
    try { auto = !!S.autoStart(); } catch (e) {}
    try { tun = !!S.tunMode(); } catch (e) {}
    try { ver = S.version(); } catch (e) {}
    set.innerHTML = '<div class="nxp"><div class="nxsl">НАСТРОЙКИ</div><div class="nxlist">' +
      row('bolt', 'c4', 'Запускать с Windows', 'и сразу включать VPN', 'scSetAuto()', tg(auto)) +
      (S.tunMode ? row('shield', 'c2', 'Весь компьютер', tun ? 'игры и все программы — через VPN' : 'сейчас — браузеры и большинство программ', 'scTun()', tg(tun)) : '') +
      row('phone', 'c2', 'Исключения', 'домены и приложения напрямую · скоро. Российские сайты и банки уже идут напрямую', '', '', true) +
      row('clock', 'c5', 'Сворачивание в трей', 'кнопка × прячет окно, VPN продолжает работать. Выход — значок у часов → «Выход»', '', '', true) +
      row('gear', 'c5', 'Отправить логи', 'архив ляжет на рабочий стол — отправьте его в поддержку', 'SCNative.advanced()') +
      '</div><div class="scft">Siberia Connect' + (ver ? ' · v' + ver : '') + '</div></div>';
  }

  window.scSetAuto = function () {
    var S = window.SCNative, on = !S.autoStart();
    try { S.setAutoStart(on); } catch (e) {}
    try { toast(on ? '✅ Будет запускаться с Windows и включать VPN' : 'Автозапуск выключен'); } catch (e) {}
    setTimeout(function () { if (set.classList.contains('on')) drawSettings(); }, 150);
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
    if (prof && !$('sc-ref')) {
      var nxp = prof.querySelector('.nxp');
      if (nxp) {
        var card = document.createElement('div');
        card.id = 'sc-ref'; card.className = 'scref';
        card.innerHTML = '<b>🎁 Пригласи друга</b><p>Другу — 6 дней бесплатно, тебе — +30 дней за каждых троих, кто оплатит.</p>' +
          '<button class="btn" type="button" onclick="go(\'bonus\',\'profile\')">Получить ссылку</button>';
        var first = nxp.querySelector('.nxcard');
        if (first && first.parentNode === nxp) nxp.insertBefore(card, first.nextSibling); else nxp.insertBefore(card, nxp.firstChild);
      }
    }
  }

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
      new MutationObserver(tidy).observe(document.body, { childList: true, subtree: true, characterData: true });
      setInterval(sync, 300);
      tidy(); sync();
    } catch (e) {}
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
