# MoonSharp profile: where time and allocations go

Date: 2026-10-28. Profiled code: MoonSharp `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` (upstream master,
https://github.com/moonsharp-devs/moonsharp/tree/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7), in a separate worktree; the
fork's checkout was not modified. The tools are in
[src/DevTools/Research/Profile](../../src/DevTools/Research/Profile/README.md), with the commands. Fork master has since moved to
`58fa6bb` (int64, grow-on-demand stacks); `Processor_InstructionLoop.cs` line numbers are identical there, only
`FastStack.cs` line numbers moved.

Permalink base used below: `PB = https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter`.

Evidence markers: [C] confirmed by measurement or by reading the code, [I] inference, [U] unverified.

## 1. Method

- Machine: 4-core x64 VM, Linux 6.18, .NET 8.0.33 (SDK 8.0.427), workstation non-concurrent GC, TieredPGO on.
  Other agents ran benchmarks and NativeAOT builds on the same machine during this work, so absolute
  timings carry about +-10% noise. **Bytes per iteration are exact and deterministic** and are the primary
  evidence; CPU shares come from sampling and are relative.
- Driver: `Profile/app/Program.cs`, a console app referencing the interpreter csproj. Workload
  sources are copied verbatim from `src/DevTools/Benchmarks/LuaWorkloads.cs` on branch
  `chore/devtools-benchmarks` (same sources as xLua-NG `Test/Native/flag_bench.c`). Added:
  - `clr_interop`: Lua calls `obj:Add(i, 2)` (instance method, 2 int args) and reads `obj.Value` (int
    property), 1M times, `UserData.RegisterType<Calc>()` with the default access mode (LazyOptimized: on
    .NET 8 JIT this compiles expression trees on first use);
  - `clr_interop_reflection`: same with `InteropAccessMode.Reflection`, which is what IL2CPP/AOT gets.
- Modes: `bench` (Stopwatch + `GC.GetAllocatedBytesForCurrentThread` + `GC.CollectionCount` per call),
  `alloc` (in-process `EventListener` on `GCAllocationTick`, type histogram), `loop` (runs one workload for N
  seconds under `dotnet-trace`).
- CPU + allocation stacks: `dotnet-trace 10.0.750501 collect --profile dotnet-sampled-thread-time --providers
  Microsoft-Windows-DotNETRuntime:0x100003801D:5`, 10 s per workload, one at a time. Analysed with a small
  TraceEvent 3.1.21 tool (`Profile/analyzer/Program.cs`): self and inclusive sample shares,
  caller->callee edges, **source line of the first MoonSharp frame** (IL-to-native maps + portable PDB), and
  allocation-tick bytes per (type, allocating MoonSharp frame and line).
- Caveat on line attribution: the JIT inlines most `Exec*` helpers into `Processing_Loop`, so an inlined
  callee is charged to its call-site line (for example `Processor_InstructionLoop.cs:201` is `case
  OpCode.Local` and includes the inlined `AsReadOnly()` clone). `:0` is code without a sequence point (the
  switch jump table, loop back-edge, prolog).
- Every run had a `timeout`; one profile or benchmark process at a time (benchmarks under the shared
  benchmark lock).
- Raw outputs (traces, analyses, `bench-baseline.md`, `alloc-types.txt`, `ab-run1.md`, `ab-run2.md`) and
  the full per-workload tables were not kept; `run-all.sh` and `summarize.py` regenerate them.

## 2. Baseline numbers (fbd0367)

`bench 5` (median of 5 calls), ns per iteration and **exact** bytes per iteration:

| Case | Iter | Median ns | Bytes/iter | Gen0 GCs per 1M iter | DynValue objects/iter (48 B each) |
|---|---:|---:|---:|---:|---:|
| empty_loop | 4M | 58 | 48 | 0.4 | 1 |
| function_call | 1M | 335 | 552 | 4.0 | ~8 (+ CallStackItem, Slice, array) |
| method_call | 1M | 599 | 696 | 5.0 | ~11 |
| table_array_insert | 1M | 842 | 371 | 1.8 (+1 gen2) | ~5 |
| table_array_read | 1M | 365 | 528 | 3.8 | 11 |
| table_hash_set_get | 1M | 718 | 912 | 6.6 | 19 |
| string_format | 500K | 4435 | 8000 | 59 | 13 |
| int_math | 2M | 230 | 288 | 2.1 | 6 |
| fib_recursive (fib(12) = 465 calls) | 20K | 177255 | 301368 (648 per Lua call) | 2210 | ~10 per call |
| coroutine_switch | 500K | 737 | 1084 | 8.0 | ~12 |
| closure_create_call | 1M | 617 | 912 | 6.8 | ~8 |
| clr_interop | 1M | 1006 | 1176 | 8.6 | ~14 |
| clr_interop_reflection | 1M | 1103 | 1176 | 8.6 | ~14 |

[C] `DynValue` is 48 bytes: object header 16 + `m_Object` 8 + `m_Number` 8 + `m_RefID` 4 + `m_HashCode` 4 +
`m_Type` 4 + `m_ReadOnly` 1, padded (`PB/DataTypes/DynValue.cs#L12-L22`). The empty loop allocates exactly one
per iteration.

[C] GC itself is cheap on CoreCLR here: samples in native runtime code (GC + allocation slow path) are 0.3-9%
of CPU. The cost is the allocation fast path, cache traffic and write barriers. On Unity Mono/IL2CPP with the
Boehm collector (non-generational, non-moving) each of these allocations costs far more and collections scan
the whole heap [I], so the allocation numbers matter more for MoonSharp's main target than the CoreCLR
timings suggest.

## 3. Where the bytes go (all workloads)

Bytes per iteration by allocation site (sampled share x exact total). Line numbers are
`Processor_InstructionLoop.cs` unless stated.

| Allocation site | empty_loop | function_call | method_call | table_array_insert | table_array_read | table_hash_set_get | int_math | fib (per fib(12)) | string_format | coroutine_switch | closure_create_call | clr_interop | clr_interop_reflection |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| local read clone (`AsReadOnly`, :201) |  | 98 (18%) | 96 (14%) | 144 (39%) | 192 (36%) | 432 (47%) | 96 (33%) | 56K (19%) | 208 (3%) | 173 (16%) | 96 (10%) | 96 (8%) | 96 (8%) |
| upvalue read clone (`AsReadOnly`, :204) |  | 46 (8%) | 48 (7%) |  | 48 (9%) | 48 (5%) |  | 22K (7%) |  | 43 (4%) | 48 (5%) | 96 (8%) | 95 (8%) |
| fresh local cell after `Clean` (StoreLcl -> AssignLocal `NewNil`, :211/:360) | 48 (100%) | 50 (9%) | 47 (7%) | 48 (13%) | 48 (9%) | 96 (10%) | 48 (17%) |  |  | 49 (4%) | 97 (11%) | 48 (4%) | 48 (4%) |
| ExecArgs cell + `CloneAsWritable` (:645-661) |  | 93 (17%) | 95 (14%) |  |  |  |  | 44K (15%) |  |  |  |  |  |
| argument-count DynValue (:743) |  | 47 (9%) | 48 (7%) |  |  |  |  | 22K (7%) |  |  | 47 (5%) |  |  |
| CallStackItem per call (:713/:744) |  | 96 (17%) | 95 (14%) |  |  |  |  | 45K (15%) | 96 (1%) | 192 (18%) | 96 (10%) | 95 (8%) | 95 (8%) |
| `Slice<DynValue>` args view (:629/:639/:705) |  | 43 (8%) | 40 (6%) |  |  |  |  | 18K (6%) |  | 40 (4%) |  | 40 (3%) | 40 (3%) |
| LocalScope array per call (:587) |  | 30 (5%) | 32 (5%) |  |  |  |  | 14K (5%) |  |  | 24 (3%) |  |  |
| arithmetic result `NewNumber` (:907/:928/:950/:973) |  | 46 (8%) | 48 (7%) |  | 95 (18%) | 95 (10%) | 143 (50%) | 33K (11%) |  | 99 (9%) | 47 (5%) | 96 (8%) | 95 (8%) |
| boolean result `NewBoolean` (:162/:573/:579/:1096/:1121/:1125) |  |  |  |  | 144 (27%) | 145 (16%) |  | 45K (15%) |  |  |  |  |  |
| table/userdata read clone (`AsReadOnly`, :1323/:1349) |  |  | 96 (14%) |  |  | 47 (5%) |  |  |  | 96 (9%) |  | 191 (16%) | 188 (16%) |
| table key DynValue / LinkedListNode / Dictionary growth |  |  | 48 (7%) | 178 (48%) |  | 47 (5%) |  |  |  |  |  |  |  |
| closure creation (LINQ + ClosureContext) |  |  |  |  |  |  |  |  |  |  | 455 (50%) |  |  |
| CLR-call / coroutine plumbing (context, args, yield) |  |  |  |  |  |  |  |  |  | 382 (35%) |  | 143 (12%) | 143 (12%) |
| interop (bound callback, boxing, arg arrays) |  |  |  |  |  |  |  |  |  |  |  | 367 (31%) | 366 (31%) |
| string.format (KopiLua sprintf / Regex) |  |  |  |  |  |  |  |  | >=4984 (>=62%) |  |  |  |  |
| **total bytes/iter** | 48 | 552 | 696 | 371 | 528 | 912 | 288 | 301368 | 8000 | 1084 | 912 | 1176 | 1176 |

Worked example, one `x = f(x)` call in `function_call` (552 B, 11.5 objects) [C]:
`Local x` clone 48 + `Upvalue f` clone 48 + arg-count DynValue 48 + CallStackItem 96 + Slice 40 + LocalScope
`DynValue[1]` 32 + callee cell `NewNil` 48 + `CloneAsWritable` 48 + `a + 1` result 48 + loop `i` cell 48 + local
read of `a` 48 = ~552.

## 4. Where the CPU goes

Top self methods and source lines per workload (the full lists were not kept; `summarize.py` prints them from new traces):

| Workload | Dispatch + fetch (`:0`, `:30`, `:37`) | Other hot lines / methods (self share) |
|---|---:|---|
| empty_loop | 46% | `Clean` -> `ClearBlockData` -> `Array.Clear` (:154) 17% + `Array.Clear`/`ClearWithReferences` self 10%; StoreLcl (:210-211) 15%; Incr (:175) 7%; JFor (:180) 5% |
| function_call | 36% | Internal_ExecCall 7% (:743-744), ExecArgs 7.5%, ExecRet 5.5%, `StelemRef` 2.6%, `Array.Clear` 4% |
| method_call | 30% | `Dictionary<string,...>.FindValue` 13% + `SequenceEqual` 1.9% + `GetNonRandomizedHashCode` 1.7% (constant-key `self.v`, `C.__index`, `o:inc` lookups); ExecIndex :1319 13%, :1323 5.7% |
| table_array_insert | 22% | `Table.PerformTableSet` (Table.cs:164) 22%, `Table.Length` (Table.cs:619, final `#t` walks 1M keys) 18%, `Dictionary<int,...>.TryInsert/Resize/FindValue` 19%, `Buffer._BulkMoveWithWriteBarrier` 6% |
| table_array_read | 37% | Local (:201) 6.6%, Add (:66) 5.9%, StoreLcl (:210) 5.2%, Index (:221, :1319) 9%, LessEq/CNot/Not (:102,:126,:123, ExecNot) 7% |
| table_hash_set_get | 31% | Local (:201) 8.5%, Index/IndexSet (:221,:227,:1239,:1319) 18%, `Dictionary<string>` 6.6% |
| int_math | 38% | Mod (:90) 9.5%, Local (:201) 6.5%, StoreLcl (:210) 5.7%, Add 5.3%, Mul 4.7%, Clean 4.4%, `StelemRef` 2.9% |
| fib_recursive | 38% | ExecArgs 7%, ExecRet 5.4%, Internal_ExecCall 5.2%, `StelemRef` 3.7% |
| string_format | 5% | `RegexInterpreter` 20% + `GroupCollection` 4.5% + `RunSingleMatch` 3.8% (Tools.cs:596 alone 26.5%), `Tools.sprintf` 12%, `str_format` 9.5%, `Memmove` 6.5% |
| coroutine_switch | 26% | Internal_ExecCall CLR path 16% (:713, :727), `coroutine.yield` global+field lookup (:1319) 9.6% (`Dictionary<string>` 4.8% + `SequenceEqual` 2.2%), Coroutine_Resume 5% |
| closure_create_call | 8% | `List<DynValue>.AddRange` 68% (Closure.cs:68 -> ClosureContext.cs:19), LINQ `Select/ToList` 2% |
| clr_interop | 19% | `HashSet<Type>.FindItemIndex` 10% (`NumericConversions.NumericTypes.Contains`), member `Dictionary` lookup 7.7%, `MethodMemberDescriptor.Execute` 7%, conversions ~8%, `DispatchingUserDataDescriptor.Index` (:235) 6.6% |
| clr_interop_reflection | 17% | as above plus `RuntimeMethodInfo.Invoke` 1.7% + `MethodBaseInvoker` 1.9% |

[C] Per instruction the loop does: `m_RootChunk.Code[ip]` (field, field, `List<T>` indexer with two bounds
checks, then a load of a 72-byte `Instruction` class), `m_Debug.DebuggerAttached` (two loads), `++executed`
and the auto-yield test, then the `switch` (`PB/Execution/VM/Processor/Processor_InstructionLoop.cs#L28-L47`).

[C] Every `FastStack<DynValue>` store goes through `CastHelpers.StelemRef` (2-4% CPU): `FastStack<T>` is
shared generic code over `__Canon`, so the JIT cannot drop the array covariance check even though `DynValue`
is sealed (`PB/DataStructs/FastStack.cs#L12-L31`). `RemoveLast(n)` and `ClearBlockData` call `Array.Clear` for
1-3 elements (`PB/DataStructs/FastStack.cs#L65-L78`, `PB/Execution/VM/Processor/Processor_Scope.cs#L7-L18`).

## 5. Root causes in the code

1. **Locals and upvalues are mutable cells, so reads clone.** `Local`/`Upvalue` push
   `scope[index].AsReadOnly()` (`PB/Execution/VM/Processor/Processor_InstructionLoop.cs#L198-L205`), and
   `AsReadOnly` allocates a copy whenever the cell is writable, which it always is
   (`PB/DataTypes/DynValue.cs#L379-L412`). Stores copy into the cell with `Assign` (`#L354-L371`). The clone
   exists because the pushed value could otherwise alias a cell that a later `Assign` mutates. Table reads
   clone the same way (`#L1319-L1324`, `#L1342-L1350`).
2. **Block scopes null their slots, so each iteration allocates new cells.** `Clean` -> `ClearBlockData`
   (`PB/Execution/VM/Processor/Processor_Scope.cs#L7-L18`) clears every block local; the next `StoreLcl` finds
   `null` and allocates `NewNil()` (`#L358-L362`). This is needed only for locals captured by a closure (each
   iteration must get a fresh upvalue), but is done for all. This is the single allocation of `empty_loop`.
3. **Every Lua call allocates 5-7 objects** (`#L582-L590`, `#L609-L664`, `#L741-L757`): a `CallStackItem`
   class (`PB/Execution/VM/CallStackItem.cs#L5-L23`), a `DynValue` holding the argument count, a
   `Slice<DynValue>` (accessed through two interface layers: `Slice` -> `IList<T>` -> `FastStack`), a
   `DynValue[]` LocalScope, and for each parameter a `NewNil` cell plus a `CloneAsWritable` copy (the copy is
   then copied again into the cell by `Assign`).
4. **Every arithmetic and comparison result is a new DynValue** (`#L897-L1145`). Booleans are allocated even
   though `DynValue.True/False` exist. `a > b` compiles to `LessEq, CNot, Not` = three allocations
   (`PB/Execution/VM/ByteCode.cs#L139-L150`, `PB/Tree/Expressions/BinaryOperatorExpression.cs#L260-L330`).
5. **Tables have no array part.** Every entry is a `LinkedListNode<TablePair>` (48 B) plus a `Dictionary`
   entry; the key `DynValue` is re-allocated on every `Set`, even when the key already exists
   (`PB/DataTypes/Table.cs#L162-L228`); `IndexSet` does a `Get` before every `Set` to check for
   `__newindex` even when the table has no metatable (`PB/Execution/VM/Processor/Processor_InstructionLoop.cs#L1235-L1244`);
   `Length` walks keys 1..n through the dictionary when the cache is invalid (`PB/DataTypes/Table.cs#L611-L624`).
6. **Constant-key lookups hash the string every time.** `IndexN` with a literal key goes to
   `Dictionary<string, LinkedListNode<TablePair>>`; .NET does not cache string hashes, and the key instance
   in the instruction differs from the instance stored in the table, so equality falls through to
   `SequenceEqual` (method_call 17% CPU).
7. **`string.format` runs the KopiLua C emulation**: two `char[]` buffers per format item (`MAX_ITEM = 512`
   chars, `PB/CoreLib/StringLib/KopiLua_StrLib.cs#L912-L917`), then `Tools.sprintf` matches a `Regex` over a
   `StringBuilder.ToString()` copy for every specifier (`PB/Interop/LuaStateInterop/Tools.cs#L290`, `#L592-L598`).
8. **Interop rebuilds a bound callback on every `obj:Method` access**: `OverloadedMethodMemberDescriptor.GetValue`
   -> `new CallbackFunction(GetCallback(...))` -> lambda closure + delegate + `CallbackFunction` + `DynValue`,
   then `ExecIndex` clones that DynValue again
   (`PB/Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs#L423-L474`).
   Numeric conversion checks hash `Type` objects in a `HashSet<Type>` on every argument and return value
   (`PB/Interop/Converters/ClrToScriptConversions.cs#L73`, `PB/Interop/Converters/ScriptToClrConversions.cs#L144`,
   `PB/Interop/Converters/NumericConversions.cs#L11-L33`); arguments and results are boxed into an `object[]`
   (`PB/Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs#L177-L219`).
   Each CLR call also allocates a `ScriptExecutionContext`, a `CallbackArguments` and a `Slice`
   (`PB/Execution/VM/Processor/Processor_InstructionLoop.cs#L702-L740`).
9. **Closure creation goes through LINQ and `List<T>.AddRange`** (`#L414-L420`,
   `PB/DataTypes/Closure.cs#L61-L70`, `PB/Execution/Scopes/ClosureContext.cs#L9-L26`).

## 6. Correctness bug found while profiling: `>` and `>=` [C]

MoonSharp compiles `a > b` as `not (a <= b)` and `a >= b` as `not (a < b)`
(`PB/Tree/Expressions/BinaryOperatorExpression.cs#L260-L330`, and the same in the dynamic evaluator
`#L453-L456`). Lua 5.2 defines them as `b < a` and `b <= a` (reference manual 3.4.3 and 2.4 "lt"/"le",
https://www.lua.org/manual/5.2/manual.html#3.4.3). Measured with fbd0367:

| Expression | Lua 5.2 | MoonSharp fbd0367 | Prototype (swap + Less/LessEq) |
|---|---|---|---|
| `(0/0) > 1` | false | **true** | false |
| `(0/0) >= 1` | false | **true** | false |
| `1 > (0/0)` | false | **true** | false |
| `x > y`, `__lt` always true | true (calls `__lt(y, x)`) | **false** | true |

The fix (`Emit_Swap(0,1)` + `Less`/`LessEq`, no `Not`) is also faster, so it belongs with fix 5 below.

## 7. Prototype A/B (scratch only, not a proposed patch)

To check that the shares translate into time, five cheap changes were applied cumulatively in a second
scratch worktree (patch in [Profile/proto-ABCDE.patch](../../src/DevTools/Research/Profile/proto-ABCDE.patch), 428 lines, on `fbd0367`):

- **A** cached booleans (`DynValue.FromBoolean` returns the read-only `True`/`False`) + `>`/`>=` as swapped `<`/`<=`;
- **C** table: reuse the existing key on update, skip the `Get` before `Set` when the table has no metatable;
- **D** `Instruction[]` snapshot in `Processing_Loop` (refreshed when `ip` passes its end), a non-generic
  sealed `DynValueStack`, loop clears for <= 8 slots;
- **E** `ExecArgs` reads arguments straight off the stack (no `Slice`) and stores the fresh copy as the cell;
  closure creation without LINQ; `IsNumericType` reference checks instead of `HashSet<Type>`;
- **J** (experiment only, **unsafe**: aliasing) `Local`/`Upvalue` push the cell without cloning, to measure
  what value semantics for locals would buy.

The MoonSharp test suite (`DotNetCoreTestRunner --unit`) passes on A..E: 669/669 OK, 2 skipped, same as
fbd0367. J was not tested; it is a measurement, not a candidate.

Results, `bench`, runs 1 and 2 pooled (run 1: base A C D E J base E with 5 calls each; run 2: base E J base E J
with 7 calls each). Cells are median ns (min ns) / exact bytes per iteration; percentages compare minimums,
which are the least disturbed by the other jobs on the machine.

| Case | base ns (min) / B | A ns (min) / B | C ns (min) / B | D ns (min) / B | E ns (min) / B | J ns (min) / B | E vs base (min ns) | E vs base (B) | J vs E (min ns) | J vs E (B) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| empty_loop | 54 (51) / 48 | 56 (55) / 48 | 56 (51) / 48 | 49 (46) / 48 | 54 (46) / 48 | 48 (46) / 48 | -9% | +0% | +1% | +0% |
| function_call | 360 (315) / 552 | 389 (361) / 552 | 321 (315) / 552 | 328 (314) / 552 | 332 (278) / 464 | 249 (233) / 320 | -12% | -16% | -16% | -31% |
| method_call | 608 (554) / 696 | 584 (564) / 696 | 572 (529) / 648 | 637 (524) / 648 | 534 (486) / 560 | 470 (444) / 416 | -12% | -20% | -9% | -26% |
| table_array_insert | 830 (545) / 371 | 931 (644) / 371 | 870 (556) / 371 | 802 (530) / 371 | 856 (621) / 371 | 410 (300) / 227 | +14% | +0% | -52% | -39% |
| table_array_read | 383 (350) / 528 | 400 (360) / 384 | 403 (356) / 384 | 342 (337) / 384 | 341 (304) / 384 | 269 (251) / 192 | -13% | -27% | -18% | -50% |
| table_hash_set_get | 684 (624) / 912 | 657 (632) / 768 | 609 (564) / 720 | 591 (567) / 720 | 595 (550) / 720 | 445 (410) / 288 | -12% | -21% | -26% | -60% |
| int_math | 247 (227) / 288 | 265 (256) / 288 | 278 (232) / 288 | 248 (227) / 288 | 241 (226) / 288 | 216 (202) / 192 | -0% | +0% | -11% | -33% |
| fib_recursive | 180.2us (166.8us) / 301.4us | 187.7us (182.0us) / 256.7us | 171.0us (163.8us) / 256.7us | 162.8us (160.3us) / 256.7us | 150.4us (136.5us) / 215.8us | 121.4us (112.9us) / 137.7us | -18% | -28% | -17% | -36% |
| string_format | 4015 (3667) / 8000 | 3846 (3664) / 7856 | 3986 (3690) / 7808 | 4057 (3722) / 7808 | 4066 (3728) / 7808 | 3900 (3700) / 7520 | +2% | -2% | -1% | -4% |
| coroutine_switch | 723 (670) / 1084 | 710 (687) / 1084 | 699 (679) / 1084 | 761 (716) / 1084 | 721 (668) / 1060 | 659 (605) / 820 | -0% | -2% | -9% | -23% |
| closure_create_call | 618 (562) / 912 | 670 (614) / 912 | 655 (610) / 912 | 614 (608) / 912 | 415 (375) / 704 | 353 (338) / 560 | -33% | -23% | -10% | -20% |
| clr_interop | 933 (870) / 1176 | 948 (891) / 1176 | 903 (859) / 1176 | 898 (845) / 1176 | 879 (791) / 1176 | 785 (746) / 984 | -9% | +0% | -6% | -16% |
| clr_interop_reflection | 965 (893) / 1176 | 1000 (941) / 1176 | 986 (890) / 1176 | 984 (915) / 1176 | 909 (843) / 1176 | 864 (776) / 984 | -6% | +0% | -8% | -16% |

Reading it:

- Bytes are exact, so the allocation savings are certain: A..E remove 16% of the bytes in `function_call`,
  20% `method_call`, 27% `table_array_read`, 21% `table_hash_set_get`, 28% `fib_recursive`, 23%
  `closure_create_call`.
- Time: -33% `closure_create_call`, -18% `fib_recursive`, -12..13% `function_call`, `method_call`,
  `table_array_read`, `table_hash_set_get`, -6..9% interop, flat for `int_math`, `string_format` and
  `coroutine_switch` (none of A..E touches their hot spots).
- `table_array_insert` timing is dominated by gen1/gen2 collections over the growing 1M-entry table and swings
  +-15% between runs; only its bytes are meaningful here.
- J shows what value semantics for locals would add on top of E: another -31% to -60% bytes and -9% to -26%
  time on VM-bound workloads, -52% on `table_array_insert`.


## 8. Ranked fixes

Ranked by measured share (bytes, and CPU where it was measured) across the 13 workloads. "Share" columns
come from sections 3 and 4; "measured" means the prototype in section 7.

Effort: S = hours, M = days, L = weeks. "Bytes" = share of allocated bytes per iteration from section 3;
"CPU" = sampled self share from section 4.

### 1. Locals hold immutable values; only captured locals get a cell

- **Evidence.** Local/upvalue read clones (`:201`, `:204`), fresh cells after `Clean` (`:211`) and ExecArgs
  copies together are 100% of the bytes in `empty_loop`, 62% in `table_hash_set_get`, 54%
  `table_array_read`, 52% `function_call` and `table_array_insert`, 50% `int_math`, 42% `method_call`, 41%
  `fib_recursive`, 26% `closure_create_call`, 24% `coroutine_switch`, 20% `clr_interop`.
- **Measured.** Experiment J (read clones removed, unsafe) on top of E: bytes -31% `function_call`, -60%
  `table_hash_set_get`, -50% `table_array_read`, -39% `table_array_insert`, -36% `fib_recursive`, -33%
  `int_math`; min time -16% `function_call`, -17% `fib_recursive`, -26% `table_hash_set_get`, -18% `table_array_read`,
  -11% `int_math`, -52% `table_array_insert` (fewer gen1/gen2 collections while the 1M-entry table is alive).
- **Mechanism.** The clone exists only because slots hold mutable cells that `Assign` overwrites
  (`PB/Execution/VM/Processor/Processor_InstructionLoop.cs#L198-L205`, `#L354-L371`). Lua itself keeps plain
  values in registers; a closure points at the register through an open upvalue, which is closed (copied out)
  only when the variable leaves scope. For MoonSharp:
  the compiler marks a local as captured when a nested function resolves it as an upvalue; uncaptured slots
  hold immutable values (`Local` pushes the reference, `StoreLcl` replaces the reference, `Clean` need not
  allocate a new cell), captured slots keep today's cell. Values the VM creates are born read-only, so
  `AsReadOnly()` also stops cloning on table reads (`:1323`, `:1349`).
- **MoonSharp mapping.** `Execution/Scopes/BuildTimeScope.cs`, `BuildTimeScopeBlock.cs`, `BuildTimeScopeFrame.cs`
  and `SymbolRef.cs` (captured flag), `Execution/VM/ByteCode.cs` (`Emit_Load`/`Emit_Store` choose plain or
  cell opcodes), `Processor_InstructionLoop.cs` (`Local`, `Upvalue`, `StoreLcl`, `StoreUpv`, `ExecArgs`,
  `ExecClosure`, `GetUpvalueSymbol`), `Processor_Scope.cs` (`ClearBlockData`, `GetGenericSymbol`,
  `AssignGenericSymbol`), `Processor_Debugger.cs` and `CoreLib/DebugModule.cs` (locals/upvalue views).
- **Effort / risk.** L. Aliasing bugs if any path still mutates a shared DynValue; hosts calling
  `DynValue.Assign` on values they read from scripts would now get "Assigning on r-value". Needs a mutation
  audit (all `Assign`/`AssignNumber` callers at fbd0367: `Processor_InstructionLoop.cs:362, 384, 558`, `Processor_Scope.cs:75, 86`, `CoreLib/DebugModule.cs:166`) and aliasing regression tests (closures in
  loops, `debug.setlocal`/`setupvalue`, coroutines).
- **Compatibility.** No public signature change; `DynValue.ReadOnly` becomes true for more VM-produced values.
  IL2CPP/AOT, netstandard2.0, Unity 2021.3 unaffected; the debugger keeps working through the same scopes.

### 2. Stop allocating per Lua call (frame, argument count, args view, scope)

- **Evidence.** `CallStackItem` (96 B), argument-count `DynValue`, `Slice<DynValue>` and the `DynValue[]`
  LocalScope are 39% of bytes in `function_call`, 33% `fib_recursive`, 32% `method_call`, 22%
  `coroutine_switch`, 18% `closure_create_call`; `Internal_ExecCall` + `ExecArgs` + `ExecRet` are 15-20% of
  CPU in the call-heavy workloads.
- **Measured.** Prototype E removed the `Slice` and the double copy in `ExecArgs` only: bytes -16%
  `function_call`, -16% `fib_recursive` (256.7K -> 215.8K), -14% `method_call`; min time -18% `fib_recursive`
  and -12% `function_call` (base -> E, includes A-D).
- **Mechanism.** Keep `CallStackItem` objects in the execution stack and reset them on push (or make the
  frame a struct in a `CallStackItem[]` used by `ref`), store the argument count in the frame instead of
  pushing `DynValue.NewNumber(argsCount)` (`#L743`), read arguments directly from the value stack, and take
  the LocalScope from a per-processor `DynValue[]` window (Lua-style register window) or a size-bucketed pool.
- **MoonSharp mapping.** `Execution/VM/CallStackItem.cs`, `DataStructs/FastStack.cs`,
  `Processor_InstructionLoop.cs` (`Internal_ExecCall`, `ExecArgs`, `ExecBeginFn`, `ExecRet`, `PerformTCO`,
  `PopToBasePointer`), `Processor.cs` (`PushClrToScriptStackFrame`), `Processor_Debugger.cs` (call stack
  snapshots must copy, not keep, frames).
- **Effort / risk.** M. Frames are read by the debugger, error unwinding and coroutines; reuse is safe only
  if nothing keeps a frame after it is popped. A pooled LocalScope must not be captured (closures capture
  cells, not the array, so this holds today [C], `#L422-L430`).
- **Compatibility.** Internal only.

### 3. Cheaper dispatch loop and value stack

- **Evidence.** 30-46% of CPU is on `Processor_InstructionLoop.cs:0`, `:30`, `:37` (fetch through
  `List<Instruction>`, debugger check, instruction counter, switch). `CastHelpers.StelemRef` 2-4%;
  `Array.Clear`/`ClearWithReferences` 2-4% (10% in `empty_loop`).
- **Measured.** D (`Instruction[]` snapshot, non-generic sealed `DynValueStack`, loop clears for <= 8 slots)
  after C: `table_array_read` 403 -> 342 ns, `fib_recursive` 171 -> 163 us, `empty_loop` 56 -> 49 ns
  (single run, noisy).
- **Mechanism.** Read an `Instruction[]` once per `Processing_Loop` entry and refresh it only when `ip` runs
  past its end (code is append-only); move `DebuggerAttached` and auto-yield checks behind one flag that is
  set when either is enabled; specialise the value stack for `DynValue` so stores skip the covariance check;
  clear 1-8 slots with a loop. Next steps: a compact struct instruction array, fused compare-and-jump
  (`Less`+`Jf`), `Local`/`Local`/`Add` super-instructions.
- **MoonSharp mapping.** `Execution/VM/ByteCode.cs` (array snapshot), `Processor_InstructionLoop.cs`
  (`Processing_Loop`), `Processor.cs` (stack fields), `DataStructs/FastStack.cs` (+ a `DynValueStack`),
  `Processor_Scope.cs` (`ClearBlockData`), `Execution/VM/OpCode.cs` and `Tree/Expressions/*` for fused ops.
- **Effort / risk.** S for the snapshot, stack and clears; M for fused ops (binary dump format version bump,
  debugger source refs). On fork master the stacks grow on demand (`58fa6bb`), so the specialised stack must
  keep `Grow`.
- **Compatibility.** Internal. Fused opcodes change the dump format (`Processor_BinaryDump.cs`).

### 4. Do not box every number result

- **Evidence.** `NewNumber` for arithmetic results is 50% of bytes in `int_math`, 18% `table_array_read`,
  11% `fib_recursive`, 10% `table_hash_set_get`, 9% `coroutine_switch`, 5-8% elsewhere.
- **Mechanism.** Full fix: an internal struct value (type tag + double + object) on the VM stack and in
  uncaptured slots, with `DynValue` kept as the public boxed form created at API boundaries. Interim, cheap:
  a cache of read-only `DynValue`s for small integers (for example -256..1024) used by `ExecAdd`/`ExecSub`/
  `ExecIncr` results and table keys; it helps counters and indices, not `int_math`. Independent: drop
  `m_RefID` (a static counter write on every allocation, 4 bytes) by assigning reference ids lazily, which
  shrinks `DynValue` from 48 to 40 bytes [I: `ReferenceID` callers must be checked].
- **MoonSharp mapping.** `DataTypes/DynValue.cs`, `Processor_InstructionLoop.cs` (`Exec*` arithmetic,
  `ExecIncr`, `ExecToNum`), `DataStructs/FastStack.cs`, `DataTypes/CallbackArguments.cs` (would wrap the
  struct stack), `Interop/Converters/*`.
- **Effort / risk.** L (struct value) / S (small-int cache, after fix 1 makes values immutable). The struct
  path touches every VM helper and `CallbackArguments`.
- **Compatibility.** Struct path: internal if `DynValue` stays the public type; IL2CPP fine. Small-int cache:
  values become read-only, which needs fix 1 first.

### 5. Booleans from singletons; `>` and `>=` as swapped `<` and `<=`

- **Evidence.** Boolean results are 27% of bytes in `table_array_read`, 16% `table_hash_set_get`, 15%
  `fib_recursive`. `a > b` costs three allocations and three dispatches (`LessEq`, `CNot`, `Not`).
- **Measured.** A: bytes -27% `table_array_read` (528 -> 384), -16% `table_hash_set_get`, -15%
  `fib_recursive`. Also fixes the NaN and `__lt` results in section 6.
- **Mechanism.** Internal `DynValue.FromBoolean(bool)` returning the read-only `True`/`False`; in
  `BinaryOperatorExpression.Compile`, emit `Swap(0,1)` + `Less`/`LessEq` for `>`/`>=`; fix the same in
  `Eval`.
- **MoonSharp mapping.** `DataTypes/DynValue.cs`, `Processor_InstructionLoop.cs` (`ExecLess`, `ExecLessEq`,
  `ExecEq`, `ExecNot`, `ExecCNot`, `ToBool`), `Tree/Expressions/BinaryOperatorExpression.cs`,
  `Execution/VM/ByteCode.cs`.
- **Effort / risk.** S. Read-only booleans can never be `Assign`ed (that would throw, not corrupt). The
  comparison change is a behaviour fix to document.
- **Compatibility.** Public `NewBoolean` unchanged (still returns a writable value).

### 6. Table array part, lazy keys, no pre-`Get`

- **Evidence.** `table_array_insert`: 48% of bytes in `LinkedListNode`, `Dictionary` entries and keys;
  CPU 22% `PerformTableSet` (Table.cs:164), 18% `Table.Length` (Table.cs:619, the final `#t` walks 1M keys),
  19% `Dictionary<int,...>` insert/resize. Key `DynValue` re-allocated on every update: 5-7% of bytes in
  `method_call` and `table_hash_set_get`.
- **Measured.** C (reuse key on update, skip `Get` when no metatable): bytes -7% `method_call`, -6%
  `table_hash_set_get`; min time `table_hash_set_get` 632 -> 564 ns.
- **Mechanism.** A dense `DynValue[]` part for keys 1..n with Lua-like sizing, `Length` from the array part
  plus a border search, hash part only for the rest; create key `DynValue`s only on insert; skip the
  `__newindex` probe when `MetaTable == null`.
- **MoonSharp mapping.** `DataTypes/Table.cs`, `DataStructs/LinkedListIndex.cs`, `DataTypes/TablePair.cs`,
  `CoreLib/TableModule.cs` (`insert`, `remove`, `sort`, `unpack`), `CoreLib/BasicModule.cs` (`next`, `ipairs`),
  `Processor_InstructionLoop.cs` (`ExecIndex`, `ExecIndexSet`, `ExecTblInitI`).
- **Effort / risk.** M. `pairs` order changes for array keys (MoonSharp iterates in insertion order today;
  the fork's `spike/table-order` branch is measuring exactly this), `next` during modification, nil holes and
  `#` border cases. Lazy keys and the no-metatable shortcut are S and order-neutral.
- **Compatibility.** Iteration order is observable; decide with the owner.

### 7. `string.format` without the C emulation

- **Evidence.** In `string_format`, ~75% of CPU is in `Regex` (20% interpreter, Tools.cs:596 alone 26.5%),
  `Tools.sprintf` 12%, `str_format` 9.5%, `Memmove` 6.5%; >= 62% of the 8000 B/iter is `char[]` (two buffers
  per specifier, `MAX_FORMAT` and `MAX_ITEM` = 512 chars), `Regex` `Match`/`Group` objects, `CharPtr` and `StringBuilder`.
- **Mechanism.** A managed formatter that parses the format string once (cache parsed specs per format
  string), handles `%d %i %u %c %x %X %o %e %E %f %g %G %q %s %%` with flags, width and precision directly into
  a reused `StringBuilder` using the invariant culture, and falls back to KopiLua only for anything it does not
  handle.
- **MoonSharp mapping.** `CoreLib/StringModule.cs` (`format`), `CoreLib/StringLib/KopiLua_StrLib.cs`
  (`str_format` stays as fallback), `Interop/LuaStateInterop/Tools.cs`.
- **Effort / risk.** M. printf corner cases (`%#x`, `% d`, `%+.3g`, `%5.2s`, huge widths, `%d` on
  non-integers) need differential tests against the current implementation and against native Lua 5.2.
- **Compatibility.** Output must stay byte-identical for supported specifiers.

### 8. Interop: reuse method bindings, no `HashSet<Type>`, less boxing

- **Evidence.** `clr_interop` (1176 B/iter): a new lambda closure, `Func<>`, `CallbackFunction` and
  `DynValue` per `obj:Add` access (17%), boxed `Int32` x4 (8%), `object[]` args (3%),
  `ScriptExecutionContext` + `CallbackArguments` + `Slice` per CLR call (12%), userdata read clones (16%).
  CPU: `HashSet<Type>.FindItemIndex` 10%, member `Dictionary` 7.7%, conversions ~8%.
- **Measured.** E replaced `NumericTypes.Contains` with reference compares: min time -9% `clr_interop`,
  -6% `clr_interop_reflection` (base -> E, which also includes A-D).
- **Mechanism.** (a) Bind `obj:Method(...)` at the call: when `IndexN` on a userdata finds a method
  descriptor and the next instruction is `ThisCall`, invoke the descriptor with `obj` = `args[0]` and no bound
  delegate; otherwise cache one bound `DynValue` per (object, member) in a small per-descriptor cache.
  (b) `IsNumericType` / `Type.GetTypeCode` switches instead of hashing `Type`. (c) Fast converters for
  `int`/`double`/`bool`/`string` parameters and returns. (d) Avoid the read-only clone of userdata index
  results (fix 1).
- **MoonSharp mapping.** `Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs`,
  `MethodMemberDescriptor.cs`, `PropertyMemberDescriptor.cs`, `Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs`,
  `Interop/Converters/ClrToScriptConversions.cs`, `ScriptToClrConversions.cs`, `NumericConversions.cs`,
  `Processor_InstructionLoop.cs` (`ExecIndex`, `Internal_ExecCall`).
- **Effort / risk.** M. `obj.Method` read as a value must still return a bound callable; overload-cache and
  extension-method semantics must not change.
- **Compatibility.** Internal; works the same under `InteropAccessMode.Reflection` (IL2CPP) and Hardwire.

### 9. Constant-key field access: interned keys and inline caches

- **Evidence.** `Dictionary<string,...>` lookup + string hashing + `SequenceEqual`: 17% of CPU in
  `method_call`, 11% `clr_interop`, 8% `coroutine_switch`, 7% `table_hash_set_get`.
- **Mechanism.** Intern identifier and string-literal keys per `Script` at compile time so equality is a
  reference hit; then a per-instruction inline cache on `IndexN`/`IndexSetN` (last table, last
  `LinkedListNode<TablePair>`, valid while `node.List != null` and the table matches).
- **MoonSharp mapping.** `Tree/Lexer/Lexer.cs` (intern), `Execution/VM/ByteCode.cs` and `Instruction.cs`
  (cache slots), `Processor_InstructionLoop.cs` (`ExecIndex`, `ExecIndexSet`), `DataTypes/Table.cs`.
- **Effort / risk.** S (interning) / M (inline caches; invalidation on remove, clear and metatable change).
- **Compatibility.** Internal.

### 10. Closure creation without LINQ

- **Evidence.** `closure_create_call`: 68% of CPU in `List<DynValue>.AddRange` (Closure.cs:68 ->
  ClosureContext.cs:19), 50% of bytes in LINQ iterators, `Func<>`, `string[]`, `List` and `ClosureContext`.
- **Measured.** E: bytes -23% (912 -> 704), min time -33% (562 -> 375 ns).
- **Mechanism.** Build the upvalue array with a loop, fill `ClosureContext` without `AddRange(IEnumerable)`,
  and compute the symbol-name array once per function prototype instead of per closure.
- **MoonSharp mapping.** `Processor_InstructionLoop.cs` (`ExecClosure`), `DataTypes/Closure.cs`,
  `Execution/Scopes/ClosureContext.cs`, `Script.cs` (other `new Closure` callers).
- **Effort / risk.** S / low.
- **Compatibility.** Internal.

### Also seen, smaller

- Coroutine resume/yield: `ScriptExecutionContext`, `CallbackArguments`, `YieldRequest` and two
  `GetArray()` copies per switch = 35% of bytes in `coroutine_switch` (`CoreLib/CoroutineModule.cs#L33-L40`,
  `#L84-L88`, `Processor_Coroutines.cs#L55-L60`).
- `PlatformAutoDetector.AutoDetectPlatformFlags` scans all loaded assemblies' types on the first `Script`
  (0.8% of a 10 s trace; startup only).


## 9. Unsafe inventory

None. MoonSharp has no `unsafe`, `stackalloc`, `fixed`, `Unsafe.*` or `MemoryMarshal`; the three grep hits are
comments (`Interop/InteropRegistrationPolicy.cs:32`, `Interop/PropertyTableAssigner.cs:50`,
`Interop/ProxyObjects/IProxyFactory.cs:6`). None of the fixes below needs unsafe code; the prototypes use only
safe C# that compiles for netstandard2.0.

## 10. Licence

MoonSharp is BSD-3-Clause (`LICENSE`, "Copyright (c) 2014-2016, Marco Mastropaolo"; parts of the string
library come from KopiLua, MIT). Everything here is MoonSharp-native work; no outside code was copied.

## 11. Notes for whoever repeats this

- The shared benchmark lock was held for more than 10 minutes by an orphaned MSBuild node
  (`dotnet ... MSBuild.dll /nodemode:1 /nodeReuse:true`, parent PID 1): a `flock bench.lock dotnet build ...`
  let the reusable build node inherit the locked file descriptor, so the lock outlived the build and every
  queued benchmark waited. `flock -o` (close the descriptor before running the command) or
  `dotnet build --disable-build-servers` / `-nodeReuse:false` under a lock avoids it. Run 2 above was started
  without the lock for that reason.
- `dotnet-trace`'s profile union (`dotnet-common` + `gc-verbose`) kept level 4 and dropped the verbose
  `GCAllocationTick` events; pass `--providers Microsoft-Windows-DotNETRuntime:0x100003801D:5` explicitly.
- Tools and scripts, in [src/DevTools/Research/Profile](../../src/DevTools/Research/Profile/README.md):
  `app` (driver), `analyzer` (TraceEvent report), `run-all.sh` (trace + analyse each workload),
  `categorize.py`, `summarize.py`, `abtable.py`, `ab.sh`. `build-proto.sh` is replaced by a `dotnet build`
  with `-p:MoonSharpProject`; `dotnet-trace` 10.0.750501 installs with `dotnet tool install`.

## Appendix A. Per-workload hot methods, lines and allocation sites

Line numbers refer to fbd0367. `MS.` = `MoonSharp.Interpreter.`.

#### empty_loop

_CPU: thread 28533, 6666 samples in MoonSharp stacks (9.0% External/native-runtime)_

Top self (exclusive) methods:

- 80.4% `MS.Execution.VM.Processor.Processing_Loop`
- 8.5% `[runtime/native] under MS.Execution.VM.Processor.Processing_Loop`
- 7.2% `System.Array.Clear`
- 3.1% `System.SpanHelpers.ClearWithReferences`
- 0.3% `[runtime/native] under System.Array.Clear`
- 0.2% `MS.Execution.VM.Processor.ExecIncr`
- 0.1% `[runtime/native] under System.SpanHelpers.ClearWithReferences`
- 0.1% `MS.DynValue.AssignNumber`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 29.9% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 17.0% `Processor_InstructionLoop.cs:154` (Execution.VM.Processor.Processing_Loop)
- 11.5% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 8.7% `Processor_InstructionLoop.cs:210` (Execution.VM.Processor.Processing_Loop)
- 7.4% `Processor_InstructionLoop.cs:175` (Execution.VM.Processor.Processing_Loop)
- 6.0% `Processor_InstructionLoop.cs:211` (Execution.VM.Processor.Processing_Loop)
- 5.3% `Processor_InstructionLoop.cs:180` (Execution.VM.Processor.Processing_Loop)
- 4.4% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)
- 2.3% `Processor_InstructionLoop.cs:153` (Execution.VM.Processor.Processing_Loop)
- 1.5% `Processor_InstructionLoop.cs:151` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 83.8% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 16.2% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:86)`

#### function_call

_CPU: thread 23858, 6120 samples in MoonSharp stacks (7.6% External/native-runtime)_

Top self (exclusive) methods:

- 63.9% `MS.Execution.VM.Processor.Processing_Loop`
- 7.5% `MS.Execution.VM.Processor.ExecArgs`
- 6.4% `MS.Execution.VM.Processor.Internal_ExecCall`
- 5.0% `[runtime/native] under MS.Execution.VM.Processor.Processing_Loop`
- 4.8% `MS.Execution.VM.Processor.ExecRet`
- 3.3% `System.Array.Clear`
- 3.2% `System.Runtime.CompilerServices.CastHelpers.StelemRef`
- 1.3% `System.SpanHelpers.ClearWithReferences`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 17.0% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 14.8% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 5.7% `Processor_InstructionLoop.cs:744` (Execution.VM.Processor.Internal_ExecCall)
- 4.0% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)
- 3.9% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)
- 3.8% `Processor_InstructionLoop.cs:66` (Execution.VM.Processor.Processing_Loop)
- 3.7% `Processor_InstructionLoop.cs:210` (Execution.VM.Processor.Processing_Loop)
- 3.7% `Processor_InstructionLoop.cs:154` (Execution.VM.Processor.Processing_Loop)
- 2.6% `Processor_InstructionLoop.cs:204` (Execution.VM.Processor.Processing_Loop)
- 2.4% `Processor_InstructionLoop.cs:822` (Execution.VM.Processor.ExecRet)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 17.8% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 17.4% `Execution.VM.CallStackItem  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:744) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 9.0% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 8.6% `DynValue  @ Execution.VM.Processor.ExecArgs (Processor_InstructionLoop.cs:657) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:166)`
- 8.6% `DynValue  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:743) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 8.4% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 8.3% `DynValue  @ Execution.VM.Processor.ExecAdd (Processor_InstructionLoop.cs:907) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:66)`
- 8.3% `DynValue  @ Execution.VM.Processor.ExecArgs (Processor_InstructionLoop.cs:650) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:166)`
- 7.6% `DataStructs.Slice`1[DynValue]  @ Execution.VM.Processor.ExecArgs (Processor_InstructionLoop.cs:639) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:166)`
- 5.4% `DynValue[]  @ Execution.VM.Processor.ExecBeginFn (Processor_InstructionLoop.cs:587) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:160)`

