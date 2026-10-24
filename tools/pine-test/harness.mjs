// Test harness for the IDCator TradingView indicators.
//
// - makeMarket(): a seeded, tick-by-tick NQ-like order-flow simulation (aggressive buys lift the ask, aggressive sells
//   hit the bid), aggregated into candles and per-price buy/sell footprint levels.
// - runPine(): runs a .pine file in PineTS (LuxAlgo's open-source Pine Script v6 runtime) on that market, with
//   request.footprint() served from the simulated footprints.
// - drawings(): the boxes / labels / lines / polylines the script left on the chart.
// - renderPng(): draws candles + drawings in TradingView's dark theme (SVG rendered by Chromium) for visual checks.
import fs from 'node:fs';
import path from 'node:path';
import { PineTS, Indicator } from 'pinets';

// ───────────────────────────────────────────── random numbers
export function rng(seed) {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

const round = (v, tick) => Math.round(v / tick) * tick;

// ───────────────────────────────────────────── market simulation
/**
 * Simulated tick stream. `segments` describe regimes in order: { bars, buyProb, liftProb, tradesPerBar, bigProb }.
 * buyProb: share of aggressive buys; liftProb: chance that an aggressive trade moves the touched side one tick.
 * Returns { tick, barSec, candles, footprints, trades }.
 */
export function makeMarket({ seed = 1, start = Date.UTC(2026, 2, 2, 14, 30, 0), barSec = 15, price = 25000, tick = 0.25,
    segments = [{ bars: 400 }], hooks = {} } = {}) {
    const r = rng(seed);
    let bid = round(price, tick);
    const trades = [];
    let bar = 0;
    for (const seg of segments) {
        const s = { buyProb: 0.5, liftProb: 0.22, tradesPerBar: 90, bigProb: 0.004, revert: null, ...seg };
        for (let b = 0; b < s.bars; b++, bar++) {
            const t0 = start + bar * barSec * 1000;
            const n = Math.max(8, Math.round(s.tradesPerBar * (0.6 + 0.8 * r())));
            for (let k = 0; k < n; k++) {
                let buy = r() < s.buyProb;
                if (s.revert) {
                    // mean reversion around a level (builds high-volume nodes)
                    const d = (bid - s.revert.level) / tick;
                    buy = r() < 0.5 - Math.max(-0.35, Math.min(0.35, d * s.revert.strength));
                }
                let size = 1 + Math.floor(-Math.log(1 - r()) * 2.2);
                if (r() < s.bigProb) size = 25 + Math.floor(r() * 120);
                const time = t0 + Math.floor(((k + r()) / n) * barSec * 1000);
                const px = buy ? bid + tick : bid;
                trades.push({ time, price: round(px, tick), size, buy, bar });
                if (r() < s.liftProb / Math.sqrt(Math.max(1, size / 3))) bid = round(bid + (buy ? tick : -tick), tick);
            }
            if (hooks.afterBar) {
                const extra = hooks.afterBar(bar, { bid, tick, t0, barSec, r });
                if (extra) for (const e of extra) { trades.push({ ...e, bar }); if (e.moveTo !== undefined) bid = e.moveTo; }
            }
        }
    }
    trades.sort((a, b) => a.time - b.time || 0);
    return aggregate(trades, { start, barSec, tick, bars: bar });
}

/**
 * The same order flow at another price level, e.g. Bitcoin: each 0.25 point move of the NQ-like simulation becomes `step`,
 * prices sit on a `tick` grid and sizes are scaled to fractional lots (crypto volume in coins). `jitter` (ticks) spreads the
 * trades of each step over that many ticks above it, as real coins trade at every tick (0 = exactly on the steps).
 */
export function scaleMarket(m, { price = 100000, step = 5, tick = 0.01, sizeScale = 0.01, base = 25000, jitter = 0 } = {}) {
    const k = step / m.tick;
    const off = (i) => (jitter > 0 ? ((Math.imul(i + 1, 2654435761) >>> 0) % jitter) * tick : 0);
    const trades = m.trades.map((t, i) => ({ ...t, price: round(price + (t.price - base) * k + off(i), tick), size: Math.round(t.size * sizeScale * 1e4) / 1e4 }));
    return aggregate(trades, { start: m.start, barSec: m.barSec, tick, bars: m.candles.length });
}

/** Candles + footprints (price -> buy/sell) from a trade list. */
export function aggregate(trades, { start, barSec, tick, bars }) {
    const candles = [], footprints = [];
    const byBar = Array.from({ length: bars }, () => []);
    for (const t of trades) {
        const i = Math.floor((t.time - start) / (barSec * 1000));
        if (i >= 0 && i < bars) byBar[i].push(t);
    }
    for (let i = 0; i < bars; i++) {
        const list = byBar[i];
        const openTime = start + i * barSec * 1000;
        if (!list.length) continue;
        const levels = new Map();
        let hi = -Infinity, lo = Infinity, vol = 0;
        for (const t of list) {
            hi = Math.max(hi, t.price); lo = Math.min(lo, t.price); vol += t.size;
            const l = levels.get(t.price) ?? { price: t.price, buyVolume: 0, sellVolume: 0 };
            if (t.buy) l.buyVolume += t.size; else l.sellVolume += t.size;
            levels.set(t.price, l);
        }
        candles.push({ openTime, closeTime: openTime + barSec * 1000, open: list[0].price, high: hi, low: lo, close: list[list.length - 1].price, volume: vol });
        footprints.push({ openTime, tick, levels: [...levels.values()] });
    }
    return { tick, barSec, start, candles, footprints, trades };
}

// ───────────────────────────────────────────── PineTS
export function provider(market, { symbol = 'NQ1!', withFootprint = true, info = {} } = {}) {
    const p = {
        getMarketData: async (_t, _tf, _limit, sDate, eDate) =>
            market.candles.filter((c) => (sDate === undefined || c.openTime >= sDate) && (eDate === undefined || c.openTime <= eDate)),
        getSymbolInfo: async () => ({
            ticker: symbol, tickerid: 'CME_MINI:' + symbol, prefix: 'CME_MINI', description: 'E-mini Nasdaq-100 Futures',
            type: 'futures', mintick: market.tick, pricescale: Math.round(1 / market.tick), minmove: 1,
            currency: 'USD', timezone: 'America/Chicago', session: '24x7', volumetype: 'base',
            ...info,
        }),
    };
    if (withFootprint)
        p.getFootprintData = async (_t, _tf, _limit, sDate, eDate) =>
            market.footprints.filter((b) => (sDate === undefined || b.openTime >= sDate) && (eDate === undefined || b.openTime < eDate));
    return p;
}

export const TF = { 15: '15S', 30: '30S', 60: '1', 300: '5' };

export async function runPine(file, market, inputs = {}, opts = {}) {
    const src = fs.readFileSync(file, 'utf8');
    const ind = new Indicator(src);
    for (const [k, v] of Object.entries(inputs)) ind.input[k] = v;
    const pine = new PineTS(provider(market, opts), opts.symbol ?? 'NQ1!', TF[market.barSec] ?? '15S');
    await pine.ready();
    const ctx = await pine.run(ind);
    return ctx;
}

const live = (arr) => (arr?.array ?? arr ?? []).filter((o) => o && !o._deleted);

/** Drawings left on the chart: plain objects with resolved coordinates. */
export function drawings(ctx) {
    const P = ctx.pine;
    return {
        boxes: live(P.box?.all),
        labels: live(P.label?.all),
        lines: live(P.line?.all),
        polylines: live(P.polyline?.all),
        tables: live(P.table?.all),
    };
}

// ───────────────────────────────────────────── rendering (TradingView dark theme)
function parseColor(c) {
    if (c === undefined || c === null || c === '' || Number.isNaN(c)) return { css: 'none', a: 0 };
    if (typeof c === 'object' && c.r !== undefined) return { css: `rgba(${c.r},${c.g},${c.b},${c.a ?? 1})`, a: c.a ?? 1 };
    const s = String(c);
    let m = s.match(/^#([0-9a-f]{6})([0-9a-f]{2})?$/i);
    if (m) {
        const n = parseInt(m[1], 16);
        const a = m[2] ? parseInt(m[2], 16) / 255 : 1;
        return { css: `rgba(${n >> 16},${(n >> 8) & 255},${n & 255},${a.toFixed(3)})`, a };
    }
    m = s.match(/rgba?\(([^)]+)\)/);
    if (m) {
        const p = m[1].split(',').map((x) => parseFloat(x));
        return { css: `rgba(${p[0]},${p[1]},${p[2]},${p[3] ?? 1})`, a: p[3] ?? 1 };
    }
    return { css: s, a: 1 };
}

const SIZE_PT = { tiny: 8, small: 10, normal: 12, large: 18, huge: 24, auto: 12 };
function pt(size) {
    if (typeof size === 'number') return size;
    const s = String(size ?? 'normal').replace('size.', '');
    return SIZE_PT[s] ?? 12;
}
const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/**
 * SVG of the last `bars` candles with the script's drawings. TradingView-like dark theme, price axis on the right.
 * `from`/`to`: bar index window; prices auto-fit.
 */
export function renderSvg(market, ctx, { from, to, width = 1280, height = 720, title = '' } = {}) {
    const C = market.candles;
    to = to ?? C.length - 1;
    from = from ?? Math.max(0, to - 90);
    const right = 70, top = 24, bottom = 26;
    const plotW = width - right, plotH = height - top - bottom;
    const n = to - from + 1 + 4; // room for the right margin
    const bw = plotW / n;
    const xOf = (i) => (i - from + 0.5) * bw;
    let lo = Infinity, hi = -Infinity;
    for (let i = from; i <= to; i++) { lo = Math.min(lo, C[i].low); hi = Math.max(hi, C[i].high); }
    const pad = (hi - lo) * 0.08 + market.tick * 4;
    lo -= pad; hi += pad;
    const yOf = (p) => top + (hi - p) / (hi - lo) * plotH;
    const timeToIndex = new Map(C.map((c, i) => [c.openTime, i]));
    const xi = (x, xloc) => (xloc === 'bt' || xloc === 'bar_time' || xloc === 'xloc.bar_time') ? (timeToIndex.get(x) ?? NaN) : x;

    const out = [];
    out.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" font-family="-apple-system, 'Trebuchet MS', Roboto, Ubuntu, sans-serif">`);
    out.push(`<rect width="${width}" height="${height}" fill="#131722"/>`);
    out.push(`<defs><clipPath id="plot"><rect x="0" y="${top}" width="${plotW}" height="${plotH}"/></clipPath></defs>`);
    // grid + price axis
    const step = niceStep((hi - lo) / 8);
    for (let p = Math.ceil(lo / step) * step; p <= hi; p += step) {
        const y = yOf(p);
        out.push(`<line x1="0" x2="${plotW}" y1="${y}" y2="${y}" stroke="#1f2430" stroke-width="1"/>`);
        out.push(`<text x="${plotW + 6}" y="${y + 4}" fill="#b2b5be" font-size="11">${p.toFixed(2)}</text>`);
    }
    out.push(`<g clip-path="url(#plot)">`);
    const D = { boxes: [], labels: [], lines: [], polylines: [], tables: [] };
    for (const one of Array.isArray(ctx) ? ctx : [ctx]) {
        const d = drawings(one);
        for (const k of Object.keys(D)) D[k].push(...d[k]);
    }
    // boxes (behind candles, as TradingView draws script boxes under the price series by default)
    for (const b of D.boxes) {
        const x1 = xOf(xi(b.left, b.xloc)) - bw / 2, x2 = xOf(xi(b.right, b.xloc)) - bw / 2;
        const y1 = yOf(b.top), y2 = yOf(b.bottom);
        const bg = parseColor(b.bgcolor), bc = parseColor(b.border_color);
        out.push(`<rect x="${Math.min(x1, x2)}" y="${Math.min(y1, y2)}" width="${Math.abs(x2 - x1)}" height="${Math.max(1, Math.abs(y2 - y1))}" fill="${bg.css}" stroke="${bc.a > 0 ? bc.css : 'none'}" stroke-width="${b.border_width ?? 1}"/>`);
        if (b.text) {
            const fs_ = pt(b.text_size) * 1.333;
            const ha = String(b.text_halign ?? 'center');
            const anchor = ha.includes('right') ? 'end' : ha.includes('left') ? 'start' : 'middle';
            const tx = anchor === 'end' ? Math.max(x1, x2) - 4 : anchor === 'start' ? Math.min(x1, x2) + 4 : (x1 + x2) / 2;
            const weight = String(b.text_formatting ?? '').includes('bold') ? 'bold' : 'normal';
            out.push(`<text x="${tx}" y="${(y1 + y2) / 2 + fs_ * 0.35}" fill="${parseColor(b.text_color).css}" font-size="${fs_}" font-weight="${weight}" text-anchor="${anchor}">${esc(b.text)}</text>`);
        }
    }
    // polylines
    for (const pl of D.polylines) {
        const pts = (pl.points?.array ?? pl.points ?? []).map((p) => `${xOf(p.index ?? timeToIndex.get(p.time)) - (pl.xloc === 'bt' ? 0 : 0)},${yOf(p.price)}`).join(' ');
        const fill = parseColor(pl.fill_color), lc = parseColor(pl.line_color);
        out.push(`<polyline points="${pts}" fill="${pl.closed ? fill.css : 'none'}" stroke="${lc.a > 0 ? lc.css : 'none'}" stroke-width="${pl.line_width ?? 1}"/>`);
    }
    // candles
    for (let i = from; i <= to; i++) {
        const c = C[i];
        const up = c.close >= c.open;
        const col = up ? '#089981' : '#f23645';
        const x = xOf(i);
        out.push(`<line x1="${x}" x2="${x}" y1="${yOf(c.high)}" y2="${yOf(c.low)}" stroke="${col}" stroke-width="1"/>`);
        const yb = yOf(Math.max(c.open, c.close)), ye = yOf(Math.min(c.open, c.close));
        out.push(`<rect x="${x - bw * 0.35}" y="${yb}" width="${bw * 0.7}" height="${Math.max(1, ye - yb)}" fill="${col}"/>`);
    }
    // lines
    for (const l of D.lines) {
        const x1 = xOf(xi(l.x1, l.xloc)), x2 = xOf(xi(l.x2, l.xloc));
        const ext = String(l.extend ?? 'n').replace('extend.', '');
        const both = ext === 'b' || ext === 'both';
        const X1 = both || ext === 'l' || ext === 'left' ? 0 : x1, X2 = both || ext === 'r' || ext === 'right' ? plotW : x2;
        const lc = parseColor(l.color);
        const dash = String(l.style ?? '').includes('dash') ? 'stroke-dasharray="6 4"' : String(l.style ?? '').includes('dot') ? 'stroke-dasharray="2 3"' : '';
        out.push(`<line x1="${X1}" x2="${X2}" y1="${yOf(l.y1)}" y2="${yOf(l.y2)}" stroke="${lc.css}" stroke-width="${l.width ?? 1}" ${dash}/>`);
    }
    // labels
    for (const l of D.labels) {
        const x = xOf(xi(l.x, l.xloc));
        const bi = Math.round(xi(l.x, l.xloc));
        const yl = String(l.yloc ?? 'pr').replace('yloc.', '');
        let y = yOf(l.y);
        if ((yl === 'ab' || yl === 'abovebar') && C[bi]) y = yOf(C[bi].high) - 9;
        if ((yl === 'bl' || yl === 'belowbar') && C[bi]) y = yOf(C[bi].low) + 9;
        const fsz = pt(l.size) * 1.333;
        const style = String(l.style ?? '');
        const tc = parseColor(l.textcolor);
        const bg = parseColor(l.color);
        const weight = String(l.text_formatting ?? '').includes('bold') ? 'bold' : 'normal';
        const family = String(l.text_font_family ?? '').includes('mono') ? 'monospace' : 'inherit';
        if (style.includes('circle') && !l.text) {
            out.push(`<circle cx="${x}" cy="${y}" r="${fsz * 0.35}" fill="${bg.css}"/>`);
            continue;
        }
        if (bg.a > 0 && !style.includes('none')) {
            const w = String(l.text).length * fsz * 0.6 + 8, h = fsz * 1.4;
            out.push(`<rect x="${x - w / 2}" y="${y - h / 2}" width="${w}" height="${h}" rx="3" fill="${bg.css}"/>`);
        }
        const anchor = style.includes('label_right') ? 'end' : style.includes('label_left') ? 'start' : 'middle';
        const dx = anchor === 'end' ? -4 : anchor === 'start' ? 4 : 0;
        const lines_ = String(l.text ?? '').split('\n');
        lines_.forEach((ln, k) => {
            out.push(`<text x="${x + dx}" y="${y + fsz * 0.35 + (k - (lines_.length - 1) / 2) * fsz * 1.15}" fill="${tc.css}" font-size="${fsz}" font-weight="${weight}" font-family="${family}" text-anchor="${anchor}" xml:space="preserve">${esc(ln)}</text>`);
        });
    }
    out.push(`</g>`);
    if (title) out.push(`<text x="10" y="16" fill="#d1d4dc" font-size="13">${esc(title)}</text>`);
    out.push(`</svg>`);
    return out.join('\n');
}

function niceStep(raw) {
    const p = Math.pow(10, Math.floor(Math.log10(raw)));
    for (const m of [1, 2, 2.5, 5, 10]) if (m * p >= raw) return m * p;
    return 10 * p;
}

let browser = null;
export async function renderPng(svg, file) {
    if (!browser) {
        const { chromium } = await import('playwright');
        browser = await chromium.launch(fs.existsSync('/opt/pw-browsers/chromium') ? { executablePath: '/opt/pw-browsers/chromium' } : {});
    }
    const m = svg.match(/width="(\d+)" height="(\d+)"/);
    const page = await browser.newPage({ viewport: { width: +m[1], height: +m[2] } });
    await page.setContent(`<html><body style="margin:0;background:#131722">${svg}</body></html>`);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    await page.screenshot({ path: file });
    await page.close();
}

export async function closeBrowser() {
    if (browser) await browser.close();
    browser = null;
}
