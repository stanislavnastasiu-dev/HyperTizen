'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const capture = require('../js/capture.js');

const [top, bottom, left, right] = capture.ZONES;

test('the four zone settings, with the limits and defaults of the service', () => {
  assert.deepEqual(capture.ZONES.map(zone => [zone.key, zone.id, zone.max, zone.fallback]), [
    ['zonesTop', 'top', 16, 4],
    ['zonesBottom', 'bottom', 16, 4],
    ['zonesLeft', 'left', 12, 3],
    ['zonesRight', 'right', 12, 3]
  ]);
});

test('a stored zone count is read from the reply', () => {
  assert.equal(capture.zoneValue({ error: false, key: 'zonesTop', value: '8' }, top), 8);
  assert.equal(capture.zoneValue({ error: false, key: 'zonesLeft', value: '0' }, left), 0);
});

test('a zone count the service does not have shows the default', () => {
  // An older service answers every zone key with an error.
  assert.equal(capture.zoneValue({ error: true, key: 'zonesTop', value: "Key doesn't exist." }, top), 4);
  assert.equal(capture.zoneValue({ error: false, key: 'zonesRight', value: 'many' }, right), 3);
  assert.equal(capture.zoneValue({ error: false, key: 'zonesRight', value: '99' }, right), 3);
  assert.equal(capture.zoneValue(undefined, bottom), 4);
});

test('a step stays within the limits of its edge', () => {
  const counts = { top: 16, bottom: 4, left: 12, right: 0 };
  assert.equal(capture.stepZone(counts, bottom, 1), 5);
  assert.equal(capture.stepZone(counts, bottom, -1), 3);
  assert.equal(capture.stepZone(counts, top, 1), null);
  assert.equal(capture.stepZone(counts, left, 1), null);
  assert.equal(capture.stepZone(counts, right, -1), null);
  assert.equal(capture.stepZone(counts, right, 1), 1);
});

test('the last zone cannot be removed', () => {
  assert.equal(capture.stepZone({ top: 1, bottom: 0, left: 0, right: 0 }, top, -1), null);
  assert.equal(capture.stepZone({ top: 1, bottom: 0, left: 0, right: 1 }, top, -1), 0);
});

test('the counts in one line', () => {
  assert.equal(capture.zoneSummary({ top: 4, bottom: 0, left: 3, right: 12 }), '4 top, 0 bottom, 3 left, 12 right');
});

test('a typed number is accepted only as a whole number within its range', () => {
  assert.equal(capture.parseNumber('1', 1, 60), 1);
  assert.equal(capture.parseNumber('60', 1, 60), 60);
  assert.equal(capture.parseNumber('007', 1, 60), 7);
  assert.equal(capture.parseNumber('', 1, 60), null);
  assert.equal(capture.parseNumber('0', 1, 60), null);
  assert.equal(capture.parseNumber('61', 1, 60), null);
  assert.equal(capture.parseNumber('1.5', 1, 60), null);
  assert.equal(capture.parseNumber('-5', 1, 60), null);
  assert.equal(capture.parseNumber('ten', 1, 60), null);
});

test('timing as a line of text', () => {
  assert.equal(capture.timingText({ frameMs: 620, fps: 1.6 }), '620 ms per frame, 1.6 per second');
  assert.equal(capture.timingText({ frameMs: 33, fps: 30 }), '33 ms per frame, 30.0 per second');
});

test('no timing line without timing', () => {
  assert.equal(capture.timingText({ frameMs: null, fps: 0 }), '');
  // An older service does not send the fields at all.
  assert.equal(capture.timingText({ version: '1.1.0' }), '');
  assert.equal(capture.timingText(null), '');
});

test('preview positions come from the reply', () => {
  const result = {
    ok: true,
    colors: [[1, 2, 3], [4, 5, 6]],
    points: [{ x: 0.25, y: 0.05, edge: 'top' }, { x: 0.75, y: 0.05, edge: 'top' }]
  };
  assert.deepEqual(capture.previewPoints(result), [{ x: 0.25, y: 0.05 }, { x: 0.75, y: 0.05 }]);
});

test('a reply without positions uses the sixteen places of older services', () => {
  const colors = Array.from({ length: 16 }, () => [0, 0, 0]);
  const points = capture.previewPoints({ ok: true, colors });
  assert.equal(points.length, 16);
  assert.deepEqual(points[0], { x: 0.21, y: 0.05 });
  assert.deepEqual(points[15], { x: 0.65, y: 0.5 });
});

test('positions that do not match the colours are not used', () => {
  const result = { ok: true, colors: [[1, 2, 3], [4, 5, 6]], points: [{ x: 0.5, y: 0.05, edge: 'top' }] };
  assert.deepEqual(capture.previewPoints(result), []);
  assert.deepEqual(capture.previewPoints({ ok: true, colors: [[1, 2, 3]], points: [{ x: 'left', y: 0, edge: 'top' }] }), []);
});
