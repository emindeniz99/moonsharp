# Register VM spike harnesses

The measurements in [register-vm-spike.md](../../../../docs/research/register-vm-spike.md). The spike
lives on the branch `spike/register-vm` (`6e8c79b` prototype and tests, `2a1e221` coverage counters), on
`next` `48fca10`.

| Folder | Measures | Section of the note |
|---|---|---|
| `bench/` | Every build in one process, each MoonSharp build in its own `AssemblyLoadContext`, plus Lua-CSharp; 7 workloads; rounds with a rotating engine order | "Measurements" |
| `b1/` | One engine per process, built per engine for NativeAOT (`net8.0`) or Mono (`net48`); `run.sh` interleaves the processes, `agg.py` aggregates; `result.txt` is the aggregate the note quotes | "NativeAOT and Unity Mono" |
| `t1/` | Instructions per iteration (the `InterruptHandler` count), bytes per execution (`alloc`), register code of a function (`dump`), and compile coverage (`coverage <files>`) | instruction counts, "Coverage of real code" |

`numeric-loop` and `nbody` come from Lua-CSharp's benchmark; the projects compile them from
`src/DevTools/Benchmarks/LuaCSharpScripts.cs`, which carries the MIT notice. The projects are standalone:
no solution, test runner or Dockerfile builds them.

## Inputs

- MoonSharp builds, netstandard2.0 Release (net45 for the Mono runs), from detached worktrees:

  ```sh
  git worktree add --detach /tmp/rv-next 48fca10
  git worktree add --detach /tmp/rv-spike 2a1e221        # origin/spike/register-vm
  mise exec -- dotnet build -c Release -f netstandard2.0 /tmp/rv-spike/src/MoonSharp.Interpreter
  mise exec -- dotnet build -c Release -f net45 /tmp/rv-spike/src/MoonSharp.Interpreter   # Mono only
  ```

  The DLLs land in `src/MoonSharp.Interpreter/bin/Release/<tfm>/` of each worktree. The note's first
  table used the spike at `6e8c79b`; `2a1e221` has the same VM code plus the counters `t1 coverage` reads.
- Lua-CSharp `main` at `a8d84c7c5e858a6118e2a9747f08856f5224793a` from <https://github.com/nuskey8/Lua-CSharp>,
  built with `dotnet build src/Lua/Lua.csproj -c Release -f net8.0` (read its build files first; it is
  third-party code). `LuaCsDir` is its `src/Lua/bin/Release/net8.0`.
- Unity's Mono for the Mono runs: `unity-mono-linux-2026-02-24.3.tar.gz` from
  <https://github.com/Benjamin-Dobell/unity-mono>, the archive the repository's `Dockerfile` pins, checked
  against the SHA-256 there and unpacked into a private prefix (not into `/`). Its `bin/mono` runs the
  `net48` builds.
- For `t1 coverage`: the Lua files to compile, for example `src/Hosting/fixtures/scripts/*.lua` and
  `src/MoonSharp.Interpreter.Tests/TestMore/*.t`. The note's third row used the Lua 5.2 suite copy in
  Lua-CSharp's `tests/Lua.Tests/tests-lua`.

## Build and run

From the repository root; build outside the benchmark lock.

```sh
# bench: two passes of 21 rounds
mise exec -- dotnet build -c Release src/DevTools/Research/RegisterVm/bench -p:LuaCsDir=<Lua-CSharp>/src/Lua/bin/Release/net8.0 -o /tmp/rv/bench
ns=src/MoonSharp.Interpreter/bin/Release/netstandard2.0/MoonSharp.Interpreter.dll
mise exec -- src/DevTools/Research/RegisterVm/run-bench.sh /tmp/rv/bench/bench.dll /tmp/rv/out 21 \
  next=/tmp/rv-next/$ns next2=/tmp/rv-next/$ns spike=/tmp/rv-spike/$ns spikeoff=/tmp/rv-spike/$ns:off luacs

# b1: one NativeAOT executable per engine (needs clang), one net48 executable per MoonSharp build
b1=src/DevTools/Research/RegisterVm/b1
mise exec -- dotnet publish -c Release -r linux-x64 -p:PublishAot=true $b1 -p:MoonSharpDll=/tmp/rv-next/$ns -o /tmp/rv/aot-next
mise exec -- dotnet publish -c Release -r linux-x64 -p:PublishAot=true $b1 -p:MoonSharpDll=/tmp/rv-spike/$ns -p:Spike=true -o /tmp/rv/aot-spike
mise exec -- dotnet publish -c Release -r linux-x64 -p:PublishAot=true $b1 -p:LuaCsDir=<Lua-CSharp>/src/Lua/bin/Release/net8.0 -o /tmp/rv/aot-luacs
net45=src/MoonSharp.Interpreter/bin/Release/net45/MoonSharp.Interpreter.dll
mise exec -- dotnet build -c Release $b1 -p:Mono=true -p:MoonSharpDll=/tmp/rv-next/$net45 -o /tmp/rv/mono-next
mise exec -- dotnet build -c Release $b1 -p:Mono=true -p:MoonSharpDll=/tmp/rv-spike/$net45 -p:Spike=true -o /tmp/rv/mono-spike
M=<unity-mono prefix>/bin/mono
$b1/run.sh /tmp/rv/runs 1 11 "aot_next=/tmp/rv/aot-next/b1 3" "aot_next2=/tmp/rv/aot-next/b1 3" \
  "aot_spike=/tmp/rv/aot-spike/b1 3" "aot_spikeoff=/tmp/rv/aot-spike/b1 3 off" "aot_luacs=/tmp/rv/aot-luacs/b1 3" \
  "mono_next=$M /tmp/rv/mono-next/b1.exe 3" "mono_next2=$M /tmp/rv/mono-next/b1.exe 3" \
  "mono_spike=$M /tmp/rv/mono-spike/b1.exe 3" "mono_spikeoff=$M /tmp/rv/mono-spike/b1.exe 3 off"
python3 -I $b1/agg.py /tmp/rv/runs

# t1: against the spike build at 2a1e221
mise exec -- dotnet build -c Release src/DevTools/Research/RegisterVm/t1 -p:MoonSharpDll=/tmp/rv-spike/$ns -o /tmp/rv/t1
mise exec -- dotnet /tmp/rv/t1/t1.dll                       # instructions per iteration, stack and register
mise exec -- dotnet /tmp/rv/t1/t1.dll alloc                 # bytes per execution
mise exec -- dotnet /tmp/rv/t1/t1.dll coverage src/Hosting/fixtures/scripts/*.lua
```

Each program prints its usage when called without the required arguments (`t1` with none runs the
instruction count). The scripts take the shared benchmark lock (`BENCH_LOCK`, default
`/tmp/moonsharp-bench.lock`) and print their usage when called without arguments. The `b1/` builds share
`obj/`, so run them one after the other.
