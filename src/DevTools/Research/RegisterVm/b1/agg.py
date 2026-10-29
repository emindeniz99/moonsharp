"""Aggregates run.sh output. Usage: agg.py <outdir of run.sh>"""
import glob, os, re, statistics, sys
if len(sys.argv) < 2:
    sys.exit(__doc__)
R = sys.argv[1]
data = {}  # (runtime, engine) -> {case: {round: ns}}
cases = []
for f in glob.glob(os.path.join(R, '*.tsv')):
    m = re.match(r'(\d+)-(aot|mono)_(\w+)\.tsv', os.path.basename(f))
    r, rt, eng = int(m.group(1)), m.group(2), m.group(3)
    for line in open(f):
        c, v = line.split('\t')
        if c not in cases: cases.append(c)
        data.setdefault((rt, eng), {}).setdefault(c, {})[r] = float(v)
def q(xs, p): xs = sorted(xs); return xs[int(p * (len(xs) - 1) + 0.5)]
for rt in ('aot', 'mono'):
    engs = [e for e in ('next', 'next2', 'spike', 'spikeoff', 'luacs') if (rt, e) in data]
    rounds = sorted(data[(rt, 'next')][cases[0]])
    print(f'## {rt}: {len(rounds)} rounds; median of rounds (each round: median of 3 runs in one process)')
    print('case\t' + '\t'.join(engs))
    for c in cases:
        row = []
        for e in engs:
            v = statistics.median(data[(rt, e)][c].values())
            row.append(f'{v/1000:.1f}us' if v > 10000 else f'{v:.1f}')
        print(c + '\t' + '\t'.join(row))
    print('ratios to next: median of per-round ratios (interquartile range)')
    print('case\t' + '\t'.join(e + '/next' for e in engs[1:]))
    for c in cases:
        row = []
        for e in engs[1:]:
            rs = [data[(rt, e)][c][r] / data[(rt, 'next')][c][r] for r in rounds if r in data[(rt, e)][c]]
            row.append(f'{statistics.median(rs):.3f} ({q(rs,.25):.3f}-{q(rs,.75):.3f})')
        print(c + '\t' + '\t'.join(row))
    if (rt, 'luacs') in data:
        print('spike/luacs: ' + ', '.join(f"{c} {statistics.median([data[(rt,'spike')][c][r]/data[(rt,'luacs')][c][r] for r in rounds]):.2f}" for c in cases))
    print()
