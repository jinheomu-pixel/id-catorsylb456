#!/usr/bin/env python3
"""Compile check and simulation of the IDCator MT5 (.mq5) indicators with a C++ compiler.

MetaEditor (the real MQL5 compiler) is not available here, so each indicator is translated to C++ and compiled
against a small MQL5 runtime (mql5_prelude.hpp):

    python3 tools/mql-check/check.py                    # g++ -fsyntax-only on all indicators
    python3 tools/mql-check/check.py --run IND.mq5 TICKS.csv OUT.json [driver options]

--run builds the indicator with the simulation driver (sim_main.inc) and plays a tick file through it like an
MT5 chart; see sim_main.inc for the driver options. The check catches unknown names, wrong argument counts and
type mismatches in our own code; MQL5-only rules are not checked.
"""
import hashlib
import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
INCLUDE = os.path.join(ROOT, 'MT5', 'Include')
INDICATORS = os.path.join(ROOT, 'MT5', 'Indicators', 'IDCator')
PRELUDE = os.path.join(HERE, 'mql5_prelude.hpp')
DRIVER = os.path.join(HERE, 'sim_main.inc')

TYPE = r'(?:const\s+)?(?:unsigned\s+)?[A-Za-z_]\w*'


def strip_comments(src):
    out, i, n = [], 0, len(src)
    while i < n:
        if src.startswith('//', i):
            j = src.find('\n', i)
            i = n if j < 0 else j
        elif src.startswith('/*', i):
            j = src.find('*/', i + 2)
            i = n if j < 0 else j + 2
        elif src[i] == '"':
            j = i + 1
            while j < n and src[j] != '"':
                j += 2 if src[j] == '\\' else 1
            out.append(src[i:j + 1])
            i = j + 1
        elif src[i] == "'":
            j = i + 1
            while j < n and src[j] != "'":
                j += 2 if src[j] == '\\' else 1
            out.append(src[i:j + 1])
            i = j + 1
        else:
            out.append(src[i])
            i += 1
    return ''.join(out)


def inline_includes(src, seen):
    def repl(m):
        path = os.path.join(INCLUDE, m.group(1))
        if path in seen:
            return ''
        seen.add(path)
        with open(path, encoding='ascii') as f:
            return inline_includes(f.read(), seen)
    return re.sub(r'#include\s*<([^>]+)>', repl, src)


def arr(t):
    t = t.strip()
    if t.startswith('const '):
        return 'const MqlArr<' + t[6:].strip() + '>'
    return 'MqlArr<' + t + '>'


def translate(src, sim=False):
    """MQL5 source -> C++ source. sim=True keeps inputs assignable and adds SimSetInput()."""
    src = strip_comments(inline_includes(src, set()))
    src = '\n'.join(l for l in src.split('\n') if not l.strip().startswith(('#property', 'input group')))
    inputs = re.findall(r'(?m)^\s*input\s+([A-Za-z_]\w*)\s+([A-Za-z_]\w*)\s*=', src)
    # colour literals C'r,g,b'
    src = re.sub(r"C'(\d+)\s*,\s*(\d+)\s*,\s*(\d+)'", r'MqlRgb(\1,\2,\3)', src)
    # inputs are read-only in MQL5 (the simulation sets them before OnInit)
    src = re.sub(r'(?m)^(\s*)input\s+', r'\1' if sim else r'\1const ', src)
    # string literals become MQL strings (MQL5 allows "a" + "b")
    src = re.sub(r'(?<![\w\'])"((?:[^"\\]|\\.)*)"', r'MqlString("\1")', src)
    # array parameters: T &name[]  ->  MqlArr<T> &name
    src = re.sub(r'(' + TYPE + r')\s*&\s*([A-Za-z_]\w*)\s*\[\s*\]', lambda m: arr(m.group(1)) + ' &' + m.group(2), src)
    # dynamic array declarations: T a[], b[];  ->  MqlArr<T> a, b;

    def decl(m):
        t, names = m.group(1), m.group(2)
        parts = [p.strip() for p in names.split(',')]
        if all(re.fullmatch(r'[A-Za-z_]\w*\s*\[\s*\]', p) for p in parts):
            return m.group(0)[:m.start(1) - m.start(0)] + arr(t) + ' ' + ', '.join(re.sub(r'\s*\[\s*\]', '', p) for p in parts) + ';'
        return m.group(0)
    src = re.sub(r'(?m)^[ \t]*(' + TYPE + r')\s+((?:[A-Za-z_]\w*\s*\[\s*\]\s*,\s*)*[A-Za-z_]\w*\s*\[\s*\])\s*;', decl, src)
    if sim:
        body = []
        for t, name in inputs:
            if t == 'double':
                conv = 'std::stod(v)'
            elif t == 'bool':
                conv = '(v == "true" || v == "1")'
            elif t == 'string':
                conv = 'MqlString(v)'
            else:
                conv = '(%s)std::stoll(v)' % t
            body.append('   if(n == "%s") { %s = %s; return true; }' % (name, name, conv))
        src += '\nstatic bool SimSetInput(const std::string &n, const std::string &v)\n  {\n%s\n   return false;\n  }\n' % '\n'.join(body)
    return src


