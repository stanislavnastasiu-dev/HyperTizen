// Typing a whole number with the remote, for settings with too many values for buttons.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0', 'del'];

  HT.screens['number'] = function (ctx) {
    const ok = ctx.el('number-ok');
    let params = {};
    let typed = '';

    function value() {
      return HT.capture.parseNumber(typed, params.min, params.max);
    }

    function render() {
      ctx.el('number-value').textContent = typed;
      ok.disabled = value() === null;
    }

    function press(key) {
      if (key === 'del') typed = typed.slice(0, -1);
      else if (/^[0-9]$/.test(key) && typed.length < String(params.max).length + 1) typed += key;
      else return;
      render();
    }

    KEYS.forEach(key => {
      const button = document.createElement('button');
      button.className = 'focusable key';
      button.id = 'num-' + key;
      button.textContent = key === 'del' ? 'Delete' : key;
      button.onclick = () => press(key);
      ctx.el('number-keys').appendChild(button);
    });

    ok.onclick = () => {
      const number = value();
      if (number === null || !ctx.set(params.key, String(number))) return;
      ctx.router.go(params.back);
    };

    return {
      // params: { key, title, name, hint, min, max, back }
      enter(newParams) {
        params = newParams;
        typed = '';
        ctx.el('number-title').textContent = params.title;
        ctx.el('number-name').textContent = params.name;
        ctx.el('number-hint').textContent = params.hint;
        render();
        ctx.el('num-1').focus();
      },

      // Digits typed on a keyboard or a remote with number keys.
      key: press,

      back() {
        ctx.router.go(params.back);
        return true;
      }
    };
  };
})(typeof window !== 'undefined' ? window : globalThis);
