# Register VM spike

Date: 2026-10-28. Branch `spike/register-vm` (not for merging), on `next` `48fca10`. Commits `6e8c79b`
(prototype and tests) and `2a1e221` (coverage instrumentation). Evidence marks as in [README.md](README.md):
[C] confirmed by measurement or by reading the code, [S] secondary, [U] unconfirmed. File:line references
point at this branch unless they name another tree.

Owner decision (ROADMAP, 2026-10-29): build a time-boxed register VM prototype for loops and arithmetic and
decide on a full rewrite from its measured gain.

## Short answer

- The prototype compiles a subset of Lua to 3-address register code and runs it in its own dispatch loop over
  the existing value stack, `LuaValue` slots and call frames. Anything outside the subset stays on the stack
  VM, whole function at a time.
- Against `next` (stack VM with instruction fusion and inline caches), .NET 8, two locked passes of 21
  interleaved rounds, A/A within 0.95-1.03 [C]:

  | Workload | spike / next | spike / Lua-CSharp main |
  |---|---:|---:|
  | empty_loop | 0.66 | 0.96-1.00 |
  | int_math | 0.51 | 0.82-0.84 |
  | float_math | 0.39-0.40 | 0.73-0.75 |
  | function_call | 0.53 | 0.69-0.74 |
  | fib_recursive | 0.60 | 0.81-0.82 |
  | numeric-loop (Lua-CSharp suite) | 0.45-0.46 | 0.65-0.66 |
  | nbody (Lua-CSharp suite) | 0.46-0.48 | 1.20-1.29 |

- The gain holds without a JIT and on Unity's runtime: spike / next is 0.38-0.59 on NativeAOT (the IL2CPP
  stand-in) and 0.44-0.64 on Unity Mono 6.13 (`unity-mono` 2026-02-24.3, the docker-ci build), A/A within
  ±8% [C]. IL2CPP itself was not run. See [NativeAOT and Unity Mono](#nativeaot-and-unity-mono).

- On loops and arithmetic the register code runs as many instructions per iteration as the fused stack code
  (int_math 4 and 4, numeric-loop 2 and 2). The gain there comes from cheaper instructions: a struct
  instruction array with integer operands, registers addressed directly, constants in a `LuaValue[]`. Calls
  also run fewer instructions (11 to 7 per call in function_call) [C].
- The subset covers the numeric kernels and 20% of the functions in the corpus's application scripts; table
  constructors, `..`, `#`, `and`/`or`, generic `for`, closures and varargs keep the rest on the stack VM [C].
- Recommendation: build the register VM, incrementally, on this prototype's structure (register code next to
  stack code, whole-function fallback, differential tests). A check on IL2CPP in the Unity matrix is still
  worth doing first, but NativeAOT and Unity Mono already show the gain. Do not extend fusion further for loops and arithmetic: it already matches
  the register instruction count, so it has nothing left to remove there. Details in
  [Recommendation](#recommendation).

## What the prototype covers

Subset (`src/MoonSharp.Interpreter/Tree/RegisterCompiler.cs`):

- locals, upvalue reads and writes, globals through `_ENV` (upvalue in functions, local in chunks);
- number, string, boolean and nil constants;
- `+ - * / % ^`, unary minus, `not`;
- `< <= > >= == ~=` as conditions (compare-and-jump) and as values;
- `t.k`, `t["k"]`, `t[k]` reads and writes, with the stack VM's field caches;
- calls with a fixed argument list, as statements, as one-value expressions and as `return f(...)`;
- `if`/`elseif`/`else`, `while`, `repeat`, numeric `for`, `break`, `do ... end`, `return` of zero or one value.

Outside the subset, so the function stays on the stack VM: table constructors, `..`, `#`, `and`/`or` as
values, method calls `o:m()`, closures, varargs, multiple results feeding a call or an assignment, `return`
of several values, generic `for`, `goto`, expression trees deeper than 100 levels
(`RegisterCompiler.cs:240`, `:580`, `:713`).

### Design

- **Instructions.** `struct RInstr { ROp Op; int A, B, C, D; }` (`Execution/VM/RegisterProto.cs:54`). RK
  operands use `~k` for constants, as Lua's `ISK` bit does (Lua 5.3.6 `lopcodes.h:137`, `:159`) [C].
  Arithmetic comes in RR, RK and KR forms, one `case` each, so a number fast path checks one or two types
  (`Processor_Registers.cs`, the 18 cases before `arithmetic_slow:` at `:721`) [C].
- **Registers.** Locals keep one register for the whole function, parameters first
  (`RegisterCompiler.cs:79`); temporaries are a stack above them; a call puts the function and its arguments
  in consecutive top registers (`RegisterCompiler.cs:917`), so a register callee's frame starts at its first
  argument and needs no copy. Lua does the same: `luaD_precall` sets `base = func + 1` (Lua 5.3.6
  `ldo.c:451`) [C].
- **Frames.** A register frame is an ordinary `CallStackItem` whose `BasePointer` is its first register and
  `ArgCount` 0 (`Processor_Registers.cs:20`). Error unwinding, `PopScriptFrame` and the debugger's stack walk
  work unchanged [C]. Calls from register code to register code stay in the loop (`:630`); the frame carries
  `RegisterCall` and the caller's next pc in a new `SavedPc` field.
- **Slow paths.** Every instruction that can meet a metamethod, coercion, a C# function, a stack function or
  an error calls the stack VM's own helper (`ExecArithmetic`, `ExecIndex`, `ExecIndexSet`, `ExecLess`,
  `ExecEq`, `Internal_ExecCall`) with a per-site trampoline in the stack code as return address
  (`RegisterCompiler.cs:145`). A Lua metamethod or callee runs on the stack loop and returns to the
  trampoline; `OpCode.RegResume` (`Processor_InstructionLoop.cs:279`) resumes the register frame
  (`Processor_Registers.cs:94`, `:107`). The stack VM's behaviour on these paths is reused, which is what made
  the spike small and its results identical. `return f(...)` uses a stack `Ret 1` as return address
  (`:699`), so the stack VM's tail call optimisation and multiple results apply.
