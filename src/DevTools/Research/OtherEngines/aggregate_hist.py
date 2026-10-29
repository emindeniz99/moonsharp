"""History checkpoints: median of per-round medians (ns and B/iter), in checkpoint order, plus step ratios.
Usage: aggregate_hist.py <outdir>"""
import json, glob, os, re, statistics, sys
if len(sys.argv) < 2:
    sys.exit(__doc__)
from collections import defaultdict

ORDER = ["ws-ccba46b", "ws-abbf807", "ws-2127ac9", "ws-1daaff1", "ws-2b55309", "ws-c4a2cc1", "ws-5079260", "ws-792aab7", "ws-df931b6", "ws"]
ns = defaultdict(list); alloc = defaultdict(list); cases = []; bad = []
for path in sorted(glob.glob(os.path.join(sys.argv[1], "r*-*.jsonl"))):
    label = re.match(r"r\d+-\w+?-(ws.*)\.jsonl", os.path.basename(path)).group(1)
    for line in open(path):
        if not line.startswith("{"): continue
        o = json.loads(line)
        if "ns" in o:
            if o["case"] not in cases: cases.append(o["case"])
            ns[(o["case"], label)].append(statistics.median(o["ns"]))
            alloc[(o["case"], label)].append(statistics.median(o["alloc"]))
            if not o["ok"]: bad.append((label, o["case"], o["check"], o["expected"]))
        elif o["kind"] == "error":
            bad.append((label, o["case"], o["error"]))

def m(d, k): return statistics.median(d[k]) if k in d else None
def f(x): return "n/a" if x is None else (f"{x:,.1f}" if x < 1000 else f"{x:,.0f}")

print("rounds:", {l: max((len(v) for (c, ll), v in ns.items() if ll == l), default=0) for l in ORDER})
print("\nns/iter (median of rounds); each cell after the first: value (ratio to previous checkpoint)")
print("| case | " + " | ".join(ORDER) + " |")
print("|---|" + "---:|" * len(ORDER))
for c in cases:
    row = []; prev = None
    for l in ORDER:
        v = m(ns, (c, l))
        row.append(f(v) if prev is None or v is None else f"{f(v)} ({v / prev:.2f})")
        prev = v if v is not None else prev
    print(f"| {c} | " + " | ".join(row) + " |")
print("\nB/iter (median of rounds)")
print("| case | " + " | ".join(ORDER) + " |")
print("|---|" + "---:|" * len(ORDER))
for c in cases:
    print(f"| {c} | " + " | ".join(f(m(alloc, (c, l))) for l in ORDER) + " |")
print("\nfailures/errors:", bad)
