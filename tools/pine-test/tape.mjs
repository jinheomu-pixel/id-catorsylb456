// Hand-built order flow for deterministic scenarios, and a live (tick-by-tick) runner.
import fs from 'node:fs';
import { PineTS, Indicator } from 'pinets';
import { aggregate, provider, TF, resolveScript } from './harness.mjs';

export const T0 = Date.UTC(2026, 2, 2, 14, 30, 0); // Monday 08:30 Chicago
export const barStart = (i, barSec = 15) => T0 + i * barSec * 1000;

/** Builds trades bar by bar; levels = [{ price, ask, bid }] (ask = aggressive buys at that price, bid = aggressive sells). */
export class Tape {
    constructor({ tick = 0.25, barSec = 15, start = T0 } = {}) {
        this.tick = tick;
        this.barSec = barSec;
        this.start = start;
        this.trades = [];
        this.bars = 0;
    }

    P(v) {
        return Math.round(v / this.tick) * this.tick;
    }

    /** One bar: price path open -> low -> high -> close (up bar) or open -> high -> low -> close, prints of maxPrint lots. */
    bar(i, open, close, levels, maxPrint = 5) {
        const tk = (p) => Math.round(p / this.tick);
        const byTick = new Map(levels.map((l) => [tk(l.price), l]));
        let lo = Math.min(...levels.map((l) => tk(l.price)), tk(open), tk(close));
        let hi = Math.max(...levels.map((l) => tk(l.price)), tk(open), tk(close));
        const o = tk(open), c = tk(close);
        const path = [];
        if (c >= o) { for (let t = o; t >= lo; t--) path.push(t); for (let t = lo + 1; t <= hi; t++) path.push(t); for (let t = hi - 1; t >= c; t--) path.push(t); }
        else { for (let t = o; t <= hi; t++) path.push(t); for (let t = hi - 1; t >= lo; t--) path.push(t); for (let t = lo + 1; t <= c; t++) path.push(t); }
        const prints = [];
        const seen = new Set();
        let closePrint = null;
        for (const t of path) {
            if (seen.has(t)) continue;
            seen.add(t);
            const l = byTick.get(t);
            if (!l) continue;
            let a = l.ask, b = l.bid;
            const lp = [];
            while (a > 0 || b > 0) {
                if (a > 0) { const v = Math.min(maxPrint, a); a -= v; lp.push({ price: t * this.tick, size: v, buy: true }); }
                if (b > 0) { const v = Math.min(maxPrint, b); b -= v; lp.push({ price: t * this.tick, size: v, buy: false }); }
            }
            if (t === c && lp.length) closePrint = lp.pop();
            prints.push(...lp);
        }
        if (closePrint) prints.push(closePrint);
        if (!prints.length || Math.round(prints[0].price / this.tick) !== o) prints.unshift({ price: o * this.tick, size: 1, buy: c < o });
        if (Math.round(prints[prints.length - 1].price / this.tick) !== c) prints.push({ price: c * this.tick, size: 1, buy: c >= o });
        const t0 = this.start + i * this.barSec * 1000;
        const span = this.barSec * 1000 - 2;
        prints.forEach((p, k) => this.trades.push({ ...p, time: t0 + 1 + Math.floor((span * k) / prints.length) }));
        this.bars = Math.max(this.bars, i + 1);
        return this;
    }

    /** A quiet two-sided bar around `mid` (filler). */
    filler(i, open, close, lo, hi, v = 10) {
        const levels = [];
        for (let p = lo; p <= hi + 1e-9; p += this.tick) levels.push({ price: this.P(p), ask: v, bid: v });
        return this.bar(i, open, close, levels, 5);
    }

    market() {
        return aggregate([...this.trades].sort((a, b) => a.time - b.time), { start: this.start, barSec: this.barSec, tick: this.tick, bars: this.bars });
    }
}

/**
 * Runs a script on trades up to `until`, then replays the remaining trades tick by tick like TradingView's realtime:
 * every update re-executes the forming bar (PineTS updateTail, with rollback of the bar's state and drawings).
 */
export class Live {
    constructor(file, tape, inputs = {}) {
        ({ file, inputs } = resolveScript(file, inputs));
        this.file = file;
        this.tape = tape;
        this.inputs = inputs;
        this.now = 0;
        this.realNow = Date.now;
    }

    data() {
        const visible = this.tape.trades.filter((t) => t.time <= this.now).sort((a, b) => a.time - b.time);
        const bars = Math.floor((this.now - this.tape.start) / (this.tape.barSec * 1000)) + 1;
        return aggregate(visible, { start: this.tape.start, barSec: this.tape.barSec, tick: this.tape.tick, bars: Math.min(bars, this.tape.bars) });
    }

    async start(until) {
        this.now = until;
        Date.now = () => this.now;
        const self = this;
        const base = provider({ tick: this.tape.tick, candles: [], footprints: [] });
        const p = {
            getSymbolInfo: base.getSymbolInfo,
            getMarketData: async (_t, _tf, _l, sDate) => self.data().candles.filter((c) => sDate === undefined || c.openTime >= sDate),
            getFootprintData: async (_t, _tf, _l, sDate, eDate) =>
                self.data().footprints.filter((b) => (sDate === undefined || b.openTime >= sDate) && (eDate === undefined || b.openTime < eDate)),
        };
        const ind = new Indicator(fs.readFileSync(this.file, 'utf8'));
        for (const [k, v] of Object.entries(this.inputs)) ind.input[k] = v;
        this.pine = new PineTS(p, 'NQ1!', TF[this.tape.barSec] ?? '15S');
        await this.pine.ready();
        this.ctx = await this.pine.run(ind);
        return this.ctx;
    }

    /** Advances the clock to `t` (one realtime update). */
    async to(t) {
        this.now = t;
        await this.pine.updateTail(this.ctx);
        return this.ctx;
    }

    stop() {
        Date.now = this.realNow;
    }
}
