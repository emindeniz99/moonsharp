# Baseline: upstream MoonSharp at the fork point

Measurements of unmodified `master` (`fbd0367`, 2026-10-14), so later changes
have something to compare against. Every number here was measured; nothing is
estimated.

## Environment

| | |
|---|---|
| Machine | cloud VM, Intel Xeon 2.1 GHz, 4 cores, 15 GB RAM |
| OS | Ubuntu 24.04.5, Linux 6.18 |
| .NET | SDK 10.0.403; runtimes 8.0.33 and 10.0.14 |
| Date | 2026-10-28 |

Run-to-run noise on this machine is 5–10% between two BenchmarkDotNet runs of
the same build. An A/A comparison (`Benchmarks ab` of master against itself,
21 interleaved rounds) put medians within ±3%.

## Tests

```sh
dotnet build -c Release src/TestRunners/DotNetCoreTestRunner/DotNetCoreTestRunner.csproj
cd src/TestRunners/DotNetCoreTestRunner/bin/Release/net8.0 && dotnet DotNetCoreTestRunner.dll /unit
```

669 run, 669 passed, 2 skipped (`TestMore_308_io`, `TestMore_309_os`, skipped by
the runner on every platform). About 3.5 s. Not run here: the Mono/net45
runner, AOT tests and the Unity test bed.

## Speed and allocations

`src/DevTools/Benchmarks`, BenchmarkDotNet 0.15.8, default job, .NET 8:

| Benchmark | Mean | StdDev | Allocated |
|---|---:|---:|---:|
| `new Script()` | 1,408 µs | 112 µs | 2.32 MB |
| `new Script(CoreModules.None)` | 276 µs | 25 µs | 2.13 MB |
| `CreateCoroutine` | 111 µs | 9 µs | 2 MB |
| Call an eligibility rule (table argument) | 973 ns | 63 ns | 1.23 KB |
| `fib(20)` | 8.42 ms | 0.38 ms | 13.5 MB |
| 100K-iteration arithmetic loop | 19.36 ms | 0.90 ms | 22.9 MB |
| 1K table inserts and lookups | 849 µs | 44 µs | 843 KB |
| Build a 1K-part string with `table.concat` | 5.36 ms | 0.55 ms | 914 KB |

Arithmetic allocates: about 230 bytes per loop iteration, because each
intermediate number is a new `DynValue` object.

## Memory held by live objects

`Benchmarks retained`:

| Object | Bytes each |
|---|---:|
| `Script` (default modules) | 2,305,068 |
| Coroutine | 2,097,528 |

The cause is two 131,072-slot stacks allocated per processor (one per `Script`,
one per coroutine). 1,000 idle coroutines hold 2.1 GB.

## Reliability

| Behaviour | Result |
|---|---|
| Recursion depth | works at 30,000 frames; fails between 30,000 and 40,000 for a one-argument function |
| Runaway recursion | `IndexOutOfRangeException`, not catchable by `pcall`; the Script then fails every call |
| Failed `Script.Call` | leaks `args + 2` stack slots; after 43,689 failed one-argument calls every call fails |
| Repeated `LoadString` in one Script | memory grows without bound (`OutOfMemoryException` after about a million compiles) |

## Culture

The same script under different process locales:

| Locale | `'a' < 'B'` | `string.upper('i')` | `json.serialize({x = 1.5})` |
|---|---|---|---|
| C, en_US | true | I | `{"x":1.5}` |
| tr_TR | true | İ | `{"x":1,5}` (invalid JSON) |
| de_DE | true | I | `{"x":1,5}` (invalid JSON) |
| invariant globalization | false | I | `{"x":1.5}` |

## 64-bit integers

MoonSharp numbers are doubles. With a CLR class registered as userdata:

| Lua expression | Result |
|---|---|
| `Ids.MaxLong()` (returns `long.MaxValue`) | `9.223372036854776E+18` |
| `Ids.Show(Ids.MaxLong())` (takes `long`) | error: cannot convert a number to `System.Int64` |
| `Ids.Show(9007199254740993)` | `9007199254740992` (silently rounded) |
| `9007199254740993 == 9007199254740992` | `true` |
| `json.serialize(json.parse('{"id": 9007199254740993}'))` | `{"id":9007199254740992}` |
| `json.serialize(json.parse('{"id": 18446744073709551615}'))` | `{"id":1.8446744073709552e+19}` |
| `string.format('%d', 9007199254740993)` | `9007199254740992` |
| `bit32.lshift(1, 40)` | `256` (32-bit, as in Lua 5.2) |

Integers up to 2^53 are exact. Beyond that, values round silently, and
`long`/`ulong` values near their limits cannot make a round trip through Lua.

## Binary dumps

`Script.Dump` writes format `0x151`. Loaded chunks report every error at line 0.
Dump size is close to source size (for example 9,081 bytes for the 9,499-byte
`Test/More.lua`).
