"""Aggregate run-rounds.sh output (usage: aggregate.py <outdir>): per-round median of the inner runs, then median and IQR across rounds."""
import json, glob, os, re, statistics, sys
if len(sys.argv) < 2:
    sys.exit(__doc__)
from collections import defaultdict

outdir = sys.argv[1]
# (kind, case, label) -> list of per-round medians
ns = defaultdict(list); alloc = defaultdict(list); allocg = defaultdict(list); retained = defaultdict(list)
checks = defaultdict(set); fails = []; completions = defaultdict(lambda: [0, 0])

for path in sorted(glob.glob(os.path.join(outdir, "r*-*.jsonl"))):
    m = re.match(r"r(\d+)-(\w+)-(\w+)\.jsonl", os.path.basename(path))
    rnd, mode, label = m.groups()
    for line in open(path):
        line = line.strip()
        if not line.startswith("{"):
            continue
        o = json.loads(line)
        k = o["kind"]
        if k in ("workload", "boundary", "create"):
            key = (k, o["case"], label)
            ns[key].append(statistics.median(o["ns"]))
            alloc[key].append(statistics.median(o["alloc"]))
            allocg[key].append(statistics.median(o["allocGlobal"]))
            checks[(k, o["case"], label)].add(o["check"])
            if not o["ok"]:
                fails.append((path, o["case"], o["check"], o["expected"]))
        elif k == "retained":
            retained[(o["case"], label)].append(statistics.median(o["bytes"]))
        elif k == "end":
            completions[label][0] += o["syncCompletions"]; completions[label][1] += o["asyncCompletions"]


def q(xs):
    xs = sorted(xs)
    if len(xs) < 4:
        return statistics.median(xs), xs[0], xs[-1]
    q1, q2, q3 = statistics.quantiles(xs, n=4, method="inclusive")
    return q2, q1, q3


def fmt(x):
    return f"{x:,.1f}" if x < 1000 else f"{x:,.0f}"


def cell(key, d=ns):
    if key not in d:
        return "n/a"
    m, a, b = q(d[key])
    return f"{fmt(m)} ({fmt(a)}–{fmt(b)})"


cases = []
for (k, c, l) in ns:
    if (k, c) not in cases:
        cases.append((k, c))

print("rounds per series:", {f"{k}/{c}/{l}": len(v) for (k, c, l), v in ns.items() if c in ("empty_loop", "a_cs_to_lua_call3", "c_create_default", "c_create_stdlib", "c_create_complete")})
print()
print("| kind | case | msA ns med (IQR) | msB ns med (IQR) | luacs ns med (IQR) | msA/luacs | msA/msB (A/A) | A/A per-round ratio range |")
print("|---|---|---:|---:|---:|---:|---:|---|")
for k, c in cases:
    A, B, L = (k, c, "msA"), (k, c, "msB"), (k, c, "luacs")
    if k == "create":
        A, B = (k, c, "msA"), (k, c, "msB")
        if c == "c_create_complete":
            A, B = (k, c, "mscomplete"), None
    r = ""
    aa = ""; rng = ""
    if A in ns and L in ns:
        r = f"{q(ns[A])[0] / q(ns[L])[0]:.2f}x"
    if A in ns and B and B in ns:
        aa = f"{q(ns[A])[0] / q(ns[B])[0]:.3f}"
        per = [a / b for a, b in zip(ns[A], ns[B])]
        rng = f"{min(per):.3f}–{max(per):.3f}"
    print(f"| {k} | {c} | {cell(A)} | {cell(B) if B else ''} | {cell(L)} | {r} | {aa} | {rng} |")

# Native Lua 5.5.1 via xLua-NG flag_bench.c, copied from docs/comparison-xlua-ng.md (not re-measured here).
NATIVE = {"empty_loop": 3.5, "function_call": 26.0, "method_call": 66.3, "table_array_insert": 16.1, "table_array_read": 15.7,
          "table_hash_set_get": 35.8, "string_concat": 225.8, "string_format": 304.8, "closure_create_call": 94.0, "int_math": 11.3,
          "fib_recursive": 10085.0, "float_math": 20.8, "string_pattern": 3199.0, "table_sort_52": 51008.9, "table_concat": 869.2,
          "coroutine_switch": 118.9, "gc_small_tables": 128.8, "oop_inherit": 69.6}
print()
print("| Workload | native Lua 5.5.1 (doc) | MoonSharp | Lua-CSharp 0.5.8 | MoonSharp/Lua-CSharp | Lua-CSharp/native | MoonSharp/native |")
print("|---|---:|---:|---:|---:|---:|---:|")
for k, c in cases:
    if k != "workload":
        continue
    A, L = (k, c, "msA"), (k, c, "luacs")
    ma, la = q(ns[A])[0], q(ns[L])[0]
    nat = NATIVE.get(c)
    print(f"| {c} | {fmt(nat)} | {cell(A)} | {cell(L)} | {ma / la:.1f}x | {la / nat:.1f}x | {ma / nat:.1f}x |")

print()
print("| kind | case | label | alloc B/op thread (median of rounds) | alloc B/op process | rounds |")
print("|---|---|---|---:|---:|---:|")
for key in sorted(alloc):
    print(f"| {key[0]} | {key[1]} | {key[2]} | {statistics.median(alloc[key]):,.2f} | {statistics.median(allocg[key]):,.2f} | {len(alloc[key])} |")

print()
print("retained bytes per instance (median of rounds, min-max):")
for key, v in sorted(retained.items()):
    print(" ", key, f"{statistics.median(v):,.0f}", f"({min(v):,.0f}–{max(v):,.0f})", len(v))

print()
print("distinct checks:")
for key, v in sorted(checks.items()):
    print(" ", key, sorted(v))
print()
print("failures:", fails)
print("ValueTask completions (sync, async):", dict(completions))
