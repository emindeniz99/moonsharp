# Lua-CSharp's own benchmark suite, with the fork added

Date: 2026-10-28. Every number here was measured on one machine for this note. The columns labelled
"earlier" come from runs made earlier the same day, on a busier machine and an older fork commit. The note
labels them every time they are used.

Short answer:

- **Lua-CSharp's suite** (`sandbox/Benchmark`, .NET 10, BenchmarkDotNet). It was run with the fork at
  `next` `e750daa` added as a fourth engine. Lua-CSharp `main@a8d84c7` is the baseline.
  - On execution, the fork is faster than Lua-CSharp on `patterns` (0.71x the time) and `math` (0.80x), and
    about even on `coroutines` (0.92x). It is 1.2x-2.4x slower on the other 8 cases.
  - The NuGet MoonSharp 2.0.0 that Lua-CSharp compares against is 2.0x-11.8x slower than Lua-CSharp, and
    1.7x-7.2x slower than the fork.
  - NuLua (native Lua 5.2 through P/Invoke) is faster than Lua-CSharp on 8 of 11 cases.
- **Startup:** the fork takes 23 µs (56 KB), against 2.2 ms (2.46 MB) for MoonSharp 2.0.0 and 8 µs for
  Lua-CSharp.
- **Compilation:** the fork is equal to 2.0.0 on most sources and 3.5x faster on the small loop. On
  `control-flow` the suite reports it 2.9x slower than 2.0.0. That is not slower compiling:
  - BenchmarkDotNet forces a full GC before every iteration. On that clean heap, 2.0.0 compiles a whole batch
    of 64 loads without a single GC, while the fork runs one blocking gen2 GC inside the timed batch.
  - The change dates from `a700bca`, which removed about 2 MB of large-object-heap stacks per `Script`.
  - With no forced GC, the fork's mean cost per load is lower than 2.0.0's (2.96 ms against 3.35 ms). See
    "The control-flow compile result".
- **Lua-CSharp main@a8d84c7 against its NuGet release 0.5.8**, on the fork's 18 `LuaWorkloads` (.NET 8,
  7 rounds):
  - main is faster on `int_math` (1.42x), `string_pattern` (1.38x) and `table_concat` (1.38x);
  - the other 15 cases are within noise.

## Setup

| | Value |
|---|---|
| Lua-CSharp | `github.com/nuskey8/Lua-CSharp` `main` at `a8d84c7c5e858a6118e2a9747f08856f5224793a` (2026-10-27). It was still the head of `main` when these runs ended. |
| Fork | `next` at `e750daa1246830b549d331b944d583b7d127d2df` (inline caches, instruction fusion, the new pattern matcher, typed calls). Earlier columns: `master` at `92c34aabb4cc01d349223ac01801584ceb41a5b3`. |
| MoonSharp 2.0.0 | NuGet `MoonSharp` 2.0.0, as pinned by Lua-CSharp's `Benchmark.csproj`. Restore picks `lib/netstandard1.6`. |
| NuLua | NuGet `NuLua.Lua52` 0.1.0 with its prebuilt `liblua52.so`, whose compiler flags are unknown. |
| Runtime | .NET SDK 10.0.403, runtime 10.0.14, RyuJIT x86-64-v4, BenchmarkDotNet 0.15.8, default tiering and PGO. The 0.5.8 comparison uses .NET 8.0.33. |
| Machine | 4-vCPU Intel Xeon 2.1 GHz cloud VM, 15 GB RAM, Ubuntu 24.04.5 |

### Reproducing

1. Clone Lua-CSharp at `a8d84c7` and apply [lua-csharp-own-bench.patch](lua-csharp-own-bench.patch) with
   `git apply --ignore-whitespace`. Their files use CRLF line endings, and this repository stores the patch
   with LF.
   - It adds a `MoonSharpFork` benchmark to the execution, compilation and startup suites, with the same
     setup, checks and adapter code as their `MoonSharp` one.
   - It adds the fork to `--verify`.
   - It changes no workload and no job setting.
