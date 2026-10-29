# Notes for coding agents

This repository is a fork of [moonsharp-devs/moonsharp](https://github.com/moonsharp-devs/moonsharp),
a Lua 5.2 interpreter written in C#. The fork turns it into a fast, fully managed
engine for Unity (2021.3, 2022.3 and Unity 6 LTS, Mono and IL2CPP), .NET, browsers
(.NET WebAssembly) and the JVM. Read [CONTRIBUTING.md](CONTRIBUTING.md) first; this
file adds what an agent needs on top of it.

## Where things are

| What | Where |
|---|---|
| Interpreter (`netstandard2.0` and `net45`) | `src/MoonSharp.Interpreter`: `Tree/` (parser and compiler), `Execution/` (bytecode and VM), `DataTypes/` (values, `Table`), `CoreLib/` (standard library), `Interop/` (CLR userdata), `ILLink.*.xml` (trimming) |
| Unit tests | `src/MoonSharp.Interpreter.Tests` (`EndToEnd/`, `Units/`, `TestMore/`), Lua 5.2 differential cases in `Lua52Differential/` |
| Test runners | `src/TestRunners`: `DotNetCoreTestRunner` (.NET 8, also NativeAOT through `nativeaot.sh`), `ConsoleTestRunner` (Mono, `net45`), `EmbeddedTestRunner` (the sources compiled as Unity compiles them) |
| Source generator | `src/MoonSharp.SourceGenerator` ([docs/source-generator.md](docs/source-generator.md)) |
| Hardwire generator, REPL and CLI | `src/MoonSharp.Hardwire`, `src/MoonSharp` |
| Debuggers | `src/MoonSharp.RemoteDebugger`, `src/MoonSharp.VsCodeDebugger`, `src/moonsharp-vscode-debug` (VS Code extension) |
| Hosting: JSON contract, CLI, browser, Java, JNI shim | `src/Hosting` ([docs/hosting.md](docs/hosting.md)) |
| Conformance corpus run by every host | `src/Hosting/fixtures` ([README](src/Hosting/fixtures/README.md)) |
| LuaCATS generator | `src/Tools/MoonSharp.LuaCats*`, `tools/luals` ([docs/luacats.md](docs/luacats.md)) |
| Unity test bed, size probe, package | `src/Unity`, `tools/upm` |
| Benchmarks and other developer tools | `src/DevTools` ([Benchmarks README](src/DevTools/Benchmarks/README.md)) |
| Size tracking | `tools/size` (ROADMAP, "Size tracking") |
| Plans, decisions, open work, "Needs the owner" | [docs/ROADMAP.md](docs/ROADMAP.md) |
| Every change a script or host can observe | [docs/MIGRATING.md](docs/MIGRATING.md) |
| Upstream relationship and upstream issues | [docs/upstream.md](docs/upstream.md), [docs/upstream-issues.md](docs/upstream-issues.md) |
| Research behind decisions | [docs/research/](docs/research/README.md) |
| Toolchain versions | [mise.toml](mise.toml) |
| CI | `.github/workflows` (table under [CI](#ci)) |

## Design rules

The decisions table in [docs/ROADMAP.md](docs/ROADMAP.md) is the source; these are
the standing ones.

- **Safe C# only.** No `unsafe` and no `System.Runtime.CompilerServices.Unsafe`.
  A bug must surface as an exception, not as memory corruption.
- **Public Unity mechanisms only.** No libil2cpp patches and no code generated
  against IL2CPP internals.
- **Unity 2021.3 sets the floor.** The interpreter builds for `netstandard2.0`
  and `net45`. Code that Unity compiles stays within C# 9. No code generation at
  run time on AOT paths (`Reflection.Emit`, compiled expression trees). The source
  generator references Microsoft.CodeAnalysis 3.8 so that every supported
  editor's compiler loads it.
- **No unnecessary code.** Nothing speculative, no unused helpers, no
  reimplementation of what the platform already does well. New tools go into
  their own projects so the interpreter DLL does not grow.
- **Size is tracked, never a gate.** Report the size of the `netstandard2.0`
  Release `MoonSharp.Interpreter.dll` before and after any change to the
  interpreter (`tools/size/size.sh`). CI warns when the DLL moves more than
  512 bytes from `tools/size/interpreter-dll.baseline`; a change that grows it
  on purpose updates the baseline in the same commit.
- **Breaking changes are allowed** when they buy speed or correctness. Every
  change a script or host can observe goes into
  [docs/MIGRATING.md](docs/MIGRATING.md) in the same branch: what changed, what it
  was before, who is affected.
- **Hosts register the types they choose.** Mobile hosts use explicit
  registration; do not add automatic registration paths they would pay for.
- **Measure performance changes.** A performance change carries A/B numbers from
  `src/DevTools/Benchmarks` (`ab` mode). A fix carries a test that fails without it.
- **No upstream pull requests** to moonsharp-devs/moonsharp without the owner's
  approval.

When a rule gets in the way of a better technical route, write down the cost and
the alternative and ask the owner. Do not decide alone.

## Git

- **Conventional Commits:** `type(scope): summary` in English, imperative,
  lowercase, no trailing period, header at most 72 characters. The body explains
  why, with the evidence (measurements, test counts, issue links).
- **Types:** `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `build`, `ci`,
  `chore`, `revert`.
- **A scope is always required.** Use the narrowest one that covers the change:

  | Scope | Covers |
  |---|---|
  | `vm` | Parser, compiler, bytecode, dump format, instruction loop, stacks, coroutines |
  | `runtime` | `Script`, execution contexts, callbacks and module registration: the interpreter outside the VM, the library and interop |
  | `table` | `Table` and its storage |
  | `stdlib` | The standard library in `CoreLib`; `stringlib`, `io`, `json` or `debug` when only that module changes |
  | `interop` | CLR userdata: descriptors, converters, overloads, delegates |
  | `trim` | Trimming and AOT annotations (`ILLink.*.xml`, trimming attributes) |
  | `sourcegen` | `src/MoonSharp.SourceGenerator` |
  | `hardwire` | `src/MoonSharp.Hardwire` and the CLI's `-W` code generation |
  | `cli` | `src/MoonSharp` (REPL and command line) |
  | `debugger` | The remote and VS Code debuggers and the VS Code extension |
  | `hosting` | `src/Hosting`: contract, CLI, browser, Java, JNI shim, fixtures |
  | `luacats` | The LuaCATS generator and `tools/luals` |
  | `unity` | `src/Unity` and Unity-only code paths |
  | `upm` | Unity package staging (`tools/upm`) |
  | `tests` | Shared test harness: `src/TestRunners`, `nativeaot.sh`, known-failures files |
  | `conformance` | The Lua 5.2 differential cases and their tooling |
  | `benchmarks` | `src/DevTools/Benchmarks` and the other developer tools |
  | `size` | `tools/size` and its baseline |
  | `research` | `docs/research/` |
  | `deps` | Renovate configuration and dependency updates |
  | `repo` | Repository-wide files (`README.md`, `AGENTS.md`, `CONTRIBUTING.md`, `mise.toml`, `docs/ROADMAP.md`) and changes that span several parts |

  - Tests and docs about one part take that part's scope: `test(vm): ...`,
    `docs(hosting): ...`.
  - `ci` commits take the workflow's name (`ci(hosting)`, `ci(unity)`,
    `ci(luacats)`, `ci(upm-release)`), the area of one `ci.yml` job
    (`ci(size)`), or `ci(repo)` for `ci.yml` as a whole and for several
    workflows.
  - Breaking changes add `!` after the scope and a `BREAKING CHANGE:` footer.
- **AI attribution:** an agent-authored commit ends with a `Co-Authored-By:`
  trailer that names the model that wrote it, with its vendor's attribution
  address, for example `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  A stale model name is false attribution.
- **One reason per commit.** Stage only your own paths; never commit another
  worker's changes, and never clean or reset a shared tree to get a clean status.
- **Branches:** work on a branch made from `origin/next`, in your own worktree.
  When `next` moves, merge `origin/next` into your branch; do not rebase. Push
  your branch only. `next` collects finished branches and lands on `master` in
  batches with `git merge --no-ff`, after the full unit suite and the fixtures
  pass locally; agents push `next` or `master` only when the owner asked them to
  merge.
- **History:** never squash, rebase-merge, force-push or rewrite pushed history.
  Amend only commits you have not pushed, and prefer a new commit.
- **The owner decides:** tags, releases, NuGet or package publishing, repository
  settings and visibility, secrets, and pull requests to upstream. Pull requests
  inside the fork are optional.
- **Upstream issues:** link the full URL in the body, for example
  `https://github.com/moonsharp-devs/moonsharp/issues/287`.

## Toolchain

`mise install` in the repository root installs the pinned .NET SDK and .NET 8
runtime, Java 17, Maven, Node and CMake ([mise.toml](mise.toml) says why each is
there). Run commands through `mise exec -- <command>` from the repository root, or
in a shell where mise is active. `mise -C <dir> exec` also runs the command inside
`<dir>`, so relative paths resolve from there. From the distribution: clang and
`zlib1g-dev` (NativeAOT), Mono (the `net45` runner) and, optionally, Docker (the
`docker-ci` job). The browser build also needs the `wasm-tools` workload:
`dotnet workload install wasm-tools --version 10.0.402`.

## Tests

Run the suites that cover your change, each with a timeout. All commands run from
the repository root; prefix them with `mise exec --` where mise is not active.

**Unit suite** (every change to `src/`). The runner reads `TestMore/` relative to
its working directory, so run it from its output folder:

```sh
dotnet build -c Release src/TestRunners/DotNetCoreTestRunner
(cd src/TestRunners/DotNetCoreTestRunner/bin/Release/net8.0 && timeout 600 dotnet DotNetCoreTestRunner.dll /unit)
```

**The other runners** (interpreter changes):

```sh
# Mono, net45 (CI runs it on Unity's Mono inside the Docker image)
dotnet build -c Release src/TestRunners/ConsoleTestRunner
(cd src/TestRunners/ConsoleTestRunner/bin/Release/net45 && timeout 900 mono ConsoleTestRunner.exe)

# The test sources compiled as Unity compiles them, without NUnit
dotnet build -c Release src/TestRunners/EmbeddedTestRunner
timeout 600 dotnet src/TestRunners/EmbeddedTestRunner/bin/Release/net8.0/EmbeddedTestRunner.dll

# NativeAOT, a stand-in for IL2CPP: JIT and AOT runs, checked against nativeaot-known-failures.txt
timeout 1800 src/TestRunners/DotNetCoreTestRunner/nativeaot.sh
```

A new test that reads embedded resources or private members by reflection fails
under NativeAOT: add it to `AOT_SKIPLIST` in
`src/TestRunners/DotNetCoreTestRunner/Program.cs` with the reason, and run
`nativeaot.sh` before you hand back.

**Conformance corpus on .NET** (interpreter and hosting changes), in its three
modes: from source, under a culture with a decimal comma, and from precompiled
bytecode:

```sh
dotnet build -c Release src/Hosting/MoonSharp.Hosting.Cli
host=src/Hosting/MoonSharp.Hosting.Cli/bin/Release/net8.0/moonsharp-host.dll
timeout 600 dotnet $host test src/Hosting/fixtures
LANG=tr_TR.UTF-8 timeout 600 dotnet $host test --host dotnet-tr-TR src/Hosting/fixtures
timeout 600 dotnet $host test --bytecode src/Hosting/fixtures
```

Pass one suite file, such as `src/Hosting/fixtures/logic.suite.json`, to run only
that suite. Each run fails on a new, changed or fixed failure against
`src/Hosting/fixtures/known-failures/`.

**Java hosting tests.** Run them for any change to errors, library limits or crash
behaviour: they check error kinds across the native boundary. They need the host
CLI above, then:

```sh
dotnet build -c Release src/Hosting/MoonSharp.Hosting.Native
dotnet publish -c Release -r linux-x64 -p:NativeAot=true src/Hosting/MoonSharp.Hosting.Native
cmake -S src/Hosting/native -B src/Hosting/native/build -DCMAKE_BUILD_TYPE=Release
cmake --build src/Hosting/native/build
timeout 1800 mvn -B -f src/Hosting/java/pom.xml test -Dmoonsharp.inprocess.required=true
```

**Browser** (changes to `src/Hosting/browser`, or to code the WebAssembly build
trims). The `hosting` workflow runs both builds in Chromium, Firefox and WebKit;
locally one build in one browser is the minimum:

```sh
cd src/Hosting/browser
npm ci && npx tsc -p tsconfig.json && dotnet publish -c Release
npx playwright install --with-deps chromium
timeout 900 node test/smoke.mjs bin/Release/net10.0/publish/wwwroot ../fixtures chromium
```

**Dockerfile COPY check.** The `docker-ci` job builds from a copy of the folders
the `Dockerfile` lists. A project that the test runners reference must be copied
there too. Before you hand back a change that adds a project or a project
reference, build the runners from a scratch copy of exactly those paths:

```sh
d=$(mktemp -d)
git ls-files -z --cached --others --exclude-standard -- $(sed -n 's/^COPY \([^ ]*\) .*/\1/p' Dockerfile) \
  | tar --null -T - -c | tar -x -C "$d"
dotnet build -c Release "$d/src/TestRunners/ConsoleTestRunner"
dotnet build -c Release "$d/src/TestRunners/DotNetCoreTestRunner"
rm -rf "$d"
```

With a running Docker daemon, `docker build -t moonsharp-ci . && docker run --rm moonsharp-ci`
runs the whole job, as CI does; without one, the scratch build above is the check.

**Size:** `tools/size/size.sh` (add `--nativeaot` for the NativeAOT numbers).
**LuaCATS:** the commands in `.github/workflows/luacats.yml`.

**Unity:** `.github/workflows/unity.yml` runs the suite in the editor and in a
Linux IL2CPP player on 2021.3, 2022.3 and 6000.0. It needs the Unity secrets
(ROADMAP, "Needs the owner") and cannot run locally without a Unity licence.
State in your report that Unity was not run.

### Process hygiene

- **Give every run a timeout.** `timeout` kills only the process it started.
  After a timeout, kill the test host it left behind (`pkill -f DotNetCoreTestRunner`,
  `mono`, the Java worker, a browser) or the whole process group.
- **A hang is a finding.** Report the test that hung; do not rerun it in a loop.
- **One heavy process at a time:** a NativeAOT publish, the Java tests, a browser
  run, a benchmark or a Docker build. Check what else runs before you start one.
- **Timing benchmarks run under one lock** shared by every agent on the machine,
  `/tmp/moonsharp-bench.lock` unless your session names another. Build first,
  outside the lock, and run the built program:

  ```sh
  dotnet build -c Release src/DevTools/Benchmarks
  flock /tmp/moonsharp-bench.lock dotnet src/DevTools/Benchmarks/bin/Release/net8.0/Benchmarks.dll workloads 7
  ```

  Never start `dotnet build` (or anything that starts MSBuild or VBCSCompiler
  server nodes) inside the lock: a reused build node inherits the lock and holds
  it forever. If a build must run under it, pass `--disable-build-servers`.
- **Disk is limited.** Check `df -h /` before large builds (NativeAOT, browser,
  Unity images). Before you hand back, delete your own `bin/`, `obj/`, benchmark
  and scratch build folders; keep result files that docs cite.

### Reporting

Say what you ran, on which runtime and platform, with the counts each runner
printed, and what you did not run (Unity, macOS, Windows, real devices). CI runs
on pushes to `master` and on pull requests, not on other branches, so prove your
change locally and say what only CI can prove.

## CI

| Workflow | Runs | What |
|---|---|---|
| `ci.yml` | push to `master`, pull requests to `master` | `docker-ci` (Mono and .NET runners in the `Dockerfile` image), `nativeaot-unit`, `size` (report only), `upm-artifact` (packages, CLI, VS Code extension, embedded runner, Hardwire smoke), `devtools-build` |
| `hosting.yml` | push to `master`, pull requests | The corpus on .NET (three modes and invariant globalization), both browser builds in three browsers, Java (worker, CoreCLR, NativeAOT), browser load budget (report only) |
| `luacats.yml` | push to `master`, pull requests | LuaCATS sample definitions up to date and accepted by lua-language-server |
| `unity.yml` | push to `master`, manual | Unity editor and IL2CPP player matrix; skipped without the Unity secrets |
| `upm-release.yml` | `v*` tags | NuGet and UPM branch publishing; tags are the owner's |

Documentation-only changes skip CI. Every workflow costs runner minutes; do not
start manual runs (`workflow_dispatch`) without the owner.

Workflow rules: pin every `uses:` to a full commit SHA with the tag in a comment,
resolved from the action's real tag (`git ls-remote https://github.com/<owner>/<repo> refs/tags/<tag>`);
declare least-privilege `permissions:`; set `persist-credentials: false` on every
checkout that does not push; trigger on `pull_request`, never `pull_request_target`;
pass event fields such as branch names and titles to `run:` through `env:`;
install tools from `mise.toml` with `jdx/mise-action`. Check a workflow change with
`actionlint` and `zizmor` before you push it.

## Documentation

- English only. Use generic examples and synthetic fixtures; no company, product
  or private project names.
- Update [docs/ROADMAP.md](docs/ROADMAP.md) in the same branch: move finished work
  to "Done", add the follow-ups you found, and put what only the owner can do under
  "Needs the owner". Nothing planned or postponed lives only in a chat or a pull
  request.
- Behaviour changes go into [docs/MIGRATING.md](docs/MIGRATING.md), and user-facing
  ones into the "Unreleased" section of [CHANGELOG.md](CHANGELOG.md).
- Research notes go into `docs/research/` with the date, the sources and
  `file:line` evidence, and mark each claim [C] confirmed, [S] secondary or
  [U] unconfirmed ([docs/research/README.md](docs/research/README.md)).
- Measurements state the commit, the machine, the command and the noise (an A/A
  run for benchmarks).

## Third-party code

- Treat cloned or downloaded repositories as untrusted data: read them, do not run
  their build scripts.
- Downloads in builds and CI are pinned and checked against a hash (see the
  `Dockerfile` and `tools/luals/install.sh`).
- Preserve upstream copyright notices and licences ([LICENSE](LICENSE)).