#### method_call

_CPU: thread 24026, 6934 samples in MoonSharp stacks (2.9% External/native-runtime)_

Top self (exclusive) methods:

- 53.8% `MS.Execution.VM.Processor.Processing_Loop`
- 13.0% `System.Collections.Generic.Dictionary`2[System.__Canon,System.__Canon].FindValue`
- 8.4% `MS.Execution.VM.Processor.ExecIndex`
- 5.1% `MS.Execution.VM.Processor.ExecArgs`
- 3.4% `MS.Execution.VM.Processor.Internal_ExecCall`
- 2.6% `System.Runtime.CompilerServices.CastHelpers.StelemRef`
- 2.1% `MS.Execution.VM.Processor.ExecIndexSet`
- 1.9% `System.SpanHelpers.SequenceEqual`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 13.2% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 13.0% `Processor_InstructionLoop.cs:1319` (Execution.VM.Processor.ExecIndex)
- 12.4% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 5.7% `Processor_InstructionLoop.cs:1323` (Execution.VM.Processor.ExecIndex)
- 3.6% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)
- 3.4% `Processor_InstructionLoop.cs:1239` (Execution.VM.Processor.ExecIndexSet)
- 3.0% `Table.cs:164` (Table.PerformTableSet)
- 3.0% `Processor_InstructionLoop.cs:227` (Execution.VM.Processor.Processing_Loop)
- 2.7% `Processor_InstructionLoop.cs:744` (Execution.VM.Processor.Internal_ExecCall)
- 2.5% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 13.8% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 13.8% `DynValue  @ Execution.VM.Processor.ExecIndex (Processor_InstructionLoop.cs:1323) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:221)`
- 13.7% `Execution.VM.CallStackItem  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:744) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 6.9% `DynValue  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:743) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 6.9% `DynValue  @ Execution.VM.Processor.ExecIndexSet (Processor_InstructionLoop.cs:1241) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:227)`
- 6.9% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 6.9% `DynValue  @ Execution.VM.Processor.ExecAdd (Processor_InstructionLoop.cs:907) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:66)`
- 6.9% `DynValue  @ Execution.VM.Processor.ExecArgs (Processor_InstructionLoop.cs:652) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:166)`
- 6.8% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 6.8% `DynValue  @ Execution.VM.Processor.ExecArgs (Processor_InstructionLoop.cs:645) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:166)`

#### table_array_insert

_CPU: thread 24122, 3049 samples in MoonSharp stacks (0.7% External/native-runtime)_

Top self (exclusive) methods:

- 46.9% `MS.Execution.VM.Processor.Processing_Loop`
- 16.7% `MS.Table.get_Length`
- 11.3% `System.Collections.Generic.Dictionary`2[System.Int32,System.__Canon].TryInsert`
- 6.2% `System.Buffer._BulkMoveWithWriteBarrier`
- 4.7% `MS.Execution.VM.Processor.ExecIndexSet`
- 4.1% `System.Collections.Generic.Dictionary`2[System.Int32,System.__Canon].FindValue`
- 3.8% `System.Collections.Generic.Dictionary`2[System.Int32,System.__Canon].Resize`
- 2.1% `System.Array.Clear`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 21.9% `Table.cs:164` (Table.PerformTableSet)
- 18.1% `Table.cs:619` (Table.get_Length)
- 10.0% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 9.4% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 5.5% `Processor_InstructionLoop.cs:227` (Execution.VM.Processor.Processing_Loop)
- 5.4% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)
- 3.2% `Processor_InstructionLoop.cs:1252` (Execution.VM.Processor.ExecIndexSet)
- 3.1% `Processor_InstructionLoop.cs:154` (Execution.VM.Processor.Processing_Loop)
- 3.1% `Processor_InstructionLoop.cs:1241` (Execution.VM.Processor.ExecIndexSet)
- 2.6% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 38.7% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 17.0% `Entry[System.Int32,System.Collections.Generic.LinkedListNode`1[TablePair]][]  @ Table.PerformTableSet (Table.cs:164) <- Execution.VM.Processor.ExecIndexSet (Processor_InstructionLoop.cs:1252)`
- 14.7% `System.Collections.Generic.LinkedListNode`1[TablePair]  @ Table.PerformTableSet (Table.cs:164) <- Execution.VM.Processor.ExecIndexSet (Processor_InstructionLoop.cs:1252)`
- 12.9% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 12.5% `DynValue  @ Execution.VM.Processor.ExecIndexSet (Processor_InstructionLoop.cs:1252) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:227)`
- 2.8% `System.Int32[]  @ Table.PerformTableSet (Table.cs:164) <- Execution.VM.Processor.ExecIndexSet (Processor_InstructionLoop.cs:1252)`

