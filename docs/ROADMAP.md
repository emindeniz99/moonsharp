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
| NativeAOT (.NET 8), as a stand-in for IL2CPP | The unit suite runs in CI (`nativeaot-unit` in `ci.yml`): all tests pass, none on the known-failures list. How well it predicts IL2CPP is unmeasured ([hosting.md](hosting.md), "The unit suite under NativeAOT"). |
| Unity 2021.3 / 2022.3 / 6 LTS, Mono and IL2CPP | **Not run yet.** `.github/workflows/unity.yml` runs the suite in the editor and in a Linux IL2CPP player on 2021.3.45f2, 2022.3.62f3 and 6000.0.85f1 once the licence secrets exist. Without Unity: the test bed compiles with each line's own compiler and references, and its assemblies pass the suite on .NET. |

What Unity 2021.3 support means for the code:

- The interpreter targets `netstandard2.0` and `net45`; new code must build for both.
- No code generation at run time on IL2CPP: no `Reflection.Emit`, no compiled expression trees on AOT paths.
- Any Roslyn source generator must load in Unity 2021.3's compiler. Check the supported Roslyn version before building one.
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
| branch `feat/luacats-generator` | LuaCATS definitions generated from the registry and globals of a configured `Script` (`src/Tools/MoonSharp.LuaCats`, `moonsharp-luacats` CLI with `--check`), checked with lua-language-server 3.19.1 in a new workflow. Explicit registration for mobile hosts documented and tested; no runtime change was needed. See [luacats.md](luacats.md) and [registration.md](registration.md). |

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

- Found with the NativeAOT suite, not fixed:
  - `CallbackFunction.FromDelegate` caches descriptors in a `ConditionalWeakTable` keyed by `Delegate.Method`. NativeAOT returns a new `MethodInfo` on every read (measured: `ReferenceEquals(d.Method, d.Method)` is false), so the cache never hits there and each conversion builds a descriptor. Key it by equality. IL2CPP not measured.
  - A table converted to `List<T>` or `Dictionary<K,V>` of a value type the app never instantiates, and a generic extension method over one, have no native code on NativeAOT, nor on IL2CPP without full generic sharing (before Unity 2022.1). The source generator in this list removes both.
  - 23 trim and AOT analysis warnings remain in the interpreter, listed in [hosting.md](hosting.md). Most are inherent to reflection over host types.
  - The trimming attributes are declared inside the interpreter for netstandard2.0. If a Unity class library declares them publicly, Unity's compiler warns CS0436; check in the Unity matrix, and whether UnityLinker reads the annotations at higher stripping levels.

## Next: correctness and diagnostics

- Run the official Lua 5.2 test suite and track failures.
- Name the variable in runtime errors ("attempt to call a nil value (global 'foo')").
- A `Script` keeps every chunk it compiles, so a long-lived `Script` that compiles in a loop grows without bound.
- `ScriptOptions(defaults)` does not copy `ColonOperatorClrCallbackBehaviour`.
- Property getters and setters called through reflection (AOT platforms, `InteropAccessMode.Reflection`) still wrap a thrown exception in `TargetInvocationException`; methods no longer do. A setter's own `ArgumentException` is also reported as a type mismatch on the compiled path.
- `PlatformAutoDetector` and `UnityAssetsScriptLoader` test `UNITY_5`, which Unity 2017 and later no longer define, so the package always takes the reflection path (`IsUnityNative` is false). The Unity test bed checks the default loader; replace the checks with `UNITY_5_3_OR_NEWER` if it fails or once the matrix is green.
- Upstream bugs that reproduce in the string library, the table code, the VM core and the compiler, and `require` ignoring `package.path` and `package.preload`: each has an ignored test in `UpstreamIssueTests.cs` that fails without the `Ignore` (list in [upstream-issues.md](upstream-issues.md)). Remove the `Ignore` with the fix.
- Found by the conformance corpus; each is a case with a `note`, so a fix updates its expectation. The owner decides which are bugs to fix (in `legacy` too) and which stay as documented differences:
  - `math.huge` is `Double.MaxValue`, so `math.huge == 1/0` is false.
  - `error(t)` with a table, and `error()`, raise "bad argument #1 to 'error'"; `pcall` never sees the error object.
  - The lexer reads `1e15+0.5` as one malformed number.
  - `string.format`: `%e` writes three exponent digits, `%d` of 2^63 wraps instead of failing, `%s` rejects nil.
  - `string.find(s, '', init)` with `init` past the end still matches.
  - `tostring` writes shortest round-trip digits, `NaN` and `Infinity`, where Lua 5.2 writes `%.14g`, `nan` and `inf`.

## Later, and ideas

- Unity matrix additions, each a cost question: Unity 6000.3 LTS (newest Unity 6 line); the hardwire registration mode (`MyNamespace.MyClass.Initialize()`); Android and iOS IL2CPP players.
- Run the conformance corpus in the Unity players too, through `ScriptHost`, with `unity-mono` and `unity-il2cpp` known-failures files.
- Run the bytecode leg of the corpus on the browser and Java hosts too (they accept the base64 dump text today; only the .NET runner compiles).
- The JSON contract for int64 results (the decision above): today a context with int64 returns them as an `output` error.
- Publish per-host corpus results over time, as JsonLogic's compat tables do.
- In-process JVM hosting as the default: the native stack guard and the execution budget (`ScriptOptions.InterruptHandler`) now exist; the hosting layer needs an error kind for an interrupted script, then a new measurement against the worker.
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
- The `pairs` order decision, once the table measurements are in.
- Approval before any upstream pull request.
- Upstream bugs that change interop behaviour (details in [upstream-issues.md](upstream-issues.md#decisions-for-the-owner)): overload scores for fractional numbers and custom converters (#293, #294); static members of base classes (#175); tables instead of userdata for lists under automatic registration (#274); per-script type registries (#181).
