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
| 2026-10-28 | **No unnecessary code.** Features may add code; nothing speculative, no unused helpers, no reimplementing what the platform already does well. Every change reports the netstandard2.0 Release `MoonSharp.Interpreter.dll` size before and after so growth stays visible. Fork point `fbd0367`: 372,224 bytes; master `0c0e5d4`: 392,704 bytes (mostly exact int64). | APK and IPA size matter for the mobile clients. |
| 2026-10-28 | The struct-value, struct-frame and table rewrites land on `master`, not on a separate branch. | Breaking changes are allowed, the public API stays, and a long-lived branch would drift. |
| 2026-10-28 | Allowed behaviour changes, each documented in a migration guide: `require` may become a C# function; runtime errors may use Lua's exact text with variable names; fast-called builtins may be missing from tracebacks while they run; closures without captured variables may be shared (visible through `rawequal`). | Each one buys start-up time or call speed; none changes results. |
| 2026-10-28 | Script authors get LuaCATS definitions generated from the registered C# API; a short Teal vs TypeScriptToLua trial follows. | Types and completion without changing the runtime. |
| 2026-10-28 | 64-bit integers in JSON: the schema marks int64 fields; JS callers get decimal strings, hosts that opt in get exact digits. | JavaScript numbers are exact only up to 2^53. |
| 2026-10-28 | `math.sin`, `cos`, `exp`, `log`, `pow` and the `^` operator keep using the platform's maths library; their last bit may differ between platforms, and the docs say so. No own maths library. | `+ - * /`, `sqrt` and `floor` are exact everywhere under IEEE 754; rewriting the platform's maths would be code for a problem nobody has seen. |
| 2026-10-28 | Mobile hosts register only the C# types they choose (no automatic registration). Script signing waits until scripts are served from a CDN or a third party. | Store review: downloaded scripts must not open up native APIs. The scripts are the team's own and arrive over HTTPS; the server re-runs the same rule to check results. |
| 2026-10-28 | Browser budget: first load under 5 s on a mid-range phone over slow 4G (today 1.91 MB brotli, about 0.6 s start on desktop). Optimise when a measurement crosses it. | Size matters only for mobile web users; the cache serves later visits. |
| 2026-10-28 | Tables get an array part and keep full insertion order for `pairs`; the dead-key bugs are fixed (only the first cleared key was ever removed, and `Table.Pairs` still returned keys set to nil). | Measured on `spike/table-order`: insertion order costs +0.7% time against hash order (inside noise), is 39% faster on delete and re-insert, and keeps `json.serialize` output identical across runs; hash order changes JSON key order from one process run to the next. |
| 2026-10-28 | Empty result lists follow Lua: a call that returns nothing gives zero values, so `select('#', f())` is 0 ([#287](https://github.com/moonsharp-devs/moonsharp/issues/287), [#198](https://github.com/moonsharp-devs/moonsharp/issues/198)). Lands after the struct-value work, which owns every expansion site. | Plainly wrong today; breaking changes are allowed. |
| 2026-10-28 | Overloads: a fractional number no longer picks an integer parameter ([#293](https://github.com/moonsharp-devs/moonsharp/issues/293)), and an exact type match beats a custom converter ([#294](https://github.com/moonsharp-devs/moonsharp/issues/294)). | `F(1.5)` called `F(int)` and dropped the fraction. |
| 2026-10-28 | Scripts see static members of base classes, except those of framework base types such as `object`, `ValueType` and `Enum` ([#175](https://github.com/moonsharp-devs/moonsharp/issues/175)). | Matches C#; keeps member lists, Hardwire output and LuaCATS definitions free of `Equals`/`ReferenceEquals`. |
| 2026-10-28 | Under automatic registration, lists and dictionaries convert to tables as documented, not to userdata ([#274](https://github.com/moonsharp-devs/moonsharp/issues/274)). | Documented behaviour; mobile hosts register types explicitly and are not affected. |
| 2026-10-28 | Per-script type registries ([#181](https://github.com/moonsharp-devs/moonsharp/issues/181)) are postponed. | Large change to the static `UserData` API with no current need. |
| 2026-10-28 | Public API changes accepted with the struct-value VM: `Closure.GetUpvalue` returns a read-only copy (use `Closure.SetUpvalue` to change a variable), every `DynValue` the VM hands out is read-only, and `ReferenceID` numbers are assigned on first read. | They let values live as structs inside the VM; documented in MIGRATING.md. |
| standing | Safe C# only: no `unsafe`, no `System.Runtime.CompilerServices.Unsafe`. Public Unity mechanisms only: no libil2cpp patches. | Bugs must stay exceptions, not memory corruption. The `Unsafe` package also fails to load in some Unity 6 setups ([Lua-CSharp#337](https://github.com/nuskey8/Lua-CSharp/issues/337)). |
| 2026-10-28 | Work collects on the `next` branch and lands on `master` in batches with `git merge --no-ff`, after the full unit suite and the fixtures pass locally. Pull requests are optional. CI runs on pushes to `master` (not for docs-only changes), and a red `master` is fixed forward first. | Fewer round trips and fewer CI runs. CI does not run on branches, so local checks come first. |
| 2026-10-28 | The development toolchain is pinned in `mise.toml` (.NET SDK 10 with the .NET 8 runtime, Java 17, Maven, Node 24, CMake), following the shared stack's toolchain rules; CI installs it with `jdx/mise-action`. clang and zlib come from apt. | Local and CI builds use the same exact versions instead of whatever a machine or runner image carries. mise offers clang only through conda, whose sysroot fails the NativeAOT link (`cannot find -lz`). |
| 2026-10-28 | The userdata source generator references Microsoft.CodeAnalysis 3.8 and implements `ISourceGenerator`; generated descriptors register only when the host calls `GeneratedUserData.RegisterAll()` or `CreateDescriptor`. | The 2021.3 and 2022.3 manuals require 3.8, a generator built for a newer compiler does not load in an older one, and two builds would both run in newer editors. Explicit registration follows the mobile decision above. See [source-generator.md](source-generator.md). |
| standing | Every performance change carries A/B numbers from `src/DevTools/Benchmarks`; every fix carries a test that fails without it. | See [upstream.md](upstream.md). |

## Platform targets

| Target | State in this fork |
|---|---|
| .NET 8 / .NET 10 | Tested in CI (unit tests, fixtures). |
| Browser (.NET 10 WebAssembly) | Chromium tested in CI with Playwright. Firefox and WebKit not tested. |
| Java 17+ (worker process) | Tested in CI, with a Spring Boot 4 sample. |
| Java in-process (hostfxr or NativeAOT) | Supported option, Linux x64, with per-call limits and a capped .NET heap: fixtures pass with CoreCLR and NativeAOT, and recursion through callbacks and endless loops come back as errors instead of ending or holding the JVM. The worker stays the default ([hosting.md](hosting.md), "Java: in process"). |
| NativeAOT (.NET 8), as a stand-in for IL2CPP | The unit suite runs in CI (`nativeaot-unit` in `ci.yml`): all tests pass, none on the known-failures list. How well it predicts IL2CPP is unmeasured ([hosting.md](hosting.md), "The unit suite under NativeAOT"). |
| Unity 2021.3 / 2022.3 / 6 LTS, Mono and IL2CPP | **Not run yet.** `.github/workflows/unity.yml` runs the suite in the editor and in a Linux IL2CPP player on 2021.3.45f2, 2022.3.62f3 and 6000.0.85f1 once the licence secrets exist. Without Unity: the test bed compiles with each line's own compiler and references, and its assemblies pass the suite on .NET. |

What Unity 2021.3 support means for the code:

- The interpreter targets `netstandard2.0` and `net45`; new code must build for both.
- No code generation at run time on IL2CPP: no `Reflection.Emit`, no compiled expression trees on AOT paths.
- A Roslyn source generator must load in every supported line's compiler: `MoonSharp.SourceGenerator` references Microsoft.CodeAnalysis 3.8 for that reason ([source-generator.md](source-generator.md#roslyn-version)).
- Code that Unity compiles (the package, the test sources) must stay within C# 9, the newest version Unity 2021.3 to Unity 6 support.
- The UPM manifest declares `"unity": "2021.3"`.

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
| branch `fix/aot-interop` | The unit suite runs as a NativeAOT executable in CI, against a known-failures file in both directions. Its first run failed 58 of 761 tests; fixed: NativeAOT detected as AOT, enum values without `Enum.GetValues`, `List`/`Dictionary` constructors and registered types' members kept for trimmed apps (`[DynamicDependency]`, `[DynamicallyAccessedMembers]`), generic extension overloads that cannot be instantiated skipped, property accessors rethrow their own exception in reflection mode, a repeated `RegisterType` builds no descriptor (`new Script()` 166 to 69 KB on NativeAOT, 92 to 70 KB under JIT). |
| branch `ci/unity-matrix` | Unity CI matrix (`.github/workflows/unity.yml`): 2021.3, 2022.3 and 6000.0, editor (Mono) and Linux IL2CPP player, each compared test by test with the .NET run. The test sources compile without NUnit (`src/TestRunners/EmbeddedTestRunner`, run on every push) and the fixtures are found by reflection, so no list can miss one. Exceptions from reflection calls keep their type. The UPM package requires 2021.3. |
| branch `feat/conformance-corpus` | Cross-host conformance corpus: 225 cases in 8 suites (numbers, strings, patterns, tables, errors, JSON, int64, logic) run by the .NET CLI (also from precompiled bytecode and under a decimal-comma culture), Chromium and the three Java transports, each checked against its own known-failures file in both directions. `moonsharp-host compile` writes bytecode with source locations; every transport can create int64 contexts. See [src/Hosting/fixtures/README.md](../src/Hosting/fixtures/README.md). |
| branch `feat/exact-int64` | Exact 64-bit integers: opt-in `int64`/`uint64` values (`CoreModules.Int64`), CLR `long`/`ulong` in both directions (`ScriptOptions.ExactInt64Interop` for CLR to Lua), exact JSON on request. See [int64.md](int64.md). |
| branch `perf/struct-values` | Struct call frames in a growable array; locals in a window of the value stack; Lua-style open/closed upvalues (closed on block exit, return, errors and coroutine recycling); values as an internal 24-byte struct (`LuaValue`) on the stack, in locals and upvalues, with `DynValue` (now 40 bytes) created only where a value leaves the VM. Zero bytes per iteration for empty_loop, function_call, int_math, float_math, fib and table_array_read (were 48 to 576). A/B against `next`, median of 21 interleaved rounds: Fib20 0.62x time, SumLoop100K 0.53x, CallEligibility 0.90x, StringFormatMix1K 1.10x (arguments of C# functions are now materialised). `Closure.SetUpvalue` added; see [MIGRATING.md](MIGRATING.md). |
| branch `perf/table-array-part` | Tables store integer keys 1..n in an array part and every other key in one compact hash part kept in insertion order, so `pairs` and JSON keep their order (decision above). Every dead key is forgotten at the next insert and hosts no longer see keys set to nil; upstream #308, #315 and PR #179 fixed. Against `next` with struct values (A/B, 21 rounds): `table_array_insert` 0.51, `record_build_read` 0.62, `gc_small_tables` 0.69, `table_sort_52` 0.74 (allocation 60 KB to 19 KB), `table_insert_len_10k` 0.79, `hash_delete_reinsert` 0.83, reads and `pairs` 0.93-1.02. Retained bytes per table: `{}` 264 to 136, 4 string keys 975 to 455, 16 array items 2407 to 287. See [MIGRATING.md](MIGRATING.md). |
| branch `feat/luacats-generator` | LuaCATS definitions generated from the registry and globals of a configured `Script` (`src/Tools/MoonSharp.LuaCats`, `moonsharp-luacats` CLI with `--check`), checked with lua-language-server 3.19.1 in a new workflow. Explicit registration for mobile hosts documented and tested; no runtime change was needed. See [luacats.md](luacats.md) and [registration.md](registration.md). |
| branch `feat/source-generator` | A Roslyn source generator writes userdata descriptors at compile time for `[MoonSharpUserData]` types and types listed with `[assembly: MoonSharpUserDataTypes]`: typed argument and result conversion, one `switch` per type, structs changed in place, per-member reflection fallback with a diagnostic, explicit registration (`GeneratedUserData.RegisterAll()`). Shipped in the UPM package. Results and error texts match reflection in the parity suite, and 20 interop fixtures pass on generated descriptors. See [source-generator.md](source-generator.md). |
| branch `feat/hosting-hardening` | Per-call instruction and time limits in the hosting contract (`instructionLimit`, `timeLimitMs`, error kind `interrupted`) on the CLI worker, the browser and Web Worker API, the Java worker, pool and both in-process backends; `limits` and `time-limit` corpus suites (244 cases on every host). The JVM crash probes rerun: recursion through `gsub` or `sort` callbacks is a "C stack overflow" error on CoreCLR and NativeAOT in process (it used to end the JVM), an endless loop with a limit returns `interrupted` in 15–126 ms, and an allocation without bound is a `host` error once `DOTNET_GCHeapHardLimit` caps the .NET heap. In process becomes a supported option; the worker stays the default ([hosting.md](hosting.md)). |
| branch `fix/interop-decisions` | The owner's interop decisions: a fractional number picks a floating-point overload and the overload cache tells whole from fractional numbers ([#293](https://github.com/moonsharp-devs/moonsharp/issues/293); `overload_call` interop workload B/A 0.95 to 0.98 against `next` after the struct-value merge, inside noise); an exact type match beats a custom converter ([#294](https://github.com/moonsharp-devs/moonsharp/issues/294)); derived types expose their base classes' static members, except those declared in the core library ([#175](https://github.com/moonsharp-devs/moonsharp/issues/175)); lists and dictionaries become tables under automatic registration ([#274](https://github.com/moonsharp-devs/moonsharp/issues/274)). `MoonSharp.Interpreter.dll` (netstandard2.0 Release) 416,256 to 417,792 bytes (+1,536). The source generator follows the #175 rule. See [MIGRATING.md](MIGRATING.md). |

## In progress

| Work | Where |
|---|---|
| Measured comparison with Lua-CSharp on the same workloads | report pending |
| Comparison of other engines (Lua-CSharp, Luau, Jint, xLua, xlua-ng, WattleScript, UniLua and others) and a profile of MoonSharp | to be published under `docs/research/` |

## Next: the VM, in order

Today MoonSharp runs the benchmark workloads 10 to 20 times slower than native
Lua 5.5.1 ([comparison-xlua-ng.md](comparison-xlua-ng.md)).

Done: cheap fixes, upvalue cells, struct call frames and internal struct values (see the Done table).

1. **Tables store `LuaValue`.** The array part and the ordered hash part are done (Done table). Tables
   should now store `LuaValue`: the VM reaches tables only through `TableGet` and `TableSet` in
   `Processor_InstructionLoop.cs`, which today create a `DynValue` for every value stored (the 40 bytes
   left in method_call and table_hash_set_get). Table needs internal `Get`/`Set` taking `LuaValue`
   keys and values, and `InitNextArrayKeys` taking a `LuaValue`. All storage is inside `Table.cs`: the array becomes
   `LuaValue[]` and the hash entries hold `LuaValue` keys and values (56 instead of 24 bytes per entry,
   but no 40-byte `DynValue` per key and value), with markers for "absent" and "pointer entry" that
   `DynValue` references give today. The public `DynValue` API then creates a `DynValue` per read, as the
   VM boundary already does. The model and order tests carry over unchanged.
2. **Zero-copy calls into the standard library.** Arguments are read from the stack and results written back, with no per-call argument list. Today each call to a C# function builds a `DynValue[]` and a `DynValue` per string or non-small number argument (StringFormatMix1K 1.10x after the struct values).
3. **Compiler and dispatch.** Constant operands, fused compare-and-branch (today `a < b` runs `Less`, `ToBool`, `Jf`; `<=` pushes a marker for `CNot`), superinstructions, inline caches for field and global access, a cache for absent metamethods, one counter for the debugger and the instruction budget, the calling location of a frame looked up only when asked.

Checkpoint after the struct values, against Lua-CSharp 0.5.8 on the same workloads (ns per
iteration; Lua-CSharp from the research run, MoonSharp from `workloads 7` on `perf/struct-values`
5d7e42d; one shared 4-core VM, so about 10% noise): empty_loop 34.2 vs 5.0 (6.8x), function_call
171 vs 81.6 (2.1x), method_call 415 vs 167 (2.5x), int_math 106 vs 33.1 (3.2x), float_math 190 vs
42.6 (4.5x), fib_recursive 80.1 vs 40.8 us (2.0x), closure_create_call 268 vs 180 (1.5x). Loops and
arithmetic stay more than 2x behind: what is left there is instruction count (a stack VM runs 5
instructions for an empty `for` iteration, Lua-CSharp's register VM one), so the decision on a
register VM (option C, PLAN B5) is due.

## Next: CLR interop

- Cache the resolved descriptor per runtime type.
- Stop allocating a bound callback on every `obj:Method()` call.
- Choose argument converters once per parameter, not per call.
- Hash userdata by identity so userdata keys do not all collide.
- Call Lua from C# with typed arguments and no boxing.

- Found with the NativeAOT suite, not fixed:
  - `CallbackFunction.FromDelegate` caches descriptors in a `ConditionalWeakTable` keyed by `Delegate.Method`. NativeAOT returns a new `MethodInfo` on every read (measured: `ReferenceEquals(d.Method, d.Method)` is false), so the cache never hits there and each conversion builds a descriptor. Key it by equality. IL2CPP not measured.
  - A table converted to `List<T>` or `Dictionary<K,V>` of a value type the app never instantiates, and a generic extension method over one, have no native code on NativeAOT, nor on IL2CPP without full generic sharing (before Unity 2022.1). The source generator in this list removes both.
  - 23 trim and AOT analysis warnings remain in the interpreter, listed in [hosting.md](hosting.md). Most are inherent to reflection over host types.
  - The trimming attributes are declared inside the interpreter for netstandard2.0. If a Unity class library declares them publicly, Unity's compiler warns CS0436; check in the Unity matrix, and whether UnityLinker reads the annotations at higher stripping levels.

## Next: correctness and diagnostics

- A `goto` out of a numeric or generic `for` leaves the loop's control values on the value stack, so
  an enclosing `for` reads them as its own counters and fails with "stack overflow" (reproduced on
  `next`; found while testing the struct values).

- An `__index` function that indexes its own table (`__index = function(t, k) return t[k] end`) runs forever without growing any stack, where Lua 5.2 raises "C stack overflow"; found with the hosting limits, which now stop it (corpus case `limits / an __index function that indexes its own table is interrupted`).
- Run the official Lua 5.2 test suite and track failures.
- Name the variable in runtime errors ("attempt to call a nil value (global 'foo')").
- A `Script` keeps every chunk it compiles, so a long-lived `Script` that compiles in a loop grows without bound.
- `ScriptOptions(defaults)` does not copy `ColonOperatorClrCallbackBehaviour`.
- Property getters and setters called through reflection (AOT platforms, `InteropAccessMode.Reflection`) still wrap a thrown exception in `TargetInvocationException`; methods no longer do. A setter's own `ArgumentException` is also reported as a type mismatch on the compiled path.
- `PlatformAutoDetector` and `UnityAssetsScriptLoader` test `UNITY_5`, which Unity 2017 and later no longer define, so the package always takes the reflection path (`IsUnityNative` is false). The Unity test bed checks the default loader; replace the checks with `UNITY_5_3_OR_NEWER` if it fails or once the matrix is green.
- Upstream bugs that reproduce in the string library, the VM core and the compiler, and `require` ignoring `package.path` and `package.preload`: each has an ignored test in `UpstreamIssueTests.cs` that fails without the `Ignore` (list in [upstream-issues.md](upstream-issues.md)). Remove the `Ignore` with the fix.
- Found by the conformance corpus; each is a case with a `note`, so a fix updates its expectation. The owner decides which are bugs to fix (in `legacy` too) and which stay as documented differences:
  - `math.huge` is `Double.MaxValue`, so `math.huge == 1/0` is false.
  - `error(t)` with a table, and `error()`, raise "bad argument #1 to 'error'"; `pcall` never sees the error object.
  - The lexer reads `1e15+0.5` as one malformed number.
  - `string.format`: `%e` writes three exponent digits, `%d` of 2^63 wraps instead of failing, `%s` rejects nil.
  - `string.find(s, '', init)` with `init` past the end still matches.
  - `tostring` writes shortest round-trip digits, `NaN` and `Infinity`, where Lua 5.2 writes `%.14g`, `nan` and `inf`.

## Later, and ideas

- Table follow-ups from the array-part work:
  - the array part never shrinks: a 1M-element array cleared with `t[i] = nil` keeps its 8 MB until `Clear()` or the table dies. Lua shrinks on rehash; re-check the "more than half live" rule when the hash part is rebuilt.
  - `table.insert`, `table.remove`, `table.sort` and `table.unpack` still go through `Get`/`Set` per element; fast paths over the array part are not measured yet.
  - `pairs` over array keys creates a key `DynValue` per step (`pairs_mixed_128` 265 to 285 B); this goes away when tables store `LuaValue`.
  - clearing a key inside a long run that was appended in order (`t[1] = nil` in a 1M sequence, then an insert) turns the rest of the run into pointer entries once, 24 bytes each.
  - `table_remove_readd_10k` is 1.30x slower in `ab`, where each round is 1,500 iterations right after a forced full GC. Both builds pay a fixed cost of about 2.5 ms per such batch (`collectgarbage('collect')` then 3 x 500 iterations: 2.46 µs per iteration on `next`, 2.64 µs on the branch, against 0.77 and 0.68 µs over 30,000 iterations); each of its operations alone is 3-14% faster on the branch. The cause of the post-GC cost is not known.

- Unity matrix additions, each a cost question: Unity 6000.3 LTS (newest Unity 6 line); the hardwire registration mode (`MyNamespace.MyClass.Initialize()`); Android and iOS IL2CPP players.
- Run the conformance corpus in the Unity players too, through `ScriptHost`, with `unity-mono` and `unity-il2cpp` known-failures files.
- Run the bytecode leg of the corpus on the browser and Java hosts too (they accept the base64 dump text today; only the .NET runner compiles).
- The JSON contract for int64 results (the decision above): today a context with int64 returns them as an `output` error.
- Publish per-host corpus results over time, as JsonLogic's compat tables do.
- Hosting limits follow-ups ([hosting.md](hosting.md), "Limits"):
  - a single long library call (`gsub`, `rep`, `sort` without a comparator) is not interrupted: one `gsub` over 20 million characters ran 2.8–3.3 s under a 100 ms limit. Checking the limit inside the long string and table loops would need the VM-core files;
  - no memory limit per context: in process one capped .NET heap serves every context;
  - cancelling a running call from another thread (for example a Java `Future.cancel` reaching the context), on top of the time limit;
  - in-process builds for macOS and Windows, which the default would need.
- Browser: AOT-compiled WebAssembly against the interpreter build; Firefox and WebKit tests.
- From upstream feature requests ([upstream-issues.md](upstream-issues.md)):
  - convert Lua functions to `Action` and `Func<...>` parameters (upstream #186), with the typed calls from C# to Lua above;
  - a `ScriptedImporter` for `.lua` files in the Unity package (upstream #235), with the Unity CI matrix;
  - send bytecode and source to an attached debugger only when its `GetDebuggerCaps()` asks for them (upstream #261), a two-line check in `Script.cs`.
- LuaCATS follow-ups:
  - a Unity Editor menu item that runs the generator. The UPM packages have no Editor part yet (`tools/upm` stages `Runtime` only), so it needs an Editor folder, an asmdef and the generator sources in the package;
  - typed host global functions: a global set from a delegate is declared `fun(...): any`, because a `CallbackFunction` keeps no signature;
  - `T?` for C# nullable reference annotations (only `Nullable<T>` is read today);
  - move the lua-language-server pin to 3.19.2 once its release digests have been read.
- Source generator follow-ups ([source-generator.md](source-generator.md#follow-ups)): run it in the Unity matrix (needs the secrets); overload dispatch with precomputed checks when no custom converter is registered; a typed call convention for generated members once the VM has one; a `link.xml` fragment for the members it binds by reflection.
- Convert script functions to delegate parameters (`Action<T>`, `Func<T>`). Today only `Closure` and `ScriptFunctionDelegate` parameters take a function, unless the host adds a custom converter; the LuaCATS generator reports such parameters.
- The Teal vs TypeScriptToLua trial from the decisions table.
- A Debug Adapter Protocol server, so any editor can debug scripts.
- Save and restore a `Script`'s state.
- A built-in integer subtype, if the struct-value step makes it cheap (see decisions).

## Needs the owner

- Unity account secrets for `.github/workflows/unity.yml`. Checklist:
  1. On the Unity account (a Personal licence is enough), turn on two-factor sign-in with an authenticator app, not email. Keep the setup key (the base32 text behind the QR code); that is `UNITY_TOTP_KEY`, not a six-digit code.
  2. Add repository secrets `UNITY_EMAIL`, `UNITY_PASSWORD` and `UNITY_TOTP_KEY` (Settings, Secrets and variables, Actions).
  3. Merge `unity.yml` to `master`; a push to `master` starts it, and "Run workflow" on the Unity workflow page starts it by hand (GitHub offers that button only for workflows on the default branch). The three versions run one after another, about 30 to 45 minutes each.
  4. If "Activate Unity (activation file and authenticator code)" fails with "did not return a licence": the Unity website no longer converts Personal activation files for every account ([game-ci/documentation#408](https://github.com/game-ci/documentation/issues/408)). Activate a Personal licence in Unity Hub on any machine, read the serial from its licence file (Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf`, macOS `/Library/Application Support/Unity/Unity_lic.ulf`, Windows `C:\ProgramData\Unity\Unity_lic.ulf`) with `sed -n 's/.*DeveloperData Value="\([^"]*\)".*/\1/p' Unity_lic.ulf | base64 -d | tail -c +5`, and add it as `UNITY_SERIAL`; the workflow then activates with it and returns the seat at the end.
  5. Each job uploads `unity-<version>-results` (one line per test). A red job lists the tests that failed or did not run; tests that skip themselves on Unity (the three trim-descriptor tests) are listed but allowed.
- Android and iOS devices for IL2CPP player runs.
- Approval before any upstream pull request.
- Per-script type registries (#181), postponed; details in [upstream-issues.md](upstream-issues.md#decisions-for-the-owner).