#### table_array_read

_CPU: thread 24351, 6387 samples in MoonSharp stacks (0.3% External/native-runtime)_

Top self (exclusive) methods:

- 86.9% `MS.Execution.VM.Processor.Processing_Loop`
- 3.3% `System.Collections.Generic.Dictionary`2[System.Int32,System.__Canon].FindValue`
- 3.1% `System.Runtime.CompilerServices.CastHelpers.StelemRef`
- 1.5% `MS.Execution.VM.Processor.ExecNot`
- 1.1% `System.Array.Clear`
- 1.1% `MS.Execution.VM.Processor.ExecIndex`
- 0.6% `MS.Execution.VM.Processor.ExecAdd`
- 0.6% `MS.Execution.VM.Processor.ExecLessEq`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 19.0% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 13.5% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 6.6% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)
- 5.9% `Processor_InstructionLoop.cs:66` (Execution.VM.Processor.Processing_Loop)
- 5.2% `Processor_InstructionLoop.cs:210` (Execution.VM.Processor.Processing_Loop)
- 5.1% `Processor_InstructionLoop.cs:221` (Execution.VM.Processor.Processing_Loop)
- 4.4% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)
- 4.2% `Processor_InstructionLoop.cs:1319` (Execution.VM.Processor.ExecIndex)
- 3.1% `Processor_InstructionLoop.cs:102` (Execution.VM.Processor.Processing_Loop)
- 2.5% `Processor_InstructionLoop.cs:211` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 36.4% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 17.9% `DynValue  @ Execution.VM.Processor.ExecAdd (Processor_InstructionLoop.cs:907) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:66)`
- 9.1% `DynValue  @ DynValue.NewBoolean (DynValue.cs:100) <- Execution.VM.Processor.ExecNot (Processor_InstructionLoop.cs:579)`
- 9.1% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 9.1% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 9.0% `DynValue  @ Execution.VM.Processor.ExecLessEq (Processor_InstructionLoop.cs:1125) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:102)`
- 8.9% `DynValue  @ Execution.VM.Processor.ExecCNot (Processor_InstructionLoop.cs:573) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:126)`

#### table_hash_set_get

_CPU: thread 24660, 6991 samples in MoonSharp stacks (2.9% External/native-runtime)_

Top self (exclusive) methods:

- 78.8% `MS.Execution.VM.Processor.Processing_Loop`
- 5.7% `System.Collections.Generic.Dictionary`2[System.__Canon,System.__Canon].FindValue`
- 2.3% `System.Runtime.CompilerServices.CastHelpers.StelemRef`
- 2.3% `[runtime/native] under MS.Execution.VM.Processor.Processing_Loop`
- 1.8% `MS.Execution.VM.Processor.ExecIndexSet`
- 1.7% `MS.Execution.VM.Processor.ExecIndex`
- 1.5% `System.Collections.Generic.Dictionary`2[System.Int32,System.__Canon].FindValue`
- 0.9% `System.String.GetNonRandomizedHashCode`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 17.1% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 13.9% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 8.5% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)
- 5.3% `Processor_InstructionLoop.cs:221` (Execution.VM.Processor.Processing_Loop)
- 5.1% `Processor_InstructionLoop.cs:1319` (Execution.VM.Processor.ExecIndex)
- 3.9% `Processor_InstructionLoop.cs:210` (Execution.VM.Processor.Processing_Loop)
- 3.9% `Processor_InstructionLoop.cs:227` (Execution.VM.Processor.Processing_Loop)
- 3.7% `Processor_InstructionLoop.cs:1239` (Execution.VM.Processor.ExecIndexSet)
- 3.4% `Processor_InstructionLoop.cs:66` (Execution.VM.Processor.Processing_Loop)
- 3.2% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 47.4% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 10.5% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 10.4% `DynValue  @ Execution.VM.Processor.ExecAdd (Processor_InstructionLoop.cs:907) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:66)`
- 5.3% `DynValue  @ DynValue.NewBoolean (DynValue.cs:100) <- Execution.VM.Processor.ExecNot (Processor_InstructionLoop.cs:579)`
- 5.3% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 5.2% `DynValue  @ Execution.VM.Processor.ExecIndex (Processor_InstructionLoop.cs:1323) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:221)`
- 5.2% `DynValue  @ Execution.VM.Processor.ExecCNot (Processor_InstructionLoop.cs:573) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:126)`
- 5.2% `DynValue  @ Execution.VM.Processor.ExecLessEq (Processor_InstructionLoop.cs:1125) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:102)`
- 5.2% `DynValue  @ Execution.VM.Processor.ExecIndexSet (Processor_InstructionLoop.cs:1241) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:227)`

#### int_math

_CPU: thread 25194, 6138 samples in MoonSharp stacks (0.4% External/native-runtime)_

Top self (exclusive) methods:

- 91.6% `MS.Execution.VM.Processor.Processing_Loop`
- 2.9% `System.Runtime.CompilerServices.CastHelpers.StelemRef`
- 2.2% `System.Array.Clear`
- 0.9% `System.SpanHelpers.ClearWithReferences`
- 0.9% `MS.Execution.VM.Processor.ExecMod`
- 0.4% `MS.Execution.VM.Processor.ExecMul`
- 0.4% `MS.Execution.VM.Processor.ExecAdd`
- 0.3% `[runtime/native] under MS.Execution.VM.Processor.Processing_Loop`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 20.8% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 12.3% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 9.5% `Processor_InstructionLoop.cs:90` (Execution.VM.Processor.Processing_Loop)
- 6.5% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)
- 5.7% `Processor_InstructionLoop.cs:210` (Execution.VM.Processor.Processing_Loop)
- 5.3% `Processor_InstructionLoop.cs:66` (Execution.VM.Processor.Processing_Loop)
- 4.9% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)
- 4.7% `Processor_InstructionLoop.cs:82` (Execution.VM.Processor.Processing_Loop)
- 4.4% `Processor_InstructionLoop.cs:154` (Execution.VM.Processor.Processing_Loop)
- 3.7% `Processor_InstructionLoop.cs:211` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 33.3% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 16.7% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 16.6% `DynValue  @ Execution.VM.Processor.ExecMod (Processor_InstructionLoop.cs:973) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:90)`
- 16.5% `DynValue  @ Execution.VM.Processor.ExecMul (Processor_InstructionLoop.cs:950) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:82)`
- 16.5% `DynValue  @ Execution.VM.Processor.ExecAdd (Processor_InstructionLoop.cs:907) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:66)`

#### fib_recursive

_CPU: thread 25713, 13163 samples in MoonSharp stacks (1.3% External/native-runtime)_

Top self (exclusive) methods:

- 73.3% `MS.Execution.VM.Processor.Processing_Loop`
- 7.1% `MS.Execution.VM.Processor.ExecArgs`
- 5.4% `MS.Execution.VM.Processor.ExecRet`
- 5.2% `MS.Execution.VM.Processor.Internal_ExecCall`
- 3.7% `System.Runtime.CompilerServices.CastHelpers.StelemRef`
- 1.0% `System.Array.Clear`
- 1.0% `[runtime/native] under MS.Execution.VM.Processor.Processing_Loop`
- 0.6% `System.SpanHelpers.ClearWithReferences`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 17.4% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 15.6% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 5.2% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)
- 4.8% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)
- 3.4% `Processor_InstructionLoop.cs:744` (Execution.VM.Processor.Internal_ExecCall)
- 3.4% `Processor_InstructionLoop.cs:204` (Execution.VM.Processor.Processing_Loop)
- 3.1% `Processor_InstructionLoop.cs:106` (Execution.VM.Processor.Processing_Loop)
- 2.9% `Processor_InstructionLoop.cs:78` (Execution.VM.Processor.Processing_Loop)
- 2.8% `Processor_InstructionLoop.cs:162` (Execution.VM.Processor.Processing_Loop)
- 2.7% `Processor_InstructionLoop.cs:142` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 18.7% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 15.0% `Execution.VM.CallStackItem  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:744) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 7.4% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 7.4% `DynValue  @ Execution.VM.Processor.ExecLess (Processor_InstructionLoop.cs:1096) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:106)`
- 7.4% `DynValue  @ DynValue.NewBoolean (DynValue.cs:100) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:162)`
- 7.4% `DynValue  @ Execution.VM.Processor.ExecSub (Processor_InstructionLoop.cs:928) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:78)`
- 7.3% `DynValue  @ Execution.VM.Processor.ExecArgs (Processor_InstructionLoop.cs:657) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:166)`
- 7.3% `DynValue  @ Execution.VM.Processor.ExecArgs (Processor_InstructionLoop.cs:650) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:166)`
- 7.3% `DynValue  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:743) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 6.1% `DataStructs.Slice`1[DynValue]  @ Execution.VM.Processor.ExecArgs (Processor_InstructionLoop.cs:639) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:166)`

