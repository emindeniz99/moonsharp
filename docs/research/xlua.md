# xLua and xLua-NG: the C#/Lua bridge, read for MoonSharp's CLR interop

Date: 2026-10-28. Reviewer: research subagent. Read-only: nothing in either
tree was built or run, and nothing native was measured. All timings below
that are not marked as quoted from xlua-ng's own documents are estimates.

Repositories and commits read:

| Short | Repository | Commit | Where read |
|---|---|---|---|
| `XL/` | https://github.com/Tencent/xLua | `59bf42685dbe36fe0e1678a6ba8597f859ef7ca3` (2025-11-21, "修复例子在Unity6000的打包错误 (#1208)") | shallow clone (not kept) |
| `NG/` | https://github.com/emindeniz99/xlua-ng | `3042ef63dae56543ad4e43105170f52123e284a2` (on `origin/main`) | local clone |
| `MS/` | https://github.com/moonsharp-devs/moonsharp | `fbd03678d5c0f731d731d099211c7f4a3a1ae8b7` (upstream master, identical to the fork's `master`) | the fork's checkout |

Permalink bases:

- `XL/` = `https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/`
- `NG/` = `https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/`
- `MS/` = `https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/`

Evidence marks: **[C]** confirmed by reading the source at that commit;
**[S]** secondary (a design document, a commit message, a measurement
someone else reported); **[U]** unconfirmed estimate or inference.

## 0. Summary

xLua is a native Lua with a C# bridge, so half of what it does exists only
because Lua cannot hold a managed reference (the object pool, the
finalizer queue, `RawObject`, struct packing into userdata memory). Those
parts do not transfer. What transfers is how xLua avoids work **per call**:

1. **Members are resolved once per type, then a call is a table hit.**
   Each type gets a metatable whose `__index` is a C closure over a
   `methods` table and a `getters` table. A method is stored once per type
   and takes `self` as argument 1, so `obj:Foo()` allocates nothing to look
   `Foo` up. MoonSharp allocates a fresh bound callback (closure, `Func`,
   `CallbackFunction`, `DynValue`) on **every** `obj:Foo()`.
2. **Conversion code is chosen when the wrapper is built, not per call.**
   Reflection wrappers keep a checker and a caster delegate per parameter;
   generated wrappers inline `(int)LuaAPI.xlua_tointeger(L, 2)`. MoonSharp
   re-dispatches on the target `Type` for every argument of every call
   (custom-converter dictionary, `Nullable.GetUnderlyingType`, a `HashSet`
   of numeric types, `Convert.ToXxx` in a `try`/`catch`, boxing).
3. **Generated code is typed end to end.** No `object[]`, no boxing of
   primitives, the overload chosen by inline count/type checks. MoonSharp's
   Hardwire generator removes only the `MethodInfo.Invoke`: it still builds
   `object[]`, boxes, and converts through the same reflection-era paths.
4. **Caches keyed by `Type`** for everything that is costly to compute:
   type id per `Type`, delegate creator per delegate type, method wrapper
   per (type, name), plus a per-Lua-function bridge cache so converting the
   same function to a delegate twice returns the same delegate.

xlua-ng adds what MoonSharp most needs to copy as a process:

- a Roslyn **incremental** source generator with value-equatable models,
  per-member reflection fallback and per-assembly registration;
- **best-fit overload selection with integer "kinds" precomputed** per
  parameter and a build-time "can these overloads ever be ambiguous"
  analysis, so unambiguous groups pay nothing;
- **binding-mode parity tests** that run the same Lua against reflection,
  emitted and generated wrappers and compare results and error texts;
- a measured **descriptor mode** (one `switch` per type instead of one method
  per member) that cuts IL2CPP size by about 8% against its smallest
  previous mode.

Four MoonSharp findings came out of the comparison that are bugs or
near-bugs rather than missing optimisations (section 5): userdata and
coroutine keys all hash to `999` in tables (O(n) lookups);
`CallbackFunction.FromDelegate` builds a new `MethodMemberDescriptor` (and,
in the default access mode, a new compiled expression tree on first call)
for every conversion of a delegate; the overload cache never clears
`ArgsUserDataType` when it reuses a slot, so after more than five distinct
argument signatures a cached entry can match on stale userdata types and
call an overload chosen for different arguments; and the optimised
(expression-tree) path for instance methods on value types very likely
calls the method on an unboxed copy, so mutations are lost where the
reflection path keeps them.

## 1. Licences

