# Luau VM and compiler: techniques for a managed MoonSharp

Date: 2026-10-28. Analyst scope: `luau-lang/luau` VM, compiler and bytecode format, read for ideas a safe,
managed, IL2CPP-compatible MoonSharp can adopt.

Evidence markers: **[C]** confirmed by reading the cited source, **[S]** secondary (docs, inference from
code that was read), **[U]** unconfirmed (needs a test or a measurement).

## 0. What was read

| Item | Value |
|---|---|
| Upstream | https://github.com/luau-lang/luau |
| Commit read | `32c28089305f488f901aaf1dbf3fd1cb65fa3d11` ("Sync to upstream/release/744 (#3071)", 2026-10-23), shallow clone of 50 commits |
| Licence | `LICENSE.txt`, MIT, "Copyright (c) 2019-2026 Roblox Corporation / Copyright (c) 1994–2019 Lua.org, PUC-Rio"; `lua_LICENSE.txt` (MIT, Lua). SPDX: `MIT` |
| MoonSharp read | the fork's master at `d212945`. `src/MoonSharp.Interpreter` is identical to upstream `moonsharp-devs/moonsharp@fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` (the two fork commits touch only `src/DevTools`), so MoonSharp permalinks below point at upstream `fbd0367`. MoonSharp licence: BSD-3-Clause (`LICENSE`, "Copyright (c) 2014-2016, Marco Mastropaolo") |

Permalink prefixes used below:

- `LU` = `https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11`
- `MS` = `https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter`

Relevant recent upstream PRs seen in the history: table.create/table.clear FASTCALL
https://github.com/luau-lang/luau/pull/2365 (commit `6d36d48`), A64 `FORGLOOP` array fast path
https://github.com/luau-lang/luau/pull/2683, `string.gsub` fast path for plain patterns
https://github.com/luau-lang/luau/pull/2988, `string.split` https://github.com/luau-lang/luau/pull/2981,
FASTPCALL and register reuse arrived through the release syncs https://github.com/luau-lang/luau/pull/2691,
https://github.com/luau-lang/luau/pull/2715, https://github.com/luau-lang/luau/pull/3021.
The design rationale is also summarised on https://luau.org/performance [S].

### Licence and adaptation

MIT is compatible with BSD-3-Clause. **Ideas** can be used freely. **Code** could be copied with the MIT
notice kept, but nothing here should be copied: Luau is C++ built on raw pointers, unions and
in-place patching of `const` code. Every item below is a re-implementation in MoonSharp's own terms.
If any small algorithm is transliterated (for example the `luaH_getn` boundary search or the
`computesizes` array-sizing rule, which come from PUC Lua), add an attribution line naming
Luau/Lua and the MIT licence to the file header and to MoonSharp's `LICENSE` third-party list.

## 1. MoonSharp today: where the time goes (read from code)

These are the facts the Luau ideas are measured against. All **[C]**.

1. **Every local read allocates.** `OpCode.Local` pushes `scope[index].AsReadOnly()`
   (`MS/Execution/VM/Processor/Processor_InstructionLoop.cs#L198-L202`), and `AsReadOnly()` clones any
   writable `DynValue` (`MS/DataTypes/DynValue.cs#L379-L413`). Locals are writable cells, so every read
   of a local is a `new DynValue`. `ExecIndex` does the same for every table read that returns a
   writable value (`Processor_InstructionLoop.cs#L1295-L1383`, `m_ValueStack.Push(v.AsReadOnly())`).
2. **Every arithmetic result allocates** (`DynValue.NewNumber` in `ExecAdd`…`ExecPower`,
   `Processor_InstructionLoop.cs#L897-L1043`). `CastToNumber()` returns `double?`.
3. **Every call allocates** a `CallStackItem` class (`MS/Execution/VM/CallStackItem.cs#L5-L22`,
   `new CallStackItem()` in `Internal_ExecCall`, `Processor_InstructionLoop.cs#L669-L776`), a
   `DynValue.NewNumber(argsCount)` pushed on the value stack, a `LocalScope = new DynValue[n]` in
   `ExecBeginFn` (`#L582-L590`), and a `CloneAsWritable()` per argument in `ExecArgs` (`#L634-L667`).
   CLR calls add a `ScriptExecutionContext`, a `Slice<DynValue>`/`List<DynValue>` and a
   `CallbackArguments`.
4. **The loop pays two checks per instruction**: `m_Debug.DebuggerAttached != null` and the auto-yield
   counter (`Processor_InstructionLoop.cs#L28-L46`).
5. **A numeric `for` loop is 7 dispatches per iteration** (`JFor`, `Clean`, `StoreLcl`, body, `Debug`,
   `Clean`, `Incr`, `Jump`; `MS/Tree/Statements/ForLoopStatement.cs#L52-L98`), and the `Clean` on entry
   nulls the loop-variable cell, so `StoreLcl` allocates a new cell every iteration
   (`AssignLocal`, `Processor_InstructionLoop.cs#L354-L363`).
6. **Global access** is `Upvalue(_ENV)` + `Index` with a constant string `DynValue`
   (`MS/Execution/VM/ByteCode.cs#L252-L269`), then `Table.Get(DynValue)` → `RawGet(string)` →
   `Dictionary<string, LinkedListNode<TablePair>>.TryGetValue` (`MS/DataTypes/Table.cs#L373-L405`).
   .NET does not cache string hash codes, so every global or field access re-hashes the name.
7. **Tables have no array part.** `Table` is a `LinkedList<TablePair>` plus three
   `LinkedListIndex` dictionaries (`MS/DataTypes/Table.cs#L10-L33`); `t[#t+1]=v` allocates a key
   `DynValue`, a `LinkedListNode`, and a dictionary entry (`Table.cs#L155-L200`).
8. **Metamethod lookups are uncached**: `GetMetamethodRaw` does `metatable.RawGet("__index")` every time
   (`MS/Execution/VM/Processor/Processor_IExecutionContext.cs#L74-L87`).
9. **Method calls are 4 instructions** (`Copy`, `IndexN`, `Swap`, `ThisCall`;
   `MS/Tree/Expressions/FunctionCallExpression.cs#L69-L94`), and for CLR userdata the `IndexN` builds a
   fresh `CallbackFunction` + `DynValue` per call
   (`OverloadedMethodMemberDescriptor.GetValue`, `MS/Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs#L471-L474`).
