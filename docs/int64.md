# Exact 64-bit integers: int64 and uint64

MoonSharp implements Lua 5.2, where every number is a double. A double holds
every integer exactly only up to 2^53-1 (9007199254740991). Above that,
integers round to the nearest representable value without any error:

```lua
print(9007199254740993 == 9007199254740992)  --> true
```

That breaks 64-bit ids, timestamps in nanoseconds and 64-bit bitfields. This
fork adds two userdata types that hold 64-bit integers exactly, `int64` and
`uint64`, and opt-in ways to get them from the CLR and from JSON. Nothing
changes for a script or host that does not opt in. The VM is unchanged: the
values are userdata with metamethods.

See also <https://github.com/moonsharp-devs/moonsharp/issues/300>.

## The model

- `int64` wraps a CLR `long`, `uint64` wraps a `ulong`. On the CLR side they
  are `MoonSharp.Interpreter.LuaInt64` and `LuaUInt64`: immutable, equal and
  hashed by value.
- A number is accepted as an exact integer only when it is integral and within
  +/-(2^53-1), the "safe" range. 2^53 itself is refused, because 2^53+1 also
  rounds to it, so a double equal to 2^53 may already be a rounded value.
- Every conversion that could round either succeeds exactly or fails loudly.
  None of them rounds silently.

## Opt-ins

| What | How | Default |
|---|---|---|
| The `int64` and `uint64` Lua modules | `new Script(CoreModules.Preset_Default \| CoreModules.Int64)` | Not in any preset |
| CLR `long`/`ulong` reach Lua as int64/uint64 | `script.Options.ExactInt64Interop = true`, or `Script.DefaultOptions.ExactInt64Interop` for new scripts | Off: they become numbers |
| JSON integers above 2^53-1 parse exactly | `json.parse(text, emptyArrays, "int64")` or `"string"` | `"number"`: they round |

Some parts need no opt-in:

- CLR `long`/`ulong` parameters accept int64/uint64 arguments.
- `json.serialize` writes int64/uint64 values as exact integer literals.
- A host can create values with `LuaInt64.NewDynValue(long)` or by storing a
  `LuaInt64` object, for example `script.Globals["id"] = new LuaInt64(x)`.

## Lua API

`int64(v)` and `uint64(v)` construct values. `v` can be:

| `v` | Result |
|---|---|
| int64 / uint64 | The same value; converting between the two checks the range (`uint64(int64(-1))` is an error) |
| number | Exact if integral and within +/-(2^53-1); otherwise an error |
| string | Decimal digits with an optional sign and surrounding whitespace, range-checked: `int64("9223372036854775807")`. Hex, fractions and exponents are errors |

Functions (`int64.*`; `uint64.*` has the same set except `min` and `sar`):

| Function | Meaning |
|---|---|
| `int64.max`, `int64.min`, `uint64.max` | Limits, as values |
| `int64.is(v)` | `true` if `v` is an int64 (`uint64.is` for uint64) |
| `int64.tostring(x)` | Decimal digits |
| `int64.tonumber(x)` | A number if `x` is within +/-(2^53-1), otherwise `nil` plus a message, so `assert(int64.tonumber(x))` works |
| `int64.eq(a, b)` | Numeric equality across int64, uint64 and numbers |
| `int64.band(a, b, ...)`, `bor`, `bxor` | Bitwise and, or, xor over one or more arguments |
| `int64.bnot(x)` | Bitwise not |
| `int64.shl(x, n)`, `int64.shr(x, n)` | Shifts; `shr` is logical (zeros come in) |
| `int64.sar(x, n)` | Arithmetic shift right (the sign bit comes in) |

Arguments of `int64.*` functions are int64 values or numbers that are exact
integers. `uint64.*` functions take uint64 values or non-negative exact
integer numbers. A shift count of 64 or more shifts every bit out (the CLR
would use the count modulo 64), and a negative count shifts the other way, as
in Lua 5.3.

### Arithmetic

