// Shown while the service is unreachable.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  HT.screens['connecting'] = function (ctx) {
    const title = ctx.el('connecting-title');
    const help = ctx.el('connecting-help');
    const message = ctx.el('connecting-message');
    const tips = ctx.el('connecting-tips');
    const retry = ctx.el('connecting-retry');
    let timer = null;

    function showHelp(text, withTips) {
      message.textContent = text;
      tips.hidden = !withTips;
      help.hidden = false;
      retry.focus();
    }

    // Explain what to check only once waiting has clearly taken too long.
    function arm() {
      clearTimeout(timer);
      help.hidden = true;
      timer = setTimeout(() => showHelp('The HyperTizen service is not responding.', true), HT.timing.connectHelpMs);
    }

    retry.onclick = () => {
      title.textContent = 'Starting HyperTizen...';
      arm();
      ctx.reconnect();
    };

    return {
      enter(params) {
        title.textContent = params.lost
          ? 'Connection to the HyperTizen service was lost. Reconnecting...'
          : 'Starting HyperTizen...';
        arm();
      },

      leave() {
        clearTimeout(timer);
      },

      // The service answered the connection but not the status request: it predates this UI.
      updateNeeded() {
        clearTimeout(timer);
        showHelp('Please update the HyperTizen service on your TV.', false);
      }
    };
  };
})(typeof window !== 'undefined' ? window : globalThis);