10. **Closure creation uses LINQ**: `i.SymbolList.Select(...).ToList()` plus
    `symbols.Select(s => s.i_Name).ToArray()` in `ClosureContext`
    (`Processor_InstructionLoop.cs#L414-L420`, `MS/Execution/ClosureContext.cs`).
11. **No constant folding**: `BinaryOperatorExpression.Compile` always emits both operands and the
    operator (`MS/Tree/Expressions/BinaryOperatorExpression.cs#L300-L330`).

The allocation count for `x = (x * 31 + i) % 1000003` (int_math) per iteration: `Local x` clone,
`Mul` result, `Local i` clone, `Add` result, `Mod` result, plus the loop-variable cell: about 6 objects
of ~48 bytes, matching the measured ~230-290 bytes per iteration. Luau runs the same line as
`MULK`, `ADD`, `MODK` on 16-byte `TValue` registers with zero allocations.

## 2. Luau architecture in one page

- **Value**: `TValue` is a 16-byte struct, a `Value` union (`gc`, `p`, `n`, `b`, `l`) plus `extra` and
  `tt` tag (`LU/VM/src/lobject.h#L28-L51`) **[C]**. Copies are struct copies (`setobj2s`). Nothing is
  boxed per operation.
- **Strings**: `TString` stores `hash`, `len`, an `atom` (int16 host-assigned id) and inline data, and all
  strings are interned in a global table (`LU/VM/src/lobject.h#L288-L303`,
  `LU/VM/src/lstring.cpp#L10-L98`) **[C]**. Key equality in tables is pointer equality.
- **Register VM**, 32-bit word code with an optional AUX word (`LU/Common/include/Luau/Bytecode.h#L10-L31`).
  Each function has a fixed register window on one contiguous stack; `base` is the window start.
- **Dispatch**: computed goto when available, switch otherwise (`LU/VM/src/lvmexecute.cpp#L104-L155`).
  The executor is a template `luau_execute<bool SingleStep>` instantiated twice: the normal loop has no
  per-instruction debug checks at all (`lvmexecute.cpp#L238-L239`, `#L3945-L3952`).
- **Calls**: `CallInfo` structs live in a preallocated, growable array; a call is `incr_ci(L)` plus field
  stores (`LU/VM/src/lstate.h#L57-L79`, `LU/VM/src/ldo.h#L43`, `lvmexecute.cpp#L1046-L1143`). Lua-to-Lua
  calls never recurse on the C stack and never heap-allocate.
- **Tables**: array part + chained scatter hash with Brent's variation; the length boundary is kept
  inside the array part and cached (`LU/VM/src/ltable.cpp#L4-L23`, `#L1385-L1424`).
- **Inline caches** live in the instruction word: `GETGLOBAL`, `SETGLOBAL`, `GETTABLEKS`, `SETTABLEKS`,
  `NAMECALL` carry an 8-bit predicted hash slot in field C, seeded by the compiler with the string hash and
  re-patched by the VM on a miss (`Bytecode.h#L101-L183`, `lvmexecute.cpp#L84-L88`).
- **Builtins**: `FASTCALL*` executes `math.floor`, `table.insert`, `type`, … directly on registers when the
  environment is "safe", and falls through to an ordinary `CALL` otherwise
  (`lvmexecute.cpp#L3010-L3057`, `LU/VM/src/lbuiltins.cpp`).
- **Compiler** (single pass over the AST plus analysis passes): constant folding, builtin folding,
  variable-write tracking, closure sharing, inlining with a cost model, loop unrolling, table shape
  prediction (`LU/Compiler/src/*.cpp`).
- **GC**: incremental mark-sweep with a page allocator (`lgc.cpp`, `lmem.cpp`). Not transferable to .NET;
  the managed lesson is "do not allocate in the hot path", which Luau achieves by design.

## 3. Techniques, mapped onto MoonSharp

Each technique: what Luau does (with source), why it helps, how it maps to MoonSharp's stack VM, whether it
needs a register VM rewrite, impact on the measured workloads, effort, risk, compatibility.

### T1. Monomorphic inline caches for named field and global access

**Luau** [C]. `GETTABLEKS A B C AUX`: C is a predicted node slot. The fast path is three loads and a
pointer compare: `slot = C & h->nodemask8; n = &h->node[slot]; if key(n) == kv && !nil(val(n))`
(`lvmexecute.cpp#L505-L534`). On a miss without metatable it does a full `luaH_getstr` and patches C with
the found slot (`VM_PATCH_C(pc - 2, cachedslot)`, `#L528-L547`). With a metatable it calls `luaV_gettable`,
which records the slot found in the *final* table of the `__index` chain in `L->cachedslot`
(`LU/VM/src/lvmutils.cpp#L166-L181`), and that slot is patched in. `SETTABLEKS` is symmetric
(`#L682-L757`), `GETGLOBAL`/`SETGLOBAL` use the closure's env table (`#L387-L448`). The compiler seeds C with
`getStringHash(name)` so the first execution usually hits (`LU/Compiler/src/Compiler.cpp#L3113-L3118`,
`#L3196-L3199`). The cache is **per instruction, validated by key identity at the predicted slot**, not by
table identity, so it hits for *every* table with the same layout (objects built by the same constructor).

**Why it helps.** It removes hashing and probing from `obj.field`, `obj.field = v` and global reads. In
MoonSharp it would also remove the per-access .NET string hash computation (fact 6).

**MoonSharp mapping.**

- Stage A, works on today's `Table`: add cache fields to `Instruction` (`MS/Execution/VM/Instruction.cs`):
  `object IcTable; LinkedListNode<TablePair> IcNode`. In `ExecIndex`/`ExecIndexSet` for `IndexN`,
  `IndexSetN` and global `Index` with constant `Value`: if `obj.Table == i.IcTable && i.IcNode.List != null`
  read/write `i.IcNode.Value` directly. `LinkedListNode.List` becomes null when the node is removed, so
  removal invalidates. This is identity-keyed (one table per site), which is ideal for `_ENV` globals and
  for upvalue tables (`local C = {}` used as a class), but misses for many distinct instance tables.
- Stage B, with a new `Table` (see T5): hash part as `Node[]` (struct: key, value, next index) with
  power-of-two size. Cache an `int` slot in the instruction exactly like Luau and validate with
  `ReferenceEquals(node.Key.Object, keyString) || string.Equals(...)` plus non-nil value. Intern
  compile-time constant keys in a per-`Script` pool (not `string.Intern`, which is process-global and
  never freed in Unity) so the reference check normally succeeds. Precompute the key hash once into the
  instruction so a miss does not re-hash either.
