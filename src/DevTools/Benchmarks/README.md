# MoonSharp benchmarks

Synthetic workloads for measuring the interpreter. Build in Release first:

```sh
dotnet build -c Release src/DevTools/Benchmarks
```

## Modes

**BenchmarkDotNet** (time and allocations per call):

```sh
dotnet run -c Release --project src/DevTools/Benchmarks -- --filter '*'
```

`LifecycleBenchmarks` covers creating scripts, coroutines and compiled chunks.
`ExecutionBenchmarks` covers calls to compiled functions: an eligibility rule,
recursion, an arithmetic loop, table inserts and lookups, string building.

**Retained memory** (bytes held by live `Script` and coroutine objects, which
the allocation counter does not show):

```sh
dotnet run -c Release --project src/DevTools/Benchmarks -- retained
```

**A/B comparison of two interpreter builds** in one process:

```sh
dotnet run -c Release --project src/DevTools/Benchmarks -- ab old/MoonSharp.Interpreter.dll new/MoonSharp.Interpreter.dll 21
```

Each build loads into its own `AssemblyLoadContext`. Rounds alternate between
the builds, so machine noise reaches both. The table reports the median of the
per-round ratios and their interquartile range. Run the same build against
itself first (an A/A run) to see the noise on your machine before trusting a
small difference.

Add `tables` after the rounds to run the table workloads instead (the table
cases of `workloads` plus delete/reinsert, an integer-key queue, `pairs` and
record building, timed per inner iteration), or `tablemem` with 0 rounds for
the bytes each build retains per small table:

```sh
dotnet run -c Release --project src/DevTools/Benchmarks -- ab old/MoonSharp.Interpreter.dll new/MoonSharp.Interpreter.dll 21 tables
dotnet run -c Release --project src/DevTools/Benchmarks -- ab old/MoonSharp.Interpreter.dll new/MoonSharp.Interpreter.dll 0 tablemem
```

**Pure Lua workloads** with the same sources and iteration counts as the
pure-Lua cases of xLua-NG's `Test/Native/flag_bench.c`, to compare MoonSharp
with a native Lua on the same machine:

```sh
dotnet run -c Release --project src/DevTools/Benchmarks -- workloads 7
```

`int64_math` is left out (it needs Lua 5.3 integers). `table_sort_52` copies
with a loop because Lua 5.2 has no `table.move`; its seed arithmetic exceeds
2^53, so its check value differs from Lua 5.3+. `table_insert_len_10k` and
`table_remove_readd_10k` are MoonSharp-only: they track table operations whose
cost used to grow with the table size.

The same mode also runs cases that flag_bench does not have: calls across the
C#/Lua boundary in both directions (`cs_to_lua_call3`, `lua_to_cs_call1`),
CLR method and property access (`clr_interop` in the default access mode,
`clr_interop_reflection` as IL2CPP gets it), pushing a registered enum value
(`enum_push`) and an instance of an unregistered subclass (`derived_type_push`).
Name cases after the run count to run only those:

```sh
dotnet run -c Release --project src/DevTools/Benchmarks -- workloads 7 function_call fib_recursive
```

Every case reports the exact bytes allocated per iteration next to the time.
Bytes do not depend on machine load, so they are the better evidence for a
change that removes allocations. The unit tests pin them: see
`src/MoonSharp.Interpreter.Tests/EndToEnd/AllocationBudgetTests.cs`.

**Allocations by type** for one or more cases (sampled from the runtime's
allocation events, about one sample per 100 KB):

```sh
dotnet run -c Release --project src/DevTools/Benchmarks -- alloc function_call
```

## Notes

- A `Script` keeps every chunk it compiles. Compiling repeatedly into one
  long-lived `Script` grows memory until the process runs out, so the compile
  benchmark uses a fresh `Script` each time.
- Report the machine, OS, .NET version and commit with any numbers.