2. Build the fork's interpreter with a different assembly name, so it can load next to the NuGet package, and
   put it where the patch expects it:

   ```
   dotnet build src/MoonSharp.Interpreter/MoonSharp.Interpreter.csproj -c Release -f netstandard2.0 \
     -p:AssemblyName=MoonSharpFork.Interpreter -o <Lua-CSharp>/sandbox/Benchmark/fork
   ```

3. From `sandbox/Benchmark`, run their README's commands. BenchmarkDotNet finds the project from the working
   directory, so run them from that directory:

   ```
   dotnet build Benchmark.csproj -c Release
   dotnet bin/Release/net10.0/Benchmark.dll --verify
   dotnet bin/Release/net10.0/Benchmark.dll --filter '*InterpreterComparison*' --exporters json
   dotnet bin/Release/net10.0/Benchmark.dll --filter '*CompilationComparison*' --exporters json
   dotnet bin/Release/net10.0/Benchmark.dll --filter '*StartupComparison*' --exporters json
   ```

   `--verify` passed on all four engines for all 15 workloads.

Method:

- Each suite was run three times, one suite at a time, with no other benchmark running.
- The tables give BenchmarkDotNet's per-run **median**, then the median of the three runs, with the
  min-max across runs.
- Ratios are medians of per-run ratios, so both sides of a ratio come from the same run.
- The load average during the three runs was 0.8-1.7 on 4 vCPUs. The earlier `92c34aa` runs had other
  agents' builds alongside them (load up to 11.6), and single runs there moved by up to 2x.

Their build files have no custom targets other than a pack-only one. `global.json` pins SDK 10.0.100 with
`rollForward: latestFeature`. The benchmark output also contains NLua/KeraLua's `liblua54.so`, used only by
their legacy suites.

## Execution (`InterpreterComparison`)

µs per execution of a retained compiled chunk. Median of 3 runs (min-max).

| Case | Lua-CSharp | NuLua | MoonSharp 2.0.0 | **Fork e750daa** | Fork 92c34aa (earlier) | NuLua / Lua-CSharp | 2.0.0 / Lua-CSharp | **Fork / Lua-CSharp** | **2.0.0 ÷ fork** |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| numeric-loop | 205.1 (203.8–211.3) | 136.2 (132.0–139.9) | 2,431 (2,409–2,517) | 336.4 (324.8–374.1) | 1,210 | 0.66x | 11.82x | 1.65x | 7.16x |
| calls-varargs | 275.1 (271.4–289.3) | 80.1 (79.1–80.2) | 1,137 (1,113–1,254) | 568.2 (556.1–592.9) | 664.5 | 0.29x | 4.19x | 2.02x | 2.00x |
| table-access | 317.9 (304.7–346.4) | 255.8 (249.7–277.9) | 950.1 (929.4–991.1) | 380.8 (376.8–391.5) | 659.9 | 0.80x | 2.92x | 1.23x | 2.52x |
| metamethods | 485.7 (483.7–577.1) | 170.8 (169.1–196.1) | 1,434 (1,426–1,472) | 747.1 (741.4–767.8) | 1,229 | 0.35x | 2.94x | 1.53x | 1.92x |
| math | 410.3 (402.3–447.0) | 259.8 (259.2–268.6) | 1,447 (1,432–1,465) | 331.5 (327.1–350.0) | 1,005 | 0.63x | 3.49x | **0.80x** | 4.38x |
| strings | 294.1 (283.3–326.2) | 351.0 (347.1–362.1) | 596.1 (592.6–647.6) | 357.5 (353.5–362.3) | 530.5 | 1.19x | 2.02x | 1.22x | 1.66x |
| patterns | 320.0 (290.8–348.5) | 188.5 (186.6–190.8) | 1,184 (1,147–1,190) | 225.9 (215.8–228.2) | 1,175 | 0.60x | 3.58x | **0.71x** | 5.21x |
| table-sort | 33.9 (33.5–36.7) | 66.3 (65.5–67.6) | 99.2 (96.6–99.5) | 50.7 (49.7–50.7) | 76.0 | 1.93x | 2.88x | 1.50x | 1.96x |
| coroutines | 299.8 (296.7–303.0) | 95.1 (91.9–96.5) | 1,426 (1,388–1,596) | 275.2 (273.4–278.1) | 414.4 | 0.32x | 4.71x | **0.92x** | 5.13x |
| host-calls | 124.8 (121.7–145.5) | 671.4 (619.4–685.4) | 445.2 (432.3–476.3) | 260.0 (255.9–290.0) | 304.4 | 5.38x | 3.46x | 2.14x | 1.71x |
| nbody | 537.6 (530.2–605.0) | 328.3 (325.9–330.9) | 3,564 (3,561–3,757) | 1,391 (1,280–1,517) | 1,952 | 0.61x | 6.72x | 2.41x | 2.56x |

