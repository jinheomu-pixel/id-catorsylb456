// Renders all four indicators together on simulated NQ 15-second order flow (PineTS), for a visual impression.
import path from 'node:path';
import { makeMarket, runPine, renderSvg, renderPng, closeBrowser } from './harness.mjs';
const TV = '../../TradingView';
const out = process.argv[2] ?? 'out/preview.png';
const segs = [];
let lvl = 25000;
for (let p = 0; p < 9; p++) {
  const drift = [0.5, 0.62, 0.5, 0.4, 0.5, 0.58, 0.45, 0.5, 0.55][p];
  segs.push({ bars: 10, tradesPerBar: 170, bigProb: 0.005, revert: drift === 0.5 ? { level: lvl, strength: 0.05 } : null, buyProb: drift, liftProb: 0.26 });
  segs.push({ bars: 10, tradesPerBar: 170, bigProb: 0.005, buyProb: drift, liftProb: 0.24 });
  lvl += (drift - 0.5) * 60;
}
const m = makeMarket({ seed: 42, segments: segs });
const ctxs = [];
for (const f of ['IDC_HVN_Profile.pine', 'IDC_Imbalance_Levels.pine', 'IDC_Heavy_Absorption.pine']) ctxs.push(await runPine(path.join(TV, f), m));
await renderPng(renderSvg(m, ctxs, { title: 'NQ1! 15s - IDC HVN Profile + Imbalance Levels + Heavy Absorption (simulated data, PineTS)', from: m.candles.length - 95, width: 1400, height: 800 }), out);
const ptk = await runPine(path.join(TV, 'IDC_Pressure_Toolkit.pine'), m);
await renderPng(renderSvg(m, [ptk], { title: 'NQ1! 15s - IDC Pressure Toolkit (simulated data, PineTS)', from: m.candles.length - 95, width: 1400, height: 800 }), out.replace('.png', '-ptk.png'));
await closeBrowser();
console.log('ok', m.candles.length, 'bars');
