# MoonSharp optimisation and architecture plan

Date: 2026-10-28. Inputs: seven code-level reports in this folder (`lua-csharp.md`, `unityenhanced.md`,
`xlua.md`, `luau.md`, `jint.md`, `other-managed.md`, `moonsharp-profile.md`) plus the measured
Lua-CSharp head-to-head of [lua-csharp-measured.md](lua-csharp-measured.md) (aggregated with `aggregate.py` in
[src/DevTools/Research/LuaCSharp](../../src/DevTools/Research/LuaCSharp/README.md), 3-4 rounds x 7 runs,
.NET 8.0.33, same 4-core VM).

Target code: fork master `emindeniz99/moonsharp@58fa6bb` (upstream base `fbd0367`; the VM files have the
same line numbers, only `FastStack.cs` moved; int64 values live in `DynValue.m_Object` as
`LuaInt64`/`LuaUInt64`). Benchmark harness: `src/DevTools/Benchmarks` (`LuaWorkloads.cs`, `AbCompare.cs`).

Evidence markers: [C] confirmed by measurement or code reading, [S] secondary (reasoned estimate or
third-party data), [U] unverified.

Permalink bases used in this file (all pinned to the SHA that was read):

| Key | Base |
|---|---|
| MS | https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter |
| LC | https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a |
| UE | https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6 |
| XL | https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3 |
| XN | https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2 |
| LU | https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11 |
| JI | https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384 |
| WS | https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3 |
| SS | https://github.com/BraedonWooding/solarsharp/blob/e10a939fdc29e70b2dfef6f37faa8d9e9269c01a |
| NL | https://github.com/neolithos/neolua/blob/1840895f19d7d1ebaf88df58e5f75744258a3144 |
| UL | https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618 |

Every link below is written out in full so it can be clicked; the table is only a reading aid.

---

## 0. Ground truth

### 0.1 Three engines on the same workloads [C]

ns per iteration, median across rounds (IQR in the aggregated output). MoonSharp = fork build `msA`;
native = xLua-NG Lua 5.5.1 `flag_bench` on the same machine.

| Workload | native Lua | Lua-CSharp 0.5.8 | MoonSharp | MS / LC | MS bytes/iter | LC bytes/iter |
|---|---:|---:|---:|---:|---:|---:|
| empty_loop | 3.5 | 5.0 | 57.4 | 11.4x | 48 | 0 |
| function_call | 26.0 | 81.6 | 341 | 4.2x | 552 | 0 |
| method_call | 66.3 | 167 | 604 | 3.6x | 696 | 0 |
| table_array_insert | 16.1 | 87.6 | 1,236 | 14.1x | 371 | ~0 |
| table_array_read | 15.7 | 47.7 | 418 | 8.8x | 528 | 0 |
| table_hash_set_get | 35.8 | 98.6 | 755 | 7.7x | 912 | 0 |
| int_math | 11.3 | 33.1 | 251 | 7.6x | 288 | 0 |
| float_math | 20.8 | 42.6 | 407 | 9.5x | 576 | 0 |
| fib_recursive (us) | 10.1 | 40.8 | 181 | 4.4x | 301,368 | 0 |
| closure_create_call | 94.0 | 180 | 647 | 3.6x | 912 | 200 |
| string_format | 305 | 298 | 4,076 | 13.7x | 8,000 | - |
| string_pattern | 3,199 | 6,474 | 23,988 | 3.7x | - | - |
| coroutine_switch | 119 | 743 | 772 | **1.0x** | 1,080 | 0 |
| gc_small_tables | 129 | 340 | 1,082 | 3.2x | - | - |
| oop_inherit | 69.6 | 195 | 531 | 2.7x | - | - |
| Lua -> C# call, 1 arg | - | 73.8 | 303 | 4.1x | 504 | 0 |
| C# -> Lua call, 3 args | - | 333 | 490 | 1.5x | 952 | 48 |
| `new Script()` default / stdlib | - | 43.5 us | 1,202 us | 27.6x | 185,712 | 28,720 |

What the table says:

- A safe-ish managed register VM with struct values gets within 1.4-4x of native C Lua on the VM-bound
  rows. That is the realistic ceiling for MoonSharp.
