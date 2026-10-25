#!/usr/bin/env python3
"""Builds TradingView/IDC_OrderFlow.pine, the four IDCator tools in one indicator with on/off switches, from the four
single-tool scripts (the source of truth: edit those, then run this), and TradingView/IDC_OrderFlow_Lite.pine, the same
indicator on footprints estimated from candles (no request.footprint, so it runs on every TradingView plan).

    python3 tools/build_suite.py

What the build does:
- one request.footprint() (a script may only have one), one automatic row size and one status table for all tools;
- every top-level name of a tool gets the tool's prefix (zones -> imbZones, Zone -> ImbZone), so nothing collides;
- every top-level `if` / `for` of a tool runs only when its switch is on;
- the drawing caps of the tools are lowered so that together they stay under TradingView's 500 boxes / labels / lines.
tools/pine-test checks that the suite with one tool switched on draws exactly what the single script draws.
"""
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
TV = os.path.join(ROOT, 'TradingView')
OUT = os.path.join(TV, 'IDC_OrderFlow.pine')
OUT_LITE = os.path.join(TV, 'IDC_OrderFlow_Lite.pine')

# file, prefix, switch, title (switch label and group prefix); order = drawing order (HVN zones under the others)
TOOLS = [
    ('IDC_HVN_Profile.pine', 'hvn', 'hvnOn', 'HVN Profile', True),
    ('IDC_Imbalance_Levels.pine', 'imb', 'imbOn', 'Imbalance Levels', True),
    ('IDC_Heavy_Absorption.pine', 'ha', 'haOn', 'Heavy Absorption', True),
    ('IDC_Pressure_Toolkit.pine', 'ptk', 'ptkOn', 'Pressure Toolkit', False),
]
SHARED = {'fp', 'tick', 'rowTicks', 'autoRow', 'niceRow', 'anyFp', 'typRange', 'rowHint', 'rowTicksInput'}
# drawing caps in the suite (pattern in the tool's input line -> replacement)
CAPS = {
    'hvn': [('input.int(200, "Max zones on chart", minval = 10, maxval = 240', 'input.int(100, "Max zones on chart", minval = 10, maxval = 100')],
    'imb': [('input.int(300, "Max zones on chart", minval = 10, maxval = 500', 'input.int(250, "Max zones on chart", minval = 10, maxval = 300')],
    'ha': [('input.int(160, "Max markers on chart", minval = 10, maxval = 166', 'input.int(50, "Max markers on chart", minval = 10, maxval = 50')],
    'ptk': [('input.int(450, "Max markers on chart (labels)", minval = 20, maxval = 480', 'input.int(100, "Max markers on chart (labels)", minval = 20, maxval = 120'),
            ('input.int(400, "Max lines on chart", minval = 10, maxval = 440', 'input.int(250, "Max lines on chart", minval = 10, maxval = 250')],
}

TOKEN = re.compile(r'"(?:[^"\\\n]|\\.)*"|//[^\n]*|[^"/]+|/', re.S)


def code_sub(src, fn):
    """Applies fn to the code parts of src (not to strings and comments)."""
    return ''.join(fn(t) if not (t.startswith('"') or t.startswith('//')) else t for t in TOKEN.findall(src))


def cut(src, start, end, keep_end=False):
    """Removes src[start marker .. end marker) (end marker kept when keep_end)."""
    i = src.index(start)
    j = src.index(end, i + len(start))
    return src[:i] + (src[j:] if keep_end else src[j + len(end):])


def top_names(src, shared):
    names = []
    typed = r'(?:var\s+|varip\s+)?(?:int|float|bool|string|color|line|label|box|table|polyline|footprint|[A-Z]\w*|array<[\w.]+>|map<[\w.]+,\s*[\w.]+>)\s+(\w+)\s*='
    for line in src.split('\n'):
        for pat in (r'^(\w+)\s*=(?!=)', '^' + typed, r'^(\w+)\([^)]*\)\s*=>', r'^type\s+(\w+)'):
            m = re.match(pat, line)
            if m:
                names.append(m.group(1))
                break
    return [n for n in dict.fromkeys(names) if n not in shared]


def renamed(name, prefix):
    if re.fullmatch(r'[A-Z][A-Z0-9_]*', name):
        return prefix.upper() + '_' + name
    return prefix + name[0].upper() + name[1:]


