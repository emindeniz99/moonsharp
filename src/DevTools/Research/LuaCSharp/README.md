# Lua-CSharp comparison harnesses

Three tools behind the MoonSharp against Lua-CSharp numbers:

| Tool | Measures | Cited by |
|---|---|---|
| `LuaCsBench.csproj`, `run-rounds.sh`, `aggregate.py` | The 18 workloads of `src/DevTools/Benchmarks/LuaWorkloads.cs`, boundary calls and engine creation, on MoonSharp and Lua-CSharp, one engine per process | [lua-csharp-measured.md](../../../../docs/research/lua-csharp-measured.md), [PLAN.md](../../../../docs/research/PLAN.md), [wattlescript-measured.md](../../../../docs/research/wattlescript-measured.md) (Lua-CSharp column) |
| the same project built twice, `run-versions.sh`, `aggregate_versions.py` | Lua-CSharp 0.5.8 (NuGet) against another Lua-CSharp build on the same workloads | [lua-csharp-own-bench.md](../../../../docs/research/lua-csharp-own-bench.md), "Lua-CSharp main@a8d84c7 against 0.5.8" |
| `run-suites.sh`, `combine_bdn.py` | Lua-CSharp's own BenchmarkDotNet suites with the fork added as an engine | [lua-csharp-own-bench.md](../../../../docs/research/lua-csharp-own-bench.md) |

`results/default-summary.md` and `results/tc0-summary.md` are the `aggregate.py` output that
lua-csharp-measured.md quotes, with the interquartile ranges the note leaves out. The raw per-process
files were not kept.

The project is standalone: no solution, test runner or Dockerfile builds it. Commands run from the
repository root.

## Inputs

- The MoonSharp build to measure. The project references
  `src/MoonSharp.Interpreter/bin/Release/netstandard2.0/MoonSharp.Interpreter.dll` unless
  `-p:MoonSharpDll=<path>` names another one. The published runs used fork master `8933421`, built in a
  detached worktree with `dotnet build -c Release -f netstandard2.0`.
- Lua-CSharp. By default NuGet `LuaCSharp` `[0.5.8]` (restored from nuget.org only, see `NuGet.config`).
  For another version, build it yourself and pass `-p:LuaCsDir=<folder with Lua.dll and Lua.Annotations.dll>`.
  `main@a8d84c7` was built from <https://github.com/nuskey8/Lua-CSharp> at
  `a8d84c7c5e858a6118e2a9747f08856f5224793a` with `dotnet build src/Lua/Lua.csproj -c Release -f net8.0`.
  Read its build files before you build it (third-party code); lua-csharp-measured.md, "Not measured",
  lists what they contain.
- For `run-suites.sh`: a Lua-CSharp checkout at `a8d84c7` with
  [lua-csharp-own-bench.patch](../../../../docs/research/lua-csharp-own-bench.patch) applied, set up as
  lua-csharp-own-bench.md, "Reproducing", describes.

## Build

```sh
mise exec -- dotnet build -c Release -f netstandard2.0 src/MoonSharp.Interpreter
mise exec -- dotnet build -c Release src/DevTools/Research/LuaCSharp -o /tmp/luacs-058
# Lua-CSharp main instead of 0.5.8:
mise exec -- dotnet build -c Release src/DevTools/Research/LuaCSharp -o /tmp/luacs-main \
  -p:LuaCsDir=<Lua-CSharp>/src/Lua/bin/Release/net8.0
```

Build outside the benchmark lock, and use separate output folders: both builds share `obj/`, so build
them one after the other.

## Run

```sh
# One process: <workloads|boundary|create> <ms|luacs> [inner runs] [create case]
mise exec -- dotnet /tmp/luacs-058/LuaCsBench.dll workloads ms 7

# The published series: 7 rounds of everything, then aggregate
mise exec -- src/DevTools/Research/LuaCSharp/run-rounds.sh /tmp/luacs-058/LuaCsBench.dll /tmp/out/default 1 7 all
python3 -I src/DevTools/Research/LuaCSharp/aggregate.py /tmp/out/default

# The tiering-off series (3 rounds, workloads only)
mise exec -- src/DevTools/Research/LuaCSharp/run-rounds.sh /tmp/luacs-058/LuaCsBench.dll /tmp/out/tc0 1 3 workloads DOTNET_TieredCompilation=0

# 0.5.8 against main
mise exec -- src/DevTools/Research/LuaCSharp/run-versions.sh /tmp/luacs-058/LuaCsBench.dll /tmp/luacs-main/LuaCsBench.dll /tmp/out/main 1 7
python3 -I src/DevTools/Research/LuaCSharp/aggregate_versions.py /tmp/out/main

# Lua-CSharp's own suites, three runs, then one table per suite
mise exec -- src/DevTools/Research/LuaCSharp/run-suites.sh <Lua-CSharp> /tmp/out/bdn run1 InterpreterComparison CompilationComparison StartupComparison
python3 -I src/DevTools/Research/LuaCSharp/combine_bdn.py InterpreterComparison /tmp/out/bdn run1 run2 run3
```

The scripts print their usage when called without arguments. They take the shared benchmark lock
(`BENCH_LOCK`, default `/tmp/moonsharp-bench.lock`) once per round, as rounds 5-7 of the published run
did; rounds 1-4 took it once per process. Every process has a timeout.
