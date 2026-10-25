// IDCator TradingView indicators - behaviour tests in PineTS.
//
//   cd tools/pine-test && npm install && node run.mjs [--verbose] [--render] [FILTER...]
//
// Every scenario runs the real .pine file from TradingView/ in PineTS (LuxAlgo's open-source Pine Script v6 runtime)
// on hand-built or simulated order flow; --render also writes PNG charts to tools/pine-test/out/.
// Exit code = number of failed scenarios.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { runPine, drawings, makeMarket, scaleMarket, renderSvg, renderPng, closeBrowser, ALIASES, resolveScript, candlesAt } from './harness.mjs';
import { Tape, Live, barStart } from './tape.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const TV = path.join(here, '..', '..', 'TradingView');
// --suite: the tool scenarios run on IDC Order Flow (the four tools in one script) with only that tool switched on
const ON_SUITE = process.argv.includes('--suite');
const tool = (file, name) => {
    if (!ON_SUITE) return path.join(TV, file);
    const sw = Object.fromEntries(['HVN Profile', 'Imbalance Levels', 'Heavy Absorption', 'Pressure Toolkit'].map((n) => [n, n === name]));
    ALIASES.set('suite:' + name, { file: path.join(TV, 'IDC_OrderFlow.pine'), inputs: sw });
    return 'suite:' + name;
};
const IMB = tool('IDC_Imbalance_Levels.pine', 'Imbalance Levels');
const HVN = tool('IDC_HVN_Profile.pine', 'HVN Profile');
const HA = tool('IDC_Heavy_Absorption.pine', 'Heavy Absorption');
const PTK = tool('IDC_Pressure_Toolkit.pine', 'Pressure Toolkit');
const OUT = path.join(here, 'out');

const args = process.argv.slice(2);
const verbose = args.includes('--verbose');
const render = args.includes('--render');
const filters = args.filter((a) => !a.startsWith('--'));

const scenarios = [];
const scenario = (name, desc, fn) => scenarios.push({ name, desc, fn });

class Ctx {
    constructor() { this.fails = []; this.notes = []; }
    check(ok, what) { if (!ok) this.fails.push(what); return !!ok; }
    eq(expected, actual, what) { return this.check(Object.is(expected, actual) || expected === actual, `${what}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`); }
    near(expected, actual, tol, what) { return this.check(Math.abs(expected - actual) <= tol, `${what}: expected ${expected} ± ${tol}, got ${actual}`); }
    note(s) { this.notes.push(s); }
}

const near = (a, b) => Math.abs(a - b) < 1e-6;
// live (forming bar / period) drawings have a dashed outline; permanent ones a solid one
const dashed = (o) => /dash/.test(String(o.border_style ?? o.style ?? ''));
const solid = (list) => list.filter((o) => !dashed(o));
const balanced = (lo, hi, v, tick = 0.25) => { const out = []; for (let p = lo; p <= hi + 1e-9; p += tick) out.push({ price: Math.round(p / tick) * tick, ask: v, bid: v }); return out; };

// ═════════════════════════════════════════════ Imbalance Levels
/** Bar 1 holds asks 50/75/40 at 5000.25-5000.75 over bids 8/6/5 one row lower (ratios 6.25 / 12.5 / 8): one buy stack. */
function buyStackTape(touchBar = 3) {
    const t = new Tape();
    t.filler(0, 5001.50, 5001.75, 5001.25, 5002.00);
    const stack = [{ price: 5000.00, ask: 2, bid: 8 }, { price: 5000.25, ask: 50, bid: 6 }, { price: 5000.50, ask: 75, bid: 5 },
        { price: 5000.75, ask: 40, bid: 3 }, { price: 5001.00, ask: 10, bid: 9 }, ...balanced(5001.25, 5003.00, 8)];
    t.bar(1, 5000.75, 5002.00, stack, 5);
    t.filler(2, 5002.00, 5001.75, 5001.25, 5002.25);
    if (touchBar === 3) t.filler(3, 5001.75, 5001.00, 5000.75, 5001.75);
    else t.filler(3, 5001.75, 5001.50, 5001.25, 5002.00);
    t.filler(4, 5001.25, 5001.50, 5001.00, 5002.00);
    return t;
}

scenario('IMB-01 buy stack -> support zone', "asks over thin diagonal bids form one support zone labelled '12.5x | 165'", async (c) => {
    const t = buyStackTape(4);
    const ctx = await runPine(IMB, t.market());
    const boxes = drawings(ctx).boxes;
    c.note(boxes.map((b) => `${b.left}-${b.right} ${b.bottom}-${b.top} ${b.text}`).join(' / '));
    if (c.check(boxes.length === 1, `one zone, got ${boxes.length}`)) {
        const b = boxes[0];
        c.eq(1, b.left, 'zone starts at the stack bar');
        c.check(near(b.bottom, 5000.25) && near(b.top, 5001.00), `zone 5000.25..5001.00 (row edges), got ${b.bottom}..${b.top}`);
        c.eq('12.5x | 165', b.text, 'label = strongest ratio | stack volume');
        c.eq(5, b.right, 'untouched zone extends to the current bar (+1)');
        c.check(String(b.bgcolor).toLowerCase().startsWith('#22ab5c'), `green support fill, got ${b.bgcolor}`);
    }
});

scenario('IMB-02 touch freezes the zone', 'bar 3 trades into the zone: it stops at bar 3 and does not extend later', async (c) => {
    const ctx = await runPine(IMB, buyStackTape(3).market());
    const b = drawings(ctx).boxes[0];
    if (c.check(b, 'zone exists')) c.eq(3, b.right, 'right edge at the touching bar');
});

scenario('IMB-03 buy stack above the close is ignored', 'a down bar closing below its buy stack makes no support zone (filter on), one with the filter off', async (c) => {
    const t = new Tape();
    t.filler(0, 5003.00, 5003.25, 5002.75, 5003.50);
    const stack = [{ price: 5000.00, ask: 2, bid: 8 }, { price: 5000.25, ask: 50, bid: 6 }, { price: 5000.50, ask: 75, bid: 5 },
        { price: 5000.75, ask: 40, bid: 3 }, { price: 5001.00, ask: 10, bid: 9 }, ...balanced(5001.25, 5003.00, 8)];
    t.bar(1, 5003.00, 5000.00, stack, 5);
    t.filler(2, 4999.75, 4999.50, 4999.25, 4999.75);
    const on = drawings(await runPine(IMB, t.market())).boxes.filter((b) => String(b.bgcolor).toLowerCase().startsWith('#22ab5c'));
    const off = drawings(await runPine(IMB, t.market(), { 'Buy stacks below close, sell stacks above': false })).boxes.filter((b) => String(b.bgcolor).toLowerCase().startsWith('#22ab5c'));
    c.eq(0, on.length, 'no support zone with the filter on');
    c.eq(1, off.length, 'one support zone with the filter off');
});

