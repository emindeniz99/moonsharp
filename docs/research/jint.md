# Jint: what a managed interpreter teaches MoonSharp

Date: 2026-10-28. Analyst read-only pass.

- **Engine:** Jint, a managed JavaScript interpreter (tree-walking, not bytecode).
- **Repo / commit read:** `sebastienros/jint@9ec2352c222dff6b7bff806d16b087cf0f1e6384` (2026-10-27). Clone at
  `/tmp/claude-0/-home-user/49abf7c9-632c-52bb-859e-20c0836e18dc/scratchpad/ext/jint`. Full history fetched (`--filter=blob:none`) to date the techniques.
- **MoonSharp read:** `/home/user/moonsharp` `master` = `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` (via `git show master:<path>`).
- **Permalink base:** `J = https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384`
  and `M = https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7`.
  Labels: [C] confirmed in source, [S] secondary (commit message or doc), [U] unconfirmed.

## 1. Licence

- `LICENSE.txt`: BSD 2-Clause, "Copyright (c) 2013, Sebastien Ros" ([C], `J/LICENSE.txt`). SPDX `BSD-2-Clause`.
- `Jint/Collections/StringDictionarySlim.cs` and `DictionarySlim.cs` carry an MIT header from the .NET Foundation
  (corefxlab `DictionarySlim`) ([C], `J/Jint/Collections/StringDictionarySlim.cs#L6-L8`). SPDX `MIT`.
- **Compatibility with BSD-3-Clause MoonSharp:** ideas are free. Copying code is allowed for both licences if the
  copyright notice, conditions and disclaimer are kept (in the file header and a `THIRD-PARTY-NOTICES`). The plan below
  re-implements ideas in MoonSharp's own structure; nothing needs to be copied verbatim. If the ordered slim dictionary
  (idea 9) is ported closely, keep the MIT .NET Foundation header.

## 2. Architecture in one page

Jint parses with Acornima into an AST, then lazily builds a parallel tree of `JintExpression` / `JintStatement`
handler nodes, cached on `Node.UserData` (`J/Jint/Runtime/Interpreter/Expressions/JintExpression.cs#L189-L194`) [C].
Every handler node specializes at build time on its operand shapes (constant, identifier, slot) and keeps small
per-node caches (slot location, callee identity, member shape). There is no bytecode and no dispatch loop: speed comes
from (a) never allocating on the hot path, (b) build-time specialization of nodes, (c) per-site monomorphic caches,
(d) unboxed numeric storage in local slots.

