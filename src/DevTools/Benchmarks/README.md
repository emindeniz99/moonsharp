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

## Notes

- A `Script` keeps every chunk it compiles. Compiling repeatedly into one
  long-lived `Script` grows memory until the process runs out, so the compile
  benchmark uses a fresh `Script` each time.
- Report the machine, OS, .NET version and commit with any numbers.
