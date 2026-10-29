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

## Host contexts can limit each call

`create` takes an instruction limit and a time limit for each call; a call
past either returns an error of the new kind `interrupted` (docs/hosting.md,
"Limits"). Contexts created without them behave as before. Signatures that
changed:

- C: `moonsharp_context_create(int32_t options)` is now
  `moonsharp_context_create(int32_t options, int64_t instruction_limit,
  int32_t time_limit_ms)`; pass 0, 0 for no limits.
- Java: `MoonSharpHost.create(String, ContextOptions)` is the method to
  implement; `create(String, boolean)` is now a default method calling it.
- Browser: the export `CreateContext` takes two more numbers;
  `createContext(options)` in TypeScript keeps working as before.

Code that switches over error kinds should expect `interrupted`.

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

## The browser package runs without dynamic code

`src/Hosting/browser` now sets `DynamicCodeSupport=false`, so the browser
counts as an AOT platform: `_MOONSHARP.is_aot` is true, the platform name
changes from `core.dotnet.clr4.netcore` to `core.dotnet.clr4.netcore.aot`, and
CLR interop descriptors use `InteropAccessMode.Reflection` instead of compiled
expression trees (which the browser interpreted anyway). With the interpreter's
new `ILLink.Substitutions.xml`, the trimmer then drops `System.Linq.Expressions`
from the download (docs/hosting.md, "Browser budget"). The same substitutions
apply to any trimmed app that turns dynamic code off, NativeAOT included; such
apps already ran with `IsRunningOnAOT()` true, so only their size changes.

A Release build of the browser package also needs the `wasm-tools` workload
(`dotnet workload install wasm-tools --version 10.0.402`); without it the
build stops with NETSDK1147 instead of shipping the 976 KB prebuilt runtime.
Affected: browser hosts that read `is_aot` or the platform name, and anyone who
builds the browser package.

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

Strings that arithmetic and the library functions turn into numbers
(`DynValue.CastToNumber`) follow the same rules: `"0x10" + 1` is 17,
and `"1,000" + 1` is an error where it was 1001. Affected: scripts that
passed formatted numbers to `tonumber` or used them in arithmetic
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

## `>` and `>=` follow Lua 5.2

**Before:** `a > b` ran as `not (a <= b)` and `a >= b` as `not (a < b)`.

