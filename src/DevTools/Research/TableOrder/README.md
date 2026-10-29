# Table iteration order spike

Scripts, probes and fuzzers behind [table-order.md](../../../../docs/research/table-order.md) (also cited
by [PLAN.md](../../../../docs/research/PLAN.md)). The spike itself is the branch `spike/table-order`
(`2c9e546`, `67c01be`, `51e5d38`, `97def54`), based on master `ed96417`; its build switches select the
three orders: (a) the default, (b) `-p:TableOrder=B`, (c) `-p:TableOrder=C`, and (d) one build that reads
`MOONSHARP_TABLE_ORDER` at run time (`-p:TableOrderDynamic=true`).

`results/` holds the `ab` and `workloads` outputs the note's tables come from (each under 2 KB; the DLL
paths in their headers are relative to the work folder). Nothing here is compiled.

| File | What it does |
|---|---|
| `build_variants.sh` | Builds the four variant DLLs and a copy of the spike's `Benchmarks` |
| `ab.sh`, `chain.sh` | Every A/B and memory run of the note, each under the benchmark lock |
| `summarize.py`, `geomean.py`, `wl.py` | The note's tables from `results/` (or another results folder) |
| `runtests.sh`, `runhost.sh` | Unit suite and hosting fixtures with one order |
| `runlua.sh`, `runlua-all.sh`, `runlua-mf.sh`, `cmp.py` | A Lua file under master and each order, and the comparison |
| `probe*.lua`, `host_*.lua` | Order and content probes |
| `fuzz_*.lua` | Fuzzers: insert, delete and queue patterns whose output must match master's |
| `scramble-probe.patch` | Part 1: a temporary patch to master's `Table` that scrambles `next`, `Pairs`, `Keys` and `Values` by `MOONSHARP_ORDER_PROBE` (`b`, `c1`, `c2`, `c3`); applies to `ed96417` and `fbd0367` |
| `masterfix.py` | Rewrites a master worktree's `Table.cs` so `CollectDeadKeys` collects every dead key |

## Inputs

A scratch folder `WORK` and worktrees, all built Release:

```sh
git worktree add --detach $WORK/spike origin/spike/table-order
git worktree add --detach $WORK/master ed96417
mise exec -- dotnet build -c Release -f netstandard2.0 $WORK/master/src/MoonSharp.Interpreter
mkdir -p $WORK/bin-master && cp $WORK/master/src/MoonSharp.Interpreter/bin/Release/netstandard2.0/MoonSharp.Interpreter.dll $WORK/bin-master/
mise exec -- dotnet build -c Release $WORK/master/src/DevTools/Benchmarks && cp -r $WORK/master/src/DevTools/Benchmarks/bin/Release/net8.0 $WORK/bench-master
mise exec -- dotnet build -c Release $WORK/master/src/MoonSharp && cp -r $WORK/master/src/MoonSharp/bin/Release/net8.0 $WORK/repl-master
```

`bin-2c9e546` is the interpreter built at the spike's first commit the same way. For `repl-masterfix`, run
`python3 -I masterfix.py <a second master worktree>` and build its `src/MoonSharp`. The spike's REPL, unit
runner and host CLI are built in `$WORK/spike` (`src/MoonSharp`, `src/TestRunners/DotNetCoreTestRunner`,
`src/Hosting/MoonSharp.Hosting.Cli`).

## Run

From the repository root, with `t=src/DevTools/Research/TableOrder`; build outside the lock.

```sh
SPIKE=$WORK/spike WORK=$WORK mise exec -- $t/build_variants.sh
WORK=$WORK mise exec -- $t/chain.sh                  # writes $WORK/results/*.md
python3 -I $t/summarize.py $WORK/results; python3 -I $t/geomean.py $WORK/results; python3 -I $t/wl.py $WORK/results

SPIKE=$WORK/spike mise exec -- $t/runtests.sh c $WORK/tests_c.txt
SPIKE=$WORK/spike mise exec -- $t/runhost.sh c
SPIKE=$WORK/spike WORK=$WORK mise exec -- $t/runlua-all.sh $t/fuzz_order.lua > $WORK/fuzz_order.out
python3 -I $t/cmp.py $WORK fuzz_order

# Part 1 scramble: on a master worktree with scramble-probe.patch applied and src/MoonSharp built
SPIKE=<that worktree> mise exec -- $t/runlua.sh c1 $t/probe1.lua
SPIKE=<that worktree> mise exec -- $t/runtests.sh c1 $WORK/tests_probe_c1.txt
```

Without a folder argument the three table scripts read the committed `results/`. The shell scripts print
their usage when the arguments or variables they need are missing. `ab.sh` and `chain.sh` take the shared
benchmark lock (`BENCH_LOCK`, default `/tmp/moonsharp-bench.lock`).