- For the `__index` chain (method_call, oop_inherit), copy Luau's `cachedslot` trick: the slow path returns
  the slot found in the table that finally held the key, and the instruction caches `(slot in __index
  table)`. Luau's `NAMECALL` fast path checks "key absent in the instance's main position, metatable
  `__index` is a table, predicted slot in that table holds the key" (`lvmexecute.cpp#L929-L951`).

**Register VM needed?** No. `IndexN`/`IndexSetN`/global `Index` already carry the constant key in
`Instruction.Value`; the cache is extra fields on the same instruction.

**Impact.** table_hash_set_get uses dynamic keys (`t[k]`), so only Stage B's precomputed-hash-free path helps
there (-10-20%) [S]. method_call (`self.v = self.v + 1`, `o:inc()` through `__index`): the three named
accesses and the method lookup become cache hits; with T7 and T9 expect 705 → ~350 ns [S]. Every benchmark
that reads a global or a library function (`string.format`, `coroutine.yield`) gains a little.
Effort: medium (Stage A small). Risk: low; a cache miss falls back to the existing code. Compatibility: no
public API change; IL2CPP-safe (plain fields); debugger unaffected. Thread safety: `Script` is
single-threaded already, but cached fields on shared `Instruction` objects mean two `Script`s must never
share a `ByteCode` (they do not today).

### T2. Builtin fast calls (`FASTCALL`, `FASTCALL1/2/2K/3`) and import resolution (`GETIMPORT`)

**Luau** [C]. The compiler recognises calls to known builtins when the global is not assigned in the module
(`getBuiltin`, `LU/Compiler/src/Builtins.cpp#L19-L64`), including `local m = math` aliases, and emits
`FASTCALL1 bfid, arg, skip` *in front of* the normal call sequence (`Compiler.cpp#L1424-L1500`,
`Bytecode.h#L355-L400`). At run time, if `cl->env->safeenv` is set, the VM calls a C function
`luauF_floor(L, res, arg0, nresults, args, nparams)` that works directly on registers and returns -1 when
it cannot handle the arguments (`lvmexecute.cpp#L3010-L3057`, `lbuiltins.cpp#L173-L183`). On success it
skips the function load and the `CALL`; on failure the ordinary call runs, so errors and metamethods behave
exactly as before. `table.insert(t, v)` becomes `luaH_getn + luaH_setnum` (`lbuiltins.cpp#L1015-L1030`).
`GETIMPORT` resolves `math.floor` once at load time into the constant table (`LU/VM/src/lvmload.cpp#L176-L215`)
and reuses it while the env is safe (`lvmexecute.cpp#L483-L503`). `safeenv` is cleared by
`getfenv`/`setfenv` (`LU/VM/src/lbaselib.cpp#L134`, `#L143`) and the global/library tables are made
read-only by `luaL_sandbox` (`LU/VM/src/linit.cpp#L108-L127`).

**Why it helps.** A builtin call in MoonSharp pays for `CallStackItem`, `ScriptExecutionContext`, an args
`Slice`/`List`, `CallbackArguments`, the delegate invocation and a result `DynValue`. A fast call pays for
the result only.

**MoonSharp mapping.** MoonSharp has no sandbox and globals stay mutable, so the guard must be by identity,
not by a "safe env" flag:

- Step 1, runtime only, no compiler change: give `CallbackFunction` an `internal int FastId` (or an
  `internal Func<…>` fast delegate) set by `ModuleRegister` for selected `CoreModules` functions
  (`math.floor/abs/min/max/sqrt/sin/cos/fmod`, `string.sub/byte/len/char`, `table.insert` (2-arg),
  `type`, `rawget/rawset/rawequal`, `select('#')`, `tostring/tonumber` on primitives, `ipairs/pairs`
  setup). In `Internal_ExecCall`'s `ClrFunction` branch (`Processor_InstructionLoop.cs#L702-L739`): if
  `FastId != 0` and the arguments on the stack have the expected types, compute the result in place,
  `RemoveLast(argsCount + 1)`, push the result, return. Otherwise run the existing path. Identity of the
  `CallbackFunction` *is* the guard: if a script replaces `math.floor`, the new value has no `FastId`.
- Step 2, compiler: a fused `CallBuiltin` opcode for `lib.fn(args)` patterns that uses T1's caches for the
  `lib` global and the `fn` field and compares the loaded value with the expected `CallbackFunction` before
  taking the fast path. This removes the `Index`/`Index` dispatches as well.

**Register VM needed?** No.

**Impact.** table_array_insert in the benchmark uses `t[i] = i`, not `table.insert`, so it does not benefit;
code that does use `table.insert`, `math.*`, `string.byte/sub` and `type` (common in game scripts) gains 3-5x
per call [S]. string_format: `string.format` itself is not a Luau fastcall either; only call overhead (~10%)
[S]. coroutine_switch: a fast path for `coroutine.yield` is not possible this way (it must unwind). Effort:
small (step 1), medium (step 2). Risk: low if the fast path only handles exactly-typed arguments and falls
back for anything else, so error messages stay identical. Compatibility: a fast-called builtin does not
appear in `debug.traceback`/call-stack inspection during its (non-observable) execution, same as Luau.
Hooks that replace library functions keep working because identity is checked.

### T3. Constant-operand and fused compare-and-branch opcodes (`ADDK`, `SUBRK`, `JUMPXEQK*`, `JUMPIFLT`)

**Luau** [C]. Arithmetic with a numeric constant operand uses `ADDK…POWK`, `IDIVK`, and reversed `SUBRK`/`DIVRK`
(`Bytecode.h#L236-L245`, `#L395-L401`; compiler `Compiler.cpp#L2438-L2480`). Comparisons never materialise a
boolean when used as a condition: `JUMPIFLT/LE/EQ` compare two registers and branch (`Bytecode.h#L214-L223`),
and equality with `nil`, a boolean, a number or a string constant is `JUMPXEQKNIL/KB/KN/KS`, a single tag +
value compare (`Compiler.cpp#L2034-L2124`, `lvmexecute.cpp#L3395-L3406`).

**MoonSharp mapping.** Peephole fusion at emit time in `ByteCode`/`BinaryOperatorExpression.Compile`:

- `Literal(number k); Add|Sub|Mul|Div|Mod|Power` → `AddK` … with `k` in `Instruction.Value` (read
  `Value.Number`). Saves one dispatch and one push/pop; does not save the result allocation until values are
  structs.