def tool_body(path, prefix, switch, title):
    src = open(path).read()
    # header comments and the whole indicator(...) call (it may span lines)
    i, depth = src.index('indicator(') + len('indicator'), 0
    while True:
        depth += {'(': 1, ')': -1}.get(src[i], 0)
        i += 1
        if depth == 0:
            break
    src = src[src.index('\n', i) + 1:]
    src = re.sub(r'(?m)^(int    rowTicksInput .*\n(?:     .*\n)?)', '', src) if prefix != 'hvn' else src
    src = re.sub(r'(?m)^int    fpTicksInput .*\n(?:     .*\n)?', '', src)
    src = cut(src, '// ───────────────────────────────────────────── footprint row size', 'display = display.data_window, editable = false)\n')
    src = src.replace('float tick = syminfo.mintick\n', '')
    src = src.replace('var bool anyFp = false\nif not na(fp)\n    anyFp := true\n', '')
    # the row size hint (typRange and rowHint) is shared: once in the suite's status section
    i = src.index('// typical bar range of this chart, for the row size hint')
    src = src[:i] + src[src.index('\n\n', src.index('rowHint(string name', i)) + 1:]
    src = src[:src.index('var table info = table.new(')].rstrip() + '\n'
    src = re.sub(r'(?m)^(G_\w+\s*=\s*")', r'\1' + title + ': ', src)
    # HVN's rowTicksInput is its profile row size, not the shared footprint row input
    names = top_names(src, SHARED - {'rowTicksInput'} if prefix == 'hvn' else SHARED)
    mapping = {n: renamed(n, prefix) for n in names}
    if prefix == 'hvn':
        mapping['fpTicks'] = 'rowTicks'
    pat = re.compile(r'(?<![\w.])(' + '|'.join(sorted(map(re.escape, mapping), key=len, reverse=True)) + r')(?!\w)')
    src = code_sub(src, lambda c: pat.sub(lambda m: mapping[m.group(1)], c))
    for a, b in CAPS.get(prefix, []):
        assert src.count(a) == 1, (prefix, a)
        src = src.replace(a, b)
    # switches: top-level if / for run only when the tool is on
    out, lines, i = [], src.split('\n'), 0
    while i < len(lines):
        line = lines[i]
        if line.startswith('if '):
            code, sep, comment = line.partition(' //')
            out.append('if %s and (%s)%s%s' % (switch, code[3:].rstrip(), sep, comment))
        elif line.startswith('for '):
            out.append('if ' + switch)
            out.append('    ' + line)
            while i + 1 < len(lines) and (lines[i + 1].startswith(' ') or lines[i + 1] == ''):
                i += 1
                out.append(('    ' + lines[i]) if lines[i] else '')
        elif line.startswith('else'):
            raise SystemExit('%s: top-level else is not supported by the build' % path)
        else:
            out.append(line)
        i += 1
    return '\n'.join(out), mapping


# IDC Order Flow Lite: the suite with request.footprint() replaced by an estimate from candles
LITE_INPUT = """G_EST = "Data (Lite: estimated from candles)"
string estLtfInput = input.string("Auto", "Intrabars", options = ["Auto", "Chart bars only", "1 second", "5 seconds", "15 seconds", "1 minute", "5 minutes"],
     group = G_EST, tooltip = "Lower-timeframe candles inside each chart bar give a finer estimate. Auto: 1 minute on charts up to 1 hour, 5 minutes up to 4 hours, 15 minutes up to 1 day, else 1 hour; none on 1-minute and lower charts (the chart bar itself is used). Seconds need a TradingView plan with second-based data.")
"""

