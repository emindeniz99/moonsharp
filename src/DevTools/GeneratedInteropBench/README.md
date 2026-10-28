# Generated interop benchmark

Lua calling a C# method (`obj:Add(i, 2)`) and reading a property (`obj.Value`)
through three descriptors of the same type: reflection
(`InteropAccessMode.Reflection`, what IL2CPP and other AOT targets use),
`LazyOptimized` (the JIT default, compiled expression trees) and the
descriptor MoonSharp.SourceGenerator writes. It reports the median time and the
exact bytes allocated per iteration. The `loop` row is the same Lua loop without
interop, the floor under every other row.

The same program runs under the JIT and as a NativeAOT executable, the
closest stand-in for IL2CPP available without Unity:

```sh
dotnet run -c Release --project src/DevTools/GeneratedInteropBench -- 7
dotnet publish -c Release -r linux-x64 -p:Aot=true src/DevTools/GeneratedInteropBench -o out/aot
out/aot/GeneratedInteropBench 7
```

NativeAOT needs clang and zlib (see `mise.toml`). Under NativeAOT, `LazyOptimized`
interprets its expression trees instead of compiling them.

`Calc` has 20 members, so it is also the sample for the size of generated
code: build or publish once more with `-p:NoGenerator=true` (no generator, no
generated descriptor) and compare the files.

Run timings one at a time on a quiet machine, and report the machine, OS,
.NET version and commit with any numbers.