- **Entry.** The stack loop's `BeginFn` hands a function with register code to `RegisterEnter`, only on the
  main processor and without a debugger (`Processor_InstructionLoop.cs:270`). Coroutines, debugging and
  binary dumps use the stack code, which always exists.
- **Error positions.** Register code saves its pc before anything that can raise; `FillDebugData` takes the
  position from the innermost script frame when that frame runs register code (`Processor_Errors.cs:29`).
  Lookups of locals by name translate symbol indexes to registers (`Processor_Scope.cs:97`).
- **Instruction count.** The register loop decrements the same countdown as the stack loop
  (`Processor_Registers.cs:155`, `Processor_InstructionLoop.cs:80`), so the interrupt handler and the hosting
  instruction limit keep working. Lua-CSharp also counts every instruction (`--hookCount`,
  `LuaVirtualMachine.cs:423` in Lua-CSharp `a8d84c7`) [C].
- **Safe C# only.** Ref locals into arrays and structs; no `unsafe`, no `System.Runtime.CompilerServices.Unsafe`,
  no code generation. Lua-CSharp's loop uses `Unsafe.Add` to skip bounds checks
  (`LuaVirtualMachine.cs:421`, `:436`); the prototype pays them [C].

`ScriptOptions.RegisterVm` (default on, on this branch) turns the compiler off per `Script`, which the
differential tests and the `spikeoff` benchmark column use.

## Measurements

### Setup

| | |
|---|---|
| next | `48fca10`, netstandard2.0 Release `MoonSharp.Interpreter.dll` |
| spike | `6e8c79b`, same build |
| spikeoff | the spike DLL with `RegisterVm = false`: the stack path with the spike's extra fields and checks |
| Lua-CSharp | `main` `a8d84c7`, net8.0 Release, run as its own benchmark adapter does (`state.Load` once, `ExecuteAsync` per execution) |
| Runtime | .NET 8.0.32, default tiering and PGO, 4-vCPU cloud VM; load average 2-6 from other sessions' builds |
| Harness | one process; each MoonSharp build in its own `AssemblyLoadContext`; 7 workloads; 3 warm-up runs per engine with the result checked; 21 rounds, engine order rotating per round; the whole process under `flock bench.lock`; two passes in separate processes |