#### string_format

_CPU: thread 26821, 9329 samples in MoonSharp stacks (0.3% External/native-runtime)_

Top self (exclusive) methods:

- 20.4% `System.Text.RegularExpressions.RegexInterpreter.TryMatchAtCurrentPosition`
- 17.9% `MS.Execution.VM.Processor.Processing_Loop`
- 11.9% `MS.Interop.LuaStateInterop.Tools.sprintf`
- 9.5% `MS.CoreLib.StringLib.KopiLua_StringLib.str_format`
- 6.5% `System.Buffer.Memmove`
- 4.5% `System.Text.RegularExpressions.GroupCollection.GetGroupImpl`
- 3.8% `System.Text.RegularExpressions.Regex.RunSingleMatch`
- 2.5% `MS.Execution.VM.Processor.Internal_ExecCall`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 26.5% `Tools.cs:596` (Interop.LuaStateInterop.Tools.sprintf)
- 5.0% `Tools.cs:324` (Interop.LuaStateInterop.Tools.sprintf)
- 4.0% `KopiLua_StrLib.cs:859` (CoreLib.StringLib.KopiLua_StringLib.scanformat)
- 3.9% `Tools.cs:598` (Interop.LuaStateInterop.Tools.sprintf)
- 3.6% `Tools.cs:699` (Interop.LuaStateInterop.Tools.FormatNumber)
- 2.8% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 2.6% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 2.2% `KopiLua_StrLib.cs:983` (CoreLib.StringLib.KopiLua_StringLib.str_format)
- 1.8% `KopiLua_StrLib.cs:973` (CoreLib.StringLib.KopiLua_StringLib.str_format)
- 1.7% `Tools.cs:712` (Interop.LuaStateInterop.Tools.FormatNumber)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 25.8% `System.Char[]  @ CoreLib.StringLib.KopiLua_StringLib.str_format (KopiLua_StrLib.cs:916) <- CoreLib.StringModule.format (StringModule.cs:225)`
- 4.7% `System.Text.RegularExpressions.Group  @ Interop.LuaStateInterop.Tools.sprintf (Tools.cs:324) <- CoreLib.StringLib.KopiLua_StringLib.str_format (KopiLua_StrLib.cs:973)`
- 4.5% `System.Text.RegularExpressions.Group  @ Interop.LuaStateInterop.Tools.sprintf (Tools.cs:324) <- CoreLib.StringLib.KopiLua_StringLib.str_format (KopiLua_StrLib.cs:962)`
- 2.6% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 1.8% `System.Char[]  @ CoreLib.StringLib.KopiLua_StringLib.str_format (KopiLua_StrLib.cs:915) <- CoreLib.StringModule.format (StringModule.cs:225)`
- 1.7% `DynValue[]  @ Interop.LuaStateInterop.LuaState..ctor (LuaState.cs:21) <- CoreLib.StringModule.format (StringModule.cs:225)`
- 1.6% `System.Char[]  @ CoreLib.StringLib.KopiLua_StringLib.str_format (KopiLua_StrLib.cs:983) <- CoreLib.StringModule.format (StringModule.cs:225)`
- 1.5% `System.Text.RegularExpressions.Match  @ Interop.LuaStateInterop.Tools.sprintf (Tools.cs:598) <- CoreLib.StringLib.KopiLua_StringLib.str_format (KopiLua_StrLib.cs:973)`
- 1.5% `System.Int32[]  @ Interop.LuaStateInterop.Tools.sprintf (Tools.cs:596) <- CoreLib.StringLib.KopiLua_StringLib.str_format (KopiLua_StrLib.cs:962)`
- 1.3% `System.Text.RegularExpressions.Match  @ Interop.LuaStateInterop.Tools.sprintf (Tools.cs:598) <- CoreLib.StringLib.KopiLua_StringLib.str_format (KopiLua_StrLib.cs:962)`

