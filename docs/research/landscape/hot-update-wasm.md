# Non-script routes: C# hot update on Unity, and WebAssembly as the portable logic format

Survey date: 2026-10-28. Scope: fit for the need below, not VM internals.

**The need, restated generically.** A backend sends business logic (rules, eligibility, offer/variant selection, config-driven behaviour) as trusted scripts. The same script must give identical results on Unity clients (2021.3 / 2022.3 / 6 LTS, Mono and IL2CPP, iOS/Android/desktop, WebGL a plus), in browsers (today .NET WebAssembly + TypeScript), on JVM servers (Java/Spring Boot), and on plain .NET. Priorities: exact 64-bit integers, JSON in/out, cross-platform determinism, small download, fast cold start, cheap host calls, good CLR interop on Unity, debuggability and typed tooling, only public Unity mechanisms (no libil2cpp patches), few unsafe-heavy dependencies. Current choice: a modernised MoonSharp fork.

Evidence marks: **[C]** confirmed by reading source or official docs; **[S]** secondary (blog, issue, forum, vendor marketing); **[U]** unconfirmed or my inference.

## Summary table

| Candidate | What it is | Unity Mono | Unity IL2CPP / iOS | Unity WebGL | Browser | JVM | Plain .NET | Same logic on all four? | Activity / licence (Oct 2026) | Fit |
|---|---|---|---|---|---|---|---|---|---|---|
| **HybridCLR** | CIL interpreter added to IL2CPP; hot-update DLLs run interpreted | Not needed (Mono loads DLLs itself) | Yes, but by **replacing libil2cpp** with a patched copy | Yes | Not Unity: .NET WASM can load the same DLL | **No** | Yes (load the DLL) | No: JVM gap | v9.0.3 (2026-10-13), very active; MIT core + paid editions | **No** (breaks the no-libil2cpp-patch rule; v9 drops 2021.3; no JVM) |
| **ILRuntime** | Pure-C# CIL interpreter | Yes | Yes (public mechanisms only) | Yes [U] | Via .NET WASM (load DLL) | **No** | Yes | No: JVM gap | Last tag v2.1.0 (2022-10-07); commits through 2026-07-24; MIT | **Partial** (C#-only world; unsafe-heavy; no releases) |
| **InjectFix** | IL-patch hotfix of existing methods | Yes | Yes | Yes [U] | No | No | No | No | Last code commit 2022-04-19; MIT | **No** |
| **Roslyn scripting** | Compile C# at runtime and load it | Desktop/Android Mono only [U] | **No** (needs Reflection.Emit / Assembly.Load) | No | Heavy (Roslyn in browser) | No | Yes | No | n/a | **No** |
| **WASM + WACS** (managed interpreter) | Pure-C# Wasm 3.0 interpreter | Yes | Yes (interpreter only) | Yes, or better: browser's own Wasm via jslib [U] | Native | Endive/Chicory | WACS or wasmtime-dotnet | **Yes, technically** | WACS 0.16.14 (2026-05-28), single maintainer; Apache-2.0 | **Partial** (weakest leg is Unity IL2CPP/iOS) |
| **WASM + native runtime in Unity** (wasmtime/WAMR/wasm3 via P/Invoke) | Native engine called from C# | Yes | Needs custom static iOS build; wasmtime iOS is Tier 3 | Use browser Wasm | Native | Endive / wasmtime4j | wasmtime-dotnet | Yes, with per-platform native builds | wasmtime v50, WAMR 2.4.5, wasm3 in minimal maintenance | **Partial** (native per-platform burden on mobile) |
| **Extism** | Plugin framework on top of Wasm runtimes | via .NET SDK (native lib) | **No host** (no iOS/Android natives) | n/a | JS SDK | Chicory SDK (experimental) | .NET SDK (native) | No on mobile | extism v1.41.0; dotnet-sdk last commit 2025-12-01 | **No** as-is (ABI ideas reusable) |

**One-line answer.** WebAssembly can run one compiled artifact bit-for-bit on browser, JVM and .NET today. This is proven in production by OpenFeature flag-evaluation providers. On the Unity IL2CPP/iOS leg it is only an interpreter running inside an AOT runtime, with no published performance data, weaker C# interop and weaker tooling than a managed Lua. So WASM is a realistic **complement** (best for the JVM leg and for heavy, stable, deterministic kernels), not a replacement for Lua as the authoring format for frequently changed business rules. The C# hot-update tools are Unity-only and do not reach the JVM. HybridCLR also fails the no-libil2cpp-patch rule.

---

## Part 1: C# hot update on Unity

### 1.1 HybridCLR (focus-creative-games / Code Philosophy)

