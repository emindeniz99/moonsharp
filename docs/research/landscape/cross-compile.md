# Cross-compilation survey: one source, compiled to each platform's language

Date: 2026-10-28. Scope: ways of writing business rules once and compiling them ahead of time to C#, Java/JVM, JS and Lua, compared with shipping them as interpreted scripts. VM internals are covered elsewhere and are not repeated here.

Evidence marks: **[C]** confirmed by reading source or official docs; **[S]** secondary (blog, forum, issue, mirror); **[U]** unconfirmed or my inference.

## Summary table

| Candidate | What it is | Latest state (Oct 2026) | Reaches Unity IL2CPP / browser / JVM / .NET | Can logic change without an app release on Unity iOS? | Fit |
|---|---|---|---|---|---|
| **Haxe** | Typed language compiled to JS, JVM, Lua, C++, HashLink, Python, PHP (C#/Java in 4.x only) | 4.3.7 stable (2025-05-09); 5.0.0-preview.1 (2025-07-04); default branch commit 2026-10-28. C# and Java targets **removed** on the Haxe 5 branch; new C# target is a stalled draft PR | Unity: 4.x C# target (Tier 3) or Haxe->Lua run by our interpreter. Browser: JS. JVM: JVM target. .NET: 4.x C# target | Only by compiling to Lua and interpreting it, which brings back the interpreter | **No** |
| **Kotlin Multiplatform** | Kotlin compiled to JVM, JS/Wasm, native (LLVM) | 2.4.21 (2026-10-08); 2.5.0 planned Dec 2026 | Unity: only as a native library over the C ABI (P/Invoke). Browser: Kotlin/JS or Wasm. JVM: native. .NET: C ABI only | No: native code cannot be downloaded | **No** |
| **Fable (F#)** | F# compiled to JS/TS (stable), Python, Dart (beta), Rust (alpha), PHP, Beam (experimental) | 5.24.0 (2026-10-22); default branch commit 2026-10-27; MIT | Unity/.NET: F# compiles to normal .NET IL. Browser: Fable JS. JVM: **no target** | No: a new IL assembly cannot be loaded under IL2CPP | **No** |
| **TypeScriptToLua (TSTL)** | TypeScript compiled to Lua 5.0 to 5.5, LuaJIT, Luau | 1.37.1 (2026-07-09); MIT | Output is Lua, so it reaches every platform the Lua interpreter reaches | **Yes**, because the shipped artifact is still interpreted Lua | **Partial**: useful as an authoring layer |
| **Teal** | Typed Lua dialect compiled to plain Lua (5.1, 5.3, 5.4, 5.5 code generation) | 0.24.8 (2025-10-13); default branch commit 2026-10-26 (next major in progress); language server commit 2026-07-27; MIT | Same as TSTL: the output is Lua | **Yes** (interpreted Lua) | **Partial / best authoring fit** |
| **CSharp.lua** | C# (Roslyn) compiled to Lua 5.3 (or 5.1 with `-c`) plus a CoreSystem.lua runtime | v1.2 tag; default branch commit 2026-09-22; Apache-2.0 | The output is Lua. The same C# can also be compiled natively into the app | **Yes** for the Lua output | **Partial** (int64 needs Lua 5.3 integers) |
| **Nelua** | Lua-like systems language compiled to C | Alpha; last commit 2025-06-24 | Native only | No | **No** |
| **Lua to JVM bytecode (LuaJ `luajc`)** | Lua 5.2 compiled to Java bytecode on top of the LuaJ runtime | 3.0.2; last commit 2020-04-01 | JVM only | Not applicable (server side) | Shows the "AOT tier over the same runtime" pattern |
| **NeoLua** | Lua 5.3 compiled at runtime to .NET through the DLR | v1.3.19; commit 2026-03-27 | .NET with a JIT. IL2CPP not suitable [U] | No on IL2CPP | **No** |
| **Server-generated code** | Server emits C#/Java/JS from a DSL | n/a | JVM and browser can load new code at runtime. Unity IL2CPP cannot | No: it must be shipped as interpreted script or data | **Server/browser only** |

**Short answer.** Ahead-of-time transpilation **cannot replace the interpreter** on Unity IL2CPP (iOS, 64-bit Android, WebGL) for logic that must change without an app release. The runtime cannot load new code there, and both stores allow downloaded logic only as interpreted code. Transpilation is a better fit for three narrower jobs:
1. a **typed authoring language compiled to Lua** (Teal first, TSTL second);
2. **pre-compiling scripts to interpreter bytecode on the server**, for cold start and early error detection;
3. optionally, a **compiled tier for rules baked into the build or running on the server**, on top of the same runtime library (the LuaJ `luajc` pattern), used only if profiling shows the interpreter is the bottleneck.

---

## 1. The hard constraint: what can change on the client without a release

### 1.1 Technical: IL2CPP cannot take new code
- Unity: "An AOT platform can't implement any of the methods in the System.Reflection.Emit namespace", and `dynamic` "requires JIT compilation, which is not possible with IL2CPP". [C] https://docs.unity3d.com/6000.2/Documentation/Manual/scripting-restrictions.html
- On 64-bit Android, Mono has no ARM64 support, so Play builds use IL2CPP. [S] https://blog.unity.com/cn/technology/meeting-google-play-requirements-in-the-future, https://discussions.unity.com/t/google-play-not-compliant-with-64-bit-despite-following-the-instructions/226704. In practice this means iOS, Android and WebGL clients all run AOT code with no way to load new IL. [U, inference from the above]
- The one widely used way to run new C# on IL2CPP, HybridCLR, "extends the il2cpp runtime" into a hybrid AOT plus interpreter runtime. [C] https://github.com/focus-creative-games/hybridclr/blob/main/README_EN.md. Latest tags as of 2026-10: `v6000.3.x-9.0.1`, `v6000.5.x-8.15.0` [C, `git ls-remote`]. It changes libil2cpp, so it **breaks the "public Unity mechanisms only" rule**. It is still an interpreter, of IL rather than Lua.

So any client-side path that updates without a release ends in an interpreter. Transpilation can only change what the interpreter runs (source language, bytecode form), not remove it.

### 1.2 Policy: stores allow downloaded interpreted code, not executable code
- Apple Developer Program License Agreement, 3.3.1(B) "Executable Code": "an Application may not download or install executable code. Interpreted code may be downloaded to an Application but only so long as such code: (a) does not change the primary purpose of the Application ... (b) does not bypass signing, sandbox, or other security features of the OS; and (c) ... does not create a store or storefront for other Applications." [C] https://developer.apple.com/support/terms/apple-developer-program-license-agreement/ (fetched 2026-10-28)
- App Review Guideline 2.5.2: apps may not "download, install, or execute code which introduces or changes features or functionality of the app". [C] https://developer.apple.com/app-store/review/guidelines/
- Precedent: App Review's 2017 warnings about JSPatch cited 2.5.2 and the ability to reach private APIs from downloaded scripts. [S] https://developer.apple.com/forums/thread/73856. Rules logic that works through a narrow host API is a different case from that, but this is the risk to watch: script bindings that expose arbitrary native or CLR APIs. [U]
- Google Play, Device and Network Abuse: an app "may not download executable code (such as dex, JAR, .so files) from a source other than Google Play". The exception is code in "a virtual machine or an interpreter" where "either provides indirect access to Android APIs". [C] https://support.google.com/googleplay/android-developer/answer/9888379

Consequence: a downloaded **Lua script or bytecode interpreted by a managed VM with a narrow host API** fits both policies. A downloaded **compiled C# assembly or native library** fits neither, even on the rare platform where it would load. [U, policy reading]

### 1.3 Other platforms can load new code
- **Browser:** new JS can be loaded at runtime (dynamic `import()`, `new Function`), subject to the page's CSP. [U, general web platform knowledge]
- **JVM:** classes can be generated and loaded at runtime. LuaJ's `luajc` does this for Lua ("runtime jit-compiling via luajc and bcel"). [C] https://github.com/luaj/luaj/blob/daf3da94e3cdba0ac6a289148d7e38bd53d3fe64/README.md
- **.NET server:** Roslyn and collectible `AssemblyLoadContext` allow runtime compilation. [U, general .NET knowledge]

The asymmetry matters. Compilation is technically possible on the server and in the browser, but not on the Unity client. **A design that compiles on some platforms and interprets on others runs two execution engines.** "Identical results everywhere" then depends on a conformance suite rather than holding by construction.

---

## 2. Candidates

### 2.1 Haxe
- **What:** statically typed language with many targets. The current target list and tiers (JS, HashLink, JVM, PHP Tier 1; C++, Lua Tier 2; C#, Python, Java Tier 3). [C] https://haxe.org/documentation/introduction/compiler-targets.html
- **Latest:** 4.3.7 released 2025-05-09 [C] https://haxe.org/download/. 5.0.0-preview.1 (2025-07-04) lists "java/cs : remove C# and Java targets (#11551)" [S, search summary of https://haxe.org/download/version/5.0.0-preview.1 and https://github.com/HaxeFoundation/haxe/wiki/Nightlies-changelog]. On the default branch at `6704768` (2026-10-28) there is no `gencs.ml`, only `genjs`, `genjvm`, `genlua`, `genhl`, `genpy`, `genphp7`, `gencpp` and others. [C] https://github.com/HaxeFoundation/haxe/tree/670476843df4986c612683b0a5c5332efd5c4dcd/src/generators
- **New C# target:** draft PR #12575 (opened 2026-02-06) by the author of the Ceramic engine. It is written in OCaml in the compiler and generates C# source, with Unity as a stated goal. The author says embedded reflection data is needed because the old approach "simply cannot work in scenario like Unity/AOT exporting C# to C++ via IL2CPP". Last activity was 2026-09-10, and the author is "not doing active work" until Haxe 5 settles. [C] https://github.com/HaxeFoundation/haxe/pull/12575, [S] https://community.haxe.org/t/a-new-c-target-for-haxe-5/4764
- **Same script everywhere:** Haxe->JS (browser), Haxe->JVM (server) and Haxe 4 C# (Unity/.NET) produce three different compiled programs with target-specific runtime semantics. None of them updates on the Unity client without a release. Haxe->Lua run by our Lua interpreter would update, but then Haxe is only an authoring layer over Lua, and a heavy one (next point).
- **64-bit ints and numbers:** `Int` is 32-bit. On Lua, "integer clamping is always required" and the generator injects `_hx_bit_clamp`. [C] https://github.com/HaxeFoundation/haxe/blob/670476843df4986c612683b0a5c5332efd5c4dcd/src/generators/genlua.ml#L2260. `haxe.Int64` is native on JVM, HL, C++ and eval, but a high/low class on JS and Lua (`std/haxe/Int64.hx` has `#if lua` branches and a `___Int64` class). [C] https://github.com/HaxeFoundation/haxe/blob/670476843df4986c612683b0a5c5332efd5c4dcd/std/haxe/Int64.hx. JSON numbers come back as `Float`, so exact int64 JSON needs custom handling. [U]
- **Lua target runtime needs:** recommends LuaRocks packages (lrexlib-pcre2, luv, luautf8, hx-lua-simdjson, and others) for full std support, but they can be skipped "if ... using an embedded Lua client". Lua versions 5.1 to 5.3 and LuaJIT are listed. [C] https://haxe.org/manual/target-lua-getting-started.html
- **HashLink:** JIT backend is x86-64 only (`src/jit_x86_64.c`); mobile and consoles use HL/C (AOT C). [C] https://github.com/HaxeFoundation/hashlink/tree/1da4f469a677d4b55c8dba69f657b77f4469c63f/src. It is not usable as a downloadable-script runtime on iOS. [U]
- **Performance:** a cross-target benchmark site exists (benchs.haxe.org). I quote no numbers because none were measured for this kind of workload. [U]
- **Tooling:** good: types, the Haxe language server, VS Code extension. [S]
- **Licence:** compiler GPL-2.0+, standard library MIT. [S]
- **Studio use:** InnoGames converted the browser client of a long-running strategy game from Flash to HTML5 with Haxe (Haxe Summit 2017 talk). This is browser-only and predates 2022. [S] https://haxe.org/videos/conferences/haxe-summit-2017/automated-conversion-from-flash-to-html5-andre-keller.html
- **Showstoppers:** the C# target is removed in Haxe 5 and its replacement is unmerged; no hot update on Unity; per-target integer semantics.
- **Verdict: No.**

### 2.2 Kotlin Multiplatform
- **Latest:** 2.4.21 (2026-10-08). 2.4 line supported until 2027-12-03; 2.5.0 planned for December 2026. [C] https://kotlinlang.org/docs/releases.html. Apache-2.0.
- **Reaches:** JVM natively; browser via Kotlin/JS or Wasm; Unity and .NET only through Kotlin/Native compiled to a `.so`/`.dylib`/`.dll` with a C header (Apple targets: frameworks recommended). [C] https://kotlinlang.org/docs/native-dynamic-libraries.html. There is no C# target. A community Gradle plugin packs Kotlin/Native libraries into NuGet packages with a generated `Interop.cs`. [S] https://proandroiddev.com/bring-your-kmp-library-to-nuget-02e1131a4707
- **Interop with Unity:** C ABI only. Every value crosses as C types, and there are two GCs in one process. No official Unity guidance; no WebGL path found. [U]
- **64-bit ints:** exact on JVM and native. On JS, `Long` is emulated unless the experimental `-Xes-long-as-bigint` (Kotlin 2.2.20+, ES2020) is used. [C] https://kotlinlang.org/docs/whatsnew2220.html
- **Hot update:** none on Unity (native code). On the browser and JVM, normal redeploy.
- **Verdict: No.** It is a release-time shared-library option for JVM plus browser, not a hot-update option for Unity.

### 2.3 Fable (F#)
- **Latest:** 5.24.0 (2026-10-22). Steady releases through Sep-Oct 2026 (5.18 to 5.24). [C] https://github.com/fable-compiler/Fable/blob/d88621c3b9d6ce48a72dc09b86a6805f612ac185/src/Fable.Cli/CHANGELOG.md. MIT.
- **Targets and stability:** JS and TypeScript stable; Dart and Python beta; Rust alpha; PHP and Beam experimental. [C] https://fable.io/docs/
- **Same script everywhere:** F# runs natively on .NET and Unity (as IL) and as JS in the browser. **No JVM target.** Two compilers (FSC and Fable) mean two semantics: in JS, int32 and smaller "are stored as doubles", with "No bound checks for numeric types". int64 uses BigInt since Fable 4.0.5, with .NET-identical semantics claimed. [C] https://fable.io/docs/javascript/compatibility.html
- **Hot update:** on Unity, F# would be a compiled assembly, so no update without a release.
- **Verdict: No.** No JVM target and no client hot update.

### 2.4 TypeScriptToLua (TSTL)
- **Latest:** 1.37.1, commit 2026-07-09; built against TypeScript 6.0.2; MIT. [C] https://github.com/TypeScriptToLua/TypeScriptToLua/tree/0c3a141280d6796c1a1906210622bda8ea4180a2
- **Targets:** `universal`, `5.0`, `5.1`, `5.2`, `5.3`, `5.4`, `5.5`, `JIT`, `Luau`. [C] https://github.com/TypeScriptToLua/TypeScriptToLua/blob/0c3a141280d6796c1a1906210622bda8ea4180a2/src/CompilerOptions.ts#L63. On `5.2`, bitwise operators are emitted as `bit32.*` calls. [C] https://github.com/TypeScriptToLua/TypeScriptToLua/blob/0c3a141280d6796c1a1906210622bda8ea4180a2/src/transformation/visitors/binary-expression/bit.ts#L75. Our Lua 5.2 interpreter ships a `bit32` module. [C, local source]
- **Same script everywhere:** works if the **Lua output** is what runs everywhere (Unity, .NET, the browser build of the interpreter, the JVM worker). It does **not** work if the original TS runs natively in the browser or JVM next to the Lua: the documented caveats include `null`/`undefined` both becoming `nil`; `0`, `""` and `NaN` being truthy in Lua; `.length` mapping to `#` with holes; unstable `sort`; object key order; Promises resolving immediately. [C] https://typescripttolua.github.io/docs/caveats
- **64-bit ints:** TS `number` maps to Lua numbers, which are doubles in Lua 5.2. No BigInt transform: `BigIntLiteral` appears only in the visitor type map, with no transformer. [C] https://github.com/TypeScriptToLua/TypeScriptToLua/blob/0c3a141280d6796c1a1906210622bda8ea4180a2/src/transformation/context/visitors.ts#L9. Exact int64 would need a branded host type plus TSTL's operator-mapping language extensions. [C, file exists: `src/transformation/visitors/language-extensions/operators.ts`; usability for int64 is U]
- **Tooling:** the full TypeScript toolchain (types, autocomplete, refactors) through `.d.ts` declarations of the host API. Debugging happens at the Lua level, and source maps are emitted. [S]
- **Size and cold start:** adds a `lualib` helper bundle (inline or as a required module). I found no published size numbers. [U]
- **Verdict: Partial.** It is a strong authoring option if the team prefers TS, but its semantic gaps from real TS mean rule authors must write "TS that knows it is Lua".

### 2.5 Teal
- **Latest release:** 0.24.8 (2025-10-13), described as "hopefully the last one in the '24 series". The default branch is active (commit 2026-10-26, `tl-dev-2.rockspec`). [C] https://github.com/teal-language/tl/blob/28b9e8148adf3aaec029c753c96034adcfd86c6b/CHANGELOG.md. Language server last commit 2026-07-27 (tags up to 0.2.1). [C] https://github.com/teal-language/teal-language-server. MIT.
- **Code generation:** the default target is 5.3; when running on Lua 5.1 or 5.2 the default becomes `"5.1"`; 5.4 and 5.5 targets exist. [C] https://github.com/teal-language/tl/blob/28b9e8148adf3aaec029c753c96034adcfd86c6b/teal/environment.lua#L93, https://github.com/teal-language/tl/blob/28b9e8148adf3aaec029c753c96034adcfd86c6b/teal/api/v1.lua#L91
- **Same script everywhere:** the output is plain Lua with type annotations erased. The semantic gap from the runtime is near zero, because the source already is Lua semantics. [C/U]
- **64-bit ints:** has an `integer` type in the checker. On a 5.2 VM, its exactness depends on the VM. [U]
- **Tooling:** types, records, generics, a language server and a VS Code extension. A smaller ecosystem than TS. [S]
- **Verdict: Partial, and the best authoring fit:** types and autocomplete for free on top of the existing interpreter, with no new runtime semantics.

### 2.6 CSharp.lua (C# to Lua)
- **Latest:** tag v1.2; default branch commit 2026-09-22 (C# 11/12 features); Apache-2.0. Default output targets Lua 5.3, and `-c` gives "classic lua version(5.1)". [C] https://github.com/yanghuan/CSharp.lua/blob/790d3cfb94a0656ce84b063f7dc72b0fc3106b4d/README.md#L61. Ships a CoreSystem.lua runtime (collections, LINQ, DateTime, and more). [C]
- **Interesting property:** the same C# rule source can be (a) compiled natively into the app and the .NET server for the baseline, and (b) transpiled to Lua for hot delivery. But (a) and (b) then disagree wherever CoreSystem.lua differs from the BCL, which is exactly the two-engine problem. [U]
- **64-bit ints:** `long` needs Lua 5.3 integers. On a double-only 5.2 VM it is not exact unless the VM has an integer subtype. [U]
- **JVM:** only by running the Lua output. **Verdict: Partial.** It is an option only if the team strongly prefers C# authoring, and it should run the Lua output on every platform rather than mixing it with native C#.

### 2.7 Nelua
- Compiles to C; "The language is in alpha state and still evolving". Last commit 2025-06-24. [C] https://github.com/edubart/nelua-lang/blob/a58450563e2d2ec49bff499865c8b5cfdf6ff81a/README.md#L12
- Native only, so there is no hot update anywhere it matters. **Verdict: No.**

### 2.8 Transpiling Lua or a DSL to C#/Java/JS ahead of time

**Precedent on the JVM: LuaJ `luajc`.** It compiles Lua 5.2 to Java bytecode over the same `LuaValue` runtime that the LuaJ interpreter uses. [C] https://github.com/luaj/luaj/tree/daf3da94e3cdba0ac6a289148d7e38bd53d3fe64/src/jse/org/luaj/vm2/luajc. The README's published timings (LuaJ 3.0, JVM and hardware not stated) are in seconds, compiled versus interpreted:

| Benchmark | luajc | interpreted |
|---|---|---|
| binarytrees 15 | 2.980 | 12.838 |
| fannkuch 10 | 5.073 | 23.290 |
| nbody 1e6 | 16.794 | 36.894 |
| nsieve 9 | 11.274 | 15.163 |

Source: [C] https://github.com/luaj/luaj/blob/daf3da94e3cdba0ac6a289148d7e38bd53d3fe64/README.md. These are CPU-bound benchmark-game programs, not rules workloads. The same README lists the cost of having two engines: "lua compiled into java bytecode using luajc cannot use string.dump() or xpcall()". [C, same file] Project last commit 2020-04-01.

**NeoLua** (Lua 5.3 through the DLR, compiled at run time; "No deployment of precompiled scripts") needs runtime code generation. Last commit 2026-03-27. [C] https://github.com/neolithos/neolua/blob/1840895f19d7d1ebaf88df58e5f75744258a3144/README.md. Unsuitable for IL2CPP. [U]

**Lua in the browser without .NET:** Fengari (Lua 5.3 VM in JS; v0.1.5; last commit 2025-12-26) and wasmoon (Lua 5.4 in Wasm; last commit 2025-01-18) are interpreters, not transpilers. [C, `git log`] I found no maintained Lua-to-JS transpiler with full semantics. [U]

**What an AOT tier would give us:**
- Rules shipped in the build or run on the server could be compiled to C# or JVM bytecode, with downloaded updates falling back to the interpreter.
- Only worth it if profiling shows interpretation dominates. For rules and eligibility checks, host calls and JSON marshalling usually dominate. [U]
- It doubles the semantic surface, and the conformance suite would have to cover both tiers.

**Bytecode instead of source:** pre-compiling scripts to the interpreter's own binary chunk format on the server removes parsing from the client cold start and moves syntax errors to publish time. The bytecode is still interpreted, so it fits store policy. This is the cheapest kind of "AOT" with a real benefit. [U]

### 2.9 Server-generated code
- **JVM and browser:** the server can generate and load Java or JS for new rules at runtime (1.3).
- **Unity:** the server can generate only interpreted artifacts (Lua source or bytecode) or data (rule tables, decision tables).
- Generating a different language per platform from one DSL is again N engines with N semantics. The verifiable form is: **one DSL, compiled once to one script or bytecode format, run by one interpreter implementation on every platform.** [U]

---

## 3. What studios publicly describe (2022-2026)

Public, verifiable material on **server-driven client logic** from large mobile studios is thin. GDC Vault redirected to a maintenance page during this research (2026-10-28), so talk abstracts could not be checked. Each item below states its date and how strong the evidence is.

| Who / what | Language and runtime | Why (as stated) | Date | Evidence |
|---|---|---|---|---|
| Defold engine (originally an in-house engine at a large casual-games studio) | Lua: LuaJIT on most platforms, interpreter-only on iOS ("iOS does not allow JIT compilation"), plain Lua 5.1.4 on HTML5; "ONLY use language features from Lua 5.1" for portability | Cross-platform consistency | current docs | [C] https://defold.com/manuals/lua/ |
| HybridCLR (Chinese mobile market) | C# interpreted inside a patched IL2CPP | Hot update C# without a release; claims "over a thousand" titles launched on App Store and Play, and "nearly a hundred" in the iOS top-500 free chart | current README | [C for the claims as written; S for their truth] https://github.com/focus-creative-games/hybridclr/blob/main/README_EN.md |
| xLua (Tencent) | Lua (C VM) with a C# hotfix injector | Patch C# methods with Lua after release | current README | [S] https://github.com/Tencent/xLua/blob/master/README_EN.md |
| Metaplay (backend SDK for mobile games) | One C# codebase "compiled to both your client and server"; deterministic fixed-point F32/F64, ordered collections; client runs actions first and the server re-executes them to validate | Server validation needs identical results | current docs | [S, search summary of] https://docs.metaplay.io/introduction/introduction-to-metaplay.html, https://metaplay.io/glossary |
| Unity Gaming Services Cloud Code | C# modules (recommended) or JavaScript scripts, **server-side** | Change logic "for all of your players instantly" by keeping it off the client | current docs | [C] https://docs.unity.com/ugs/en-us/manual/cloud-code/manual |
| Apple vs JSPatch (2017) | Downloaded JS patches through Objective-C runtime bridging | App Review warnings citing 2.5.2 and private-API reach | 2017 | [S] https://developer.apple.com/forums/thread/73856 |
| Red Hot Labs, GDC 2013 "Ditching the Server" | Sandboxed script code run on both client and server | Client-side games with server validation | 2013 (outside window) | [S, search snippet only] https://www.slideshare.net/amittmahajan/gdc-2013-ditching-the-server-final |

**Pattern [U, my synthesis]:**
- Studios that need logic to change after release on mobile use an **interpreter**: Lua (most common), JS (PuerTS; Cloud Code server-side), or IL interpretation (HybridCLR, which needs engine patching).
- Studios that need **client and server agreement** either share one **compiled** codebase and accept that logic changes need a release (Metaplay pattern), or move the logic to the server (Cloud Code pattern).
- No public source found describes shipping hot-updatable client logic through a multi-target transpiler (Haxe, KMP, Fable).

---

## 4. Determinism across transpiled targets

Each multi-target compiler documents target-specific numeric behaviour:
- **Haxe:** Int clamping on Lua; Int64 emulated on JS and Lua. [C]
- **Kotlin/JS:** `Long` emulated, or BigInt only experimentally. [C]
- **Fable:** int32 held as doubles without bound checks. [C]
- **TSTL:** truthiness, nil and ordering differences. [C]

"One source, N targets" therefore gives **N similar programs, not one program**. Identical results come most cheaply from **one execution engine** (the same interpreter) on every platform. The parallel need already does this: a managed interpreter on Unity and .NET, its WebAssembly build in the browser, and a worker on the JVM. [U, synthesis]

---

## 5. Verdict for our need

| Part of the need | AOT transpilation better than the interpreter? | Verdict |
|---|---|---|
| Unity client logic that changes without a release (IL2CPP iOS/Android/WebGL) | No. IL2CPP cannot load new code; stores allow downloaded **interpreted** code only (Apple DPLA 3.3.1(B), Play policy) | **No**: the interpreter is required |
| Authoring experience (types, autocomplete, refactors) | Yes: compile a typed language **to Lua** ahead of time. Teal (near-zero semantic gap; 5.1 output runs on 5.2) or TSTL (best tooling, documented semantic caveats, no BigInt) | **Fits** as a layer on top of the interpreter |
| Cold start and early errors | Yes: precompile to the interpreter's bytecode on the server and ship bytecode | **Fits**, cheap |
| Browser and JVM evaluation | Possible (JS or JVM bytecode generated at runtime), but it adds a second engine and puts identical results at risk; the interpreter already runs there | **Partial**: only with a conformance suite, and only if profiling demands it |
| Rules baked into the build (offline defaults) | A compiled tier over the same runtime (LuaJ `luajc` pattern: 1.3x to 4.6x faster on CPU benchmarks in its README) is possible | **Partial**: defer until profiling shows interpretation dominates |
| Haxe / Kotlin Multiplatform / Fable / Nelua as the main approach | None can update Unity client logic without a release. Haxe 5 drops C#; KMP has no C# or Unity path beyond the C ABI; Fable has no JVM target; Nelua is alpha and native-only | **No** |

**Recommendation:**
1. Keep the interpreter as the single execution engine on every platform.
2. If the team wants types, add **Teal** as an optional authoring front end (or TSTL if TypeScript familiarity matters more than semantic fidelity).
3. Precompile to bytecode at publish time.
4. Revisit a compiled tier only with profiling evidence.
