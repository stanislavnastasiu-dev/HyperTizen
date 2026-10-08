// Shows the colors the TV is capturing, at the places on screen they are read from.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};
  HT.screens = HT.screens || {};

  HT.screens['preview'] = function (ctx) {
    const frame = ctx.el('preview-frame');
    const message = ctx.el('preview-message');
    let swatches = [];
    let timer = null;
    let waiting = false;
    let active = false;

    // One swatch per color; the number changes with the zone settings.
    function resize(count) {
      while (swatches.length > count) frame.removeChild(swatches.pop());
      while (swatches.length < count) {
        const swatch = document.createElement('div');
        swatch.className = 'swatch';
        frame.appendChild(swatch);
        swatches.push(swatch);
      }
    }

    function show(result) {
      if (!active) return;
      if (!result.ok || !result.colors) {
        frame.classList.add('empty');
        message.textContent = 'This TV did not return any colors. ' + (result.error || '');
        return;
      }

      const points = HT.capture.previewPoints(result);
      frame.classList.remove('empty');
      message.textContent = points.length ? '' : 'The service sent colors without their positions.';
      resize(points.length);
      points.forEach((point, index) => {
        const color = result.colors[index];
        swatches[index].style.left = (point.x * 100) + '%';
        swatches[index].style.top = (point.y * 100) + '%';
        swatches[index].style.backgroundColor = 'rgb(' + color[0] + ',' + color[1] + ',' + color[2] + ')';
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
