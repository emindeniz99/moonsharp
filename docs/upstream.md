# Fork and upstream

This repository is a GitHub fork of
[moonsharp-devs/moonsharp](https://github.com/moonsharp-devs/moonsharp). It
keeps MoonSharp's namespace, public API, package id (`org.moonsharp.moonsharp`)
and licence (BSD-3-Clause). Generic fixes go upstream as small pull requests;
everything else stays in clearly separate directories.

## Base

| | Commit | Date |
|---|---|---|
| Fork base (`upstream/master`) | `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` | 2026-10-14 |
| Latest upstream release tag `v3.0.0-beta.1` | `7b2e3e4b981ebae1e78a1c3c2e936d461d1ef105` | 2026-03-03 |

At the base, `Script.VERSION` is `3.0.0.0` and `Script.LUA_VERSION` is `5.2`.
MoonSharp 3 is not Lua 5.3: numbers are doubles and there is no integer subtype.

## Remotes

```sh
git remote add upstream https://github.com/moonsharp-devs/moonsharp.git
git fetch upstream
git log --oneline master..upstream/master   # upstream commits not in the fork yet
```

Bring upstream in with a merge commit; never rebase or force-push `master`.

## Rules for changes

- One reason per branch and pull request; Conventional Commits with a
  kebab-case scope (`fix(vm): ...`, `feat(hosting): ...`).
- A behaviour fix carries a test that fails before it.
- A performance change carries before/after numbers from
  `src/DevTools/Benchmarks` (`ab` mode interleaves the two builds).
- Fork-only code lives in `src/Hosting` and `docs/hosting.md`. Upstream pull
  requests never include it.
- Public commits and pull requests use generic examples and synthetic
  fixtures only.

## Divergence from upstream

| Change | Fork PR | In fork `master` | Upstream candidate | Upstream reference |
|---|---|---|---|---|
| Keep a Script usable after failed calls and stack overflow | [#1](https://github.com/emindeniz99/moonsharp/pull/1) | merged | yes | [#221](https://github.com/moonsharp-devs/moonsharp/issues/221) |
| Benchmarks (lifecycle, hot calls, A/B builds, native-Lua workloads) | [#2](https://github.com/emindeniz99/moonsharp/pull/2) | merged | yes | |
| Allocate VM stacks on demand | [#3](https://github.com/emindeniz99/moonsharp/pull/3) | merged | yes | [#304](https://github.com/moonsharp-devs/moonsharp/issues/304) |
| JSON numbers, string case and string order independent of culture | [#4](https://github.com/emindeniz99/moonsharp/pull/4) | merged | yes (the order change needs a maintainer decision) | none yet |
| Hosting: JSON host, browser, Java worker, fixtures | [#5](https://github.com/emindeniz99/moonsharp/pull/5) | open | no (fork-only) | |
| Keep source locations in binary dumps | [#6](https://github.com/emindeniz99/moonsharp/pull/6) | merged | yes | [#348](https://github.com/moonsharp-devs/moonsharp/issues/348) |
| Baseline, upstream tracking, comparison with native Lua | [#7](https://github.com/emindeniz99/moonsharp/pull/7) | merged | no (fork docs) | |
| `io` on browser WebAssembly, trim descriptor, `io.read("*n")` culture, remove unused `ExecutionState` | [#8](https://github.com/emindeniz99/moonsharp/pull/8) | merged | yes | none yet |
| Exact 64-bit integers (`int64`/`uint64`, CLR `long`/`ulong`, JSON) | branch `feat/exact-int64` | merged | needs an upstream design discussion first | [#300](https://github.com/moonsharp-devs/moonsharp/issues/300) |

Planned work and open decisions: [ROADMAP.md](ROADMAP.md).

Every open upstream issue and pull request, and what the fork does about it:
[upstream-issues.md](upstream-issues.md).

## Upstream issues found here, not yet fixed

- A `Script` keeps every chunk it compiles. Compiling repeatedly into one
  long-lived `Script` grows memory without bound.

Fixed in the fork by [#8](https://github.com/emindeniz99/moonsharp/pull/8),
not yet reported upstream: the `io` module failing on browser WebAssembly,
the standard library disappearing from trimmed apps, culture-dependent
`io.read("*n")`, and the unused `ExecutionState`.

## Related upstream work

- [#344](https://github.com/moonsharp-devs/moonsharp/pull/344) generic CLR
  methods: open; maintainers prefer explicit wrappers. Not duplicated here.
- [#300](https://github.com/moonsharp-devs/moonsharp/issues/300) integers and
  [#334](https://github.com/moonsharp-devs/moonsharp/pull/334) bitwise
  operators: #334 targets `legacy/2.0`. A native integer subtype is a VM
  change and needs an upstream design discussion first.
- The `luau` branch uses dump format version `0x152`, which [fork PR #6](https://github.com/emindeniz99/moonsharp/pull/6) also uses.
