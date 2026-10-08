// Moves focus with the remote's arrow keys, by where elements are on screen.
(function (root) {
  'use strict';
  const HT = root.HyperTizen = root.HyperTizen || {};

  // Sideways offset counts three times as much as distance, so rows and columns are followed.
  const CROSS_WEIGHT = 3;

  function center(rect) {
    return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  }

  // Distance between two ranges on one axis; 0 when they overlap.
  function gap(startA, sizeA, startB, sizeB) {
    if (startB > startA + sizeA) return startB - (startA + sizeA);
    if (startA > startB + sizeB) return startA - (startB + sizeB);
    return 0;
  }

  function pickNext(current, candidates, direction) {
    const from = center(current.rect);
    let best = null;
    let bestScore = Infinity;

    candidates.forEach(candidate => {
      if (candidate === current) return;
      const to = center(candidate.rect);
      const horizontal = direction === 'left' || direction === 'right';
      const primary = horizontal
        ? (direction === 'right' ? to.x - from.x : from.x - to.x)
        : (direction === 'down' ? to.y - from.y : from.y - to.y);
      if (primary <= 1) return;

      // The whole candidate must lie past the current element's edge, not just its centre:
      // a wide element in another row is not "to the right" of something beneath it.
      const a = current.rect;
      const b = candidate.rect;
      const clearance = horizontal
        ? (direction === 'right' ? b.left - (a.left + a.width) : a.left - (b.left + b.width))
        : (direction === 'down' ? b.top - (a.top + a.height) : a.top - (b.top + b.height));
      if (clearance < -1) return;

      const cross = horizontal
        ? gap(current.rect.top, current.rect.height, candidate.rect.top, candidate.rect.height)
        : gap(current.rect.left, current.rect.width, candidate.rect.left, candidate.rect.width);
      const score = primary + cross * CROSS_WEIGHT;
      if (score < bestScore) {
        best = candidate;
        bestScore = score;
      }
    });

    return best;
  }

  function focusables(container) {
    return Array.prototype.filter.call(
      container.querySelectorAll('.focusable'),
      element => !element.disabled && element.offsetParent !== null);
  }

  function move(container, direction) {
    const elements = focusables(container);
    if (!elements.length) return;

    const index = elements.indexOf(document.activeElement);
    if (index < 0) {
      elements[0].focus();
      return;
    }

    const items = elements.map(element => ({ element, rect: element.getBoundingClientRect() }));
    const next = pickNext(items[index], items, direction);
    if (next) next.element.focus();
  }

  HT.focus = { pickNext, move, focusables };
  if (typeof module !== 'undefined' && module.exports) module.exports = HT.focus;
})(typeof window !== 'undefined' ? window : globalThis);
