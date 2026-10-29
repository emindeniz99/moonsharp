# Research harnesses

The programs and scripts behind the measurements in [docs/research](../../../docs/research/README.md), kept
so that a later session can reproduce or extend a published number from the repository alone. Each folder
has a README with what it measures, the note that cites it, its inputs and the exact commands.

| Folder | Measures | Cited by |
|---|---|---|
| [LuaCSharp](LuaCSharp/README.md) | MoonSharp against Lua-CSharp on the fork's workloads, Lua-CSharp versions against each other, Lua-CSharp's own BenchmarkDotNet suites | lua-csharp-measured.md, lua-csharp-own-bench.md, wattlescript-measured.md, PLAN.md |
| [CompileBisect](CompileBisect/README.md) | `Script.LoadString` time and GCs per build; the bisect to `a700bca` | lua-csharp-own-bench.md |
| [OtherEngines](OtherEngines/README.md) | MoonSharp fork and upstream builds against WattleScript and its history | wattlescript-measured.md, other-managed.md |
| [RegisterVm](RegisterVm/README.md) | The register VM spike on .NET, NativeAOT and Unity's Mono; instruction counts and coverage | register-vm-spike.md |
| [UnityLocal](UnityLocal/README.md) | The Unity test bed compiled with Unity's own compiler; workloads on Unity's Mono | register-vm-spike.md |
| [Profile](Profile/README.md) | CPU samples and allocation sites per workload; the prototype A/B | moonsharp-profile.md, PLAN.md |
| [TableOrder](TableOrder/README.md) | Table iteration order: probes, fuzzers, variant builds and A/B runs | table-order.md, PLAN.md |
| [Lua52](Lua52/README.md) | Lua 5.2 conformance: the runner, the library fuzzer, format and `bit32` diffs | lua52-conformance.md |
| [UnityEnhancedPoc](UnityEnhancedPoc/README.md) | Experiment A of the moonsharp-unityenhanced review and its probes | unityenhanced.md |
| [Browser](Browser/README.md) | Lua speed per browser build and cross-build loading | docs/hosting.md |

Rules for this folder:

- Nothing here is part of a solution, a test runner, the `Dockerfile` or a workflow. Each project builds
  on its own with `mise exec -- dotnet build -c Release <folder> -o <scratch dir>`, and the MoonSharp build
  to measure is a property (`MoonSharpDll`, `MoonSharpProject`, `MoonSharpSrc` or `EngineDll`), so the same
  harness measures any commit.
- Third-party code is not committed: Lua-CSharp, WattleScript and the Lua test suite are inputs that each
  README says how to fetch at a pinned commit or hash. Lua scripts copied from Lua-CSharp's benchmark live
  in `src/DevTools/Benchmarks/LuaCSharpScripts.cs` with their MIT notice, and the projects compile that
  file in.
- Timing scripts take the shared benchmark lock (`BENCH_LOCK`, default `/tmp/moonsharp-bench.lock`) and
  give every process a timeout. Build outside the lock.
- Results are not committed, except small aggregates that a note quotes (`results/` folders and
  `RegisterVm/b1/result.txt`).
