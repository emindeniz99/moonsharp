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

`x >= y` with only `__lt` gives the same result as before (`not __lt(x, y)`). Error messages for
comparisons of unrelated types still name the operands in source order.

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
