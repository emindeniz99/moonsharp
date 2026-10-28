# Lua-family landscape: one script on Unity, browser, JVM and .NET

Surveyed 2026-10-28. This note is about **fit**, not VM internals. The fit criteria come
from the need below. Repositories were shallow-cloned (`--depth 1 --filter=blob:none`) and
read as data. No build script, test or install hook was run. Package dates come from the
npm and NuGet registry APIs. Dates and versions are as of the survey day.

Confidence marks: **[C]** confirmed by reading source, docs or registry metadata.
**[S]** secondary (blog, issue, forum, search summary). **[U]** unconfirmed or my
inference.

**The need, in general terms.** A backend ships business-logic scripts (rules,
eligibility, offer and variant selection, config-driven behaviour). Each script must give
identical results on:

- Unity 2021.3, 2022.3 and Unity 6 (Mono and IL2CPP; iOS, Android, desktop; WebGL is a plus);
- browsers;
- JVM servers;
- plain .NET.

The scripts are trusted. The other requirements:

- exact 64-bit integers;
- JSON in and out;
- deterministic results;
- small download and fast cold start;
- cheap host calls and good CLR interop on Unity;
- a debugger, types and autocomplete;
- only public, documented Unity mechanisms.

**The baseline being judged against.** A managed Lua engine in C# (a MoonSharp fork). It
reaches the browser through .NET WebAssembly and the JVM through a .NET worker process.

---

## Summary table

