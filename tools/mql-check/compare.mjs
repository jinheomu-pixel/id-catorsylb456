// Cross-check: the MT5 indicators (simulated, check.py --run) against the TradingView scripts (PineTS) on the same
// simulated NQ trades and 1-minute bars. Both are ports of the same algorithms, so their zones and markers should match.
//
//   node tools/mql-check/compare.mjs DIR      # DIR holds <indicator>.json from check.py --run on DIR/nq-trades.csv
//
// Exit code = number of indicators whose results differ.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { runPine, drawings } from '../pine-test/harness.mjs';
import { simMarket } from './ticks.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const TV = path.join(here, '..', '..', 'TradingView');
const f2 = (v) => Number(v).toFixed(2);

// right edge in MT5 terms: a touched object covers its touch bar, an active one reaches one bar past the last
// (TradingView ends a touched object at the touch bar and an active one at bar_index + 1)
const tvEnd = (right, last) => (right === last + 1 ? right : right + 1);

function mt5(dir, name) {
    const d = JSON.parse(fs.readFileSync(path.join(dir, name + '.json'), 'utf8'));
    const by = new Map(d.objects.map((o) => [o.name, o]));
    return { d, by, last: d.bars.length - 1 };
}

/** Compares two maps key -> value; prints the differences. */
function diff(log, title, a, b, show = 8) {
    const keys = [...new Set([...a.keys(), ...b.keys()])].sort();
    const bad = keys.filter((k) => JSON.stringify(a.get(k)) !== JSON.stringify(b.get(k)));
    log(`${bad.length ? 'DIFF' : 'SAME'}  ${title}: TradingView ${a.size}, MT5 ${b.size}${bad.length ? `, ${bad.length} differ` : ''}`);
    for (const k of bad.slice(0, show)) log(`      ${k}:  TV ${JSON.stringify(a.get(k))}  MT5 ${JSON.stringify(b.get(k))}`);
    return bad.length;
}

