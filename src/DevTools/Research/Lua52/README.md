# Lua 5.2 conformance tools

The runner and fuzzers behind [lua52-conformance.md](../../../../docs/research/lua52-conformance.md). The
differential cases, `run_fork.sh`, `compare.py` and the official-suite rewriter (`suite/soften.py`,
`suite/workarounds.txt`) live with the tests in
[src/MoonSharp.Interpreter.Tests/Lua52Differential](../../../MoonSharp.Interpreter.Tests/Lua52Differential).

| File | What it does |
|---|---|
| `msrun/` | The console runner the note shows: runs one Lua file with `CoreModules.Preset_Complete`, a file-system script loader and an `arg` table; exit 1 and `MSERR` on a Lua error, exit 2 and `MSCRASH` on any other exception |
| `fuzz/` | Library fuzzer: calls every library function and file method with combinations of odd arguments under `pcall` and prints each CLR exception that reaches the host (`CRASH` lines). `fuzz <name>` runs one function; `fuzz string.format` adds every flag, width, precision and conversion |
| `difall.sh` | Runs every differential case file with `msrun` and prints `compare.py`'s totals |
| `fmtdiff.lua` | One line per `string.format` case (conversions, flags, widths, precisions, values); run on `lua5.2` and on the fork and diff the outputs |
| `bitfuzz.lua` | Every `bit32` function on a grid of values, with and without a third argument; diff as above |

The projects reference this repository's interpreter and are standalone: no solution, test runner or
Dockerfile builds them.

## Inputs

- `lua5.2` from the distribution (`apt install lua5.2`; the note used Debian's 5.2.4).
- For the official suite: `lua-5.2.2-tests.tar.gz` from <https://www.lua.org/tests/>, checked against
  SHA-256 `07c1071c66d9cb4dc80200ed430fc21ebbbccf1f55148b87eb0b9522932e4eb6` and unpacked into its own
  folder. It is not committed.

## Build and run

From the repository root:

```sh
l=src/DevTools/Research/Lua52
mise exec -- dotnet build -c Release $l/msrun -o /tmp/l52/msrun
mise exec -- dotnet build -c Release $l/fuzz -o /tmp/l52/fuzz

# Differential cases (writes Lua52Differential/observed/)
mise exec -- $l/difall.sh /tmp/l52/msrun/msrun.dll

# Fuzzer: every function, then one. It writes fuzz.tmp in its working directory; run these two
# and the suite lines below in a shell where mise is active (`eval "$(mise env)"` in the repository root).
(cd /tmp/l52 && timeout 1800 dotnet fuzz/fuzz.dll > fuzz.txt)
(cd /tmp/l52 && timeout 1800 dotnet fuzz/fuzz.dll string.format > fuzz-format.txt)

# string.format and bit32 against lua5.2
lua5.2 $l/fmtdiff.lua > /tmp/l52/fmt-lua.txt
timeout 600 mise exec -- dotnet /tmp/l52/msrun/msrun.dll $l/fmtdiff.lua > /tmp/l52/fmt-ms.txt
diff /tmp/l52/fmt-lua.txt /tmp/l52/fmt-ms.txt | head
lua5.2 $l/bitfuzz.lua > /tmp/l52/bit-lua.txt
timeout 600 mise exec -- dotnet /tmp/l52/msrun/msrun.dll $l/bitfuzz.lua > /tmp/l52/bit-ms.txt

# Official suite: rewrite, then run each file from its folder with both interpreters
python3 -I src/MoonSharp.Interpreter.Tests/Lua52Differential/suite/soften.py <lua-5.2.2-tests> /tmp/l52/suite \
  src/MoonSharp.Interpreter.Tests/Lua52Differential/suite/workarounds.txt
(cd /tmp/l52/suite && timeout -k 5 300 lua5.2 strings.lua; timeout -k 5 300 dotnet ../msrun/msrun.dll strings.lua)
```

`fmtdiff.lua` takes one conversion letter as an optional argument (`fmtdiff.lua d`). `msrun` and
`difall.sh` print their usage when called without arguments. Give every run a timeout: a crash or hang is a
finding.
