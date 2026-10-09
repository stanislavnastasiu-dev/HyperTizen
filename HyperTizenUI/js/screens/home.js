// Status at a glance, the on/off switch, and the way to everything else.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  const CAPTURE_TEXT = {
    running: 'Running',
    stopped: 'Stopped',
    unsupported: 'Not supported on this TV',
    unknown: 'Not checked yet'
  };
  const CAPTURE_STYLE = { running: 'good', unsupported: 'bad' };

  HT.screens['home'] = function (ctx) {
    const toggle = ctx.el('home-toggle');
    const toggleState = ctx.el('home-toggle-state');
    const message = ctx.el('home-message');
    const error = ctx.el('home-error');
    const details = ctx.el('home-details');

    function showSwitch(on) {
      toggleState.textContent = on ? 'On' : 'Off';
      toggle.classList.toggle('on', on);
    }

    function render(status) {
      if (!status) return;
      // Say where the service is: this TV's address, or the other device being watched.
      const host = ctx.serviceHost();
      ctx.el('tile-version').textContent = 'Version ' + status.version + (host ? ' on ' + host : '');
      ctx.el('tile-server').textContent = status.rpcServer ? HT.address.display(status.rpcServer) : 'None';

      const connection = ctx.el('tile-connection');
      connection.textContent = status.connected ? 'Connected' : 'Not connected';
      connection.className = 'tile-value ' + (status.connected ? 'good' : 'idle');

      const capture = ctx.el('tile-capture');
      capture.textContent = CAPTURE_TEXT[status.capture] || status.capture;
      capture.className = 'tile-value ' + (CAPTURE_STYLE[status.capture] || 'idle');
      // Keeps its line even when empty, so the tiles stay the same height.
      ctx.el('tile-timing').textContent = HT.capture.timingText(status) || '\u00a0';

      showSwitch(!!status.enabled);
      error.hidden = !status.lastError;
      error.textContent = status.lastError || '';
      // The capturer's own report (which measure API, point count, and per-frame timing).
      details.textContent = status.captureDetails || '';
    }

    toggle.onclick = () => {
      const status = ctx.status();
      const on = !(status && status.enabled);
      if (!ctx.set('enabled', String(on))) return;
      showSwitch(on);
      message.textContent = '';
      setTimeout(ctx.refresh, 400);
    };

    ctx.el('home-test').onclick = () => {
      const client = ctx.client();
      if (!client) return;
      message.textContent = 'Sending test colors...';
      client.testLeds().then(
        result => { message.textContent = result.ok ? 'Test colors sent.' : 'Test failed: ' + (result.error || ''); },
        () => { message.textContent = 'The service did not answer.'; });
    };

    ctx.el('home-preview').onclick = () => ctx.router.go('preview');
    ctx.el('home-server').onclick = () => ctx.router.go('setup-server', { fromHome: true });
    ctx.el('home-settings').onclick = () => ctx.router.go('settings');

    return {
      enter() {
        message.textContent = '';
        render(ctx.status());
        toggle.focus();
        ctx.refresh();
      },

      onStatus: render
    };
  };
})(typeof window !== 'undefined' ? window : globalThis);
