"""Per-workload tables from run-all.sh analyses. Usage: summarize.py <tracedir>"""
import sys, re, os
if len(sys.argv) < 2:
    sys.exit(__doc__)
T = sys.argv[1]
order = ["empty_loop","function_call","method_call","table_array_insert","table_array_read","table_hash_set_get","int_math","fib_recursive","string_format","coroutine_switch","closure_create_call","clr_interop","clr_interop_reflection"]
def section(lines, start, stop, n):
    out=[]; on=False
    for l in lines:
        if l.startswith(start): on=True; continue
        if on and stop and l.startswith(stop): break
        if on and l.strip():
            out.append(l.strip())
            if len(out)>=n: break
    return out
for w in order:
    f=os.path.join(T, w+".analysis.txt")
    if not os.path.exists(f): continue
    L=open(f).read().splitlines()
    print(f"#### {w}\n")
    print("_" + L[0] + "_\n")
    print("Top self (exclusive) methods:\n")
    for x in section(L,"Self (exclusive)","Source lines",8):
        m=re.match(r"([\d.]+)%\s+(.*)",x); print(f"- {m.group(1)}% `{m.group(2)}`")
    print("\nTop source lines (first MoonSharp frame; inlined callees are charged to the call-site line):\n")
    for x in section(L,"Source lines","Inclusive",10):
        m=re.match(r"([\d.]+)%\s+(\S+)\s+(.*)",x); print(f"- {m.group(1)}% `{m.group(2)}` ({m.group(3).replace('MS.','')})")
    print("\nTop allocation sites (share of sampled bytes; type @ allocating frame):\n")
    for x in section(L,"Allocation sites",None,10):
        m=re.match(r"([\d.]+)%\s+(.*)",x)
        if float(m.group(1))<0.5: break
        print(f"- {m.group(1)}% `{m.group(2).replace('MS.','')}`")
    print()