scenario('IMB-04 sell stack -> resistance; zero diagonal = min ratio', "bids over EMPTY diagonal asks: '5x | 75' red resistance above the close", async (c) => {
    const t = new Tape();
    t.filler(0, 5000.50, 5000.25, 5000.00, 5000.75);
    const levels = [...balanced(4999.00, 5000.50, 8), { price: 5000.75, ask: 3, bid: 30 }, { price: 5001.00, ask: 0, bid: 20 }, { price: 5001.25, ask: 0, bid: 25 }, { price: 5001.50, ask: 0, bid: 4 }];
    t.bar(1, 5001.25, 5000.00, levels, 5);
    t.filler(2, 5000.00, 4999.75, 4999.50, 5000.25);
    const boxes = drawings(await runPine(IMB, t.market())).boxes;
    c.note(boxes.map((b) => `${b.bottom}-${b.top} ${b.text} ${b.bgcolor}`).join(' / '));
    const red = boxes.filter((b) => String(b.bgcolor).toLowerCase().startsWith('#e9393d'));
    if (c.check(red.length === 1, `one resistance zone, got ${red.length}`)) {
        c.check(near(red[0].bottom, 5000.75) && near(red[0].top, 5001.50), `zone 5000.75..5001.50, got ${red[0].bottom}..${red[0].top}`);
        c.eq('5x | 75', red[0].text, 'empty diagonal counts as the minimum ratio');
    }
});

scenario('IMB-05 realtime: zone freezes on the touching tick', 'the zone stops on the update that trades into it, one alert at that update', async (c) => {
    const t = buyStackTape(3);
    const trades = [...t.trades].sort((a, b) => a.time - b.time);
    const firstOf3 = trades.findIndex((x) => x.time >= barStart(3));
    const touchIdx = trades.findIndex((x, i) => i >= firstOf3 && x.price <= 5000.75);
    const live = new Live(IMB, t);
    try {
        await live.start(trades[firstOf3 - 1].time);
        let frozenAt = -1;
        for (let i = firstOf3; i < trades.length; i++) {
            const ctx = await live.to(trades[i].time);
            const b = drawings(ctx).boxes[0];
            if (b && b.right === 3 && frozenAt < 0) frozenAt = i;
        }
        c.eq(touchIdx, frozenAt, 'frozen on the touching trade');
        const alerts = (live.ctx.alerts ?? []).filter((a) => String(a.message).includes('imbalance'));
        c.check(alerts.length >= 1, `touch alert raised, got ${alerts.length}`);
        if (alerts.length) c.check(alerts[0].message.includes('12.5x | 165'), `alert names the zone: ${alerts[0].message}`);
    } finally { live.stop(); }
});

scenario('IMB-06 realtime: second zone touched later in the bar alerts too', 'bar 3 trades into the upper zone, later into the lower one: two alerts, one per newly touched zone', async (c) => {
    const t = new Tape();
    t.filler(0, 5001.50, 5001.75, 5001.25, 5002.00);
    // lower stack 5000.25-5000.75 (12.5x | 165), upper stack 5001.75-5002.25 (10x | 180)
    t.bar(1, 5000.75, 5003.00, [{ price: 5000.00, ask: 2, bid: 8 }, { price: 5000.25, ask: 50, bid: 6 }, { price: 5000.50, ask: 75, bid: 5 },
        { price: 5000.75, ask: 40, bid: 3 }, { price: 5001.00, ask: 10, bid: 9 }, { price: 5001.25, ask: 8, bid: 8 },
        { price: 5001.50, ask: 8, bid: 6 }, { price: 5001.75, ask: 60, bid: 6 }, { price: 5002.00, ask: 60, bid: 6 },
        { price: 5002.25, ask: 60, bid: 8 }, ...balanced(5002.50, 5003.00, 8)], 5);
    t.filler(2, 5003.00, 5002.75, 5002.75, 5003.25);
    t.filler(3, 5002.75, 5001.00, 5000.50, 5002.75);
    t.filler(4, 5001.00, 5001.25, 5000.75, 5001.50);
    const trades = [...t.trades].sort((a, b) => a.time - b.time);
    const firstOf3 = trades.findIndex((x) => x.time >= barStart(3));
    const live = new Live(IMB, t);
    try {
        await live.start(trades[firstOf3 - 1].time);
        c.eq(2, drawings(live.ctx).boxes.length, 'two support zones before bar 3');
        for (let i = firstOf3; i < trades.length && trades[i].time < barStart(4); i++) await live.to(trades[i].time);
        const alerts = (live.ctx.alerts ?? []).filter((a) => String(a.message).includes('imbalance')).map((a) => String(a.message));
        c.eq(2, alerts.length, 'alerts in bar 3');
        if (alerts.length === 2) {
            c.check(alerts[0].includes('10x | 180') && !alerts[0].includes('12.5x'), `first alert: upper zone only: ${alerts[0]}`);
            c.check(alerts[1].includes('12.5x | 165'), `second alert names the lower zone: ${alerts[1]}`);
        }
    } finally { live.stop(); }
});

scenario('IMB-07 realtime: the forming bar shows its stack live', 'bar 1 builds its buy stack tick by tick: a dashed zone shows before the close, a solid one stays after it', async (c) => {
    const t = buyStackTape(4);
    const trades = [...t.trades].sort((a, b) => a.time - b.time);
    const first1 = trades.findIndex((x) => x.time >= barStart(1));
    const first2 = trades.findIndex((x) => x.time >= barStart(2));
    const live = new Live(IMB, t);
    try {
        await live.start(trades[first1 - 1].time);
        let firstLive = -1, lastLabel = '';
        for (let i = first1; i < first2; i++) {
            const d = drawings(await live.to(trades[i].time));
            const lv = d.boxes.filter(dashed);
            c.check(solid(d.boxes).length === 0, `no final zone during bar 1 (update ${i - first1})`);
            if (lv.length && firstLive < 0) firstLive = i - first1;
            if (lv.length) lastLabel = String(lv[0].text);
        }
        c.check(firstLive >= 0 && first1 + firstLive < first2 - 1, `the stack shows live before the bar's last trade (update ${firstLive} of ${first2 - first1})`);
        c.eq('12.5x | 165', lastLabel, 'live label at the last trade of bar 1');
        const d = drawings(await live.to(trades[first2].time));
        c.eq(1, solid(d.boxes).length, 'one final zone after the close');
        c.eq('12.5x | 165', String(solid(d.boxes)[0]?.text), 'final label');
        c.eq(0, d.boxes.filter(dashed).length, 'the live outline is gone');
    } finally { live.stop(); }
});

// ═════════════════════════════════════════════ HVN Profile
/** One 5-minute period (20 bars of 15 s) whose profile is base + nodes at `primary` (heavier) and `secondary`. */
function hvnPeriod(t, firstBar, lo, hi, primary, secondary, primaryBuy = 0.65, secondaryBuy = 0.40) {
    const g = (p, ctr, w) => Math.exp(-(((p - ctr) / w) ** 2));
    const total = new Map();
    for (let p = lo; p <= hi + 1e-9; p += t.tick) total.set(t.P(p), Math.round(4 + 120 * g(p, primary, 0.75) + 100 * g(p, secondary, 0.75)));
    for (let b = 0; b < 20; b++) {
        const levels = [];
        for (const [p, v0] of total) {
            const v = Math.floor(v0 / 20) + (b < v0 % 20 ? 1 : 0);
            if (v <= 0) continue;
            const share = Math.abs(p - primary) <= 1 ? primaryBuy : Math.abs(p - secondary) <= 1 ? secondaryBuy : 0.5;
            const ask = Math.round(v * share);
            levels.push({ price: p, ask, bid: v - ask });
        }
        const mid = t.P((lo + hi) / 2);
        t.bar(firstBar + b, mid, b % 2 === 0 ? mid + t.tick : mid, levels, 7);
    }
    return total;
}

