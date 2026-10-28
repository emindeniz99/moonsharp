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
share one formatter that reproduces .NET Core's output on every runtime: the fewest significant digits
that read back as the same number, in the invariant culture. Output on .NET is unchanged.

- On Mono and Unity (Mono and IL2CPP), numbers that need more than 15 digits used to lose digits:
  `2^53` printed `9.00719925474099E+15`. It now prints `9007199254740992`, as on .NET.
- Concatenation and `%s` used the process culture: `1.5 .. ''` gave `1,5` under a decimal-comma culture.
  They now always give `1.5`.
- Negative zero prints `-0` everywhere (Mono printed `0`).

Who is affected: scripts and hosts that compare printed numbers as text, mostly on Unity.

## Exceptions from C# methods called through reflection

A C# method that threw, called from Lua through reflection, reached the
host as a `TargetInvocationException` wrapping the real exception; through
the compiled path it reached the host as itself. Reflection is what
`InteropAccessMode.Reflection` uses, what every call uses on AOT platforms
(IL2CPP, iOS), and what a `BackgroundOptimized` method uses until it is
compiled. The original exception is now rethrown on every path, with its
stack trace. Affected: hosts on IL2CPP or iOS, or using `Reflection`, that
caught `TargetInvocationException` and read `InnerException`.

The same holds for property getters and setters called through reflection.
A setter that throws `ArgumentException` itself is now reported as a type
mismatch (`ScriptRuntimeException`), as the compiled setter already did.

## The Unity package requires Unity 2021.3

`package.json` of `org.moonsharp.moonsharp` and
`org.moonsharp.debugger.vscode` declares `"unity": "2021.3"` (it was
`2020.3`). The fork targets Unity 2021.3 LTS, 2022.3 LTS and Unity 6 LTS,
which `.github/workflows/unity.yml` tests; 2020.3 is out of support and was
never tested. Nothing in the package needed the lower number. Affected:
projects on Unity 2020.3.

## NativeAOT counts as an AOT platform

`Script.GlobalOptions.Platform.IsRunningOnAOT()` returned false on .NET
NativeAOT: its probe compiles an expression tree, which NativeAOT interprets
instead of refusing. It now reads `RuntimeFeature.IsDynamicCodeSupported`
where the runtime has it, and returns true on NativeAOT. There, as on IL2CPP,
every descriptor uses `InteropAccessMode.Reflection` whatever mode was asked
for, `_MOONSHARP.is_aot` is true and the platform name ends in `.aot`.
Affected: hosts published with NativeAOT that checked these values or
counted adapter compilations.

## Generic extension overloads that AOT cannot instantiate are skipped

When a script calls an extension method by name, MoonSharp instantiates each
generic candidate for the userdata's type. On NativeAOT an instantiation the
app never compiled threw `NotSupportedException` and ended the lookup, so
`list:Sum()` failed although `Sum(IEnumerable<int>)` existed. Such a
candidate is now skipped like one whose arguments do not fit; when no
candidate is left, the member is missing, as for any unknown name.
Affected: hosts on NativeAOT that registered extension types.

## Trimmed apps keep the members of registered types

`UserData.RegisterType<T>()`, `RegisterType(Type, ...)` and
`RegisterExtensionType(Type)` carry trimming annotations
(`DynamicallyAccessedMembers`). An app trimmed by the .NET SDK or published
with NativeAOT now keeps every member of a type it registers with a
`typeof` or a type argument, without a descriptor file, and grows by those
members. A `Type` computed at run time gets a trim warning in the host's
build, as other reflection does. Unity's linker is not known to read these
annotations; keep using `link.xml` or `[Preserve]` there.

## Extension methods from every registered class are found

