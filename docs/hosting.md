# Running the same scripts on .NET, in the browser and on the JVM

`src/Hosting` lets one set of trusted Lua scripts run in three places with the
same results:

| Host | How it runs MoonSharp | Entry point |
|---|---|---|
| .NET (CLI, servers, tests) | in process | `MoonSharp.Hosting.ScriptHost`, `moonsharp-host` CLI |
| Browser | .NET WebAssembly in the page or a Web Worker | `src/Hosting/browser`, TypeScript API `moonsharp.ts` |
| Java / Spring Boot | a persistent `moonsharp-host worker` process per worker slot | `src/Hosting/java`, `MoonSharpWorker`, `MoonSharpPool` |

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

Hosting CoreCLR inside the JVM through `hostfxr` would avoid the pipe. That is
a planned experiment; the worker stays the default until measurements show the
extra complexity pays off.

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

Most of a hot call is pipe I/O and JSON, not Lua. The in-process alternative
(hosting CoreCLR inside the JVM) would remove the pipe; it has not been built
yet. Reproduce with `WorkerBenchmark` in `src/Hosting/java/client/src/test`.
