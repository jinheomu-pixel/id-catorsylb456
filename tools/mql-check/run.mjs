// IDCator MT5 indicators - behaviour tests in a simulated MT5 terminal (check.py --run).
//
//   cd tools/pine-test && npm install        # once: PineTS + Playwright, shared with the TradingView tests
//   node tools/mql-check/run.mjs [--verbose] [--render] [FILTER...]
//
// The indicators run tick by tick on simulated NQ order flow (exchange feed with buy/sell flags, and a CFD quote
// feed). --render also writes chart images to tools/mql-check/out/. Exit code = number of failed scenarios.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { simMarket, btcMarket, tradeTicks, quoteTicks, writeTicks } from './ticks.mjs';
import { compareAll } from './compare.mjs';
import { renderMt5 } from './render.mjs';
import { renderPng, closeBrowser } from '../pine-test/harness.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.join(here, 'out');
const CHECK = path.join(here, 'check.py');
const IND = ['IDC_Imbalance_Levels', 'IDC_Heavy_Absorption', 'IDC_HVN_Profile', 'IDC_Pressure_Toolkit'];

const args = process.argv.slice(2);
const verbose = args.includes('--verbose');
const render = args.includes('--render');
const filters = args.filter((a) => !a.startsWith('--'));

fs.mkdirSync(OUT, { recursive: true });
const market = simMarket();
const TRADES = path.join(OUT, 'nq-trades.csv'), QUOTES = path.join(OUT, 'nq-quotes.csv');
const trades = tradeTicks(market), quotes = quoteTicks(market);
writeTicks(TRADES, trades);
writeTicks(QUOTES, quotes);
const btc = btcMarket();
const BTC_TRADES = path.join(OUT, 'btc-trades.csv'), BTC_QUOTES = path.join(OUT, 'btc-quotes.csv');
writeTicks(BTC_TRADES, tradeTicks(btc));
writeTicks(BTC_QUOTES, quoteTicks(btc));
const t0 = market.candles[0].openTime / 1000;
const bar = (t) => Math.floor((t - t0) / 60);
const LIVE_FROM = 62000;                             // ticks before this are history when the indicator is attached
const liveBar = bar(Math.floor(trades[LIVE_FROM][0] / 1000));
const lastBar = market.candles.length - 1;

/** Runs one indicator in the simulated terminal; returns its JSON result. */
function sim(ind, ticks, tag, opts = []) {
    const out = path.join(OUT, `${ind}-${tag}.json`);
    const r = spawnSync('python3', [CHECK, '--run', ind + '.mq5', ticks, out, ...opts.map(String)], { encoding: 'utf8', maxBuffer: 1 << 26 });
    if (r.status !== 0) throw new Error(`${ind} ${tag}: exit ${r.status}\n${r.stderr}${r.stdout}`);
    return JSON.parse(fs.readFileSync(out, 'utf8'));
}
// the same drawings: compared by content, without the running number in the object name (two objects created in the
// same bar can get their numbers in another order live than from history)
const objKey = (o) => JSON.stringify({ ...o, name: o.name.replace(/\d+/g, '#') });
function sameObjects(c, what, a, b) {
    const count = (d) => d.objects.reduce((m, o) => m.set(objKey(o), (m.get(objKey(o)) ?? 0) + 1), new Map());
    const A = count(a), B = count(b);
    const bad = [...new Set([...A.keys(), ...B.keys()])].filter((k) => A.get(k) !== B.get(k));
    c.check(bad.length === 0, `${what}: ${bad.length} drawings differ (${bad.slice(0, 2).join(' | ')})`);
    return bad.length === 0;
}

const scenarios = [];
const scenario = (name, desc, fn) => scenarios.push({ name, desc, fn });
class Ctx {
    constructor() { this.fails = []; this.notes = []; }
    check(ok, what) { if (!ok) this.fails.push(what); return !!ok; }
    eq(expected, actual, what) { return this.check(expected === actual, `${what}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`); }
    note(s) { this.notes.push(s); }
}

