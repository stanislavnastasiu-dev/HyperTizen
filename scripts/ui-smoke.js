'use strict';
// Drives the real UI in headless Edge against the desktop host and a stand-in Hyperion server.
// Usage: node scripts/ui-smoke.js   (build HyperTizen.Desktop first; port 8086 must be free)
const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'ht-smoke-'));
const shots = path.join(work, 'shots');
fs.mkdirSync(shots);

const UI_URL = 'http://127.0.0.1:8086/';
const DEBUG_PORT = 9333;
const KEY_CODES = { Enter: 13, Escape: 27, ArrowLeft: 37, ArrowUp: 38, ArrowRight: 39, ArrowDown: 40 };
const children = [];
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let send;

// Minimal WebSocket server: completes the handshake and records every text message.
function startFakeHyperion() {
  const messages = [];
  const server = net.createServer(socket => {
    let buffer = Buffer.alloc(0);
    let upgraded = false;
    socket.on('error', () => {});
    socket.on('data', chunk => {
      buffer = Buffer.concat([buffer, chunk]);
      if (!upgraded) {
        const end = buffer.indexOf('\r\n\r\n');
        if (end < 0) return;
        const key = /sec-websocket-key:\s*(.+)\r\n/i.exec(buffer.toString('latin1', 0, end + 2))[1].trim();
        const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
        socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + accept + '\r\n\r\n');
        buffer = buffer.subarray(end + 4);
        upgraded = true;
      }
      for (;;) {
        if (buffer.length < 2) return;
        const opcode = buffer[0] & 0x0f;
        let length = buffer[1] & 0x7f;
        let offset = 2;
        if (length === 126) {
          if (buffer.length < 4) return;
          length = buffer.readUInt16BE(2);
          offset = 4;
        } else if (length === 127) {
          if (buffer.length < 10) return;
          length = Number(buffer.readBigUInt64BE(2));
          offset = 10;
        }
        if (buffer.length < offset + 4 + length) return;
        const mask = buffer.subarray(offset, offset + 4);
        const payload = Buffer.from(buffer.subarray(offset + 4, offset + 4 + length));
        for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i % 4];
        buffer = buffer.subarray(offset + 4 + length);
        if (opcode === 1) messages.push(payload.toString('utf8'));
        if (opcode === 8) {
          socket.end(Buffer.from([0x88, 0x00]));
          return;
        }
      }
    });
  });
  return new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve({ port: server.address().port, messages })));
}

async function waitForJson(url, timeoutMs = 20000) {
  const start = Date.now();
  for (;;) {
    try {
      const response = await fetch(url);
      if (response.ok) return await response.json().catch(() => ({}));
    } catch (error) {
      // Not up yet.
    }
    if (Date.now() - start > timeoutMs) throw new Error('Nothing answered at ' + url);
    await sleep(200);
  }
}

async function connectDevTools(webSocketUrl) {
  const socket = new WebSocket(webSocketUrl);
  await new Promise((resolve, reject) => {
    socket.onopen = resolve;
    socket.onerror = () => reject(new Error('Could not connect to the browser'));
  });
  let nextId = 0;
  const pending = new Map();
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    const request = pending.get(message.id);
    if (!request) return;
    pending.delete(message.id);
    if (message.error) request.reject(new Error(message.error.message));
    else request.resolve(message.result);
  };
  return (method, params) => new Promise((resolve, reject) => {
    const id = ++nextId;
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params: params || {} }));
  });
}

async function evaluate(expression) {
  const result = await send('Runtime.evaluate', { expression, returnByValue: true });
  if (result.exceptionDetails) throw new Error('Page error in `' + expression + '`: ' + result.exceptionDetails.text);
  return result.result.value;
}

async function press(key) {
  for (const type of ['rawKeyDown', 'keyUp']) {
    await send('Input.dispatchKeyEvent', { type, key, code: key, windowsVirtualKeyCode: KEY_CODES[key], nativeVirtualKeyCode: KEY_CODES[key] });
  }
  await sleep(80);
}

