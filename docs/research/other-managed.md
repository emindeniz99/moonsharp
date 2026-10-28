# Other managed Lua engines and MoonSharp forks: code-level survey

Date: 2026-10-28. Analyst scope: WattleScript, SolarSharp, UniLua, NeoLua, KopiLua, AsyncLua
(Lua-CSharp, moonsharp-unityenhanced, xLua, Luau and Jint are covered by sibling reports).
Baseline compared against: MoonSharp `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` (upstream master).

Evidence marks: **[C]** confirmed by reading the source at the pinned SHA, **[M]** measured by a
third party (data file downloaded and parsed here), **[S]** secondary (docs/commit message only),
**[U]** unconfirmed / inferred.

## 0. Method and limits

| Repo | Pinned SHA | Licence (file, SPDX) |
|---|---|---|
| WattleScript/wattlescript | `8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3` (2026-08-04, full history fetched, 656 commits) | `LICENSE`, BSD-3-Clause (+ KopiLua MIT notice) |
| BraedonWooding/solarsharp | `e10a939fdc29e70b2dfef6f37faa8d9e9269c01a` (2025-11-30) | `LICENSE`: own part says only "Copyright (c) 2024-2025, Braedon Wooding. All rights reserved" (no grant text; README claims BSD-3) + MoonSharp BSD-3 + LuaJIT/Lua MIT. **Licence of SolarSharp's own code is ambiguous [C]** |
| xebecnan/UniLua | `194eb311191111bfdbc77070de67c100235dc618` (2022-06-27) | `LICENSE`, MIT |
| neolithos/neolua | `1840895f19d7d1ebaf88df58e5f75744258a3144` (2026-03-27) | `LICENSE.md`, Apache-2.0 |
| NLua/KopiLua | `90496c7ea02239528210ab6580ea40df37f21965` (2021-01-18) | `COPYRIGHT`, MIT (KopiLua + Lua) |
| RomeCore/AsyncLua | `78c95db46d78a02b235caa8a14b6ed8a2554ae0b` (2026-10-19) | `LICENSE`, MIT |

Limits, stated plainly:

- **No same-machine A/B was run.** I wrote a harness that compiles MoonSharp `fbd0367` sources and
  WattleScript sources with my own csproj and runs the xLua-NG flag_bench workloads plus four CLR
  interop cases (`scratchpad/bench-other/Program.cs`, `ms/ms.csproj`, `ws/ws.csproj`), but the
  permission classifier denied building/running external code. The harness is ready if the owner
  approves running it. All WattleScript speed claims below are therefore mechanism-based estimates.
- The only cross-engine measurements are SolarSharp's own CI BenchmarkDotNet series
  (`https://braedonwooding.github.io/solarsharp/dev/bench/data.js`, last entry commit `e10a939`,
  2025-11-30), downloaded and parsed here. Its "MoonSharp" is the **NuGet MoonSharp 2.0.0**
  package (`Benchmark/Benchmark.csproj` at e10a939), not upstream master, and runs on GitHub-hosted
  runners (noisy).

## 1. Summary matrix

| Engine | Value repr | VM | Table | Call frames | CLR interop | AOT/IL2CPP |
|---|---|---|---|---|---|---|
| MoonSharp fbd0367 | `sealed class DynValue` (~48 B: refid, hash cache, readonly flag, double, object, type) | stack VM, `Instruction` is a **class** with `DynValue Value`, `SymbolRef` fields; one shared code array | `LinkedList<TablePair>` + 3 `LinkedListIndex` maps, no array part | `class CallStackItem` per call + `new DynValue[n]` LocalScope; each local is a mutable heap cell (`Assign`) | reflection descriptors, expression-tree compile when not AOT, Hardwire offline generator | yes |
| WattleScript | **16-byte struct** `DynValue` (double/ulong overlay + object; number tagged by sentinel object) | stack VM, **8-byte struct `Instruction`**, per-function `FunctionProto` with constant pools | array part + `Dictionary<DynValue, LinkedListNode<TablePair>>` + LinkedList | **struct `CallStackItem`** in `FastStack`, locals live **on the value stack**, Lua-style open/closed `Upvalue` | MoonSharp descriptors + descriptor cache + **incremental Roslyn source generator** | yes, no unsafe |
| SolarSharp | still `sealed class LuaValue` (struct planned in README) | MoonSharp stack VM, struct callstack | 1-based `LuaValue?[]` array segment (up to 16M) + forked .NET `Dictionary` (`LuaDictionary`) | struct `CallStackItem` | MoonSharp-derived | netstandard2.0/2.1; uses `System.Runtime.CompilerServices.Unsafe` |
| UniLua | 32-byte `struct TValue` inside `class StkId` slots (pre-allocated boxes) | **register VM**, port of Lua 5.2 lvm.c | port of ltable.c (array part + chained hash, `computesizes`) | pooled `CallInfo` objects, reused | C-API style (`ILuaState`), no reflection binder | yes |
| NeoLua | `object` (numbers boxed unless statically typed) | **compiles to DLR expression trees**, `LambdaExpression.Compile()` | custom open hash `LuaTableEntry[]` + `int[]` buckets + zero-based `object[]` array | CLR stack (compiled delegates) | DLR `CallSite`/binders, `IDynamicMetaObjectProvider` | **no** (see §5) |
| KopiLua | `class Value { object p; }` - every number boxed | register VM, line-by-line C port | ltable.c port | C port with pointer emulation classes | none | yes but slow |
| AsyncLua | abstract `class LuaValue` hierarchy, `LuaNumber` is a class | register VM (Lua 5.5 syntax) | class-based | struct `CallStackFrame` | reflection metatable generator | not stated |

