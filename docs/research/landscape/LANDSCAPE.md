# Landscape: engines for server-sent business logic on Unity, browser, JVM and .NET

Date: 2026-10-28. Synthesis of six landscape surveys plus the earlier engine deep dives. No new
benchmarks were run for this document; every number is quoted from a survey, which in turn quotes
its source and conditions.

**Inputs (all read in full):**

| Survey | File (under `docs/research/`) |
|---|---|
| Lua family | `landscape/lua-family.md` |
| JavaScript / TypeScript family | `landscape/js-family.md` |
| C# hot update and WebAssembly | `landscape/hot-update-wasm.md` |
| Rule and expression DSLs | `landscape/rules-dsl.md` |
| Cross-compilation | `landscape/cross-compile.md` |
| Platform constraints (stores, iOS, IL2CPP, .NET wasm, JVM) | `landscape/platform-constraints.md` |
| Earlier deep dives (context only, VM internals not repeated) | `lua-csharp.md`, `unityenhanced.md`, `xlua.md`, `luau.md`, `jint.md`, `other-managed.md`, `PLAN.md` |

**Missing surveys:** none. All six requested surveys exist. Known gaps inside them are listed in §7.

**Evidence marks** are carried from the surveys: **[C]** confirmed by reading source, docs or
registry metadata; **[S]** secondary (vendor benchmark, blog, issue, forum, search summary);
**[U]** unconfirmed or inference. **[M]** (in `other-managed.md`) is a third-party measurement
whose data file was parsed.

**The need, generic.** A backend sends trusted business-logic scripts (rules, eligibility, offer and
variant selection, config-driven behaviour). The same script must give identical results on Unity
2021.3 / 2022.3 / 6 LTS (Mono and IL2CPP; iOS, Android, desktop; WebGL a plus), in browsers, on
JVM servers and on plain .NET. Priorities: exact int64, JSON in/out, cross-platform determinism,
small download, fast cold start, cheap host calls, good CLR interop on Unity, debugger and typed
tooling, only public Unity mechanisms, few unsafe-heavy dependencies.

---

## 1. Answer

**Yes: a modernised managed Lua engine (the MoonSharp fork) is the right core, and nothing surveyed
replaces it.** Store rules and iOS leave one class of solution that reaches every target with one
script: an interpreter compiled into each app that reads downloaded source or bytecode and exposes
a fixed host API (Apple DPLA 3.3.1(B), Play Device and Network Abuse) [C]. A managed C# interpreter
is the only member of that class that runs as *one implementation* on IL2CPP/iOS/WebGL, .NET, the
browser (.NET wasm) and the JVM (worker, or in process), with direct CLR interop and no native
code. That makes identical results a property of the design instead of a test target
(platform-constraints §10) [C/U]. Every alternative either multiplies engines (JS: 5+ engines;
"C Lua everywhere": one C core but four binding layers; CEL: 3–4 implementations), fails a hard
rule (HybridCLR patches libil2cpp; PuerTS's fast path uses libil2cpp internals; NeoLua, YantraJS
and Roslyn need runtime codegen), or cannot reach a target (Haxe 5 dropped C#, Fable has no JVM,
KMP has no Unity path) [C]. The route's weaknesses are engineering, not architecture: Unity Mono
and IL2CPP are **not tested yet** [C, fork ROADMAP], the browser bundle is about 1.9 MB brotli with
a 550–610 ms runtime start [C, fork hosting.md], VM speed is 10–20x behind native Lua [C, fork
ROADMAP], and the int64 story is an opt-in library type [C]. **Complement it with:**

1. A language-neutral **conformance corpus** run on every host, Unity IL2CPP players included (the
   pattern CEL, flagd and GrowthBook rely on).
2. **Engine-owned determinism**: number formatting and parsing, iteration order, hashing, and a
   policy for transcendental `math.*`.
3. **Typed authoring** compiled or annotated onto the same engine: LuaCATS now, then Teal or
   TypeScriptToLua after a spike.
4. **Store hygiene**: a narrow host API in mobile builds, signed bundles, and bytecode
   precompiled at publish time.
5. **Optional second backends behind explicit triggers only**: Lua-or-engine-in-Wasm on Endive for
   a JVM without .NET, or CEL with cel-java for high-volume server-side predicates.

---

## 2. Comparison matrix

Fifteen candidates, ordered by fit. The matrix is split into two tables with the same rows so it
stays readable. "Route" rows combine several packages that must be used together.

### 2a. Reach and fit

