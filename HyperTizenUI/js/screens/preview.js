// Shows the colors the TV is capturing, at the places on screen they are read from.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  // Same order and positions as the capture points in HyperTizen/VideoEnhanceCapturer.cs.
  const POINTS = [
    [0.21, 0.05], [0.45, 0.05], [0.7, 0.05], [0.93, 0.07],
    [0.95, 0.275], [0.95, 0.5], [0.95, 0.8],
    [0.79, 0.95], [0.65, 0.95], [0.35, 0.95], [0.15, 0.95],
    [0.05, 0.725], [0.05, 0.4], [0.05, 0.2],
    [0.35, 0.5], [0.65, 0.5]
  ];

  HT.screens['preview'] = function (ctx) {
    const frame = ctx.el('preview-frame');
    const message = ctx.el('preview-message');
    let timer = null;
    let waiting = false;
    let active = false;

    const swatches = POINTS.map(point => {
      const swatch = document.createElement('div');
      swatch.className = 'swatch';
      swatch.style.left = (point[0] * 100) + '%';
      swatch.style.top = (point[1] * 100) + '%';
      frame.appendChild(swatch);
      return swatch;
    });

    function show(result) {
      if (!active) return;
      if (!result.ok || !result.colors) {
        frame.classList.add('empty');
        message.textContent = 'This TV did not return any colors. ' + (result.error || '');
        return;
      }

      frame.classList.remove('empty');
      message.textContent = '';
      result.colors.forEach((color, index) => {
        if (swatches[index]) swatches[index].style.backgroundColor = 'rgb(' + color[0] + ',' + color[1] + ',' + color[2] + ')';
      });
    }

    function update() {
      const client = ctx.client();
      if (!client || waiting) return;
      waiting = true;
      client.getPreview()
        .then(show, () => { if (active) message.textContent = 'The service did not answer.'; })
        .then(() => { waiting = false; });
    }

    return {
      enter() {
        active = true;
        message.textContent = 'Loading...';
        update();
        timer = setInterval(update, HT.timing.previewMs);
      },

      leave() {
        active = false;
        clearInterval(timer);
      },

      back() {
        ctx.router.go('home');
        return true;
      }
    };
  };
})(typeof window !== 'undefined' ? window : globalThis);
