'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const protocol = require('../js/protocol.js');

function fakeSocket() {
  const socket = {
    sent: [],
    send(text) { socket.sent.push(JSON.parse(text)); },
    close() { if (socket.onclose) socket.onclose(); },
    receive(message) { socket.onmessage({ data: typeof message === 'string' ? message : JSON.stringify(message) }); }
  };
  return socket;
}

test('a status request sends event 5 and resolves with the event 6 reply', async () => {
  const socket = fakeSocket();
  const client = protocol.createClient(socket);

  const pending = client.getStatus();
  assert.deepEqual(socket.sent, [{ event: 5 }]);
  socket.receive({ Event: 6, version: '1.1.0', capture: 'unknown' });

  assert.equal((await pending).version, '1.1.0');
});

test('config replies are matched by key, whatever order they arrive in', async () => {
  const socket = fakeSocket();
  const client = protocol.createClient(socket);

  const fps = client.readConfig('maxFps');
  const priority = client.readConfig('priority');
  socket.receive({ Event: 2, error: false, key: 'priority', value: '50' });
  socket.receive({ Event: 2, error: true, key: 'maxFps', value: "Key doesn't exist." });

  assert.equal((await priority).value, '50');
  assert.equal((await fps).error, true);
});

test('writes send the expected messages and wait for no reply', () => {
  const socket = fakeSocket();
  const client = protocol.createClient(socket);

  client.setConfig('enabled', 'true');
  client.deleteConfig('rpcServer');

  assert.deepEqual(socket.sent, [
    { event: 0, key: 'enabled', value: 'true' },
    { event: 11, key: 'rpcServer' }
  ]);
});

test('a request without a reply times out', async () => {
  const socket = fakeSocket();
  const client = protocol.createClient(socket, { timeouts: { default: 20, scan: 20, test: 20 } });

  await assert.rejects(client.getStatus(), /timeout/);
  await assert.rejects(client.scan(), /timeout/);
  await assert.rejects(client.testLeds(), /timeout/);
});

test('closing rejects what is pending, reports once, and refuses new requests', async () => {
  const socket = fakeSocket();
  const client = protocol.createClient(socket);
  let closes = 0;
  client.onClose = () => { closes++; };

  const pending = client.getPreview();
  socket.close();
  socket.close();

  await assert.rejects(pending, /closed/);
  await assert.rejects(client.getStatus(), /closed/);
  assert.throws(() => client.setConfig('enabled', 'true'), /closed/);
  assert.equal(closes, 1);
});

test('two unanswered status requests in a row end the connection', async () => {
  const socket = fakeSocket();
  let socketClosed = 0;
  socket.close = () => { socketClosed++; };
  const client = protocol.createClient(socket, { timeouts: { default: 20 } });
  let closes = 0;
  client.onClose = () => { closes++; };

  await assert.rejects(client.getStatus(), /timeout/);
  assert.equal(closes, 0);
  await assert.rejects(client.getStatus(), /timeout/);

  assert.equal(closes, 1);
  assert.equal(socketClosed, 1);
  await assert.rejects(client.getStatus(), /closed/);
});

test('an answered status request resets the count of unanswered ones', async () => {
  const socket = fakeSocket();
  const client = protocol.createClient(socket, { timeouts: { default: 20 } });
  let closes = 0;
  client.onClose = () => { closes++; };

  await assert.rejects(client.getStatus(), /timeout/);
  const answered = client.getStatus();
  socket.receive({ Event: 6, version: 'x' });
  await answered;
  await assert.rejects(client.getStatus(), /timeout/);

  assert.equal(closes, 0);
});

test('messages that are not replies are ignored', async () => {
  const socket = fakeSocket();
  const client = protocol.createClient(socket);

  const pending = client.getStatus();
  socket.receive('not json');
  socket.receive('null');
  socket.receive({ Event: 4, devices: [] });
  socket.receive({ Event: 6, version: 'x' });

  assert.equal((await pending).version, 'x');
});
