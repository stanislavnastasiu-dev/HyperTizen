// Validation and formatting of Hyperion / HyperHDR server addresses.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};

  function isValidIp(text) {
    const parts = String(text).split('.');
    return parts.length === 4 && parts.every(part => /^\d{1,3}$/.test(part) && Number(part) <= 255);
  }

  function isValidPort(text) {
    return /^\d{1,5}$/.test(String(text)) && Number(text) >= 1 && Number(text) <= 65535;
  }

  // Leading zeros are dropped: "010" would otherwise be read as octal by the service.
  function toWsUrl(ip, port) {
    return 'ws://' + String(ip).split('.').map(Number).join('.') + ':' + Number(port);
  }

  // Discovery reports http(s) addresses; the service connects over ws(s).
  function fromDeviceUrl(urlBase) {
    if (!urlBase) return null;
    const text = String(urlBase);
    return text.indexOf('https') === 0 ? text.replace('https', 'wss') : text.replace('http', 'ws');
  }

  function display(url) {
    return String(url || '').replace(/^wss?:\/\//, '').replace(/\/$/, '');
  }

  HT.address = { isValidIp, isValidPort, toWsUrl, fromDeviceUrl, display };
  if (typeof module !== 'undefined' && module.exports) module.exports = HT.address;
})(typeof window !== 'undefined' ? window : globalThis);
