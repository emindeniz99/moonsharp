# WattleScript vs MoonSharp vs Lua-CSharp: measured

Date: 2026-10-28. Replaces the mechanism-based WattleScript speed estimates in `other-managed.md`
(§9 ideas 2-5, 8 and open question 1). Marks: **[C]** measured here on this machine, **[R]** reused
from an earlier measurement in this session (stated where it came from).

## 1. Setup

| Engine | Exact source | How it was built |
|---|---|---|
| MoonSharp fork (main column) | `emindeniz99/moonsharp` origin/master `731b924dd7490882c9cee8e5aba62051f0d92a52` (2026-10-28 15:34 UTC) | its own `MoonSharp.Interpreter.csproj`, `-c Release -f netstandard2.0`, from a detached worktree |
| MoonSharp fork, earlier | origin/master `0c0e5d4` | same (measured before master moved; §4) |
| MoonSharp upstream | `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` | same |
| WattleScript | `WattleScript/wattlescript` `8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3` (HEAD, BSD-3) | own `OtherEngines/wslib/wslib.csproj`: compiles only `src/WattleScript.Interpreter/**/*.cs`, netstandard2.0, LangVersion 8 (as its own csproj), Release. None of its scripts, tools or sample projects were run |
| WattleScript history | 9 older commits of the same clone (§6) | `git archive` of the interpreter folder + the same `wslib.csproj` with `-p:InterpreterSrc` |
| Lua-CSharp | NuGet `LuaCSharp` 0.5.8 | **[R]** not rebuilt: the default series of [lua-csharp-measured.md](lua-csharp-measured.md), 7 rounds, same workload sources |

- Runtime: .NET 8.0.33 (CoreCLR, JIT, tiered PGO on by default), workstation concurrent GC, `InvariantGlobalization`.
  Harness exes target net8.0; all engine DLLs are netstandard2.0. SDK 10.0.403.
- Machine: 4 vCPU "Intel Xeon Processor @ 2.10GHz" VM, 1 thread per core, 15 GB RAM, Linux 6.18.44.
  Other agents worked on the machine throughout; their timing runs are serialised by the shared
  `bench.lock`, their builds are not. The 1-minute load average logged at each process start ranged
  from 1.0 to 20.4.
