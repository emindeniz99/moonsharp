"""Aggregate run-versions.sh output (usage: aggregate_versions.py <outdir>): per-process median of 7 inner runs, then median and IQR across rounds."""
import glob, json, os, re, statistics, sys
if len(sys.argv) < 2:
    sys.exit(__doc__)
from collections import defaultdict

outdir = sys.argv[1]
ns = defaultdict(list); alloc = defaultdict(list); checks = defaultdict(set); fails = []
comp = defaultdict(lambda: [0, 0]); meta = {}
for path in sorted(glob.glob(os.path.join(outdir, "r*-*.jsonl"))):
    rnd, mode, label = re.match(r"r(\d+)-(\w+)-(\w+)\.jsonl", os.path.basename(path)).groups()
    for line in open(path):
        if not line.startswith("{"):
            continue
        o = json.loads(line)
        if o["kind"] == "meta":
            meta[label] = (o["runtime"], o["luacs"], o["moonsharp"])
        elif o["kind"] in ("workload", "boundary", "create"):
            key = (o["kind"], o["case"], label)
            ns[key].append(statistics.median(o["ns"]))
            alloc[key].append(statistics.median(o["alloc"]))
            checks[key].add(o["check"])
            if not o["ok"]:
                fails.append((path, o["case"], o["check"], o["expected"]))
        elif o["kind"] == "end":
            comp[label][0] += o["syncCompletions"]; comp[label][1] += o["asyncCompletions"]


def q(xs):
    xs = sorted(xs)
    if len(xs) < 4:
        return statistics.median(xs), xs[0], xs[-1]
    a, m, b = statistics.quantiles(xs, n=4, method="inclusive")
    return m, a, b


def fmt(x):
    return f"{x:,.1f}" if x < 1000 else f"{x:,.0f}"


def cell(k):
    m, a, b = q(ns[k])
    return f"{fmt(m)} ({fmt(a)}–{fmt(b)})"


print("meta:", meta)
cases = []
for (k, c, l) in ns:
    if (k, c) not in cases:
        cases.append((k, c))
print("rounds:", {l: len(v) for (k, c, l), v in ns.items() if c == cases[0][1]})
print()
print("| kind | case | 0.5.8 A | main@a8d84c7 | 0.5.8 B | 0.5.8A/main | A/A (A/B) | A/A per-round range | per-round 0.5.8A/main range | alloc B/op 0.5.8 | alloc B/op main |")
print("|---|---|---:|---:|---:|---:|---:|---|---|---:|---:|")
for k, c in cases:
    A, M, B = (k, c, "v058A"), (k, c, "main"), (k, c, "v058B")
    if not (A in ns and M in ns and B in ns):
        continue
    aa = [x / y for x, y in zip(ns[A], ns[B])]
    am = [x / y for x, y in zip(ns[A], ns[M])]
    print(f"| {k} | {c} | {cell(A)} | {cell(M)} | {cell(B)} | {q(ns[A])[0] / q(ns[M])[0]:.2f}x | {q(ns[A])[0] / q(ns[B])[0]:.3f} | "
          f"{min(aa):.2f}–{max(aa):.2f} | {min(am):.2f}–{max(am):.2f} | {statistics.median(alloc[A]):,.2f} | {statistics.median(alloc[M]):,.2f} |")
print()
print("checks differing between versions:")
for k, c in cases:
    s = {l: checks[(k, c, l)] for l in ("v058A", "main", "v058B") if (k, c, l) in checks}
    vals = set().union(*s.values())
    if len(vals) != 1:
        print(" ", k, c, s)
print("failures:", fails)
print("ValueTask completions (sync, async):", dict(comp))
