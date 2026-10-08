// Finds the HyperTizen service: works out where it may be listening and opens the first that answers.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};

  const PORT = 8086;
  const TIZENBREW_URL = 'http://127.0.0.1:8081';

  // The TV's own network address, from the Samsung network API when the page runs on a TV.
  function ownIp(scope) {
    try {
      const ip = scope.webapis.network.getIp();
      return HT.address.isValidIp(ip) ? ip : null;
    } catch (error) {
      return null;
    }
  }

  function candidates(location, tizenBrewIp, ownAddress) {
    const urls = [];
    const add = host => {
      const url = 'ws://' + host + ':' + PORT;
      if (urls.indexOf(url) < 0) urls.push(url);
    };

    // Served over http: the desktop host, which also runs the service.
    if (location && /^https?:$/.test(location.protocol) && location.hostname) add(location.hostname);
    add('127.0.0.1');
    // A TV app may not be allowed to reach the service over loopback, only over the TV's address.
    if (ownAddress) add(ownAddress);
    if (tizenBrewIp) add(tizenBrewIp);
    return urls;
  }

  function tryOpen(url, WebSocketConstructor, timeoutMs) {
    return new Promise((resolve, reject) => {
      let socket;
      try {
        socket = new WebSocketConstructor(url);
      } catch (error) {
        reject(error);
        return;
      }

      let settled = false;
      const timer = setTimeout(() => settle(new Error('timeout')), timeoutMs);

      function settle(error) {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        socket.onopen = socket.onerror = socket.onclose = null;
        if (!error) {
          resolve(socket);
          return;
        }
        try {
          socket.close();
        } catch (closeError) {
          // Already closed.
        }
        reject(error);
      }

      socket.onopen = () => settle(null);
      socket.onerror = () => settle(new Error('failed'));
      socket.onclose = () => settle(new Error('closed'));
    });
  }

  // Tries the addresses one after another. onAttempt(url, result) hears how each one went:
  // 'open', 'failed', 'closed', 'timeout', or the error message.
  function open(urls, WebSocketConstructor, timeoutMs, onAttempt) {
    const report = onAttempt || (() => {});
    return urls.reduce(
      (attempt, url) => attempt.catch(() => tryOpen(url, WebSocketConstructor, timeoutMs).then(
        socket => {
          report(url, 'open');
          return socket;
        },
        error => {
          report(url, error.message);
          throw error;
        })),
      Promise.reject(new Error('no addresses')));
  }

  // TizenBrew reports the TV's own address; resolves with null anywhere else.
  function fetchTizenBrewIp() {
    if (typeof fetch !== 'function') return Promise.resolve(null);

    const controller = typeof AbortController === 'function' ? new AbortController() : null;
    const timer = setTimeout(() => { if (controller) controller.abort(); }, 2000);
    const done = value => {
      clearTimeout(timer);
      return value;
    };

    return fetch(TIZENBREW_URL, controller ? { signal: controller.signal } : undefined)
      .then(response => response.text())
      .then(text => done(HT.address.isValidIp(text.trim()) ? text.trim() : null))
      .catch(() => done(null));
  }

  HT.connection = { candidates, open, ownIp, fetchTizenBrewIp };
  if (typeof module !== 'undefined' && module.exports) module.exports = HT.connection;
})(typeof window !== 'undefined' ? window : globalThis);
