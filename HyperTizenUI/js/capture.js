// Zone counts, typed numbers, timing text and preview positions: the logic the screens share.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};

  // The same limits and defaults as CaptureLayout in the service.
  const ZONES = [
    { key: 'zonesTop', id: 'top', name: 'Top zones', max: 16, fallback: 4 },
    { key: 'zonesBottom', id: 'bottom', name: 'Bottom zones', max: 16, fallback: 4 },
    { key: 'zonesLeft', id: 'left', name: 'Left zones', max: 12, fallback: 3 },
    { key: 'zonesRight', id: 'right', name: 'Right zones', max: 12, fallback: 3 }
  ];

  // Where services older than the zone settings measure their sixteen colors.
  const LEGACY_POINTS = [
    [0.21, 0.05], [0.45, 0.05], [0.7, 0.05], [0.93, 0.07],
    [0.95, 0.275], [0.95, 0.5], [0.95, 0.8],
    [0.79, 0.95], [0.65, 0.95], [0.35, 0.95], [0.15, 0.95],
    [0.05, 0.725], [0.05, 0.4], [0.05, 0.2],
    [0.35, 0.5], [0.65, 0.5]
  ].map(point => ({ x: point[0], y: point[1] }));

  function parseNumber(text, min, max) {
    if (!/^[0-9]+$/.test(String(text))) return null;
    const value = Number(text);
    return value >= min && value <= max ? value : null;
  }

  function zoneValue(reply, zone) {
    if (!reply || reply.error) return zone.fallback;
    const value = parseNumber(reply.value, 0, zone.max);
    return value === null ? zone.fallback : value;
  }

  // The new count, or null when the step would leave the limits or remove the last zone.
  function stepZone(counts, zone, delta) {
    const next = counts[zone.id] + delta;
    if (next < 0 || next > zone.max) return null;
    const total = ZONES.reduce((sum, other) => sum + (other.id === zone.id ? next : counts[other.id]), 0);
    return total > 0 ? next : null;
  }

  function zoneSummary(counts) {
    return ZONES.map(zone => counts[zone.id] + ' ' + zone.id).join(', ');
  }

  function timingText(status) {
    if (!status || typeof status.frameMs !== 'number') return '';
    return status.frameMs + ' ms per frame, ' + Number(status.fps || 0).toFixed(1) + ' per second';
  }

  function previewPoints(result) {
    const colors = (result && result.colors) || [];
    if (!result || result.points === undefined || result.points === null) return LEGACY_POINTS.slice(0, colors.length);

    const usable = Array.isArray(result.points) && result.points.length === colors.length
      && result.points.every(point => point && typeof point.x === 'number' && typeof point.y === 'number');
    return usable ? result.points.map(point => ({ x: point.x, y: point.y })) : [];
  }

  HT.capture = { ZONES, parseNumber, zoneValue, stepZone, zoneSummary, timingText, previewPoints };
  if (typeof module !== 'undefined' && module.exports) module.exports = HT.capture;
})(typeof window !== 'undefined' ? window : globalThis);