async function waitFor(description, condition, timeoutMs = 10000) {
  const start = Date.now();
  for (;;) {
    if (await condition()) {
      console.log('ok   ' + description);
      return;
    }
    if (Date.now() - start > timeoutMs) throw new Error('Timed out waiting for: ' + description);
    await sleep(100);
  }
}

function expect(description, actual, expected) {
  if (actual !== expected) throw new Error(description + ': expected ' + JSON.stringify(expected) + ', got ' + JSON.stringify(actual));
  console.log('ok   ' + description);
}

const currentScreen = () => evaluate("(document.querySelector('.screen:not([hidden])') || {}).id || null");
const onScreen = name => async () => (await currentScreen()) === 'screen-' + name;
const text = id => evaluate("document.getElementById('" + id + "').textContent.trim()");
const click = id => evaluate("document.getElementById('" + id + "').click()");
const focused = () => evaluate('document.activeElement ? document.activeElement.id : null');
const visible = id => evaluate("document.getElementById('" + id + "').offsetParent !== null");

async function shot(name) {
  const result = await send('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(path.join(shots, name + '.png'), Buffer.from(result.data, 'base64'));
}

async function main() {
  const hyperion = await startFakeHyperion();
  const settingsFile = path.join(work, 'settings.json');
  const hostDll = path.join(root, 'HyperTizen.Desktop', 'bin', 'Debug', 'net10.0', 'HyperTizen.Desktop.dll');
  if (!fs.existsSync(hostDll)) throw new Error('Build the desktop host first: dotnet build HyperTizen.Desktop');

  const startHost = () => {
    const host = spawn('dotnet', [hostDll], { env: Object.assign({}, process.env, { HYPERTIZEN_SETTINGS: settingsFile }), stdio: 'ignore' });
    children.push(host);
    return host;
  };
  let host = startHost();
  await waitForJson(UI_URL);

  const browserPath = [
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
  ].find(candidate => fs.existsSync(candidate));
  if (!browserPath) throw new Error('Neither Edge nor Chrome was found.');
  children.push(spawn(browserPath, [
    '--headless=new', '--remote-debugging-port=' + DEBUG_PORT, '--user-data-dir=' + path.join(work, 'profile'),
    '--window-size=1920,1080', '--no-first-run', '--disable-gpu', 'about:blank'
  ], { stdio: 'ignore' }));

  const targets = await waitForJson('http://127.0.0.1:' + DEBUG_PORT + '/json/list');
  send = await connectDevTools(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
  await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url: UI_URL });

  // First run: no server stored.
  await waitFor('first run opens Setup step 1', onScreen('setup-server'));
  await waitFor('the first scan finishes', async () => (await text('server-hint')) !== 'Searching your network...', 40000);
  expect('manual entry is focused', await focused(), 'server-manual');
  await shot('1-setup-server');

  // A key held down repeats; only the first press may act, or it would also press the next screen's button.
  await send('Input.dispatchKeyEvent', { type: 'rawKeyDown', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13, nativeVirtualKeyCode: 13, autoRepeat: true });
  await sleep(200);
  expect('a repeated Enter does nothing', await currentScreen(), 'screen-setup-server');

  // Manual address with the keypad.
  await press('Enter');
  await waitFor('Enter opens the keypad', onScreen('keypad'));
  expect('Connect is disabled without an address', await evaluate("document.getElementById('keypad-connect').disabled"), true);
  for (const key of ['1', '2', '7', 'dot', '0', 'dot', '0', 'dot', '1']) await click('key-' + key);
  expect('the IP address is shown', await text('value-ip'), '127.0.0.1');
  await click('keypad-next');
  for (let i = 0; i < 4; i++) await click('key-del');
  for (const digit of String(hyperion.port)) await click('key-' + digit);
  expect('the port is shown', await text('value-port'), String(hyperion.port));
  expect('Connect is enabled for a valid address', await evaluate("document.getElementById('keypad-connect').disabled"), false);
  await shot('2-keypad');
  await click('keypad-connect');

  // LED test.
  await waitFor('connecting opens Setup step 2', onScreen('setup-test'));
  await waitFor('the test finishes and asks about the LEDs', async () => (await text('test-title')).indexOf('Did your LEDs') === 0, 15000);
  const colors = hyperion.messages.filter(message => message.indexOf('"command":"color"') >= 0);
  expect('four colors were sent', colors.length, 4);
  expect('the test ended with clear', hyperion.messages[hyperion.messages.length - 1], '{"command":"clear","priority":99}');
  expect('Yes is focused', await focused(), 'test-yes');
  await shot('3-setup-test');
  await press('Enter');

  await waitFor('Yes opens Setup step 3', onScreen('setup-enable'));
  expect('Turn on is focused', await focused(), 'enable-on');
  await press('Enter');

  // Home.
  await waitFor('Turn on opens Home', onScreen('home'));
  await waitFor('capture is running', async () => (await text('tile-capture')) === 'Running');
  await waitFor('the server is connected', async () => (await text('tile-connection')) === 'Connected');
  await waitFor('frames reach the server', async () => hyperion.messages.some(message => message.indexOf('"command":"image"') >= 0));
  expect('the switch shows On', await text('home-toggle-state'), 'On');
  expect('the switch is focused', await focused(), 'home-toggle');
  await shot('4-home');

  // Preview, reached with the arrow keys.
  await press('ArrowDown');
  expect('Down moves to the first button', await focused(), 'home-test');
  await press('ArrowRight');
  expect('Right moves to Preview', await focused(), 'home-preview');
  await press('Enter');
  await waitFor('Enter opens Preview', onScreen('preview'));
  await waitFor('swatches are colored', () => evaluate("!!document.querySelector('.swatch') && document.querySelector('.swatch').style.backgroundColor !== ''"));
  expect('there are 14 swatches', await evaluate("document.querySelectorAll('.swatch').length"), 14);
  await shot('5-preview');
  await press('Escape');
  await waitFor('Back returns to Home', onScreen('home'));
  expect('the switch is focused again', await focused(), 'home-toggle');

  // Settings.
  await press('ArrowDown');
  expect('Down moves to the first button again', await focused(), 'home-test');
  for (const id of ['home-preview', 'home-server', 'home-settings']) {
    await press('ArrowRight');
    expect('Right moves to ' + id, await focused(), id);
  }
  await press('Enter');
  await waitFor('Enter opens Settings', onScreen('settings'));
  await click('fps-custom');
  await waitFor('the limit opens number entry', onScreen('number'));
  expect('OK is disabled without a number', await evaluate("document.getElementById('number-ok').disabled"), true);
  for (const key of ['6', '1']) await click('num-' + key);
  expect('OK is disabled for 61', await evaluate("document.getElementById('number-ok').disabled"), true);
  await click('num-del');
  await click('num-del');
  for (const key of ['1', '0']) await click('num-' + key);
  await click('number-ok');
  await waitFor('OK returns to Settings', onScreen('settings'));
  await waitFor('the limit shows 10', async () => (await text('fps-custom')) === '10');
  await waitFor('the zones are summed up', async () => (await text('zones-summary')) === '4 top, 4 bottom, 3 left, 3 right');

  await click('settings-zones');
  await waitFor('Change opens Capture zones', onScreen('zones'));
  expect('plus on the top row is focused', await focused(), 'zones-top-up');
  await click('zones-top-up');
  expect('top zones shows 5', await text('zones-top-value'), '5');
  await waitFor('the zone count is stored', async () => JSON.parse(fs.readFileSync(settingsFile, 'utf8')).zonesTop === '5');
  await waitFor('the timing is shown', async () => (await text('zones-timing')).indexOf('ms per frame') > 0);
  await shot('6b-zones');
  await press('Escape');
  await waitFor('Back returns to Settings from zones', onScreen('settings'));
  await click('priority-up');
  expect('priority shows 100', await text('priority-value'), '100');
  await waitFor('frames move to priority 100', async () => hyperion.messages.some(
    message => message.indexOf('"command":"image"') >= 0 && message.indexOf('"priority":100') >= 0));
  await shot('6-settings');
  await press('Escape');
  await waitFor('Back returns to Home from Settings', onScreen('home'));
  await waitFor('Home shows the timing', async () => (await text('tile-timing')).indexOf('ms per frame') > 0);
  await click('home-preview');
  await waitFor('Preview opens again', onScreen('preview'));
  await waitFor('there are 15 swatches after adding a zone', () => evaluate("document.querySelectorAll('.swatch').length === 15"));
  await press('Escape');
  await waitFor('Back returns to Home again', onScreen('home'));

  // Losing and regaining the service.
  host.kill();
  await waitFor('a stopped service shows Connecting', onScreen('connecting'));
  await waitFor('help and Retry appear after 15 seconds', () => visible('connecting-retry'), 25000);
  await waitFor('the help lists the address that was tried', async () => (await text('connecting-details')).indexOf('127.0.0.1:8086 ') >= 0);
  await shot('7-connecting');
  host = startHost();
  await waitFor('the UI returns to Home when the service is back', onScreen('home'), 30000);
  const stored = JSON.parse(fs.readFileSync(settingsFile, 'utf8'));
  expect('settings were stored', stored.maxFps + '/' + stored.priority + '/' + stored.enabled, '10/100/true');

  // Forget server.
  await press('ArrowDown');
  await press('ArrowRight');
  await press('ArrowRight');
  await press('ArrowRight');
  await press('Enter');
  await waitFor('Settings opens again', onScreen('settings'));
  await click('forget-server');
  expect('Cancel is focused in the confirmation', await focused(), 'forget-no');
  await click('forget-yes');
  await waitFor('forgetting opens Setup step 1', onScreen('setup-server'));
  await waitFor('the server is removed from the settings file', async () => !('rpcServer' in JSON.parse(fs.readFileSync(settingsFile, 'utf8'))));
  expect('capture was turned off', JSON.parse(fs.readFileSync(settingsFile, 'utf8')).enabled, 'false');

  // Pointing the page at a named service.
  await evaluate("document.getElementById('server-manual').click()");
  await waitFor('the keypad opens again', onScreen('keypad'));
  for (const key of ['1', '2', '7', 'dot', '0', 'dot', '0', 'dot', '1']) await click('key-' + key);
  await click('keypad-next');
  for (let i = 0; i < 4; i++) await click('key-del');
  for (const digit of String(hyperion.port)) await click('key-' + digit);
  await click('keypad-connect');
  await waitFor('the server is stored again', async () => 'rpcServer' in JSON.parse(fs.readFileSync(settingsFile, 'utf8')));
  await send('Page.navigate', { url: UI_URL + '?service=127.0.0.1' });
  await waitFor('a page with ?service= reaches Home', onScreen('home'), 30000);
  await waitFor('Home names the service it is watching', async () => (await text('tile-version')) === 'Version 1.1.0 on 127.0.0.1');
  await send('Page.navigate', { url: UI_URL + '?service=127.0.0.2' });
  await waitFor('an unreachable ?service= is not replaced by the local one', async () =>
    (await currentScreen()) === 'screen-connecting' && (await visible('connecting-retry'))
    && (await text('connecting-details')).indexOf('127.0.0.2:8086 ') >= 0
    && (await text('connecting-details')).indexOf('127.0.0.1:8086') < 0, 30000);

  console.log('\nSMOKE TEST PASSED. Screenshots: ' + shots);
}

main()
  .then(() => 0, error => {
    console.error('\nSMOKE TEST FAILED: ' + error.message);
    console.error('Screenshots so far: ' + shots);
    return 1;
  })
  .then(code => {
    children.forEach(child => {
      try {
        child.kill();
      } catch (error) {
        // Already gone.
      }
    });
    process.exit(code);
  });
