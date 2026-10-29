# MoonSharp compared with xLua-NG

Two measurements:

- [Unity IL2CPP on a phone (2026-10-29)](#unity-il2cpp-on-a-phone-2026-10-29):
  this fork against xLua-NG in release Android players, pure Lua and the C#
  bridge, allocations, APK size, and the pure Lua set again on a Mac under
  .NET and Mono.
- [Desktop interpreter speed, upstream MoonSharp (2026-10-28)](#desktop-interpreter-speed-upstream-moonsharp-2026-10-28):
  the first, narrow comparison of upstream MoonSharp with native Lua on Linux.

The full method, every case and the raw numbers are in xLua-NG's
[`docs/research/benchmark-vs-moonsharp-ng.md`](https://github.com/emindeniz99/xlua-ng/blob/main/docs/research/benchmark-vs-moonsharp-ng.md);
the harness is xLua-NG's `Test/MobileSmoke/vs_bench.py` with
`Test/MobileSmoke/VsBench/VsBenchMoonSharp.cs` as this fork's adapter.

## Unity IL2CPP on a phone (2026-10-29)

### Setup

| | MoonSharp-ng | xLua-NG |
|---|---|---|
| Commit | `75864ce` (master, 2026-10-29) | `7c2037fd` (main, 2026-10-29) |
| Engine | Lua 5.2, managed, compiled by IL2CPP | Lua 5.5.1 in `libxlua.so` (NDK r30), called through P/Invoke |
| Package | `tools/upm/stage-local-package.sh`: the sources, `link.xml` and the source generator in `Runtime/Analyzers` | its UPM package with its source generator |
| Bindings | the source generator: the bench's types and delegate types in `[assembly: MoonSharpUserDataTypes]`, `GeneratedUserData.RegisterAll()`, types as `UserData.CreateStatic<T>()` globals, constructors through `__new` | `[LuaCallCSharp]`, `[GCOptimize]`, `[CSharpCallLua]`; legacy plain wrappers (shown here), GCM and strict minimal in the full note |
| Player | Unity 6000.3.21f1, Android, IL2CPP, ARM64, release, Minimal stripping | same |
| Device | moto g play 2024 (Snapdragon 680, Android 14), 8 launches per player in rotating order; ratios also from the `xlua16k` arm64 emulator (7 launches) | same |

Values are the median of the per-launch medians. Pure Lua workloads use the
sources and iteration counts of `src/DevTools/Benchmarks/LuaWorkloads.cs`
(5 rounds per launch, ns per iteration); the bridge cases are a subset of
zlua-benchmark's case list (7 rounds of 100,000 operations, ns per operation).
Collectors run during the timing. Bytes per operation come from a separate
pass with both collectors stopped (`GC.GetTotalMemory`, rounded to Boehm's
size classes; xLua-NG's Lua heap from `collectgarbage('count')`).

### Pure Lua on the phone

| Workload | xLua-NG (ns) | MoonSharp-ng (ns) | MoonSharp-ng / xLua-NG | emulator ratio | MoonSharp-ng bytes/iter |
|---|---:|---:|---:|---:|---:|
| empty_loop | 5.1 | 47.8 | 9.4x | 4.4x | 0 |
| function_call | 48.8 | 524.9 | 10.8x | 8.2x | 0 |
| method_call | 109.5 | 863.0 | 7.9x | 6.5x | 0 |
| table_array_insert | 29.4 | 406.1 | 13.8x | 14.2x | 82 |
| table_array_read | 41.7 | 362.0 | 8.7x | 6.5x | 0 |
| table_hash_set_get | 86.1 | 889.7 | 10.3x | 8.6x | 0 |
| string_concat | 1,137.9 | 921.8 | 0.8x | 1.3x | 88 |
| string_format | 1,130.2 | 1,750.2 | 1.5x | 1.8x | 45 |
| closure_create_call | 651.7 | 1,142.5 | 1.8x | 4.7x | 182 |
| int_math | 26.8 | 197.1 | 7.4x | 3.9x | 0 |
| fib_recursive | 24,714 | 221,743 | 9.0x | 8.8x | 0 |
| float_math | 57.1 | 399.0 | 7.0x | 3.9x | 0 |
| string_pattern | 6,422.8 | 21,710 | 3.4x | 3.7x | 578 |
| table_sort_52 (copy loop in both) | 118,706 | 427,604 | 3.6x | 2.6x | 24,166 |
| table_concat | 2,801.2 | 5,035.3 | 1.8x | 3.1x | 647 |
| coroutine_switch | 308.8 | 1,500.8 | 4.9x | 2.9x | 1 |
| gc_small_tables | 972.1 | 2,607.1 | 2.7x | 7.3x | 614 |
| oop_inherit | 122.9 | 735.2 | 6.0x | 4.6x | 0 |
| geometric mean | | | 4.8x | 4.6x | |

Every checked result matched `LuaWorkloads.cs` in both engines. xLua-NG
allocates no managed memory in these workloads; its Lua heap grows by 0 to
119 bytes per iteration (2,359 for `table_sort_52`).

### The C# bridge on the phone

By group (geometric mean of the cases):

| Group | xLua-NG (ns) | MoonSharp-ng (ns) | MoonSharp-ng / xLua-NG | emulator ratio |
|---|---:|---:|---:|---:|
| Lua → C#: static method, property, field | 135.8 | 726.0 | 5.35x | 4.33x |
| Lua → C#: instance calls (int, long, double) | 368.0 | 1,466.8 | 3.99x | 3.69x |
| Lua → C#: instance properties and fields (int, enum) | 364.4 | 1,112.0 | 3.05x | 3.12x |
| Lua → C#: strings | 738.7 | 1,123.8 | 1.52x | 1.51x |
| Lua → C#: Vector3 and struct | 816.1 | 3,600.6 | 4.41x | 5.57x |
| Lua → C#: objects | 1,015.8 | 4,345.5 | 4.28x | 4.24x |
| C# → Lua: numbers | 219.7 | 1,723.1 | 7.84x | 7.17x |
| C# → Lua: string | 1,000.9 | 2,146.0 | 2.14x | 1.87x |
| C# → Lua: Vector3 and struct | 1,623.7 | 12,313 | 7.58x | 11.24x |
| C# → Lua: object and class | 711.8 | 9,178.8 | 12.90x | 13.72x |
| C# reads a Lua table field | 1,294.2 | 92.8 | 0.07x | 0.10x |

Selected cases, with bytes per operation:

| Case | xLua-NG ns | MoonSharp-ng ns | ratio | xLua-NG bytes (managed / Lua) | MoonSharp-ng bytes |
|---|---:|---:|---:|---:|---:|
| `TT.StaticNoRet()` | 74.1 | 498.2 | 6.72x | 0 / 0 | 0 |
| `o:MethodVoidIntArity0()` | 272.8 | 549.7 | 2.02x | 0 / 0 | 0 |
| `o:MethodRetIntArity3(1, 2, 3)` | 381.2 | 1,689.6 | 4.43x | 0 / 0 | 0 |
| `o.Prop_Int` (read) | 370.5 | 1,026.1 | 2.77x | 0 / 0 | 27 |
| `o.Prop_Int = v` | 322.4 | 632.0 | 1.96x | 0 / 0 | 0 |
| `o.Prop_Vector3` (read) | 777.1 | 3,753.5 | 4.83x | 0 / 52 | 97 |
| `o:MethodRet_Class3(c, c, c)` | 879.7 | 5,178.7 | 5.89x | 0 / 0 | 68 |
| `o:MethodRet_String3(s, s, s)` | 1,200.0 | 1,717.7 | 1.43x | 141 / 0 | 0 |
| C# → `function(a, b, c) return a end`, ints | 244.4 | 2,018.0 | 8.26x | 0 / 0 | 0 |
| the same, strings | 1,000.9 | 2,146.0 | 2.14x | 45 / 0 | 0 |
| the same, Vector3 | 1,576.2 | 12,097 | 7.67x | 0 / 156 | 433 |
| the same, a class instance | 716.1 | 11,509 | 16.07x | 0 / 0 | 341 |
| C# reads `tbl.x` | 1,294.2 | 92.8 | 0.07x | 0 / 0 | 0 |

### APK size over an empty project

The same project settings, every player with the same bench code.

| Player | APK over empty | `libil2cpp.so` | `global-metadata.dat` | `libunity.so` | native Lua library |
|---|---:|---:|---:|---:|---|
| MoonSharp-ng | +1,372,380 | +3,930,384 (+1,136,642 compressed) | +724,852 | +6,448 | none |
| xLua-NG strict minimal | +582,542 | +1,285,768 | +203,584 | +17,184 | `libxlua.so` 312,176 |
| xLua-NG legacy (plain) | +1,619,334 | +2,522,824 | +452,564 | +1,107,840 | `libxlua.so` 388,136 |

xLua-NG legacy's `libunity.so` share is the UnityWebRequest and TLS modules
that its StreamingAssets searcher keeps. Higher stripping levels were not
measured.

### The same pure Lua on a Mac

Apple M2 Pro, macOS 27.0.1; 3 interleaved process rounds, the median of the
per-process medians of 7 runs. xLua-NG: `flag_bench.c` against
`libxlua.dylib` (Lua 5.5.1, Release). MoonSharp-ng `75864ce`:
`src/DevTools/Benchmarks workloads 7` on .NET 8.0.32, and the same
`LuaWorkloads.cs` compiled against the `net45` interpreter on Mono 6.14.1
(`mono --optimize=all`). As in the desktop table below, `table_sort` is not
the same script: native uses `table.move`, MoonSharp-ng the copy loop.

| Workload | xLua-NG native (ns) | MoonSharp-ng .NET 8 (ns) | ratio | MoonSharp-ng Mono (ns) | ratio | upstream ratio (2026-10-28, Linux) |
|---|---:|---:|---:|---:|---:|---:|
| empty_loop | 2.0 | 5.7 | 2.9x | 15.3 | 7.8x | 18.7x |
| function_call | 10.8 | 72.6 | 6.7x | 201.0 | 18.5x | 14.5x |
| method_call | 21.2 | 115.4 | 5.4x | 320.8 | 15.1x | 10.6x |
| table_array_insert | 3.7 | 64.5 | 17.3x | 141.9 | 38.1x | 79.3x |
| table_array_read | 7.6 | 51.1 | 6.8x | 173.3 | 22.9x | 28.5x |
| table_hash_set_get | 19.9 | 148.8 | 7.5x | 462.1 | 23.2x | 21.1x |
| string_concat | 153.0 | 103.4 | 0.7x | 299.4 | 2.0x | 3.5x |
| string_format | 189.0 | 164.9 | 0.9x | 527.6 | 2.8x | 14.5x |
| closure_create_call | 70.5 | 114.2 | 1.6x | 253.3 | 3.6x | 7.0x |
| int_math | 10.4 | 23.6 | 2.3x | 101.3 | 9.7x | 23.1x |
| fib_recursive | 4,886.9 | 35,987.8 | 7.4x | 90,861.3 | 18.6x | 18.7x |
| float_math | 19.6 | 42.7 | 2.2x | 179.6 | 9.2x | 19.2x |
| string_pattern | 1,594.7 | 2,257.2 | 1.4x | 4,665.8 | 2.9x | 7.9x |
| table_sort | 29,841.0 | 41,906.3 | 1.4x | 124,482.1 | 4.2x | 2.5x |
| table_concat | 499.7 | 687.4 | 1.4x | 878.9 | 1.8x | 1.8x |
| coroutine_switch | 71.8 | 198.1 | 2.8x | 555.8 | 7.7x | 7.6x |
| gc_small_tables | 97.2 | 218.9 | 2.3x | 644.5 | 6.6x | 9.0x |
| oop_inherit | 25.3 | 96.9 | 3.8x | 263.7 | 10.4x | 9.0x |
| geometric mean | | | 2.9x | | 8.0x | 11.3x |

### Reading the numbers

- **Pure Lua:** on the phone MoonSharp-ng runs the workloads 4.8 times slower
  than native Lua 5.5.1 (geometric mean). Under .NET on the Mac it is 2.9
  times, against 11.3 times for upstream MoonSharp in the earlier desktop
  run: the struct values, fusion, inline caches and array part show. IL2CPP
  on the phone sits between .NET and Mono (8.0 times).
- **Where the gap stays large:** the interpreter loop. An empty `for` step
  costs 47.8 against 5.1 ns on the phone, a Lua call 525 against 49 ns, an
  array insert 406 against 29 ns. Library-heavy work is within 1.5 to 3.6
  times, and string building is faster than native.
- **The bridge:** Lua → C# costs 1.5 to 5.4 times xLua-NG's per group;
  property and field reads allocate a result `DynValue` (27 bytes), and
  Vector3 and structs are boxed (97 bytes), which costs 3.75 µs per Vector3
  property read on this phone. C# → Lua is 2 to 16 times slower, and
  allocates 340 to 430 bytes per call with three class, Vector3 or struct
  arguments. Reading a Lua table from C# is 14 times faster than xLua-NG,
  which pays several P/Invoke calls for it.
- **Size:** 1.37 MB of APK, all of it IL2CPP output and metadata; between
  xLua-NG strict minimal (0.58 MB) and legacy (1.62 MB).
- **Not measured here:** reflection mode, the register VM prototype
  (`spike/register-vm-unity`), start-up cost and memory per `Script`,
  managed GC pauses, iOS, High stripping.

## Desktop interpreter speed, upstream MoonSharp (2026-10-28)

A first, narrow comparison of upstream MoonSharp (before this fork's changes)
with native Lua: pure Lua micro-workloads on one Linux x64 machine. It says
nothing about Unity, IL2CPP, phones, package size or the C# bridge; the
section above covers those for this fork.

### Setup

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

### Results

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

### Reading the numbers

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

### Not compared then

- Unity IL2CPP players, package size and the C#/Lua bridge: measured on
  2026-10-29 for Android (the section above). Still open: iOS, the Unity
  Editor (Mono), and other stripping levels.
- Script start-up and memory per context. MoonSharp retains 79.5 KB per
  `Script` with [fork PR #3](https://github.com/emindeniz99/moonsharp/pull/3) (2.31 MB before). xLua-NG's own notes report about
  1.4–1.6 ms per `LuaEnv` create and dispose on its hardware; not measured here.