const results = {};
const history = (ind) => (results[ind + '-hist'] ??= sim(ind, TRADES, 'hist', ['--quiet', 120]));
const liveRun = (ind) => (results[ind + '-live'] ??= sim(ind, TRADES, 'live', ['--hist', LIVE_FROM, '--quiet', 120]));

scenario('MT5-01 compiles', 'all four indicators pass the C++ compile check against the MQL5 API', async (c) => {
    const r = spawnSync('python3', [CHECK], { encoding: 'utf8' });
    c.eq(0, r.status, 'check.py exit code');
    c.note(r.stdout.trim().split('\n').pop());
});

scenario('MT5-02 realtime = history', `the last ${trades.length - LIVE_FROM} ticks arrive live one by one: the chart ends exactly as when all ticks are history`, async (c) => {
    for (const ind of IND) {
        const h = history(ind), l = liveRun(ind);
        c.check(h.objects.length > 0, `${ind} draws something`);
        sameObjects(c, ind, h, l);
        c.note(`${ind}: ${h.objects.length} objects, ${l.alerts.length} live alerts`);
    }
});

scenario('MT5-03 skipped OnCalculate calls', 'MT5 may hand over several ticks per OnCalculate: 37 ticks per call gives the same chart', async (c) => {
    for (const ind of IND) sameObjects(c, ind, history(ind), sim(ind, TRADES, 'batch', ['--hist', LIVE_FROM, '--batch', 37, '--quiet', 120]));
});

scenario('MT5-04 CFD quote feed', 'no trades, only bid/ask changes (CFD/forex): every indicator draws, realtime = history', async (c) => {
    const liveFrom = Math.floor(quotes.length * 0.7);
    for (const ind of IND) {
        const h = sim(ind, QUOTES, 'cfd-hist', ['--quiet', 120]);
        c.check(h.objects.length > 0, `${ind} draws on quotes`);
        sameObjects(c, ind, h, sim(ind, QUOTES, 'cfd-live', ['--hist', liveFrom, '--quiet', 120]));
        c.note(`${ind}: ${h.objects.length} objects`);
    }
});

scenario('MT5-05 same as TradingView', 'MT5 and the TradingView scripts (PineTS) on the same trades: identical zones, labels, markers and lines', async (c) => {
    const dir = path.join(OUT, 'compare');
    fs.mkdirSync(dir, { recursive: true });
    for (const ind of IND) fs.writeFileSync(path.join(dir, ind + '.json'), JSON.stringify(history(ind)));
    const lines = [];
    const failed = await compareAll(dir, (s) => lines.push(s));
    c.eq(0, failed, 'indicators that differ');
    for (const l of lines) (l.startsWith('SAME') ? c.note(l) : c.fails.push(l));
});

scenario('MT5-06 touch alert on the touching tick', 'every imbalance zone touched while live alerts once, at the first trade inside it', async (c) => {
    const l = liveRun('IDC_Imbalance_Levels');
    const by = new Map(l.objects.map((o) => [o.name, o]));
    let checked = 0;
    for (const z of l.objects.filter((o) => o.name.startsWith('IDCimb_z'))) {
        const end = bar(z.t2) - 1;
        if (end < liveBar + 1 || end >= lastBar) continue;   // touched while live (the last bar is ambiguous)
        const bottom = z.p2, top = z.p1 - market.tick;
        const range = `${bottom.toFixed(2)}-${top.toFixed(2)}`;
        const tick = trades.find((k, i) => i >= LIVE_FROM && bar(Math.floor(k[0] / 1000)) === end && k[5] & 8 && k[3] >= bottom - 1e-9 && k[3] <= top + 1e-9);
        const alerts = l.alerts.filter((a) => a.msg.includes(range) && a.msg.includes(by.get(z.name.replace('_z', '_t'))?.text ?? '?'));
        c.eq(1, alerts.length, `alerts for zone ${range}`);
        if (tick && alerts.length) c.eq(Math.floor(tick[0] / 1000), alerts[0].wall, `zone ${range}: alert second = first trade inside it`);
        checked++;
    }
    c.check(checked >= 3, `zones touched while live: ${checked}`);
    c.note(`${checked} zones touched live, ${l.alerts.length} alerts`);
});

