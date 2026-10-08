// The three setup steps: choose a server, test the LEDs, turn capture on.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  HT.screens['setup-server'] = function (ctx) {
    const list = ctx.el('server-list');
    const hint = ctx.el('server-hint');
    const manual = ctx.el('server-manual');
    let params = {};
    let timer = null;
    let scanning = false;
    let scanned = false;
    let known = [];

    function updateHint() {
      if (known.length) hint.textContent = 'Select a server:';
      else hint.textContent = scanned ? 'No servers found yet.' : 'Searching your network...';
    }

    function choose(url) {
      if (!ctx.set('rpcServer', url)) return;
      ctx.router.go('setup-test', { fromHome: params.fromHome, url });
    }

    function add(device) {
      const url = HT.address.fromDeviceUrl(device.UrlBase);
      if (!url || known.indexOf(url) >= 0) return;
      known.push(url);

      const button = document.createElement('button');
      button.className = 'focusable device';
      const name = document.createElement('span');
      name.textContent = device.FriendlyName || 'Hyperion';
      const address = document.createElement('span');
      address.className = 'device-address';
      address.textContent = HT.address.display(url);
      button.appendChild(name);
      button.appendChild(address);
      button.onclick = () => choose(url);
      list.appendChild(button);
    }

    function scan() {
      const client = ctx.client();
      if (!client || scanning) return;
      scanning = true;
      client.scan()
        .then(result => (result.devices || []).forEach(add), () => {})
        .then(() => {
          scanning = false;
          scanned = true;
          updateHint();
        });
    }

    manual.onclick = () => ctx.router.go('keypad', { fromHome: params.fromHome });
    ctx.el('server-rescan').onclick = scan;

    return {
      enter(newParams) {
        params = newParams;
        known = [];
        scanned = false;
        list.textContent = '';
        ctx.el('server-step').hidden = !!params.fromHome;
        updateHint();
        manual.focus();
        scan();
        timer = setInterval(scan, HT.timing.scanMs);
      },

      leave() {
        clearInterval(timer);
      },

      back() {
        if (!params.fromHome) return false;
        ctx.router.go('home');
        return true;
      }
    };
  };

  HT.screens['setup-test'] = function (ctx) {
    const title = ctx.el('test-title');
    const detail = ctx.el('test-detail');
    const tips = ctx.el('test-tips');
    const question = ctx.el('test-question');
    const retry = ctx.el('test-retry');
    const yes = ctx.el('test-yes');
    const again = ctx.el('test-again');
    let params = {};
    let run = 0;

    function show(rows) {
      question.hidden = rows !== 'question';
      retry.hidden = rows !== 'retry';
    }

    function chooseAnother() {
      ctx.router.go('setup-server', { fromHome: params.fromHome });
    }

    function start() {
      const client = ctx.client();
      if (!client) return;
      const thisRun = ++run;
      const address = HT.address.display(params.url);

      title.textContent = 'Sending test colors to ' + address + '...';
      detail.textContent = '';
      tips.hidden = true;
      show('none');

      client.testLeds().then(result => {
        if (thisRun !== run) return;
        if (result.ok) {
          title.textContent = 'Did your LEDs show red, green, blue and white?';
          show('question');
          yes.focus();
        } else {
          title.textContent = 'Could not reach ' + address + '.';
          detail.textContent = result.error || '';
          show('retry');
          again.focus();
        }
      }, () => {
        if (thisRun !== run) return;
        title.textContent = 'The service did not answer.';
        show('retry');
        again.focus();
      });
    }

    yes.onclick = () => ctx.router.go(params.fromHome ? 'home' : 'setup-enable');
    ctx.el('test-no').onclick = () => {
      title.textContent = "Let's check a few things";
      detail.textContent = '';
      tips.hidden = false;
      show('retry');
      again.focus();
    };
    again.onclick = start;
    ctx.el('test-other').onclick = chooseAnother;

    return {
      enter(newParams) {
        params = newParams;
        ctx.el('test-step').hidden = !!params.fromHome;
        start();
      },

      // A result arriving after the screen was left must not change it.
      leave() {
        run++;
      },

      back() {
        chooseAnother();
        return true;
      }
    };
  };

  HT.screens['setup-enable'] = function (ctx) {
    const on = ctx.el('enable-on');

    on.onclick = () => {
      ctx.set('enabled', 'true');
      ctx.router.go('home');
    };
    ctx.el('enable-later').onclick = () => ctx.router.go('home');

    return {
      enter() {
        on.focus();
      }
    };
  };
})(typeof window !== 'undefined' ? window : globalThis);