Managed bytes allocated per execution. NuLua allocates on Lua's native heap, which `MemoryDiagnoser` does not
see.

| Case | Lua-CSharp | MoonSharp 2.0.0 | Fork e750daa | Fork 92c34aa (earlier) |
|---|---:|---:|---:|---:|
| numeric-loop | 0 | 3,840,744 | 168 | 168 |
| calls-varargs | 56 | 2,465,240 | 496,312 | 496,312 |
| table-access | 681,256 | 1,711,440 | 400,840 | 870,608 |
| metamethods | 2,008 | 2,628,368 | 1,776 | 2,576 |
| math | 0 | 3,265,544 | 168 | 1,412,168 |
| strings | 141,024 | 1,238,152 | 576,408 | 788,520 |
| patterns | 361,600 | 3,149,752 | 131,368 | 3,011,368 |
| table-sort | 24,656 | 149,856 | 17,200 | 60,096 |
| coroutines | 8,384 | 3,051,739 | 283,072 | 374,968 |
| host-calls | 0 | 912,896 | 396,528 | 396,528 |
| nbody | 3,952 | 6,005,400 | 246,728 | 448,144 |

What changed between `92c34aa` and `e750daa`:

- `patterns` went from even with 2.0.0 to 5.2x faster, with allocation down from 3.0 MB to 131 KB. That is
  the new pattern matcher.
- `math` lost its 1.4 MB of allocation.
- `numeric-loop` (3.6x), `table-access` (1.7x) and `coroutines` (1.5x) are faster than before.

The earlier and current runs come from different machine states, so treat the two fork columns as a trend,
not an A/B pair.

The fork still allocates on the paths where it is slowest:

| Case | Bytes per execution |
|---|---:|
| `calls-varargs` | 496 KB |
| `host-calls` | 397 KB |
| `strings` | 576 KB |
| `coroutines` | 283 KB |
| `nbody` | 247 KB |

Lua-CSharp allocates 0-56 B on `calls-varargs`, `host-calls` and `nbody`.

## Compilation (`CompilationComparison`)

µs per `Load`/`LoadString`. Median of 3 runs (min-max). Batches of 64 loads, each into a fresh state.

| Case | Lua-CSharp | NuLua | MoonSharp 2.0.0 | **Fork e750daa** | Fork 92c34aa (earlier) | Fork / Lua-CSharp | 2.0.0 ÷ fork |
|---|---:|---:|---:|---:|---:|---:|---:|
| numeric-loop | 2.48 (2.46–2.50) | 4.16 (3.84–6.72) | 17.6 (17.2–17.7) | 5.10 (4.79–5.16) | 4.52 | 2.04x | 3.47x |
| nbody | 99.7 (98.4–103.1) | 92.2 (90.7–97.2) | 170.6 (164.3–174.5) | 167.0 (166.1–172.6) | 153.6 | 1.67x | 1.02x |
| control-flow | 319.4 (315.7–326.7) | 287.2 (286.6–314.3) | 926.7 (915.5–926.9) | 2,712 (2,605–2,770) | 2,427 | 8.48x | 0.34x, see below |
| identifiers | 205.9 (201.2–207.3) | 146.4 (142.6–157.0) | 332.5 (327.3–335.8) | 353.5 (336.4–365.1) | 326.5 | 1.76x | 0.93x |
| plain-strings | 125.3 (124.3–125.6) | 82.8 (82.0–83.4) | 228.2 (220.2–228.6) | 225.0 (219.3–225.1) | 211.2 | 1.79x | 1.01x |
| escaped-strings | 160.1 (142.9–176.2) | 92.4 (89.4–93.0) | 249.6 (248.7–253.5) | 268.6 (253.8–287.7) | 245.9 | 1.68x | 0.93x |

