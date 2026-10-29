# Writes EndToEnd/StringFormatGolden.cs: string.format cases with the results reference Lua 5.2 gives
# (lua5.2 on PATH, 5.2.4). StringFormatTests.StringFormat_MatchesGoldenFile replays them on MoonSharp.
#   python3 -I gen_format_golden.py
# Left out, because they differ on purpose: %s and %q of numbers (MoonSharp's number text, the tostring
# question in docs/research/lua52-conformance.md), strings with characters above 127 (Lua counts bytes,
# MoonSharp UTF-16 characters) and %c of 256 to 65535 (that UTF-16 character in MoonSharp, the low byte in
# Lua). One glibc bug is replaced by C's rule: %#g whose rounding carries into a new digit drops the zeros
# '#' keeps ("1.e+06" for 999999.5; C99 7.19.6.1 and Python give "1.00000e+06").
import itertools
import math
import os
import subprocess
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'EndToEnd', 'StringFormatGolden.cs')

FLAGS = ['', '-', '+', ' ', '#', '0', '-0', '0-', '+0', ' 0', '#0', '+ ', '-+', '-#', '#-', '+-', ' -', '-+ #0', '00', '0#']
WIDTHS = ['', '1', '5', '12', '30']
PRECISIONS = ['', '.', '.0', '.1', '.3', '.10', '.20']
NUMERIC = 'd i u c x X o e E f g G a A'.split()
STRINGS_CONV = ['s', 'q']
NUMBERS = [0.0, -0.0, 1.0, -1.0, 7.0, 42.0, -42.0, 65.0, 255.0, 4096.0, 0.5, -0.5, 0.1, 2.5, -2.5, 3.14159265358979,
           1.0 / 3.0, 99.999, 1.005, 0.125, 123456.789, 1e-5, 1e-300, 5e-324, 1e15, 1e16, 1e20, -1e20, 1e100, 1e308,
           -1e308, 2147483648.0, 4294967296.0, -4294967296.0, 9007199254740991.0, 9007199254740992.0,
           9223372036854775808.0, -9223372036854775808.0, 18446744073709551616.0, 999999.5,
           float('nan'), float('inf'), float('-inf')]
STRINGS = ['', 'a', 'abc', 'hello world', '1.5', ' 0x10 ', 'a\0b', '\0', '\x001', 'quote"back\\slash', '\r\n\t',
           '\x01\x012\x0f9\x10\x1e\x7f', 'x' * 99, 'y' * 100, 'z' * 150, 'tab\there' + 'w' * 120 + '\0tail']


def n(d):
    return ('n', float(d))


def s(t):
    return ('s', t)


HANDWRITTEN = [
    ('%d:%s', [n(499968), s('x')]),
    ('%s %s', [s('1'), s('2'), s('3')]),
    ('%5.2s|%-5d|%05.1f|%x|%%|%c', [s('abcdef'), n(-3), n(-2.25), n(255), n(66)]),
    ('pi = %.4f', [n(math.pi)]),
    ('%02d/%02d/%04d', [n(5), n(11), n(1990)]),
    ('%X %x', [n(126), n(126)]),
    ('%% %c %%', [n(65)]),
    ('no specifiers at all', []),
    ('', []),
    ('100%%', []),
    ('a\0b%sc\0', [s('\0')]),
    ('%5.3s|%.0s|%.2s', [s('abcdef'), s('abc'), s('abc')]),
    ('%.10s', [s('abc')]),
    ('%q', [s('a string with "quotes" and \n new line')]),
    ('%q', [s('a string with \b and \b2')]),
    ('%q', [s('\0\0001\r\n\\"\x07\x10')]),
    ('%d', [s('10')]),
    ('%d', [s(' 0x10 ')]),
    ('%5.1f', [s('1e2')]),
    ('%x', [s('255')]),
    ('%c', [s('65')]),
    ('%s %s', [n(1)]),
    ('%d', []),
    ('%d', [s('toto')]),
    ('%d', [('b', True)]),
    ('%d', [('nil', None)]),
    ('%f', [('table', None)]),
    ('%s', [('b', False)]),
    ('%s', [('nil', None)]),
    ('%k', [s('toto')]),
    ('%------s', [s('toto')]),
    ('%-----s', [s('toto')]),
    ('pi = %.123f', [n(math.pi)]),
    ('% 123s', [s('toto')]),
    ('%100d', [n(1)]),
    ('%5', [n(1)]),
    ('%', [n(1)]),
    ('abc%', []),
    ('%5%', [n(1)]),
    ('%p', [n(1)]),
    ('%n', [n(1)]),
    ('%ld', [n(1)]),
    ('%hd', [n(1)]),
    ('%\0d', [n(1)]),
    ('%c', [n(-1)]),
    ('%c', [n(65536)]),
    ('%c', [n(-0.5)]),
    ('%c', [n(0)]),
    ('%+00d', [n(1)]),
    ('%#00x', [n(1)]),
    ('%#0o', [n(1)]),
    ('%#00o', [n(0)]),
    ('%#05o', [n(0)]),
    ('%{', [n(1)]),
    ('%-\0d', [n(1)]),
    ('%-0c', [n(65)]),
    ('%0-s', [s('abc')]),
    ('%99.99f', [n(-1e308)]),
    ('%+99.99f', [n(1e308)]),
    ('%99.99e', [n(1 / 3)]),
    ('%.99g', [n(0.1)]),
    ('%99s', [s('a' * 99)]),
    ('%-99.99s', [s('a' * 200)]),
    ('%c%c%c', [n(72), n(105), s('33')]),
]


