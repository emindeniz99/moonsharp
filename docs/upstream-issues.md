# Upstream issues

What this fork does about every open issue and every open pull request on
[moonsharp-devs/moonsharp](https://github.com/moonsharp-devs/moonsharp). It records findings so nobody has to check them
again; planned work goes in [ROADMAP.md](ROADMAP.md), and fork-only changes in
[upstream.md](upstream.md).

- Checked on 2026-10-28 on branch `fix/upstream-issues` (based on `next`).
  Upstream `master` is still the fork base `fbd0367`.
- Source: the github.com issue and pull request pages (the API was not
  reachable from the session), pull request code from `refs/pull/N/head`.
- Each bug was run on the fork, from the issue's own script when it had one.
  Tests live in `src/MoonSharp.Interpreter.Tests/EndToEnd/UpstreamIssueTests.cs`,
  named after the issue. A bug in code that other work owned at the time has
  an ignored test that fails without the `Ignore`; the fix removes it.
- To update: change the row, give a commit or test as evidence, and compare
  the open lists on github.com with this file.

## Summary

| Status | Issues | Pull requests |
|---|---|---|
| fixed in the fork | 16 | 5 |
| reproduces, waits for other work | 10 | 4 |
| needs the owner | 5 | 2 |
| does not reproduce | 16 | 3 |
| question, answered | 8 | 0 |
| feature (not a bug) | 19 | 5 |
| out of scope | 18 | 3 |
| total | 92 | 22 |

Fixed on branch `fix/upstream-issues`: #70, #102 (with #305, #328), #161, #173, #219 (with #257),
#229 (`tonumber`), #286, #291, #307. Fixed on branch `perf/vm-group-a`: #229 (arithmetic),
#258, #263. Fixed earlier in the fork: #221, #304, #348,
and #300 in part (opt-in 64-bit integers). Two JSON bugs found by the
conformance corpus have no upstream issue: `\u` escapes in `json.parse`
(`5788be3`) and `json.serialize` dropping string keys of a table that also has
keys 1..n (`77c8662`).

## Decisions for the owner

- **#293, #294: overload scores.** A Lua number weighs the same (99) for every
  integer type and `float`, so `F(1.5)` calls `F(int)` when that overload comes
  first. Scoring a fractional number lower for integer parameters fixes it, but
  the overload cache keys on argument types only, so the cache would also have to
  record whether a number is integral, a check on every cached call. #294 asks
  that an exact match beat a custom converter (both 100); lowering the converter
  weight changes which overload hosts with number converters get. The website
  calls the heuristic "dark magic" and asks for calibration reports.
- **#175 (PR #336): static members of base classes.** `BindingFlags.FlattenHierarchy`
  makes `Derived.StaticBase()` work as in C#, but also adds `object.Equals(a, b)`,
  `ReferenceEquals` and `Enum`'s statics to every descriptor, which changes
  member lists, Hardwire output and generated LuaCATS definitions. Options: flatten
  everything, flatten except the framework base types, or keep it.
- **#274: automatic registration.** With `InteropRegistrationPolicy.Automatic`,
  lists and dictionaries become userdata instead of the documented tables.
- **#181: per-script registries.** Useful to run scripts with different trust
  levels in one process; it replaces the static `UserData` API.
- **PR #321: Unity defines.** The `#if UNITY_5` paths are dead on Unity 2017 and
  later; switching them on needs the Unity CI matrix first.

## Issues

| Issue | Title | Type | Status in the fork | Evidence |
|---|---|---|---|---|
| [#348](https://github.com/moonsharp-devs/moonsharp/issues/348) | Preserve source locations in Script.Dump / LoadStream bytecode | feature | fixed | Fork [PR #6](https://github.com/emindeniz99/moonsharp/pull/6) (dump format 0x152, old dumps still load) |
| [#332](https://github.com/moonsharp-devs/moonsharp/issues/332) | No module found | bug | waits: module registration | The report mixes invalid Lua (`let`) and Unity's Resources loader, but one part reproduces: `require` ignores `package.path` and reads only the `LUA_PATH` global and `ScriptLoader.ModulePaths`. Ignored test `UpstreamIssueTests.Issue332_RequireUsesPackagePath` |
| [#330](https://github.com/moonsharp-devs/moonsharp/issues/330) | API Reference link returns 404 | docs | out of scope | Upstream website |
| [#328](https://github.com/moonsharp-devs/moonsharp/issues/328) | Chaining Extension Methods only works after running the script again | bug | fixed | `099a347` (same cause as #102); `Issue102_EveryRegisteredExtensionMethodIsFound` |
| [#319](https://github.com/moonsharp-devs/moonsharp/issues/319) | json.parse fails on negative integers | bug | does not reproduce | `json.parse('{"foo": -1}')` and `'[-1, -2.5e3]'` parse (probe on this branch) |
| [#318](https://github.com/moonsharp-devs/moonsharp/issues/318) | KopiLua string.gsub() "pattern too complex" error | bug | waits: string library | Reproduces: `'^%s*(.-)%s*$'` on a 300-character string. Ignored test `UpstreamIssueTests.Issue318_TrimIdiomWorksOnLongStrings` |
| [#317](https://github.com/moonsharp-devs/moonsharp/issues/317) | Unexpected result on string XOR | bug | does not reproduce | The issue's script returns `test1234567890`. Related, not reported: `string.char(300)` gives `?` where Lua raises "value out of range" |
| [#315](https://github.com/moonsharp-devs/moonsharp/issues/315) | Lua code can set table[0], but C# can't read it | bug | waits: table | Reproduces: `Table.Get(0)` and `Get(-1)` return nil. Ignored test `UpstreamIssueTests.Issue315_TableGetReadsZeroAndNegativeKeys` |
| [#313](https://github.com/moonsharp-devs/moonsharp/issues/313) | Watch Item Location | bug | does not reproduce | `ScriptRuntimeException.CallStack` has a location for every frame of the issue's script |
| [#312](https://github.com/moonsharp-devs/moonsharp/issues/312) | Feature Request: Increased Error Data | feature | feature: planned | `InterpreterException.CallStack` already gives the frames and locations; naming the variable in runtime errors is on the roadmap |
| [#310](https://github.com/moonsharp-devs/moonsharp/issues/310) | Can we make a C# method that "Yield requests" but also returns a value to lua? | question | answered | Works: a callback returns `DynValue.NewYieldReq(...)`, and the values passed to the next `Resume` become the call's results (checked on this branch). Not in our docs |
| [#308](https://github.com/moonsharp-devs/moonsharp/issues/308) | ScriptRuntimeException thrown from Table.ResolveMultipleKeys has incorrectly formatted error message | bug | waits: table | Reproduces (`Key '{0}' did not point to anything`). Ignored test `UpstreamIssueTests.Issue308_MultiKeyGetNamesTheMissingKey`; upstream [PR #355](https://github.com/moonsharp-devs/moonsharp/pull/355) |
| [#307](https://github.com/moonsharp-devs/moonsharp/issues/307) | Returning null from callback invoked in CallbackFunction causes NullReferenceException | bug | fixed | `5cf4dfb`; `Issue307_CallbackReturningNullGivesNil` |
| [#306](https://github.com/moonsharp-devs/moonsharp/issues/306) | Support table constructor/object initializer on created CLR objects | feature | feature: not planned | No roadmap item; `PropertyTableAssigner` already does this for host-side configuration |
| [#305](https://github.com/moonsharp-devs/moonsharp/issues/305) | Cache issue Userdata.ExtensionType? | bug | fixed | `099a347` (same cause as #102) |
| [#304](https://github.com/moonsharp-devs/moonsharp/issues/304) | Creating coroutines creates 2 megabytes of garbage | bug | fixed | Fork [PR #3](https://github.com/emindeniz99/moonsharp/pull/3): a coroutine allocates 2.9 KB |
| [#302](https://github.com/moonsharp-devs/moonsharp/issues/302) | MissingMethodException: Default constructor not found for type System.Diagnostics.SystemDiagnosticsSection | bug | out of scope | Unity player with stripping; `UnityAssetsScriptLoader` still calls `Debug.WriteLine` in its error path. Needs the Unity CI matrix |
| [#300](https://github.com/moonsharp-devs/moonsharp/issues/300) | Support to Lua 5.3, especially integers | feature | feature: partly done | Opt-in `int64`/`uint64` values ([int64.md](int64.md)); a number subtype is a roadmap decision for after the struct-value step |
| [#298](https://github.com/moonsharp-devs/moonsharp/issues/298) | Post reference document as html | docs | out of scope | Upstream website |
| [#297](https://github.com/moonsharp-devs/moonsharp/issues/297) | Moonsharp VS Code debugger launch.json documentation incorrect | docs | out of scope | VS Code extension, unchanged in the fork; upstream [PR #347](https://github.com/moonsharp-devs/moonsharp/pull/347) |
| [#294](https://github.com/moonsharp-devs/moonsharp/issues/294) | Shouldn't EXACT_MATCH score higher than CUSTOM_CONVERTER_MATCH? | bug | needs owner | Both weights are 100 (`ScriptToClrConversions.cs`). Lowering the converter weight makes exact matches win, but also changes which overload a number picks when a host registered a number converter. See below |
| [#293](https://github.com/moonsharp-devs/moonsharp/issues/293) | Docs say heuristic should resolve float/int overloads properly; actually does not | bug | needs owner | Reproduces: with `F(int)` and `F(float)`, `F(1.5)` calls `F(int)` (both weigh 99). See below |
| [#291](https://github.com/moonsharp-devs/moonsharp/issues/291) | System.NullReferenceException on registering DynamicMethod | bug | fixed | `c86a064`; `Issue291_CompiledExpressionDelegateIsCallable` |
| [#288](https://github.com/moonsharp-devs/moonsharp/issues/288) | CSharp.lua error | bug | does not reproduce | No reproduction given; `require('Core.Sys')` through `LUA_PATH` with a dotted directory resolves |
| [#287](https://github.com/moonsharp-devs/moonsharp/issues/287) | Empty tuples are incorrectly treated as nil | bug | waits: VM core | Reproduces: `select('#', table.unpack({}, 1, 0))` is 1. Ignored test `UpstreamIssueTests.Issue287_EmptyUnpackReturnsNoValues` |
| [#286](https://github.com/moonsharp-devs/moonsharp/issues/286) | Cannot register type SqlCommand | bug | fixed | `34c66d4`; `Issue286_MembersHiddenWithNewUseTheDerivedMember` |
| [#285](https://github.com/moonsharp-devs/moonsharp/issues/285) | The package table is missing most of its fields | bug | waits: module registration | Reproduces: `package` has only `loaded` and `config`; `package.preload` is ignored. Ignored test `UpstreamIssueTests.Issue285_RequireUsesPackagePreload` |
| [#282](https://github.com/moonsharp-devs/moonsharp/issues/282) | JSON parse error for "\/" | duplicate | does not reproduce | Fixed upstream in 714c265, before the fork base |
| [#279](https://github.com/moonsharp-devs/moonsharp/issues/279) | [Help] Doubt about using a moon function as a parameter | question | answered | Answered in the thread (store the `DynValue`/`Closure`, call it later). Not in our docs |
| [#275](https://github.com/moonsharp-devs/moonsharp/issues/275) | string.format('%s', nil) throws an exception. | bug | waits: string library | Reproduces for nil and tables. Ignored test `UpstreamIssueTests.Issue275_FormatPercentSAcceptsAnyValue` |
| [#274](https://github.com/moonsharp-devs/moonsharp/issues/274) | CLR types don't get converted correctly when allowing auto-registration of userdata | bug | needs owner | Reproduces: with `InteropRegistrationPolicy.Automatic`, `List<int>` and `Dictionary<,>` become userdata instead of tables. The fork's mobile hosts register types explicitly, so the choice matters only for automatic hosts |
| [#270](https://github.com/moonsharp-devs/moonsharp/issues/270) | Json is not in indent | enhancement | feature: not planned | No roadmap item |
| [#265](https://github.com/moonsharp-devs/moonsharp/issues/265) | Sync Unity code files Powershell script version | enhancement | out of scope | Unity packaging goes through `tools/upm` |
| [#263](https://github.com/moonsharp-devs/moonsharp/issues/263) | unassigned local variable can sometimes hold values other than nil | bug | fixed | `local x` compiles as `local x = nil`; `UpstreamIssueTests.Issue263_LocalWithoutValueIsNil` |
| [#261](https://github.com/moonsharp-devs/moonsharp/issues/261) | Debugger Signal(Byte\|Source)CodeChange should use GetDebuggerCaps() | enhancement | feature: worth adding | A two-line check in `Script.cs`, which the parallel VM and registration work owns now |
| [#258](https://github.com/moonsharp-devs/moonsharp/issues/258) | lua 'type' function called with void variable error | bug | fixed | A variable assigned no value holds nil; `UpstreamIssueTests.Issue258_LocalFromVoidCallIsNil` |
| [#257](https://github.com/moonsharp-devs/moonsharp/issues/257) | Json parse error | duplicate | fixed | `103a2d4` (duplicate of #219) |
| [#255](https://github.com/moonsharp-devs/moonsharp/issues/255) | Hardwiring throws errors | bug | does not reproduce | The errors are only in screenshots; not reproduced |
| [#253](https://github.com/moonsharp-devs/moonsharp/issues/253) | Custom return types and error help | question | answered | A returned CLR type must be registered (`UserData.RegisterType`); [registration.md](registration.md) explains registration |
| [#245](https://github.com/moonsharp-devs/moonsharp/issues/245) | Add support for `__name` metaproperty | feature | feature: not planned | Lua 5.3 behaviour; the fork targets Lua 5.2 |
| [#244](https://github.com/moonsharp-devs/moonsharp/issues/244) | Implement support for debugging with source maps | feature | feature: planned | Fits the roadmap's Debug Adapter Protocol server |
| [#243](https://github.com/moonsharp-devs/moonsharp/issues/243) | Debug multiple scripts simultaneously | feature | feature: planned | Fits the roadmap's Debug Adapter Protocol server |
| [#241](https://github.com/moonsharp-devs/moonsharp/issues/241) | Bytecode loading error | bug | does not reproduce | Details only on a mailing list; dump and load round-trip (fork [PR #6](https://github.com/emindeniz99/moonsharp/pull/6) changed the format) |
| [#240](https://github.com/moonsharp-devs/moonsharp/issues/240) | "debug" module is not available on the Unity | bug | answered | By design: `new Script()` uses `Preset_Default`, which leaves `debug` out; `Preset_Complete` has it |
| [#235](https://github.com/moonsharp-devs/moonsharp/issues/235) | Add .lua file import support for Unity | feature | feature: worth adding | A `ScriptedImporter` for `.lua`; belongs with the Unity package and CI matrix work |
| [#232](https://github.com/moonsharp-devs/moonsharp/issues/232) | System.IndexOutOfRangeException randomly when calling Closure | bug | does not reproduce | iOS only, no reproduction. A stack overflow (#221, fixed) or a Script used from two threads gives this exception |
| [#231](https://github.com/moonsharp-devs/moonsharp/issues/231) | Script timeout/cancellation support | feature | feature: planned | Coroutines have `AutoYieldCounter`; an execution budget for plain calls is on the roadmap |
| [#229](https://github.com/moonsharp-devs/moonsharp/issues/229) | tonumber("0x23") does not work | bug | fixed | `8a4ca33` for `tonumber`; string coercion in arithmetic on `perf/vm-group-a` (`UpstreamIssueTests.Issue229_ArithmeticCoercesLuaNumerals`) |
| [#228](https://github.com/moonsharp-devs/moonsharp/issues/228) | Async / await support | feature | feature: not planned | `AsyncExtensions` covers calling Lua from async C#; awaiting a `Task` from Lua is not on the roadmap |
| [#221](https://github.com/moonsharp-devs/moonsharp/issues/221) | MoonSharp does not catch stack overflows | bug | fixed | Fork [PR #1](https://github.com/emindeniz99/moonsharp/pull/1) |
| [#219](https://github.com/moonsharp-devs/moonsharp/issues/219) | json.parse lexer fails on 2D arrays | bug | fixed | `103a2d4`; `Issue219_JsonParsesNestedArrays` |
| [#218](https://github.com/moonsharp-devs/moonsharp/issues/218) | System.IndexOutOfRangeException | bug | does not reproduce | The nested-callback script now reports the inner exception |
| [#213](https://github.com/moonsharp-devs/moonsharp/issues/213) | Lowercase Descriptors | enhancement | feature: not planned | `FuzzySymbolMatching` covers camelCase and PascalCase, not all-lowercase names |
| [#212](https://github.com/moonsharp-devs/moonsharp/issues/212) | Unity 2017.1 WebGL build crashes on Lua script run | bug | out of scope | Needs a Unity WebGL player; upstream [PR #146](https://github.com/moonsharp-devs/moonsharp/pull/146) |
| [#210](https://github.com/moonsharp-devs/moonsharp/issues/210) | Website reverses BackgroundOptimized and Preoptimized | docs | out of scope | Upstream website |
| [#209](https://github.com/moonsharp-devs/moonsharp/issues/209) | UserData.RegisterAssembly() does not work in .Net Standard | bug | does not reproduce | `UserData.RegisterAssembly()` works on .NET 8 |
| [#208](https://github.com/moonsharp-devs/moonsharp/issues/208) | Unable to create instance of C# classes | question | does not reproduce | `Matrix.__new()` on a registered generic type works |
| [#207](https://github.com/moonsharp-devs/moonsharp/issues/207) | Script organization like Grimrock | question | answered | Separate global tables per entity (`DoString(code, env)`); not in our docs |
| [#204](https://github.com/moonsharp-devs/moonsharp/issues/204) | json.parse() and then json.serialize() turns empty array to empty object. | bug | does not reproduce | `json.parse(s, true)` keeps empty arrays (`JsonEmptyArray`); without it Lua cannot tell `[]` from `{}` |
| [#199](https://github.com/moonsharp-devs/moonsharp/issues/199) | Table.Get does not respect __index? | question | answered | `Table.Get` is a raw read; there is no metamethod-aware read in the C# API. Not in our docs |
| [#198](https://github.com/moonsharp-devs/moonsharp/issues/198) | Bug in Varargs when no parameters is passed to a function | bug | waits: VM core | Reproduces. Ignored test `UpstreamIssueTests.Issue198_VarargsWithoutArgumentsAreEmpty`; upstream [PR #324](https://github.com/moonsharp-devs/moonsharp/pull/324) |
| [#197](https://github.com/moonsharp-devs/moonsharp/issues/197) | VS Code debugger not working properly since 1.13 update | bug | out of scope | VS Code extension, unchanged in the fork |
| [#196](https://github.com/moonsharp-devs/moonsharp/issues/196) | Support for .Net 4.5 portable profile 111 | feature | out of scope | PCL profiles are gone; the interpreter targets netstandard2.0 and net45 |
| [#192](https://github.com/moonsharp-devs/moonsharp/issues/192) | CTRL-Z crashes with unhandled exception | bug | does not reproduce | The REPL loop checks for end of input (`src/MoonSharp/Program.cs`) |
| [#190](https://github.com/moonsharp-devs/moonsharp/issues/190) | Optional parameters / params list | feature | does not reproduce | Optional parameters and `params` arrays work (`o.M(1)`, `o.P(1,2,3)`, checked on this branch) |
| [#187](https://github.com/moonsharp-devs/moonsharp/issues/187) | unicode replace string bug? | bug | waits: string library | Reproduces. Ignored test `UpstreamIssueTests.Issue187_PatternsCompareWholeCharacters`; upstream [PR #184](https://github.com/moonsharp-devs/moonsharp/pull/184) |
| [#186](https://github.com/moonsharp-devs/moonsharp/issues/186) | ScriptRuntimeException: cannot convert a function to a clr type System.Action | question | feature: worth adding | Reproduces: a Lua function does not convert to `Action` or `Func<>`. Fits the roadmap's typed calls from C# to Lua |
| [#185](https://github.com/moonsharp-devs/moonsharp/issues/185) | loadstring() not usable | question | answered | Lua 5.2 removed `loadstring`; `load` works |
| [#183](https://github.com/moonsharp-devs/moonsharp/issues/183) | Proxy operator overloads | feature | feature: not planned | No roadmap item |
| [#181](https://github.com/moonsharp-devs/moonsharp/issues/181) | Non-global TypeDescriptorRegistry and ExtensionMethodsRegistry | feature | needs owner | Per-script registration would let one process run scripts with different trust levels; it changes the static `UserData` API |
| [#180](https://github.com/moonsharp-devs/moonsharp/issues/180) | JsonTableConverter.JsonToTable can't handle escaped forward slash | bug | does not reproduce | Fixed upstream in 714c265, before the fork base |
| [#175](https://github.com/moonsharp-devs/moonsharp/issues/175) | Inheritance for static objects | question | needs owner | Reproduces: `CreateStatic<Derived>()` does not see `Base`'s static methods. See below |
| [#169](https://github.com/moonsharp-devs/moonsharp/issues/169) | Winrt (Windows Store App) not working with version 2.0 | bug | out of scope | Windows Store 8.1 and PCL builds are gone |
| [#161](https://github.com/moonsharp-devs/moonsharp/issues/161) | io.write() does not print standard console | bug | fixed | `d02864b`; `Issue161_StandardOutputIsWrittenWithoutFlush` |
| [#160](https://github.com/moonsharp-devs/moonsharp/issues/160) | TableTestReverseSafer example does not work | docs | out of scope | Upstream website sample |
| [#158](https://github.com/moonsharp-devs/moonsharp/issues/158) | Hardwire some type error | bug | does not reproduce | Delegate types get no constructors (`StandardUserDataDescriptor.FillMemberList`), so no such descriptor is generated |
| [#155](https://github.com/moonsharp-devs/moonsharp/issues/155) | Table extension points | feature | feature: not planned | Conflicts with the table rewrite (struct values, array part) |
| [#137](https://github.com/moonsharp-devs/moonsharp/issues/137) | debug.traceback() ? | question | answered | `debug.traceback` works with `Preset_Complete`; C# gets frames from `InterpreterException.CallStack` |
| [#136](https://github.com/moonsharp-devs/moonsharp/issues/136) | Unity Testbed project missing .meta files | tech debt | out of scope | Waits for the Unity CI matrix |
| [#135](https://github.com/moonsharp-devs/moonsharp/issues/135) | Lua stack trace in exception | enhancement | feature: planned | `InterpreterException.CallStack` exists; better messages are on the roadmap |
| [#134](https://github.com/moonsharp-devs/moonsharp/issues/134) | Unicode-based string pattern matching breaks compatibility | bug | waits: string library | Reproduces: `%p` misses `=`, `+`, `$`, `<`, `\|`, `~`, `^` and the backtick. Ignored test `UpstreamIssueTests.Issue134_PunctuationClassMatchesAsciiPunctuation` |
| [#106](https://github.com/moonsharp-devs/moonsharp/issues/106) | Create Unity asset store package | enhancement | out of scope | The fork ships UPM packages |
| [#103](https://github.com/moonsharp-devs/moonsharp/issues/103) | implementation of lua 5.3 features | feature | feature: partly done | See #300; bitwise operators wait for the integer decision |
| [#102](https://github.com/moonsharp-devs/moonsharp/issues/102) | Wrong extension methods caching? | bug | fixed | `099a347`; `Issue102_EveryRegisteredExtensionMethodIsFound` |
| [#98](https://github.com/moonsharp-devs/moonsharp/issues/98) | Exception rethrow might lose part of the call stack on Unity | bug | out of scope | Mono runtime behaviour; .NET keeps the stack |
| [#95](https://github.com/moonsharp-devs/moonsharp/issues/95) | Better replacement of single fields in userdata | enhancement | out of scope | No description |
| [#75](https://github.com/moonsharp-devs/moonsharp/issues/75) | "Staticizer" tool for AOT platforms and optimizations | feature | feature: planned | The roadmap's Roslyn source generator for descriptors |
| [#74](https://github.com/moonsharp-devs/moonsharp/issues/74) | Support some kind of extensible class on both sides | feature | out of scope | No description |
| [#70](https://github.com/moonsharp-devs/moonsharp/issues/70) | ScriptOptions stdxxx files overrides don't really work.. | bug | fixed | `44484bc`; `Issue70_StandardStreamsSetAfterCreationAreUsed` |
| [#66](https://github.com/moonsharp-devs/moonsharp/issues/66) | AST+Lexer exposed to third parties | feature | feature: not planned | No roadmap item |
| [#60](https://github.com/moonsharp-devs/moonsharp/issues/60) | Update website pages | docs | out of scope | Upstream website |
| [#11](https://github.com/moonsharp-devs/moonsharp/issues/11) | Tutorials on website to be completed | docs | out of scope | Upstream website |

## Pull requests that describe bugs or features

| Pull request | Title | Type | Status in the fork | Evidence |
|---|---|---|---|---|
| [#355](https://github.com/moonsharp-devs/moonsharp/pull/355) | Format the key in Table.ResolveMultipleKeys error messages | bug | waits: table | Same as #308 |
| [#354](https://github.com/moonsharp-devs/moonsharp/pull/354) | Treat a null return from a CLR callback as nil | bug | fixed | `5cf4dfb` (same change) |
| [#347](https://github.com/moonsharp-devs/moonsharp/pull/347) | Fix debug server launch configuration | docs | out of scope | Same as #297 |
| [#344](https://github.com/moonsharp-devs/moonsharp/pull/344) | Map generic c# functions of registered types to lua functions | feature | feature: not planned | See [upstream.md](upstream.md); maintainers prefer explicit wrappers |
| [#336](https://github.com/moonsharp-devs/moonsharp/pull/336) | Fix for #175 (Inheritance for static methods) | bug | needs owner | Same as #175 |
| [#334](https://github.com/moonsharp-devs/moonsharp/pull/334) | Bitwise operators and floor division operator from Lua 5.3 | feature | feature: not planned | Waits for the integer decision; targets `legacy/2.0` |
| [#327](https://github.com/moonsharp-devs/moonsharp/pull/327) | DynValue ToString uses InvariantCulture | bug | fixed | `00c75be` on `next` (numbers print the same on every runtime); `UpstreamIssueTests.PR327_ConcatenationIgnoresTheCulture` passes |
| [#324](https://github.com/moonsharp-devs/moonsharp/pull/324) | Fix for #198 (var args are nil when void is passed) | bug | waits: VM core | Same as #198 |
| [#321](https://github.com/moonsharp-devs/moonsharp/pull/321) | Added compatibility with Unity 2017+ | bug | needs owner | `#if UNITY_5` paths are dead on Unity 2017 and later (also in the fork); enabling them needs the Unity CI matrix |
| [#316](https://github.com/moonsharp-devs/moonsharp/pull/316) | FIX (net6.0-ios, net6.0-wasm) | bug | does not reproduce | The fork's browser build runs the netstandard2.0 interpreter (fork [PR #8](https://github.com/emindeniz99/moonsharp/pull/8)) |
| [#283](https://github.com/moonsharp-devs/moonsharp/pull/283) | Fix of incorrect work of the split string with the Cyrillic symbols | bug | does not reproduce | `gmatch('[^ ]+')` splits Cyrillic text |
| [#281](https://github.com/moonsharp-devs/moonsharp/pull/281) | Enumerator based coroutine yielding | feature | feature: not planned | No roadmap item |
| [#280](https://github.com/moonsharp-devs/moonsharp/pull/280) | Bunch of fixes and compatibility tweaks (fix #275, #263, #229) | bug | fixed (part) | #229 and #263 fixed; #275 waits |
| [#246](https://github.com/moonsharp-devs/moonsharp/pull/246) | Implement script async execution control | feature | feature: planned | Same need as #231 |
| [#202](https://github.com/moonsharp-devs/moonsharp/pull/202) | m_IsRunningOnAOT doesn't be used | tech debt | out of scope | A compiler warning under IL2CPP defines; waits for the Unity CI matrix |
| [#191](https://github.com/moonsharp-devs/moonsharp/pull/191) | Make TimeZone changeable | feature | feature: not planned | No roadmap item |
| [#184](https://github.com/moonsharp-devs/moonsharp/pull/184) | Fix stringlib against non-ascii chars | bug | waits: string library | Same as #187 |
| [#179](https://github.com/moonsharp-devs/moonsharp/pull/179) | No NullReferenceException when passing null to Table.Set | bug | waits: table | Reproduces. Ignored test `UpstreamIssueTests.PR179_TableSetWithNullRemovesTheKey` |
| [#173](https://github.com/moonsharp-devs/moonsharp/pull/173) | Fixed : call debug.getmetatable maybe cause NullReferenceException | bug | fixed | `ed11539`; `Issue173_DebugGetmetatableReturnsNilWithoutMetatable` |
| [#146](https://github.com/moonsharp-devs/moonsharp/pull/146) | fixed crash in web browser + unity + webgl | bug | out of scope | Same as #212 |
| [#132](https://github.com/moonsharp-devs/moonsharp/pull/132) | Table remove by negative fix | bug | does not reproduce | `table.remove(t, -1)` raises "position out of bounds", as Lua 5.2 does |
| [#130](https://github.com/moonsharp-devs/moonsharp/pull/130) | Generic extension exception and multiple extension methos for single userdata: fixes | bug | fixed | `099a347` (the first half; the generic part was not reproduced) |
