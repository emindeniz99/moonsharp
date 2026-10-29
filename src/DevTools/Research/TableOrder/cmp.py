"""Compares the master section of runlua-all.sh outputs with variants a, b, c.
Usage: cmp.py <dir> <name>... (reads <dir>/<name>.out)"""
import os, re, sys
if len(sys.argv) < 3:
    sys.exit(__doc__)
D = os.path.join(sys.argv[1], '')
for name in sys.argv[2:]:
    txt = open(D + name + '.out').read()
    parts = re.split(r'^== (\w+)\n', txt, flags=re.M)
    runs = {parts[i]: parts[i + 1].splitlines() for i in range(1, len(parts), 2)}
    m = runs['master']
    print(name, {k: len(v) for k, v in runs.items()})
    for v in 'abc':
        diff = [i for i, (x, y) in enumerate(zip(m, runs[v])) if x != y]
        print('  ', v, 'differing lines:', len(diff))
        if diff:
            print('     master:', m[diff[0]][:300])
            print('     ' + v + '     :', runs[v][diff[0]][:300])