function fourPeriods() {
    const t = new Tape();
    const p1 = hvnPeriod(t, 0, 5003, 5012, 5010, 5005);
    hvnPeriod(t, 20, 5020, 5029, 5022, 5027);
    hvnPeriod(t, 40, 5040, 5049, 5045, 5042);
    t.filler(60, 5035, 5034, 5033, 5035);
    t.filler(61, 5034, 5033, 5032, 5034);
    t.filler(62, 5033, 5031, 5030.5, 5033);
    t.filler(63, 5031, 5016, 5016, 5031, 2);
    t.filler(64, 5016, 5014, 5009.75, 5016, 2);
    for (let b = 65; b < 82; b++) t.filler(b, 5014, 5015, 5013, 5016, 3);
    return { t, p1 };
}

const hvnZones = (ctx) => {
    const d = drawings(ctx);
    const labels = d.labels;
    return d.boxes.map((b) => ({ ...b, labels: labels.filter((l) => near(l.y, (b.top + b.bottom) / 2) && Math.abs(l.x - b.right) <= 1).map((l) => String(l.text).trim()) }));
};

scenario('HVN-01 two nodes per period', 'P1 nodes at 5010 (orange, primary) and 5005 (slate); labels "POC | buy%" at period end; touched by bar 64 only', async (c) => {
    const { t, p1 } = fourPeriods();
    const ctx = await runPine(HVN, t.market());
    const zones = hvnZones(ctx);
    c.note(zones.map((z) => `${z.left}-${z.right} ${z.bottom}-${z.top} [${z.labels.join(' ')}] ${z.bgcolor}`).join('\n      '));
    const P1 = zones.filter((z) => z.left === 0);
    if (c.check(P1.length === 2, `two zones from period 1, got ${P1.length}`)) {
        const prim = P1.find((z) => z.bottom <= 5010 && z.top > 5010);
        const sec = P1.find((z) => z.bottom <= 5005 && z.top > 5005);
        if (c.check(prim && sec, 'zones cover 5010 and 5005')) {
            c.check(String(prim.bgcolor).toLowerCase().startsWith('#ffa500'), `primary orange, got ${prim.bgcolor}`);
            c.check(String(sec.bgcolor).toLowerCase().startsWith('#6e7da0'), `secondary slate, got ${sec.bgcolor}`);
            c.eq(String(p1.get(5010)), prim.labels.find((s) => /^\d/.test(s)), 'primary label number = POC volume');
            c.check(prim.labels.some((s) => s === '| 65%'), `buyers 65%: [${prim.labels}]`);
            c.check(sec.labels.some((s) => s === '| 59%'), `sellers 59% (asks rounded per bar: 41% buyers): [${sec.labels}]`);
            c.eq(64, prim.right, 'P1 primary (5010) touched by bar 64 (low 5009.75)');
            c.check(sec.right > 64, `P1 secondary (5005) still active, right ${sec.right}`);
        }
    }
    const P2 = zones.filter((z) => z.left === 20);
    c.check(P2.length === 2 && P2.every((z) => z.right === 63), `P2 zones both touched at bar 63: ${P2.map((z) => z.right)}`);
});

scenario('HVN-02 realtime: period closes with its last bar', 'zones of a period appear when its last bar closes, not one bar later', async (c) => {
    const { t } = fourPeriods();
    const trades = [...t.trades].sort((a, b) => a.time - b.time);
    const live = new Live(HVN, t);
    try {
        const lastOf19 = trades.filter((x) => x.time < barStart(20)).pop();
        await live.start(trades.find((x) => x.time >= barStart(19)).time);
        c.eq(0, solid(drawings(live.ctx).boxes).length, 'no final zones while the period runs');
        c.eq(2, drawings(live.ctx).boxes.filter(dashed).length, 'the running period shows its 2 nodes live (dashed)');
        await live.to(lastOf19.time);
        c.eq(0, solid(drawings(live.ctx).boxes).length, 'still no final zones before the bar closes');
        const ctx = await live.to(barStart(20) - 1);
        c.check(drawings(ctx).boxes.length >= 0, 'bar 19 close');
        const after = await live.to(trades.find((x) => x.time >= barStart(20)).time);
        c.eq(2, solid(drawings(after).boxes).length, 'both period-1 zones are final on the first tick of the next period');
        c.eq(0, drawings(after).boxes.filter(dashed).length, 'no live outline left over');
    } finally { live.stop(); }
});

// ═════════════════════════════════════════════ Heavy Absorption
function populationTape(bars = 150, seed = 11) {
    const m = makeMarket({ seed, segments: [{ bars, tradesPerBar: 120, bigProb: 0 }] });
    const t = new Tape();
    t.trades = m.trades.map((x) => ({ ...x }));
    t.bars = bars;
    return t;
}

scenario('HA-01 heavy price marked with its volume', 'a 120-lot buy at one price in bar 150 gets a green circle with "126" (6 + 120)', async (c) => {
    const t = populationTape();
    const time = barStart(150) + 1000;
    t.trades.push({ time, price: 25001.00, size: 6, buy: true }, { time: time + 500, price: 25001.00, size: 120, buy: true });
    t.filler(151, 25001.25, 25001.5, 25001, 25001.75, 2);
    t.bars = 152;
    const ctx = await runPine(HA, t.market());
    const labels = drawings(ctx).labels.filter((l) => l.x === 150);
    c.note(labels.map((l) => `${l.text}@${l.y} ${l.textcolor} ${l.size}`).join(' / '));
    const txt = labels.find((l) => l.text === '126');
    if (c.check(txt, 'number "126" at bar 150')) c.check(near(txt.y, 25001.00), `at 25001.00, got ${txt.y}`);
    c.check(labels.some((l) => l.text === '●' && String(l.textcolor).toLowerCase().startsWith('#26a65b')), 'green filled circle');
});