/** Compares DIR/<indicator>.json (MT5 simulation of DIR/nq-trades.csv) with PineTS; returns the number of indicators that differ. */
// `market`, `info` (TradingView symbol info) and `inputs` (TradingView inputs per script) describe the run that DIR holds.
export async function compareAll(dir, log = console.log, { market = simMarket(), info = {}, inputs = {} } = {}) {
    const t0 = market.candles[0].openTime / 1000;
    const bar = (t) => Math.floor((t - t0) / 60);
    const runTv = (file) => runPine(path.join(TV, file), market, inputs[file] ?? {}, { info });
    let failed = 0;

    // ───────────────── Imbalance Levels: zones (start bar, bottom, top) -> label, end
    {
        const ctx = await runTv('IDC_Imbalance_Levels.pine');
        const m = mt5(dir, 'IDC_Imbalance_Levels');
        const tv = new Map(), mt = new Map();
        for (const b of drawings(ctx).boxes) tv.set(`${b.left}|${f2(b.bottom)}|${f2(b.top)}`, [String(b.text), tvEnd(b.right, m.last)]);
        for (const o of m.d.objects.filter((o) => o.name.startsWith('IDCimb_z'))) {
            const t = m.by.get(o.name.replace('_z', '_t'));
            mt.set(`${bar(o.t1)}|${f2(o.p2)}|${f2(o.p1)}`, [t ? t.text : '', bar(o.t2)]);
        }
        failed += diff(log, 'Imbalance zones [label, right edge]', tv, mt) ? 1 : 0;
    }

    // ───────────────── HVN Profile: zones (start bar, bottom, top) -> label, end
    {
        const ctx = await runTv('IDC_HVN_Profile.pine');
        const m = mt5(dir, 'IDC_HVN_Profile');
        const d = drawings(ctx);
        const tv = new Map(), mt = new Map();
        for (const b of d.boxes) {
            const mid = (b.top + b.bottom) / 2;
            const txt = d.labels.filter((l) => Math.abs(l.y - mid) < 1e-6 && Math.abs(l.x - b.right) <= 1).map((l) => String(l.text).trim()).join(' ');
            tv.set(`${b.left}|${f2(b.bottom)}|${f2(b.top)}`, [txt.replace(/\s+/g, ' '), tvEnd(b.right, m.last)]);
        }
        for (const o of m.d.objects.filter((o) => o.name.startsWith('IDChvn_z'))) {
            const n = m.by.get(o.name.replace('_z', '_n')), p = m.by.get(o.name.replace('_z', '_p'));
            const txt = [n?.text, p?.text].filter(Boolean).map((s) => s.trim()).join(' ');
            mt.set(`${bar(o.t1)}|${f2(o.p2)}|${f2(o.p1)}`, [txt.replace(/\s+/g, ' '), bar(o.t2)]);
        }
        failed += diff(log, 'HVN zones [label, right edge]', tv, mt) ? 1 : 0;
    }

    // ───────────────── Heavy Absorption: markers (bar, price) -> number
    {
        const ctx = await runTv('IDC_Heavy_Absorption.pine');
        const m = mt5(dir, 'IDC_Heavy_Absorption');
        const tv = new Map(), mt = new Map();
        for (const l of drawings(ctx).labels.filter((l) => l.text && !['●', '○'].includes(l.text))) tv.set(`${l.x}|${f2(l.y)}`, String(l.text));
        for (const o of m.d.objects.filter((o) => o.name.startsWith('IDCha_t'))) mt.set(`${bar(o.t1)}|${f2(o.p1)}`, o.text);
        failed += diff(log, 'Heavy Absorption markers [number]', tv, mt) ? 1 : 0;
    }

    // ───────────────── Pressure Toolkit: bubbles, big trades, dots, absorption lines, psychological levels
    {
        const ctx = await runTv('IDC_Pressure_Toolkit.pine');
        const m = mt5(dir, 'IDC_Pressure_Toolkit');
        const d = drawings(ctx);
        const tvB = new Map(), mtB = new Map(), tvQ = new Map(), mtQ = new Map(), tvD = new Map(), mtD = new Map(), tvL = new Map(), mtL = new Map();
        for (const l of d.labels) {
            if (l.text === '●') tvB.set(`${l.x}|${f2(l.y)}`, 'bubble');
            else if (l.text === '□') tvQ.set(`${l.x}|${f2(l.y)}`, 'big');
            else if (!l.text && String(l.style).includes('circle')) tvD.set(`${l.x}|${String(l.yloc).includes('ab') ? 'above' : 'below'}`, 'dot');
        }
        const psychTv = d.lines.filter((l) => String(l.extend).match(/^(b|both|extend\.both)$/)).map((l) => f2(l.y1)).sort();
        for (const l of d.lines.filter((l) => !String(l.extend).match(/^(b|both|extend\.both)$/)))
            tvL.set(`${l.x1}|${f2(l.y1)}`, tvEnd(l.x2, m.last));
        for (const o of m.d.objects) {
            if (o.type === 'text' && /wingdings/i.test(o.font) && o.text === 'l' && !/_d[hl]$/.test(o.name)) mtB.set(`${bar(o.t1)}|${f2(o.p1)}`, 'bubble');
            else if (o.type === 'text' && /wingdings/i.test(o.font) && o.text === 'o') mtQ.set(`${bar(o.t1)}|${f2(o.p1)}`, 'big');
            else if (/_d[hl]$/.test(o.name)) mtD.set(`${bar(o.t1)}|${o.name.endsWith('dh') ? 'above' : 'below'}`, 'dot');
            else if (o.type === 'trend') mtL.set(`${bar(o.t1)}|${f2(o.p1)}`, bar(o.t2));
        }
        const psychMt = m.d.objects.filter((o) => o.type === 'hline').map((o) => f2(o.p1)).sort();
        let bad = 0;
        bad += diff(log, 'Pressure bubbles', tvB, mtB);
        bad += diff(log, 'Pressure big trades', tvQ, mtQ);
        bad += diff(log, 'Pressure absorption dots', tvD, mtD);
        bad += diff(log, 'Pressure absorption lines [right edge]', tvL, mtL);
        bad += diff(log, 'Pressure psychological levels', new Map(psychTv.map((p) => [p, 1])), new Map(psychMt.map((p) => [p, 1])));
        failed += bad ? 1 : 0;
    }

    return failed;
}

if (import.meta.url === `file://${process.argv[1]}`) {
    const failed = await compareAll(process.argv[2] ?? '.');
    console.log(`SUMMARY: ${4 - failed} of 4 indicators match`);
    process.exit(failed);
}
