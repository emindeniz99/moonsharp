# Generated userdata descriptors

`src/MoonSharp.SourceGenerator` is a Roslyn source generator. At compile time
it writes a userdata descriptor for each C# type you mark, so that Lua calls
the type's members directly instead of through reflection. It matters most
where MoonSharp cannot compile its fast paths at run time: IL2CPP, NativeAOT
and other AOT targets, where every member access otherwise goes through
`MethodBase.Invoke` with an `object[]` of boxed arguments.

Nothing changes for code that does not use it. The interpreter gains one
attribute, `MoonSharpUserDataTypes`, and nothing else. The netstandard2.0
Release DLL grows in 512-byte steps: 0 bytes at `fab3ca7` (404,480 both),
512 bytes at `2afebc9` (396,288 to 396,800).

## Use

1. Reference the generator as an analyzer.
   - Unity: the UPM package ships it in `Runtime/Analyzers` with the
     `RoslynAnalyzer` label. It runs for every assembly that references
     `MoonSharp.Interpreter`.
   - .NET: `<ProjectReference Include="path/MoonSharp.SourceGenerator.csproj"
     OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`. For a
     `net4x` project add `SetTargetFramework="TargetFramework=netstandard2.0"`
     and `SkipGetTargetFrameworkProperties="true"`.
2. Choose the types. Mark your own with `[MoonSharpUserData]`, and list types
   you do not own at assembly level:

   ```csharp
   [assembly: MoonSharpUserDataTypes(typeof(Vector2), typeof(List<int>))]
   ```

3. Register them once at start-up, before anything else registers the same
   types (the first registration of a type wins):

   ```csharp
   MoonSharp.Generated.GeneratedUserData.RegisterAll();
   // or one at a time:
   UserData.RegisterType(MoonSharp.Generated.GeneratedUserData.CreateDescriptor(typeof(Player)));
   ```

   `GeneratedUserData` is internal to each assembly, so call it from code in
   that assembly. There is no module initializer and no
   `[RuntimeInitializeOnLoadMethod]`: following the decision that mobile hosts
   register only the types they choose, registration stays an explicit call.
   In Unity, make that call from your own start-up code.
   `UserData.RegisterAssembly()` after it still registers the remaining
   `[MoonSharpUserData]` types by reflection.

Scripts see no difference. The descriptor has the same members, names and
overloads as the reflection one, and the same rules apply: overload
resolution, extension methods, fuzzy names, metamethods, `__new`, nested
types, `[MoonSharpHidden]`/`[MoonSharpVisible]`, `[MoonSharpHideMember]`,
custom converters and `ExactInt64Interop`. The LuaCATS generator
([luacats.md](luacats.md)) reads the registered descriptor, so its
declarations do not change either.

## What is generated

For each type, one class derived from `DispatchingUserDataDescriptor`, with
one nested class for all its methods and one for all its fields and
properties. Each nested class picks the member with a `switch` on a member
number, so a type costs three classes however many members it has. xlua-ng
measured about 8% less IL2CPP output for one switch per type than for one
class per member, the shape MoonSharp's Hardwire uses.

A method body converts each argument with a typed helper, calls the member
and converts the result:

```csharp
case 4: // int Add(int a, int b)
{
    int i0 = j++;
    int a0 = ToInt32(args, i0);
    int i1 = j++;
    int a1 = ToInt32(args, i1);
    var r = ((Calc)obj).Add(a0, a1);
    return ReturnInt32(context, script, r);
}
```

`ToInt32` reads a Lua number in place on the value stack
(`CallbackArguments.TypeAt` and `NumberAt`) when no custom converter is
registered for it, and otherwise calls `DynValue.ToObject(typeof(int))` on
`args.RawGet(i, false) ?? DynValue.Void`, the conversion reflection uses.
Strings, booleans, tables, Lua functions, userdata and enums are read in place
the same way. Results and error messages are therefore the same as with
reflection (the tests below compare them). A number or string result goes
back through `ScriptExecutionContext.Return`, which hands it to the VM
without a `DynValue`; other results are built without boxing where they are
primitive and go through `DynValue.FromObject` otherwise. A method call from
Lua with such arguments and result allocates nothing (`AllocationBudgetTests`,
`clr_interop_generated`).

