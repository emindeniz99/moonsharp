# moonsharp-unityenhanced: experiment A and the probes

The measurements in [unityenhanced.md](../../../../docs/research/unityenhanced.md), section 4, made on
upstream `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` compiled from source:

| Mode | What it prints | Section |
|---|---|---|
| `bench` | ns and bytes per iteration for four loops (best of 7 calls of 1,000,000 iterations) | Experiment A table |
| `sem` | The eight-case semantics check; the base and the patched build must print the same lines | Experiment A |
| `startup` | Time and bytes of `new Script(...)` per core module preset | startup table |
| `voidchk` | What `Script.Call` returns for a function without `return`, and what `Table.Set` stores | the `Void` finding |

`poc.patch` is experiment A, the 30-line proof of concept of "immutable values, cells only for captured
locals" (section 4 lists its parts). It applies to `fbd0367` and is a measurement, not a proposed change:
upvalue reads still clone.

The project compiles the interpreter sources into the program, so one build measures one source tree. It
is standalone: no solution, test runner or Dockerfile builds it.

## Build and run

From the repository root:

```sh
git worktree add --detach /tmp/ue-base fbd0367
git worktree add --detach /tmp/ue-poc fbd0367
git -C /tmp/ue-poc apply "$PWD/src/DevTools/Research/UnityEnhancedPoc/poc.patch"

p=src/DevTools/Research/UnityEnhancedPoc
mise exec -- dotnet build -c Release $p -p:MoonSharpSrc=/tmp/ue-base/src/MoonSharp.Interpreter -o /tmp/ue/base
mise exec -- dotnet build -c Release $p -p:MoonSharpSrc=/tmp/ue-poc/src/MoonSharp.Interpreter -o /tmp/ue/poc

for b in base poc; do mise exec -- flock /tmp/moonsharp-bench.lock timeout 600 dotnet /tmp/ue/$b/UeProbe.dll bench; done
for b in base poc; do mise exec -- dotnet /tmp/ue/$b/UeProbe.dll sem; done
mise exec -- dotnet /tmp/ue/base/UeProbe.dll startup
mise exec -- dotnet /tmp/ue/base/UeProbe.dll voidchk
```

The two builds share `obj/`, so build them one after the other. Without `MoonSharpSrc` the build stops
with a message; without a mode the program prints its usage.
