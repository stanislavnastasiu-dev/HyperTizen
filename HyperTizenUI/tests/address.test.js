'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const address = require('../js/address.js');

test('accepts IPv4 addresses', () => {
  for (const ip of ['192.168.1.20', '10.0.0.1', '0.0.0.0', '255.255.255.255']) {
    assert.equal(address.isValidIp(ip), true, ip);
  }
});

test('rejects anything that is not an IPv4 address', () => {
  for (const ip of ['', '192.168.1', '192.168.1.256', '192.168.1.20.5', '192.168..20', 'a.b.c.d', '1.2.3.4 ', '1234.1.1.1', '.1.2.3']) {
    assert.equal(address.isValidIp(ip), false, JSON.stringify(ip));
  }
});

test('accepts ports 1 to 65535 only', () => {
  assert.equal(address.isValidPort('1'), true);
  assert.equal(address.isValidPort('8090'), true);
  assert.equal(address.isValidPort('65535'), true);
  for (const port of ['', '0', '65536', '80a', '-1', '123456', ' 80']) {
    assert.equal(address.isValidPort(port), false, JSON.stringify(port));
  }
});

test('formats the stored address', () => {
  assert.equal(address.toWsUrl('192.168.1.20', '8090'), 'ws://192.168.1.20:8090');
});

test('converts a discovered device address and shows it without the scheme', () => {
  assert.equal(address.fromDeviceUrl('http://10.0.0.5:8090'), 'ws://10.0.0.5:8090');
  assert.equal(address.fromDeviceUrl('https://10.0.0.5:8092'), 'wss://10.0.0.5:8092');
  assert.equal(address.fromDeviceUrl(''), null);
  assert.equal(address.fromDeviceUrl(undefined), null);
  assert.equal(address.display('ws://10.0.0.5:8090'), '10.0.0.5:8090');
  assert.equal(address.display('wss://10.0.0.5:8092/'), '10.0.0.5:8092');
  assert.equal(address.display(null), '');
});
