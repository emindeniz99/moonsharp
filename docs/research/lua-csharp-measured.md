# Lua-CSharp versus MoonSharp: measured

Date: 2026-10-28. Everything below was run on this machine for this note unless it says otherwise. The only numbers
not measured here are the native Lua 5.5.1 column, copied from `ms-docs/docs/comparison-xlua-ng.md`.

Short answer:

- On the 18 pure-Lua workloads, Lua-CSharp 0.5.8 ran 1.0x to 14x faster than MoonSharp. The median speed-up was
  about 4x. The earlier "5-10x" estimate holds for loops, arithmetic, tables and `string.format` (7-14x).
  Calls, closures, OOP, patterns and small-table churn are 3-4.4x. String building, `table.concat` and
  coroutines are 1.0-2.2x.
- Lua-CSharp ran these workloads 1.0x to 6.3x slower than native Lua 5.5.1. It was faster than native on
  `table_sort_52`, but that comparison uses different scripts (see below).
- At the host boundary, calling into Lua was 1.45x faster with the README's array API and about 4x faster with
  its stack API. Lua calling C# was 4.2x faster. Lua-CSharp allocated 0-48 B per call, against MoonSharp's
  504-952 B.
- A fresh `LuaState` with the standard library took 43 µs and retained 25 KB. A fresh MoonSharp `Script` took
  1.19 ms and retained 80 KB.
- Lua-CSharp `main@a8d84c7` was **not measured**: building it was blocked (see "Not measured").

## Setup

