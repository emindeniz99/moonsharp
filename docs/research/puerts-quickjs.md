# PuerTS and QuickJS / quickjs-ng: techniques for MoonSharp

Date: 2026-10-28. Code-level follow-up to `landscape/js-family.md`, which judged these engines only on
fit. This report asks a different question: which of their techniques a managed Lua 5.2 engine in
safe C# (the MoonSharp fork) should adopt. Items already in `PLAN.md` §2-5 (from xLua, xlua-ng,
Lua-CSharp, Luau, Jint, WattleScript) are not repeated; a row says when it only sharpens one.

Evidence markers: [C] confirmed by reading the code at the SHA below (or by a quoted document),
[S] secondary (vendor claim, reasoned estimate, general platform knowledge), [U] unconfirmed.
Nothing here was measured: no .NET SDK is installed in this VM, and the cloned repos were only read
(their build scripts were not run).

## Code read

| Key | Repository | Default-branch SHA | Commit date | Licence |
|---|---|---|---|---|
| PT | https://github.com/Tencent/puerts | `c5cd0965598c4f34c6fc920d1dcd721db83e551e` | 2026-10-22 | BSD-3 for PuerTS itself, "except for the third-party components listed" in `LICENSE` [C] |
| QB | https://github.com/bellard/quickjs | `19d6bd865e709e2b5717fb4f2290c0cecc57ee4b` | 2026-10-26 | MIT [C] |
| QN | https://github.com/quickjs-ng/quickjs | `7a23d8e3fedb010070ef1a03873c55500c7aa1a9` | 2026-10-24 | MIT [C] |
| MSF | https://github.com/emindeniz99/moonsharp (fork master) | `0c0e5d4b32e1566e525a5c76b7fac82a27225fb1` | 2026-10-28 | BSD-3 |

PLAN.md was written against fork `58fa6bb`; master has moved to `0c0e5d4` (merge of `next`). The
files cited below were re-read at `0c0e5d4`. Clones: `scratchpad/clones/{Tencent_puerts,
bellard_quickjs,quickjs-ng_quickjs,emindeniz99_moonsharp}`. Ideas only from all three engines; no code
is copied. PuerTS's PInvoke runtime and its generators are C#, so its BSD-3 notice would be needed if
any of it were ever adapted; nothing below proposes that.

Permalink bases (every link below is written out in full):
`PT` = https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e,
`QB` = https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b,
`QN` = https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9,
`MSF` = https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1.

---

## Ranked table

Impact 1-10 (10 = removes a hosting blocker or a crash class, or >= 40% of a measured cost). Effort
S/M/L as in PLAN.md. "PLAN" says whether the row is new, sharpens or supersedes an item.

