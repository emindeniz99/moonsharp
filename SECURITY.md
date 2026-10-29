# Security policy

## Supported versions

This fork has no release yet. Fixes land on `master`; only `master` is supported.
For MoonSharp's own releases, see
[moonsharp-devs/moonsharp](https://github.com/moonsharp-devs/moonsharp).

## Reporting a vulnerability

Report security problems privately through GitHub's
[private vulnerability reporting](https://github.com/emindeniz99/moonsharp/security/advisories/new),
not in a public issue. Include:

- the commit you tested;
- the runtime (.NET version, Mono, Unity version and scripting backend, browser,
  JVM) and the platform;
- the host setup that matters: the `CoreModules` the `Script` gets, the types it
  registers, and the hosting limits if you use `src/Hosting`;
- a script or steps that reproduce the problem.

## Scope

- A script runs with what the host grants it. The modules a `Script` gets
  (`CoreModules`), the CLR types the host registers and the per-call instruction
  and time limits of the hosting contract ([docs/hosting.md](docs/hosting.md))
  set what a script can reach and how long it can run.
- In scope: a script that reaches something its host did not grant, that ends or
  corrupts the host process instead of raising a Lua error, or that gets past the
  hosting limits; and flaws in the hosting transports (JSON contract, JNI shim,
  browser worker).
- Out of scope: what a host exposes on purpose. A script that can call a
  registered type, or that has the `io`, `os` or `load` modules, can do whatever
  those allow. A memory limit per script does not exist yet
  ([docs/ROADMAP.md](docs/ROADMAP.md)), so unbounded allocation is a known gap
  rather than a new finding.
