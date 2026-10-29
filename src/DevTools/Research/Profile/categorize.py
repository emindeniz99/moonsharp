"""Groups allocation sites into categories. Usage: categorize.py <tracedir> <Profile bench output>"""
import re, os, sys
if len(sys.argv) < 3:
    sys.exit(__doc__)
T=sys.argv[1]
bytes_per_iter = {}
for l in open(sys.argv[2]):
    m=re.match(r"\| (\w+) \| \d+ \| [\d.]+ \| [\d.]+ \| ([\d.]+) \|", l)
    if m: bytes_per_iter[m.group(1)]=float(m.group(2))
IL="Processor_InstructionLoop.cs:"
def cat(t, site):
    def has(*xs): return any((IL+str(x)+")") in site.split("<-")[0] for x in xs)
    first = site.split("<-")[0]
    if t=="MS.DynValue" and has(201): return "1 local read clone (AsReadOnly, :201)"
    if t=="MS.DynValue" and has(204): return "2 upvalue read clone (AsReadOnly, :204)"
    if t=="MS.DynValue" and (has(211) or "AssignLocal" in first): return "3 fresh local cell after Clean (StoreLcl->AssignLocal NewNil, :211/:360)"
    if t=="MS.DynValue" and "ExecArgs" in first: return "4 ExecArgs cell + CloneAsWritable (:645-661)"
    if t=="MS.DynValue" and has(743): return "5 argument-count DynValue (:743)"
    if "CallStackItem" in t and "[]" not in t: return "6 CallStackItem per call (:713/:744)"
    if "Slice" in t: return "7 Slice<DynValue> args view (:629/:639/:705)"
    if t=="MS.DynValue[]" and "ExecBeginFn" in first: return "8 LocalScope array per call (:587)"
    if t=="MS.DynValue" and any(x in first for x in ["ExecAdd","ExecSub","ExecMul","ExecMod","ExecDiv"]) : return "9 arithmetic result NewNumber (:907/:928/:950/:973)"
    if t=="MS.DynValue" and (any(x in first for x in ["ExecLess","ExecNot","ExecCNot","ExecEq","NewBoolean"]) or has(162)): return "10 boolean result NewBoolean (:162/:573/:579/:1096/:1121/:1125)"
    if t=="MS.DynValue" and has(1323,1349): return "11 table/userdata read clone (AsReadOnly, :1323/:1349)"
    if "Table" in first or has(1241,1252) or "LinkedList" in t or "Entry[" in t or (t=="System.Int32[]" and "Table" in site): return "12 table key DynValue / LinkedListNode / Dictionary growth"
    if "Closure" in first or "ClosureContext" in t or "SelectArrayIterator" in t or "Func`2[MS.SymbolRef" in t or "MS.Closure"==t: return "13 closure creation (Linq + ClosureContext)"
    if any(x in t for x in ["ScriptExecutionContext","CallbackArguments","YieldRequest"]) or ("DynValue[]" in t and ("GetArray" in first or has(705))) or ("NewYieldReq" in first) or ("Coroutine_Resume" in first): return "14 CLR-call/coroutine plumbing (context, args, yield)"
    if "StringLib" in first or "LuaStateInterop" in first or "Regex" in t or "Char[]" in t or "StringBuilder" in t or "CharPtr" in t: return "16 string.format (KopiLua sprintf/Regex)"
    if "Interop" in first or "System.Int32"==t or "Func`3" in t or "DisplayClass" in t or "CallbackFunction"==t.replace("MS.","") or "NewCallback" in first or t=="System.Object[]": return "15 interop (bound callback, boxing, arg arrays)"
    if "StringLib" in first or "LuaStateInterop" in first or "Regex" in t or "Char[]" in t or "StringBuilder" in t or "CharPtr" in t: return "16 string.format (KopiLua sprintf/Regex)"
    return "99 other"
order = ["empty_loop","function_call","method_call","table_array_insert","table_array_read","table_hash_set_get","int_math","fib_recursive","string_format","coroutine_switch","closure_create_call","clr_interop","clr_interop_reflection"]
allcats={}
for w in order:
    f=os.path.join(T,w+".analysis.txt")
    on=False; acc={}
    for l in open(f):
        if l.startswith("Allocation sites"): on=True; continue
        if not on: continue
        m=re.match(r"\s*([\d.]+)%\s+(\S+)\s+@\s*(.*)", l.rstrip())
        if not m: continue
        share=float(m.group(1)); t=m.group(2); site=m.group(3)
        c=cat(t,site); acc[c]=acc.get(c,0)+share
    allcats[w]=acc
cats=sorted({c for a in allcats.values() for c in a}, key=lambda c:int(c.split()[0]))
print("| Allocation site (MoonSharp fbd0367) | " + " | ".join(order) + " |")
print("|---|" + "---:|"*len(order))
for c in cats:
    row=[]
    for w in order:
        s=allcats[w].get(c,0); b=bytes_per_iter.get(w,0)*s/100
        row.append("" if s<0.5 else (f"{b:.0f} ({s:.0f}%)" if b<10000 else f"{b/1000:.0f}K ({s:.0f}%)"))
    print("| "+c[c.index(' ')+1:]+" | "+" | ".join(row)+" |")
print("| **total bytes/iter** | " + " | ".join(f"{bytes_per_iter.get(w,0):.0f}" for w in order) + " |")