| # | Candidate | Unity IL2CPP / iOS | Unity WebGL | Browser | JVM | Same script everywhere | C# interop on Unity | App Store / Play fit |
|---|---|---|---|---|---|---|---|---|
| 1 | **MoonSharp fork** (managed Lua 5.2) | Yes by design, **not tested** [C ROADMAP] | Upstream 2.0 added WebGL [C]; fork untested [U] | .NET 10 wasm, Chromium in CI [C] | Worker (CI) or in process via hostfxr / NativeAOT (Linux x64, experimental) [C] | **Yes**: one assembly on all hosts; 24/24 fixtures on .NET, Chromium, Java [C] | Reflection descriptors + Hardwire; binds types you do not own [C]; IL2CPP falls back to reflection invoke [C] | Fits if auto-registration of arbitrary CLR types is off in mobile builds [C/U] |
| 2 | Lua-CSharp (managed Lua 5.2) | Mono and IL2CPP claimed [C README] | [U] | via .NET wasm [U] | none built | Possible, not built [U] | `[LuaObject]` generator on owned partial types only [C] | Same as #1 |
| 3 | WattleScript (MoonSharp fork) | Yes, source generator [C] | [U] | via .NET wasm [U] | none | As #1, not built | MoonSharp descriptors + incremental generator [C] | Same as #1 |
| 4 | **PUC Lua 5.4/5.5 C core everywhere** (xLua-NG + emscripten/wasmoon + LuaJava or Lua-on-Endive + KeraLua) | xLua-NG, P/Invoke, `__Internal` [C] | Lua 5.5.1 only [C] | wasmoon or own emscripten build [C] | LuaJava JNI [C]; Endive unproven [S] | Same C source, 4 binding layers [U] | Generated bindings, marshalling, native crash surface [C/U] | Fits (interpreter) [C] |
| 5 | Luau everywhere (NuLua + Luau.Web + luau-java) | NuLua desktop only; luau-dotnet archived [C] | luau-dotnet claimed wasm32, archived [C] | official `Luau.Web`, `luau-web` npm [C] | luau-java, **Java 25+** [C] | Same C++ core, mobile binding missing [C] | NuLua struct values, preview [C] | Fits (interpreter) [C] |
| 6 | Jint (managed JS) | By being managed; no official statement [S] | [U] | via .NET wasm, or browser JS = another engine [C] | none | One engine on .NET hosts only [U] | Reflection CLR projection; IL2CPP speed unmeasured [C/U] | Fits [U] |
| 7 | PuerTS (V8 / QuickJS) | Yes; V8 `--jitless` on iOS [C] | Browser's own JS [C] | n/a | none | No: engine differs per platform [C] | Generated wrappers; fast "xil2cpp" mode uses libil2cpp internals (**forbidden here**), P/Invoke mode allowed [C] | Fits [U] |
| 8 | QuickJS-ng with own bindings | Via ReactUnity / PuerTS bindings [C] | Hands off to browser JS [C] | Own wasm build possible [U] | JNI bindings exist [U] | Only if we own every binding [U] | Own bindings, native [U] | Fits [U] |
| 9 | JS native per host (browser + Jint/PuerTS + GraalJS/Javet) | as #6/#7 | as #7 | Native, zero download [C] | GraalJS (interpreter-only on stock OpenJDK) or Javet (JNI) [C] | **No**: 5+ engines [C] | as #6/#7 | Fits [U] |
| 10 | CEL (cel-java + Celly or Cel.NET + cel-es) | Celly is `net8.0` only; Cel.NET netstandard2.1 with heavy deps; no IL2CPP evidence [C] | [U] | cel-es (beta) or .NET wasm [C] | **Official cel-java** [C] | 3–4 implementations, shared 2,456-test corpus [C] | Data in/out only, by design [C] | Lowest risk: cannot add features [U] |
| 11 | Wasm module (engine compiled to Wasm; WACS / browser / Endive / Wasmtime) | WACS managed interpreter claims 2021.3+ IL2CPP iOS [C README]; no device data [C] | Browser Wasm via `.jslib` [U] | Native [C] | **Endive**, pure Java [C] | **Yes**, one artifact; proven by OpenFeature providers outside Unity [C] | Bytes in/out, no CLR object access [U] | Fits as interpreted bytecode; AOT Wasm must not be downloaded [C] |
| 12 | HybridCLR (IL interpreter in IL2CPP) | Yes, by **replacing libil2cpp** [C] | Yes [C] | .NET wasm could load the DLL [U] | **No** | No [C] | Best: real CLR types [C] | Vendor claims >1,000 shipped titles [S] |
| 13 | ILRuntime (managed IL interpreter) | Yes, public mechanisms [C] | [U] | .NET wasm loads DLL directly [U] | **No** | No [C] | Adapters, delegate registration [C] | Fits [U] |
| 14 | TypeScriptToLua → our engine (authoring) | Inherits engine | Inherits | Inherits | Inherits | Yes, if only the Lua output runs anywhere [C caveats] | Inherits; `.d.ts` for host API | Inherits |
| 15 | Teal → our engine (authoring) | Inherits engine | Inherits | Inherits | Inherits | Yes; near-zero semantic gap [C/U] | Inherits | Inherits |

### 2b. Qualities

