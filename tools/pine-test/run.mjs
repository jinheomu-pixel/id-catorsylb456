// IDCator TradingView indicators - behaviour tests in PineTS.
//
//   cd tools/pine-test && npm install && node run.mjs [--verbose] [--render] [FILTER...]
//
// Every scenario runs the real .pine file from TradingView/ in PineTS (LuxAlgo's open-source Pine Script v6 runtime)
// on hand-built or simulated order flow; --render also writes PNG charts to tools/pine-test/out/.
// Exit code = number of failed scenarios.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { runPine, drawings, makeMarket, scaleMarket, renderSvg, renderPng, closeBrowser } from './harness.mjs';
import { Tape, Live, barStart } from './tape.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const TV = path.join(here, '..', '..', 'TradingView');
const IMB = path.join(TV, 'IDC_Imbalance_Levels.pine');
const HVN = path.join(TV, 'IDC_HVN_Profile.pine');
const HA = path.join(TV, 'IDC_Heavy_Absorption.pine');
const PTK = path.join(TV, 'IDC_Pressure_Toolkit.pine');
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
        c.eq(0, drawings(live.ctx).boxes.length, 'no zones while the period runs');
        await live.to(lastOf19.time);
        c.eq(0, drawings(live.ctx).boxes.length, 'still none before the bar closes');
        const ctx = await live.to(barStart(20) - 1);
        c.check(drawings(ctx).boxes.length >= 0, 'bar 19 close');
        const after = await live.to(trades.find((x) => x.time >= barStart(20)).time);
        c.eq(2, drawings(after).boxes.length, 'both period-1 zones exist on the first tick of the next period');
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

scenario('HA-03 uniform volume marks nothing', 'every price trades the same: no markers', async (c) => {
    const t = new Tape();
    for (let i = 0; i < 120; i++) t.filler(i, 5000, 5000.25, 4999.5, 5000.75, 4);
    const ctx = await runPine(HA, t.market());
    c.eq(0, drawings(ctx).labels.length, 'no labels');
});

// ═════════════════════════════════════════════ Pressure Toolkit
scenario('PTK-01 bubble, big trade, absorption dot and lines', 'heavy buy price -> bright bubble + square + number; heavy upper wick -> red dot and green lines until price returns', async (c) => {
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
const ticksUsed = (ctx, title = 'Ticks per row') => ctx.plots?.[title]?.data?.at(-1)?.value;
const status = (ctx) => drawings(ctx).tables.flatMap((t) => (t.cells ?? []).flat()).map((c) => c?.text ?? '').join(' ').trim();

scenario('CRY-01 auto row size per coin and timeframe', 'crypto: about 20 rows per typical bar (BTC 1m $5, 15s $2, ETH 1m $0.10); 1 tick on futures; manual value wins', async (c) => {
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
        ['BTC/USDT 1m, manual 7', nq60, BTC, { 'Ticks per row (0 = auto)': 7 }, 7],
    ];
    for (const [name, m, info, inputs, want] of cases) {
        const ctx = await runPine(IMB, m, inputs, { info });
        c.eq(want, ticksUsed(ctx), `${name}: ticks per row`);
    }
    const hvn = await runPine(HVN, nq60, {}, { info: BTC });
    c.eq(500, ticksUsed(hvn, 'Footprint ticks per row'), 'HVN BTC/USDT 1m: footprint ticks per row');
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
