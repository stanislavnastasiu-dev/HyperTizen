// Messages exchanged with the HyperTizen service over one WebSocket.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};

  const Events = {
    SetConfig: 0,
    ReadConfig: 1,
    ReadConfigResult: 2,
    ScanSSDP: 3,
    SSDPScanResult: 4,
    GetStatus: 5,
    StatusResult: 6,
    TestLeds: 7,
    TestLedsResult: 8,
    GetPreview: 9,
    PreviewResult: 10,
    DeleteConfig: 11
  };

  const MAX_UNANSWERED_STATUS = 2;

  HT.timing = { connectHelpMs: 15000, statusMs: 2000, previewMs: 500, scanMs: 10000, retryMs: 2000 };

  function createClient(socket, options) {
    const timeouts = Object.assign({ default: 5000, scan: 30000, test: 15000 }, options && options.timeouts);
    let pending = [];
    let closed = false;
    const client = { onClose: null };

    socket.onmessage = function (event) {
      let message;
      try {
        message = JSON.parse(event.data);
      } catch (error) {
        return;
      }
      if (!message || typeof message !== 'object') return;

      for (let i = 0; i < pending.length; i++) {
        const request = pending[i];
        if (request.replyEvent === message.Event && (!request.matches || request.matches(message))) {
          pending.splice(i, 1);
          clearTimeout(request.timer);
          request.resolve(message);
          return;
        }
      }
    };

    function markClosed() {
      if (closed) return false;
      closed = true;
      const abandoned = pending;
      pending = [];
      abandoned.forEach(request => {
        clearTimeout(request.timer);
        request.reject(new Error('closed'));
      });
      return true;
    }

    socket.onclose = function () {
      if (markClosed() && client.onClose) client.onClose();
    };

    // A service that stops answering without closing the socket is treated as gone.
    function giveUp() {
      if (!markClosed()) return;
      try {
        socket.close();
      } catch (error) {
        // Already closed.
      }
      if (client.onClose) client.onClose();
    }

    // A close event always follows an error.
    socket.onerror = function () {};

    function send(message) {
      if (closed) throw new Error('closed');
      socket.send(JSON.stringify(message));
    }

    function request(message, replyEvent, timeoutMs, matches) {
      return new Promise((resolve, reject) => {
        if (closed) {
          reject(new Error('closed'));
          return;
        }

        const entry = { replyEvent, matches, resolve, reject, timer: null };
        entry.timer = setTimeout(() => {
          const index = pending.indexOf(entry);
          if (index >= 0) pending.splice(index, 1);
          reject(new Error('timeout'));
        }, timeoutMs);
        pending.push(entry);

        try {
          socket.send(JSON.stringify(message));
        } catch (error) {
          clearTimeout(entry.timer);
          pending.splice(pending.indexOf(entry), 1);
          reject(error);
        }
      });
    }

    client.readConfig = key => request(
      { event: Events.ReadConfig, key }, Events.ReadConfigResult, timeouts.default, reply => reply.key === key);
    client.setConfig = (key, value) => send({ event: Events.SetConfig, key, value });
    client.deleteConfig = key => send({ event: Events.DeleteConfig, key });
    client.scan = () => request({ event: Events.ScanSSDP }, Events.SSDPScanResult, timeouts.scan);
    let unansweredStatus = 0;
    client.getStatus = () => request({ event: Events.GetStatus }, Events.StatusResult, timeouts.default).then(
      status => {
        unansweredStatus = 0;
        return status;
      },
      error => {
        if (error.message === 'timeout' && ++unansweredStatus >= MAX_UNANSWERED_STATUS) giveUp();
        throw error;
      });
    client.testLeds = () => request({ event: Events.TestLeds }, Events.TestLedsResult, timeouts.test);
    client.getPreview = () => request({ event: Events.GetPreview }, Events.PreviewResult, timeouts.default);
    client.close = () => socket.close();

    return client;
  }

  HT.protocol = { Events, createClient };
  if (typeof module !== 'undefined' && module.exports) module.exports = HT.protocol;
})(typeof window !== 'undefined' ? window : globalThis);