- Harness: `Program.cs` in [src/DevTools/Research/OtherEngines](../../src/DevTools/Research/OtherEngines/README.md) (one source, compiled per engine; namespace switch by
  `#if WATTLE`), with the build and run commands. It replaced an earlier draft with its own case list.
  - 18 pure-Lua workloads: sources and iteration counts copied verbatim from the fork's
    `src/DevTools/Benchmarks/LuaWorkloads.cs`. Copied at 0c0e5d4; the 18 Lua cases are byte-identical
    at 731b924, which only adds host cases (§8).
  - Interop: `i_clr_method_call` (Lua calls `v:Add(1)` on a registered userdata), `i_clr_prop_get`
    (Lua reads `v.Y`), `a_cs_to_lua_call3` (C# `Script.Call` of a Lua function, 3 number args),
    `b_lua_to_cs_call1` (Lua calls a `CallbackFunction`; extra, so Lua-CSharp has a counterpart),
    `c_create_default` (`new Script()`, batches of 200; plus retained heap for 100 live instances).
    Default `InteropAccessMode`, reflection descriptors, no Hardwire.
- Method: each engine runs in its own process; 7 inner runs per process after one warm-up call of
  n/10 iterations; the per-process median is one round; tables give the **median and IQR across 7
  rounds**. Allocated bytes per iteration = `GC.GetAllocatedBytesForCurrentThread()` delta around the
  timed call (median of rounds; the global counter agreed). Round order: `msA, ws, msB` (731b924 set)
  and `msA, ws, msup, msB` (0c0e5d4 set). Each round ran under
  `flock` on the shared benchmark lock with `timeout 3600`, each process under `timeout 900`. Scripts:
  `rounds.sh` (which now also does what `round.sh` and `rounds-from.sh` did), `aggregate.py`,
  `aggregate_hist.py`.
- Raw data: not kept. Aggregates, in `OtherEngines/results/`:
  `results-731-aggregate.txt`, `results-main-aggregate.txt`, `results-hist-aggregate.txt`.

### Noise (A/A)

`msA` and `msB` are the same binary run twice per round.

- 731b924 set: msA/msB ratio of medians 0.90-1.07 across the 23 cases; per-round ratios 0.76-1.42.
- 0c0e5d4 set: ratio of medians 0.89-1.15; per-round ratios 0.36-2.07 (load reached 20).
- Read differences of medians under about 10-15% as noise.

Cross-harness check for the reused Lua-CSharp numbers: the Lua-CSharp harness ([src/DevTools/Research/LuaCSharp](../../src/DevTools/Research/LuaCSharp/README.md)) also timed MoonSharp
(fork ed96417) in the same rounds as Lua-CSharp. Our 0c0e5d4 medians divided by its MoonSharp
medians give 0.90-1.07 (median 0.95) over 21 cases, so the two harnesses agree within that margin.
Lua-CSharp ratios below carry that extra ~10% uncertainty.

## 2. Results [C] (Lua-CSharp column [R])

ns per iteration, median (IQR) over 7 rounds. "MS/WS" > 1 means WattleScript is faster;
"WS/LC" > 1 means Lua-CSharp is faster. MS = fork 731b924.

| Workload | MoonSharp fork | WattleScript | Lua-CSharp 0.5.8 | MS/WS | WS/LC | B/iter MS / WS / LC |
|---|---:|---:|---:|---:|---:|---|
| empty_loop | 44.2 (42.2-47.3) | 52.3 (51.2-53.6) | 5.8 (5.2-7.0) | 0.84 | 9.1 | 48 / 0 / 0 |
| function_call | 276 (265-289) | 338 (328-349) | 89.3 (82.2-114) | 0.82 | 3.8 | 416 / 32 / 0 |
| method_call | 486 (475-501) | 804 (783-867) | 171 (162-203) | **0.60** | 4.7 | 512 / 32 / 0 |
| table_array_insert | 1,113 (1,090-1,132) | 224 (213-242) | 87.6 (83.4-91.6) | **4.97** | 2.6 | 371 / 42 / 50 |
| table_array_read | 314 (304-327) | 290 (279-319) | 47.7 (41.0-52.1) | 1.08 | 6.1 | 384 / 0 / 0 |
| table_hash_set_get | 581 (576-582) | 599 (570-626) | 99.3 (98.4-103) | 0.97 | 6.0 | 720 / 0 / 0 |
| string_concat | 413 (405-423) | 508 (482-531) | 266 (250-276) | 0.81 | 1.9 | 469 / 85 / 85 |
| string_format | 591 (576-629) | 2,563 (2,510-2,741) | 320 (299-333) | **0.23** | 8.0 | 688 / 2,349 / 174 |
| closure_create_call | 329 (325-342) | 629 (622-672) | 180 (178-192) | **0.52** | 3.5 | 584 / 464 / 200 |
| int_math | 198 (197-200) | 159 (156-172) | 31.3 (30.8-33.0) | **1.25** | 5.1 | 288 / 0 / 0 |
| fib_recursive (fib(12) per iter) | 130,500 (129,477-132,215) | 156,659 (153,871-161,212) | 41,421 (40,397-43,155) | 0.83 | 3.8 | 193,488 / 14,880 / 0 |
| float_math | 367 (357-372) | 319 (292-327) | 40.8 (40.1-44.7) | 1.15 | 7.8 | 576 / 0 / 0 |
| string_pattern | 23,318 (23,065-23,847) | 14,094 (13,824-14,377) | 6,474 (6,412-7,279) | **1.65** | 2.2 | 49,856 / 11,352 / 1,680 |
| table_sort_52 | 111,170 (107,291-115,984) | 183,349 (175,093-192,909) | 30,585 (29,872-33,657) | **0.61** | 6.0 | 115,848 / 19,072 / 12,344 |
| table_concat | 1,326 (1,222-1,521) | 1,715 (1,658-1,941) | 1,064 (992-1,161) | 0.77 | 1.6 | 1,392 / 824 / 200 |
| coroutine_switch | 724 (715-769) | 881 (853-906) | 743 (730-752) | 0.82 | 1.2 | 1,056 / 224 / 0 |
| gc_small_tables | 1,014 (1,002-1,051) | 1,001 (978-1,055) | 343 (336-369) | 1.01 | 2.9 | 1,736 / 544 / 704 |
| oop_inherit | 443 (425-451) | 679 (676-688) | 172 (161-201) | **0.65** | 3.9 | 464 / 32 / 0 |
| Lua -> C# callback, 1 arg | 278 (273-294) | 256 (247-270) | 73.8 (71.9-78.4) | 1.08 | 3.5 | 504 / 80 / 0 |
| C# -> Lua call, 3 args | 437 (423-443) | 476 (459-492) | 338 (332-343) | 0.92 | 1.4 | 720 / 224 / 48 |
| Lua -> C# method `v:Add(1)` | 300 (286-310) | 498 (488-514) | n/a | **0.60** | - | 440 / 312 / - |
| Lua -> C# property get `v.Y` | 233 (217-239) | 223 (218-241) | n/a | 1.04 | - | 312 / 24 / - |
| `new Script()` default modules (ns) | 174,652 (155,445-183,643) | 1,266,006 (1,245,344-1,374,718) | 43,319 (41,482-44,106) (stdlib) | **0.14** | 29 | 69,160 / 198,160 / 28,720 |

Retained heap per live `new Script()` (100 instances): MoonSharp fork 60,664 B, WattleScript
84,494 B, Lua-CSharp 25,216 B.

Summary of section 2:

- **WattleScript is faster than the fork on 4 of 23 rows**, beyond noise: table_array_insert 4.97x,
  string_pattern 1.65x, int_math 1.25x, float_math 1.15x.
- It is on par (within ±10%) on table_array_read, table_hash_set_get, gc_small_tables, the Lua -> C#
  callback, the property get and C# -> Lua calls.
- It is **slower on 13 rows**: `new Script()` 7.2x, string_format 4.3x, closure_create_call 1.9x,
  method_call and the CLR method call 1.7x, table_sort_52 1.6x, oop_inherit 1.5x, table_concat 1.3x,
  string_concat, function_call, coroutine_switch and fib_recursive 1.2x, empty_loop 1.2x.
- It allocates far less: 0-32 B/iter on the VM rows where the fork allocates 48-720 B. That byte cut
  does not turn into time on CoreCLR: the rows WattleScript wins are table insert, arithmetic and
  patterns, not the call-heavy ones.
- Lua-CSharp is 1.2-9x faster than WattleScript on every workload and call row with a counterpart,
  and 29x on engine creation.

## 3. DLL size (Release, netstandard2.0)

| Assembly | Bytes | gzip -9 |
|---|---:|---:|
| MoonSharp fork 731b924 (strong-name signed) | 404,480 | 169,007 |
| MoonSharp fork 0c0e5d4 | 392,704 | 162,194 |
| MoonSharp upstream fbd0367 | 372,224 | 154,271 |
| WattleScript 8feb3c6 (own csproj, unsigned) | 463,872 | 190,377 |

WattleScript is 59,392 B (15%) larger than the fork's master. The history builds (§6) show where:
the range 5079260..792aab7 (Wattle syntax, Regex string library) adds 54,784 B, and ccba46b..5079260
(the struct VM work plus the first C-like syntax) adds 43,520 B.

## 4. Earlier fork master and upstream [C]

Measured first, before the fork's master moved (7 rounds, same harness, order msA/ws/msup/msB).

| Workload | fork 0c0e5d4 | upstream fbd0367 | WattleScript (same rounds) |
|---|---:|---:|---:|
| function_call | 339 | 355 | 353 |
| method_call | 581 | 623 | 793 |
| table_array_insert | 1,180 | 1,173 | 224 |
| string_format | 4,122 | 4,085 | 2,596 |
| closure_create_call | 656 | 698 | 660 |
| int_math | 247 | 274 | 163 |
| fib_recursive | 173,654 | 188,075 | 163,794 |
| `new Script()` | 1,092,788 | 2,689,990 | 1,281,209 |

Between 0c0e5d4 and 731b924 the fork's perf branches landed: string_format 4,122 -> 591 ns,
closure_create_call 656 -> 329, `new Script()` 1.09 -> 0.17 ms, function_call 339 -> 276,
fib 174K -> 131K. Against 0c0e5d4, WattleScript was ahead on string_format (1.59x), fib (1.06x) and
equal on closures; those leads are gone at 731b924. WattleScript's own numbers agree across the two
sets within 7% on all VM rows except table_concat (12%).

## 5. What differs

- **No workload failed or computed a different value.** All checks matched on all engines,
  including method_call `7100000` and table_sort_52 `960000`; no engine threw.
- Both use the same default module preset (`Preset_SoftSandbox | LoadMethods | OS_System | IO`) and
  default (Lua) syntax; `ScriptSyntax.Lua` is WattleScript's default.
- WattleScript's string patterns run on .NET `Regex` (other-managed.md §2: semantic drift on edge
  cases). These 3 patterns give the same answers; equivalence in general was not tested.
