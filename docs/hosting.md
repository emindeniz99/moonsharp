# Running the same scripts on .NET, in the browser and on the JVM

`src/Hosting` lets one set of trusted Lua scripts run in three places with the
same results:

| Host | How it runs MoonSharp | Entry point |
|---|---|---|
| .NET (CLI, servers, tests) | in process | `MoonSharp.Hosting.ScriptHost`, `moonsharp-host` CLI |
| Browser | .NET WebAssembly in the page or a Web Worker | `src/Hosting/browser`, TypeScript API `moonsharp.ts` |
| Java / Spring Boot | a persistent `moonsharp-host worker` process per worker slot (default) | `src/Hosting/java`, `MoonSharpWorker`, `MoonSharpPool` |
| Java, in process (Linux x64, with limits) | CoreCLR or a NativeAOT library loaded into the JVM through JNI | `InProcessMoonSharp`, `src/Hosting/native` |

All three run the same interpreter assembly and the same `ScriptHost` class.
Only the transport differs. The shared fixtures in `src/Hosting/fixtures` prove
that the hosts agree.

Unity uses MoonSharp directly and does not need this layer. To check Unity
against the other hosts, run the fixtures through `ScriptHost` inside a player
(not done yet; see the status table below).

## Tools

Run `mise install` in the repository root before building. It installs the
versions pinned in [`mise.toml`](../mise.toml): the .NET 10 SDK with the .NET 8
runtime, Java 17, Maven, Node and CMake. CI installs the same versions with
`jdx/mise-action`. The NativeAOT build also needs clang and zlib, which come
from the distribution (`sudo apt-get install clang zlib1g-dev`), as in CI.

```sh
mise install
dotnet build -c Release src/Hosting/MoonSharp.Hosting.Cli
dotnet src/Hosting/MoonSharp.Hosting.Cli/bin/Release/net8.0/moonsharp-host.dll test src/Hosting/fixtures
```

`test` runs the conformance corpus in `src/Hosting/fixtures` and checks the
results against `known-failures/dotnet.json`; the browser smoke test and the
Java conformance tests do the same for their hosts. The corpus format and the
known-failures rules are in [its README](../src/Hosting/fixtures/README.md).

Run the commands in a shell where mise is activated, or prefix them with
`mise exec --`.

## The contract (version 1)

Every call takes and returns JSON text. A result is an envelope:

```json
{ "ok": true,  "value": 42, "output": "printed text\n", "timing": { "runMs": 0.12 } }
{ "ok": false, "error": { "kind": "runtime", "message": "config:34: boom", "chunk": "config",
                          "line": 34, "stack": ["fail config:(34,1-20)"] }, "output": "" }
```

`kind` is one of:

- `syntax`: the source does not compile.
- `runtime`: the script raised an error.
- `input`: the arguments are not valid for the contract (bad JSON, an integer beyond 2^53).
- `output`: the result cannot be expressed as JSON (a function, a cycle, NaN).
- `host`: an unknown module or function, or an exception from host code.
- `interrupted`: the call ran past the context's instruction or time limit (see "Limits" below).

### Operations

| Operation | Meaning |
|---|---|
| `validate(chunk, source)` | Compile only. Use before publishing a script. |
| `load(moduleId, source)` | Run a module and keep its return value. `require(moduleId)` returns it too. Loading the same id again replaces the module and drops cached functions. |
| `invoke(moduleId, function, argsJson)` | Call a function of a loaded module. The compiled function is cached. |
| `run(chunk, source, inputJson)` | Compile and run once. If the chunk returns a function, call it with the input. |
| `unload(moduleId)`, `dispose(context)` | Free a module or a whole context. |
| `create(context, options)` | Create a context, replacing one of that name. Contexts are also created on first use, without limits; `create` is for options. With `int64` the context has the `int64` and `uint64` modules ([int64.md](int64.md)); `instructionLimit` and `timeLimitMs` limit each call (see "Limits"). |

A context is one `Script` (one Lua state). Globals and modules are per context.
Every transport creates it through `ScriptHost.Create(int64)`, with
`ScriptHost.PortableModules` and, when asked, `CoreModules.Int64`. Returning an
int64 value to the host is an `output` error for now; return
`tostring(x)` or `json.serialize` it.
A context is not thread-safe: the browser has one thread per runtime, and the
Java pool gives each worker process its own contexts.

### Limits

A context can stop a call that runs too long. Both limits are off (0) unless
`create` sets them, and each applies to every call on its own:

| Option | Stops a call after | Same on every host |
|---|---|---|
| `instructionLimit` | that many VM instructions | yes: the count does not depend on the machine |
| `timeLimitMs` | that many milliseconds | the outcome, not the moment |

The VM checks both every 10,000 instructions
(`ScriptOptions.InterruptHandler`), so a call stops at the first check past
the limit. The call then returns an `interrupted` error that names the chunk
and line where it stopped:

```json
{ "ok": false, "error": { "kind": "interrupted", "message": "instruction limit of 1000000 exceeded",
                          "chunk": "rules", "line": 7, "stack": ["M.spin rules:(7,2-15)"] }, "output": "" }
```

`pcall`, `xpcall` and `coroutine.resume` cannot catch it, so a script cannot
keep running after its budget is spent, and the context stays usable for the
next call. A call is one entry into Lua: `load` runs the module body, `invoke`
the function, and `run` the chunk and then, with its own budget, the function
the chunk returns. The time limit covers the whole operation.

What the limits do not stop: a single long library call is not interrupted,
because the check runs between VM instructions. `string.rep('a', 2e7):gsub('a',
'b')` runs for 2.5 s in one call, and a 100 ms limit stops the script only
after it returns. Memory is not limited either.

Which limit to choose: an instruction limit is deterministic, so a script
that passes in tests passes in production on any machine. A numeric `for`
loop costs 10 instructions per iteration, and 1,000,000 instructions of such a
loop take 6 ms on the machine in "Status" (warm worker, median of 30); library
calls cost more time per instruction. A time limit bounds latency whatever the
script does between checks, and a context can set both.

The stack guard needs no option: recursion that passes through C# code (a
`gsub` replacement function, a `table.sort` comparator, a C# callback that
calls Lua) fails with a `runtime` error "C stack overflow" after 200 nested
calls, or earlier when the thread's stack runs low, and `pcall` catches it as
in Lua 5.2. The `limits` suite in the corpus checks both on every host.

### Value mapping

| JSON | Lua | Back to JSON |
|---|---|---|
| `true`, `false` | boolean | same |
| string | string (UTF-16, see below) | same |
| integer within ±2^53 | number | integer |
| integer beyond ±2^53 | **rejected** with kind `input` | n/a |
| other number | number (double) | shortest round-trip text, for example `0.30000000000000004` |
| object | table | object; number keys become strings |
| array | table with a marker metatable | array, including an empty input array |
| `null` in an object or as an argument | nil | omitted or `null` |
| `null` inside an array | `json.null()` | `null` |

Lua tables with keys 1..n and nothing else become arrays. An empty table becomes
`{}`. Return `json.emptyarray()` (built into MoonSharp) for `[]`. Fields set to
nil do not count, so a table emptied with `table.remove` is still an array.

A string with an unpaired surrogate (half of a character outside the BMP, as
`('👋'):sub(1, 1)` gives) is an `output` error. UTF-8 cannot encode it, and
System.Text.Json cannot read its `\u` escape, so each transport would deliver
something different.

**Large integers.** MoonSharp follows Lua 5.2: every number is a double, which
holds integers exactly only up to 2^53. The host refuses larger integer literals
instead of rounding them. Send 64-bit IDs as strings. The callers help keep the
text exact: the TypeScript API writes `bigint` values as exact literals, and the
Java client reads and writes numbers as `BigInteger`/`BigDecimal`. A rejected
value is better than a silently different ID.

**Strings.** MoonSharp strings are .NET strings: `#s` counts UTF-16 code units,
not UTF-8 bytes. `string.byte` returns the code unit when it is 255 or less and
63 (`?`) otherwise; `string.unicode` returns the code unit. A script that hashes
non-ASCII text gets a different value than standard Lua, but the same value on
every MoonSharp host.

## Java: worker protocol

`moonsharp-host worker` reads one JSON request per line on stdin and writes one
reply per line on stdout, in order:

```json
{"id":1,"op":"load","context":"c1","module":"offers","source":"..."}
{"id":1,"result":{"ok":true,"value":{},"output":"","timing":{"runMs":1.9}}}
{"id":2,"op":"invoke","context":"c1","module":"offers","function":"select_offers","args":[...]}
```

`args` may also be sent as text in `argsJson`, and `input` (for `run`) as
`inputJson`.
`{"id":3,"op":"create","context":"c2","int64":true,"instructionLimit":1000000,"timeLimitMs":100}`
creates a context with options. Run several workers for parallel
calls: `MoonSharpPool` keeps a fixed set, loads every module into each of them,
and replaces a worker that dies.

The worker is the default. The next section describes the in-process
alternative, when to use it and why the worker stays the default.

## Java: in process

`InProcessMoonSharp` runs MoonSharp inside the JVM, without a pipe. It has the
same Java API as `MoonSharpWorker` (both implement `MoonSharpHost`), returns the
same `HostResult` envelopes and passes the same fixtures. There are two
backends:

- **CoreCLR**: `libmoonsharp_jni.so` finds `hostfxr` through `nethost`, starts
  the .NET 8 runtime from `MoonSharp.Hosting.Native.runtimeconfig.json` and binds
  the `[UnmanagedCallersOnly]` methods of `MoonSharp.Hosting.Native.dll`
  (`load_assembly_and_get_function_pointer`). This is the documented
  [native hosting](https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting) API.
