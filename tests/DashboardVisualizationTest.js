const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Only startup controls are stubbed; assertions exercise the dashboard's real calculations.
const nodes = new Map();
const element = id => {
  if (!nodes.has(id)) nodes.set(id, {
    addEventListener() {}, setAttribute() {}, getAttribute() { return null; },
    classList: { add() {}, toggle() {} }, style: { setProperty() {} }
  });
  return nodes.get(id);
};
const context = vm.createContext({
  Intl, Date, Number, Math, Array, Map,
  document: {
    getElementById: element, documentElement: element('document'),
    querySelectorAll() { return []; }
  },
  window: { matchMedia() { return { matches: false }; }, addEventListener() {} },
  fetch() { return new Promise(() => {}); }, setInterval() {}, setTimeout() {},
  ResizeObserver: class { observe() {} }
});
vm.runInContext(fs.readFileSync(path.join(__dirname, '..', 'assets', 'dashboard.js'), 'utf8'), context);
assert.equal(typeof context.getVisibleDailyUsage, 'function', 'calendar-based chart selection is required');
assert.equal(typeof context.summarizeDailyUsage, 'function', 'shared chart aggregation is required');
const plain = value => JSON.parse(JSON.stringify(value));

const sparse = context.getVisibleDailyUsage([
  { date: '2026-09-01', tokens: 5 },
  { date: '2026-09-25', tokens: 10 },
  { date: '2026-09-26', tokens: 20 },
  { date: '2026-10-02', tokens: 30 }
], 7);
assert.deepEqual(plain(sparse.map(item => item.date)), ['2026-09-26', '2026-10-02'],
  '7 days must select calendar days, rather than the last 7 records');

const ordered = context.getVisibleDailyUsage([
  { date: '2026-10-02', tokens: 30 },
  { date: '2026-09-28', tokens: 10 },
  { date: '2026-09-29', tokens: 0 },
  { date: 'invalid', tokens: 100 },
  { date: '2026-09-31', tokens: 100 },
  { date: '2026-09-30', tokens: -1 }
], 30);
const summary = context.summarizeDailyUsage(ordered);
assert.equal(summary.total, 40, 'all charts must agree on the selected token total');
assert.deepEqual(plain(summary.cumulative.map(item => item.tokens)), [10, 10, 40],
  'cumulative totals must preserve zero-token records in chronological order');
assert.deepEqual(plain(summary.weekdayTotals.map(item => item.tokens)), [10, 0, 0, 0, 30, 0, 0],
  'weekday totals must use the record date and sum to the selected total');
assert.equal(summary.weekdayTotals.reduce((sum, item) => sum + item.tokens, 0), summary.total);

const empty = context.summarizeDailyUsage(context.getVisibleDailyUsage([], 30));
assert.equal(empty.total, 0);
assert.equal(empty.cumulative.length, 0);
assert.equal(empty.weekdayTotals.every(item => item.tokens === 0), true);

// Dense cumulative points must not let later dates capture an earlier point's center.
class ChartElement {
  constructor(tag) { this.tag = tag; this.attributes = {}; this.children = []; this.classList = { add() {} }; }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  append(...children) { this.children.push(...children); }
  appendChild(child) { this.children.push(child); }
  replaceChildren() { this.children = []; }
  addEventListener() {}
}
context.document.createElement = tag => new ChartElement(tag);
context.document.createElementNS = (namespace, tag) => new ChartElement(tag);
const denseChart = new ChartElement('div');
denseChart.clientWidth = 300;
nodes.set('cumulativeChart', denseChart);
const denseDates = Array.from({ length: 30 }, (_, index) => ({
  date: `2026-09-${String(index + 1).padStart(2, '0')}`, tokens: (index + 1) * 10
}));
context.renderSvgChart('cumulativeChart', denseDates, true);
const marks = denseChart.children[0].children.filter(item => item.tag === 'g');
for (let index = 0; index < marks.length - 1; index++) {
  const point = marks[index].children[0].attributes;
  const nextHit = marks[index + 1].children[1].attributes;
  const distance = Math.hypot(Number(nextHit.cx) - Number(point.cx), Number(nextHit.cy) - Number(point.cy));
  assert.ok(distance > Number(nextHit.r), 'later date hit regions must not cover an earlier visible point');
}
console.log('Dashboard visualization calculations passed.');