- `Less|LessEq|Eq` followed by `Jf` (the `if`/`while`/`repeat` condition path) → `JLess`, `JLessEq`, `JEq`,
  `JNotEq` that compare and jump without pushing `DynValue.True/False` (and without the extra
  `Not`/`CNot`/`ToBool` that `Emit_Operator` adds for `>`/`>=`/`~=`, `ByteCode.cs#L139-L150`).
- `x == nil`, `x == "str"`, `x == 3` → `JEqK` with the constant in `Value`.
- `LessEq` currently pushes an extra `DynValue.False` before its result for the `__le`/`__lt` fallback
  (`Processor_InstructionLoop.cs#L1113-L1145`); the fused opcode can keep that only on the metamethod path.

**Register VM needed?** No; this is classic stack-VM superinstruction work.

**Impact.** fib(12): `n < 2` + branch, `n - 1`, `n - 2` are all fusable: ~15-20% fewer dispatches [S].
int_math: `x * 31` and `% 1000003` become K-forms (2 fewer dispatches of ~9). table_array_read: `j > 1024`
branch fused. Effort: small-medium. Risk: low; metamethod and string-comparison fallbacks must call the
same helpers (`Internal_InvokeBinaryMetaMethod`). Compatibility: bytecode dump format
(`Processor_BinaryDump.cs`) gains opcodes; `OpCode` enum order is serialised (`wr.Write((byte)OpCode)`,
`Instruction.cs`) so new opcodes must be **appended before `Invalid`** and the dump version bumped, or old
dumps rejected.

### T4. Numeric `for` loop as two opcodes, with no per-iteration allocation

**Luau** [C]. `FORNPREP` checks types once and jumps over an empty loop; `FORNLOOP` does
`idx += step; if (step > 0 ? idx <= limit : limit <= idx) jump back` on registers
(`lvmexecute.cpp#L2511-L2561`). The register layout is `[limit, step, index, variable]`; the user-visible
variable is a copy, so the body cannot disturb the counter (`Bytecode.h#L289-L301`). Loop variables that are
captured by a closure get their upvalue closed by `CLOSEUPVALS` at the end of each iteration; uncaptured
ones cost nothing.

**MoonSharp mapping.** In `ForLoopStatement.Compile` (`MS/Tree/Statements/ForLoopStatement.cs#L52-L98`):

1. Drop the `Emit_Debug("..end")` no-op (or emit it only when a debugger build flag is set): one dispatch
   per iteration for nothing.
2. Replace `Incr; Jump; JFor` with one `ForLoop` opcode that increments, tests and jumps back.
3. When the loop variable is **not captured** by any closure in the body (the scope builder already resolves
   which `SymbolRef`s become upvalues, see `MS/Execution/Scopes/BuildTimeScope*.cs`), skip the
   `Clean` on entry/exit for that slot and update the existing cell in place, so no new cell is allocated per
   iteration. Keep today's per-iteration fresh cell when it is captured (that is what gives each closure its
   own `i`, the closure_create_call case).

**Register VM needed?** No.

**Impact.** empty_loop: 7 dispatches + 1 allocation → 2-3 dispatches + 0 allocations; 65 → ~20-25 ns [S].
Every loop-heavy benchmark gains 10-30 ns per iteration. Effort: small. Risk: low; NaN/infinite step and
non-number bounds must keep the `ToNum` error messages (`ConvertToNumberFailed(stage)`). Compatibility:
none visible; the debugger loses the `..end` pseudo-instruction, which has no source position of its own
beyond `m_RefEnd` (keep the source ref on the `ForLoop` instruction).

### T5. Table with an array part, cached boundary, and size hints

**Luau** [C]. Integer keys `1..n` live in a `TValue array[]` sized by the "at least half used" rule
(`computesizes`/`rehash`, `ltable.cpp#L557-L583`, `#L906-L968`). `luaH_getnum` is a bounds check and an index
(`#L1142-L1162`). The length boundary is kept inside the array part (`adjustasize`, `#L881-L888`) and cached
in `aboundary`, so `#t` is O(1) in the common append case and a branch-light binary search otherwise
(`#L549-L555`, `#L1362-L1424`). The compiler sizes tables up front: `NEWTABLE` carries hash and array size
(`Bytecode.h#L274-L278`, `lvmexecute.cpp#L2447-L2459`), predicted by `TableShape.cpp` from the constructor
and from subsequent `t.x = …` / `t[i] = …` (with `for i = 1, k` bounds up to 16) assignments
(`LU/Compiler/src/TableShape.cpp#L1-L155`). `SETLIST` copies a run of registers into the array part in one
go (`lvmexecute.cpp#L2474-L2509`). `DUPTABLE` clones a constant template table, so all objects built by one
constructor share a hash layout (`#L2461-L2472`) and T1's slot caches stay monomorphic.

**MoonSharp mapping.** Replace `Table`'s internals (`MS/DataTypes/Table.cs`, `MS/DataStructs/LinkedListIndex.cs`):

- `DynValue[] m_Array; int m_ArrayCount` for keys `1..n` (later `Value[]` once values are structs), with
  `GetIntegralKey` (`Table.cs#L72-L80`) routing integer-valued doubles there.
- Hash part: open addressing or chained `Node[]` with `int next`, insertion-ordered (see compatibility),
  string keys compared by reference first, hashes cached in the node.
- Cache the boundary like `aboundary`; `m_CachedLength` already exists but is invalidated on any nil write.
- `NewTable` gets size hints in `NumVal`/`NumVal2` from a small shape pass in `TableConstructor.Compile`
  (count positional and named fields) and, optionally, from following `local t = {}; t.x = …` statements.
- `TblInitI` runs become one `TblInitList n` opcode that bulk-copies `n` stack values.

**Register VM needed?** No.