- **NativeAOT**: the same managed project, published with `PublishAot`, is a
  self-contained `MoonSharp.Hosting.Native.so` that exports the same methods as C
  functions. `libmoonsharp_jni.so` loads it with `dlopen`. No .NET installation
  is needed.

```text
Java (InProcessMoonSharp, NativeBridge)
  -> JNI: libmoonsharp_jni.so (src/Hosting/native, C)
       -> hostfxr + CoreCLR -> MoonSharp.Hosting.Native.dll   or
       -> MoonSharp.Hosting.Native.so (NativeAOT)
            -> Exports.cs -> ScriptHost (the same class every other host uses)
```

Strings cross the boundary as UTF-8 byte arrays with a length. JNI's own string
functions use "modified UTF-8", which encodes characters outside the BMP
differently, so the bridge does not use them. Results are allocated by .NET and
freed by the shim after it copies them into a Java `byte[]`.

### Building

```sh
# CoreCLR component: MoonSharp.Hosting.Native.dll, its runtimeconfig.json and the MoonSharp assemblies
dotnet build -c Release src/Hosting/MoonSharp.Hosting.Native
# NativeAOT library (needs clang and zlib1g-dev); NativeAot, not PublishAot, see the csproj
dotnet publish -c Release -r linux-x64 -p:NativeAot=true src/Hosting/MoonSharp.Hosting.Native
# JNI shim; finds jni.h through JAVA_HOME and nethost in the SDK's app host pack
cmake -S src/Hosting/native -B src/Hosting/native/build -DCMAKE_BUILD_TYPE=Release
cmake --build src/Hosting/native/build
# Tests: worker, CoreCLR and NativeAOT fixtures, one JVM per test class
cd src/Hosting/java && mvn test -Dmoonsharp.inprocess.required=true
```

Without `-Dmoonsharp.inprocess.required=true`, the in-process tests are skipped
when the native files are missing, so `mvn test` still works with only the
worker built.

```java
MoonSharpHost host = InProcessMoonSharp.nativeAot(
    Path.of("/opt/moonsharp/libmoonsharp_jni.so"),
    Path.of("/opt/moonsharp/MoonSharp.Hosting.Native.so"));
// or InProcessMoonSharp.coreClr(jniLib, Path.of(".../MoonSharp.Hosting.Native.dll"), null)
host.load("rules", "eligibility", source).valueOrThrow();
HostResult r = host.invokeJson("rules", "eligibility", "eligible", "[{\"level\":12}]");
```

### What to ship

| | Worker | In process, CoreCLR | In process, NativeAOT |
|---|---|---|---|
| Files | `moonsharp-host.dll` + 2 MoonSharp DLLs + runtimeconfig | `libmoonsharp_jni.so` (81 KB) + `MoonSharp.Hosting.Native.dll`, `.runtimeconfig.json`, `.deps.json` + `MoonSharp.Hosting.dll`, `MoonSharp.Interpreter.dll` (400 KB) | `libmoonsharp_jni.so` (81 KB) + `MoonSharp.Hosting.Native.so` (5.5 MB) |
| .NET runtime on the host | yes, .NET 8 (`dotnet` on `PATH`) | yes, .NET 8, found through `DOTNET_ROOT`, the default install locations or `/etc/dotnet`; or pass `dotnetRoot` | no |
| Native dependencies | ICU (as any .NET app) | libc, libstdc++, ICU | libc, libm; ICU is loaded at run time |
| Platforms | any with .NET 8 | Linux x64 only (built and tested); macOS needs a `.dylib` build, Windows a `wchar_t` port of the shim | Linux x64 only; one `.so` per OS and architecture |

The JNI shim links `nethost` statically, so no `libnethost.so` is shipped.
Setting `InvariantGlobalization` in the native project would drop ICU; the
fixtures already pass with invariant globalization on the CLI, but the
in-process builds measured here keep the default.

### Threads and contexts

