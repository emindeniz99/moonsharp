# Compile time and the gen2 bisect

`CFlow` loads one MoonSharp build into its own `AssemblyLoadContext` and times `Script.LoadString` in
batches of 64 loads of Lua-CSharp's `control-flow` source (or one of its parts), reading the thread's CPU
time, the GC pause time and the collection counts per batch. It found the gen2 collection that made
`next` look 2.9x slower than MoonSharp 2.0.0 in Lua-CSharp's compilation suite, and `bisect-step.sh` found
the commit that introduced it (`a700bca`).

Cited by [lua-csharp-own-bench.md](../../../../docs/research/lua-csharp-own-bench.md), "The control-flow
compile result". The Lua block comes from Lua-CSharp; its copy and MIT notice are in
`src/DevTools/Benchmarks/LuaCSharpScripts.cs`, which this project compiles in.

The project is standalone: no solution, test runner or Dockerfile builds it.

## Inputs

- A scratch folder (`WORK`) holding a detached worktree of this repository and the builds:
  `git worktree add --detach $WORK/wt origin/next`.
- MoonSharp 2.0.0 for the `nuget` label: `MoonSharp.Interpreter.dll` from the `MoonSharp` 2.0.0 package
  (`lib/netstandard1.6`) in the NuGet cache. Restoring Lua-CSharp's `sandbox/Benchmark` puts it there, or
  pass the path of any copy instead of `nuget`.

## Build and run

From the repository root:

```sh
mise exec -- dotnet build -c Release src/DevTools/Research/CompileBisect -o $WORK/cflow
export WORK CFLOW=$WORK/cflow/CFlow.dll

# Interpreter builds at the commits to compare
mise exec -- src/DevTools/Research/CompileBisect/build-at.sh fbd0367
mise exec -- src/DevTools/Research/CompileBisect/build-at.sh 92c34aa

# The forced-GC table: a full GC before each batch, as BenchmarkDotNet does
CFLOW_CLEAN=1 mise exec -- src/DevTools/Research/CompileBisect/run.sh "nuget fbd0367 92c34aa" "full:40:batch" 15
# Steady state: no forced GC, 40 batches; "fresh" loads each chunk into its own Script
mise exec -- src/DevTools/Research/CompileBisect/run.sh "nuget fbd0367 92c34aa" "full:40:batch full:40:fresh" 40
# One construct alone: localfn, repeat, whilegoto, while, gotoonly, forbreak, plain
mise exec -- src/DevTools/Research/CompileBisect/run.sh "fbd0367 92c34aa" "plain:640:batch" 15

# The bisect (fbd0367 good, 92c34aa bad)
git -C $WORK/wt bisect start 92c34aa fbd0367
mise exec -- git -C $WORK/wt bisect run "$PWD/src/DevTools/Research/CompileBisect/bisect-step.sh"
```

`CFLOW_TICKS=1` also prints the GC starts and allocation ticks by type. `run.sh` holds the shared
benchmark lock (`BENCH_LOCK`, default `/tmp/moonsharp-bench.lock`); the scripts print their usage when
called without arguments. Build outside the lock.