Workloads: `empty_loop`, `int_math`, `float_math`, `function_call`, `fib_recursive` with the sources and
iteration counts of `src/DevTools/Benchmarks/LuaWorkloads.cs` (fib at 4,000 instead of 20,000 iterations per
round), ns per inner iteration; `numeric-loop` and `nbody` from Lua-CSharp's `ComparisonWorkloads.cs`, ns per
execution of the retained chunk. The harness lives in the session scratchpad (`regvm/bench/Program.cs`).

### Results

Medians of 21 rounds, pass 1 / pass 2:

| Workload | next | next (A/A copy) | spike | spikeoff | Lua-CSharp |
|---|---:|---:|---:|---:|---:|
| empty_loop (ns) | 7.5 / 7.8 | 7.6 / 7.7 | 4.9 / 5.1 | 7.5 / 7.8 | 4.9 / 5.3 |
| int_math (ns) | 36.4 / 38.3 | 36.4 / 36.9 | 19.0 / 19.0 | 37.5 / 37.1 | 22.7 / 23.2 |
| float_math (ns) | 70.7 / 68.6 | 70.1 / 68.5 | 27.4 / 26.8 | 70.9 / 70.3 | 36.7 / 36.5 |
| function_call (ns) | 120.7 / 118.2 | 114.7 / 116.4 | 64.5 / 60.8 | 120.4 / 114.4 | 87.6 / 88.4 |
| fib_recursive (µs) | 58.0 / 57.1 | 53.8 / 58.5 | 34.3 / 33.3 | 57.1 / 53.2 | 42.0 / 40.9 |
| numeric-loop (µs) | 313 / 315 | 316 / 337 | 141 / 145 | 314 / 318 | 212 / 221 |
| nbody (µs) | 1,622 / 1,517 | 1,677 / 1,536 | 774 / 713 | 1,631 / 1,547 | 600 / 593 |

Ratios to `next`, median of per-round ratios, pass 1 / pass 2:

| Workload | A/A | spike | spikeoff | Lua-CSharp |
|---|---:|---:|---:|---:|
| empty_loop | 1.009 / 0.991 | 0.659 / 0.658 | 1.004 / 0.991 | 0.655 / 0.657 |
| int_math | 0.998 / 0.984 | 0.514 / 0.511 | 1.031 / 0.973 | 0.616 / 0.617 |
| float_math | 0.983 / 1.013 | 0.387 / 0.399 | 0.972 / 1.018 | 0.524 / 0.541 |
| function_call | 0.963 / 1.001 | 0.535 / 0.527 | 0.993 / 0.983 | 0.736 / 0.756 |
| fib_recursive | 0.946 / 1.030 | 0.596 / 0.598 | 0.984 / 0.962 | 0.725 / 0.732 |
| numeric-loop | 0.984 / 1.002 | 0.452 / 0.461 | 1.008 / 1.000 | 0.664 / 0.710 |
| nbody | 1.011 / 1.003 | 0.477 / 0.464 | 1.028 / 1.019 | 0.366 / 0.390 |

Reading [C]:

- The A/A column bounds the noise at about ±5%. Every spike row lies far outside it.
- `spikeoff` matches `next`, so the extra frame field, closure field and `BeginFn` check cost nothing
  measurable on the stack path.
- The spike beats Lua-CSharp main on five rows, ties on `empty_loop` and loses on `nbody` (1.2-1.3x).

These Lua-CSharp ratios come from .NET 8. The note [lua-csharp-own-bench.md](lua-csharp-own-bench.md) used
.NET 10 and BenchmarkDotNet and found `next` 1.65x behind on numeric-loop and 2.41x on nbody; this harness
finds 1.4-1.5x and 2.6-2.7x [C for each run; the runtime difference explains the gap only as a guess, U].

### NativeAOT and Unity Mono

