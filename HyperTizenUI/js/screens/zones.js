// How many zones each edge of the screen has, and what that costs in time.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  HT.screens['zones'] = function (ctx) {
    const zones = HT.capture.ZONES;
    const counts = {};
    const timing = ctx.el('zones-timing');

    function show() {
      zones.forEach(zone => { ctx.el('zones-' + zone.id + '-value').textContent = String(counts[zone.id]); });
    }

    function change(zone, delta) {
      const next = HT.capture.stepZone(counts, zone, delta);
      if (next === null || !ctx.set(zone.key, String(next))) return;
      counts[zone.id] = next;
      show();
    }

    function load() {
      const client = ctx.client();
      if (!client) return;
      zones.forEach(zone => {
        client.readConfig(zone.key).then(reply => {
          counts[zone.id] = HT.capture.zoneValue(reply, zone);
          show();
        }, () => {});
      });
    }

    zones.forEach(zone => {
      ctx.el('zones-' + zone.id + '-down').onclick = () => change(zone, -1);
      ctx.el('zones-' + zone.id + '-up').onclick = () => change(zone, 1);
    });

    return {
      enter() {
        zones.forEach(zone => { counts[zone.id] = zone.fallback; });
        show();
        timing.textContent = HT.capture.timingText(ctx.status());
        load();
        ctx.el('zones-top-up').focus();
      },

      onStatus(status) {
        timing.textContent = HT.capture.timingText(status);
      },

      back() {
        ctx.router.go('settings');
        return true;
      }
    };
  };
})(typeof window !== 'undefined' ? window : globalThis);
