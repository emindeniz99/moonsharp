# Running the same scripts on .NET, in the browser and on the JVM

`src/Hosting` lets one set of trusted Lua scripts run in three places with the
same results:

| Host | How it runs MoonSharp | Entry point |
|---|---|---|
| .NET (CLI, servers, tests) | in process | `MoonSharp.Hosting.ScriptHost`, `moonsharp-host` CLI |
| Browser | .NET WebAssembly in the page or a Web Worker | `src/Hosting/browser`, TypeScript API `moonsharp.ts` |
| Java / Spring Boot | a persistent `moonsharp-host worker` process per worker slot (default) | `src/Hosting/java`, `MoonSharpWorker`, `MoonSharpPool` |
| Java, in process (experimental, Linux x64) | CoreCLR or a NativeAOT library loaded into the JVM through JNI | `InProcessMoonSharp`, `src/Hosting/native` |

All three run the same interpreter assembly and the same `ScriptHost` class.
Only the transport differs. The shared fixtures in `src/Hosting/fixtures` prove
that the hosts agree.

Unity uses MoonSharp directly and does not need this layer. To check Unity
against the other hosts, run the fixtures through `ScriptHost` inside a player
(not done yet; see the status table below).

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

### Operations

| Operation | Meaning |
|---|---|
| `validate(chunk, source)` | Compile only. Use before publishing a script. |
| `load(moduleId, source)` | Run a module and keep its return value. `require(moduleId)` returns it too. Loading the same id again replaces the module and drops cached functions. |
| `invoke(moduleId, function, argsJson)` | Call a function of a loaded module. The compiled function is cached. |
| `run(chunk, source, inputJson)` | Compile and run once. If the chunk returns a function, call it with the input. |
| `unload(moduleId)`, `dispose(context)` | Free a module or a whole context. |

A context is one `Script` (one Lua state). Globals and modules are per context.
A context is not thread-safe: the browser has one thread per runtime, and the
Java pool gives each worker process its own contexts.

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
`{}`. Return `json.emptyarray()` (built into MoonSharp) for `[]`.

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

`args` may also be sent as text in `argsJson`. Run several workers for parallel
calls: `MoonSharpPool` keeps a fixed set, loads every module into each of them,
and replaces a worker that dies.

The worker is the default. The next section describes an in-process
alternative and why the worker stays the default.

## Java: in process (experimental)

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

Measured with `CrashProbe` (test sources), which runs each case in a child JVM:

| Case | Worker | In process, CoreCLR | In process, NativeAOT |
|---|---|---|---|
| .NET exception that is not a Lua error (`string.rep('x', 2^31)`) | `host` error envelope | same | same |
| Deep Lua recursion (`local function f(n) return 1 + f(n + 1) end`) | `runtime` error "stack overflow"; the context keeps working | same | same |
| Recursion through a CLR callback (`string.gsub(s, '.', f)` where `f` calls `gsub` again) | the worker dies with "Stack overflow"; Java gets `WorkerException`, a pool starts a new worker | **the JVM dies** (SIGABRT, exit 134, "Stack overflow" on stderr, no `hs_err` file) | **the JVM dies** (SIGSEGV, exit 139, no message) |
| Script that never returns, then `Thread.interrupt()` | the thread stays blocked; `close()` kills the worker and frees it | the thread keeps running; only ending the JVM stops it | same as CoreCLR |
| SIGTERM / SIGINT to the JVM | shutdown hooks run (exit 143 / 130) | shutdown hooks run, because the shim restores the JVM's SIGTERM handler (see below) | shutdown hooks run |
| SIGQUIT (thread dump) | works | works | works |
| Java `NullPointerException`s after .NET is loaded (the JVM raises them from SIGSEGV) | work | work (CoreCLR's handler passes signals it does not own to the JVM's) | work |

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

The build still reports 24 trim and AOT warnings, all in CLR interop code that
the JSON contract never reaches: `FrameworkClrBase` reflects over arbitrary
types (IL2075, IL2026 for `Assembly.GetTypes`), `TableConversions` builds
`List<T>`/`Dictionary<K,V>` with `MakeGenericType` and `Activator` (IL2055,
IL2067, IL3050), `ExtensionMethodsRegistry` calls `MakeGenericMethod` (IL2060,
IL3050), `StandardEnumUserDataDescriptor` calls `Enum.GetValues(Type)` (IL3050),
and `ValueTypeDefaultCtorMemberDescriptor` creates value types from a `Type`
(IL2072).

To see what those warnings cost, MoonSharp's own unit test runner
(`src/TestRunners/DotNetCoreTestRunner`) was compiled with NativeAOT, with
`MoonSharp.Interpreter`, `MoonSharp.Interpreter.Tests` and `nunit.framework` as
roots. 680 of 732 tests pass (all 732 pass under JIT). Three failures are
`TrimDescriptorTests`, which read the embedded `ILLink.Descriptors.xml`; the
trimmer applies that resource and then removes it, so they cannot pass in any
trimmed build. The other 49 are all CLR interop:

| Failures | Error | Cause |
|---|---|---|
| 19 | `NotSupportedException: 'MyEnum[]' is missing native code or metadata` | `Enum.GetValues(Type)` for a script-visible enum |
| 18 | `MissingMethodException: No parameterless constructor defined for type 'List<string>'` (also `List<int[]>`) | converting a Lua table to a generic CLR collection |
| 12 | `cannot access field GetEnumerator / Last / Sum / TryGetValue of userdata<List<int>>` (and `Dictionary`) | BCL members trimmed away, and LINQ extension methods instantiated with `MakeGenericMethod` |

Making those work would need a `TrimmerRootDescriptor` (ILLink XML) or
`[DynamicDependency]` for every CLR type exposed to scripts, an rd.xml-style
directive for each generic instantiation (`List<string>`, `Enumerable.Sum<int>`,
`MyEnum[]`), and `Enum.GetValues` replaced by reading the enum's static fields.
MoonSharp also does not detect NativeAOT: `PlatformAutoDetector.IsRunningOnAOT`
compiles an expression tree to find out, which succeeds on NativeAOT (it falls
back to the interpreter), so MoonSharp reports `false` and uses its JIT-only
code paths. Checking `RuntimeFeature.IsDynamicCodeSupported` would detect it.

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

### Recommendation: keep the worker as the default

In process is about 9 times faster per call once warm (4.3–5.2 µs against
43–46 µs at the median in the typical runs; CoreCLR's first 50,000 calls take
13–15 µs). Throughput is 6–10 times higher on one thread, 3–5 times higher than
one worker per thread on 4 threads, and about 10 times higher than the pooled
setup `MoonSharpPool` gives (8 client threads on 4 workers: 25–36 k calls/s).
NativeAOT starts in 38 ms against 223 ms, and in process uses less memory as
soon as more than one worker would be needed. But it removes the process
boundary, and the probes above show what that boundary is for:

1. A script can end the JVM. Recursion through a CLR callback such as
   `string.gsub` with a function replacement overflows the .NET stack, which no
   .NET code can catch. In the worker that costs one process, which the pool
   replaces; in process it ends the whole Java service, without an `hs_err` log
   for NativeAOT. In-process calls also run on the Java thread's stack (1 MB by
   default, against the usual 8 MB of the worker's main thread), so the limit
   is lower.
2. A script that never returns cannot be stopped. `ScriptHost` has no
   execution budget (MoonSharp can preempt only scripts that run as coroutines,
   through `Coroutine.AutoYieldCounter`), and `Thread.interrupt()` does not
   reach .NET code. A worker can be killed.
3. Packaging: Linux x64 only so far, one native library per platform, and for
   CoreCLR a .NET runtime on the server that the JVM finds through
   `DOTNET_ROOT`.

For most callers, 45 µs per call is already small next to the rest of a
request. Use `InProcessMoonSharp` where latency or throughput matters and the
scripts are trusted not to recurse without bound or loop forever. Prefer the
NativeAOT backend there: no .NET install, 38 ms start, full speed from the
first call and the smallest memory, as long as the scripts use only the JSON
contract (CLR interop is partly broken under NativeAOT, see above). Prefer the
CoreCLR backend when scripts need CLR interop.

Making in process the default would need two changes in MoonSharp, after which
this comparison should be repeated:

- a stack guard on the CLR recursion paths (`Processor.Call`, callbacks from
  `gsub`, `sort`, metamethods), for example
  `RuntimeHelpers.EnsureSufficientExecutionStack()`, so the overflow becomes a
  Lua error;
- an execution budget (instruction count or cancellation) for plain calls, so a
  runaway script returns an error instead of holding the thread.

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

## Browser notes

- The build publishes untrimmed `MoonSharp.Interpreter`
  (`TrimmerRootAssembly`). MoonSharp registers its standard library through
  reflection, and trimming removes those methods: `string`, `table` and
  `require` come back nil.
- The worker script listens with `addEventListener("message")`. The .NET runtime
  replaces `self.onmessage` inside a Web Worker, so a handler assigned to
  `onmessage` never runs.
- Pass 64-bit IDs as strings. For raw JSON text use `invoke(module, fn, { json })`.

## Status

Measured on 2026-10-28 on a 4-core Intel Xeon 2.1 GHz cloud VM, Ubuntu 24.04.

| Check | Result |
|---|---|
| Shared fixtures, .NET 8 CLI | 24/24 under `C.UTF-8`, `tr_TR`, `de_DE` and invariant globalization |
| Shared fixtures, headless Chromium 153 (Playwright 1.63), main thread | 24/24 |
| Web Worker path, Chromium | pass (eligibility call, 2^53 + 1 rejected) |
| Shared fixtures, Java 21 through the worker | 24/24, plus 6 worker behaviour tests |
| Shared fixtures, Java 21, in process through CoreCLR 8.0.33 (hostfxr + JNI) | 24/24, plus 10 in-process behaviour tests |
| Shared fixtures, Java 21, in process through NativeAOT (.NET 8) | 24/24, plus 10 in-process behaviour tests |
| MoonSharp unit tests compiled with NativeAOT (experiment, not in CI) | 680/732; 49 failures are CLR interop, 3 are descriptor tests that cannot pass when trimmed (see "NativeAOT and trimming") |
| Spring Boot 4.1 sample over HTTP | 4/4 |
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
"Java: in process (experimental)".