Notes:

- BenchmarkDotNet warns that these iterations are below its recommended 100 ms. Their README says the same.
- On five of the six sources the fork allocates 3-6% more than 2.0.0 per load, for example 2,022,975 B against 1,915,935 B on
  `control-flow`.
- Apart from `numeric-loop` (3.5x faster) and `control-flow` (below), the fork is within 7% of 2.0.0.
- `identifiers` and `escaped-strings` read 7% slower than 2.0.0, at the edge of the run-to-run range. The cause
  was not investigated.

### The control-flow compile result

BenchmarkDotNet's compilation job calls `GC.Collect` before every iteration and times 64 loads into one fresh
`Script`. Their own README warns the batches are short.

The cases were reproduced outside BenchmarkDotNet with a small console program:

- It loads one MoonSharp build into its own `AssemblyLoadContext`.
- It calls `Script.LoadString` 64 times per batch on the `control-flow` source (40 repeats of a block with a
  local function, `if/elseif`, `repeat`, `while` with `goto`, and `for` with `break`).
- It reads the thread's CPU time (`clock_gettime(CLOCK_THREAD_CPUTIME_ID)`), `GC.GetTotalPauseDuration()` and
  `GC.CollectionCount`.
- It runs 10 warm-up batches, then 8-40 measured ones.

With a full GC before each batch, as BenchmarkDotNet does (15 batches, load average below 1):

| Build | CPU µs per load (median) | GC pause µs per load | GCs per 64-load batch |
|---|---:|---:|---|
| MoonSharp 2.0.0 | 1,080 | 0 | none |
| fork point `fbd0367` | 1,125 | 0 | none |
| `92c34aa` | 2,608 | 1,494 | one gen2 |
| `next` `9695eb7` | 2,754 | 1,606 | one gen2 |

Compiling itself is about the same in every build: 2.0.0 takes 1.08 ms, and `next` takes 2.75 ms minus a
1.61 ms GC pause, about 1.15 ms. The extra time is a single blocking gen2 GC that the fork runs inside the
timed batch.

**Bisect.** `git bisect run` between `fbd0367` (good) and `92c34aa` (bad) found the first bad commit:
**`a700bca` "perf(vm): allocate VM stacks on demand"**.

- Each step was classified by the number of gen2 GCs per batch, which is deterministic, not by time.
- Every bad commit showed exactly 1.0 gen2 per batch; every good one showed 0.

**Cause.**

- Before `a700bca`, every `Script` allocated a value stack and a call stack of 131,072 slots each, plus a
  16,384-slot loop tracker: about 2 MB of large-object-heap (LOH) arrays that stay alive.
- The runtime's GC sizes its LOH allocation budget from what survives on the LOH, so those arrays kept the
  budget high.
- Without them, nothing large survives. When 64 chunks pile up in one `Script`, `ByteCode.Code`
  (`List<Instruction>`) doubles into LOH arrays from 128 KB up to 2 MB.
- That growth exceeds the small budget. The GC event stream reports the collections it triggers with reason
  `AllocLarge`.
- No Lua construct is responsible. Each construct in the source scales the same way when tested alone.

**Steady state** is the same 64-load batches with no forced GC, 40 batches, load average below 1:

| Build | Mean CPU µs per load | Mean GC pause µs per load | gen0 / gen2 GCs per batch |
|---|---:|---:|---|
| MoonSharp 2.0.0 | 3,353 | 2,081 | 0.9 / 0.2 |
| fork point `fbd0367` | 3,168 | 1,929 | 0.9 / 0.1 |
| `next` `9695eb7` | **2,959** | **1,673** | 2.0 / 2.0 |

- One load into each of 64 fresh `Script`s, with no forced GC, gives a mean of 3,084 µs for 2.0.0, 2,308 µs
  for `fbd0367` and 2,320 µs for `next`.
- In a real process the garbage of earlier loads is still there. The old builds then pay for it in gen0 and
  gen1 collections that promote the parse trees.
- The fork's LOH-triggered collections come earlier and cost less in total. The 2.9x in the table above
  comes from BenchmarkDotNet measuring the first batch after a forced GC.
