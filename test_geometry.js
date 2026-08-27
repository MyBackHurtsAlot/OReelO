const assert = require("node:assert/strict");
const { itemIndex, itemPosition } = require("./geometry");

assert.equal(itemIndex(0, -100, 4, 20), 0);
assert.equal(itemIndex(100, 0, 4, 20), 1);
assert.equal(itemIndex(0, 100, 4, 20), 2);
assert.equal(itemIndex(-100, 0, 4, 20), 3);
assert.equal(itemIndex(2, 2, 4, 20), -1);

const top = itemPosition(0, 4, 100);
assert.ok(Math.abs(top.x) < 0.0001);
assert.ok(Math.abs(top.y + 100) < 0.0001);

console.log("geometry checks passed");
