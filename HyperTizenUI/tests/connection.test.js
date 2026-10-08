'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
require('../js/address.js');
const connection = require('../js/connection.js');

// A WebSocket stand-in whose outcome depends on the URL.
function socketsThat(behavior) {
  const created = [];
  function FakeWebSocket(url) {
    this.url = url;
    this.closed = false;
    created.push(this);
    const outcome = behavior(url);
    setTimeout(() => {
      if (outcome === 'open' && this.onopen) this.onopen();
      if (outcome === 'fail' && this.onerror) this.onerror();
    }, 5);
  }
  FakeWebSocket.prototype.close = function () { this.closed = true; };
  return { FakeWebSocket, created };
}

test('a page served over http tries its own host first, without duplicates', () => {
  assert.deepEqual(
    connection.candidates({ protocol: 'http:', hostname: '127.0.0.1' }, null),
    ['ws://127.0.0.1:8086']);
  assert.deepEqual(
    connection.candidates({ protocol: 'http:', hostname: '192.168.1.9' }, '192.168.1.50'),
    ['ws://192.168.1.9:8086', 'ws://127.0.0.1:8086', 'ws://192.168.1.50:8086']);
});

test('a page opened from a file tries the TV itself, then the address TizenBrew reports', () => {
  assert.deepEqual(
    connection.candidates({ protocol: 'file:', hostname: '' }, '192.168.1.50'),
    ['ws://127.0.0.1:8086', 'ws://192.168.1.50:8086']);
  assert.deepEqual(connection.candidates(undefined, null), ['ws://127.0.0.1:8086']);
});

test("the TV's own network address is tried after loopback", () => {
  assert.deepEqual(
    connection.candidates({ protocol: 'file:', hostname: '' }, null, '192.168.1.145'),
    ['ws://127.0.0.1:8086', 'ws://192.168.1.145:8086']);
  assert.deepEqual(
    connection.candidates({ protocol: 'file:', hostname: '' }, '192.168.1.145', '192.168.1.145'),
    ['ws://127.0.0.1:8086', 'ws://192.168.1.145:8086']);
});

test('a service address in the page address is the only one tried', () => {
  assert.deepEqual(
    connection.candidates({ protocol: 'http:', hostname: '127.0.0.1' }, '10.0.0.9', '10.0.0.8', '192.168.1.145'),
    ['ws://192.168.1.145:8086']);
});

test('reads the service address from the page address', () => {
  assert.equal(connection.serviceOverride({ search: '?service=192.168.1.145' }), '192.168.1.145');
  assert.equal(connection.serviceOverride({ search: '?x=1&service=10.0.0.5&y=2' }), '10.0.0.5');
  assert.equal(connection.serviceOverride({ search: '?service=192.168.001.145' }), '192.168.1.145');
});

test('ignores a missing or malformed service address', () => {
  for (const search of ['', '?', '?service=', '?service=tv.local', '?service=1.2.3', '?service=1.2.3.4:9000', '?other=1.2.3.4', '?myservice=1.2.3.4']) {
    assert.equal(connection.serviceOverride({ search }), null, search);
  }
  assert.equal(connection.serviceOverride(undefined), null);
  assert.equal(connection.serviceOverride({}), null);
});

test("reads the TV's own address from the Samsung network API when it exists", () => {
  assert.equal(connection.ownIp({ webapis: { network: { getIp: () => '192.168.1.145' } } }), '192.168.1.145');
  assert.equal(connection.ownIp({ webapis: { network: { getIp: () => '0.0.0.0.0' } } }), null);
  assert.equal(connection.ownIp({ webapis: { network: { getIp: () => { throw new Error('denied'); } } } }), null);
  assert.equal(connection.ownIp({}), null);
});

test('the service shown is on the device named in the page address, else on this TV', () => {
  const tv = { webapis: { network: { getIp: () => '192.168.1.145' } } };
  assert.equal(connection.serviceHost(tv, null), '192.168.1.145');
  assert.equal(connection.serviceHost(tv, '10.0.0.7'), '10.0.0.7');
  assert.equal(connection.serviceHost({}, null), null);
});

test('reports what happened to each address it tried', async () => {
  const { FakeWebSocket } = socketsThat(url => ({ 'ws://a': 'fail', 'ws://b': 'hang', 'ws://c': 'open' })[url]);
  const attempts = [];

  await connection.open(['ws://a', 'ws://b', 'ws://c'], FakeWebSocket, 30, (url, result) => attempts.push(url + ' ' + result));

  assert.deepEqual(attempts, ['ws://a failed', 'ws://b timeout', 'ws://c open']);
});

test('uses the first address that opens and closes the ones that failed', async () => {
  const { FakeWebSocket, created } = socketsThat(url => (url === 'ws://b' ? 'open' : 'fail'));

  const socket = await connection.open(['ws://a', 'ws://b', 'ws://c'], FakeWebSocket, 100);

  assert.equal(socket.url, 'ws://b');
  assert.deepEqual(created.map(s => s.url), ['ws://a', 'ws://b']);
  assert.equal(created[0].closed, true);
  assert.equal(socket.closed, false);
});

test('fails when no address opens', async () => {
  const { FakeWebSocket } = socketsThat(() => 'fail');

  await assert.rejects(connection.open(['ws://a', 'ws://b'], FakeWebSocket, 100));
  await assert.rejects(connection.open([], FakeWebSocket, 100));
});

test('gives up on an address that neither opens nor fails', async () => {
  const { FakeWebSocket, created } = socketsThat(url => (url === 'ws://slow' ? 'hang' : 'open'));

  const socket = await connection.open(['ws://slow', 'ws://ok'], FakeWebSocket, 30);

  assert.equal(socket.url, 'ws://ok');
  assert.equal(created[0].closed, true);
});