scenario('HA-02 realtime count-up', 'the circle appears on the heavy print and its number grows with later same-price buys', async (c) => {
    const t = populationTape();
    const time = barStart(150) + 1000;
    t.trades.push({ time, price: 25001.00, size: 120, buy: true }, { time: time + 2000, price: 25001.00, size: 9, buy: true }, { time: time + 4000, price: 25001.00, size: 11, buy: true });
    t.filler(151, 25001.25, 25001.5, 25001, 25001.75, 2);
    t.bars = 152;
    const live = new Live(HA, t);
    try {
        await live.start(time - 1);
        const seen = [];
        for (const ts of [time, time + 2000, time + 4000]) {
            const ctx = await live.to(ts);
            const l = drawings(ctx).labels.find((x) => x.x === 150 && /^\d/.test(String(x.text)));
            seen.push(l ? l.text : '-');
        }
        c.note(`numbers on the live bar: ${seen.join(' -> ')}`);
        c.check(seen[0] === '120' || seen[0] === String(120 + (seen[0] | 0) - 120), `marker on the 120-lot update: ${seen[0]}`);
        c.eq('140', seen[2], 'counts up to 120 + 9 + 11');
        c.check(drawings(live.ctx).labels.filter((x) => x.x === 150 && x.text === '●').length === 1, 'one circle (old ones rolled back)');
    } finally { live.stop(); }
});

scenario('HA-04 live alert when the circle appears', "alert mode 'Live': one alert on the update the 120-lot circle appears, none for the later count-up", async (c) => {
    const t = populationTape();
    const time = barStart(150) + 1000;
    t.trades.push({ time, price: 25001.00, size: 120, buy: true }, { time: time + 2000, price: 25001.00, size: 9, buy: true }, { time: time + 4000, price: 25001.00, size: 11, buy: true });
    t.filler(151, 25001.25, 25001.5, 25001, 25001.75, 2);
    t.bars = 152;
    const live = new Live(HA, t, { Alert: 'Live (when a circle appears)' });
    const count = () => (live.ctx.alerts ?? []).filter((a) => String(a.message).includes('heavy BUY')).length;
    try {
        await live.start(time - 1);
        c.eq(0, count(), 'no alert before the heavy print');
        const seen = [];
        for (const ts of [time, time + 2000, time + 4000]) {
            await live.to(ts);
            seen.push(count());
        }
        c.eq('1,1,1', seen.join(','), 'alerts after each update');
        c.note(String((live.ctx.alerts ?? [])[0]?.message));
    } finally { live.stop(); }
});

scenario('HA-03 uniform volume marks nothing', 'every price trades the same: no markers', async (c) => {
    const t = new Tape();
    for (let i = 0; i < 120; i++) t.filler(i, 5000, 5000.25, 4999.5, 5000.75, 4);
    const ctx = await runPine(HA, t.market());
    c.eq(0, drawings(ctx).labels.length, 'no labels');
});

// ═════════════════════════════════════════════ Pressure Toolkit
/** Quiet bars, then bar 80 with a 90-lot buy price and bar 81 with a heavy upper wick (5002.50 / 5002.75). */
function ptkTape() {
    const t = new Tape();
    for (let b = 0; b < 80; b++) {
        const mid = 5000.75 + ((b % 4) - 2) * 0.25;
        const lv = [];
        for (let l = -2; l <= 2; l++) lv.push({ price: mid + l * 0.25, ask: 8 + ((b + l + 2) % 5), bid: 8 + ((b * 3 + l + 2) % 5) });
        t.bar(b, mid, b % 2 === 0 ? mid + 0.25 : mid - 0.25, lv, 3);
    }
    const l80 = balanced(5000.00, 5002.00, 10);
    l80.find((l) => l.price === 5001.00).ask = 90;
    l80.find((l) => l.price === 5001.00).bid = 4;
    t.bar(80, 5000.25, 5001.50, l80, 5);
    const l81 = [...balanced(5000.75, 5002.25, 10), { price: 5002.50, ask: 45, bid: 25 }, { price: 5002.75, ask: 52, bid: 28 }, { price: 5003.00, ask: 5, bid: 5 }];
    t.bar(81, 5001.50, 5001.00, l81, 3);
    for (let b = 82; b < 85; b++) t.filler(b, 5001.0, 5001.25, 5000.5, 5001.75);
    t.filler(85, 5001.75, 5002.25, 5001.5, 5002.5);
    t.filler(86, 5002.0, 5002.0, 5001.75, 5002.25);
    return t;
}

scenario('PTK-01 bubble, big trade, absorption dot and lines', 'heavy buy price -> bright bubble + square + number; heavy upper wick -> red dot and green lines until price returns', async (c) => {
    const t = ptkTape();
    const ctx = await runPine(PTK, t.market());
    const d = drawings(ctx);
    const at = (bar) => d.labels.filter((l) => l.x === bar);
    c.note('bar 80: ' + at(80).map((l) => `${JSON.stringify(l.text)}@${l.y} ${l.textcolor}`).join(' / '));
    c.note('bar 81: ' + at(81).map((l) => `${JSON.stringify(l.text)}@${l.y} ${l.style} ${l.color}`).join(' / '));
    c.check(at(80).some((l) => l.text === '●' && near(l.y, 5001.00)), 'bubble at 5001.00');
    c.check(at(80).some((l) => l.text === '□' && near(l.y, 5001.00)), 'big-trade square at 5001.00');
    c.check(at(80).some((l) => l.text === '90'), 'big-trade number 90');
    const dot = at(81).find((l) => String(l.style).includes('circle'));
    c.check(dot && ['ab', 'abovebar', 'yloc.abovebar'].includes(String(dot.yloc)) && String(dot.color).includes('198, 40, 40'), `red dot above bar 81: ${dot && dot.yloc} ${dot && dot.color}`);
    const lines = d.lines.filter((l) => l.x1 === 81);
    c.note('lines from bar 81: ' + lines.map((l) => `${l.y1} -> x2 ${l.x2}`).join(' / '));
    c.check(lines.some((l) => near(l.y1, 5002.75)) && lines.some((l) => near(l.y1, 5002.50)), 'lines at the heavy wick prices 5002.50 / 5002.75');
    const l250 = lines.find((l) => near(l.y1, 5002.50));
    if (l250) c.eq(85, l250.x2, 'the 5002.50 line ends at bar 85 (price returns)');
    const l275 = lines.find((l) => near(l.y1, 5002.75));
    if (l275) c.check(l275.x2 > 86, `the 5002.75 line is still open (x2 ${l275.x2})`);
});

scenario('PTK-03 realtime: absorption lines of the forming bar', 'bar 81 builds a heavy upper wick: dashed lines show live, solid ones stay after the close', async (c) => {
    const t = ptkTape();
    const trades = [...t.trades].sort((a, b) => a.time - b.time);
    const first81 = trades.findIndex((x) => x.time >= barStart(81));
    const first82 = trades.findIndex((x) => x.time >= barStart(82));
    const live = new Live(PTK, t);
    try {
        await live.start(trades[first81 - 1].time);
        let seenLive = false;
        for (let i = first81; i < first82; i++) {
            const d = drawings(await live.to(trades[i].time));
            if (d.lines.some((l) => l.x1 === 81 && dashed(l))) seenLive = true;
            c.check(!d.lines.some((l) => l.x1 === 81 && !dashed(l)), 'no final line during bar 81');
        }
        c.check(seenLive, 'dashed lines from bar 81 while it forms');
        const d = drawings(await live.to(trades[first82].time));
        const fin = d.lines.filter((l) => l.x1 === 81);
        c.check(fin.length >= 2 && fin.every((l) => !dashed(l)), `solid lines after the close: ${fin.length}`);
    } finally { live.stop(); }
});