| # | Candidate | Performance evidence | Cold start / size | Exact int64 / JSON | Determinism | Tooling | Activity (latest) | Licence |
|---|---|---|---|---|---|---|---|---|
| 1 | **MoonSharp fork** | 3.6–14x slower than Lua-CSharp on VM rows; 10–20x slower than native Lua 5.5.1 [C fork bench, .NET 8, 4-core VM] | Browser 1.91 MB brotli, runtime start 550–610 ms headless Chromium [C]; JVM in-process NativeAOT 38 ms open+first call, 5.5 MB `.so`; worker 223 ms [C]; `new Script()` ~1.2 ms [C] | Opt-in `int64`/`uint64` userdata; host JSON rejects ints beyond ±2^53, send IDs as strings [C] | One implementation; `pairs` = insertion order [C]; number→string via BCL formatter [C]; `math.*` via platform libm [C]; culture fixed [C]; `#s` = UTF-16 units on every host [C] | VS Code debugger; LuaCATS generation planned [C] | Fork active Oct 2026; upstream `v3.0.0-beta.1` NuGet 2026-07-04, commits to 2026-10-14 [C] | BSD-3 |
| 2 | Lua-CSharp | Within 1.4–4x of native Lua on VM rows [C fork bench]; `new` state 43.5 µs [C] | DLL size not surveyed | No int64 subtype [C]; JSON in core [U] | One implementation; UTF-16 strings by default [C]; hash-order `pairs` [U] | LuaCATS; debug hooks, no debugger [C] | v0.5.8, 2026-10-25 [C] | MIT |
| 3 | WattleScript | No published numbers [C] | — | No int64 [U]; `json` module [C] | As #1 | Own dialect + Lua 5.2 | One maintainer; NuGet 1.0.0 from 2022-10; 8 commits in 2026, last 2026-08-04 [C] | BSD-3 |
| 4 | **C Lua everywhere** | Native speed; wasmoon heapsort 15.3 ms vs fengari 389.9 ms (machine not stated) [S] | wasmoon 393 kB raw / 130 kB gz [C]; JSON via yyjson +38 to +104 KB Android arm64 [C] | Native int64 (5.3+) [C]; no JSON in core [C] | Same C source; **hash seed must be pinned** (`luai_makeseed`), libm/printf may differ across libc and emscripten [C/U] | LuaLS/LuaCATS; per-host debuggers [S] | xLua-NG HEAD 2026-10-28, **unpublished**; LuaJava 4.1.0 2026-01-03; wasmoon npm **2023-12** [C] | MIT (+Apache-2.0 Endive) |
| 5 | Luau everywhere | No embedding numbers in surveys | luau-web 2.1 MB unpacked [C] | int64 library type behind a flag, **no `+`/`-` operators** [C] | Same core [U] | **Best**: luau-analyze, luau-lsp 1.70.1 [C] | Luau 0.744 2026-10-23; NuLua 0.1.0 preview 2026-06-22 [C] | MIT |
| 6 | Jint | Minimal prepared script 1.094 µs vs ClearScript 379.5 µs (Jint 4.16, .NET 10, Ryzen 5950X) [S vendor]; no IL2CPP or mobile numbers | Jint.dll 2.59 MB + Acornima 0.37 MB IL [C] | `BigInt`, `JSON.rawJSON` [C]; BigInt cannot mix with number [C] | One engine; spec'd number printing and key order; `Math` via `System.Math` [C] | DevTools needs .NET 8+, not in Unity [C] | 4.17.0 2026-10-07 [C] | BSD-2 |
| 7 | PuerTS | iPhone XS Max fib(40): V8 jitless 17,513 ms vs xLua 20,581 ms vs C# 501 ms [S vendor] | V8 iOS 26.4 MB static lib, Android arm64 18.5 MB; QuickJS iOS 1.25 MB [C, release archives] | `long`→`bigint` [C] | Different engine per platform [C] | `.d.ts` generation; V8 inspector; no QuickJS debugger [C] | Unity 3.0.3 2026-09-04; master 2026-10-22 [C] | BSD-3 |
| 8 | QuickJS-ng | "367 KiB of x86 code", runtime life cycle <300 µs [S vendor] | iOS static 1.49 MB (ReactUnity) [C] | `BigInt`; `rawJSON` in `quickjs.c` [C] | One C engine if owned; `sin()` from platform [C] | No debugger in Unity bindings [C] | v0.17.0 2026-09-18 [C] | MIT |
| 9 | JS native per host | V8 jitless −40% Speedometer, −80% Web Tooling (x64 desktop, 2019) [S] | Browser 0; truffle-api jar 17 MB; Javet Node artefact 32.7 MB [C] | BigInt discipline; JSON needs `context.source` or strings [C] | 5+ engines; Math, Date, Intl differ [C] | Full TypeScript [C] | GraalJS 25.4.4.1.1 2026-10-26; Javet 6.0.2 2026-10-04 [C] | mixed |
| 10 | CEL | Celly 129 ns vs cel-go 107 ns on a simple expression (Apple Silicon, author-run) [S]; no IL2CPP/mobile numbers | Celly ~10.7k lines, zero deps; Cel.NET pulls protobuf, gRPC, Avro, NodaTime, Newtonsoft, ANTLR [C] | int64 and uint64 exact, overflow is an error [C]; JSON numbers arrive as double unless typed [U] | Spec-deterministic; regex (RE2), time zones, map order are hazards [C/U] | Type checker; no LSP found [U] | cel-spec v0.25.3 2026-07-29; cel-java v0.15.0 2026-10-26; Celly v1.2.0 2026-07-18 [C] | Apache-2.0 |
| 11 | Wasm module | WACS interpreter CoreMark 0.79–1.12% of native (CoreCLR JIT, M3 Max, **not IL2CPP**) [C]; Endive redline ~4–10x vs Chicory AOT on real modules [S] | TinyGo rule engine ~760 KB [C]; WACS nupkg 762 KB; WACS cold start 35–55 ms CoreCLR for a 242-byte module [C] | Native `i64`; JSON inside the guest [C/U] | **Strongest**: libm compiled into the module; deterministic profile in Wasm 3.0 [S/U] | Great for guest languages; no source debugger on WACS/Endive [U] | WACS 0.16.14 2026-05-28 (one maintainer); Endive 1.1.0 2026-09 [C] | Apache-2.0 |
| 12 | HybridCLR | Vendor only, one device: commercial edition 140–330% of Mono on numeric code [S] | — | Native `long`, any C# JSON [C] | C# float differs per backend [U] | Full C# IDE; **no on-device debugging** of interpreted code [C] | v9.0.3 2026-10-13; **v9 drops 2021.3** [C] | MIT core + paid editions |
| 13 | ILRuntime | None published | — | Native `long` [C] | As #12 | VS debugger extension [C] | Last tag v2.1.0 2022-10-07; commits to 2026-07-24 [C] | MIT; needs `unsafe` [C] |
| 14 | TypeScriptToLua | Inherits engine; no size numbers for `lualib` [U] | adds `lualib` helpers [C] | **None**: TS `number`, no BigInt transform [C] | Inherits; documented TS-vs-Lua caveats [C] | Full TS + source maps [C/S] | 1.37.1 2026-07-09 [C] | MIT |
| 15 | Teal | Inherits engine | none extra [U] | `integer` type in checker; exactness depends on VM [U] | Inherits | LSP + VS Code [C/S] | 0.24.8 2025-10-13; commits to 2026-10-26 [C] | MIT |