**After:** `a > b` runs as `b < a` and `a >= b` as `b <= a`, as the Lua 5.2 manual defines them
([3.4.3](https://www.lua.org/manual/5.2/manual.html#3.4.3)). Operands are still evaluated left to right.

| Expression | Before | After (and Lua 5.2) |
|---|---|---|
| `(0/0) > 1`, `(0/0) >= 1`, `1 > (0/0)` | `true` | `false` |
| `x > y` with `__lt` and `__le` | `not __le(x, y)` | `__lt(y, x)` |
| `x >= y` with `__le` | `not __lt(x, y)` (an error without `__lt`) | `__le(y, x)` |
| `x > y` with only `__le` | `not __le(x, y)` | error: attempt to compare two table values |

`x >= y` with only `__lt` gives the same result as before (`not __lt(x, y)`). Errors for comparisons
of unrelated types name the operands of the comparison that runs, as Lua 5.2 does: `1 > 'x'` now
reports "attempt to compare string with number" (it said "number with string").

**Affected:** scripts that compare NaN with `>`/`>=`, and types whose `__le` is not the complement of
`__lt` (for example partial orders). Code compiled by an older version and loaded with `load` from a
`string.dump` keeps the old behaviour until it is recompiled.

## Comparison and `not` results are read-only

**Before:** `==`, `<`, `<=`, `>`, `>=` and `not` produced a new writable boolean each time.

**After:** they produce the shared read-only `DynValue.True` and `DynValue.False`, as the literals
`true` and `false` already did. This removes an allocation per comparison.

**Affected:** hosts that call `DynValue.Assign` on a boolean they received from a script. That now
throws `ScriptRuntimeException` ("Assigning on r-value") instead of changing the value; create a new
value with `DynValue.NewBoolean` instead. `DynValue.NewBoolean` still returns a writable value.

## `%` follows Lua 5.2 for negative divisors

**Before:** `a % b` was `IEEERemainder(a, b)`, plus `b` when that was negative. That is right for a
positive divisor and wrong for a negative one.

**After:** `a % b` is `a - math.floor(a/b)*b`, the Lua 5.2 definition
([3.4.1](https://www.lua.org/manual/5.2/manual.html#3.4.1)), in scripts and in dynamic expressions.
The result has the sign of the divisor.

| Expression | Before | After (and Lua 5.2) |
|---|---|---|
| `5 % -3` | `-4` | `-1` |
| `-5 % -3` | `1` | `-2` |
| `5.5 % -2` | `-2.5` | `-0.5` |
| `5 % (1/0)` | `5` | `nan` |
| `-5 % (1/0)` | `inf` | `nan` |

Positive divisors give the same results as before, except where the two formulas round differently
in the last bit for fractional operands.

**Affected:** scripts that use `%` with a negative or infinite divisor.

## Native recursion is limited to 200 levels and fails with an error

**Before:** recursion that runs through C# (a Lua function passed to `string.gsub` or `table.sort`
that calls them again, a C# callback that calls back into Lua, `coroutine.wrap` inside a coroutine),
source nested tens of thousands of levels deep, and deeply nested or cyclic data in `json.parse` and
`json.serialize` overflowed the native stack. The runtime then ended the whole process; no `catch` or
`pcall` could stop it.

**After:** the same limit as Lua 5.2 (`LUAI_MAXCCALLS`, 200) applies everywhere, as a count, so every
host stops at the same depth:

| What | Error |
|---|---|
| More than 200 nested entries into the VM from C# on one thread (`Script.Call`, callbacks, `coroutine.resume`) | `ScriptRuntimeException` "C stack overflow" (Lua 5.2's text); `pcall` catches it |
| Source with more than 200 nested expressions and blocks (`load`, `Script.LoadString`) | `SyntaxErrorException` "chunk has too many syntax levels" |
| `json.parse` input or `json.serialize` tables more than 200 levels deep, or cyclic tables | `json nesting exceeds 200 levels` (a `ScriptRuntimeException` from Lua) |

Chains of any length, such as a generated `1+1+...+1` with a million terms or `a.b.b.b...`, now
compile without recursion, as in Lua; they used to overflow the stack too. A stack probe
(`RuntimeHelpers.EnsureSufficientExecutionStack`) stays behind the VM count for threads with a small
stack.

**Affected:** code nested deeper than Lua 5.2 allows, which MoonSharp used to accept as long as the
stack held. The stack probe does not work on Mono here (a test on Mono 6 overflowed inside it) and is
unverified on IL2CPP; the counts work everywhere.

## `xpcall` message handlers only see errors that reach them

**Before:** an error raised below an `xpcall` ran its message handler even when an inner `pcall`
caught the error first, and an error crossing several C# callbacks could run a handler once per
callback level.

**After:** the handler runs once, for errors that reach its `xpcall`, as in Lua.

**Affected:** scripts whose message handlers have side effects (logging, counters).

## `InterpreterException.CallStack` keeps the innermost frames

**Before:** when an error crossed C# callbacks (for example a Lua function called from `string.gsub`),
each level overwrote `CallStack`, and the host saw only the outermost frames.

**After:** `CallStack` is filled once, where the error is first caught, and lists every frame from
the error outwards.

**Affected:** hosts that print `CallStack`; the list is longer and starts where the error happened.

## `pcall` catches compile errors raised while a script runs

**Before:** a `SyntaxErrorException` raised from running code, for example `require` of a module
with a syntax error or a C# function that loads source, went past `pcall` and `xpcall` to the host.

**After:** `pcall` and `xpcall` catch it like any other error and return `false` and the message, as
in Lua. Without a `pcall` the host still receives the `SyntaxErrorException`.

**Affected:** scripts that call `require` or loader functions inside `pcall`.

## Coroutines can run C# functions

**Before:** `coroutine.create(f)` with a C# function `f` (`select`, `string.upper`, `pcall`, a host
callback) produced a coroutine that `coroutine.resume` and `Coroutine.Resume(args)` refused with an
`InvalidOperationException`; only `Coroutine.Resume(context, args)` ran it, and only once.

**After:** it is an ordinary coroutine, as in Lua: resuming it calls the function; a tail call it
returns (as `pcall` does) runs inside the coroutine and may yield; `coroutine.create(coroutine.yield)`
yields its arguments and returns the next resume's arguments. `Coroutine.Type` is `Coroutine` for it;
`CoroutineType.ClrCallback` and `ClrCallbackDead` are no longer produced.

**Affected:** hosts that checked `Coroutine.Type == CoroutineType.ClrCallback`.

## Variables given no value hold nil

**Before:** `local x` with no value could pick up a value left on the stack (inside a `for ... in`
loop it read the iterator's state), and `local v = f()` where `f` returns nothing stored "no value",
so `type(v)` failed with "bad argument #1 to 'type' (value expected)".

**After:** both hold `nil`, as in Lua. A call with no results passed straight as an argument,
`type(f())`, still passes no value, as Lua does
([upstream #263](https://github.com/moonsharp-devs/moonsharp/issues/263),
[#258](https://github.com/moonsharp-devs/moonsharp/issues/258)).

**Affected:** scripts that read a variable declared without a value and got the leftover value.

## Parameters and `...` receive what Lua 5.2 passes

**Before:** a parameter that received a call with no results, `g(f())` where `f` returns nothing,
held "no value", so `type(a)` in `g` failed with "bad argument #1 to 'type' (value expected)". And
`...` that received no values held one nil: in `function v(a, ...)` called as `v(1)`,
`select('#', ...)` was 1 and `f(...)` passed `f` a nil.

**After:** the parameter holds nil, and `...` holds no values (`select('#', ...)` is 0), as in Lua.

**Affected:** scripts that relied on `select('#', ...)` being 1 for an empty `...`, or on the error.

## Values from the VM are read-only copies

The VM keeps values as internal structs on its stack, in locals and in upvalues, and creates a
`DynValue` only where a value leaves it: arguments of C# functions, values a script stores in a
table, results of `Script.Call`, debugger views.

**Before:** a C# function could receive the very `DynValue` a script local held (usually a read-only
clone), arithmetic results were writable, and `Closure.GetUpvalue(i)` returned the upvalue's own
cell, documented as the way to change it (`GetUpvalue(i).Assign(v)`). Every `DynValue` took a
`ReferenceID` from a counter when created.

**After:**

- Every `DynValue` the VM creates is read-only; `Assign` on one throws `ScriptRuntimeException`
  ("Assigning on r-value"). Values the host creates with `DynValue.New*` stay writable.
- Nil, booleans and the integers -256..4095 come from shared instances, so two results can be the
  same object (`ReferenceEquals`); compare with `Equals` or by value.
- `Closure.GetUpvalue(i)` returns a read-only copy of the value. Use the new
  `Closure.SetUpvalue(i, value)` to change the variable; every closure that shares it sees the change,
  as with `debug.setupvalue`.
- `ReferenceID` is assigned when first read, so the numbers are sparse; they are still unique per
  thread. `DynValue` no longer caches its hash code.

**Affected:** hosts that call `Assign` on values received from scripts, that change upvalues through
`GetUpvalue(i).Assign`, or that compare values by reference.

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

## `Table.Set` with a null value, multi-key errors and `TablePair.Value`

`Table.Set(key, null)` threw `NullReferenceException`; a null value now
means nil, as `t[k] = nil` in a script. The error of a multi-key
`Table.Get`/`Set` whose path breaks now names the key ("Key 'settings'
did not point to anything") instead of printing "Key '{0}' ...".
Setting `TablePair.Value` recursed until the process
ended with a stack overflow; it now sets the pair's value (a copy: the
table does not change). Affected: hosts that caught those exceptions.

## A fractional number picks a floating-point overload

**Before:** a Lua number weighed the same for every integer parameter and for
`float`, so with overloads `F(int)` and `F(float)`, `F(1.5)` called `F(int)`
when that overload sorted first, and the fraction was lost.

**After:** a number with a fractional part (or NaN) weighs less for an
integer parameter than for `float`, `double` or `decimal`, so `F(1.5)` calls
`F(float)`. A whole number such as `F(2)` picks the same overload as before,
and a fractional number still converts to an integer parameter when no
floating-point overload exists. The overload cache records whether each
number argument is whole, so a cached choice for `F(2)` is not reused for
`F(1.5)`
([upstream #293](https://github.com/moonsharp-devs/moonsharp/issues/293)).

**Affected:** hosts with integer and `float` overloads of one method whose
scripts pass fractional numbers.

## An exact type match beats a custom converter

**Before:** an argument that a custom converter
(`Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion`) could
convert weighed the same as an argument whose type matched the parameter
exactly, so the overload that sorted first won. With a string-to-`Tag`
converter and overloads `Describe(Tag)` and `Describe(string)`,
`Describe("x")` called `Describe(Tag)`.

**After:** the exact match wins: `Describe("x")` calls `Describe(string)`. A
converter still beats every other conversion (number to `int`, number to
string, table to list), so arguments that only the converter can take reach
the converter's overload as before
([upstream #294](https://github.com/moonsharp-devs/moonsharp/issues/294)).

**Affected:** hosts with custom converters and overloads that take both the
converter's target type and the argument's own type, for example a
number-to-`int` converter on a method with `F(int)` and `F(double)`, where a
number now calls `F(double)`.

## Static members of base classes are visible through derived types

**Before:** a registered type exposed only the static members it declared
itself. `Square.Make()` failed with "cannot access field Make" when `Make`
was a static method of `Square`'s base class, although C# code can call it
that way.

**After:** a type also exposes the static methods, properties, fields and
events of its base classes, as C# does. A static member that the derived
type hides with `new` stays hidden, and same-named static methods of the
derived and base types become overloads of one member. Static members
declared in the core library (`object.ReferenceEquals`,
`object.Equals(a, b)`, the statics of `Enum`, `EventArgs`, `Exception` and
other framework base types) stay out, so member lists, Hardwire output and
LuaCATS definitions do not fill up with them
([upstream #175](https://github.com/moonsharp-devs/moonsharp/issues/175),
[PR #336](https://github.com/moonsharp-devs/moonsharp/pull/336)).

**Affected:** hosts that list a descriptor's members, scripts that relied
on such a member being missing, and Hardwire and LuaCATS output for types
whose base classes declare public statics: the new members appear on the
derived type too. For example, a registered Unity component now also exposes
the public static methods of `UnityEngine.Object`, such as `Destroy` and
`Instantiate`.

## Lists and dictionaries become tables under automatic registration

**Before:** with `UserData.RegistrationPolicy = InteropRegistrationPolicy.Automatic`,
a `List<T>`, an array or a `Dictionary<K,V>` passed to a script was
registered on the spot and became userdata, so `#list`, `ipairs` and
`pairs` did not work on it and indexing started at 0. The default policy
already gave tables.

**After:** lists (`IList`, arrays included) and dictionaries (`IDictionary`)
become tables under every policy, as documented: a copy with Lua indices
from 1. A type the host registers explicitly, or that a registered base
type or interface covers, is still userdata. Other enumerables and other
types are registered automatically as before
([upstream #274](https://github.com/moonsharp-devs/moonsharp/issues/274)).

**Affected:** hosts using automatic registration whose scripts call list or
dictionary methods (`list.Add`, `dict.ContainsKey`) or change the C#
collection through it. Register the collection type with
`UserData.RegisterType` to keep the userdata.

## Some library functions run without a call frame

`math.abs`, `ceil`, `floor`, `sqrt`, `sin`, `cos`, `exp`, `max` and `min`,
`string.len`, `sub` and `byte`, `type`, `select('#', ...)` and
`table.insert(t, v)` on a table without a metatable run inside the VM when
their arguments have exactly the types they expect: numbers for `math`, a
string and numbers for `string`, one value for `type`. They used to push a C#
call frame and create an execution context and an argument list on every
call. Any other call (a string where a number is expected, a missing argument,
a call result as the last argument, a `string.byte` range that is not one
character) takes the ordinary path, so results and error messages do not
change.

Such a call does not appear in a call stack while it runs. These functions
call no script code on the fast path, so no traceback, debugger stop or hook
can see the difference; the one error they can raise there, `table.insert`
storing a value owned by another `Script`, has no `insert` frame in its stack. The fast path follows the
function object, not its name: a script that assigns `math.floor = f` calls
`f`, and a local copy of the library function keeps the fast path.

## Fused instructions: dump format, instruction counts, debugger stops

The compiler emits fewer, larger instructions. Results do not change; what a
host can see:

- **Binary dumps** (`Script.Dump`, `string.dump`) are format 0x154, or
  0x153 without source locations. Older MoonSharp versions reject them with
  "Invalid version". Dumps in the previous formats (0x152, 0x151) still load.
- **Instruction counts** are lower, so `AutoYieldCounter` yields and
  `ScriptOptions.InterruptHandler` calls come after more script work than
  before. A numeric `for` iteration with an empty body was 5 instructions and
  is now 1; the condition `if i < n then` with locals `i` and `n` was 5 and
  is now 1, and so is `x = x + 1`. While a debugger is attached,
  comparisons and arithmetic run unfused, so it sees the same instructions
  and stops as before.
- **Debugger stops in a numeric `for`.** The `for` line is a stop when the
  loop starts; each later iteration stops on the `end` line only. Stepping
  through `for i = 1, 2 do x = x + i end` written on lines 2 to 4 went
  2, 3, 4, 2, 3, 4, 2, 4; it now goes 2, 3, 4, 3, 4. A breakpoint on the `for`
  line is hit once per loop, not once per iteration.

## `goto` out of a `for` loop

A `goto` that left a numeric or generic `for` loop left the loop's control
values on the value stack, so an enclosing `for` loop then read them as its
own: it skipped or repeated iterations. The jump now removes them, as `break`
does. Affected: scripts that `goto` a label outside a `for` loop (a
`continue` label inside the same loop body was not affected).

## `require` follows Lua 5.2: `package.preload`, `package.path` and `package.searchers`

**Before:** `require` looked in `package.loaded`, then asked the script
loader, which read only the `LUA_PATH` global and `ModulePaths`. `package`
had only `loaded` and `config`
([upstream #285](https://github.com/moonsharp-devs/moonsharp/issues/285),
[upstream #332](https://github.com/moonsharp-devs/moonsharp/issues/332)).

**After:** `require` works as in Lua 5.2 (manual §6.3):

- `package.preload[name]` is the loader of module `name`, tried before any
  file. Hosts can put C# functions there.
- `package.path` is a string, empty at first. Loaders derived from
  `ScriptLoaderBase` search its templates first, then the `LUA_PATH` global
  or `ModulePaths` as before, skipping templates already tried. It starts
  empty because hosts often set `ModulePaths`, or a new loader, after
  creating the `Script`; those paths keep working, and a script cannot
  remove them. `IgnoreLuaPathGlobal` now ignores `package.path` as well.
  Loaders that do not derive from `ScriptLoaderBase` ignore `package.path`.
- `package.searchers` lists the functions `require` asks for a loader:
  `package.preload`, then the script loader. Scripts can add their own.
  `package.loaders`, the Lua 5.1 name that Lua 5.2 keeps, is the same table.
- The loader receives the module name and an extra value (the file name
  for a file); before, only the name.
- A module that stores its value in `package.loaded[name]` and returns
  nothing gets that value from `require`; before, `require` replaced it
  with `true`. `false` in `package.loaded` no longer counts as loaded.
- When nothing is found, the error lists what was tried, at the position
  of `require`'s caller:

  ```text
  before: libfunc_require:(12,1-44): module 'util' not found
  now:    main:(3,0-28): module 'util' not found:
          	no field package.preload['util']
          	no file 'scripts/util.lua'
  ```

  Called from C# (`pcall(require, 'util')`, `Script.Call`), the message has
  no position. A loader not derived from `ScriptLoaderBase` is named instead
  of the files: `no module 'util' in InMemoryScriptLoader`.

`require` is still compiled from Lua on first use, so loading still yields
inside coroutines, works under `pcall` and from `Script.Call`, and chunk
names do not change. The internal global `__require_clr_impl` now returns
the module or its loader; scripts should call `require`.

**Affected:** hosts or tests that match the text of the "not found" error;
scripts that tested `package.path` or `package.preload` for nil; modules that
fill `package.loaded` themselves and relied on `require` returning `true`.

## Lua patterns follow Lua 5.2

`string.find`, `match`, `gmatch` and `gsub` no longer run KopiLua's port of the Lua 5.1 matcher.
A port of Lua 5.2.4's `lstrlib.c` over string indices replaced it: it gives the same results as
`lua5.2` on 219,075 generated ASCII cases. A pattern item still matches one UTF-16 code unit.
What a script can notice:

| Case | Before | Now |
|---|---|---|
| `('  ' .. long .. '  '):match('^%s*(.-)%s*$')` on a string of more than a few dozen characters ([#318](https://github.com/moonsharp-devs/moonsharp/issues/318)) | error "pattern too complex" | the trimmed string; the limit is 200 nested items, as in Lua |
| A code unit above 255 in the subject or the pattern ([#187](https://github.com/moonsharp-devs/moonsharp/issues/187)) | compared by its low byte: `'\n'` matched `上` (U+4E0A), `%s` matched it too | compared whole; it belongs to no class (`%a`, `%s`, `%p`...), so only `.`, itself, a range such as `[一-龥]` or a complement (`%A`, `[^x]`) matches it |
| `%p` ([#134](https://github.com/moonsharp-devs/moonsharp/issues/134)) | .NET `IsPunctuation`: missed `$`, `+`, `<`, `=`, `>`, `^`, `` ` ``, `\|` and `~` | C's `ispunct`: every printable ASCII character that is not a letter, digit or space; below 256 also symbols such as `¢ © ° ×` |
| `'\0'` in a pattern | rewritten to `%z`, so `[\0-\31]` matched only `'\0'`, `'-'` and `'\31'` | an ordinary character; `%z` still works |
| `s:find(p, init)` or `s:match(p, init)` with `init` past `#s + 1` | moved back to the end: `('abc'):find('', 10)` gave `4 3` | `nil`, as in Lua 5.2 |
| `%b` without its two characters | error "unbalanced pattern" | error "malformed pattern (missing arguments to '%b')" |
| A back reference to a missing capture, `('a'):find('(a)%2')` | error "invalid capture index 2" | error "invalid capture index %2" |
| A `gmatch` iterator called through `pcall` or another C# function | `NullReferenceException` | works |

Unchanged: classification below 256 other than `%p` (`é` is still a letter), `gsub` with a table
reading it without `__index`, and `gsub` with a negative count replacing nothing (Lua 5.2 replaces
everything there; Lua 5.3 and later replace nothing). A `gsub` that matches nothing now returns the
subject string itself.

Affected: scripts that relied on any "Before" column. The old matcher is kept in the tests
(`KopiLuaPatternOracle`); `StringPatternTests` compares the two over 228,000 generated cases and
allows only the differences above.

## LuaBase, CharPtr and LuaLBuffer are gone

The public classes `LuaBase`, `CharPtr` and `LuaLBuffer` in
`MoonSharp.Interpreter.Interop.LuaStateInterop` were helpers for porting C library code: a C-string
pointer emulation, `ctype.h` functions and `lua_*`/`luaL_*` look-alikes. MoonSharp's own ports used
them (`string.format` and the pattern functions); neither does any more, so they left the
interpreter (7 KB of the DLL). `LuaState` and `ScriptExecutionContext.EmulateClassicCall` stay.

Affected: hosts that subclass `LuaBase` or use `CharPtr`. Copy the three files from MoonSharp 2.0
([LuaStateInterop](https://github.com/moonsharp-devs/moonsharp/tree/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/LuaStateInterop))
into your project; they need only the public API. Scripts are not affected.

## Typed calls from C# to Lua

New, nothing changes for existing calls. `Closure.Call<TResult>()` up to
`Closure.Call<T1, T2, T3, T4, TResult>(...)` call a Lua function with typed
arguments and return its first result as `TResult`:

```csharp
Closure area = script.DoString("return function(w, h) return w * h end").Function;
double a = area.Call<double, double, double>(3, 4.5);
```

Arguments convert as `Call(params object[])` converts them (custom
converters, `ExactInt64Interop`, ownership checks and errors included), and
the result as `DynValue.ToObject<T>()` converts it. Numbers, booleans,
strings, tables, functions and `DynValue`s go to the function without boxing,
without an argument array and without a `DynValue` each; a `double`,
`float`, `int`, `long`, `bool`, `string`, `Table` or `Closure` result comes
back without a `DynValue`. A call with three numbers and a number result
allocates nothing (224 bytes through `Script.Call`). With `DynValue` as
`TResult` all results come back, as `Call()` returns them; use it when the
result does not matter.

## Lua functions convert to delegate parameters

**Before:** a Lua function passed to a C# parameter of type `Action`,
`Func<...>` or any other delegate type failed with "cannot convert" (or
"function call doesn't match any overload"); only `Closure`,
`ScriptFunctionDelegate`, `object` and `DynValue` parameters took one
([upstream #186](https://github.com/moonsharp-devs/moonsharp/issues/186)).

**After:** the function converts to the delegate when its `Invoke` has up to
four parameters, none by reference; the delegate converts its arguments and
result as the typed calls above. `Closure.ToDelegate<TDelegate>()` does the
same from C#. A function returns the same delegate for a type every time, so
a handler added with `+=` can be removed with `-=`. The delegate keeps the
function and its script alive. Overloads still prefer `Closure`,
`ScriptFunctionDelegate`, `object` and `DynValue` parameters; a delegate
parameter weighs less, so only calls that failed before change.

On AOT platforms the delegate for a type with value-type parameters or
result needs code compiled ahead of time: the source generator writes it for
delegate types its generated members take and for delegate types listed with
`[assembly: MoonSharpUserDataTypes]` (`GeneratedUserData.RegisterAll()` or
`RegisterDelegateTypes()` registers them), or register one with
`UserData.RegisterDelegateType<TDelegate>(factory)`. Without one, the
conversion fails with an error that names the type and these fixes.

**Affected:** hosts whose scripts relied on such calls failing; the source
generator no longer registers a listed delegate type as a userdata type by
reflection (it warned MSUD002 for it before), it writes the factory instead.

## Overload choices follow custom converters added later

**Before:** the overload resolver remembered its choice for the last five
argument signatures of a method, and kept it after a custom converter was
set or removed. With `G(int)` and `G(string)`, `o:G(2)` called `G(int)`;
after a number-to-`string` converter it still called `G(int)`, although the
converter weighs more than the number downcast.

**After:** setting, removing or clearing a script-to-CLR converter makes
every method resolve its overloads again. Overloaded calls whose arguments
are nil, booleans, numbers, strings or functions now look the overload up in
a table keyed by the argument kinds (and whether each number has a fraction),
filled from the same weights; the table is skipped while any custom
converter is registered or an extension method of the same name applies.

**Affected:** hosts that register custom converters after scripts have
called overloaded methods.

## Empty result lists are no values

**Before:** a C# function that returned no values (`DynValue.NewTuple()`
with no arguments, and so `table.unpack({})`, `string.byte('')`,
`coroutine.yield()` resumed with nothing) gave one nil:
`select('#', table.unpack({}))` was 1, `f(1, table.unpack({}))` passed `f`
two values, `pcall(table.unpack, {})` returned `true, nil`, and a coroutine
resumed with no values saw one nil from `coroutine.yield()`
([upstream #287](https://github.com/moonsharp-devs/moonsharp/issues/287),
[#198](https://github.com/moonsharp-devs/moonsharp/issues/198)). A Lua
function that returned nothing gave no values already, but `(f())` kept "no
value" instead of nil, and `return 1, f()` returned two values.

**After:** as in Lua 5.2. `DynValue.NewTuple()` with no values returns
`DynValue.Void`, the value a function that returns nothing gives; argument
lists, `return` lists, `pcall`, `coroutine.resume` and `coroutine.yield`
add no value for it, and parentheses turn it into nil.

**Affected:** hosts that test `DynValue.NewTuple()` or a call's result for
`DataType.Nil`: test `IsNil()`, which is true for both. Scripts that counted
the extra nil.

## Tail calls from C# functions keep their continuation

**Before:** a C# function can return a tail call request
(`DynValue.NewTailCallReq`) with a continuation, a C# function that receives
the callee's results. The VM ran it when the callee was a Lua function and
dropped it when the callee was a C# function. Because of that `pcall`
refused such functions: `pcall(tostring, x)` for an `x` with a `__tostring`
metamethod failed with "the function passed to pcall cannot be called
directly by pcall", and so did `pcall(coroutine.yield, ...)`.

**After:** the continuation runs for every callee, also after a C# callee
yields, and errors before it runs reach the caller's `pcall`. `pcall` and
`xpcall` take any C# function: `pcall(tostring, x)` returns `true` and the
text, and `pcall(coroutine.yield, v)` yields `v` and returns `true` and the
resume values, as in Lua 5.2. A C# `__tostring` now goes through `tostring`'s
check like a Lua one: a number becomes its text (Lua 5.2 accepts numbers;
MoonSharp rejected them from Lua functions too before), a table or a boolean
is the error "'tostring' must return a string".

**Affected:** C# `__tostring` functions that return values other than
strings, numbers and nil. `Script.Call(pcall, ...)` from C# still hands
the host the tail call request when the called function has a continuation
(docs/ROADMAP.md).

## Metamethods nest 200 levels deep and return one value

**Before:** a metamethod written in Lua that triggers itself, such as
`__index = function(t, k) return t[k] end`, ran forever: once 65,536 frames
were on the stack, the tail call optimisation reused them. A metamethod's
results were not adjusted, so `select('#', t.x)` was 0 when `__index`
returned nothing and 2 when it returned two values.

**After:** as in Lua 5.2, metamethod calls (`__index`, `__newindex`,
arithmetic, comparison, `__concat`, `__len`, `__unm`) nested 200 deep raise
"C stack overflow" at the expression that triggered the last one; `pcall`
catches it and the `Script` stays usable. `__call` is not counted, as in Lua.
A metamethod's result is one value: nil when it returns nothing.

**Affected:** scripts with metamethod recursion deeper than 200 levels, which
Lua 5.2 rejects too.

## Stack overflow as a chunk starts has a position

**Before:** an error in the instructions a chunk starts with, the value stack
overflowing when a module's chunk starts during a cyclic `require`, was
reported as `bytecode:<n>: stack overflow`.

**After:** it is reported at the chunk's first statement
(`mod:(1,0-19): stack overflow`).

**Affected:** nobody, unless a host parsed the `bytecode:` text.

## Long library calls can be interrupted

**Before:** `ScriptOptions.InterruptHandler` (and the hosting time limit) ran
only between VM instructions, so one long library call could not be stopped:
`string.rep('a', 2e7):gsub('a', 'b')` ran 2.6 s under a 100 ms time limit.

**After:** the pattern functions (`find`, `match`, `gmatch`, `gsub`),
`string.rep`, `table.concat` and `table.sort` call the handler every 10,000
steps too (match attempts and backtracking steps, repetitions, elements,
comparisons). The instruction count the handler receives does not change, so
an instruction limit stays deterministic and does not stop such a call; a
time limit or a cancellation does. An interruption inside a `table.sort`
comparator now stops the script; `table.sort` used to drop it and return a
half-sorted table.

**Affected:** hosts whose interrupt handler counts its own calls.

## Library calls raise Lua errors instead of ending the host

**Before:** some library calls threw a CLR exception that `pcall` could not
catch, so a script could end the host process: `pcall()`,
`pcall(coroutine.wrap(f))` when `f` failed or was dead, `dofile` and
`loadfile` of a missing file (or of `""`), `io.input`, `io.output` and
`io.lines` of a file that does not open, `file:seek` before the start of the
file, `file:read` with a negative count, reading a file opened only for
writing (and writing one opened only for reading), calling an exhausted
`io.lines` iterator, `string.rep` with 2^31 copies or a result longer than a
CLR string, `os.time` with a field out of range (`month = 13`, `day = 0`),
and `os.date` with a time `DateTime` cannot hold or NaN.

**After:** each is a Lua error or a Lua result, with Lua 5.2's text where
Lua 5.2 has one:

| Call | Result |
|---|---|
| `pcall()` | error "bad argument #1 to 'pcall' (value expected)" |
| `pcall(nil)` | `false, "attempt to call a nil value"` (was "attempt to pcall a non-function") |
| `pcall(coroutine.wrap(f))` | `false` and `f`'s error, or "cannot resume dead coroutine" |
| `dofile(name)`, `loadfile(name)` of a missing file | error, or `nil` and the message: "cannot open name: No such file or directory" |
| `io.input(name)`, `io.output(name)`, `io.lines(name)` | error "cannot open file 'name' (No such file or directory)" |
| `f:seek("set", -1)` | `nil, "Invalid argument", 22` |
| `f:read(-1)` | the rest of the file (Lua 5.2 tries to allocate (size_t)-1 bytes and fails) |
| `f:read()` on a write-only file, `f:write(s)` on a read-only one | `nil, "Bad file descriptor", 9` |
| `string.rep("x", 2^31)` | error "resulting string too large" |
| `os.time{year = 2020, month = 13, day = 1}` | January 2021, as C's `mktime` normalises; `nil` for a date `DateTime` cannot hold |
| `os.date("%Y", 2^53)`, `os.date("%Y", 0/0)` | `nil` (Lua 5.2 on Linux gives year 285428751 for 2^53) |

The message of a loader that fails is the loader's: `FileSystemScriptLoader`
gives "No such file or directory", the Unity loader its own text.

**Affected:** scripts that relied on these calls failing in another way.
Hosts that caught `FileNotFoundException` from `dofile` or `loadfile` now see
a `ScriptRuntimeException` (or `loadfile`'s `nil` result).

## `io.lines` and `file:lines` return functions that read as they go

**Before:** they read the whole file up front and returned an enumerator
userdata; calling it after the end threw `NotSupportedException`, and
`f:lines("*a")`, whose result never ends, filled the memory.

**After:** as in Lua 5.2 they return a function that reads one item per call
with the given formats and returns nothing at the end of the file.
`io.lines(name)` closes the file there, so a further call raises "file is
already closed"; `io.lines()` without a name reads the default input.

**Affected:** scripts that checked `type(io.lines(name))` (now `"function"`).

## `io.open` modes follow C's `fopen`

**Before:** `"w+"` failed on a file that did not exist, `"a"` and `"a+"`
always failed ("Append access can be requested only in write-only mode"),
`"r+"` created a missing file, and `"rw"`, `"rt"` and other strings made of
`rwab+t` were accepted.

**After:** `"r"` and `"r+"` need the file, `"w"` and `"w+"` create or truncate
it, `"a"` and `"a+"` create it and write at its end (`"a+"` reads too). A mode
other than one of `r`, `w`, `a`, an optional `+` and then only `b`s raises
"bad argument #2 to 'open' (invalid mode)", as in Lua 5.2, so `"rt"` is now
an error.

**Affected:** scripts that pass `"t"` in a mode; drop it.

## `string.rep` keeps the separators of an empty string

`string.rep("", 3, ".")` was `""`; it is `".."`, as in Lua 5.2.

## `string.format` follows Lua 5.2 and C's printf

**Before:** `string.format` reproduced a port of C's `sprintf` byte for byte,
with its quirks: .NET number formats (`1.000000e+004`), the precision of
`%d` ignored, the width of `%c` ignored, `%a` unknown, `%s` refusing `nil`,
booleans and tables, `%d` and `%x` wrapping out-of-range numbers (and, for
negative numbers, giving 0 on .NET 9 and later), and several inputs ending
the host (`%.10s` of a shorter string, `%c` outside 0..65535, zero padding
with `+` or `#` to a width of 0 or 1, items over 512 characters).

**After:** the output is that of Lua 5.2.4 with glibc's printf, checked
against `lua5.2` on 709,800 generated cases and a golden file recorded from
it (`Lua52Differential/gen_format_golden.py`):

```lua
string.format("%e", 12345.678)   -- before: 1.234568e+004   now: 1.234568e+04
string.format("%.3d", 7)         -- before: 7               now: 007
string.format("%s %s", nil, true)-- before: error          now: nil true (and __tostring)
string.format("%x", -1)          -- before: ffffffffffffffff (0 on .NET 9+)   now: error "not a non-negative number in proper range"
string.format("%d", 2^63)        -- before: -9223372036854775808   now: error "not a number in proper range"
string.format("%a", 1)           -- before: error           now: 0x1p+0
string.format("%q", "\r")        -- before: "\r"            now: "\13"
string.format("[%5c]", 65)       -- before: [A]             now: [    A]
```

Floating-point conversions use the exact value of the double, rounded half
to even, so the text is the same on every runtime: before, .NET Framework,
Mono and Unity printed `%.2f` of 1.005 as `1.01` and `%.0f` of 2.5 as `3`,
where Lua and .NET Core print `1.00` and `2`.

Kept on purpose: `%s` and `%q` of a number use MoonSharp's `tostring` text
(an open question for the owner, see docs/ROADMAP.md); `%c` of 256 to 65535
gives that UTF-16 character, where Lua gives the low byte (other values give
the low byte, as in Lua: `%c` of -1 is `"\255"`); `%#g` keeps its zeros
after a rounding carry ("1.00000e+06" for 999999.5), as C specifies, where
glibc drops them.

**Affected:** scripts and hosts that compare `string.format` output with
`%e`, `%E`, `%g` or `%G` exponents, `%d` precisions, `%q` of control
characters, or that format negative or huge numbers with `%x`, `%o`, `%u`,
`%d`.

## `and` and `or` give one value

**Before:** a call or `...` as an operand of `and`/`or` passed all its
results on: `print(nil or f())` printed every result of `f`,
`table.insert(t, x or f())` passed extra arguments, and `select('#', nil or
g())` was 0 when `g` returned nothing.

**After:** as in Lua 5.2 (3.4, "In all other contexts, Lua adjusts the result
list to one element") each operand is cut to one value, nil when a call
returns nothing.

**Affected:** scripts that relied on the extra values, which Lua drops.

## `^` binds tighter than a unary operator inside an expression

**Before:** `local x = 3; print(1 + -x^2)` printed 10: after a binary
operator, `-x` was closed before `^` joined, so it computed `(-x)^2`. `-x^2`
at the start of an expression was already right.

**After:** `1 + -x^2` is `1 + -(x^2)`, -8, as in Lua 5.2; so are
`2 * -x^2`, `true and -2^2` and `2^-x^2`.

**Affected:** arithmetic written with a unary minus after an operator and
before `^`, which gave the wrong sign.

## Multiple assignment evaluates every target first

**Before:** in `i, a[i] = i + 1, 20` the key of `a[i]` was read after `i`
had been assigned, so `a[2]` was set, and `t, t.x = {}, 1` wrote into the new
table. Targets were stored left to right.

**After:** as Lua 5.2 3.3.3 says ("The assignment statement first evaluates
all its expressions and only then are the assignments performed"), the tables
and keys of all targets are evaluated, left to right, before the values;
then the stores run from the last target to the first, as in reference Lua.
So `a[1]` is set, `a, a = 1, 2` leaves 1, and `__newindex` sees `T.B` before
`T.A` in `T.A, T.B = 1, 2`. A single assignment is unchanged.

**Affected:** scripts whose multiple assignments read a variable they also
assign, or whose `__newindex` depends on the order of the stores.

## `goto` may jump to a label at the end of a block

**Before:** `do goto e; local x = 1; ::e:: end`, and so `goto continue` past
a `local` to `::continue::` at the end of a loop body, raised "jumps into the
scope of local 'x'".

**After:** as in Lua 5.2 (3.5: a local's scope lasts until the last non-void
statement of its block), a label followed only by labels and `;` up to the
end of its block is outside the block's locals. Before `until` it is not,
because the condition still sees them; there the error stays, as in Lua.

## `__eq` runs for two metatables with the same handler

**Before:** two tables compared with `==` called `__eq` only when they had
the same metatable.

**After:** as in Lua 5.2, `__eq` also runs when the two metatables are
different but their `__eq` fields are the same function. Userdata keep
MoonSharp's rule (`__eq` whenever either side is a userdata).

**Affected:** class hierarchies that copy `__eq` into each class: instances
of different classes now compare with it.

## `math.fmod` and `math.modf` follow C

**Before:** `math.fmod` rounded the quotient (`math.fmod(7, 4)` was -1) and
`math.modf` floored (`math.modf(-3.7)` was -4, 0.3; `math.modf(1/0)` gave
NaN as the fraction).

**After:** C's `fmod` and `modf`, as Lua 5.2: `math.fmod(7, 4)` is 3,
`math.fmod(-7, 4)` is -3, `math.modf(-3.7)` is -3, -0.7, `math.modf(1/0)` is
inf, 0.

## `bit32` follows Lua 5.2

**Before:** arguments went through a cast of the double to `uint`, which
.NET 9 and later and ARM64 saturate, so `bit32.band(-1)` was 0 there
(4294967295 on .NET 8 x64); fractions were truncated (`band(1.5)` was 1);
shifts by 32 or more wrapped (`lshift(1, 33)` was 2); `arshift` returned
negative numbers; `extract` and `replace` refused fields ending at bit 31,
and `replace` did not shift its value into place; `band()`, `bor()`,
`bxor()` and `btest()` needed an argument.

**After:** as Lua 5.2's lbitlib on every runtime: arguments rounded to the
nearest integer (halves to even) and reduced modulo 2^32, shifts of 32 or
more give 0, `arshift(-1, 1)` is 4294967295, `extract(x, 28, 4)` and
`replace(0, 1, 2)` (4) work, and `band()` is 4294967295, `bor()` and `bxor()`
0, `btest()` true. The public helper `Bit32Module.Bitwise` is gone.

**Affected:** scripts that used `bit32` with negative numbers, fractions,
shifts of 32 or more, or relied on the old `replace`; hosts that called
`Bit32Module.Bitwise`.

## `load` readers, `mode` and `env`, and `package`

**Before:** `load` with a reader treated `""` (the end of the chunk) as an
error, let an error raised in the reader propagate, ignored `mode`, accepted
only a table as `env` (an explicit `nil` meant the global table), and
refused a number as the chunk. `package.loaded` had no `_G` and no `package`
entry, and `package.cpath`, `package.loadlib` and `package.searchpath` did
not exist.

**After:** as in Lua 5.2: `nil`, nothing or `""` from the reader ends the
chunk, a reader's error is returned as `nil` and the message, `mode` `"t"`
or `"b"` refuses the other kind of chunk ("attempt to load a binary chunk
(mode is 't')"; MoonSharp's binary chunks are `string.dump` text), any value
given as `env` becomes the chunk's `_ENV`, `nil` included, and a number is
loaded as its text. `package.loaded._G` and `package.loaded.package` are set,
`package.cpath` is `""`, `package.loadlib` returns `nil`, "dynamic libraries
not enabled; check your Lua installation", "absent", and
`package.searchpath(name, path)` tries each template through the script
loader and returns the first file found, or `nil` and the files tried.

**Affected:** scripts that called `load(s, name, mode, nil)` expecting the
global table, and hosts that list the fields of `package` (the globals
snapshot gained three).

## `coroutine.wrap` functions keep their coroutine in the delegate

The function `coroutine.wrap` returns no longer stores its coroutine in
`CallbackFunction.AdditionalData` (`pcall` runs a C# function with its own
context, where that data was not found). Hosts that read the coroutine from
there should keep their own reference.

## Tables keep values, not `DynValue` objects

Tables store keys and values in the VM's own value struct, so a script that
stores a number into a table allocates nothing.

**Before:** a table kept the `DynValue` object it was given, and `Get`
returned that object. A host that stored a writable `DynValue` and later
called `Assign` on it changed the table, and every read of a field returned
the same object.

**After:**

- `Set` copies the value. Changing the `DynValue` afterwards does not change
  the table.
- `Get`, `RawGet`, `Pairs`, `Keys`, `Values` and `NextKey` return a read-only
  `DynValue` made for that read, keys included. Nil, booleans and the
  integers -256..4095 come from shared instances; any other value is a new
  object on every read, so compare values with `Equals` or by value, not by
  reference, and expect a different `ReferenceID` from each read.
- `RawGet(i)` of an array key that a script set to nil returns null, as for
  any absent key. It returned `DynValue.Nil`.

**Memory:** a slot of the array part takes 24 bytes and an entry of the hash
part 56 bytes. Before, a slot took 8 bytes and an entry 24, plus a 40-byte
`DynValue` for each key and each value that was not a shared instance. Tables
of fractional numbers, strings or tables are smaller than before; tables of
small integers are larger (a 16-item integer array: 295 to 551 bytes).

**Affected:** hosts that change a stored `DynValue` with `Assign`, that compare
values read from tables by reference, or that test `RawGet` against
`DynValue.Nil`.

## `table.unpack`, `table.insert` and `table.remove` follow Lua 5.2's argument rules

**Before:**

- `table.unpack(t, i, j)` with `i > j + 1`, such as `table.unpack({}, 2)`, ended
  the process with `OverflowException`; `pcall` could not catch it. Any count
  of results was accepted: `table.unpack({}, 1, 1e7)` returned ten million nils.
- `table.remove(t, #t + 1)` raised "position out of bounds", so did
  `table.remove(q, 1)` on an empty queue, and a third argument raised "wrong
  number of arguments".
- `table.insert(t, pos, nil)` took the nil for the two-argument form and
  appended `pos`; `table.insert(t)` was accepted.

**After:**

- `table.unpack` returns no values when `i > j`, and raises "too many results
  to unpack" for a million results or more (Lua 5.2's stack limit). Positions
  are read as `luaL_optint` reads them: `table.unpack({}, 1, 2^40)` returns no
  values, as in Lua 5.2.
- `table.remove` accepts positions 1 to `#t + 1` (and `#t`), returns `t[pos]`
  and ignores extra arguments.
- `table.insert` picks its form by the number of arguments: three inserts at
  `pos` even when the value is nil, two appends, anything else raises "wrong
  number of arguments to 'insert'".

`table.insert` still returns the table, where Lua 5.2 returns nothing.

**Affected:** scripts that relied on the old errors or on `table.insert(t, pos, nil)`
appending.

## C# function calls reuse their argument object; host callbacks get their own context

**Before:** every call from Lua to a C# function created a new
`ScriptExecutionContext` and a new `CallbackArguments`; outside the standard
library it also copied the arguments into a new `DynValue[]`.

**After:** the processor keeps one `ScriptExecutionContext` per call depth and
lends it only to C# functions that never hand it to host code:

- standard library functions and MoonSharp's own callbacks: the iterators of
  `pairs`, `ipairs`, `string.gmatch`, `io.lines` and CLR enumerables,
  `coroutine.wrap`'s function, `package.searchers`, the comparison and length
  operators of userdata, enum flag helpers, int64 constructors and event
  `add`/`remove`;
- userdata methods (reflection, hardwired and source-generated descriptors),
  extension methods and delegates converted by MoonSharp, when the CLR method
  takes neither a `ScriptExecutionContext` nor a `CallbackArguments`
  parameter.

Every other C# function gets a new context on every call and may keep it:
host callbacks (`DynValue.NewCallback`, `new CallbackFunction(...)`, with or
without `UsesArgumentsOnlyDuringCall`), CLR methods with a
`ScriptExecutionContext` or `CallbackArguments` parameter,
`ObjectCallbackMemberDescriptor` members, and descriptor classes of your own
derived from `FunctionMemberDescriptorBase` or `MethodMemberDescriptor`. A
context kept after its call reports that call's `CallingLocation` and
`AdditionalData`, as in MoonSharp 2.0.

Standard library functions, userdata methods and delegates converted by
MoonSharp also get a reused `CallbackArguments` that reads the arguments in
place on the value stack. A CLR method with a `CallbackArguments` parameter
gets a copy, which it may keep.

Host callbacks still get a `CallbackArguments` of their own, which they may
keep. A host callback that does not keep it can say so:

```csharp
script.Globals["add"] = new CallbackFunction(
    (ctx, args) => DynValue.NewNumber(args[0].Number + args[1].Number))
    { UsesArgumentsOnlyDuringCall = true };
```

Such a callback must not read its `CallbackArguments` after it returns; it
reads the value stack, which later calls overwrite. The flag is about the
arguments only: the callback still gets a context of its own.

Bytes per call (`AllocationBudgetTests`): `string.format` 216 to 120, a host
callback with one argument 192 to 216 (152 with
`UsesArgumentsOnlyDuringCall`; the context grew by the slot that keeps a
result given with `ScriptExecutionContext.Return`), a userdata method call and
a property read through reflection 424 to 256, `coroutine.wrap` plus
`coroutine.yield` 248 to 72.

A descriptor class of your own derived from `FunctionMemberDescriptorBase`
can set the protected `ExecuteKeepsNoContext` when its `Execute` hands the
context only to a `ScriptExecutionContext` parameter of the member; its calls
then share the reused context too. Generated descriptors set it.

**Affected:** no behaviour changes for host code: every context it can
receive is its own, as in MoonSharp 2.0. Host callbacks allocate that context
again on every call.

## C# functions can read arguments and return numbers without a DynValue

New, opt-in API for callbacks that run often:

- `CallbackArguments.TypeAt(i)`, `NumberAt(i)`, `BooleanAt(i)`, `StringAt(i)`,
  `TableAt(i)`, `FunctionAt(i)` and `UserDataAt(i)` read an argument without
  creating a `DynValue`. `TypeAt` is the type of
  `RawGet(i, false) ?? DynValue.Void`: `Void` for a missing argument and for a
  call that returned nothing.
- `ScriptExecutionContext.Return(double)` and `Return(string)` give the
  callback's result to the VM without a `DynValue` when Lua called the
  callback. Use them only as the return value of the callback the context was
  passed to (`return ctx.Return(x);`): the `DynValue` they return is a
  placeholder for the value kept in the context. Called from C# (for example
  through `Script.Call` or `CallbackFunction.Invoke`), the callback's result is
  an ordinary `DynValue`.

```csharp
script.Globals["add"] = new CallbackFunction(
    (ctx, args) => ctx.Return(args.NumberAt(0) + args.NumberAt(1)))
    { UsesArgumentsOnlyDuringCall = true };
```

This callback allocates only its own context per call (72 bytes; see above);
written with `args[0].Number` and `DynValue.NewNumber` it allocates 240 bytes
per call with two large numbers on `next` before this change, 264 now (192
with `UsesArgumentsOnlyDuringCall`).

**Affected:** nobody; existing callbacks behave as before.

## Tail call optimization keeps the frame C# called

**Before:** past `ScriptOptions.TailCallOptimizationThreshold` (65,536 by
default, counted in call frames or in value stack slots) a tail call in the
function that C# called (the chunk of `DoString`, a function run by
`Script.Call`) removed that function's frame. A tail-called C# function then
failed with `IndexOutOfRangeException` (`return tostring(1)` with the threshold
at 1), and an error in a tail-called Lua function unwound past the frame into
the frames of the C# caller's own Lua code.

**After:** that frame never takes part in the optimization; every other tail
call does as before.

**Affected:** scripts that ran with a low threshold or a value stack deeper
than the threshold.

## C# functions called by `pcall` or `ScriptExecutionContext.Call` get their own context

**Before:** `pcall(f)` with a C# function `f`, and `context.Call(f)` from a
C# function, ran `f` with the caller's `ScriptExecutionContext`, so
`context.AdditionalData` in `f` was the caller's (`pcall`'s: null).

**After:** `f` gets a context with its own `AdditionalData`; the processor,
calling location and everything else are the caller's, as before. Error texts
do not change.

**Affected:** host callbacks that read `AdditionalData` and are called through
`pcall`, `xpcall` or `ScriptExecutionContext.Call`.

## The browser package has an AOT build in its own folder

`src/Hosting/browser` now ships a second, ahead-of-time compiled build next to
the default interpreter build (docs/hosting.md, "Two browser builds"). The
default is unchanged: `dotnet publish -c Release` still writes the page to
`bin/Release/net10.0/publish/wwwroot`. A publish with
`-p:RunAOTCompilation=true` used to write to the same folder and now writes to
`bin/Release-aot/net10.0/publish/wwwroot` (and `obj/Release-aot`). Affected:
scripts that published the package with that switch and read the result from
the old folder.
