# Migrating from upstream MoonSharp

Changes in this fork that scripts or hosts can observe, compared with upstream MoonSharp 2.0
(`moonsharp-devs/moonsharp@fbd0367`). Each section says what changed, why, and who is affected.

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

A stack probe (`RuntimeHelpers.EnsureSufficientExecutionStack`) stays behind the counts for threads
with a small stack, and stops very long operator chains such as a generated `1+1+...+1` with a million
terms, which parse in a loop but compile recursively.

**Affected:** code nested deeper than Lua 5.2 allows, which MoonSharp used to accept as long as the
stack held. On IL2CPP the stack probe depends on the runtime's support, which has not been verified;
the counts work everywhere.

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
