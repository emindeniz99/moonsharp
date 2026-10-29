"""Builds the A/B tables from ab.sh outputs. Usage: summarize.py [results dir, default ./results]"""
import re, os, sys
D = os.path.join(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results'), '')


def parse(name):
    rows = {}
    p = D + name
    if not os.path.exists(p):
        return rows
    for line in open(p):
        m = re.match(r'\| (\w+) \| ([^|]+) \| ([^|]+) \| ([\d.]+) \| ([\d.]+) to ([\d.]+) \| ([^|]+) \| ([^|]+) \|', line)
        if m:
            rows[m.group(1)] = dict(a=m.group(2).strip(), b=m.group(3).strip(), ratio=float(m.group(4)),
                                    lo=float(m.group(5)), hi=float(m.group(6)), aa=m.group(7).strip(), ba=m.group(8).strip())
    return rows


runs = ['aa_master.md', 'aa_a.md', 'master_vs_a.md', 'master_vs_b.md', 'master_vs_c.md', 'c_vs_a.md', 'c_vs_b.md',
        'a_vs_d.md', 'c_vs_d.md', 'master_vs_2c9e546.md']
data = {r: parse(r) for r in runs}
names = list(data['master_vs_a.md'].keys()) or list(data['aa_master.md'].keys())

print('| Workload | A/A master | A/A a | master | (a) | (b) | (c) | alloc master | alloc a | alloc b | alloc c |')
print('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
for n in names:
    def g(r, k):
        x = data[r].get(n)
        return x[k] if x else '-'

    def rr(r):
        x = data[r].get(n)
        return '%.3f (%.3f-%.3f)' % (x['ratio'], x['lo'], x['hi']) if x else '-'
    print('| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |' % (
        n, rr('aa_master.md'), rr('aa_a.md'), g('master_vs_a.md', 'a'), g('master_vs_a.md', 'b'), g('master_vs_b.md', 'b'),
        g('master_vs_c.md', 'b'), g('master_vs_a.md', 'aa'), g('master_vs_a.md', 'ba'), g('master_vs_b.md', 'ba'), g('master_vs_c.md', 'ba')))

print()
print('| Workload | (a)/master | (b)/master | (c)/master | (a)/(c) direct | (b)/(c) direct | (d, a mode)/(a) | (d, c mode)/(c) | 2c9e546/master |')
print('|---|---:|---:|---:|---:|---:|---:|---:|---:|')
for n in names:
    def rr(r):
        x = data[r].get(n)
        return '%.3f (%.3f-%.3f)' % (x['ratio'], x['lo'], x['hi']) if x else '-'
    print('| %s | %s | %s | %s | %s | %s | %s | %s | %s |' % (n, rr('master_vs_a.md'), rr('master_vs_b.md'), rr('master_vs_c.md'),
          rr('c_vs_a.md'), rr('c_vs_b.md'), rr('a_vs_d.md'), rr('c_vs_d.md'), rr('master_vs_2c9e546.md')))
