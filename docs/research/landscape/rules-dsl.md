# Rule and expression DSLs for "evaluate server-defined logic on many platforms"

Survey date: 2026-10-28. Scope: should part of the server-sent business logic move from Lua
(a modernised managed Lua 5.2 interpreter in C#) to a non-Turing-complete DSL that has a
cross-platform conformance suite, and where would the boundary sit?

Hosts that must agree bit-for-bit: Unity 2021.3 / 2022.3 / 6 LTS (Mono and IL2CPP; iOS,
Android, desktop; WebGL a plus), browsers (today via .NET WebAssembly plus TypeScript), JVM
servers, plain .NET.

Evidence markers: **[C]** confirmed by reading source or official docs at the cited
commit/URL; **[S]** secondary (blog, package listing, forum, issue); **[U]** unconfirmed or my
inference. Repository facts (HEAD date, latest tag and its commit date) come from shallow
clones and `git ls-remote` made on 2026-10-28; the GitHub API was not reachable from this
environment, so star counts, open-issue counts and maintainer counts are mostly not given.
No benchmarks were run. Every number quoted below is someone else's, with its conditions.

---

## 1. Summary table

"TC" = Turing-complete. "Conformance" = a test corpus shared by independent implementations.
"Fit" = fit for the need above as a *complement* to Lua, not as a replacement.

| Candidate | Kind | TC? | Unity (Mono/IL2CPP) | Browser | JVM | .NET | Shared conformance corpus | Exact int64 | Latest release (date) | Licence | Fit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **CEL** (spec + Go/Java/C++/C + 3rd-party .NET/JS) | expression language | No (terminating by spec) | Managed C# impls exist; none Unity-ready out of the box | cel-es (beta), cel-js; or a .NET impl in .NET-wasm | **Official cel-java** | Celly (net8.0), Cel.NET (netstandard2.1) | **Yes**: 32 textproto files in cel-spec; Celly claims 2,456/2,456 | **Yes**: int64 and uint64, overflow is an error | cel-spec v0.25.3 (2026-07-29); cel-go v0.30.0 (2026-07-24); cel-java v0.15.0 (2026-10-26) | Apache-2.0 | **Partial / best of the field** |
| JsonLogic (many ports) | JSON-encoded rule AST | No | Ports are plain C# | Reference is JS | Java ports | C# ports | Community suite exists, ports pass 62–69% | No: number type is whatever the port uses (double, decimal) | json-logic-js v1.0.12 (2024-07-09) | MIT (most ports) | **No** (divergence measured) |
| flagd / OpenFeature targeting | JsonLogic + custom ops | No | .NET provider is netstandard2.0, not Unity-targeted | JS provider | Java provider | Yes | Yes (Gherkin testbed), feature-flag-specific | Capped at 2^53−1 in tests | flagd-testbed v3.10.4 (HEAD 2026-10-27) | Apache-2.0 | No (narrow), but a good *pattern* |
| GrowthBook conditions | Mongo-style query JSON | No | netstandard2.0 SDK | JS SDK | Java SDK | Yes | Yes (`standard-cases.json`) | Not checked | C# SDK v1.2.0 (2026-05-28) | MIT [S] | No (narrow) |
| Unleash Yggdrasil | Rust core for flag logic | No | Native FFI per platform [U] | WASM | FFI | FFI | Yes (client-specification) | n/a | v0.21.6 (2026-10-15) | Apache-2.0 [S] | No (narrow, native) |
| GoRules ZEN | decision tables + expression lang (Rust) | Expression: no; Function node: JS (yes) | Native lib; .NET binding desktop-only [S] | WASM (needs SharedArrayBuffer) [S] | Java/Kotlin binding | Native binding | Own test data, single implementation | Yes (`rust_decimal`) | zen-engine npm 0.54.0 (HEAD 2026-10-27) | MIT | No (native-only on mobile) |
| Microsoft RulesEngine | JSON rules + C# lambda strings | Yes (C# expressions) | Uses `Expression.Compile` | No | No | Yes | None | C# semantics | v6.0.0 (2025-06-03) | MIT | No |
| NRules | Rete engine, rules are C# | Yes | Uses `Expression.Compile` | No | No | Yes | None | C# | v1.0.4 (2026-02-02) | MIT | No |
| Drools / Kogito DMN-FEEL | rule engine / decision standard | DRL yes; FEEL mostly expression | No | feelin (JS FEEL) | **Yes** | No mature option found | **DMN TCK** (feelin: 1,901/2,053) | FEEL is decimal | Drools 10.2.0 (tag) | Apache-2.0 | No |
| OPA Rego (+ WASM) | policy language | No (no unbounded recursion) [U] | Only via a WASM runtime (managed WASM interpreter or native wasmtime) | Official JS WASM SDK | opa-java-wasm | OpaDotNet (wasmtime) | OPA's own test suite, one compiler | int64 or arbitrary-precision ref | v1.21.4 (2026-10-23) | Apache-2.0 | No (two layers of indirection) |
| Starlark | Python-like config language | No (no recursion, no `while`) by default | No C# impl | starlark-go → WASM | Java impl lives inside Bazel | No | None shared; spec allows UTF-8 vs UTF-16 strings | Arbitrary precision | starlark-rust v0.14.0 (2026-05-22) | Apache-2.0/BSD | No |
| Jsonnet | JSON templating | Yes (recursion) | No C# impl | Via WASM builds | sjsonnet (Scala) | No | Partial | **No: IEEE double** | v0.22.0 (2026-03-24) | Apache-2.0 | No |
| CUE | constraint/config language | No | No | Go→WASM only | No | No | Single impl | Arbitrary precision | v0.18.0-rc.2 (2026-10-26) | Apache-2.0 | No |
| Pkl | config language (JVM/Truffle) | Yes | No | No | **Yes** | No | Single impl | Int is 64-bit [U] | 0.32.2 (2026-10-21) | Apache-2.0 | No |
| Expr (Go) | expression language | No ("always terminating") | No | No | No | No | Single impl | Go int | v1.17.8 (2026-02-14) | MIT | No |
| NCalc | expression evaluator | No | Plain C#, netstandard2.0 | No | No | Yes | Single impl | Configurable (long/decimal/BigDecimal) | v7.2.0 (2026-09-13) | MIT | No (.NET only) |
| DynamicExpresso | C# expression interpreter | Yes-ish (C# expressions) | Uses `Expression.Compile` and `DynamicInvoke` | No | No | Yes | Single impl | C# | v2.19.6 (2026-09-24) | MIT | No |
| Jexl (JS) / JexlNet / Apache JEXL | two unrelated languages sharing a name | JS Jexl: no; Apache JEXL: yes | JexlNet (C#) | TomFrost Jexl (stale since 2021) | Apache JEXL (different language) | JexlNet | None | JS double | Jexl v2.3.0 (2020-09-15) | MIT / Apache-2.0 | No |

Also looked at: JSONata (JS reference, .NET and Java ports, shared JS test suite; the .NET
port intentionally changes `$formatNumber` semantics) and Cedar (Rust, Java via JNI, WASM;
authorization-specific, no C#). Neither changes the verdict.

---

## 2. Top findings

1. **CEL is the only candidate whose spec owners maintain a cross-implementation conformance
   corpus *and* whose number model matches the need** (int64 and uint64, overflow is an error,
   double is separate, no implicit numeric conversion). It also has an official,
   actively released JVM implementation. The weak link is .NET for Unity: one managed
   implementation claims 100% conformance but targets only `net8.0` and uses .NET 7+ APIs; the
   other targets `netstandard2.1` but pulls in protobuf, gRPC, Avro, NodaTime, Newtonsoft and
   ANTLR, and states no conformance result.
2. **"Cross-platform JSON rules" without a strict spec diverge in practice.** On the
   JsonLogic community's own 1,138-test suite, the most-used C# port passes 66.3%, the
   Java port 62.0%, and the original JS reference only 68.7%. Even the project built on
   JsonLogic for multi-language flag evaluation had to write its own testbed and move its
   bucketing from floats to integers. The lesson applies to Lua too: a conformance corpus
   matters more than which language you pick.
3. **Every other candidate fails on reach, not on design.** Starlark, Expr, CUE, Pkl, Jsonnet,
   Rego and the decision engines either have no managed C# implementation (so no IL2CPP story
   without native or WASM layers), have no JVM implementation, or use IEEE doubles for all
   numbers. The .NET-only engines (RulesEngine, NRules, DynamicExpresso) depend on
   `Expression.Compile`, which IL2CPP only supports through an interpreter fallback with
   stripping caveats.

---

## 3. Cross-cutting hazards for any "same result everywhere" language

These came up for nearly every candidate. They apply equally to the Lua path.

| Hazard | Where it bites | Evidence |
|---|---|---|
| Integer width and number model | JsonLogic ports use double (JS) or `decimal` (json-everything C#); Jsonnet is IEEE double only; flagd's testbed caps large ints at 2^53−1 | [C] json-everything `AddRule.cs` uses `decimal result` ([L59](https://github.com/gregsdennis/json-everything/blob/4d7ca86f1276c9be0048bb4dca11ef70d5837f04/src/JsonLogic/Rules/AddRule.cs#L59)); [C] Jsonnet "IEEE754 64-bit floating point" ([language.html.md L134-L136](https://github.com/google/jsonnet/blob/2ef32abf3d5b83c24863949e8413b810d54c7f2a/doc/ref/language.html.md#L134-L136)); [C] flagd [precision.feature](https://github.com/open-feature/flagd-testbed/blob/7308f9b1bf722716edd1c19f7ea5681d8a414c27/evaluator/gherkin/precision.feature) |
| Float-based bucketing / hashing | flagd's `fractional` op had to add a "v2 high-precision integer bucketing" next to the "legacy float-based" one | [C] [fractional.feature L4-L6](https://github.com/open-feature/flagd-testbed/blob/7308f9b1bf722716edd1c19f7ea5681d8a414c27/evaluator/gherkin/fractional.feature#L4-L6) |
| String length unit | Starlark's spec lets Go/Rust use UTF-8 bytes and Java use UTF-16 code units, so `len()` of non-ASCII text differs by implementation. CEL defines strings as code-point sequences | [C] [Starlark spec L758-L767](https://github.com/bazelbuild/starlark/blob/c0af0ea03dc90d3bbc021394b693e6957cc15030/spec.md?plain=1#L758-L767); [C] [CEL langdef L333](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md?plain=1#L333) |
| Regex dialect | CEL specifies RE2. Each host needs RE2 semantics: cel-es ships its own JS RE2 port (`@bufbuild/re2`); Celly maps to .NET `RegexOptions.NonBacktracking` (.NET 7+, not in Unity's BCL) | [C] [CEL langdef "Regular Expressions"](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md); [C] [cel-es package.json deps](https://github.com/bufbuild/cel-es/blob/8f0c64a73e9f5f57b25c59d66c0f952233caea85/packages/cel/package.json); [C] [Celly RegexEngine.cs L14-L52](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/src/Celly/Stdlib/RegexEngine.cs#L14-L52) |
| Time zones | CEL timestamp functions accept IANA names. Celly resolves them via `TimeZoneInfo.FindSystemTimeZoneById`, so the answer depends on the OS tz database (Windows vs ICU vs iOS/Android versions) | [C] [Celly TimeFunctions.cs L207](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/src/Celly/Stdlib/TimeFunctions.cs#L207); [U] OS-version drift |
| Map iteration order | CEL does not specify iteration order for maps, so `m.map(k, …)` may produce lists in different orders on different engines | [U] the ordering section of [langdef](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md) covers comparison operators only; I found no map-iteration guarantee |
| Extension drift | CEL's core is portable but extensions are not equally available: cel-es skips `bindings_ext`, `block_ext`, `encoders_ext`, `proto2_ext`; Cel.NET and the TELUS port lack the strings/math extensions | [C] [cel-es conformance.test.ts L22-L52](https://github.com/bufbuild/cel-es/blob/8f0c64a73e9f5f57b25c59d66c0f952233caea85/packages/cel/src/conformance.test.ts#L22-L52); [S] [Celly performance.md](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/docs/performance.md) |

Practical rule: whatever language runs the logic, only the functions and types that the
shared corpus covers can be trusted to agree. Anything else (regex, time zones, float
formatting, hashing) has to be a host function with its own cross-platform tests.

---

## 4. Candidates

### 4.1 CEL (Common Expression Language)

**What it is.** A non-Turing-complete expression language with a written spec, an optional
static type checker, macros for bounded comprehensions (`all`, `exists`, `exists_one`, `map`,
`filter`), and opt-in extensions (strings, math, lists incl. `sort`/`sortBy`, sets, optionals,
`cel.bind`). The spec describes it as memory-safe, side-effect-free, terminating, strongly and
dynamically typed, and gradually typed.
[C] [langdef L7-L21](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md?plain=1#L7-L21);
[C] [cel-go ext/README.md](https://github.com/google/cel-go/blob/6d32ef54e4f124929730b15d5eb9b9205da58992/ext/README.md)

**Expressiveness.** No statements, no user-defined functions, no mutable state, no unbounded
loops. Comprehensions are bounded by input size, but nested macros can still blow up
exponentially, and the spec says so. cel-go and Celly offer static cost estimation to reject
expensive expressions.
[C] [langdef "Macro Performance" L880-L910](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md?plain=1#L880-L910);
[C] Celly `src/Celly/Checking/CostEstimator.cs`

**Numbers and JSON.** `int` is 64-bit signed and `uint` is 64-bit unsigned. `double` is
separate. There are no implicit mixed-type arithmetic conversions (`1 + 1u` fails to
dispatch), and integer overflow raises an error rather than wrapping. The spec defines JSON
mapping through `google.protobuf.Value`/`Struct`. Caveat [U]: JSON numbers arrive as `double`
unless the host declares typed inputs, so int64 exactness needs declared variable types or
host-side conversion.
[C] [langdef L208-L235](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md?plain=1#L208-L235),
[Overflow L1196-L1215](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md?plain=1#L1196-L1215)

**Determinism.** "For a given evaluation environment, a CEL expression will deterministically
evaluate to either a value or an error." Logical operators are commutative with respect to
errors, so evaluation order is not observable. The hazards in §3 (regex, time zones, map
order) are where engines can still differ.
[C] [langdef L604](https://github.com/cel-expr/cel-spec/blob/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/doc/langdef.md?plain=1#L604)

**Conformance corpus.** `cel-spec/tests/simple/testdata/*.textproto` has 32 files covering
basic, integer_math, fp_math, macros, string, timestamps, proto2/3, optionals and extensions.
Celly counts 2,456 tests in it. Each test gives an expression, a typed environment, bindings,
and the expected value, error, or deduced type. The format is textproto, and C# protobuf has
no textproto parser, so Celly converts the files to binary protobuf when vendoring.
[C] [cel-spec tests/simple/testdata](https://github.com/cel-expr/cel-spec/tree/21ec6f4e52e8d694687ff9ed7408d3eccbb955d7/tests/simple/testdata);
[C] [Celly conformance.md](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/docs/internals/conformance.md)

**Implementations and activity (2026-10-28).** Note that the spec repo now lives in the
`cel-expr` organisation.

| Implementation | Host | Latest tag (date) | HEAD | Notes |
|---|---|---|---|---|
| [cel-expr/cel-spec](https://github.com/cel-expr/cel-spec) | spec + corpus | v0.25.3 (2026-07-29) | 2026-10-27 | [C] |
| [google/cel-go](https://github.com/google/cel-go) | Go | v0.30.0 (2026-07-24) | 2026-09-09 | Reference implementation [C] |
| [google/cel-java](https://github.com/google/cel-java) | JVM | v0.15.0 (2026-10-26) | 2026-10-26 | "Java 8 or newer"; protobuf "full or lite runtime" [C] [README](https://github.com/google/cel-java/blob/ab84c6ee19327d7f140d33a2fe7aeef9fa484e2b/README.md) |
| [google/cel-cpp](https://github.com/google/cel-cpp) | C++ | v0.16.1 (2026-08-13) | 2026-10-27 | Heavy deps (abseil, protobuf, re2) [U] |
| [cel-expr/cel-c](https://github.com/cel-expr/cel-c) | C | no tags | 2026-10-27 | Built on upb; README says "no stability guarantees" for API/ABI [C] |
| [bsidio/celly](https://github.com/bsidio/celly) | .NET | v1.2.0 (2026-07-18) | 2026-07-18 | Claims 2,456/2,456; Apache-2.0; `net8.0` only; `IsAotCompatible`; zero runtime deps; ~10.7k lines in `src/Celly` [C] |
| [rayokota/cel.net](https://github.com/rayokota/cel.net) | .NET | package 2.3.1, no git tags | 2026-09-10 | Port of projectnessie cel-java; `netstandard2.1;net462;net8.0;net10.0`; deps Antlr4.Runtime.Standard, Google.Protobuf, Grpc.Core.Api, Apache.Avro, Newtonsoft.Json, NodaTime; conformance via gRPC server, no result published [C] [csproj](https://github.com/rayokota/cel.net/blob/9aea3698ea4565018d826a7cc6cc14f54408b5a6/src/main/Cel.main.csproj) |
| [telus-labs/cel-net](https://github.com/telus-labs/cel-net) | .NET | v0.3.3 (2026-06-08) | 2026-06-08 | Requires protobuf messages as types; Celly measures it 6–9× slower [S] |
| [bufbuild/cel-es](https://github.com/bufbuild/cel-es) | JS/TS | v0.6.1 (2026-08-26) | 2026-10-14 | "Status: Beta"; runs the spec corpus with a skip list (quoted fields, part of type deduction, some extensions, strong enums) [C] |
| [marcbachmann/cel-js](https://github.com/marcbachmann/cel-js) | JS | v8.0.0 (2026-07-07) | 2026-07-07 | Zero deps; ints are `BigInt`; "most of the CEL spec"; no spec-corpus runner found in the repo [C] |

**Performance evidence (published, not reproduced).** From Celly's own BenchmarkDotNet
"short job" runs on Apple Silicon, net8.0, compared with cel-go 0.29 on go1.26: simple
expression `x + 1 > 3 && name.startsWith('h')` takes 129 ns in Celly, 107 ns in cel-go,
152 ns in Cel.NET and 1,112 ns in TELUS Cel. A 100-element filter/map/exists comprehension
takes 14.8 µs (Celly) vs 22.5 µs (cel-go). Parse plus plan costs 1.5 µs and 6.6 KB; the
type check costs 8.6 µs and 48 KB.
[S] (author-run) [Celly performance.md](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/docs/performance.md).
cel-js publishes its own micro-benchmarks against the older ChromeGG cel-js (for example,
"variable lookups" evaluate at 15.6 M ops/s vs 1.0 M ops/s, machine not stated).
[S] [cel-js README](https://github.com/marcbachmann/cel-js/blob/86cd97216a1cbb522eda81aaa0221c245d5913d7/README.md).
I found no IL2CPP or mobile numbers for any CEL implementation.

**Unity / IL2CPP fit.**
- Celly cannot be dropped into Unity as-is. It targets `net8.0` only
  ([Directory.Build.props L4](https://github.com/bsidio/celly/blob/c31c2774f4df7b9bbbf0786906b43b3015db8aa4/Directory.Build.props#L4)),
  uses `LangVersion latest` with collection expressions, and depends on
  `RegexOptions.NonBacktracking` (.NET 7+). Unity 2021.3–6 compile C# 9 against a
  netstandard2.1-style BCL [U]. A port means retargeting about 10.7k lines and replacing the
  regex engine with an RE2-compatible managed one. That port is mechanical but real work,
  and it creates a fork to keep in sync with the 2,456-test corpus [U].
- Cel.NET targets netstandard2.1, so it loads in Unity, but its dependency closure (protobuf,
  gRPC abstractions, Avro, NodaTime, Newtonsoft, ANTLR runtime) works against "small
  download". Reflection-based JSON/type registries need `link.xml` under IL2CPP stripping [U].
  It publishes no conformance number.
- Neither uses `Reflection.Emit` or expression compilation in its core evaluation path
  ([C] for Celly: no `System.Linq.Expressions`/`Emit` usage found under `src/`). Both are
  tree-walking interpreters, so IL2CPP is feasible in principle [U]. Not tested here.

**Browser.** Two routes: (a) reuse the same .NET implementation inside the existing
.NET-WebAssembly bundle (one engine for Unity, .NET and browser) [U]; (b) cel-es or cel-js
in TypeScript. With (b) you have three independent engines to keep in conformance.

**JVM.** cel-java is official, Google-maintained, Java 8+, protobuf full or lite. This is
the strongest part of CEL's story for this need: server-side validation and evaluation need
no .NET or Lua runtime on the JVM.

**Tooling.** The type checker validates expressions against declared variable types at
authoring time [C]. cel-go has a REPL [S]. I found no maintained CEL language server or
autocomplete extension [U, search found nothing]. Error messages carry source positions
[C, Celly `CelIssue`/`Location`].

**Maturity and risk.** Spec, Go, Java and C++ are healthy and released regularly. The .NET
options are either young or heavy. Celly was announced on 2026-07-18 in the author's
write-up [S] ([bsid.io](https://bsid.io/writing/building-a-cel-engine-for-net)), and its
package metadata names only "Celly Contributors" (maintainer count not verified) [U].
Cel.NET carries a large dependency closure. Risk sits on the Unity side, which is where
most of the evaluations happen.

**Policy.** Expressions are data interpreted by an embedded evaluator. Google Play's rule
on downloaded executable code "does not apply to code that runs in a virtual machine or an
interpreter". [C] [Play policy](https://support.google.com/googleplay/android-developer/answer/9888379).
Apple 2.5.2 is stricter in wording, but interpreted rule data that does not change the app's
features is the lowest-risk shape [S] ([App Review Guidelines](https://developer.apple.com/app-store/review/guidelines/#software-requirements)).
CEL rules carry less policy risk than Lua scripts, because they cannot add features [U].

### 4.2 JsonLogic

**What it is.** Rules as a JSON AST (`{"and":[{">":[{"var":"level"},10]}, …]}`) with
`map`/`filter`/`reduce`/`all`/`some`, no user functions, and no general loops [C, suites].
Hosts add custom operators.

**Reach.** Ports exist in almost every language, including C#: JsonLogic.Net, and
json-everything's `JsonLogic` (netstandard2.0 + net8–10, MIT, v6.1.0 2026-05-16) [C]
[csproj](https://github.com/gregsdennis/json-everything/blob/4d7ca86f1276c9be0048bb4dca11ef70d5837f04/src/JsonLogic/JsonLogic.csproj).
Java has jamsesso json-logic-java (v1.0.7, 2022-01-18) and the gzsombor fork used by flagd
(1.1.6) [C]. JS has the original json-logic-js (v1.0.12, 2024-07-09) and json-logic-engine
(5.0.0, 2025-05-26) [C].

**Conformance, measured by the community.** The [json-logic org](https://github.com/json-logic)
formed to "address ambiguities and inconsistencies". Its
[compat-tables](https://github.com/json-logic/compat-tables/tree/25669097955453d3c2381bf953f4741b9a4fc0d1/results)
run a 1,138-test suite weekly. Results on 2026-10-26 [C]:

| Port | Passed / 1,138 |
|---|---|
| JsonLogic.Net (C#, v1.1.11) | 754 (66.3%) |
| jamsesso json-logic-java 1.0.6 | 706 (62.0%) |
| json-logic-js (original reference) | 782 (68.7%) |
| json-logic-engine (JS) | 1,127 (99.0%) |
| DataLogic (Rust) | 1,138 (100%) |

Even on the original `compatible.json` subset, the C# port passes 260 of 278 [C]. The suite
contains no test above 2^53, so 64-bit behaviour is unspecified [C, grep of `suites/`].
Number types differ by port: json-everything uses `decimal`, JS uses double (§3).

**Verdict.** No. It is the clearest evidence that a "portable rules JSON" without a strict
numeric spec and one shared corpus produces different answers on different hosts.

### 4.3 Feature-flag targeting engines (OpenFeature/flagd, GrowthBook, Unleash, LaunchDarkly-style)

- **OpenFeature** is a vendor-neutral evaluation *API*, not a language [C]. Its reference
  backend **flagd** uses "a modified version of JsonLogic" plus custom operators
  (`fractional`, `sem_ver`, `starts_with`/`ends_with`)
  [C] [flag-definitions.md L219](https://github.com/open-feature/flagd/blob/260d2fe31af35a92faf937149bd53b42467f4c58/docs/reference/flag-definitions.md?plain=1#L219).
  In-process providers embed different JsonLogic libraries per language: .NET pins
  json-everything `JsonLogic` 6.0.2
  ([csproj](https://github.com/open-feature/dotnet-sdk-contrib/blob/bba127f946e928bad7e1646552b5f992801019f5/src/OpenFeature.Providers.Flagd.Core/OpenFeature.Providers.Flagd.Core.csproj)),
  and Java uses gzsombor json-logic-java 1.1.6
  ([tools/flagd-core/pom.xml](https://github.com/open-feature/java-sdk-contrib/blob/a91a79075f056fac2c1387607ba7935e8314631d/tools/flagd-core/pom.xml)) [C].
  Agreement comes from the Gherkin **flagd-testbed** (`evaluator/gherkin/*.feature`:
  targeting, fractional, semver, precision, …), not from the libraries [C]
  ([testbed](https://github.com/open-feature/flagd-testbed/tree/7308f9b1bf722716edd1c19f7ea5681d8a414c27/evaluator/gherkin)).
- **GrowthBook** conditions use a MongoDB-style query JSON. Every SDK runs one shared
  `standard-cases.json`; the C# SDK is netstandard2.0 with Newtonsoft [C]
  ([tests](https://github.com/growthbook/growthbook-c-sharp/tree/2a4b0082a08f777757f8724b72f9e24f09382066/GrowthBook.Tests/Json)).
- **Unleash Yggdrasil** goes the other way: one Rust core, reached through FFI by most SDKs
  and through WASM for Node [C]
  ([README](https://github.com/Unleash/yggdrasil/blob/9f22f176bf672a8ef5c959196ba601916524d2a0/README.md)),
  validated against a shared client-specification [C]. For Unity this would mean native
  libraries per platform (iOS static lib via `__Internal`) and WASM for WebGL [U].

**Fit.** Too narrow for "offer/variant selection and config-driven behaviour". The pattern
is the useful part: (1) a shared, language-neutral test corpus that every SDK runs, and
(2) integer-only bucketing with a specified hash (murmur3 in flagd).

### 4.4 GoRules ZEN (decision engine)

A Rust core runs JSON Decision Models: decision tables, switch nodes, a ZEN expression
language, and "Function" nodes in JavaScript. Bindings: Node, Python, Go, Kotlin/Java,
.NET, and a C FFI [C] (`bindings/{c,nodejs,python,uniffi}` at
[ec108e8](https://github.com/gorules/zen/tree/ec108e8069d9eb3154e1856310cc1e8b6bcf4276/bindings)).
Numbers are `rust_decimal` [C]
([core/expression/Cargo.toml](https://github.com/gorules/zen/blob/ec108e8069d9eb3154e1856310cc1e8b6bcf4276/core/expression/Cargo.toml)).
Licence MIT [C]. The .NET package lists Windows/macOS/Linux only. The browser WASM build
needs SharedArrayBuffer and COOP/COEP headers [S]
([docs](https://docs.gorules.io/developers/sdks/wasm)). The vendor reports "91,000
evaluations per second" averaged over 74 decisions on one M3 core [S]
([docs](https://docs.gorules.io/developers/overview/performance)).
**Fit: no.** Native-only on Unity mobile, and Function nodes embed a JS runtime, which
brings back a Turing-complete language.

### 4.5 .NET-only rule engines: Microsoft RulesEngine, NRules, NCalc, DynamicExpresso

| | RulesEngine | NRules | NCalc | DynamicExpresso |
|---|---|---|---|---|
| Model | JSON workflows whose rule expressions are C# lambda strings | Rete forward-chaining; rules are C# classes (NRules.Json can serialise them) | Infix expressions, functions, parameters | C# expression subset |
| Frameworks | net6–10, netstandard2.0/2.1 | netstandard2.0/2.1 | net462, netstandard2.0, net8/10 | netstandard2.0 |
| Execution | System.Linq.Dynamic.Core + FastExpressionCompiler → compiled delegates | `expression.Compile()` behind `IExpressionCompiler` | Interpreted core; lambda compilation is an opt-in plugin | `Expression.Lambda(...).Compile()`, `DynamicInvoke`, Microsoft.CSharp |
| Latest | v6.0.0 (2025-06-03) | v1.0.4 (2026-02-02) | v7.2.0 (2026-09-13) | v2.19.6 (2026-09-24) |
| Evidence | [C] [csproj](https://github.com/microsoft/RulesEngine/blob/5650f93f843865610240e0498b26b68b477a3920/src/RulesEngine/RulesEngine.csproj) | [C] [ExpressionCompiler.cs](https://github.com/NRules/NRules/blob/ec221234fe6f42b39b096f9085b851eeffb9b54b/src/NRules/NRules/Extensibility/ExpressionCompiler.cs) | [C] [src/Directory.Build.props](https://github.com/ncalc/ncalc/blob/9e875d92aaf7110d66374a98a628662698d55908/src/Directory.Build.props) | [C] [Lambda.cs L28, L134](https://github.com/dynamicexpresso/DynamicExpresso/blob/ad167c0f396127de6df1f0b555d2916992345713/src/DynamicExpresso.Core/Lambda.cs#L28) |

IL2CPP cannot JIT. `Expression.Compile` reportedly falls back to the LINQ interpreter
since Unity 2018.2 and needs `link.xml` against stripping, and AOT failures on that
interpreter's generics have been reported [S]
([efcore#13099](https://github.com/dotnet/efcore/issues/13099),
[UniVRM#471](https://github.com/vrm-c/UniVRM/issues/471)). None of these four has a JVM or
JS implementation, so the server and browser cannot run the same rules. NCalc is the only
one that is a clean interpreter on netstandard2.0, but it is single-implementation and
.NET-only. **Fit: no.**

### 4.6 Drools / Kogito, DMN and FEEL

Drools (DRL, Rete/Phreak) and Kogito are JVM-only. The latest Drools release tag is
10.2.0 [C]; HEAD was 2026-10-27, Apache-2.0
([repo](https://github.com/apache/incubator-kie-drools)). DMN's expression language FEEL
has a vendor-neutral **DMN TCK** ([dmn-tck/tck](https://github.com/dmn-tck/tck), HEAD
2026-10-19) [C]. The JS FEEL interpreter
[feelin](https://github.com/nikku/feelin) (v8.2.0, HEAD 2026-09-20) passes 1,901 of 2,053
FEEL TCK tests (102 pending, 50 failing) [C]
([tck-results.json](https://github.com/nikku/feelin/blob/ea0d6ba543f59bb97ab7fe52d16a27ab2c86bf64/docs/tck-results.json)).
Its runtime deps include no decimal library [C], while FEEL numbers are decimal, so its
arithmetic is likely JS double [U]. I found no maintained managed .NET FEEL/DMN engine (the
previously known `adamecr/net-dmn-engine` URL no longer resolves) [U].
**Fit: no.** No Unity path, and the hosts would disagree on numbers.

### 4.7 OPA Rego, compiled to WebAssembly

`opa build -t wasm` compiles Rego to a WASM module with a documented ABI (1.0–1.3). Some
built-ins are "not, and probably never will be, natively supported" and must be supplied by
the host through `opa_builtin*` imports [C]
([OPA WASM docs](https://www.openpolicyagent.org/docs/wasm)). Numbers in the WASM runtime
are `long long` or an arbitrary-precision reference [C]
([value.h L22-L50](https://github.com/open-policy-agent/opa/blob/6d3fadd3724fc461073de43bed1faf6891967a0d/wasm/src/value.h#L22-L50)).
OPA v1.21.4 (2026-10-23), Apache-2.0 [C]. Hosts:
- JS: official npm-opa-wasm [C].
- JVM: [opa-java-wasm](https://github.com/StyraInc/opa-java-wasm) 2.1.0 (2026-10-20) [C].
- .NET: [OpaDotNet](https://github.com/me-viper/OpaDotNet) (v2026.1.2, 2026-07-03, MIT,
  credits wasmtime-dotnet) [S] and
  [csharp-opa-wasm](https://github.com/christophwille/csharp-opa-wasm) (v0.91, 2025-10-10) [C].
- Unity: wasmtime is native and JIT-based [U], so it does not fit iOS. A pure-C# WASM
  interpreter ([WACS](https://www.nuget.org/packages/WACS), netstandard2.1, "IL2CPP/AOT
  compatibility", no unsafe) would work in theory [S]. The result is a policy compiled to
  WASM, interpreted by a WASM interpreter, which is itself compiled by IL2CPP. Performance:
  no published numbers [U].

**Fit: no.** Rego's datalog-style semantics suit allow/deny over JSON documents, not offer
selection or computed values, and the Unity path adds two layers.

### 4.8 Configuration languages: Starlark, Jsonnet, CUE, Pkl

- **Starlark**: "Execution is finite. The language does not allow recursion or unbounded
  loops." It is "deterministic and hermetic", ints are arbitrary-precision, and dicts keep
  insertion order [C]
  ([spec L42-L51](https://github.com/bazelbuild/starlark/blob/c0af0ea03dc90d3bbc021394b693e6957cc15030/spec.md?plain=1#L42-L51)).
  Implementations: Go (HEAD 2026-10-21, no tags), Rust (v0.14.0, 2026-05-22), Java inside
  Bazel ("may not be suitable for use in other applications"), and a starlark-go WASM
  build [S] ([awesome-starlark](https://github.com/laurentlb/awesome-starlark)). There is
  no C# implementation and no shared conformance corpus. The spec allows UTF-8 vs UTF-16
  strings (§3). **No**, although its "Python without recursion or while" design is the
  best existing model for a restricted *Lua* subset (§6).
- **Jsonnet**: pure functional and Turing-complete. Numbers are IEEE doubles; integers are
  exact only within ±(2^53−1) [C]. Implementations: C++/Go (v0.22.0, 2026-03-24), Scala
  [sjsonnet](https://github.com/databricks/sjsonnet) (0.7.5, 2026-09-23), Rust
  [jrsonnet](https://github.com/CertainLach/jrsonnet). No C# implementation. **No.**
- **CUE**: constraint unification with arbitrary-precision numbers. One Go implementation
  (v0.18.0-rc.2, 2026-10-26), with WASM only through Go's WASM target [C/U]. **No.**
- **Pkl**: JVM/Truffle (0.32.2, 2026-10-21). Non-JVM bindings (Swift, Go) talk to a
  `pkl server` child process, and a C library is "planned" [S]
  ([binding spec](https://pkl-lang.org/main/0.26.3/bindings-specification/index.html)).
  No .NET, no browser. **No.**

### 4.9 Expr (Go)

"Always terminating", memory-safe, with an optimising compiler to a bytecode VM. MIT,
v1.17.8 (2026-02-14) [C]
([README](https://github.com/expr-lang/expr/blob/4b31df3a2e0eefec04c017a82a00e0f08541d3e4/README.md)).
Go only, with no ports and no spec corpus. **No.**

### 4.10 Jexl

The name covers two unrelated languages. Mozilla/TomFrost **Jexl** (JS) is a small
expression language with transforms; last release v2.3.0 (2020-09-15), HEAD 2021-04-26 [C],
with a C# port **JexlNet** on System.Text.Json [S]
([NuGet](https://www.nuget.org/packages/JexlNet)). **Apache Commons JEXL** is a Java
scripting language with loops and functions (4.0 snapshots, HEAD 2026-10-26) [C]. Neither
has a shared corpus or JVM+C#+JS coverage of one language. JS Jexl is effectively
unmaintained. **No.**

---

## 5. Verdict for our need

| Candidate | Verdict | Why |
|---|---|---|
| CEL | **Partial: the only credible DSL; adopt only for a narrow layer, and only after a Unity-side spike** | Non-TC, exact int64/uint64 with overflow errors, a spec-owned conformance corpus, an official JVM implementation, managed .NET implementations. Against: no Unity-ready .NET build (Celly needs a netstandard2.1/C# 9 port plus a managed RE2-compatible regex; Cel.NET is heavy and unproven on conformance), no IL2CPP evidence, no mature editor tooling, and a second engine on every host |
| JsonLogic | No | Measured cross-port divergence (62–69% for the C#/Java/JS-reference ports), no numeric spec |
| flagd / GrowthBook / Unleash | No (copy their test-corpus pattern) | Built for flag targeting only |
| GoRules ZEN, OPA/Rego, Cedar | No | Native or WASM only on Unity; domain mismatch |
| RulesEngine, NRules, DynamicExpresso, NCalc | No | .NET-only; three of them depend on `Expression.Compile` |
| Drools/DMN-FEEL | No | JVM-centric; no Unity path; hosts would disagree on decimal vs double |
| Starlark, Jsonnet, CUE, Pkl, Expr, Jexl | No | No C# implementation, or double-only numbers, or no JVM |

### Should part of the logic move to a non-TC DSL?

**Not wholesale, and not yet.** A DSL pays off only when its conformance corpus is run on
every host. For CEL that means three or four engines (a Unity C# build, cel-java, and
either the same C# engine in .NET-wasm or cel-es). That doubles the number of runtimes,
binding layers and debugging stories next to Lua. The benefit is real but specific:

1. **Server-side evaluation on the JVM at volume.** cel-java is official, so the server
   needs no Lua or .NET runtime to evaluate rules. This is the strongest reason to adopt CEL.
2. **Authoring by people who should not write Lua** (designers, live-ops), with a static
   type check at save time that rejects a rule before it ships.
3. **Guaranteed termination and bounded cost.** Not a goal here, because scripts are
   trusted, but it removes timeouts and instruction counting from the predicate path.

### Where the boundary sits

Put a rule in the DSL only if all of these hold:

- it is a **pure function** of (JSON context, constants) → bool / int64 / string / small
  list or map;
- it needs **no state across calls**, no host calls with side effects, no early-exit loops
  over unbounded data, and no recursion or user-defined helper functions;
- every function it uses is in the **portable core** or in a host extension that has its
  own cross-host tests (no regex beyond an RE2-compatible subset, no named time zones, no
  float formatting);
- the server may need to **evaluate it too**.

Typical members: eligibility and segment predicates, offer filters, variant pickers that use
an integer hash supplied by the host and cross-tested, threshold and price formulas in
int64, and input validation rules.

Everything else stays in Lua: multi-step flows, state machines and coroutines, anything that
builds structures incrementally, anything that calls the game or UI, and shared helper
libraries. The dependency runs one way only: configuration or Lua references CEL predicates
by id and passes them a context; CEL never calls Lua.

### What to do instead or first (cheaper, same benefit)

1. **Adopt the conformance-corpus pattern for Lua now.** A language-neutral corpus of
   (script id, input JSON, expected output JSON or error), run in CI on every host: Unity
   Mono and IL2CPP players, .NET-wasm in a headless browser, the JVM path, plain .NET. Cover
   the §3 hazards explicitly: int64 edges and overflow, the string length unit, regex
   (Lua patterns), map/table iteration order, float-to-string. This is what flagd,
   GrowthBook and CEL actually rely on, and it is independent of the DSL decision.
2. **Define a "pure rule" profile of Lua**, checked statically at upload time: no
   `while`/`repeat`, no recursion, no global writes, no host calls outside a whitelist of
   pure functions. This is close to Starlark's restrictions. It delivers most of point 3
   above without a second engine [U].
3. **Spike CEL only if point 1 or 2 from the previous list becomes a requirement.**
   Port Celly to netstandard2.1 / C# 9 (or evaluate Cel.NET with a trimmed dependency set),
   run the 2,456-test corpus under IL2CPP on iOS and Android and under WebGL, and measure
   assembly size and cold start. Pair it with cel-java on the server. Go/no-go criteria:
   100% of the core corpus (extensions optional) on every host, plus an acceptable added
   download size.

Revisit triggers: a maintained netstandard2.1 CEL build with published conformance; a CEL
language server; the JVM needing to evaluate rules where hosting the Lua engine is too
costly; or non-programmers authoring rules.
