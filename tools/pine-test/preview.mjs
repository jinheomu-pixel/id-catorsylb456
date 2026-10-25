// Renders all four indicators together on simulated NQ 15-second order flow (PineTS), for a visual impression.
import path from 'node:path';
import { makeMarket, scaleMarket, runPine, renderSvg, renderPng, closeBrowser, drawings } from './harness.mjs';
import { Live } from './tape.mjs';
const TV = '../../TradingView';
const out = process.argv.find((a, i) => i > 1 && !a.startsWith('--')) ?? 'out/preview.png';
const LITE_ONLY = process.argv.includes('--lite');
const segs = [];
let lvl = 25000;
for (let p = 0; p < 9; p++) {
  const drift = [0.5, 0.62, 0.5, 0.4, 0.5, 0.58, 0.45, 0.5, 0.55][p];
  segs.push({ bars: 10, tradesPerBar: 170, bigProb: 0.005, revert: drift === 0.5 ? { level: lvl, strength: 0.05 } : null, buyProb: drift, liftProb: 0.26 });
  segs.push({ bars: 10, tradesPerBar: 170, bigProb: 0.005, buyProb: drift, liftProb: 0.24 });
  lvl += (drift - 0.5) * 60;
}
// IDC Order Flow Lite (no footprint data, every TradingView plan): simulated gold 1-minute bars, chart bars only
async function lite() {
    const g = scaleMarket(makeMarket({ seed: 42, barSec: 60, segments: segs.map((x) => ({ ...x, tradesPerBar: x.tradesPerBar * 4 })) }),
        { price: 4000, step: 0.04, tick: 0.001, sizeScale: 1, jitter: 40 });
    const info = { type: 'cfd', basecurrency: 'XAU', currency: 'USD', ticker: 'XAUUSD', tickerid: 'OANDA:XAUUSD', prefix: 'OANDA' };
    const ctx = await runPine(path.join(TV, 'IDC_OrderFlow_Lite.pine'), g, { Intrabars: 'Chart bars only' }, { info });
    await renderPng(renderSvg(g, [ctx], { title: 'XAUUSD 1m - IDC Order Flow Lite: footprints estimated from candles, every TradingView plan (simulated data, PineTS)', from: g.candles.length - 95, width: 1400, height: 800 }), out.replace('.png', '-lite.png'));
}
if (LITE_ONLY) {
    await lite();
    await closeBrowser();
    console.log('ok lite');
    process.exit(0);
}
const m = makeMarket({ seed: 42, segments: segs });
const ctxs = [];
for (const f of ['IDC_HVN_Profile.pine', 'IDC_Imbalance_Levels.pine', 'IDC_Heavy_Absorption.pine']) ctxs.push(await runPine(path.join(TV, f), m));
await renderPng(renderSvg(m, ctxs, { title: 'NQ1! 15s - IDC HVN Profile + Imbalance Levels + Heavy Absorption (simulated data, PineTS)', from: m.candles.length - 95, width: 1400, height: 800 }), out);
const ptk = await runPine(path.join(TV, 'IDC_Pressure_Toolkit.pine'), m);
await renderPng(renderSvg(m, [ptk], { title: 'NQ1! 15s - IDC Pressure Toolkit (simulated data, PineTS)', from: m.candles.length - 95, width: 1400, height: 800 }), out.replace('.png', '-ptk.png'));
// live: IDC Order Flow part-way into a bar near the end (the moment with the most live, dashed zones)
const tape = { trades: m.trades, start: m.start, barSec: 15, tick: m.tick, bars: m.candles.length };
let best = null;
for (let b = m.candles.length - 4; b < m.candles.length; b++) {
    const t = m.start + b * 15000 + 10000;
    const live = new Live(path.join(TV, 'IDC_OrderFlow.pine'), tape);
    try {
        const ctx = await live.start(t);
        const n = drawings(ctx).boxes.filter((x) => String(x.border_style).includes('dash')).length;
        if (!best || n > best.n) best = { n, ctx, data: live.data(), b };
    } finally { live.stop(); }
}
await renderPng(renderSvg(best.data, [best.ctx], { title: 'NQ1! 15s - IDC Order Flow, live: dashed = the forming bar / period, solid = final (simulated data, PineTS)', from: best.data.candles.length - 95, width: 1400, height: 800 }), out.replace('.png', '-live.png'));
await lite();
await closeBrowser();
console.log('ok', m.candles.length, 'bars; live view at bar', best.b, 'with', best.n, 'dashed zones');