Not in the matrix, for brevity: LuaJIT (no JIT on iOS, Lua 5.1, no browser), fengari (32-bit
integers), LuaJ / Cobalt / Rembulan (stale, "Don't use", or dead), slua / toLua# / UniLua (dead),
NeoLua / YantraJS / Jurassic / Roslyn scripting (runtime codegen), ClearScript (no mobile), Okojo
(.NET 10+), Hermes (no embedding), JsonLogic (measured 62–69% cross-port agreement), Starlark /
Jsonnet / CUE / Pkl / Expr (no C# or no JVM implementation), Haxe / KMP / Fable / Nelua (no hot
update on IL2CPP), InjectFix (inactive). Each has a "no" verdict with reasons in its survey.

---

## 3. Showstoppers per family

| Family | Showstoppers for this need |
|---|---|
| **Managed Lua in C#** (MoonSharp fork, Lua-CSharp, WattleScript) | None structural. Risks: Unity untested, VM 10–20x behind native, browser bundle ~1.9 MB brotli [C]. Lua-CSharp specifically: no interop for types you do not own, no debugger, `System.Runtime.CompilerServices.Unsafe` package fails to load in a Unity 6000.3 setup ([#337](https://github.com/nuskey8/Lua-CSharp/issues/337)) [S], async-everywhere VM caused a WebGL deadlock ([#327](https://github.com/nuskey8/Lua-CSharp/issues/327)) [S]. WattleScript: one maintainer, no release since 2022, Lua patterns rewritten onto .NET Regex with semantic drift [C]. |
| **Native C Lua** (xLua-NG, KeraLua, LuaJava, wasmoon) | Four separately maintained binding layers to keep in lockstep (Lua version, `luai_makeseed`, number formatting) [U]; xLua-NG unpublished; wasmoon npm stale since 2023-12; JNI natives in server images, and JEP 472 warns on native loading since JDK 24 [C]; per-host JSON and debugger. Not a showstopper, but a large ownership cost. |
| **LuaJIT** | No JIT on iOS ("Runtime code generation is restricted on iOS"), no browser port, Lua 5.1 numbers [C]. |
| **Luau** | Unity mobile bindings missing (NuLua desktop only, luau-dotnet archived); luau-java requires Java 25+ while Spring Boot 4 baselines Java 17 [C]; int64 has no arithmetic operators [C]; not Lua-compatible for existing scripts [U]. |
| **JS / TypeScript at runtime** | Exact int64 needs `BigInt`, which throws when mixed with `number` [C]; 5+ engines for one script [C]; PuerTS fast IL2CPP path uses libil2cpp internals [C]; V8 is 18–26 MB per platform and jitless on iOS [C]; GraalJS is interpreter-only on stock OpenJDK [C]; Jint DevTools not usable in Unity [C]. |
| **Rule / expression DSLs** | CEL: no Unity-ready .NET build (Celly `net8.0` + `RegexOptions.NonBacktracking`; Cel.NET heavy, no published conformance) [C]; not Turing-complete, so only predicates and formulas move. JsonLogic: measured cross-port divergence [C]. Others: no C# or no JVM implementation, or IEEE-double-only numbers [C]. |
| **WebAssembly** | Unity IL2CPP/iOS leg is a young single-maintainer interpreter with no device numbers; its fast transpiler cannot run downloaded modules on IL2CPP [C]; interop is bytes in/out [U]; no source debugger on WACS or Endive [U]; authoring needs a compiler toolchain. Downloading AOT-compiled Wasm is forbidden on iOS [C]. |
| **C# hot update** | HybridCLR replaces libil2cpp (violates our rule) and v9 drops Unity 2021.3 [C]; ILRuntime needs `unsafe` and has no release since 2022 [C]; neither reaches the JVM; Roslyn scripting cannot run on IL2CPP [C]. |
| **Cross-compilation** (Haxe, KMP, Fable, Nelua) | IL2CPP cannot load new code, so no hot update on the client [C]; Haxe 5 removed the C# target [C]; Fable has no JVM target [C]; KMP reaches Unity only through a C ABI [C]. "One source, N targets" gives N similar programs with different integer semantics [C]. |
| **Authoring layers** (TSTL, Teal, CSharp.lua) | Not engines, so no reach showstoppers. TSTL has no int64 story and documented TS-vs-Lua semantic gaps [C]; CSharp.lua needs Lua 5.3 integers for `long` [U]. |