scenario('MT5-07 circle number counts up live', 'a heavy-absorption circle appears while its bar is forming and its number only grows until the close', async (c) => {
    const final = liveRun('IDC_Heavy_Absorption');
    const marks = final.objects.filter((o) => o.name.startsWith('IDCha_t') && bar(o.t1) > liveBar && bar(o.t1) < lastBar);
    c.check(marks.length >= 1, `markers in fully live bars: ${marks.length}`);
    for (const m of marks.slice(0, 2)) {
        const b = bar(m.t1);
        const idx = [];
        trades.forEach((k, i) => { if (i >= LIVE_FROM && bar(Math.floor(k[0] / 1000)) === b) idx.push(i); });
        const snap = sim('IDC_Heavy_Absorption', TRADES, 'snap', ['--hist', LIVE_FROM, '--snap', idx.join(',')]);
        const seq = snap.snapshots.map((s) => s.objects.find((o) => o.name.startsWith('IDCha_t') && bar(o.t1) === b && o.p1 === m.p1)?.text);
        const firstSeen = seq.findIndex((v) => v !== undefined);
        const nums = seq.filter((v) => v !== undefined).map((v) => parseFloat(v) * (/K$/.test(v) ? 1000 : 1));
        c.check(firstSeen >= 0 && firstSeen < seq.length - 1, `bar ${b}: circle appears before the bar's last tick (at tick ${firstSeen + 1} of ${seq.length})`);
        c.check(nums.every((v, i) => i === 0 || v >= nums[i - 1]), `bar ${b}: number never decreases`);
        c.check(new Set(nums).size >= 2, `bar ${b}: number grows live (${[...new Set(nums)].join(' -> ')})`);
        c.eq(m.text, seq[seq.length - 1], `bar ${b}: last live value = final value`);
        c.note(`bar ${b} @ ${m.p1}: ${[...new Set(seq.filter(Boolean))].join(' -> ')}`);
    }
});

scenario('MT5-08 quiet market closes the bar by timer', 'no next trade: the last bar (end of an M5 period) closes 30 s after its end, its HVN zones appear', async (c) => {
    const startOfLastPeriod = lastBar - (lastBar % 5);
    const zonesFrom = (d) => d.objects.filter((o) => o.name.startsWith('IDChvn_z') && bar(o.t1) === startOfLastPeriod).length;
    const early = sim('IDC_HVN_Profile', TRADES, 'quiet20', ['--quiet', 20]);
    const late = sim('IDC_HVN_Profile', TRADES, 'quiet120', ['--quiet', 120]);
    c.eq(0, zonesFrom(early), '20 s after the last trade (bar not over + 30 s yet): zones of the last period');
    c.check(zonesFrom(late) >= 1, `120 s after: zones of the last period appear (${zonesFrom(late)})`);
});

// ───────────────── Bitcoin (100,000, tick 0.01, fractional sizes): the symbol's tick goes to the simulated terminal
const BTC_INFO = { type: 'crypto', basecurrency: 'BTC', currency: 'USDT', mintick: 0.01, ticker: 'BTCUSDT', tickerid: 'BINANCE:BTCUSDT', prefix: 'BINANCE', session: '24x7' };

