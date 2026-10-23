#!/usr/bin/env python3
"""Syntax / type check of the IDCator MT5 (.mq5) indicators with a C++ compiler.

MetaEditor (the real MQL5 compiler) is not available here, so each indicator is translated to C++ against a stub of the
MQL5 API (mql5_prelude.hpp) and compiled with `g++ -fsyntax-only`. This catches unknown names, wrong argument counts and
type mismatches in our own code; MQL5-only rules are not checked.

    python3 tools/mql-check/check.py            # all indicators in MT5/Indicators/IDCator
"""
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
INCLUDE = os.path.join(ROOT, 'MT5', 'Include')
INDICATORS = os.path.join(ROOT, 'MT5', 'Indicators', 'IDCator')
PRELUDE = os.path.join(os.path.dirname(__file__), 'mql5_prelude.hpp')

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


def translate(src):
    src = strip_comments(inline_includes(src, set()))
    lines = []
    for line in src.split('\n'):
        s = line.strip()
        if s.startswith('#property') or s.startswith('input group'):
            continue
        lines.append(line)
    src = '\n'.join(lines)
    # colour literals C'r,g,b'
    src = re.sub(r"C'(\d+)\s*,\s*(\d+)\s*,\s*(\d+)'", r'MqlRgb(\1,\2,\3)', src)
    # inputs are read-only in MQL5
    src = re.sub(r'^(\s*)input\s+', r'\1const ', src, flags=re.M)
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
    return src


def arr(t):
    t = t.strip()
    if t.startswith('const '):
        return 'const MqlArr<' + t[6:].strip() + '>'
    return 'MqlArr<' + t + '>'


def main():
    files = sys.argv[1:] or sorted(os.path.join(INDICATORS, f) for f in os.listdir(INDICATORS) if f.endswith('.mq5'))
    failed = 0
    for path in files:
        with open(path, encoding='ascii') as f:
            src = f.read()
        cpp = '#include "%s"\n%s\nstring _Symbol; ENUM_TIMEFRAMES _Period; double _Point; int _Digits;\n' % (PRELUDE, translate(src))
        with tempfile.NamedTemporaryFile('w', suffix='.cpp', delete=False) as tmp:
            tmp.write(cpp)
        r = subprocess.run(['g++', '-std=c++17', '-fsyntax-only', '-Wall', '-Wno-unused-variable', '-Wno-unused-but-set-variable',
                            '-Wno-comment', '-ferror-limit=50' if False else '-fmax-errors=40', tmp.name],
                           capture_output=True, text=True)
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


if __name__ == '__main__':
    sys.exit(main())