The coordinator asked for the runtimes the first runs did not cover. Same seven workloads, one engine per
process (`regvm/b1/src/Program.cs` in the session scratchpad), built outside the lock:

- **NativeAOT**: `dotnet publish -p:PublishAot=true` for linux-x64 (.NET SDK 10.0.402, net8.0, clang 18),
  one executable per build: `next` `48fca10`, the spike at `2a1e221` (same VM code as `6e8c79b` plus compile
  counters), the spike with `RegisterVm = false`, Lua-CSharp main `a8d84c7`. No tiered JIT and ahead-of-time
  code, as on IL2CPP; it keeps bounds checks as IL2CPP does by default [S].
- **Unity Mono**: `unity-mono-linux-2026-02-24.3.tar.gz` from Benjamin-Dobell/unity-mono, the archive the
  Dockerfile pins, checked against its SHA-256 (`3cecef99...0008`) and unpacked into a private prefix, not
  into `/` as the Dockerfile does. `mono --version`: 6.13.0 (explicit/fbe0f703560). Its loader log shows it
  loads its own `lib/mono/4.5/mscorlib.dll`, not the system Mono 6.8 [C]. The harness is a net48 executable
  against the net45 interpreter builds of the same commits. Lua-CSharp was left out: it needs
  netstandard2.1, which a net48 executable cannot reference.
- 11 rounds; each round holds `bench.lock` once and runs every engine as its own process, in an order that
  rotates per round; each process reports the median of 3 timed runs after 2 checked warm-up runs. A/A is a
  second process of the `next` build. Load average 2.1-6.8 from other sessions.

NativeAOT, medians of rounds (ns per iteration, µs per execution for the last two rows):

| Workload | next | next (A/A) | spike | spikeoff | Lua-CSharp |
|---|---:|---:|---:|---:|---:|
| empty_loop | 8.2 | 8.2 | 4.9 | 8.2 | 5.2 |
| int_math | 39.4 | 39.6 | 18.9 | 38.9 | 20.0 |
| float_math | 69.4 | 69.0 | 26.7 | 68.6 | 34.0 |
| function_call | 129.8 | 131.0 | 64.1 | 135.2 | 85.7 |
| fib_recursive (µs) | 58.5 | 62.9 | 32.7 | 62.1 | 39.2 |
| numeric-loop (µs) | 318.5 | 324.4 | 148.9 | 315.0 | 190.7 |
| nbody (µs) | 1,690 | 1,710 | 858 | 1,712 | 601 |

Unity Mono 6.13, same layout, no Lua-CSharp:

| Workload | next | next (A/A) | spike | spikeoff |
|---|---:|---:|---:|---:|
| empty_loop | 18.8 | 18.6 | 12.0 | 18.8 |
| int_math | 88.7 | 87.2 | 44.7 | 88.1 |
| float_math | 172.4 | 166.1 | 86.5 | 164.1 |
| function_call | 182.0 | 185.7 | 83.0 | 183.8 |
| fib_recursive (µs) | 82.8 | 84.4 | 39.2 | 86.8 |
| numeric-loop (µs) | 791 | 769 | 455 | 751 |
| nbody (µs) | 2,605 | 2,586 | 1,159 | 2,534 |

