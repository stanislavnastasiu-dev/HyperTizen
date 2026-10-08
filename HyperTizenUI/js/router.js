// Shows one screen at a time and passes it lifecycle calls and the Back key.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};

  // screens: name -> { enter(params)?, leave()?, back()? }. setVisible(name, visible) shows or hides it.
  function createRouter(screens, setVisible) {
    let current = null;

    return {
      current: () => current,

      go(name, params) {
        const next = screens[name];
        if (!next) throw new Error('Unknown screen: ' + name);

        if (current) {
          if (screens[current].leave) screens[current].leave();
          setVisible(current, false);
        }
        current = name;
        setVisible(name, true);
        if (next.enter) next.enter(params || {});
      },

      // True when the current screen handled Back.
      back() {
        const screen = current && screens[current];
        return !!(screen && screen.back && screen.back());
      },

      notify(method, argument) {
        const screen = current && screens[current];
        if (screen && screen[method]) screen[method](argument);
      }
    };
  }

  HT.router = { createRouter };
  if (typeof module !== 'undefined' && module.exports) module.exports = HT.router;
})(typeof window !== 'undefined' ? window : globalThis);
