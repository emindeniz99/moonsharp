# Migrating

Changes in this fork that a script or a host can observe, newest last. Each
entry says what changed, what it was before and who is affected.

## One descriptor per CLR type resolved through interfaces

A CLR object whose type is not registered, but whose interfaces are, gets a
descriptor that combines the interface descriptors. MoonSharp used to build a
new combined descriptor on every push, so two pushes of the same object were
different values to `DynValue.Equals`, and therefore different table keys.
The combined descriptor is now built once per type and reused.

```lua
-- x and y are the same C# object, pushed twice
local t = {}
t[x] = 1
print(t[y])   -- before: nil   now: 1
```

`x == y` was already true and does not change. Affected: hosts that push
objects of unregistered types implementing registered interfaces and use
them as table keys, or compare `UserData.Descriptor` references.

## Userdata and coroutines no longer share one hash code

`DynValue.GetHashCode` returned 999 for every userdata (other than int64 and
uint64) and every coroutine, so a table keyed by many of them searched all
keys on every lookup. Now:

- a userdata whose CLR type compares by reference (does not override
  `Equals`) hashes by object identity;
- any other userdata hashes by its descriptor, so a key whose `Equals`
  follows state that changes is still found after it changes;
- a coroutine hashes by identity.

Equality is unchanged, and `pairs` order does not depend on hashes.
Affected: hosts that stored `DynValue.GetHashCode()` values.

## Overloaded methods called with many userdata signatures

The overload resolver keeps the last five argument signatures of a method.
When it reused a slot it kept the previous signature's userdata types, so a
later call could match the wrong slot and run the wrong overload or fail
with "cannot convert". It now clears the slot. Affected: scripts calling an
overloaded method with more than five different userdata argument types.

## Methods on structs mutate the userdata in every access mode

A method on a struct called from a script, for example `v:Increment()`,
changed the boxed struct inside the userdata under
`InteropAccessMode.Reflection`, but only a copy of it under
`LazyOptimized`, `Preoptimized` and `BackgroundOptimized`, so the change
was lost. The compiled adapters now call the method on the struct inside
its box, so every mode changes the userdata.

```lua
v:Increment()
print(v.Count)   -- before: 0 in the optimized modes, 1 with Reflection   now: 1
```

Affected: hosts exposing structs with mutating methods. Hardwired
descriptors are generated code that casts the receiver to a copy; they keep
the old behaviour.

## Reading a method from a userdata returns the same callback

Each read of a method from a userdata, which includes every
`obj:Method()` call, built a new callback bound to that userdata. The
userdata now keeps its last two bound callbacks and returns them again, so:

```lua
print(obj.Method == obj.Method)          -- before: false   now: true
print(rawequal(obj.Method, obj.Method))  -- before: false   now: true
print(obj.Method == other.Method)        -- false, as before
```

The kept callback is read-only and is dropped when the descriptor's members
change (`AddMember`, `RemoveMember`, a new extension method found by
name). Overloads and extension methods are still resolved at each call.
Affected: scripts that compared method values, and hosts that set
`CallbackFunction.AdditionalData` on a callback read from a userdata
(the object is now shared by later reads).

## Named enum values pushed from the CLR share one userdata

A CLR enum value with a name in a registered enum, returned by a method or
pushed with `UserData.Create` or `DynValue.FromObject`, now reuses the
read-only value the enum descriptor holds for that name, as `MyEnum.A` in a
script already did, instead of a new userdata each time. Equality,
`tostring` and table keys are unchanged. Affected: hosts that call
`Assign` on such a value (it is read-only, like `DynValue.Nil`), or that
attach a `UserValue` (`debug.setuservalue`) to one enum value and expect
other pushes of the same value not to see it.

## Flags functions return enum values

`flagsAnd`, `flagsOr`, `flagsXor` and `flagsNot` on a registered `[Flags]`
enum returned a userdata wrapping the bare underlying number (a boxed
`int`, `ushort`, ...), not a value of the enum type. So it printed as a
number and never compared equal to the enum's members:

```lua
print(MyFlags.flagsOr(MyFlags.A, MyFlags.B))                  -- before: 3      now: A, B
print(MyFlags.flagsOr(MyFlags.A, MyFlags.B) == MyFlags.AB)    -- before: false  now: true
```