- API: the same harness source compiles against both (`Script`, `DynValue`, `UserData.RegisterType`,
  `DynValue.NewCallback`, `ToPrintString`). `DynValue` is a struct in WattleScript, which this harness
  never compares with `null`.

## 6. Where WattleScript's numbers come from: history checkpoints [C]

Each checkpoint was built from the interpreter sources at that commit and run **3 rounds** (not 7)
with the same harness. Step ratios below 0.85 or above 1.15 are the ones to trust. Bytes per
iteration are deterministic (identical in every round).

| Checkpoint (range) | What it adds | Time, step vs previous | Bytes, step vs previous |
|---|---|---|---|
| ccba46b | MoonSharp-era base | empty 52, call 323, method 557, insert 1,129, int 207, fib 178K | 48 / 552 / 696 / 371 / 288 / 301K |
| abbf807 | struct `DynValue` + struct `Instruction` | **slower**: empty_loop 2.03, table_sort 1.50, int_math 1.29, table_concat 1.26, oop 1.26, method 1.22 | array_read 528->160, hash 912->272, int 288->160, fib 301K->130K; empty_loop 48->**160**; `new Script()` 2.4->4.5 MB |
| 023e8f5, 2127ac9 | table-set tweak; `ref` ScalarReference, arithmetic fast paths | insert 0.53, int 0.72, float 0.75, method 0.84, call 0.84 | loops 160->96 |
| 1daaff1 | open/closed upvalues | empty 0.68, insert 0.53, int 0.85 | empty/int/float/array_read/hash -> **0**; call 352->248 |
| 13 commits to 2b55309 | locals on the value stack, FastStack initial capacity | neutral (0.97-1.15); `new Script()` 0.41 | call 248->192, fib 115K->89K; `new Script()` 4.5 MB->368 KB |
| 19 commits to c4a2cc1 | struct `CallStackItem` (+ C-like syntax) | neutral to slower: call 1.12, method 1.14, fib 1.14 | call 192->40, fib 89K->18.6K, C#->Lua 424->272, coroutine 632->328 |
| 55 commits to 5079260 | 8-byte instructions + `FunctionProto` (3811213), 16-byte DynValue (f366e87) | int 0.79, empty 0.83, call 0.87, method 0.91 | `new Script()` 420->194 KB |
| 155 commits to 792aab7 | Wattle syntax, Regex string library | format 0.65, pattern 0.55; **regressions**: int 1.22, method 1.21, oop 1.20, table_sort 1.23, gc_small 1.21 | format 7.4K->2.4K, pattern 49K->11K |
| 4 commits to df931b6 | array part + single map tables (88c951c) | insert 0.70, table_sort 0.88; method 1.18, oop 1.19, table_concat 1.20 | insert 89->42, table_sort 35K->19K, gc_small 832->544 |
| 34 commits to 8feb3c6 | struct `ScriptExecutionContext`, `IEquatable`, descriptor cache, frame re-fetch | method 0.89, oop 0.88; table_concat 1.22 | callback 128->80, CLR method 360->312 |