## 2. WattleScript (the most relevant source)

WattleScript is a MoonSharp fork. `git merge-base` with upstream MoonSharp master is
`84eff75f0569edbe063de995973b940416e51478` (2019-02-08); MoonSharp has 75 commits since then that
WattleScript does not have (mostly fixes: `__call` in pcall, label scoping, tonumber, etc.). The core
VM files in MoonSharp have barely changed since 2019, so WattleScript's VM diffs still apply
conceptually to MoonSharp master [C].

### 2.1 Performance commits, in order

| Commit | Date | What | Files |
|---|---|---|---|
| [abbf807](https://github.com/WattleScript/wattlescript/commit/abbf807d0aa013a1cfc2292b7be02d23a04886b1) | 2022-03-11 | DynValue and Instruction become structs | 76 files, +570/-664 |
| [023e8f5](https://github.com/WattleScript/wattlescript/commit/023e8f57fa87fcb16ab77dfada3ad39933d481de) | 2022-03-11 | slight table-set optimisation | 1 |
| [2127ac9](https://github.com/WattleScript/wattlescript/commit/2127ac982e63d64e97569268290550dcbeea0f42) | 2022-03-12 | `ref`-based `ScalarReference`, `TryCastToNumber`, faster arithmetic fast paths | 11 |
| [1daaff1](https://github.com/WattleScript/wattlescript/commit/1daaff1215ffaf45e90b7d5d84ebbe7b20cb523d) | 2022-03-13 | closure capture redesigned as open/closed `Upvalue` (Lua's design) | 5 |
| [2342527](https://github.com/WattleScript/wattlescript/commit/2342527a3d45e5f4ace7fa403b60a977f57e2ef3) | 2022-03-24 | FastStack initial capacity + growth (MoonSharp fork already did this as M3) | 5 |
| [cf817bf](https://github.com/WattleScript/wattlescript/commit/cf817bf18c7699f32b7ed5b5e198045987b86211) | 2022-03-26 | fix double boxing of `Script.Call` | 2 |
| [2b55309](https://github.com/WattleScript/wattlescript/commit/2b55309bff10115441e28be8384a8459c594d22a) | 2022-03-28 | locals stored on the value stack (no per-call `DynValue[]`) | 6 |
| [6ce39e6](https://github.com/WattleScript/wattlescript/commit/6ce39e65e1bdbcbfa446ea93e70e6866a3a4267e) (PR #5) | 2022-03-31 | `CallStackItem` class -> struct | 6 |
| [f366e87](https://github.com/WattleScript/wattlescript/commit/f366e870529dc309b1c8224424e4d9c5bd8e19dd) | 2022-04-09 | DynValue tag packed into high bits: 24 -> 16 bytes | 1 |
| [3811213](https://github.com/WattleScript/wattlescript/commit/3811213069bbedb38263068228f2826d9110338d) (PR #23) | 2022-04-14 | flat 8-byte instructions, per-function `FunctionProto` with string/number constant pools | 54 |
| [8371631](https://github.com/WattleScript/wattlescript/commit/8371631c7d3238b1f0eb23f534f0d07bb84c28e8) | 2022-05-17 | Less/LessEq fold the following NOT | 5 |
| [88c951c](https://github.com/WattleScript/wattlescript/commit/88c951c0c838f747f465bd505170d67eae000f63) (PR #59) | 2022-10-09 | table = array part + single map (was 3 LinkedListIndex) | 7, -503 lines |
| [34e859c](https://github.com/WattleScript/wattlescript/commit/34e859cd1737376f9c29f443dedba30639cec954) | 2023-08-18 | cache `GetDescriptorForType` results | 3 |
| [bbe9fec](https://github.com/WattleScript/wattlescript/commit/bbe9fece81ceb104b1b2ae1e17d3e2a485f458cb) (PR #64) | 2023-09-24 | `ScriptExecutionContext` class -> struct | 2 |
| [b9a4024](https://github.com/WattleScript/wattlescript/commit/b9a4024ae36ccf4fd7eafb9c3a0222d5d215f8ea), [73a7fc0](https://github.com/WattleScript/wattlescript/commit/73a7fc0a72ae5151e741927dc7a7f0e1d3961e11) | 2023-09 / 2025-02 | typed `Equals(DynValue)`, `IEquatable<DynValue>` (removes boxing in `Dictionary<DynValue,...>`) | 1 each |
| [254bdc1](https://github.com/WattleScript/wattlescript/commit/254bdc1743791896aed5e2f062e53efee35bd5be) | 2025-03-25 | Hardwire as incremental Roslyn source generator | 418 |
| [e9a007a](https://github.com/WattleScript/wattlescript/commit/e9a007ab3954462f7e3aaba568ab6c24285cf7df) | 2026-04-07 | cache `Type.IsByRef` in `ParameterDescriptor` | 11 |
| [0ebe8d0](https://github.com/WattleScript/wattlescript/commit/0ebe8d07acb47d3fa58709751a54609acbd32046) | 2026-04-07 | generic `HardwiredMemberDescriptor<T>` | 4 |
| [8feb3c6](https://github.com/WattleScript/wattlescript/commit/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3) | 2026-08-04 | re-fetch `ref currentFrame` only after frame-changing opcodes | 1 |

No benchmark project or published numbers exist in the repo (grep for "benchmark" finds nothing) [C].
README claims "Minimal garbage generation at runtime" [S].

### 2.2 Value representation [C]

[`DynValue.cs#L16-L48`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/DataTypes/DynValue.cs#L16-L48):

```csharp
[StructLayout(LayoutKind.Explicit)]
public struct DynValue : IEquatable<DynValue>
{
    private static readonly object m_NumberTag = new object();
    [FieldOffset(0)] private double m_Number;
    [FieldOffset(0)] private ulong m_U64;
    [FieldOffset(8)] private object m_Object;
    ...
    public DataType Type { get {
        if (m_Object == m_NumberTag) return DataType.Number;
        return (DataType) ((m_U64 >> 40) & 0xFF); } }
```

- 16 bytes. A number is `{double, sentinel}`, so all 2^64 double bit patterns stay valid (no NaN
  canonicalisation); the type test for numbers is one reference compare.
- Non-numbers keep `QNAN | type << 40` in the 8-byte slot, booleans use the low 16 bits, bit 24 is a
  `FromMetatable` flag used by WattleScript's implicit-`this` calls.
- The overlay is only `double`/`ulong` at offset 0 and an object reference at offset 8: no
  reference overlaps a value field, so the type is verifiable and loads fine under IL2CPP and Mono.
  It is **not** unsafe code. A safe alternative to the overlay is `BitConverter.DoubleToInt64Bits`.
- `default(DynValue)` is `Nil` (`DataType.Nil == 0`), so arrays of DynValue are nil-initialised.
- Equality and hashing: [`#L571-L654`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/DataTypes/DynValue.cs#L571-L654).
  The first struct commit returned `false` for equal userdata/threads (`default: return false`) and
  needed later fixes (b9a4024). Lesson: the struct switch needs explicit equality tests per type.

Public API removed by the struct switch ([abbf807 DynValue.cs diff](https://github.com/WattleScript/wattlescript/commit/abbf807d0aa013a1cfc2292b7be02d23a04886b1)):
`ReferenceID`, `ReadOnly`, `AsReadOnly()`, `Clone()`, `CloneAsWritable()`, `Assign()`, `NewNil()`;
`null` checks against DynValue no longer compile; `Table.RawGet` can no longer return `null` for a
missing key (MoonSharp `Table.cs` returns `null` there [C]); the debugger's `(val #id)` uses
`DynValue.ReferenceID` (`MoonSharp.VsCodeDebugger/DebuggerLogic/VariableInspector.cs:16` [C]);
`debug.upvalueid` uses it too (`CoreLib/DebugModule.cs:147`).

### 2.3 Instructions and prototypes [C]

[`Instruction.cs#L12-L73`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/Instruction.cs#L12-L73):
one `ulong`: 7-bit opcode, 32-bit `NumVal`, 13-bit signed `NumVal2`, 12-bit `NumVal3`. Strings,
numbers and nested protos move into
[`FunctionProto`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/FunctionProto.cs#L16-L52)
(`strings[]`, `numbers[]`, `functions[]`, `code[]`, `sourceRefs[]` parallel to code).
MoonSharp's `Instruction` is a class with `OpCode, Symbol, SymbolList, Name, Value (DynValue), NumVal,
NumVal2, SourceCodeRef` (`Execution/VM/Instruction.cs:9-18` at fbd0367) [C]: each dispatch loads a
heap object; constants are DynValues hanging off instructions. Struct instructions give dense,
cache-friendly code arrays and let `IndexN` carry a string-pool index instead of a DynValue.

### 2.4 Stack, frames, locals, upvalues [C]

- Frames: [`CallStackItem.cs#L6-L24`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/CallStackItem.cs#L6-L24)
  is a struct in a `FastStack<CallStackItem>` whose indexer returns `ref T`
  ([`FastStack.cs#L27`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/DataStructs/FastStack.cs#L27)).
  The loop keeps `ref var currentFrame = ref m_ExecutionStack.Peek();` and jumps back to
  `new_frame:` after any opcode that can push/pop frames or grow the stack
  ([`Processor_InstructionLoop.cs#L37-L66`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L37-L66)).
  Hazard: a `ref` into `FastStack` storage is invalidated by `Array.Resize`; every opcode that can
  grow either stack must `goto new_frame` (that is exactly what 8feb3c6 encodes).
- Locals: `Call` does `BasePointer = m_ValueStack.Reserve(localCount)`
  ([`#L939-L955`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L939-L955)),
  `Local` is `m_ValueStack.Push(m_ValueStack[BasePointer + i.NumVal])` (`#L349-L351`),
  `StoreLcl` writes the slot (`#L534-L537`). No allocation per call or per local read/write.
- Upvalues: [`Upvalue.cs`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Execution/Scopes/Upvalue.cs#L9-L44)
  is Lua's design: an open upvalue holds `(FastStack, index)`, `Close()` copies into private storage,
  `Value()` returns `ref DynValue`. Open upvalues are tracked per frame (`OpenClosures`), shared by
  index lookup in `GetUpvalueSymbol` (`#L579-L600`), and closed by the `CloseUp` opcode at block exit
  (`#L191-L202`) and when the frame is popped (`PopToBasePointer`, `#L766-L774`). This preserves
  per-iteration fresh locals for closures created in loops.

MoonSharp today, for comparison [C]:
- `case OpCode.Local: m_ValueStack.Push(scope[index].AsReadOnly());`
  (`Processor_InstructionLoop.cs:198-202`): **every read of a writable local clones a DynValue**
  (`AsReadOnly()` -> `Clone(true)`, `DynValue.cs:379-386`). The same for `Upvalue` (`:203-205`).
- `AssignLocal` mutates the cell in place with `v.Assign(value)` (`:354-363`), because closures capture
  the cell object itself (`GetUpvalueSymbol` returns `LocalScope[i]`, `:422-430`).
- So the ~230 B/iteration of `s = s + i % 7` is roughly: clone `s`, clone `i`, result of `%`, result of
  `+`, plus the loop-counter DynValue. The struct design removes all of them.

### 2.5 Table [C]

[`Table.cs#L13-L338`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/DataTypes/Table.cs#L13-L338):
`DynValue[] arrayPart` (0-based storage indexed by the Lua key itself; slot 0 is index 0) plus
`Dictionary<DynValue, LinkedListNode<TablePair>> valueMap` + `LinkedList<TablePair> valueList` for
ordered `next`. An integer key `ik` goes to the array if `ik <= arrayPart.Length + 5`
(`ARRAY_PART_THRESHOLD`), growth doubles and migrates map entries (`MapToArray`).
Weaknesses: still one `LinkedListNode` allocation per hash entry; `CollectDeadKeys` is still O(n)
and still triggered on re-insert after a nil (`#L328-L337`), which is MoonSharp's O(n^2) pattern
(see §3.3). `IEquatable<DynValue>` (73a7fc0) matters: without it `Dictionary<DynValue,...>` uses
`ObjectEqualityComparer` and boxes the struct key on every lookup.

### 2.6 Interop [C]

- Descriptor cache, [`TypeDescriptorRegistry.cs#L188-L263`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/Interop/UserDataRegistries/TypeDescriptorRegistry.cs#L188-L263).
  MoonSharp's `GetDescriptorForType` (`TypeDescriptorRegistry.cs:238` at fbd0367) walks base types,
  calls `GetInterfaces()` (array allocation), builds a `List<IUserDataDescriptor>` and possibly a
  `CompositeUserDataDescriptor` **every time** a CLR object of an unregistered/derived type is
  converted to userdata. WattleScript caches per `(Type, searchInterfaces)` and clears on
  registration changes. **Bug to avoid when porting:** setting `UserData.RegistrationPolicy`
  (`#L409`) does not clear the cache, so a type cached as "no descriptor" under the default policy
  stays unresolved after switching to `Automatic`.
- `IsByRef` cache: `ParameterDescriptor.TypeByRef` (e9a007a). MoonSharp calls `desiredType.IsByRef`
  inside `DynValueToObjectOfType` / `...Weight` on every argument of every overload score
  (`Interop/Converters/ScriptToClrConversions.cs:82,206` [C]). Cheap, safe, measurable on overloads.
- `ScriptExecutionContext` struct (bbe9fec): removes one allocation per CLR call. Public type changes
  from class to struct; callbacks still compile, `null` checks and reference identity do not.
- Source generator: `src/WattleScript.HardwireGen/HardwireSourceGenerator.cs` (IIncrementalGenerator,
  netstandard2.0, Roslyn 4.4). Generates one sealed descriptor class per member;
  [`HardwireInterop.cs#L34-L140`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.HardwireGen/HardwireInterop.cs#L34-L140).
  Methods still go through `object[] pars` and `(object)` return (boxing), fields/properties go
  through `HardwiredMemberDescriptor<T>` with `DynValueToObject<T>`. AOT-safe; removes reflection but
  not boxing.
- Still allocating per CLR call in WattleScript: `Slice<DynValue>` (class) args, `CallbackArguments`
  (class), a `CallStackItem` copy, `object[]` for the reflection/hardwire invoke.

### 2.7 String library [C]

- Pattern matching was rewritten to translate Lua patterns into .NET `Regex`
  ([bffd4ab](https://github.com/WattleScript/wattlescript/commit/bffd4abfd16c1e7fe0d034d6c31f36ce3e0e1c69),
  [`PatternRegex.cs`](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/src/WattleScript.Interpreter/CoreLib/StringLib/PatternRegex.cs#L19-L272)).
  **Do not port**: `%w -> \w` also matches `_` and all Unicode letters/digits (Lua `%w` is C
  `isalnum`), `%a -> \p{L}`, `%d -> \d` (Unicode digits); a new `Regex` is built on every call with no
  cache (`StringModule.cs:178,226,268,376`). Semantics drift and per-call regex parsing.
- `string.format` moved to a regex-driven spec parser
  ([6543b8b](https://github.com/WattleScript/wattlescript/commit/6543b8b4308cdf247dd6b5d2fd124f2f871f4f5b)).
  Unmeasured; not obviously faster than a hand-written scanner.

### 2.8 What is safe, tested and still applicable to MoonSharp master

WattleScript keeps the MoonSharp TestMore suite and end-to-end tests
(`src/WattleScript.Interpreter.Tests/TestMore`, 4 commented-out `[Ignore]`s) [C].

| Change | Applicable to master? | Notes |
|---|---|---|
| Struct DynValue (abbf807 + f366e87 + b9a4024 + 73a7fc0) | yes, as the end state | biggest win; public API break (see §2.2); needs the locals/upvalue redesign at the same time |
| Locals on the value stack + Lua-style upvalues (2b55309, 1daaff1, CloseUp) | yes; can be done **before** the struct switch | removes LocalScope arrays and the per-read clone |
| Struct CallStackItem + ref currentFrame (6ce39e6, 8feb3c6) | yes | internal only; watch `ref` invalidation on stack growth |
| Struct Instruction + FunctionProto constant pools (3811213) | yes | internal; breaks binary dump format (bump dump version) |
| Descriptor cache (34e859c) | yes, tiny | add invalidation on `RegistrationPolicy` set |
| `IsByRef` cache (e9a007a) | yes, tiny | internal |
| ScriptExecutionContext struct (bbe9fec) | maybe | public type kind change; alternative: pool/reuse one context per Processor |
| Table array part (88c951c) | partly | better done with a Lua-style array part + index-chained hash (see §3, §4) |
| Regex patterns (bffd4ab) | **no** | semantic drift |
| Source generator (254bdc1) | yes, as a separate project | replaces offline Hardwire step; AOT-safe |

## 3. SolarSharp (MoonSharp fork, 2024-2025)

Fork of MoonSharp (history includes MoonSharp to 2021-07 coroutine recycling, then
`cafa6be` 2024-08-28 "Simplify project & rename"). `LuaValue` is **still a sealed class**
(`SolarSharp.Interpreter/DataTypes/LuaValue.cs:15` [C]); its README describes a planned 16-byte
struct (NaN boxing or static tag objects) and span-based calls [S].

### 3.1 Measured, third party [M]

SolarSharp CI, BenchmarkDotNet, last entry `e10a939` (2025-11-30), mean ms. "MoonSharp" is NuGet 2.0.0.
Scripts in `Benchmark/Tests` and `Benchmark/Tests/specific_features` (10,000-element loops for the
table cases).

| Script | MoonSharp 2.0.0 | SolarSharp | Lua-CSharp 0.4.2 | NeoLua | NLua/KeraLua (native) |
|---|---:|---:|---:|---:|---:|
| ack.lua | 1279.7 | 924.7 | 282.5 | 184.1 | 185.4 / 183.9 |
| heapsort.lua | 655.5 | 381.3 | 110.3 | 143.6 | 79.5 |
| mandel.lua | 12470 | 10800 | 5040 | - | 6053 |
| queen.lua | 48.8 | 32.7 | 10.3 | 11.5 | 9.0 |
| sieve.lua | 769.4 | 469.4 | - | 200.3 | 103.6 |
| binarytrees | 11.5 | 8.8 | 5.1 | - | 1.6 |
| interop_25k_calls (`x = add(x,1)`, CLR delegate) | 12.89 | 11.77 | 2.64 | 2.45 | 28.35 |
| table_array_insert_indexed (`table.insert(t,i)` x10k) | **563.9** | 3.2 | 84.0 | 2.0 | 2.0 |
| table_array_insert_remove_end | **1131.9** | 6.0 | 219.4 | 3.2 | 4.5 |
| table_array_remove_then_add_immediate (`t[i]=nil; t[i]=i`) | **522.5** | 3.1 | 2.5 | 43.6 | 0.9 |
| table_map_remove_then_add_immediate (string keys) | **498.6** | 14.1 | 12.4 | 15.1 | 11.8 |
| table_map_insert | 12.8 | 5.1 | 12.2 | 4.1 | 5.3 |
| table_map_pairs | 22.1 | 12.5 | 10.4 | 6.0 | 6.7 |
| Startup (new Script) | 1.19 | 1.11 | 0.04 | 0.02 | 0.25 |

Read-outs:
- The general VM gains of SolarSharp without a struct value are modest (ack -28%, heapsort -42%).
- The four bold rows are **algorithmic** (O(n^2)) problems in MoonSharp's table, not constant-factor
  ones. Cause in MoonSharp master [C]: `Table.PerformTableSet` calls `CollectDeadKeys()` (a full walk
  of `m_Values`) whenever a key that had been set to nil is set again (`DataTypes/Table.cs:167-170`),
  and `CollectDeadKeys` also resets `m_CachedLength = -1` (`:537-549`), after which `Length` rescans
  from 1 (`:611-625`), and `table.insert` needs `Length` each call. These need to be re-measured on
  MoonSharp master (the M3 fork state), but the code paths are unchanged since 2.0.0.
- Startup: MoonSharp `new Script()` costs ~1.2 ms vs 0.02-0.25 ms for others; SolarSharp's
  [36cb3f1](https://github.com/BraedonWooding/solarsharp/commit/36cb3f1b03c8d9f668058110be29fc531d7e53cb)
  attributes part of it to building `Regex` objects at startup [S].

### 3.2 Code worth reading [C]

- Table: [`Table.cs#L14-L45`, `#L387-L463`](https://github.com/BraedonWooding/solarsharp/blob/e10a939fdc29e70b2dfef6f37faa8d9e9269c01a/SolarSharp.Interpreter/DataTypes/Table.cs#L387-L463):
  array segment `LuaValue?[]` indexed directly by the Lua key, cached length maintained on append,
  `ArrayRemoveAt` uses `Array.Copy`. **Defect:** any integer key `0 <= k < 16_000_000` goes to the
  array (`MAX_INT_KEY_ARRAY`, `#L16`, `#L451`), so `t[15000000] = 1` allocates a 16M-slot array
  (128 MB on 64-bit). Do not copy that rule; use Lua's `computesizes` (>50% density) instead.
- `LuaDictionary<TKey,TValue>`: a fork of .NET `Dictionary` (MIT, dotnet/runtime) with
  `TryGetEnumeratorFrom(key)` for O(1) `next`
  (`DataTypes/Custom/LuaDictionary.cs`, 1755 lines). Uses `Unsafe.NullRef/IsNullRef` (see §8).
- Pattern matching: [`Regex/LuaRegex.cs`](https://github.com/BraedonWooding/solarsharp/blob/e10a939fdc29e70b2dfef6f37faa8d9e9269c01a/SolarSharp.Interpreter/Regex/LuaRegex.cs#L1-L30)
  (789 lines) is a direct port of the Lua/LuaJIT matcher over `string` indices, replacing KopiLua's
  `CharPtr` code ([6c83605](https://github.com/BraedonWooding/solarsharp/commit/6c83605), "Goodbye
  KopiLua"). This is the right direction; write MoonSharp's own port from Lua's `lstrlib.c` (MIT) to
  avoid SolarSharp's licence ambiguity.
- Removed features that MoonSharp must keep: AutoYield
  ([5cd7316](https://github.com/BraedonWooding/solarsharp/commit/5cd73168c295e4685741659ac02f9100ef472b7c)),
  readonly values ([276f572](https://github.com/BraedonWooding/solarsharp/commit/276f5729e9ddbf5b32a380b2b024c5cffc4f9138)),
  script-ownership checks ([9e808a2](https://github.com/BraedonWooding/solarsharp/commit/9e808a279cfc4527535d2658768bfc2ebe7985a5)).

## 4. UniLua (managed Lua 5.2 port, Unity) [C]

- Value: [`LuaObject.cs#L10-L210`](https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618/Assets/UniLua/LuaObject.cs#L10-L210)
  `struct TValue { int Tt; double NValue; ulong UInt64Value; object OValue; }` (32 bytes with
  padding). Wider than needed, but all setters write in place (`SetNValue`, `SetObj(ref TValue)`).
- Stack slots: [`StkId`](https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618/Assets/UniLua/LuaObject.cs#L212-L238)
  is a **class wrapping one TValue**, pre-allocated for the whole stack, emulating C pointers
  (`StkId.inc`). Arithmetic writes `ra.V.SetNValue(...)` into an existing box: zero allocation
  without needing struct semantics everywhere. This is an incremental path for MoonSharp: a
  stack of pre-allocated mutable cells. With C# 7 `ref` returns a plain `TValue[]` is better.
- VM: [`VM.cs#L61-L285`](https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618/Assets/UniLua/VM.cs#L61-L285)
  is a register VM (lvm.c port). `OP_ADD` is two type tests and one store. RK operands avoid a
  push/pop pair per operand that MoonSharp's stack machine pays.
- Frames: [`Do.cs#L109-L179`](https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618/Assets/UniLua/Do.cs#L109-L179),
  `ExtendCI()` (`#L220`) reuses a growable array of `CallInfo` objects (`LuaState.cs#L79`): no
  allocation per call, C# functions take the state and read arguments from the stack (no args list).
- Table: [`LuaTable.cs`](https://github.com/xebecnan/UniLua/blob/194eb311191111bfdbc77070de67c100235dc618/Assets/UniLua/LuaTable.cs#L11-L330)
  ports ltable.c: `StkId[] ArrayPart`, chained `HNode[] HashPart`, `Rehash`/`ComputeSizes`
  (`#L463-L495`) choose the array size by the >50% density rule. Anti-patterns: a **finalizer**
  `~LuaTable()` recycles nodes into a global locked free list (`#L19-L22`, `#L258-L310`); every table
  goes through the finalization queue. `Nums` is a static array shared by all states (`#L483`), not
  thread-safe.
- No CLR reflection binder: interop is the C API (`ILuaAPI`).
- Unsafe: none.

## 5. NeoLua (DLR compiler) [C]

- Compiles a chunk to a `LambdaExpression` and calls `expr.Compile()`
  ([`Lua.cs#L304-L307`, `#L335`](https://github.com/neolithos/neolua/blob/1840895f19d7d1ebaf88df58e5f75744258a3144/NeoLua/Lua.cs#L304-L307));
  operators are `DynamicExpression.Dynamic(binder, ...)` (`LuaEmit.cs:651, 894, 938`); tables are
  `IDynamicMetaObjectProvider` (`LuaTable.cs:87`, `:1625`).
- Why it is not AOT/IL2CPP-safe:
  1. `LambdaExpression.Compile()` needs Reflection.Emit. Without it, .NET falls back to the
     `System.Linq.Expressions` interpreter where available [S]; Unity IL2CPP has no Reflection.Emit,
     so NeoLua either fails or runs through that interpreter, losing its whole design point [U for
     the exact Unity behaviour].
  2. DLR `CallSite<T>` rules are compiled delegates; the call path builds new generic delegate types
     at run time: `CallSite.Create(Expression.GetFuncType(signature), binder)` with a signature array
     sized by argument count, then `dlg.DynamicInvoke(args)`
     ([`Lua.Runtime.cs#L1261-L1284`](https://github.com/neolithos/neolua/blob/1840895f19d7d1ebaf88df58e5f75744258a3144/NeoLua/Lua.Runtime.cs#L1261-L1284)).
     Full AOT cannot instantiate generic types it did not see at build time.
  3. The debug path uses `AppDomain.DefineDynamicAssembly` (`LuaStackTraceDebugger.cs:402`).
  This is the same reason MoonSharp guards its expression-tree member access with `IsRunningOnAOT`.
- Value: `object`, so numbers box unless the compiler proves a static type.
- Table: [`LuaTable.cs#L1007-L1029`, `#L1531-L1545`](https://github.com/neolithos/neolua/blob/1840895f19d7d1ebaf88df58e5f75744258a3144/NeoLua/LuaTable.cs#L1007-L1029):
  `struct LuaTableEntry { int hashCode; object key; object value; bool isMethod; int nextHash; }`,
  `int[] hashLists` buckets, free-list in the entries array, zero-based `object[] arrayList` with an
  `arrayLength` border and migration of trailing hash keys into the array (`SetArrayValue`,
  `#L2052-L2100`). This entries-array design (same as .NET `Dictionary` internals) gives O(1) `next`
  by entry index and no per-entry node allocation: a good model for MoonSharp's hash part.
- Measured [M]: very fast on JIT (ack 184 ms vs MoonSharp 1280 ms), table_array_remove_then_add_immediate 43.6 ms.

## 6. KopiLua [C]

Line-by-line port of Lua 5.1 C. `Value` is a class holding `object p`; `n` setter boxes every number
([`lobject.cs#L67-L95`](https://github.com/NLua/KopiLua/blob/90496c7ea02239528210ab6580ea40df37f21965/KopiLua/src/lobject.cs#L67-L95)),
stack slots are `LuaTypeValue` objects with operator overloads emulating pointer arithmetic
(`#L104-L190`). Nothing to adopt for speed. Relevance: MoonSharp's string library
(`CoreLib/StringLib/KopiLua_StrLib.cs`, `Interop/LuaStateInterop/*`) is KopiLua code with `CharPtr`
allocations; both WattleScript and SolarSharp replaced it. That is a likely cause of MoonSharp's
`string_format` (4421 ns vs 305 ns native) and `string_pattern` costs [U, needs a profile].

## 7. AsyncLua [C]

Lua 5.5 syntax, register VM, `struct CallStackFrame`, `readonly struct Instruction`, but values are
an abstract class hierarchy (`Values/LuaValue.cs:9`, `LuaNumber` is a sealed class). Allocation per
number like MoonSharp. MIT. No unsafe. Interesting only for its async/await integration, not speed.

## 8. Unsafe inventory

| Engine | Use | What it buys | Safe alternative |
|---|---|---|---|
| WattleScript | none (`[StructLayout(Explicit)]` overlays only `double`/`ulong`) | 16-byte value without `BitConverter` calls | `BitConverter.DoubleToInt64Bits`/`Int64BitsToDouble` (JIT intrinsics), or keep `double` + separate tag byte (24 bytes) |
| SolarSharp | `Unsafe.NullRef/IsNullRef` in `LuaDictionary.cs` (10 sites), `Unsafe.ReadUnaligned/As` in `ScriptRuntimeException.cs:563`, `MemoryMarshal.GetReference` in `ValueStringBuilder.cs:66,80` | ref-returning dictionary lookup without a found flag; double bit reads; span builder | dictionary `FindEntry` returning an `int` index (-1 = missing) and indexing `entries[i]` by ref; `BitConverter.DoubleToInt64Bits`; `StringBuilder` cached per Processor |
| UniLua, NeoLua, KopiLua, AsyncLua | none found by grep for `unsafe`, `Unsafe.`, `MemoryMarshal`, `stackalloc` | - | - |

## 9. Ideas for MoonSharp, ranked

Workload numbers refer to the fork's measured ns/iteration (MoonSharp vs native Lua 5.5.1).

1. **Fix the O(n^2) table paths** (`CollectDeadKeys` on re-insert, length cache reset, `table.insert`
   Length rescan). Map: `DataTypes/Table.cs` (`PerformTableSet`, `CollectDeadKeys`, `Length`),
   `CoreLib/TableModule.cs`. Approach: never compact on the set path (tombstones stay until a rehash
   that is triggered by growth, as Lua does), maintain the border incrementally. Impact: SolarSharp
   data shows 150-500x on the bold rows; neutral on flag_bench rows. Small effort, low risk, no API change.
2. **Locals by value on the value stack + Lua-style open/closed upvalues** (WattleScript 2b55309,
   1daaff1, `CloseUp`). Map: `Execution/VM/CallStackItem.cs` (drop `LocalScope`, add `OpenUpvalues`),
   `Processor_InstructionLoop.cs` (`Local`, `StoreLcl`, `Upvalue`, `StoreUpv`, `ExecClosure`,
   `ExecArgs`, `ExecRet`, `ExecBeginFn`), `Processor_Scope.cs`, `Execution/Scopes/ClosureContext.cs`
   (list of `Upvalue` cells), new `Upvalue` class, `Tree/Statements/*` to emit `CloseUp` at block
   exit, debugger `Processor_Debugger.cs:358-368`. Works with class DynValue too: if locals hold
   immutable DynValues and `Assign` is gone from the local path, `Local` no longer needs
   `AsReadOnly()` cloning. Impact: removes 2-3 of ~5 allocations per iteration in empty_loop/int_math/
   table_* loops (est. 25-40% on those), plus one `DynValue[]` per call (function_call, fib).
   Medium effort, medium risk (closure-in-loop semantics; TestMore covers it).
3. **Struct `CallStackItem` in `FastStack` with `ref` access** (WattleScript 6ce39e6/8feb3c6) and a
   reused frame pool for CLR frames. Map: `CallStackItem.cs`, `Processor*.cs`, coroutine code.
   Impact: one object less per call (function_call 376 ns, method_call 705 ns, fib). Small-medium,
   internal only. Risk: stale `ref` after stack growth; encode the `goto new_frame` rule and a test
   that forces growth during a call.
4. **Struct `DynValue` (16 bytes, sentinel-tagged number)** (WattleScript abbf807 + f366e87 +
   IEquatable). Map: `DataTypes/DynValue.cs` and every `== null` / `ReadOnly` / `Assign` site (76 files
   in WattleScript's diff), `Table.RawGet` null contract, `DebugModule.upvalueid`, VS Code debugger
   `VariableInspector`. Impact: largest single win (arithmetic and table reads become allocation-free;
   est. 2-4x on int_math/float_math/empty_loop based on allocation removal, unmeasured). Large effort,
   public API break; needs a `legacy`-style shim decision by the owner (e.g. keep `DynValue` name,
   drop `ReadOnly/Assign/ReferenceID`, add `IsNil()`-based helpers, an analyzer for `== null`).
   IL2CPP: explicit layout without ref overlap is supported; validate on Unity 2021.3.
5. **Struct `Instruction` + per-function constant pools** (WattleScript 3811213). Map:
   `Execution/VM/Instruction.cs`, `ByteCode.cs`, `Processor_BinaryDump.cs` (format version bump),
   `Tree/*` emitters, `Processor_Debugger.cs` (SourceRef array parallel to code). Impact: dispatch
   locality on all workloads (est. 10-20%), less memory per loaded chunk. Medium-large, internal;
   breaks dumped bytecode compatibility.
6. **Descriptor cache + `IsByRef` cache** (WattleScript 34e859c, e9a007a) with policy invalidation.
   Map: `Interop/UserDataRegistries/TypeDescriptorRegistry.cs`, `Interop/BasicDescriptors/ParameterDescriptor.cs`,
   `Interop/Converters/ScriptToClrConversions.cs`. Impact: interop-heavy code returning CLR objects
   (per conversion: no `GetInterfaces()` array, no List); overload scoring. Small, low risk.
7. **Allocation-free CLR call path**: `Array.Empty<DynValue>()` instead of `new DynValue[0]`
   (`Processor_InstructionLoop.cs:611`), struct slice instead of `class Slice<T>`
   (`DataStructs/Slice.cs:12`), reuse one `CallbackArguments` / `ScriptExecutionContext` per Processor
   or make them structs (WattleScript bbe9fec, SolarSharp a4324b6, 144f43c). Impact:
   interop_25k_calls-type workloads (MoonSharp 12.9 ms vs Lua-CSharp 2.6 ms [M]). Medium; making
   public types structs is an API change, pooling is not (but must be reentrancy-safe for callbacks
   that call back into Lua).
8. **Table with a real array part and an index-chained hash** (NeoLua `LuaTableEntry[]` + buckets;
   UniLua/ltable `computesizes`; avoid SolarSharp's 16M rule and WattleScript's LinkedList nodes).
   Map: `DataTypes/Table.cs`, remove `DataStructs/LinkedListIndex.cs` use, `TablePair`,
   `CoreLib/TableIteratorsModule.cs` (`next` by entry index), `TableModule.cs` (insert/remove with
   `Array.Copy`). Impact: table_array_insert 1277 ns, table_array_read 447 ns, table_hash_set_get
   757 ns: est. 2-5x. Medium-large. Compatibility: `pairs` order changes (insertion order is not
   guaranteed by Lua but users may depend on MoonSharp's) -> owner decision.
9. **Replace KopiLua string code**: write a string-index Lua pattern matcher from `lstrlib.c` (MIT),
   and a hand-written `string.format` spec scanner on a reused `StringBuilder`. Do not translate to
   .NET Regex (WattleScript's semantic drift). Map: `CoreLib/StringLib/KopiLua_StrLib.cs`,
   `Interop/LuaStateInterop/*`, `CoreLib/StringModule.cs`. Impact: string_format 4421 ns,
   string_pattern. Medium, risk = pattern edge cases (TestMore 304-string.t, plus a differential test
   against native Lua output in xlua-ng's runner).
10. **Roslyn incremental source generator for hardwired descriptors** (WattleScript 254bdc1), as an
    optional package beside the existing reflection path. Removes reflection invoke on IL2CPP. Large,
    additive, no runtime API break. Improve on WattleScript by generating typed argument reads
    (`args[i].Number` -> `double`) instead of `object[]` boxing.
11. **Startup**: lazy-build `Regex` and module tables in `new Script()` (SolarSharp 36cb3f1).
    MoonSharp 1.19 ms vs 0.02-0.25 ms [M]. Small.

Not recommended: NeoLua-style compilation (not AOT-safe), UniLua finalizer-based node recycling,
KopiLua boxed values, SolarSharp feature removals (AutoYield, readonly, ownership checks),
WattleScript Regex patterns.

## 10. Licence notes for adaptation into BSD-3-Clause MoonSharp

- WattleScript: BSD-3, same family as MoonSharp: code can be adapted with attribution (keep the
  WattleScript Authors notice in the touched files' header or NOTICE).
- UniLua, KopiLua, AsyncLua: MIT, compatible with attribution.
- NeoLua: Apache-2.0, compatible for inclusion in a BSD project only with the Apache NOTICE/licence
  terms carried; ideas only is simpler.
- SolarSharp: own code has no explicit grant in `LICENSE` (README says BSD-3). Use ideas only, or ask
  the author to clarify. `LuaDictionary` is derived from dotnet/runtime (MIT).
- Lua `lstrlib.c`/`ltable.c`: MIT; preferred source for porting algorithms.

## 11. Open questions

1. Same-machine A/B of WattleScript vs MoonSharp fbd0367 on the 18 flag_bench workloads and 4 interop
   cases: harness ready at `scratchpad/bench-other/`, blocked on permission to build external code.
2. Re-measure SolarSharp's four O(n^2) table scripts on MoonSharp master to confirm the cause before
   fixing (expected: same, code unchanged).
3. IL2CPP behaviour of `[StructLayout(Explicit)]` DynValue and `ref` returns on Unity 2021.3: expected
   fine, needs one Unity build.
4. Whether MoonSharp users depend on `pairs` insertion order (blocks idea 8's ordering choice).
