# Copies the official Lua 5.2 test suite (lua-5.2.2-tests.tar.gz from
# https://www.lua.org/tests/, sha256
# 07c1071c66d9cb4dc80200ed430fc21ebbbccf1f55148b87eb0b9522932e4eb6) with every
# assert(...) call rewritten to __A(<line>, ...): a failing assertion prints
# "ASSERTFAIL <line>" and the file goes on. The file ends by printing
# "ASSERTCOUNT <n>" (assertions executed) and "DONE <file>".
#   python3 -I soften.py <suite dir> <output dir> [workarounds.txt]
# workarounds.txt blanks line ranges ("file:first-last  # reason") and replaces
# single lines ("file@line: new text"), so a file that stops at an error can run
# past it; line numbers stay those of the original file. Run each output file
# with its directory as the working directory, with lua5.2 and with MoonSharp.
import re, sys, os
src, dst = sys.argv[1], sys.argv[2]
skips = {}
repl = {}
if len(sys.argv) > 3:
    for l in open(sys.argv[3]):
        l = l.split('  #')[0].rstrip('\n')
        if not l or l.startswith('#'): continue
        if '@' in l.split(':')[0]:
            f, rest = l.split('@', 1)
            ln, text = rest.split(':', 1)
            repl.setdefault(f, {})[int(ln)] = text.strip()
            continue
        f, r = l.split(':')
        a, b = (r.split('-') + [r])[:2]
        skips.setdefault(f, []).append((int(a), int(b)))
os.makedirs(dst, exist_ok=True)
PRE = ('_U=true;_soft=true;_port=true;_no32=true;_nomsg=false;_noposix=true;_nolonglong=true;_noformatA=true;'
       'local __S={n=0};local __w=io.write;local __A=function(l,c,...) __S.n=__S.n+1 if not c then __w("ASSERTFAIL ",l,"\\n") end return c,... end;'
       '_G.__A=__A;')
pat = re.compile(r'(?<![\w.:])assert\s*\(')
for name in sorted(os.listdir(src)):
    if not name.endswith('.lua'):
        continue
    lines = open(os.path.join(src, name), encoding='latin-1').read().split('\n')
    out = []
    n = 0
    for i, line in enumerate(lines, 1):
        if i in repl.get(name, {}):
            line = repl[name][i]
        if any(a <= i <= b for a, b in skips.get(name, [])):
            out.append('')
            continue
        line, k = pat.subn(lambda m: '__A(%d,' % i, line)
        line = re.sub(r'__A\((\d+),\s*\)', r'__A(\1)', line)
        n += k
        out.append(line)
    out[0] = PRE + ('' if out[0].startswith('#') else out[0])
    tail = '__w("ASSERTCOUNT ", __S.n, "\\n") print("DONE ' + name + '") '
    last = max(i for i, l in enumerate(out) if l.strip())
    if re.match(r'return\b', out[last]):
        out[last] = tail + out[last]
    else:
        out.append(tail)
    open(os.path.join(dst, name), 'w', encoding='latin-1').write('\n'.join(out))
    print(name, n)
