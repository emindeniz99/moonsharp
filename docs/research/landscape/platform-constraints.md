# Platform constraints for downloaded business-logic scripts (October 2026)

Research date: 2026-10-28. Scope: the hard rules that decide between all
candidate engines for "backend sends trusted scripts; the same script runs with
identical results on Unity (2021.3 / 2022.3 / 6 LTS, Mono and IL2CPP,
iOS/Android/desktop, WebGL), browsers, JVM servers and plain .NET".

Evidence marks: **[C]** confirmed by reading the source, the document or the
binary text myself; **[S]** secondary (blog, forum, issue, search summary);
**[U]** unconfirmed or my inference. In-house measurements come from the
MoonSharp fork at `emindeniz99/moonsharp` commit
[`0c0e5d4`](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/docs/hosting.md)
(2026-10-28) and are marked [C] because the numbers are in the repo with the
command that produced them.

## Summary table

| # | Constraint (state in Oct 2026) | Rules out | Leaves open | Evidence |
|---|---|---|---|---|
| 1 | Apple DPLA 3.3.1(B) (updated 2026-10-13): no downloaded *executable* code; *interpreted* code may be downloaded if it does not change the app's primary purpose, does not bypass signing/sandbox, and does not create a storefront. Guideline 2.5.2 (2026-10-16) and 2.5.19 (no hidden or dormant features). | Downloading compiled code of any kind: native libraries, .NET assemblies, AOT-compiled Wasm, Pulley/R2R images built as native code. Scripts that switch on features App Review never saw. | Downloaded Lua / JS / Wasm bytecode run by an interpreter compiled into the app, used for the reviewed purpose (rules, eligibility, offers, config). | [C] |
| 2 | Google Play Device and Network Abuse: no executable code (dex, JAR, .so) from outside Play; exception for code in a VM or interpreter with only indirect access to Android APIs; runtime-loaded Lua/JS/Python must not enable policy violations. | Same class as row 1, plus self-updating outside Play. | Same as row 1. | [C] |
| 3 | iOS: no writable+executable memory for third-party apps. JIT exists only for alternative browser engines (BrowserEngineKit, EU since iOS 17.4, Japan since iOS 26.2), and only for dedicated browser apps or in-app-browser apps. | Any engine whose speed depends on a JIT on iOS: LuaJIT's trace JIT, V8 TurboFan/Sparkplug/Maglev, Wasmtime Cranelift/Winch, Mono JIT, `System.Reflection.Emit`, compiled expression trees. | Interpreters: LuaJIT interpreter (`LJ_OS_NOJIT`), V8 `--jitless`, QuickJS, Wasm interpreters (Pulley, wasm3, wasmi, WAMR interp), and IL2CPP/AOT-compiled managed interpreters such as MoonSharp. | [C] for policy and source flags, [S] for JIT side-loading folklore |
| 4 | Unity IL2CPP: AOT only, no `Reflection.Emit`, no `dynamic`, missing generic instantiations fail at run time (2021.3: `ExecutionEngineException`; 2022.1+: slow fully-shared fallback), managed stripping on by default (Minimal), `Disabled` not available, `link.xml` ignored inside UPM packages. | Engines that generate IL or rely on `MakeGenericType`/`MakeGenericMethod` over value types without pre-instantiation; reflection-only bindings without preservation annotations; .NET hot-update via IL interpretation that needs libil2cpp patches (excluded by our own rule). | Pure C# interpreters with pre-generated (source-generated or hand-written) bindings and `[Preserve]` annotations. | [C] |
| 5 | Unity C# toolchain: C# 9 (some features unsupported), API profiles .NET Standard 2.1 (default) or .NET Framework 4.8; Roslyn source generators must reference Microsoft.CodeAnalysis **3.8** on 2021.3/2022.3 and **4.3** on 6000.0/6000.3; Unity 7.0 alpha (Oct 2026) still targets C# 9 / .NET Standard 2.1. | Library code needing C# 10+ or .NET 5+ APIs inside the Unity build; incremental-generator-only designs on 2021.3/2022.3. | `netstandard2.0`/`2.1` libraries in C# 9; source generators against Roslyn 3.8 (`ISourceGenerator`) with a 4.3 path. | [C] |
| 6 | Unity WebGL/Web: no managed threads (only native C/C++ and, from 6.4, Burst jobs); default exception mode catches only explicitly thrown exceptions. | Engines that need threads, timers on threads, or catch `NullReferenceException`/`IndexOutOfRangeException` as control flow. | Single-threaded interpreters that throw their own exceptions explicitly. | [C] |
| 7 | Unity runtime roadmap: 6.7 LTS is the last Mono-based release; Unity 7.0 (alpha since 2026-10-05) is CoreCLR in the editor and desktop player; IL2CPP stays for mobile/console/web and moves to a .NET 10 BCL; C# 14/.NET 10 has no public date. 6.6 makes "no code reload on Play" the default for new projects. | Designs that depend on domain reload to clear static state, or on Mono-specific behaviour. | Code that keeps per-`Script` state and survives Assembly Load Context reloads. | [C] Unity forum posts, [S] for the Unity 7 GA window |
| 8 | .NET in the browser: .NET 10 (LTS) and .NET 11 use Mono-on-Wasm by default; CoreCLR-on-Wasm is opt-in in .NET 11 and expected to become the path in .NET 12; single-threaded in practice; no official 2026 size number found (secondary: 2–4 MB compressed for a trimmed app). | Expecting threads or a JIT in the browser build; reflection-heavy code without trimming roots. | The same managed engine in the browser, accepting a multi-MB first download (cacheable) and interpreter-speed .NET. | [C] .NET 11 docs, [S] sizes |
| 9 | JVM: FFM final in JDK 22; JNI/FFM native loading warns since JDK 24 (JEP 472), exceptions planned later; Spring Boot 4 baseline is Java 17; Oracle dropped Native Image and Graal JIT from Java SE products after JDK 24. In-process .NET: `hostfxr` loads one runtime per process; NativeAOT shared libraries cannot be unloaded; .NET installs its own signal handlers, and a CLR stack overflow kills the JVM. | Assuming FFM is available on all customer JVMs; assuming in-process .NET is crash-isolated; relying on GraalVM Native Image as part of a supported Java SE stack. | Out-of-process worker (crash-isolated), or in-process JNI/FFM with depth limits and `--enable-native-access`. | [C] |
| 10 | Cross-runtime numeric/string behaviour: `Math.Sin` etc. call the C runtime ("may differ between operating systems or architectures"); `string.GetHashCode` differs across implementations and runs; .NET Core 3.0+ changed `double` formatting; Mono-vs-IL2CPP float differences reported. | Any "identical results" claim that rests on BCL math, BCL hashing or BCL number formatting. | An engine that owns its number formatting, parsing, hashing and iteration order, and limits or pins transcendental math. | [C] docs, [S] Unity float reports |

