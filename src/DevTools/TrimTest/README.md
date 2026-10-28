# Trim smoke test

Checks that MoonSharp still has its standard library after a trimmed browser-WebAssembly
publish, and that `new Script(CoreModules.Preset_Complete)` works in a browser.

```sh
src/DevTools/TrimTest/run-smoke.sh
```

It publishes `TrimTest.csproj` (.NET 10, `Microsoft.NET.Sdk.WebAssembly`, `TrimMode=full`),
serves the output from a small node server, and loads it in headless Chromium through
Playwright. It prints the size of the published `MoonSharp.Interpreter*.wasm` (raw and
brotli) and exits non-zero unless the page reports the expected results.

MoonSharp is **not** passed as a `TrimmerRootAssembly`. The library has to keep what it
needs by itself, through the `ILLink.Descriptors.xml` embedded in the assembly. Extra
arguments go to `dotnet publish`, so `-p:RootMoonSharp=true` roots the whole assembly
for a size comparison.

Needs the .NET 10 SDK, node, and Playwright with a Chromium build
(`npm i -g playwright`). It is not part of the CI workflows.
