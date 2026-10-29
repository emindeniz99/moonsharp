"""Aggregate rounds.sh output: per-round median of the inner runs, then median and IQR across rounds.
Usage: aggregate.py <outdir> [luacs-results-dir]
Lua-CSharp numbers come from ../LuaCSharp run-rounds.sh output (separate harness, same workload sources), label luacs."""
import json, glob, os, re, statistics, sys
if len(sys.argv) < 2:
    sys.exit(__doc__)
from collections import defaultdict

outdir = sys.argv[1]
luacs_dir = sys.argv[2] if len(sys.argv) > 2 else None
ns = defaultdict(list); alloc = defaultdict(list); retained = defaultdict(list)
checks = defaultdict(set); errors = []; fails = []
order = []

def ingest(path, label, rename=None):
    for line in open(path):
        line = line.strip()
        if not line.startswith("{"):
            continue
        o = json.loads(line)
        k = o["kind"]
        if k in ("workload", "interop", "boundary", "create"):
            case = o["case"]
            if rename: case = rename.get(case, case)
            key = (case, label)
            if case not in order: order.append(case)
            ns[key].append(statistics.median(o["ns"]))
            alloc[key].append(statistics.median(o["alloc"]))
            checks[key].add(o["check"])
            if not o["ok"]:
                fails.append((os.path.basename(path), case, o["check"], o["expected"]))
        elif k == "retained":
            retained[((rename or {}).get(o["case"], o["case"]), label)].append(statistics.median(o["bytes"]))
        elif k == "error":
            errors.append((os.path.basename(path), o["case"], o["error"]))

for path in sorted(glob.glob(os.path.join(outdir, "r*-*.jsonl"))):
    m = re.match(r"r(\d+)-(\w+)-([\w-]+)\.jsonl", os.path.basename(path))
    ingest(path, m.group(3))
if luacs_dir:
    for path in sorted(glob.glob(os.path.join(luacs_dir, "r*-*-luacs.jsonl"))):
        ingest(path, "luacs", {"c_create_stdlib": "c_create_default"})

labels = sorted({l for (_, l) in ns}, key=lambda l: (l != "msA", l != "msB", l != "ws", l))

def q(xs):
    xs = sorted(xs)
    if len(xs) < 4:
        return statistics.median(xs), xs[0], xs[-1]
    q1, q2, q3 = statistics.quantiles(xs, n=4, method="inclusive")
    return q2, q1, q3

def fmt(x):
    return f"{x:,.1f}" if x < 1000 else f"{x:,.0f}"

def cell(key):
    if key not in ns: return "n/a"
    m, a, b = q(ns[key]); return f"{fmt(m)} ({fmt(a)}–{fmt(b)})"

def med(key): return q(ns[key])[0] if key in ns else None

print("rounds:", {l: max(len(v) for (c, ll), v in ns.items() if ll == l) for l in labels})
print()
print("| case | " + " | ".join(f"{l} ns med (IQR)" for l in labels) + " |")
print("|---|" + "---:|" * len(labels))
for c in order:
    print(f"| {c} | " + " | ".join(cell((c, l)) for l in labels) + " |")
print()
print("Ratios of medians (>1 means the first is slower):")
pairs = [("msA", "ws"), ("msup", "ws"), ("ws", "luacs"), ("msA", "luacs"), ("msA", "msB")]
pairs = [p for p in pairs if p[0] in labels and p[1] in labels]
print("| case | " + " | ".join(f"{a}/{b}" for a, b in pairs) + " | A/A per-round range |")
print("|---|" + "---:|" * len(pairs) + "---|")
for c in order:
    cells = []
    for a, b in pairs:
        x, y = med((c, a)), med((c, b))
        cells.append(f"{x / y:.2f}" if x and y else "")
    rng = ""
    if (c, "msA") in ns and (c, "msB") in ns:
        per = [a / b for a, b in zip(ns[(c, "msA")], ns[(c, "msB")])]
        rng = f"{min(per):.3f}–{max(per):.3f}"
    print(f"| {c} | " + " | ".join(cells) + f" | {rng} |")
print()
print("Allocated bytes per iteration (current thread, median of per-round medians):")
print("| case | " + " | ".join(labels) + " |")
print("|---|" + "---:|" * len(labels))
for c in order:
    print(f"| {c} | " + " | ".join((f"{statistics.median(alloc[(c, l)]):,.1f}" if (c, l) in alloc else "n/a") for l in labels) + " |")
print()
print("Retained heap per instance (bytes):", {f"{c}/{l}": fmt(q(v)[0]) for (c, l), v in retained.items()})
print()
print("Checks (distinct values per series):")
for (c, l), v in sorted(checks.items()):
    print(f"  {c} {l}: {sorted(v)[:4]}")
print("Check failures:", fails)
print("Errors:", sorted(set((c, e) for _, c, e in errors)))