**Bottom line.** The store rules and iOS together leave exactly one class of
solution that reaches every target with one script: **an interpreter that ships
inside each app binary, reads downloaded source or bytecode, and exposes only a
fixed, reviewed host API**. JIT-dependent engines lose most of their speed on
iOS, downloaded native or IL code is forbidden or impossible, and IL2CPP removes
every run-time code generation trick. A managed C# interpreter such as the
MoonSharp fork is inside that class on every target. The remaining risks are
determinism (row 10), IL2CPP/trimming reflection gaps (rows 4–6) and in-process
JVM crash coupling (row 9). All three can be handled in the engine.

---

## 1. Store policies on downloaded and interpreted code

### 1.1 Apple

**Developer Program License Agreement, section 3.3.1(B) "Executable Code"** —
page says "Developer Program License Agreement, including its Schedule 1 last
updated October 13, 2026" [C]
(<https://developer.apple.com/support/terms/apple-developer-program-license-agreement/>,
downloaded 2026-10-28):

> Except as set forth in the next paragraph, an Application may not download or
> install executable code. Interpreted code may be downloaded to an Application
> but only so long as such code: (a) does not change the primary purpose of the
> Application by providing features or functionality that are inconsistent with
> the intended and advertised purpose of the Application (b) does not bypass
> signing, sandbox, or other security features of the OS; and (c) for
> Applications distributed on the App Store, does not create a store or
> storefront for other Applications.