#### coroutine_switch

_CPU: thread 27212, 5200 samples in MoonSharp stacks (1.2% External/native-runtime)_

Top self (exclusive) methods:

- 58.9% `MS.Execution.VM.Processor.Processing_Loop`
- 16.2% `MS.Execution.VM.Processor.Internal_ExecCall`
- 4.9% `MS.Execution.VM.Processor.Coroutine_Resume`
- 4.8% `System.Collections.Generic.Dictionary`2[System.__Canon,System.__Canon].FindValue`
- 3.0% `System.Runtime.CompilerServices.CastHelpers.StelemRef`
- 2.2% `System.SpanHelpers.SequenceEqual`
- 1.6% `System.Array.Clear`
- 1.5% `MS.Execution.VM.Processor.ExecIndex`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 14.1% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 9.6% `Processor_InstructionLoop.cs:1319` (Execution.VM.Processor.ExecIndex)
- 9.1% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 8.1% `Processor_InstructionLoop.cs:727` (Execution.VM.Processor.Internal_ExecCall)
- 4.3% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)
- 4.0% `Processor_InstructionLoop.cs:221` (Execution.VM.Processor.Processing_Loop)
- 3.8% `Processor_InstructionLoop.cs:66` (Execution.VM.Processor.Processing_Loop)
- 3.7% `Processor_InstructionLoop.cs:713` (Execution.VM.Processor.Internal_ExecCall)
- 3.0% `Processor_InstructionLoop.cs:210` (Execution.VM.Processor.Processing_Loop)
- 2.5% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 17.7% `Execution.VM.CallStackItem  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:713) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 9.7% `ScriptExecutionContext  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:727) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 9.1% `DynValue  @ Execution.VM.Processor.ExecAdd (Processor_InstructionLoop.cs:907) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:66)`
- 8.9% `DynValue  @ Execution.VM.Processor.ExecIndex (Processor_InstructionLoop.cs:1323) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:221)`
- 8.3% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 7.7% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Coroutine_Resume (Processor_Coroutines.cs:70)`
- 4.9% `CallbackArguments  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:727) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 4.5% `DynValue  @ Execution.VM.Processor.Coroutine_Resume (Processor_Coroutines.cs:58) <- Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:727)`
- 4.5% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 4.0% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Coroutine_Resume (Processor_Coroutines.cs:70)`

