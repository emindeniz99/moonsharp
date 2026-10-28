# Conformance corpus

Every host runs these suites and must produce the same results:

| Host name | Runner |
|---|---|
| `dotnet` | `moonsharp-host test` (.NET CLI, also run with invariant globalization) |
| `dotnet-tr-TR` | `moonsharp-host test --host dotnet-tr-TR` with `LANG=tr_TR.UTF-8`, a culture with a decimal comma |
| `dotnet-bytecode` | `moonsharp-host test --bytecode`: every chunk is compiled first, as `moonsharp-host compile` does at publish time, and loaded as bytecode |
| `browser-chromium`, `browser-firefox`, `browser-webkit` | `src/Hosting/browser/test/smoke.mjs` (headless browsers through Playwright, .NET WebAssembly) |
| `java-worker`, `java-coreclr`, `java-nativeaot` | `FixtureConformanceTest` and its subclasses in `src/Hosting/java` |

All of them run the same interpreter, so a case that fails on one host and passes
on another points at the host: its build (trimming, NativeAOT, WebAssembly), its
globalization mode or its transport (UTF-8 pipes, JNI, JavaScript strings).

The format follows [cel-spec's test data](https://github.com/cel-expr/cel-spec/tree/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/tests/simple/testdata)
(a case is data: script, input, expected output or error) and the known-failures
check follows quickjs-ng's test262 runner.

## Suites

| Suite | What it pins |
|---|---|
| `logic` | Synthetic application logic: eligibility rules, A/B bucketing, offer selection, building a JSON document |
| `numbers` | Number to string conversion (`tostring`, `..`, `string.format`), `tonumber`, modulo, NaN and infinity |
| `strings` | Length and bytes of UTF-16 strings, `string.sub` on surrogate pairs, case mapping, `%q` and `%s` |
| `patterns` | `find`, `match`, `gmatch` and `gsub` with captures, anchors, classes, frontiers and malformed patterns |
| `tables` | `pairs` order for mixed keys, after removal and re-insertion, length borders, the table library |
| `errors` | Error kinds, exact messages and lines for runtime, syntax and library errors |
| `json` | The host's JSON boundary (2^53, null, `json.emptyarray()`, nesting up to the 128 limit, unpaired surrogates) and the `json` module |
| `int64` | The `int64` and `uint64` modules and `json.parse`'s `"int64"` and `"string"` modes |
| `limits` | The per-call instruction limit (`interrupted` errors that `pcall` cannot catch, a usable context afterwards) and the stack guard for recursion through CLR callbacks |
| `time-limit` | The per-call time limit |

The expectations are what MoonSharp does today on every host. Where that differs
from reference Lua 5.2, the case has a `note` saying what Lua 5.2 does; some of
those are bugs, listed in docs/ROADMAP.md. Fixing one changes the expectation in
the same commit. The `tables` cases pin the `pairs` order: insertion order, as the
owner decided for the table rewrite (docs/ROADMAP.md).

## Suite format

```json
{
  "suite": "logic",
  "description": "...",
  "context": { "int64": true, "instructionLimit": 1000000, "timeLimitMs": 100 },
  "modules": { "offers": "scripts/offers.lua" },
  "cases": [
    { "name": "...", "module": "offers", "function": "select_offers", "args": [ ... ], "expect": ... },
    { "name": "...", "module": "config", "function": "echo", "argsJson": "[9007199254740993]",
      "expectError": { "kind": "input", "messageContains": "9007199254740993" } },
    { "name": "...", "op": "validate", "chunk": "broken.lua", "source": "...",
      "expectError": { "kind": "syntax", "line": 2 } },
    { "name": "...", "op": "run", "chunk": "adhoc.lua", "source": "...", "input": { ... }, "expect": ...,
      "note": "Lua 5.2 gives ..." }
  ]
}
```

- Each suite gets one fresh script context. `context.int64` creates it with the
  int64 and uint64 modules (`ScriptHost.Create(true)`); without it the context has
  `ScriptHost.PortableModules`. `context.instructionLimit` and
  `context.timeLimitMs` set the context's limits for each call
  (docs/hosting.md, "Limits").
- `modules` (optional) maps module ids to files, relative to the suite. Every
  module is loaded into the suite's context, so modules can `require` each other.
- `op` is `invoke` (the default), `validate` or `run`. `chunk` is the chunk name
  that error messages show; it defaults to the case name.
- `args` is a JSON array, `input` a JSON value. Use `argsJson` or `inputJson`
  (JSON text) for inputs a JSON parser would change or reject on the way, such as
  integers beyond 2^53 or invalid JSON: JavaScript and Jackson would round or
  refuse them before the host could.
- `expect` is compared structurally: object key order is ignored and numbers
  compare by value. To pin exact text, such as a number's formatting or an object's
  key order, return a string from the script.
- `expectError` checks `kind` (`syntax`, `runtime`, `input`, `output`, `host`,
  `interrupted`),
  `messageContains` and `line`. Each field is optional. Most cases put the whole
  message in `messageContains`, position included.
- `note` is for people; runners ignore it.
- Case names are unique within a suite. A suite name and a case name, joined by
  `" / "`, identify a case in the known-failures files.

## Known failures

`known-failures/<host>.json` lists the cases that fail on that host, with the
exact mismatch the runner reports:

```json
{
  "numbers / concatenation formats numbers like tostring": "$[1]: expected \"1.5\", got \"1,5\""
}
```

The file is `{}` when everything passes. Every runner compares its results
with its file and fails on any difference:

- **new**: a case fails that is not listed;
- **changed**: a listed case fails with a different detail;
- **fixed**: a listed case passes (remove it from the file);
- **stale**: a listed case of a suite that ran no longer exists.

So a fix must update the file, and a regression cannot hide behind an old entry.
When a run fails this way, the runner prints the file content that matches the
run; review it and copy it into the file. Each runner ends with a count line, for
example `browser-firefox: 245 cases, 245 passed, 0 known failures; 0 new,
0 changed, 0 fixed, 0 stale`, and CI copies those lines into the job summary.

## Adding a case

1. Add it to the suite of its area (or a new `<area>.suite.json`; runners pick up
   every `*.suite.json` in this directory).
2. Run it on .NET and take the result as the expectation only after checking it
   is right: compare with reference Lua 5.2 and add a `note` where they differ.
3. Run the other hosts. A host that disagrees gets an entry in its known-failures
   file, or better, a fix.

## Running

```sh
dotnet build -c Release src/Hosting/MoonSharp.Hosting.Cli
dotnet src/Hosting/MoonSharp.Hosting.Cli/bin/Release/net8.0/moonsharp-host.dll test src/Hosting/fixtures
dotnet src/Hosting/MoonSharp.Hosting.Cli/bin/Release/net8.0/moonsharp-host.dll test --bytecode src/Hosting/fixtures
```

`test` also takes single suite files, and `--report out.json` writes every result
with its outcome. The browser and Java commands are in docs/hosting.md and
`.github/workflows/hosting.yml`.