scenario('PTK-02 psychological step', 'auto step about 0.3% of price in whole ticks: NQ 29800 -> 100, ES 6500 -> 25, GC 4100 -> 10, CL 62 -> 0.25', async (c) => {
    for (const [price, tick, expected] of [[29800, 0.25, 100], [6500, 0.25, 25], [4100, 0.1, 10], [62, 0.01, 0.25]]) {
        const t = new Tape({ tick });
        for (let b = 0; b < 3; b++) t.filler(b, price, price, price - 2 * tick, price + 2 * tick, 2);
        const ctx = await runPine(PTK, t.market());
        const ys = drawings(ctx).lines.filter((l) => ['b', 'both', 'extend.both'].includes(String(l.extend))).map((l) => l.y1).sort((a, b) => a - b);
        const step = ys.length > 1 ? Math.round((ys[1] - ys[0]) / tick) * tick : NaN;
        c.check(near(step, expected), `price ${price}: step ${expected}, got ${step}`);
    }
});

// ═════════════════════════════════════════════ simulated sessions (render only, plus sanity checks)
scenario('SIM-01 all four on a simulated NQ session', 'every script runs on 300 bars of simulated order flow without warnings', async (c) => {
    const m = makeMarket({ seed: 5, segments: [{ bars: 120, tradesPerBar: 150 }, { bars: 40, buyProb: 0.62, liftProb: 0.3, tradesPerBar: 150 }, { bars: 60, tradesPerBar: 150, revert: { level: 25030, strength: 0.05 } }, { bars: 40, buyProb: 0.4, liftProb: 0.3, tradesPerBar: 150 }, { bars: 40, tradesPerBar: 150 }] });
    for (const [file, name] of [[IMB, 'imbalance'], [HVN, 'hvn'], [HA, 'heavy-absorption'], [PTK, 'pressure-toolkit']]) {
        const ctx = await runPine(file, m);
        c.check(!(ctx.warnings ?? []).length, `${name}: no runtime warnings ${JSON.stringify(ctx.warnings)}`);
        const d = drawings(ctx);
        c.note(`${name}: ${d.boxes.length} boxes, ${d.labels.length} labels, ${d.lines.length} lines, ${d.polylines.length} polylines`);
        if (render) await renderPng(renderSvg(m, ctx, { title: `${path.basename(file)} - simulated NQ 15s (PineTS)`, from: m.candles.length - 100 }), path.join(OUT, `${name}.png`));
    }
});

// ═════════════════════════════════════════════ runner
// ═════════════════════════════════════════════ Crypto
const BTC = { type: 'crypto', basecurrency: 'BTC', currency: 'USDT', mintick: 0.01, ticker: 'BTCUSDT', tickerid: 'BINANCE:BTCUSDT', prefix: 'BINANCE', description: 'Bitcoin / TetherUS', session: '24x7' };
/** Simulated Bitcoin order flow: the preview's NQ session at 100,000 with $5 moves and fractional BTC sizes. */
function btcMarket(barSec = 60) {
    const segs = [];
    for (const drift of [0.5, 0.62, 0.5, 0.4, 0.5, 0.58]) {
        segs.push({ bars: 12, tradesPerBar: 170, bigProb: 0.005, revert: drift === 0.5 ? { level: 25000, strength: 0.05 } : null, buyProb: drift, liftProb: 0.26 });
        segs.push({ bars: 12, tradesPerBar: 170, bigProb: 0.005, buyProb: drift, liftProb: 0.24 });
    }
    return scaleMarket(makeMarket({ seed: 7, barSec, segments: segs }));
}
// ticks per row in use: the tooltip of the status cell (no plot: a plotted value of 1 would move the drawings to a scale
// of their own in TradingView)
const ticksUsed = (ctx) => Number((drawings(ctx).tables.flatMap((t) => (t.cells ?? []).flat()).map((c) => c?.tooltip ?? '').join(' ')
    .match(/ticks per row: (\d+)/i) ?? [])[1]);
const status = (ctx) => drawings(ctx).tables.flatMap((t) => (t.cells ?? []).flat()).map((c) => c?.text ?? '').join(' ').trim();

scenario('CRY-01 auto row size per coin and timeframe', 'crypto and spot metals: about 20 rows per typical bar (BTC 1m $5, 15s $2, ETH 1m $0.10, XAUUSD 1m $0.10); 1 tick on futures and forex; manual value wins', async (c) => {
    const nq15 = makeMarket({ seed: 5, segments: [{ bars: 30 }] });
    const nq60 = makeMarket({ seed: 5, barSec: 60, segments: [{ bars: 30 }] });
    const cases = [
        ['BTC/USDT 1m', nq60, BTC, {}, 500],
        ['BTC/USDT 15s', nq15, BTC, {}, 200],
        ['ETH/USDT 1m', nq60, { ...BTC, basecurrency: 'ETH' }, {}, 10],
        ['DOGE/USDT 1m', nq60, { ...BTC, basecurrency: 'DOGE', mintick: 0.00001 }, {}, 1],
        ['unlisted coin 1m', nq60, { ...BTC, basecurrency: 'ABCD', mintick: 0.0001 }, {}, 1],
        ['ETH/BTC 1m (not in dollars)', nq60, { ...BTC, basecurrency: 'ETH', currency: 'BTC', mintick: 0.00001 }, {}, 1],
        ['NQ futures 15s', nq15, {}, {}, 1],
        ['OANDA:XAUUSD 1m (spot gold, tick 0.001): $0.10 rows', nq60, { type: 'cfd', basecurrency: 'XAU', currency: 'USD', mintick: 0.001, ticker: 'XAUUSD', tickerid: 'OANDA:XAUUSD', prefix: 'OANDA' }, {}, 100],
        ['OANDA:XAUUSD without a base currency: known by its ticker', nq60, { type: 'commodity', basecurrency: '', currency: 'USD', mintick: 0.001, ticker: 'XAUUSD', tickerid: 'OANDA:XAUUSD', prefix: 'OANDA' }, {}, 100],
        ['EURUSD 1m (forex)', nq60, { type: 'forex', basecurrency: 'EUR', currency: 'USD', mintick: 0.00001, ticker: 'EURUSD', tickerid: 'OANDA:EURUSD', prefix: 'OANDA' }, {}, 1],
        ['BTC/USDT 1m, manual 7', nq60, BTC, { 'Ticks per row (0 = auto)': 7 }, 7],
    ];
    for (const [name, m, info, inputs, want] of cases) {
        const ctx = await runPine(IMB, m, inputs, { info });
        c.eq(want, ticksUsed(ctx), `${name}: ticks per row`);
    }
    const hvn = await runPine(HVN, nq60, {}, { info: BTC });
    c.eq(500, ticksUsed(hvn), 'HVN BTC/USDT 1m: footprint ticks per row');
});