End to end, from ccba46b to 8feb3c6 (time ratio, lower is faster): table_array_insert 0.19,
string_pattern 0.59, string_format 0.65, table_array_read 0.71, int_math 0.73, float_math 0.75,
table_hash_set_get 0.86, fib 0.88, empty_loop and function_call 1.00, coroutine 1.20, oop 1.31,
method_call 1.40, table_sort 1.56, table_concat 1.90.

## 7. What this means for our plan

Ranked by measured time payoff, using the history steps (§6) and the gap to the fork's master (§2):

1. **Array-part tables (PLAN B3; WattleScript 023e8f5 + 2127ac9 + 88c951c): biggest measured
   payoff.** table_array_insert went 1,129 -> 219 ns in three steps (0.53, 0.53 with upvalues, 0.70),
   and WattleScript is 4.97x ahead of the fork's master. The master already has the quick table fixes
   (key allocation, `__newindex` probe, length rescan) and still takes 1,113 ns, so the rest of the gap
   is structural. array_read (1.08x) and hash get/set (0.97x) show no gain, so the win is on the
   insert and grow path only. Watch the regressions this refactor brought: method_call and
   oop_inherit 1.18-1.19, table_concat 1.20.
2. **Open/closed upvalues and arithmetic fast paths (B2 part; 1daaff1, 2127ac9).** These made the
   loop rows allocation-free and gave the arithmetic wins that remain: int_math 1.25x and float_math
   1.15x over the master. empty_loop is still 0.84x, so dispatch costs as much as the saved
   allocations did.
