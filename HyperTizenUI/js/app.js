// Wires the screens to the service: connects, polls status, and routes remote keys.
(function (root) {
  'use strict';
  const HT = root.HyperTizen;
  HT.uiVersion = '1.1.4';

  const SERVICE_APP_ID = 'io.gh.reisxd.HyperTizen';
  const ARROWS = { 37: 'left', 38: 'up', 39: 'right', 40: 'down' };
  const KEY_ENTER = 13;
  const KEY_BACKSPACE = 8;
  const KEY_ESCAPE = 27;
  const KEY_TV_BACK = 10009;

  const state = { client: null, status: null, pollTimer: null, polling: false, attempt: 0 };
  // Set when the page address names a service on another device ("?service=<address>").
  const remoteService = HT.connection.serviceOverride(root.location);
  // What the last connection attempt did, shown on the Connecting screen when the service cannot be reached.
  const diagnostics = { launch: 'not running on a TV', attempts: [] };
  const screens = {};

  const screenElement = name => document.getElementById('screen-' + name);

  const router = HT.router.createRouter(screens, (name, visible) => {
    // Focus must not stay on a button of a screen that is no longer shown.
    if (!visible && document.activeElement) document.activeElement.blur();
    screenElement(name).hidden = !visible;
  });

  function withClient(action) {
    if (!state.client) return false;
    try {
      action(state.client);
      return true;
    } catch (error) {
      return false;
    }
  }

  const ctx = {
    router,
    el: id => document.getElementById(id),
    client: () => state.client,
    status: () => state.status,
    refresh: pollStatus,
    reconnect: connect,
    serviceHost: () => HT.connection.serviceHost(root, remoteService),
    diagnostics: () => 'Service start: ' + diagnostics.launch + '. Tried: '
      + (diagnostics.attempts.length ? diagnostics.attempts.join(', ') : 'nothing yet') + '.',
    set: (key, value) => withClient(client => client.setConfig(key, value)),
    del: key => withClient(client => client.deleteConfig(key))
  };

  function pollStatus() {
    const client = state.client;
    if (!client || state.polling) return;
    state.polling = true;
    client.getStatus()
      .then(status => {
        state.status = status;
        router.notify('onStatus', status);
      }, () => {})
      .then(() => { state.polling = false; });
  }

  function stopPolling() {
    clearInterval(state.pollTimer);
    state.pollTimer = null;
    state.polling = false;
  }

  // As a standalone TV app nothing else starts the service; under TizenBrew this is harmless.
  function launchService() {
    if (!(root.tizen && root.tizen.application)) return;
    try {
      diagnostics.launch = 'requested';
      root.tizen.application.launch(
        SERVICE_APP_ID,
        () => { diagnostics.launch = 'ok'; },
        error => { diagnostics.launch = 'refused (' + ((error && (error.message || error.name)) || 'unknown') + ')'; });
    } catch (error) {
      diagnostics.launch = 'failed (' + error.message + ')';
    }
  }

  function retryLater() {
    const attempt = state.attempt;
    setTimeout(() => { if (attempt === state.attempt) connect(); }, HT.timing.retryMs);
  }

  function connect() {
    const attempt = ++state.attempt;
    stopPolling();
    if (state.client) {
      const previous = state.client;
      state.client = null;
      previous.onClose = null;
      previous.close();
    }
    launchService();

    const attempts = [];
    HT.connection.fetchTizenBrewIp()
      .then(ip => HT.connection.open(
        HT.connection.candidates(root.location, ip, HT.connection.ownIp(root), remoteService),
        root.WebSocket,
        3000,
        (url, result) => {
          attempts.push(HT.address.display(url) + ' ' + result);
          if (attempt !== state.attempt) return;
          diagnostics.attempts = attempts.slice();
          router.notify('onDiagnostics');
        }))
      .then(socket => {
        if (attempt !== state.attempt) {
          socket.close();
          return;
        }

        const client = HT.protocol.createClient(socket);
        state.client = client;
        client.onClose = () => {
          if (state.client !== client) return;
          state.client = null;
          stopPolling();
          router.go('connecting', { lost: true });
          retryLater();
        };

        client.getStatus().then(status => {
          if (state.client !== client) return;
          state.status = status;
          state.pollTimer = setInterval(pollStatus, HT.timing.statusMs);
          router.go(status.rpcServer ? 'home' : 'setup-server');
        }, error => {
          if (state.client === client && error.message === 'timeout') router.notify('updateNeeded');
        });
      }, () => {
        if (attempt === state.attempt) retryLater();
      });
  }

  function onKeyDown(event) {
    const current = router.current();
    if (!current) return;
    const container = screenElement(current);

    if (ARROWS[event.keyCode]) {
      event.preventDefault();
      HT.focus.move(container, ARROWS[event.keyCode]);
      return;
    }

    if (event.keyCode === KEY_ENTER) {
      event.preventDefault();
      // A held key repeats; acting on repeats would also press the next screen's focused button.
      if (event.repeat) return;
      const focused = document.activeElement;
      if (focused && container.contains(focused) && !focused.disabled && focused.offsetParent !== null) focused.click();
      return;
    }

    if (/^[0-9.]$/.test(event.key || '')) {
      router.notify('key', event.key);
      return;
    }

    if (event.keyCode === KEY_BACKSPACE && screens[current].key) {
      event.preventDefault();
      screens[current].key('del');
      return;
    }

    if (event.keyCode === KEY_TV_BACK || event.keyCode === KEY_ESCAPE || event.keyCode === KEY_BACKSPACE) {
      if (router.back()) event.preventDefault();
    }
  }

  Object.keys(HT.screens).forEach(name => { screens[name] = HT.screens[name](ctx); });
  document.addEventListener('keydown', onKeyDown);
  router.go('connecting');
  connect();
})(typeof window !== 'undefined' ? window : globalThis);