**What it is.** HybridCLR adds an interpreter to IL2CPP. The README says it "extends the il2cpp runtime, transforming it from a pure AOT runtime into a hybrid AOT+Interpreter runtime, natively supporting dynamic assembly loading" [C] ([README_EN @6fa39d5](https://github.com/focus-creative-games/hybridclr/blob/6fa39d5a21c8e45bc779d415294d51b62532b62d/README_EN.md)). Hot-update code is normal C# compiled to a DLL that is downloaded and interpreted. It supports generics, reflection, MonoBehaviour, async and multithreading [C, same README].

**How it works with IL2CPP: it patches libil2cpp.** The install docs say the original libil2cpp "must be replaced with the modified libil2cpp to support hot updates" (需要替换成改造后的libil2cpp). The modified tree is `il2cpp_plus/libil2cpp` (a light modification of Unity's metadata module with inserted hooks) merged with the `hybridclr` interpreter core. A local install copies the Editor's il2cpp into `{project}/HybridCLRData/LocalIl2CppData-{platform}` and points Unity at it with the `UNITY_IL2CPP_PATH` environment variable. A global install overwrites the Editor's own `Data/il2cpp/libil2cpp` [C] ([install docs](https://www.hybridclr.cn/docs/basic/install); [il2cpp_plus README @479b2b9](https://github.com/focus-creative-games/il2cpp_plus/blob/479b2b9820a6d52a8a1401e816bdc117992ca11a/README.md): "我们轻微改造了metadata管理模块，插入了一些hook代码"). This directly violates our "no libil2cpp patches" rule. Pinning to a patched libil2cpp also couples us to HybridCLR's release cadence for every Unity patch upgrade [U]. il2cpp_plus is labelled MIT but carries modified Unity libil2cpp source. Redistribution rights for that code are a question for the owner, not something this survey can settle [U].

**Latest versions.**
- Unity package `com.code-philosophy.hybridclr` **9.0.3**. Tags: v9.0.0 on 2026-10-08, v9.0.1 on 10-09, v9.0.2 on 10-12, v9.0.3 on 10-13. Latest default-branch commit 2026-10-18 [C] (`git for-each-ref` on [hybridclr_unity](https://github.com/focus-creative-games/hybridclr_unity)).
- Core repo latest commit 2026-10-15 ("feat: support exported type") [C].
- The GitHub "Releases" page is empty; versions are git tags only [C].

**Unity version support, 2021 to 6000.** v9.0.0 removed Unity 2019 to 2021: commit [44057b0](https://github.com/focus-creative-games/hybridclr_unity/commit/44057b0107576efd3443fdb74bf5cb68eaddcb29) "refactor: remove support for unity 2019-2021" [C].
- v9.0.x pins 2022.3.x, 2022.3.x-tuanjie, 6000.0.x and 6000.3.x [C] ([hybridclr_version.json @e48a7be](https://github.com/focus-creative-games/hybridclr_unity/blob/e48a7beb956f79cd33e636e0efbb1914ff045437/Data~/hybridclr_version.json)).
- v8.15.0 (2026-09-29) still listed 2019, 2020, 2021, 2022, 2023, 6000, 6000.3.x and 6000.5.x [C] (same file at tag v8.15.0).
- So supporting Unity 2021.3 means staying on the frozen 8.x line [C]. The docs page agrees that v9 dropped 2019.4/2020.3/2021.3 [C] ([supported versions](https://www.hybridclr.cn/docs/basic/supportedplatformanduniyversion)).

**Platforms.** Listed as tested: iOS arm64, Android armv7/arm64, Windows, macOS, Linux, WebGL (including mini-game platforms), consoles and UWP [C] (same page).

**Editions and licence.**
- The community edition is MIT and free [C].
- Paid **Pro**, **Ultimate** and **Reload** editions add:
  - a faster interpreter (vendor says 280% to 735% of community on numeric code);
  - full generic sharing, which removes the need for "supplementary metadata" for AOT generics;
  - metadata/code encryption;
  - DHE (unchanged functions run AOT, Ultimate only);
  - assembly unloading (Reload).
- Paid editions are priced by email, sold per project, and include 2 years of updates and support [C] ([business intro](https://www.hybridclr.cn/docs/business/intro)).

**Performance evidence (vendor-published only).**
- A single device, a OnePlus 9R [S] ([performance page](https://www.hybridclr.cn/docs/basic/performance)).
- Commercial edition numeric instructions: 140% to 330% of Mono's, and 651% to 720% of xLua's.
- Native AOT is 4.1 to 90 times the community edition on numeric code, and 1.3 to 12.9 times the commercial edition.
- I found no independent benchmark.

**Interop with C#/Unity.** This is the best of any option: interpreted code is real CLR types, so there is no binding layer [C, README].

**64-bit integers and JSON.** Native `long` and any C# JSON library [C].

**Determinism.**
- Integer and `decimal` logic is exact [U].
- Floating-point results can differ between IL2CPP (C++ compiler), Mono and CoreCLR, especially in `Math.*` functions, which call the platform libm [U].

**Tooling.** Full C# IDE, types and tests. However, "C#-level debugging of interpreted code is not supported" (不支持对解释代码部分进行C#级别调试) [C] ([unsupported features](https://www.hybridclr.cn/docs/basic/notsupportedfeatures)). In the Editor the code runs on Mono, so normal debugging works there [U].

**Store policy.**
- The vendor claims "over a thousand" titles launched on the App Store and Google Play, and "nearly a hundred" in the iOS top-500 free chart [S] (README).
- Apple's position is the same as for Lua: see §3.

**Showstoppers for us.**
1. It patches libil2cpp.
2. v9 drops 2021.3.
3. It is Unity-only and does nothing for JVM servers.
4. The interpreted C# would still need a separate JVM implementation of every rule.

**Verdict: no** for this need. It is excellent for "hot-update the game's own C# gameplay code", which is a different problem.

**Related: LeanCLR.** Same vendor. A "production-oriented CLR implementation" in C++11 that runs AOT plus an interpreter with no JIT. It claims a core under 600 KB on x64/WebAssembly (about 300 KB trimmed), targets Windows/Linux/macOS/Android/iOS/HarmonyOS/WebAssembly, is MIT-licensed, and has no tags yet. Latest commit 2026-09-18 [C] ([README_EN @6feb073](https://github.com/focus-creative-games/leanclr/blob/6feb07303397111b41bce52b060c9c409373f81f/README_EN.md)). In theory a LeanCLR build compiled to `.wasm` could run the same C# DLL on the JVM through Endive. Nobody has published this, and it would be an interpreter inside a Wasm interpreter [U]. Too immature to plan on.

### 1.2 ILRuntime (Ourpalm)

**What it is.** A pure-C# CIL interpreter "to run c# code on environments without JIT. For example on iOS" [C] ([ReadMe-EN @482cb88](https://github.com/Ourpalm/ILRuntime/blob/482cb88c114d493f7acacf07e06f6b9e020b43f0/ReadMe-EN.md)). Because it is a library and not a runtime patch, it uses only public Unity mechanisms [C].

**State.**
- Last tag **v2.1.0 (2022-10-07)**.
- 23 commits since 2025-10, the latest 2026-07-24 ("Fixed a bug with async await"), with fixes such as "ceq cgt for NaN values" (2026-06-30) and "beq and bne" (2025-11-26) [C] (`git log` on [Ourpalm/ILRuntime](https://github.com/Ourpalm/ILRuntime)).
- Licence MIT [C] (License.md).
- So it is maintained but unreleased [C]. Bugs in basic branch and compare opcodes were still being fixed in 2025 and 2026 [C].

**Interop.**
- Cross-domain inheritance needs adapters.
- Delegates must be registered (`RegisterMethodDelegate<...>`), and CLR redirection/binding code is generated for performance [C] (README).
- This is clearly heavier than HybridCLR and comparable to Lua bindings [U].

**Unsafe.** "ILRuntime uses unsafe code, so you must enable unsafe mode" [C] (README). This goes against our "no unsafe-heavy dependencies" preference.

**Reach.**
- Unity Mono/IL2CPP and plain .NET [C].
- Browser: the same DLL could simply be loaded by the .NET WebAssembly runtime, so ILRuntime is not needed there [U].
- JVM: no route.

**Debugger.** The repo ships a VS/VS Code debugger extension (`Debugging/`) [C].

**Verdict: partial.** It only makes sense if we abandoned the JVM requirement or reimplemented rules in Java. Lack of releases and the unsafe dependency count against it.

### 1.3 InjectFix (Tencent)

An IL-injection hotfix: it patches bodies of existing methods ("Modify C# directly on the Unity project to bug fixes") [C] ([README_en](https://github.com/Tencent/InjectFix)).
- The last code commit is **2022-04-19**. The only later commit (2025-07-07) changes the LICENSE company name [C] (`git log`).
- It cannot ship new rule logic as data and does not reach the JVM.

**Verdict: no.**

### 1.4 Roslyn scripting

`Microsoft.CodeAnalysis.CSharp.Scripting` compiles C# at runtime and must load the emitted assembly. Unity's docs: "An AOT platform can't implement any of the methods in the System.Reflection.Emit namespace" [C] ([scripting restrictions, 6000.0](https://docs.unity3d.com/6000.0/Documentation/Manual/scripting-restrictions.html)). IL2CPP also cannot load new IL assemblies. That limitation is the reason HybridCLR and ILRuntime exist [S].
- Roslyn is also many megabytes [U].
- It works only on JIT hosts (Editor, desktop/Android Mono, server .NET).
- The useful variant is "Roslyn on the backend to compile, then HybridCLR or ILRuntime on the client", which falls back to 1.1 or 1.2.

**Verdict: no** on IL2CPP.

### 1.5 Can the same C# logic also run in the browser and on the JVM?

| Target | Route | State | Notes |
|---|---|---|---|
| Browser | .NET WebAssembly runtime (what we already ship) | Production | A downloaded rule DLL can be loaded into the browser .NET runtime, which runs IL through the Mono interpreter [U]. Payload is the .NET runtime (multi-MB) [U]. |
| JVM | NativeAOT shared library plus JNI or Java FFM | Production tech, awkward fit | NativeAOT exports `[UnmanagedCallersOnly(EntryPoint=...)]` methods from a self-contained shared library. Only shared libraries are supported, and "Unloading Native AOT libraries ... is not supported" [C] ([MS docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries)). The library is AOT, so it **cannot load new rule DLLs at runtime**. Hot-updated rules would need an IL interpreter (ILRuntime-like) inside the library [U]. Per-OS/arch native binaries. |
| JVM | .NET worker process beside the JVM | Works | Real CoreCLR loads rule DLLs with full JIT. Costs: IPC per call and process management [U]. (This is the pattern our MoonSharp fork already uses for its Java worker.) |
| JVM | C# compiled to Wasm (componentize-dotnet / NativeAOT-LLVM, or the `wasi-experimental` workload), run on Endive | Preview | componentize-dotnet: latest tag **v0.8.0-preview00011 (2026-06-12)**. README: "All the underlying technologies are under heavy development and are missing features"; needs .NET 10 SDK; LLVM package steps documented for Linux/Windows x64 only [C] ([README @f6bfc5a](https://github.com/bytecodealliance/componentize-dotnet/blob/f6bfc5a9a5a25728aeec959be60c8f737278a183/README.md)). Extism's .NET PDK still requires `dotnet workload install wasi-experimental` on .NET 8 and calls itself "an experimental PDK"; last commit 2025-04-01 [C] ([dotnet-pdk README](https://github.com/extism/dotnet-pdk/blob/5bd5ce3fa564bd66428348d684fce199837d8eb5/README.md)). |
| JVM | CIL compiled to JVM bytecode | None maintained | Only academic or abandoned translators found (il2j, a 2004 MSIL-to-bytecode paper, Mainsoft's old commercial product). IKVM works in the opposite direction [S] (search, 2026-10). |

**Conclusion for Part 1.** No C# hot-update route gives "one artifact, four runtimes, all hot-updatable". The JVM leg always needs a native library, a sidecar process, or a preview C#-to-Wasm toolchain.

---

## Part 2: WebAssembly as the portable logic format

### 2.1 Two different shapes (this matters more than the runtime choice)

1. **Rules compiled to Wasm.** Each rule set is authored in Rust, AssemblyScript, TinyGo, C or C#, compiled to a `.wasm` file, and shipped. Every module carries its own allocator, JSON parser and libm. There is a compile step, no `eval`, and authors write a systems or typed language, not a script [U].
2. **An engine compiled to Wasm, with rules sent as data.** A rule interpreter (a JSON rule DSL, a CEL/Rego evaluator, or even a Lua VM) is compiled to Wasm once and shipped once. Rules stay data and keep "script" ergonomics. This is what the production precedents do:
   - **GO Feature Flag.** The Go evaluation core is compiled with **TinyGo** (`-opt=s --no-debug -scheduler=none`) to one module "embedded by the OpenFeature in-process providers (Python, Java, .NET, JavaScript)". The ABI is `malloc`/`free`/`evaluate(ptr,len)->i64` (packed ptr<<32|len) with **JSON in and JSON out** [C] ([cmd/wasm/README @6e87bcd](https://github.com/thomaspoignant/go-feature-flag/blob/6e87bcd4b7cbc02ef9a6d681ae3172ff58c3a270/cmd/wasm/README.md); [Makefile](https://github.com/thomaspoignant/go-feature-flag/blob/6e87bcd4b7cbc02ef9a6d681ae3172ff58c3a270/Makefile)).
     - Module size about **758 to 781 KB** uncompressed (v0.2.1 to v0.2.4) [C] ([wasm-releases @8566264](https://github.com/go-feature-flag/wasm-releases/tree/8566264bc7ddf4018e8611d80d885e340a49201a/evaluation)).
     - The .NET provider uses **Wasmtime ≥ 34.0.2** [C] ([NuGet 1.2.0, 2026-07-02](https://www.nuget.org/packages/OpenFeature.Providers.GOFeatureFlag)).
     - The Java provider moved from the Chicory interpreter to the build-time Chicory compiler (PR merged 2025-11-24, no numbers published) [C] ([PR #1628](https://github.com/open-feature/java-sdk-contrib/pull/1628)).
     - Lesson learned the hard way: the 64 KB default Wasm shadow stack overflowed on realistic rule sets (about 30 nested parentheses, or a 154-item `in` list) and "permanently poison[ed] the instance". They raised it to 1 MB and added a CI check [C] (same README, issue [#5651](https://github.com/thomaspoignant/go-feature-flag/issues/5651)).
   - **Spotify Confidence resolver.** A Rust flag resolver compiled to Wasm, with OpenFeature providers for Go, Java, JS/TS, Python, Rust and Ruby. Status "Beta"; benchmarks are "work-in-progress" [S] ([repo](https://github.com/spotify/confidence-resolver)).
   - **Shopify Functions.** Business logic (discounts, delivery and payment customisation) runs as Wasm written in Rust or JS (via Javy). Instruction counts are deterministic ("The same WebAssembly module and the same input produce the same count"). Limit: 11 M instructions for carts up to 200 lines. "Your compiled binary must stay under 256 kB." "A JavaScript function executes significantly more instructions than the equivalent Rust function" [C] ([docs](https://shopify.dev/docs/apps/build/functions/optimize-instruction-counts)).
   - **OPA (Rego compiled to Wasm)** and others appear in Endive's adopter list (OPA Java Wasm SDK, OpenFeature GO Feature Flag provider, Spotify Confidence resolver) [S] ([ADOPTERS.md @bc68502](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/ADOPTERS.md)).

   None of these precedents has a Unity IL2CPP host [U: none found].

### 2.2 Guest languages (what authors would write)

| Language | Toolchain (latest seen) | Fit notes |
|---|---|---|
| Rust | `wasm32-unknown-unknown` / `wasm32-wasip1` | Smallest and fastest modules; serde for JSON; exact i64/u64. Not a script language for rule authors [U]. |
| AssemblyScript | v0.28.20, last commit 2026-07-21 [C] (tags on [repo](https://github.com/AssemblyScript/assemblyscript)) | TypeScript-like syntax with real `i64`/`u64`; small output. Pre-1.0. JSON needs a third-party library [U]. |
| TinyGo | v0.44.0, commit 2026-10-28 [C] | Proven for rule engines (GO Feature Flag), but about 760 KB modules and stack tuning needed [C, see 2.1]. |
| C/C++ | wasi-sdk / emscripten | Can compile **reference Lua** to Wasm: wasmoon (Lua 5.4) is 393 kB raw / 130 kB gzipped, and on a 2k-element heapsort ×10 runs in 15.3 ms vs 389.9 ms for fengari (pure JS) [S] ([wasmoon README](https://github.com/ceifa/wasmoon)). |
| JavaScript/TypeScript | Javy v9.1.0, commit 2026-10-23 [C] ([bytecodealliance/javy](https://github.com/bytecodealliance/javy)) | Embeds QuickJS, so it costs many more instructions than Rust (Shopify, above). JS numbers lose exact i64 unless BigInt is used throughout [U]. |
| C# | componentize-dotnet (preview), Extism .NET PDK (experimental) | See §1.5. Multi-MB output expected [U], toolchain preview-grade. |

### 2.3 Hosts, per target

#### Unity (Mono, IL2CPP, iOS, Android, desktop)

**WACS** (kelnishi/WACS).
- "A pure C# WebAssembly Interpreter for running WASM modules in .NET environments, including Godot and AOT environments like Unity's IL2CPP". Claims "Compatible with Unity 2021.3+ including AOT/IL2CPP modes for iOS", Wasm 3.0 spec-suite compliance, .NET Standard 2.1, "No `unsafe` keyword blocks" [C] ([README @cad6530](https://github.com/kelnishi/WACS/blob/cad6530f5d5765f5de79ca77ed166b99802372ea/README.md)).
- The fastest switch dispatcher uses `System.Runtime.CompilerServices.Unsafe` intrinsics. The default "polymorphic" path uses none [C].
- Dependencies: FluentValidation and Microsoft.Extensions.ObjectPool [C].
- Latest NuGet **0.16.14 (2026-05-28)**, package 762 KB, 14.3 K total downloads [C] ([NuGet](https://www.nuget.org/packages/WACS)). Latest commit 2026-05-28 [C]. Apache-2.0 [C]. One maintainer [S].

WACS performance, as published:
- **Conditions:** CoreMark on an M3 Max, **.NET 8 CoreCLR with JIT, not IL2CPP**.
- **Interpreter:** 274 to 385 iterations/s, which is **0.79% to 1.12% of native** C. The authors say it is slower than wasm3 ("typically 4–6 k iter/s on M-series") and "Think 'Python-for-WASM' speed".
- **Transpiler (Wasm compiled to IL via Reflection.Emit):** 17,552 iterations/s (50.9% of native). This path is **not available on IL2CPP**: "That's incompatible with … Unity IL2CPP … On those platforms use one of the three interpreter modes". A `.dll` precompiled on a JIT host can be shipped, but on IL2CPP it is AOT-compiled into the player build, so it cannot be a downloaded rule [C for the quote, U for the consequence].
- **Cold start:** 35 to 55 ms on default CoreCLR publish; 0.3 to 1.0 ms with NativeAOT for the interpreter. Both for a 242-byte module [C].
- **No IL2CPP or device numbers exist** [C: none in the repo docs].

**Native runtimes via P/Invoke** (`__Internal` on iOS).
- **wasmtime-dotnet:** NuGet packages ship natives only for linux-x64/arm64, osx-x64/arm64 and win-x64/arm64 [C] ([Wasmtime.csproj @cb74b21](https://github.com/bytecodealliance/wasmtime-dotnet/blob/cb74b21210a49bfd822113e4370154ae99413f76/src/Wasmtime.csproj)). Latest commit 2026-10-25 [C].
- **Wasmtime on mobile:** `aarch64-apple-ios` and `aarch64-linux-android` are **Tier 3**. The Pulley interpreter, which you would need on iOS without JIT, is Tier 2 [C] ([stability tiers](https://docs.wasmtime.dev/stability-tiers.html)). By default Pulley is used only where Cranelift is unsupported. Elsewhere it needs the `pulley` crate feature [C] ([Pulley docs](https://docs.wasmtime.dev/examples-pulley.html)).
- **WAMR:** v2.4.5, commit 2026-10-28; has an interpreter and an AOT mode [C for activity, U for iOS specifics].
- **wasm3:** "**will enter a minimal maintenance phase**" (README); last commit 2026-09-29 [C] ([README](https://github.com/wasm3/wasm3/blob/28ecb9af6d2040e474a70f7cb7f43666740141fb/README.md)).
- Either way we would own iOS/Android static builds and IL2CPP-compatible callbacks (`MonoPInvokeCallback`, static methods) [U].

**Unity WebGL.** The browser already has a JIT-backed Wasm engine. A `.jslib` bridge to `WebAssembly.instantiate` would beat any interpreter compiled into the IL2CPP output [U].

#### Browser

Native `WebAssembly`. Wasm `i64` crosses into JS as `BigInt` (JS-BigInt integration is standardised) [C] (listed in WACS's feature table, which mirrors [webassembly.org/features](https://webassembly.org/features/)). JSON output must not be parsed with plain `JSON.parse` if it carries integers above 2^53. The same pitfall exists today for Lua [U].

#### JVM

- **Endive** (formerly Chicory). "A JVM native WebAssembly runtime … zero native dependencies or JNI". "Endive is a fork of Chicory by Dylibso, Inc." and a Bytecode Alliance project [C] ([README @bc68502](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/README.md)).
  - Maven `run.endive:runtime` **1.1.0** (Maven metadata lastUpdated 2026-09-03); latest commit 2026-10-28; Apache-2.0 [C].
  - Chicory itself stopped at tag 1.7.5, last commit 2026-05-27 [C]. **New work should target Endive.**
  - Engines:
    - Interpreter.
    - Runtime compiler that translates Wasm to JVM bytecode in memory. It passes the same spec tests; you pay a "small performance penalty at Instance initialization" [C] ([runtime-compiler doc](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/docs/docs/execution/runtime-compiler.md)).
    - Build-time compiler.
    - Experimental `redline`: Cranelift-compiled native code via Panama FFM (Java 25+) or jffi. On real modules it reports ~10× (Prism parser), ~4× (SQLite) and ~6.5× (toml2json) **vs Chicory's bytecode AOT**, and 1.5× to 22× on shootout benchmarks [S] ([redline post](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/docs/blog/2026-04-16-chicory-redline.md); [1.1.0 post](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/docs/blog/2026-09-03-endive-1.1.0.md)).
  - The runtime compiler is the right fit for downloaded rules on a server [U].
- **GraalWasm.** `org.graalvm.polyglot:wasm`; versions 25.1.3 (2026-07-01) and 25.0.3 (2026-04-21) on Maven; vendor claims "Fastest Wasm on the JVM" with the Graal JIT [S] ([mvnrepository](https://mvnrepository.com/artifact/org.graalvm.wasm/wasm-language), [Jfokus 2026 slides](https://www.jfokus.se/jfokus26-preso/Graalvm-Wasm-Fast-Efficient-Portable-Apps.pdf)).
- **Wasmtime JNI bindings** (kawamuray wasmtime-java, tegmentum wasmtime4j 46.0.1-1.2.0). Not officially supported by Wasmtime; native per platform [S].

#### Plain .NET

wasmtime-dotnet (production; used by the GO Feature Flag provider), or WACS (pure managed, with the JIT transpiler available) [C].

### 2.4 Extism

A plugin framework over Wasm runtimes: PDKs for Rust, JS, Go, AssemblyScript, .NET, C and others, plus host SDKs [C] ([README @051b765](https://github.com/extism/extism/blob/051b765bb6abe4fe95d5e4f489f03ad7b137edbc/README.md)).
- Native releases cover only desktop and server targets (darwin, linux, windows). "For Android we suggest … the Chicory SDK" [C].
- The .NET SDK needs the native `Extism.runtime.*` package; last commit 2025-12-01 [C] ([dotnet-sdk](https://github.com/extism/dotnet-sdk)).
- The Chicory SDK says "This is an experimental Java SDK … still very experimental"; last commit 2025-10-31 [C] ([chicory-sdk](https://github.com/extism/chicory-sdk)).
- Core v1.41.0, last commit 2026-10-09 (Wasmtime 49 upgrade) [C].
- **There is no IL2CPP/iOS host.** Extism's value for us is its conventions (bytes in and out, host functions, fuel limits), which a thin custom ABI like GO Feature Flag's gives with less machinery [U].

### 2.5 Cross-cutting questions

| Question | Answer |
|---|---|
| iOS (no JIT) | Allowed only as an interpreter: WACS, wasm3, WAMR interpreter, Wasmtime Pulley. Wasmtime iOS is Tier 3 [C]. On IL2CPP with WACS, a downloaded module is always interpreted [C/U, see 2.3]. |
| Size | Runtime: WACS nupkg 762 KB [C]. Engine-as-Wasm: about 760 KB for a TinyGo rule engine [C]; Lua 5.4 in Wasm 393 kB raw / 130 kB gzipped [S]. Rule-as-Wasm: Shopify enforces < 256 kB [C]. Rust rule modules can be far smaller [U]. Compare: a Lua script is a few KB of text. |
| Interop cost | Everything crosses linear memory as bytes: copy the JSON in, call, copy the JSON out (GFF ABI) [C]. Host functions take only numbers and references, so there is **no direct access to CLR/Unity objects** and every rich call needs serialisation [U]. This is much weaker than MoonSharp/xLua userdata binding. |
| 64-bit integers | Native `i64` in Wasm; exact on WACS (`long`), Endive (`long`) and browser (`BigInt`) [C/U]. |
| JSON | Must live inside the guest (serde_json, Go `encoding/json`, an AssemblyScript library) or be passed as pre-parsed numbers [U]. |
| Determinism | Integer ops are fully deterministic. Floating-point arithmetic is IEEE-754 except for NaN bit patterns and relaxed SIMD. Wasm 3.0 defines a **deterministic profile** that fixes NaN generation and relaxed-SIMD results [S] ([Wasm 3.0 news](https://webassembly.org/news/2025-09-17-wasm-3.0)). Wasmtime can canonicalise NaNs at a cost [C] ([deterministic execution](https://docs.wasmtime.dev/examples-deterministic-wasm-execution.html)). Big advantage: **libm (sin, pow, …) is compiled into the module**, so transcendental results are the same bits on every host. That is stronger than any C#/Java/JS-native implementation, where each platform's Math library differs [U]. Avoid relaxed SIMD and threads. |
| Cold start | Browser: fast native compile [U]. Endive interpreter: none; runtime compiler pays at instantiation [C]. WACS: parse, validate and link on IL2CPP not measured anywhere [C: none published]. |
| Tooling | Authoring tools are excellent for Rust, AssemblyScript and TypeScript (types, LSP). Source-level debugging works in browser DevTools via DWARF [U]. **No source debugger** for WACS or Endive [U]. Every rule change needs a compile pipeline and per-language test harnesses. |
| Sandboxing | Comes for free (not a goal for us, but harmless). |
| Maturity | Browser and Wasmtime: very high. Endive: high for the JVM (wide adopter list) [S]. WACS: young, single maintainer [S]. |

---

## 3. Store policy for downloaded logic (applies to Lua, HybridCLR and Wasm alike)

- **Apple App Review Guideline 2.5.2** (current text): apps may not "download, install, or execute code which introduces or changes features or functionality of the app" [C] ([guidelines](https://developer.apple.com/app-store/review/guidelines/)).
- **Apple Developer Program License Agreement §3.3.2** allows downloaded interpreted code only if it does not change the app's primary purpose, does not create a store for other code, and does not bypass OS security [S] (quoted in [Expo forum](https://forums.expo.dev/t/expo-updates-and-apples-app-store-review-guidelines/64495) from the 2022 DPLA; Apple's PDF not fetched).
- **Google Play** forbids downloading "executable code (such as dex, JAR, .so files)" but "This restriction does not apply to code that runs in a virtual machine or an interpreter where either provides indirect access to Android APIs" [C] ([Device and Network Abuse](https://support.google.com/googleplay/android-developer/answer/9888379)).
- A Wasm module run by WACS, a CIL DLL run by HybridCLR, and a Lua script are all "interpreted code" in this sense [U]. Business rules (eligibility, offer selection) that stay within the advertised app purpose are the same risk class for all three. None of the options changes the policy position.

## 4. Verdict for our need

| Option | Verdict | Why |
|---|---|---|
| HybridCLR | **No** | Patches libil2cpp (fails a hard rule). v9 (2026-10-08) drops Unity 2021.3. Unity-only, so the JVM still needs a second implementation. No on-device debugging of interpreted code. |
| ILRuntime | **Partial (not recommended)** | Public mechanisms only and pure C#, but no JVM route, unsafe-heavy, no release since 2022, and opcode-level bugs still being fixed in 2025-26. |
| InjectFix | **No** | Hotfix-only, inactive since 2022. |
| Roslyn scripting | **No** | Cannot run on IL2CPP. |
| C# everywhere (any route) | **No** for the JVM leg | Only preview C#-to-Wasm, an AOT native library that cannot load new rules, or a sidecar process. |
| **WebAssembly as the single format** | **Partial: a complement, not a replacement** | **For:** one artifact runs bit-identically on browser, JVM (Endive) and .NET (Wasmtime/WACS), with production precedent (OpenFeature providers in .NET, Java, JS and Python sharing one module). Exact i64; libm baked in gives better float determinism. **Against:** on the client that matters most (Unity IL2CPP/iOS), the only no-native option is a young single-maintainer interpreter with no published IL2CPP numbers. Its own CoreCLR numbers are about 1% of native C, and under IL2CPP it cannot use its fast transpiler for downloaded modules. Interop is bytes-in/bytes-out with no CLR object access. There is no debugger in Unity or the JVM. Authoring needs a compiler toolchain. Modules are 100s of KB vs KB-sized scripts. |

**Where WASM is genuinely useful next to Lua (ranked).**
1. **JVM leg without .NET.** Compile the chosen script engine's reference semantics into Wasm and run it on Endive (runtime compiler). That gives a pure-Java server evaluator with identical results. Two candidate engines:
   - A C Lua VM in the way wasmoon does it [S]: semantics match only if our clients run that same reference Lua, which our managed Lua port may not match exactly [U].
   - Our C# engine via componentize-dotnet, once that leaves preview [U].
   This needs a spike: measure Endive throughput and instantiation cost against the current Java-to-.NET worker [U].
2. **Stable, compute-heavy, determinism-critical kernels** (pricing maths, hashing, bucketing) that change rarely. Write them once in Rust, ship them as Wasm, and call them from Lua hosts on every platform. In the browser and on the JVM this is fast. On Unity, WACS is acceptable only for cold-path calls [U].
3. **Unity WebGL.** Route Wasm modules to the browser's own engine through `.jslib` rather than interpreting inside IL2CPP output [U].

**Not recommended.** Replacing Lua scripts with per-rule Wasm modules as the main business-rule format. The IL2CPP/iOS leg, interop, tooling, size and authoring ergonomics are all worse than a managed Lua for frequently changed, CLR-integrated rules.

**Open questions worth a spike (none have public answers).**
- WACS throughput and cold start under IL2CPP on an iOS device, against MoonSharp on the same rule.
- Endive runtime-compiler instantiation time for a 400 to 800 KB engine module.
- Whether WACS's FluentValidation dependency survives IL2CPP managed-code stripping without `link.xml` work [U].

## Sources (main)

- HybridCLR: [README_EN @6fa39d5](https://github.com/focus-creative-games/hybridclr/blob/6fa39d5a21c8e45bc779d415294d51b62532b62d/README_EN.md), [hybridclr_version.json @e48a7be](https://github.com/focus-creative-games/hybridclr_unity/blob/e48a7beb956f79cd33e636e0efbb1914ff045437/Data~/hybridclr_version.json), [commit 44057b0](https://github.com/focus-creative-games/hybridclr_unity/commit/44057b0107576efd3443fdb74bf5cb68eaddcb29), [il2cpp_plus README](https://github.com/focus-creative-games/il2cpp_plus/blob/479b2b9820a6d52a8a1401e816bdc117992ca11a/README.md), [install](https://www.hybridclr.cn/docs/basic/install), [versions](https://www.hybridclr.cn/docs/basic/supportedplatformanduniyversion), [performance](https://www.hybridclr.cn/docs/basic/performance), [unsupported](https://www.hybridclr.cn/docs/basic/notsupportedfeatures), [editions](https://www.hybridclr.cn/docs/business/intro)
- LeanCLR: [README_EN @6feb073](https://github.com/focus-creative-games/leanclr/blob/6feb07303397111b41bce52b060c9c409373f81f/README_EN.md)
- ILRuntime: [ReadMe-EN @482cb88](https://github.com/Ourpalm/ILRuntime/blob/482cb88c114d493f7acacf07e06f6b9e020b43f0/ReadMe-EN.md)
- InjectFix: [repo](https://github.com/Tencent/InjectFix), [last commit 5ba42eb](https://github.com/Tencent/InjectFix/commit/5ba42eb8f63fe37134a3c78690f97d8425e13588)
- Unity: [scripting restrictions](https://docs.unity3d.com/6000.0/Documentation/Manual/scripting-restrictions.html)
- .NET: [Native AOT libraries](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries), [componentize-dotnet @f6bfc5a](https://github.com/bytecodealliance/componentize-dotnet/blob/f6bfc5a9a5a25728aeec959be60c8f737278a183/README.md)
- WACS: [README @cad6530](https://github.com/kelnishi/WACS/blob/cad6530f5d5765f5de79ca77ed166b99802372ea/README.md), [NuGet](https://www.nuget.org/packages/WACS)
- Wasmtime: [wasmtime-dotnet csproj @cb74b21](https://github.com/bytecodealliance/wasmtime-dotnet/blob/cb74b21210a49bfd822113e4370154ae99413f76/src/Wasmtime.csproj), [stability tiers](https://docs.wasmtime.dev/stability-tiers.html), [Pulley](https://docs.wasmtime.dev/examples-pulley.html), [deterministic execution](https://docs.wasmtime.dev/examples-deterministic-wasm-execution.html)
- wasm3: [README @28ecb9a](https://github.com/wasm3/wasm3/blob/28ecb9af6d2040e474a70f7cb7f43666740141fb/README.md)
- Endive/Chicory: [Endive README @bc68502](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/README.md), [ADOPTERS](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/ADOPTERS.md), [runtime compiler](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/docs/docs/execution/runtime-compiler.md), [redline post](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/docs/blog/2026-04-16-chicory-redline.md), [Endive 1.1.0 post](https://github.com/bytecodealliance/endive/blob/bc68502fa57482e6bd66a78224ddbf42f6155be9/docs/blog/2026-09-03-endive-1.1.0.md), [Chicory last commit](https://github.com/dylibso/chicory/commit/e2e2e4058f49fbffeffc5ea92c54b41534cb45d3)
- GraalWasm: [Maven](https://mvnrepository.com/artifact/org.graalvm.wasm/wasm-language), [Jfokus 2026](https://www.jfokus.se/jfokus26-preso/Graalvm-Wasm-Fast-Efficient-Portable-Apps.pdf)
- Extism: [README @051b765](https://github.com/extism/extism/blob/051b765bb6abe4fe95d5e4f489f03ad7b137edbc/README.md), [dotnet-sdk](https://github.com/extism/dotnet-sdk), [chicory-sdk](https://github.com/extism/chicory-sdk), [dotnet-pdk](https://github.com/extism/dotnet-pdk/blob/5bd5ce3fa564bd66428348d684fce199837d8eb5/README.md)
- Precedents: [GO Feature Flag wasm README @6e87bcd](https://github.com/thomaspoignant/go-feature-flag/blob/6e87bcd4b7cbc02ef9a6d681ae3172ff58c3a270/cmd/wasm/README.md), [wasm-releases @8566264](https://github.com/go-feature-flag/wasm-releases/tree/8566264bc7ddf4018e8611d80d885e340a49201a/evaluation), [.NET provider](https://www.nuget.org/packages/OpenFeature.Providers.GOFeatureFlag), [Java PR #1628](https://github.com/open-feature/java-sdk-contrib/pull/1628), [Confidence resolver](https://github.com/spotify/confidence-resolver), [Shopify instruction counts](https://shopify.dev/docs/apps/build/functions/optimize-instruction-counts), [wasmoon](https://github.com/ceifa/wasmoon)
- Wasm spec: [Wasm 3.0 announcement](https://webassembly.org/news/2025-09-17-wasm-3.0)
- Store policy: [Apple guidelines](https://developer.apple.com/app-store/review/guidelines/), [Google Play](https://support.google.com/googleplay/android-developer/answer/9888379), [DPLA 3.3.2 quote](https://forums.expo.dev/t/expo-updates-and-apples-app-store-review-guidelines/64495)
