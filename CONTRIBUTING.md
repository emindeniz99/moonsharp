# Contributing

This is a fork of [MoonSharp](https://github.com/moonsharp-devs/moonsharp) that
aims at a fast, fully managed Lua 5.2 engine for Unity, .NET, browsers and the JVM.
[docs/ROADMAP.md](docs/ROADMAP.md) lists the decisions, the work in progress and
what comes next. Keep MoonSharp's public API unless a change documents its
migration, and keep upstream copyright notices and the [licence](LICENSE).

Coding agents: read [AGENTS.md](AGENTS.md) as well. It has the full test commands
and the rules agents follow.

## Setup

1. Install [mise](https://mise.jdx.dev/) and run `mise install` in the repository
   root. It installs the pinned .NET SDK with the .NET 8 runtime, Java 17, Maven,
   Node and CMake listed in [mise.toml](mise.toml).
2. Install from your distribution what mise does not manage: clang and
   `zlib1g-dev` for NativeAOT builds, and Mono for the `net45` test runner.
3. For the browser package, install the WebAssembly workload:
   `dotnet workload install wasm-tools --version 10.0.402`.

Run the commands below in a shell where mise is active, or prefix them with
`mise exec --`.

## Build and test

```sh
dotnet build -c Release src/TestRunners/DotNetCoreTestRunner
cd src/TestRunners/DotNetCoreTestRunner/bin/Release/net8.0
dotnet DotNetCoreTestRunner.dll /unit
```

The runner reads its Lua test files relative to the working directory, so run it
from its output folder. Interpreter changes also need the Mono, embedded and
NativeAOT runners and the conformance corpus in `src/Hosting/fixtures`; hosting
changes need the browser and Java tests. [AGENTS.md](AGENTS.md#tests) lists the
commands for each. The Unity editor and IL2CPP runs happen in CI only.

## Changes

- One reason per branch. Branch from `next`; finished work collects there and
  lands on `master` in batches.
- A bug fix comes with a test that fails without the fix.
- A performance change comes with before and after numbers from
  `src/DevTools/Benchmarks` (its `ab` mode compares two builds in one process).
- Report the size of `MoonSharp.Interpreter.dll` (`tools/size/size.sh`) when you
  change the interpreter. Size never blocks a change, but it stays visible.
- A change that scripts or hosts can observe goes into
  [docs/MIGRATING.md](docs/MIGRATING.md), and a user-facing one into
  [CHANGELOG.md](CHANGELOG.md), in the same branch.
- Code that Unity compiles stays within C# 9 and builds for `netstandard2.0` and
  `net45`. No `unsafe` code.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) with a
required scope: `fix(vm): ...`, `feat(hosting): ...`, `docs(repo): ...`. The scopes
are listed in [AGENTS.md](AGENTS.md#git). Write the subject in the imperative,
and use the body to explain why, with the measurements or the issue behind the
change. Commits written with an AI assistant end with a `Co-Authored-By:` trailer
that names the model.

## Pull requests

Pull requests are welcome against `next`. Describe the problem, the change, how
you tested it (runners, platforms, counts) and what you could not test. The
hosting and LuaCATS workflows run on every pull request, the main CI workflow on
pull requests to `master`, and Unity only on `master`, so run the tests locally
first. Maintainers merge with a merge commit and never squash.

Upstream fixes that also apply to MoonSharp itself are reported upstream by the
maintainer ([docs/upstream.md](docs/upstream.md)).

## Language

Issues, pull requests, code comments and documentation are in English.

## Security

Report vulnerabilities privately; see [SECURITY.md](SECURITY.md).