LITE_DATA = """// ───────────────────────────────────────────── estimated footprint (Lite)
// TradingView sends real footprints (buy and sell volume per price) only to Premium and Ultimate plans; IDC Order Flow
// uses them. IDC Order Flow Lite estimates them from candles, so it runs on every plan:
// - each candle is assumed to travel open -> low -> high -> close when it closes up (or flat), open -> high -> low ->
//   close when it closes down; 90 % of its volume is spread along that path by distance (each tick travelled weighs
//   the same); up legs count as buying, down legs as selling; each leg leaves 20 % of its share at its end, where
//   price turned;
// - the other 10 % trades both ways around the close (its row and the rows above and below): the candle settles there;
// - the candles are the chart bar's intrabars (a lower timeframe, see 'Intrabars') or, without them, the bar itself.
// The four tools then run exactly as in IDC Order Flow. Volumes are estimates, not traded prints.
int    estChartSec = timeframe.in_seconds()
int    estLtfSec   = estLtfInput == "Auto" ? (estChartSec <= 60 ? 0 : estChartSec <= 3600 ? 60 : estChartSec <= 14400 ? 300 : estChartSec <= 86400 ? 900 : 3600) :
     estLtfInput == "1 second" ? 1 : estLtfInput == "5 seconds" ? 5 : estLtfInput == "15 seconds" ? 15 : estLtfInput == "1 minute" ? 60 :
     estLtfInput == "5 minutes" ? 300 : 0
string estLtf      = estLtfSec == 1 ? "1S" : estLtfSec == 5 ? "5S" : estLtfSec == 15 ? "15S" : estLtfSec == 60 ? "1" : estLtfSec == 300 ? "5" :
     estLtfSec == 900 ? "15" : "60"
bool   estUseLtf   = estLtfSec > 0 and estLtfSec < estChartSec

//@type One price row of an estimated footprint (the methods mirror TradingView's volume_row).
type EstRow
    float up
    float down
    float buy
    float sell

//@type Estimated footprint of a bar (the methods mirror TradingView's footprint).
type EstFootprint
    array<EstRow> items
    float buy
    float sell

method up_price(EstRow r) => r.up
method down_price(EstRow r) => r.down
method buy_volume(EstRow r) => r.buy
method sell_volume(EstRow r) => r.sell
method total_volume(EstRow r) => r.buy + r.sell
method delta(EstRow r) => r.buy - r.sell
method rows(EstFootprint f) => f.items.copy()
method buy_volume(EstFootprint f) => f.buy
method sell_volume(EstFootprint f) => f.sell
method total_volume(EstFootprint f) => f.buy + f.sell
method delta(EstFootprint f) => f.buy - f.sell

//@function Adds one leg of a candle's path, from tick `a` to tick `b` with `v` volume, to the rows (row `k0` first,
// `rt` ticks each): buying when it goes up, selling when it goes down. 80 % is spread along the leg; 20 % stays at its end,
// where price turned.
estLeg(array<float> buy, array<float> sell, int k0, int rt, int a, int b, float v) =>
    if b != a and v > 0
        array<float> side = b > a ? buy : sell
        int   n   = side.size()
        int   lo  = b > a ? a + 1 : b
        int   hi  = b > a ? b : a - 1
        float per = v * 0.8 / (hi - lo + 1)
        for k = int(math.floor(lo / rt)) to int(math.floor(hi / rt))
            int i  = math.max(0, math.min(n - 1, k - k0))
            int ov = math.min(hi, (k + 1) * rt - 1) - math.max(lo, k * rt) + 1
            side.set(i, side.get(i) + per * ov)
        int e = math.max(0, math.min(n - 1, int(math.floor(b / rt)) - k0))
        side.set(e, side.get(e) + v * 0.2)

//@function Adds a candle's estimated buying and selling to the rows (row `k0` first, `rt` ticks each): 10 % of its volume
// trades both ways around the close (its row and the rows above and below), the rest along its path.
estCandle(array<float> buy, array<float> sell, int k0, int rt, float o, float h, float l, float c, float v) =>
    if v > 0 and not na(o) and not na(h) and not na(l) and not na(c)
        int tkO  = math.round(o / syminfo.mintick)
        int tkH  = math.round(h / syminfo.mintick)
        int tkL  = math.round(l / syminfo.mintick)
        int tkC  = math.round(c / syminfo.mintick)
        int p1   = tkC >= tkO ? tkL : tkH
        int p2   = tkC >= tkO ? tkH : tkL
        int dist = math.abs(p1 - tkO) + math.abs(p2 - p1) + math.abs(tkC - p2)
        int n    = buy.size()
        int e    = int(math.floor(tkC / rt)) - k0
        float settle = dist == 0 ? v : v * 0.1
        for k = e - 1 to e + 1
            int i = math.max(0, math.min(n - 1, k))
            buy.set(i, buy.get(i) + settle / 6)
            sell.set(i, sell.get(i) + settle / 6)
        if dist > 0
            float pv = v - settle
            estLeg(buy, sell, k0, rt, tkO, p1, pv * math.abs(p1 - tkO) / dist)
            estLeg(buy, sell, k0, rt, p1, p2, pv * math.abs(p2 - p1) / dist)
            estLeg(buy, sell, k0, rt, p2, tkC, pv * math.abs(tkC - p2) / dist)

array<float> estO = na
array<float> estH = na
array<float> estL = na
array<float> estC = na
array<float> estV = na
if estUseLtf
    [io, ih, il, ic, iv] = request.security_lower_tf(syminfo.tickerid, estLtf, [open, high, low, close, volume])
    estO := io
    estH := ih
    estL := il
    estC := ic
    estV := iv

//@variable Estimated footprint of the current bar (`na` without volume, or above 2000 rows like a real footprint).
EstFootprint fp = na
int estK0 = int(math.floor(math.round(low / syminfo.mintick) / rowTicks))
int estK1 = int(math.floor(math.round(high / syminfo.mintick) / rowTicks))
if volume > 0 and estK1 - estK0 + 1 <= 2000
    int n = estK1 - estK0 + 1
    array<float> eb = array.new_float(n, 0.0)
    array<float> es = array.new_float(n, 0.0)
    if estUseLtf and not na(estV) and estV.size() > 0
        for j = 0 to estV.size() - 1
            estCandle(eb, es, estK0, rowTicks, estO.get(j), estH.get(j), estL.get(j), estC.get(j), estV.get(j))
    else
        estCandle(eb, es, estK0, rowTicks, open, high, low, close, volume)
    array<EstRow> er = array.new<EstRow>()
    for i = 0 to n - 1
        er.push(EstRow.new(math.round_to_mintick((estK0 + i + 1) * rowTicks * syminfo.mintick),
             math.round_to_mintick((estK0 + i) * rowTicks * syminfo.mintick), eb.get(i), es.get(i)))
    fp := EstFootprint.new(er, eb.sum(), es.sum())
"""