| Rank | ID | Technique | From | PLAN | Impact | Effort | Size | Safe C# / IL2CPP | Evidence |
|---:|---|---|---|---|---:|---|---|---|---|
| 1 | Q1 | Execution budget: an event counter on calls and back-edges, a host interrupt callback every N events, and an interrupt error that `pcall` cannot catch | QB, QN | **New A21**; merges into A5's slow-path counter; supersedes the interrupt part of B10 | 8 | S-M | none | yes / yes | [C] code; hosting.md names it as blocker 2 |
| 2 | Q2 | Deterministic CLR-recursion limit plus a stack probe, applied at every recursion site QuickJS guards (calls, parser, JSON, dump reader/writer, pattern matcher, serializer) | QB, QN | **Sharpens A20** | 7 | S | none | yes / yes (probe [U] on WASM) | [C] code; hosting.md blocker 1 |
| 3 | Q3 | Conformance corpus run like test262: per-test metadata, per-host feature flags, a known-failures file with exact error text, and a two-way ratchet (new, changed **and fixed** results fail CI), with an update mode | QN | **New A22**; sharpens A1 and the ROADMAP item "Run the official Lua 5.2 test suite and track failures" | 7 | M | none | n/a | [C] code |
| 4 | P1 | One binding policy (per-member Fast / Lazy / Reflection / Don't, strictest filter wins) read by the generator, the runtime registry, the LuaCATS emitter and the link.xml emitter, so declarations cannot drift from what scripts can call | PT | **Sharpens B6; new B18 (LuaCATS generator)** | 6 | M | smaller (fewer preserved members) | yes / yes | [C] code |
| 5 | P3 | Value-type receivers: copy, call, write back (skip for `readonly` members); optionally a typed `UserData<T>` that stores the struct inline and is mutated in place | PT | **Sharpens A9 and B6** | 5 | S (write-back) / M (typed holder) | none | yes / yes | [C] code; MoonSharp mismatch was [U] in A9 |
| 6 | P2 | One binding plan rendered by two back ends (runtime-compiled and generated source), so the two paths cannot disagree | PT | **Sharpens B6, B8, A9** | 5 | M | none | yes / yes | [C] code; [C] changelog shows the bugs it prevents |
| 7 | P4 | Signature-shared invokers: one generic invoker per normalised signature plus open delegates from `Delegate.CreateDelegate`, as a size mode between "generate every member" and reflection | PT (xil2cpp idea, re-done in safe C#) | **Sharpens B6** (adds a size mode) | 4 | M | smaller IL2CPP output [S] | yes / yes [S] | [C] design; gain [U] |
| 8 | P5 | AOT-safe generic methods: bind an open generic method only when every type parameter has a class constraint, instantiate with the constraint; let generated code pre-instantiate value-type cases | PT | **Sharpens B6/B9**; fixes the `MakeGenericMethod` failure class in hosting.md | 4 | S | none | yes / yes | [C] code |
| 9 | P6 | Event handlers held weakly per sender (MoonSharp bug found while studying PuerTS lifetimes); delegate identity per (closure, delegate type) | PT | **Sharpens A9 and B9**; new bug in §7 terms | 4 | S | none | yes / yes | [C] code reading |
| 10 | Q4 | Atom keys: constant string keys carry a precomputed hash and are compared by reference, so a field access hashes no string | QB | **Sharpens A17 (1) and B3** | 4 | S after B3 | none | yes / yes | [C] QuickJS code; .NET hashing [S] |
| 11 | Q5 | Inline 64-bit integer lane in the struct value (QuickJS "short big int"), replacing the `LuaInt64` heap object per int64 value | QB | **Sharpens B1** and the fork's int64 design | 4 | M inside B1 | none | yes / yes | [C] code |
| 12 | Q6 | Opcode stack-effect table and a bytecode verifier pass that also yields max stack per prototype | QB, QN | **New A23**; feeds B2, B4, B5 | 3 | S-M | none | yes / yes | [C] code |
| 13 | P7 | Lazy, declared-only type registration with a base-type link (and member-level lazy creation) | PT | **Sharpens A15 and B6** | 3 | M | none | yes / yes | [C] code; gain [U] |
| 14 | Q7 | Runtime/context split: one shared runtime (interned constants, compiled prototypes, module tables) and cheap per-script contexts | QB, QN | **Sharpens B14 and A15** | 4 | L | none | yes / yes | [C] code; vendor numbers [S] |
| 15 | Q8 | Errors raised by Lua `error()` unwind as a VM request, not as a .NET exception | QB, QN | **New B19** | 3 | M | none | yes / yes | [C] QuickJS design; MoonSharp cost [U] |
| 16 | Q9 | Precompiled-only profile and modules registered by direct reference, so the linker can drop the parser and unused modules | QN (`QJS_DISABLE_PARSER`, `JS_AddIntrinsic*`) | **New B20** (size) | 2 | M | smaller [U] | yes / yes | [C] code |
| 17 | Q10 | Concrete thresholds: rope rules for B16; tombstone compaction rule for B3 | QB | **Sharpens B16 and B3** | 1 | S | none | yes / yes | [C] code |

Nothing here supersedes a whole PLAN item. Q1 replaces the "interrupt polling only on calls, returns
and back-edges" sentence of B10 with a concrete design; B10's generic-policy loop specialisation
stays as written.

### What changes in PLAN.md

- **New**: A21 execution budget (Q1), A22 conformance corpus (Q3), A23 bytecode verifier (Q6),
  B18 LuaCATS generator built on P1, B19 error unwinding without .NET exceptions (Q8), B20 size
  profile (Q9).
- **Sharpened**: A5 (one counter also drives the budget), A9 (struct write-back design, event leak),
  A15 (lazy registration, runtime/context split), A17 (precomputed hashes), A20 (depth counter and
  the full site list), B1 (inline int64), B3 (compaction threshold, hash per entry), B6 (one policy,
  one plan, a signature-shared size mode, generic rule, build check), B8 (shared plan), B9 (weak event
  table, generics), B10 (interrupt part moves to A21), B14 (runtime/context split), B16 (thresholds).
- **§7 correctness bugs**: add the static event-subscription leak (P6) and the unbounded recursion in
  `json.parse`/`json.serialize` (Q2).

---

## Details: QuickJS / quickjs-ng

### Q1. Execution budget with an uncatchable interrupt (new A21)

**What QuickJS does [C].**
- A per-context counter starts at 10,000
  ([QB quickjs.c#L517](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L517)).
  `js_poll_interrupts` decrements it; at zero `__js_poll_interrupts` resets it and calls the host's
  `JSInterruptHandler`; a non-zero return throws "interrupted"
  ([QB quickjs.c#L7976-L8001](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L7976-L8001)).
- It is polled at every function entry
  ([QB quickjs.c#L18135](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L18135)),
  on `goto` and on both conditional jumps
  ([QB quickjs.c#L19174-L19230](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L19174-L19230)),
  and inside built-ins that can loop for a long time without running bytecode, such as property
  enumeration and the prototype walk of `instanceof`
  ([QB quickjs.c#L16645-L16660](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L16645-L16660),
  [QB quickjs.c#L41858-L41866](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L41858-L41866)).
- The interrupt error is **uncatchable**: `JS_ThrowInterrupted` sets the flag, and the unwinder skips
  every `try/catch` while it is set
  ([QB quickjs.c#L21140](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L21140)).
  quickjs-ng moved the flag onto the error object, so it survives being stored and rethrown, and
  passes the context to the handler
  ([QN quickjs.c#L8651-L8676](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.c#L8651-L8676),
  [QN quickjs.c#L12068-L12097](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.c#L12068-L12097),
  [QN quickjs.h#L1192](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.h#L1192)).

**Gain.** No published number. The hot-path cost is one decrement and one well-predicted branch per
call or jump [S].

**MoonSharp today [C].** `AutoYieldCounter` exists only for coroutines; the counter is a local of
`Processing_Loop`, so every re-entry (callback, metamethod, `Script.Call`) starts again from zero, and
plain calls cannot be pre-empted
([MSF Processor_InstructionLoop.cs#L14-L47](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L14-L47)).
hosting.md lists "an execution budget (instruction count or cancellation) for plain calls" as one of
the two changes needed before in-process JVM hosting can be the default
([MSF docs/hosting.md#L363-L371](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/docs/hosting.md#L363-L371)).

**Design for MoonSharp.**
1. Use the single slow-path counter that A5 already introduces. Decrement it on `Call`/`ThisCall`
   (`Internal_ExecCall`), on backward `Jump`, `JFor`, `IterUpd` and the loop jumps, and in long
   library loops (`string.rep`, `table.concat`, `table.sort`, `string.gsub`, the pattern matcher's
   main loop). At zero, one slow path handles the debugger, auto-yield and the budget.
2. Keep the counter on the `Script`, not in a loop local, so re-entrant calls share it.
3. Two budgets:
   - a **deterministic event budget**, which gives the same result on .NET, in the browser and on the
     JVM, so it fits the cross-host contract;
   - an optional host callback (`Func<bool>`, called every N events) for wall-clock deadlines and a
     `CancellationToken`.
4. Throw `ScriptInterruptedException : InterpreterException`, **not** a `ScriptRuntimeException`.
   The VM already rethrows non-runtime `InterpreterException`s past every `pcall` handler
   ([MSF Processor_InstructionLoop.cs#L249-L256](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L249-L256)).
   The CLR `pcall` and `coroutine.resume` paths catch only `ScriptRuntimeException`
   ([MSF ErrorHandlingModule.cs#L73](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/CoreLib/ErrorHandlingModule.cs#L73),
   [MSF CoroutineModule.cs#L76](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/CoreLib/CoroutineModule.cs#L76)).
   So QuickJS's uncatchable semantics come almost for free.
5. `Processor.Call` already crops both stacks on any exception, so the context stays usable after an
   interrupt
   ([MSF Processor.cs#L84-L102](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor.cs#L84-L102)).
6. Map the error to a new hosting error kind, `limit`.

**Tests.**
- `while true do end` and `repeat until false` are stopped by the event budget.
- `pcall(function() while true do end end)` does not catch the interrupt.
- `coroutine.wrap` around an infinite loop is stopped.
- An infinite loop inside a `__index` metamethod called from C# is stopped.
- `string.rep("x", 1e9)` is stopped inside the library loop.
- The same script stops at the same event count on every host.
- The context still works after an interrupt.
- The debugger still breaks on the next instruction.

**Size**: none. **IL2CPP**: plain C#. **Licence**: ideas only.

### Q2. Native-recursion guard: depth counter plus probe, at every recursion site (sharpens A20)

**What QuickJS does [C].**
- `js_check_stack_overflow` compares the C stack pointer with a limit derived from the stack top and
  a configurable size (1 MB by default)
  ([QB quickjs.c#L2087-L2092](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L2087-L2092),
  [QB quickjs.c#L2900-L2919](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L2900-L2919),
  [QN quickjs.h#L445](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.h#L445)).
- quickjs-ng calls it from every native-call path (`js_call_c_function`, bound functions,
  `JS_CallInternal`), the tokenizer, the JSON parser, `JSON.stringify` and its internalizer, the
  bytecode writer and reader, the regexp engine, `Array.prototype.flat` and module linking. The
  sites are the `js_check_stack_overflow(` calls at
  [QN quickjs.c#L6615-L52951](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.c#L6615),
  for example `json_parse_value`
  ([QN quickjs.c#L51039](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.c#L51039))
  and `JS_ReadObjectRec`
  ([QN quickjs.c#L40365](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.c#L40365)).

**MoonSharp today [C].**
- The script recursion limit exists (FastStack "stack overflow").
- The pattern matcher has `MAXCCALLS = 200`
  ([MSF KopiLua_StrLib.cs#L104](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/CoreLib/StringLib/KopiLua_StrLib.cs#L104)).
- Nothing guards C# re-entry. hosting.md measured that recursion through `string.gsub` kills the JVM
  ([MSF docs/hosting.md#L184-L196](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/docs/hosting.md#L184-L196)).
- `json.parse` and `json.serialize` recurse with no depth limit: `ParseJsonValue` → `ParseJsonObject`
  / `ParseJsonArray`, and `TableToJson`
  ([MSF JsonTableConverter.cs#L36](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Serialization/Json/JsonTableConverter.cs#L36),
  [MSF JsonTableConverter.cs#L171-L234](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Serialization/Json/JsonTableConverter.cs#L171-L234)).
  A script that parses a deeply nested string from its input can therefore overflow the .NET stack,
  which no .NET code can catch [C by reading; not run]. The hosting layer's own JSON has a 128 limit,
  but the in-script `json` module does not.

**Design.**
1. A **deterministic** per-Script CLR-nesting counter, incremented in `Processor.EnterProcessor` and
   decremented in `LeaveProcessor`
   ([MSF Processor.cs#L147-L190](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor.cs#L147-L190)).
   The limit is 200, Lua 5.2's `LUAI_MAXCCALLS`, with the error "C stack overflow". A counter gives
   the same error on a 1 MB JVM thread, an 8 MB main thread and the browser. A byte budget like
   QuickJS's would not.
2. `RuntimeHelpers.EnsureSufficientExecutionStack()` stays as the safety net (A20). Whether it fires
   on browser WebAssembly is [U].
3. Depth parameters (limit 200, error text from the Lua manual) in:
   - `JsonTableConverter` (parse and serialize);
   - `SerializationExtensions.SerializeValue`;
   - `Processor_BinaryDump` load;
   - the parser's `Expression_.SubExpr` / statement recursion
     ([MSF Expression_.cs#L58-L86](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Tree/Expression_.cs#L58-L86)).

**Tests.**
- `json.parse(string.rep("[", 1e5))` raises a Lua error.
- gsub recursion 10k deep raises "C stack overflow" on every host.
- `load(string.rep("(", 1e5))` raises a syntax error.
- The JVM in-process probe from hosting.md now returns `runtime`.

**Effort** S. **Size** none.

### Q3. Conformance corpus with a two-way known-failures ratchet (new A22)

**What quickjs-ng does [C].**
- `run-test262` reads a config file: feature list, `[exclude]` paths, harness directory, error file
  ([QN test262.conf#L1-L40](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/test262.conf#L1-L40)).
- It compares each result with a **known-errors file** that holds `file:line: exact message`
  (40 lines at this SHA,
  [QN test262_errors.txt](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/test262_errors.txt)).
- It counts **new**, **changed** (same test, different message or line) and **fixed** errors
  ([QN run-test262.c#L1455-L1510](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/run-test262.c#L1455-L1510)).
  The exit status is non-zero if any of the three is non-zero, "Signal that the error file is out of
  date"
  ([QN run-test262.c#L2380-L2383](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/run-test262.c#L2380-L2383)).
  A fix therefore has to update the file in the same change, and a later regression cannot hide
  behind a stale entry.
- `-u` rewrites the file (`make test262-update`), and a fast config excludes slow tests
  ([QN Makefile#L128-L138](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/Makefile#L128-L138),
  [QN test262-fast.conf](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/test262-fast.conf)).
- The project's own regression tests (`tests/bugNNNN.js`, named after issues) go through the same
  runner with a toolchain feature flag (`skip-if-tcc=!tcc`)
  ([QN tests.conf](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/tests.conf)).
- A separate CI job reruns everything under forced GC
  ([QN .github/workflows/gc-stress.yml](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/.github/workflows/gc-stress.yml)).

**MoonSharp today [C].**
- TestMore files carry `skip`/`todo` inline (18 sites, for example "LuaJIT TODO. format.") and the
  NUnit wrapper skips whole files per platform
  ([MSF TestMoreTests.cs#L248-L273](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter.Tests/TestMoreTests.cs#L248-L273)).
  Nothing fails when a `todo` starts passing, and nothing records the error text.
- The hosting fixtures are a JSON suite with exact expectations, but have no per-host expected
  failures
  ([MSF src/Hosting/fixtures/logic.suite.json](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/Hosting/fixtures/logic.suite.json)).
- ROADMAP plans to "Run the official Lua 5.2 test suite and track failures"
  ([MSF docs/ROADMAP.md#L97-L99](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/docs/ROADMAP.md#L97-L99)).

**Design.**
1. One corpus directory: TestMore files, the official Lua 5.2 tests split per file, the
   hosting fixtures, and `regress/NNN-*.lua` named after fork issues.
2. A front-matter comment per file, for example
   `--[[--- features: [int64, json] expect: error "attempt to index" ---]]`.
3. One runner, used by the .NET tests, the browser test, the JVM fixtures and a Unity player, with a
   per-host feature set (`!io`, `!wasm`, `!il2cpp`, `!jvm-inprocess`).
4. One `known-failures/<host>.txt` per host. CI fails on new, changed and fixed entries; `--update`
   rewrites them. A host divergence then becomes a reviewed line of data, not a silent skip.
5. A GC-stress leg (`DOTNET_GCgen0size` small, or `GC.Collect` in the A21 slow path every N events)
   for finalizer- and weak-table bugs.

Pairs with A1: allocation budgets catch performance regressions, the ratchet catches behaviour
regressions. **Effort** M (runner plus moving files). **Licence**: the Lua 5.2 test suite is MIT;
keep its notice.

### Q4. Atom keys with a precomputed hash (sharpens A17 (1) and B3)

**What QuickJS does [C].**
- Property names are atoms (`uint32`), and a string's hash lives in its header
  ([QB quickjs.c#L590-L606](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L590-L606)).
- Integer-like names are tagged atoms
  ([QB quickjs.c#L2929-L2952](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L2929-L2952)).
- A shape lookup is `atom & mask` followed by integer compares, so no string is hashed or compared
  on a property access
  ([QB quickjs.c#L6246-L6266](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L6246-L6266)).
- 251 common names are compiled-in atoms
  ([QB quickjs-atom.h](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs-atom.h)).

**Mapping.**
- A17 (1) already interns compile-time keys per Script so `Dictionary<string>` equality hits
  `ReferenceEquals`. But .NET strings do not cache their hash code, so every lookup still hashes the
  whole key [S: `System.String` has no hash field].
- With B3's own hash part (entries already store `hash`), the compiler can store the precomputed
  hash on the `IndexN`/`IndexSetN`/global instructions and call `FindStr(string key, int hash)`.
  The probe then costs a mask plus a reference compare, falling back to `string.Equals` only for
  runtime-built keys.
- The metamethod names (`__index`, `__newindex`, ...) become static pre-hashed keys, the analogue of
  compiled-in atoms.

**Gain.** method_call/clr_interop spend 17%/11% CPU in dictionary hash and `SequenceEqual` (PLAN
§0.2) [C]. The hashing part of that is the target [S].

**Effort** S on top of B3. **Risk**: dump format unchanged (hash recomputed at load). B14's
shared prototypes need the hash to be process-independent: use a fixed hash function, not
`string.GetHashCode`, which is randomised per process on .NET Core [S].

### Q5. Inline 64-bit integers in the struct value (sharpens B1)

**What QuickJS does [C].**
- On 64-bit targets `JSValue` is a 16-byte tagged struct
  ([QB quickjs.h#L216-L240](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.h#L216-L240)).
  NaN boxing is used only when pointers are 32-bit
  ([QB quickjs.h#L55-L65](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.h#L55-L65)).
- Small BigInts live in the value itself (`JS_TAG_SHORT_BIG_INT`,
  [QB quickjs.h#L93](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.h#L93)).
  The changelog: "new BigInt implementation optimized for small numbers" (2025-04-26,
  [QB Changelog](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/Changelog)).

**Mapping.**
- The fork's int64 is a userdata wrapping `LuaInt64` (`sealed class`,
  [MSF LuaInt64.cs#L13](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/DataTypes/LuaInt64.cs#L13))
  with metamethods. Every int64 result therefore allocates a `LuaInt64`, a `UserData` and a
  `DynValue`, and every operation is a metamethod call [C].
- B1's internal struct (`object o; double n; DataType t`) can carry a `long` in the same 8 bytes via
  `BitConverter.DoubleToInt64Bits`/`Int64BitsToDouble` (netstandard2.0, safe, no explicit layout),
  with `DataType.Int64`/`UInt64` tags.
- `LuaInt64` remains the boxed form at the public API boundary.
- This also gives ROADMAP's "A built-in integer subtype, if the struct-value step makes it cheap" a
  concrete form.

**Effort** M inside B1. **IL2CPP**: plain struct and BitConverter [S].

### Q6. Stack-effect table and bytecode verifier (new A23)

**What QuickJS does [C].**
- One X-macro table gives each opcode its size, pops, pushes and operand format
  ([QB quickjs-opcode.h#L62-L77](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs-opcode.h#L62-L77)).
- `compute_stack_size` walks the control-flow graph after compilation.
  - It records the stack depth at each pc and rejects underflow, inconsistent depths at merge points,
    inconsistent catch positions and out-of-range jumps.
  - It returns the maximum depth stored in the function
    ([QB quickjs.c#L36580-L36832](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L36580-L36832)).
- Bytecode loaded with `JS_ReadObject` is **not** verified, and quickjs-ng declares malicious
  bytecode out of scope
  ([QN SECURITY.md#L5-L13](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/SECURITY.md#L5-L13)).

**Mapping.**
- MoonSharp has `InstructionFieldUsage` per opcode but no stack-effect data.
- Add `(pops, pushes)` per `OpCode`, with variable forms reading `NumVal`.
- A verifier runs:
  - in Debug builds and tests after every compile, which guards A16 peepholes, B4 re-encoding and a
    B5 register compiler;
  - optionally on `undump`.
- In safe C#, a bad dump cannot corrupt memory, but it can fail with `IndexOutOfRangeException` deep
  in the loop. The verifier turns that into a load error.
- The max depth per prototype is what B2 needs to reserve a register window once at call time,
  instead of checking capacity on every push
  ([MSF FastStack.cs#L35-L63](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/DataStructs/FastStack.cs#L35-L63)).

**Effort** S-M. **Gain** is safety, not speed, until B2.

### Q7. Runtime / context split (sharpens B14 and A15)

**What QuickJS does [C].**
- `JSRuntime` owns atoms, shapes, the GC and memory limits; `JSContext` owns globals and intrinsics.
- Several contexts share one runtime, so compiled functions and atoms are shared.
- `JS_NewContextRaw` + `JS_AddIntrinsic*` lets the host choose what each context contains
  ([QN quickjs.h#L549-L564](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.h#L549-L564)).
- Vendor claim: "complete life cycle of a runtime instance ... less than 300 microseconds" [S,
  https://bellard.org/quickjs/].

**Mapping.**
- In MoonSharp each `Script` holds everything, except the static UserData registry.
- A `ScriptRuntime` holding the A15 module-registration cache, the Q4 constant-key pool (immutable,
  compile-time keys only, so UnityEnhanced's leak is not repeated) and B14's prepared prototypes gives
  B14 a natural home and makes `new Script()` mostly table setup.
- A15's target (`new Script()` under 300 us) is the same order as QuickJS's claim.

**Effort** L. Do after A15 and B4; owner decision on the public API.

### Q8. `error()` without a .NET exception (new B19)

**What QuickJS does [C].** Errors are return values (`JS_EXCEPTION`). The interpreter jumps to its
`exception:` label and unwinds by scanning the value stack for catch markers (`JS_TAG_CATCH_OFFSET`)
([QB quickjs.c#L21135-L21150](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L21135-L21150)).

**Mapping.**
- MoonSharp raises every Lua error as `ScriptRuntimeException` and catches it in the loop to find the
  `pcall` frame (Processor_InstructionLoop.cs L249+).
- Do not convert all errors. Only `error()` and `assert()` become a CLR callback result kind (like
  the existing `TailCallRequest`/`YieldRequest`) that the loop unwinds directly when a `pcall` frame
  is on the same Processor. Everything else keeps throwing.

**Gain.** A .NET throw/catch costs microseconds, several times more than a Lua call [S]. Only
error-heavy scripts (validation rules that `error()` on bad input, `pcall` used as control flow)
benefit. Add a `pcall_error` workload to A1 before deciding.

**Effort** M. **Risk**: error decoration, `xpcall` handlers and the debugger's
`SignalRuntimeException` must behave the same; test against today's messages.

### Q9. Size profile: precompiled-only and modules by direct reference (new B20)

**What QuickJS does [C].**
- `QJS_DISABLE_PARSER` builds an engine that can only run precompiled bytecode
  ([QN CMakeLists.txt#L38](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/CMakeLists.txt#L38)).
- Intrinsics are added by explicit calls, so a static linker drops unused ones.
- The 2025-04-26 changelog adds `qjs -s/--strip-source`.

**Mapping.**
- The parser (`Tree/`, 4,217 lines) is about 12% of the interpreter's 36,041 source lines [C, line
  count, not IL size].
- The fork's embedded `ILLink.Descriptors.xml` preserves every core module so that reflection
  registration survives trimming
  ([MSF ILLink.Descriptors.xml](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/ILLink.Descriptors.xml)).
  That is correct for today, but it also means no app can drop `io`, `debug` or `json`.
- If A15's registration cache is generated as direct references (`new CallbackFunction(StringModule.len, ...)`),
  the descriptor is no longer needed and the linker keeps only the modules the host registers.
- A compiler service registered by the host (`Script.GlobalOptions.Compiler`) would let a
  dump-only player drop the parser.

**Gain** [U]: measure IL2CPP output with and without before committing. **Effort** M.

### Q10. Thresholds worth reusing (sharpens B16 and B3)

- **Ropes.** Concatenation copies when the right side is at most 512 chars and the left at most
  8,192. Otherwise it builds a rope, folding short right pieces into the rope's right leaf, with depth
  capped at 60
  ([QB quickjs.c#L221-L225](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L221-L225),
  [QB quickjs.c#L5172-L5225](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L5172-L5225)).
  The changelog says "faster repeated string concatenation". These are the starting numbers if B16
  is ever reopened.
- **Tombstones.** `delete_property` compacts when deleted entries are at least 8 **and** at least
  half of the entries
  ([QB quickjs.c#L9440-L9486](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L9440-L9486),
  [QB quickjs.c#L5511-L5577](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L5511-L5577)).
  For Lua the check must run on **insert** (B3 already says "purged on resize"), because compacting
  on a nil assignment would move keys under a running `next`. The floor of 8 avoids thrashing small
  tables; A7's "dead > live" rule should take the same floor.

---

## Details: PuerTS

### How PuerTS binds C# (background for P1-P7) [C]

- **Two runtime modes.** P/Invoke (`PUERTS_DISABLE_IL2CPP_OPTIMIZATION || !ENABLE_IL2CPP`) and
  xil2cpp (IL2CPP default on desktop, Android and WebGL since 2.2.2; iOS default is P/Invoke)
  ([PT doc/unity/en/performance/il2cpp.md](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/performance/il2cpp.md)).
- **Type registration, P/Invoke mode.** A type is registered on first use (`OnTypeNotFound`). Static
  wrappers are used when registration info exists; otherwise expression-tree wrappers are built at
  runtime
  ([PT TypeRegister.cs#L157-L175](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/TypeRegister.cs#L157-L175),
  [PT TypeRegister.cs#L510-L515](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/TypeRegister.cs#L510-L515)).
  Members are `DeclaredOnly`, with a link to the base type's registration
  ([PT TypeRegister.cs#L179-L183](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/TypeRegister.cs#L179-L183),
  [PT TypeRegister.cs#L286-L290](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/TypeRegister.cs#L286-L290)).
- **Overloads.** Candidates are tried in declaration order, and the first that passes an arity check
  and per-argument type checks wins. Checks are emitted only when there are several overloads or
  optional/`params` parameters
  ([PT ExpressionsWrap.cs#L1280-L1381](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L1280-L1381)).
  MoonSharp's weighted scoring is better than first-match; do not copy (see "Not worth copying").
- **Arguments.** Per type, typed reads (`get_value_int32`, ...); `ref`/`out` through boxed script
  values updated after the call; `params` and default values in the generated code
  ([PT ExpressionsWrap.cs#L781-L997](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L781-L997),
  [PT ExpressionsWrap.cs#L1478-L1486](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L1478-L1486)).
  No `object[]` and no boxing of primitives.
- **What still allocates.** A struct pushed to script is boxed into an object-pool slot
  ([PT ExpressionsWrap.cs#L299-L311](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L299-L311)).
  A string push allocates a UTF-16 `byte[]`
  ([PT ExpressionsWrap.cs#L137-L145](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L137-L145)).
  A delegate call allocates an `IntPtr[]` of arguments
  ([PT ExpressionsWrap.cs#L745-L757](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L745-L757)).
  PuerTS's P/Invoke path is not allocation-free.
- **Published cost of the struct path** (PuerTS's own table, 200,000 calls; "puer S" = P/Invoke with
  static wrappers, "puer X S" = xil2cpp with wrappers) [C doc / S numbers]:

  | Call | Android: puer S / puer X S (ms) | iOS: puer S / puer X S (ms) |
  |---|---:|---:|
  | `void Payload(int, int, float)` | 38 / 16 | 34 / 26 |
  | `Quaternion Payload(Transform, Vector3)` | 152 / 47 | 159 / 64 |

  Source: [PT doc/unity/en/performance/index.md#L24-L63](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/performance/index.md#L24-L63).
  The struct case is where the boxed path loses most (about 3x). That is the allocation P3 targets
  in MoonSharp.

### P/Invoke mode vs xil2cpp: what our rules allow

| Part | Mechanism | Allowed for us? |
|---|---|---|
| `[DllImport]` into a native plugin; `__Internal` on iOS/WebGL ([PT PuertsNative.cs#L32](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/Native/PuertsNative.cs#L32)) | public P/Invoke | Yes (MoonSharp has no native part and needs none) |
| Generated C# static wrappers, `[UnityEngine.Scripting.Preserve]`, generated `link.xml` | public C#, linker config | Yes |
| `IPreprocessBuildWithReport` that fails the build when generated files are missing ([PT GenerateChecker.cs#L20-L53](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/GenerateChecker.cs#L20-L53)) | documented build callback | Yes |
| Expression trees compiled at runtime | public API; interpreted on IL2CPP [S] | Yes, but slow on IL2CPP (PLAN B8 note) |
| Generated `Puerts_il2cpp.cpp`: includes `il2cpp-class-internals.h`, `vm/Object.h`, `vm/MetadataCache.h`, `gc/GCHandle.h`, `gc/WriteBarrier.h` ([PT Puerts_il2cpp.cpp.txt#L9-L32](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/xil2cpp/Puerts_il2cpp.cpp.txt#L9-L32)) | libil2cpp internals | **No** |
| Calling `Il2CppMethodPointer` directly, `Object::GetVirtualMethod`, `Object::Unbox` ([PT Puerts_il2cpp.cpp.txt#L575-L604](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/xil2cpp/Puerts_il2cpp.cpp.txt#L575-L604)) | internals | **No** |
| Struct bytes `memcpy`'d into script memory, freed without a GC handle when the struct has no references | internals | **No** |
| FAQ: edit Unity's `il2cpp-config.h`; copy missing libil2cpp headers into the Xcode project ([PT il2cpp.md](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/performance/il2cpp.md)) | patching the engine | **No** |

The xil2cpp source needs per-version `#if`s because the internals change between Unity versions
[C]:
- `MetadataCache::HasAttribute` became attribute handles in 2022.1
  ([PT Puerts_il2cpp.cpp.txt#L356-L361](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/xil2cpp/Puerts_il2cpp.cpp.txt#L356-L361));
- `Thread::Current` became `CurrentInternal` and `GetDefaultParameterValueObject` changed signature
  in 2021.1
  ([PT Puerts_il2cpp.cpp.txt#L724-L768](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/xil2cpp/Puerts_il2cpp.cpp.txt#L724-L768));
- `Il2CppGCHandle` changed in 6000.0
  ([PT Puerts_il2cpp.cpp.txt#L864-L866](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/xil2cpp/Puerts_il2cpp.cpp.txt#L864-L866));
- the Nullable layout differs before 2021.1 (L1996-L2003).

That is the maintenance cost our "public mechanisms only" rule avoids. What is reusable from
xil2cpp is only the *idea* of signature sharing (P4), re-done with delegates.

**Unity versions [C].**
- xil2cpp code is guarded by `UNITY_2020_1_OR_NEWER`.
- 3.0.1 lists "Unity 2018 compatibility fixes" and a `Preserve` fix for "Unity 2022 WebGL default
  settings"
  ([PT unity/changelog.md#L44-L64](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/changelog.md#L44-L64)).
- 2.2.2 supports the "Faster (smaller) builds" IL2CPP option (L84-L92).
- So PuerTS covers 2021.3, 2022.3 and 6 [C for code paths]; it ships no test matrix result for each
  [U].

**link.xml [C].** `GenLinkXml` writes `preserve="all"` for every type in the binding list
([PT CSharpCodeGen.cs#L1171-L1205](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/CSharpCodeGen.cs#L1171-L1205),
[PT CSharpFileExporter.cs#L428-L440](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/CSharpFileExporter.cs#L428-L440)).
This is coarse: it keeps every member, including ones a filter excluded (see P1 for the finer rule).

### P1. One binding policy for generator, runtime, declarations and linker (sharpens B6; new B18)

**What PuerTS does [C].**
- `BindingMode` has four values: `FastBinding` (static wrapper), `LazyBinding` (reflect on first
  call), `SlowBinding` (reflection), `DontBinding` ("not able to called in runtime. Also will not
  generate d.ts")
  ([PT RegisterInfoManager.cs#L12-L17](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/RegisterInfoManager.cs#L12-L17)).
- User `[Filter]` methods return a mode per `MemberInfo`, and the strictest mode wins
  ([PT Generator/Utils.cs#L225-L234](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/Utils.cs#L225-L234)).
- The same `IsNotSupportedMember` and `getBindingMode` filters select members for the wrappers and
  for the `.d.ts`
  ([PT DTS.cs#L139-L163](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/v8/Editor/Src/Generator/DTS.cs#L139-L163),
  [PT DTS.cs#L267-L306](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/v8/Editor/Src/Generator/DTS.cs#L267-L306)).
- The binding list and the typing list come from one `[Configure]` class
  ([PT DTSUnityMenu.cs#L81-L100](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/v8/Editor/Src/Generator/DTSUnityMenu.cs#L81-L100)).
- A host can make deny-by-default the runtime policy (`SetDefaultBindingMode(DontBinding)`)
  ([PT doc/unity/en/wrapper/filter.md](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/wrapper/filter.md)).

**`.d.ts` lessons that carry over to LuaCATS [C].**
1. **Transitive closure.** Every type reached through a base type, interface, generic argument,
   delegate signature, field, parameter or return value is declared. Only bound types get members, so
   editors never meet an unknown type name
   ([PT DTS.cs#L460-L532](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/v8/Editor/Src/Generator/DTS.cs#L460-L532),
   [PT DTS.cs#L655-L710](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/v8/Editor/Src/Generator/DTS.cs#L655-L710)).
2. **Base overloads re-listed.** A derived class that declares `Foo(int)` hides a base `Foo(string)`
   in TypeScript. `CheckGenInfos` re-adds the base overloads that are not overridden, and copies
   interface members into implementing classes
   ([PT DTS.cs#L554-L653](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/v8/Editor/Src/Generator/DTS.cs#L554-L653)).
   LuaLS has the same hiding when a `---@class Derived : Base` redeclares a field, so the LuaCATS
   emitter needs the same merge into `---@overload` lines [S: LuaLS behaviour not re-tested here].
3. **Extension methods** become members of the extended type with the `this` parameter dropped
   (L303-L306), matching MoonSharp's colon-call view.
4. **Type mapping.** `long` → `bigint`, `ref`/`out` → `$Ref<T>`, `params` → rest arrays, `Nullable<T>`
   → `T | null`, generic parameters → their constraint type
   ([PT Generator/Utils.cs#L431-L492](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/Utils.cs#L431-L492)).
   For MoonSharp the mapping is instead: out params → extra return values (`BuildReturnValue`
   already does this); `long` → `number` or `int64` depending on `ExactInt64Interop`.
5. **XML doc comments** go into the declaration (`DocResolver.GetTsDocument`).

**What PuerTS lacks, and we should add.** A staleness check. The build check only tests that the
generated file exists. B6's Roslyn generator cannot be stale. The LuaCATS file can be, because it
lives outside compilation: embed a hash of (type list, member list, policy) in its header and fail a
test or build callback when it differs.

**Mapping.**
- MoonSharp's visibility today is spread over `[MoonSharpVisible]`, `[MoonSharpHidden]`,
  `InteropAccessMode`, `UserData.RegistrationPolicy` and per-type registration.
- B6 should compute one `MemberPolicy` per member: generated, reflection-lazy, reflection, hidden.
- The same function feeds:
  - the generated descriptors;
  - the runtime registry for reflection-registered types;
  - the LuaCATS emitter (new B18: `---@class`, `---@field`, `---@overload`, `---@param`; colon
    methods; out params as extra returns);
  - a link.xml emitter. It preserves only members whose policy is reflection: generated members are
    rooted by the direct calls in generated code, so they need no entry.

**Effort** M. **Size**: smaller than PuerTS's `preserve="all"` [S].

### P2. One binding plan, two renderers (sharpens B6, B8, A9)

**What PuerTS does [C].**
- Since 3.0, one function builds an expression tree per member: argument checks, typed conversion,
  call, ref write-back, return.
  - With dynamic code, the tree is `Compile()`d at runtime
    ([PT ExpressionsWrap.cs#L1434-L1437](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L1434-L1437)).
  - Otherwise `ExpressionToCSharpVisitor` prints the same tree as C# source for a static wrapper
    ([PT StaticWrapperGenerator.cs#L19-L24](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/StaticWrapperGenerator.cs#L19-L24),
    [PT StaticWrapperGenerator.cs#L422-L460](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/StaticWrapperGenerator.cs#L422-L460)).
- Changelog: 3.0.0 "Complete rewrite of the non-il2cpp environment, using ExpressionTree to
  dynamically generate bidirectional bridge code"; 3.0.2 "Added static C# wrapper generation support
  for PInvoke".
- The motivation shows in earlier entries, where two implementations disagreed: 2.2.3 "Fixed a
  series of parameter validation mismatches between the il2cpp-optimized and P/Invoke versions".
  3.0.3 still fixes by-ref overload matching separately for P/Invoke and for xil2cpp, which keep two
  implementations
  ([PT unity/changelog.md#L9-L83](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/changelog.md#L9-L83)).

**Mapping.**
- MoonSharp has four call paths per member:
  - reflection `Invoke`;
  - `LazyOptimized`/`Preoptimized` expression lambdas
    ([MSF MethodMemberDescriptor.cs#L225-L275](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs#L225-L275));
  - Hardwire;
  - and soon B6.
- A9's parity suite catches disagreements after the fact; PuerTS's lesson is to remove their cause.
- Roslyn generators see `ITypeSymbol`, not `System.Type`, so B6 cannot literally share PuerTS-style
  code with the runtime. What can be shared is the *plan*:
  - B8's per-parameter converter kind, nullable/params/default handling, overload order and weight
    inputs, the write-back flag (P3) and the policy (P1);
  - computed by one library over a small `ITypeView` abstraction with two adapters, Roslyn symbols and
    reflection;
  - rendered as C# text by B6 and as delegates (or a cached interpreter) at runtime.

**Effort** M. **Tests**: A9's parity probes run against both renderers; a test that fails when a
renderer handles a converter kind the plan does not list.

### P3. Value-type receivers and typed value-type userdata (sharpens A9 and B6)

**What PuerTS does [C].**
- For an instance method on a struct, P/Invoke wrappers read the struct out of the pool slot, call
  the method on the copy, then write the copy back with `UpdateValueType` (`ReplaceValueType`)
  ([PT ExpressionsWrap.cs#L1385-L1403](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L1385-L1403),
  [PT ExpressionsWrap.cs#L118-L126](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L118-L126)).
- It does this for every instance member, getters included, and each write-back re-boxes.

**MoonSharp today [C].**
- Reflection `MethodInfo.Invoke` on the boxed struct mutates the box in place.
- `Optimize` uses `Expression.Convert(objinst, DeclaringType)`, which unboxes a copy, so the mutation
  is lost
  ([MSF MethodMemberDescriptor.cs#L241](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs#L241)).
  That is the [U] mismatch A9 plans to test; the code reading here supports it.
- Every struct push allocates a box, a `UserData` and a `DynValue`
  ([MSF UserData.cs#L208-L234](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/DataTypes/UserData.cs#L208-L234)).

**Design.**
1. Expression path: `Expression.Unbox`, as A9 says.
2. Generated (B6) path: safe C# cannot take a reference into a box, so use PuerTS's copy, call,
   write back. Skip the write-back when the method or the struct is `readonly` (`IsReadOnlyAttribute`)
   and for property getters of `readonly struct`s. PuerTS always writes back.
3. Optional, M effort: a typed holder `ValueUserData<T> : UserData` with a `public T Value` field.
   - One allocation replaces the box plus the `UserData`.
   - Generated code calls `((ValueUserData<T>)ud).Value.Method()`, which mutates in place with no
     write-back and no re-box.
   - `UserData.Object` returns a boxed copy for existing hosts; this needs `Object` to become
     virtual, or the box to be materialised on demand.

**Tests**: a mutating struct method called through each access mode leaves the same state; a
`readonly` member does no write-back (count allocations with the A1 probe).

**Gain**: on the `Payload(Transform, Vector3)` style call, PuerTS's own table shows the boxed path at
about 3x the copy path. For MoonSharp, -1 allocation per struct push with the holder [S].

### P4. Signature-shared invokers as a B6 size mode (sharpens B6)

**What PuerTS does [C].**
- xil2cpp generates one C++ wrapper per **call signature**, not per method. A signature is a string
  of type codes: `i4`, `r4`, `o` for any reference type, `S_<field codes>_` for a struct by field
  layout, `P` for by-ref, `D` for an optional parameter, `V` for `params`
  ([PT IL2Cpp/TypeUtils.cs#L280-L475](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/IL2Cpp/TypeUtils.cs#L280-L475)).
- Every method with that signature calls the shared wrapper with its own method pointer.
- Wrappers sit in a sorted table found by binary search, and a method with no wrapper falls back to a
  reflection wrapper
  ([PT il2cppwrapper.tpl.mjs#L27-L45](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/templates/il2cppwrapper.tpl.mjs#L27-L45),
  [PT Puerts_il2cpp.cpp.txt#L2532-L2576](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/xil2cpp/Puerts_il2cpp.cpp.txt#L2532-L2576)).
- Menu modes trade size for speed: "Static Wrapper Mode", "Reflection Mode", "Minimal Bridge,
  Reflection Mode" ([PT UnityMenu.cs#L24-L90](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/UnityMenu.cs#L24-L90)).

**Safe-C# equivalent.**
- B6 emits, per normalised signature, a generic invoker such as
  `static int Inv_V_O_R4R4R4<TSelf>(Action<TSelf, float, float, float> f, ...) where TSelf : class`.
  Reference positions normalise to a class-constrained type parameter, so IL2CPP shares the code
  across reference types.
- At registration, `Delegate.CreateDelegate(typeof(Action<TSelf, float, float, float>), methodInfo)`
  binds an open-instance delegate. Reflection is used only for lookup, never for `Invoke`.
- Static methods and structs use the same pattern with `Func`/`Action` or a generated delegate type
  when there are more than 16 parameters or by-ref parameters.

**Feasibility.**
- IL2CPP compiles the needed delegate instantiations because the generated code names them.
- `CreateDelegate` from a `MethodInfo` works when the target method has AOT code [S].
- Value-type `TSelf` (struct receivers) needs open delegates with a `ref` first parameter, which
  `Action<>` cannot express. Keep per-member generation for struct receivers.

**Size and speed.**
- A third mode next to "per member" (B6 today, fastest) and "reflection" (smallest).
- One delegate call (~2 ns [S]) per invocation over direct code.
- xlua-ng's descriptor mode already cut APK size about 8% by merging per-member classes (PLAN B6).
  Signature sharing goes further on types with many same-shaped members [U: measure].

**Effort** M.

### P5. AOT-safe generic methods (sharpens B6/B9)

**What PuerTS does [C].**
- An open generic method is bound only when every type parameter appears in the parameters and has
  a class (reference) constraint that is not itself a constructed generic.
- It is instantiated with the constraint type: `MakeGenericMethod(constraint)`.
- A return type that is a type parameter must be one of those parameters.
- Code over reference types is shared on IL2CPP, so the instantiation needs no AOT code that does not
  exist
  ([PT IL2Cpp/TypeUtils.cs#L99-L143](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/IL2Cpp/TypeUtils.cs#L99-L143),
  [PT IL2Cpp/TypeUtils.cs#L497-L590](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/IL2Cpp/TypeUtils.cs#L497-L590)).

**MoonSharp today [C].**
- Normal descriptors reject any method with open generic parameters
  ([MSF MethodMemberDescriptor.cs#L136-L141](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs#L136-L141)).
- Generic **extension** methods are instantiated with the extended type's own arguments, for example
  `Enumerable.Sum<int>` for `List<int>`
  ([MSF ExtensionMethodsRegistry.cs#L140-L154](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Interop/UserDataRegistries/ExtensionMethodsRegistry.cs#L140-L154)).
  That is exactly the instantiation that fails under NativeAOT (hosting.md: 12 failures,
  "LINQ extension methods instantiated with `MakeGenericMethod`") and, before full generic sharing,
  under IL2CPP [S: full sharing exists from Unity 2022.1; verify on the 2021.3 matrix leg].

**Design.**
1. Accept PuerTS's rule for reflection-bound generic methods: class-constrained, instantiated with
   the constraint. Example: `T Find<T>(T key) where T : Component`.
2. For value-type instantiations, only B6-generated code. The generator writes the closed call, which
   both binds and AOT-roots it.
3. Otherwise raise a clear "generic instantiation not available in AOT" error instead of
   `ExecutionEngineException`/`NotSupportedException`.
4. Check `RuntimeFeature.IsDynamicCodeSupported` (hosting.md already notes `IsRunningOnAOT` misses
   NativeAOT).

**Effort** S.

### P6. Event subscriptions held weakly; delegate identity (sharpens A9 and B9; new bug)

**What PuerTS does [C].**
- A script function converted to a delegate is cached per (script function, delegate type)
  (`tryGetCachedDelegate`/`cacheDelegate`), so `+=` and `-=` see the same instance.
- The script function's C# wrapper is found again through a private slot that holds a **weak**
  `GCHandle`, so the C# side does not keep it alive
  ([PT ExpressionsWrap.cs#L343-L380](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L343-L380),
  [PT ExpressionsWrap.cs#L672-L779](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L672-L779),
  [PT PInvoke/JSType/ScriptObject.cs#L146-L162](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/JSType/ScriptObject.cs#L146-L162)).
- 3.0.3 still had to fix "the js function passed into a C# delegate and everything its closure
  captures" leaking (changelog #9).

**MoonSharp bug found while comparing [C by code reading].**
- `EventMemberDescriptor` keeps `m_Callbacks` (`MultiDictionary<object, Closure>`) and `m_Delegates`
  (`Dictionary<object, Delegate>`), keyed **strongly** by the sender object
  ([MSF EventMemberDescriptor.cs#L24-L26](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/EventMemberDescriptor.cs#L24-L26),
  [MSF EventMemberDescriptor.cs#L173-L221](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/EventMemberDescriptor.cs#L173-L221)).
- Descriptors live in the static type registry.
- So every object that ever had a Lua handler, the handler's closure and its whole `Script` stay
  reachable until the script removes the handler. An object destroyed by its host while still
  subscribed is never collected.
- The event-to-handler cycle alone would be collectable; the static dictionary is the root.

**Fix.** `ConditionalWeakTable<object, Subscriptions>` per descriptor (net45, netstandard2.0, IL2CPP).
The `Subscriptions` object holds the closures and the installed handler.

**Test.** Subscribe from Lua, drop all references to the sender and the `Script`, force GC, and
assert a `WeakReference` to the sender is dead.

B9's closure → delegate cache should likewise be weak (a `ConditionalWeakTable` keyed by `Closure`),
as PuerTS's is.

**Effort** S.

### P7. Lazy, declared-only registration (sharpens A15 and B6)

**What PuerTS does [C].**
- Generated code installs a `Func<RegisterInfo>` per type, and the getter runs only when the type is
  first reached from script
  ([PT registerinfo.tpl.mjs#L11-L60](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/templates/registerinfo.tpl.mjs#L11-L60),
  [PT TypeRegister.cs#L157-L175](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/TypeRegister.cs#L157-L175)).
- Members are `DeclaredOnly`, with a link to the base type.
- 3.0.3 adds "member-level lazyload ... on-demand loading of instance methods/properties" (changelog
  #1).

**MoonSharp today [C].**
- `StandardUserDataDescriptor` builds a descriptor for every visible member at registration.
- `GetMethods` uses `Public | NonPublic | Instance | Static` without `DeclaredOnly`
  ([MSF FrameworkClrBase.cs#L11](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Compatibility/Frameworks/Base/FrameworkClrBase.cs#L11),
  [MSF StandardUserDataDescriptor.cs#L77-L130](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Interop/StandardDescriptors/StandardUserDataDescriptor.cs#L77-L130)).
  So every subclass of a large engine base type rebuilds descriptors for all inherited instance
  members.

**Design.**
- B6 registers per-type thunks, not descriptors.
- Reflection descriptors keep a `MemberInfo[]` per name and build the `MethodMemberDescriptor` on
  first `Index`.
- A parent-descriptor link replaces copying inherited members. Overload sets across levels merge on
  first lookup of that name; that is the P1 base-overload rule again.

**Gain** [U]: add a `register_deep_subclass` case to A1 before deciding. **Effort** M.

---

## Not worth copying

| Technique | Source | Reason |
|---|---|---|
| xil2cpp: generated C++ against libil2cpp internals, raw method pointers, struct memcpy, GC handles | PT ([Puerts_il2cpp.cpp.txt#L9-L32](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/xil2cpp/Puerts_il2cpp.cpp.txt#L9-L32)) | Forbidden by the public-mechanisms rule. Needs per-Unity-version `#if`s (2021.1, 2022.1, 6000.0) and, per its FAQ, edits to Unity's own headers. MoonSharp is managed and has no boundary to cross. |
| Object pool / handle table between script and C# | PT ([PInvoke/ObjectPool.cs#L27-L216](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/ObjectPool.cs#L27-L216)) | Exists because V8/QuickJS objects cannot hold .NET references. MoonSharp userdata references the object directly and one GC collects both sides; a pool would add manual lifetime and a lookup per access. |
| Weak GC handles plus finalizer "pending kill" lists processed on `Tick` | PT ([ScriptObject.cs#L60-L143](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/JSType/ScriptObject.cs#L60-L143)) | Cross-GC machinery; one heap needs none. Its 3.0.3 changelog shows the bug class it brings (leaks, use after dispose, finalizer deadlock #2316). |
| First-match overload resolution in declaration order | PT ([ExpressionsWrap.cs#L1367-L1381](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Runtime/Src/PInvoke/Wrapper/ExpressionsWrap.cs#L1367-L1381)) | xlua-ng moved away from it for best-fit (xlua.md §3.1). PuerTS 3.0.3 still fixes "ref long/ref int always matched the first overload". MoonSharp's weighted scoring is already better. |
| Identity cache for pushed objects (weak-valued table, same C# object → same script value) | PT Lua backend ([CppObjectMapperLua.cpp#L192-L208](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/native/papi-lua/source/CppObjectMapperLua.cpp#L192-L208)) | MoonSharp already compares userdata by `Object.Equals` ([MSF DynValue.cs#L630-L682](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L630-L682)). A `ConditionalWeakTable` lookup per push probably costs about what the two allocations it saves cost [U]. Revisit only if A13's per-userdata bound-method cache needs stable `UserData` instances. |
| BlittableCopy (struct memory shared with native) | PT ([blittablecopy.md](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/wrapper/blittablecopy.md)) | Needs `unsafe`; P3's typed holder gets the managed equivalent. |
| A JavaScript engine at editor time to render templates | PT (3.0.2 moved generation to C#) | PuerTS itself dropped it; our generators are C#/Roslyn. |
| `preserve="all"` link.xml for every bound type | PT ([CSharpCodeGen.cs#L1171-L1205](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/CSharpCodeGen.cs#L1171-L1205)) | Keeps filtered-out members; P1's per-policy link.xml is finer. |
| Nominal-type marker in declarations (`__keep_incompatibility`) | PT ([dts.tpl.mjs](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/templates/dts.tpl.mjs)) | TypeScript is structural; LuaCATS classes are already nominal. |
| Reference counting with a cycle collector | QB/QN ([QN quickjs.c#L7363-L7483](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.c#L7363-L7483)) | The .NET GC does this. Refcounting in managed code adds a write per copy and gains nothing. Deterministic destruction is the only feature it gives, and Lua 5.2 does not promise it. |
| Per-runtime memory limit through allocator accounting (`JS_SetMemoryLimit`) | QN ([quickjs.c#L2435-L2438](https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.c#L2435-L2438)) | .NET has no per-Script allocation accounting; `GC.GetAllocatedBytesForCurrentThread` is per thread and not in netstandard2.0. Use deterministic limits at the library choke points instead (`string.rep`, `..`, table growth), checked in the Q1 slow path. |
| NaN boxing | QB (32-bit only, [quickjs.h#L55-L65](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.h#L55-L65)) | Hiding references in a `long` is impossible in safe C#. QuickJS itself uses a 16-byte tagged struct on 64-bit, which is B1's design. |
| Shapes / hidden classes for tables | QB ([quickjs.c#L978-L998](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L978-L998), [quickjs.c#L5579-L5672](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs.c#L5579-L5672)) | QuickJS uses shapes for memory sharing and has **no inline caches** on top of them [C: no IC structures in either repo at these SHAs]. Luau's per-instruction slot hint (A17 stage B on B3) already hits across tables built in the same order, without transition tables, a shape hash or a dictionary-mode fallback. Lua's `next` order and nil-deletes would also fight shape transitions. |
| Variable-length bytecode and short opcodes (`push_0..7`, `get_loc0..3`) | QB ([quickjs-opcode.h](https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/quickjs-opcode.h)) | Shrinks C bytecode, but byte decoding in C# costs more than a packed `ulong` (B4). Dump size is already handled (fork dedupes strings and compresses source refs). |
| Polling interrupts on every conditional jump | QB (L19191-L19230) | Calls plus back-edges bound the work between polls just as well and poll less (B10, Q1). |
| Loading unverified bytecode | QB/QN (documented as unsafe) | MoonSharp should not adopt "trusted dumps only" as policy; Q6's verifier is cheap. |

---

## Found in passing (MoonSharp, outside this report's scope)

- `LeaveProcessor` sets `m_OwningThreadID = -1` even when `m_ExecutionNesting` is still above zero,
  so after a nested call returns, the cross-thread check is off until the next outer `Enter`
  ([MSF Processor.cs#L150-L153](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor.cs#L150-L153))
  [C by reading; impact small; upstream behaviour].
- P6's static event-subscription leak and Q2's unbounded `json.parse`/`json.serialize` recursion
  belong in PLAN §7.

## Sources

- PuerTS changelog: https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/changelog.md
- PuerTS performance doc: https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/performance/index.md
- PuerTS filter doc: https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/wrapper/filter.md
- QuickJS changelog: https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/Changelog
- QuickJS documentation (bytecode trust): https://github.com/bellard/quickjs/blob/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b/doc/quickjs.texi#L860
- quickjs-ng security policy: https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/SECURITY.md
- QuickJS vendor page (size, startup claims): https://bellard.org/quickjs/
- Earlier fit survey: `scratchpad/research/landscape/js-family.md`; plan: `scratchpad/research/PLAN.md`.
