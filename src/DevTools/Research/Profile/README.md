# MoonSharp profile: driver, trace analyzer, prototype patch

The tools behind [moonsharp-profile.md](../../../../docs/research/moonsharp-profile.md) (profile of upstream
`fbd0367`), also cited by [PLAN.md](../../../../docs/research/PLAN.md).

| File | What it does |
|---|---|
| `app/` (`Profile`) | Workload driver: `bench [runs]` (ns and exact bytes per iteration), `loop <workload> <seconds>` (for `dotnet-trace`), `alloc <workload>` (allocation-tick histogram by type), `counts <workload>` (GC counts) |
| `analyzer/` (`Analyzer`) | Reads a `.nettrace` with TraceEvent 3.1.21: self and inclusive sample shares, caller-callee edges, the source line of the first MoonSharp frame, allocation bytes per type and allocating line |
| `run-all.sh` | Traces and analyses each workload, one at a time |
| `summarize.py`, `categorize.py` | The per-workload tables and the allocation categories of the note |
| `ab.sh`, `abtable.py` | The prototype A/B runs and their table |
| `proto-ABCDE.patch` | Changes A, C, D and E of section 7, applied together to `fbd0367` |

Not kept: the traces and their analyses (about 750 MB), `per-workload.md` and the other raw outputs
(`summarize.py` and `categorize.py` regenerate them from new traces), the snapshots of variants A, C and D
(the patch is the cumulative state E) and the unsafe experiment J (`Local`/`Upvalue` push the cell without
cloning; not kept as code).

The projects are standalone: no solution, test runner or Dockerfile builds them.

## Inputs

- An interpreter to profile. `Profile` references this repository's `MoonSharp.Interpreter.csproj` unless
  `-p:MoonSharpProject=<path>` names another one. The note profiled upstream `fbd0367`:
  `git worktree add --detach /tmp/prof-base fbd0367`, and for the prototype a second worktree with
  `git -C /tmp/prof-proto apply src/DevTools/Research/Profile/proto-ABCDE.patch` (run from the repository
  root, with the patch path made absolute).
- `dotnet-trace` 10.0.750501: `mise exec -- dotnet tool install dotnet-trace --version 10.0.750501 --tool-path /tmp/tools`.

## Build and run

From the repository root; build outside the benchmark lock.

```sh
p=src/DevTools/Research/Profile
mise exec -- dotnet build -c Release $p/app -p:MoonSharpProject=/tmp/prof-base/src/MoonSharp.Interpreter/MoonSharp.Interpreter.csproj -o /tmp/prof/base
mise exec -- dotnet build -c Release $p/app -p:MoonSharpProject=/tmp/prof-proto/src/MoonSharp.Interpreter/MoonSharp.Interpreter.csproj -o /tmp/prof/E
mise exec -- dotnet build -c Release $p/analyzer -o /tmp/prof/analyzer

# Baseline numbers and the allocation histogram
mise exec -- flock /tmp/moonsharp-bench.lock timeout 900 dotnet /tmp/prof/base/Profile.dll bench 5 > /tmp/prof/bench-baseline.md
mise exec -- dotnet /tmp/prof/base/Profile.dll alloc function_call

# Traces: 10 s per workload, then the tables
DOTNET_TRACE=/tmp/tools/dotnet-trace PROFILE=/tmp/prof/base ANALYZER=/tmp/prof/analyzer/Analyzer.dll \
  mise exec -- $p/run-all.sh /tmp/prof/traces empty_loop function_call fib_recursive
python3 -I $p/summarize.py /tmp/prof/traces
python3 -I $p/categorize.py /tmp/prof/traces /tmp/prof/bench-baseline.md

# Prototype A/B
mise exec -- $p/ab.sh base=/tmp/prof/base E=/tmp/prof/E base=/tmp/prof/base E=/tmp/prof/E > /tmp/prof/ab-run.md
python3 -I $p/abtable.py /tmp/prof/ab-run.md
```

Pass `--providers Microsoft-Windows-DotNETRuntime:0x100003801D:5` to `dotnet-trace` explicitly, as
`run-all.sh` does: the profile union of `dotnet-common` and `gc-verbose` drops the verbose
`GCAllocationTick` events. The scripts print their usage when called without arguments.