#### closure_create_call

_CPU: thread 29123, 10277 samples in MoonSharp stacks (3.3% External/native-runtime)_

Top self (exclusive) methods:

- 67.6% `System.Collections.Generic.List`1[System.__Canon].AddRange`
- 16.1% `MS.Execution.VM.Processor.Processing_Loop`
- 3.1% `[runtime/native] under System.Collections.Generic.List`1[System.__Canon].AddRange`
- 1.4% `MS.Execution.VM.Processor.ExecClosure`
- 1.2% `System.Linq.Enumerable+SelectArrayIterator`2[System.__Canon,System.__Canon].ToList`
- 1.2% `MS.Execution.VM.Processor.Internal_ExecCall`
- 1.0% `MS.Execution.VM.Processor.ExecRet`
- 1.0% `System.Runtime.CompilerServices.CastHelpers.IsInstance_Helper`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 72.4% `Closure.cs:68` (Closure..ctor)
- 5.4% `Processor_InstructionLoop.cs:416` (Execution.VM.Processor.ExecClosure)
- 4.1% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 3.6% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 1.5% `ClosureContext.cs:19` (Execution.ClosureContext..ctor)
- 1.2% `Processor_InstructionLoop.cs:210` (Execution.VM.Processor.Processing_Loop)
- 1.0% `Processor_InstructionLoop.cs:37` (Execution.VM.Processor.Processing_Loop)
- 0.9% `Processor_InstructionLoop.cs:201` (Execution.VM.Processor.Processing_Loop)
- 0.9% `Processor_InstructionLoop.cs:744` (Execution.VM.Processor.Internal_ExecCall)
- 0.8% `Processor_InstructionLoop.cs:211` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 10.6% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 10.5% `Execution.VM.CallStackItem  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:744) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 10.5% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 7.0% `System.Func`2[SymbolRef,DynValue]  @ Execution.VM.Processor.ExecClosure (Processor_InstructionLoop.cs:416) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:157)`
- 6.1% `DynValue[]  @ Closure..ctor (Closure.cs:68) <- Execution.VM.Processor.ExecClosure (Processor_InstructionLoop.cs:416)`
- 5.3% `DynValue  @ DynValue.NewClosure (DynValue.cs:176) <- Execution.VM.Processor.ExecClosure (Processor_InstructionLoop.cs:419)`
- 5.3% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 5.3% `SelectArrayIterator`2[SymbolRef,DynValue]  @ Execution.VM.Processor.ExecClosure (Processor_InstructionLoop.cs:416) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:157)`
- 5.3% `SelectArrayIterator`2[SymbolRef,System.String]  @ Closure..ctor (Closure.cs:68) <- Execution.VM.Processor.ExecClosure (Processor_InstructionLoop.cs:416)`
- 5.2% `DynValue  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:743) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`

#### clr_interop

_CPU: thread 27672, 6715 samples in MoonSharp stacks (0.6% External/native-runtime)_

Top self (exclusive) methods:

- 43.8% `MS.Execution.VM.Processor.Processing_Loop`
- 10.2% `System.Collections.Generic.HashSet`1[System.__Canon].FindItemIndex`
- 7.7% `System.Collections.Generic.Dictionary`2[System.__Canon,System.__Canon].FindValue`
- 7.1% `MS.Interop.MethodMemberDescriptor.Execute`
- 7.0% `MS.Execution.VM.Processor.Internal_ExecCall`
- 3.2% `MS.Execution.VM.Processor.ExecIndex`
- 2.2% `System.Runtime.CompilerServices.CastHelpers.StelemRef`
- 2.1% `MS.Interop.Converters.ClrToScriptConversions.ObjectToDynValue`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 8.6% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 8.5% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 6.6% `Processor_InstructionLoop.cs:1319` (Execution.VM.Processor.ExecIndex)
- 6.6% `DispatchingUserDataDescriptor.cs:235` (Interop.BasicDescriptors.DispatchingUserDataDescriptor.Index)
- 6.1% `Processor_InstructionLoop.cs:221` (Execution.VM.Processor.Processing_Loop)
- 5.8% `ScriptToClrConversions.cs:144` (Interop.Converters.ScriptToClrConversions.DynValueToObjectOfType)
- 5.5% `MethodMemberDescriptor.cs:186` (Interop.MethodMemberDescriptor.Execute)
- 5.4% `ClrToScriptConversions.cs:73` (Interop.Converters.ClrToScriptConversions.TryObjectToSimpleDynValue)
- 3.6% `Processor_InstructionLoop.cs:727` (Execution.VM.Processor.Internal_ExecCall)
- 2.5% `Processor_InstructionLoop.cs:66` (Execution.VM.Processor.Processing_Loop)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 8.2% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 8.2% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 8.2% `DynValue  @ Execution.VM.Processor.ExecIndex (Processor_InstructionLoop.cs:1349) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:221)`
- 8.2% `DynValue  @ Execution.VM.Processor.ExecAdd (Processor_InstructionLoop.cs:907) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:66)`
- 8.1% `Execution.VM.CallStackItem  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:713) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 8.1% `DynValue  @ Interop.Converters.ClrToScriptConversions.TryObjectToSimpleDynValue (ClrToScriptConversions.cs:74) <- Interop.Converters.ClrToScriptConversions.ObjectToDynValue (ClrToScriptConversions.cs:102)`
- 8.0% `DynValue  @ Execution.VM.Processor.ExecIndex (Processor_InstructionLoop.cs:1323) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:221)`
- 5.3% `System.Func`3[ScriptExecutionContext,CallbackArguments,DynValue]  @ Interop.OverloadedMethodMemberDescriptor.GetCallback (OverloadedMethodMemberDescriptor.cs:0) <- Interop.OverloadedMethodMemberDescriptor.GetCallbackFunction (OverloadedMethodMemberDescriptor.cs:443)`
- 4.1% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 4.1% `CallbackFunction  @ Interop.OverloadedMethodMemberDescriptor.GetCallbackFunction (OverloadedMethodMemberDescriptor.cs:443) <- Interop.OverloadedMethodMemberDescriptor.GetValue (OverloadedMethodMemberDescriptor.cs:473)`

#### clr_interop_reflection

_CPU: thread 28080, 6959 samples in MoonSharp stacks (2.1% External/native-runtime)_

Top self (exclusive) methods:

- 40.0% `MS.Execution.VM.Processor.Processing_Loop`
- 9.7% `System.Collections.Generic.HashSet`1[System.__Canon].FindItemIndex`
- 7.0% `MS.Interop.MethodMemberDescriptor.Execute`
- 6.8% `System.Collections.Generic.Dictionary`2[System.__Canon,System.__Canon].FindValue`
- 6.3% `MS.Execution.VM.Processor.Internal_ExecCall`
- 3.2% `MS.Execution.VM.Processor.ExecIndex`
- 2.3% `System.SpanHelpers.SequenceEqual`
- 2.3% `System.Runtime.CompilerServices.CastHelpers.StelemRef`

Top source lines (first MoonSharp frame; inlined callees are charged to the call-site line):

- 7.9% `Processor_InstructionLoop.cs:0` (Execution.VM.Processor.Processing_Loop)
- 7.4% `Processor_InstructionLoop.cs:30` (Execution.VM.Processor.Processing_Loop)
- 6.9% `DispatchingUserDataDescriptor.cs:235` (Interop.BasicDescriptors.DispatchingUserDataDescriptor.Index)
- 6.3% `Processor_InstructionLoop.cs:221` (Execution.VM.Processor.Processing_Loop)
- 6.2% `Processor_InstructionLoop.cs:1319` (Execution.VM.Processor.ExecIndex)
- 5.6% `ScriptToClrConversions.cs:144` (Interop.Converters.ScriptToClrConversions.DynValueToObjectOfType)
- 5.0% `MethodMemberDescriptor.cs:186` (Interop.MethodMemberDescriptor.Execute)
- 5.0% `ClrToScriptConversions.cs:73` (Interop.Converters.ClrToScriptConversions.TryObjectToSimpleDynValue)
- 3.5% `MethodMemberDescriptor.cs:210` (Interop.MethodMemberDescriptor.Execute)
- 3.4% `Processor_InstructionLoop.cs:727` (Execution.VM.Processor.Internal_ExecCall)

Top allocation sites (share of sampled bytes; type @ allocating frame):

- 8.2% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:201) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 8.1% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:204) <- Execution.VM.Processor.Call (Processor.cs:90)`
- 8.1% `DynValue  @ Execution.VM.Processor.ExecIndex (Processor_InstructionLoop.cs:1349) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:221)`
- 8.1% `DynValue  @ Execution.VM.Processor.ExecAdd (Processor_InstructionLoop.cs:907) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:66)`
- 8.1% `Execution.VM.CallStackItem  @ Execution.VM.Processor.Internal_ExecCall (Processor_InstructionLoop.cs:713) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:115)`
- 7.9% `DynValue  @ Interop.Converters.ClrToScriptConversions.TryObjectToSimpleDynValue (ClrToScriptConversions.cs:74) <- Interop.Converters.ClrToScriptConversions.ObjectToDynValue (ClrToScriptConversions.cs:102)`
- 7.9% `DynValue  @ Execution.VM.Processor.ExecIndex (Processor_InstructionLoop.cs:1323) <- Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:221)`
- 5.3% `System.Func`3[ScriptExecutionContext,CallbackArguments,DynValue]  @ Interop.OverloadedMethodMemberDescriptor.GetCallback (OverloadedMethodMemberDescriptor.cs:0) <- Interop.OverloadedMethodMemberDescriptor.GetCallbackFunction (OverloadedMethodMemberDescriptor.cs:443)`
- 4.1% `CallbackFunction  @ Interop.OverloadedMethodMemberDescriptor.GetCallbackFunction (OverloadedMethodMemberDescriptor.cs:443) <- Interop.OverloadedMethodMemberDescriptor.GetValue (OverloadedMethodMemberDescriptor.cs:473)`
- 4.1% `DynValue  @ Execution.VM.Processor.Processing_Loop (Processor_InstructionLoop.cs:211) <- Execution.VM.Processor.Call (Processor.cs:90)`