A userdata looked for extension methods only when an extension class had
been registered since its previous lookup. After a script used one
extension method of a type, the others stayed invisible ("cannot access
field") until another extension class was registered. Every registered
extension method is now found. Affected: hosts that register extension
methods for one type from several static classes
([upstream #102](https://github.com/moonsharp-devs/moonsharp/issues/102)).

## Types that hide inherited members with `new` can be registered

Registering a type that hides an inherited property, field or event with
`new` (for example a command type whose `Connection` property returns a
more derived connection) threw "Multiple members named ... are being
added". The type now registers and scripts see the member that C# code
sees through that type: the most derived visible one. Affected: hosts
that worked around the exception with wrapper types
([upstream #286](https://github.com/moonsharp-devs/moonsharp/issues/286)).

## Delegates compiled at run time can be passed to scripts

A delegate built with `Expression.Compile()` or a `DynamicMethod` threw
`NullReferenceException` when converted for a script, because its method
has no declaring type. Such delegates are now called through their
`Invoke` method. Affected: hosts that wrapped them in a lambda to work
around the exception
([upstream #291](https://github.com/moonsharp-devs/moonsharp/issues/291)).

## A CLR callback that returns null gives the script nil

A `CallbackFunction` whose function returned `null` instead of a
`DynValue` made the VM throw `NullReferenceException`. Scripts now
receive `nil`. Affected: hosts that relied on the exception to detect
such callbacks
([upstream #307](https://github.com/moonsharp-devs/moonsharp/issues/307)).

## `debug.getmetatable` returns nil for values without a metatable

For a table without a metatable, or a type without a type metatable,
`debug.getmetatable` returned a table value wrapping nothing: it was not
`nil`, and using it threw `NullReferenceException`. It now returns `nil`,
as `getmetatable` and Lua do. Affected: scripts that used the debug
library to inspect metatables
([upstream PR #173](https://github.com/moonsharp-devs/moonsharp/pull/173)).

## `json.parse` reads nested arrays

`json.parse` used the Lua lexer, which reads `[[` as the start of a long
string, so JSON such as `[[1,2],[3,4]]` failed with "Unexpected token".
It now reads `[[` as two array openings. Affected: hosts that inserted
spaces between brackets as a workaround
([upstream #219](https://github.com/moonsharp-devs/moonsharp/issues/219)).

## `tonumber` reads Lua numerals

`tonumber(s)` used .NET number parsing. It returned `nil` for
hexadecimal numerals such as `"0x23"`, and returned numbers for strings
that Lua rejects, such as `"1,000"` (1000) and `"(5)"` (-5). It now
follows Lua 5.2: decimal or hexadecimal numerals (with an optional
fraction and `p` exponent), an optional sign and surrounding whitespace.

```lua
print(tonumber("0x23"), tonumber("1,000"))   -- before: nil 1000   now: 35 nil
```

String coercion in arithmetic (`"0x10" + 1`) still uses the old parsing
until the VM work changes it. Affected: scripts that passed formatted
numbers to `tonumber`
([upstream #229](https://github.com/moonsharp-devs/moonsharp/issues/229)).

## Standard streams set after a script is created are used

`Script.Options.Stdin`, `Stdout` and `Stderr` were read only while the
`Script` was constructed; setting them later had no effect and `io`
kept using the process console. The `io` module now switches to the
stream the options name when it next uses a standard file. A file value
a script already holds (`local out = io.stdout`) keeps its old stream.
Affected: hosts that set these options after creating a script
([upstream #70](https://github.com/moonsharp-devs/moonsharp/issues/70)).

## `json.parse` reads JSON string escapes

`json.parse` unescaped strings with Lua's rules: `é` failed with
"'{' expected near '\u'", while Lua-only escapes such as `\x41`, `\65`
and `\u{41}` were accepted. Strings now use JSON's escapes (`\"`, `\\`,
`\/`, `\b`, `\f`, `\n`, `\r`, `\t`, `\uXXXX`, with surrogate pairs kept
as two UTF-16 units), and other escapes are a parse error. Affected:
hosts that pre-processed JSON to avoid `\u` escapes, and input that
relied on Lua escapes.

## `json.serialize` writes mixed tables as objects

A table with keys 1..n was written as a JSON array even when it had other
keys, which were dropped: `{ 10, name = "x" }` became `[10]`. Such a
table is now an object, with integer keys written as strings
(`{"1":10,"name":"x"}`), as the host's JSON writer already did. A table
whose keys are exactly 1..n is still an array, and a table with only
integer keys that are not 1..n (`{ [3] = "c" }`) is now an object
instead of `{}`. Affected: scripts that relied on the dropped keys being
left out.

## `io.write` to the standard streams is not buffered

`io.write` and `io.stdout:write` kept text in a `StreamWriter` buffer
until `io.flush()`. Nothing flushed it when a script finished, so output
appeared late or never (each `Script` has its own writer). The standard
output and error files now write through. A script that writes much
small output can ask for buffering with `io.stdout:setvbuf("full")` and
flush itself. Affected: hosts that read the output stream and saw
partial text before
([upstream #161](https://github.com/moonsharp-devs/moonsharp/issues/161)).

## Delegates compiled from expression trees work on AOT runtimes

On NativeAOT and IL2CPP an expression-tree delegate is interpreted, and reading its `Method` throws.
Passing such a delegate to a script used to fail there; it is now called through its `Invoke` method.
Passing a delegate whose type is not `Func<ScriptExecutionContext, CallbackArguments, DynValue>` but has
the same signature used to throw `InvalidCastException`; it is now converted like any other delegate.

## Keys set to nil are gone for hosts, and every one of them is forgotten

Two dead-key bugs are fixed:

- `Table.Pairs`, `Table.Keys` and `Table.Values` returned keys whose value
  had been set to nil, as pairs with a nil value. Hosts that convert tables
  saw fields the script had removed: `json.serialize` wrote `null` for
  them, and `Dictionary` conversion and the Lua-source serializer kept
  them. (The host JSON writer already skipped them, see "Host JSON output
  skips fields set to nil".)
- A key set to nil is forgotten when the next new key is stored, so it goes
  to the end of the `pairs` order when it gets a value again. Only the first
  such key was forgotten; the others kept their old place. Now every one is.

```lua
local o = { a = 1, b = 2 }; o.b = nil
print(json.serialize(o))   -- before: {"a":1,"b":null}   now: {"a":1}

local u = {}
u.a = 1; u.b = 2; u.c = 3
u.a = nil; u.b = nil
u.d = 4                    -- forgets a and b (before: only a)
u.b = 5; u.a = 6
for k in pairs(u) do io.write(k, ' ') end
-- before: b c d a   now: c d b a
```

`pairs` in a script never returned the nil keys and still does not. Affected:
hosts that enumerate tables (`Table.Pairs`, `Keys`, `Values`,
`json.serialize`, `Dictionary` conversion, the Lua-source serializer) and
scripts or snapshot tests that depend on the `pairs` order after clearing
more than one key.

## Tables have an array part

Tables now keep integer keys 1..n in an array and every other key in one
hash part, instead of a linked list indexed by three dictionaries. Values,
`#t` and the `pairs` order stay as they were (insertion order, with the
dead-key rule above). What a host can see:

- Integer keys below 1 stored with `Table.Set(int, ...)` are the same keys
  a script uses. They used to live in an index that `t[0]` and `t[-1]` never
  reached, so a host's `t.Set(0, v)` was invisible to the script, and a
  later `pairs(t)` failed with "invalid key to 'next'".
- `Table.Remove(key)` returns false for a key whose value was set to nil.
  It returned true while the dead key was still stored.
- `Table.Pairs`, `Keys` and `Values` follow the rules of `next`: clearing
  fields while enumerating is allowed; whether a key added during the
  enumeration is visited is not defined.

## `Table.Set` with a null value, and multi-key errors

`Table.Set(key, null)` threw `NullReferenceException`; a null value now
means nil, as `t[k] = nil` in a script. The error of a multi-key
`Table.Get`/`Set` whose path breaks now names the key ("Key 'settings'
did not point to anything") instead of printing "Key '{0}' ...".
Affected: hosts that caught those exceptions.