| Candidate | Core | Unity IL2CPP / iOS | Unity WebGL | Browser | JVM | Plain .NET | 64-bit int | Typed tooling | Activity (Oct 2026) | Licence | Fit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **MoonSharp upstream** (moonsharp-devs) | Managed C#, Lua 5.2 | yes [C] | yes since 2.0 [C] | only via .NET WASM | no (needs a .NET process) | yes | no, double only [C] | LuaCATS/LuaLS; VS Code debugger built in [C] | revived: commits to 2026-10-14; `v3.0.0-beta.1` on NuGet 2026-07-04 [C] | BSD-3 | baseline family |
| MoonSharp `luau` branch | same | same | same | same | no | yes | no (parses Luau syntax and drops the types) [C] | Luau-style annotations parsed but **not checked** [C] | 11 commits, 2026-07-30 to 08-04, not merged [C] | BSD-3 | syntax only |
| **WattleScript** | Managed C#, MoonSharp fork | yes, with its source generator [C] | [U] | via .NET WASM | no | yes | no [U] | own dialect plus Lua 5.2 | one maintainer, 8 commits in 2026; last NuGet 1.0.0 in 2022-10 [C] | BSD-3 | no |
| **Lua-CSharp** (nuskey8) | Managed C#, Lua 5.2 | Mono and IL2CPP [C] | [U] | via .NET WASM | no | yes (netstandard2.1+) | no (`number` is double/float/int) [C] | LuaCATS; debug hooks [C] | very active: v0.5.8 2026-10-25 [C] | MIT | baseline family |
| **xLua** (Tencent) | native PUC Lua 5.3 / LuaJIT plus C# bridge | yes [S] | yes [S] | no | no | yes | yes on 5.3 [S] | EmmyLua / LuaPanda [S] | last commit 2025-11-21; 278 open issues [C] | MIT | Unity only |
| **xLua-NG** (fork) | native Lua 5.5.1 (5.4, 5.3, LuaJIT) plus a source generator | yes [C] | yes, Lua 5.5.1 only [C] | no | no | yes [C] | yes [C] | LuaCATS generation [C] | very active, but **unpublished** [C] | MIT | Unity leg of a native plan |
| slua / toLua# / tolua_runtime | native Lua 5.1 / LuaJIT | yes, historically | partly | no | no | no | no | — | dead (2020 / 2021 / 2019) [C] | MIT | no |
| **NLua / KeraLua** | P/Invoke to native Lua 5.4 | manual plugin setup; NLua uses Reflection.Emit for events [C] | not listed | no | no | yes | yes (5.4) | — | active: KeraLua 1.4.11 on 2026-10-19 [C] | MIT | .NET leg only |
| **NuLua** (nuskey8) | P/Invoke to Lua 5.1–5.5, LuaJIT, Luau | **not yet**; iOS/Android/Web marked 🚧 [C] | no | no | no | desktop x64/arm64 | depends on the backend | — | preview 0.1.0, 2026-06-22; commits to 2026-10-27 [C] | MIT | watch |
| UniLua | Managed C#, Lua 5.2 | yes | [U] | no | no | yes | no | — | dead, last commit 2022-06 [C] | MIT | no |
| **Luau, native** | C++ VM | via NuLua (desktop only) or the archived luau-dotnet [C] | archived luau-dotnet claimed wasm32 [C] | official `Luau.Web` target [C]; `luau-web` npm 1.5.1 [C] | `luau-java` 1.2.1 (FFM, **Java 25+**) [C] | NuLua | int64 is a **library type** behind the `LuauIntegerLibrary` flag; no `+`/`-` operators [C] | best in class: luau-analyze, luau-lsp 1.70.1 [C] | very active: release 0.744 on 2026-10-23 [C] | MIT | partial |
| **LuaJIT** | C plus asm, Lua 5.1 | iOS runs the interpreter only (no JIT) [C] | no known wasm port [S] | no | via luajava (JNI) [C] | KeraLua-style | FFI cdata only [U] | — | commits weekly; rolling, source-only [C] | MIT | no |
| **wasmoon** | PUC Lua 5.4 → wasm, JS API | — | — | yes; 393 kB raw / 130 kB gz [C] | — | — | yes | — | npm 1.16.0 is from **2023-12**; last commit 2025-01 [C] | MIT | browser leg, stale |
| lua-wasm-bindings (TSTL) | PUC Lua 5.0–5.4 → wasm | — | — | partial bindings, written for testing [C] | — | — | yes | — | 0.5.3, 2026-01-26 [C] | MIT | reference only |
| **fengari** | Lua 5.3 rewritten in JS | — | — | yes; 214 kB / 69 kB gz [C] | — | — | **32-bit integers** [C] | — | 0.1.5, 2025-12-26 [C] | MIT | no |
| **LuaJ** (and forks) | pure Java, Lua 5.2 | — | — | — | yes | — | no (double) [U] | — | luaj/luaj last 2020-04; wagyourtail fork 2024-03 [C] | MIT | no |
| **Cobalt** | pure Java, Lua 5.2 (LuaJ-derived) | — | — | — | yes | — | no [U] | — | active (2026-10-16), but its README says **"Using: Don't"** [C] | MIT | no |
| Rembulan | pure Java, Lua 5.3 | — | — | — | yes | — | yes | — | dead (2016) [C] | Apache-2.0 | no |
| **LuaJava** (gudzpoz) | JNI to native Lua 5.1–5.5, LuaJIT, LuaJ | — | — | — | yes: Win, Linux, macOS, Android natives [C] | — | yes (5.3+) | — | 4.1.0 on 2026-01-03; commits to 2026-08 [C] | MIT | JVM leg of a native plan |
| **Chicory** + Lua compiled to wasm | pure-Java wasm runtime | — | — | — | yes, no JNI [C] | — | yes (same C core) | — | Chicory 1.7.5 on 2026-03-24 [C]; **no Lua build found** [S] | Apache-2.0 | spike candidate |
| Teal | typed dialect → Lua 5.1–5.4 | tooling | | | | | `integer` type [U] | own LSP | v0.24.8 2025-10-13; commits to 2026-10-26 [C] | MIT | tooling option |
| LuaCATS / lua-language-server | annotations in comments | tooling | | | | | `integer` annotation | LSP | 3.19.2, 2026-10-13 [C] | MIT | tooling option |
| TypeScriptToLua | TS → Lua 5.0–5.5, JIT, Luau | tooling | | | | | none (TS `number`) [U] | full TypeScript | 1.37.1, 2026-07-09 [C] | MIT | tooling option |

**Short answer.** No. No Lua project in October 2026 runs the *same* script on Unity
IL2CPP, the browser and the JVM out of the box from one maintained package. The nearest
real alternative to the baseline is "the PUC Lua 5.4/5.5 C core everywhere", assembled from
three or four separately maintained bindings. Details are in the verdict at the end.

---

## 1. Managed C# engines (the baseline family)

### 1.1 MoonSharp upstream (`moonsharp-devs/moonsharp`)

