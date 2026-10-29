# JavaScript / TypeScript as the script language: fit survey

Date: 2026-10-28. Scope: whether a TypeScript-first route could replace or sit beside the current managed-Lua route for backend-delivered business logic. This report covers fit for the need, not VM internals.

Evidence marks: **[C]** confirmed by reading source, docs or release artefacts. **[S]** secondary source (vendor blog, forum, third-party article, or a vendor's own benchmark). **[U]** unconfirmed, or my inference.

Clones read, all shallow and never built (not kept). Permalinks below pin the commit that was read.

## The need, restated

A backend sends business logic as scripts: rules, eligibility checks, offer and variant selection, config-driven behaviour. The same script must give identical results on:

- Unity clients: 2021.3 LTS, 2022.3 LTS and Unity 6, on Mono and IL2CPP, for iOS, Android and desktop (WebGL is a plus);
- browsers;
- JVM servers;
- plain .NET.

The scripts are trusted. The need also lists:

- exact 64-bit integers;
- JSON in and out;
- determinism;
- small size and fast cold start;
- cheap host calls;
- debuggability and types;
- only public, documented Unity mechanisms.

## Summary table

| Candidate | Version / date (latest) | Kind | Unity Mono / IL2CPP / iOS / WebGL | Browser | JVM | Plain .NET | int64 | Tooling | Licence | Fit |
|---|---|---|---|---|---|---|---|---|---|---|
| **PuerTS** (Tencent) | Unity 3.0.3, 2026-09-04; master 2026-10-22 | Bridge to native V8 / QuickJS / Node (and in 3.0, Lua and Python) | Yes / Yes / Yes (V8 `--jitless` or QuickJS) / Yes (uses the browser's own JS engine) | n/a (the browser runs the JS natively) | No | NuGet packages (desktop only, no AOT) | C# `long` maps to `bigint` | TS `.d.ts` generation, V8 inspector debugger (none for QuickJS) | BSD-3 | **Partial**: its fast IL2CPP mode binds to libil2cpp internals; V8 is large |
| **Jint** | 4.17.0, 2026-10-07; main (v5 preview) 2026-10-27 | Managed tree-walking interpreter | Yes (netstandard2.0, used by ReactUnity); IL2CPP / iOS / WebGL by being managed [S] | via .NET wasm only | No | Yes | `BigInt`; JSON `rawJSON` | DevTools/CDP package is .NET 8+ only | BSD-2 | **Partial, best JS fit for Unity / .NET** |
| **QuickJS / quickjs-ng** | Bellard 2026-06-04; ng v0.17.0, 2026-09-18 | Small C interpreter | Through bindings: PuerTS, ReactUnity (fork of unity-jsb), OneJS (Unity 6.3+) | wasm build possible | JNI bindings exist [U] | P/Invoke | `BigInt` | No debugger in PuerTS | MIT | **Partial**: native, but small |
| **ClearScript** (V8) | 7.5.1.1, 2026-09-04 | .NET binding to V8 | No mobile targets, no IL2CPP | No | No | Windows / Linux / macOS | `BigInt` maps to `BigInteger` | Full V8 debugging | MIT | **No** for clients |
| **YantraJS** | 1.2.507, 2026-10-21 | JS compiled to IL | No: uses `System.Reflection.Emit` | No | No | Yes | `BigInt`, plus a decimal extension | Little | Apache-2.0 | **No** |
| **Jurassic** | 3.2.9, 2025-02-04 | JS compiled to CIL, ES5 | No (Emit) | No | No | Yes | — | VS debugging | MIT | **No** |
| **NiL.JS** | 2.6.1722, 2026-05-04 | Managed interpreter, ES2015 | Risky: `Expression.Compile()` in its interop layer [U] | No | No | Yes | ? | Little | BSD-3 | **No** |
| **Okojo** | 0.1.2-preview.1 (NuGet, 2026-04-18); repo at 0.2.0-preview.2 | Managed register VM | No: needs .NET 10+ | No | No | .NET 10+ | `BigInt` | `.d.ts` generator | MIT | **No** (watch) |
| **Acornima / Esprima.NET** | Acornima 1.9.1 | Parser only (Jint uses it) | n/a | n/a | n/a | n/a | n/a | n/a | BSD-3 | Building block, not an engine |
| **Hermes** | master 2026-10-27; tags `hermes-v260318099.0.x` | AOT-bytecode engine for React Native | No Unity binding exists | No | No | No | `BigInt` | RN tooling | MIT | **No** |
| **Unity's own JS** | — | UnityScript was removed; WebGL `.jslib` plug-ins remain | WebGL only, ES5 plug-ins | — | — | — | — | — | — | Not a script engine |
| **GraalJS** (Polyglot) | 25.4.4.1.1 on Maven, 2026-10-26 | Truffle JS on the JVM | — | — | Yes | — | `BigInt`, `BigInteger` interop | Chrome inspector | UPL source; GFTC for Oracle builds | **Fits on the JVM** (interpreter-only on stock OpenJDK) |
| **Javet** | 6.0.2, 2026-10-04 | JNI binding to V8 / Node.js | — | — | Yes (Linux / macOS / Windows x64, Linux / macOS arm64, Android) | — | Native `BigInt` | Chrome DevTools | Apache-2.0 | **Partial**: native, heavy, one maintainer |
| **Rhino** | 1.9.2, 2026-10-27 | Pure-Java interpreter / compiler | — | — | Yes (Java 17+) | — | `BigInt` | Small debugger | MPL-2.0 | **Partial**: small, but ES coverage is incomplete |
| **Nashorn** (standalone) | 15.7, 2025-08-21 | ES5.1 plus some ES6 | — | — | Yes | — | No `BigInt` | — | GPLv2 + Classpath exception | **No** |
| **TypeScriptToLua** (hybrid) | 1.37.1, 2026-07-09 | Compiles TS to Lua 5.0–5.5 / LuaJIT / Luau | Runs on our Lua engine | via our engine | via our engine | via our engine | Inherits Lua's semantics | Full TS type checking; source maps | MIT | **Fits as an authoring layer** |

## Top findings

1. **No single JavaScript implementation reaches every target.** The JS route means three to five different engines:
   - V8, JavaScriptCore or SpiderMonkey in browsers;
   - V8-jitless or QuickJS (PuerTS), or Jint, in Unity;
   - GraalJS or Javet on the JVM;
   - Jint on .NET.

   The spec and test262 make these engines very close, but "identical results" then depends on our own conformance suite run against every engine. Today's Lua route runs one engine (our .NET fork) on every host. The JS route trades one engine we own for many engines we do not.
2. **The 64-bit integer requirement is JavaScript's weakest point.** A JS `number` is an IEEE double. Exact int64 needs `BigInt`, which:
   - throws a `TypeError` when mixed with `number`;
   - has no `Math` functions;
   - and its JSON form needs a reviver that reads `context.source` (ES2026 "JSON.parse source text access", stage 4) or a string encoding.

   Both PuerTS (`long` maps to `bigint` [C]) and GraalJS (implicit lossy conversion for foreign BigInteger [C]) push this cost into every script.
3. **The best-known Unity JS bridge breaks our rules in its fast mode, and V8 loses its main advantage on iOS.**
   - PuerTS's fast IL2CPP mode ("xil2cpp", on by default on Android, desktop and WebGL since 2.2.2) is generated C++ against libil2cpp internals (`vm/*.h`, `il2cpp-class-internals.h`) [C]. Its FAQ even tells users to edit Unity's own `il2cpp-config.h` [C]. That violates "only public, documented Unity mechanisms"; the P/Invoke mode (the iOS default) does not.
   - On iOS, PuerTS runs V8 with `--jitless` [C]. Its own benchmark shows fib(40) at 17.5 s against 20.6 s for xLua: V8 drops to Lua-interpreter speed while adding about 26 MB of static library [C].

   Even so, TypeScript itself is a real gain: types, LSP, refactoring. TypeScriptToLua (actively released, supports a `5.2` target [C]) gives that gain without changing the runtime.

## 1. JavaScript as a language for this need

### 1.1 Numbers and exact 64-bit integers

- JS has one numeric type for ordinary maths: an IEEE-754 double. Integers are exact only up to 2^53 − 1 [C, ECMA-262 Number type, <https://tc39.es/ecma262/#sec-ecmascript-language-types-number-type>].
- `BigInt` is exact and arbitrary-precision, but mixing it with `number` in arithmetic throws a `TypeError` [C, ECMA-262 `ApplyStringOrNumericBinaryOperator`, <https://tc39.es/ecma262/#sec-applystringornumericbinaryoperator>]. Rules that handle both money-like IDs and ratios must convert explicitly everywhere.
- **JSON:**
  - `JSON.parse` turns large integers into doubles and loses precision.
  - The "JSON.parse source text access" proposal is at **stage 4** [C, <https://github.com/tc39/proposal-json-parse-with-source>]. A reviver can then read the raw digits through `context.source`, and `JSON.rawJSON` emits exact digits.
  - Engine support, where read:

    | Engine | Support |
    |---|---|
    | Jint | `Jint/Native/Json/JsRawJson.cs` [C] |
    | QuickJS (Bellard) and quickjs-ng | `rawJSON` symbols in `quickjs.c` [C] |
    | GraalJS 24.0+ | Implemented, but only in staging mode at the time [C, CHANGELOG]; whether the ES2026 default in 25.1.3 includes it was not checked [U] |
    | V8, JavaScriptCore, SpiderMonkey | Implementation tickets linked from the proposal [C]; shipping versions not verified [U] |
    | Older browsers | No support [U] |

  - Portable contract: carry int64 as decimal strings in JSON and convert to `BigInt` at the boundary. This works on every engine but is noisier for script authors.
- **Host bridges:**
  - PuerTS generates `bigint` as the TypeScript type for C# `long` and `ulong` [C, <https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Src/Generator/Utils.cs#L449-L452>].
  - GraalJS says foreign `BigInteger` needs an explicit `BigInt()` cast, and that arithmetic otherwise "perform[s] an implicit lossy conversion to double" [C, GraalJS CHANGELOG, <https://github.com/oracle/graaljs/blob/220f582dbd3c4bc65e7ca1c8348bddaaf2196fbb/CHANGELOG.md>].
- **Compared with Lua:** Lua 5.3+ has a native 64-bit integer subtype. Lua 5.2, and MoonSharp's double-only numbers, have the same 2^53 problem; the fork already adds exact long/ulong handling. TypeScript adds no help here: `bigint` and `number` are separate types. The type checker catches mixing at compile time, which is a small gain.

### 1.2 Determinism across engines

What the spec pins down, and where JS beats Lua [C, ECMA-262]:

- `Number::toString` returns the shortest digits that round-trip. Every conforming engine prints doubles identically. Lua's `tostring` uses a C format such as `%.14g`, so it depends on the implementation.
- Property order of ordinary objects is specified: integer-like keys ascending, then string keys in insertion order (`OrdinaryOwnPropertyKeys`). `JSON.stringify` output is therefore deterministic. Lua table iteration order is unspecified.
- `Array.prototype.sort` has been required to be stable since ES2019.

What it does not pin down:

- `Math.sin`, `exp`, `pow` and the other transcendental functions are "implementation-approximated" [C, <https://tc39.es/ecma262/#sec-function-properties-of-the-math-object>].
  - Jint calls `System.Math.Exp` and the like [C, <https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Native/Math/MathInstance.cs#L383>], which follows the platform's C runtime.
  - quickjs-ng calls the platform `sin()` [C, <https://github.com/quickjs-ng/quickjs/blob/7a23d8e3fedb010070ef1a03873c55500c7aa1a9/quickjs.c#L49060>].
  - The Lua route has the same exposure through `math.*`.
- `Date` parsing of non-ISO strings, `Intl` output and `toLocaleString` differ between engines. ICU data also differs: Jint ships its own CLDR provider; browsers ship their own ICU.
- Native engines built from C or C++ (QuickJS, V8) may let the compiler contract `a*b+c` into an FMA instruction on some targets [U].

Rules have to stay in integer, `+ - * /` and comparison territory, under either language, if results must be bit-identical.

**Engine count is the main determinism risk.** With JS, the same script meets V8 (Chrome, Javet, PuerTS), JavaScriptCore (Safari), SpiderMonkey (Firefox), Jint, QuickJS and GraalJS. test262 pass rates are high (Jint reports 99.9 % of its pinned corpus [C, Jint README]), but each engine has its own bugs. A cross-engine golden-test suite in CI becomes mandatory, not optional [U].

### 1.3 iOS: no JIT, so V8's advantage disappears

- iOS forbids writable executable memory to normal apps. V8's `--jitless` mode exists for this.
  - V8's own measurements, on an x64 Linux desktop: about 40 % slower on Speedometer 2.0 and 80 % slower on the Web Tooling Benchmark. Most of the cost comes from losing TurboFan and from interpreted regular expressions [S, <https://v8.dev/blog/jitless>].
  - PuerTS turns it on for iOS and OpenHarmony [C, <https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/native/papi-v8/source/BackendEnv.cpp#L219-L220>].
- PuerTS's published iPhone XS Max numbers (their table; conditions in the doc: xLua with Lua 5.3, PuerTS with V8 9.4) [C doc / S numbers, <https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/performance/index.md>]:

  | Workload | C# | xLua | PuerTS (V8 jitless) |
  |---|---|---|---|
  | fib(40) | 501 ms | 20,581 ms | 17,513 ms |
  | 200 M script-to-script calls | 282 ms | 9,093 ms | 10,896 ms |

  On Android, where V8 can JIT, fib(40) is 1,899 ms against 20,771 ms for xLua.
- QuickJS has no JIT anywhere, so it behaves the same on every platform.
- For small rule scripts, both cold start and host calls matter more than raw throughput (§2).

### 1.4 Store policy for downloaded scripts

The policies are language-neutral. Lua and JS sit in the same place.

- **Google Play:** "an app may not download executable code (such as dex, JAR, .so files) from a source other than Google Play. This restriction does not apply to code that runs in a virtual machine or an interpreter where either provides indirect access to Android APIs (such as JavaScript in a webview or browser)" [C, <https://support.google.com/googleplay/android-developer/answer/9888379>].
- **Apple 2.5.2:** apps may not "download, install, or execute code which introduces or changes features or functionality of the app" [C, <https://developer.apple.com/app-store/review/guidelines/>].
- **Apple 4.7:** explicitly allows "HTML5 and JavaScript mini apps and mini games", with duties under 4.7.1–4.7.5: an index, content filtering, and no exposing native APIs without permission [C, same page]. That clause targets mini apps, not hidden business rules. It does not make JS safer than Lua for this use [U].
- Server-driven rules that do not change what the app is for are common practice. The safe position under either language is to keep scripts within behaviour that was reviewed [U].

### 1.5 TypeScript tooling

- **Gains:**
  - static types for the host API, through `.d.ts` files generated from C# (PuerTS generator [C], Okojo's DocGenerator [C]);
  - autocomplete and rename in any LSP editor;
  - refactoring;
  - the npm ecosystem for test runners;
  - source maps.
- TypeScript 7.0 (the native Go compiler) reached GA on 2026-07-08 [S, <https://www.infoworld.com/article/4196378/go-based-typescript-7-0-arrives.html>].
- **Debugging differs by engine:**

  | Engine | Debugging |
  |---|---|
  | V8 in PuerTS | Chrome or VS Code inspector [C, <https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/doc/unity/en/knowjs/debugging.md>] |
  | QuickJS in PuerTS | "does not support debugging" [C, PuerTS FAQ] |
  | Jint | Chrome DevTools via `Jint.DevTools`, which targets .NET 8 / 10 only, so not inside Unity [C, <https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/docs/guide/supported-platforms.md>] |
  | GraalJS, Javet | Chrome inspector [C] |
  | Browser | Native devtools |

- **TypeScript does not need a JS runtime.** TypeScriptToLua 1.37.1 (2026-07-09, MIT) compiles TS to Lua with targets `universal`, `5.0`–`5.5`, `JIT` and `Luau` [C, <https://github.com/TypeScriptToLua/TypeScriptToLua/blob/0c3a141280d6796c1a1906210622bda8ea4180a2/src/CompilerOptions.ts#L63-L73>]. The changelog shows active fixes: TypeScript 6.0 upgrade, try/catch, promise semantics [C, CHANGELOG]. Lua stack traces map back to TS lines through source maps [C, changelog 1.34.0]. A TS-to-Lua pipeline keeps one engine everywhere and gives authors types. The cost: TS-to-Lua semantic gaps (array indexing, `>>>`, async generators unsupported [C, changelog 1.37.0]) and a build step.

## 2. Unity and .NET engines

### 2.1 PuerTS (Tencent)

- **What it is:** a bridge from Unity, Unreal and .NET to native script backends.
  - Unity 3.0 (2025-12-05) introduced a "core + language extension pack" design. The same `ScriptEnv` API hosts V8, QuickJS, Node.js, **Lua** and **Python** backends; several can coexist [C, <https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/changelog.md>, README].
  - Current release: Unity_v3.0.3 (2026-09-04). The 2.2.3 LTS line is still tagged. Master was last committed 2026-10-22 [C].
  - Maintained by a Tencent team, BSD-3 [C].
- **Unity reach:**
  - Supports Unity 2018.3+ via UPM [C install.md].
  - Ships native plug-ins for Android (arm64, armv7, x86_64), iOS (static `.a`), macOS, Windows, Linux, OpenHarmony and WebGL [C, release archive listing].
  - WebGL: JS runs in the page's own engine with the browser's JIT. Their example: 100k fib(12) takes 165 ms against 6,200 ms for xLua on WebGL [S vendor, <https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/webgl/readme.md>].
- **Interop modes:**
  - **P/Invoke**: public mechanism; the iOS default.
  - **"xil2cpp"**: generated C++ that calls into IL2CPP directly. It includes `vm/Object.h`, `vm/Reflection.h`, `vm/MetadataCache.h`, `il2cpp-class-internals.h`, `gc/WriteBarrier.h` [C, <https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/upms/core/Editor/Resources/puerts/xil2cpp/Puerts_il2cpp.cpp.txt#L9-L32>].
    - It is enabled by default on Windows, macOS, Linux, Android and WebGL since 2.2.2 [C, performance/il2cpp.md].
    - The FAQ tells iOS users to copy IL2CPP headers by hand and to patch Unity's `il2cpp-config.h` [C, same file].
    - **This mode breaks our "public, documented mechanisms only" rule.** Using PuerTS would mean forcing `PUERTS_DISABLE_IL2CPP_OPTIMIZATION` everywhere and accepting the slower P/Invoke path.
- **Performance:** vendor table on Vivo and iPhone, 200k calls per row [S vendor, C doc]. With xil2cpp static wrappers, host calls run 9–49 ms per 200k on Android, about 45–245 ns per call. The iOS P/Invoke path ("puer S") runs 28–43 ms per 200k for primitive signatures, slower than xLua's 10–32 ms in the same table.
- **Size:** what the 3.0.3 release archives ship, before any stripping by Gradle or Xcode [C, listing of `PuerTS_*_3.0.3.tar.gz`, <https://github.com/Tencent/puerts/releases/tag/Unity_v3.0.3>]:

  | Library | Android arm64 | iOS (static) | Windows x64 |
  |---|---|---|---|
  | V8 backend `libPapiV8` | 18.5 MB | 26.4 MB `libwee8.a` + 0.5 MB `libPapiV8.a` | 19.1 MB |
  | QuickJS backend `libPapiQuickjs` | 6.6 MB (likely unstripped [U]) | 1.25 MB | 0.9 MB |
  | Core `libPuertsCore` | 0.4 MB | 0.05 MB | — |
  | `WSPPAddon` (websocket) | 1.8 MB | 3.2 MB | — |
  | Lua backend `libPapiLua` | 1.95 MB | 1.27 MB | — |

  No published per-app size delta was found.
- **int64 and JSON:** `long` and `ulong` map to `bigint` [C]. JSON uses the engine's own `JSON` object.
- **Tooling:** `.d.ts` generation; VS Code debugging on V8; none on QuickJS [C FAQ].
- **Other targets:**
  - JVM: none.
  - .NET: NuGet `Puerts.*.Complete` 3.0.3 (2026-09-05) for net8/9/10. Listings say desktop only, "AOT compilation is not currently supported" [S, <https://www.nuget.org/packages/Puerts.QuickJS.Complete>].
- **Showstoppers for us:** the libil2cpp-internals fast path; V8's size on mobile; a different engine per platform; a native dependency with recent crash fixes. The 3.0.3 changelog lists use-after-free, heap corruption in V8 WeakMap and deadlock fixes [C changelog].

### 2.2 Jint

- **What it is:** a managed ECMAScript interpreter with no native code and no runtime code generation [C README].
  - 4.x is the maintenance line (4.17.0 on NuGet, 2026-10-07); v5 is in development on `main` (last commit 2026-10-27) [C].
  - Maintainers: Sébastien Ros and Marko Lahma; BSD-2 [C].
  - Used by RavenDB, EventStoreDB, Orchard Core, Elsa and Docfx [C README].
- **Targets:** `net472; netstandard2.0; netstandard2.1; net8.0; net10.0` [C, <https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint/Jint.csproj#L4>].
  - v5 keeps `netstandard2.0` on purpose [C, migrating-to-v5.md].
  - It compiles with `AllowUnsafeBlocks` [C], so some unsafe code is present.
- **Unity:**
  - No official Unity statement.
  - ReactUnity ships a `com.reactunity.jint` package for Unity 2019.4+: `Jint.dll` 2.59 MB IL, `Acornima.dll` 0.37 MB, plus `System.Memory`, `System.Buffers`, `System.Numerics.Vectors` and `System.Runtime.CompilerServices.Unsafe` [C, <https://github.com/ReactUnity/core/tree/ea833cc651423c7d86421babf640f3a77f64a355/unity/jint>].
  - Being managed, it reaches IL2CPP, iOS and WebGL by the same route as our Lua fork [U, not measured here].
  - The only IL2CPP issue found is Unity tracker 797130 from Unity 5.4, closed "By Design" [S, <https://issuetracker.unity3d.com/issues/il2cpp-build-throws-a-managed-exception-at-runtime-with-jint>].
  - Jint's AOT notes warn about "generic instantiations over value types discovered only at runtime" [C, docs/reference/native-aot.md]. That class of problem also bites IL2CPP [U].
- **Performance:** Jint's own BenchmarkDotNet report, Jint 4.16.0, .NET 10.0.11, Ryzen 9 5950X, fresh engine per operation, 2026-08-13 [S vendor, <https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/docs/guide/engine-comparison.md>]:
  - Minimal prepared script: 1.094 µs, against 379.5 µs for ClearScript-V8.
  - Host interop is 3.4–11.2× faster than ClearScript.
  - V8 wins the compute-heavy loops.
  - No numbers exist for Jint under IL2CPP or on mobile.
- **int64 and JSON:** `BigInt`, `JSON.rawJSON` and `context.source` [C].
- **Tooling:**
  - Jint core has debugging hooks.
  - The Chrome DevTools server (`Jint.DevTools`) needs .NET 8+ [C], so in Unity debugging is limited to host-side hooks.
  - No `.d.ts` generator ships with Jint [U].
- **Fit:** the only JS engine that matches our Lua fork's deployment model (managed, one DLL, every .NET host). It does not cover the JVM, and the browser would run either Jint inside .NET wasm (one engine) or native JS (another engine).

### 2.3 QuickJS and quickjs-ng bindings

- **Engines:**
  - Bellard's QuickJS: latest release 2026-06-04, commit 2026-10-26. Advertised as "367 KiB of x86 code" with "complete life cycle of a runtime instance ... less than 300 microseconds" [S vendor, <https://bellard.org/quickjs/>].
  - quickjs-ng: v0.17.0 (2026-09-18), commit 2026-10-24, maintained by Ben Noordhuis and Saúl Ibarra Corretgé. MIT [C].
- **Unity bindings:**
  - PuerTS's QuickJS backend builds quickjs-ng from source [C changelog 2.2.0].
  - **ReactUnity `com.reactunity.quickjs`** is a fork of unity-jsb on quickjs-ng. Commit 2026-10-20, MIT. It covers 11 native targets plus WebGL, where it hands the code to the browser's engine.
    - Shipped binary sizes: Windows x64 `quickjs.dll` 1.14 MB, iOS `libquickjs.a` 1.49 MB, Android arm64 `.so` 7.4 MB (probably unstripped [U]) [C, <https://github.com/ReactUnity/core/tree/ea833cc651423c7d86421babf640f3a77f64a355/unity/quickjs>].
  - unity-jsb upstream: last commit 2023-09-01, so dormant [C].
  - OneJS uses QuickJS on desktop and mobile and the browser engine on WebGL, but needs **Unity 6.3+** [C, OneJS README at <https://github.com/Singtaa/OneJS/tree/8a3f5e329632804ee5ec7ebd571901386dbf773a>]. That rules it out for 2021.3 and 2022.3.
- **.NET outside Unity:** no maintained general-purpose quickjs-ng NuGet binding was found [S search]. PuerTS's desktop NuGet is the closest.
- **Fit:**
  - Small, deterministic interpreter speed on all platforms, `BigInt`, and the same C code could in principle also be compiled to wasm and JNI.
  - It is unsafe native code with no debugger in the Unity bindings.
  - For our trusted, small rules it is a plausible "one native engine everywhere" option, but only if we own the bindings [U].

### 2.4 ClearScript

- V8 for .NET 5+, .NET Framework 4.6.2+ and .NET Standard 2.1 on Windows, Linux and macOS only [C, <https://github.com/microsoft/ClearScript/blob/df0ce7425261f8ef48684c1d4ca7e0fbfdd4ef2d/ReadMe.md>]. No Android, iOS or IL2CPP.
- 7.5.1.1, 2026-09-04, MIT.
- Good for a .NET server, not for clients.

### 2.5 YantraJS, Jurassic, NiL.JS: compile-to-IL engines

- **YantraJS 1.2.507** (2026-10-21, Apache-2.0) compiles JS to IL with `System.Reflection.Emit` (`RuntimeMethodBuilder.cs` imports `System.Reflection.Emit`) [C, <https://github.com/yantrajs/yantra/blob/9cf0ba07734ba852a82142a7c375827b9997efb9/YantraJS.ExpressionCompiler/Runtime/RuntimeMethodBuilder.cs>].
- **Jurassic** compiles to CIL; ES5 with partial ES6; last release 2025-02-04 [C README].
- Unity: "An AOT platform can't implement any of the methods in the System.Reflection.Emit namespace" [C, <https://docs.unity3d.com/6000.0/Documentation/Manual/scripting-restrictions.html>]. Both are therefore out for IL2CPP.
- **NiL.JS 2.6.1722** (2026-05-04, BSD-3) is an ES2015 interpreter, but its `MethodProxy` builds interop thunks with `Expression.Compile()` [C, <https://github.com/nilproject/NiL.JS/blob/b41e8e40dca31fe6fe179e0cfc2f4bd13ae96eea/NiL.JS/Core/Functions/MethodProxy.cs#L228>]. IL2CPP behaviour is unverified [U]. It is effectively a single-maintainer project.

### 2.6 Okojo

- A managed register-VM JS engine by the author "akeit0".
- 100 % of non-legacy test262 is claimed; MIT; NuGet 0.1.2-preview.1; repo at 0.2.0-preview.2 (2026-09-21) [C, <https://github.com/akeit0/okojo/tree/478ec2d34df6f880e6e8fb2439419cb82c677c19>].
- **".NET 10+"** only, so no Unity, and still prerelease.
- In Jint's benchmark it was the slowest lane on small scripts (minimal script 1,154 µs) [S vendor (Jint)].
- Worth watching: the register-VM design ideas parallel the Lua-CSharp work.

### 2.7 Parsers: Esprima.NET, Acornima

`esprima-dotnet` has been succeeded by **Acornima** (1.9.1, BSD-3), which Jint uses [C, ReactUnity bundles `Acornima.dll`]. It is a parser, useful for static checks of scripts (an allow-list of syntax) on any .NET host, but not an engine.

### 2.8 Hermes

Hermes is Meta's engine for React Native, with AOT bytecode for fast start-up; MIT; very active (commit 2026-10-27) [C README]. There is no Unity or .NET binding and no documented C embedding API [S, <https://docs.sheetjs.com/docs/demos/engines/hermes>]. Static Hermes compiles to native code, which does not fit downloaded logic. Not a candidate.

### 2.9 Unity's own JavaScript options

- UnityScript was removed years ago [S, Unity blog "UnityScript's long ride off into the sunset", 2017].
- Today Unity only supports `.jslib` / `.jspre` plug-ins for Web builds, written in ES5: "ES6 isn't yet supported" [C, <https://docs.unity3d.com/6000.0/Documentation/Manual/web-interacting-browser-js.html>]. This is the bridge PuerTS-WebGL and ReactUnity use to reach the browser's engine. It is not a scripting runtime for other platforms.

## 3. JVM engines

### 3.1 GraalJS (GraalVM Polyglot)

- **Versions:** latest `org.graalvm.polyglot:js` / `js-community` on Maven Central is **25.4.4.1.1** (2026-10-26) [C, maven-metadata]. ECMAScript 2026 has been the default since 25.1.3 [C CHANGELOG].
- **Licence:** source is UPL-1.0 [C LICENSE]. The `js` artefacts built on Oracle GraalVM are under the GraalVM Free Terms and Conditions; `js-community` is the Community Edition [C, <https://www.graalvm.org/latest/reference-manual/embed-languages/>].
- **Performance depends on the JVM:**
  - Oracle GraalVM 25 runs the optimizing runtime.
  - On **Oracle JDK 25 or OpenJDK 25** the optimizing runtime is available "Polyglot isolate only". Otherwise it falls back to "interpreter-only mode, without runtime compilation to machine code" and logs a warning, silenced with `-Dpolyglot.engine.WarnInterpreterOnly=false` [C, same page].
  - "When executed without the Graal Compiler, JavaScript performance will be significantly worse" [C, <https://github.com/oracle/graaljs/blob/220f582dbd3c4bc65e7ca1c8348bddaaf2196fbb/docs/user/RunOnJDK.md>].
  - On a stock Spring Boot JVM, rule evaluation would be interpreted unless the service moves to GraalVM.
- **Footprint:** `truffle-api` alone is a 17 MB jar [C, Maven HEAD].
- **int64:** Java `long` arguments choose overloads by value range [C, docs/user/JavaInteroperability.md]. `BigInteger` needs explicit `BigInt()` [C CHANGELOG].
- **Fit:** the strongest JS option on the JVM: pure Java, Oracle-maintained, current ECMAScript. It is one more engine to keep in conformance with the clients.

### 3.2 Javet

- V8 and Node.js embedded through JNI.
- 6.0.2 (2026-10-04) bundles Node.js v26.10.0 and V8 15.5.35.14. Apache-2.0. Effectively one maintainer, who asks for donations [C, <https://github.com/caoccao/Javet/blob/1b9ccf77217d86646ea6b8b39f257c35aa1fd332/README.rst>].
- Platforms:
  - Linux and macOS: x86_64 and arm64.
  - Windows: x86_64 only.
  - Android: all four ABIs.
- Native `BigInt`, an engine pool, Spring integration, Chrome DevTools [C].
- Node artefact for Linux x86_64 is 32.7 MB [C, Maven HEAD].
- The same V8 family as Chrome, which helps browser parity, but it is a native dependency on the server.

### 3.3 Rhino

- Pure Java (17+), MPL-2.0, Mozilla community.
- 1.9.0 (2025-12-22) brought "Performance improvements from 10 to 30%" plus ES6+ work: destructuring, spread, named groups [C, RELEASE-NOTES.md]. 1.9.1 followed on 2026-02-15, and 1.9.2 jar was published 2026-10-27 [C].
- Jar is 1.68 MB [C].
- The next line is 2.0.0 with announced compatibility breaks [C].
- ES coverage is still incomplete [C, compat table link in README]. Rules written against modern TS output would need a conservative `target`.

### 3.4 Nashorn (standalone)

ES5.1 with some ES6; 15.7 (2025-08-21); GPLv2 with Classpath exception; "still a work in progress" for documentation [C, <https://github.com/openjdk/nashorn/blob/4923f1e70e460d87c0eeb1f5232344487d46108d/README.md>]. No `BigInt`. Not a candidate.

## 4. Browser

JS runs natively, with no runtime download. This is the single biggest gain of a JS route. Today the browser path ships a .NET WebAssembly runtime plus our Lua engine. The JS path ships nothing.

The browser engine is chosen by the user: V8, JavaScriptCore or SpiderMonkey, with versions we do not control. Features used by rules must be limited to a baseline. `JSON.parse` with source, for example, may be missing on older Safari versions [U].

## 5. What a TypeScript-first approach would cost and gain

### Option A: TS compiled to JS, native engine per platform

Browser native; Unity on PuerTS (V8 or QuickJS, P/Invoke mode) or Jint; JVM on GraalJS or Javet; .NET on Jint.

| | Gain | Cost |
|---|---|---|
| Authoring | Types, LSP, refactoring, npm test tooling; large hiring pool | TS-to-JS compile step |
| Browser | No runtime download; JIT speed | Engine and version spread across Chrome, Safari and Firefox |
| Unity | WebGL runs on the browser engine (fast); V8 on Android JIT | iOS V8 is jitless, about Lua speed [C]; V8 adds about 18–26 MB per ABI; QuickJS has no debugger; PuerTS fast mode is off-limits; Jint is an interpreter with unknown IL2CPP speed |
| JVM | GraalJS or Javet are mature | GraalJS is interpreted on stock OpenJDK; Javet is native |
| int64 | `BigInt` is exact | No mixing with `number`; JSON needs `context.source` or strings; host bridges differ (PuerTS `bigint`, GraalJS lossy by default) |
| Determinism | Spec'd number printing, key order, stable sort | Five or more engines to keep in lock-step; Math, `Date` and `Intl` differ; a cross-engine CI suite becomes mandatory |
| Ownership | Nothing to maintain in the VM | Bugs in third-party native engines (PuerTS 3.0.3 fixed heap corruption and use-after-free [C]) are outside our control |

### Option B: TS compiled to JS, one managed engine (Jint) in every .NET host

The browser stays on .NET wasm, as today. The JVM keeps calling a .NET worker, as today.

- **Gain:** one engine, so the same determinism story as the Lua fork; TS authoring; `BigInt` and modern JSON.
- **Cost:**
  - Jint.dll is about 2.6 MB IL plus dependencies;
  - interop with Unity types goes through reflection-heavy CLR projection, and its IL2CPP performance is unmeasured;
  - no native-browser win;
  - JS numbers still need the `BigInt` discipline.
- In effect this swaps the Lua VM for a larger JS VM while keeping everything else.

### Option C: TS compiled to Lua (TypeScriptToLua) on our existing engine

- **Gain:** TS types, LSP and source maps for authors, with no runtime change, one engine, our int64 and JSON contract, and no new native code.
- **Cost:**
  - TS-to-Lua semantic gaps (0-based arrays mapped to Lua tables, no async generators, some operators restricted per target [C changelog]);
  - generated Lua is harder to debug at runtime unless source maps are wired into our tracebacks;
  - number semantics remain those of our engine, not JS.

### Unchanged in all options

Store policy is the same. Transcendental Math is still non-deterministic. The scripts are trusted, so sandbox strength does not decide anything.

## 6. Verdict for our need

| Candidate | Verdict | Why |
|---|---|---|
| PuerTS | **Partial / no as the primary route** | Reaches every Unity target and is very active. Its default fast IL2CPP path uses libil2cpp internals (forbidden here). V8 is large and jitless on iOS. QuickJS has no debugger. A different engine per platform. Its 3.0 Lua backend is a reminder that Tencent itself now offers Lua beside JS. |
| Jint | **Partial** (best JS fit for Unity and .NET) | Managed, active, conformant, `BigInt` and raw JSON. No JVM, IL2CPP performance unknown, DevTools not available in Unity, larger than a Lua VM. |
| QuickJS / quickjs-ng (via ReactUnity, PuerTS) | **Partial** | Small and portable, one C engine for every target in theory. Native and unsafe, no Unity debugger, we would own the bindings. OneJS needs Unity 6.3+. |
| ClearScript | **No** | No mobile targets, no IL2CPP. |
| YantraJS, Jurassic | **No** | Reflection.Emit cannot run on AOT platforms. |
| NiL.JS | **No** | Expression-compiled interop; small project. |
| Okojo | **No** (watch) | .NET 10+ only, prerelease. |
| Hermes | **No** | No Unity or .NET embedding. |
| Unity's own JS | **No** | Only WebGL `.jslib` glue. |
| GraalJS | **Fits** (JVM only) | Best JVM JS engine. Interpreter-only on stock OpenJDK; 17 MB+ footprint; one more engine to keep in lock-step. |
| Javet | **Partial** (JVM) | V8 parity with Chrome; native, heavy, one maintainer. |
| Rhino | **Partial** (JVM) | Small and pure Java. ES coverage gaps; 2.0 compatibility breaks ahead. |
| Nashorn | **No** | ES5.1, no `BigInt`. |
| TypeScriptToLua | **Fits as an authoring layer** | Gives the TypeScript gain without leaving the single-engine Lua route. |

**Overall:** a TS-first, JS-at-runtime approach is feasible. It wins clearly in the browser and in authoring tooling. For this need it loses where the need is strictest:

- identical results, because one engine becomes five or more;
- exact int64, because `BigInt` cannot mix with `number`;
- the Unity rule set, because PuerTS's fast path is forbidden and V8 is large and jitless on iOS.

The JS candidate that best preserves the current architecture is Jint (Option B). It is effectively "a bigger VM in the same place", so it does not justify a switch on its own.

If the driver is developer experience (types and autocomplete), Option C (TypeScriptToLua onto the existing engine) is the cheaper way to get it. Spike it first [U]: compile a sample rule set with `luaTarget: "5.2"` and run it through the existing conformance fixtures.

## Sources (main)

- PuerTS repo at `c5cd0965598c4f34c6fc920d1dcd721db83e551e`: <https://github.com/Tencent/puerts/tree/c5cd0965598c4f34c6fc920d1dcd721db83e551e>
  - Changelog: <https://github.com/Tencent/puerts/blob/c5cd0965598c4f34c6fc920d1dcd721db83e551e/unity/changelog.md>
  - Release Unity_v3.0.3: <https://github.com/Tencent/puerts/releases/tag/Unity_v3.0.3>
- Jint repo at `9ec2352c222dff6b7bff806d16b087cf0f1e6384`: <https://github.com/sebastienros/jint/tree/9ec2352c222dff6b7bff806d16b087cf0f1e6384>
  - Engine comparison: <https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/docs/guide/engine-comparison.md>
- ReactUnity at `ea833cc651423c7d86421babf640f3a77f64a355`: <https://github.com/ReactUnity/core/tree/ea833cc651423c7d86421babf640f3a77f64a355>
- quickjs-ng at `7a23d8e3fedb010070ef1a03873c55500c7aa1a9`: <https://github.com/quickjs-ng/quickjs/tree/7a23d8e3fedb010070ef1a03873c55500c7aa1a9>
- QuickJS (Bellard): <https://bellard.org/quickjs/> and <https://github.com/bellard/quickjs/tree/19d6bd865e709e2b5717fb4f2290c0cecc57ee4b>
- unity-jsb: <https://github.com/ialex32x/unity-jsb/tree/2bcabe9da47a27eb0717db78f8b9710ba4476430>
- OneJS: <https://github.com/Singtaa/OneJS/tree/8a3f5e329632804ee5ec7ebd571901386dbf773a>
- ClearScript: <https://github.com/microsoft/ClearScript/tree/df0ce7425261f8ef48684c1d4ca7e0fbfdd4ef2d>
- YantraJS: <https://github.com/yantrajs/yantra/tree/9cf0ba07734ba852a82142a7c375827b9997efb9>
- Jurassic: <https://github.com/paulbartrum/jurassic/tree/17f72827150166c79dd7789bfe01a4a2f7bfe4aa>
- NiL.JS: <https://github.com/nilproject/NiL.JS/tree/b41e8e40dca31fe6fe179e0cfc2f4bd13ae96eea>
- Okojo: <https://github.com/akeit0/okojo/tree/478ec2d34df6f880e6e8fb2439419cb82c677c19>
- Hermes: <https://github.com/facebook/hermes/tree/f837b9dd8396ebf5c4e8115412417d273565f0fe>
- GraalJS: <https://github.com/oracle/graaljs/tree/220f582dbd3c4bc65e7ca1c8348bddaaf2196fbb> and <https://www.graalvm.org/latest/reference-manual/embed-languages/>
- Javet: <https://github.com/caoccao/Javet/tree/1b9ccf77217d86646ea6b8b39f257c35aa1fd332>
- Rhino: <https://github.com/mozilla/rhino/tree/997eb96c50318b56c311dda4ca02bc76bb9878a8>
- Nashorn: <https://github.com/openjdk/nashorn/tree/4923f1e70e460d87c0eeb1f5232344487d46108d>
- TypeScriptToLua: <https://github.com/TypeScriptToLua/TypeScriptToLua/tree/0c3a141280d6796c1a1906210622bda8ea4180a2>
- V8 jitless: <https://v8.dev/blog/jitless>
- JSON.parse source text access: <https://github.com/tc39/proposal-json-parse-with-source>
- Apple App Review Guidelines: <https://developer.apple.com/app-store/review/guidelines/>
- Google Play Device and Network Abuse: <https://support.google.com/googleplay/android-developer/answer/9888379>
- Unity scripting restrictions: <https://docs.unity3d.com/6000.0/Documentation/Manual/scripting-restrictions.html>
- Unity Web JS interop: <https://docs.unity3d.com/6000.0/Documentation/Manual/web-interacting-browser-js.html>
- NuGet registration data (publish dates) and Maven Central `maven-metadata.xml` (versions), queried 2026-10-28.