What matters for MoonSharp is not the tree-walking design (MoonSharp's bytecode stack VM is a better base), but the
**value-lifetime discipline**: Jint values are immutable, so they can be shared, cached and stored by reference with no
defensive copy. MoonSharp's mutable `DynValue` forces a clone on every local read. Most of Jint's wins rest on that
one property.

### 2.1 Value representation

- `JsValue` is an abstract class with one field, `InternalTypes _type` (a `[Flags]` enum used for one-test type
  checks), `J/Jint/Native/JsValue.cs#L23-L45`, `J/Jint/Runtime/InternalTypes.cs#L3-L80` [C].
- `JsNumber` is sealed and immutable (`readonly double _value`). It distinguishes `InternalTypes.Integer` from
  `Number` at construction so integer fast paths can test one flag
  (`J/Jint/Native/JsNumber.cs#L49-L72`) [C].
- **Small-integer cache:** `_intToJsValue` holds 10 240 pre-built `JsNumber`s (0..10239) plus singletons for -1, -0,
  NaN, ±Infinity, 1, 2, 3, PI (`J/Jint/Native/JsNumber.cs#L21-L47`) [C]. `Create(int)` is an unsigned range check
  and an array load (`#L161-L175`).
- `Create(double)` detects integral values with a `(long)` cast round-trip, not `value % 1`: the comment says `fmod`
  dominated arithmetic-heavy workloads (`J/Jint/Native/JsNumber.cs#L103-L144`) [C]. Rare cases go to a
  `[NoInlining]` helper so the hot method stays small.
- `JsBoolean.True/False` are the only two booleans; the constructor is private
  (`J/Jint/Native/JsBoolean.cs#L5-L20`) [C].
- Strings: cached single-ASCII-char strings and `0..1023` int-to-string table
  (`J/Jint/Native/JsString.cs#L94-L136`, `#L442-L470`) [C]; `ConcatenatedString` is an owned, in-place extensible
  rope for `s += x` loops (`J/Jint/Native/JsString.cs#L737-L810`) [C], with ownership tracked in the binding
  (`J/Jint/Runtime/Environments/Binding.cs#L22-L33`, `#L128-L138`) [C].

### 2.2 Locals: `Binding` struct with an unboxed-number lane

- Function locals live in `Binding[] _slots` on `DeclarativeEnvironment`
  (`J/Jint/Runtime/Environments/DeclarativeEnvironment.cs#L14-L22`) [C].
- `Binding` is a `readonly struct { JsValue? _value; double _number; BindingFlags _flags; }`. When the
  `UnboxedNumber` flag is set the value lives in `_number` and `_value` is null
  (`J/Jint/Runtime/Environments/Binding.cs#L7-L20`, `#L37-L61`, `#L140-L149`) [C].
- Readers that only need a double (`TryGetNumberSlotForRead`) never materialize a `JsNumber`; writers
  (`SetNumberSlot`) store the raw double. A reader that must hand a value on materializes once and writes the
  `JsNumber` back so later reads are free (`ReadSlotSlow` / `MaterializeUnboxedOrNull`,
  `J/Jint/Runtime/Environments/DeclarativeEnvironment.cs#L39-L200`) [C].
- Introduced by "Store function-local numbers unboxed in environment slot bindings (#2499)",
  commit `3ca07cdc`, 2026-06-07 [S] — https://github.com/sebastienros/jint/pull/2499.
- Consumers: `x++` / `++x` (`J/Jint/Runtime/Interpreter/Expressions/JintUpdateExpression.cs#L74-L135`),
  compound assignment `x += e` (`J/Jint/Runtime/Interpreter/Expressions/JintAssignmentExpression.cs#L560-L630`),
  binary arithmetic and comparisons over identifier/constant leaves (`NumericOperandLane`,
  `J/Jint/Runtime/Interpreter/Expressions/JintBinaryExpression.cs#L294-L406`) [C], and the modulo lane
  ("Avoid materializing numeric modulo operands", #4154, commit `067a54fd`) [S].

### 2.3 Expressions: typed fast lanes and boolean results without boxing

- `PlusBinaryExpression`: unboxed lane first, then `AreIntegerOperands` (integer add in `long`, then
  `JsNumber.Create(long)` hits the cache), then double/double, then the generic ToPrimitive path
  (`J/.../JintBinaryExpression.cs#L1544-L1586`) [C].
- Comparisons return `JsBoolean.True/False` and **override `GetBooleanValue`** so `if`/`while`/`for` tests get a raw
  `bool` and never touch a value object (`J/.../JintExpression.cs#L146-L153`, `J/.../JintBinaryExpression.cs#L1427-L1481`) [C].
- `EvaluateAndDiscard` lets expression statements skip materializing a result (`J/.../JintExpression.cs#L129-L144`) [C].
- Constant argument lists are evaluated once and the same array is reused (`ExpressionCache._fullyCached`,
  `J/Jint/Runtime/Interpreter/Expressions/ExpressionCache.cs#L10-L69`) [C]; #2037 [S].

### 2.4 Calls

- **Frames are structs.** `ExecutionContext` is a `readonly struct` stored in `RefStack<T>` (a struct stack with
  `ref readonly Peek`) (`J/Jint/Runtime/Environments/ExecutionContext.cs#L8`, `J/Jint/Collections/RefStack.cs#L11-L71`,
  `J/Jint/Runtime/ExecutionContextStack.cs#L10-L33`) [C]. Pushing a frame allocates nothing.
- **Argument arrays are pooled** by size 1-4, 15 arrays per bucket, cleared on return
  (`J/Jint/Pooling/JsValueArrayPool.cs#L9-L120`) [C]; first added in #451 (commit `93bee1d2`, 2018) [S].
- **Two monomorphic call-site lanes** in `JintCallExpression`:
  - built-in fast lane: guarded by `ReferenceEquals(func, _fastCallee)`, passes `arg0`, `arg1` as parameters,
    no argument array (`J/Jint/Runtime/Interpreter/Expressions/JintCallExpression.cs#L262-L293`) [C];
  - register lane for script functions: evaluates up to four arguments **straight into the callee's local slots**
    (`CallFromRegisters`), no `JsValue[]` (`#L295-L307`, `#L482-L560`) [C]. The callee body is shared through a
    generic `CallCore<TArgs>` over a struct argument source, so the JIT specializes it per source
    (`J/Jint/Native/Function/ScriptFunction.cs#L368-L382`) [C].
  - The cache is filled after a successful dispatch and keyed on callee identity, so a reassigned function misses
    safely (`#L396-L417`) [C].
- **Environment reuse with escape analysis.** A function whose environment cannot escape (no closures referencing
  its slots, no generators, no direct eval) reuses its last environment and slot array on the next call; directly
  recursive functions use a bounded LIFO pool of 16 (`J/Jint/Native/Function/ScriptFunction.cs#L307-L366`,
  `J/Jint/Runtime/Environments/RecursiveEnvPool.cs#L17-L45`,
  `J/Jint/Runtime/Interpreter/JintFunctionDefinition.cs#L955-L1000`) [C]; #2568 (`eceb7ba5`) [S]. The escape
  test is slot-aware: a closure that reads none of the slots does not block reuse.
- "Leaf call": a function with no bindings and no `this` reference skips creating an environment at all
  (`JintFunctionDefinition.cs#L975-L990`) [C].

### 2.5 CLR interop

- **Process-wide accessor cache** on `TypeResolver` (`ConcurrentDictionary<AccessorCacheKey, ReflectionAccessor>`),
  shared by all engines; cleared when the member filter changes
  (`J/Jint/Runtime/Interop/TypeResolver.cs#L11-L113`) [C]; #951 [S].
- **Per-parameter classification precomputed once** (`InteropParameterFlags`: JsValue-assignable, generic-like,
  `params JsValue[]`) so the per-call loop does flag tests instead of reflection queries
  (`J/Jint/Runtime/Interop/MethodDescriptor.cs#L686-L698`, used in
  `J/Jint/Runtime/Interop/MethodInfoFunction.cs#L394-L470`) [C].
- **Single-candidate shortcut:** with one overload, skip scoring (`MethodInfoFunction.cs#L196-L205`) [C].
- **Numeric fast conversion** before the general converter: `double`, and integral `int`/`long` with range check
  (`J/Jint/Runtime/Interop/InteropHelper.cs#L564-L594`, call site `MethodInfoFunction.cs#L452-L455`) [C].
- **Measured NO-GO:** pooling the `object[]` reflection parameter buffer with `ArrayPool` was slower than allocating
  it (`MethodInfoFunction.cs#L411-L414`) [C].
- **Compiled delegates only when they will be JIT-compiled.** `CompiledMethodInvoker` (#2733, `fed894d4`) and
  `CompiledMemberAccessor` (#2744, `bdf5bcfb`) build typed delegates with `System.Linq.Expressions`; both are gated on
  `RuntimeFeature.IsDynamicCodeCompiled` because an *interpreted* expression lambda (AOT, Mono interpreter) is slower
  than cached reflection (`J/Jint/Runtime/Interop/MethodDescriptor.cs#L305-L345`,
  `J/Jint/Runtime/Interop/Reflection/CompiledMemberAccessor.cs#L13-L131`) [C]. The "JsValue lane" getter returns
  the script value directly (`Func<object, JsValue>`) for `int/long/double/bool/string/JsValue` members: no boxing,
  no `FromObject` dispatch. Fast lanes switch off when custom converters could observe the value
  (`MethodInfoFunction.cs#L206-L229`) [C].
- **AOT path = source generator with explicit registration.** `[JsAccessible]` types get generated typed accessors;
  the generated `RegisterAll()` is called by the host, not by a module initializer, and late registration
  invalidates only that type's cache entries (`J/Jint/Runtime/Interop/JsAccessibleRegistry.cs#L10-L89`) [C];
  #3333 (`cff5d5bc`) [S]. Generator: `J/Jint.SourceGenerators.Interop/JsAccessibleGenerator.cs`.
- **CLR→script conversion** is a `Dictionary<Type, Func<Engine, object, JsValue>>` lookup, extended by
  copy-on-write (`Interlocked.CompareExchange` of a new dictionary) so reads are lock-free
  (`J/Jint/Runtime/Interop/DefaultObjectConverter.cs#L15-L75`) [C].

### 2.6 Property storage

- `Key` struct with a precomputed FNV hash for property names (`J/Jint/Key.cs#L7-L55`) [C]; #634 [S].
- `HybridDictionary<T>`: a linear list up to 9 entries, then `StringDictionarySlim<T>`
  (`J/Jint/Collections/HybridDictionary.cs#L8-L60`) [C].
- `StringDictionarySlim<T>`: entries in one array, **insertion-ordered**, removal leaves a tombstone (reclaimed at
  resize), ref-returning `GetValueRefOrAddDefault` avoids the TryGetValue+Add double lookup
  (`J/Jint/Collections/StringDictionarySlim.cs#L17-L60`, `#L296-L338`) [C].
- Arrays: dense `JsValue?[] _dense` with a sparse `Dictionary<uint, PropertyDescriptor>` fallback
  (`J/Jint/Native/Array/ArrayInstance.cs#L55-L115`) [C].
- Hidden-class shapes (`JsObjectShape`, `ShapeMode` flags) exist but target JS object literals and constructors;
  they do not map to Lua tables directly.

### 2.7 Prepared scripts

`Engine.PrepareScript` returns `Prepared<Script>`, a reusable, thread-safe parse plus static analysis that many
engines can execute (`J/Jint/Engine.Ast.cs#L23-L58`, `J/Jint/Prepared.cs`) [C]. Per-engine state (environment
caches) was moved off the shared state so a prepared script does not pin engines (#2568) [S].

### 2.8 Benchmarks and allocation tests

- BenchmarkDotNet suite with 190 files, including `FunctionLocalNumberLoopBenchmark`, `FastCallLaneBenchmarks`,
  `ClosureCallBenchmarks`, `RecursionBenchmark`, `InteropMethodDispatchBenchmark`, `PreparedScriptBenchmark`
  (`J/Jint.Benchmark/`) [C].
- `AllocTypeProbe` attributes allocations to CLR types with `GCAllocationTick` events
  (`J/Jint.Benchmark/AllocTypeProbe.cs#L7-L20`) [C].
- Allocation-budget regression tests: `GC.GetAllocatedBytesForCurrentThread()` before/after, assert below a budget,
  under `#if NET` (`J/Jint.Tests/Runtime/SlotLocationCacheTests.cs#L250-L267`) [C]; 23 such uses in `Jint.Tests`.
- Perf checklist and pitfalls: AggressiveInlining overuse exhausts the inline budget (#4317); copy fields to locals
  before loops; `(uint)i < (uint)len` bounds idiom (`J/Jint/AGENTS.md#L120-L152`) [C].

## 3. Unsafe inventory

Counts over `Jint/**/*.cs` [C]: `Unsafe.As<T>` 72, `Unsafe.AsRef` 12, `Unsafe.NullRef`/`IsNullRef` 11,
`Unsafe.Add` 11, `Unsafe.Read/WriteUnaligned` 3, `MemoryMarshal.*` 26, `CollectionsMarshal` 3, `unsafe` 22,
`fixed` 16, `stackalloc` 95.

| Use | Where | What it buys | Safe alternative for MoonSharp |
|---|---|---|---|
| `Unsafe.As<T>(obj)` | `JintExpression.GetValue` (`#L75-L90`), Less/Plus lanes, call expression | skips `castclass` after a flag test already proved the type | ordinary cast; on .NET 8 the JIT often removes it for sealed types, and IL2CPP casts are cheap. Make types `sealed` and test `Type` enum first. |
| `Unsafe.AsRef(in field) = x` | `ExecutionContextStack.ReplaceTop*` (`#L25-L41`) | write one field of a readonly struct in an array slot | make the frame a plain (non-readonly) struct and mutate through `ref` returned by the stack: `ref CallFrame f = ref stack.PeekRef();` — fully safe C# 7. |
| `Unsafe.NullRef`/`IsNullRef` | `StringDictionarySlim.GetValueRefOrNullRef`, `DeclarativeEnvironment` | ref-returning "not found" | return an index (`-1` = miss) or `bool TryGet(out)`, then `ref entries[i]`. |
| `Unsafe.Add` | `SumOfProductsLane` (`JintBinaryExpression.cs#L575-L578`) | bounds-check-free element step | span/array indexing; cost is one bounds check. |
| `unsafe`/`fixed`/`MemoryMarshal` | `Extensions/Polyfills.cs`, JSON, Atomics, `ValueStringBuilder` | netstandard2.0 polyfills for span-based string APIs | not on any VM hot path; MoonSharp does not need them. |

Conclusion: Jint's core interpreter speed does **not** depend on pointers. The unsafe uses are cast elision and
ref-to-readonly writes; each has a safe equivalent costing at most a type check or bounds check.

## 4. MoonSharp facts the ideas depend on (read at fbd0367)

- `OpCode.Local` pushes `scope[index].AsReadOnly()`, and `AsReadOnly` **clones** any writable value
  (`M/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L198-L202`,
  `M/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L379-L412`) [C]. Locals are writable cells mutated by
  `Assign` (`DynValue.cs#L760-L769`, `Processor_InstructionLoop.cs#L354-L363`), so **every local read allocates**.
- Arithmetic pushes `DynValue.NewNumber(...)` (`Processor_InstructionLoop.cs#L897-L1033`); comparisons push
  `DynValue.NewBoolean(...)` (`#L1087-L1126`), `Not`/`CNot` too (`#L562-L580`) [C].
- Each script call pushes `DynValue.NewNumber(argsCount)` and a new `CallStackItem` class
  (`#L741-L750`; `M/src/MoonSharp.Interpreter/Execution/VM/CallStackItem.cs`); CLR calls also allocate a
  `CallStackItem`, `ScriptExecutionContext` and a `Slice<DynValue>` (`#L702-L740`, `#L609-L631`) [C].
- `ExecArgs` builds a `Slice` (or a `List` for tuple tails), then per argument does
  `argsList[i].ToScalar().CloneAsWritable()` and `AssignLocal` — which copies the fields anyway, so the clone is pure
  garbage; `AssignLocal` also allocates the cell with `NewNil()` the first time (`#L634-L667`, `#L354-L363`) [C].
- `ExecBeginFn` allocates `new DynValue[i.NumVal]` per call (`#L582-L590`) [C].
- `ExecClosure` uses LINQ `Select(...).ToList()` per closure creation (`#L414-L420`) [C].
- `ExecIncr` already handles a read-only loop variable by cloning (`#L543-L559`) [C], and literals are already
  read-only (`M/src/MoonSharp.Interpreter/Tree/Expressions/LiteralExpression.cs#L30-L34`) [C], so read-only
  shared values on the stack are an existing, supported state.
- Tables: `LinkedList<TablePair>` + three `LinkedListIndex` (each a `Dictionary<K, LinkedListNode<TablePair>>`);
  no array part (`M/src/MoonSharp.Interpreter/DataTypes/Table.cs#L12-L31`) [C].
- Interop: `FunctionMemberDescriptorBase.BuildArgumentList` re-tests `Type.IsByRef`, `== typeof(Script)` etc. per
  parameter per call (`M/src/MoonSharp.Interpreter/Interop/StandardDescriptors/MemberDescriptors/FunctionMemberDescriptorBase.cs#L131-L200`),
  and `ScriptToClrConversions.DynValueToObjectOfType` does a custom-converter lookup, `Nullable.GetUnderlyingType`
  and string-subtype classification per argument
  (`M/src/MoonSharp.Interpreter/Interop/Converters/ScriptToClrConversions.cs#L80-L140`) [C].
  `OverloadedMethodMemberDescriptor` has a 5-entry overload cache (`#L29-L43`) and `MethodMemberDescriptor` has
  `LazyOptimized` expression-compiled delegates gated on `IsRunningOnAOT` (`MethodMemberDescriptor.cs#L33-L97`) [C].

Allocation arithmetic for `for i=1,N do s = s + i % 7 end`: `Local s` clone, `Local i` clone, `Mod` result,
`Add` result = 4 `DynValue`s of ~48-56 bytes each, which matches the measured ~230 B/iteration [C by reading;
size per object is [U] until measured].

## 5. Ideas, ranked

Effort: S < 1 day, M 1-4 days, L > 4 days. Impact estimates are reasoned, not measured; every one needs an A/B run
on the flag_bench workloads plus an allocation-budget test.

### Tier 1: small, safe, immediate

**1. Shared read-only booleans for VM results** (area: value-repr). Jint: `JsBoolean.True/False` only
(`J/Jint/Native/JsBoolean.cs#L5-L20`), comparisons return them (`JintBinaryExpression.cs#L1436-L1457`).
MoonSharp: replace `DynValue.NewBoolean(x)` with `x ? DynValue.True : DynValue.False` in
`Processor_InstructionLoop.cs` (`ExecLess`, `ExecLessEq`, `ExecEq`, `ExecNot`, `ExecCNot`, `OpCode.ToBool`),
and in stdlib returns where the value is fresh. Impact: removes one allocation per comparison; `fib`, any loop with
an `if`/`while`, `int_math` with conditions: est. 3-10%. Risk: code that mutates a result it got from the stack —
only `ExecIncr`, which already handles read-only. Public `DynValue.NewBoolean` keeps returning a writable instance.
IL2CPP/netstandard2.0: no issue.

**2. Small-integer DynValue cache for VM-produced numbers** (value-repr). Jint: `_intToJsValue[10240]`, integral
check by `(long)` cast (`J/Jint/Native/JsNumber.cs#L21-L47`, `#L103-L175`). MoonSharp: add
`internal static DynValue DynValue.FromNumberShared(double d)` returning a cached read-only instance for integral
`d` in, say, `[-256, 4096)` (excluding -0: test `BitConverter.DoubleToInt64Bits`), otherwise `NewNumber(d)` marked
read-only. Use it in `ExecAdd/Sub/Mul/Mod/Div/Neg`, `ExecLen`, the `argsCount` push in `Internal_ExecCall` (every
call), `ipairs`/`next` index results, `string.len/byte`, `#t`. Keep `m_HashCode` precomputed in the cache. Impact:
`function_call`/`method_call`/`fib` lose the `argsCount` allocation per call (~5-10%); `table_array_*`,
`int_math` and `i % 7` style code lose most result allocations when values are small (10-30% on those). Risk: the
cache must be read-only; any `AssignNumber` on a stack value must handle read-only (`ExecIncr` does). Compatibility:
`ReferenceEquals` between two results can now be true; no public API promises otherwise.

**3. Remove redundant clones in the call path** (calls). Jint principle: values moved into a frame are not copied
(register lane, `JintCallExpression.cs#L482-L560`). MoonSharp: in `ExecArgs` drop `CloneAsWritable()` for the
non-vararg case (`AssignLocal` copies fields), read arguments directly from `m_ValueStack` when the last argument is
not a multi-value tuple instead of allocating `Slice<DynValue>`, and replace LINQ in `ExecClosure` with a loop over an
array. Impact: per call, 1 clone per argument plus the `Slice` object; `function_call`, `method_call`, `fib`,
`closure_create_call`: est. 10-20%. Risk: low; vararg path keeps cloning because the tuple outlives the frame.

**4. Fused compare-and-branch** (dispatch). Jint: comparison nodes override `GetBooleanValue` so the `if` reads a raw
`bool` (`JintExpression.cs#L146-L153`, `JintBinaryExpression.cs#L1459-L1480`). MoonSharp: in the compiler
(`Tree/Statements/IfStatement.cs`, `WhileStatement.cs`, `RepeatStatement.cs`, `Execution/VM/ByteCode.cs`) peephole
`Less|LessEq|Eq` + `Jf` into new opcodes `JLt/JLe/JEq` (with a negate bit) whose handler compares two numbers or
two strings and jumps, falling back to the current metamethod path. Impact: removes a push, a pop, a `ToScalar`
and the boolean value per test; loops and `fib`: est. 5-15%. Risk: medium-low; debugger source refs for the fused
instruction must keep the comparison's `SourceRef`. Binary dump format changes: bump the dump version.

**5. Precomputed parameter classification and numeric fast lane for CLR calls** (interop). Jint:
`InteropParameterFlags` (`MethodDescriptor.cs#L686-L698`), `TryConvertNumberFast` (`InteropHelper.cs#L564-L594`),
single-candidate shortcut (`MethodInfoFunction.cs#L196-L205`). MoonSharp: in `ParameterDescriptor` compute once a
`ParameterKind` (Script, Context, CallbackArguments, Out, ByRef, VarArgs, DynValue, Object, Number(TypeCode),
String, Bool, Other) and the nullable underlying type; in `BuildArgumentList` switch on it; for `Number` kinds when
`CustomConverters` has no Number→T converter, convert `double` directly (`(int)`, `(float)`, `(long)` with range
checks, same rules as `NumericConversions`). Cache "has any custom converter" as a version number on
`CustomConvertersCollection`. Impact: CLR method calls with numeric/string args (Unity-style `vec:Set(x,y,z)`):
est. 15-30% of the interop call cost. Risk: conversion-rule drift; test every `NumericConversions` case against the
fast lane. IL2CPP: pure reflection data, safe. Note Jint's NO-GO: do not pool the `object[] pars` buffer.

### Tier 2: structural, high payoff

**6. Locals stored by reference replacement; cells only for captured locals** (stack / value-repr). Jint: immutable
values in `Binding` slots, `ChangeValue` replaces the reference (`Binding.cs#L37-L126`); escape analysis decides
which environments closures can see (`JintFunctionDefinition.cs#L955-L975`). MoonSharp: mark each local
`SymbolRef` as captured at compile time (`Execution/Scopes/BuildTimeScope*.cs` already resolves upvalues when an
inner function references an outer local). For non-captured locals: `StoreLcl` does `LocalScope[i] = value`
(value must be read-only, see below) and `Local` pushes `LocalScope[i]` with no `AsReadOnly` clone. Captured locals
keep today's cell semantics (`Assign` + clone on read). All VM-produced values become read-only at creation
(constructor flag), so a CLR callback that calls `Assign` on an argument fails loudly instead of corrupting a
local. Touch: `Processor_InstructionLoop.cs` (`Local`, `StoreLcl`, `AssignLocal`, `ExecArgs`, `ExecIncr` for the
loop variable, `ExecClosure/GetUpvalueSymbol`), `Processor_Scope.cs`, `ClearBlockData`, debugger locals view
(`Processor_Debugger.cs` reads `LocalScope`), coroutine resume. Impact: removes the clone on every local read;
combined with 2 the `s = s + i % 7` loop drops from 4 to ~1 allocations; all workloads: est. 20-40%.
Risk: medium-high; correctness hinges on the captured-set analysis (`goto`/repeat-until scoping, `local function`
recursion). Mitigation: full Lua test suite, plus a debug-build assertion that a non-captured slot's value is never
written through `Assign`. Public API: `DynValue` values returned to hosts become read-only more often; document in
the changelog; `Clone()` gives a writable copy.

**7. Unboxed numeric lane in local slots plus fused local arithmetic** (value-repr / codegen). Jint: `UnboxedNumber`
flag and `_number` field, read/modify/write lanes that never box (`Binding.cs#L114-L149`,
`DeclarativeEnvironment.cs#L102-L200`, `JintUpdateExpression.cs#L74-L95`; PR #2499). MoonSharp: after idea 6,
make `LocalScope` a `struct LocalSlot { DynValue Ref; double Num; bool IsNum; }[]` for non-captured locals, and add
superinstructions emitted by a peephole pass: `AddLL a b -> c`, `AddLK a k -> c`, `IncrL a k`, `LtLK`/`LtLL`
+jump, `ModLK`. The handler reads doubles from slots (or `Number` of the `DynValue` when `!IsNum` and type is
Number), computes, and writes `Num` without allocating. A `Local` read of an unboxed slot materializes via idea 2's
cache, else `NewNumber`, and writes the reference back (Jint's write-back caching). Impact: tight numeric loops
(`empty_loop`, `int_math`, `fib` arithmetic, for-loop bodies) approach zero allocation; est. 2-4x on `int_math`.
Risk: high (new opcodes, dump format, debugger must materialize slots, metamethods `__add` on non-numbers must fall
back exactly). Effort L. Do it only after 6 lands and is measured.

**8. Call frames as structs; pooled local arrays for non-escaping functions** (calls / gc-pooling). Jint:
`readonly struct ExecutionContext` in `RefStack<T>` (`RefStack.cs#L11-L71`), env reuse for non-escaping functions
and `RecursiveEnvPool` for direct recursion (`ScriptFunction.cs#L307-L366`, `RecursiveEnvPool.cs#L17-L45`).
MoonSharp: turn `CallStackItem` into a struct held in a `FastStack<CallStackItem>` with a `ref` `Peek`
(`Execution/VM/CallStackItem.cs`, `DataStructs/FastStack.cs`, every `m_ExecutionStack.Peek().X = ...` site in
`Processor_*.cs`, coroutine and debugger code that snapshots frames — copy to a class only when a debugger asks).
For closures with no captured locals (from idea 6's analysis) rent `LocalScope` arrays from a per-`Closure` pool on
`BeginFn` and return them cleared on `Ret`, bounded to 16 like Jint; skip pooling for coroutine frames that suspend
(return only on normal `Ret`). Impact: removes `CallStackItem` (~100 B) and `DynValue[]` per call:
`function_call`, `method_call`, `fib`, `closure_create_call`, `coroutine_switch`: est. 15-30%. Risk: medium; error
unwinding (`Processor_Errors`/`PopToBasePointer`) must return pooled arrays or leak them (leaking is safe, just
loses reuse). Struct-in-array with `ref` returns is C# 7.0, fine for Unity 2021.3/IL2CPP and netstandard2.0.

**9. Table storage: ordered slim dictionary plus an array part** (tables). Jint: `StringDictionarySlim` insertion-
ordered entries array with tombstones and ref access (`StringDictionarySlim.cs#L17-L60`, `#L296-L338`), list-to-
dictionary hybrid (`HybridDictionary.cs#L8-L60`), dense array with sparse fallback (`ArrayInstance.cs#L55-L115`).
MoonSharp: replace `LinkedList<TablePair>` + `LinkedListIndex` in `DataTypes/Table.cs` with (a) a `DynValue[]`
array part for keys `1..n` (Lua 5.x semantics: `#t` is a border; rehash moves integer keys between parts) and (b)
one ordered hash part `Entry { DynValue Key; DynValue Value; int Next; int Hash; }[]` with buckets, tombstones on
removal, so `next(t, k)` is "find k's index, scan forward" and assigning `nil` during traversal stays legal (Lua's
rule). Keep string-keyed lookups fast by hashing `string` with the cached hash where possible. Impact: removes the
`LinkedListNode` + dictionary entry per element; `table_array_insert`/`read` est. 3-10x, `table_hash_set_get`
est. 1.5-3x, lower memory. Risk: high, touches iteration order (`pairs` order is unspecified in Lua but scripts and
tests may depend on MoonSharp's insertion order — the ordered hash part preserves it for the hash part; the array
part iterates first in index order, which differs from today for mixed tables). `Table.Pairs`, `Keys`, `Values`
public enumerations keep their signatures. MIT attribution if code is ported from `DictionarySlim`.

**10. Compiled typed accessors that produce DynValue, gated on JIT availability; source generator for AOT**
(interop / codegen). Jint: `CompiledMemberAccessor` JsValue lane, `CompiledMethodInvoker`, both gated on
`RuntimeFeature.IsDynamicCodeCompiled` (#2733, #2744); `[JsAccessible]` source generator with explicit
`RegisterAll()` (#3333). MoonSharp: in `PropertyMemberDescriptor`/`FieldMemberDescriptor`/`MethodMemberDescriptor`
`LazyOptimized` builds, emit `Func<object, DynValue>` for `int/float/double/bool/string` members so the getter calls
`DynValue.NewNumber((double)((T)o).P)` without boxing and without `ClrToScriptConversions.ObjectToDynValue`. Keep the
existing `IsRunningOnAOT` gate and add Jint's lesson: on Mono interpreter or when expression compilation would be
interpreted, stay on reflection. For IL2CPP, replace or complement `MoonSharp.Hardwire` with a Roslyn incremental
source generator (netstandard2.0 analyzer DLL; Unity 2021.3 supports source generators via the `RoslynAnalyzer`
asset label) that emits `HardwiredUserDataDescriptor` subclasses and an explicit `RegisterAll()`. Impact: property
get/set and method calls on userdata (`method_call` via CLR, Unity scripting): est. 1.5-3x on JIT; on IL2CPP the
generator removes reflection entirely. Risk: medium; generated code must honour `MoonSharpHidden`/visibility and
custom converters exactly as reflection descriptors do. Effort M (lane) + L (generator).

### Tier 3: useful, narrower

**11. Lock-free copy-on-write caches for type→converter lookups** (interop). Jint:
`DefaultObjectConverter._typeMappers` swapped with `Interlocked.CompareExchange`
(`DefaultObjectConverter.cs#L15-L75`). MoonSharp: `ClrToScriptConversions.ObjectToDynValue` and
`CustomConvertersCollection` lookups; replace per-call type-test chains with a `Dictionary<Type, Func<Script, object,
DynValue>>` populated on first sight. Impact: return-value conversion of CLR calls, est. 5-10% of interop call cost.
Effort S. Risk: low.

**12. Single-char and small-int string caches** (strings). Jint: `JsString._charToStringJsValue`,
`_intToStringJsValue[1024]` (`JsString.cs#L94-L136`, `#L442-L470`). MoonSharp: cache read-only `DynValue` strings
for ASCII chars (used by `string.sub(s,i,i)`, `string.char`, `gmatch` single chars) and for `tostring` of integers
0..1023. Impact: string-iteration scripts; `string_format` gains little (its cost is formatting). Effort S. Risk: low.

**13. Owned rope for `s = s .. x` loops** (strings). Jint: `ConcatenatedString` with ownership by the binding
(`JsString.cs#L737-L810`, `Binding.cs#L22-L33`, #4265). MoonSharp: needs a new internal string representation inside
`DynValue.m_Object` (a `StringBuilder`-backed rope flattened on `.String` access) and idea 6's slot ownership. Turns
quadratic concatenation linear. Effort L. Risk: high, every `m_Object as string` read must flatten. Defer.

**14. Prepared chunks shared across `Script` instances** (compiler). Jint: `Prepared<Script>` (`Engine.Ast.cs#L23-L58`;
per-engine state kept off the shared tree, #2568). MoonSharp: bytecode is appended to one per-`Script` `ByteCode`
list and function entry points are absolute indices, so sharing needs per-chunk instruction arrays with relative
addresses. Impact: load time for hosts that create many `Script`s from the same source (Unity entities); no effect on
steady-state benchmarks. Effort L. Risk: medium. Existing `Script.Dump`/undump already skips parsing.

**15. Allocation-budget regression tests and an allocation-by-type probe** (other). Jint:
`GC.GetAllocatedBytesForCurrentThread` budget assertions (`SlotLocationCacheTests.cs#L250-L267`) and
`AllocTypeProbe` (`Jint.Benchmark/AllocTypeProbe.cs`). MoonSharp: add `#if NET` tests in the .NET Core test runner
that run each flag_bench workload once warm and assert bytes/iteration below a budget (e.g. `empty_loop` 0 B after
idea 6, `function_call` < N B). This is what keeps ideas 1-8 from regressing silently. Effort S. Risk: none.

## 6. What not to copy

- Tree-walking handler nodes: MoonSharp's bytecode VM is the better base; take the *specializations*, emit them as
  superinstructions.
- `Unsafe.As` cast elision: small win, needs `System.Runtime.CompilerServices.Unsafe` on netstandard2.0; sealed types
  and enum tests get most of it.
- Hidden-class shapes: JS-specific; for Lua an instruction-level cache of `(Table, version, entry index)` on top of
  idea 9 is the analogue, worth revisiting only after 9.
- `ArrayPool` for reflection parameter buffers: Jint measured it slower.
- Expression-compiled delegates on runtimes that interpret them: slower than reflection (Jint's gate).

## 7. Suggested order

1 → 2 → 3 → 15 (budget tests) → 5 → 4 → 11/12 → 6 → 8 → 9 → 10 → 7 → 13/14.
Ideas 1-5 are independent and low-risk; 6 is the hinge that makes 7 and 8 safe; 9 is independent and the largest
single win for table workloads.
