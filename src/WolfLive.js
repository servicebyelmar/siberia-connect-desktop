// Loaded after the cabinet's inline scripts and before the first user gesture.
(function (install) {
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', install, { once: true });
  else install();
})(function () {
/* Siberia Connect | shared circular portrait and user-triggered howl. */
(function () {
  'use strict';
  if (window.top !== window || window.SCNative?.platform !== 'windows' || location.protocol !== 'https:') return;
  if (window.WOLF?.hero?.().includes('sc-living-wolf')) return;
  const native = window.SCNative;
  const web = new URL('img/', location.href).href;
  let assets = web;
  try { if (native?.wolfMediaVersion?.() === 1) assets = native.assetBase() || web; } catch (_) {}
  const idle = assets + 'wolf-idle.webp';
  const howl = assets + 'wolf-howl.webp';
  // The landing page's V22 cue uses the original recording from 13.35 s for 4.9 s.
  const howlDurationMs = 4900;
  const fallback = web + 'wolf-portrait.svg';
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  let enabled = true;
  try { enabled = localStorage.getItem('sc_wolf_sound') !== 'off'; } catch (_) {}

  const audio = new Audio();
  const supportsOgg = !audio.canPlayType || audio.canPlayType('audio/ogg; codecs="vorbis"');
  audio.src = assets + (supportsOgg ? 'wolf-howl.ogg' : 'wolf-howl.mp3');
  audio.preload = 'auto';
  audio.volume = 0.75;

  const animated = new Image();
  animated.src = howl;
  const still = new Image();
  still.src = idle;
  const animations = new WeakMap();
  let active = null;

  function loadPortrait(img) {
    if (!img || img.dataset.scWolf === '1') return;
    img.dataset.scWolf = '1';
    img.addEventListener('error', () => {
      if (!img.src.endsWith('/wolf-portrait.svg')) img.src = fallback;
    });
    img.src = idle;
  }

  function stop() {
    if (active) {
      const { img, shell, timer } = active;
      clearTimeout(timer);
      animations.delete(img);
      shell.classList.remove('sc-howling');
      if (img.isConnected) img.src = idle;
      active = null;
    }
    try { audio.pause(); audio.currentTime = 0; } catch (_) {}
  }

  function play(img, shell, fromGesture) {
    if (!img || !shell || document.hidden || (!fromGesture && (active || reduced.matches))) return;
    stop();
    // Called in the trusted click handler itself, before frames or VPN callbacks.
    if (fromGesture && enabled) {
      try { audio.play()?.catch(() => {}); } catch (_) {}
    }
    if (reduced.matches) return;
    const animation = { img, shell, fromGesture };
    active = animation;
    animations.set(img, animation);
    shell.classList.add('sc-howling');
    img.src = idle;
    requestAnimationFrame(() => requestAnimationFrame(() => {
      if (img.isConnected && animations.get(img) === animation && !document.hidden) img.src = howl;
    }));
    animation.timer = setTimeout(() => {
      if (active !== animation) return;
      animations.delete(img);
      shell.classList.remove('sc-howling');
      if (img.isConnected) img.src = idle;
      active = null;
    }, howlDurationMs);
  }

  if (native) {
    let previous = null;
    window.WOLF = {
      enabled: true,
      hero: () => '<div class="wf sc-living-wolf" data-w="idle"><i class="wf-glow"></i>' +
        '<div class="wf-m sc-wolf-orbit"><img class="wf-i sc-wolf-real" src="' + idle + '" alt="" decoding="async"></div></div>',
      sync(btn) {
        const img = btn?.querySelector('.wf-i');
        if (!img) return;
        loadPortrait(img);
        const state = btn.classList.contains('s-wait') ? 'connecting' : btn.classList.contains('s-on') ? 'connected' : 'idle';
        const shell = img.closest('.wf');
        if (!shell) return;
        shell.dataset.w = state;
        shell.classList.toggle('wf-on', state === 'connected');
        shell.classList.toggle('sc-connecting', state === 'connecting');
        const prior = previous;
        previous = state;
        if (state === 'connected' && prior === 'connecting') play(img, shell, false);
        if (state === 'idle' && prior === 'connecting' && active && !active.fromGesture) stop();
      }
    };
    window.WOLF.sync(document.getElementById('anc'));
    document.querySelectorAll('.wf video').forEach(video => { video.pause(); video.remove(); });
  }

  function renderSoundButton() {
    const host = document.querySelector('.an-hero, .w3-hero, .t3-hero, .wolf-hero-frame');
    if (!host || host.querySelector('.sc-wolf-sound')) return;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'sc-wolf-sound';
    button.setAttribute('aria-label', 'Звук воя волка');
    const render = () => {
      button.textContent = enabled ? 'Звук: вкл' : 'Звук: выкл';
      button.setAttribute('aria-pressed', String(enabled));
    };
    button.addEventListener('click', () => {
      enabled = !enabled;
      try { localStorage.setItem('sc_wolf_sound', enabled ? 'on' : 'off'); } catch (_) {}
      if (!enabled) {
        try { audio.pause(); audio.currentTime = 0; } catch (_) {}
      }
      render();
    });
    render();
    host.appendChild(button);
  }

  document.addEventListener('click', event => {
    const trigger = event.target.closest('.wolf-preview-action, #anc');
    if (!trigger) return;
    const shell = trigger.matches('#anc')
      ? trigger.querySelector('.wf, .t3-mascot')
      : trigger.closest('.wolf-hero-frame, .t3-mascot');
    play(shell?.querySelector('img'), shell, event.isTrusted);
  }, true);
  document.addEventListener('visibilitychange', () => { if (document.hidden) stop(); });
  reduced.addEventListener?.('change', () => { if (reduced.matches) stop(); });
  window.addEventListener?.('pagehide', stop);

  function update() {
    document.querySelectorAll('.sc-wolf-orbit img, .t3-mascot .t3-wolf img, .wolf-hero-frame > img').forEach(loadPortrait);
    renderSoundButton();
  }
  function boot() {
    new MutationObserver(update).observe(document.body, { childList: true, subtree: true });
    update();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
})();
});