- Lua-CSharp's lead is largest where MoonSharp allocates per operation (empty_loop, tables, arithmetic)
  and smallest where both pay host-plumbing costs (coroutines, C# -> Lua calls, table_concat).
- MoonSharp's `string.format` is 13.7x slower than both others: a library problem, not a VM problem.

### 0.2 Where MoonSharp's bytes and CPU go [C] (moonsharp-profile.md §3-4)

| Cause | Share of bytes (examples) | CPU |
|---|---|---|
| Local/upvalue read clones (`AsReadOnly`, `Processor_InstructionLoop.cs:201/:204`) + fresh cell per block iteration (`:211`) + ExecArgs double copy | empty_loop 100%, hash 62%, array_read 54%, function_call 52%, int_math 50%, fib 41% | Local/StoreLcl 10-15% |
| Per-call objects: CallStackItem 96 B, argc DynValue, Slice, LocalScope | function_call 39%, fib 33%, method_call 32% | Internal_ExecCall+ExecArgs+ExecRet 15-20% |
| Arithmetic results `NewNumber` | int_math 50%, array_read 18% | - |
| Boolean results `NewBoolean` | array_read 27%, hash 16%, fib 15% | - |
| Dispatch: `List<Instruction>` fetch, debugger + auto-yield checks, switch; `StelemRef`; `Array.Clear` for 1-3 slots | - | 30-46% fetch/dispatch, 2-4% StelemRef, 2-10% clears |
| Table: LinkedListNode + Dictionary entry + re-allocated keys; `Length` rescans | array_insert 48% | PerformTableSet 22%, Length 18%, Dictionary 19% |
| `string.format` KopiLua + Regex per specifier | >=62% of 8,000 B | ~75% |
| Interop: bound callback per `obj:M`, `HashSet<Type>` per arg, boxing, context/args/Slice | clr_interop 31% + 12% | HashSet 10%, member Dictionary 7.7% |
| Constant-key `Dictionary<string>` hash + `SequenceEqual` | - | method_call 17%, clr_interop 11% |
| Closure creation LINQ + `List.AddRange` | closure 50% | 68% |

### 0.3 Prototype A/B already measured on fbd0367 [C] (moonsharp-profile.md §7, 669/669 tests pass for A-E)

| Case | base min ns / B | after A..E | after J (unsafe experiment: no read clones) |
|---|---:|---:|---:|
| function_call | 315 / 552 | 278 / 464 (-12% / -16%) | 233 / 320 |
| fib_recursive (us) | 166.8 / 301K | 136.5 / 216K (-18% / -28%) | 112.9 / 138K |
| table_array_read | 350 / 528 | 304 / 384 (-13% / -27%) | 251 / 192 |
| table_hash_set_get | 624 / 912 | 550 / 720 (-12% / -21%) | 410 / 288 |
| closure_create_call | 562 / 912 | 375 / 704 (-33% / -23%) | 338 / 560 |
| table_array_insert | 545 / 371 | noisy / 371 | 300 / 227 (-52% time) |

A = boolean singletons + `>`/`>=` fix; C = lazy table keys + no `__newindex` probe without metatable;
D = `Instruction[]` snapshot + sealed `DynValueStack` + small clears; E = ExecArgs without Slice/double
copy + closure without LINQ + `IsNumericType` by reference compare. Patch: [proto-ABCDE.patch](../../src/DevTools/Research/Profile/proto-ABCDE.patch).

UnityEnhanced-derived proof of concept (plain cells only for captured locals, 30 lines, unityenhanced.md):
sum loop 240 -> 144 B/iter, 189 -> 163 ns; fib(15) 127 -> 85 B, 74 -> 61 ns; 8-case aliasing check matched.

---

## 1. Decision: base on Lua-CSharp minus unsafe, or modernise MoonSharp?

**Modernise MoonSharp. Take Lua-CSharp's techniques, not its codebase.** Keep one escalation gate
(B5) where a go-lua/Lua-CSharp-derived register compiler can be transplanted behind MoonSharp's API.

Decisive facts:

1. **The unsafe code is not where Lua-CSharp's speed comes from.** Its ~120 `Unsafe.*`/`MemoryMarshal`
   sites remove bounds checks and casts; the report estimates under ~10% of throughput for replacing all
   of them with indexing and real casts [S, lua-csharp.md §unsafe]. The 3.6-14x lead comes from struct
   values, register frames, array-part tables and zero-copy host calls, all of which are safe C#
   ([LC LuaValue.cs#L23-L37](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaValue.cs#L23-L37),
   [LC CallStackFrame.cs#L5-L18](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/CallStackFrame.cs#L5-L18),
   [LC LuaTable.cs#L35-L443](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaTable.cs#L35-L443),
   [LC LuaFunctionExecutionContext.cs#L8-L196](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaFunctionExecutionContext.cs#L8-L196)).
   One of its unsafe uses is a real memory-safety hazard (stack refs held across `Array.Resize`,
   VM L435-L1080), so "Lua-CSharp minus unsafe" means touching the hottest code anyway.
2. **Rebasing would drop most of what MoonSharp users depend on.** Lua-CSharp has no reflection interop
   (only `[LuaObject]` on partial, non-nested types the user owns; no overloads, no extension methods,
   Unity types need wrappers, [LC LuaObjectGenerator.Emit.cs#L104-L131](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua.SourceGenerator/LuaObjectGenerator.Emit.cs#L104-L131)),
   no Hardwire, no debugger (MoonSharp has the VS Code and remote debuggers), and targets
   net6+/netstandard2.1 with the `System.Runtime.CompilerServices.Unsafe` package (Unity load failure:
   https://github.com/nuskey8/Lua-CSharp/issues/337). MoonSharp ships netstandard2.0 + net45 and a large
   public API (`DynValue`, `Table`, `CallbackFunction`, `UserData`, `Script.Call`).
3. **Its async-everywhere VM is a liability for Unity.** Every path returns `ValueTask<int>`
   ([LC LuaVirtualMachine.cs#L272-L332](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L272-L332));
   this produced frame delays (https://github.com/nuskey8/Lua-CSharp/issues/126), a WebGL deadlock
   (https://github.com/nuskey8/Lua-CSharp/issues/327) and 3x count-hook overhead
   (https://github.com/nuskey8/Lua-CSharp/issues/338). Measured: its coroutine switch is no faster than
   MoonSharp's (743 vs 772 ns). MoonSharp's synchronous per-coroutine `Processor` is the better base.
4. **MoonSharp's gap is explained and partly closed already.** Every big cost in §0.2 maps to a named
   line; prototypes A-E cut 16-28% of bytes and 12-33% of time with no architecture change, and the
   no-clone experiment J shows another 31-60% of bytes. WattleScript, a BSD-3 MoonSharp fork, shows the
   remaining path (struct values, struct instructions, struct frames, real upvalues) is reachable
   incrementally on this exact codebase (commits listed under B1-B4).
5. **One Lua-CSharp defect must not be inherited either way**: hash tombstones are never compacted, so
   set/queue-style tables grow without bound ([LC LuaValueDictionary.cs#L188-L318](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Internal/LuaValueDictionary.cs#L102-L457)).

Escalation gate (B5): after A + B1-B3, if VM-bound rows are still more than 2x Lua-CSharp, port a
register compiler (go-lua lineage, MIT, attribution kept) behind MoonSharp's API in the NG line. Expected
not to be needed for the numbers users feel; decide on data then.

---

## 2. Merged idea catalogue

Each merged idea has an ID used in §3-5. Sources from every engine are kept.

| ID | Idea | Engines that use or motivate it | Group |
|---|---|---|---|
| M1 | Measurement safety net: allocation budgets per workload, interop/boundary workloads, alloc-by-type probe | Jint, profile | A1 |
| M2 | VM booleans from singletons; `>`/`>=` as swapped `<`/`<=` | Jint, UnityEnhanced, profile | A2 |
| M3 | Closure creation without LINQ; names per prototype | Lua-CSharp, Luau, Jint, profile | A3 |
| M4 | Lua call path: no Slice, no double arg copy, argc in frame | Jint, Luau, Lua-CSharp, profile | A4 |
| M5 | Dispatch: `Instruction[]` snapshot, sealed value stack, small clears, one slow-path flag | Lua-CSharp, Luau, profile | A5 (+B10) |
| M6 | Arithmetic number fast path; `%` semantics fix | Lua-CSharp, Luau | A6 |
| M7 | Table quick fixes: lazy keys, no probe without metatable, no O(n^2) CollectDeadKeys/Length | SolarSharp data, profile | A7 |
| M8 | Interop caches: resolved descriptor per Type (versioned), `IsNumericType` by TypeCode, custom-converter flag, `IsByRef` precompute, descriptor per MethodInfo for delegates | xLua, WattleScript, UnityEnhanced, Jint, profile | A8 |
| M9 | Interop correctness: userdata hash 999, overload-cache slot reuse, struct receiver copy + binding-mode parity suite | xLua, xlua-ng | A9 |
| M10 | Immutable values in uncaptured locals; cells only for captured locals | UnityEnhanced, Jint, Luau, WattleScript, Lua-CSharp, profile (J) | A10 |
| M11 | Small-integer DynValue cache; DynValue 48 -> 40 B (lazy RefID, no hash cache) | Jint, UnityEnhanced, profile | A11 |
| M12 | Reused call frames and pooled LocalScope arrays | Luau, UniLua, Jint, profile | A12 |
| M13 | No bound callback per `obj:Method`; enum value cache; memoized fuzzy names | xLua, Luau, UnityEnhanced, profile | A13 |
| M14 | Managed `string.format` | SolarSharp, profile | A14 |
| M15 | Module-registration cache; cheaper `new Script()` | UnityEnhanced, SolarSharp | A15 |
| M16 | Compiler peepholes: compare-and-branch, K-operand arithmetic, ForLoop opcode | Luau, Jint, Lua-CSharp | A16 |
| M17 | Interned constant keys; metamethod-absence bits; IndexN inline cache (stage A) | Luau, xLua, profile | A17 |
| M18 | Builtin fast calls guarded by CallbackFunction identity | Luau | A18 |
| M19 | Coroutine resume/yield without tuples and per-switch objects | Luau, Lua-CSharp, profile | A19 |
| M20 | Native stack-overflow guard | Lua-CSharp | A20 |
| M21 | Internal struct value on stack/slots; DynValue only at API boundary | Lua-CSharp, WattleScript, Jint, Luau, profile | B1 |
| M22 | Struct frames + locals on the value stack + open/closed upvalues | Lua-CSharp, WattleScript, Luau, UniLua, Jint | B2 |
| M23 | Table rewrite: array part + flat ordered hash, tombstones purged on resize | Lua-CSharp, Luau, Jint, NeoLua, UniLua, profile | B3 |
| M24 | Struct instructions + per-function constant pools | WattleScript, Lua-CSharp, Luau | B4 |
| M25 | Register VM / compiler | Lua-CSharp (go-lua), Luau, UniLua | B5 (gate) |
| M26 | Roslyn incremental source generator for descriptors | xlua-ng, WattleScript, Jint, Lua-CSharp | B6 |
| M27 | Zero-copy callback convention (ref struct arg view) for stdlib and generated descriptors | Lua-CSharp, xlua-ng, WattleScript, SolarSharp, UniLua | B7 |
| M28 | Per-parameter converters (typed casters) | xLua, xlua-ng, Jint | B8 |
| M29 | Typed C# -> Lua API (`ClrToScript<T>`), Lua functions as arbitrary delegates | xLua, xlua-ng | B9 |
| M30 | Debug/no-debug loop specialisation (generic struct policy), breakpoints by patching | Luau, Lua-CSharp | B10 |
| M31 | Lua pattern matcher port of lstrlib.c over string indices | SolarSharp, KopiLua (what to replace) | B11 |
| M32 | pairs/ipairs fast iteration in the loop opcode | Luau | B12 |
| M33 | Lua-style variable names in errors | Lua-CSharp | B13 |
| M34 | Prepared chunks shareable across Scripts | Jint | B14 |
| M35 | Multi-operand concat, constant folding, capture-free closure sharing | Luau | B15 |
| M36 | Owned rope for `s = s .. x` | Jint | B16 |
| M37 | Public 16-byte struct DynValue | WattleScript | B17 (NG 3.0 only) |
| R* | Rejected ideas | various | C |

---

## 3. Ranking

Score = impact / (effort x risk). Impact uses the measured shares in §0.2 and the prototype deltas in
§0.3 where they exist; [S] estimates otherwise. Effort S=1, M=3, L=8; risk low=1, medium=2, high=4;
impact 1-10 (10 = removes >= 40% of bytes or time on >= 4 workloads).

| Rank | Item | Impact | Effort | Risk | Score | Evidence |
|---:|---|---:|---:|---:|---:|---|
| 1 | A2 booleans + `>` fix | 5 | 1 | 1 | 5.0 | measured A; correctness bug |
| 2 | A3 closure without LINQ | 4 | 1 | 1 | 4.0 | measured E (-33% time) |
| 3 | A4 call-path copies | 5 | 1 | 1 | 5.0 | measured E (-18% fib) |
| 4 | A7 table quick fixes | 6 | 1 | 1 | 6.0 | measured C; SolarSharp O(n^2) data |
| 5 | A8 interop caches | 4 | 1 | 1 | 4.0 | measured E (-9% interop), code reading |
| 6 | A9 interop correctness | 3 | 1 | 1 | 3.0 | bugs [C]/[U] |
| 7 | A5 dispatch loop | 4 | 1 | 1 | 4.0 | measured D |
| 8 | A6 arithmetic fast path + `%` fix | 3 | 1 | 1 | 3.0 | code reading |
| 9 | A10 immutable locals | 10 | 3 | 2 | 1.7 | measured J, PoC |
| 10 | A14 string.format | 7 | 3 | 1 | 2.3 | 75% CPU in one workload |
| 11 | A15 module cache | 5 | 1 | 1 | 5.0 | 1.2 of 1.4 ms startup [C] |
| 12 | A11 small-int cache + 40 B DynValue | 5 | 1 | 1 | 5.0 (after A10) | share 50% int_math |
| 13 | A12 frame reuse | 6 | 3 | 2 | 1.0 | 39% bytes function_call |
| 14 | A13 bound-method cache | 5 | 3 | 2 | 0.8 | 31% bytes clr_interop |
| 15 | A17 interning + tmcache + IC | 4 | 3 | 2 | 0.7 | 17% CPU method_call |
| 16 | A16 peepholes | 4 | 3 | 2 | 0.7 | dispatch 30-46% CPU |
| 17 | A18 builtin fast calls | 3 | 1 | 1 | 3.0 | not in current workloads |
| 18 | A19 coroutine plumbing | 2 | 3 | 1 | 0.7 | 35% bytes coroutine_switch |
| 19 | A20 stack guard | 1 | 1 | 1 | 1.0 | robustness |
| - | B1 struct value | 10 | 8 | 4 | 0.3 | Lua-CSharp ceiling |
| - | B3 table rewrite | 8 | 8 | 2 | 0.5 | 14x table_array_insert gap |
| - | B2 struct frames / stack locals | 8 | 8 | 4 | 0.25 | |
| - | B6 source generator | 6 (IL2CPP) | 8 | 2 | 0.4 | xlua-ng 55.7 vs ~400 ns |

Implementation order (§4) follows the score, but puts the measurement net first and puts each item
after the item it depends on (A11 after A10, A12 after A4, A17 stage B after B3).

---

## 4. Group A: incremental, upstream-friendly, one PR each

Rules for every PR in this group:

- Regression test that fails before the change (CONTRIBUTING/AGENTS rule); behaviour fixes get a
  test that compares with Lua 5.2 semantics.
- A/B with `src/DevTools/Benchmarks` (`AbCompare.cs`), one benchmark process at a time, report median
  and min ns plus exact bytes/iter. Bytes are the primary evidence because they are deterministic on
  this noisy shared VM.
- Full suite: `DotNetCoreTestRunner --unit` (run from its `bin` folder; TestMore loads `*.t` by relative
  path) and the hosting fixtures.
- Behaviour visible to scripts or hosts goes in the migration notes in the same PR.
- Licence column: "ideas" means no code copied, credit the source in the commit body; nothing in group A
  copies code.

### A1. Measurement net: allocation budgets and interop workloads

- **Files**: `src/DevTools/Benchmarks/LuaWorkloads.cs` (add `clr_interop`, `clr_interop_reflection`,
  `lua_to_cs_call1`, `cs_to_lua_call3`, `enum_push`, `derived_type_push`, `table_insert_remove_10k`,
  `table_remove_readd`), new `src/MoonSharp.Interpreter.Tests/EndToEnd/AllocationBudgetTests.cs`
  compiled only for net8 (`#if NET`), `src/DevTools/Benchmarks/AllocTypeProbe.cs` (EventListener on
  `GCAllocationTick`).
- **Design**: run each workload warm, read `GC.GetAllocatedBytesForCurrentThread()` around N
  iterations, assert bytes/iter <= budget. Budgets start at today's exact numbers (§0.1) plus 0 B, and
  each later PR lowers its rows. The probe reports bytes by CLR type so a PR shows which allocation it
  removed. Idea from Jint ([JI SlotLocationCacheTests.cs#L250-L267](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint.Tests/Runtime/SlotLocationCacheTests.cs#L250-L267),
  [JI AllocTypeProbe.cs#L7-L20](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint.Benchmark/AllocTypeProbe.cs#L7-L20)).
- **Tests**: the budget tests themselves; one test that proves the harness fails when a deliberate
  extra allocation is added.
- **Benchmark**: none moves; this pins later gains.
- **Compatibility**: test-only, net8 only. **Licence**: ideas only (Jint BSD-2).

### A2. Boolean singletons and correct `>` / `>=`

- **Files**: `DataTypes/DynValue.cs` (internal `FromBoolean`), `Execution/VM/Processor/Processor_InstructionLoop.cs`
  (`ExecLess`, `ExecLessEq`, `ExecEq`, `ExecNot`, `ExecCNot`, `OpCode.ToBool` at L162),
  `Tree/Expressions/BinaryOperatorExpression.cs` (Compile and `EvalComparison`), `Execution/VM/ByteCode.cs`.
- **Design**: VM-produced booleans return the existing read-only `DynValue.True/False`. Compile `a > b` as
  `Swap(0,1); Less` and `a >= b` as `Swap(0,1); LessEq` (today `LessEq, CNot, Not`, three allocations and
  wrong for NaN and `__lt`); same in the constant evaluator. Public `DynValue.NewBoolean` keeps returning
  a writable value.
- **Sources**: [MS BinaryOperatorExpression.cs#L260-L330](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Tree/Expressions/BinaryOperatorExpression.cs#L260-L330),
  [MS BinaryOperatorExpression.cs#L453-L456](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Tree/Expressions/BinaryOperatorExpression.cs#L453-L456),
  [MS ByteCode.cs#L139-L150](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/ByteCode.cs#L139-L150),
  [MS Processor_InstructionLoop.cs#L1087-L1145](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L1087-L1145),
  [JI JsBoolean.cs#L5-L20](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Native/JsBoolean.cs#L5-L20),
  [UE DynValue.cs#L95-L137](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L95-L137),
  Lua 5.2 manual §3.4.3 https://www.lua.org/manual/5.2/manual.html#3.4.3.
- **Tests**: `(0/0) > 1`, `(0/0) >= 1`, `1 > (0/0)` are false; `x > y` with `__lt` calls `__lt(y, x)`;
  `>=` with only `__le`; `__le` falling back to `not __lt(b, a)`; string ordering; error text for
  `1 > "x"`; host `Assign` on a VM boolean throws "Assigning on r-value" instead of corrupting `true`.
- **Benchmark**: bytes table_array_read 528 -> 384, table_hash_set_get -16%, fib -15% (measured A).
- **Compatibility**: comparison results change only where MoonSharp deviated from Lua 5.2; document.
  **Licence**: ideas only.

### A3. Closure creation without LINQ

- **Files**: `Processor_InstructionLoop.cs` (`ExecClosure` L414-L430), `DataTypes/Closure.cs` (L61-L70),
  `Execution/Scopes/ClosureContext.cs` (L9-L26), the function-prototype metadata in `ByteCode`/`Instruction`
  (cache the `string[]` of upvalue names once per `SymbolList`).
- **Design**: a sized loop into `DynValue[]`, `ClosureContext` filled from the array (no
  `AddRange(IEnumerable)`, which is 68% of CPU in `closure_create_call`), names computed once per prototype.
- **Sources**: [MS Processor_InstructionLoop.cs#L414-L430](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L414-L430),
  [MS Closure.cs#L61-L70](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/Closure.cs#L61-L70),
  [MS ClosureContext.cs#L9-L26](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/Scopes/ClosureContext.cs#L9-L26),
  [LU Compiler.cpp#L1671-L1781](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Compiler/src/Compiler.cpp#L1671-L1781).
- **Tests**: closures in loops get distinct upvalues; shared upvalue between two closures; `debug.getupvalue`
  names and order unchanged; `string.dump` round trip of a closure with upvalues.
- **Benchmark**: closure_create_call 912 -> 704 B, min time -33% (measured E). Further ~40 B from
  per-prototype names.
- **Compatibility**: internal. **Licence**: ideas only.

### A4. Lua call path without Slice, double copy and argc DynValue

- **Files**: `Processor_InstructionLoop.cs` (`CreateArgsListForFunctionCall` L608-L631, `ExecArgs`
  L634-L664, `Internal_ExecCall` L741-L757), `Execution/VM/CallStackItem.cs` (add `int ArgCount`),
  `ExecRet`/`PopToBasePointer`, `Processor.cs` (`PushClrToScriptStackFrame`).
- **Design**: `ExecArgs` reads arguments from the value stack by index when the last argument is not a
  tuple; it stores the fresh writable copy as the cell (no `NewNil` + `CloneAsWritable` + `Assign`). The
  vararg tuple keeps its clone because it outlives the frame. The argument count goes into the frame
  instead of `DynValue.NewNumber(argsCount)` pushed on the stack; `ExecRet` reads it from the frame.
  CLR callbacks keep receiving a `Slice` until B7.
- **Sources**: [MS Processor_InstructionLoop.cs#L582-L776](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L582-L776),
  [MS Slice.cs#L12-L47](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataStructs/Slice.cs#L12-L47),
  [JI JintCallExpression.cs#L482-L560](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Interpreter/Expressions/JintCallExpression.cs#L482-L560),
  [LU lvmexecute.cpp#L1046-L1143](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L1046-L1143).
- **Tests**: fewer/more args than parameters, varargs with `select('#')`, a call whose last argument is a
  multi-return call, tail calls (`PerformTCO`), error unwinding inside a call, `debug.getlocal` on
  parameters, a coroutine yielding mid-call, a parameter mutated in the callee does not change the
  caller's local.
- **Benchmark**: fib bytes -16% (256.7K -> 215.8K), min time fib -18%, function_call -12% (measured E);
  argc removal another ~48 B/call [S].
- **Compatibility**: internal; debugger frame views unchanged. **Licence**: ideas only.

### A5. Cheaper dispatch loop and value stack

- **Files**: `Execution/VM/ByteCode.cs` (expose `Instruction[]` snapshot), `Processor_InstructionLoop.cs`
  (`Processing_Loop` L16-L47), `Processor.cs`, new `DataStructs/DynValueStack.cs` (sealed, non-generic,
  keeps fork master's `Grow`), `Processor_Scope.cs` (`ClearBlockData` L7-L18), `Processor_Debugger.cs`,
  auto-yield setter.
- **Design**: read `Instruction[]` once per loop entry and refresh when `ip` passes its end (code is
  append-only, so an old snapshot stays valid). Replace the per-instruction `DebuggerAttached` load and
  `++executed`/auto-yield compare with one `int slowPathBudget` that the debugger attach and the
  auto-yield setter arm (Lua-CSharp's single budget counter idea; keep debugger break latency at one
  instruction by setting the budget to 1 while attached). A sealed `DynValueStack` removes the
  `StelemRef` covariance check of shared generic `FastStack<__Canon>`. Clear up to 8 slots with a loop.
- **Sources**: [MS Processor_InstructionLoop.cs#L16-L47](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L16-L47),
  [MS FastStack.cs#L12-L98](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataStructs/FastStack.cs#L12-L98),
  [MS Instruction.cs#L9-L23](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Instruction.cs#L9-L23),
  [LC LuaVirtualMachine.cs#L396-L426](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L396-L426),
  [LU lvmexecute.cpp#L90-L102](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L90-L102),
  https://github.com/nuskey8/Lua-CSharp/issues/338 (never run a hook as a full Lua call).
- **Tests**: `load()` of a new chunk from inside a running function (code appended while the loop runs);
  debugger attached mid-run stops on the next instruction; `AutoYieldCounter` yields after exactly N
  instructions (pin today's count); deep recursion still grows the stack (fork master M3 tests).
- **Benchmark**: measured D after C: table_array_read 403 -> 342 ns, fib 171 -> 163 us, empty_loop 56 -> 49 ns.
- **Compatibility**: internal; auto-yield granularity unchanged. **Licence**: ideas only.

### A6. Arithmetic number fast path and the `%` fix

- **Files**: `Processor_InstructionLoop.cs` (`ExecAdd`/`Sub`/`Mul`/`Div`/`Mod`/`Power`/`Neg`, `ExecIncr`,
  `ExecToNum`), constant folding in `BinaryOperatorExpression.Eval` if it shares the helper.
- **Design**: test `Type == Number` on both popped values first and compute in place before falling back
  to `ToScalar()` + `CastToNumber()` (which returns `double?`). Fix `ExecMod`: it uses
  `IEEERemainder(a, b)` and adds `b` when the result is negative, so `5 % -3` gives `-4` (Lua: `-1`) and
  `-5 % -3` gives `1` (Lua: `-2`); positive divisors are correct [C by reading L961-L982 on fork master; arithmetic checked by hand].
  Use Lua's definition `a - floor(a/b)*b` (keep the `inf`/NaN cases of lvm.c `luai_nummod`). Integer-mode
  values (`LuaInt64`) keep their own path.
- **Sources**: [MS Processor_InstructionLoop.cs#L897-L1043](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L897-L1043),
  [LC LuaVirtualMachine.cs#L610-L623](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L610-L623),
  [LU ConstantFolding.cpp#L78-L560](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Compiler/src/ConstantFolding.cpp#L78-L560).
- **Tests**: `%` table over signs and `inf`/NaN/`-0` against Lua 5.2 values; string coercion `"10" + 1`;
  `__add` on tables; error text for arithmetic on nil.
- **Benchmark**: int_math and float_math time down 5-15% [S]; bytes unchanged (results still allocate
  until A11/B1).
- **Compatibility**: `%` with a negative divisor changes to the Lua result; document. **Licence**: none
  copied; the formula is the Lua manual's.

### A7. Table quick fixes (order-neutral)

- **Files**: `DataTypes/Table.cs` (`Set(string)`/`Set(int)`/`PerformTableSet` L162-L228, `CollectDeadKeys`
  L537-L549, `Length` L611-L625), `Processor_InstructionLoop.cs` (`ExecIndexSet` L1235-L1244),
  `CoreLib/TableModule.cs` (`insert`, `remove`).
- **Design**: (1) create the key `DynValue` only when the key is new; (2) skip the `Get` probe for
  `__newindex` when `MetaTable == null`; (3) stop calling `CollectDeadKeys` on every re-insert after a nil
  (it walks the whole list and resets the length cache): leave dead nodes until a count threshold is
  crossed (dead > live, checked on insert), which keeps `next` valid during traversal; (4) keep the length
  cache valid on append at `n+1` and on nil-at-border, so `table.insert(t, v)` does not rescan.
- **Sources**: [MS Table.cs#L162-L228](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/Table.cs#L162-L228),
  [MS Table.cs#L537-L549](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/Table.cs#L537-L549),
  [MS Table.cs#L611-L625](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/Table.cs#L611-L625),
  SolarSharp CI data https://braedonwooding.github.io/solarsharp/dev/bench/data.js and
  [SS table_array_remove_then_add_immediate.lua](https://github.com/BraedonWooding/solarsharp/blob/e10a939fdc29e70b2dfef6f37faa8d9e9269c01a/Benchmark/Tests/specific_features/table_array_remove_then_add_immediate.lua),
  https://github.com/BraedonWooding/solarsharp/commit/c65701fa61783998a934a9fd12e1915a129a3414,
  [LC commit 606696b](https://github.com/nuskey8/Lua-CSharp/commit/606696b3c0b41b72f52f75eb75bc2eb08e8be1d3) (known-prefix length cache).
- **Tests**: 10k `table.insert` and 10k remove-then-add finish under a linear time bound (assert on op
  count of the dictionary walk via an internal counter, not wall time); `next` while assigning nil to
  every key; `#t` after `t[#t] = nil`; `Table.Pairs` does not yield dead keys (today it does, see
  [table-order.md](table-order.md) §1.2); metatable added after first set still triggers `__newindex`.
- **Benchmark**: method_call 696 -> 648 B and hash 768 -> 720 B, hash min 632 -> 564 ns (measured C);
  new `table_insert_remove_10k` from ~0.5 s to ~ms [S: SolarSharp 564 -> 3 ms on 2.0.0, re-measure on master].
- **Compatibility**: iteration order unchanged (insertion order kept). **Licence**: ideas only.

### A8. Interop caches (no behaviour change)

- **Files**: `Interop/UserDataRegistries/TypeDescriptorRegistry.cs` (`GetDescriptorForType` L238-L326;
  bump a `s_Version` in `RegisterType_Impl`, `UnregisterType`, the `RegistrationPolicy` setter),
  `Interop/Converters/NumericConversions.cs` (`IsNumericType` by `Type.GetTypeCode`/reference compares
  instead of `HashSet<Type>`), `Interop/CustomConvertersCollection.cs` (internal `HasScriptToClr`/
  `HasClrToScript` flags), `Interop/Converters/ClrToScriptConversions.cs`, `ScriptToClrConversions.cs`,
  `Interop/BasicDescriptors/ParameterDescriptor.cs` (cache `IsByRef`, nullable underlying type),
  `DataTypes/CallbackFunction.cs` (`FromDelegate`/`FromMethodInfo` reuse a `MethodMemberDescriptor`
  cached per `MethodInfo` and access mode).
- **Design**: resolved descriptors (including negative results) in a dictionary keyed by
  `(Type, searchInterfaces)` and checked against `s_Version`, read without walking base types or calling
  `GetInterfaces()`. WattleScript forgets to clear on policy change; UnityEnhanced pollutes the registry
  with `PerformRegistration`. Neither mistake is copied.
- **Sources**: [MS TypeDescriptorRegistry.cs#L238-L326](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/UserDataRegistries/TypeDescriptorRegistry.cs#L238-L326),
  [MS NumericConversions.cs#L11-L60](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/Converters/NumericConversions.cs#L11-L60),
  [MS CustomConvertersCollection.cs#L107-L113](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/CustomConvertersCollection.cs#L107-L113),
  [MS CallbackFunction.cs#L87-L113](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/CallbackFunction.cs#L87-L113),
  [XL ObjectTranslator.cs#L1027-L1101](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1027-L1101),
  [XL MethodWarpsCache.cs#L320-L452](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/MethodWarpsCache.cs#L320-L452),
  [WS TypeDescriptorRegistry.cs#L188-L263](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Interop/UserDataRegistries/TypeDescriptorRegistry.cs#L188-L263),
  https://github.com/WattleScript/wattlescript/commit/34e859cd1737376f9c29f443dedba30639cec954,
  https://github.com/WattleScript/wattlescript/commit/e9a007ab3954462f7e3aaba568ab6c24285cf7df,
  [UE TypeDescriptorRegistry.cs#L239-L335](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/Interop/UserDataRegistries/TypeDescriptorRegistry.cs#L239-L335),
  [JI DefaultObjectConverter.cs#L15-L75](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Interop/DefaultObjectConverter.cs#L15-L75).
- **Tests**: register a base type after an unregistered subclass was pushed -> subclass now resolves;
  unregister -> resolves to nothing; change `RegistrationPolicy` -> cache cleared; `IsTypeRegistered` and
  `RegisteredTypes` unchanged by pushes; a custom converter registered after first use still wins;
  every numeric CLR type round-trips (byte..decimal, nullable, enum); the same delegate converted twice
  yields equal behaviour and builds one descriptor.
- **Benchmark**: clr_interop -6..9% (measured E, `IsNumericType` part); `derived_type_push` large [S:
  100-500 ns per push]; no pure-Lua change.
- **Compatibility**: none visible; thread-safe (lock or copy-on-write). **Licence**: ideas only.

### A9. Interop correctness fixes

- **Files**: `DataTypes/DynValue.cs` `GetHashCode` (L567-L610: userdata and threads hash to constant 999),
  `Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs` (`Cache`,
  L223-L293: `ArgsUserDataType` not cleared on slot reuse), `MethodMemberDescriptor.cs` (`Optimize`,
  L225-L245: value-type instance calls through `Expression.Convert`, which unboxes into a copy [U]).
- **Design**: hash userdata by `Object.GetHashCode()` (descriptor hash for static userdata) and threads by
  the coroutine; clear the signature list when a cache slot is reused; for value-type instance methods
  either use `Expression.Unbox` or skip optimisation (as `OptimizeSetter` already does). Add a parity
  suite that runs the same probes under `Reflection`, `LazyOptimized`, `Preoptimized` and `Hardwire`.
- **Sources**: [MS DynValue.cs#L567-L610](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L567-L610),
  [MS OverloadedMethodMemberDescriptor.cs#L223-L293](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs#L223-L293),
  [MS MethodMemberDescriptor.cs#L225-L245](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs#L225-L245),
  [XL ObjectTranslator.cs#L1247-L1289](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1247-L1289),
  [XL MethodWarpsCache.cs#L244-L247](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/MethodWarpsCache.cs#L244-L247),
  [XN binding-mode-parity.md](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/research/binding-mode-parity.md),
  https://github.com/emindeniz99/xlua-ng/commit/f3ae62f888f2ae633ebf34115eab3b6cffb391cd.
- **Tests**: table keyed by 10k userdata: lookup cost stays flat (count `Equals` calls); a method group
  called with 7 different userdata signatures picks the right overload every time; a mutating struct
  method behaves the same in every access mode (write the failing test first; if the struct case
  already matches, drop that part and record it).
- **Benchmark**: userdata-keyed table lookups O(n) -> O(1) [S]; nothing else moves.
- **Compatibility**: behaviour changes only where results were wrong. **Licence**: ideas only.

### A10. Immutable values in uncaptured locals (keystone)

- **Files**: `Execution/Scopes/BuildTimeScope.cs`, `BuildTimeScopeBlock.cs`, `BuildTimeScopeFrame.cs`,
  `SymbolRef.cs` (captured flag set when a nested function resolves the symbol as an upvalue);
  `Execution/VM/ByteCode.cs` (`Emit_Load`/`Emit_Store` pick plain or cell opcodes; new opcodes appended
  before `Invalid`); `Processor_InstructionLoop.cs` (`Local`, `Upvalue`, `StoreLcl`, `StoreUpv`,
  `AssignLocal`, `ExecArgs`, `ExecIncr`, `ExecClosure`, `GetUpvalueSymbol`, `ExecIndex` read clone);
  `Processor_Scope.cs` (`ClearBlockData`, `GetGenericSymbol`, `AssignGenericSymbol`);
  `Processor_Debugger.cs`, `CoreLib/DebugModule.cs` (`setlocal`, `setupvalue`, `upvalueid`);
  `Processor_BinaryDump.cs` (version bump).
- **Design**: uncaptured slots hold immutable `DynValue`s: `Local` pushes the reference, `StoreLcl`
  replaces it (no `Assign`), `Clean` does not force a new cell. Captured slots keep today's cell, so
  closures, per-iteration captures and `debug.setupvalue` keep their semantics. Values the VM creates are
  read-only at birth, so `AsReadOnly()` on table/userdata reads stops cloning. `ExecIncr` (numeric `for`)
  replaces its counter instead of mutating it. This is the "plain cell only for captured locals" design
  measured by the UnityEnhanced PoC; the fork's delegate-pair `DynValueAccessor` is not used. A debug
  build assertion checks that no uncaptured slot is written through `Assign`. Mutation audit list at
  fbd0367: `Processor_InstructionLoop.cs:362, 384, 558`, `Processor_Scope.cs:75, 86`,
  `CoreLib/DebugModule.cs:166`.
- **Sources**: [MS Processor_InstructionLoop.cs#L198-L205](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L198-L205),
  [MS Processor_InstructionLoop.cs#L354-L385](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L354-L385),
  [MS DynValue.cs#L379-L421](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L379-L421),
  [MS Processor_Scope.cs#L7-L18](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_Scope.cs#L7-L18),
  https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/commit/7c332c20c5524ee0cc208d572de3992bd5f6ea43,
  [UE DynValueAccessor.cs#L14-L41](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/DataTypes/DynValueAccessor.cs#L14-L41) (what not to do),
  [JI Binding.cs#L37-L126](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Environments/Binding.cs#L37-L126),
  [LU Compiler.cpp#L1724-L1750](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Compiler/src/Compiler.cpp#L1724-L1750),
  [LU ValueTracking.cpp#L15-L60](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Compiler/src/ValueTracking.cpp#L15-L60).
- **Tests** (aliasing suite, written first and run against today's code): `local a=1 t={a} a=2` keeps
  `t[1]==1`; closures created in a `for` and a `while` body each see their own iteration value; two
  closures share one upvalue; `goto` back into a block with a captured local (label-scope fixes 976e40f,
  ec3e6a6); `local function f` recursion; `repeat ... until x` where `x` is block-local; varargs stored and
  later read; coroutines with captured locals; `debug.setlocal` on captured and uncaptured locals;
  `debug.upvalueid`/`upvaluejoin`; a host reading a local via the debugger and calling `Assign` gets the
  documented error.
- **Benchmark**: bytes empty_loop 48 -> 0, table_hash_set_get -60%, table_array_read -50%,
  function_call -31%, fib -36%, int_math -33%; time -11..26%, table_array_insert -52% (J measurements on
  top of E). A10 must reach J's numbers without J's aliasing.
- **Compatibility**: no public signature change; more VM values are `ReadOnly`; dump version bump if new
  opcodes; debugger keeps working through the same scope objects. **Licence**: ideas only (UnityEnhanced
  BSD-3 idea credited in the commit body).

### A11. Small-integer cache and a 40-byte DynValue

- **Files**: `DataTypes/DynValue.cs` (internal `FromNumberShared`; remove eager `m_RefID = ++s_RefIDCounter`
  and the `m_HashCode` cache; lazy `ReferenceID`), arithmetic sites in `Processor_InstructionLoop.cs`,
  `ExecLen`, `CoreLib/TableIteratorsModule.cs` (`ipairs`/`next` indices), `StringModule` (`len`, `byte`),
  `CoreLib/DebugModule.cs` (`upvalueid` uses the A10 cell), `MoonSharp.VsCodeDebugger/DebuggerLogic/VariableInspector.cs`.
- **Design**: integral results in about [-256, 4096) excluding -0 come from a static array of read-only
  instances (Jint's 10,240 `JsNumber`s; integral test by `(long)` round trip, not `% 1`). `ReferenceID`
  is assigned on first read from a counter (debugger and `tostring` of references only). Dropping both
  fields packs the object into 40 B on x64 and removes a shared static write per allocation.
- **Sources**: [JI JsNumber.cs#L21-L47](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Native/JsNumber.cs#L21-L47),
  [JI JsNumber.cs#L103-L175](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Native/JsNumber.cs#L103-L175),
  [UE DynValue.cs#L14-L44](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L14-L44),
  [MS DynValue.cs#L12-L28](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L14-L28).
- **Tests**: `-0` stays `-0` (`1/x == -inf`); `ReferenceID` unique and stable per object; equal small
  results are `rawequal`; `Table` keys with cached numbers; a host `Assign` on a cached number throws.
- **Benchmark**: int_math and table_array_* bytes -10..30% [S]; every remaining DynValue 17% smaller.
- **Compatibility**: `ReferenceID` values become sparse, documented as debug identifiers. **Licence**: ideas only.

### A12. Reused call frames and pooled LocalScope arrays

- **Files**: `Execution/VM/CallStackItem.cs`, `DataStructs/FastStack.cs` (reuse the object in the slot on
  push, `Reset()` fields), `Processor_InstructionLoop.cs` (`Internal_ExecCall`, `ExecBeginFn` L582-L590,
  `ExecRet`, `PerformTCO`), `Processor_Debugger.cs` and `Processor_Coroutines.cs` (snapshots copy frame
  data), `Processor_Errors`/unwinding.
- **Design**: the class stays (no ref-return churn yet); popped frames are cleared and reused. LocalScope
  arrays come from a per-processor free list bucketed by size and are cleared on return. Safe because
  closures capture cells, never the array ([MS L422-L430](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L414-L430)).
  On error unwinding do not return arrays (a lost array is lost reuse only).
- **Sources**: [MS CallStackItem.cs#L5-L22](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/CallStackItem.cs#L5-L22),
  [LU lstate.h#L57-L79](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lstate.h#L57-L79),
  [UL Do.cs#L109-L179](https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618/Assets/UniLua/Do.cs#L109-L179),
  [JI RecursiveEnvPool.cs#L17-L45](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Environments/RecursiveEnvPool.cs#L17-L45).
- **Tests**: debugger call-stack view after the frame returned; `debug.traceback` inside `xpcall` handler;
  error thrown from depth 50 then a new call reuses frames cleanly; coroutine suspended with frames,
  another coroutine runs, first resumes; `ClrToScript` frames.
- **Benchmark**: function_call/fib/method_call bytes -96 B and -24..40 B per call; time -10..20% [S].
- **Compatibility**: internal. **Licence**: ideas only.

### A13. Interop calls: no bound callback per `obj:Method`, enum cache, fuzzy-name memo

- **Files**: `Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs` (`Index` L218-L315, fuzzy path
  L234-L249, L347-L349), `OverloadedMethodMemberDescriptor.cs` (`GetValue` L423-L474),
  `FunctionMemberDescriptorBase.cs` (L103-L106), `DataTypes/UserData.cs`, `StandardEnumUserDataDescriptor.cs`,
  `Interop/PropertyTableAssigner.cs`.
- **Design**: phase 1: cache the bound read-only callback `DynValue` per (descriptor, member, object)
  in one or two fields on the `UserData` instance, so repeated `obj:M()` reuses it; `obj.M` as a value
  still returns a bound callable. Phase 2 (with A16 compiler work): a colon call on userdata invokes the
  member with `self = args[0]` and skips binding. Enum values: per-descriptor table of read-only
  `DynValue`s by underlying value. Fuzzy names: per-descriptor map from requested name to resolved member
  or a no-match sentinel, keyed by the `FuzzySymbolMatching` flags and the extension-method version.
- **Sources**: [MS OverloadedMethodMemberDescriptor.cs#L423-L474](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs#L423-L474),
  [MS DispatchingUserDataDescriptor.cs#L218-L315](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs#L218-L315),
  [MS FunctionCallExpression.cs#L69-L94](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Tree/Expressions/FunctionCallExpression.cs#L69-L94),
  [XL xlua.c#L389-L466](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/build/xlua.c#L389-L466),
  [XL Utils.cs#L1049-L1160](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/Utils.cs#L1049-L1160),
  [XL ObjectTranslator.cs#L1198-L1220](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1198-L1220),
  [LU lvmexecute.cpp#L919-L1044](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L919-L1044),
  [UE DispatchingUserDataDescriptor.cs#L233-L250](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs#L233-L250) (deletes the feature; we memoize instead),
  https://github.com/moonsharp-devs/moonsharp/pull/267.
- **Tests**: `local f = obj.Method; f(obj, 1)` and `obj.Method(obj, 1)` still work in all three
  `ColonOperatorBehaviour` modes; extension method registered after first call is found; `obj.someMethod`
  (fuzzy) and a negative probe `if obj.missing` stay correct after caching; enum equality and `tostring`.
- **Benchmark**: clr_interop bytes -17% (bound-callback share) in phase 1, -30..45% with A10 [S];
  `enum_push` -2..3 allocations.
- **Compatibility**: two references per `UserData` in phase 1. **Licence**: ideas only.

### A14. Managed `string.format`

- **Files**: `CoreLib/StringModule.cs` (`format`), new `CoreLib/StringLib/LuaFormatter.cs`,
  `CoreLib/StringLib/KopiLua_StrLib.cs` (`str_format` kept as fallback), `Interop/LuaStateInterop/Tools.cs`.
- **Design**: parse a format string once into a spec list (small per-Script LRU keyed by format string),
  render `%d %i %u %c %x %X %o %e %E %f %g %G %q %s %%` with flags, width and precision into a
  `StringBuilder` reused per `Processor`, invariant culture; anything unsupported (or any spec where the
  managed result could differ, e.g. `%a`) falls back to KopiLua.
- **Sources**: [MS KopiLua_StrLib.cs#L891-L990](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/CoreLib/StringLib/KopiLua_StrLib.cs#L891-L990),
  [MS Tools.cs#L290](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/LuaStateInterop/Tools.cs#L290),
  [MS Tools.cs#L592-L598](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/LuaStateInterop/Tools.cs#L592-L598),
  [SS LuaRegex.cs#L1-L30](https://github.com/BraedonWooding/solarsharp/blob/e10a939fdc29e70b2dfef6f37faa8d9e9269c01a/SolarSharp.Interpreter/Regex/LuaRegex.cs#L1-L30) (same motivation for patterns).
- **Tests**: differential corpus of ~2,000 generated format/argument pairs compared against the current
  KopiLua output (golden file) and, where they differ, against native Lua 5.2 output recorded once;
  `%5.2s`, `%#x`, `% d`, `%+.3g`, huge widths (error), `%d` on 3.5 (error text), `%s` calling `__tostring`,
  `%q` with control chars and `\0`, culture `tr_TR`/`de_DE` (fork has `fix/culture-invariant` probes).
- **Benchmark**: string_format 4,076 ns -> under 1,000 ns, bytes 8,000 -> under 500 [S]; Lua-CSharp
  does 298 ns.
- **Compatibility**: byte-identical output for supported specs. **Licence**: own code; spec semantics from
  the Lua manual and C printf.

### A15. Module registration cache and cheaper `new Script()`

- **Files**: `Modules/ModuleRegister.cs` (L84-L160), `Platforms/PlatformAutoDetector.cs`, optionally
  `CoreLib/LoadModule.cs` (L215-L242).
- **Design**: cache per module `Type` the list of `(name, delegate factory, constants, MoonSharpInit)`
  in a thread-safe static; create fresh `CallbackFunction`s per Script from it. `require` stays Lua source
  in this PR. A follow-up may make `require` a C# callback that returns a tail-call request with a
  continuation (pattern of `pcall` in `ErrorHandlingModule.cs`); that changes `debug.getinfo(require).what`
  and `string.dump(require)`, so it needs an owner decision. UnityEnhanced's C# `require` is not copied:
  it is not yieldable and compares with `DynValue.Nil` while `Script.Call` returns `Void`.
- **Sources**: [MS ModuleRegister.cs#L84-L160](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Modules/ModuleRegister.cs#L84-L160),
  [MS LoadModule.cs#L215-L242](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/CoreLib/LoadModule.cs#L215-L242),
  [UE ModuleRegister.cs#L77-L147](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/Modules/ModuleRegister.cs#L77-L147),
  [UE LoadModule.cs#L219-L258](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/CoreLib/LoadModule.cs#L219-L258),
  https://github.com/BraedonWooding/solarsharp/commit/36cb3f1b03c8d9f668058110be29fc531d7e53cb.
- **Tests**: two Scripts do not share callback objects or module tables; sandbox `CoreModules` presets
  produce the same globals as today (snapshot of global names per preset); parallel `new Script()` on 4
  threads.
- **Benchmark**: `new Script()` default 1,202 us / 185,712 B -> under 300 us [S]; measured split:
  None 218 us, Basic 554, LoadMethods 707, Preset_Default 1,411.
- **Compatibility**: none in this PR. **Licence**: ideas only.

### A16. Compiler peepholes: compare-and-branch, K-operand arithmetic, ForLoop

- **Files**: `Execution/VM/OpCode.cs` (append before `Invalid`), `ByteCode.cs` emitters,
  `Tree/Expressions/BinaryOperatorExpression.cs`, `Tree/Statements/IfStatement.cs`, `WhileStatement.cs`,
  `RepeatStatement.cs`, `ForLoopStatement.cs` (L52-L98), `Processor_InstructionLoop.cs`,
  `Processor_BinaryDump.cs` (version bump, old dumps still load), `InstructionFieldUsage.cs`.
- **Design**: `Less/LessEq/Eq` followed by `Jf` becomes `JLt/JLe/JEq/JNe` (raw bool, no value); equality
  with a nil/number/string constant becomes `JEqK`; `Literal k` + arithmetic becomes `AddK`-style ops;
  numeric `for` fuses `Incr`+`Jump`+`JFor` into one `ForLoop` and drops the `..end` debug no-op while
  keeping `m_RefEnd` on the instruction. Metamethod fallbacks call the existing
  `Internal_InvokeBinaryMetaMethod`. Each fused op keeps the comparison's `SourceRef` for errors.
- **Sources**: [LU Bytecode.h#L214-L245](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Common/include/Luau/Bytecode.h#L214-L245),
  [LU Bytecode.h#L289-L301](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Common/include/Luau/Bytecode.h#L289-L301),
  [LU Compiler.cpp#L2034-L2124](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Compiler/src/Compiler.cpp#L2034-L2124),
  [LU lvmexecute.cpp#L2511-L2561](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L2511-L2561),
  [JI JintExpression.cs#L146-L153](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Interpreter/Expressions/JintExpression.cs#L146-L153),
  [LC OpCode.cs#L3-L62](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/OpCode.cs#L3-L62),
  [MS ForLoopStatement.cs#L52-L98](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Tree/Statements/ForLoopStatement.cs#L52-L98).
- **Tests**: NaN in every fused compare; `__lt`/`__le`/`__eq` through fused jumps; `for` with non-number
  bounds (same error text), negative/zero/fractional step, captured loop variable per iteration; debugger
  step-over in a `for` and an `if`; dump/undump round trip and loading a previous-version dump.
- **Benchmark**: empty_loop 7 -> 3 dispatches per iteration, ~-30% time [S]; fib and int_math -10..20%.
- **Compatibility**: dump format version bump. **Licence**: ideas only.

### A17. Interned constant keys, metamethod-absence bits, IndexN inline cache (stage A)

- **Files**: compile-time key interning per Script (`Tree/Expressions/IndexExpression.cs`,
  `SymbolRefExpression.cs` for globals, `ByteCode.cs`), `DataTypes/Table.cs` (store the interned key
  instance; `int MetaAbsentBits` cleared on any string-key write), `Processor_IExecutionContext.cs`
  (`GetMetamethodRaw` L74-L87 checks the bit), `Execution/VM/Instruction.cs` (runtime-only cache fields),
  `Processor_InstructionLoop.cs` (`ExecIndex`, `ExecIndexSet` L1216-L1383).
- **Design**: (1) the compiler interns identifier and literal string keys in a per-Script pool so the
  `Dictionary<string>` equality hits `ReferenceEquals` before `SequenceEqual`. (2) a table used as a
  metatable remembers "no `__index`/`__newindex`/`__len`/`__eq`/arith" bits; any string-key write clears
  all bits. (3) `IndexN`/`IndexSetN`/global `Index` keep `(Table, LinkedListNode<TablePair>)`; valid while
  the table matches and `node.List != null`. Stage B (slot index into the B3 hash) replaces (3).
- **Sources**: [LU ltm.h#L43-L47](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/ltm.h#L43-L47),
  [LU ltm.cpp#L97-L108](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/ltm.cpp#L97-L108),
  [LU lvmexecute.cpp#L387-L448](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L387-L448),
  [LU lvmutils.cpp#L166-L181](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmutils.cpp#L166-L181),
  [MS Processor_InstructionLoop.cs#L1295-L1376](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L1295-L1376),
  [MS Table.cs#L363-L406](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/Table.cs#L363-L406),
  [MS Processor_IExecutionContext.cs#L74-L87](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_IExecutionContext.cs#L74-L87).
- **Tests**: field removed and re-added between two executions of the same instruction; `rawset` and host
  `Table.Set` invalidate; metatable gains `__index` after a cached miss; same function run on two tables
  of the same shape; coroutine sharing the instruction.
- **Benchmark**: method_call and oop_inherit -15..30% [S] (17% CPU in dictionary/hash/SequenceEqual);
  coroutine_switch ~-8% (global+field lookup of `coroutine.yield`).
- **Compatibility**: instruction cache fields are not serialised. **Licence**: ideas only.

### A18. Builtin fast calls

- **Files**: `DataTypes/CallbackFunction.cs` (internal `FastId`), `Modules/ModuleRegister.cs` (assign ids),
  `Processor_InstructionLoop.cs` (`Internal_ExecCall` CLR branch L702-L739).
- **Design**: for `math.floor/abs/min/max/sqrt`, `string.sub/byte/len`, `type`, `rawget/rawset/rawequal`,
  `select('#')`, `table.insert` (2 args), `tostring`/`tonumber` on primitives: when the callee is the
  registered `CallbackFunction` instance and the arguments have the exact expected types, compute on the
  stack and skip context/args/frame; otherwise fall through to the normal call so errors are identical.
- **Sources**: [LU lvmexecute.cpp#L3010-L3057](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L3010-L3057),
  [LU lbuiltins.cpp#L173-L183](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lbuiltins.cpp#L173-L183),
  [LU Builtins.cpp#L19-L64](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Compiler/src/Builtins.cpp#L19-L64),
  https://github.com/luau-lang/luau/pull/2365.
- **Tests**: user replaces `math.floor` -> replacement runs; wrong argument types give today's error text;
  `debug.traceback` inside a metamethod called from a builtin.
- **Benchmark**: new `builtin_calls` workload 3-5x [S]; existing workloads barely move.
- **Compatibility**: a fast-called builtin does not appear in a traceback while running (same as Luau);
  document. **Licence**: ideas only.

### A19. Coroutine switch plumbing

- **Files**: `CoreLib/CoroutineModule.cs` (L33-L40, L84-L88), `Execution/VM/Processor/Processor_Coroutines.cs`
  (L55-L60), `YieldRequest`.
- **Design**: move resume/yield values directly between the two value stacks instead of `NewTuple` +
  two `GetArray()` copies; one reusable `YieldRequest` per Processor.
- **Sources**: [LU lstate.cpp#L31-L42](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lstate.cpp#L31-L42),
  [MS Processor_Coroutines.cs](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_Coroutines.cs).
- **Tests**: yield/resume with 0, 1, many values and nil holes; `coroutine.wrap` errors; auto-yield
  (`AutoYieldCounter`) resume path; coroutine recycling (#301) still works.
- **Benchmark**: coroutine_switch 1,080 B -> ~400 B, 772 -> ~500 ns [S].
- **Compatibility**: internal. **Licence**: ideas only.

### A20. Native stack-overflow guard

- **Files**: `Internal_ExecCall` CLR branch, `Processor.Call` re-entry, parser recursion in `Tree/`.
- **Design**: `RuntimeHelpers.EnsureSufficientExecutionStack()` (netstandard2.0, net45, IL2CPP) before
  re-entering the VM from C# and in recursive descent; convert to `ScriptRuntimeException`/syntax error.
- **Sources**: [LC LuaFunction.cs#L20-L30](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaFunction.cs#L20-L30),
  https://github.com/nuskey8/Lua-CSharp/commit/3012ad64e40194a103bb87d9e9c9571f5c5149ca.
- **Tests**: Lua -> C# -> Lua recursion 100k deep raises a script error, process survives; parser on
  `((((...` 100k deep.
- **Benchmark**: neutral (one check per CLR re-entry). **Licence**: ideas only.

---

## 5. Group B: larger architectural changes

Where to do them: the fork's master is already a MoonSharp line with features upstream lacks (int64,
hosting, grow-on-demand stacks). Call the B line "MoonSharp-NG" in that fork, keep the public API
(`DynValue` class, `Table`, `CallbackFunction`, `UserData`, debugger) unless B17 is chosen, and offer
pieces upstream when they stand alone.

### B1. Internal struct value behind public DynValue — NG, after A10-A12

- **Why**: removes the last per-operation allocation (arithmetic results, constants). It is the main
  difference between MoonSharp and Lua-CSharp on empty_loop, int_math, float_math (§0.1).
- **Design**: `internal readonly struct Value { object o; double n; DataType t; }` (24 B; 16 B variant
  with a type sentinel in `o` like WattleScript) on the value stack, in uncaptured slots and later in
  table storage. `DynValue` is materialised at API boundaries (`CallbackArguments`, `Table.Get`,
  `Script.Call`) with cached nil/booleans/small ints. int64 objects stay in `o`.
- **Sources**: [LC LuaValue.cs#L23-L37](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaValue.cs#L23-L37),
  [LC LuaVirtualMachine.cs#L610-L719](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L610-L719),
  [WS DynValue.cs#L16-L48](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/DataTypes/DynValue.cs#L16-L48),
  https://github.com/WattleScript/wattlescript/commit/abbf807d0aa013a1cfc2292b7be02d23a04886b1,
  https://github.com/WattleScript/wattlescript/commit/f366e870529dc309b1c8224424e4d9c5bd8e19dd,
  [JI Binding.cs#L7-L20](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Environments/Binding.cs#L7-L20),
  https://github.com/sebastienros/jint/pull/2499.
- **Expected**: empty_loop, int_math, float_math to within ~2x of Lua-CSharp [S].
- **Risk**: every VM helper and the callback plumbing; boundary allocations must stay cheap. IL2CPP fine
  (plain struct, no `Unsafe`). **Recommendation**: do it in NG, behind the A1 budgets.

### B2. Struct frames, locals on the value stack, open/closed upvalues — NG, together with or after B1

- **Design**: `CallStackItem` becomes a struct in a growable array accessed by `ref`; locals live in a
  register window at `BasePointer`; captured locals use `Upvalue` objects that point at (stack, index)
  while open and copy out on block exit/return (`CloseUp`). Rare frame fields move to a side object.
  Re-fetch the frame ref after any op that can grow the stack (WattleScript `goto new_frame`); never hold
  a ref across `Array.Resize` (the Lua-CSharp hazard).
- **Sources**: [LC CallStackFrame.cs#L5-L18](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/CallStackFrame.cs#L5-L18),
  [LC UpValue.cs#L5-L76](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/UpValue.cs#L5-L76),
  [LC LuaState.cs#L724-L751](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaState.cs#L724-L751),
  [LC LuaVirtualMachine.cs#L1421-L1527](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L1421-L1527),
  [WS Upvalue.cs#L9-L44](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/Scopes/Upvalue.cs#L9-L44),
  [WS CallStackItem.cs#L6-L24](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/CallStackItem.cs#L6-L24),
  [WS Processor_InstructionLoop.cs#L37-L66](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L37-L66),
  https://github.com/WattleScript/wattlescript/commit/2b55309bff10115441e28be8384a8459c594d22a,
  https://github.com/WattleScript/wattlescript/commit/1daaff1215ffaf45e90b7d5d84ebbe7b20cb523d,
  https://github.com/WattleScript/wattlescript/commit/6ce39e65e1bdbcbfa446ea93e70e6866a3a4267e,
  [JI RefStack.cs#L11-L71](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Collections/RefStack.cs#L11-L71),
  [LU lfunc.cpp#L98-L155](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lfunc.cpp#L98-L155).
- **Expected**: function_call/fib/method_call to within ~1.5-2x of Lua-CSharp [S].
- **Recommendation**: NG. Supersedes A12's pooling; A10's captured-flag analysis is reused.

### B3. Table rewrite: array part + flat hash part — NG first, upstream when order is settled

- **Design**: `Value[]` array part for keys 1..n (Lua `computesizes` >50% rule on rehash, migrate integer
  keys from the hash on growth, known-prefix length cache); hash part = struct entries
  `{hash, key, value, next}` + `int[] buckets`, insertion-ordered dense entries so string-key order is
  preserved; nil assignment leaves a tombstone so `next` stays valid; tombstones purged on resize
  (Lua-CSharp's never-compacted defect avoided); no SolarSharp-style "any int key < 16M goes to the
  array". Public `Table` API unchanged; `CollectDeadKeys` becomes a no-op. Enables A17 stage B (slot
  index caches) and B12.
- **Order decision for the owner**: today MoonSharp iterates in insertion order. The fork's
  `table-order` spike ([table-order.md](table-order.md) §1) found no repo test that depends on order and
  showed: option (a) keep insertion order everywhere, (b) integer keys 1..#t first then insertion order
  (same `json.serialize` output as master), (c) hash order (changes per process on .NET Core because of
  randomized string hashes; reject). Recommendation: (b), documented, because it matches Lua and keeps
  JSON stable.
- **Sources**: [LC LuaTable.cs#L35-L443](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaTable.cs#L35-L443),
  [LC LuaValueDictionary.cs#L102-L457](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Internal/LuaValueDictionary.cs#L102-L457),
  [LU ltable.cpp#L4-L23](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/ltable.cpp#L4-L23),
  [LU ltable.cpp#L881-L968](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/ltable.cpp#L881-L968),
  [LU ltable.cpp#L1362-L1424](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/ltable.cpp#L1362-L1424),
  [LU TableShape.cpp#L1-L155](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Compiler/src/TableShape.cpp#L1-L155),
  [JI StringDictionarySlim.cs#L17-L60](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Collections/StringDictionarySlim.cs#L17-L60),
  [JI HybridDictionary.cs#L8-L60](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Collections/HybridDictionary.cs#L8-L60),
  [NL LuaTable.cs#L1007-L1029](https://github.com/neolithos/neolua/blob/1840895f19d7d1ebaf88df58e5f75744258a3144/NeoLua/LuaTable.cs#L1007-L1029),
  [UL LuaTable.cs#L415-L495](https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618/Assets/UniLua/LuaTable.cs#L415-L495),
  [SS Table.cs#L387-L463](https://github.com/BraedonWooding/solarsharp/blob/e10a939fdc29e70b2dfef6f37faa8d9e9269c01a/SolarSharp.Interpreter/DataTypes/Table.cs#L387-L463) (rule to avoid),
  https://github.com/WattleScript/wattlescript/commit/88c951c0c838f747f465bd505170d67eae000f63.
- **Expected**: table_array_insert 1,236 -> ~100-250 ns, table_array_read -> ~100 ns, hash -2x [S].
- **Licence**: write our own; if any of `LuaValueDictionary`/`DictionarySlim` (MIT .NET Foundation) or the
  `luaH_getn`/`computesizes` algorithms (Lua MIT) is transliterated, keep the MIT header and add a
  THIRD-PARTY-NOTICES entry.

### B4. Struct instructions and per-function constant pools — NG, after B2

- **Design**: packed `ulong` instructions in a dense per-function array, `strings[]`/`numbers[]`/
  `functions[]` pools, parallel `SourceRef[]`; runtime caches in a parallel array. Operand range limits
  need compile errors. Dump format version bump.
- **Sources**: [WS Instruction.cs#L12-L73](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/Instruction.cs#L12-L73),
  [WS FunctionProto.cs#L16-L52](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/FunctionProto.cs#L16-L52),
  https://github.com/WattleScript/wattlescript/commit/3811213069bbedb38263068228f2826d9110338d,
  [LC Instruction.cs#L89-L139](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/Instruction.cs#L89-L139).
- **Expected**: 10-20% across VM workloads [S], smaller chunks, faster load. Also makes B14 possible.

### B5. Register VM (escalation gate) — NG only, only if the gate trips

- **Gate**: after A + B1-B3, VM-bound rows still > 2x Lua-CSharp.
- **Options**: (a) evolve the stack machine with superinstructions (A16) and RK-like operands; (b) port a
  register compiler: Lua-CSharp's compiler is a port of Shopify/go-lua (both MIT) — copying needs both
  MIT notices; (c) write our own register allocator over MoonSharp's AST. Recommendation: (a) first; (b)
  over (c) if needed, because go-lua's code generator is proven against the Lua 5.2 test suite.
- **Sources**: [LC LuaVirtualMachine.cs#L385-L1110](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L385-L1110),
  [LC Instruction.cs#L63-L125](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/Instruction.cs#L63-L125),
  [UL LuaObject.cs#L309-L321](https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618/Assets/UniLua/LuaObject.cs#L309-L321).
- **Cost**: debugger (source refs, breakpoints, locals), binary dump, all compiler tests. Weeks.

### B6. Roslyn incremental source generator for descriptors — MoonSharp, additive

- **Design**: `IIncrementalGenerator` on Roslyn 4.3.1 (Unity 2021.3), `ForAttributeWithMetadataName` on
  `[MoonSharpUserData]` plus an assembly-level list for third-party types; emits classes deriving from
  the existing `HardwiredUserDataDescriptor` with typed argument reads (`args[i].Number -> int`), direct
  calls, typed results, no `object[]`, no boxing; one `switch` per type for member lookup (xlua-ng's
  "descriptor mode" cut IL2CPP APK size ~8% vs one class per member); disjoint overload groups dispatch by
  `DataType`, ambiguous ones use MoonSharp's existing weights; per-member reflection fallback with a
  diagnostic; registration via an assembly marker read by `UserData.RegisterAssembly` and
  `RuntimeInitializeOnLoadMethod` under Unity. Hardwire stays. Parity suite from A9 must cover it.
- **Sources**: [XN XLuaBindingGenerator.cs#L19-L66](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/XLuaBindingGenerator.cs#L19-L66),
  [XN Wrappers.cs#L1329-L1524](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L1329-L1524),
  [XN Wrappers.cs#L1744-L1838](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L1744-L1838),
  [XN Binding.cs#L317-L420](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Src/Binding.cs#L317-L420),
  [XN descriptor-wrappers.md#L130-L210](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/research/descriptor-wrappers.md#L130-L210),
  [XN source-generator.md](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/design/source-generator.md),
  https://github.com/emindeniz99/xlua-ng/commit/2351d5b0363b33a0ba8c4ce6b2ed9484f3f5a29a,
  https://github.com/emindeniz99/xlua-ng/commit/380625fe7baad5dc9d4b9d56b8684b8e03c47168,
  https://github.com/emindeniz99/xlua-ng/commit/fb25a5c84c96afa1050eb72532e7733fbcbdf7cb,
  https://github.com/emindeniz99/xlua-ng/commit/056cb9777d9d9b44047f26d6eeef87e8f3686c0d,
  [XL LuaClassWrap.tpl.txt#L371-L450](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/Editor/Template/LuaClassWrap.tpl.txt#L371-L450),
  [WS HardwireInterop.cs#L34-L140](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.HardwireGen/HardwireInterop.cs#L34-L140),
  https://github.com/WattleScript/wattlescript/commit/254bdc1743791896aed5e2f062e53efee35bd5be,
  [JI JsAccessibleRegistry.cs#L10-L89](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Interop/JsAccessibleRegistry.cs#L10-L89),
  https://github.com/sebastienros/jint/pull/3333,
  [LC LuaObjectGenerator.Emit.cs#L600-L700](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua.SourceGenerator/LuaObjectGenerator.Emit.cs#L600-L700) (its O(n) if-chain lookup is the thing to avoid),
  https://github.com/nuskey8/Lua-CSharp/issues/194.
- **Expected**: interop calls 3-6x on IL2CPP, where today's path is `MethodBase.Invoke` [S; xlua-ng
  measured 55.7 ns generated vs ~400 ns reflection].
- **Licence**: xlua-ng and xLua are MIT; ideas only, no generator code copied. **Recommendation**: build
  in MoonSharp (useful upstream too), after A8/A9/A13 so the runtime side is stable.

### B7. Zero-copy callback convention — NG

- **Design**: internal `FastInvoke(Script, object self, ref ArgView args)` where `ArgView` is a `ref
  struct` over the value-stack range (array + offset; no `Span` requirement on net45); results pushed in
  place, count returned. Public `Func<ScriptExecutionContext, CallbackArguments, DynValue>` stays as an
  adapter. Stdlib (`string`, `math`, `table`) and B6 descriptors use the fast form.
- **Sources**: [LC LuaFunctionExecutionContext.cs#L8-L196](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaFunctionExecutionContext.cs#L8-L196),
  [LC LuaVirtualMachine.cs#L1482-L1526](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L1482-L1526),
  [XN LuaCall.cs#L735-L830](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaCall.cs#L735-L830) (pointer version; the managed ref struct is the safe equivalent),
  https://github.com/WattleScript/wattlescript/commit/bbe9fece81ceb104b1b2ae1e17d3e2a485f458cb,
  https://github.com/BraedonWooding/solarsharp/commit/a4324b615e99adf70896b4fb1c6709199b8bed7e,
  https://github.com/BraedonWooding/solarsharp/commit/144f43c1ca28a5f77e82a8a88dcf734ca1906432.
- **Expected**: Lua -> C# call 303 ns / 504 B -> ~100 ns / ~0 B (Lua-CSharp: 74 ns / 0 B) [S].
- **Note**: do not pool `ScriptExecutionContext`/`CallbackArguments` for user callbacks; users may keep them.

### B8. Per-parameter converters — MoonSharp, after A8

- Pick a converter per parameter at descriptor build time (Int32, Double, String, Bool, Enum,
  UserData(Type), DynValue, Table, Nullable(inner), generic fallback), each with `Convert` and `Score`;
  keep allocating `object[]` (Jint measured `ArrayPool` slower); replace `DoubleToType`'s try/catch with
  range checks; `CustomConvertersCollection` version so custom converters still win; parity test runs
  every case through old and new paths.
- Sources: [XL MethodWarpsCache.cs#L63-L146](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/MethodWarpsCache.cs#L63-L146),
  [XL ObjectCasters.cs#L223-L246](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectCasters.cs#L223-L246),
  [XN ObjectCasters.cs#L276-L340](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Src/ObjectCasters.cs#L276-L340),
  [JI MethodDescriptor.cs#L686-L698](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Interop/MethodDescriptor.cs#L686-L698),
  [JI MethodInfoFunction.cs#L377-L470](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Interop/MethodInfoFunction.cs#L377-L470),
  [MS ScriptToClrConversions.cs#L80-L200](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/Converters/ScriptToClrConversions.cs#L80-L200),
  [MS FunctionMemberDescriptorBase.cs#L131-L229](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/MemberDescriptors/FunctionMemberDescriptorBase.cs#L131-L229).
- Also: Jint builds compiled typed accessors only when `RuntimeFeature.IsDynamicCodeCompiled`, because an
  interpreted expression lambda is slower than reflection
  ([JI CompiledMemberAccessor.cs#L13-L131](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Runtime/Interop/Reflection/CompiledMemberAccessor.cs#L13-L131),
  https://github.com/sebastienros/jint/pull/2733, https://github.com/sebastienros/jint/pull/2744).
  MoonSharp's `LazyOptimized` should apply the same check on runtimes where it is available.

### B9. Typed C# -> Lua API and Lua functions as delegates — MoonSharp, additive

- `ClrToScript<T>`/`ScriptToClr<T>` static generic caches; `Closure.Call<T1..Tn>`, `Table.Get<T>/Set<T>`;
  a Lua function converts to any delegate type, cached per (Closure, delegate type) so `+=`/`-=` see the
  same instance; reference-type signatures via generic bodies under AOT, value-type ones only from B6
  generated code, clear error otherwise.
- Sources: [XL ObjectTranslator.cs#L912-L937](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L912-L937),
  [XL ObjectTranslator.cs#L349-L550](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L349-L550),
  [XL DelegateBridge.cs#L25-L85](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/DelegateBridge.cs#L25-L85),
  [XL GenericDelegateBridge.cs#L31-L89](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/GenericDelegateBridge.cs#L31-L89),
  [XN LuaValue.cs#L36-L80](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaValue.cs#L36-L80).
- Expected: C# -> Lua call 490 ns / 952 B -> nearer Lua-CSharp's 333 ns / 48 B [S].

### B10. Debug/no-debug loop specialisation — NG, optional after A5

- `Processing_Loop<TPolicy> where TPolicy : struct` so JIT and IL2CPP fold the debugger/auto-yield checks;
  interrupt polling only on calls, returns and back-edges.
  [LU lvmexecute.cpp#L238-L330](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L238-L330),
  [LU ldebug.cpp#L395-L410](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/ldebug.cpp#L395-L410).
  Only if A5's single budget leaves a measurable gap (estimate 5-10%).

### B11. Lua pattern matcher over string indices — MoonSharp

- Own port of lstrlib.c (MIT) `match`/`find`/`gmatch`/`gsub` over `string` + `int`, replacing KopiLua
  `CharPtr`/`LuaLBuffer`. Do not translate patterns to .NET `Regex` (WattleScript did: `%w` then matches
  `_` and Unicode, and a `Regex` is built per call:
  [WS PatternRegex.cs#L19-L272](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/CoreLib/StringLib/PatternRegex.cs#L19-L272),
  https://github.com/WattleScript/wattlescript/commit/bffd4abfd16c1e7fe0d034d6c31f36ce3e0e1c69).
  SolarSharp did the index port: https://github.com/BraedonWooding/solarsharp/commit/6c83605 (licence
  unclear: ideas only). KopiLua origin: [lobject.cs#L67-L95](https://github.com/NLua/KopiLua/blob/90496c7ea02239528210ab6580ea40df37f21965/KopiLua/src/lobject.cs#L67-L95).
- Expected: string_pattern 23,988 ns -> nearer 6,474 (Lua-CSharp) [S]. Tests: TestMore `304-string.t` +
  differential corpus vs native Lua 5.2. Licence: keep Lua MIT notice in the file header.

### B12. pairs/ipairs fast iteration — after B3

- When the generator is the builtin `next`/`ipairs` callback (identity) and the state is a `Table`, keep
  an integer cursor in the loop opcode.
  [LU lvmexecute.cpp#L2685-L2835](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L2685-L2835),
  https://github.com/luau-lang/luau/pull/2683,
  [MS Processor_InstructionLoop.cs#L476-L524](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L476-L524).

### B13. Lua-style variable names in errors — MoonSharp, optional

- Record a name hint per failing instruction at compile time; "attempt to call a nil value (global 'foo')".
  [LC LuaDebug.cs#L325-L540](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Internal/LuaDebug.cs#L325-L540),
  [LC Exceptions.cs#L250-L310](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Exceptions.cs#L250-L310).
  Error text changes; owner decision.

### B14. Prepared chunks shared across Scripts — NG, after B4

- Per-chunk instruction arrays with relative addresses so one compiled chunk loads into many Scripts.
  [JI Engine.Ast.cs#L23-L58](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Engine.Ast.cs#L23-L58),
  [JI Prepared.cs](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Prepared.cs).
  Matters for one-Script-per-entity Unity hosts.

### B15. Small compiler items — MoonSharp, low priority

- Multi-operand concat ([LU lvmexecute.cpp#L2339-L2353](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L2339-L2353),
  [LU lvmutils.cpp#L459-L530](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmutils.cpp#L459-L530)).
- Constant folding sharing the VM helpers ([LU ConstantFolding.cpp#L803](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/Compiler/src/ConstantFolding.cpp#L803)).
- Capture-free closure sharing (DUPCLOSURE, [LU lvmexecute.cpp#L2891-L2946](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lvmexecute.cpp#L2891-L2946)):
  observable through `rawequal`, Lua 5.2 §3.4.3 allows it; owner decision.
- Cached single-char and small-integer strings ([JI JsString.cs#L94-L136](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Native/JsString.cs#L94-L136)).

### B16. Owned rope for `s = s .. x` — defer

- [JI JsString.cs#L737-L810](https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Native/JsString.cs#L737-L810).
  Every `m_Object as string` read would need flattening; revisit only with a workload that needs it.

### B17. Public struct DynValue — NG 3.0 only, recommended against for now

- WattleScript made `DynValue` a 16-byte public struct (76 files changed; first version got userdata
  equality wrong): https://github.com/WattleScript/wattlescript/commit/b9a4024ae36ccf4fd7eafb9c3a0222d5d215f8ea,
  https://github.com/WattleScript/wattlescript/commit/73a7fc0a72ae5151e741927dc7a7f0e1d3961e11,
  [WS DynValue.cs#L571-L654](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/DataTypes/DynValue.cs#L571-L654).
  Breaks every host (`null` checks, `Assign`, `ReadOnly`, `ReferenceID`). B1 gets the VM benefit without
  it; only consider if boundary materialisation shows up as a top cost after B1/B7.

---

## 6. Group C: rejected, with reasons

| Idea | Source | Reason |
|---|---|---|
| Rebase MoonSharp on Lua-CSharp | Lua-CSharp | §1: loses reflection interop, Hardwire, debugger, sync API, netstandard2.0/net45, public API; unsafe hazard in the hot loop |
| Port Lua-CSharp's `Unsafe.*`/pointer register access, `Unsafe.As` value reads | [LC LuaVirtualMachine.cs#L391-L450](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L391-L450), [LC LuaStack.cs#L141-L175](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaStack.cs#L141-L175), [LC LuaValue.cs#L39-L461](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaValue.cs#L39-L461) | <10% gain [S]; stale refs across `Array.Resize` and type confusion are memory-safety bugs; Unity package-load issue #337 |
| ValueTask-everywhere VM | [LC LuaVirtualMachine.cs#L272-L332](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Runtime/LuaVirtualMachine.cs#L272-L332) | issues #126, #327, #338; no coroutine gain (743 vs 772 ns); net45 has no ValueTask |
| Count hook dispatched as a Lua call | https://github.com/nuskey8/Lua-CSharp/issues/338 | 3x slowdown; keep a budget counter (A5) |
| Never-compacted hash tombstones | [LC LuaValueDictionary.cs#L188-L318](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/Internal/LuaValueDictionary.cs#L102-L457) | unbounded growth for set/queue tables |
| O(n) if-chain member lookup in generated code | [LC LuaObjectGenerator.Emit.cs#L619-L652](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua.SourceGenerator/LuaObjectGenerator.Emit.cs#L600-L700) | use `switch` (B6) |
| Cherry-pick the UnityEnhanced rollup | https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/commit/7c332c20c5524ee0cc208d572de3992bd5f6ea43 | silently reverts ~12 upstream fixes (f59ebdd, 44fda67, e49ef77, f6aa248, 8e0cd47, ...), removes #301 recycling, #267 fuzzy matching, CustomConverters |
| Process-wide static string intern table | [UE DynValue.cs#L159-L167](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L159-L167) | leaks every runtime string, not thread-safe; A17 interns compile-time keys per Script instead |
| Delegate-pair local cells (`DynValueAccessor`) | [UE DynValueAccessor.cs#L36-L40](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/DataTypes/DynValueAccessor.cs#L14-L41) | 4 allocations per slot + delegate calls; A10 uses plain cells only for captured locals |
| Unsealed DynValue; `enum DataType : short`; field reordering | UnityEnhanced | slower type checks; binary break; no effect under auto layout |
| Fixed 4096 stacks, 16-entry LoopTracker | [UE FastStack.cs#L28-L31](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/DataStructs/FastStack.cs#L28-L31), [UE LoopTracker.cs#L15-L17](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/Execution/Scopes/LoopTracker.cs#L15-L17) | raw `IndexOutOfRangeException`; fork master already grows stacks |
| Deleting fuzzy matching or CustomConverters | UnityEnhanced | documented features; A13 memoizes, A8 adds a flag |
| Static unsynchronised module cache sharing `CallbackFunction`s across Scripts; non-yieldable C# `require` | [UE ModuleRegister.cs#L77-L147](https://github.com/SmilingCatEntertainment/moonsharp-unityenhanced/blob/3be1cdb8da5435da30a5780c3991f406066310c6/src/MoonSharp.Interpreter/Modules/ModuleRegister.cs#L77-L147) | thread safety and per-Script isolation; A15 does it safely |
| Pattern -> .NET Regex translation | [WS PatternRegex.cs#L19-L272](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/CoreLib/StringLib/PatternRegex.cs#L19-L272) | changes `%w`/`%d` semantics; Regex per call |
| Descriptor cache not cleared on `RegistrationPolicy` change | [WS TypeDescriptorRegistry.cs#L188-L263](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Interop/UserDataRegistries/TypeDescriptorRegistry.cs#L188-L263) | stale results; A8 versions the cache |
| "Any int key below 16M goes to the array part" | [SS Table.cs#L387-L463](https://github.com/BraedonWooding/solarsharp/blob/e10a939fdc29e70b2dfef6f37faa8d9e9269c01a/SolarSharp.Interpreter/DataTypes/Table.cs#L387-L463) | `t[15e6]=1` allocates 128 MB; use Lua's density rule |
| Table node recycling via finalizers | UniLua | finalizer cost and non-determinism |
| DLR / `LambdaExpression.Compile` / `DefineDynamicAssembly` execution | NeoLua | not AOT/IL2CPP-safe |
| `ArrayPool` for reflection `object[]` | Jint measured NO-GO | slower than allocating |
| Pooling `ScriptExecutionContext`/`CallbackArguments` for user callbacks | xLua report, profile | users can retain them; corruption risk. Internal callbacks only (B7) |
| Hash-ordered `pairs` (option c) | table-order spike | JSON key order changes per process on .NET Core |
| Inlining and loop unrolling | Luau Compiler | debugger and `SourceRef` assume one frame per Lua function |
| Breakpoints by opcode patching | Luau | MoonSharp breakpoints already work per instruction; low value after A5 |
| Unboxed numeric lane in slots (Jint `Binding`) as a separate project | Jint #2499 | subsumed by B1's struct value |

---

## 7. Correctness bugs found by this research (fix with tests, in the PR listed)

| Bug | Evidence | PR |
|---|---|---|
| `a > b` compiled as `not (a <= b)`: wrong for NaN and never calls `__lt(b, a)` | [C] measured, profile §6 | A2 |
| `%` with a negative divisor: `5 % -3 = -4` (Lua `-1`) | [C] code reading of `ExecMod` L961-L982 on fork master | A6 |
| `CollectDeadKeys` on every re-insert after nil + length cache reset -> O(n^2) | [C] code, [S] SolarSharp timings | A7 |
| `Table.Pairs` yields dead keys with nil values | [C] table-order spike §1.2 | A7 (test) / B3 |
| Userdata and threads all hash to 999 | [C] code | A9 |
| Overload cache reuses a slot without clearing `ArgsUserDataType` | [C] code | A9 |
| Value-type receiver mutation lost in optimized modes | [U] needs the test | A9 |

---

## 8. Owner decisions needed

1. `pairs` order for B3: recommend option (b) (array part first, then insertion order).
2. `require` as a C# function (A15 follow-up): changes `debug.getinfo(require).what`.
3. Fast-called builtins absent from tracebacks while running (A18).
4. Capture-free closure sharing (B15): `rawequal` identity.
5. Lua-style error names (B13): error text changes.
6. Whether B-line work goes to fork master directly or to a separate `ng` branch until B1+B2+B3 land
   together (they share the stack/slot redesign).

## 9. Licence summary

| Source | Licence | Use in this plan |
|---|---|---|
| Lua-CSharp | MIT (c) 2025 Yusuke Nakada; `LuaValueDictionary` MIT .NET Foundation; compiler from go-lua MIT | ideas; B5 may copy compiler code with both MIT notices |
| moonsharp-unityenhanced | BSD-3 (MoonSharp + Smiling Cat 2023 notice) | ideas only, credit in commit bodies |
| xLua, xlua-ng | MIT (Tencent) | ideas only |
| Luau | MIT (Roblox, Lua.org) | ideas; any transliterated algorithm keeps the MIT header |
| Jint | BSD-2; `DictionarySlim` MIT .NET Foundation | ideas only |
| WattleScript | BSD-3 | ideas; code adaptable with attribution if ever needed |
| SolarSharp | own code has no explicit grant | ideas only |
| UniLua, KopiLua, AsyncLua | MIT | ideas only |
| NeoLua | Apache-2.0 | ideas only |
| Lua 5.2 (`lstrlib.c`, `ltable.c`) | MIT | preferred source if an algorithm is ported (B3, B11) |

None of group A copies code. Every PR body names the engines whose idea it uses, with the permalinks above.