def compile_check(files):
    failed = 0
    for path in files:
        with open(path, encoding='ascii') as f:
            src = f.read()
        cpp = '#include "%s"\n%s\n' % (PRELUDE, translate(src))
        with tempfile.NamedTemporaryFile('w', suffix='.cpp', delete=False) as tmp:
            tmp.write(cpp)
        r = subprocess.run(['g++', '-std=c++17', '-fsyntax-only', '-Wall', '-Wno-unused-variable', '-Wno-unused-but-set-variable',
                            '-Wno-comment', '-fmax-errors=40', tmp.name], capture_output=True, text=True)
        name = os.path.basename(path)
        if r.returncode == 0:
            warnings = [l for l in r.stderr.splitlines() if 'warning' in l]
            print('OK    %s%s' % (name, ('  (%d warnings)' % len(warnings)) if warnings else ''))
            for w in warnings[:20]:
                print('      ' + w.replace(tmp.name, name))
        else:
            failed += 1
            print('FAIL  %s' % name)
            for l in r.stderr.splitlines()[:80]:
                print('      ' + l.replace(tmp.name, name))
            keep = os.path.join(tempfile.gettempdir(), name + '.cpp')
            with open(keep, 'w') as f:
                f.write(cpp)
            print('      translated source: ' + keep)
        os.unlink(tmp.name)
    print('SUMMARY: %d of %d indicators pass' % (len(files) - failed, len(files)))
    return failed


def build_sim(path):
    """Builds (or reuses) the simulation binary of one indicator; returns its path."""
    with open(path, encoding='ascii') as f:
        src = f.read()
    cpp = '#include "%s"\n%s\n#include "%s"\n' % (PRELUDE, translate(src, sim=True), DRIVER)
    deps = cpp + open(PRELUDE).read() + open(DRIVER).read()
    key = hashlib.sha1(deps.encode()).hexdigest()[:16]
    cache = os.environ.get('IDC_SIM_CACHE') or os.path.join(tempfile.gettempdir(), 'idc-mql-sim')
    os.makedirs(cache, exist_ok=True)
    exe = os.path.join(cache, '%s-%s' % (os.path.splitext(os.path.basename(path))[0], key))
    if not os.path.exists(exe):
        cpp_file = exe + '.cpp'
        with open(cpp_file, 'w') as f:
            f.write(cpp)
        r = subprocess.run(['g++', '-std=c++17', '-O2', '-g', '-o', exe, cpp_file], capture_output=True, text=True)
        if r.returncode != 0:
            sys.stderr.write(r.stderr[:6000])
            raise SystemExit('build failed: ' + cpp_file)
    return exe


def main():
    args = sys.argv[1:]
    if args and args[0] == '--run':
        if len(args) < 4:
            raise SystemExit('usage: check.py --run IND.mq5 TICKS.csv OUT.json [driver options]')
        ind = args[1] if os.path.exists(args[1]) else os.path.join(INDICATORS, args[1])
        exe = build_sim(ind)
        return subprocess.run([exe] + args[2:]).returncode
    files = args or sorted(os.path.join(INDICATORS, f) for f in os.listdir(INDICATORS) if f.endswith('.mq5'))
    return compile_check(files)


if __name__ == '__main__':
    sys.exit(main())