The "next paragraph" only covers learning-to-program apps. The task brief's
"3.3.x" is this clause: Apple reorganised section 3.3 on 2023-12-22 and the old
3.3.2 interpreted-code text now lives in 3.3.1(B)
(<https://developer.apple.com/news/?id=7k36ynqd>) [C].

**App Review Guidelines** — page footer "Last Updated: October 16, 2026" [C]
(<https://developer.apple.com/app-store/review/guidelines/>):

- **2.5.2:** "Apps should be self-contained in their bundles, and may not read
  or write data outside the designated container area, nor may they download,
  install, or execute code which introduces or changes features or
  functionality of the app, including other apps." [C]
- **2.5.19:** "Don't include any hidden, dormant, or undocumented features in
  your app; your app's functionality should be clear to end users and App
  Review." [C]
- **2.3.1(a):** new features and product changes "must be described with
  specificity in the Notes for Review section". [C]
- **4.7 / 4.7.2:** apps may offer HTML5/JavaScript mini apps and mini games
  that are not in the binary, but "Your app may not extend or expose native
  platform APIs or technologies to the software without prior permission from
  Apple." [C] Not our use case (our scripts are not user-facing software), but
  it shows Apple's line: downloaded code must not reach native APIs.

**Enforcement precedent (2017).** Apple sent warnings to apps that used
hot-patch SDKs. Text quoted by a developer on Apple's forum [S]
(<https://developer.apple.com/forums/thread/73640>):

> This includes any code which passes arbitrary parameters to dynamic methods
> such as dlopen(), dlsym(), respondsToSelector:, performSelector:,
> method_exchangeImplementations(), and running remote scripts in order to
> change app behavior or call SPI, based on the contents of the downloaded
> script. Even if the remote resource is not intentionally malicious, it could
> easily be hijacked via a Man In The Middle (MiTM) attack …

The pattern Apple flagged was a script engine bridged to **arbitrary** native
or Objective-C reflection, not interpretation itself.

### 1.2 Google Play

Device and Network Abuse policy
(<https://support.google.com/googleplay/android-developer/answer/9888379>, no
effective date on the page, read 2026-10-28) [C]:

- "an app may not download executable code (such as dex, JAR, .so files) from
  a source other than Google Play."
- "This restriction does not apply to code that runs in a virtual machine or an
  interpreter where either provides indirect access to Android APIs (such as
  JavaScript in a webview or browser)."
- "Apps or third-party code, like SDKs, with interpreted languages (JavaScript,
  Python, Lua, etc.) loaded at run time (for example, not packaged with the app)
  must not allow potential violations of Google Play policies."

### 1.3 How games stay compliant in practice

Large numbers of shipped games download interpreted code: the IL2CPP
hot-update project HybridCLR states that over a thousand projects using it are
live on the App Store and Google Play [S]
(<https://github.com/focus-creative-games/hybridclr/blob/main/README_EN.md>),
and Lua hot-fix frameworks are common. Neither store publishes an approved
list, so the practices below are inferred from the policy text [U]:

1. The interpreter ships in the binary; only source or bytecode is downloaded.
2. Scripts stay inside the app's reviewed purpose (pricing rules, eligibility,
   offer selection, tuning). New user-visible features ship as app updates.
3. The host API exposed to scripts is fixed and narrow: no `dlopen`, no
   reflection over arbitrary types, no access to private APIs (the 2017
   warning and 4.7.2).
4. Payloads are fetched over TLS and **signed**; the client verifies the
   signature before running anything (answers Apple's MITM concern).
5. Script-driven behaviour is described in review notes (2.3.1(a), 2.5.19).

**Policy fit for our need:** downloaded Lua or JS business rules run by an
embedded interpreter fit both stores when the five practices hold. Our need
also says sandboxing is not a goal; for store purposes the relevant limit is
the *host API surface*, not the language sandbox. An engine that lets a
script reflect over any CLR type (MoonSharp's automatic `UserData` registration
of whole assemblies) is the part to switch off in mobile builds.

## 2. iOS: no JIT for ordinary apps

**What is allowed.** Alternative browser engines may use JIT through
BrowserEngineKit, only in the EU (iOS 17.4+ / iPadOS 18+) and Japan (iOS 26.2+,
Mobile Software Competition Act), and only for a dedicated browser app or an app
whose primary focus is in-app browsing; the app must be distributed solely in
that region [C] (<https://developer.apple.com/support/alternative-browser-engines/>,
<https://developer.apple.com/support/alternative-browser-engines-jp>). Apple
lists JIT among the "system technologies" it makes available to approved
engines and requires passing tests with JIT unavailable (Lockdown Mode) [C].
A game or a business app cannot use this route.

**Effect per runtime class:**

| Runtime | On iOS | Evidence |
|---|---|---|
| LuaJIT 2.1 | Builds with `LJ_OS_NOJIT` unless `LUAJIT_ENABLE_JIT` is forced: "Runtime code generation is restricted on iOS. Complain to Apple, not me." (`src/lj_arch.h`, vendored copy in xlua-ng `build/luajit-2.1/src/lj_arch.h:588-592`; upstream <https://github.com/LuaJIT/LuaJIT/blob/v2.1/src/lj_arch.h>). Interpreter only; FFI callbacks use a reduced path. | [C] |
| V8 | "jitless is a hard requirement for iOS (as of Dec. 2020)" (<https://v8.dev/docs/cross-compile-ios>). Published jitless cost: Speedometer 2.0 ~40 % slower, Web Tooling Benchmark ~80 % slower (V8 7.4, x64 Linux desktop, 2019; <https://v8.dev/blog/jitless>). The same post said Wasm was unsupported in jitless mode; a Wasm interpreter ("DrumBrake") has since been reported in Chromium's jitless mode [S]. | [C] / [S] |
| JavaScriptCore via `JSContext` | No JIT for third-party processes; WKWebView JIT runs in Apple's own web content process [U, widely reported, no Apple doc read]. | [U] |
| Wasm runtimes | Wasmtime: "Usage of the Cranelift or Winch requires a host operating system which supports creating executable memory pages on-the-fly"; the Pulley interpreter is the portable fallback, "will never be as fast as native Cranelift backends"; iOS is "supported but less well tested" (`docs/stability-platform-support.md` at [`b1b352b`](https://github.com/bytecodealliance/wasmtime/blob/b1b352b41637deb576277de80b33a06e7b8acf54/docs/stability-platform-support.md), 2026-10-27). wasm3 is in "minimal maintenance" (README at [`28ecb9a`](https://github.com/wasm3/wasm3/blob/28ecb9af6d2040e474a70f7cb7f43666740141fb/README.md)). Precompiled (AOT) Wasm is native code: it must ship inside the binary, never be downloaded (row 1). | [C] |
| .NET (Mono/IL2CPP/NativeAOT) | Everything is AOT; .NET 11 adds a CoreCLR interpreter path and notes "On platforms that lack JIT support, such as iOS, interface dispatch was falling back to an expensive generic fixup path" (<https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime>, updated for RC2, 2026-10-13). Expression trees fall back to the LINQ interpreter or fail. | [C] |

**Conclusion for iOS:** every candidate runs as an interpreter. Comparing a
JIT engine's desktop benchmark with MoonSharp's IL2CPP numbers overstates the
JIT engine's iOS speed. Benchmarks that matter for the client must be run on an
iOS device with the shipping build mode.

## 3. Unity IL2CPP and the Unity C# toolchain

### 3.1 Per-version facts

| | 2021.3 LTS | 2022.3 LTS | Unity 6 (6000.0 / 6000.3 LTS) |
|---|---|---|---|
| C# version | 9.0; unsupported: init-only setters, covariant returns, module initializers, `SkipLocalsInit`, unmanaged calling-convention extensions | same | same (6000.3 page) |
| API compatibility | .NET Standard 2.1 (default) or .NET Framework 4.8 (+ .NET Standard 2.1 APIs) | same | same (6000.3 page) |
| Source generators | must reference Microsoft.CodeAnalysis **3.8**; project targets `netstandard2.0`; example uses `ISourceGenerator` | **3.8** | **Microsoft.CodeAnalysis.CSharp 4.3** (6000.0 and 6000.3 pages) |
| System.Text.Json in generators | "Unity only supports version 6.0.0-preview" | same | same |
| Missing generic instantiation at run time | `ExecutionEngineException` (manual's `OnMessage<T>` example) | fully shared fallback code, slower; with "Faster (smaller) builds" only the fully shared version is compiled | same as 2022.3 |
| Support status | not on Unity's current support page | Unity blog: "fully supported until at least May 30, 2025", plus one Enterprise year [S] | 6.0 LTS "supported through October 2026"; 6.3 LTS "supported until December 2027" |

Sources [C]:
<https://docs.unity3d.com/2021.3/Documentation/Manual/CSharpCompiler.html>,
<https://docs.unity3d.com/6000.3/Documentation/Manual/csharp-compiler.html>,
<https://docs.unity3d.com/6000.3/Documentation/Manual/dotnet-profile-support.html>,
<https://docs.unity3d.com/2021.3/Documentation/Manual/roslyn-analyzers.html>,
<https://docs.unity3d.com/2022.3/Documentation/Manual/roslyn-analyzers.html>,
<https://docs.unity3d.com/6000.0/Documentation/Manual/create-source-generator.html>,
<https://docs.unity3d.com/6000.3/Documentation/Manual/create-source-generator.html>,
<https://docs.unity3d.com/2021.3/Documentation/Manual/ScriptingRestrictions.html>,
<https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptingRestrictions.html>,
<https://unity.com/releases/unity-6/support>. 2022.3 dates [S]:
<https://unity.com/blog/unity-2021-long-term-support-extended>,
<https://endoflife.ai/unity/2022.3>. Full generic sharing introduction [S]:
<https://unity.com/blog/engine-platform/il2cpp-full-generic-sharing-in-unity-2022-1-beta>.

Incremental generators (`IIncrementalGenerator`) exist in Roslyn 4.0+, so they
are not available on 2021.3/2022.3 (Roslyn 3.8) [U, inferred from the version
numbers; Unity's pages do not mention them].

### 3.2 Restrictions that apply to every Unity version [C]

From the scripting restrictions pages above:

- "An AOT platform can't implement any of the methods in the
  System.Reflection.Emit namespace."
- "IL2CPP does not support the C# `dynamic` keyword."
- Generic virtual calls and `MakeGenericType`/`MakeGenericMethod` instances that
  the compiler cannot see may be missing; the documented workaround is a
  never-called `UsedOnlyForAOTCodeGeneration` method that mentions each
  instantiation.
- Reverse P/Invoke callbacks must be static and carry `[MonoPInvokeCallback]`.
- Reflection over `MarshalAs`/`FieldOffset` is not supported at run time.

### 3.3 Managed code stripping [C]

<https://docs.unity3d.com/6000.3/Documentation/Manual/managed-code-stripping-configure.html>,
<https://docs.unity3d.com/6000.3/Documentation/Manual/managed-code-stripping-preserving.html>:

- Levels: Disabled, Minimal, Low, Medium, High. **Minimal is the IL2CPP
  default; Disabled exists only for Mono.**
- A `link.xml` "can't" be included in a package; project-level `link.xml` may
  reference package assemblies.
- A `PreserveAttribute` defined "in any assembly and in any namespace" is
  honoured; `RequireDerived`, `RequiredInterface`, `RequireAttributeUsages`
  and `AlwaysLinkAssembly` are also available.
- An embedded `<AssemblyName>.xml` resource is reported to work as a packaged
  `link.xml` [S] (<https://github.com/jilleJr/Newtonsoft.Json-for-Unity/wiki/Embed-link.xml-in-UPM-package>);
  whether Unity's linker honours an embedded `ILLink.Descriptors.xml` (which
  the fork already embeds for .NET trimming) is **not confirmed** [U].

### 3.4 WebGL / Web [C]

- `PlayerSettings.WebGL.threadsSupport` (6000.4 page): threads enable "C/C++
  code and C# jobs compiled with the Burst compiler"; "Multithreading standard
  C# code is not yet available." 2022.3 and 6000.0 pages: native C/C++ only
  (<https://docs.unity3d.com/ScriptReference/PlayerSettings.WebGL-threadsSupport.html>).
- Exception support default is "Explicitly Thrown Exceptions Only"; "Full
  Without Stacktrace" is the level that "also catches null references and
  out-of-bounds array access"; "None" stops the content on any exception
  (<https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-building.html>).

### 3.5 What this means for the current MoonSharp code [C]

Read at upstream `moonsharp-devs/moonsharp`
[`fbd0367`](https://github.com/moonsharp-devs/moonsharp/tree/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7)
(the fork's `master` at `0c0e5d4` has the same code; some line numbers shift, e.g. `ToDynamic` is at `DynValue.cs:898` there):

- `PlatformAutoDetector.IsRunningOnAOT` decides by compiling an expression
  tree in a `try`/`catch`
  ([L43-L67](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Platforms/PlatformAutoDetector.cs#L43-L67)),
  unless built as source in Unity with `ENABLE_IL2CPP`. The fork measured that
  this returns `false` on NativeAOT because the expression interpreter succeeds
  ([hosting.md](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/docs/hosting.md#nativeaot-and-trimming)).
  A precompiled MoonSharp DLL under IL2CPP is in the same position [U].
- Field/property/method descriptors build `Expression.Lambda(...).Compile()`
  getters and setters (`FieldMemberDescriptor.cs:135`,
  `PropertyMemberDescriptor.cs:175-216`, `MethodMemberDescriptor.cs:272-279`).
- `TableConversions` uses `MakeGenericType` for `List<T>`/`Dictionary<K,V>`
  (`TableConversions.cs:137,184`); `ExtensionMethodsRegistry` uses
  `MakeGenericMethod` (`ExtensionMethodsRegistry.cs:153`).
- `DynValue.ToDynamic()` uses `dynamic` (`DynValue.cs:887`).
- The fork's NativeAOT run of MoonSharp's own test suite: 680 of 732 pass;
  all 49 interop failures are `Enum.GetValues(Type)`, generic collection
  construction and LINQ `MakeGenericMethod` — the same classes IL2CPP rejects
  ([hosting.md](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/docs/hosting.md#nativeaot-and-trimming)).
  NativeAOT is a usable, licence-free proxy for IL2CPP failures in CI [U: proxy
  quality not measured against a real IL2CPP player].

## 4. Unity 6.x → Unity 7: CoreCLR

Primary sources, Unity's scripting team on Unity Discussions [C]:

- 2026-03-06: 6.7 LTS has an "Experimental Release of the CoreCLR Desktop
  Player" and is the last Mono-based release; .NET 10 / C# 14 were planned
  then; CoreCLR replaces domain reload with Assembly Load Context (ALC)
  reloads (<https://discussions.unity.com/t/coreclr-scripting-and-ecs-status-update-march-2026/1711852>).
- 2026-06-16: 6.6 makes scene-reload-only Play mode the default for new
  projects, "so code isn't reloaded and static state isn't reset"; 6.7 Alpha 5
  has an experimental IL2CPP iOS player; experimental players are limited to
  .NET Standard 2.1 and C# 9; Unity 7.0 removes Mono
  (<https://discussions.unity.com/t/coreclr-scripting-and-serialization-update-june-2026/1723299>).
- 2026-10-05: the Unity 7.0 alpha is available; the editor runs on CoreCLR
  without Mono "but still targets C# 9 and .NET Standard 2.1"; C# 14 / .NET 10:
  "We cannot commit to a public date yet." All platforms migrate to an updated
  IL2CPP with a .NET 10-based BCL, one platform at a time; IL2CPP keeps the
  Boehm GC "for the foreseeable future"
  (<https://discussions.unity.com/t/coreclr-scripting-and-net-update-october-2026/1738338>).

Unity 7 GA timing is only reported by third parties ("early 2027", "beta
December 2026, release Q1 2027") [S]
(<https://www.guardingpearsoftware.com/blog/unity-is-replacing-mono-with-coreclr-what-changes-16411>,
<https://daily.dev/posts/the-unity-engine-roadmap-unite-seoul-2026-d8ar8a69r>).

**Consequences through 2027 [U, derived from the above]:**

- The Unity-facing library stays C# 9 / `netstandard2.0` or `2.1` for the
  whole period: 2021.3 and 2022.3 never move, and Unity 7.0 itself starts at
  C# 9.
- IL2CPP remains the mobile and WebGL backend; its AOT rules (section 3) do not
  go away with CoreCLR.
- With 6.6's default and Unity 7's ALC reloads, static caches inside the engine
  (type descriptor registries, string interning tables, default options)
  survive between Play sessions and may hold types from an unloaded ALC.

## 5. .NET on WebAssembly in 2026

- **Runtime:** .NET 10 is the current LTS. In .NET 11 (RC2 docs dated
  2026-10-13) CoreCLR-on-Wasm is an opt-in build flavour ("WebCIL V1 is the
  default for CoreCLR WASM builds", Emscripten re-link works, "CoreCLR on
  WebAssembly now runs the libraries test suite end to end"); Mono remains the
  default [C] (<https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime>).
  A framework vendor's decision record (2026-09-17, open) expects .NET 12 to
  move browser-wasm to CoreCLR and reports a contributor's impression that
  "MonoVM will not exist" in .NET 12 [S]
  (<https://github.com/unoplatform/Uno.Wasm.Bootstrap/issues/1074>).
- **Speed:** one hobby project measured .NET 11 RC2 CoreCLR-interpreter on Wasm
  at ~98 s vs Mono AOT ~10 s to the same load point, ~21 s with a custom
  ReadyToRun step [S] (<https://github.com/meneerkrabs/OpenTPW/pull/341>).
  Single data point, not a benchmark.
- **Size:** no Microsoft-published 2026 number found. A magazine article gives
  "on the order of 2 to 4 MB" compressed for a trimmed app, AOT "roughly
  double" [S] (<https://codemag.com/Article/2610051/Building-Cross-Platform-High-Performance-Apps-Using-WASM-and-.NET>).
  The fork's browser build should publish its own measured `_framework` size.
- **Threads:** browser .NET apps are described as single-threaded; CoreCLR-Wasm
  is described as a step toward removing that constraint [S] (same article).
- **WASI:** still the `wasi-experimental` workload from .NET 8; I found no
  official .NET 10/11 status statement [U]
  (<https://github.com/SteveSandersonMS/dotnet-wasi-sdk> points to it).
- **Trimming:** the fork already hit trimming of reflection-registered standard
  library functions in the browser and fixed it with an embedded ILLink
  descriptor [C] (fork `docs/hosting.md`, commit `c3fb9ca` cited there).

**Alternatives for the browser that keep one engine:** none beyond shipping
the same managed engine on .NET Wasm. Lua-in-JS ports (Fengari) or C Lua
compiled to Wasm (wasmoon) are different engines and need cross-engine
conformance tests to claim identical results [U].

## 6. JVM integration

- **FFM (Panama):** "Closed / Delivered" in JDK 22; restricted methods warn
  unless `--enable-native-access` (or `Enable-Native-Access: ALL-UNNAMED` in an
  executable JAR) [C] (<https://openjdk.org/jeps/454>).
- **JNI restrictions:** JEP 472 delivered in JDK 24: loading native libraries
  through JNI or FFM warns by default; "a future JDK release will throw
  exceptions by default" [C] (<https://openjdk.org/jeps/472>).
- **Baseline:** Spring Boot 4.0 requires Java 17 and supports up to 25 [S]
  (<https://docs.spring.io/spring-boot/4.0/system-requirements.html>). So an
  FFM-only binding excludes Java 17/21 servers; JNI covers them.
- **GraalVM:** Oracle's blog: "GraalVM for JDK 24 was the final GraalVM release
  licensed and supported as part of Oracle Java SE Products … Native Image, is
  being discontinued for Java SE Product customers", startup work moves to
  Project Leyden [S, quoted via search of
  <https://blogs.oracle.com/java/post/detaching-graalvm-from-the-java-ecosystem-train>].
  GraalVM Community releases continue on their own cadence (25.2.4 on
  2026-07-28 per <https://www.graalvm.org/release-notes/JDK_25>) [S].
  Running a JS/Wasm engine on GraalVM is therefore a non-standard dependency
  for a Spring Boot server.
- **.NET in the JVM process:**
  - `hostfxr`: "Only one runtime can be loaded inside a single process";
    framework-dependent deployments only [C]
    (<https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting>, 2026-08-10).
  - NativeAOT shared library: exports `[UnmanagedCallersOnly(EntryPoint=…)]`;
    "Unloading Native AOT libraries … is not supported" [C]
    (<https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries>).
  - The fork's `CrashProbe` measurements (Linux x64, OpenJDK 21, .NET 8) [C]:
    deep Lua recursion is caught in all modes, but recursion through a CLR
    callback (`string.gsub` calling `gsub`) **kills the JVM** in process
    (CoreCLR: SIGABRT; NativeAOT: SIGSEGV) while the worker mode survives;
    CoreCLR's SIGTERM handler skipped JVM shutdown hooks until the shim
    restored the JVM handler; a script that never returns cannot be stopped in
    process. NativeAOT: open + first call 38 ms vs 237 ms CoreCLR vs 223 ms
    worker; 5.5 MB library
    ([hosting.md](https://github.com/emindeniz99/moonsharp/blob/0c0e5d4b32e1566e525a5c76b7fac82a27225fb1/docs/hosting.md)).
  - An older Apache Ignite guide documents the same SIGSEGV/SIGCHLD handler
    conflict between .NET and the JVM [S]
    (<https://ignite.apache.org/docs/latest/net-specific/net-troubleshooting>).
- **Wasm inside the JVM:** Chicory is a pure-Java Wasm runtime with WASI
  preview 1 and a Wasm-to-bytecode compiler (Maven releases March 2026) [S]
  (<https://mvnrepository.com/artifact/com.dylibso.chicory>). Running a .NET
  WASI build of the engine on it depends on the experimental .NET WASI workload
  [U]; not a 2027 production path.

## 7. Determinism constraints across runtimes

| Source of divergence | Fact | Evidence |
|---|---|---|
| Transcendental math | `Math.Sin`: "This method calls into the underlying C runtime, and the exact result or valid input range may differ between different operating systems or architectures." | [C] <https://learn.microsoft.com/en-us/dotnet/api/system.math.sin?view=net-10.0> |
| Float evaluation per backend | Unity issue UUM-104759: Mono on Apple Silicon differs from IL2CPP and server CoreCLR; closed "By Design". Unity forum: IL2CPP gives no cross-architecture float determinism. | [S] <https://issuetracker.unity3d.com/issues/apple-silicon-floating-point-inconsistencies-between-mono-and-il2cpp>, <https://discussions.unity.com/t/are-floating-point-numbers-deterministic-on-the-same-architecture-il2cpp/892251> |
| String hashing | "Hash codes for identical strings can differ across .NET implementations, across .NET versions, and across .NET platforms … two subsequent runs of the same program may return different hash codes." | [C] <https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0> |
| Number formatting | .NET Core 3.0 made formatting/parsing IEEE-compliant and shortest-round-trip; earlier frameworks did not guarantee round-trip. Which path Unity's Mono and IL2CPP class libraries use is not documented. | [C] <https://devblogs.microsoft.com/dotnet/floating-point-parsing-and-formatting-improvements-in-net-core-3-0/>; Unity side [U] |
| Culture | Fixed in the fork (PR #4: JSON numbers, case and string order no longer depend on process culture). | [C] fork ROADMAP |
| Table iteration order | MoonSharp `Table` stores pairs in a `LinkedList<TablePair>` with dictionary indexes, so `pairs` order is insertion order, independent of hash codes (`Table.cs:12-31`). A Lua-CSharp-style hash part would make order depend on the hash function. | [C] <https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/Table.cs#L12-L31> |
| Number → string | MoonSharp `tostring` uses `double.ToString(CultureInfo.InvariantCulture)` (`DynValue.cs:539`, `BasicModule.cs:246`); JSON uses `"r"` (`JsonTableConverter.cs:99`; the fork adds `InvariantCulture` at line 100). Output therefore follows each runtime's BCL formatter. Lua 5.2 itself uses `%.14g`. | [C] code; BCL divergence [U] |

## 8. Which constraints rule out which classes of solution

| Class of solution | iOS (no JIT) | Store rules | IL2CPP | WebGL | Browser | JVM | Verdict |
|---|---|---|---|---|---|---|---|
| Managed C# interpreter (MoonSharp fork, Lua-CSharp, Jint-like) | OK (AOT C#) | OK if host API is narrow | OK if no codegen, bindings pre-generated, preserved | OK, single thread | same assembly on .NET Wasm | worker, or in-process via JNI/FFM with limits | **fits** |
| Native C Lua / Luau via P/Invoke (xLua-style) | OK, interpreter | OK | OK (`__Internal`, `MonoPInvokeCallback`) | needs Emscripten build of the C library | separate Wasm build + JS glue | separate JNI/FFM build per OS/arch | **partial**: one script language everywhere, but several native builds, `unsafe`/native crash surface, CLR interop by marshalling |
| LuaJIT | interpreter only on iOS (`LJ_OS_NOJIT`) | OK | as above | no JIT in Wasm; interpreter only | as above | as above | **partial**: speed edge gone on iOS/Web; Lua 5.1 semantics |
| V8 / full JS JIT engines | `--jitless` required (~40–80 % slower on published benchmarks) | OK if interpreted | native plugin | n/a (browser has its own JS) | native JS | GraalJS (non-standard) or J2V8/Javet | **partial**: large binaries; JS differs between engines |
| QuickJS-class JS interpreters | OK | OK | native plugin | needs Wasm build | browser JS differs from QuickJS in edge cases | JNI/FFM | **partial** |
| Wasm interpreters embedded in each host | OK (interpreter) | bytecode = interpreted: OK; AOT/native: no | native plugin | Wasm inside Wasm | native | Chicory (pure Java) | **partial**: one artifact everywhere, but interop is by-value only and the guest language/toolchain is a second choice |
| Downloaded .NET assemblies / IL interpretation needing libil2cpp patches (HybridCLR-style) | works in practice | executable-code risk; relies on "interpreted" reading | **excluded** by our "no libil2cpp patches" rule; HybridCLR's installer replaces `libil2cpp` ([InstallerController.cs L198-L233](https://github.com/focus-creative-games/hybridclr_unity/blob/e48a7beb956f79cd33e636e0efbb1914ff045437/Editor/Installer/InstallerController.cs#L198-L233)) | — | — | — | **no** |
| Run-time code generation (Reflection.Emit, compiled expression trees, `dynamic`) for speed | impossible | — | impossible | impossible | Mono-Wasm interprets | fine | **no** as a requirement; allowed only as an optional JIT-host fast path |
| Downloaded native code, AOT Wasm, R2R images | impossible / forbidden | **forbidden** | — | — | — | — | **no** |

## 9. What the MoonSharp fork must guarantee to stay safe through 2027

Each item names the constraint it answers.

1. **No run-time code generation on any path that can run on IL2CPP, NativeAOT
   or Wasm** (rows 3, 4, 8). Detect with
   `System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported`
   plus Unity defines, not with a `try { Expression.Compile() }` probe; the
   probe gives the wrong answer on NativeAOT (measured) [C] and probably on a
   precompiled DLL under IL2CPP [U]. Keep `Expression.Compile` only as an
   opt-in fast path on JIT hosts.
2. **No `dynamic`, no `MakeGenericType`/`MakeGenericMethod` over
   caller-supplied types on AOT paths** (row 4). Generate bindings at compile
   time (source generator against Roslyn **3.8**, with a 4.3 build for Unity 6)
   or provide explicit, pre-instantiated converters (`List<T>`, `Dictionary<K,V>`
   for the types the host API uses). Replace `Enum.GetValues(Type)` with static
   field reads.
3. **Stripping-proof by construction** (row 4): mark reflection-reached members
   with a library-defined `PreserveAttribute` (Unity honours any namespace),
   keep the embedded ILLink descriptor for .NET trimming, and document the
   project-level `link.xml` Unity users must add (packages cannot carry one).
   Test with IL2CPP stripping at **Medium/High**, not only Minimal.
4. **Language level C# 9, targets `netstandard2.0` (Unity 2021.3+) and
   `netstandard2.1`, no `unsafe`, no `System.Runtime.CompilerServices.Unsafe`**
   (rows 5, 7). No init-only setters or covariant returns in Unity-compiled
   source. Re-check when Unity 7 ships C# 14, not before.
5. **Single-threaded core with no thread or timer dependency** (rows 6, 8):
   coroutines stay VM-level; any async API is a wrapper the host may ignore.
6. **Exceptions only by explicit `throw`** (row 6): never rely on catching
   `NullReferenceException`, `IndexOutOfRangeException` or
   `InvalidCastException` raised by the runtime (two `catch
   (InvalidCastException)` sites exist today in field/property descriptors).
   WebGL's default exception mode would abort instead.
7. **Bounded native stack use** (rows 6, 9): limits on parser nesting, on Lua
   call depth and on re-entrant CLR→Lua→CLR depth, reported as Lua errors. A
   CLR `StackOverflowException` cannot be caught on CoreCLR/IL2CPP and killed
   the JVM in the fork's measurements. Same for runaway loops: an instruction
   budget or cancellation check the host can set, since in-process hosts
   cannot kill a thread.
8. **The engine owns everything that decides output bytes** (row 10):
   - number → string (`tostring`, `..`, `string.format('%g')`, JSON) by the
     engine's own formatter (Lua's `%.14g` for `tostring`, shortest round-trip
     for JSON), never `double.ToString()`;
   - string → number parsing by the engine's own parser;
   - table iteration order independent of `GetHashCode` (keep insertion order,
     or use a fixed engine hash such as FNV/xxHash over UTF-16 code units) —
     this constrains any Lua-CSharp-style table redesign;
   - culture-invariant everything (done in PR #4);
   - `math.random` with a specified, seeded generator;
   - transcendental `math.*` either implemented in managed code (portable,
     bit-identical) or documented as "may differ in the last bit across
     platforms" and excluded from business rules by lint; integer and
     `int64` arithmetic exact everywhere (done for `int64`/`uint64`).
9. **A cross-runtime conformance gate** (rows 8–10): the existing fixtures run
   on .NET JIT, NativeAOT, browser Wasm, the JVM hosts, and Unity Mono + IL2CPP
   players (the ROADMAP marks Unity as "Not tested"), comparing output bytes,
   not parsed values. Add iOS and Android device runs before any client
   release; NativeAOT in CI is a cheap early proxy for IL2CPP failures.
10. **Narrow, explicit host API for downloaded scripts** (rows 1, 2): in
    client builds, disable automatic registration of arbitrary CLR types and
    `load`/`dofile` from paths; expose only registered functions. Verify a
    signature over each script bundle before compiling it.
11. **Reload-safe static state** (row 7): no engine-global caches that hold CLR
    `Type`s across Unity Play sessions or ALC unloads, or a documented reset
    API called on `[RuntimeInitializeOnLoadMethod]`/subsystem registration.
12. **JVM binding that works on Java 17 and 25+** (row 9): keep JNI for 17/21,
    offer FFM for 22+, ship `Enable-Native-Access` guidance, keep the
    out-of-process worker as the crash-isolated default, and restore the JVM's
    signal handlers after starting .NET (already done for SIGTERM).
13. **No dependency on GraalVM, V8 or a native Lua build** for any target, so
    the "same engine everywhere" claim stays true without per-OS native
    artifacts on the client.

## 10. Verdict for our need

| Option class | Verdict | Why |
|---|---|---|
| Managed C# interpreter (the MoonSharp fork) | **Fits** | Only class that is AOT-safe on IL2CPP/iOS/WebGL, runs the *same assembly* in .NET, browser Wasm and the JVM (worker or in-process), and gives first-class CLR interop on Unity without native code. Risks are engineering, not policy: the 13 guarantees above, mainly determinism ownership, IL2CPP/stripping safety and JVM crash coupling. |
| Native Lua/Luau via P/Invoke | **Partial** | Store- and iOS-compatible as an interpreter, fast, but needs separate native builds for every Unity platform, Emscripten for browser/WebGL and JNI/FFM per server OS; marshalling-based CLR interop; native crashes. |
| LuaJIT | **Partial** | iOS and Wasm builds are interpreter-only, so its main advantage disappears on the client. |
| JS engines (V8 jitless, QuickJS, Jint) | **Partial** | Store-compatible; one language with the browser, but the browser's own JS engine is a different implementation, V8 jitless loses 40–80 % on published benchmarks, and Jint is already covered elsewhere. |
| Wasm as the script format | **Partial** | Store-compatible only with interpreters; one binary format everywhere, but by-value interop, a second toolchain, and interpreter speed on iOS. |
| C# hot update via IL interpretation inside IL2CPP | **No** | Requires libil2cpp patches (excluded by our rules); not usable in browser/JVM. |
| Anything that downloads native, AOT or IL code, or needs a JIT | **No** | Forbidden by Apple DPLA 3.3.1(B) and Google Play, or impossible on iOS/IL2CPP. |

## Open questions to settle with measurements

1. Does Unity's IL2CPP linker honour an embedded `ILLink.Descriptors.xml`? [U]
2. What does `double.ToString(CultureInfo.InvariantCulture)` return for `0.1+0.2`
   under Unity 2021.3/2022.3/6 Mono and IL2CPP? (Decides how urgent guarantee 8 is.) [U]
3. What does `RuntimeFeature.IsDynamicCodeSupported` return under Unity
   IL2CPP and Mono on each LTS? [U]
4. The fork's measured browser download size (compressed `_framework`) for
   .NET 10 interpreter and AOT builds. [none published]
5. Unity 7.0 GA date and when IL2CPP moves to the .NET 10 BCL per platform. [S only]