| | MoonSharp | Lua-CSharp |
|---|---|---|
| Source | fork `origin/master` **8933421** (merge of #8), detached worktree at `scratchpad/ms-master` | nuget.org **LuaCSharp 0.5.8**, pinned `[0.5.8]` |
| Commit | `89334213e7f2a5a3d28a233ed55dcaa89c13c8c3` | nuspec `<repository commit="c735770a21407226cfd1204bbe11a25de27cddf9">`, which is "Merge pull request #371 from nuskey8/codex/release-0.5.8". It is 32 commits behind the `a8d84c7` the research note read, and 22 files under `src/Lua` differ, including the stdlib and VM-semantics PRs #377-#383. |
| Binary | `MoonSharp.Interpreter.dll` netstandard2.0, Release, built with `dotnet build -c Release -f netstandard2.0`; sha256 `b550cbaf…5b204e69` | `lib/net8.0/Lua.dll` from the package (sha256 `c52eee64…e036f9`), plus `Lua.Annotations.dll`. The nupkg's sha256 is `051cc0cc…0d7b9c`. |
| Language level | Lua 5.2, doubles only | Lua 5.2, doubles only |
| Host app | `scratchpad/luacs-bench` (`LuaCsBench.csproj`, `Program.cs`), net8.0, Release, `InvariantGlobalization=true`, default GC (workstation, concurrent) and default tiered JIT | same process style |
| Runtime | .NET 8.0.33 (RyuJIT), built with SDK 10.0.403 | same |
| Machine | 4-vCPU Intel Xeon 2.1 GHz cloud VM, 15 GB RAM, Ubuntu 24.04.5, kernel 6.18.44 | same |

The native column comes from `comparison-xlua-ng.md`: xLua-NG `3042ef6`, Lua 5.5.1, gcc `-O2`, `flag_bench.c`,
7 runs, pinned to one core, on the same VM type. It was not re-run for this note. Its MoonSharp column was
measured at `fbd0367`, not at 8933421.

### Method

- **Workloads.** The 18 Lua sources, iteration counts and expected checks are copied verbatim from
  `src/DevTools/Benchmarks/LuaWorkloads.cs` at fork master (blob `0930391`, unchanged on the
  `chore/devtools-benchmarks` branch). For each case and engine the harness:
  - creates a fresh engine with the standard library: `new Script()` for MoonSharp, `LuaState.Create()` plus
    `OpenStandardLibraries()` for Lua-CSharp;
  - compiles the chunk once (`DoString` / `DoStringAsync`), which returns `function(n)`;
  - makes one warm-up call with `n/10`;
  - then makes 7 timed calls with `n`.

  The Stopwatch wraps only the call. Allocation is `GC.GetAllocatedBytesForCurrentThread()` read just outside
  the Stopwatch, divided by iterations. `GC.GetTotalAllocatedBytes(true)` (process-wide) was recorded as well
  and matched in every case.
- **Rounds.** Each engine runs in its own process. One round runs, in order: MoonSharp (A), Lua-CSharp,
  MoonSharp (B). There were **7 rounds**. A process reports the median of its 7 inner runs. The tables below give
  the median across rounds, with the interquartile range (Q1-Q3, inclusive method) in parentheses. The
  MoonSharp A and B series form the **A/A check**.
- **Driving Lua-CSharp synchronously.** Its API returns `ValueTask`. The harness calls
  `vt.IsCompletedSuccessfully ? vt.Result : vt.AsTask().GetAwaiter().GetResult()`. Over all 7 rounds,
  112,001,218 ValueTasks completed synchronously and **0** asynchronously, so the `AsTask` fallback never ran.
  Its cost is measured in boundary case (a3) below.
- **Locking.** Every process ran under
  `flock scratchpad/bench.lock` with `timeout 900`. Rounds 1-4 took the lock per process. Rounds 5-7 took it once
  per round (`run-round-locked.sh`), because other agents' queued benchmarks kept winning the lock between
  processes. Both ways, no other process holding the lock ran at the same time. Unlocked work by other agents
  did run alongside rounds 1-4 (load average up to 5.2 on 4 vCPUs). The A/A spread below includes that noise.
- **Discarded data.** These runs are kept but not used:
  - one partial round against MoonSharp `ed96417`, the master before #8
    (`results/discarded-ms-ed96417`);
  - two partial round-5 attempts, cut off by the 2-hour background limit and by a restart
    (`results/discarded-partial-r5*`).

### Noise (A/A, MoonSharp against itself)

- Per workload, the median of the 7 rounds for A divided by the same for B was 0.95-1.07 for 20 of 21 cases.
  The exception was `table_concat` at 1.18.
- Single-round A/B ratios ranged from 0.50 to 1.73, mostly in rounds 1-4 while other work shared the CPU.
- **Read ratios below about 1.2x as noise.** Every engine-to-engine difference called out in this note is 1.4x
  or larger, except `coroutine_switch` and `table_concat`, which this note calls "no clear difference".

## Results: pure Lua workloads (ns per iteration, .NET 8, default tiered JIT, 7 rounds)

MoonSharp is series A. Each cell is the median (Q1-Q3).

| Workload | native Lua 5.5.1 (doc) | MoonSharp 8933421 | Lua-CSharp 0.5.8 | MoonSharp / Lua-CSharp | Lua-CSharp / native | MoonSharp / native |
|---|---:|---:|---:|---:|---:|---:|
| empty_loop | 3.5 | 56.5 (55.2–58.6) | 5.8 (5.2–7.0) | 9.8x | 1.6x | 16.2x |
| function_call | 26.0 | 344.5 (336.6–349.1) | 89.3 (82.2–113.8) | 3.9x | 3.4x | 13.2x |
| method_call | 66.3 | 620.0 (595.9–653.0) | 171.0 (162.0–202.7) | 3.6x | 2.6x | 9.4x |
| table_array_insert | 16.1 | 1,241 (1,153–1,283) | 87.6 (83.4–91.6) | 14.2x | 5.4x | 77.1x |
| table_array_read | 15.7 | 393.6 (373.4–481.8) | 47.7 (41.0–52.1) | 8.2x | 3.0x | 25.1x |
| table_hash_set_get | 35.8 | 738.5 (689.1–781.1) | 99.3 (98.4–102.8) | 7.4x | 2.8x | 20.6x |
| string_concat | 225.8 | 595.7 (578.1–621.8) | 266.3 (249.7–276.1) | 2.2x | 1.2x | 2.6x |
| string_format | 304.8 | 4,204 (3,932–4,309) | 319.8 (299.4–333.4) | 13.1x | 1.0x | 13.8x |
| closure_create_call | 94.0 | 611.0 (590.5–684.0) | 179.7 (177.6–192.3) | 3.4x | 1.9x | 6.5x |
| int_math | 11.3 | 253.2 (246.0–261.6) | 31.3 (30.8–33.0) | 8.1x | 2.8x | 22.4x |
| fib_recursive | 10,085 | 180,198 (176,510–190,306) | 41,421 (40,397–43,155) | 4.4x | 4.1x | 17.9x |
| float_math | 20.8 | 395.9 (386.7–426.5) | 40.8 (40.1–44.7) | 9.7x | 2.0x | 19.0x |
| string_pattern | 3,199 | 24,028 (23,921–24,958) | 6,474 (6,412–7,279) | 3.7x | 2.0x | 7.5x |
| table_sort_52 ¹ | 51,009 | 127,942 (125,971–141,719) | 30,585 (29,872–33,657) | 4.2x | 0.6x ¹ | 2.5x ¹ |
| table_concat | 869.2 | 1,468 (1,419–1,537) | 1,064 (992–1,161) | 1.4x ² | 1.2x | 1.7x |
| coroutine_switch | 118.9 | 777.0 (743.6–785.5) | 743.2 (730.3–751.9) | 1.0x ² | 6.3x | 6.5x |
| gc_small_tables | 128.8 | 1,072 (1,044–1,158) | 342.9 (335.6–369.1) | 3.1x | 2.7x | 8.3x |
| oop_inherit | 69.6 | 558.7 (530.6–621.9) | 172.4 (161.4–201.3) | 3.2x | 2.5x | 8.0x |

¹ Not the same script. The native run uses `table.move`, while MoonSharp and Lua-CSharp copy with a Lua loop
(the `_52` variant). The two managed engines ran the same script, so MoonSharp / Lua-CSharp is valid. Both
ratios against native are not.

² Within or near the A/A noise: `table_concat`'s own A/A was 1.18. Read both rows as "no clear difference".

The MoonSharp column agrees with the earlier `fbd0367` numbers in `comparison-xlua-ng.md` to within about 15%. In
this run, at 8933421, most cases were slightly faster, for example `string_format` 4,204 against 4,421 and
`table_concat` 1,468 against 1,598. This note does not treat that as a finding, because it is not an A/B run of
the two commits.

### Correctness

- Both engines produced the same check value in every round for all 18 workloads: the 16 with a fixed
  expectation, plus `method_call` = 7100000 and `table_sort_52` = 960000, which have no fixed expectation.
- Lua-CSharp failed no workload and returned no different result.
- The only harness-side mapping: for `empty_loop`, Lua-CSharp returns zero values. The harness reports that as
  `void` so MoonSharp's expectation applies unchanged.
- No Lua source had to change.

### Allocations (bytes per iteration, median of 7 rounds; identical in A and B)

| Workload | MoonSharp | Lua-CSharp |
|---|---:|---:|
| empty_loop | 48 | 0 |
| function_call | 552 | 0 |
| method_call | 696 | 0 |
| table_array_insert | 371 | 50 |
| table_array_read | 528 | 0 |
| table_hash_set_get | 912 | 0 |
| string_concat | 661 | 85 |
| string_format | 8,000 | 174 |
| closure_create_call | 912 | 200 |
| int_math | 288 | 0 |
| fib_recursive (per fib(12), 465 calls) | 301,368 | 0 |
| float_math | 576 | 0 |
| string_pattern | 49,856 | 1,680 |
| table_sort_52 | 128,128 | 12,344 |
| table_concat | 1,392 | 200 |
| coroutine_switch | 1,080 | 0 |
| gc_small_tables | 1,776 | 704 |
| oop_inherit | 600 | 0 |

"0" means below 0.02 B per iteration: a few hundred bytes per run of millions of iterations. Lua-CSharp allocates
only for objects the script itself creates (tables, closures, strings). MoonSharp allocates on every arithmetic
result, call and local read, as described in `lua-csharp.md` section 3.

### Sensitivity: tiered JIT off (`DOTNET_TieredCompilation=0`), 3 rounds, supplementary

Each interpreter loop is entered only 8 times per case: one warm-up and 7 timed calls. Under default tiering it
therefore runs as on-stack-replacement code with dynamic PGO, not as a call-counted tier-1 method. To check
whether that favours either engine, the workloads were re-run with tiering off, which means fully optimised JIT
code with no PGO. This used 3 rounds, below the 7-round minimum, so treat it as an indication only.

| Workload | MoonSharp, TC off | Lua-CSharp, TC off | MoonSharp / Lua-CSharp, TC off | same, default tiering (above) |
|---|---:|---:|---:|---:|
| empty_loop | 60.2 | 5.3 | 11.3x | 9.8x |
| function_call | 372.0 | 95.8 | 3.9x | 3.9x |
| method_call | 773.1 | 184.8 | 4.2x | 3.6x |
| table_array_insert | 1,224 | 71.0 | 17.2x | 14.2x |
| table_array_read | 464.8 | 39.3 | 11.8x | 8.2x |
| table_hash_set_get | 806.3 | 94.6 | 8.5x | 7.4x |
| string_concat | 679.1 | 316.4 | 2.1x | 2.2x |
| string_format | 5,241 | 333.4 | 15.7x | 13.1x |
| closure_create_call | 938.2 | 216.7 | 4.3x | 3.4x |
| int_math | 310.4 | 32.5 | 9.5x | 8.1x |
| fib_recursive | 206,643 | 52,226 | 4.0x | 4.4x |
| float_math | 495.0 | 52.4 | 9.4x | 9.7x |
| string_pattern | 26,704 | 9,566 | 2.8x | 3.7x |
| table_sort_52 | 145,231 | 81,056 | 1.8x | 4.2x |
| table_concat | 1,408 | 1,286 | 1.1x | 1.4x |
| coroutine_switch | 799.4 | 983.9 | 0.8x | 1.0x |
| gc_small_tables | 1,299 | 368.8 | 3.5x | 3.1x |
| oop_inherit | 664.0 | 215.5 | 3.1x | 3.2x |

- All values are ns per iteration, as the median of 3 rounds.
- The MoonSharp A/A (median of rounds) was 0.93-1.07.
- Round 1 ran while other agents' unlocked work pushed the load average up to 10.6, which widened some IQRs.
  Raw data and IQRs are in `luacs-bench/results/tc0-summary.md`.
- All checks matched. All 486 Lua-CSharp `ValueTask`s completed synchronously.

With tiering off, both engines were slower in most cases. The loop, arithmetic, table and `string.format`
ratios stayed similar or grew (MoonSharp / Lua-CSharp of 8.5-17x). Lua-CSharp lost more than MoonSharp on
`table_sort_52` (4.2x fell to 1.8x), `coroutine_switch` (1.0x fell to 0.8x, so MoonSharp was faster) and
`table_concat`. That suggests these paths rely on PGO, probably through guarded devirtualisation of
`ValueTask` and delegate calls; this is not verified. The default-tiering numbers above are the primary result.

## Results: host boundary (1,000,000 calls, 7 rounds)

| Case | MoonSharp ns/call | Lua-CSharp ns/call | MoonSharp / Lua-CSharp | MoonSharp B/call | Lua-CSharp B/call |
|---|---:|---:|---:|---:|---:|
| (a) C# calls a compiled Lua `function(a, b, c) return a * b + c end` | 491.7 (485.5–522.6) | | | 952 | |
| (a1) Lua-CSharp `CallAsync(fn, [i, 2, 3])` returning `LuaValue[]`, sync-driven | | 338.1 (332.2–343.1) | 1.45x | | 48 |
| (a2) Lua-CSharp stack API: `Push` x4, `CallAsync(funcIndex, returnBase)`, `ReadStack` | | 124.6 (121.6–140.2) | 3.9x | | 0 |
| (a3) same as a1, but `await` inside an `async Task` loop | | 316.7 (314.2–356.3) | 1.55x | | 48 |
| (b) Lua loop calls a C# function `f(i)` returning `i + 1` | 308.2 (296.8–340.0) | 73.8 (71.9–78.4) | 4.2x | 504 | 0 |

- MoonSharp (a) is `script.Call(fn, DynValue.NewNumber(i), two, three).Number`, with `two` and `three`
  pre-allocated. MoonSharp (b) is a `DynValue.NewCallback((ctx, args) => DynValue.NewNumber(args[0].Number + 1))`.
  Lua-CSharp (b) is `new LuaFunction((ctx, ct) => new ValueTask<int>(ctx.Return(ctx.GetArgument<double>(0) + 1)))`.
  Both bind `f` to a local upvalue (`local f = f`). Both engines return the same sums (1000002000000 and
  500001500000).
- MoonSharp's own A/A for these two cases was 0.99 and 1.03.
- **The sync wrapper costs nothing measurable.** Driving with `await` (a3) was 0.94x of the sync helper (a1),
  inside the noise.
- **The allocating part is the result array.** `CallAsync(LuaValue, ReadOnlySpan<LuaValue>)` allocates the
  48-byte `LuaValue[]` result. The stack API (a2) avoids it and runs 2.7x faster than (a1).

## Results: creating an engine (7 rounds; each round: 200 creations timed, then 100 kept alive for retained bytes)

| Engine and call | Time per create | Allocated per create | Retained per live instance |
|---|---:|---:|---:|
| MoonSharp `new Script()` (default modules) | 1,193 µs (1,055–1,239) | 185,712 B | 79,551 B |
| MoonSharp `new Script(CoreModules.Preset_Complete)` | 1,278 µs (1,224–1,421) | 193,944 B | 83,620 B |
| Lua-CSharp `LuaState.Create()` + `OpenStandardLibraries()` | 43.3 µs (41.5–44.1) | 28,720 B | 25,216 B |

- Retained bytes are the managed-heap growth over 100 live instances, using the method of
  `src/DevTools/Benchmarks/RetainedMemory.cs`. They were identical in all 7 rounds.
- MoonSharp's A/A for creation time was 0.95 (per round 0.71-1.22).
- An early harness version measured retained bytes inside the outer loop. On-stack replacement then let the GC
  collect the kept instances early, and runs 2..N read about 0 B. The measurement now runs in its own
  `AggressiveOptimization` method, and every run agrees. The numbers above come only from the fixed harness.
- Lua-CSharp's `OpenStandardLibraries` opens every library it has, including io, os and debug. The two engines'
  module sets are not identical.

## What had to differ between the engines

- **Scripts:** nothing. Every Lua source is byte-identical to `LuaWorkloads.cs`.
- **Host API shapes:** these necessarily differ: `DynValue` against `LuaValue`, `CallbackFunction` against
  `LuaFunction`, and sync against `ValueTask`, as described above.
- **Result formatting:**
  - MoonSharp's checks use `ToPrintString()`.
  - Lua-CSharp's use `LuaValue.ToString()`, which formats numbers with
    `double.ToString(InvariantCulture)`. That gives the same text as Lua's `%.14g` for every expected value
    here.
  - Zero results map to `void` (`empty_loop` only).

## Not measured

- **Lua-CSharp `main@a8d84c7` (2026-10-27).** This is the column the owner asked for, and it is missing. I
  fetched the commit into `scratchpad/ext/build/lua-csharp-main` and read the build files:
  - `src/Lua/Lua.csproj` has no `Target`, `Exec`, `UsingTask` or `Import`. It references the PolySharp 1.14.1
    package (a source generator that runs at compile time) and, on net6.0/netstandard2.1 only,
    `Microsoft.Bcl.TimeProvider` and `System.Runtime.CompilerServices.Unsafe`. It has `ProjectReference`s to
    `Lua.SourceGenerator` and `Lua.Annotations`.
  - `Directory.Build.props` sets package metadata only.
  - The referenced `Lua.SourceGenerator.csproj` has one custom target, `PackBuildOutputs`. It runs only for
    `pack`, adds items and has no `Exec`.
  - No `Directory.Build.targets`, `global.json` or `NuGet.config` applies to `src/` (the ones under `tools/` and
    `src/Lua.Unity` do not).

  So the files met the condition for building. However, the session's permission classifier denied
  `dotnet build -c Release -f net8.0 src/Lua/Lua.csproj` as running external code, and I did not try to work
  around it. To add the column, the owner can approve that build. Then point `LuaCsBench.csproj` at
  `ext/build/lua-csharp-main/src/Lua/bin/Release/net8.0/Lua.dll` (plus `Lua.Annotations.dll`) in a copy of the
  project, and run `run-round-locked.sh`. The harness compiled against the 0.5.8 API; whether it compiles
  unchanged against main is unchecked.
- **Lua-CSharp on .NET 10, NativeAOT, Unity Mono or IL2CPP, and phones.** Everything here is .NET 8 RyuJIT on a
  server CPU.
- **Lua-CSharp's own benchmark project and scripts.** These were deliberately not run.
- **Userdata and CLR object interop.** Not measured: MoonSharp's reflection descriptors against Lua-CSharp's
  `[LuaObject]` generator, as well as delegates, tables passed across the boundary, and strings across the
  boundary.
- **Other gaps:**
  - error paths (`pcall`, exceptions);
  - compile time of chunks;
  - coroutines driven from C#;
  - the hook and instruction-budget cost (Lua-CSharp issue #338);
  - memory per coroutine.
- **Native Lua 5.5.1.** Not re-run. Its column is copied, and only it uses `table.move` in `table_sort`.
- **Isolation.** CPU pinning and an otherwise idle machine. The A/A check bounds the effect at about ±20% per
  case (median of rounds).

## Files

All paths are under `/tmp/claude-0/-home-user/49abf7c9-632c-52bb-859e-20c0836e18dc/scratchpad/`:

- harness: `luacs-bench/Program.cs`, `luacs-bench/LuaCsBench.csproj`, `luacs-bench/NuGet.config`
- runners: `luacs-bench/run-rounds.sh` (rounds 1-4), `luacs-bench/run-round-locked.sh` (rounds 5-7 and the
  tiering-off runs)
- aggregation: `luacs-bench/aggregate.py`
- raw per-process JSON lines: `luacs-bench/results/default/`, `luacs-bench/results/tc0/`; aggregated output in
  `luacs-bench/results/default-summary.md` and `luacs-bench/results/tc0-summary.md`
- run logs with load averages: `luacs-bench/results-default.log`, `luacs-bench/results-tc0.log`