- In the steady-state runs, GC pauses (1.1-2.1 ms per load) cost about as much as compiling itself (about
  1.1 ms). Compile allocation (about 2 MB per load for 15 KB of source) is the lever for load time, not the
  order of collections.

## Startup (`StartupComparison`)

| | Lua-CSharp | NuLua | MoonSharp 2.0.0 | **Fork e750daa** | Fork 92c34aa (earlier) |
|---|---:|---:|---:|---:|---:|
| Time, median of 3 runs | 8.12 µs (7.60–8.27) | 37.0 µs (36.0–37.5) | 2,184 µs (2,171–2,341) | 23.4 µs (22.5–24.3) | 33.7 µs |
| Managed bytes | 22,472 | 496 | 2,460,795 | 56,352 | 69,504 |

The operation is one state lifecycle in a warm process:

- Lua-CSharp: `LuaState.Create()` + `OpenStandardLibraries()` + `Dispose()`;
- MoonSharp: `new Script(CoreModules.Preset_Complete)`;
- NuLua: `Lua52State.Create()` + `OpenLibraries()` + `Dispose()`.

## What their benchmarks measure, and how fair they are

What is timed:

- **Execution.** Each case runs a whole chunk that was compiled once and kept, and reads one number. The chunk
  rebuilds its data on every call. Loop counts are small (300-20,000), so per-chunk work such as creating
  local functions, tables and a coroutine is part of the time. The fork's own `LuaWorkloads` measure ns per
  iteration over 20,000-4,000,000 iterations instead. The two answer different questions.
- **Compilation** times `Load`/`LoadString` only.
- **Startup** is a warm-process lifecycle. It does not measure process start or first-use JIT.

Fair to each engine:

- **Checksums.** Every case has an independently computed expected value. It is checked in each benchmark
  process's setup, and twice per case by `--verify`.
- **The same source text** goes to every engine: ASCII, Lua 5.2 semantics, no engine-specific branches.
- **Each engine uses its cheap public path.** Lua-CSharp executes synchronously into a caller-owned
  result buffer. NuLua uses a registry reference and its stack API. MoonSharp uses
  `Script.Call(closure).Number`. Host callbacks use each engine's direct callback API, with no reflection
  binding.

Where the comparison tilts:

- **"MoonSharp" in their published results is the 2017 NuGet 2.0.0**, not this fork.
- **Compilation timing depends on where GCs fall.** The forced GC before each iteration lets an engine that
  allocates 2 MB per load finish a batch without collecting, and the cost then falls outside the timing (see
  above). Compare compile allocation alongside time.
- **Startup is not like for like.**
  - Lua-CSharp's `Dispose()` returns its stack data to a static pool (`LuaState.ThreadCoreData`), so a warm
    create-and-dispose loop reuses it. MoonSharp has no `Dispose`.
  - An earlier harness that kept 100 states alive measured Lua-CSharp 0.5.8 at 43 µs on .NET 8
    ([lua-csharp-measured.md](lua-csharp-measured.md)). That is five times this suite's 8 µs; how much of
    the difference is pooling was not isolated.
  - Module sets differ: `Preset_Complete` against Lua-CSharp's full standard library, including io, os and
    debug.
- **NuLua's "0 B" is not zero allocation.** Lua's heap is native.
- **`host-calls`** measures the embedding boundary as much as the VM. NuLua is the slowest engine there
  (5.4x Lua-CSharp), because every call crosses P/Invoke into a managed callback.
- **Low statistical power.** The execution job is one launch and five 250 ms iterations, so use the
  three-run medians. On a quiet machine the ranges above are mostly within ±10%.
- **JIT only.** .NET 10 with tiered PGO, after warm-up. Nothing here measures cold start, NativeAOT, Mono or
  IL2CPP. Lua-CSharp's `sandbox/Benchmark.NativeAot`, `tools/PerformanceProbe` and the legacy suites were
  not run.
- **Not profiled.** The fork's `ab` tool compares two MoonSharp builds, so it cannot explain the remaining gap
  to Lua-CSharp on `numeric-loop` and `nbody`, and it was not run. The allocation table above is the
  evidence so far.

## Lua-CSharp main@a8d84c7 against 0.5.8 on the fork's workloads