def make_lite(suite):
    """The suite on estimated footprints: same tools, same drawings, data from candles."""
    s = suite
    s = s.replace("""// IDC Order Flow: the four IDCator order-flow tools in one indicator, each switched on or off in the settings""",
                  """// IDC Order Flow Lite: the four IDCator order-flow tools in one indicator, each switched on or off in the settings""")
    s = s.replace("""// Needs TradingView volume footprint data (request.footprint: Premium or Ultimate plan). Without such a plan, use
// IDC_OrderFlow_Lite.pine: the same tools on footprints estimated from candles, for every plan.
""", """// Runs on every TradingView plan: the footprints (buy and sell volume per price) are ESTIMATED from candles, see
// "estimated footprint" below. For real footprint data use IDC Order Flow (needs a Premium or Ultimate plan).
""")
    old_ind = ('indicator("IDC Order Flow", "IDC", overlay = true, max_boxes_count = 500, max_labels_count = 500, '
               'max_lines_count = 500,\n     max_polylines_count = 100)')
    assert s.count(old_ind) == 1
    s = s.replace(old_ind, 'indicator("IDC Order Flow Lite", "IDC Lite", overlay = true, max_boxes_count = 500, '
                           'max_labels_count = 500,\n     max_lines_count = 500, max_polylines_count = 100)')
    assert 'IDC_OrderFlow_Lite.pine: the same tools' not in s
    # type names of the footprint API -> the estimate's types (code only, not comments or strings)
    s = code_sub(s, lambda c: re.sub(r'(?<![\w.])volume_row(?!\w)', 'EstRow', re.sub(r'(?<![\w.])footprint(?!\w)', 'EstFootprint', c)))
    old_fp = 'EstFootprint fp = request.footprint(rowTicks)\n'
    assert s.count(old_fp) == 1
    s = s.replace(old_fp, '\n' + LITE_DATA)
    anchor = '\n// ───────────────────────────────────────────── footprint row size'
    assert s.count(anchor) == 1
    s = s.replace(anchor, '\n' + LITE_INPUT + anchor)
    s = s.replace('"IDC Order Flow: no footprint data (needs a Premium/Ultimate plan and a symbol with volume)"',
                  '"IDC Order Flow Lite: no volume data on this symbol"')
    s = s.replace('rowHint("IDC Order Flow", rowTicks,', 'rowHint("IDC Order Flow Lite", rowTicks,')
    s = s.replace("too many price rows per bar for the footprint - set", "too many price rows per bar - set")
    code = ''.join(t for t in TOKEN.findall(s) if not t.startswith('//'))
    assert 'request.footprint(' not in code and 'request.security_lower_tf' in code
    return s


