# Migrating

Changes in this fork that a script or a host can notice, one section per change: what it was, what it
is now, and who is affected.

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