scenario('MT5-09 Bitcoin with the automatic row size', 'BTC exchange feed and BTC CFD quotes: rows sized from the bars (a few dollars), labels keep decimals, realtime = history', async (c) => {
    const btcLive = Math.floor(tradeTicks(btc).length * 0.7);
    for (const ind of IND) {
        const h = sim(ind, BTC_TRADES, 'btc-hist', ['--tick', 0.01, '--quiet', 120]);
        c.check(h.objects.length > 0, `${ind} draws on BTC`);
        sameObjects(c, `${ind} BTC`, h, sim(ind, BTC_TRADES, 'btc-live', ['--tick', 0.01, '--hist', btcLive, '--quiet', 120]));
        const q = sim(ind, BTC_QUOTES, 'btc-cfd', ['--tick', 0.01, '--quiet', 120]);
        c.check(q.objects.length > 0, `${ind} draws on BTC CFD quotes`);
    }
    // row size: imbalance zones are whole rows; the smallest (3-row) stack gives the row
    const z = results['btc-imb'] = sim('IDC_Imbalance_Levels', BTC_TRADES, 'btc-hist', ['--tick', 0.01, '--quiet', 120]);
    const heights = z.objects.filter((o) => o.name.startsWith('IDCimb_z')).map((o) => +(o.p1 - o.p2).toFixed(2));
    const row = Math.min(...heights) / 3;
    c.check(row >= 1 && row <= 10, `row size $${row} (about 20 rows per median bar)`);
    c.check(heights.every((h) => Math.abs(h / row - Math.round(h / row)) < 1e-6), 'every zone is a whole number of rows');
    const labels = z.objects.filter((o) => o.name.startsWith('IDCimb_t')).map((o) => o.text);
    c.check(labels.some((t) => /\| \d+\.\d+$/.test(t)), `labels keep decimals: ${labels.slice(0, 4).join(', ')}`);
    c.note(`row $${row}; zones ${labels.slice(0, 3).join(', ')}`);
});

scenario('MT5-10 Bitcoin: same as TradingView', 'both forced to $5 rows (500 ticks) on the same BTC trades: identical zones, labels, markers and lines', async (c) => {
    const dir = path.join(OUT, 'compare-btc');
    fs.mkdirSync(dir, { recursive: true });
    for (const ind of IND) {
        const rows = ind === 'IDC_HVN_Profile' ? ['--set', 'InpHvnRowPoints=500', '--set', 'InpHvnBasePoints=500'] : ['--set', 'InpRowPoints=500'];
        fs.writeFileSync(path.join(dir, ind + '.json'), JSON.stringify(sim(ind, BTC_TRADES, 'btc-500', ['--tick', 0.01, '--quiet', 120, ...rows])));
    }
    const t500 = { 'Ticks per row (0 = auto)': 500 };
    const inputs = { 'IDC_Imbalance_Levels.pine': t500, 'IDC_Heavy_Absorption.pine': t500, 'IDC_Pressure_Toolkit.pine': t500, 'IDC_HVN_Profile.pine': { 'Row size (ticks, 0 = auto)': 500 } };
    const lines = [];
    const failed = await compareAll(dir, (s) => lines.push(s), { market: btc, info: BTC_INFO, inputs });
    c.eq(0, failed, 'indicators that differ');
    for (const l of lines) (l.startsWith('SAME') ? c.note(l) : c.fails.push(l));
});

// ───────────────── IDC Order Flow: the four tools in one indicator, one tick feed
const SUITE_ALL = ['--set', 'InpOnPtk=true'];
function union(runs) {
    return { bars: runs[0].bars, period: runs[0].period, objects: runs.flatMap((r) => r.objects), alerts: runs.flatMap((r) => r.alerts) };
}

scenario('MT5-11 IDC Order Flow = the four tools together', 'all four switched on: exactly the objects of the four single indicators, on NQ and on Bitcoin', async (c) => {
    for (const [label, ticks, extra] of [['NQ', TRADES, []], ['BTC', BTC_TRADES, ['--tick', 0.01]]]) {
        const singles = IND.map((ind) => sim(ind, ticks, label + '-single', [...extra, '--quiet', 120]));
        const suite = sim('IDC_OrderFlow', ticks, label + '-suite', [...extra, '--quiet', 120, ...SUITE_ALL]);
        sameObjects(c, `${label} suite`, union(singles), suite);
        c.note(`${label}: ${suite.objects.length} objects`);
    }
});

