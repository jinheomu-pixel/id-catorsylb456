#!/usr/bin/env python3
"""Builds TradingView/IDC_OrderFlow.pine, the four IDCator tools in one indicator with on/off switches, from the four
single-tool scripts (the source of truth: edit those, then run this).

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

# file, prefix, switch, title (switch label and group prefix); order = drawing order (HVN zones under the others)
TOOLS = [
    ('IDC_HVN_Profile.pine', 'hvn', 'hvnOn', 'HVN Profile', True),
    ('IDC_Imbalance_Levels.pine', 'imb', 'imbOn', 'Imbalance Levels', True),
    ('IDC_Heavy_Absorption.pine', 'ha', 'haOn', 'Heavy Absorption', True),
    ('IDC_Pressure_Toolkit.pine', 'ptk', 'ptkOn', 'Pressure Toolkit', False),
]
SHARED = {'fp', 'tick', 'rowTicks', 'coinPrice', 'niceRow', 'autoTicks', 'anyFp', 'typRange', 'rowHint', 'rowTicksInput'}
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


def main():
    first = open(os.path.join(TV, 'IDC_Imbalance_Levels.pine')).read()
    shared_rows = first[first.index('// ───────────────────────────────────────────── footprint row size'):first.index('int rowTicks = autoTicks(rowTicksInput)')]
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
// Needs TradingView volume footprint data (request.footprint: Premium or Ultimate plan).
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
int rowTicks = autoTicks(rowTicksInput)
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


if __name__ == '__main__':
    main()
