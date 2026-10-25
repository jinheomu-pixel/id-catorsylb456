// Writes the parity fixtures of the Python version (python/tests/fixtures): simulated order flow (trades, candles) and
// what the TradingView scripts draw on it in PineTS. The Python tests run the same data through idcator and compare.
//
//   cd tools/pine-test && node export.mjs
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { makeMarket, scaleMarket, runPine, drawings, candlesAt, closeBrowser } from './harness.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const TV = path.join(here, '..', '..', 'TradingView');
const OUT = path.join(here, '..', '..', 'python', 'tests', 'fixtures');
fs.mkdirSync(OUT, { recursive: true });

const BTC = { type: 'crypto', basecurrency: 'BTC', currency: 'USDT', mintick: 0.01, ticker: 'BTCUSDT', tickerid: 'BINANCE:BTCUSDT', prefix: 'BINANCE', session: '24x7' };
// drawing caps at their maximum, so nothing is trimmed (Python keeps everything)
const CAPS = {
    'IDC_Imbalance_Levels.pine': { 'Max zones on chart': 500 },
    'IDC_HVN_Profile.pine': { 'Max zones on chart': 240 },
    'IDC_Heavy_Absorption.pine': { 'Max markers on chart': 166 },
    'IDC_Pressure_Toolkit.pine': { 'Max markers on chart (labels)': 480, 'Max lines on chart': 440 },
};

function session(seed, barSec, bars, tradesPerBar) {
    const segs = [];
    const per = Math.round(bars / 12);
    for (const d of [0.5, 0.62, 0.5, 0.4, 0.5, 0.58]) {
        segs.push({ bars: per, tradesPerBar, bigProb: 0.005, revert: d === 0.5 ? { level: 25000, strength: 0.05 } : null, buyProb: d, liftProb: 0.26 });
        segs.push({ bars: per, tradesPerBar, bigProb: 0.005, buyProb: d, liftProb: 0.24 });
    }
    return makeMarket({ seed, barSec, segments: segs });
}

const plain = (o) => Object.fromEntries(Object.entries(o).filter(([k, v]) => !k.startsWith('_') && k !== 'id' && typeof v !== 'object' && typeof v !== 'function'));
function dump(ctx) {
    const d = drawings(ctx);
    const ticks = Number((d.tables.flatMap((t) => (t.cells ?? []).flat()).map((c) => c?.tooltip ?? '').join(' ').match(/ticks per row: (\d+)/i) ?? [])[1]);
    return { ticksPerRow: ticks, boxes: d.boxes.map(plain), labels: d.labels.map(plain), lines: d.lines.map(plain) };
}

const gz = (file, text) => fs.writeFileSync(path.join(OUT, file), zlib.gzipSync(text, { level: 9 }));
const tradesCsv = (m) => 'time,price,size,side\n' + m.trades.map((t) => `${t.time},${t.price},${t.size},${t.buy ? 1 : -1}`).join('\n') + '\n';
const candlesCsv = (cs) => 'time,open,high,low,close,volume\n' + cs.map((c) => `${c.openTime},${c.open},${c.high},${c.low},${c.close},${c.volume}`).join('\n') + '\n';

const cases = [
    { name: 'nq15', m: session(21, 15, 192, 150), info: {} },
    { name: 'btc60', m: scaleMarket(session(7, 60, 144, 170), { jitter: 40 }), info: BTC },
];
for (const { name, m, info } of cases) {
    gz(`${name}.trades.csv.gz`, tradesCsv(m));
    const out = { tick: m.tick, barSec: m.barSec, bars: m.candles.length, scripts: {} };
    for (const [file, caps] of Object.entries(CAPS)) {
        out.scripts[file] = dump(await runPine(path.join(TV, file), m, caps, { info }));
        console.log(name, file, out.scripts[file].boxes.length, 'boxes', out.scripts[file].labels.length, 'labels', out.scripts[file].lines.length, 'lines');
    }
    gz(`${name}.pine.json.gz`, JSON.stringify(out));
}

// IDC Order Flow Lite: chart candles (and 15-second intrabars) of a 1-minute NQ session
const m = session(42, 60, 120, 680);
gz('lite60.candles.csv.gz', candlesCsv(m.candles));
gz('lite60.intrabars15.csv.gz', candlesCsv(candlesAt(m, 15)));
const lite = { tick: m.tick, barSec: 60, bars: m.candles.length, runs: {} };
// (the suite's drawing caps apply: the Python tests check that everything Lite drew is in Python's result)
const allOn = { 'HVN Profile': true, 'Imbalance Levels': true, 'Heavy Absorption': true, 'Pressure Toolkit': true, 'Ticks per row (0 = auto)': 1 };
// Lite's clean defaults hide touched zones; the comparison keeps them (and the suite's caps), for more to compare
const liteSrc = fs.readFileSync(path.join(TV, 'IDC_OrderFlow_Lite.pine'), 'utf8')
    .replaceAll('input.bool(false, "Keep touched zones"', 'input.bool(true, "Keep touched zones"')
    .replace('input.int(20, "Max zones on chart"', 'input.int(250, "Max zones on chart"')
    .replace('input.int(12, "Max zones on chart"', 'input.int(100, "Max zones on chart"')
    .replace('input.int(1, "Max clusters per period"', 'input.int(3, "Max clusters per period"');
const liteFile = path.join(here, 'out', 'lite-keep-touched.pine');
fs.mkdirSync(path.dirname(liteFile), { recursive: true });
fs.writeFileSync(liteFile, liteSrc);
for (const ib of ['Chart bars only', '15 seconds']) {
    lite.runs[ib] = dump(await runPine(liteFile, m, { ...allOn, Intrabars: ib }));
    console.log('lite60', ib, lite.runs[ib].boxes.length, 'boxes', lite.runs[ib].labels.length, 'labels');
}
gz('lite60.pine.json.gz', JSON.stringify(lite));
await closeBrowser();
process.exit(0);