scenario('MT5-12 IDC Order Flow realtime = history', 'all four on, the last ticks arrive live one by one: same chart as from history', async (c) => {
    const h = sim('IDC_OrderFlow', TRADES, 'suite-hist', ['--quiet', 120, ...SUITE_ALL]);
    const l = sim('IDC_OrderFlow', TRADES, 'suite-live', ['--hist', LIVE_FROM, '--quiet', 120, ...SUITE_ALL]);
    sameObjects(c, 'suite', h, l);
    c.note(`${h.objects.length} objects, ${l.alerts.length} live alerts`);
});

const ticksOfBar = (b) => trades.reduce((a, k, i) => (i >= LIVE_FROM && bar(Math.floor(k[0] / 1000)) === b ? [...a, i] : a), []);
// the bar closes on the next bar's first trade (a quote change before it does not close it)
const firstTickOfBar = (b) => trades.findIndex((k, i) => i >= LIVE_FROM && bar(Math.floor(k[0] / 1000)) === b && (k[5] & 8) !== 0);

scenario('MT5-13 live signals of the forming bar', 'imbalance stacks, HVN nodes and absorption lines show dashed while the bar (period) forms and become final at its close', async (c) => {
    // imbalance: a bar that leaves a zone
    const imbFinal = liveRun('IDC_Imbalance_Levels');
    const zb = imbFinal.objects.filter((o) => o.name.startsWith('IDCimb_z')).map((o) => bar(o.t1)).find((b) => b > liveBar && b < lastBar - 1);
    if (c.check(zb !== undefined, 'a zone from a live bar')) {
        const idx = ticksOfBar(zb);
        const snap = sim('IDC_Imbalance_Levels', TRADES, 'snap-imb', ['--hist', LIVE_FROM, '--snap', [...idx, firstTickOfBar(zb + 1)].join(',')]).snapshots;
        const during = snap.slice(0, -1), after = snap[snap.length - 1];
        const liveAt = during.findIndex((s) => s.objects.some((o) => o.name.startsWith('IDCimb_L') && o.name.endsWith('f') && o.style === 1));
        c.check(liveAt >= 0 && liveAt < during.length - 1, `dashed live box during bar ${zb} (from tick ${liveAt + 1} of ${during.length})`);
        c.check(during.every((s) => !s.objects.some((o) => o.name.startsWith('IDCimb_z') && bar(o.t1) === zb)), 'no final zone before the close');
        c.check(after.objects.some((o) => o.name.startsWith('IDCimb_z') && bar(o.t1) === zb), 'final zone after the close');
        c.check(!after.objects.some((o) => o.name.startsWith('IDCimb_L') && bar(o.t1) === zb), 'live box of the closed bar removed');
    }
    // HVN: the middle of a live period
    const pb = liveBar + 6 - (liveBar % 5) + 2;
    const hv = sim('IDC_HVN_Profile', TRADES, 'snap-hvn', ['--hist', LIVE_FROM, '--snap', ticksOfBar(pb)[5]]).snapshots[0].objects;
    const frames = hv.filter((o) => o.name.startsWith('IDChvn_L') && o.name.endsWith('f'));
    c.check(frames.length >= 1 && frames.every((o) => o.style === 1 && bar(o.t1) === pb - (pb % 5)), `dashed nodes of the forming period from its first bar: ${frames.length}`);
    c.check(!liveRun('IDC_HVN_Profile').objects.some((o) => o.name.startsWith('IDChvn_L')), 'no live nodes left at the end');
    // absorption lines: a bar that leaves lines
    const ptkFinal = liveRun('IDC_Pressure_Toolkit');
    const lb = ptkFinal.objects.filter((o) => o.type === 'trend' && /^IDCptk_L\d+$/.test(o.name)).map((o) => bar(o.t1)).find((b) => b > liveBar && b < lastBar - 1);
    if (c.check(lb !== undefined, 'absorption lines from a live bar')) {
        const snap = sim('IDC_Pressure_Toolkit', TRADES, 'snap-ptk', ['--hist', LIVE_FROM, '--snap', [...ticksOfBar(lb), firstTickOfBar(lb + 1)].join(',')]).snapshots;
        const isLive = (o) => o.type === 'trend' && o.name.startsWith(`IDCptk_b${lb}_L`) && o.style === 1;
        c.check(snap.slice(0, -1).some((s) => s.objects.some(isLive)), `dashed lines while bar ${lb} forms`);
        const after = snap[snap.length - 1].objects;
        c.check(!after.some(isLive) && after.some((o) => /^IDCptk_L\d+$/.test(o.name) && bar(o.t1) === lb && o.style === 0), 'solid lines after the close');
    }
});