Ratios to `next`, median of per-round ratios (interquartile range in the scratchpad's `regvm/b1/result.txt`):

| Workload | AOT A/A | AOT spike | AOT spikeoff | AOT spike / Lua-CSharp | Mono A/A | Mono spike | Mono spikeoff |
|---|---:|---:|---:|---:|---:|---:|---:|
| empty_loop | 1.002 | 0.594 | 1.000 | 0.93 | 1.005 | 0.637 | 1.006 |
| int_math | 1.007 | 0.490 | 0.988 | 0.95 | 0.967 | 0.516 | 1.017 |
| float_math | 1.001 | 0.381 | 0.993 | 0.81 | 0.959 | 0.503 | 0.982 |
| function_call | 0.997 | 0.492 | 1.054 | 0.75 | 1.007 | 0.460 | 1.012 |
| fib_recursive | 1.075 | 0.560 | 1.072 | 0.84 | 1.010 | 0.479 | 1.060 |
| numeric-loop | 1.013 | 0.468 | 0.979 | 0.78 | 0.985 | 0.596 | 0.977 |
| nbody | 0.990 | 0.506 | 1.005 | 1.45 | 0.999 | 0.444 | 0.991 |

Reading [C]:

- **NativeAOT.** The spike runs in 0.38-0.59 of `next`'s time, as on the JIT (0.39-0.66). It stays ahead of
  Lua-CSharp on six rows and behind on nbody (1.45x).
- **Unity Mono.** Each engine is 1.4-2.5x slower than on NativeAOT. The ratio is 0.44-0.64: on calls (0.46-0.48)
  and nbody (0.44) the gain is larger than on CoreCLR, on float_math (0.50) and numeric-loop (0.60) smaller.
- **Noise.** A/A stays within ±8%. The worst row is AOT fib_recursive at 1.075.
- **Stack-path overhead.** `spikeoff` on AOT function_call is 1.054 with an interquartile range of
  1.035-1.073, while its A/A row is 0.997. That suggests a 5% cost of the larger call frame (`SavedPc`) on
  stack calls under NativeAOT [S]. The fib row shows the same 1.07, but its A/A does too, so it says nothing.

### Where the gain comes from

Instructions per iteration, counted with the interrupt handler (one call per 10,000 instructions) [C]:

| Workload | stack (fused) | register |
|---|---:|---:|
| empty_loop | 1 | 1 |
| int_math | 4 | 4 |
| float_math | 8 | 8 |
| function_call | 11 | 7 |
| fib_recursive (per outer iteration, fib(12)) | 4,420 | 2,555 |
| numeric-loop (per execution) | 40,000 | 40,000 |
| nbody (per execution, ±10,000) | 110,000 | 60,000 |

The register code for `int_math`'s loop body is `MulRK`, `AddRR`, `ModRK`, `ForLoop`; the fused stack code
also runs 4 (`CompilerFusionTests.cs`, `ArithmeticCases`). So on loops and arithmetic, fusion already
removed the push/pop instructions, and the remaining 1.5-2.6x is the cost of each instruction. A fused stack
instruction:

- loads a class `Instruction` and then reaches its operands through objects: `i.Symbol.i_Index` for a local,
  `i.Value.Number` (a `DynValue`) for a constant (`Processor_InstructionLoop.cs:373` onwards);
- switches on an operand-mode field inside the case;
- reads and writes `m_ValueStack.Count`;
- `ForLoop` also clears the block's locals to `Undeclared` and checks open upvalues on every iteration
  (`Processor_InstructionLoop.cs:463`, `:474`).

The register loop reads integer operands from a struct and addresses slots directly. Which of these costs
dominates was not measured [U].

Calls save instructions and work: an in-loop register call skips `Internal_ExecCall`'s general path, the
`ExecArgs` copy, `BeginFn`'s `Expand` and the `CallingSourceRef` lookup through `List<Instruction>`
(`Processor_InstructionLoop.cs:1264`, `:1164`, `:1073`) [C for the code paths, U for their shares].

`nbody` allocates 246,752 bytes per execution on both VMs (Lua-CSharp: 3,952 in the earlier note). Tables
store `DynValue`, so every number written to a field allocates one. A register VM does not change that; the
remaining nbody gap to Lua-CSharp is table storage [C for the bytes, S for the attribution].

### Coverage of real code

Functions the compiler accepted, by source set (`RegisterCompiler.s_Fallbacks`, compile only) [C]:

| Source | Functions | Register | Main reasons for fallback |
|---|---:|---:|---|
| corpus `src/Hosting/fixtures/scripts/*.lua` | 15 | 3 (20%) | table constructors (7), `..`, `and`, `#`, generic for, varargs |
| `TestMore/*.t` | 495 | 323 (65%) | table constructors (44), `..` (23), closures (19), varargs (17), `#` (15) |
| Lua 5.2 test suite (Lua-CSharp `tests/Lua.Tests/tests-lua`, 23 of 26 files parse) | 474 | 224 (47%) | table constructors (38), varargs (28), multi-value calls (25 + 21), `or` (23) |
| `n-body.lua` | 4 | 3 | the setup chunk (table constructor) |

The gain applies to numeric kernels today. Application logic needs the rest of the opcodes before it sees
any of it.

## Correctness

- `RegisterVmSpikeTests` (on this branch): about 30,000 generated sources, each run with the register VM off
  and on in two `Script`s with the same setup; results and decorated error messages, positions included, must
  match. Values cover numbers, NaN, infinity, numeric and non-numeric strings, nil, booleans, plain tables,
  tables with Lua metamethods for every arithmetic and comparison event, a table with only `__lt`, `__index` and
  `__newindex` functions and tables, `__call`, C# functions, stack functions with `...`, functions returning two
  values, `pcall`, numeric `for` bounds of every type including the error stages. All pass [C].
- The tests can fail: making `AddRR` add 1 for one operand value, or dropping the register error position,
  turned them red [C].
- `RegisterVm_RunsFewerInstructionsPerIteration` shows the register path runs (a global store loop: 2
  instructions per iteration against more on the stack VM) [C].
- Unit suite (`DotNetCoreTestRunner /unit`): the runner reports `OK : 1071/1073, Failed 1/1073, Skipped 3/1073`
  (`next`: `OK : 1064/1065, Failed 0/1065, Skipped 3/1065`). The failure is
  `ProcessorLoop_DebuggerAttachedMidRunStopsAtTheNextInstruction`. A debugger attached while register code runs
  is not consulted until that function returns. That is a gap of the spike, listed below [C].
- `closure_create_call` allocates 160 bytes per iteration instead of 152: `Closure.Register` adds 8 bytes. The
  spike raised the budget [C].
- Conformance corpus: 248 of 248 on the `dotnet` and `dotnet-bytecode` legs [C]. The bytecode leg runs the
  stack VM, since dumps carry no register code.
- Interpreter DLL (netstandard2.0 Release): 436,736 to 454,656 bytes (+17,920) [C].
- The workloads give the same checked results on NativeAOT and Unity Mono [C]. The unit suite was not run on
  either for this branch. IL2CPP and the Unity matrix were not run [U]. The code uses only constructs IL2CPP
  compiles (structs, ref locals, no reflection emit), so failures there are unlikely [S].

## What a full rewrite needs

1. **Compiler, every construct.** Table constructors (`NEWTABLE`/`SETLIST`), `..` over a register range,
   `#`, `and`/`or` as values (`TESTSET`), method calls (`SELF`), closures with upvalue capture of registers
   and block-exit closing, varargs, multiple results (`CALL` with open results, multi-assignment, `return`
   of several values), generic `for` with MoonSharp's `__iterator` and table iteration extensions, `goto`,
   MoonSharp's lambda syntax, and long expression chains without recursion (the stack compiler walks
   200,000-term chains; `NativeStack_LongOperatorChainsCompileAndRun`).