They now return values of the enum type. Affected: scripts that printed or
compared these results, and hosts that received them as `object`.

## Objects with script event handlers can be collected

An event descriptor is shared by every script and lives as long as the
type's registration. It kept each object that had a script handler, the
handler closures and so their scripts, in plain dictionaries, so none of
them were ever collected unless the script removed every handler. It now
holds the object weakly: while the object lives its handlers live and
fire as before; once the host drops the object, the object, its handlers
and a script referenced only by them can be collected. Affected: hosts
that relied on a script staying alive only because one of its functions
handled an event of an object nobody else referenced (such an event could
never fire again anyway).

## Background optimization no longer ends the process

With `InteropAccessMode.BackgroundOptimized`, the thread-pool work item
that compiles adapters walked a type's overload list while the first
script call sorted it. The resulting exception was unhandled on a pool
thread, which ends the process. The work item now walks copies of the
lists and ignores a failure: members it did not optimize keep running
through reflection, as they do before it finishes. Affected: hosts using
`BackgroundOptimized` with overloaded methods.

## `table.remove` on an empty table follows Lua 5.2

With `#t == 0`, `table.remove(t)` stored nil under an internal key 0 that
script key 0 cannot reach. Once that entry came first in the table,
`pairs(t)` failed with "invalid key to 'next'". It now does what Lua 5.2
does: it returns `t[0]` and sets `t[0]` to nil, and a position below 1
other than `#t` is an error.

```lua
local t = {}
table.remove(t); t[1] = 'a'
for k, v in pairs(t) do end   -- before: error "invalid key to 'next'"   now: works

local u = { [0] = 'x' }
print(table.remove(u), u[0])  -- before: nil  x   now: x  nil

table.remove({}, -2)          -- before: returned nil   now: error "position out of bounds"
```

Affected: scripts that call `table.remove` on tables that may be empty
(stacks, queues). `Table.Pairs` no longer returns a `0 = nil` entry to
hosts after such a call.

## string.format: the C sprintf port is gone from LuaBase

`string.format` no longer runs the KopiLua port of C's `sprintf` (a regular expression and
`String.Format` per specifier). A one-pass formatter replaced it. Its output, errors and exceptions
are the same as before for every input, quirks included (for example `%5.1d` ignores the precision,
`%0-5d` prints the specifier literally, `%c` ignores the width).

What a host can notice is the removal of members of the public `LuaBase` class
(`MoonSharp.Interpreter.Interop.LuaStateInterop`) that only `string.format` used:

| Removed | Use instead |
|---|---|
| `public static void sprintf(CharPtr, CharPtr, params object[])` | `string.Format` or your own formatting |
| protected `strchr`, `strcpy`, `strncpy`, `strlen`, `isdigit(int)` | `string`/`char` methods; `isdigit(char)` remains |
| protected `LuaLAddString`, `LuaLCheckNumber`, `LuaGetTop`, `LUA_INTFRMLEN` | `LuaLAddLString`, `ArgAsType(L, pos, DataType.Number)`, `L.Count` |

Affected: hosts that subclass `LuaBase` to port C library code and call these helpers. Scripts are
not affected.

## Script.Call finishes a tail call returned by a C# function

`Script.Call` on a C# function that ends with a plain tail call (`dofile`, and now `require`) returned
the internal tail-call request (`DataType.TailCallRequest`) to the host instead of running it.