def generated():
    i = 0
    for conv in NUMERIC:
        for f, w, p in itertools.product(FLAGS, WIDTHS, PRECISIONS):
            for d in NUMBERS:
                # %c of 256 to 65535 is that UTF-16 character in MoonSharp, the low byte in Lua.
                if conv == 'c' and 256 <= d < 65536:
                    continue
                if i % 149 == 0:
                    yield ('%' + f + w + p + conv, [n(d)])
                i += 1
    for conv in STRINGS_CONV:
        for f, w, p in itertools.product(FLAGS, WIDTHS, PRECISIONS):
            for t in STRINGS:
                if i % 23 == 0:
                    yield ('%' + f + w + p + conv, [s(t)])
                i += 1


def lua_string(t):
    return '"' + ''.join(c if 32 <= ord(c) < 127 and c not in '"\\' else '\\%03d' % ord(c) for c in t) + '"'


def lua_value(a):
    kind, v = a
    if kind == 'n':
        if math.isnan(v):
            return '(0/0)'
        if math.isinf(v):
            return '(1/0)' if v > 0 else '(-1/0)'
        if v == 0:
            return '(-0.0)' if math.copysign(1, v) < 0 else '0'
        return '(%s)' % v.hex()
    if kind == 's':
        return lua_string(v)
    if kind == 'b':
        return 'true' if v else 'false'
    if kind == 'nil':
        return 'nil'
    return '{}'


def encode(a):
    # StringFormatTests.EncodeArgs
    kind, v = a
    if kind == 'n':
        if math.isnan(v):
            return 'n:nan'
        if math.isinf(v):
            return 'n:inf' if v > 0 else 'n:-inf'
        if v == 0 and math.copysign(1, v) < 0:
            return 'n:-0'
        return 'n:' + repr(v)
    if kind == 's':
        return 's:' + v
    if kind == 'b':
        return 'b:true' if v else 'b:false'
    return kind


def c_rule(fmt, args, result):
    """C's %#g where glibc drops the zeros after a carry; None to keep Lua's result."""
    if len(args) != 1 or args[0][0] != 'n' or not fmt.endswith(('g', 'G')) or '#' not in fmt:
        return None
    d = args[0][1]
    if math.isnan(d) or math.isinf(d):
        return None
    return 'ok:' + (fmt % d)


def csharp(t):
    out = []
    for ch in t:
        o = ord(ch)
        if ch == '"':
            out.append('\\"')
        elif ch == '\\':
            out.append('\\\\')
        elif o < 0x20 or o >= 0x7f:
            out.append('\\u%04x' % o)
        else:
            out.append(ch)
    return '"' + ''.join(out) + '"'


def main():
    cases = list(generated()) + HANDWRITTEN
    lines = ['local function str(r) local o = {} for i = 1, #r do o[i] = string.format("%03d", r:byte(i)) end return table.concat(o) end']
    for fmt, args in cases:
        call = ', '.join([lua_string(fmt)] + [lua_value(a) for a in args])
        lines.append('do local ok, r = pcall(string.format, %s) io.write(ok and "ok:" or "err:", str(ok and r or tostring(r)), "\\n") end' % call)
    with tempfile.NamedTemporaryFile('w', suffix='.lua', delete=False) as f:
        f.write('\n'.join(lines) + '\n')
        path = f.name
    try:
        raw = subprocess.run(['lua5.2', path], capture_output=True, check=True).stdout.decode('ascii').split('\n')
    finally:
        os.remove(path)

    rows = []
    for (fmt, args), line in zip(cases, raw):
        tag, digits = line[:3], line[3:].lstrip(':')
        text = ''.join(chr(int(digits[k:k + 3])) for k in range(0, len(digits), 3))
        if tag == 'ok:':
            result = 'ok:' + text
            fixed = c_rule(fmt, args, result)
            if fixed is not None:
                result = fixed
        else:
            result = 'err:ScriptRuntimeException:' + text.replace("'string.format'", "'format'")
        rows.append('\t\t\t%s, %s, %s,' % (csharp(fmt), csharp('\x1f'.join(encode(a) for a in args)), csharp(result)))

    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('// Generated by Lua52Differential/gen_format_golden.py from lua5.2 (Lua 5.2.4); do not edit by hand.\n')
        f.write('namespace MoonSharp.Interpreter.Tests.EndToEnd\n{\n\tinternal static class StringFormatGolden\n\t{\n')
        f.write('\t\t// Triples: format, arguments (see StringFormatTests.EncodeArgs), result ("ok:" or "err:").\n')
        f.write('\t\tinternal static readonly string[] Data =\n\t\t{\n')
        f.write('\n'.join(rows) + '\n')
        f.write('\t\t};\n\t}\n}\n')
    print('%d cases' % len(rows))


main()