The harness from [lua-csharp-measured.md](lua-csharp-measured.md) was compiled, unchanged, twice:

- once against NuGet LuaCSharp `[0.5.8]`;
- once against `Lua.dll` built from `a8d84c7` (`dotnet build src/Lua/Lua.csproj -c Release -f net8.0`).

Each run used .NET 8.0.33 and 7 rounds. A round runs three processes in turn: 0.5.8 (A), main, 0.5.8 (B).
Each process reports the median of 7 inner runs per case. The 0.5.8 column pools A and B. Values are ns per
loop iteration.

| Workload | MoonSharp 8933421 (earlier note) | 0.5.8 (earlier note) | 0.5.8, this run | main@a8d84c7 | 0.5.8 / main | Rounds where main beat both 0.5.8 runs | 0.5.8 A/A |
|---|---:|---:|---:|---:|---:|---:|---:|
| empty_loop | 56.5 | 5.8 | 5.1 | 4.9 | 1.03x | 4/7 | 0.93 |
| function_call | 344.5 | 89.3 | 91.4 | 82.9 | 1.10x | 3/7 | 1.03 |
| method_call | 620.0 | 171.0 | 155.9 | 163.2 | 0.96x | 2/7 | 1.02 |
| table_array_insert | 1,241 | 87.6 | 78.8 | 78.3 | 1.01x | 4/7 | 1.00 |
| table_array_read | 393.6 | 47.7 | 40.8 | 38.6 | 1.06x | 5/7 | 0.99 |
| table_hash_set_get | 738.5 | 99.3 | 89.8 | 90.3 | 0.99x | 1/7 | 0.95 |
| string_concat | 595.7 | 266.3 | 245.8 | 240.4 | 1.02x | 4/7 | 1.07 |
| string_format | 4,204 | 319.8 | 309.4 | 283.6 | 1.09x | 4/7 | 1.12 |
| closure_create_call | 611.0 | 179.7 | 185.3 | 191.4 | 0.97x | 0/7 | 1.02 |
| **int_math** | 253.2 | 31.3 | 32.0 | **22.6** | **1.42x** | **7/7** | 1.12 |
| fib_recursive | 180,198 | 41,421 | 39,217 | 40,119 | 0.98x | 2/7 | 1.04 |
| float_math | 395.9 | 40.8 | 40.0 | 36.7 | 1.09x | 5/7 | 1.04 |
| **string_pattern** | 24,028 | 6,474 | 6,334 | **4,584** | **1.38x** | **7/7** | 1.07 |
| table_sort_52 | 127,942 | 30,585 | 34,260 | 34,381 | 1.00x | 1/7 | 1.03 |
| **table_concat** | 1,468 | 1,064 | 1,032 | **745.9** | **1.38x** | **6/7** | 1.12 |
| coroutine_switch | 777.0 | 743.2 | 709.0 | 762.3 | 0.93x | 1/7 | 0.98 |
| gc_small_tables | 1,072 | 342.9 | 304.1 | 294.2 | 1.03x | 5/7 | 1.04 |
| oop_inherit | 558.7 | 172.4 | 160.5 | 169.1 | 0.95x | 0/7 | 0.96 |

- **Correctness.** Every case returned the same check value in all 21 processes, and every `ValueTask`
  completed synchronously.
- **Allocation.** Allocations are identical between the versions except `string_pattern`, where they fell
  from 1,680 B to 1,600 B per iteration.
- **Clear speed-ups:** `int_math`, `string_pattern` and `table_concat`. They line up with three commits after
  0.5.8: "Streamline numeric VM dispatch", "align Lua pattern captures and replacement semantics" and
  "Reduce standard-library copies". Which commit causes which change was not checked [U].
- **Within noise:** `coroutine_switch` (0.93x) and `oop_inherit` (0.95x) lean slower, but neither crosses
  the 1.2x noise line used in [lua-csharp-measured.md](lua-csharp-measured.md).
- The fresh 0.5.8 column agrees with the earlier note's within 15%.
- The MoonSharp column is the earlier note's `8933421`. The fork has changed a lot since then, so read it
  only as context for the 0.5.8 columns.

## After `perf/table-layout` (2026-10-29)

