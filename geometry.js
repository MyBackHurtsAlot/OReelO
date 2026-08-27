(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  root.SequenceWheelGeometry = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  function itemIndex(dx, dy, count, deadZone) {
    if (!count || Math.hypot(dx, dy) < deadZone) return -1;
    const angle = (Math.atan2(dy, dx) + Math.PI * 2 + Math.PI / 2) % (Math.PI * 2);
    return Math.floor((angle + Math.PI / count) / (Math.PI * 2 / count)) % count;
  }

  function itemPosition(index, count, radius) {
    const angle = index * Math.PI * 2 / count - Math.PI / 2;
    return {
      x: Math.cos(angle) * radius,
      y: Math.sin(angle) * radius
    };
  }

  return { itemIndex, itemPosition };
});