3. **Struct instructions + constant pools (B4; 3811213, inside a 55-commit range):** 13-21% on
   int_math, empty_loop and function_call. That matches the earlier 10-20% estimate, but the range
   also contains f366e87 and many other commits, so the share is not isolated.
4. **Struct DynValue (B1; abbf807): a bytes win, not a time win on CoreCLR.** Alone it made 7 of 18
   VM rows 13-103% slower and none faster beyond noise, while cutting bytes 20-70% on most rows
   (empty_loop rose from 48 to 160 B). Only the later `ref` fast paths recovered the
   time. The estimate in other-managed.md, "2-4x on int_math/float_math/empty_loop", is refuted:
   the measured end state is 1.25x, 1.15x and 0.84x against the fork's master. Treat B1 as
   GC-pressure work, whose value depends on Unity's non-generational GC (not measured here).
5. **Struct frames and locals on the value stack (B2; 6ce39e6, 2b55309): bytes only.** They cut
   function_call to 40 B and fib from 89K to 18.6K B, with no measured time gain (steps 0.97-1.15).
   WattleScript's call rows end up slower than the fork's master: function_call 0.82, method_call
   0.60, fib 0.83, oop 0.65. Copy their allocation-free call path, not their call speed.
6. **Not worth copying: the Regex string library and WattleScript's `new Script()`.** The fork's
   `string.format` (23f44fe) is already 4.3x faster. Pattern matching is 1.65x faster in WattleScript,
   but on Regex with semantic drift; a port of `lstrlib.c` remains the better route (other-managed.md
   idea 9). `new Script()` is 7.2x slower than the master.

Overall, the struct-value/struct-frame design closes very little of the 3-9x gap to Lua-CSharp on
CoreCLR. The gains that survive are in the table insert path and in arithmetic/upvalue handling.

## 8. Not measured

- Unity Mono or IL2CPP (Boehm GC, where the byte cuts may matter more), NativeAOT, .NET 10, ARM,
  server GC.
- WattleScript's Hardwire source generator: the CLR rows use reflection descriptors on both sides.
- The host cases that 731b924 added to `LuaWorkloads.cs` (`clr_interop`, `clr_interop_reflection`,
  `enum_push`, `derived_type_push`, `table_insert_len_10k`, `table_remove_readd_10k`); only
  `cs_to_lua_call3` and `lua_to_cs_call1` match ours exactly.
- Single commits inside the large history ranges (3811213, f366e87 and 2342527 are bundled with other
  commits). The history ran 3 rounds, not 7.
- Lua-CSharp was not re-run; its numbers come from the Lua-CSharp harness (7 rounds, another harness, checked via
  MoonSharp to agree within 0.90-1.07).
- Debugger attached, WattleScript syntax mode, long-running retained-memory or GC-pause behaviour.
- WattleScript's own test suite was not run.
