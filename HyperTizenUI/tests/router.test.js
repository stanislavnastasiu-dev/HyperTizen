'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRouter } = require('../js/router.js');

function setup() {
  const log = [];
  const screens = {
    home: {
      enter: params => log.push(['home.enter', params]),
      leave: () => log.push(['home.leave']),
      onStatus: status => log.push(['home.onStatus', status])
    },
    settings: {
      enter: params => log.push(['settings.enter', params]),
      back: () => { log.push(['settings.back']); return true; }
    },
    plain: {}
  };
  const router = createRouter(screens, (name, visible) => log.push(['visible', name, visible]));
  return { log, router };
}

test('showing a screen makes it visible and enters it with its parameters', () => {
  const { log, router } = setup();

  router.go('home', { fromSetup: true });

  assert.equal(router.current(), 'home');
  assert.deepEqual(log, [['visible', 'home', true], ['home.enter', { fromSetup: true }]]);
});

test('switching leaves and hides the previous screen first', () => {
  const { log, router } = setup();
  router.go('home');
  log.length = 0;

  router.go('settings');

  assert.deepEqual(log, [['home.leave'], ['visible', 'home', false], ['visible', 'settings', true], ['settings.enter', {}]]);
});

test('back is handled only by screens that define it', () => {
  const { router } = setup();

  assert.equal(router.back(), false);
  router.go('home');
  assert.equal(router.back(), false);
  router.go('settings');
  assert.equal(router.back(), true);
});

test('notifications reach the current screen only when it has that method', () => {
  const { log, router } = setup();
  router.go('home');
  log.length = 0;

  router.notify('onStatus', { enabled: true });
  router.go('plain');
  router.notify('onStatus', { enabled: false });

  assert.deepEqual(log.filter(entry => entry[0] === 'home.onStatus'), [['home.onStatus', { enabled: true }]]);
});

test('an unknown screen is an error', () => {
  const { router } = setup();

  assert.throws(() => router.go('nope'), /Unknown screen: nope/);
});