Tables store `LuaValue`s and field caches are keyed by layout (ROADMAP, Done table). The three cases
the owner named, rerun with the same patch and setup on the same 4-vCPU VM, Lua-CSharp and the fork
only (`--filter '*InterpreterComparison.LuaCSharp' '*InterpreterComparison.MoonSharpFork'`, a
`LUACS_CASES` filter on the case list). Three runs per build, the builds interleaved; µs per execution,
median of the three runs (min-max), and the median of the per-run ratios to Lua-CSharp. Load average
1-3 (other agents' builds).

| Case | Lua-CSharp | Fork `next` 68d9693 | Fork `perf/table-layout` 500b848 | Ratio before | Ratio after | Bytes before | Bytes after |
|---|---:|---:|---:|---:|---:|---:|---:|
| nbody | 367.6-395.3 | 944.6 (937.7-972.5) | 759.8 (736.7-772.6) | 2.57x | 1.93x | 240,950 | 5,630 |
| table-access | 231.1-256.5 | 272.1 (268.4-279.5) | 250.4 (249.7-251.0) | 1.15x | 1.02x | 391,450 | 431,200 |
| metamethods | 325.1-340.8 | 521.1 (515.3-559.5) | 525.6 (513.9-532.7) | 1.59x | 1.56x | 1,730 | 1,880 |

- **nbody** no longer allocates per field store. Its remaining gap is instruction dispatch: under a
  sampling profile, table access went from about 30% of the samples to 10%, and the instruction loop
  is 83%.
- **table-access** allocates 10% more: each `{x=i,y=i%7}` gets four 56-byte hash entries, where it had
  four 24-byte entries and a 40-byte `DynValue` per key (the values were shared small integers).
- **metamethods** is unchanged in time: its cost is the two Lua calls per iteration, not the lookups,
  which no longer make a `DynValue` for the metamethod.
- **Startup** (`StartupComparison`, MoonSharpFork row, three runs each, after the module-table sizing
  commit): 18.90 to 18.21 µs, 56.04 to 56.64 KB. Before that commit the branch measured 21.0 µs and
  64.5 KB.

## After `perf/call-path`

Date: 2026-10-29. The same suite with two fork columns in the same runs: `next` at `5761ae2` (tables store
`LuaValue`) and branch `perf/call-path` merged with it, each built as in "Reproducing" under its own assembly
name (`MoonSharpForkNext.Interpreter`, `MoonSharpFork.Interpreter`). Lua-CSharp `a8d84c7`, .NET SDK 10.0.403,
runtime 10.0.14, the same 4-vCPU VM; three runs of `InterpreterComparison`, one suite at a time under the
shared benchmark lock (load average 1.0-2.6, other agents building alongside). Values: µs per execution,
median of the three BenchmarkDotNet medians (min-max); ratios are medians of per-run ratios.

| Case | Lua-CSharp µs | fork next µs | fork perf/call-path µs | MoonSharp 2.0.0 µs | NuLua µs | branch / next | branch / Lua-CSharp | next/Lua-CSharp |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| calls-varargs | 187.0 (186.1–190.2) | 407.0 (401.0–423.1) | 300.5 (292.7–301.1) | 880.0 (842.4–881.1) | 51.6 (50.8–52.1) | 0.72x | 1.61x | 2.15x |
| coroutines | 201.7 (200.2–206.0) | 205.2 (194.6–207.8) | 169.8 (167.7–175.1) | 1,247.2 (1,233.1–1,295.7) | 60.4 (59.0–62.5) | 0.85x | 0.84x | 1.02x |
| host-calls | 87.4 (84.9–89.0) | 196.6 (191.3–199.0) | 178.9 (176.5–181.2) | 344.8 (332.9–357.8) | 387.2 (386.9–393.7) | 0.91x | 2.04x | 2.24x |
| math | 265.8 (264.5–282.5) | 234.5 (225.0–235.2) | 231.3 (223.8–237.4) | 1,073.2 (1,065.5–1,075.1) | 166.3 (164.0–171.3) | 0.99x | 0.85x | 0.85x |
| metamethods | 342.0 (322.8–345.4) | 533.3 (507.7–533.7) | 527.7 (516.4–544.0) | 1,081.9 (1,052.6–1,113.7) | 110.9 (109.6–112.0) | 1.02x | 1.54x | 1.56x |
| nbody | 376.7 (365.2–390.3) | 761.6 (740.2–772.0) | 754.2 (739.7–757.2) | 2,616.1 (2,611.8–2,674.8) | 223.2 (217.5–224.1) | 0.99x | 2.00x | 2.02x |
| numeric-loop | 133.2 (129.4–135.1) | 247.1 (234.8–254.4) | 246.4 (236.8–249.0) | 1,816.7 (1,780.5–1,836.3) | 90.9 (89.0–94.7) | 0.98x | 1.83x | 1.91x |
| patterns | 215.1 (212.2–215.3) | 164.6 (162.0–166.2) | 155.8 (155.8–157.5) | 824.1 (822.1–886.7) | 128.0 (127.8–138.5) | 0.95x | 0.73x | 0.77x |
| strings | 192.0 (190.5–198.9) | 240.6 (235.3–241.6) | 223.7 (220.7–224.9) | 441.8 (422.7–460.0) | 229.5 (229.0–231.1) | 0.93x | 1.15x | 1.25x |
| table-access | 237.5 (234.7–242.0) | 245.4 (240.3–245.5) | 252.1 (243.9–253.8) | 689.2 (682.4–699.1) | 154.6 (151.8–165.4) | 1.03x | 1.05x | 1.01x |
| table-sort | 24.2 (24.0–25.0) | 42.0 (42.0–42.3) | 41.7 (40.7–43.2) | 72.4 (70.9–72.7) | 42.1 (41.9–43.3) | 0.98x | 1.72x | 1.75x |

| Case | Lua-CSharp B/op | fork next B/op | fork perf/call-path B/op | MoonSharp 2.0.0 B/op | NuLua B/op |
|---|---:|---:|---:|---:|---:|
| calls-varargs | 56 | 496,312 | 184 | 2,465,240 | 0 |
| coroutines | 8,384 | 283,072 | 123,216 | 3,051,832 | 0 |
| host-calls | 0 | 396,528 | 300,400 | 912,896 | 0 |
| math | 0 | 168 | 40 | 3,265,544 | 0 |
| metamethods | 2,008 | 1,920 | 1,600 | 2,628,368 | 0 |
| nbody | 3,952 | 5,760 | 5,536 | 6,005,400 | 0 |
| numeric-loop | 0 | 168 | 40 | 3,840,744 | 0 |
| patterns | 361,600 | 131,368 | 92,840 | 3,149,752 | 0 |
| strings | 141,024 | 528,392 | 413,064 | 1,238,152 | 0 |
| table-access | 681,256 | 441,544 | 441,416 | 1,711,440 | 0 |
| table-sort | 24,656 | 33,528 | 33,304 | 149,856 | 0 |

- The call path work moves calls-varargs from 2.15x to 1.61x of Lua-CSharp (496 KB to 184 B a run: what is
  left is the chunk's own local function and the result), coroutines from 1.02x to 0.84x (283 to 123 KB),
  and host-calls from 2.24x to 2.04x (397 to 300 KB).
- host-calls still pays for its callback written the usual way: `DynValue.NewCallback`, `args[0].Number` and
  `DynValue.NewNumber`. The same callback written against the allocation-free API
  (`new CallbackFunction(...) { UsesArgumentsOnlyDuringCall = true }`, `args.NumberAt(i)`,
  `context.Return(x)`), as a separate `HostCallsApiComparison` class in the same harness, three runs:
  Lua-CSharp 86.4 µs (80.5-88.4), fork 118.3 µs (117.9-120.9), 1.37x, 40 B a run (the `DynValue` that
  `Script.Call` returns).
- The rows with no varargs and no C# calls in their loops (numeric-loop, nbody, table-access, table-sort,
  metamethods) stay within 0.98-1.03 of `next`; math (fast-called builtins) 0.99, strings and patterns
  (library calls) 0.93 and 0.95.
- The harness copy, with the second fork column and the API class, is not committed anywhere; the patch in
  [lua-csharp-own-bench.patch](lua-csharp-own-bench.patch) gives the one-fork version.
