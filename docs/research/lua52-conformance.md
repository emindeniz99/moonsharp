# Lua 5.2 conformance survey

Date: 2026-10-29. Tree: `next` `03a5536`, read-only (no interpreter change). Branch
`docs/lua52-conformance`. Evidence marks as in [README.md](README.md): [C] confirmed by running it or by
reading the code, [S] secondary, [U] unconfirmed. File:line references point at `03a5536`.

Goal: a measured list of where the fork still differs from reference Lua 5.2, so the next round of fixes
starts from evidence. Two agents are changing the call path and `Table` at the same time, so every
difference says whether its fix touches either.

## Short answer

- The official Lua 5.2 test suite: 22 of its 26 test files run on the fork (the other 4 need the C
  test library or the standalone `lua` binary). With 50 workarounds for points where a file stops, 9
  files pass every assertion they reach and 13 fail 69 assertion lines (each line counted once) [C].
- 5,710 differential cases (14 files) against `lua5.2` 5.2.4: 3,620 identical, 1,176 different values,
  142 message-only, 451 errors where Lua 5.2 returns, 203 results where Lua 5.2 raises an error, and
  118 cases that end the host process with a CLR exception `pcall` cannot catch [C]. `string.format` is
  3,989 of those cases; without it: 1,376 identical, 229 values, 60 message-only, 32 + 15 error
  mismatches, 9 crashes.
- The top three by how often real scripts would meet them are compiler bugs that give wrong values
  silently: `x and f()` / `x or f()` keep all of `f`'s results, `a * -x^2` computes `a * (-x)^2`, and
  `i, t[i] = i + 1, v` indexes `t` with the new `i` (list below).
- Ten library calls can end the host process from a script: `table.unpack({}, 2)`,
  `string.format("%.5s", "ab")`, `string.format("%c", -1)`, `pcall()`, `pcall(coroutine.wrap(f))` when
  `f` fails, `dofile`/`loadfile`/`io.input` of a missing file, `file:seek` before the start and an
  exhausted `io.lines` iterator [C].

## After `fix/lua52-batch1`

Measured 2026-10-29 on branch `fix/lua52-batch1` with the same runner, cases and workarounds;
`observed/` now holds this output, the tables further down stay as measured at `03a5536` [C].

| Differential cases (the 14 files) | `03a5536` | `fix/lua52-batch1` |
|---|---:|---:|
| Same as Lua 5.2 | 3,620 | 5,338 |
| Different values | 1,176 | 272 |
| Message only | 142 | 62 |
| Errors where Lua 5.2 returns | 451 | 20 |
| Results where Lua 5.2 raises | 203 | 15 |
| End the host process | 118 | 3 |

