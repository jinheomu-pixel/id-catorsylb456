// Tick files for the MT5 simulation (check.py --run), from the same simulated NQ order flow as the TradingView preview.
//
//   node tools/mql-check/ticks.mjs OUT_DIR
//
// Writes OUT_DIR/nq-trades.csv (exchange feed: trades with buy/sell flags plus quote changes, as for CME futures)
// and OUT_DIR/nq-quotes.csv (CFD feed: bid/ask changes only), plus the same as Bitcoin: btc-trades.csv, btc-quotes.csv.
// Columns: time_msc,bid,ask,last,volume,flags.
import fs from 'node:fs';
import path from 'node:path';
import { makeMarket, scaleMarket } from '../pine-test/harness.mjs';

const BID = 2, ASK = 4, LAST = 8, VOLUME = 16, BUY = 32, SELL = 64;

export function simMarket({ seed = 42, barSec = 60, tradesPerBar = 420 } = {}) {
    const segs = [];
    let lvl = 25000;
    for (let p = 0; p < 9; p++) {
        const drift = [0.5, 0.62, 0.5, 0.4, 0.5, 0.58, 0.45, 0.5, 0.55][p];
        segs.push({ bars: 10, tradesPerBar, bigProb: 0.005, revert: drift === 0.5 ? { level: lvl, strength: 0.05 } : null, buyProb: drift, liftProb: 0.2 });
        segs.push({ bars: 10, tradesPerBar, bigProb: 0.005, buyProb: drift, liftProb: 0.19 });
        lvl += (drift - 0.5) * 60;
    }
    return makeMarket({ seed, barSec, segments: segs });
}

/** The same order flow as Bitcoin: 100,000 with $5 moves (trades spread over the step's 500 ticks), sizes in BTC. */
export function btcMarket() {
    return scaleMarket(simMarket(), { price: 100000, step: 5, tick: 0.01, sizeScale: 0.01, jitter: 500 });
}

/** Exchange feed: every trade (Last + Buy/Sell flag) and every change of the inside quote. */
export function tradeTicks(m) {
    const out = [];
    let bid = 0;
    for (const t of m.trades) {
        const b = t.buy ? t.price - m.tick : t.price;
        if (b !== bid) {
            bid = b;
            out.push([t.time, b, b + m.tick, 0, 0, BID | ASK]);
        }
        out.push([t.time, b, b + m.tick, t.price, t.size, LAST | VOLUME | (t.buy ? BUY : SELL)]);
    }
    return out;
}

/** CFD feed: bid/ask changes only, no volume. */
export function quoteTicks(m) {
    const out = [];
    let bid = 0;
    for (const t of m.trades) {
        const b = t.buy ? t.price - m.tick : t.price;
        if (b === bid) continue;
        bid = b;
        out.push([t.time, b, b + m.tick, 0, 0, BID | ASK]);
    }
    return out;
}

export function writeTicks(file, ticks) {
    // prices and sizes without floating-point noise (100004.99, not 100004.98999999999)
    const fix = (v, i) => (i >= 1 && i <= 4 ? Number(v.toFixed(8)) : v);
    fs.writeFileSync(file, 'time_msc,bid,ask,last,volume,flags\n' + ticks.map((k) => k.map(fix).join(',')).join('\n') + '\n');
}

if (import.meta.url === `file://${process.argv[1]}`) {
    const dir = process.argv[2] ?? '.';
    fs.mkdirSync(dir, { recursive: true });
    const m = simMarket();
    const tr = tradeTicks(m), qu = quoteTicks(m);
    writeTicks(path.join(dir, 'nq-trades.csv'), tr);
    writeTicks(path.join(dir, 'nq-quotes.csv'), qu);
    const b = btcMarket();
    writeTicks(path.join(dir, 'btc-trades.csv'), tradeTicks(b));
    writeTicks(path.join(dir, 'btc-quotes.csv'), quoteTicks(b));
    console.log(`${m.candles.length} bars, ${tr.length} exchange ticks, ${qu.length} CFD ticks (NQ and BTC) -> ${dir}`);
}