2. **Every opcode with its own slow path.** The spike borrows the stack VM's helpers through trampolines;
   without the stack VM those helpers move to register operands.
3. **Calling convention.** Results in registers instead of one `Tuple` `DynValue`, which also removes the
   per-call allocation on `calls-varargs` (496 KB in the earlier note). C# functions keep returning
   `DynValue`, so the boundary needs an adapter.
4. **Coroutines.** Yield inside register frames. The trampoline design already resumes a register frame
   after any slow path, which is the mechanism a yield needs, but the spike enables register code only on
   the main processor, and auto-yield counting is not wired into the register loop [U].
5. **Debugger.** Stepping and breakpoints per register instruction, a debugger attached mid-run, locals by
   register (the spike's `RegOf` map), the VS Code debugger's bytecode listing. `IDebugger.GetAction(int ip,
   SourceRef)` passes a stack address; a register VM changes what `ip` means for host debuggers (public API).
6. **Dump format.** A new format version for register code. Old dumps (0x151-0x154) hold only stack code and
   no syntax tree, so loading them means keeping the stack VM or translating stack bytecode to register code.
7. **Host-visible counts.** `InterruptHandler` and the hosting `instructionLimit` count instructions; the counts
   drop on calls and change in general (MIGRATING.md entry, corpus `limits` suite expectations).
8. **Hotfix-like features.** MoonSharp has no hotfix injection; the features that read the VM's internals are
   dynamic expressions (`CreateDynamicExpression` evaluates the syntax tree and reads locals by name, as the
   spike already handles) and `debug.getlocal`/`getinfo`/`traceback`.
9. **Tests.** The unit suite, TestMore and the corpus as they are, plus the differential stack-against-register
   tests for as long as both VMs exist.

### Size and risk

- Spike: 1,438 code lines (compiler 723, loop 661, types 54), about a third of the opcodes, every slow path
  borrowed [C].
- Lua-CSharp, for scale: compiler 4,703 lines, runtime 5,714 (VM 3,278) [C].
- Estimate for a complete register VM in MoonSharp: compiler 3,000-4,000 lines, VM 2,500-3,500, debugger,
  dump and coroutine work 1,000-1,500 [U]. It replaces the stack loop (1,633 lines), fusion (173) and the
  `Compile` methods of the syntax tree.
- DLL: +18 KB for the spike; while both VMs ship, likely +60-100 KB [U]; less once the stack VM goes.
- Risk: medium while the stack VM stays as fallback, since every function can drop back to code that passes
  today's tests and the differential tests compare the two. High at the point the stack VM is removed (dumps,
  debugger API, instruction counts).

