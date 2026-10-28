# Shared fixtures

Every host runs these suites and must produce the same results: the .NET CLI
(`moonsharp-host test`), the browser runner and the Java integration test. A suite
that passes everywhere shows that the hosts map values the same way, because they
all run the same MoonSharp interpreter.

The scripts are synthetic examples of application logic: eligibility rules, A/B
bucketing, offer selection and building a JSON document.

## Suite format

```json
{
  "suite": "logic",
  "modules": { "offers": "scripts/offers.lua" },
  "cases": [
    { "name": "...", "module": "offers", "function": "select_offers", "args": [ ... ], "expect": ... },
    { "name": "...", "module": "config", "function": "echo", "argsJson": "[9007199254740993]",
      "expectError": { "kind": "input", "messageContains": "9007199254740993" } },
    { "name": "...", "op": "validate", "chunk": "broken.lua", "source": "...",
      "expectError": { "kind": "syntax", "line": 2 } },
    { "name": "...", "op": "run", "chunk": "adhoc.lua", "source": "...", "input": { ... }, "expect": ... }
  ]
}
```

- `modules` maps module ids to files, relative to the suite. Each suite gets one
  fresh script context and loads every module into it, so modules can `require`
  each other.
- `op` is `invoke` (the default), `validate` or `run`.
- `args` is a JSON array. Use `argsJson` (JSON text) for inputs that a JSON parser
  would change on the way, such as integers beyond 2^53: JavaScript and Jackson
  would round them before the host could reject them.
- `expect` is compared structurally: object key order is ignored and numbers
  compare by value.
- `expectError` checks `kind` (`syntax`, `runtime`, `input`, `output`, `host`),
  `messageContains` and `line`. Each field is optional.

## Running

```sh
dotnet run --project src/Hosting/MoonSharp.Hosting.Cli -- test src/Hosting/fixtures/logic.suite.json
```