- Before: `script.Call(script.Globals.Get("dofile"), "a.lua")` returned a `TailCallRequest` value.
- Now: it runs the chunk and returns its result. Tail calls with a continuation or error handler
  (such as `pcall`'s) are returned as before.

Affected: hosts that call `dofile` (or other tail-calling C# functions) through `Script.Call`.

## `require` is a C# function

`require` was a Lua function compiled into every new `Script`. It is now a C# function that compiles
the same Lua code on its first call and then tail-calls it, so loading, caching in `package.loaded`,
yielding while a module loads, error messages and their positions (`libfunc_require:(12,1-44): ...`),
tracebacks below `require`, and chunk numbering (`libfunc_require` is still source 0, so the first
chunk is still `chunk_1`) are unchanged. What differs is how `require` itself appears:

| | Before | Now |
|---|---|---|
| `type(require)` | `function` | `function` |
| Value type seen by the host | `DataType.Function` | `DataType.ClrFunction` |
| `string.dump(require)` | a dump of a Lua function | an error, as for any C# function |
| `coroutine.create(require)` | a Lua coroutine | a C# coroutine, which `coroutine.resume` cannot run (as for any C# function) |

The same applies to any module member written in Lua (a `[MoonSharpModuleMethod]` string field) in a
host's own module types. A syntax error in such a field now surfaces on the first call instead of at
registration.

Affected: hosts that inspect `require` as a Lua function. A debugger attached before the first
`require` call receives the bytecode of `libfunc_require` when it is compiled.

## Host JSON output skips fields set to nil

`ScriptHost` wrote a table's fields set to nil as `null`, because the C#
`Table.Pairs` still lists them, and counted them when deciding between an
array and an object. A table shortened with `table.remove` therefore left
the host as an object:

```lua
local t = { 1, 2, 3, 4 }
table.remove(t)
return t   -- before: {"1":1,"2":2,"3":3,"4":null}   now: [1,2,3]
```

Fields set to nil are now skipped, as Lua's `pairs` skips them. Affected:
hosts (CLI, browser, Java) that read such results.

## Host JSON output rejects unpaired surrogates

A string holding half of a character outside the BMP, such as
`('👋'):sub(1, 1)`, reached each host differently: the browser got the
lone code unit, the Java worker and the in-process builds got U+FFFD from
the UTF-8 encoder, and a .NET caller parsing the envelope with
System.Text.Json got an exception. It is now an `output` error on every
host ("a string with an unpaired surrogate at code unit 1 has no JSON
representation"). Affected: scripts that return such strings to a host;
return whole characters, or the code units from `string.unicode`.

## Host contexts can be created with the int64 modules

Every transport can now create a context with `CoreModules.Int64`:
`ScriptHost.Create(true)`, the worker's `create` request, `create(context,
int64)` on the Java `MoonSharpHost`, and `createContext({ int64: true })`
in TypeScript. Two signatures changed for it:

- C: `moonsharp_context_create(void)` is now
  `moonsharp_context_create(int32_t options)`; pass 0 for the old
  behaviour or `MOONSHARP_CONTEXT_INT64`.
- Java: `MoonSharpHost` has a new `create` method, so a class outside this
  repository that implements the interface must add it.

The browser export `CreateContext` takes a boolean; `createContext()` in
TypeScript keeps working without arguments.

## `moonsharp-host test` checks known failures

`moonsharp-host test` takes suite files or directories and compares the
results with `known-failures/<host>.json` next to the suites. It exits 1
on a new failure, a changed failure, a listed failure that passes and a
listed case that does not exist, and prints one line per case (`pass`,
`known`, `FAIL`, `CHANGED`, `FIXED`, `STALE`) and a count line. Before,
it exited 1 on any failure and printed `pass` or `FAIL`.

## Custom converter lookups accept every DataType

`CustomConvertersCollection.GetScriptToClrCustomConversion` threw
`IndexOutOfRangeException` for `DataType.TailCallRequest` and
`DataType.YieldRequest`; it now returns null, as for any type without a
converter. `SetScriptToClrCustomConversion` throws `ArgumentException` for
them instead of `IndexOutOfRangeException`. Affected: code that looped over
every `DataType`.

## Numbers print the same text on every runtime

`tostring`, concatenation (`..`), `string.format('%s')`, JSON output and conversions to CLR strings now
share one formatter: the fewest significant digits that read back as the same number, in the invariant
culture.

- On Mono and Unity (Mono and IL2CPP), numbers that need more than 15 digits used to lose digits:
  `2^53` printed `9.00719925474099E+15`. It now prints `9007199254740992`, as on .NET.
- Concatenation and `%s` used the process culture: `1.5 .. ''` gave `1,5` under a decimal-comma culture.
  They now always give `1.5`.
- Negative zero prints `-0` everywhere (Mono printed `0`).
- On .NET, a few values of `1E+16` and above with 16 significant digits change layout:
  `75987868744874290` now prints `7.598786874487429E+16`. The digits are the same.

Who is affected: scripts and hosts that compare printed numbers as text, mostly on Unity.