- `string.format` 2,244 to 3,908 of 3,989 (the 81 left are `%s` and `%q` of numbers, whose text is
  MoonSharp's `tostring`); `bit32` 4 to 15 of 17 (2 message names); `load` 12 to 21 of 31; `syntax`
  27 to 41 of 63.
- The 3 crashes left are `table.unpack` with `i > j + 1` (entry 15, `TableModule`, the Table work).
  The 20 errors are `math.log10` (14, owner question with `loadstring`), `table.remove` and
  `table.insert` (5, Table work) and `tostring` of a `__tostring` returning a table (1). The 15
  accepts are `string.char` out of range (11) and `table.insert`/`table.sort` (4).
- A new file, `cases/hostcrash.lua`, holds 24 calls a library fuzzer (`fuzz/` in
  [src/DevTools/Research/Lua52](../../src/DevTools/Research/Lua52/README.md)) found ending the host; on `next`
  13 of them crashed and one hung. Now 20 match Lua 5.2 and 4 differ by design (`string.rep` past a
  CLR string and `os.date` past `DateTime` raise or return nil where Lua 5.2 on Linux returns a value;
  `io.open`'s message names `open`, not `io.open`).
- `Lua52DifferentialGuardTests` runs every case in the unit runners and fails on any that ends the
  host, with the three `table.unpack` cases listed until they are fixed.
- Official suite, distinct failing assertion lines with the same workarounds (lua5.2 fails only
  `errors.lua` 51 and 52, not counted): 69 to 38. `bitwise`, `constructs`, `events` and `math` now pass
  every assertion they reach, `strings` fails only lines 82 and 84 (Latin-1 source bytes, entry 28).
  `files.lua` no longer ends the host at the exhausted `io.lines` iterator; it now stops at line 97,
  whose failure (the `os.rename` of line 88 failed first) the harness writes to a default output the
  test has closed. With failures written to `io.stderr` it runs to line 272, failing on Latin-1 bytes
  and on `seek` positions, which count the bytes the reader buffered.

## How it was measured

The runner, the fuzzer and the `string.format` and `bit32` diff scripts are in [src/DevTools/Research/Lua52](../../src/DevTools/Research/Lua52/README.md), with the
commands.

Reference: `lua5.2` from Debian (`Lua 5.2.4  Copyright (C) 1994-2015 Lua.org, PUC-Rio`), built with
`LUA_COMPAT_ALL` as the 5.2 Makefile does by default, so `loadstring`, `unpack`, `module`, `table.maxn`,
`math.log10` and `package.seeall` exist there [C]. Fork: a 40-line console program referencing
`src/MoonSharp.Interpreter` (Release, .NET 8.0.32 runtime) that runs a file with
`CoreModules.Preset_Complete`, `FileSystemScriptLoader` and an `arg` table, and prints
`DecoratedMessage` on an `InterpreterException`. Its source:

```csharp
Script.DefaultOptions.ScriptLoader = new FileSystemScriptLoader();
var s = new Script(CoreModules.Preset_Complete);
var argt = new Table(s); argt[0] = args[0];
for (int i = 1; i < args.Length; i++) argt[i] = args[i];
s.Globals["arg"] = argt;
try { s.DoFile(args[0]); return 0; }
catch (InterpreterException ex) { Console.Error.WriteLine("MSERR " + (ex.DecoratedMessage ?? ex.Message)); return 1; }
catch (Exception ex) { Console.Error.WriteLine("MSCRASH " + ex); return 2; }
```

Every run had a timeout (60-300 s, `timeout -k 5`), and no process was left running.

### The official suite

`lua-5.2.2-tests.tar.gz` (the suite for 5.2.2 to 5.2.4) from <https://www.lua.org/tests/>, checked
against the SHA-256 the page publishes (`07c1071c...4eb6`), unpacked into its own directory and read
only; its scripts ran only under the two interpreters. The copy in Lua-CSharp's repository, which the
register VM spike used for its 47% coverage figure, is edited (9 files differ, `checktable.lua`, `libs/`
and `ltests/` are missing), so this survey used the original.

[`src/MoonSharp.Interpreter.Tests/Lua52Differential/suite/soften.py`](../../src/MoonSharp.Interpreter.Tests/Lua52Differential/suite/soften.py)
rewrites each `assert(` into a call that prints `ASSERTFAIL <line>` and goes on, sets the suite's
portability flags as its own `_U` mode does (`_soft`, `_port`, `_no32`, `_noposix`, `_nolonglong`,
`_noformatA` true), and counts the assertions executed. When the fork stopped at an error, the
statement was blanked or replaced in
[`suite/workarounds.txt`](../../src/MoonSharp.Interpreter.Tests/Lua52Differential/suite/workarounds.txt)
(50 entries, each with its reason) and the file run again, until it reached its end. Line numbers stay
those of the original files. The same rewritten files run on `lua5.2`, which fails only 6 assertion
executions, on 4 lines whose results depend on `assert` being a C function (`db.lua` 243, 282;
`errors.lua` 51, 52); those lines are not counted for the fork either.

### Differential cases

[`src/MoonSharp.Interpreter.Tests/Lua52Differential/`](../../src/MoonSharp.Interpreter.Tests/Lua52Differential/)
holds 14 case files (`cases/`, 6 of them generated by `gen.py` as cross products), the runner `run.lua`,
the `lua5.2` output (`expected/`) and the fork's output on `03a5536` (`observed/`). `run.lua` runs each
case as its own chunk named `=c` and writes one line per case without `tostring` or `string.format`
(integers in decimal, other numbers as mantissa and power of two, strings byte by byte), so number
formatting cannot hide or invent a difference; error positions are stripped. `run_fork.sh` restarts the
runner after a case that ends the process and records it as `CRASH` or `HANG`; `compare.py` classifies
each case. Both output sets are byte-identical across repeated runs (12 for `lua5.2`; cases whose
message names a base function are compared by status only, because 5.2 names it `rawlen` or
`_G.rawlen` depending on hash order). Nothing is wired into the
test runners yet.

```sh
cd src/MoonSharp.Interpreter.Tests/Lua52Differential
python3 -I gen.py
lua5.2 run.lua cases/tables.lua > expected/tables.txt
./run_fork.sh tables <command that runs a Lua file with MoonSharp>
python3 -I compare.py expected/tables.txt observed/tables.txt --show
```

## Results: official suite

Assertions are those executed after the workarounds; "lines" counts distinct failing `assert` lines.

| File | Asserts run (5.2 / fork) | Failing lines | Workarounds | Main causes |
|---|---:|---:|---:|---|
| api, code, checktable | - | - | - | need the C test library `T`; not runnable on a managed engine (by design) |
| main | - | - | - | tests the standalone `lua` program (by design) |
| big | - | - | - | returns at once under `_soft`; run with `_soft = false` it passes up to two message checks and the `coroutine.yield` it expects `all.lua` to catch |
| locals, verybig, vararg, sort | same | 0 | 1 (sort) | pass; `sort.lua` needs the `table.unpack` crash avoided |
| attrib | 6,040 / 6,040 | 0 | 1 | multiple assignment order |
| bitwise | 1,160 / 1,160 | 10 (137 executions) | 4 | `bit32` shifts, `arshift`, `extract`/`replace`, no-argument calls |
| calls | 60 / 60 | 0 | 8 | `load` reader and mode, `string.dump` with upvalues, `table.remove` |
| closure | 48 / 48 | 12 | 2 | weak tables (hang), `goto` to a block end, `debug.upvalueid`/`upvaluejoin` |
| constructs | 2,001 / 2,001 | 3 | 2 | unary minus with `^`, `and`/`or` truncation, `math.fmod`, `debug.getinfo` |
| coroutine | same output | 3 | 1 | `error` with non-string values, weak tables |
| db | 7 / 7 | 0 | 4 (596 lines) | `debug.getinfo`, `getlocal`, `sethook` absent; `_ENV` is always upvalue 1 |
| errors | 34 / 34 | 8 | 8 replaced helpers | messages (positions, names), non-string errors, syntax accepted, limits |
| events | 233 / 233 | 1 | 0 | `__eq` across metatables |
| files | 827 / stops | 9 | 3 | CLR exceptions from `io`, invalid modes accepted, no `errno` |
| gc | 5,178 / stops | 1 | 1 | `collectgarbage` returns nothing; GC tests assume Lua's collector |
| goto | 20 / 20 | 0 | 1 | `goto` to a block end |
| literals | 812 / 825 | 4 | 1 + 5 replaced | bytes >= 128 in names, `\r` line ends, `os.setlocale` |
| math | 171 / 171 | 2 | 0 | unary minus with `^` |
| nextvar | 9,554 / 9,535 | 4 | 1 + 3 replaced | `table.remove`, `package.loaded`, `ipairs` identity |
| pm | 180 / 180 | 1 | 0 | `gsub` with a table ignores `__index` (already in ROADMAP) |
| strings | 171 / 171 | 11 | 4 replaced | `%s` of non-strings, `format` range checks, `string.rep`, source bytes |

Root causes behind the suite failures and workarounds, by category [C]:

| Category | Count | Causes |
|---|---:|---|
| Wrong result | 17 | `bit32` (shifts, `arshift` sign, `extract`/`replace` width 32, `replace` position, no-argument calls), multiple assignment order, unary minus with `^`, `and`/`or` truncation, `math.fmod`, `goto` to a block end, `__eq` across metatables, `error` with non-strings, `table.remove`, `load` reader `""` and reader errors, `string.rep("", n, sep)`, `package.loaded`, `return;;` and `(..., ...)` accepted |
| Missing feature | 9 | `debug.getinfo`/`getlocal`/`setlocal`/`sethook`/`gethook`, `debug.upvalueid`/`upvaluejoin`, `load` mode, `load` env other than a table, `string.dump` with upvalues, `collectgarbage` options, `%a`, `io` `errno` results, `os.setlocale` |
| Crash (CLR exception) | 3 | `table.unpack` with `i > j + 1`; `dofile`/`loadfile`/`io.input` of a missing file; `seek` and exhausted `io.lines` |
| Hang | 1 | waiting for a weak table entry to be collected (`closure.lua:26`) |
| By design / implementation-defined | 7 | weak tables and `__gc`, Latin-1 source bytes, closure caching, `ipairs` iterator identity, `_ENV` upvalue index, `_VERSION`, the C API tests |
| Message text only | 6 | position format, chunk names, variable names, function names in `bad argument`, "CLR-call boundary", lexer token text |

## Results: differential cases

| File | Cases | Same | Value | Message | Fork errors | Fork accepts | Crash |
|---|---:|---:|---:|---:|---:|---:|---:|
| bit32 | 17 | 4 | 5 | 2 | 6 | 0 | 0 |
| coroutines | 25 | 15 | 1 | 6 | 0 | 0 | 3 |
| errors | 56 | 22 | 8 | 25 | 0 | 0 | 1 |
| format | 3,989 | 2,244 | 947 | 82 | 419 | 188 | 109 |
| load | 31 | 12 | 12 | 2 | 3 | 0 | 2 |
| math | 508 | 463 | 26 | 5 | 14 | 0 | 0 |
| metamethods | 49 | 41 | 7 | 1 | 0 | 0 | 0 |
| misc | 38 | 23 | 14 | 1 | 0 | 0 | 0 |
| select | 21 | 21 | 0 | 0 | 0 | 0 | 0 |
| strings | 418 | 349 | 57 | 1 | 0 | 11 | 0 |
| syntax | 63 | 27 | 19 | 14 | 3 | 0 | 0 |
| tables | 94 | 60 | 19 | 3 | 5 | 4 | 3 |
| tonumber | 268 | 268 | 0 | 0 | 0 | 0 | 0 |
| tostring | 133 | 71 | 61 | 0 | 1 | 0 | 0 |
| **Total** | **5,710** | **3,620** | **1,176** | **142** | **451** | **203** | **118** |

No case hung. `select` (negative indices included) and `tonumber` (bases, hex, exponents, whitespace,
`inf`/`nan`, overflow) match exactly; the `tonumber` rewrite in MIGRATING.md holds [C]. Most `tostring`
and `format` value differences are the known number formatting (`%.14g`, `inf`/`nan`, three-digit
exponents; ROADMAP "Found by the conformance corpus"). `format` also has 354 `%a`/`%A` errors (not
implemented) and 109 crashes (73 `%c`, 20 `%.Ns`, 16 `%c` with flags).

## Differences found

Each entry: repro, Lua 5.2, fork, likely cause, fix size (S under a day, M a few days, L more), whether
the fix touches the call path or `Table` internals, and whether it is a compatibility question for the
owner. "Plain bug" means Lua 5.2 behaviour is the only sensible one; it still goes into MIGRATING.md.

### Compiler

1. **`and`/`or` keep every result of a call on their right** [C].
   `local function f() return 1, 2, 3 end; print(nil or f())` prints `1` on 5.2, `1 2 3` on the fork;
   `local a, b = true and f()` gives `1 nil` / `1 2`; `table.insert(t, x or f())` passes extra arguments.
   Cause: the right operand is compiled as a plain call and nothing adjusts it to one value after the
   `JtOrPop`/`JfOrPop` jump (`Tree/Expressions/BinaryOperatorExpression.cs:331-338`) [S for the exact fix
   point]. S. Compiler, VM-core agent's area; not `Table`. Plain bug.
   **Fixed** on `fix/lua52-batch1`: an operand that is a call or `...` is cut to one value.
2. **Unary operators bind looser than `^` only at the start of an expression** [C].
   `local x = 3; return 1 + -x^2, 2 * -x^2, true and -2^2, 2 - -2^2` gives `-8 -18 -4 6` on 5.2 and
   `10 18 4 -2` on the fork (`-x^2` alone is right); `not 2^2` after a binary operator becomes `(not 2)^2` and fails.
   Cause: the power chain after a unary operator is built only when `isPrimary`
   (`Tree/Expression_.cs:120`, `:125`); an operand of a binary chain is parsed with `isPrimary = false`,
   so `-x` is closed before `^` joins the chain. This is also what fails `constructs.lua:15` and
   `math.lua:77,126`. S. Parser only. Plain bug.
   **Fixed** on `fix/lua52-batch1`: the power chain follows every unary operand.
3. **Multiple assignment evaluates table and key after earlier targets are stored** [C].
   `local a, i = {}, 1; i, a[i] = i + 1, 20` stores `a[1]` on 5.2 and `a[2]` on the fork;
   `t, t.x = {}, 1` writes into the new table. Cause: `AssignmentStatement.Compile` calls
   `CompileAssignment` per target (`Tree/Statements/AssignmentStatement.cs:124-127`), and
   `IndexExpression.CompileAssignment` compiles its base and key right before its `IndexSet`
   (`Tree/Expressions/IndexExpression.cs:57-74`). Lua evaluates all prefixes and keys first. M (stack
   offsets and the `__newindex` scratch slot change). Compiler, VM-core area. Plain bug; the manual
   leaves the order undefined, but reference Lua and its suite (`attrib.lua:382-403`) rely on it.
   **Fixed** on `fix/lua52-batch1`: tables and keys of every target are evaluated first, then the values,
   and the stores run from the last target to the first, as in reference Lua.
4. **`goto` to a label at the end of a block after a `local`** [C].
   `do goto e; local x = 1; ::e:: end` compiles on 5.2 ("this restriction does not apply to labels at the
   end of blocks") and raises "jumps into the scope of local 'x'" on the fork. Cause:
   `Execution/Scopes/BuildTimeScopeBlock.cs:130` compares defined-variable counts without the end-of-block
   exception. S. Compiler. Plain bug (scripts that compile on 5.2 are rejected).
   **Fixed** on `fix/lua52-batch1`: a label followed only by void statements up to the end of its block
   counts no locals of the block (not before `until`).
5. **Syntax the fork accepts** [C]: `return;;`, `function f(..., ...) end`, `\u{48}` escapes, a leading
   `#!` line in `load`, bytes >= 128 in names, more than 200 locals or 255 upvalues. S each. Low
   priority; the `\u{}` escape and wider limits are compatibility questions (scripts may use them).
6. **The lexer reads `1e15+0.5` as one malformed number** (already in ROADMAP) and does not count a lone
   `\r` or `\n\r` as one line end; `"a\<CR>b"` fails to load [C]. S. Lexer.

### Base library and errors

7. **`error` accepts only strings** (already in ROADMAP) [C]. `error({code = 7})` raises "bad argument #1
   to 'error' (string expected, got table)", `error()` and `error(nil)` likewise; `pcall` never returns
   the object, `xpcall` handlers get a string. Cause: `CoreLib/BasicModule.cs:88`
   (`AsType(0, "error", DataType.String)`); the exception carries only a message. M-L: the exception
   needs a `DynValue` payload that `pcall`, `xpcall`, `coroutine.resume`/`wrap` and hosts pass on.
   Call path. Compatibility question: hosts read `ScriptRuntimeException.Message`.
8. **`pcall()` with no arguments ends the process** with `OverflowException` [C]:
   `new DynValue[args.Count - firstArg]` with a count of 0 (`CoreLib/ErrorHandlingModule.cs:40`). Lua
   5.2: "bad argument #1 to 'pcall' (value expected)". S. Call path (`pcall`). Plain bug.
   **Fixed** on `fix/lua52-batch1`: "bad argument #1 to 'pcall' (value expected)".
9. **`pcall(coroutine.wrap(f))` ends the process when `f` fails** (`NullReferenceException`) [C]. Also
   for a dead wrapped coroutine. `pcall(function() return w() end)` works. Cause: `__wrap_wrapper` reads
   `executionContext.AdditionalData` (`CoreLib/CoroutineModule.cs:39`), which is not set when `pcall`
   invokes the callback directly (`ErrorHandlingModule.cs:49`). S. Call path. Plain bug.
   **Fixed** on `fix/lua52-batch1` in `CoroutineModule` (the wrapper keeps its coroutine in its delegate);
   `pcall` still runs a C# function with its own context.
10. **`collectgarbage` returns nothing for every option** [C]. `collectgarbage("count")` is `nil` (5.2:
    KB as a number plus a remainder), `"step"`, `"isrunning"`, `"setpause"` are `nil`, an invalid option
    is accepted; `collectgarbage()` and `"collect"` run `GC.Collect(2, GCCollectionMode.Forced)`
    (`CoreLib/BasicModule.cs:59-75`). Scripts that log memory get `nil` or "attempt to perform
    arithmetic on a nil value". S (`GC.GetTotalMemory`). Neither path. Compatibility question: whether
    `collectgarbage()` should keep forcing a blocking full CLR collection, which a script calling it
    every frame turns into a stall.
    **Fixed** on `fix/stdlib-compat` (owner decision 2026-10-29): Lua 5.2's eleven options, results and
    argument errors. `"count"` is the process's managed heap (`GC.GetTotalMemory(false)`) as KB plus the
    remainder in bytes; `"collect"` asks for an optimized, non-blocking collection and returns 0; `"step"`
    and `"isrunning"` return `true`, the tuning options 200 and the others 0, without effect (see
    [MIGRATING.md](../MIGRATING.md)). All 24 `collectgarbage` differential cases match `lua5.2`; none of
    them observes the no-op differences (`"step"` true before a cycle ends, `"isrunning"` true after
    `"stop"`, the tuning options always 200). `gc.lua` passes line 5 (`isrunning`) and runs past its
    first `gcinfo` call (line 143, `collectgarbage"count" * 1024`), where it stopped, to line 232 (weak
    tables, 37); it fails lines 154, 156 and 181 (`isrunning` after `"stop"`) and 164 (`step` does not
    collect) by design, and its workaround for lines 138-139 is gone [C].
11. **`xpcall` handler errors** [C]: a `nil` handler or a handler that fails gives the original message
    (or "b\n...: a") instead of "error in error handling". S. Call path.
12. **`print` returns one value, `tostring` of a `__tostring` returning `nil` returns none, `__unm` gets
    one operand, `rawequal(1)` returns false instead of raising** [C]. S each. Low.
13. **Error messages** (message-only, 142 cases plus `errors.lua`) [C]:
    - positions are `chunk:(line,col-col):` instead of `chunk:line:`, so scripts that strip them with
      `gsub("^.-:%d+: ", "")` keep them;
    - chunk names keep their `=` or `@` (`=mychunk:(1,5-10): x`; 5.2 `mychunk:1: x`), and `load` of a
      string defaults to `=(load)` instead of `[string "..."]`;
    - no variable names ("attempt to call a nil value" for 5.2's "global 'undefined_fn'"; already in
      ROADMAP);
    - `bad argument` names `rep` where 5.2 says `string.rep`, and counts a method's `self` differently;
    - errors from `coroutine.resume` lose their position; `f()()` on a multi-value result reports
      "attempt to call a internal<tuple> value".
    The position format is MoonSharp's own; changing it is a compatibility question (hosts and tests
    parse it). Names and the `=` prefix are S each.

### Tables and iteration

14. **`table.remove(t, #t + 1)` raises "position out of bounds"** [C]; so does `table.remove({}, 1)`, the
    queue idiom `local x = table.remove(q, 1)` on an empty queue. Lua 5.2 allows `1 <= pos <= #t + 1`
    and returns `t[pos]`. Cause: `pos >= len + 1` (`CoreLib/TableModule.cs:208`). Extra arguments raise
    "wrong number of arguments" (`:192`); 5.2 ignores them. S. `TableModule`, the Table agent's file.
    Plain bug. **Fixed on `perf/table-layout`** (tremove's rules).
15. **`table.unpack(t, i, j)` with `i > j + 1` ends the process** with `OverflowException` [C]:
    `table.unpack({}, 2)`, `unpack(args, 2)` on an empty list. Cause: `new DynValue[ij - ii + 1]`
    (`CoreLib/TableModule.cs:28`). There is no "too many results to unpack" limit either:
    `unpack({}, 1, 1e7)` returns ten million `nil`s and `unpack({}, 1, 2^40)` overflows the `int` cast.
    S. `TableModule` (Table agent). Plain bug. (The VM agent's unpack work is on another branch; check
    whether it covers this.) **Fixed on `perf/table-layout`**: no values for `i > j`, "too many results
    to unpack" from a million results, positions read as `luaL_optint` does.
16. **`table.insert(t, pos, nil)` appends the position** [C]: `local t = {1, 2}; table.insert(t, 1, nil)`
    leaves `{nil, 1, 2}` on 5.2 and `{1, 2, 1}` on the fork, because a `nil` value is taken for the
    two-argument form (`CoreLib/TableModule.cs:160-164`). `table.insert(t)` is accepted; `insert` and
    `sort` return the table instead of nothing; a string position is rejected. S. Table agent's file.
    Plain bug. **Fixed on `perf/table-layout`** except the return values and string positions: the
    argument count picks the form, and `table.insert(t)` is an error.
17. **`table.sort` calls the comparator twice per comparison** (`lt(a, b)` and `lt(b, a)`,
    `CoreLib/TableModule.cs:118-127`), raises "invalid order function" where 5.2 sorts, and sorts a
    table with a `nil` hole where 5.2 raises [C]. M (a port of 5.2's `auxsort` also fixes the unstable
    order that differs from 5.2). Table agent's file. Compatibility question: tie order changes.
18. **`#t` with holes** (document only; any border is valid) [C]: `#{nil, 2, 3}` is 3 on 5.2 and 0 on
    the fork, `#{1, nil, 3}` 3 / 1, `local t = {...}` with a `nil` argument 3 / 1, `t[2] = nil` in a
    4-element array 4 / 1. Lua 5.2 returns the array part's size when its last slot is set; the fork
    returns the first border. Scripts that build `{...}` and loop to `#t` see fewer elements. Table
    internals. Compatibility question.
    Checked for `fix/lua52-batch1`: Lua 5.2 3.4.6 defines the length only for a sequence ("the length of a
    table t is only defined if the table is a sequence"), so both results are allowed; nothing changed.
    Scripts that need the count of `...` with nils should use `select('#', ...)` or `table.pack(...).n`.
19. **`next` returns two values at the end, `pairs` returns two, `__pairs` results are not cut to
    three** (already in ROADMAP) [C]. `pairs(5)` and `ipairs(nil)` are accepted and the loop fails at its
    first step, naming `next` or the internal `!!next_i!!` instead of `pairs`/`ipairs`
    (`CoreLib/TableIteratorsModule.cs:20-43`). S. Iterators; not `Table` internals.
20. **`package.loaded` has no `_G` and no `package` entry**; `package.cpath`, `searchpath`, `loadlib`
    are absent [C]. S. Neither path.
    **Fixed** on `fix/lua52-batch1`: `loaded._G` and `loaded.package`, `cpath = ""`, `loadlib` answers as
    a build without C libraries, `searchpath` searches through the script loader.

### Math and `bit32`

21. **`math.fmod` uses `Math.IEEERemainder`** [C]: `math.fmod(7, 4)` is -1 (5.2: 3), `fmod(5.5, 2)`
    -0.5 (1.5), `fmod(2^53, 3)` -1 (2). Any quotient whose fraction is .5 or more gives a wrong result
    (`CoreLib/MathModule.cs:151`). C# `%` on doubles is C's `fmod`. S. Plain bug.
    **Fixed** on `fix/lua52-batch1` (C#'s `%`).
22. **`math.modf` floors instead of truncating** [C]: `math.modf(-3.7)` is `-4, 0.3` (5.2: `-3, -0.7`);
    `modf(1/0)` returns `nan` for the fraction (5.2: 0) (`CoreLib/MathModule.cs:241`). S. Plain bug.
    **Fixed** on `fix/lua52-batch1`.
23. **`math.huge` is `Double.MaxValue`** (already in ROADMAP) [C]; `math.log10` is absent (5.2 compat
    build has it); `math.frexp` of `inf`/`nan`/`-0`, `math.ldexp` with a fractional exponent,
    `math.max(1, 0/0, 3)`, `math.random(0)` and `math.random(2, 1)` differ [C]. S each.
    `math.log10` is **added** on `fix/stdlib-compat` (with 29); its 16 differential cases match [C].
24. **`bit32` differs in eight ways** [C] (`CoreLib/Bit32Module.cs`):
    - `band()`, `bor()`, `bxor()`, `btest()` with no arguments raise (5.2: identities) (`:51`);
    - shifts by 32 or more wrap modulo 32 (`lshift(1, 33)` is 2, 5.2: 0) (`:128-171`);
    - `arshift` returns negative numbers (`arshift(-1, 1)` is -1, 5.2: 4294967295) (`:124-135`);
    - `extract`/`replace` reject `pos + width == 32` (`extract(x, 28, 4)`, `extract(x, 31)`) (`:109`);
    - `replace` does not shift the value into place: `replace(0, 1, 2)` is 0 (5.2: 4) (`:98-101`);
    - fractions are truncated (`band(1.5)` 1, 5.2: 2);
    - names in messages are `band` instead of `bit32.band`;
    - negative arguments go through `(uint)double` (`:24-29`), which saturates to 0 on .NET 9 and later:
      on the .NET 10.0.13 runtime `bit32.band(-1)`, `bor(-2)` and `lshift(-1, 1)` are 0 and
      `string.format("%x", -1)` is "0", against 4294967295, 4294967294, 4294967294 and an error on 5.2
      and the .NET 8 results that match 5.2 [C]. ARM64 converts the same way in hardware, so IL2CPP
      players on phones are likely affected too [S, not run].
    S-M for the module (a port of `lbitlib.c`'s `luaL_checkunsigned` rounding). Neither path. Plain bugs;
    the runtime dependence makes the last one the most urgent.
    **Fixed** on `fix/lua52-batch1` (a port of `lbitlib.c`, with an exact modulo instead of a cast); the
    names in messages remain.

### Strings and `string.format`

25. **`string.format` ends the process** [C]: `%.Ns` with a string shorter than N
    (`string.format("[%.10s]", "ab")`, `ArgumentOutOfRangeException`, kept on purpose from the old port:
    `CoreLib/StringLib/LuaFormatter.cs:473`), and `%c` outside 0..65535 (`Convert.ToChar`, `:191`;
    `%c` of 0 also returns an empty string). 5.2 prints `[ab]` and the low byte. S. String library, the
    string agent's files. Plain bugs.
    **Fixed** on `fix/lua52-batch1`, with the other crashes of the formatter found by a fuzzer (zero
    padding to width 0 or 1, items over 512 characters).
26. **`string.format` accepts what 5.2 rejects and ignores some flags** [C]: `%d`/`%x`/`%u`/`%o` of
    out-of-range or negative values wrap (already in ROADMAP for `%d`); `%.3d` ignores the precision
    (`7` for `007`); `%s` rejects `nil`, booleans and tables (already in ROADMAP); `%a`/`%A` are not
    implemented; `%q` writes `\r` where 5.2 writes `\13`, and numbers in `%s`/`%q` follow the fork's
    `tostring`. S-M. String agent's files. The number text is the known `tostring` question for the
    owner.
    **Fixed** on `fix/lua52-batch1` (a formatter that follows Lua 5.2.4 and glibc, with exact float
    digits), except the number text of `%s`/`%q` (owner question) and `%c` of 256 to 65535, kept as a
    UTF-16 character.
27. **`string.rep("", n, sep)` returns `""`** (5.2: `n - 1` separators); string indices of 2^31 or
    more wrap through an `int` cast (`string.sub("abc", 2^53)` is "abc", 5.2: ""); `string.char(256)`
    and `string.char(-1)` return "?" instead of raising; `string.sub(s, nil)` is accepted [C]. S each.
    **Partly fixed** on `fix/lua52-batch1`: `string.rep` keeps the separators and raises "resulting string
    too large" past a CLR string; the indices of 2^31 or more, `string.char(256)` and `string.sub(s, nil)`
    remain.
28. **Source files are decoded as UTF-8**: a Latin-1 byte in a string literal becomes `?` (63), so
    `strings.lua:82,84` fail [C]. By design (strings are UTF-16 text); worth a line in the docs.

### `load`, environments and the debug library

29. **`loadstring` is absent** [C]. Lua 5.2 as built by its Makefile (`LUA_COMPAT_ALL`) has it, and
    5.1-era scripts use it everywhere. `module` and `table.maxn` are absent too. S (an alias). Neither
    path. Compatibility question: which presets get the 5.1 names.
    **Fixed** on `fix/stdlib-compat` (owner decision 2026-10-29): `loadstring` (with `load`, in
    `CoreModules.LoadMethods`), `table.maxn` and `math.log10` exist; `module` and `package.seeall` do
    not. `loadstring` is `load` under its own name, so `loadstring == load` is false (5.2: true, the same
    C function); its messages name `loadstring`, as 5.2's do when it is called by that name. Of the
    differential cases, the 10 `maxn` and 16 `math.log10` ones match `lua5.2`, and 9 of the 12
    `loadstring` ones: the others are that comparison and two chunk names in syntax errors (13) [C].
30. **`load` with a reader function** [C]: a reader that returns `""` (the documented end marker) gives
    "reader function must return a string" (`CoreLib/LoadModule.cs:125-130`); an error raised inside the
    reader propagates instead of `nil, msg`; pieces are joined with `+=` (quadratic). The `mode` argument
    is ignored (text loads under `"b"`, binary under `"t"`), `env` must be a table (5.2: any value, and an
    explicit `nil`), and a number chunk raises. S. Neither path. Plain bugs.
    **Fixed** on `fix/lua52-batch1` for the reader, `mode`, `env` and number chunks; the default chunk
    name and error positions remain (13).
31. **`dofile`, `loadfile` and `io.input` of a missing file end the process** with
    `FileNotFoundException` instead of returning or raising a Lua error [C] (measured with
    `FileSystemScriptLoader`; the CLI's `ReplInterpreterScriptLoader` extends it [S]). `io.open` handles it.
    `seek` before the file start (`IOException`) and calling an exhausted `io.lines` iterator
    (`NotSupportedException`; `io.lines` returns a userdata, not a function) end it too; invalid
    `io.open` modes are accepted; `io.open` failures have no `errno` value [C]. S-M. Neither path. Plain
    bugs. The common cause is that a CLR exception from a library callback is not turned into a script
    error, so `pcall` cannot catch it; one conversion point in the callback invocation would fix the
    whole class, and that is call path.
    **Fixed** on `fix/lua52-batch1` with the fixed library crash points: missing files, `seek`, `io.lines`
    (now a function that reads as it goes), reads and writes on the wrong mode (EBADF) and `io.open`'s
    modes; the `errno` value of `io.open` failures remains.
32. **`debug.getinfo`, `getlocal`, `setlocal`, `sethook`, `gethook` are absent** (`CoreLib/DebugModule.cs:247`,
    `:297` are commented out) [C]. They block most of `db.lua` (596 lines) and every use of
    `debug.getinfo(1).currentline`. `debug.upvalueid` returns different ids for shared upvalues and
    `upvaluejoin` does not join (`closure.lua:219-238`); every closure has `_ENV` as upvalue 1, so
    upvalue indices are shifted. M-L (`getinfo` needs the call stack's function, line and names).
    Call path for `getinfo`/`sethook`. Missing feature.
33. **`string.dump` refuses functions with upvalues other than `_ENV`** (`Script.cs:267`) [C].
    5.2 dumps them with fresh upvalues. M. Missing feature.
34. **`os.setlocale` returns `"n/a"` for any locale** and `os.date` accepts unknown conversions such as
    `%Ez` [C]. S. Low.

### Metamethods

35. **`__eq` requires the same metatable** [C]: two tables with different metatables whose `__eq` is
    the same function are equal on 5.2 and not on the fork (class hierarchies that copy `__eq` into
    each class metatable). Also a userdata compared with a value of another type calls `__eq`; 5.2
    returns false. Cause: `Execution/VM/Processor/Processor_InstructionLoop.cs:1696-1718`. S. VM core
    (VM agent's file). Plain bug.
    **Fixed** on `fix/lua52-batch1` for tables; a userdata compared with another type still calls `__eq`
    (interop dispatches it to `Equals`).
36. `__le` falling back to `not __lt(b, a)`, `__lt` with mixed types, `__concat` with numbers on either
    side, `__len` on strings (not consulted), `__index`/`__newindex` chains, `__call`, `__metatable`
    and `__pairs`/`__ipairs` all match 5.2 [C].
37. **`__gc` and `__mode` are ignored** (expected; document only) [C]. Weak tables never lose entries,
    so a script that loops until a weak entry is collected hangs (`closure.lua:26`).

## Top 10 by how likely real scripts hit them

Ranked by how common the idiom is in application and game scripts, then by how quietly it fails.

| Rank | Difference | Entry | Fails how | Fix | Touches | Owner question |
|---:|---|---|---|---|---|---|
| 1 | `x or f()`, `c and f()` keep all results | 1 | silently wrong values or arguments | S | compiler (VM area) | no |
| 2 | `table.remove(q, 1)` on an empty queue, `remove(t, #t + 1)` raise | 14 | error | S | `TableModule` (Table agent) | no |
| 3 | `a * -x^2`, `== -2^31` parse as `(-x)^2` | 2 | silently wrong numbers | S | parser | no |
| 4 | `collectgarbage("count")` is `nil`; `collectgarbage()` forces a blocking GC | 10 | error, stalls | S | neither | yes (`collect`) |
| 5 | `loadstring` absent | 29 | error | S | neither | yes (presets) |
| 6 | `error({...})` and `error(nil)` rejected; `pcall` gets a string | 7 | wrong error object | M-L | call path | yes (host API) |
| 7 | `string.format("%.10s", short)` and `%c` out of range end the process | 25 | host crash | S | string library (string agent) | no |
| 8 | `table.unpack(t, i)` with `i > #t + 1` ends the process | 15 | host crash | S | `TableModule` (Table agent) | no |
| 9 | `math.fmod` wrong for most non-exact quotients; `math.modf` floors negatives | 21, 22 | silently wrong numbers | S | neither | no |
| 10 | `i, t[i] = i + 1, v` uses the new `i` | 3 | silently wrong table slot | M | compiler (VM area) | no |

On `fix/lua52-batch1`: ranks 1, 3, 7, 9 and 10 are fixed; 2, 6 and 8 wait for the Table and call path
work; 4 and 5 are owner questions. On `fix/stdlib-compat`: 4 and 5 are fixed as the owner decided.

Next in line: `#{...}` with `nil` arguments (18), `pcall(coroutine.wrap(f))` crash (9), `bit32` with
negative numbers on .NET 9+ and likely ARM64 (24), error message positions that scripts parse (13),
`goto continue` before a `local` at the end of a loop body (4), `__eq` across class metatables (35),
`dofile`/`loadfile` of a missing file (31), `debug.getinfo` (32).
Of these, 9, 24, 4, 35 and 31 are fixed on `fix/lua52-batch1`; 18 is a border both versions may return
(an owner question), and 13 and 32 remain.

Fixes that touch neither the call path nor `Table` and can start now (entry numbers):
`collectgarbage` (10), `loadstring` once the owner decides (29), `math.fmod`/`modf` (21, 22), `bit32`
(24), `load` (30), `package.loaded` (20), and the parser and `goto` fixes (2, 4) if the VM agent agrees,
since they sit in the compiler.

## What this survey does not cover

- The suite ran with the `_U` portability flags; `_port = false` needs the suite's `libs/` C modules
  and a writable `/tmp` layout, and was not run [C]. `strings.lua` with `_nolonglong = false` passes its
  large-number `format` checks and stops at `%a` [C].
- Only the .NET 8 runtime was used for the counts; .NET 10 was used for the `bit32` check alone. Mono,
  IL2CPP and WebAssembly were not run [U for their results].
- String patterns (covered by the pattern agent), tail calls and `unpack` in the VM (VM agent) and the
  CLR interop were not fuzzed here.
- `coroutine.lua`'s C API part, `api.lua` and `code.lua` test the C implementation and do not apply.
