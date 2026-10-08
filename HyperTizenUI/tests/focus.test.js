'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const focus = require('../js/focus.js');

const item = (name, left, top, width = 100, height = 50) => ({ name, rect: { left, top, width, height } });

test('moves to the nearest element in the pressed direction', () => {
  const a = item('a', 0, 0);
  const b = item('b', 150, 0);
  const c = item('c', 300, 0);
  const all = [a, b, c];

  assert.equal(focus.pickNext(a, all, 'right'), b);
  assert.equal(focus.pickNext(c, all, 'left'), b);
});

test('moves down to a wider element and back up from it', () => {
  const left = item('left', 0, 0);
  const right = item('right', 600, 0);
  const wide = item('wide', 0, 100, 700, 50);
  const all = [left, right, wide];

  assert.equal(focus.pickNext(right, all, 'down'), wide);
  assert.equal(focus.pickNext(wide, all, 'up'), left);
});

test('stays put when nothing lies in that direction', () => {
  const a = item('a', 0, 0);
  const b = item('b', 150, 0);

  assert.equal(focus.pickNext(a, [a, b], 'left'), null);
  assert.equal(focus.pickNext(a, [a, b], 'up'), null);
  assert.equal(focus.pickNext(a, [a], 'right'), null);
});

test('prefers an aligned element over a nearer diagonal one', () => {
  const current = item('current', 0, 0);
  const aligned = item('aligned', 300, 0);
  const diagonal = item('diagonal', 200, 200);

  assert.equal(focus.pickNext(current, [current, diagonal, aligned], 'right'), aligned);
});

test('walks a keypad grid', () => {
  const keys = [];
  for (let row = 0; row < 4; row++) {
    for (let column = 0; column < 3; column++) keys.push(item('k' + row + column, column * 120, row * 100));
  }
  const at = (row, column) => keys[row * 3 + column];

  assert.equal(focus.pickNext(at(1, 1), keys, 'up'), at(0, 1));
  assert.equal(focus.pickNext(at(1, 1), keys, 'down'), at(2, 1));
  assert.equal(focus.pickNext(at(1, 1), keys, 'left'), at(1, 0));
  assert.equal(focus.pickNext(at(1, 1), keys, 'right'), at(1, 2));
  assert.equal(focus.pickNext(at(3, 2), keys, 'down'), null);
});
