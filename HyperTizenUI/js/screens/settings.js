// Frame rate limit, priority, and forgetting the server.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  const DEFAULT_PRIORITY = 99;

  HT.screens['settings'] = function (ctx) {
    const fpsButtons = Array.prototype.slice.call(ctx.el('fps-options').querySelectorAll('button'));
    const priorityValue = ctx.el('priority-value');
    const forget = ctx.el('forget-server');
    const confirmation = ctx.el('forget-confirm');
    const cancel = ctx.el('forget-no');
    let priority = DEFAULT_PRIORITY;

    function showFps(value) {
      fpsButtons.forEach(button => button.classList.toggle('selected', button.dataset.fps === value));
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

    function showConfirmation(visible) {
      confirmation.hidden = !visible;
      forget.hidden = visible;
    }

    function load() {
      const client = ctx.client();
      if (!client) return;
      client.readConfig('maxFps').then(reply => showFps(reply.error ? '0' : String(reply.value)), () => {});
      client.readConfig('priority').then(reply => {
        priority = reply.error ? DEFAULT_PRIORITY : (Number(reply.value) || DEFAULT_PRIORITY);
        showPriority();
      }, () => {});
    }

    fpsButtons.forEach(button => {
      button.onclick = () => {
        if (ctx.set('maxFps', button.dataset.fps)) showFps(button.dataset.fps);
      };
    });
    ctx.el('priority-down').onclick = () => changePriority(-1);
    ctx.el('priority-up').onclick = () => changePriority(1);

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
        showPriority();
        load();
        fpsButtons[0].focus();
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