| Operator | int64 | uint64 |
|---|---|---|
| `+ - *` | Wrap around (two's complement), as Lua 5.3 integers | Wrap around modulo 2^64 |
| `/` | Floor division, Lua 5.3's `//` | Unsigned division |
| `%` | Floor modulo (sign of the divisor), so `a == (a / b) * b + a % b` | Unsigned modulo |
| unary `-` | Wraps: `-int64.min == int64.min` | `0 - x` modulo 2^64 |
| `^` | Error | Error |

`/` is integer division because the result stays an int64: a float result
would round, and Lua 5.2 has no `//` operator to put it on. Convert with
`int64.tonumber` when you want floating-point division. `int64.min / -1`
wraps to `int64.min`, and dividing by zero is an error.

An operand may be a number only if it is an exact integer (`int64(1) + 1.5`
and `int64(1) + 2^53` are errors). int64 and uint64 do not mix in arithmetic;
convert one side with `int64()` or `uint64()`. Strings are not converted to
numbers.

### Comparison and equality

- `<`, `<=`, `>`, `>=` compare exactly between int64, uint64 and numbers in any
  order, including non-integral numbers: `int64("9007199254740993") > 2^53` is
  true and `int64(2) < 2.5` is true.
- For `<` and `<=`, MoonSharp tries the first operand's `__lt`/`__le`, then
  the second's, so mixed comparisons work in both orders. Lua 5.2 does the
  same (its `luaV_lessthan` uses the binary-handler lookup); Lua 5.1 required
  both operands to share one handler.
- For `==`, MoonSharp calls `__eq` whenever either side is a userdata, while
  Lua 5.2 calls it only for two userdata with the same handler. The int64
  handler returns false for mixed types, so the result matches Lua 5.2.
- MoonSharp compiles `a >= b` as `not (a < b)` and `a > b` as
  `not (a <= b)`. Against NaN, `<` and `<=` are false and `>`/`>=` are true,
  the same as for plain numbers in MoonSharp.
- `==` follows Lua's rule that values of different types are never equal:
  `int64(5) == 5` and `int64(5) == uint64(5)` are false, while two int64
  values with the same value are equal. Use `int64.eq` to compare across types.

### Table keys

int64 and uint64 values index tables by value: a value built twice finds the
same slot. `t[int64(5)]`, `t[uint64(5)]` and `t[5]` are three different keys,
in line with `==`. Normalize keys first, for example with `int64(v)`, when
they can arrive as different types.

`DynValue.GetHashCode` hashes these two types by value. Other userdata keep
the constant hash code, because their equality can depend on mutable state and
the hash is cached in the `DynValue`.

### Strings

`tostring(x)`, `print(x)` and `..` give the decimal digits. MoonSharp's
`string.format("%s", x)` accepts only strings and numbers, so use
`string.format("%s", tostring(x))`. `string.format("%d", x)` is an error, not
a rounded number.

The global `tonumber(x)` returns `nil` for an int64, as for any userdata. Use
`int64.tonumber`.

## CLR interop

### Lua to CLR

int64 and uint64 arguments convert to CLR parameters as follows:

| Parameter type | int64 | uint64 | Overload weight |
|---|---|---|---|
| `long`, `long?` | Exact | Range-checked | Exact match (int64), downcast (uint64) |
| `ulong`, `ulong?` | Range-checked | Exact | Downcast (int64), exact match (uint64) |
| `int`, `uint`, `short`, `ushort`, `sbyte`, `byte`, `decimal` | Range-checked | Range-checked | Same as a number downcast |
| `double`, `float` | Not converted | Not converted | No match |
| `object` | The `LuaInt64`/`LuaUInt64` object | Same | Exact match |
| `string` | Decimal digits | Decimal digits | Same as other userdata |

A value out of range raises a script error ("int64 value -1 is out of range
for UInt64"). Doubles are excluded because the conversion would round again;
call `int64.tonumber` first. With overloads `F(long)` and `F(double)`, an
int64 picks `F(long)` and a number still picks `F(double)`.

Plain numbers passed to `long` parameters convert as before. A number that has
already rounded, such as the literal 9007199254740993, arrives rounded.

On the host, read values with the usual conversions: `dynValue.ToObject<long>()`,
or `ToObject<LuaInt64>()` for the wrapper.

### CLR to Lua

With `ExactInt64Interop` on, every `long` and `ulong` that reaches the script
becomes an int64 or uint64, whatever its magnitude: method results and out
parameters, properties, fields, arguments of `Script.Call`, and values written
through `Table` indexers. The type depends only on the CLR type, so a field
always has the same Lua type and works the same way as a table key. `int` and
smaller types stay numbers, since a double holds them exactly. Enums keep
their existing conversion.

## JSON

`json.parse(text, emptyArrays, bigIntegers)`. In C#,
`JsonTableConverter.ParseString(json, script, emptyArrays, JsonBigIntegerMode)`.

| `bigIntegers` | Integer literals outside +/-(2^53-1) |
|---|---|
| `nil` or `"number"` | Numbers, rounded. The old behaviour |
| `"int64"` | int64, or uint64 above `int64.max`. A literal that fits neither is an error |
| `"string"` | Strings of decimal digits; does not need the int64 module |

In every mode, integers inside the safe range stay numbers, so ordinary JSON
reads as before, and literals with a fraction or an exponent stay numbers
(they are JSON floats). Because of that, a field can be a number in one
document and an int64 in another; `int64(v)` accepts both and gives an int64.

`json.serialize` writes int64 and uint64 values as exact integer literals, so
`json.parse(json.serialize(t), false, "int64")` returns the same values.

`JsonTableConverter.ObjectToJson` on a CLR object still turns `long` members
into doubles.

## Cost

Each operation calls a CLR metamethod and allocates a new value (the wrapper,
its `UserData` and a `DynValue`). Rough figures on a 4-core 2.1 GHz Xeon VM,
.NET 8, per loop iteration minus an empty loop (about 114 ns), from runs with
about 20% noise:

| Operation | Cost |
|---|---:|
| number + number | 25-60 ns |
| int64 + int64, int64 + number | 160-200 ns |
| int64 < int64, int64 < number, int64 == int64 | 170-190 ns |
| `int64.band(a, b)` | about 290 ns |

The default path (no opt-in) adds one null check and one boolean read when a
CLR number is converted to Lua, and a type check when a userdata is hashed or
passed to a CLR parameter it does not match directly. A/B runs of the interpreter benchmarks against the
previous build showed no difference beyond the noise.

## Limitations

- No literal syntax: `9007199254740993` in source is a rounded number. Write
  `int64("9007199254740993")`.
- `math.*`, numeric `for` loops and `string.format("%d")` take numbers only.
- Only decimal strings are parsed; there is no hex input. There is no
  bit-reinterpreting conversion between int64 and uint64; the conversion
  checks the range.
- `>` and `>=` against NaN follow MoonSharp's compilation (see above), not
  IEEE rules.
- No string coercion in arithmetic: `int64(1) + "1"` is an error.