- **What it is.** A managed Lua 5.2 interpreter in C#. Its README says "99% compatible
  with Lua 5.2 (... weak tables)". The README also lists IL2CPP and AOT, a built-in JSON
  parser and a Debug Adapter Protocol debugger for VS Code
  ([README @ fbd0367](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/README.md)). [C]
- **Activity: revived.** The project was dormant for years and is active again in 2026:
  - the default branch `master` has commits up to 2026-10-14, for example PR #353 "Honor `__call` in pcall and xpcall" and PR #349 on labels in empty scopes; [C]
  - committers include Benjamin Dobell, Max Hayman, XinyuGuan, Kelly Birr, Immanuel Scholz and Ariel Coppes; [C]
  - `v3.0.0-beta.1` was tagged 2026-03-03 and published to NuGet 2026-07-04 for `net45` and `netstandard2.0` (NuGet registration API). [C]
  - The release notes say v3 "Dropped support for .NET < 4.5 and consequently old Unity builds (< 2019)". It adds `json.emptyarray()` / `json.isemptyarray()`, conditional breakpoints and multi-script debugging ([releases](https://github.com/moonsharp-devs/moonsharp/releases)). [C]
  - The repository has 92 open issues and 22 open PRs. [C]
- **Unity.** v3 adds a UPM package via a git URL (`?path=/interpreter#upm/v3.0`) plus a
  separate debugger package. The branches `upm/beta/v3` and `upm/beta/v3.0` exist
  ([README](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/README.md)). [C]
  Release 2.0.0.0 added "Unity WebGL support". [C]
- **The `luau` branch.** It is 11 commits ahead of `master`, all by Max Hayman between
  2026-07-30 and 2026-08-04, and **not merged**
  ([branch head ab172c2](https://github.com/moonsharp-devs/moonsharp/tree/ab172c2bf31d9aab779f4ee44b38efff98573ecd)). [C]
  - It adds Luau *syntax* behind `ScriptOptions.LuauFeatures`: compound assignment,
    `continue`, string interpolation, if-expressions, `//`, binary literals, digit
    separators, and type annotations. [C]
  - The type annotations are parsed and discarded:
    "MoonSharp has no type checker, so nothing here builds a tree ... Annotations
    therefore cost nothing at run time and cannot change what a script does"
    ([TypeAnnotation.cs @ ab172c2](https://github.com/moonsharp-devs/moonsharp/blob/ab172c2bf31d9aab779f4ee44b38efff98573ecd/src/MoonSharp.Interpreter/Tree/TypeAnnotation.cs)). [C]
  - So the branch lets authors *write* Luau-typed scripts that MoonSharp runs, and leaves
    type checking to the Luau toolchain (`luau-analyze`, `luau-lsp`). [U]
  - The same branch bumps the bytecode dump version, because adding an opcode renumbered
    the enum. [C]
- **64-bit integers.** Lua 5.2 has one number type, held as `double` in `DynValue`. Upstream
  has no exact int64. [C for Lua 5.2 semantics. U for the absence of any upstream extension.]
- **Reach.** Unity (Mono, IL2CPP, iOS, WebGL), .NET, and the browser through .NET WASM.
  There is no JVM path without a .NET process.
- **Fit.** This is the baseline. The revival matters for a fork: upstream fixes (pcall
  `__call`, label scoping, `tonumber` with a base, `__newindex` with multiple assignment)
  are worth tracking.

### 1.2 WattleScript

- **What it is.** A MoonSharp fork, "dual-language": Lua 5.2 plus its own "Wattle"
  language. It has a source generator for AOT interop and a `json` module
  ([README @ 8feb3c6](https://github.com/WattleScript/wattlescript/blob/8feb3c6540005f9dbe4a9e2572bc372f8adfa2b3/README.md)). [C]
- **Activity.** It has one active maintainer (CallumDev). The last 40 commits are spread
  over 2022-10 to 2026-08: 5 in 2026-04, 1 in 2026-06, 1 in 2026-08. [C]
- **Releases.** There are no release tags of its own; the tags are MoonSharp's. NuGet
  `WattleScript.Interpreter` 1.0.0 dates from 2022-10-10. [C]
- **Fit.** It reaches the same places as MoonSharp, and its packages are less maintained.
  It is useful only as a source of ideas, for example its source-generated interop.
  No as a dependency.

### 1.3 Lua-CSharp (nuskey8)

This engine was researched in depth elsewhere; fit points only.

- It is a Lua 5.2 interpreter, "Unity support (works with both Mono and IL2CPP)",
  netstandard2.1+, with no runtime IL generation. [C]
- Its default strings are UTF-16, which changes `#s` for non-ASCII text. Byte-string mode
  is opt-in
  ([README @ a8d84c7](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/README.md)). [C]
  That difference matters for cross-platform identity against any C-Lua host. [U]
- Lua `number` maps to `double`, `float` or `int`. There is no int64 integer subtype. [C]
- Release v0.5.8 came out on 2026-10-25, the latest commit is from 2026-10-27, and NuGet
  shows about 114k downloads. [C]
- Reach is the same as MoonSharp. No JVM.

### 1.4 UniLua, KopiLua, NeoLua

- **UniLua** is a managed Lua 5.2. Its last commit, 2022-06-27, fixed `%` differing from
  C Lua ([repo](https://github.com/xebecnan/UniLua)). Dead. [C]
- **KopiLua** was last published to NuGet in 2018. [C]
- **NeoLua** compiles Lua to the DLR, with 1.3.20-beta.1 on 2026-01-15. It relies on
  runtime code generation, so it does not suit IL2CPP or iOS. [C for the version, U for the
  IL2CPP claim, by design.]

---

## 2. Native C Lua with a Unity bridge

### 2.1 xLua (Tencent) and xLua-NG

- **xLua upstream.** It is native Lua 5.3.5 or LuaJIT, with a C# bridge and code generation.
  - Its last commit, on 2025-11-21, fixed the examples on Unity 6000
    ([59bf426](https://github.com/Tencent/xLua/commit/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3)). [C]
  - Its releases are build-config refreshes (`2.1.16_android_16kb`, `v2.1.16_with_ohos`). It
    has 278 open issues and 12 open PRs ([releases](https://github.com/Tencent/xLua/releases)). [C]
  - It is in maintenance mode. [U]
- **xLua-NG.** This independent fork lives at `/home/user/xlua-ng` (HEAD 3042ef6, 2026-10-28).
  - Its README: Lua 5.5.1 by default (5.4.9, 5.3.6, LuaJIT 2.1), lossless 64-bit integers,
    a Roslyn source generator, presets, Unity 2021.3 to 6000.6, IL2CPP and Mono, WebGL
    (Lua 5.5.1 only), and .NET/Mono outside Unity. [C]
  - Also from its README: "nothing is published: no release, no prebuilt plugins, no UPM
    package in a registry". [C]
  - Its JSON research recommends yyjson. It measured +38 to +104 KB on Android arm64
    depending on the features enabled (`docs/research/json-module.md`). [C]
- **Fit.** These give the strongest Unity leg for a "C Lua everywhere" plan:
  - cheap host calls through generated bindings;
  - real Lua 5.4/5.5 semantics, including integers;
  - only documented Unity mechanisms (P/Invoke, `__Internal`, source plugins).

  Neither reaches the browser or the JVM.

### 2.2 slua, toLua#, tolua_runtime

- [slua](https://github.com/pangweiwei/slua): last commit 2020-07-27. [C]
- [toLua#](https://github.com/topameng/tolua): last commit 2021-05-08. [C]
- [tolua_runtime](https://github.com/topameng/tolua_runtime): last commit 2019-08-19. [C]

All three are LuaJIT/Lua 5.1 based and dead. Not candidates.

### 2.3 NLua / KeraLua

- **KeraLua** is C# P/Invoke bindings for native Lua 5.4: "compatible with
  iOS/Mac/Android/UWP/.NET". Version 1.4.11 came out on 2026-10-19
  ([README @ 03ef9ff](https://github.com/NLua/KeraLua/blob/03ef9ffdbd839729023112311ec62bca4987dd15/README.md)). [C]
- **NLua** is a reflection bridge on top of KeraLua, version 1.7.9 from 2026-05-01. [C]
  - Its Unity section is a manual copy of netstandard2.0 DLLs plus the native libraries, and
    notes that "numbers will come back as `double`"
    ([README @ ef3efdb](https://github.com/NLua/NLua/blob/ef3efdb4c62d539ab0dd808c31e5f076eaa0dfee/README.md)). [C]
  - It ships `src/GenerateEventAssembly/CodeGeneration.cs` using Reflection.Emit, so event
    and delegate generation will not work on IL2CPP. [C for the file, U for exactly which
    paths fail.]
- **Fit.** KeraLua is a reasonable *plain .NET* leg for a native-Lua plan. NLua is not a
  good Unity bridge next to xLua-NG.

### 2.4 NuLua (nuskey8)

- **What it is.** "Unified Lua5.x/LuaJIT/Luau bindings for .NET and Unity", one
  `ILuaState` over Lua 5.1 to 5.5, LuaJIT and Luau, with allocation-light struct
  `LuaValue`s ([README @ 1d6d641](https://github.com/nuskey8/NuLua/blob/1d6d641786f5920fc9dd3ce2f7b8dbb5fb9bad42/README.md)). [C]
- **Status.** A preview: 0.1.0 on 2026-06-22, with commits up to 2026-10-27.
  - The README: "currently supports Windows/macOS/Linux only; iOS/Android/Web support is
    planned". [C]
  - The Unity section of the README is "TODO" (NuGet page). [C]
- **Fit.** Worth watching: one API over PUC Lua and Luau would make a backend swap cheap.
  It cannot ship to mobile today.

---

## 3. Luau embedded natively

- **Core.** [luau-lang/luau](https://github.com/luau-lang/luau) release 0.744 came out on
  2026-10-23, with weekly syncs. [C]
  - It is a C++ VM with gradual typing, a sandbox, and an interpreter tuned for JIT-less
    targets.
  - CMake has an official `LUAU_BUILD_WEB` option that builds `Luau.Web` with emscripten
    ([CMakeLists.txt @ 32c2808](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/CMakeLists.txt#L11)). [C]
- **64-bit integers.**
  - Luau numbers are doubles. A 64-bit `integer` value type now exists in the VM:
    `VM/src/lintlib.cpp` defines `int64_create`, `add`, `sub`, `mul`, `div`, `idiv`, `band`,
    unsigned compares and more, plus `i`-suffixed literals.
  - It is gated by the fast flag `LuauIntegerLibrary`
    ([lintlib.cpp @ 32c2808](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/lintlib.cpp),
    [linit.cpp](https://github.com/luau-lang/luau/blob/32c28089305f488f901aaf1dbf3fd1cb65fa3d11/VM/src/linit.cpp)). [C]
  - The RFC ([rfcs.luau.org/type-long-integer](https://rfcs.luau.org/type-long-integer.html))
    gives integers **only equality as an operator**. Arithmetic is `integer.add(a, b)` and so
    on, with no implicit conversion to or from `number`. [C]
  - For business rules on money or IDs this is exact but verbose.
  - It is incompatible with plain-Lua scripts. [U]
- **Unity.**
  - NuLua.Luau is a preview with desktop only (§2.4). [C]
  - [luau-dotnet](https://github.com/nuskey8/luau-dotnet) listed iOS arm64/x64, Android and
    wasm32, but its README now opens with "This library is archived. Use NuLua instead"
    (HEAD 4ce0a1d, 2026-06-23). [C]
  - No other maintained Unity binding turned up. [S]
- **Browser.**
  - `Luau.Web` is the official build target. [C]
  - [luau-web](https://github.com/xNasuni/luau-web) on npm, 1.5.1 from 2026-10-11, MIT, is
    a single-author binding over a Luau fork with "rewritten WASM execution API". Unpacked
    size is 2.1 MB. [C]
- **JVM.** [hollow-cube/luau-java](https://github.com/hollow-cube/luau-java), 1.2.1 from
  2026-08-02, MIT, is on Maven Central. It uses `java.lang.foreign` bindings generated with
  jextract, and the README says "**requires Java 25 or higher**". [C]
  That is a hard constraint for Spring Boot fleets on Java 17/21. [U]
- **Tooling.** This is the strongest in the survey:
  - `luau-analyze`;
  - [luau-lsp](https://github.com/JohnnyMorganz/luau-lsp), 1.70.1 on 2026-09-27, with a
    commit on 2026-10-28; [C]
  - TSTL can also emit Luau. [C]
- **Fit: partial.** One C++ core with official web support and a JVM binding gives real
  same-semantics reach. The Unity mobile bindings are not ready, int64 needs function-call
  syntax, and the Java 25 floor is a problem.

## 4. LuaJIT in Unity

- iOS: "the JIT compiler is disabled for iOS, because regular iOS Apps are not allowed to
  generate code at runtime" ([luajit.org/install.html](https://luajit.org/install.html)). [C]
- Consoles: the JIT and the FFI are disabled. [C]
- LuaJIT is distributed as source only (rolling). The latest commit is 2026-10-21. [C]
- The language is Lua 5.1, with no integer subtype. 64-bit values exist only as FFI cdata,
  which the console builds disable. [C for the console FFI, U for using cdata in rules.]
- I found no maintained WebAssembly port. The interpreter is hand-written assembly. [S]
- **Fit: no.** It cannot run the same script in browsers, and it has no exact-integer
  story.

## 5. Lua in the browser without .NET

- **wasmoon.** Official Lua (a `lua/lua` git submodule) compiled to wasm, with a JS/TS API.
  - Published size: **393 kB raw, 130 kB gzipped** vs fengari's 214 / 69 kB.
  - Published speed: heap sort of 2k numbers ×10, **15.267 ms vs 389.923 ms** for fengari.
    The README gives no machine or browser for these numbers
    ([README @ 4cf40cb](https://github.com/ceifa/wasmoon/blob/4cf40cb6dc335bbdecf67fb8027dfb176146be34/README.md)). [C]
  - **Activity is the weak point.** npm `wasmoon` 1.16.0 was published 2023-12-08, and the
    last commit was 2025-01-18. [C]
  - A Lua 5.1 fork, `wasmoon-lua5.1` 1.18.12 from 2026-10-08, is active. [C]
  - Building your own emscripten Lua is small work, because Lua is plain C. [U]
- **lua-wasm-bindings** (TypeScriptToLua org), 0.5.3 from 2026-01-26: "only includes the
  bindings used to test TypescriptToLua". [C] Reference only.
- **fengari.** Lua 5.3 rewritten in JS, 0.1.5 from 2025-12-26.
  - "floats are doubles, but integers are 32 bits" ([README @ ac8c387](https://github.com/fengari-lua/fengari/blob/ac8c3872509a56fb4bd9c1ef853de01f391c058b/README.md#integers)). [C]
  - It has no weak tables, no `__gc` and no `collectgarbage`. [C]
  - **Fit: no**, because of the integers.
- **Lua inside the .NET WASM runtime.** C sources can be linked into a .NET WebAssembly app
  through `NativeFileReference`, a documented .NET mechanism
  ([learn.microsoft.com](https://learn.microsoft.com/aspnet/core/blazor/webassembly-native-dependencies)). [S]
  That would let KeraLua-style bindings run in the existing .NET WASM host, but it keeps the
  .NET download. [U]

## 6. Lua on the JVM

- **LuaJ.** Pure Java, Lua 5.2.
  - [luaj/luaj](https://github.com/luaj/luaj): last commit 2020-04-01. [C]
  - [wagyourtail/luaj](https://github.com/wagyourtail/luaj): last commit 2024-03-04. [C]
  - No exact int64 (Lua 5.2). [U] Not a candidate.
- **Cobalt** (CC: Tweaked).
  - It is LuaJ-derived, "(mostly) compliant implementation of Lua 5.2", backports from the
    5.3/5.4 libraries, and is active (commits 2026-10-16;
    [Codeberg](https://codeberg.org/cc-tweaked/Cobalt)).
  - Its README says: "**Using: Don't.** ... There is no guarantee of API stability". [C]
  - Not a candidate.
- **Rembulan.** Lua 5.3 in pure Java, last commit 2016-10-25. Dead. [C]
- **LuaJava** ([gudzpoz/luajava](https://github.com/gudzpoz/luajava)). JNI wrapper over the
  real C Lua.
  - The README table at HEAD lists Lua 5.1.5, 5.2.4, 5.3.6, **5.4.8, 5.5.1**, LuaJIT, and a
    LuaJ fork.
  - Natives cover Windows, Linux, macOS and Android on ARM and x86/x64. Java 8+, MIT.
  - Release 4.1.0 came out on 2026-01-03, with commits to 2026-08-16
    ([README @ 2a1e61d](https://github.com/gudzpoz/luajava/blob/2a1e61d126aa6928d412338adba08df7a19a8803/README.md)). [C]
  - Whether 4.1.0 itself bundles 5.5.1 is unverified. [U]
  - **Fit.** This is the JVM leg of a "C Lua everywhere" plan: in-process, exact int64,
    same C core as Unity and browser. The cost is native libraries in the server image.
- **Chicory plus Lua compiled to wasm.**
  [Chicory](https://github.com/dylibso/chicory) is a "JVM native WebAssembly runtime ... zero
  native dependencies or JNI", 1.7.5 from 2026-03-24, Java 11+, Apache-2.0. [C]
  - It has three modes: interpreter, runtime compiler (ASM, reflection), and **build-time
    compiler** to plain `.class` files
    ([execution_modes.md](https://github.com/dylibso/chicory/blob/e2e2e4058f49fbffeffc5ea92c54b41534cb45d3/docs/docs/usage/execution_modes.md)). [C]
  - Functions above the JVM's 64 KB method limit fall back to the interpreter, and the
    build fails unless that fallback is allowed
    ([build-time-compiler.md](https://github.com/dylibso/chicory/blob/e2e2e4058f49fbffeffc5ea92c54b41534cb45d3/docs/docs/usage/build-time-compiler.md)). [C]
  - Lua's `luaV_execute` is one large function, so it may hit that fallback. [U]
  - The pattern is proven for other C interpreters: quickjs4j runs QuickJS-as-wasm through
    Chicory-compiled bytecode. [S]
  - **No Lua-on-Chicory project was found.** [S]
  - **Fit.** It would put the *same `.wasm` file* in the browser and on the JVM, pure Java
    with no sidecar. It is unproven and worth a one-day spike.
- **LuaTruffle** (GraalVM) is named in luau-java's README as prior art. It is old and
  inactive. [U]

## 7. Typed-Lua tooling

| Tool | What it gives | Targets | Int64 notion | State |
|---|---|---|---|---|
| LuaCATS / [lua-language-server](https://github.com/LuaLS/lua-language-server) | `---@class`, `---@type` annotations in comments; LSP and checks; runtime-neutral | any Lua dialect, configurable version (5.1 to 5.5, LuaJIT) | `integer` annotation [U] | 3.19.2 on 2026-10-13, commits 2026-10-20 [C] |
| [Teal](https://github.com/teal-language/tl) | typed dialect compiled to Lua | "Lua 5.1-5.4, including LuaJIT" (README) [C] | has an `integer` type [U] | v0.24.8 on 2025-10-13; commits to 2026-10-26 [C] |
| Luau checker / luau-lsp | gradual types, best inference | Luau only, or MoonSharp's `luau` branch, which strips types | `integer` (flagged) [C] | very active [C] |
| [TypeScriptToLua](https://github.com/TypeScriptToLua/TypeScriptToLua) | author in TypeScript, emit Lua | `universal`, 5.0, 5.1, **5.2**, 5.3, 5.4, 5.5, JIT, Luau ([CompilerOptions.ts](https://github.com/TypeScriptToLua/TypeScriptToLua/blob/0c3a141280d6796c1a1906210622bda8ea4180a2/src/CompilerOptions.ts#L63)) [C] | none: TS `number` [U] | 1.37.1 on 2026-07-09 [C] |

TSTL's documented caveats are relevant to identical results
([docs/caveats](https://typescripttolua.github.io/docs/caveats)): [C]

- `0`, `""` and `NaN` are truthy;
- `null` and `undefined` both become `nil`, which deletes keys;
- `table.sort` is unstable;
- object key iteration order differs from JS.

For a team that also writes TypeScript for the browser, TSTL gives first-class editor
tooling for authoring. The runtime semantics stay Lua's. [U]

## 8. Cross-cutting points

- **App Store and Play policy is engine-independent.**
  - Apple 2.5.2 forbids downloaded code "which introduces or changes features or
    functionality of the app"
    ([App Review Guidelines](https://developer.apple.com/app-store/review/guidelines/)). [C]
  - Forum quotes of the Developer Program License Agreement allow interpreted code that does
    not change the app's primary purpose ([Expo forum](https://forums.expo.dev/t/expo-updates-and-apples-app-store-review-guidelines/64495)). [S]
  - Google Play exempts "code that runs in a virtual machine or an interpreter" with indirect
    API access, but requires that interpreted code (it names Lua) loaded at run time "must
    not allow potential violations"
    ([Device and Network Abuse](https://support.google.com/googleplay/android-developer/answer/9888379)). [C]
  - Rules, eligibility and offer selection that stay inside the app's advertised purpose fit
    both policies. The choice of engine does not change this. [U]
- **Determinism traps in C Lua.**
  - Lua 5.4 seeds string hashing from `time(NULL)` plus addresses, unless `luai_makeseed`
    is defined (`build/lua-5.4.9/src/lstate.c:54-80` in xLua-NG, verbatim upstream). [C]
    So `pairs` order over string keys can differ **between runs**, not just between
    platforms. [U]
  - Lua 5.5 moved the seed to `luaL_makeseed` in `lauxlib.c:1150-1198`. [C]
  - A "same C core everywhere" plan must pin the seed (and the Lua version) in every host,
    or the scripts must never depend on `pairs` order. [U]
  - `libm` (`sin`, `exp`, `pow`) and `printf`-based number formatting can differ across
    libc and emscripten. [U]
  - Managed engines avoid both problems inside one implementation. [U]
- **JSON.** None of the C Luas ships JSON.
  - Options are a pure-Lua library (identical everywhere and slow) or a C library per host
    (xLua-NG's yyjson study; lua-cjson).
  - MoonSharp and WattleScript ship JSON, without int64. [C]
- **Debugging.**
  - MoonSharp has a VS Code DAP debugger, improved in v3. [C]
  - Native Lua in Unity usually relies on third-party debuggers such as EmmyLua or LuaPanda.
    [S]
  - The browser and JVM legs would each need their own. [U]

---

## Verdict for our need

**Question.** Is there a Lua option that already runs the SAME script on Unity IL2CPP, the
browser and the JVM, better than a managed C# engine + .NET WASM + a .NET worker for Java?

**Answer: no turnkey option exists.** One alternative is a **partial fit** and worth a
spike. Everything else is **no**.

| Option | Verdict | Why |
|---|---|---|
| **PUC Lua 5.4/5.5 C core in every host**: xLua-NG in Unity, an emscripten build or wasmoon in the browser, LuaJava (JNI) or Chicory on the JVM, KeraLua in .NET | **Partial: the strongest alternative** | One C source gives the same semantics by construction, with real int64. The browser leg is about 130 kB gz [C], against the .NET WASM runtime. The JVM runs it in-process, with no worker. Costs: 4 binding layers to build and keep in lockstep (exact version, `luai_makeseed`, number formatting); xLua-NG is unpublished; wasmoon's npm is from 2023; JNI natives on servers, or an unproven Chicory build; no single debugger; JSON per host. |
| **Luau everywhere**: NuLua / luau-dotnet, `Luau.Web` / luau-web, luau-java | **Partial, later** | Best types and tooling, one C++ core, official web target. Today: Unity mobile bindings are preview or archived, luau-java needs Java 25+, int64 is a flagged library type with no operators, and it is not Lua-compatible for existing scripts. Re-check when NuLua ships iOS and Android. |
| Managed C# Lua (MoonSharp fork, Lua-CSharp, WattleScript) | **Current baseline; still the only single-implementation setup** | Identical results are trivial because there is one implementation. Costs: no JVM without .NET, and a heavy browser download. MoonSharp upstream is active again (v3 beta, fixes in 2026-10), so keep merging from it. |
| LuaJIT | No | No browser, no JIT on iOS, Lua 5.1, no integers. |
| fengari | No | 32-bit integers. |
| LuaJ, Cobalt, Rembulan | No | Lua 5.2 doubles, dead, or "Don't use". |
| slua, toLua#, UniLua | No | Dead. |
| xLua upstream | No (Unity only) | Maintenance mode. xLua-NG supersedes it for this need. |

**Recommended next step**, if the JVM worker or the browser download is the pain point: a
two-day spike. Build Lua 5.5.1 to wasm once, with a fixed hash seed. Run the conformance
fixtures:

- in the browser (plain JS loader);
- on the JVM through Chicory's build-time compiler, recording whether `luaV_execute` falls
  back to the interpreter;
- through LuaJava as the JNI control.

Compare the results with the C# engine on the same fixtures. That turns the [U] items above
(determinism, Chicory speed, size) into measurements. Typed authoring is independent of
this choice: LuaCATS works for either core today. Teal and TSTL can target 5.2 or 5.4/5.5.
Luau types would need the MoonSharp `luau` branch or a Luau core.
