// On-screen keypad for typing a server address with the remote.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '0', 'del'];
  const MAX_LENGTH = { ip: 15, port: 5 };

  HT.screens['keypad'] = function (ctx) {
    const connect = ctx.el('keypad-connect');
    const values = { ip: '', port: '8090' };
    let params = {};
    let active = 'ip';

    function render() {
      ctx.el('value-ip').textContent = values.ip;
      ctx.el('value-port').textContent = values.port;
      ctx.el('field-ip').classList.toggle('active', active === 'ip');
      ctx.el('field-port').classList.toggle('active', active === 'port');
      connect.disabled = !(HT.address.isValidIp(values.ip) && HT.address.isValidPort(values.port));
    }

    function press(key) {
      let value = values[active];
      if (key === 'del') value = value.slice(0, -1);
      else if (key === '.' && active !== 'ip') return;
      else value += key;

      if (value.length > MAX_LENGTH[active]) return;
      values[active] = value;
      render();
    }

    KEYS.forEach(key => {
      const button = document.createElement('button');
      button.className = 'focusable key';
      button.id = 'key-' + (key === '.' ? 'dot' : key);
      button.textContent = key === 'del' ? 'Delete' : key;
      button.onclick = () => press(key);
      ctx.el('keypad').appendChild(button);
    });

    ctx.el('keypad-next').onclick = () => {
      active = active === 'ip' ? 'port' : 'ip';
      render();
    };

    connect.onclick = () => {
      if (connect.disabled) return;
      const url = HT.address.toWsUrl(values.ip, values.port);
      if (!ctx.set('rpcServer', url)) return;
      ctx.router.go('setup-test', { fromHome: params.fromHome, url });
    };

    return {
      enter(newParams) {
        params = newParams;
        values.ip = '';
        values.port = '8090';
        active = 'ip';
        render();
        ctx.el('key-1').focus();
      },

      // Digits typed on a keyboard or a remote with number keys.
      key: press,

      back() {
        ctx.router.go('setup-server', { fromHome: params.fromHome });
        return true;
      }
    };
  };
})(typeof window !== 'undefined' ? window : globalThis);
