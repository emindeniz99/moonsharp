"""Geometric means of the ab.sh ratios. Usage: geomean.py [results dir, default ./results]"""
import os, re, sys, math
D = os.path.join(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results'), '')


def parse(n):
    r = {}
    for line in open(D + n):
        m = re.match(r'\| (\w+) \| ([^|]+) \| ([^|]+) \| ([\d.]+) \|', line)
        if m:
            r[m.group(1)] = float(m.group(4))
    return r


for f in ['aa_master.md', 'aa_a.md', 'master_vs_a.md', 'master_vs_b.md', 'master_vs_c.md', 'c_vs_a.md', 'c_vs_b.md',
          'a_vs_d.md', 'c_vs_d.md', 'master_vs_2c9e546.md']:
    r = parse(f)
    g = math.exp(sum(math.log(v) for v in r.values()) / len(r))
    ex = {k: v for k, v in r.items() if k != 'hash_delete_reinsert'}
    g2 = math.exp(sum(math.log(v) for v in ex.values()) / len(ex))
    print('%-22s geomean all %.3f | without hash_delete_reinsert %.3f | max |ratio-1| %.3f' % (f, g, g2, max(abs(v - 1) for v in r.values())))