---

## 4. Ideas worth stealing, ranked by value / effort

Value: H/M/L for this need. Effort: S (days), M (weeks), L (a quarter or more). Each item names
where it comes from; implementation details belong in a fork design note, not here.

| Rank | Idea | Source | Value | Effort | Why it matters here |
|---:|---|---|---|---|---|
| 1 | **Cross-host conformance corpus** in a language-neutral format: (script, input JSON, expected output or error), run on .NET, NativeAOT, browser, Java worker and in-process, Unity Mono and IL2CPP players; compare output bytes. Cover int64 edges, string length unit, patterns, iteration order, float-to-string. Publish weekly results like JsonLogic does. | CEL corpus [cel-spec tests/simple/testdata](https://github.com/cel-expr/cel-spec/tree/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/tests/simple/testdata); flagd [Gherkin testbed](https://github.com/open-feature/flagd-testbed/tree/7308f9b1bf722716edd1c19f7ea5681d8a414c27/evaluator/gherkin); GrowthBook [standard-cases.json](https://github.com/growthbook/growthbook-c-sharp/tree/2a4b0082a08f777757f8724b72f9e24f09382066/GrowthBook.Tests/Json); JsonLogic [compat-tables](https://github.com/json-logic/compat-tables/tree/25669097955453d3c2381bf953f4741b9a4fc0d1/results) | H | S–M (fixtures exist: 24/24 on 5 hosts [C]) | The only thing every "same result everywhere" project relies on. Unity rows are the missing ones. |
| 2 | **Narrow host API for downloaded scripts in mobile builds**, plus signature verification of each script bundle before compile. | Apple 2017 hot-patch warning ([forum](https://developer.apple.com/forums/thread/73640)) [S]; DPLA 3.3.1(B) ([terms](https://developer.apple.com/support/terms/apple-developer-program-license-agreement/)) [C] | H | S | Turns store policy from a worry into a checklist. MoonSharp's automatic `UserData` registration is the part to switch off. |
| 3 | **NativeAOT in CI as an IL2CPP proxy** (no `Reflection.Emit`, missing generic instantiations, trimming). | Fork measurement: 680/732 tests under NativeAOT, failures in the classes IL2CPP rejects ([hosting.md](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/docs/hosting.md)) [C] | H | S | Cheap early warning until Unity CI runs; proxy quality vs a real IL2CPP player is unmeasured [U]. |
| 4 | **Engine owns every output byte**: own number formatter/parser, iteration order independent of `GetHashCode`, seeded `math.random`, and integer-only bucketing with a specified hash for variant selection. | flagd ["v2 high-precision integer bucketing"](https://github.com/open-feature/flagd-testbed/blob/7308f9b1bf722716edd1c19f7ea5681d8a414c27/evaluator/gherkin/fractional.feature#L4-L6) [C]; .NET `Math.Sin` doc ("may differ between operating systems") [C]; `string.GetHashCode` doc [C] | H | M | Platform-constraints row 10: BCL math, hashing and formatting are not portable. Also constrains the planned table rewrite (§5). |
| 5 | **Generated type definitions for the host API** (LuaCATS from registered C# types; `.d.ts` if TSTL is chosen), pinned to `---@version 5.2`. | PuerTS `.d.ts` generator ([Utils.cs](https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/Utils.cs#L449-L452)) [C]; [lua-language-server](https://github.com/LuaLS/lua-language-server) 3.19.2 [C] | H | S–M | Autocomplete and checks with no runtime change. Already on the fork roadmap. |
| 6 | **Precompile scripts to bytecode at publish time**; ship bytecode, keep source maps. | cross-compile §2.8 [U]; fork binary dumps keep source locations [C] | M | S | Moves syntax errors to publish time and parsing off the client cold start; still interpreted, so store-safe. |
| 7 | **"Pure rule" profile of Lua**, checked at upload: no `while`/`repeat`, no recursion, no global writes, whitelisted pure host calls; optional static cost estimate. | [Starlark spec L42-L51](https://github.com/bazelbuild/starlark/blob/c0af0ea03dc90d3bbc021394b693e6957cc15030/spec.md?plain=1#L42-L51) [C]; CEL cost estimation ([langdef "Macro Performance"](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md?plain=1#L880-L910)) [C] | M | M | Gives most of a DSL's guarantees without a second engine [U]. |
| 8 | **Typed authoring front end**: Teal (near-zero semantic gap; 5.1 output runs on 5.2) or TypeScriptToLua with `luaTarget: "5.2"` (`bit32` for bitwise ops). | [Teal](https://github.com/teal-language/tl/blob/28b9e8148adf3aaec029c753c96034adcfd86c6b/teal/environment.lua#L93) [C]; [TSTL CompilerOptions.ts](https://github.com/TypeScriptToLua/TypeScriptToLua/blob/0c3a141280d6796c1a1906210622bda8ea4180a2/src/CompilerOptions.ts#L63) and [caveats](https://typescripttolua.github.io/docs/caveats) [C]; upstream MoonSharp `luau` branch parses and drops Luau types ([TypeAnnotation.cs](https://github.com/moonsharp-devs/moonsharp/blob/ab172c2bf31d9aab779f4ee44b38efff98573ecd/src/MoonSharp.Interpreter/Tree/TypeAnnotation.cs)) [C] | M | M | Real types for authors; the runtime stays one engine. TSTL needs an int64 convention (branded type + operator extensions) [U]. |
| 9 | **Exact big integers at the JSON boundary**: carry int64 as decimal strings, or read raw digits (`JSON.rawJSON`, reviver `context.source`) on JS hosts. | [TC39 JSON.parse source text access](https://github.com/tc39/proposal-json-parse-with-source) (stage 4) [C]; fork host contract already rejects ints beyond ±2^53 [C] | M | S | Keeps browser TS and Java callers exact; aligns with the fork's "reject, do not round" rule. |
| 10 | **Reflection-free generated bindings** via a Roslyn source generator against Roslyn 3.8 (`ISourceGenerator`) with a 4.3 build for Unity 6; switch-based member lookup. | WattleScript incremental generator ([254bdc1](https://github.com/WattleScript/wattlescript/commit/254bdc1743791896aed5e2f062e53efee35bd5be)) [C]; Unity generator Roslyn versions ([2021.3](https://docs.unity3d.com/2021.3/Documentation/Manual/roslyn-analyzers.html), [6000.3](https://docs.unity3d.com/6000.3/Documentation/Manual/create-source-generator.html)) [C]. PuerTS's P/Invoke mode is the public-mechanism lesson; its xil2cpp mode is the one **not** to copy [C] | H (IL2CPP) | L | IL2CPP cannot compile expression trees, so reflection invoke is the default there today [C]. |
| 11 | **Track upstream MoonSharp v3**: `json.emptyarray()`, conditional breakpoints, multi-script debugging, `__call` in `pcall`, label scoping. | [releases](https://github.com/moonsharp-devs/moonsharp/releases) [C] | M | S | Upstream is active again; cheap fixes, lower divergence. |
| 12 | **Wasm as a second backend for the JVM** (Lua 5.5 or the engine compiled to Wasm, run by Endive's runtime compiler), with GO Feature Flag's ABI (`malloc`/`free`/`evaluate(ptr,len)`, JSON in and out) and its 1 MB shadow-stack lesson. | [GO Feature Flag wasm README](https://github.com/thomaspoignant/go-feature-flag/blob/6e87bcd4b7cbc02ef9a6d681ae3172ff58c3a270/cmd/wasm/README.md), [issue #5651](https://github.com/thomaspoignant/go-feature-flag/issues/5651) [C]; [Endive](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/README.md) [C] | M | M–L | Pure-Java evaluation, no .NET on the server. A C Lua build is a different engine from the managed one, so it needs idea 1 to prove identity [U]. |
| 13 | **CEL as a narrow predicate layer** (eligibility, segment filters) with cel-java on the server and a ported .NET build on clients. | [cel-java](https://github.com/google/cel-java/blob/ab84c6ee19327d7f140d33a2fe7aeef9fa484e2b/README.md), [Celly conformance](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/docs/internals/conformance.md) [C] | M | L | Only on its revisit triggers (§5); doubles engines. |
| 14 | **Deterministic Wasm kernels** for rarely changing maths (pricing, hashing, bucketing) called from Lua hosts. | [Wasm 3.0 deterministic profile](https://webassembly.org/news/2025-09-17-wasm-3.0) [S]; [Shopify instruction counts](https://shopify.dev/docs/apps/build/functions/optimize-instruction-counts) [C] | L | L | Strong float determinism, weak on Unity IL2CPP speed and tooling. |
| 15 | **One API over several native backends** as a future escape hatch. | [NuLua](https://github.com/nuskey8/NuLua/blob/1d6d641786f5920fc9dd3ce2f7b8dbb5fb9bad42/README.md) (preview, desktop only) [C] | L now | — | Watch only. |

---

## 5. Risks to the current route and revisit triggers

### 5.1 Risks

| Risk | Evidence | Mitigation |
|---|---|---|
| **Unity is untested.** The thesis "one assembly everywhere" is unproven on its most important host. Known AOT hazards in the code: an expression-tree probe that answers wrongly on NativeAOT, `MakeGenericType`/`MakeGenericMethod`, `dynamic` in `ToDynamic`, runtime-thrown exceptions caught as control flow (WebGL default exception mode aborts) [C platform-constraints §3.5]. | fork ROADMAP: "Not tested" [C] | Unity CI matrix (needs owner secrets) and one iOS + one Android IL2CPP device run; NativeAOT proxy meanwhile (idea 3). |
| **Determinism drifts through the BCL.** `tostring` and JSON use `double.ToString`; `math.*` uses platform libm; Mono vs IL2CPP float reports exist [C/S]. | platform-constraints §7 | Idea 4; lint transcendental math out of rules. |
| **The planned table rewrite can break deterministic iteration.** Today `pairs` follows insertion order via `LinkedList` [C]; a Lua-CSharp-style hash part makes order depend on the hash function [C]. | platform-constraints §7; fork `spike/table-order` | Owner decision Q-L3. |
| **int64 is opt-in.** Authors can still put IDs in doubles; the host rejects ints beyond ±2^53 instead of rounding [C], which is safe but surprising. | fork int64.md, hosting.md [C] | Schema-typed inputs; lint for numeric IDs; revisit integer subtype after struct values (fork decision). |
| **Browser cost.** 1.91 MB brotli and 550–610 ms runtime start in headless Chromium [C]. .NET 12 is expected to move browser wasm to CoreCLR, and one hobby data point shows the CoreCLR interpreter much slower than Mono AOT [S]. | fork hosting.md; platform-constraints §5 | Measure AOT build; cache the runtime; re-measure on each .NET major. |
| **JVM coupling.** In process, recursion through a CLR callback **killed the JVM** (SIGABRT/SIGSEGV); a script that never returns cannot be stopped in process [C]. JEP 472 will turn native-access warnings into errors later [C]. | platform-constraints §6 | Worker stays default; native stack guard and instruction budget before in-process becomes default. |
| **Speed.** 10–20x behind native Lua; no device numbers for any engine on iOS [C]. Rule workloads are likely dominated by host calls and JSON, not VM speed [U]. | fork ROADMAP, comparison | Measure a real rule set on a device before optimising further; struct-value checkpoint already in the roadmap. |
| **Ownership.** The fork owns a VM; upstream is active again, so divergence grows [C]. | lua-family §1.1 | Cherry-pick upstream fixes (idea 11). |
| **Lua 5.2 dialect ageing.** No native integers, no `//`, `bit32` instead of operators. Tooling still supports 5.2 [C]. | lua-family §7 | Teal/TSTL target 5.1/5.2 [C]; reconsider only with an engine change. |
| **Unity 7 / ALC reloads.** Static engine caches survive Play sessions and may hold types from an unloaded ALC [U]. | platform-constraints §4 | Reset API on subsystem registration. |

### 5.2 Revisit triggers

The decision changes if any of these happens. Each names what to re-evaluate.

1. **Unity IL2CPP device runs fail in ways the engine cannot fix in safe C#** → re-evaluate C Lua (xLua-NG) for the Unity leg, with the conformance corpus guarding identity.
2. **NuLua ships iOS and Android with a Unity package** → re-evaluate "one native core everywhere" (PUC Lua or Luau).
3. **luau-java drops the Java 25 floor, or the server fleet moves to Java 25, and Luau integers get operators** → re-evaluate Luau for its tooling.
4. **The JVM must evaluate at a volume where worker IPC dominates** (hot call p50 45.5 µs, of which 6.0 µs inside the engine [C]) and in-process is not acceptable → spike Wasm-on-Endive (idea 12) or CEL with cel-java (idea 13).
5. **A maintained netstandard2.1 CEL build with published 100% core conformance, or a CEL language server, or non-programmers authoring rules** → spike CEL for predicates.
6. **The struct-value checkpoint misses** (loops and calls still >4x native Lua 5.5.1 after step 4) → register VM behind the same API (already a fork decision).
7. **The browser bundle or start time is over a budget the owner sets** → measure the AOT build first; then a native-JS-side evaluator only with the conformance corpus.
8. **componentize-dotnet leaves preview** (latest v0.8.0-preview00011, 2026-06-12 [C]) → spike the managed engine as Wasm for the JVM: one engine, no .NET runtime on the server.
9. **WACS publishes IL2CPP device numbers or gains a second maintainer** → reconsider Wasm kernels on Unity.
10. **Lua-CSharp drops the `Unsafe` package and adds interop for types you do not own plus a debugger** → re-check the "modernise in place" decision.
11. **Apple or Google change the interpreted-code clauses** → re-read DPLA 3.3.1(B), 2.5.2, 2.5.19 and Play Device and Network Abuse.
12. **Unity 7 ships C# 14 / .NET 10 BCL on IL2CPP** → raise the language level and targets.

---

## 6. Open questions for the owner

Numbered Q-L1… so they do not collide with Q-items in other documents.

| ID | Question | Options | Recommendation |
|---|---|---|---|
| Q-L1 | How do rule authors get types and autocomplete? | (a) plain Lua + generated LuaCATS; (b) Teal compiled to Lua; (c) TypeScriptToLua with `luaTarget: "5.2"`; (d) Luau syntax via upstream's unmerged `luau` branch (types parsed, not checked) | **(a) now**, then a one-week spike comparing (b) and (c) on a real rule set through the fixtures. (d) only if Luau's checker is wanted and the branch merges. |
| Q-L2 | Wire format for 64-bit integers across hosts | (a) decimal strings for any int64 field (today's guidance); (b) exact JSON digits with `int64` parsing on request; (c) both, chosen by schema | **(c)**: schema marks int64 fields; strings for JS callers by default, exact digits where every host opts in. |
| Q-L3 | `pairs` order after the table rewrite | (a) keep insertion order (deterministic, current); (b) hash order (faster, host-independent only if the hash is engine-owned); (c) insertion order with an opt-out | **(a) or (c)**: determinism is a stated requirement; decide with the `spike/table-order` numbers. |
| Q-L4 | Transcendental `math.*` in rules | (a) forbid in rule profile (lint); (b) managed implementation, bit-identical; (c) document "last-bit may differ" | **(a)** for rules, **(c)** for everything else; (b) only if a rule needs it. |
| Q-L5 | Default JVM path | (a) worker (today); (b) in process NativeAOT; (c) Wasm on Endive spike; (d) CEL on cel-java for predicates | **(a)**; (b) after the stack guard and budget land; (c)/(d) only on trigger 4. |
| Q-L6 | Add a non-Turing-complete DSL layer now? | (a) no, adopt corpus + pure-rule profile; (b) CEL for predicates now | **(a)**; revisit on trigger 5. |
| Q-L7 | Unity evidence | (a) provide Unity CI secrets and test devices; (b) keep NativeAOT proxy only | **(a)**: it is the largest unknown in the route. |
| Q-L8 | Store hygiene for mobile builds | (a) narrow registered API + signed bundles + review notes; (b) rely on content only | **(a)**. |
| Q-L9 | Relationship with upstream MoonSharp v3 | (a) cherry-pick fixes; (b) rebase on v3; (c) ignore | **(a)**; upstream PRs still need the owner's approval (fork decision). |
| Q-L10 | Ship bytecode or source to clients? | (a) bytecode precompiled at publish, source maps kept server-side; (b) source | **(a)** once the dump format settles after the VM work (it changes with struct instructions). |
| Q-L11 | Browser budget | Set a size and start-time budget for the browser bundle (today 1.91 MB brotli, 550–610 ms) | Owner sets the numbers; trigger 7 uses them. |

---

## 7. Reconciled discrepancies and gaps between the surveys

| Topic | Conflict | Resolution |
|---|---|---|
| Chicory vs Endive | lua-family and platform-constraints treat Chicory (1.7.5, March 2026) as the JVM Wasm runtime; hot-update-wasm shows Chicory stopped at 1.7.5 (last commit 2026-05-27) and work continues in the Bytecode Alliance fork **Endive** (`run.endive:runtime` 1.1.0) [C]. | Use **Endive** for any spike. hot-update-wasm read the newer state. |
| Apple clause numbering | lua-family and hot-update-wasm quote DPLA "3.3.2" through a forum [S]; platform-constraints and cross-compile read the current DPLA (updated 2026-10-13): the text is **3.3.1(B)** after the 2023-12-22 reorganisation [C]. | Cite **3.3.1(B)**. |
| Fork browser size | platform-constraints lists the fork's browser size as "none published"; the fork's `hosting.md` at the same commit records 1.91 MB brotli and 550–610 ms start [C]. | Use the measured numbers. |
| `new Script()` cost | PLAN §0.1 measures the fork at ~1.2 ms (default modules); unityenhanced cites 0.64 ms from a commit body after the stack fix [S]. Different module sets or builds. | Quote ~1.2 ms as measured; the gap is registration cost either way. |
| Teal targets | lua-family quotes the README ("Lua 5.1-5.4"); cross-compile reads code generation for 5.5 too [C]. | Source code wins; irrelevant for a 5.2 engine (5.1 output). |
| HybridCLR tags | hot-update-wasm: package 9.0.3; cross-compile: `v6000.3.x-9.0.1`, `v6000.5.x-8.15.0` in another repo. | Different repositories and tag schemes; both say v9 drops 2021.3. |

**Gaps (none fatal):** no IL2CPP or mobile device numbers exist for any candidate, including ours;
the GitHub API was unreachable for the rules survey (no star or issue counts there); Jint, WACS and
CEL have no IL2CPP evidence; no Lua-on-Endive build was found; whether GraalJS 25.1.3+ ships
`JSON.parse` source access by default was not checked.

## 8. Verdict

| Option | Verdict | Why (one line) |
|---|---|---|
| Managed Lua fork (current) | **Fits** | Only single-implementation route on every host; risks are engineering and listed in §5. |
| C Lua core everywhere | Partial | Same semantics from one C source, real int64; four bindings to own. Strongest alternative. |
| Luau everywhere | Partial, later | Best tooling; mobile bindings and JVM floor not ready. |
| Jint / JS routes | Partial / No | int64 and engine count; Jint is "a bigger VM in the same place". |
| CEL | Partial, narrow | Best DSL; only for predicates, only on triggers. |
| Wasm | Partial, complement | Best for the JVM leg and deterministic kernels; weak on Unity IL2CPP. |
| HybridCLR, PuerTS fast mode | No | Break the public-mechanisms rule. |
| Cross-compilers | No (except as authoring layers) | IL2CPP cannot load new code. |
| TSTL / Teal | Fits as authoring layers | Types without a second engine. |