## Recommendation

Build the register VM incrementally, in this order:

1. Run this branch's workloads once on IL2CPP in the Unity matrix (a player test). NativeAOT and Unity Mono
   keep the gain (0.38-0.64 of `next`'s time), so this is a check, not the open question it was.
2. Extend the subset: table constructors, `..`, `#`, `and`/`or`, method calls,
   multiple results and closures, by coverage of the corpus scripts. Each group lands with its differential
   tests and stays behind the whole-function fallback.
3. Move coroutines and the debugger to register frames.
4. Then decide whether to retire the stack VM, which is where the dump format and debugger API break.

Extending fusion instead leaves little on loops and arithmetic: the fused stack code runs as many instructions
as the register code. The rest of the gain needs a new instruction format and dispatch loop, which is most of
a register VM's work anyway. Stopping keeps `next` 1.4-1.9x behind Lua-CSharp main on six of these rows and
2.6-2.7x on nbody; the prototype is ahead on five of seven.

Separate from this decision: table storage of `LuaValue` instead of `DynValue` would close most of the
remaining `nbody` gap on either VM.

## Gaps of the prototype

- A debugger attached during a register function is ignored until it returns.
- Coroutines run the stack code.
- Deep tail recursion through in-loop register calls is not optimised (the stack VM optimises tail calls past
  65,536 frames); `return f()` itself goes through the stack VM and keeps its optimisation.
- Compile time: register compilation runs after the stack compiler and throws an exception for each fallback.
  Not measured [U].

## How to measure on IL2CPP

Added 2026-10-28 for the owner's decision (ROADMAP, 2026-10-29: measure a real Unity IL2CPP player, for
speed and size). Not run yet: it needs the Unity secrets. Steps to start it are in
[ROADMAP.md](../ROADMAP.md#needs-the-owner).

- **What runs.** `.github/workflows/unity.yml`, started by hand with `bench` and `bench_baseline`. The Unity
  test bed's Linux IL2CPP player (editor default player settings, IL2CPP backend) takes `-moonsharpBench FILE`
  and runs the seven workloads above through `src/DevTools/Benchmarks/WorkloadTsv.cs`: one warm-up and five
  timed runs each, one row per workload with the median ns per iteration and the check value. `sync.sh`
  copies `LuaWorkloads.cs`, `LuaCSharpScripts.cs` and `WorkloadTsv.cs` into the test bed, so the player runs
  the same source as `dotnet run -- workloads-tsv`. Iteration counts are those of `workloads` (fib_recursive
  20,000, numeric_loop 500 and nbody 100 executions, against 4,000, 200 and 40 in the harness above); the
  numbers are per iteration, so they compare.
- **Same runner.** With `bench_baseline` the job also builds the baseline's test bed and size probe and runs
  seven rounds, each starting the spike player, the baseline player and the spike player again (the A/A
  row), in an order that rotates per round. Ratios are medians of per-round ratios. One run instead of a run
  per branch, because hosted runners differ in speed from run to run. The editor (Mono) gets one process per
  build, with `-releaseCodeOptimization`. Each of the three Unity versions reports separately.
- **Size.** The size step builds `src/Unity/SizeProbe` with the baseline's package too; the table's last
  column is what the spike adds over the baseline in `GameAssembly.so` (as built, stripped, gzip -9),
  `global-metadata.dat` and the whole player.
- **Branches.** `spike/register-vm-unity` is this branch with `ci/unity-il2cpp-bench` merged in: the spike's
  `unity.yml` already matched `next`, so only the benchmark commits were missing. One more change was needed
  there: `RegisterVmSpikeTests` called `Assert.GreaterOrEqual` and the formatting overloads of `Assert.Fail`
  and `Assert.Greater`, which the NUnit subset Unity compiles against (`EmbeddableNUnitWrapper.cs`) does not
  have. The test bed and `EmbeddedTestRunner`, the workflow's first step, failed to compile; the job would
  have stopped before Unity started. The baseline, `ci/unity-il2cpp-bench`, is `next` with the same benchmark
  commits, so the two players differ only by the prototype.
- **Checked without Unity** [C]: the test bed with the spike and with the baseline compiles with Unity 2021.3's
  and 6000.0's own compiler and reference assemblies (editor and player defines, the source generator as an
  analyzer); 2022.3 was not available locally. `EmbeddedTestRunner` on the spike: 1,070 OK, 1 failed (the
  debugger gap above), 4 skipped. Five interleaved rounds of the same pipeline on this machine
  (`workloads-tsv` per process, `bench-summary.sh` over the files), spike / baseline, median with range:

  | Workload | .NET 8 | A/A | Unity Mono 6.13 | A/A |
  |---|---:|---:|---:|---:|
  | empty_loop | 0.618 (0.593-0.632) | 1.000 | 0.664 (0.643-0.938) | 0.967 |
  | int_math | 0.475 (0.451-0.496) | 0.992 | 0.540 (0.498-0.569) | 0.976 |
  | float_math | 0.390 (0.366-0.422) | 1.016 | 0.529 (0.508-0.556) | 1.036 |
  | function_call | 0.608 (0.512-0.652) | 0.998 | 0.432 (0.428-0.467) | 1.020 |
  | fib_recursive | 0.676 (0.615-0.702) | 0.989 | 0.475 (0.457-0.486) | 1.005 |
  | numeric_loop | 0.462 (0.440-0.512) | 0.993 | 0.604 (0.543-0.656) | 0.970 |
  | nbody | 0.611 (0.544-0.668) | 0.984 | 0.460 (0.435-0.468) | 0.985 |

  These agree with the measurements above (0.39-0.66 on .NET, 0.44-0.64 on Unity Mono), so the pipeline
  reproduces them; every check value matched on both runtimes.
- **What only the Unity run shows** [U]: IL2CPP's own code for the dispatch loop (the prototype's gain on
  NativeAOT is the stand-in so far), the Unity editor's Mono, the size on each Unity version's IL2CPP and
  linker, and whether the player runs the register code at all after stripping (the check values say the
  results are right, not which VM produced them; the speed difference does). Not covered: Android ARM64 and
  iOS players, which use another compiler and CPU.

## Reproducing

```sh
git switch spike/register-vm
dotnet build -c Release -f netstandard2.0 src/MoonSharp.Interpreter
dotnet build -c Release src/TestRunners/DotNetCoreTestRunner
(cd src/TestRunners/DotNetCoreTestRunner/bin/Release/net8.0 && dotnet DotNetCoreTestRunner.dll /unit)
```

The benchmark harness, the instruction-count probe and the coverage probe are in the session scratchpad
(`regvm/bench`, `regvm/t1`), not in this repository.
