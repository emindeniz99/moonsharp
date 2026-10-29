# Changelog

Notable changes of this fork, newest first, in the format of
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The fork has no release
yet; the "Unreleased" section covers everything since the fork point, upstream
commit `fbd0367` (2026-10-14, after MoonSharp `v3.0.0-beta.1`).

Each change that a script or a host can observe, with what it was before and who
is affected, is in [docs/MIGRATING.md](docs/MIGRATING.md). The measurements behind
the performance work are in [docs/ROADMAP.md](docs/ROADMAP.md) ("Done").

## Unreleased

### Added

- Hosting ([docs/hosting.md](docs/hosting.md)): a JSON host contract with a .NET
  CLI worker, a browser package (.NET WebAssembly, TypeScript API, Web Worker) in
  an interpreter build and an AOT build, a Java client with a worker process and
  in-process CoreCLR and NativeAOT backends, per-call instruction and time limits,
  and `moonsharp-host compile` for bytecode with source locations.
- A conformance corpus in `src/Hosting/fixtures` that every host runs: .NET from
  source, from bytecode and under a decimal-comma culture, Chromium, Firefox,
  WebKit and the three Java transports, each against its own known-failures file.
- Exact 64-bit integers as opt-in `int64` and `uint64` modules, CLR `long` and
  `ulong` in both directions, and exact JSON on request
  ([docs/int64.md](docs/int64.md)).
- A Roslyn source generator that writes userdata descriptors at compile time,
  shipped in the Unity package ([docs/source-generator.md](docs/source-generator.md)).
- Typed calls from C# to Lua (`Closure.Call<T1..T4, TResult>`) and Lua functions
  converted to delegates (`Closure.ToDelegate`), without boxing.
- `Closure.SetUpvalue`, and `CallbackArguments` readers (`TypeAt`, `NumberAt`)
  with `ScriptExecutionContext.Return` so a C# function can read arguments and
  return values without a `DynValue`.
- LuaCATS definitions generated from a configured `Script`
  (`moonsharp-luacats`), and documented explicit type registration
  ([docs/luacats.md](docs/luacats.md), [docs/registration.md](docs/registration.md)).
- `package.preload`, `package.path`, `package.searchers`, `package.searchpath`,
  `package.cpath` and `package.loadlib` as in Lua 5.2; `loadstring`,
  `table.maxn` and `math.log10`; `collectgarbage` with Lua 5.2's options.
- Benchmarks in `src/DevTools/Benchmarks` (lifecycle, A/B comparison of two
  builds, workloads shared with native Lua) and size tracking in `tools/size`.
- CI for NativeAOT, the hosts and the corpus, LuaCATS, and a Unity editor and
  IL2CPP matrix for 2021.3, 2022.3 and 6000.0 (waiting for the Unity secrets).

### Changed

- Values live in the VM as structs, with struct call frames, Lua-style upvalues
  and stacks that grow on demand: `new Script()` dropped from 2.32 MB to under
  200 KB. Every `DynValue` the VM hands out is read-only, and
  `Closure.GetUpvalue` returns a read-only copy.
- Tables have an array part, store values without a `DynValue` per entry, and
  keep insertion order for `pairs` and JSON.
- Fused instructions, inline caches for fields, globals and metamethods, and
  library functions called without a call frame. The dump format is now 0x154;
  0x152 and 0x151 dumps still load.
- Lua patterns run a port of Lua 5.2.4's matcher; `string.format` follows Lua 5.2
  and C's printf with exact digits on every runtime.
- Fewer allocations per call: C# functions read their arguments on the value
  stack, `...` and coroutine yields copy nothing, and generated interop calls
  allocate nothing.
- Lua 5.2 semantics where MoonSharp differed: empty result lists, `and`/`or`
  results, operator precedence, multiple assignment, `%` with negative divisors,
  `>` and `>=`, `tonumber`, `math.fmod`/`modf`, `bit32`, `load`, `io.open` modes.
- Overload resolution: a fractional number picks a floating-point overload, an
  exact type match beats a custom converter, and derived types expose their base
  classes' static members.
- Number text, JSON and string case no longer depend on the process culture.
- The Unity package requires Unity 2021.3. NativeAOT counts as an AOT platform,
  and trimmed apps keep the standard library and the members of registered types.

### Fixed

- A `Script` stays usable after a failed call or a stack overflow.
- Script calls no longer end the host process with a CLR exception (`pcall()`,
  missing files, `table.unpack`, out-of-range `os.time` and `string.rep`, and
  others); they raise Lua errors.
- Metamethods and C# recursion stop at 200 levels with an error, and long
  library calls (`gsub`, `string.rep`, `table.sort`) honour the interrupt limits.
- Keys set to nil are forgotten, and hosts no longer see them.
- `json.parse` reads nested arrays and string escapes; `json.serialize` keeps
  every key of a mixed table.
- The `io` module works on browser WebAssembly; standard streams set after a
  script is created are used.
- Binary dumps keep source locations.

### Removed

- The public `LuaBase`, `CharPtr` and `LuaLBuffer` classes (KopiLua's string
  helpers).