scenario('MT5-14 Heavy Absorption live alert', "alert mode 'Live': an alert inside the bar a circle appears, at most once per side and bar", async (c) => {
    const r = sim('IDC_Heavy_Absorption', TRADES, 'ha-live-alert', ['--hist', LIVE_FROM, '--quiet', 120, '--set', 'InpHaAlert=1']);
    const marks = r.objects.filter((o) => o.name.startsWith('IDCha_t') && bar(o.t1) > liveBar);
    const al = r.alerts.filter((a) => /heavy (BUY|SELL)/.test(a.msg));
    c.check(marks.length >= 1 && al.length >= 1, `markers in live bars ${marks.length}, alerts ${al.length}`);
    for (const m of marks) {
        const b = bar(m.t1), t = t0 + b * 60;
        const inBar = al.filter((a) => a.wall >= t && a.wall < t + 60);
        c.check(inBar.length >= 1 && inBar.length <= 2, `bar ${b}: ${inBar.length} alert(s) during the bar`);
    }
    c.note(al.slice(0, 2).map((a) => a.msg).join(' / '));
});

let failed = 0;
for (const s of scenarios) {
    if (filters.length && !filters.some((f) => s.name.includes(f))) continue;
    const c = new Ctx();
    const t = Date.now();
    try { await s.fn(c); } catch (e) { c.fails.push(String(e.stack ?? e)); }
    const ok = c.fails.length === 0;
    if (!ok) failed++;
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${s.name}  (${((Date.now() - t) / 1000).toFixed(1)} s)`);
    if (verbose || !ok) console.log(`      ${s.desc}`);
    for (const f of c.fails) console.log(`      x ${f}`);
    if (verbose) for (const n of c.notes) console.log(`      - ${n}`);
}
if (render) {
    const live = Object.fromEntries(IND.map((ind) => [ind, liveRun(ind)]));
    await renderPng(renderMt5([live.IDC_HVN_Profile, live.IDC_Imbalance_Levels, live.IDC_Heavy_Absorption],
        { title: 'NQ M1 - IDC HVN Profile + Imbalance Levels + Heavy Absorption (MT5, simulated data and terminal)' }), path.join(OUT, 'mt5-preview.png'));
    await renderPng(renderMt5([live.IDC_Pressure_Toolkit], { title: 'NQ M1 - IDC Pressure Toolkit (MT5, simulated data and terminal)' }), path.join(OUT, 'mt5-preview-ptk.png'));
    await closeBrowser();
    console.log(`images: ${path.join(OUT, 'mt5-preview.png')}, mt5-preview-ptk.png`);
}
console.log('='.repeat(100));
console.log(`SUMMARY: ${scenarios.length - failed} passed, ${failed} failed, ${scenarios.length} total`);
process.exit(failed);
