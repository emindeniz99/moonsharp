# LuaCATS definitions for script authors

`src/Tools/MoonSharp.LuaCats` writes [LuaCATS](https://luals.github.io/wiki/annotations/)
definition files for [lua-language-server](https://github.com/LuaLS/lua-language-server)
(LuaLS). Script authors then get completion, hover and type checks for the C#
API a host exposes, in any editor that runs LuaLS. Nothing changes at run
time, and the interpreter DLL does not grow: the generator is a separate
project.

## What it reads

The generator takes a configured `Script`, the one the host's own setup code
creates, and declares:

- every type registered with `UserData`, member by member, from the same
  descriptor that binds calls at run time. A member scripts cannot call
  (hidden by `[MoonSharpHidden]`, not public, refused by the registration
  policy) is not declared, and every declared member exists.
- the host's globals: static userdata (`script.Globals["Player"] =
  typeof(Player)`), objects, tables of values and functions.
- the standard library the script was created with (its `CoreModules`).

Declarations and bindings come from one source, so they cannot drift apart
the way a hand-maintained definition file does. The `--check` mode below
catches a committed file that is older than the code.

## Use

From the command line, point `moonsharp-luacats` at the host assembly and a
public static method without parameters that returns the configured script:

```sh
dotnet build -c Release src/Tools/MoonSharp.LuaCats.Cli
dotnet src/Tools/MoonSharp.LuaCats.Cli/bin/Release/net8.0/moonsharp-luacats.dll \
  path/to/Host.dll Game.Scripting.ScriptApi.CreateScript scripts/types --luarc scripts/.luarc.json
```

This writes one file per class plus `_globals.lua` and `_stdlib.lua` into
`scripts/types`, deletes files it generated earlier that are no longer
produced, and writes a `.luarc.json` for the workspace. Add `--check` in CI:
it writes nothing and exits with 1 when a file is missing, different or no
longer produced.

From C#, for example in an editor tool:

```csharp
LuaCatsResult result = LuaCatsGenerator.Generate(ScriptApi.CreateScript());
result.WriteTo("scripts/types");
File.WriteAllText("scripts/.luarc.json", result.CreateLuarc("types"));
foreach (string warning in result.Warnings)
    Console.Error.WriteLine(warning);
```

The library targets netstandard2.0 and C# 7.3, so it can run in a Unity
Editor. There is no menu item yet (see the roadmap).

The `.luarc.json` sets `runtime.version` to `Lua 5.2`, lists the definitions
folder as a library and turns off the LuaLS standard libraries the script
does not have. Merge it into an existing `.luarc.json` if the workspace has
one.

## Example

From the sample host in `src/Tools/MoonSharp.LuaCats.Tests` (abridged):

```lua
---@class Sample.Player : Sample.Entity, Sample.IDamageable
---@field Damaged { add: fun(handler: fun(sender: any, e: Sample.DamageEventArgs)), remove: fun(handler: fun(sender: any, e: Sample.DamageEventArgs)) }
---@field Health number
---@field Id int64 read-only
---@field MaxLevel number static, read-only
---@field Shield number?
---@field Tags string[] read-only
---@field Team Sample.Team
local Sample_Player = {}

---@param amount number
---@param overheal? boolean
---@return boolean
function Sample_Player:Heal(amount, overheal) end

---@param name string
---@return string
---@overload fun(self: Sample.Player, other: Sample.Player): string
function Sample_Player:Greet(name) end

---@param name string
---@return boolean
---@return Sample.Item item
function Sample_Player:TryGetItem(name) end
```

```lua
---@type Sample.Player
Player = nil
```

## How C# types map

Parameters use what MoonSharp accepts from a script; return values, fields
and properties use what it gives back.

| C# | Parameter | Value read by a script |
|---|---|---|
| `bool` | `boolean` | `boolean` |
| `string`, `char`, `StringBuilder` | `string` | `string` |
| `int`, `double` and the other numbers | `number` | `number` |
| `long`, `ulong` | `number\|int64\|uint64` when int64 values can exist (the `int64` module or `ExactInt64Interop`), else `number` | `int64`/`uint64` with `ExactInt64Interop`, else `number` |
| `T?` (`Nullable<T>`) | `T?` | `T?` |
| registered class, struct or interface | its class | its class |
| registered enum | `Enum\|number` | `Enum` |
| unregistered enum | `number` | `number` |
| `List<T>`, `IList<T>`, `IEnumerable<T>`, `T[]` | `T[]` (the table is converted) | `T[]` (a copy) unless the type is registered |
| `Dictionary<K,V>`, `IDictionary<K,V>` | `table<K, V>` | `table<K, V>` unless registered |
| other `IEnumerable` | | `fun(): T`, an iterator for `for ... in` |
| delegate | `fun(...)` when its `Invoke` has up to 4 parameters, none by reference (MoonSharp converts the function, `Closure.ToDelegate`); otherwise not convertible, see warnings | `fun(...)`: MoonSharp returns a callable |
| event | | `{ add: fun(handler), remove: fun(handler) }`, used as `obj.Event.add(f)` |
| `out` parameter | left out | an extra return value |
| `ref` parameter | kept | also an extra return value |
| `params T[]` | `...: T` | |
| optional parameter | `name?` | |
| `Script`, `ScriptExecutionContext` | left out, MoonSharp passes them | |
| `CallbackArguments` | `...: any` | |
| `object`, `DynValue` | `any` | `any` |

Each class gets its instance and static members: instance methods take a
colon (`p:Heal(5)`), static ones a dot (`Player.Create("a")`), and
constructors are `__new` (`Player.__new("a")`), as in MoonSharp. Operator
overloads become `---@operator` (`add`, `sub`, `mul`, `div`, `mod`, `unm`), an
indexer becomes `---@field [K] V`, and a readable `Length` or `Count`
property gives `---@operator len`. Registered extension methods appear as
methods of the type they extend. Property and event accessor methods
(`get_X`, `add_E`) and conversion operators are callable but not declared.
MoonSharp's fuzzy name matching (`p:heal()` for `Heal`) is not declared
either; scripts should use the C# names.

A type that is not registered but appears as a parameter, such as an
interface the registered classes implement, gets an empty class, so a
registered class that derives from it is accepted where it is expected.

## Warnings

Some C# signatures promise more than a script can do. The generator declares
what actually works and lists the rest in `Warnings` (the CLI prints them):

- a member whose type is not registered and has no other conversion: reading
  it raises "cannot convert clr type" at run time, so it is declared `any`.
  Register the type or change the member.
- a delegate parameter whose `Invoke` has more than four parameters, or one
  by reference: MoonSharp cannot convert a script function to it unless a
  custom converter is registered. The parameter is declared as an empty
  class, so passing a function is reported.
- a standard function LuaLS still offers although the script lacks it.
  LuaLS can only switch whole libraries; the generator declares the global
  functions, `os` and `package` itself, but keeps LuaLS's `io` and `debug`.
  MoonSharp has no `io.popen` and no `debug.getinfo`, `getlocal`, `setlocal`,
  `gethook` or `sethook`.

## Standard library

MoonSharp's `CoreModules` split some Lua libraries: the global functions
come from `Basic`, `LoadMethods`, `Metatables` and `ErrorHandling`, and `os`
from `OS_Time` and `OS_System`. A soft-sandboxed script has `pcall` but not
`load`, and `os.time` but not `os.execute`. So the generator declares the
global functions, `os` and `package` function by function for what the
script has, and turns LuaLS's own versions off. `string`, `table`, `math`,
`coroutine`, `bit32`, `io` and `debug` come from LuaLS when present and are
turned off when absent. MoonSharp's additions are declared when present:
`string.contains`, `startsWith`, `endsWith` and `unicode`, `pack`, `unpack`,
`loadsafe`, `loadfilesafe`, `_MOONSHARP`, `json`, `dynamic`, and `int64` and
`uint64` with their operators ([int64.md](int64.md)).

The Lua version is set once, by `runtime.version` in the `.luarc.json`. The
definitions do not repeat it with `---@version`, which LuaLS uses for
libraries shared by several Lua versions.

## Tests

`src/Tools/MoonSharp.LuaCats.Tests` runs a sample host (`Sample.cs`) that
covers every mapping above and registers an explicit type list
([registration.md](registration.md)). It checks that:

1. the definitions equal the committed ones in `expected/` (`--update`
   rewrites them for review);
2. a second run gives the same files;
3. the warnings are exactly the two the sample makes on purpose;
4. `scripts/good.lua`, which uses only declared API, runs on the sample
   script;
5. with `--luals`, LuaLS finds nothing in the definitions or in `good.lua`,
   and exactly the problems marked `-- expect: <code>` in `scripts/bad.lua`
   (wrong argument types, unknown fields, hidden members, missing and extra
   arguments, `load`, `io` and `os.execute` in a soft sandbox).

```sh
dotnet build -c Release src/Tools/MoonSharp.LuaCats.Tests
luals="$(tools/luals/install.sh /tmp/luals)"
dotnet src/Tools/MoonSharp.LuaCats.Tests/bin/Release/net8.0/MoonSharp.LuaCats.Tests.dll \
  src/Tools/MoonSharp.LuaCats.Tests/expected --luals "$luals"
```

`tools/luals/install.sh` installs lua-language-server 3.19.1 and checks its
SHA-256 against the digest the GitHub release lists. The LuaCATS workflow
(`.github/workflows/luacats.yml`) runs the CLI `--check` and these tests.
