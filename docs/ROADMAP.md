# Roadmap

This fork aims to be a fast, fully managed Lua 5.2 engine that keeps MoonSharp's
public API, CLR interop and debugger, and runs on:

- Unity 2021.3 LTS, 2022.3 LTS and Unity 6 LTS, with Mono and IL2CPP;
- .NET (desktop and server);
- browsers, through .NET WebAssembly;
- the JVM, through a .NET worker process today and in-process hosting later.

The plan takes the speed techniques of [Lua-CSharp](https://github.com/nuskey8/Lua-CSharp)
and the shell of MoonSharp: its API, interop, debugger and synchronous coroutines.
Breaking changes are allowed when they buy real speed or correctness; each one
goes into a migration guide in the same branch.

Everything planned or postponed is written here, so nothing lives only in a
chat or a pull request.

## Decisions

| Date | Decision | Reason |
|---|---|---|
| 2026-10-28 | Modernise MoonSharp in place, one measured step at a time (option B). | Lua-CSharp runs faster, but it cannot call CLR types you do not own, and it has no Hardwire, no debugger and no synchronous coroutine model, and it needs netstandard2.1 plus the `Unsafe` package. Its speed comes from struct values, a register VM and allocation-free calls, and all three can be written in safe C#. |
| 2026-10-28 | Checkpoint after the struct-value step: if loops and calls still run more than 4x slower than native Lua 5.5.1, decide whether to replace the compiler and VM core with a register VM behind the same public API (option C). | A register VM removes the remaining push/pop traffic but changes the dump format and debugger internals, so it should follow measurements, not precede them. |
| 2026-10-28 | 64-bit integers are an opt-in library type (`int64`, `uint64`), not a number subtype. | Costs nothing when unused and keeps Lua 5.2 semantics. Revisit once values are structs. |
| 2026-10-28 | No pull requests to upstream MoonSharp until the owner approves. | Owner's call. |
| standing | Safe C# only: no `unsafe`, no `System.Runtime.CompilerServices.Unsafe`. Public Unity mechanisms only: no libil2cpp patches. | Bugs must stay exceptions, not memory corruption. The `Unsafe` package also fails to load in some Unity 6 setups ([Lua-CSharp#337](https://github.com/nuskey8/Lua-CSharp/issues/337)). |
| 2026-10-28 | Work collects on the `next` branch and lands on `master` in batches with `git merge --no-ff`, after the full unit suite and the fixtures pass locally. Pull requests are optional. CI runs on pushes to `master` (not for docs-only changes), and a red `master` is fixed forward first. | Fewer round trips and fewer CI runs. CI does not run on branches, so local checks come first. |
| 2026-10-28 | The development toolchain is pinned in `mise.toml` (.NET SDK 10 with the .NET 8 runtime, Java 17, Maven, Node 24, CMake), following the shared stack's toolchain rules; CI installs it with `jdx/mise-action`. clang and zlib come from apt. | Local and CI builds use the same exact versions instead of whatever a machine or runner image carries. mise offers clang only through conda, whose sysroot fails the NativeAOT link (`cannot find -lz`). |
| standing | Every performance change carries A/B numbers from `src/DevTools/Benchmarks`; every fix carries a test that fails without it. | See [upstream.md](upstream.md). |

## Platform targets

| Target | State in this fork |
|---|---|
| .NET 8 / .NET 10 | Tested in CI (unit tests, fixtures). |
| Browser (.NET 10 WebAssembly) | Chromium tested in CI with Playwright. Firefox and WebKit not tested. |
| Java 17+ (worker process) | Tested in CI, with a Spring Boot 4 sample. |
| Java in-process (hostfxr or NativeAOT) | Experimental, Linux x64: fixtures pass with CoreCLR and NativeAOT on branch `feat/jvm-in-process`. The worker stays the default ([hosting.md](hosting.md)). |
| Unity 2021.3 / 2022.3 / 6 LTS, Mono and IL2CPP | **Not tested.** The inherited Unity CI job uses a 2019.4 editor and skips without licence secrets. |

What Unity 2021.3 support means for the code:

- The interpreter targets `netstandard2.0` and `net45`; new code must build for both.
- No code generation at run time on IL2CPP: no `Reflection.Emit`, no compiled expression trees on AOT paths.
- Any Roslyn source generator must load in Unity 2021.3's compiler. Check the supported Roslyn version before building one.
- The UPM manifest still declares `"unity": "2020.3"`; raise it to 2021.3 once the CI matrix runs.

## Done (fork `master`)

| PR | Change |
|---|---|
| [#1](https://github.com/emindeniz99/moonsharp/pull/1) | A `Script` stays usable after a failed call or a stack overflow. |
| [#2](https://github.com/emindeniz99/moonsharp/pull/2) | Benchmarks: lifecycle, hot calls, A/B builds, native-Lua workloads. |
| [#3](https://github.com/emindeniz99/moonsharp/pull/3) | VM stacks grow on demand: `new Script()` 2.32 MB to 182 KB, a coroutine 2 MB to 2.9 KB. |
| [#4](https://github.com/emindeniz99/moonsharp/pull/4) | JSON numbers, string case and string order no longer depend on the process culture. |
| [#5](https://github.com/emindeniz99/moonsharp/pull/5) | Hosting: JSON host contract, browser build with a TypeScript API, Java worker, shared fixtures. |
| [#6](https://github.com/emindeniz99/moonsharp/pull/6) | Binary dumps keep source locations. |
| [#7](https://github.com/emindeniz99/moonsharp/pull/7) | Baseline measurements, upstream tracking, comparison with native Lua. |
| [#8](https://github.com/emindeniz99/moonsharp/pull/8) | The `io` module works on browser WebAssembly; trimmed apps keep the standard library. |
| [#9](https://github.com/emindeniz99/moonsharp/pull/9) | This roadmap. |
| branch `feat/exact-int64` | Exact 64-bit integers: opt-in `int64`/`uint64` values (`CoreModules.Int64`), CLR `long`/`ulong` in both directions (`ScriptOptions.ExactInt64Interop` for CLR to Lua), exact JSON on request. See [int64.md](int64.md). |

## In progress

| Work | Where |
|---|---|
| Table array part and the choice of `pairs` order | branch `spike/table-order` |
| Measured comparison with Lua-CSharp on the same workloads | report pending |
| Comparison of other engines (Lua-CSharp, Luau, Jint, xLua, xlua-ng, WattleScript, UniLua and others) and a profile of MoonSharp | to be published under `docs/research/` |
| In-process JVM hosting | branch `feat/jvm-in-process` |

## Next: the VM, in order

Today MoonSharp runs the benchmark workloads 10 to 20 times slower than native
Lua 5.5.1 ([comparison-xlua-ng.md](comparison-xlua-ng.md)).

1. **Cheap fixes.** Create closures without LINQ, add a number-number fast path for arithmetic, dispatch from an instruction array, share one empty argument array.
2. **Upvalue cells.** A closure today captures the local's `DynValue` object itself, which is why `DynValue` must stay a mutable class. Give captured locals their own cell. Every later step needs this.
3. **Struct call frames.** No heap allocation per call; locals live in a window of the value stack.
4. **Internal struct values.** The VM, tables and closures hold a struct; `DynValue` stays the public type and is created only at the API boundary.
5. **Table array part** with a cached length; the `pairs` order follows the owner's decision.
6. **Zero-copy calls into the standard library.** Arguments are read from the stack and results written back, with no per-call argument list.
7. **Compiler and dispatch.** Constant operands, fused compare-and-branch, superinstructions, inline caches for field and global access, a cache for absent metamethods, one counter for the debugger and the instruction budget.

After step 4: the checkpoint in the decisions table.

## Next: CLR interop

- Cache the resolved descriptor per runtime type.
- Stop allocating a bound callback on every `obj:Method()` call.
- Choose argument converters once per parameter, not per call.
- Hash userdata by identity so userdata keys do not all collide.
- Call Lua from C# with typed arguments and no boxing.
- A Roslyn source generator that emits the descriptors Hardwire produces today, at compile time. IL2CPP needs no run-time code generation, and members are found with a `switch`.

## Next: correctness and diagnostics

- Run the official Lua 5.2 test suite and track failures.
- Name the variable in runtime errors ("attempt to call a nil value (global 'foo')").
- Turn native stack exhaustion into a `ScriptRuntimeException` (`RuntimeHelpers.EnsureSufficientExecutionStack`).
- A `Script` keeps every chunk it compiles, so a long-lived `Script` that compiles in a loop grows without bound.
- `ScriptOptions(defaults)` does not copy `ColonOperatorClrCallbackBehaviour`.
- The Unity test runner list misses `ProtectedCallMetamethodTests`, `TonumberTests` and `NewIndexAssignmentTests`.

## Later, and ideas

- A Unity CI matrix: 2021.3, 2022.3 and 6 LTS editors, Mono and IL2CPP players.
- In-process JVM hosting as the default: needs the native stack guard above and an execution budget for plain calls, then a new measurement against the worker.
- Browser: AOT-compiled WebAssembly against the interpreter build; Firefox and WebKit tests.
- LuaCATS type definitions generated from registered C# types, for editor completion.
- A Debug Adapter Protocol server, so any editor can debug scripts.
- Save and restore a `Script`'s state.
- A built-in integer subtype, if the struct-value step makes it cheap (see decisions).

## Needs the owner

- Unity account secrets for CI, as `.github/workflows/ci.yml` reads them: `UNITY_EMAIL`, `UNITY_PASSWORD` and `UNITY_TOTP_KEY` (the secret key of authenticator-app 2FA on that Unity account; email 2FA does not work).
- Android and iOS devices for IL2CPP player runs.
- The `pairs` order decision, once the table measurements are in.
- Approval before any upstream pull request.
