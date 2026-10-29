"""Workload table per variant from chain.sh. Usage: wl.py [results dir, default ./results]"""
import os, re, sys
D = os.path.join(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results'), '')
vs = ['master', 'a', 'b', 'c']
data, meta = {}, {}
for v in vs:
    rows = {}
    for line in open(D + 'workloads_%s.md' % v):
        if line.startswith('# 20'):
            meta[v] = line.strip()
        m = re.match(r'\| (\w+) \| (\d+) \| ([\d.]+) \| ([\d.]+) \| ([\d.]+) \| ([^|]*) \|', line)
        if m:
            rows[m.group(1)] = (float(m.group(3)), float(m.group(4)), float(m.group(5)), m.group(6).strip())
    data[v] = rows
for v in vs:
    print(meta.get(v))
print('| Case | master | (a) | (b) | (c) | (a)/master | (c)/master |')
print('|---|---:|---:|---:|---:|---:|---:|')
for k in data['master']:
    r = [data[v].get(k) for v in vs]
    if None in r:
        continue
    print('| %s | %.1f | %.1f | %.1f | %.1f | %.2f | %.2f |' % (k, r[0][0], r[1][0], r[2][0], r[3][0], r[1][0] / r[0][0], r[3][0] / r[0][0]))
checks = {v: {k: x[3] for k, x in data[v].items()} for v in vs}
print('checks equal across variants:', all(checks[v] == checks['master'] for v in vs))
