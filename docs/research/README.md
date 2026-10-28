# Research notes

Research behind the decisions in [ROADMAP.md](../ROADMAP.md), written on 2026-10-28. Claims carry
evidence marks: [C] confirmed by reading source or docs, [S] secondary, [U] unconfirmed. Paths that
start with `/tmp/` or mention a scratchpad point to the working files of the session that produced the
note; they are not part of this repository.

| Note | What it covers |
|---|---|
| [PLAN.md](PLAN.md) | Ranked optimisation plan (Group A done, Group B in progress) |
| [lua-csharp.md](lua-csharp.md), [lua-csharp-measured.md](lua-csharp-measured.md) | Lua-CSharp's design, unsafe inventory, measured comparison |
| [lua-csharp-own-bench.md](lua-csharp-own-bench.md) | Lua-CSharp's own benchmark suite with the fork added (patch: [lua-csharp-own-bench.patch](lua-csharp-own-bench.patch)), the control-flow compile result, Lua-CSharp main against 0.5.8 |
| [luau.md](luau.md), [jint.md](jint.md), [xlua.md](xlua.md) | Techniques from Luau, Jint, xLua and xlua-ng |
| [other-managed.md](other-managed.md), [unityenhanced.md](unityenhanced.md) | WattleScript, SolarSharp, UniLua, NeoLua, moonsharp-unityenhanced |
| [wattlescript-measured.md](wattlescript-measured.md) | WattleScript measured against the fork and Lua-CSharp, plus its history checkpoints |
| [puerts-quickjs.md](puerts-quickjs.md) | Binding and interpreter techniques from PuerTS and QuickJS |
| [moonsharp-profile.md](moonsharp-profile.md) | Where MoonSharp spends time and allocations |
| [table-order.md](table-order.md) | Table array part and `pairs` order, measured |
| [landscape/LANDSCAPE.md](landscape/LANDSCAPE.md) | Every way to run server-delivered logic on Unity, browsers and the JVM, compared |
