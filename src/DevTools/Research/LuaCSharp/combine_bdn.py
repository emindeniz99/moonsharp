"""Combine BenchmarkDotNet *-report-full-compressed.json from several runs.
Usage: combine_bdn.py <suite> <artifacts dir> <run labels...>
Per run: the BDN median (ns) and allocated bytes/op. Prints the median across runs, the run-to-run
range, and each engine relative to Lua-CSharp (median of per-run ratios)."""
import json, os, statistics, sys
if len(sys.argv) < 4:
    sys.exit(__doc__)
from collections import defaultdict

suite, root, runs = sys.argv[1], sys.argv[2], sys.argv[3:]
ENGINES = ["LuaCSharp", "NuLua52", "MoonSharp", "MoonSharpFork"]
med = defaultdict(dict); alloc = defaultdict(dict)
for run in runs:
    path = os.path.join(root, f"{run}-{suite}", "results", f"{suite}-report-full-compressed.json")
    if not os.path.exists(path):
        print("missing", path); continue
    for b in json.load(open(path))["Benchmarks"]:
        case = b.get("Parameters", "").replace("Case=", "") or "-"
        st = b.get("Statistics")
        if not st:
            print("no statistics:", run, b["Method"], case); continue
        med[(case, b["Method"])][run] = st["Median"]
        alloc[(case, b["Method"])][run] = b["Memory"]["BytesAllocatedPerOperation"]

cases = []
for (c, m) in med:
    if c not in cases:
        cases.append(c)


def us(x):
    return f"{x / 1000:,.1f}" if x >= 10000 else f"{x / 1000:,.2f}"


print(f"| Case | " + " | ".join(f"{e} µs (range)" for e in ENGINES) + " | " + " | ".join(f"{e}/LuaCSharp" for e in ENGINES[1:]) + " | MoonSharp NuGet ÷ fork (range) |")
print("|---|" + "---:|" * (len(ENGINES) * 2 - 1) + "---:|")
for c in cases:
    cells = []
    for e in ENGINES:
        v = list(med[(c, e)].values())
        cells.append(f"{us(statistics.median(v))} ({us(min(v))}–{us(max(v))})" if v else "n/a")
    ratios = []
    for e in ENGINES[1:]:
        rs = [med[(c, e)][r] / med[(c, "LuaCSharp")][r] for r in med[(c, e)] if r in med[(c, "LuaCSharp")]]
        ratios.append(f"{statistics.median(rs):.2f}x" if rs else "n/a")
    fs = [med[(c, "MoonSharp")][r] / med[(c, "MoonSharpFork")][r] for r in med[(c, "MoonSharpFork")] if r in med[(c, "MoonSharp")]]
    rng = f" ({min(fs):.2f}–{max(fs):.2f})" if fs else ""
    print(f"| {c} | " + " | ".join(cells) + " | " + " | ".join(ratios) + f" | {statistics.median(fs):.2f}x{rng} |")
print()
print("| Case | " + " | ".join(f"{e} B/op" for e in ENGINES) + " |")
print("|---|" + "---:|" * len(ENGINES))
for c in cases:
    print(f"| {c} | " + " | ".join(f"{statistics.median(alloc[(c, e)].values()):,.0f}" if alloc[(c, e)] else "n/a" for e in ENGINES) + " |")
print()
print("runs per cell:", {f"{c}/{e}": len(med[(c, e)]) for c in cases[:1] for e in ENGINES})
