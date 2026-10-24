// Renders MT5 simulation results (check.py --run JSON) as a chart image: candles plus the indicators' chart objects.
//
//   node tools/mql-check/render.mjs OUT.png "Title" A.json [B.json ...]
//
// MT5 conventions: an object's time snaps to its bar (candle centre), BACK objects are drawn behind the candles,
// text is placed by its anchor point, and Wingdings characters are drawn as the shapes they show in MT5.
import fs from 'node:fs';
import { renderPng, closeBrowser } from '../pine-test/harness.mjs';

const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const WD = { 'l': 'circle', '¡': 'ring', 'o': 'square' };
// MT5 ENUM_ANCHOR_POINT -> [text-anchor, vertical]
const ANCHOR = [['start', 'top'], ['start', 'middle'], ['start', 'bottom'], ['middle', 'bottom'], ['end', 'bottom'], ['end', 'middle'],
    ['end', 'top'], ['middle', 'top'], ['middle', 'middle']];

function niceStep(raw) {
    const p = Math.pow(10, Math.floor(Math.log10(raw)));
    for (const m of [1, 2, 2.5, 5, 10]) if (m * p >= raw) return m * p;
    return 10 * p;
}

export function renderMt5(runs, { bars: nBars = 95, width = 1400, height = 800, title = '' } = {}) {
    const B = runs[0].bars.map(([t, o, h, l, c]) => ({ t, o, h, l, c }));
    const period = runs[0].period ?? 60;
    const to = B.length - 1, from = Math.max(0, to - nBars + 1);
    const right = 70, top = 24, bottom = 26;
    const plotW = width - right, plotH = height - top - bottom;
    const bw = plotW / (to - from + 1 + 4);
    const t0 = B[0].t;
    const idx = (t) => Math.floor((t - t0) / period);          // minutes are contiguous in the simulation
    const xOf = (t) => (idx(t) - from + 0.5) * bw;
    let lo = Infinity, hi = -Infinity;
    for (let i = from; i <= to; i++) { lo = Math.min(lo, B[i].l); hi = Math.max(hi, B[i].h); }
    const pad = (hi - lo) * 0.08 + 1;
    lo -= pad; hi += pad;
    const yOf = (p) => top + (hi - p) / (hi - lo) * plotH;
    const objects = runs.flatMap((r) => r.objects);

    const out = [];
    out.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" font-family="Arial, Helvetica, sans-serif">`);
    out.push(`<rect width="${width}" height="${height}" fill="#131722"/>`);
    out.push(`<defs><clipPath id="plot"><rect x="0" y="${top}" width="${plotW}" height="${plotH}"/></clipPath></defs>`);
    const step = niceStep((hi - lo) / 8);
    for (let p = Math.ceil(lo / step) * step; p <= hi; p += step) {
        const y = yOf(p);
        out.push(`<line x1="0" x2="${plotW}" y1="${y}" y2="${y}" stroke="#1f2430" stroke-width="1"/>`);
        out.push(`<text x="${plotW + 6}" y="${y + 4}" fill="#b2b5be" font-size="11">${p.toFixed(2)}</text>`);
    }
    out.push(`<g clip-path="url(#plot)">`);

    const draw = (o) => {
        if (o.type === 'rect') {
            const x1 = xOf(o.t1), x2 = xOf(o.t2), y1 = yOf(o.p1), y2 = yOf(o.p2);
            // an unfilled rectangle is a frame (the live zones' dashed outline)
            const paint = o.fill === 0 ? `fill="none" stroke="${o.color}" stroke-width="1"${o.style === 1 ? ' stroke-dasharray="6 4"' : ''}` : `fill="${o.color}"`;
            out.push(`<rect x="${Math.min(x1, x2)}" y="${Math.min(y1, y2)}" width="${Math.abs(x2 - x1)}" height="${Math.max(1, Math.abs(y2 - y1))}" ${paint}/>`);
        } else if (o.type === 'trend' || o.type === 'hline') {
            const dash = o.style === 2 ? 'stroke-dasharray="2 3"' : o.style === 1 ? 'stroke-dasharray="6 4"' : '';
            if (o.type === 'hline') {
                out.push(`<line x1="0" x2="${plotW}" y1="${yOf(o.p1)}" y2="${yOf(o.p1)}" stroke="${o.color}" stroke-width="${o.width}" ${dash}/>`);
                return;
            }
            let x1 = xOf(o.t1), x2 = xOf(o.t2), y1 = yOf(o.p1), y2 = yOf(o.p2);
            if (o.ray && x2 !== x1) { y2 = y1 + (y2 - y1) * (plotW - x1) / (x2 - x1); x2 = plotW; }
            out.push(`<line x1="${x1}" x2="${x2}" y1="${y1}" y2="${y2}" stroke="${o.color}" stroke-width="${o.width}" ${dash}/>`);
        } else if (o.type === 'text') {
            const x = xOf(o.t1), y = yOf(o.p1);
            const [ha, va] = ANCHOR[o.anchor] ?? ANCHOR[8];
            const px = o.size * 1.333;
            if (/wingdings/i.test(o.font) && WD[o.text]) {
                const shape = WD[o.text];
                const d = shape === 'square' ? px * 0.62 : shape === 'ring' ? px * 0.8 : px * 0.66;
                const cx = ha === 'start' ? x + d / 2 : ha === 'end' ? x - d / 2 : x;
                const cy = va === 'top' ? y + px * 0.5 : va === 'bottom' ? y - px * 0.5 : y;
                if (shape === 'circle') out.push(`<circle cx="${cx}" cy="${cy}" r="${d / 2}" fill="${o.color}"/>`);
                else if (shape === 'ring') out.push(`<circle cx="${cx}" cy="${cy}" r="${d / 2}" fill="none" stroke="${o.color}" stroke-width="${Math.max(1, px * 0.06)}"/>`);
                else out.push(`<rect x="${cx - d / 2}" y="${cy - d / 2}" width="${d}" height="${d}" fill="none" stroke="${o.color}" stroke-width="${Math.max(1, px * 0.07)}"/>`);
                return;
            }
            const bold = /bold/i.test(o.font) ? 'bold' : 'normal';
            const family = /courier/i.test(o.font) ? "'Courier New', monospace" : 'Arial, Helvetica, sans-serif';
            const base = va === 'top' ? 'hanging' : va === 'bottom' ? 'text-after-edge' : 'central';
            out.push(`<text x="${x}" y="${y}" fill="${o.color}" font-size="${px}" font-weight="${bold}" font-family="${family}" text-anchor="${ha}" dominant-baseline="${base}" xml:space="preserve">${esc(o.text)}</text>`);
        }
    };

    for (const o of objects) if (o.back) draw(o);
    for (let i = from; i <= to; i++) {
        const c = B[i];
        const col = c.c >= c.o ? '#089981' : '#f23645';
        const x = (i - from + 0.5) * bw;
        out.push(`<line x1="${x}" x2="${x}" y1="${yOf(c.h)}" y2="${yOf(c.l)}" stroke="${col}" stroke-width="1"/>`);
        const yb = yOf(Math.max(c.o, c.c)), ye = yOf(Math.min(c.o, c.c));
        out.push(`<rect x="${x - bw * 0.35}" y="${yb}" width="${bw * 0.7}" height="${Math.max(1, ye - yb)}" fill="${col}"/>`);
    }
    for (const o of objects) if (!o.back) draw(o);
    out.push(`</g>`);
    if (title) out.push(`<text x="10" y="16" fill="#d1d4dc" font-size="13">${esc(title)}</text>`);
    out.push(`</svg>`);
    return out.join('\n');
}

if (import.meta.url === `file://${process.argv[1]}`) {
    const [outFile, title, ...files] = process.argv.slice(2);
    const runs = files.map((f) => JSON.parse(fs.readFileSync(f, 'utf8')));
    await renderPng(renderMt5(runs, { title }), outFile);
    await closeBrowser();
    console.log('wrote ' + outFile);
}
