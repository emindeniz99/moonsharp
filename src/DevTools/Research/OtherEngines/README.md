# Other managed engines: MoonSharp builds and WattleScript

One `Program.cs`, compiled once per engine build, runs the workloads of
`src/DevTools/Benchmarks/LuaWorkloads.cs` (copied from fork master `0c0e5d4`), interop cases (CLR
callbacks, userdata fields, properties and methods) and engine creation. It prints JSON lines in the
format of [../LuaCSharp](../LuaCSharp/README.md), so the Lua-CSharp numbers can be merged in.

Cited by [wattlescript-measured.md](../../../../docs/research/wattlescript-measured.md) and
[other-managed.md](../../../../docs/research/other-managed.md). `results/` holds the three aggregates the
WattleScript note quotes (`aggregate.py` and `aggregate_hist.py` output); the raw per-process files were
not kept.

The projects are standalone: no solution, test runner or Dockerfile builds them.

## Inputs

- MoonSharp builds, netstandard2.0 Release, from detached worktrees: the fork at `731b924` (the main
  column) or `0c0e5d4`, upstream `fbd0367`:
  `git worktree add --detach /tmp/oe-731 731b924 && mise exec -- dotnet build -c Release -f netstandard2.0 /tmp/oe-731/src/MoonSharp.Interpreter`.
- WattleScript from <https://github.com/WattleScript/wattlescript> at
  `8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3` (BSD-3-Clause). Only its interpreter sources are compiled, by
  `wslib/wslib.csproj`; none of its own projects, scripts or tools run.
- For the history table, nine older WattleScript commits, each extracted with `git archive`:
  `ccba46b abbf807 2127ac9 1daaff1 2b55309 c4a2cc1` still use the MoonSharp namespace
  (`src/MoonSharp.Interpreter`), `5079260 792aab7 df931b6` use WattleScript's.

## Build

From the repository root; build outside the benchmark lock. `AssemblyName` sets the engine label the
program prints. The builds share `obj/`, so run them one after the other.

```sh
oe=src/DevTools/Research/OtherEngines
ns=src/MoonSharp.Interpreter/bin/Release/netstandard2.0/MoonSharp.Interpreter.dll
mise exec -- dotnet build -c Release $oe -p:AssemblyName=ms -p:EngineDll=/tmp/oe-731/$ns -o /tmp/oe/ms
mise exec -- dotnet build -c Release $oe -p:AssemblyName=msup -p:EngineDll=/tmp/oe-up/$ns -o /tmp/oe/msup

# WattleScript at HEAD
mise exec -- dotnet build -c Release $oe/wslib -p:InterpreterSrc=<wattlescript>/src/WattleScript.Interpreter -o /tmp/oe/wslib
mise exec -- dotnet build -c Release $oe -p:AssemblyName=ws -p:Wattle=true -p:EngineDll=/tmp/oe/wslib/WattleScript.Interpreter.dll -o /tmp/oe/ws

# A history checkpoint before the rename
mkdir -p /tmp/oe/src-ccba46b && git -C <wattlescript> archive ccba46b src/MoonSharp.Interpreter | tar -x -C /tmp/oe/src-ccba46b
mise exec -- dotnet build -c Release $oe/wslib -p:InterpreterSrc=/tmp/oe/src-ccba46b/src/MoonSharp.Interpreter -p:AssemblyName=MoonSharp.Interpreter -o /tmp/oe/lib-ccba46b
mise exec -- dotnet build -c Release $oe -p:AssemblyName=ws-ccba46b -p:EngineDll=/tmp/oe/lib-ccba46b/MoonSharp.Interpreter.dll -o /tmp/oe/ws-ccba46b
```

## Run

```sh
# 731b924 set (round order msA, ws, msB), then the table with Lua-CSharp's numbers merged in
mise exec -- $oe/rounds.sh /tmp/oe/results-731 1 7 msA=/tmp/oe/ms/ms.dll ws=/tmp/oe/ws/ws.dll msB=/tmp/oe/ms/ms.dll
python3 -I $oe/aggregate.py /tmp/oe/results-731 <run-rounds.sh outdir of ../LuaCSharp>

# History: one label per checkpoint, ws-<sha>, plus ws for HEAD
mise exec -- $oe/rounds.sh /tmp/oe/results-hist 1 3 ws-ccba46b=/tmp/oe/ws-ccba46b/ws-ccba46b.dll ws=/tmp/oe/ws/ws.dll
python3 -I $oe/aggregate_hist.py /tmp/oe/results-hist
```

One process: `dotnet /tmp/oe/ms/ms.dll <workloads|interop|create> [inner runs]`. `rounds.sh` takes the
shared benchmark lock once per round (`BENCH_LOCK`, default `/tmp/moonsharp-bench.lock`) and prints its
usage when called without arguments.
