// Frame rate limit, priority, LED instance, capture zones, and forgetting the server.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  const DEFAULT_PRIORITY = 99;
  const MAX_INSTANCE = 254;

  HT.screens['settings'] = function (ctx) {
    const unlimited = ctx.el('fps-0');
    const limited = ctx.el('fps-custom');
    const sleepCap = ctx.el('sleep-custom');
    const priorityValue = ctx.el('priority-value');
    const zonesSummary = ctx.el('zones-summary');
    const forget = ctx.el('forget-server');
    const confirmation = ctx.el('forget-confirm');
    const cancel = ctx.el('forget-no');
    const instanceValue = ctx.el('instance-value');
    let priority = DEFAULT_PRIORITY;
    let instance = 0;

    function showFps(value) {
      const limit = Number(value) || 0;
      unlimited.classList.toggle('selected', limit === 0);
      limited.classList.toggle('selected', limit > 0);
      limited.textContent = limit > 0 ? String(limit) : 'Set a limit';
    }

    function showSleep(value) {
      const ms = Number(value) || 0;
      sleepCap.textContent = ms > 0 ? ms + ' ms' : 'Default';
    }

    function showPriority() {
      priorityValue.textContent = String(priority);
    }

    function changePriority(delta) {
      const next = Math.max(1, Math.min(253, priority + delta));
      if (next === priority || !ctx.set('priority', String(next))) return;
      priority = next;
      showPriority();
    }

    function changeInstance(delta) {
      const next = Math.max(0, Math.min(MAX_INSTANCE, instance + delta));
      if (next === instance || !ctx.set('instance', String(next))) return;
      instance = next;
      instanceValue.textContent = String(instance);
    }

    function showConfirmation(visible) {
      confirmation.hidden = !visible;
      forget.hidden = visible;
    }

    function load() {
      const client = ctx.client();
      if (!client) return;
      client.readConfig('maxFps').then(reply => showFps(reply.error ? '0' : String(reply.value)), () => {});
      client.readConfig('captureSleepMs').then(reply => showSleep(reply.error ? '0' : String(reply.value)), () => {});
      client.readConfig('priority').then(reply => {
        priority = reply.error ? DEFAULT_PRIORITY : (Number(reply.value) || DEFAULT_PRIORITY);
        showPriority();
      }, () => {});
      client.readConfig('instance').then(reply => {
        const stored = reply.error ? null : HT.capture.parseNumber(reply.value, 0, MAX_INSTANCE);
        instance = stored === null ? 0 : stored;
        instanceValue.textContent = String(instance);
      }, () => {});

      const counts = {};
      HT.capture.ZONES.forEach(zone => { counts[zone.id] = zone.fallback; });
      HT.capture.ZONES.forEach(zone => {
        client.readConfig(zone.key).then(reply => {
          counts[zone.id] = HT.capture.zoneValue(reply, zone);
          zonesSummary.textContent = HT.capture.zoneSummary(counts);
        }, () => {});
      });
    }

    unlimited.onclick = () => {
      if (ctx.set('maxFps', '0')) showFps('0');
    };
    limited.onclick = () => ctx.router.go('number', {
      key: 'maxFps',
      title: 'Frame rate limit',
      name: 'Frames per second',
      hint: 'A whole number from 1 to 60.',
      min: 1,
      max: 60,
      back: 'settings'
    });
    sleepCap.onclick = () => ctx.router.go('number', {
      key: 'captureSleepMs',
      title: 'Capture settle time',
      name: 'Milliseconds per batch',
      hint: 'A whole number of milliseconds. 0 uses the TV’s own value; lower is faster but may read stale colors.',
      min: 0,
      max: 1000,
      back: 'settings'
    });
    ctx.el('priority-down').onclick = () => changePriority(-1);
    ctx.el('priority-up').onclick = () => changePriority(1);
    ctx.el('instance-down').onclick = () => changeInstance(-1);
    ctx.el('instance-up').onclick = () => changeInstance(1);
    ctx.el('settings-zones').onclick = () => ctx.router.go('zones');

    forget.onclick = () => {
      const status = ctx.status();
      ctx.el('forget-text').textContent = 'Forget ' + HT.address.display(status && status.rpcServer) + '?';
      showConfirmation(true);
      cancel.focus();
    };
    cancel.onclick = () => {
      showConfirmation(false);
      forget.focus();
    };
    ctx.el('forget-yes').onclick = () => {
      if (!ctx.del('rpcServer')) return;
      showConfirmation(false);
      ctx.refresh();
      ctx.router.go('setup-server', {});
    };

    return {
      enter() {
        const status = ctx.status();
        ctx.el('about').textContent = 'Service ' + (status ? status.version : 'unknown') + ' - UI ' + HT.uiVersion;
        showConfirmation(false);
        showFps('0');
        showSleep('0');
        showPriority();
        zonesSummary.textContent = '';
        load();
        unlimited.focus();
      },

      back() {
        if (!confirmation.hidden) {
          showConfirmation(false);
          forget.focus();
          return true;
        }
        ctx.router.go('home');
        return true;
      }
    };
  };
})(typeof window !== 'undefined' ? window : globalThis);