Members are added to the descriptor with `AddMember`, as
`StandardUserDataDescriptor` adds them, so MoonSharp's own overload
resolution chooses between overloads. A choice generated at compile time
would copy MoonSharp's weights into the generator and go wrong as soon as a
host registers a custom converter, which changes the weights at run time.
Instead the overload set (generated or reflected) keeps a table from
argument signature to overload: the kinds of the arguments (nil, boolean,
whole or fractional number, string, Lua or C# function), filled from the
weights on the first call with each signature. While a script-to-CLR
converter is registered, or an extension method of the same name applies,
and for table and userdata arguments, it resolves as before (scoring, with a
cache of recent signatures that resets when converters change). The
`overload_call` interop workload runs at 0.955 of `next` (`ab-interop`, 21
rounds, interquartile range 0.887 to 0.987).
Member names are looked up in the descriptor's dictionary, where the bound
callback cache of `obj:Method()` calls also lives.

### Lua functions as delegates

A script can pass a Lua function where C# takes a delegate whose `Invoke` has
up to four parameters, none by reference (`Closure.ToDelegate`, see
[MIGRATING.md](MIGRATING.md)). Without help, the interpreter builds such a
delegate by closing a generic method with `MakeGenericMethod`, which an AOT
compiler has no code for when the delegate has value-type parameters or
result: NativeAOT reports "missing native code" (IL2CPP before full generic
sharing fails the same way). So `GeneratedUserData` also gets
`RegisterDelegateTypes()`, called by `RegisterAll()`, with one typed factory
per delegate type that a generated member takes as a parameter or sets as a
value, and per delegate type listed with
`[assembly: MoonSharpUserDataTypes]`:

```csharp
global::MoonSharp.Interpreter.UserData.RegisterDelegateType<global::System.Action<int>>(
    f => new global::System.Action<int>((a0) => { f.Call<int, global::MoonSharp.Interpreter.DynValue>(a0); }));
```

The factory calls the function through the typed calls of `Closure`, which
the AOT compiler sees with their concrete types. List the delegate types
the host converts itself (`closure.ToDelegate<Func<int, int>>()`). A listed
delegate type gets this factory instead of a userdata registration; one with
more than four parameters, or one by reference, gets warning MSUD002.

### Structs

A struct userdata holds a boxed value, and reflection changes that box in
place (`v.x = 1`, `v:Normalize()`). Safe C# cannot write into a box, so:

- a `partial` struct (and partial containing types) gets a generated
  interface implementation, and the descriptor calls its members through the
  box: in place, typed;
- reads (fields, property getters) and `readonly` members work on a copy;
- overrides of `object` methods and interface implementations are called on
  the box through the base type or interface: in place;
- every other member that may change the struct (setters, non-readonly
  methods of a struct the assembly cannot make partial) is bound by
  reflection, with warning MSUD001.

Property getters are assumed not to change the struct, as the optimized
reflection modes also assume.

### Reflection fallback

A member C# cannot call from generated code keeps a reflection descriptor,
and the build reports why (MSUD001): methods returning by reference, ref
struct parameters, init-only setters, members that are not accessible from
the generated code (for example `[MoonSharpVisible(true)]` on a private
member), operators C# cannot write (`op_True`, `op_Increment`) and events.
On IL2CPP or a trimmed NativeAOT app those members must survive stripping
(`link.xml` or a trimming descriptor).

A type gets no descriptor (MSUD002) when it is not accessible from the
generated code, is an open generic type (list closed ones with
`MoonSharpUserDataTypes`), a delegate, an array, a pointer or a ref struct.
Enums and `IUserDataType` types need no generated code: `RegisterAll`
registers them with MoonSharp's own descriptors.

A type from another assembly is described as compiled against: if the
runtime's version of the type has more members (a newer BCL), scripts do not
see them.

## Roslyn version

| Unity | Manual asks for | Compiler it ships (measured by xlua-ng) |
|---|---|---|
| 2021.3 | Microsoft.CodeAnalysis 3.8, `ISourceGenerator` | 4.3.1 in 2021.3.45f2 |
| 2022.3 | Microsoft.CodeAnalysis 3.8, `ISourceGenerator` | 4.3.1 in 2022.3.62f3 |
| Unity 6 | Microsoft.CodeAnalysis.CSharp 4.3 | 4.3.1 in 6000.0 to 6000.3, 4.10.0 in 6000.6 |

A generator built against a newer compiler than the one running it does not
load (warning CS9057, no output), while one built against an older compiler
loads in every newer one. The generator therefore references
Microsoft.CodeAnalysis 3.8, the lowest version any supported Unity line
documents, and implements `ISourceGenerator`, since `IIncrementalGenerator`
and `ForAttributeWithMetadataName` arrived in 4.0 and 4.3. Two builds (3.8
and 4.3) are not an option: a newer editor would load both and emit every
type twice. Roslyn deprecates `ISourceGenerator` in favour of incremental
generators, but still runs it; Unity compiles whole assemblies, so the
incremental pipeline would mostly help IDEs. Revisit when Unity's manuals
for every supported line ask for 4.x.

`tools/sourcegen/check-roslyn.sh` compiles `tools/sourcegen/Sample.cs`, which
calls `GeneratedUserData.RegisterAll()`, with csc 3.8.0 and 4.3.1 (pinned
compiler packages). Generated code is C# 7.3.

## Tests

- `InteropParityTests`: the same probe script, and a list of error cases,
  under every `InteropAccessMode` and under generated descriptors; results and
  error texts must match. The probes cover a partial struct, a struct the
  generator cannot extend, a readonly struct, `ref`/`out` parameters,
  `params`, optional parameters, overloads and C# exceptions.
- `GeneratedDescriptorTests`:
  - for 52 test types, the generated descriptor lists the same members as
    `StandardUserDataDescriptor`, with the same kind, access, static-ness and
    overload signatures;
  - a probe over BCL types (`List<int>`, `Dictionary<string, int>`,
    `DateTime`, `TimeSpan`, `Version`) gives the same results as reflection;
  - 20 interop fixtures run a second time with a registration policy that
    swaps in the generated descriptor for every type they register.
- `ClosureDelegateTests` register the generated delegate factories
  (`RegisterDelegateTypes`) and convert Lua functions to `Action<int>`,
  `Func<double, double>`, `Func<int, bool, string>` and a custom delegate.
  Without the factories, 4 of them failed under NativeAOT with "missing
  native code"; with them, all pass. `tools/sourcegen/check-roslyn.sh`
  compiles a sample with a delegate parameter.
- All of this runs in the three test runners (.NET, Mono with its hardwire
  policy, and `EmbeddedTestRunner`, which compiles the tests as Unity does).
  The Unity matrix (`.github/workflows/unity.yml`) compiles the same tests
  with the generator from the UPM package in each editor; it has not run
  yet (it needs the licence secrets).

## Measurements

`src/DevTools/GeneratedInteropBench`, 2026-10-28, commit `e0c1ed4`, Linux x64,
Intel Xeon 2.1 GHz (4 cores, shared machine), .NET 8.0.33, median of 7 runs of
one million iterations. `loop` is the same Lua loop without interop; subtract
it to get the interop part.

| Case | JIT ns | JIT bytes | NativeAOT ns | NativeAOT bytes |
|---|---:|---:|---:|---:|
| loop (no interop) | 121.5 | 192 | 132.7 | 192 |
| `obj:Add(i, 2)`, reflection | 467.3 | 616 | 854.5 | 616 |
| `obj:Add(i, 2)`, LazyOptimized | 397.6 | 616 | 846.8 | 616 |
| `obj:Add(i, 2)`, generated | 337.7 | 504 | 425.4 | 504 |
| `obj.Value`, reflection | 259.9 | 312 | 322.6 | 312 |
| `obj.Value`, LazyOptimized | 220.8 | 312 | 318.3 | 312 |
| `obj.Value`, generated | 200.4 | 288 | 229.2 | 288 |

Under NativeAOT, the stand-in for IL2CPP, the interop part of a method call
drops from 722 to 293 ns (2.5x) and of a property read from 190 to 97 ns
(2.0x); under the JIT from 346 to 216 ns and from 138 to 79 ns. Each call
allocates 112 bytes less (no `object[]`, no boxed arguments or result), each
read 24 bytes less. What remains is MoonSharp's callback path, the same for
every descriptor: the `CallbackArguments`, the result `DynValue` and the
loop's own number values.

Code size, for `Calc` in the benchmark (19 declared members, plus the
accessor and `object` methods reflection also exposes):

| | without generator | with generator | difference |
|---|---:|---:|---:|
| IL assembly | 10,240 B | 16,896 B | +6,656 B |
| NativeAOT executable | 4,509,424 B | 4,529,952 B | +20,528 B |

That is about 1 KB of native code per member on x64 NativeAOT; IL2CPP will
differ, and only a Unity player build can measure it.

The same benchmark on branch `feat/typed-calls` (2026-10-28, after `next`
gained the inline caches; load average 6 to 11 from other work, so only
large differences count) also calls C# from Lua through an overloaded
method and calls Lua from C#, two runs each:

| Case | JIT ns | NativeAOT ns | bytes |
|---|---:|---:|---:|
| `obj:Pick(i)` (int or string overload), generated | 261, 277 | 339, 278 | 240 |
| `obj:Add(i, 2)`, generated | 286, 295 | 310, 275 | 248 |
| C# calls `function(a, b, c) return a * b + c end`, `Script.Call` with `DynValue`s | 346, 351 | 375, 317 | 224 |
| the same, `Closure.Call<double, double, double, double>` | 205, 212 | 281, 238 | 0 |

On branch `perf/call-path` (2026-10-29, against `next` 5761ae2, two runs each,
both builds on the same machine one after the other under the benchmark lock;
ns per iteration):

| Case | JIT `next` | JIT branch | NativeAOT `next` | NativeAOT branch | bytes `next` | bytes branch |
|---|---:|---:|---:|---:|---:|---:|
| `obj:Add(i, 2)`, generated | 177, 181 | 113, 119 | 190, 191 | 130, 120 | 248 | 0 |
| `obj:Pick(i)` (int or string overload), generated | 176, 169 | 120, 111 | 176, 178 | 129, 126 | 240 | 0 |
| `obj:Add(i, 2)`, reflection | 256, 264 | 214, 212 | 452, 450 | 401, 382 | 360 | 192 |
| `obj.Value`, generated | 60, 63 | 61, 62 | 61, 65 | 57, 59 | 40 | 40 |
| `Script.Call` with `DynValue`s | 233, 221 | 172, 163 | 223, 212 | 170, 166 | 224 | 128 |

## Follow-ups

- Run the Unity matrix once the secrets exist: it is the first real proof
  that 2021.3, 2022.3 and 6000.0 load the generator from the package and that
  IL2CPP players run the generated code.
- Delegate factories for `void` delegates discard the result through
  `Call<..., DynValue>`, which creates a `DynValue` when the function returns
  a string, table or large number; the interpreter's own bridge does not.
- Property and field reads (`obj.Value`) still return a new `DynValue` for
  a number, string or table: the index path has no context to return through.
- Emit a `link.xml` fragment for the members bound by reflection, once
  Unity's linker can be fed one from an analyzer (an Editor build callback).