A context is not thread-safe. Every call takes the context's lock on the .NET
side, so two Java threads using one context run one after the other, and
threads with their own contexts run in parallel. Without the lock MoonSharp
rejects the second thread ("Cannot enter the same MoonSharp processor from two
different threads"); `InProcessBehaviourTest` checks this. A call runs on the
calling Java thread and its stack. .NET attaches the thread on its first call.

One process can load only one backend, once. `close()` drops the instance's
contexts, but the .NET runtime stays loaded until the JVM exits.

### What changes when MoonSharp shares the JVM

Measured with `CrashProbe` (test sources), which runs each case in a child JVM,
on 2026-10-28 with Temurin 17.0.20.1, .NET 8.0.32 and this branch (the native
stack guard and the limits in "The contract"). The JVM's threads have the
default 1 MB stack unless the case says otherwise.

| Case | Worker | In process, CoreCLR | In process, NativeAOT |
|---|---|---|---|
| .NET exception that is not a Lua error (`string.rep('x', 2^31)`) | `host` error envelope | same | same |
| Deep Lua recursion (`local function f(n) return 1 + f(n + 1) end`) | `runtime` error "stack overflow"; the context keeps working | same | same |
| Recursion through a CLR callback (`string.gsub(s, '.', f)` where `f` calls `gsub` again), on a 1 MB and an 8 MB Java thread | `runtime` error "C stack overflow"; the context keeps working | same | same |
| The same through a `table.sort` comparator | same | same | same |
| Script that never returns, no limits, then `Thread.interrupt()` | the thread stays blocked; `close()` kills the worker and frees it | the thread keeps running; only ending the JVM stops it | same as CoreCLR |
| The same script with a 100 ms time limit | `interrupted` error after 122 ms; the context keeps working | after 126 ms | after 115 ms |
| The same script with a 1,000,000 instruction limit | `interrupted` error after 31 ms | after 24 ms | after 19 ms |
| One long library call under a 100 ms time limit (`gsub` over 20 million characters) | returns normally after 2.8 s: the limit is checked only between VM instructions | after 3.1 s | after 3.3 s |
| Allocation without bound, no limits, .NET heap capped at 256 MB (`DOTNET_GCHeapHardLimit=10000000`) | `host` error `System.OutOfMemoryException` after 1.2 s; the context keeps working | same, after 1.5 s | same, after 1.2 s |
| The same without a heap cap | not run (the worker process grows, not the JVM) | not run | the JVM process grew to 2.6 GB in 12 s before the probe stopped it |
| SIGTERM / SIGINT to the JVM | shutdown hooks run (exit 143 / 130) | shutdown hooks run, because the shim restores the JVM's SIGTERM handler (see below) | shutdown hooks run |
| SIGQUIT (thread dump) | works | works | works |
| Java `NullPointerException`s after .NET is loaded (the JVM raises them from SIGSEGV) | work | work (CoreCLR's handler passes signals it does not own to the JVM's) | work |

`InProcessBehaviourTest` also runs the `gsub` recursion on a Java thread with a
256 KB stack: `RuntimeHelpers.EnsureSufficientExecutionStack` sees the JVM
thread's real stack bounds on both backends, and the call fails with "C stack
overflow" before the 200-level limit.

Before the stack guard (measured on this machine with Java 21 and .NET 8.0.33),
the `gsub` recursion killed the worker process (Java got a `WorkerException` and
the pool started a new worker) and ended the whole JVM in process: SIGABRT,
exit 134 with CoreCLR, SIGSEGV, exit 139 with NativeAOT, with no `hs_err` file.

`DOTNET_GCHeapHardLimit` is read as hexadecimal. CoreCLR also accepts a `0x`
prefix; NativeAOT ignores the setting with one, which is how the uncapped
NativeAOT row above was found. In a container with a memory limit, .NET caps
its heap at 75 % of that limit by default
([GC settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-hard-limit)),
and in process that limit also has to hold the Java heap.

**Signals.** CoreCLR installs handlers for SIGSEGV, SIGBUS, SIGFPE, SIGILL,
SIGTRAP, SIGINT, SIGQUIT, SIGTERM and SIGRTMIN, and ignores SIGPIPE. Its SIGTERM
handler shuts the process down through .NET and does not call the previous
handler, so the JVM exited with code 143 without running its shutdown hooks
(Spring Boot's graceful shutdown would not run). The shim saves the JVM's
SIGTERM handler before starting the runtime and puts it back afterwards; .NET's
`ProcessExit` therefore does not run on SIGTERM, which nothing here uses.
NativeAOT installs handlers only for SIGSEGV and SIGFPE and ignores SIGPIPE.
`-Xcheck:jni` reports the replaced handlers and suggests `libjsig`; with
`LD_PRELOAD=$JAVA_HOME/lib/libjsig.so` everything above behaves the same, except
that a CoreCLR stack overflow ends the JVM with SIGSEGV instead of SIGABRT.

### NativeAOT and trimming

MoonSharp registers its standard library by reflection. Before `master` gained
the interpreter's embedded ILLink descriptor (`c3fb9ca`, fix(trim): keep the
standard library when an app is trimmed), NativeAOT trimmed those methods away
and `load` failed with
`require is not available: enable CoreModules.LoadMethods`, the same failure as
in the browser; rooting the whole assembly (`TrimmerRootAssembly`) fixed it.
With the descriptor no root is needed: the 24 fixtures and the 10 behaviour
tests pass, and the library shrinks from 6.6 MB (rooted) to 5.5 MB.

The build reports trim and AOT warnings, all in CLR interop code that the JSON
contract never reaches: `FrameworkClrBase` reflects over arbitrary types
(IL2075, IL2026 for `Assembly.GetTypes`), `TableConversions` builds
`List<T>`/`Dictionary<K,V>` with `MakeGenericType` and `Activator` (IL2055,
IL2067, IL3050), `ExtensionMethodsRegistry` calls `MakeGenericMethod` (IL2060,
IL3050), and `ValueTypeDefaultCtorMemberDescriptor` and `PropertyTableAssigner`
create values from a `Type` (IL2072).

### The unit suite under NativeAOT

MoonSharp's unit suite also runs as a NativeAOT executable, as a stand-in for
IL2CPP until Unity CI runs: neither can generate code at run time, both have
only the generic instantiations the build saw, and both strip the class library.
The `nativeaot-unit` job in `ci.yml` runs it:

```sh
# needs clang and zlib1g-dev; builds the suite for JIT and NativeAOT and runs both
src/TestRunners/DotNetCoreTestRunner/nativeaot.sh
```

The script fails on a NativeAOT failure that
`src/TestRunners/DotNetCoreTestRunner/nativeaot-known-failures.txt` does not
list (the list is empty), on a listed test that passes, when MoonSharp does not
detect AOT, and when the NativeAOT build finds fewer tests than the JIT build.
The test assemblies are trimmer roots, because tests are found by reflection;
the interpreter is not, so it is trimmed as an app gets it. The three
`TrimDescriptorTests` are skipped there: they read the embedded
`ILLink.Descriptors.xml`, which the trimmer applies and then removes.

All 797 tests pass under NativeAOT (5 skipped), and all 800 under JIT. When the suite
first ran this way, 58 of 761 failed. What they found, and the fixes:

| Failures | Error | Fix |
|---|---|---|
| 20 | `'MyEnum[]' is missing native code or metadata` | Enum values come from `Enum.Parse` per name, not `Enum.GetValues(Type)`, which builds an array of the enum type. |
| 18 | `No parameterless constructor defined for type 'List<string>'` (also `List<int[]>`, `Dictionary<string, int>`) | `[DynamicDependency]` on the conversions keeps the constructors of `List<>` and `Dictionary<,>`. |
| 10 | `cannot access field GetEnumerator / TryGetValue / (indexer) of userdata<List<int>>` | `[DynamicallyAccessedMembers]` on `UserData.RegisterType` and `RegisterExtensionType` keeps the members of every type a host registers. |
| 2 | `Enumerable.Sum[Int32](..., Func`2[Int32,Int32]) is missing native code` | A generic extension overload that cannot be instantiated is skipped; `Sum(IEnumerable<int>)` and `Last<int>` still resolve. |
| 1 | `obj.M == obj.M` was false again | `[DynamicallyAccessedMembers]` on `DispatchingUserDataDescriptor` keeps the methods its override check reflects over. |
| 2 | `Assert.AreEqual` on an `IEquatable` value: NUnit calls `Type.GetInterfaceMap` | Test harness only; the tests call `Equals`. |
| 3 | `TrimDescriptorTests` | Skipped, see above. |
| 2 | Found once AOT was detected: a member's exception arrived wrapped in `TargetInvocationException` | Reflection access mode rethrows the member's own exception. |
| 1 | `NewScript_AllocatesLittle`: 166 KB per `new Script()` (90 KB under JIT) | A repeated `RegisterType` of a registered type no longer builds a descriptor to discard; the io and dynamic modules do that for every `Script`. Now about 70 KB on both. |

`PlatformAutoDetector.IsRunningOnAOT` also returned false on NativeAOT, because
its probe compiles an expression tree and NativeAOT interprets one instead of
throwing; it now reads `RuntimeFeature.IsDynamicCodeSupported` first.
netstandard2.0 does not declare the trimming attributes, so the interpreter
declares `DynamicallyAccessedMembersAttribute` and `DynamicDependencyAttribute`
internally; the trimmer matches them by name.

What NativeAOT does not prove about IL2CPP: IL2CPP keeps reflection data for
every method it keeps and, from Unity 2022.1, shares generic code across value
types, so the constructor and member failures above may never occur there,
while UnityLinker's handling of the annotations at higher stripping levels is
unmeasured. The enum, exception and allocation fixes do not depend on the
runtime. Only a Unity player run can confirm either.

### Measurements

Same machine, session and code for every column: 4-core Intel Xeon 2.1 GHz
cloud VM shared with other jobs (load average up to 6.7 during these runs),
Ubuntu 24.04, OpenJDK 21.0.12, .NET 8.0.33, this branch. Same eligibility rule
and arguments as `WorkerBenchmark`. Reproduce with
`HostBenchmark <worker|coreclr|aot|none> <fixtures dir> <cold|hot|memory>` in the
client's test sources; run one variant per JVM and one benchmark at a time.
Where two values are given, they are two separate runs. An empty JVM starts in
50 ms; Jackson's first use alone took 260–475 ms in these runs, which is why the
"JVM start to first result" row is dominated by Java, not by MoonSharp.

| Measurement | Worker | In process, CoreCLR | In process, NativeAOT |
|---|---|---|---|
| Open, load module, first call (inside a started JVM; median of 5) | 223 ms | 237 ms | 38 ms |
| ...of which starting .NET (`open`) | 27–38 ms (process spawn; .NET starts during `load`) | 70–97 ms | 1.6–2.7 ms |
| JVM start to first result, including JVM start and Jackson's first use (median of 5) | 602 ms | 625 ms | 420 ms |
| Hot call, p50 / p95 / p99, pass 1 (50,000 calls after 20,000 warm-up) | 22.1 / 55.3 / 92.5 µs; 46.4 / 74.5 / 113.2 µs | 13.0 / 31.3 / 54.1 µs; 14.7 / 32.3 / 53.1 µs | 6.0 / 11.4 / 22.8 µs; 5.8 / 10.9 / 22.3 µs |
| Hot call, p50 / p95 / p99, pass 2 (the next 50,000) | 18.9 / 52.5 / 73.9 µs; 43.5 / 66.7 / 102.0 µs | 4.4 / 9.9 / 18.8 µs; 4.3 / 7.1 / 16.4 µs | 6.7 / 11.6 / 24.9 µs; 5.2 / 9.6 / 19.1 µs |
| ...inside `ScriptHost.Invoke` (parse arguments, run), p50 | 3 µs; 6 µs | 7–10 µs in pass 1, 3 µs in pass 2 | 3–4 µs |
| Throughput, 1 / 2 / 4 / 8 Java threads, one context (or worker process) per thread, calls/s, median of 3 | 17,676 / 48,322 / 107,734 / 76,071; 30,298 / 71,182 / 59,618 / 64,778 | 185,378 / 286,335 / 303,079 / 308,676; 188,901 / 197,532 / 306,259 / 371,333 | 164,481 / 295,913 / 313,860 / 368,331; 188,673 / 254,764 / 299,077 / 263,109 |
| Peak RSS, Java heap fixed at 64 MB, 1 context, 10,000 calls (empty JVM with Jackson: 69 MB) | JVM 106 MB + worker 126 MB = 232 MB | 175 MB (95 MB right after starting CoreCLR) | 148 MB (69 MB right after loading) |
| Peak RSS, same, 8 contexts | JVM 135 MB + 8 workers 1,083 MB = 1,218 MB | 273 MB | 256 MB |
| Peak RSS after the hot benchmark, default JVM heap | JVM 220 MB + 8 workers 1,435–1,441 MB | 419–424 MB | 390–395 MB |

`WorkerBenchmark` itself, re-run in the same session: start, load and first call
median 211 ms; hot call p50 45.6 µs, p95 70.4 µs, p99 104.1 µs (6.0 µs inside
`ScriptHost.Invoke`); 8 client threads on a pool of 1 / 2 / 4 workers: 27,345 /
29,959 / 25,294 calls/s. The earlier numbers in the status section are within
that noise.

An earlier full set, taken on a quieter machine (load average 1–3.6) before this
branch was rebased onto the stack-recovery and stack-growth changes in the
interpreter, agrees: pass-2 p50 44–45 µs (worker), 4.5–4.6 µs (CoreCLR),
4.9–5.0 µs (NativeAOT); 8-thread throughput 86–100 k (worker), 390–466 k
(CoreCLR) and 328–336 k (NativeAOT) calls/s; open, load and first call 247,
236 and 41 ms.

These sets used a NativeAOT library with `MoonSharp.Interpreter` rooted whole.
After merging `master` (trim descriptor, exact int64), the unrooted 5.5 MB
library measured the same within noise: pass-2 p50 5.0 µs, open, load and first
call 34–55 ms, 1-thread throughput 179 k calls/s.

**Where an in-process call spends its time** (pass 2, p50, both runs):

| Part | CoreCLR | NativeAOT |
|---|---|---|
| Whole call: `invokeJson` to `HostResult` | 4.3–4.4 µs | 5.2–6.7 µs |
| Native call with pre-encoded arguments, raw reply (no Java JSON) | 3.7–4.6 µs | 4.4–4.5 µs |
| ...of which `ScriptHost.Invoke`: argument JSON to Lua values, run the rule | 3 µs | 3–4 µs |
| ...of which the rest: result to JSON envelope, UTF-8, transition, copy | about 1 µs | about 1 µs |
| Boundary alone: echo the 64-byte argument string (JNI, transition, UTF-8 both ways, alloc and free) | 0.2 µs | 0.2 µs |
| Java JSON: encode arguments, Jackson-parse the reply into `HostResult` | 0.4 µs | 0.4–0.7 µs |

So JSON and marshalling outside Lua take about 1.5 µs of a 4.5–5 µs call
(roughly 30 %), and the JNI crossing itself 0.2 µs. A binary protocol or
Panama would shave part of that, not change the comparison. In the worker,
about 38 of 44 µs (86 %) are outside `ScriptHost.Invoke`: pipe I/O, waking two
processes and JSON on both sides.

Reading the numbers:

- The first 50,000 CoreCLR calls still run partly on tier-0 code (tiered
  compilation promotes methods after 30 calls and a 100 ms quiet period), so
  pass 1 is three times slower than pass 2. NativeAOT is compiled ahead of time
  and is at full speed from the first call; its steady state is about the same
  as CoreCLR's or slightly slower.
- The worker's latency depends on how fast the OS wakes its processes. In one
  run, with the machine busy enough that no core went idle, it dropped to
  19–22 µs; the other runs and `WorkerBenchmark` gave 43–48 µs.
- Throughput above 4 threads cannot scale: the machine has 4 cores, and other
  jobs shared it. The worker numbers vary most (repetitions differ by up to 3×).
- Memory: CoreCLR adds about 26 MB when it starts; one busy context then brings
  the JVM to 175 MB, against 232 MB for the JVM plus one worker. Every extra
  worker costs another 125–180 MB, an extra in-process context about 15 MB.

### Recommendation: in process is a supported option, the worker stays the default

In process is about 9 times faster per call once warm (4.3–5.2 µs against
43–46 µs at the median in the typical runs; CoreCLR's first 50,000 calls take
13–15 µs). Throughput is 6–10 times higher on one thread, 3–5 times higher than
one worker per thread on 4 threads, and about 10 times higher than the pooled
setup `MoonSharpPool` gives (8 client threads on 4 workers: 25–36 k calls/s).
NativeAOT starts in 38 ms against 223 ms, and in process uses less memory as
soon as more than one worker would be needed.

The two ways a script could end or hang the JVM are now closed (table above).
Recursion through a CLR callback is a Lua error on both backends, also on a
Java thread with a 256 KB stack, where it used to end the JVM. A context with a
limit returns an `interrupted` error where an endless loop used to hold the
Java thread until the JVM exited. In-process hosting is therefore a supported
option on Linux x64, no longer an experiment, under these conditions:

1. Create every context with a time limit, an instruction limit or both
   (`ContextOptions`, see "Limits"). Without a limit, a script that never
   returns still holds its Java thread for good.
2. Cap the .NET heap with `DOTNET_GCHeapHardLimit` (hexadecimal, without `0x`
   for NativeAOT), set before the JVM starts. Without a cap, a script that
   allocates without bound grows the JVM process until the OS ends it; with
   one, it gets a `host` error and the JVM lives on. The cap covers every
   context in the process.
3. Accept what the limits do not stop: a single long library call holds its
   thread until it returns (2.8–3.3 s for one `gsub` over 20 million
   characters above, with a 100 ms limit).

Prefer the NativeAOT backend there: no .NET install, 38 ms start, full speed
from the first call and the smallest memory. Prefer CoreCLR when scripts call
registered C# types: under NativeAOT, a conversion to a collection of a value
type the app never instantiates has no native code
([ROADMAP.md](ROADMAP.md), "Next: CLR interop").

The worker stays the default, and this branch does not change it:

- It contains the failures nobody has probed yet. A bug in the interpreter or
  the runtime that ends .NET, such as a stack overflow on a path the guard does
  not cover, costs one worker, which the pool replaces; in process it costs the
  service. After the probes above no such case is known.
- Memory stays per process. A worker can be limited and watched on its own
  (cgroups, RSS); in process one .NET heap serves every context, so one
  script's allocation can make another context's call fail.
- It runs wherever .NET 8 runs. In process is built and tested on Linux x64
  only, needs one native library per platform and, for CoreCLR, a .NET runtime
  that the JVM finds through `DOTNET_ROOT`.

For most callers 45 µs per call is already small next to the rest of a
request. Use `InProcessMoonSharp` where latency or throughput matters and the
three conditions above hold.

## Modules

Every host creates scripts with `ScriptHost.PortableModules`: all core modules
except `io`, the `os` system calls and `debug`. Those have no browser
equivalent: creating the `io` module opens stdin, which throws
`PlatformNotSupportedException` on WebAssembly. One module set everywhere keeps
a script from passing on the server and failing in the browser. A .NET host can
still pass `CoreModules.Preset_Complete` to the `ScriptHost` constructor.

## Reloading scripts

`load` with an existing id replaces the module, and the next `invoke` uses the
new code. The old compiled code stays in memory: a MoonSharp `Script` keeps
every chunk it has compiled. To release it, dispose the context and load the
new version into a fresh one. A context per published script bundle works well.

## Precompiled scripts

`moonsharp-host compile rules.lua -o rules.luac` compiles a chunk to MoonSharp's
binary format (`Script.Dump`) with source locations, so runtime errors still
name the line. Compiling at publish time moves syntax errors out of the clients
and parsing off their start-up; the bytecode is still interpreted.
`moonsharp-host run rules.luac` runs it. A .NET or Unity host loads the file
with `Script.LoadFile` or `LoadStream`. Every `ScriptHost` operation that takes
source (`validate`, `load`, `run`) also takes a dump as text:
`MoonSharp_dump_b64::` followed by the file in base64, the form
`Script.LoadString` accepts.

`moonsharp-host test --bytecode` runs the whole corpus this way, so CI shows
that compiled and source chunks give the same results, error lines included.
Load only dumps you built: MoonSharp does not verify bytecode, and the format
changes with the MoonSharp version.

## Browser notes

- The build publishes untrimmed `MoonSharp.Interpreter`
  (`TrimmerRootAssembly`). MoonSharp registers its standard library through
  reflection, and trimming removes those methods: `string`, `table` and
  `require` come back nil.
- The worker script listens with `addEventListener("message")`. The .NET runtime
  replaces `self.onmessage` inside a Web Worker, so a handler assigned to
  `onmessage` never runs.
- Pass 64-bit IDs as strings. For raw JSON text use `invoke(module, fn, { json })`
  and `runJson(chunk, source, inputJson)`.
- `runtime.createContext({ int64: true })` creates a context with the int64 and
  uint64 modules; `instructionLimit` and `timeLimitMs` set the limits. A script
  on the page's main thread blocks the page until it returns, so set a limit
  there, or run it in the Web Worker (`WorkerClient.createContext(options)`).
  The playground uses a 5 s time limit.

## Status

Measured on 2026-10-28 on a 4-core Intel Xeon 2.1 GHz cloud VM, Ubuntu 24.04.

| Check | Result |
|---|---|
| Conformance corpus, 225 cases in 8 suites (2026-10-28, `mise.toml` tools): `dotnet`, `dotnet-bytecode`, .NET with invariant globalization, Chromium 153 main thread, Java 17 worker, CoreCLR 8.0.32 and NativeAOT in process | 225/225 on each; every known-failures file is empty |
| Conformance corpus, .NET with `LANG=tr_TR.UTF-8` | all pass since numbers convert to text without the process culture; earlier 223/225 (`1.5 .. ''` gave `1,5`) |
| Shared fixtures, .NET 8 CLI | 24/24 under `C.UTF-8`, `tr_TR`, `de_DE` and invariant globalization |
| Shared fixtures, headless Chromium 153 (Playwright 1.63), main thread | 24/24 |
| Web Worker path, Chromium | pass (eligibility call, 2^53 + 1 rejected) |
| Shared fixtures, Java 21 through the worker | 24/24, plus 6 worker behaviour tests |
| Shared fixtures, Java 21, in process through CoreCLR 8.0.33 (hostfxr + JNI) | 24/24, plus 10 in-process behaviour tests |
| Shared fixtures, Java 21, in process through NativeAOT (.NET 8) | 24/24, plus 10 in-process behaviour tests |
| MoonSharp unit tests compiled with NativeAOT (first run, before the fixes in "The unit suite under NativeAOT") | 680/732; 49 failures were CLR interop, 3 are descriptor tests that cannot pass when trimmed |
| Spring Boot 4.1 sample over HTTP | 4/4 |
| Fixtures on .NET (default culture and invariant globalization), Chromium, the Java worker, CoreCLR and NativeAOT in process, and the Spring Boot sample, rerun with the `mise.toml` tools (SDK 10.0.402, .NET 8.0.32, Temurin 17.0.20.1, Maven 3.10.0, Node 24.22.1, CMake 4.4.4) | all pass |
| Firefox, Safari/WebKit, iOS Safari | not run (only Chromium is installed here) |
| Unity Editor, Android/iOS IL2CPP | not run (no Unity licence or devices here) |
| Windows/macOS hosts | not run |

Browser download (`dotnet publish`, .NET 10, interpreter mode, no AOT): 18
files, 5,940,862 bytes raw, 2,343,322 gzip, 1,914,963 brotli. The largest is
`dotnet.native.wasm` (3.0 MB raw, 977 KB brotli); `MoonSharp.Interpreter` is
370 KB raw, 132 KB brotli. Runtime start in headless Chromium from a local
server: about 550–610 ms. Cached `invoke` from JavaScript, including JSON in
both directions: 78–89 µs per call.

Java worker (Java 21, .NET 8 worker, eligibility rule from the fixtures):

| Measurement | Result |
|---|---|
| Start process, load module, first call | median 227 ms (min 200, max 448, n=5) |
| Hot call, one worker, sequential (n=50,000) | p50 45.5 µs, p95 71.8 µs, p99 114.4 µs |
| ...of which inside `ScriptHost.Invoke` (parse arguments, run) | p50 6.0 µs, p99 19.0 µs |
| Throughput, 8 client threads, pool of 1 / 2 / 4 workers | 16,180 / 31,941 / 40,455 calls/s |

Most of a hot call is pipe I/O and JSON, not Lua. Reproduce with
`WorkerBenchmark` in `src/Hosting/java/client/src/test`. The in-process
variants remove the pipe; their numbers, a re-run of these in the same session
and the reasons the worker stays the default are under
"Java: in process".