| Item | Licence | Use in BSD-3-Clause MoonSharp |
|---|---|---|
| xLua C# and C sources | MIT, "Copyright (C) 2016 THL A29 Limited, a Tencent company" (`XL/LICENSE.TXT`, each file's header, e.g. [XL/Assets/XLua/Src/ObjectPool.cs#L1-L7](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectPool.cs#L1-L7)). SPDX `MIT`. Third-party parts listed in the same file: android-cmake (BSD-3-Clause), Lua (MIT), others. [C] | Ideas: free. Code: may be copied with the MIT notice kept (file header plus a THIRD-PARTY-NOTICES entry). |
| xlua-ng | Same `LICENSE.TXT` as xLua (MIT, Tencent copyright), new files carry the Tencent MIT header too (e.g. [NG/Assets/XLua/Core/LuaValue.cs#L1-L7](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaValue.cs#L1-L7)). [C] | Same as xLua. xlua-ng is the same owner's project, so attribution is easy, but the MIT notice still applies to copied code. |
| xlua-ng source generator (`General/SourceGenerator`) | Same repository licence (MIT). [C] | Same. |

Nothing below proposes copying files. Every recommendation is a
MoonSharp-native reimplementation of an idea, so no notice is required; if
a later change copies a helper verbatim (for example the overload "kind"
table or `EquatableArray`), keep the MIT header in that file.

## 2. How xLua's bridge works (code-level)

### 2.1 C# objects in Lua: handle pool, reverse map, userdata cache

- **Handles.** A C# object becomes a 4-byte full userdata holding an `int`
  index into `ObjectPool`, a free-list slab of `struct Slot { int next;
  object obj; }` (initial capacity 512, doubled on growth, freed slots
  chained through `next`) [C]
  [XL/Assets/XLua/Src/ObjectPool.cs#L15-L135](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectPool.cs#L15-L135).
  The pool exists because Lua cannot store a managed reference.
- **Reverse map and userdata cache.** `Push(object)` first looks the object
  up in `reverseMap` (reference equality, see `ReferenceEqualsComparer`) or,
  for enums, `enumMap`; if found it fetches the **same** userdata from a weak
  cache table in the registry (`xlua_tryget_cachedud`), so one C# object is
  one Lua userdata and Lua's `rawequal`/table keys work by identity. Value
  types are not cached. [C]
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L1247-L1289](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1247-L1289),
  [XL/build/xlua.c#L338-L364](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/build/xlua.c#L338-L364).
- **Collection.** The userdata's `__gc` calls `collectObject`, which frees the
  slot and removes the reverse-map entry only if it still points at that
  slot (the comment explains the weak-table-before-`__gc` race) [C]
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L784-L826](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L784-L826).
  `LuaEnv.Tick` also sweeps up to 20 slots per tick for destroyed
  `UnityEngine.Object`s (`ObjectPool.Check`) [C]
  [XL/Assets/XLua/Src/LuaEnv.cs#L325-L360](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/LuaEnv.cs#L325-L360).
- **Reading self.** Generated code uses `xlua_tocsobj_fast` (read the int
  from the userdata block, no metatable check) and indexes the pool [C]
  [XL/build/xlua.c#L46-L67](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/build/xlua.c#L46-L67).
  xlua-ng found that this read any light userdata's pointee and fixed it
  (class (c) item in [NG/docs/design/bridge-hardening.md#L1-L90](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/design/bridge-hardening.md#L1-L90)). [S]

**Relevance to MoonSharp:** the handle pool is not needed (MoonSharp's
`UserData` holds the reference). The *identity* consequence matters: see
idea 3 (hashing) and idea 9 (a value cache for immutable values such as
enums).

### 2.2 Types: one metatable per type, members in plain tables

- `getTypeId` resolves a `Type` to a registry reference of its metatable
  once and caches it in `typeIdMap` (`Dictionary<Type,int>`); arrays and
  delegates share common metatables [C]
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L1027-L1101](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1027-L1101).
- The metatable's `__index` is a C closure with upvalues
  `methods, getters, csindexer, base, indexfuncs, arrayindexer, baseindex`.
  A lookup is: `methods[key]` (return the function), else `getters[key]`
  (call it with `obj`), else array or C# indexer, else walk to the base
  type's indexer, **found once and cached in upvalue 7** [C]
  [XL/build/xlua.c#L389-L466](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/build/xlua.c#L389-L466),
  built in [XL/Assets/XLua/Src/Utils.cs#L1049-L1160](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/Utils.cs#L1049-L1160).
- Method functions in `methods` are **unbound**: `self` is argument 1. So
  `obj:Foo(x)` is a table hit plus a call; nothing is allocated for the
  lookup. [C]

### 2.3 Values across the boundary without boxing

- **Typed push/get delegates.** `PushByType<T>(L, T v)` and `Get<T>(L, idx,
  out T v)` look up an `Action<IntPtr,T>` / `Func<IntPtr,int,T>` in a
  `Dictionary<Type,Delegate>` keyed by `typeof(T)` and call it: no box for
  `int`, `double`, `bool`, `long`, etc. [C]
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L912-L937](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L912-L937),
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L1525-L1607](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1525-L1607).
  `RegisterPushAndGetAndUpdate<T>` lets generated code add typed entries for
  user structs (same lines, L1612-L1641).
- **Untyped path.** `PushAny(object)` tests `IsPrimitive` and does an `is`
  chain (`pushPrimitive`) [C]
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L1103-L1196](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1103-L1196).
  It is used only when the static type is unknown.
- **Structs (GCOptimize).** A configured struct is packed field by field into
  the userdata's memory (`xlua_pushstruct` + `CopyByValue.Pack`), so a
  `Vector3` crossing the boundary allocates no managed box; generated code
  writes a mutated struct back (`Update`) after an instance call [C]
  [XL/build/xlua.c#L831-L838](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/build/xlua.c#L831-L838),
  [XL/Assets/XLua/Src/CopyByValue.cs#L23-L80](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/CopyByValue.cs#L23-L80),
  `decimal` as the built-in example
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L1663-L1709](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1663-L1709).
  This relies on native memory; MoonSharp has no equivalent need, but the
  write-back rule is relevant (idea 12).
- **int64.** `long`/`ulong` go through `lua_pushint64`/`lua_toint64`
  (Lua 5.3 integers, or an int64 userdata on 5.1/LuaJIT via `i64lib.c`):
  lossless. MoonSharp is a Lua 5.2 double-only VM; `long` becomes a
  `double` through `Convert.ToDouble` and back through `Convert.ToInt64`
  [MS/src/MoonSharp.Interpreter/Interop/Converters/NumericConversions.cs#L42-L93](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/Converters/NumericConversions.cs#L42-L93),
  which loses values above 2^53. The fork's `feat/exact-int64` work is the
  place for this; the typed converters of idea 5 should be written so an
  exact-int64 path can slot in. [C]

### 2.4 Reflection wrappers (xLua's fallback when nothing is generated)

- `OverloadMethodWrap.Init` precomputes, per parameter: a **checker**
  delegate and a **caster** delegate taken from per-`Type` maps, the in/out
  positions, optional-default values, `params` element type, and
  `luaStackPosStart`. It also allocates **one `object[] args` per overload,
  reused on every call** and cleared in `finally` [C]
  [XL/Assets/XLua/Src/MethodWarpsCache.cs#L25-L146](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/MethodWarpsCache.cs#L25-L146),
  checker/caster maps
  [XL/Assets/XLua/Src/ObjectCasters.cs#L35-L58](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectCasters.cs#L35-L58),
  [XL/Assets/XLua/Src/ObjectCasters.cs#L223-L246](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectCasters.cs#L223-L246).
  Reusing `args` survives recursion only because `MethodBase.Invoke` copies
  the arguments into the callee frame before the callee runs, and the
  out/ref results are read back before any later call can overwrite them
  [U: inferred from the order of operations in `Call`, L207-L267]. A
  MoonSharp port should not rely on that subtlety; with typed descriptors
  (idea 6) there is no array at all.
- `MethodWrap.Call`: one overload without defaults skips the checks;
  otherwise the **first** overload whose `Check` passes is called (no
  ranking) [C]
  [XL/Assets/XLua/Src/MethodWarpsCache.cs#L293-L317](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/MethodWarpsCache.cs#L293-L317).
- `MethodWrapsCache` caches the built wrapper per `(Type, name)`, per
  constructor type and per delegate type [C]
  [XL/Assets/XLua/Src/MethodWarpsCache.cs#L320-L452](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/MethodWarpsCache.cs#L320-L452).
- Field getters/setters are closures over `FieldInfo.GetValue/SetValue`
  plus `PushAny` (boxing) [C]
  [XL/Assets/XLua/Src/Utils.cs#L136-L205](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/Utils.cs#L136-L205).
  xLua's reflection path is slow too (xlua-ng measures about 400 ns per
  call against about 55 ns generated, section 3.2); its value for MoonSharp
  is the *build once, call many* structure, not the speed.

### 2.5 Generated wrappers (xLua's fast path)

The template writes one static `int _m_Foo(IntPtr L)` per method group:
read `gen_param_count = lua_gettop(L)`, then per overload an `if` with the
count and an inline typed check per argument, then typed casts
(`int x = LuaAPI.xlua_tointeger(L, 2);`), a **direct call**, and a typed
push of the result [C]
[XL/Assets/XLua/Src/Editor/Template/LuaClassWrap.tpl.txt#L371-L450](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/Editor/Template/LuaClassWrap.tpl.txt#L371-L450).
The cast table per CLR type is in
[XL/Assets/XLua/Src/Editor/Template/TemplateCommon.lua.txt#L79-L95](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/Editor/Template/TemplateCommon.lua.txt#L79-L95).
No `object[]`, no boxing for primitives, no reflection. This is the shape
MoonSharp's generated descriptors should have (idea 6).

### 2.6 C# calling Lua: delegate bridges and their caches

- **Per Lua function, one bridge.** `CreateDelegateBridge` keys the registry
  by the function value itself: the first conversion stores
  `registry[func] = ref` and `delegate_bridges[ref] = WeakReference(bridge)`;
  later conversions of the same function find the same bridge [C]
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L471-L550](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L471-L550).
- **Per bridge, one delegate per delegate type**, with a one-entry fast slot
  before a dictionary (`firstKey`/`firstValue`, then `bindTo`) [C]
  [XL/Assets/XLua/Src/DelegateBridge.cs#L25-L85](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/DelegateBridge.cs#L25-L85).
  Consequence: `obj.Event += f` then `obj.Event -= f` work because the same
  delegate instance comes back.
- **Per delegate type, one creator**, cached in `delegateCreatorCache`:
  the generated `__Gen_Delegate_Imp*` method with a matching signature,
  found once by reflection, or a generic `Action<T..>`/`Func<T..,R>` method
  closed with `MakeGenericMethod` [C]
  [XL/Assets/XLua/Src/ObjectTranslator.cs#L349-L469](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L349-L469).
  **AOT rule:** outside the general (non-Unity) build, the generic fallback
  refuses value-type parameters, value-type returns and more than four
  parameters (`(x) => null`), because IL2CPP may not have code for those
  generic instantiations; reference-type instantiations share code [C]
  (L376-L392 of the same file). The generic bodies push with
  `PushByType<T>`
  [XL/Assets/XLua/Src/GenericDelegateBridge.cs#L31-L89](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/GenericDelegateBridge.cs#L31-L89).
- **`LuaFunction.Action<T>`/`Func<T,TResult>`**: typed, "no GC" call helpers
  (`PushByType` for arguments, `Get<T>` for the result) next to a
  deprecated `Call(params object[])` [C]
  [XL/Assets/XLua/Src/LuaFunction.cs#L30-L184](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/LuaFunction.cs#L30-L184).
- **Lifetime.** `LuaBase` (table, function, bridge) holds a registry ref; its
  finalizer cannot touch Lua off-thread, so it enqueues a `GCAction` and
  `LuaEnv.Tick` releases it on the Lua thread [C]
  [XL/Assets/XLua/Src/LuaBase.cs#L35-L77](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/LuaBase.cs#L35-L77),
  [XL/Assets/XLua/Src/LuaEnv.cs#L447-L461](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/LuaEnv.cs#L447-L461).
  MoonSharp's `Table` and `Closure` are ordinary managed objects, so none of
  this is needed. Not an idea; recorded so nobody ports it.

Relevant upstream history (xLua): a "fix get RealStatePtr performance
issue" change to the delegate and interface bridge templates,
https://github.com/Tencent/xLua/commit/c5a8537772555657ca47b87271593f1334b6f489,
was reverted the same day,
https://github.com/Tencent/xLua/commit/d9191985b0f04b76c87a800ab696269aaa3c448f
[S: commit messages; the revert reason is not stated]. MoonSharp has no
equivalent of the state-pointer-to-translator lookup
(`ObjectTranslatorPool.Find(L)`) because callbacks receive the `Script`.

## 3. What xlua-ng changed that matters here

### 3.1 Best-fit overload selection, cheap

- xLua (and xlua-ng before 2026-10-19) called the **first** matching
  overload, so results depended on declaration order and differed between
  reflection, emitted and generated wrappers (`DoubleInt(3)` called the
  `double` overload, etc.) [S]
  [NG/docs/research/bridge-audit.md#L1397-L1430](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/research/bridge-audit.md#L1397-L1430).
  Fixed in https://github.com/emindeniz99/xlua-ng/commit/380625fe7baad5dc9d4b9d56b8684b8e03c47168
  ("call the overload that fits the arguments best").
- **Precomputed kinds.** `OverloadSignature.Create` stores, per parameter, an
  integer kind (`KindInt32`, `KindInt64`, `KindDouble`, `KindString`,
  `KindObject`, `KindOther`, `| KindNullable`) and the CLR type used for the
  "C# object" cost; the signature has no translator or Lua state, so it is
  **shared process-wide** by every `LuaEnv` [C]
  [NG/Assets/XLua/Src/MethodWarpsCache.cs#L26-L190](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Src/MethodWarpsCache.cs#L26-L190),
  `OverloadScore`
  [NG/Assets/XLua/Src/ObjectCasters.cs#L276-L340](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Src/ObjectCasters.cs#L276-L340).
- **Only ambiguous groups are scored.** `OverloadScore.Needed` (runtime) and
  the generator's `NeedsScore` compare, position by position, which Lua types
  two candidates can both accept; `Foo(int)` next to `Foo(string)` keeps the
  plain first-match code [C]
  [NG/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L1329-L1362](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L1329-L1362).
- **One read per argument.** The number kind of each argument is read once
  for all candidates (`gen_n<i> = -1` until read), and a C# object's check
  and cost are one call (`CheckedArgScore`) [C]
  [NG/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L1410-L1524](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L1410-L1524);
  commits https://github.com/emindeniz99/xlua-ng/commit/fb25a5c84c96afa1050eb72532e7733fbcbdf7cb
  and https://github.com/emindeniz99/xlua-ng/commit/056cb9777d9d9b44047f26d6eeef87e8f3686c0d.
- **Measured** (quoted, macOS arm64, Mono 6.14.1, Lua 5.5.1, ns per call) [S]
  [NG/docs/research/bridge-audit.md#L1615-L1650](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/research/bridge-audit.md#L1615-L1650):
  unambiguous group `overload_disjoint` 392 (reflection) / 152 (emit) /
  55.7 (generated); scored `overload_num_integer` 429 / 77 / 58.7;
  `overload_obj_derived` 666 / 127 / 125.

MoonSharp already does best-fit scoring and has a 5-entry cache of argument
type signatures, so it is ahead of xLua on *correctness*; the lessons are
the precomputed integer kinds and the "skip scoring when disjoint" analysis
(idea 13).

### 3.2 The source generator

Design: [NG/docs/design/source-generator.md](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/design/source-generator.md);
the legacy text-template generator was removed in
https://github.com/emindeniz99/xlua-ng/commit/2351d5b0363b33a0ba8c4ce6b2ed9484f3f5a29a. [C]

- **Pipeline** (Roslyn 4.3.1 only, the version Unity 2021.3 ships):
  `ForAttributeWithMetadataName` per configuration attribute, entries merged
  into an `EquatableArray`, one value-equatable `TypeModel` per bound type
  (strings and flags, never symbols), one `AddSource` per type, so an edit
  regenerates only the types whose model changed [C]
  [NG/General/SourceGenerator/XLua.SourceGenerator/XLuaBindingGenerator.cs#L19-L66](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/XLuaBindingGenerator.cs#L19-L66);
  rationale in section 7 of the design (the `ImmutableArray` reference
  equality pitfall). Models of types from metadata references are cached in
  a static table [C]
  [NG/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L291-L340](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L291-L340).
- **Emitted method body**: typed casts per argument, direct call, typed push,
  `Update` for structs; checks and casts come from small type-name tables
  [C]
  [NG/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L694-L830](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L694-L830),
  [NG/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L1744-L1838](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/General/SourceGenerator/XLua.SourceGenerator/Wrappers.cs#L1744-L1838).
- **No partial classes of the runtime**; generated code talks to a small,
  versioned public `BindingApi` hidden with `[EditorBrowsable(Never)]`;
  registration through a per-assembly `Bindings.Register()` called from
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` in Unity and from an
  `[assembly: GeneratedBindings]` scan elsewhere [C]
  [NG/Assets/XLua/Src/Binding.cs#L112-L134](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Src/Binding.cs#L112-L134),
  [NG/Assets/XLua/Src/Binding.cs#L317-L420](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Src/Binding.cs#L317-L420),
  [NG/Assets/XLua/Src/Binding.cs#L610-L667](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Src/Binding.cs#L610-L667),
  design sections 3, 5.1, 5.2.
- **Per-member reflection fallback** with a reason code and a diagnostic
  (`XLSG004`) instead of dropping members; members reached by reflection get
  per-member `link.xml` entries instead of `preserve="all"` (design 5.5,
  5.7). [S]
- **Generated vs reflection, measured** (quoted): generated instance call
  83.6 ns, field get 92.4 ns, property get 90.2 ns in a release-style build
  [S] [NG/docs/design/bridge-hardening.md#L72-L90](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/design/bridge-hardening.md#L72-L90);
  reflection about 400 ns per static call in the overload table above.
- **Descriptor mode spike** (not in the product): per type, one descriptor
  string plus one `switch` method that performs the calls on an argument
  buffer, instead of one C# method and one delegate per member. On a
  264-type configuration with IL2CPP: **-8.2 to -8.6% APK delta against GCM
  mode, about 103 bytes of APK less per Lua function**; call cost
  81.5 ns static (against 53.3 generated plain), 103.9 ns instance (against
  84.5) [S]
  [NG/docs/research/descriptor-wrappers.md#L13-L60](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/research/descriptor-wrappers.md#L13-L60),
  [NG/docs/research/descriptor-wrappers.md#L130-L210](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/research/descriptor-wrappers.md#L130-L210).
  MoonSharp's Hardwire emits a **class per member**, which is the most
  IL2CPP-expensive shape of all.
- **Parity across binding modes** is tested: the same probes against
  reflection, CodeEmit, generated and GCM wrappers, with differences fixed
  (for example `self` of the wrong class, readonly fields, event handlers)
  [S]
  [NG/docs/research/binding-mode-parity.md](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/research/binding-mode-parity.md),
  commit https://github.com/emindeniz99/xlua-ng/commit/f3ae62f888f2ae633ebf34115eab3b6cffb391cd.

### 3.3 Allocation-free host calls and handles (`XLua.Core`)

- `delegate void LuaCallback(ref LuaCall call)`: a host function gets a
  `ref struct` view of its frame with typed readers (`GetLong`, `GetBool`,
  `ArgType`) and writers; reading a primitive allocates nothing [C]
  [NG/Assets/XLua/Core/LuaCall.cs#L26-L32](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaCall.cs#L26-L32),
  [NG/Assets/XLua/Core/LuaCall.cs#L735-L830](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaCall.cs#L735-L830).
  It is `unsafe` (pointer into a native frame); the managed analogue is a
  `ref struct` over MoonSharp's value stack (idea 11).
- `readonly struct LuaValue` with an implicit conversion per integral type
  (so `ulong` does not go through `double`) and handle values `(slot,
  generation)` that detect use-after-dispose without finalizers [C]
  [NG/Assets/XLua/Core/LuaValue.cs#L36-L80](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaValue.cs#L36-L80),
  [NG/Assets/XLua/Core/LuaFunctionRef.cs#L14-L60](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaFunctionRef.cs#L14-L60).
  For MoonSharp, the transferable part is the "one implicit conversion per
  integral type" rule for any typed call API (idea 7).

### 3.4 Debug-only checks follow the build type

Checks that only catch script mistakes (wrong `self`, foreign userdata) are
compiled in for Editor/Development builds and out of release players, each
costing 10-14 ns per call [S]
[NG/docs/design/bridge-hardening.md#L1-L60](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/docs/design/bridge-hardening.md#L1-L60).
MoonSharp's `CheckAccess` per call is cheap, but the same principle applies
to any debug-only validation added by the ideas below.

## 4. MoonSharp's interop today: where a call spends its time

Path of `obj:Add(i)` on a reflected type, single overload, default
`LazyOptimized` access mode (desktop) or forced `Reflection` (AOT):

1. **Lookup** (`IndexN "Add"`, emitted for the colon call: `Copy; IndexN;
   Swap; args; ThisCall`
   [MS/src/MoonSharp.Interpreter/Tree/Expressions/FunctionCallExpression.cs#L75-L92](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Tree/Expressions/FunctionCallExpression.cs#L75-L92)).
   `ExecIndex` calls `ud.Descriptor.Index(script, ud.Object, idx, true)`
   [MS/.../Processor_InstructionLoop.cs#L1338-L1346](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L1338-L1346)
   → `TryIndex` (a `Dictionary<string,…>` lookup; .NET does not cache string
   hashes) → `desc.GetValue(script, obj)`
   [MS/.../DispatchingUserDataDescriptor.cs#L218-L315](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs#L218-L315)
   → `GetCallbackAsDynValue` → **new closure + new `Func<…>` + new
   `CallbackFunction` + new `DynValue`**, every call
   [MS/.../FunctionMemberDescriptorBase.cs#L103-L106](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/MemberDescriptors/FunctionMemberDescriptorBase.cs#L103-L106),
   [MS/.../FunctionMemberDescriptorBase.cs#L288-L292](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/MemberDescriptors/FunctionMemberDescriptorBase.cs#L288-L292);
   overloaded groups do the same
   [MS/.../OverloadedMethodMemberDescriptor.cs#L423-L474](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs#L423-L474). [C]
2. **Call** (`ThisCall`): `CreateArgsListForFunctionCall` allocates a
   `Slice<DynValue>` (or a `List` when the last argument is a tuple), then a
   `CallStackItem` (class), a `ScriptExecutionContext`, and in
   `CallbackFunction.Invoke` a `CallbackArguments` [C]
   [MS/.../Processor_InstructionLoop.cs#L609-L631](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L609-L631),
   [MS/.../Processor_InstructionLoop.cs#L702-L739](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Processor/Processor_InstructionLoop.cs#L702-L739),
   [MS/src/MoonSharp.Interpreter/DataTypes/CallbackFunction.cs#L46-L59](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/CallbackFunction.cs#L46-L59).
3. **Arguments**: `BuildArgumentList` allocates `object[]`; per argument
   `DynValueToObjectOfType` does a custom-converter dictionary lookup,
   `Nullable.GetUnderlyingType`, `NumericTypes.Contains` (a `HashSet<Type>`),
   then `DoubleToType` (`Convert.ToInt32` inside `try`/`catch`, returns a
   boxed value); `params` builds a `List<DynValue>` and an
   `Array.CreateInstance` filled by `SetValue` [C]
   [MS/.../FunctionMemberDescriptorBase.cs#L131-L229](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/MemberDescriptors/FunctionMemberDescriptorBase.cs#L131-L229),
   [MS/.../ScriptToClrConversions.cs#L80-L200](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/Converters/ScriptToClrConversions.cs#L80-L200),
   [MS/.../NumericConversions.cs#L42-L65](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/Converters/NumericConversions.cs#L42-L65).
4. **Invoke**: compiled `Func<object, object[], object>` (expression tree) on
   JIT platforms; `MethodBase.Invoke` whenever `IsRunningOnAOT()`, i.e. on
   every IL2CPP player [C]
   [MS/.../MethodMemberDescriptor.cs#L83](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs#L83),
   [MS/.../MethodMemberDescriptor.cs#L177-L219](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs#L177-L219).
5. **Result**: `ObjectToDynValue` → `TryObjectToSimpleDynValue` (custom
   converter lookup, an `is` chain, `NumericTypes.Contains`,
   `Convert.ToDouble`) → for objects `UserData.Create(obj)` →
   `TypeDescriptorRegistry.GetDescriptorForType` **under a global lock**,
   `ContainsKey` + indexer, and for a type not registered itself a walk of
   base types and `GetInterfaces()` with a new `List` (and possibly a new
   `CompositeUserDataDescriptor`) **on every push** [C]
   [MS/.../ClrToScriptConversions.cs#L45-L150](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/Converters/ClrToScriptConversions.cs#L45-L150),
   [MS/.../TypeDescriptorRegistry.cs#L238-L324](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/UserDataRegistries/TypeDescriptorRegistry.cs#L238-L324),
   [MS/src/MoonSharp.Interpreter/DataTypes/UserData.cs#L208-L234](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/UserData.cs#L208-L234).
6. **Hardwire** removes step 4's reflection only: generated
   `Invoke(Script, object, object[] pars, int argscount)` casts `(T)pars[i]`
   and returns `object`, wrapped by `DynValue.FromObject`; steps 1-3 and 5
   are unchanged [C]
   [MS/.../HardwiredMethodMemberDescriptor.cs#L8-L33](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/HardwiredDescriptors/HardwiredMethodMemberDescriptor.cs#L8-L33),
   [MS/src/MoonSharp.Hardwire/Generators/MethodMemberDescriptorGenerator.cs#L77-L110](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Hardwire/Generators/MethodMemberDescriptorGenerator.cs#L77-L110).
   It also needs a runtime dump step (`PrepareForWiring` fills a Lua table
   that the CodeDOM generator reads) rather than a compile-time generator.

Allocation count for one `o:Add(i)` (`void Add(int)`), from the above:
closure, `Func`, `CallbackFunction`, `DynValue` (lookup); `Slice`,
`CallStackItem`, `ScriptExecutionContext`, `CallbackArguments` (call);
`object[]`, boxed `int` (arguments): about **10 objects, roughly 400-450
bytes per call** on x64 [U: object sizes estimated from field lists, not
measured]. xLua's generated path allocates nothing for the same call;
xLua's reflection path allocates the boxed argument only.

There are **no interop benchmarks** in the fork's harness
(`chore/devtools-benchmarks:src/DevTools/Benchmarks/LuaWorkloads.cs` covers
only pure-Lua workloads) [C]. The measured `method_call` workload (705 ns)
is a Lua-table method, not a CLR call, so none of the ideas below moves the
current baseline table; they need the interop benchmark set of section 7.

## 5. Ideas for MoonSharp, ranked

Ranking weighs impact on interop-heavy Unity code, effort and risk. All are
safe managed C#, AOT/IL2CPP-safe, netstandard2.0-compatible, and keep the
public API unless stated.

### Idea 1. Cache the resolved descriptor per runtime `Type` (high impact for subtypes, small)

- **xLua:** `typeIdMap` resolves a `Type` once
  ([XL/.../ObjectTranslator.cs#L1027-L1101](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/ObjectTranslator.cs#L1027-L1101)).
- **MoonSharp now:** every `UserData.Create(obj)` takes `s_Lock`, does two
  dictionary lookups for a registered type, and for an unregistered subtype
  (a `MonoBehaviour` subclass returned where a base class is registered, a
  class implementing a registered interface) walks base types, calls
  `GetInterfaces()` (array allocation), allocates a `List`, and may allocate
  a `CompositeUserDataDescriptor` **per push** [C] (section 4, step 5).
- **Change:** in `TypeDescriptorRegistry`, add `s_ResolvedCache`
  (`Dictionary<Type, IUserDataDescriptor>`, separate caches for
  `searchInterfaces` true/false) filled after resolution and cleared
  whenever `RegisterType_Impl`/`UnregisterType` or the registration policy
  change the registry (a version counter). Read it with `TryGetValue`.
  Keep the lock (MoonSharp's registry is global and documented as
  thread-safe), or publish an immutable snapshot dictionary for lock-free
  reads; measure both.
- **Files:** `src/MoonSharp.Interpreter/Interop/UserDataRegistries/TypeDescriptorRegistry.cs`,
  `DataTypes/UserData.cs` (`GetDescriptorForObject`).
- **Impact:** pushes of objects whose exact type is not registered: from a
  base-walk plus interface scan plus 1-3 allocations to one lookup; also
  makes `CompositeUserDataDescriptor` stable across pushes. Estimate 100-500
  ns saved per such push on desktop, more on Mono/IL2CPP [U]. Registered
  exact types gain only the second lookup (~10-20 ns) [U].
- **Risk:** stale cache if a type is registered after first use; covered by
  the version counter. `IGeneratorUserDataDescriptor.Generate` results are
  cached too: check that generics descriptors are idempotent (they register
  the generated type, so later lookups would hit the registry anyway) [U].
- **Tests:** a regression test that pushes a derived object N times and
  asserts the descriptor instance is the same; one that registers a type
  after first use and asserts the new descriptor is used.

### Idea 2. Cache `MethodMemberDescriptor` for delegates and `MethodInfo`s (high when it applies, small)

- **xLua:** wrappers are cached per type and name and per delegate type
  ([XL/.../MethodWarpsCache.cs#L320-L452](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/MethodWarpsCache.cs#L320-L452));
  xlua-ng shares `OverloadSignature`s process-wide (section 3.1).
- **MoonSharp now:** `CallbackFunction.FromDelegate` and `FromMethodInfo`
  construct a **new** `MethodMemberDescriptor` per call (reflection over
  parameters, `GetCustomAttributes(typeof(ExtensionAttribute))`), and in the
  default `LazyOptimized` mode each new descriptor compiles a **new
  expression tree** on its first call [C]
  [MS/src/MoonSharp.Interpreter/DataTypes/CallbackFunction.cs#L87-L113](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/CallbackFunction.cs#L87-L113).
  This is reached every time a C# delegate is converted to Lua
  (`ClrToScriptConversions.ObjectToDynValue`, L119 and L127 of
  [MS/.../ClrToScriptConversions.cs](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/Converters/ClrToScriptConversions.cs#L100-L130)),
  e.g. a property returning an `Action`, or `globals["f"] = someDelegate`
  in a loop.
- **Change:** a static cache `MethodBase → MethodMemberDescriptor` per
  access mode (lock or `ConcurrentDictionary`; netstandard2.0 has it). The
  descriptor is stateless apart from its lazily compiled delegate, so it is
  safe to share; the target object stays a per-call argument
  (`GetCallbackFunction(script, del.Target)`).
- **Files:** `DataTypes/CallbackFunction.cs`.
- **Impact:** removes a reflection walk (several µs) and, on JIT platforms,
  an expression-tree compile (tens to hundreds of µs) per delegate
  conversion after the first [U]. Memory: one descriptor per distinct method.
- **Risk:** unbounded cache for code that converts many dynamic methods;
  key on `MethodInfo` only (not target) keeps it bounded by code size.
  A `ConditionalWeakTable<MethodBase, …>` avoids pinning collectible
  assemblies.
- **Tests:** convert the same delegate twice and assert one descriptor
  (internal hook or `PerformanceStatistics.AdaptersCompilation` counter
  increments once).

### Idea 3. Hash userdata and coroutines by identity/value, not `999` (high for userdata-keyed tables, small)

- **xLua:** one Lua userdata per C# object (section 2.1), so Lua's hashing
  of userdata keys is by identity and O(1).
- **MoonSharp now:** `DynValue.GetHashCode` returns the constant `999` for
  `UserData` and `Thread` [C]
  [MS/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L567-L610](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L567-L610).
  `Table` stores non-number, non-string keys in a `Dictionary<DynValue,…>`
  (`m_ValueMap`, [MS/.../Table.cs#L13-L31](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/Table.cs#L13-L31)),
  so a table keyed by N userdata (a `seen[obj] = true` set, an object →
  state map, a set of coroutines) degrades to O(N) per access, O(N²) to
  fill.
- **Change:** for `UserData`, hash `ud.Object?.GetHashCode()` (or the
  descriptor's hash when `Object` is null, i.e. static userdata), matching
  `Equals`, which compares `Descriptor` and `Object.Equals`
  ([DynValue.cs#L654-L671](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/DataTypes/DynValue.cs#L654-L671));
  for `Thread`, `Coroutine.GetHashCode()`. Note that `m_HashCode` is cached
  in the `DynValue`; a boxed mutable struct whose hash changes would break
  the cache, so either skip caching for userdata or document that mutating a
  struct used as a table key is unsupported (the same contract as a .NET
  `Dictionary`).
- **Files:** `DataTypes/DynValue.cs`.
- **Impact:** userdata-keyed table get/set from O(N) to O(1). For 1,000 keys
  roughly a 100x-500x speedup of the lookup [U].
- **Risk:** a user type with a throwing or inconsistent `GetHashCode`
  (rare; `Equals` is already called). Behaviour is otherwise unchanged
  because equality is unchanged.
- **Tests:** a table with 10,000 userdata keys must fill in linear time
  (assert a time ceiling loosely, or count `Equals` calls through a test
  type); equal-but-distinct objects (value equality) still map to one key.

### Idea 4. Stop allocating a bound callback on every `obj:Method()` (high, medium)

- **xLua:** methods live once per type in the `methods` table, unbound,
  `self` is argument 1 (section 2.2).
- **MoonSharp now:** four allocations per lookup (section 4, step 1), and
  the bound `obj` is then used instead of `args[0]` when `IsMethodCall`
  (`j = args.IsMethodCall ? 1 : 0` in `BuildArgumentList`) [C].
- **Change, phase A (no VM change):** cache the bound callback on the
  `UserData` instance: a one- or two-entry cache `(IMemberDescriptor,
  Script) → DynValue` checked in `DispatchingUserDataDescriptor.TryIndex`
  before calling `GetValue` for members that are `FunctionMemberDescriptorBase`
  or `OverloadedMethodMemberDescriptor`. Loops over the same local `o` (the
  common case: `local o = ...; for ... o:Foo() end`) then allocate nothing for
  the lookup. Return a read-only `DynValue` (`AsReadOnly()`), since the VM may
  write into non-readonly values.
- **Change, phase B (VM):** mark `IndexN` instructions emitted for a colon
  call (a flag in `NumVal2`, or a new opcode) and, when the target is
  userdata and the script's `ColonOperatorClrCallbackBehaviour` is the
  default, ask the descriptor for an **unbound** per-script method callback
  that reads `self` from `args[0]`. One `CallbackFunction` per (member,
  script) for the lifetime of the script, like xLua's methods table. Keep
  the bound path for `local f = obj.Method` (dot access), which must stay
  bound for compatibility.
- **Files:** `Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs`,
  `DataTypes/UserData.cs` (cache fields), `Interop/StandardDescriptors/MemberDescriptors/FunctionMemberDescriptorBase.cs`,
  `Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs`;
  phase B also `Tree/Expressions/FunctionCallExpression.cs`,
  `Execution/VM/ByteCode.cs`, `Execution/VM/Processor/Processor_InstructionLoop.cs`.
- **Impact:** about 4 of the ~10 allocations of an instance call; estimate
  25-40% off `instance_call` on .NET, more on Unity's non-generational GC
  [U]. Phase B also helps calls on freshly pushed objects
  (`self.transform:Foo()`), which phase A does not.
- **Risk:** phase A: per-`UserData` memory (two references). Phase B:
  bytecode format change (binary dumps need a version bump or must ignore
  the flag; the debugger shows the same instructions).
- **Compatibility:** no public API change; `ColonOperatorBehaviour`
  semantics preserved by falling back to the bound path.
- **Tests:** an allocation test (`GC.GetAllocatedBytesForCurrentThread` on
  .NET Core) for `o:Add(i)` in a loop; behavioural tests that `obj.Method`
  stays bound (`local f = obj.Method; f(1)` works) and that colon and dot
  calls give the same results under all three `ColonOperatorBehaviour`
  values.

### Idea 5. Choose argument converters once per parameter (high, medium)

- **xLua:** `ObjectCheck[]`/`ObjectCast[]` per overload chosen at build time
  ([XL/.../MethodWarpsCache.cs#L63-L146](https://github.com/Tencent/xLua/blob/59bf42685dbe36fe0e1678a6ba8597f859ef7ca3/Assets/XLua/Src/MethodWarpsCache.cs#L63-L146));
  xlua-ng adds integer kinds per parameter (section 3.1).
- **MoonSharp now:** all type dispatch happens per call per argument
  (section 4, step 3).
- **Change:** extend `ParameterDescriptor` with a precomputed converter
  object chosen from the parameter type when the descriptor is built:
  `Int32Converter`, `DoubleConverter`, `StringConverter`, `BoolConverter`,
  `EnumConverter<TUnderlying>` (via a non-generic switch on
  `Type.GetTypeCode`, so no generic instantiation is needed for AOT),
  `UserDataConverter(Type)`, `DynValueConverter`, `TableConverter`,
  `NullableConverter(inner)`, and the existing generic path as the fallback.
  Each has `object Convert(DynValue v)` and `int Score(DynValue v)` so
  overload scoring uses the same object. Custom converters:
  `CustomConvertersCollection` gets a version number; a converter checks it
  and falls back to the generic path when any custom converter exists for
  its `(DataType, Type)` pair. Replace `DoubleToType`'s `try`/`catch` with
  range checks (`d >= int.MinValue && d <= int.MaxValue`), keeping the
  current result for out-of-range values.
- **Files:** `Interop/BasicDescriptors/ParameterDescriptor.cs`,
  `Interop/Converters/ScriptToClrConversions.cs`,
  `Interop/Converters/NumericConversions.cs`,
  `Interop/CustomConvertersCollection.cs`,
  `Interop/StandardDescriptors/MemberDescriptors/FunctionMemberDescriptorBase.cs`,
  `OverloadedMethodMemberDescriptor.cs`.
- **Impact:** per argument, from three lookups plus a reflection call plus
  an exception frame to one virtual call; estimate 30-80 ns saved per
  argument on .NET and more on IL2CPP, where `Nullable.GetUnderlyingType`
  and `HashSet<Type>` are slower [U]. Same gain for field and property
  setters.
- **Risk:** behavioural drift from the generic path (string-to-number
  coercions, `char`, `StringBuilder`, tables to arrays). Mitigation: each
  specialised converter is a strict subset; anything unusual goes to the
  generic path; a parity test runs every conversion case through both.
- **Compatibility:** `ParameterDescriptor` is public; add members, keep the
  existing ones.

### Idea 6. A Roslyn incremental source generator emitting typed descriptors (highest ceiling, large)

- **xLua/xlua-ng:** generated wrappers (sections 2.5, 3.2): typed casts,
  direct calls, typed pushes; xlua-ng's measured generated cost is about
  5-8x below its reflection path.
- **MoonSharp now:** Hardwire is CodeDOM driven by a runtime dump and keeps
  `object[]` and boxing (section 4, step 6); on IL2CPP everything else is
  `MethodBase.Invoke`.
- **Change:** a new `MoonSharp.SourceGenerator` (netstandard2.0,
  `IIncrementalGenerator`, Microsoft.CodeAnalysis.CSharp **4.3.1** so Unity
  2021.3 loads it, `ForAttributeWithMetadataName` on `[MoonSharpUserData]`
  plus an assembly-level `[MoonSharpBind(typeof(T))]` for types the user
  does not own). For each bound type it emits an `internal sealed` subclass
  of `HardwiredUserDataDescriptor` (so `UserData.RegisterType(descriptor)`
  keeps working) whose members implement `IOverloadableMemberDescriptor`
  directly:

  ```csharp
  // shape only, not final code
  sealed class Add_Int32 : FunctionMemberDescriptorBase { // or a lean base
      public override DynValue Execute(Script s, object obj, ScriptExecutionContext c, CallbackArguments a) {
          int i0 = a.IsMethodCall ? 1 : 0;
          var self = (Counter)obj;
          int x = ArgConv.ToInt32(a.RawGet(i0, false), "Add", 1);
          self.Add(x);
          return DynValue.Void;
      }
  }
  ```

  Getters/setters as typed `GetValue`/`SetValue` (`DynValue.NewNumber(((T)obj).Prop)`).
  Overload groups: emit xlua-ng's two shapes, a count-and-type `if` chain
  when the group is disjoint, else a scored pick with MoonSharp's existing
  weights so generated and reflected binding choose the same overload.
  Members it cannot emit fall back to the reflection descriptor for that
  member only, with an info diagnostic. Registration: an
  `[assembly: MoonSharpGeneratedBindings(typeof(Bindings))]` marker plus
  `UserData.RegisterAssembly()` reading it (MoonSharp already scans
  assemblies there), and, when `UnityEngine` is referenced, a
  `[RuntimeInitializeOnLoadMethod]` hook, as in xlua-ng 5.2. For IL2CPP
  size, offer xlua-ng's descriptor-mode shape: one `switch(memberId)`
  method per type instead of one class per member.
- **Files:** new `src/MoonSharp.SourceGenerator/`; small additions to
  `Interop/StandardDescriptors/HardwiredDescriptors/` (a lean typed base
  class and argument helpers), `DataTypes/UserData.cs` (`RegisterAssembly`
  honours the marker). Hardwire stays as is for users who rely on it.
- **Impact:** removes reflection, `object[]`, boxing and most conversion
  dispatch; together with ideas 4 and 11 this is where MoonSharp can get
  within 2-3x of xLua's generated path for instance calls and properties
  (xLua's floor is set by its P/Invoke round trip; MoonSharp's by `DynValue`
  allocation) [U]. On IL2CPP the gain is largest, since today's path is
  `MethodBase.Invoke` with boxing.
- **Risk:** large surface (generics, `ref`/`out`, `params`, extension
  methods, events, operators, indexers); parity bugs between generated and
  reflected paths (xlua-ng found 12 classes of them). Requires a parity test
  suite (section 7).
- **Compatibility:** opt-in; no change for reflection users; works with
  netstandard2.0 consumers (the generator is a build-time analyzer);
  Unity 2021.3+ supports Roslyn source generators via the `RoslynAnalyzer`
  label [S: xlua-ng's spike, `build/SOURCEGEN_SPIKE.md`].

### Idea 7. Typed C#→Lua calls and pushes without boxing (medium, small to medium)

- **xLua:** `PushByType<T>`/`Get<T>` with typed delegates, `LuaFunction.Func<T,R>`
  (section 2.3, 2.6); xlua-ng's `LuaValue` has one implicit conversion per
  integral type (section 3.3).
- **MoonSharp now:** `Script.Call(func, params object[])` boxes every
  argument and converts through `DynValue.FromObject` [U: by the signature;
  same conversion path as section 4, step 5].
- **Change:** a static generic cache `ClrToScript<T>.Convert` (a
  `Func<Script, T, DynValue>` chosen once in the static constructor via
  `typeof(T)` checks: `int` → `DynValue.NewNumber`, `string` →
  `NewString`, …, else the generic path). This is cheaper than xLua's
  `Dictionary<Type,Delegate>` lookup per push. Add `Closure.Call<T1>(T1)`,
  `Call<T1,T2>`, … and `Table.Set<T>(string, T)`/`Get<T>(string)` on top.
  The source generator uses the same helpers.
- **AOT:** a static generic class over a value type needs that
  instantiation compiled; IL2CPP generates it when the closed method is
  referenced from code (true for every call site), and Unity 2022+ has full
  generic sharing for the rest [S]. Do not create these instantiations by
  reflection.
- **Files:** `DataTypes/Closure.cs`, `DataTypes/Table.cs`, new
  `Interop/Converters/ClrToScript.cs`.
- **Impact:** removes one box per argument and the type dispatch; the
  `DynValue` allocation per argument remains until `DynValue` becomes a
  struct (VM-side work outside this report) [U: 20-50 ns per argument].
- **Risk:** low; additive API.

### Idea 8. Lua functions as arbitrary CLR delegates, cached per closure (medium, medium; also a feature)

- **xLua:** per-function bridge, per-delegate-type cache with a one-entry
  fast slot, per-delegate-type creator, AOT-restricted generic fallback
  (section 2.6).
- **MoonSharp now:** a Lua function converts only to `Closure` or
  `ScriptFunctionDelegate` [C]
  ([MS/.../ScriptToClrConversions.cs#L158-L161](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/Converters/ScriptToClrConversions.cs#L158-L161));
  passing a Lua function to a method taking `Action<int>` or a
  `UnityAction` fails. `Closure.GetDelegate()` returns a new delegate each
  time.
- **Change:** in the converter for delegate-typed parameters (idea 5),
  build a delegate whose target is the `Closure` and cache it **on the
  `Closure`** (`firstKey/firstValue` then a small dictionary, exactly
  xLua's scheme), so `obj.Event.add(f)` / `remove(f)` and repeated
  conversions return the same instance. Creators per delegate type, cached
  statically: generic `Action<T..>`/`Func<T..,R>` bodies closed with
  `MakeGenericMethod` **only for reference-type parameters and results**
  under AOT (as xLua does), and generator-emitted bodies (idea 6) for the
  rest; throw a clear error naming the delegate type otherwise.
- **Files:** `DataTypes/Closure.cs`, `Interop/Converters/ScriptToClrConversions.cs`,
  new `Interop/DelegateBridges.cs`; `EventMemberDescriptor` could reuse it.
- **Impact:** new capability; for code that re-subscribes handlers, removes
  a delegate allocation and makes `-=` work.
- **Risk:** delegates keep the `Closure` (and its `Script`) alive; document
  it (xLua has the same property through its registry reference).

### Idea 9. Cache enum value `DynValue`s per enum descriptor (medium, small)

- **xLua:** `enumMap` gives one userdata per enum value (section 2.1).
- **MoonSharp now:** each enum crossing to Lua is a new box (if not already
  boxed), `UserData` and `DynValue` (section 4, step 5).
- **Change:** `StandardEnumUserDataDescriptor` keeps a
  `Dictionary<long, DynValue>` (or an array for small dense enums) of
  read-only `DynValue`s; `UserData.Create` consults it when the descriptor is
  an enum descriptor.
- **Files:** `Interop/StandardDescriptors/StandardEnumUserDataDescriptor.cs`,
  `DataTypes/UserData.cs`.
- **Impact:** removes 2-3 allocations per enum push (state machines and
  flags passed every frame) [U].
- **Risk:** must return read-only values; equality semantics unchanged.

### Idea 10. A per-call-site inline cache for userdata member lookups (medium, medium)

- **xLua:** interned Lua strings make `methods[key]` a pointer-hash lookup.
- **MoonSharp now:** each `obj.Name` hashes the .NET string afresh and may
  try up to three fuzzy-matching variants on a miss [C] (section 4, step 1).
- **Change:** `Instruction` is a class with spare fields
  ([MS/.../Instruction.cs#L9-L18](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Execution/VM/Instruction.cs#L9-L18)).
  Add `object InlineCache` holding `(IUserDataDescriptor, IMemberDescriptor)`
  for `IndexN`/`IndexSetN` on userdata: on a hit, skip the dictionary and
  fuzzy matching and call the member directly; on a miss, take the normal
  path and refill. Monomorphic first; polymorphic (2-4 entries) only if
  measured worthwhile. Invalidate by a per-descriptor version bumped by
  `AddMember`/`RemoveMember` and extension-method registration.
- **Files:** `Execution/VM/Instruction.cs`,
  `Execution/VM/Processor/Processor_InstructionLoop.cs` (`ExecIndex`,
  `ExecIndexSet`), `Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs`
  (an `internal` `TryGetMember(name)` and a version).
- **Impact:** a dictionary lookup plus string hash per member access, ~15-40
  ns [U]; combines with idea 4 phase B.
- **Risk:** a descriptor subclass that overrides `Index` with custom logic
  must opt out (only cache for `DispatchingUserDataDescriptor` instances
  whose `Index` is not overridden, or behind an interface the descriptor
  implements); bytecode is per script, and scripts are single-threaded.

### Idea 11. Fewer per-call objects for CLR callbacks (medium, medium)

- **xlua-ng:** `LuaCallback(ref LuaCall)` over the native frame (section 3.3).
- **MoonSharp now:** `Slice`, `CallStackItem`, `ScriptExecutionContext`,
  `CallbackArguments` per CLR call (section 4, step 2).
- **Change:** keep the public `Func<ScriptExecutionContext,
  CallbackArguments, DynValue>` contract but (a) cache one
  `ScriptExecutionContext` per (Processor, callback) when the callee does
  not retain it (not knowable in general, so only for internal descriptors
  and generated code that promise not to), (b) let `CallbackArguments` read
  the value stack directly instead of through an `IList` `Slice`, and (c)
  add an internal fast-callback signature for MoonSharp's own descriptors
  and generated code, e.g. `DynValue FastInvoke(Script, object self, ref
  ArgView args)` where `ArgView` is a `ref struct` over the `FastStack`
  range. `CallStackItem` becoming a struct belongs to the VM work.
- **Files:** `DataTypes/CallbackFunction.cs`, `DataTypes/CallbackArguments.cs`,
  `Execution/ScriptExecutionContext.cs`, `Processor_InstructionLoop.cs`.
- **Impact:** 2-3 of the ~10 allocations per interop call [U].
- **Risk:** reentrancy (a callback calling back into Lua, coroutines);
  the view must not escape, which `ref struct` enforces at compile time.

### Idea 12. Keep value-type receivers consistent across access modes (low impact, small; correctness)

- **xLua:** generated code writes a mutated struct back after an instance
  call (`Update`), and xlua-ng tests parity across modes (section 3.2).
- **MoonSharp now:** the optimised method path builds
  `Expression.Convert(objinst, DeclaringType)` as the instance
  ([MS/.../MethodMemberDescriptor.cs#L225-L245](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs#L225-L245)).
  For a value type this unboxes into a temporary, so a mutating method
  (`v:Normalize()`) changes a copy, while `MethodBase.Invoke` on the box (the
  `Reflection` mode, and every AOT player) mutates the boxed value held by
  the `UserData` [U: from the semantics of `Expression.Convert` on value
  types; needs the test below]. Property setters already skip optimisation
  for value types (`OptimizeSetter`), which suggests the same issue was
  known there.
- **Change:** use `Expression.Unbox` (which the expression compiler emits as
  an address when used as a call instance [S: dotnet/runtime LambdaCompiler
  address emission, from memory]) or skip optimisation for value-type
  instance methods, as the setter does.
- **Files:** `Interop/StandardDescriptors/ReflectionMemberDescriptors/MethodMemberDescriptor.cs`.
- **Tests:** a struct with a mutating method, called from Lua under
  `Reflection`, `LazyOptimized` and `Preoptimized`, asserting the same
  observable result. Generalise into a parity suite (section 7).

### Idea 13. Fix the overload cache, then make misses cheaper (correctness first; low-medium perf, small)

- **xlua-ng:** integer kinds and the "skip scoring when disjoint" analysis
  (section 3.1).
- **MoonSharp now:** a 5-entry cache keyed by `(DataType, userdata Type)`
  lists (`List<DataType>`, `List<Type>`), checked linearly per call; on a
  miss, every overload is scored through `DynValueToObjectOfTypeWeight` [C]
  [MS/.../OverloadedMethodMemberDescriptor.cs#L147-L413](https://github.com/moonsharp-devs/moonsharp/blob/fbd03678d5c0f731d731d099211c7f4a3a1ae8b7/src/MoonSharp.Interpreter/Interop/StandardDescriptors/ReflectionMemberDescriptors/OverloadedMethodMemberDescriptor.cs#L147-L413).
  The cache never clears `ArgsUserDataType` when a slot is reused
  (`found.ArgsDataType.Clear()` at L253, no `ArgsUserDataType.Clear()`), so
  the reused slot's userdata-type list keeps the old entries at the front
  and appends the new ones [C: L223-L269]. Scenario: slot cached for
  `f(udA)` is evicted and reused for `f(udB)` (overload chosen for B); its
  lists become `DataType=[UserData]`, `UserDataType=[A, B]`; a later
  `f(udA)` passes `CheckMatch` (index 0 is `A`) and **calls the overload
  chosen for B** [U: reached only after more than five distinct signatures
  on one method group; needs the regression test below].
- **Change:** fix the missing `Clear()`; store the signature in arrays;
  precompute per overload whether the group is disjoint by Lua type and, if
  so, pick by the first `DataType` match without scoring; reuse idea 5's
  converters for scoring.
- **Impact:** a correctness fix for groups with more than five argument
  signatures; small speedup on hits, large on code that alternates more than
  five signatures (cache thrash) [U].
- **Tests:** an overloaded method `F(A)`, `F(B)` plus five other
  signatures; call each signature once to fill and evict, then call `F(a)`
  and assert the `A` overload ran.

### Not recommended (recorded so they are not ported)

- **Object pool / reverse map / finalizer queue** (sections 2.1, 2.6): they
  solve native Lua's inability to hold managed references; MoonSharp holds
  them directly.
- **Struct packing into raw memory (GCOptimize):** needs native memory or
  `unsafe`; MoonSharp's equivalent costs would be addressed by a struct
  `DynValue` (VM work) and, for specific types, by user custom converters.
- **A process-wide userdata identity cache** (`ConditionalWeakTable<object,
  DynValue>`) to mimic xLua's one-userdata-per-object: costs a weak-table
  lookup per push to save two small allocations, and equality already works
  through `Equals`. Revisit only if Unity profiling shows push allocations
  dominate after ideas 1, 4 and 9.
- **First-match overload selection** (xLua's legacy rule): MoonSharp's
  best-fit scoring is already what xlua-ng moved to.

## 6. Unsafe inventory

| Where | What | What it buys | Safe MoonSharp equivalent |
|---|---|---|---|
| xLua `LuaDLL.cs`, `CopyByValue.cs`, `xlua.c` | P/Invoke, `IntPtr` buffers, `xlua_pack_*`/`xlua_unpack_*` | struct values in Lua userdata memory without managed boxes | not needed; a struct `DynValue` or typed holders |
| xLua `Assets/XLua/Src/*.cs` | `IntPtr L` and P/Invoke everywhere, but **no C# `unsafe` keyword** in the bridge [C: `grep unsafe Assets/XLua/Src/*.cs` finds nothing] | | |
| xlua-ng `Assets/XLua/Core/LuaCall.cs` | `unsafe ref struct LuaCall` with `xlua_core_frame*`, `xlua_value*` | reads arguments from the native frame without a P/Invoke per argument | `ref struct` over MoonSharp's managed value stack (`FastStack<DynValue>` range): bounds-checked, no pointers (idea 11) |
| xlua-ng `LuaScript` | native arena (`IntPtr arena`) for strings and long result lists | no managed allocation for host results | MoonSharp results are managed already |
| MoonSharp proposals above | none | | all ideas are safe C# |

[C] for the xlua-ng entries (`unsafe` in the class declarations at
[NG/Assets/XLua/Core/LuaCall.cs#L34](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaCall.cs#L34)
and [#L741](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Assets/XLua/Core/LuaCall.cs#L741)).

## 7. Benchmarks and regression tests to add first

1. **Interop benchmark set**, ported from xlua-ng's `lua_cs` and `cs_lua`
   groups
   ([NG/Test/Native/Benchmark.cs#L124-L210](https://github.com/emindeniz99/xlua-ng/blob/3042ef63dae56543ad4e43105170f52123e284a2/Test/Native/Benchmark.cs#L124-L210))
   into the fork's `src/DevTools/Benchmarks`: `static_call`,
   `instance_call`, `property_get/set`, `field_get/set`, `string_arg`,
   `string_return`, `struct_pass`, `object_return` (same object),
   `object_return_derived` (idea 1), `enum_return` (idea 9),
   `params_int`, `overload_disjoint`, `overload_num`, `overload_obj_derived`,
   `delegate_to_lua` (idea 2), `closure_call_typed` (idea 7),
   `userdata_table_keys` with 1k and 10k keys (idea 3). Report ns per op and
   bytes allocated per op, run under `Reflection` and `LazyOptimized` (and
   generated once idea 6 exists).
2. **Binding-mode parity suite** (xlua-ng's lesson): the same Lua probes
   (results and error messages) under `Reflection`, `LazyOptimized`,
   `Preoptimized`, Hardwire and, later, generated descriptors. Idea 12 is
   the first case it would catch.
3. **Allocation regression tests** on .NET 8 with
   `GC.GetAllocatedBytesForCurrentThread()` around a warm loop, with
   per-case ceilings, so later changes cannot silently re-add per-call
   allocations.

Order of work suggested: the overload-cache fix from idea 13 and idea 12's
parity test (correctness), ideas 3, 2, 1 (small, independent, measurable),
then 4A and 9, then 5 and the rest of 13 together, then 10 and 4B, then 6, with 7, 8
and 11 alongside 6 since generated code uses them.

## 8. Open questions for the owner

1. Is a bytecode-format change (idea 4 phase B, idea 10 flag) acceptable
   inside the fork, given binary dumps?
2. Should generated descriptors (idea 6) be MoonSharp's only code generator
   in the long run (as xlua-ng did with its legacy generator), or live next
   to Hardwire?
3. Idea 3 changes `DynValue.GetHashCode` for userdata: any known consumer
   that relies on all userdata hashing equal (none found in the tree [U])?