**Impact.** table_array_insert 1277 ns: today 3-4 allocations + 2 dictionary operations per element; with an
array part, amortised O(1) append and zero per-element allocations beyond the value: expect 5-10x (≈150-250 ns)
[S]. table_array_read 447 ns → ~100-150 ns once the read no longer clones (T8/struct values) [S].
gc_small_tables (`{ i, i + 1, x = i }`): presized constructor avoids two resizes. Effort: large (Table is
public API surface: `Pairs`, `Keys`, `Values`, `Length`, `Next`, `Set/Get(object)`, and iteration during
modification). Risk: medium. **Compatibility:** MoonSharp's `pairs` today iterates in insertion order
because of the backing `LinkedList`; Lua does not define an order, but users may depend on it. An
insertion-ordered hash part (dense entries array + index buckets, like .NET `Dictionary` or CPython's dict)
preserves order for the hash part; array-part keys would then come first, which only reorders tables that mix
integer keys inserted *after* string keys. This needs an owner decision and a MIGRATING note; a
`legacy` mode could keep the list-ordered iteration at a cost.

### T6. Metamethod-absence cache (`tmcache`)

**Luau** [C]. Each table has an 8-bit `tmcache`; bit `e` set means "this metatable has no metamethod `e`"
(`LU/VM/src/lobject.h#L552`, `ltm.h#L43-L47`). `luaT_gettm` sets the bit on a miss (`LU/VM/src/ltm.cpp#L97-L108`),
and any string-key write to the table clears the whole byte (`invalidateTMcache`, `ltable.h#L15`,
`ltable.cpp#L1335-L1347`). New tables start with all bits set (`ltable.cpp#L979`). `fastnotm(h->metatable,
TM_LEN)` is then one AND (`lvmexecute.cpp#L2417-L2426`).

**MoonSharp mapping.** Add `internal int MetaAbsentBits` to `Table`; set the bit in a new
`Processor.GetMetamethodRaw` overload taking an enum id for `__index`, `__newindex`, `__eq`, `__len`,
`__call`, `__concat`, arithmetic events; clear it in every `Table.Set`/`Remove` path that writes a string key.
`ExecIndex`'s miss path (`h = GetMetamethodRaw(obj, "__index")`), `ExecEq` and `ExecLen` use it.

**Register VM needed?** No. **Impact.** Every table read that misses (common in OOP: `self.method` misses on
the instance) and every `#t`, `==` on tables saves a dictionary lookup; method_call/oop_inherit −10-15% on
their own [S]. Effort: small. Risk: low (invalidate conservatively on any string-key write, including
`rawset` and host `Table.Set`). Compatibility: none.

### T7. Fused method call (`NAMECALL`) and userdata member caches (`GETUDATAKS`, atoms)

**Luau** [C]. `NAMECALL A B C AUX` looks up method `AUX` on `R(B)`, writes the function to `R(A)` and the
object to `R(A+1)`, with the T1 slot cache applied either to the object or to its `__index` table
(`lvmexecute.cpp#L919-L1044`). For userdata, `__namecall` receives the method name through `L->namecall`,
and strings carry a host-assigned `atom` (`lobject.h#L293`) so a host can `switch` on an integer instead of
comparing names. Newer bytecode adds `GETUDATAKS/SETUDATAKS/NAMECALLUDATA`, which dispatch on the userdata's
type tag to a host callback with a cached slot, and **rewrite themselves** back to the generic opcode on a
miss (`VM_PATCH_OP(pc - 2, LOP_GETTABLEKS)`, `lvmexecute.cpp#L3408-L3478`; `Bytecode.h#L432-L437`).

**MoonSharp mapping.** This is the CLR-interop win.

- New opcode `MethodLookup` (name in `Value`) replacing `Copy; IndexN; Swap` in
  `FunctionCallExpression.Compile` (`MS/Tree/Expressions/FunctionCallExpression.cs#L75-L81`): pops the object,
  pushes `fn, obj`. Two fewer dispatches per `a:b()`.
- For tables: T1 cache on the object and on its `__index` table.
- For userdata: cache `(IUserDataDescriptor descriptor, IMemberDescriptor member)` on the instruction; on hit
  (`ud.Descriptor == cached`) skip `DispatchingUserDataDescriptor.Index` and its `m_Members` dictionary and
  fuzzy-name fallbacks (`MS/Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs#L218-L251`). For a
  `ThisCall` whose callee is an `OverloadedMethodMemberDescriptor`, call `member.Execute(script, obj, ctx,
  args)` directly instead of materialising `DynValue.NewCallback(GetCallbackFunction(script, obj))` per call
  (`OverloadedMethodMemberDescriptor.cs#L471-L474`). The cache must also be invalidated when
  `UserData.GetExtensionMethodsChangeVersion()` changes (`DispatchingUserDataDescriptor.cs#L240-L248`), so store
  the version with the cached member.
- Property get/set (`ud.X`, `ud.X = v`): the same descriptor+member cache on `IndexN`/`IndexSetN`.
- The cached overload choice can be added later (Luau has no equivalent; this is MoonSharp-specific
  overload resolution in `OverloadedMethodMemberDescriptor`).

**Register VM needed?** No. **Impact.** method_call 705 ns (table path) → with T1+T6+T7 ≈ 300-350 ns [S];
CLR method calls on userdata lose 2 allocations and 2 dictionary lookups each (the xLua-style interop
benchmarks in other reports), likely 30-50% faster per call [S]. Effort: medium. Risk: medium (descriptor
identity is per type; `IUserDataDescriptor` implementations written by users may return different results
for the same name over time, so cache only for MoonSharp's own `StandardUserDataDescriptor` family or behind
an opt-in interface such as `ICacheableUserDataDescriptor`). Compatibility: no public API change if gated by
descriptor type; IL2CPP-safe.

### T8. Frames without per-call heap allocation; captured-only boxing of locals

**Luau** [C]. `CallInfo` is a struct in a reused array (`lstate.h#L57-L79`, `ldo.h#L43`); arguments are
already in place in the callee's register window, missing ones set to nil (`lvmexecute.cpp#L1088-L1098`);
`RETURN` copies results down and pops the frame (`#L1257-L1317`). Locals are registers. Only a local that is
**captured by a closure and written after capture** becomes a heap `UpVal` (`LCT_REF`); a captured local
that is never reassigned is copied into the closure by value (`LCT_VAL`); open upvalues point into the stack
and are closed when the scope ends (`LU/Compiler/src/Compiler.cpp#L1724-L1750`, `LU/VM/src/lfunc.cpp#L98-L155`,
`Bytecode.h#L806-L812`). The `written` flag comes from `ValueTracking.cpp` (`#L15-L60`).

**MoonSharp mapping**, in increasing effort:

1. **Reuse `CallStackItem` objects**: `FastStack<CallStackItem>` keeps the instances in its array; `Push`
   reuses the object at the slot (reset its fields) instead of `new`. Or make `CallStackItem` a struct and
   access it with `ref` returns (C# 7, supported by Unity 2021.3 and IL2CPP). Check first that no one keeps a
   `CallStackItem` reference past `Pop` (the debugger's call-stack snapshot `Processor_Debugger.cs` copies
   data into `WatchItem`s; `PopToBasePointer` returns the popped item to `ExecRet`, which only reads it
   before the next push).
2. **Pool `LocalScope` arrays per size**: closures capture individual cells (`GetUpvalueSymbol` returns
   `LocalScope[i]`), never the array, so the array can go back to a per-size free list on `Ret`.
3. **Cache small-integer `DynValue`s** (readonly) for the `argsCount` marker pushed per call
   (`Processor_InstructionLoop.cs#L743`) and for `ToNum`/`Len` results.
4. **Do not clone arguments** in `ExecArgs` (`CloneAsWritable` per argument, `#L634-L667`): `AssignLocal`
   already copies into the cell with `Assign`, so the clone is a second copy.
5. **Captured-only cells** (Luau's `LCT_VAL`/`LCT_REF` split). With capture analysis in the scope builder,
   non-captured locals do not need a mutable cell: `StoreLcl` can store the (read-only) value reference into
   the slot and `Local` can push it without `AsReadOnly()` cloning, because nothing mutates it in place. The
   one in-place mutator on the stack is `ExecIncr` (`#L543-L556`), which already clones a read-only top
   before writing; values written to non-captured slots must therefore be read-only (VM-produced numbers can
   simply be created read-only). This removes the per-read clone (fact 1) **without** the full struct
   migration. It becomes moot if `DynValue` turns into a struct (other reports); then this item reduces to
   "box only captured-and-written locals".

**Register VM needed?** No for 1-5. A register window on the value stack (locals at `base + i`) is the
natural end state but is a rewrite of `Emit_Load/Emit_Store`, `Args`, `BeginFn`, `Clean`, closures and the
debugger's symbol access, and only pays off together with struct values.

**Impact.** function_call 376 ns: removes `CallStackItem` + `LocalScope` + arg-count + per-arg clone
allocations (4-5 objects per call) → ~180-220 ns [S]. fib(12) ~2x with T3 [S]. int_math: item 5 removes 2 of
the ~6 allocations per iteration (-25-30%) [S]. Effort: small (1-4), medium (5). Risk: low (1-4), medium (5:
any missed in-place mutation path would alias values; needs a test that stores a loop variable into a table
and reads it back after further iterations). Compatibility: none visible.

### T9. Closure creation without allocation where legal (`DUPCLOSURE`)

**Luau** [C]. A closure whose upvalues are all never-written locals of the top level (or that has no upvalues)
is created once as a constant and shared by `DUPCLOSURE`, with a runtime equality check that clones it if the
captured values differ (`shouldShareClosure`, `Compiler.cpp#L1671-L1704`; `lvmexecute.cpp#L2891-L2946`). The
compiler disables this when `setfenv` is used because it changes function identity.

**MoonSharp mapping.**

1. Precompute the upvalue name array at compile time (store it on the `Closure` instruction) and replace
   `i.SymbolList.Select(...).ToList()` and `symbols.Select(...).ToArray()` with a plain loop into a
   `DynValue[]` (`Processor_InstructionLoop.cs#L414-L420`, `MS/Execution/ClosureContext.cs`). Pure
   cleanup; `ClosureContext : List<DynValue>` could become an array-backed type.
2. For `SymbolList.Length == 0` (function literals that capture nothing, typically nested helper functions
   or callbacks), cache the `Closure` on the instruction and push the same `DynValue` every time. Lua 5.2
   allows this explicitly: the 5.2 manual says closures with no detectable difference "may be classified as
   equal or not (depending on internal caching details)" (Lua 5.2 reference manual §3.4.3) [S], and PUC Lua
   5.2 itself caches closures. MoonSharp has no `setfenv`, so the Luau restriction does not apply.

**Register VM needed?** No. **Impact.** closure_create_call (captures the loop variable, so item 2 does not
apply; Luau would also allocate there): item 1 removes the LINQ iterator, the `List`, the names array and
the `ToList` copy: 662 → ~350-400 ns [S]. Item 2: allocation-free for non-capturing function literals.
Effort: small. Risk: low; item 2 changes `rawequal(f1, f2)` for two evaluations of the same capture-free
literal from false to true, which is a Lua-5.2-legal but observable change: put it in MIGRATING and keep a
`ScriptOptions` switch only if the owner wants one.

### T10. Interrupt and debugger checks off the hot path

**Luau** [C]. The normal loop has **no per-instruction checks**. `VM_INTERRUPT()` (a null-check of a callback)
runs only on `CALL`, `RETURN`, backward jumps and loop opcodes (`lvmexecute.cpp#L90-L102`,
`#L1048`, `#L1259`, `#L2537`). Single-stepping uses a second template instantiation `luau_execute<true>`
(`#L238`, `#L3945-L3952`). Breakpoints patch the opcode byte to `LOP_BREAK` and keep the original in
`p->debuginsn` (`LU/VM/src/ldebug.cpp#L395-L410`). Coverage counts by patching the `COVERAGE` instruction in
place (`lvmexecute.cpp#L3059-L3069`).

**MoonSharp mapping.** `Processing_Loop` (`Processor_InstructionLoop.cs#L16-L46`) checks the debugger and the
auto-yield counter on every instruction. Options:

- Make the loop generic over a struct policy, `Processing_Loop<TPolicy>() where TPolicy : struct,
  ILoopPolicy`, with `NoChecks` and `Full` implementations. The JIT and IL2CPP both emit a separate
  specialised body for each value-type instantiation, so the `TPolicy.Debug` checks fold away. Choose the
  instantiation on entry (`DebuggerAttached != null || canAutoYield`).
- Count auto-yield budget only on back-edges and calls (as `VM_INTERRUPT`), not per instruction. This
  changes the granularity of `AutoYieldCounter` (it counts instructions today), so either keep the exact count
  in the `Full` variant or document the change.
- A debugger attached while the fast loop runs must be noticed: poll one field at back-edges/calls and switch
  loops (Luau's interrupt), or reuse the T1-style instruction mutability to patch a `Break` opcode.

**Register VM needed?** No. **Impact.** 2 branches + 1 increment per instruction; on 6-10 instructions per
iteration that is 5-10% on every loop benchmark [S]. Effort: medium (the loop body is large; a generic copy
doubles IL size but not source size). Risk: low-medium (debugger attach timing). Compatibility:
`AutoYieldCounter` semantics if the cheap variant is used.

### T11. Compiler-side analysis: constant folding, constant locals, write tracking

**Luau** [C]. `ConstantFolding.cpp` folds unary/binary operators, string concatenation, comparisons and
interpolated strings (`#L78-L560`), and tracks locals that are assigned once with a constant value
(`ConstantVisitor`, `#L803`, `locstants`) so `local N = 10 … x * N` compiles as `MULK`. `BuiltinFolding.cpp`
folds `math.pi`, `math.floor(2.5)` etc. when the global is untouched. `ValueTracking.cpp` records
`written` per local and `Global::Default/Written` per global, which drives T2, T8 (`LCT_VAL`) and T9.
`CostModel.cpp` + `tryCompileInlinedCall` inline small local functions (`Compiler.cpp#L942-L1113`), and
`tryCompileUnrolledFor` unrolls constant-trip loops under a cost threshold (`#L4357-L4417`).

**MoonSharp mapping.**

- Add a `TryGetConstant(out DynValue)` on `Expression` (`MS/Tree/Expression_.cs`): `LiteralExpression`
  returns its value; `BinaryOperatorExpression`/`UnaryOperatorExpression` fold when both operands are numeric
  (or string for `..`) constants, using **the same helpers as the VM** so results match bit for bit
  (`EvalArithmetic` already exists, `BinaryOperatorExpression.cs#L374-L404`). `Compile` then emits one
  `Literal`. Feeds T3 (more K operands).
- Write tracking in the scope builder: mark a `SymbolRef` as written if any assignment targets it after its
  declaration. Needed for T8 item 5 and T9 and for safe builtin detection in T2 step 2.
- Inlining and unrolling: **not recommended** now. MoonSharp's debugger, `SourceRef` mapping and error
  messages assume one frame per Lua function; the gain is small next to T1-T8.

**Register VM needed?** No. **Impact.** Small on these benchmarks (they contain few constant expressions);
useful as an enabler [S]. Effort: small (folding), medium (write tracking). Risk: folding must replicate
MoonSharp's runtime semantics exactly. Note while reading: `ExecMod` computes
`Math.IEEERemainder(a, b)` and adds `b` when the result is negative (`Processor_InstructionLoop.cs#L961-L980`).
For a negative divisor this gives `5 % -3 = -1 + (-3) = -4`, whereas Lua defines `a - floor(a/b)*b = -1`
**[U]** (derived from the code and `math.remainder(5,-3) = -1` in Python; needs a MoonSharp regression test).
If confirmed, it is a plain upstream bug to fix in the VM and in any folding at the same time.

### T12. Multi-operand `CONCAT`

**Luau** [C]. `CONCAT A B C` concatenates registers `B..C` in one pass: it sums lengths, then copies into one
buffer (`lvmexecute.cpp#L2339-L2353`, `LU/VM/src/lvmutils.cpp#L459-L530`).

**MoonSharp mapping.** `a .. b .. c .. d` is right-associative binary `Concat` today, creating 3 intermediate
strings and 3 `DynValue`s. In `BinaryOperatorExpression.Compile`, flatten a right-leaning chain of `..` into
`Concat n` that pops `n` values and builds one string with a `StringBuilder` or `string.Concat(string[])`
when all are strings/numbers, falling back to pairwise `__concat` from the right when a metamethod is
needed (to keep Lua's evaluation order of `__concat`). **Register VM needed?** No. **Impact.** string_concat
(`'item' .. i`) is a two-operand concat: no gain; real code with longer chains (log lines, keys) gains
1.5-3x [S]. Effort: small. Risk: low (number-to-string formatting must use the same `CastToString`).

### T13. Generic `for` fast path for `pairs`/`ipairs` (`FORGPREP_NEXT/INEXT`, `FORGLOOP`)

**Luau** [C]. When the iterator is the builtin `next`/`inext` and the env is safe, `FORGPREP_*` replaces the
generator with `nil` and an integer cursor, and `FORGLOOP` walks the array part, then the hash nodes, writing
key/value straight into registers, with no function call per step (`lvmexecute.cpp#L2685-L2835`).

**MoonSharp mapping.** `ExecIterPrep` already special-cases tables without `__call` via `EnumerableWrapper`
(`Processor_InstructionLoop.cs#L476-L524`), but each step still goes through `Call` of a CLR iterator with
tuple results. Add an `IterNext` path in `ExecIterUpd`/the `ForEachLoopStatement` code: when the tuple's
function is the `pairs`/`ipairs` `CallbackFunction` (identity check, as in T2) and the state is a `Table`,
keep an integer cursor in the iterator slot and read the next entry directly. Needs T5 (an indexable
node array) to be cheap. **Register VM needed?** No. **Impact.** Not in the current benchmark list; large for
real scripts that iterate tables (2-4x per step) [S]. Effort: medium. Risk: low-medium (modification during
iteration rules must match MoonSharp's current behaviour). Compatibility: none if the identity guard is used.

### T14. Coroutines

**Luau** [C]. A coroutine is a `lua_State` with its own small stack (`BASIC_STACK_SIZE = 2*LUA_MINSTACK`,
`BASIC_CI_SIZE = 8`, `LU/VM/src/lstate.h#L13-L17`, `lstate.cpp#L31-L42`). Yield from Lua leaves `luau_execute`
(`goto exit`) with the state in `CallInfo`; resume re-enters it. Values move between threads by copying stack
slots (`lua_xmove`); no tuple object is created.

**MoonSharp mapping.** The structure is the same (a `Processor` per coroutine, `Processing_Loop` re-entered on
resume, `MS/Execution/VM/Processor/Processor_Coroutines.cs`). The costs are allocations around it: a
`YieldRequest` `DynValue`, `DynValue.NewTuple(args)` on resume, the `coroutine.yield` CLR call machinery
(T8 item 1 helps), and `CallbackArguments`. Lessons: pass resume/yield values through the value stack
directly instead of tuples, reuse one `YieldRequest` per processor, and make sure a new coroutine's stacks
start small (the fork's "grow VM stacks on demand" work already addresses `STACK_SIZE = 131072`,
`Processor.cs#L11`). **Register VM needed?** No. **Impact.** coroutine_switch 899 ns → ~500 ns [S]. Effort:
medium. Risk: low.

### T15. Self-specialising instructions (quickening) as a general tool

**Luau** [C]. The VM rewrites its own instructions: slot caches (`VM_PATCH_C`), coverage counters
(`VM_PATCH_E`), userdata opcodes that downgrade themselves to the generic form (`VM_PATCH_OP`), and
breakpoints (`lvmexecute.cpp#L84-L88`, `#L3474-L3477`).

**MoonSharp mapping.** `Instruction` is a mutable class already, so quickening is natural and safe: e.g. an
`Index` whose key turned out to be a constant string can become `IndexN`; an `Add` that saw only numbers can
stay on a specialised path, and a `CallBuiltin` that failed its identity guard N times can rewrite itself to
the generic call. Keep a copy of the original opcode for the bytecode dumper and the debugger. **Register VM
needed?** No. Effort/risk: per use.

### Not transferable or not worth it

- Computed goto, `LUAU_LIKELY`, SSE4.1 builtin variants, `NATIVECALL`/CodeGen JIT: C++/native only. C#
  `switch` on a dense enum already compiles to a jump table in both RyuJIT and IL2CPP.
- The incremental GC, page allocator and memory categories (`lgc.cpp`, `lmem.cpp`): .NET owns memory. The
  transferable lesson is Luau's allocation discipline; Luau even emits compile-time "allocation:" remarks
  (`Compiler.cpp#L1770`), a cheap idea for MoonSharp's bytecode dump.
- Inlining and loop unrolling (see T11).
- Vectors, buffers, classes, `CALLFB` feedback slots: Luau-specific language features.

## 4. Does MoonSharp need a register VM?

No for everything ranked here. Luau's speed comes from three layers, and only the first is tied to registers:

1. **Value representation and frame layout** (16-byte `TValue`, register windows, no allocation per op).
   In MoonSharp the equivalent is a struct `DynValue`-like value plus locals living in stack slots. That is
   where most of the 10-25x gap is, and other reports (Lua-CSharp's `LuaValue`) cover it.
2. **Caches and fast paths** (T1, T2, T6, T7, T13, T15): instruction-local data plus identity guards.
   These are independent of the VM shape.
3. **Compiler shaping** (T3, T4, T9, T11, T12): fewer, fatter instructions. A stack VM gets most of this
   through superinstructions.

A register VM would remove the remaining push/pop traffic (roughly another 1.3-2x on arithmetic-heavy code
[S]) but touches the compiler, the debugger's symbol model, the binary dump format and every opcode. It is a
later step, after (1), and should be decided with measurements from (2) and (3) in hand.

## 5. Priority

Ranked by (benchmark impact × breadth) / (effort × risk), assuming the struct-value work from other reports
proceeds in parallel.

| # | Technique | Impact | Effort | Needs | Main workloads |
|---|---|---|---|---|---|
| 1 | T8 items 1-4: reuse `CallStackItem`, pool `LocalScope`, cache arg-count numbers, drop arg clones | high | small | — | function_call, fib, method_call, every CLR call |
| 2 | T4: `ForLoop` opcode, no `Debug` no-op, no cell per iteration when not captured | high | small | capture info | empty_loop, every loop |
| 3 | T1 Stage A + T6: identity inline cache for named/global access, metamethod-absence bits | high | small-medium | — | method_call, oop_inherit, globals |
| 4 | T2 step 1: `FastId` builtins in the CLR call path | medium-high | small | — | real scripts using `math`/`string`/`table` |
| 5 | T3: K-operand ops and compare-and-branch fusion | medium | small-medium | — | fib, int_math, float_math, table_array_read |
| 6 | T9 item 1 (+ item 2 after owner OK): closure creation cleanup | medium | small | — | closure_create_call |
| 7 | T7: `MethodLookup` opcode and userdata descriptor/member cache | high for interop | medium | T1 | method_call, CLR interop |
| 8 | T5: array part + insertion-ordered hash + size hints | very high | large | owner decision on `pairs` order | table_* , gc_small_tables, sort |
| 9 | T10: policy-generic loop / back-edge interrupts | medium | medium | — | all |
| 10 | T8 item 5: captured-only cells (only if struct values are deferred) | high | medium | write/capture analysis | int_math, every local read |
| 11 | T11 folding + write tracking; T12 concat; T13 pairs/ipairs; T14 coroutines | low-medium | small-medium | T5 for T13 | various |

Suggested regression tests, one per item, written to fail before the change:

- T4: closures created in a numeric `for` must each see their own `i`
  (`for i=1,3 do fs[i]=function() return i end end` → 1,2,3), and a loop variable stored into a table must
  not change after later iterations.
- T1/T6: assign `__index` after a cached miss; replace a global after it was cached; delete a field
  (`t.x = nil`) after it was cached; `rawset` a metamethod into a metatable after an absence was cached.
- T2: `math.floor = function() return 42 end` after a fast call ran; wrong argument types must produce the
  same error text as today.
- T3: `nan < x`, `x <= nan`, string comparisons, `__lt`/`__le`/`__eq` fallbacks through fused jumps.
- T7: extension methods registered after a call site was cached; two userdata types at one call site.
- T9: `rawequal` behaviour of capture-free literals (documented change).
- T11: `5 % -3`, `-5 % 3`, `5.5 % -2` against Lua 5.2 results.

## 6. Unsafe inventory

Luau is C++, so "unsafe" in the C# sense is everywhere. The parts that matter, and their safe managed
equivalents:

| Luau mechanism | Where | What it buys | Safe C# equivalent |
|---|---|---|---|
| Union-punned `TValue` (`Value` union + tag) | `lobject.h#L28-L51` | 16-byte values, no boxing | struct with `double` + `object` + tag fields (24 bytes on x64) or the `LuaValue` layout from Lua-CSharp without overlapping fields; no `Unsafe.As` needed |
| Pointer arithmetic for slot ids (`gval2slot`) | `ltable.h#L12` | slot index for caches | store `int` slot indices in node arrays |
| Patching `const Instruction*` in place (`VM_PATCH_*`) | `lvmexecute.cpp#L84-L88` | inline caches, quickening | mutable fields on MoonSharp's `Instruction` class, or a parallel cache array indexed by pc |
| Stack pointers (`StkId base`, `ra = base + A`) | throughout | register access | array + `int` base index; `ref DynValue slot = ref stack[base + a]` is safe C# 7 |
| Computed goto dispatch table | `lvmexecute.cpp#L104-L147` | 10-20% dispatch | `switch` on a dense enum (jump table) |
| `memcpy` into string buffers (`luaV_concat`) | `lvmutils.cpp#L459-L530` | one-pass concat | `string.Create`/`StringBuilder`/`string.Concat(string[])`; `string.Create` is not in netstandard2.0, so use `StringBuilder` there |
| SSE4.1 builtin variants | `lbuiltins.cpp#L2508-L2530` | faster `floor/ceil` | `Math.Floor` (the JIT emits `roundsd` itself) |

None of the recommended MoonSharp changes needs `unsafe`, `System.Runtime.CompilerServices.Unsafe`,
`MemoryMarshal` or `Reflection.Emit`.
