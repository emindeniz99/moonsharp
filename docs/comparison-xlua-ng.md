# MoonSharp compared with xLua-NG: desktop interpreter speed

A first, narrow comparison: pure Lua micro-workloads on one Linux x64 machine.
It says how fast the two interpreters run the same Lua, nothing about Unity,
IL2CPP, phones, package size or the C# bridge. Those need a Unity harness and
are not measured yet.

## Setup

| | MoonSharp | xLua-NG |
|---|---|---|
| Commit | `fbd0367` (upstream master) | `3042ef6` (main, 2026-10-28) |
| Engine | MoonSharp 3.0.0.0, Lua 5.2, managed | Lua 5.5.1, native |
| Build | Release, .NET 8.0.33 RyuJIT | `cmake -DCMAKE_BUILD_TYPE=Release -DLUA_VERSION=5.5.1`, gcc, out of tree |
| Runner | `src/DevTools/Benchmarks workloads 7` | `Test/Native/flag_bench.c` built with `cc -O2`, `--runs 7`, pinned to one core |
| Machine | 4-core Intel Xeon 2.1 GHz cloud VM, Ubuntu 24.04 | same |

Both runners use the same Lua sources and iteration counts, and check each
result. Numbers are median nanoseconds per loop iteration over 7 runs.
`int64_math` is left out: it needs Lua 5.3 integers, which MoonSharp lacks.

## Results

| Workload | xLua-NG, Lua 5.5.1 native (ns) | MoonSharp, .NET 8 (ns) | MoonSharp / native |
|---|---:|---:|---:|
| empty_loop | 3.5 | 65.4 | 18.9x |
| function_call | 26.0 | 375.9 | 14.4x |
| method_call | 66.3 | 705.3 | 10.6x |
| table_array_insert | 16.1 | 1,277.3 | 79.5x |
| table_array_read | 15.7 | 447.3 | 28.4x |
| table_hash_set_get | 35.8 | 756.7 | 21.2x |
| string_concat | 225.8 | 801.4 | 3.5x |
| string_format | 304.8 | 4,421.3 | 14.5x |
| closure_create_call | 94.0 | 661.7 | 7.0x |
| int_math | 11.3 | 261.3 | 23.0x |
| fib_recursive | 10,085.0 | 188,176.4 | 18.7x |
| float_math | 20.8 | 399.4 | 19.2x |
| string_pattern | 3,199.0 | 25,161.5 | 7.9x |
| table_sort (not the same script: MoonSharp copies with a loop, native uses `table.move`) | 51,008.9 | 127,600.3 | 2.5x |
| table_concat | 869.2 | 1,598.3 | 1.8x |
| coroutine_switch | 118.9 | 898.9 | 7.6x |
| gc_small_tables | 128.8 | 1,153.6 | 9.0x |
| oop_inherit | 69.6 | 623.2 | 9.0x |

## Reading the numbers

- MoonSharp runs this Lua 1.8 to 80 times slower than native Lua 5.5.1, and
  10 to 20 times slower in most cases.
- Library-heavy work narrows the gap (`table.concat` 1.8x, string building
  3.5x), because both spend their time in library code.
- `table_array_insert` (80x) and `table_array_read` (28x) stand out. MoonSharp
  tables have no array part, so `t[i] = i` goes through the hash index. An
  array part is the clearest optimisation target found so far.
- Arithmetic allocates a `DynValue` per intermediate result (about 230 bytes
  per iteration in the baseline loop benchmark). That shows in `int_math`,
  `float_math` and `fib_recursive`.
- IL2CPP compiles the managed VM differently from RyuJIT, and phones differ
  from a server CPU. Treat these ratios as desktop-only.

## Not compared yet

- Unity Editor and IL2CPP players on Android and iOS, at matching stripping
  levels.
- Package size (APK/IPA delta).
- The C#/Lua bridge: calls in both directions, userdata, delegates.
- Script start-up and memory per context. MoonSharp retains 79.5 KB per
  `Script` with fork PR #3 (2.31 MB before). xLua-NG's own notes report about
  1.4–1.6 ms per `LuaEnv` create and dispose on its hardware; not measured here.