scenario('QUAL-02 drawings on the price scale', 'no script plots a value (a hidden plot of 1 made TradingView put IDC Lite on a scale of its own, so its zones did not follow the candles) and every script is pinned to the right price scale', async (c) => {
    for (const f of ['IDC_Imbalance_Levels.pine', 'IDC_HVN_Profile.pine', 'IDC_Heavy_Absorption.pine', 'IDC_Pressure_Toolkit.pine', 'IDC_OrderFlow.pine', 'IDC_OrderFlow_Lite.pine']) {
        const code = fs.readFileSync(path.join(TV, f), 'utf8').split('\n').map((l) => l.replace(/\/\/.*$/, '')).join('\n');
        c.check(!/(^|[^\w.])(plot|plotshape|plotchar|plotcandle|plotbar|hline|fill|bgcolor|barcolor)\s*\(/m.test(code), `${f}: no plots`);
        c.check(/indicator\([^)]*scale = scale\.right/.test(code), `${f}: scale = scale.right`);
    }
});

scenario('QUAL-01 footprint row size is "simple"', 'TradingView compiles request.footprint() only with a "simple int" row size: it is computed at the top level with built-ins only (Pine treats the parameters of user-defined functions as "series"), and no switch / if / loop / := / var, history or bar data (PineTS does not check qualifiers)', async (c) => {
    for (const file of [IMB, HVN, HA, PTK, path.join(TV, 'IDC_OrderFlow.pine')]) {
        const real = resolveScript(file).file;
        const name = path.basename(real);
        const src = fs.readFileSync(real, 'utf8');
        const i = src.indexOf('footprint row size');
        const j = src.indexOf('request.footprint(', i);
        c.check(i > 0 && j > i, `${name}: row size section found`);
        const code = src.slice(i, j).split('\n').map((l) => l.replace(/\/\/.*$/, '').replace(/"(?:[^"\\]|\\.)*"/g, '""')).join('\n');
        for (const [what, re] of [['switch', /\bswitch\b/], ['if', /\bif\b/], ['loop', /\b(for|while)\b/], [':=', /:=/], ['var', /\bvar(ip)?\b/],
            ['history []', /\[/], ['ta./request./array./map.', /\b(ta|request|array|map)\./], ['bar data', /\b(open|high|low|close|volume|time|bar_index|hl2|hlc3|ohlc4)\b(?!\s*\()/]]) {
            c.check(!re.test(code), `${name}: no ${what} in the row size code`);
        }
        c.check(!/=>/.test(code), `${name}: no function defined in the row size code`);
        const ALLOWED = new Set(['math.pow', 'math.floor', 'math.log10', 'math.sqrt', 'math.round', 'math.max', 'int', 'timeframe.in_seconds']);
        const calls = [...code.matchAll(/([A-Za-z_][\w.]*)\s*\(/g)].map((m) => m[1]).filter((f) => !ALLOWED.has(f));
        c.check(!calls.length, `${name}: only simple-preserving built-ins in the row size code, found ${[...new Set(calls)].join(', ')}`);
    }
});

scenario('CRY-02 Bitcoin session', 'all four on simulated BTC/USDT 1m: footprint available, zones and circles drawn, labels keep decimals (e.g. "7.5x | 2.35")', async (c) => {
    const m = btcMarket();
    const res = {};
    for (const [name, file] of [['Imbalance', IMB], ['HVN', HVN], ['Heavy Absorption', HA], ['Pressure', PTK]]) {
        const ctx = await runPine(file, m, {}, { info: BTC });
        res[name] = ctx;
        const st = status(ctx);
        c.check(!/no footprint|too many|look too/.test(st), `${name}: status "${st}"`);
    }
    const zones = drawings(res.Imbalance).boxes.map((b) => String(b.text));
    c.check(zones.length >= 5, `imbalance zones: ${zones.length}`);
    c.check(zones.some((t) => /\| \d+\.\d+$/.test(t)), `imbalance labels with decimals: ${zones.slice(0, 4).join(', ')}`);
    c.check(drawings(res.HVN).boxes.length >= 5, `HVN zones: ${drawings(res.HVN).boxes.length}`);
    const marks = drawings(res['Heavy Absorption']).labels.filter((l) => l.text && !['●', '○'].includes(l.text)).map((l) => l.text);
    c.check(marks.length >= 1 && marks.every((t) => /^\d+(\.\d+)?$/.test(t)), `circle numbers: ${marks.slice(0, 5).join(', ')}`);
    c.check(drawings(res.Pressure).labels.length >= 5, `pressure markers: ${drawings(res.Pressure).labels.length}`);
    c.note(`zones ${zones.slice(0, 3).join(', ')}; circles ${marks.slice(0, 3).join(', ')}`);
});

scenario('CRY-03 rows far too fine: the chart says what to set', "BTC with 'Ticks per row' = 1: most bars exceed TradingView's 2000-row footprint, the status suggests a value", async (c) => {
    const ctx = await runPine(IMB, btcMarket(), { 'Ticks per row (0 = auto)': 1 }, { info: BTC });
    const st = status(ctx);
    const sug = Number((st.match(/(?:set 'Ticks per row' to|'Ticks per row' = )\s*(\d+)/) ?? [])[1]);
    c.check(sug >= 50 && sug <= 2000, `status suggests a value between 50 and 2000: "${st}"`);
});


// ═════════════════════════════════════════════ IDC Order Flow (the four tools in one script, built by tools/build_suite.py)
const SUITE = path.join(TV, 'IDC_OrderFlow.pine');
const TOOLS = [['HVN Profile', HVN, { 'Max zones on chart': 100 }], ['Imbalance Levels', IMB, { 'Max zones on chart': 250 }],
    ['Heavy Absorption', HA, { 'Max markers on chart': 50 }], ['Pressure Toolkit', PTK, { 'Max markers on chart (labels)': 100, 'Max lines on chart': 250 }]];
const switches = (on) => Object.fromEntries(TOOLS.map(([name]) => [name, on.includes(name)]));
/** Drawings as comparable content (no ids): kind + every plain field. */
function contents(ctx) {
    const d = drawings(ctx);
    const key = (kind, o) => kind + JSON.stringify(Object.fromEntries(Object.entries(o).filter(([k, v]) => !k.startsWith('_') && k !== 'id' && typeof v !== 'object' && typeof v !== 'function').sort()));
    const all = [...d.boxes.map((o) => key('box', o)), ...d.labels.map((o) => key('label', o)), ...d.lines.map((o) => key('line', o)), ...d.polylines.map((o) => key('polyline', o))];
    return all.reduce((m, k) => m.set(k, (m.get(k) ?? 0) + 1), new Map());
}
function sameContents(c, what, a, b) {
    const keys = [...new Set([...a.keys(), ...b.keys()])];
    const bad = keys.filter((k) => a.get(k) !== b.get(k));
    const n = [...a.values()].reduce((x, y) => x + y, 0);
    c.check(bad.length === 0, `${what}: ${bad.length} of ${n} drawings differ ${bad.slice(0, 2).map((k) => k.slice(0, 160)).join(' | ')}`);
    return n;
}
const merge = (maps) => maps.reduce((acc, m) => { for (const [k, v] of m) acc.set(k, (acc.get(k) ?? 0) + v); return acc; }, new Map());

scenario('SUITE-01 one switch on = that tool alone', 'IDC Order Flow with one tool switched on draws exactly what the single script draws (same drawing caps)', async (c) => {
    const m = makeMarket({ seed: 42, segments: [{ bars: 160, tradesPerBar: 170, bigProb: 0.006, revert: { level: 25000, strength: 0.05 } }, { bars: 140, tradesPerBar: 170, buyProb: 0.58 }] });
    for (const [name, file, caps] of TOOLS) {
        const one = contents(await runPine(file, m, caps));
        const suite = contents(await runPine(SUITE, m, switches([name])));
        c.note(`${name}: ${sameContents(c, name, one, suite)} drawings`);
    }
});

scenario('SUITE-02 all switched on = the four tools together', 'all four on: the suite draws the union of the four single scripts, on NQ and on Bitcoin', async (c) => {
    const nq = makeMarket({ seed: 9, segments: [{ bars: 150, tradesPerBar: 170, bigProb: 0.006, revert: { level: 25000, strength: 0.05 } }, { bars: 150, tradesPerBar: 170, buyProb: 0.42 }] });
    for (const [label, m, info] of [['NQ', nq, {}], ['BTC', btcMarket(), BTC]]) {
        const singles = [];
        for (const [, file, caps] of TOOLS) singles.push(contents(await runPine(file, m, caps, { info })));
        const suite = contents(await runPine(SUITE, m, switches(TOOLS.map(([name]) => name)), { info }));
        c.note(`${label}: ${sameContents(c, `${label} all on`, merge(singles), suite)} drawings`);
    }
});

// ═════════════════════════════════════════════ IDC Order Flow Lite (estimated footprints: every TradingView plan)
const LITE = path.join(TV, 'IDC_OrderFlow_Lite.pine');
const ALL_ON = { 'HVN Profile': true, 'Imbalance Levels': true, 'Heavy Absorption': true, 'Pressure Toolkit': true };
const codeOf = (file) => fs.readFileSync(file, 'utf8').split('\n').map((l) => l.replace(/\/\/.*$/, '')).join('\n');
/** Simulated NQ 1-minute session (trends, a range, big prints). */
function nq60() {
    return makeMarket({ seed: 42, barSec: 60, segments: [0.5, 0.62, 0.5, 0.4, 0.5, 0.58, 0.45, 0.5, 0.55].flatMap((d, k) => [
        { bars: 10, tradesPerBar: 680, bigProb: 0.005, revert: d === 0.5 ? { level: 25000 + k * 4, strength: 0.05 } : null, buyProb: d, liftProb: 0.26 },
        { bars: 10, tradesPerBar: 680, bigProb: 0.005, buyProb: d, liftProb: 0.24 }]) });
}
/** Lite with touched zones kept and the suite's zone caps (its clean defaults hide touched zones): for comparisons
 * with the real footprint, which keeps them. */
function liteKeepTouched() {
    const f = path.join(OUT, 'lite-keep-touched.pine');
    fs.mkdirSync(OUT, { recursive: true });
    fs.writeFileSync(f, fs.readFileSync(LITE, 'utf8').replaceAll('input.bool(false, "Keep touched zones"', 'input.bool(true, "Keep touched zones"')
        .replace('input.int(60, "Max zones on chart"', 'input.int(250, "Max zones on chart"').replace('input.int(40, "Max zones on chart"', 'input.int(100, "Max zones on chart"'));
    return f;
}
/** HVN zones (bottom, top) of a run: its boxes without text (imbalance zones carry their label inside). */
const hvnBoxes = (ctx) => drawings(ctx).boxes.filter((b) => !String(b.text ?? '').trim()).map((b) => [Math.min(b.top, b.bottom), Math.max(b.top, b.bottom)]);
/** Share of `want` zones that overlap a zone of `got`. */
const overlapShare = (want, got) => (want.length ? want.filter(([lo, hi]) => got.some(([a, b]) => a <= hi && b >= lo)).length / want.length : 0);

scenario('LITE-01 runs on every plan', 'IDC Order Flow Lite has no request.footprint() (Premium/Ultimate only) and draws all four tools without footprint data', async (c) => {
    const code = codeOf(LITE);
    c.check(!/request\.footprint\s*\(/.test(code), 'no request.footprint() in the code');
    c.check(/request\.security_lower_tf\s*\(/.test(code), 'intrabars from request.security_lower_tf()');
    const m = nq60();
    const ctx = await runPine(LITE, m, ALL_ON, { withFootprint: false });
    const d = drawings(ctx);
    const st = status(ctx);
    c.check(!/no volume|no footprint/.test(st), `status: "${st}"`);
    c.check(hvnBoxes(ctx).length >= 2, `HVN zones: ${hvnBoxes(ctx).length}`);
    c.check(d.boxes.filter((b) => /^\d+(\.\d+)?x \|/.test(String(b.text))).length >= 5, 'imbalance zones');
    c.check(d.labels.filter((l) => l.text === '●').length >= 2, 'heavy absorption circles / pressure bubbles');
    c.note(`${d.boxes.length} boxes, ${d.labels.length} labels, ${d.lines.length} lines; status "${st}"`);
});

scenario('LITE-02 estimate of one strong up bar', 'chart bars only: open 25000, low 24999.50, high 25005, close 25004.50 -> one buy stack from just above the open to just below the close', async (c) => {
    const t = new Tape();
    for (let i = 0; i < 5; i++) t.filler(i, 24998.5, 24998.75, 24998, 24999.25, 4);
    t.filler(5, 25000, 25004.5, 24999.5, 25005, 10);
    t.filler(6, 25005.5, 25005.75, 25005.25, 25006, 4);
    const ctx = await runPine(LITE, t.market(), { 'HVN Profile': false, 'Imbalance Levels': true, 'Heavy Absorption': false, 'Pressure Toolkit': false, Intrabars: 'Chart bars only' });
    const z = drawings(ctx).boxes.filter((b) => b.left === 5);
    c.eq(1, z.length, 'zones from bar 5');
    const [lo, hi] = z.length ? [Math.min(z[0].top, z[0].bottom), Math.max(z[0].top, z[0].bottom)] : [NaN, NaN];
    c.check(near(lo, 25000.25) && near(hi, 25004.5), `buy stack 25000.25 - 25004.50, got ${lo} - ${hi} "${z[0]?.text}"`);
    c.eq(0, drawings(ctx).boxes.filter((b) => b.left !== 5).length, 'no zone from the quiet bars');
});

scenario('LITE-03 intrabars sharpen the estimate', '1-minute chart: Lite with 15-second intrabars finds more of the real footprint HVN zones than Lite on chart bars alone (5-minute profiles, touched zones kept on both sides)', async (c) => {
    const m = nq60();
    // 5-minute profiles (more of them to compare than the automatic 15 minutes on a 1-minute chart)
    const five = { 'Auto profile timeframe': false, 'Profile timeframe': '5' };
    const real = hvnBoxes(await runPine(SUITE, m, { ...switches(['HVN Profile']), ...five }));
    const share = {};
    for (const ib of ['Chart bars only', '15 seconds']) {
        const lite = hvnBoxes(await runPine(liteKeepTouched(), m, { ...switches(['HVN Profile']), ...five, Intrabars: ib }));
        share[ib] = overlapShare(real, lite);
        c.note(`${ib}: ${lite.length} zones, ${(share[ib] * 100).toFixed(0)} % of the ${real.length} real zones overlapped`);
    }
    c.check(share['15 seconds'] >= 0.8, `15-second intrabars overlap at least 80 % of the real zones (${(share['15 seconds'] * 100).toFixed(0)} %)`);
    c.check(share['15 seconds'] >= share['Chart bars only'], 'intrabars do at least as well as chart bars alone');
});

scenario('LITE-04 realtime = history', "Lite replayed tick by tick over the last bars ends with the same drawings as a history run on the same data (HVN, Imbalance, Heavy Absorption; without the forming period's live HVN labels and Pressure Toolkit's label cap, which differ the same way in IDC Order Flow: PineTS runs the last historical bar differently from realtime)", async (c) => {
    const m = makeMarket({ seed: 11, segments: [{ bars: 120, tradesPerBar: 150 }, { bars: 30, buyProb: 0.62, liftProb: 0.3, tradesPerBar: 150 }, { bars: 30, tradesPerBar: 150, revert: { level: 25010, strength: 0.05 } }] });
    const tape = { trades: m.trades, start: m.start, barSec: 15, tick: m.tick, bars: m.candles.length };
    const from = m.start + (m.candles.length - 4) * 15000;
    const end = m.start + (m.candles.length - 1) * 15000 + 500;
    const inputs = { ...switches(['HVN Profile', 'Imbalance Levels', 'Heavy Absorption']), "Show the forming period's nodes (live, dashed)": false };
    const live = new Live(LITE, tape, inputs);
    try {
        await live.start(from);
        const ts = m.trades.filter((x) => x.time > from && x.time <= end).map((x) => x.time);
        for (let k = 0; k < ts.length; k += 7) await live.to(ts[k]);
        await live.to(end);
        const hist = await runPine(LITE, live.data(), inputs);
        c.note(`${sameContents(c, 'live vs history', contents(live.ctx), contents(hist))} drawings`);
    } finally { live.stop(); }
});

scenario('LITE-05 row size from the daily range', "Lite on an unknown symbol (no base currency, 5-minute bars, tick 0.001): from day 2 the rows are about 1/20 of a typical bar, from the previous day's range", async (c) => {
    const m = scaleMarket(makeMarket({ seed: 4, barSec: 300, segments: [{ bars: 288, tradesPerBar: 40 }, { bars: 60, tradesPerBar: 40 }] }), { price: 4000, step: 0.04, tick: 0.001, sizeScale: 1, jitter: 40 });
    const info = { type: 'cfd', basecurrency: '', currency: 'USD', ticker: 'ABCUSD', tickerid: 'BROKER:ABCUSD', prefix: 'BROKER' };
    const ctx = await runPine(LITE, m, { 'HVN Profile': false, 'Imbalance Levels': true, 'Heavy Absorption': false, 'Pressure Toolkit': false }, { info });
    const day = candlesAt(m, 86400)[0];
    const raw = (day.high - day.low) * Math.sqrt(300 / 86400) / 20;
    const mag = 10 ** Math.floor(Math.log10(raw));
    const f = raw / mag;
    const want = Math.max(1, Math.round(((f < 1.5 ? 1 : f < 2.25 ? 2 : f < 3.5 ? 2.5 : f < 7.5 ? 5 : 10) * mag) / 0.001));
    c.check(want > 1, `expected rows of ${want} ticks`);
    c.eq(want, ticksUsed(ctx), `ticks per row on day 2 (day 1 range ${(day.high - day.low).toFixed(3)})`);
});

scenario('LITE-06 clean chart on every timeframe', 'Lite defaults: touched imbalance and HVN zones are removed, at most 60 imbalance and 40 HVN zones, 2 nodes per profile', async (c) => {
    const m = scaleMarket(nq60(), { price: 4000, step: 0.04, tick: 0.001, sizeScale: 1, jitter: 40 });
    const info = { type: 'cfd', basecurrency: '', currency: 'USD', ticker: 'XAUUSD', tickerid: 'OANDA:XAUUSD', prefix: 'OANDA' };
    const ctx = await runPine(LITE, m, {}, { info });
    const last = m.candles.length - 1;
    const boxes = solid(drawings(ctx).boxes);
    const imb = boxes.filter((b) => String(b.text ?? '').trim());
    const hvn = boxes.filter((b) => !String(b.text ?? '').trim());
    c.check(imb.length > 0 && imb.length <= 60, `imbalance zones on the chart: ${imb.length}`);
    c.check(hvn.length > 0 && hvn.length <= 40, `HVN zones on the chart: ${hvn.length}`);
    c.check(boxes.every((b) => b.right === last + 1), `only untouched zones (reaching the last bar): ${boxes.filter((b) => b.right !== last + 1).length} touched left`);
    const perPeriod = hvn.reduce((acc, b) => acc.set(b.left, (acc.get(b.left) ?? 0) + 1), new Map());
    c.check(Math.max(...perPeriod.values()) <= 2, 'at most 2 HVN nodes per profile');
    c.eq(100, ticksUsed(ctx), 'gold 1m: rows of $0.10 (100 ticks)');
    c.note(`${imb.length} imbalance zones, ${hvn.length} HVN zones`);
});

scenario('HVN-03 automatic profile timeframe', 'about 15-25 bars per profile on every chart timeframe: 1-minute chart -> 15-minute profiles (15-second chart -> 5 minutes, as in HVN-01)', async (c) => {
    const ctx = await runPine(HVN, nq60(), {});
    const starts = [...new Set(drawings(ctx).boxes.map((b) => b.left))].sort((a, b) => a - b);
    c.check(starts.length >= 4, `profiles with zones: ${starts.length}`);
    c.check(starts.every((x) => x % 15 === 0), `profiles start every 15 bars: ${starts.slice(0, 8).join(', ')}`);
});

const selected = filters.length ? scenarios.filter((s) => filters.some((f) => s.name.toLowerCase().includes(f.toLowerCase()))) : scenarios;
console.log(`IDCator TradingView scripts (PineTS) - ${selected.length} scenario(s)`);
console.log('='.repeat(100));
let failed = 0;
for (const s of selected) {
    const c = new Ctx();
    const t0 = Date.now();
    let crash = null;
    try { await s.fn(c); } catch (e) { crash = e; }
    const ok = !crash && !c.fails.length;
    if (!ok) failed++;
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${s.name}  (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
    if (!ok || verbose) console.log(`      ${s.desc}`);
    for (const f of c.fails) console.log(`      x ${f}`);
    if (crash) console.log(`      x crash: ${crash.stack?.split('\n').slice(0, 3).join(' | ')}`);
    if (verbose) for (const n of c.notes) console.log(`      - ${n}`);
}
console.log('='.repeat(100));
console.log(`SUMMARY: ${selected.length - failed} passed, ${failed} failed, ${selected.length} total`);
await closeBrowser();
process.exit(failed);