def main():
    first = open(os.path.join(TV, 'IDC_Imbalance_Levels.pine')).read()
    shared_rows = first[first.index('// ───────────────────────────────────────────── footprint row size'):first.index('int rowTicks = rowTicksInput > 0 ? rowTicksInput : autoRow')]
    hint = first[first.index('// typical bar range of this chart, for the row size hint'):first.index('var table info = table.new(')]
    tip = re.search(r'int    rowTicksInput   = input\.int\(0, "Ticks per row \(0 = auto\)", minval = 0, group = G_DET,\n     (tooltip = "[^"]*")\)', first).group(1)
    parts, maps = [], {}
    for file, prefix, switch, title, on in TOOLS:
        body, mapping = tool_body(os.path.join(TV, file), prefix, switch, title)
        maps[prefix] = mapping
        parts.append('// ═════════════════════════════════════════════ %s (switch: %s)\n%s' % (title, switch, body.strip('\n')))
    switches = '\n'.join('bool %s = input.bool(%s, "%s", group = G_ON)' % (sw.ljust(5), 'true' if on else 'false', title)
                         for _, _, sw, title, on in TOOLS)
    m = maps
    suite = '''//@version=6
// IDC Order Flow: the four IDCator order-flow tools in one indicator, each switched on or off in the settings
// (Imbalance Levels, HVN Profile, Heavy Absorption, Pressure Toolkit). Clean-room replica of the tools shown in
// ProfitPro's videos; not their code. Not a buy/sell signal.
// Needs TradingView volume footprint data (request.footprint: Premium or Ultimate plan). Without such a plan, use
// IDC_OrderFlow_Lite.pine: the same tools on footprints estimated from candles, for every plan.
// GENERATED by tools/build_suite.py from the four single-tool scripts in this folder: edit those, then run the build.
indicator("IDC Order Flow", "IDC", overlay = true, max_boxes_count = 500, max_labels_count = 500, max_lines_count = 500,
     max_polylines_count = 100)
// HVN zones start at their period's first bar (up to 5000 bars back)
max_bars_back(time, 5000)

G_ON = "Tools (on/off)"
%s
int    rowTicksInput = input.int(0, "Ticks per row (0 = auto)", minval = 0, group = G_ON,
     %s)

%s
int rowTicks = rowTicksInput > 0 ? rowTicksInput : autoRow
footprint fp = request.footprint(rowTicks)
plot(rowTicks, "Ticks per row", display = display.data_window, editable = false)
float tick = syminfo.mintick

%s

// ═════════════════════════════════════════════ status
var bool anyFp = false
if not na(fp)
    anyFp := true

%s
var table info = table.new(position.top_right, 1, 1)
if barstate.islast
    string hint = rowHint("IDC Order Flow", rowTicks, imbOn or haOn or ptkOn)
    string msg  = not anyFp ? (str.length(hint) > 0 ? hint : "IDC Order Flow: no footprint data (needs a Premium/Ultimate plan and a symbol with volume)") :
         hvnOn and %s ? "IDC HVN: choose a profile timeframe higher than the chart timeframe" :
         haOn and not %s ? "IDC Heavy Absorption: collecting statistics (" + str.tostring(%s) + "/" + str.tostring(%s) + ")" :
         ptkOn and not %s ? "IDC Pressure Toolkit: collecting statistics" : hint
    table.cell(info, 0, 0, msg, text_color = color.orange, text_size = 9)
''' % (switches, tip, shared_rows.rstrip() + '\n', '\n\n'.join(parts), hint.rstrip() + '\n',
       m['hvn']['tfTooHigh'], m['ha']['ready'], m['ha']['totN'], m['ha']['minSamplesInput'], m['ptk']['ready'])
    open(OUT, 'w').write(suite)
    print('wrote %s (%d lines)' % (os.path.relpath(OUT, ROOT), suite.count('\n')))
    lite = make_lite(suite)
    open(OUT_LITE, 'w').write(lite)
    print('wrote %s (%d lines)' % (os.path.relpath(OUT_LITE, ROOT), lite.count('\n')))


if __name__ == '__main__':
    main()
