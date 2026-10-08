// Finds the HyperTizen service: works out where it may be listening and opens the first that answers.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};

  const PORT = 8086;
  const TIZENBREW_URL = 'http://127.0.0.1:8081';

  function candidates(location, tizenBrewIp) {
    const urls = [];
    const add = host => {
      const url = 'ws://' + host + ':' + PORT;
      if (urls.indexOf(url) < 0) urls.push(url);
    };

    // Served over http: the desktop host, which also runs the service.
    if (location && /^https?:$/.test(location.protocol) && location.hostname) add(location.hostname);
    add('127.0.0.1');
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

  // Tries the addresses one after another.
  function open(urls, WebSocketConstructor, timeoutMs) {
    return urls.reduce(
      (attempt, url) => attempt.catch(() => tryOpen(url, WebSocketConstructor, timeoutMs)),
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

  HT.connection = { candidates, open, fetchTizenBrewIp };
  if (typeof module !== 'undefined' && module.exports) module.exports = HT.connection;
})(typeof window !== 'undefined' ? window : globalThis);
