"""Compares ab.sh outputs. Usage: abtable.py <ab.sh output>... (variants base, A, C, D, E, J)"""
import re, sys, statistics
if len(sys.argv) < 2:
    sys.exit(__doc__)
runs={}  # variant -> case -> list of (median, min, bytes)
for f in sys.argv[1:]:
    v=None
    for l in open(f):
        m=re.match(r"### variant (\w+)", l)
        if m: v=m.group(1); continue
        m=re.match(r"\| (\w+) \| \d+ \| ([\d.]+) \| ([\d.]+) \| ([\d.]+) \|", l)
        if m and v:
            runs.setdefault(v,{}).setdefault(m.group(1),[]).append((float(m.group(2)),float(m.group(3)),float(m.group(4))))
order=["empty_loop","function_call","method_call","table_array_insert","table_array_read","table_hash_set_get","int_math","fib_recursive","string_format","coroutine_switch","closure_create_call","clr_interop","clr_interop_reflection"]
vs=[v for v in ["base","A","C","D","E","J"] if v in runs]
def fmt(x): return f"{x/1000:.1f}us" if x>=10000 else f"{x:.0f}"
print("| Case | " + " | ".join(f"{v} ns (min) / B" for v in vs) + " | E vs base (min ns) | E vs base (B) | J vs E (min ns) | J vs E (B) |")
print("|---|" + "---:|"*(len(vs)+4))
for c in order:
    cells=[]; mins={}; byt={}
    for v in vs:
        xs=runs[v].get(c)
        if not xs: cells.append(""); continue
        mn=min(x[1] for x in xs); md=statistics.median(x[0] for x in xs); b=xs[0][2]
        mins[v]=mn; byt[v]=b
        cells.append(f"{fmt(md)} ({fmt(mn)}) / {fmt(b)}")
    def pct(a,b): return f"{(b/a-1)*100:+.0f}%" if a and b else ""
    print(f"| {c} | " + " | ".join(cells) + f" | {pct(mins.get('base'),mins.get('E'))} | {pct(byt.get('base'),byt.get('E'))} | {pct(mins.get('E'),mins.get('J'))} | {pct(byt.get('E'),byt.get('J'))} |")
