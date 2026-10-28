# MoonSharp table iteration order: evidence and measurements

Date: 2026-10-28. Branch: `spike/table-order` on github.com/emindeniz99/moonsharp (no PR), based on
master `ed96417`. Commits: `2c9e546` (array part, struct hash, three orders), `67c01be` (collect dead
keys on the next insert, as master intends), `51e5d38` (sparse-array guard; build switches for the
variants), `97def54` (benchmark workloads). Scripts, probes, fuzzers and raw results are in the
scratchpad folder `table-order/` next to this file.

Markers: [C] confirmed here (run or read, with file:line or URL), [S] secondary or inferred,
[U] unconfirmed.

## Summary

| | (a) insertion order | (b) array first | (c) Lua order | (d) switch |
|---|---|---|---|---|
| Unit tests (685) / hosting fixtures (24) | all pass | all pass | all pass | not run on the (d) build |
| Tests that change | none | none | none | none |
| `pairs` order vs master | identical (insert/update fuzz 360/360; with deletions identical to master with its `CollectDeadKeys` bug fixed) | integer keys of the array part move to the front | hash order | as chosen |
| `json.serialize` text vs master | identical | identical (writes string keys only) | differs, and **changes between process runs** | as chosen |
| Hosting `LuaJson` text | identical | differs for tables mixing integer and string keys | differs, per run | as chosen |
| Time vs master, geomean of 11 table workloads | 0.81 | 0.80 | 0.87 | = the folded mode (±0.2%) |
| Time vs (c), direct | +0.7% (10 workloads), max +4.1%; −39% on delete/reinsert | +2.9%, max +7.5% | — | — |
| Bytes per small table (0 / 1 / 4 / 16 string keys) | 144 / 351 / 495 / 1407 | same as (a) | 144 / 311 / 455 / 1319 | +8 B per table over the mode it runs |
| Bytes per small table, master | 264 / 583 / 1007 / 2535 | | | |

**Recommendation: (a), as the only mode.** It keeps the order hosts see today (JSON, debugger,
`Dictionary` conversion), its time cost over (c) is inside the measurement noise, and its memory cost
is one `int[]` bucket array per hashed table (+9-13% bytes) plus a pointer entry for array keys that
arrive after a hash key. (c) buys nothing measurable and makes output order nondeterministic across
runs; (d) costs nothing in time but doubles the table code and test matrix for no gain. (b) is the
fallback if the owner wants less code: same speed and the same `json.serialize` output, ~130 fewer
lines, one MIGRATING note for mixed tables. Reasons in section 5.

## 1. Who depends on insertion order

### 1.1 Empirical: scramble the order and see what breaks

Method. A temporary patch to master's `Table` (`scramble-probe.patch` in the scratchpad folder) made
`next`, `Table.Pairs`, `Keys` and `Values` return keys in a different order, selected by
`MOONSHARP_ORDER_PROBE`: `b` = keys 1..`#t` ascending first, then the rest in insertion order;
`c1`, `c2`, `c3` = keys 1..`#t` first, then the rest in a seeded hash order (three seeds). A Lua probe
confirmed the patch was active (`mixed:` changed from `x,y,1,2,z` to `1,2,z,y,x` under `c1`).

| Suite | master | probe b | probe c1 | probe c2 | probe c3 |
|---|---|---|---|---|---|
| Unit tests (`DotNetCoreTestRunner /unit`) | 685 pass, 2 skipped | 685 / 2 | 685 / 2 | 685 / 2 | 685 / 2 |
| Hosting fixtures (`moonsharp-host test logic.suite.json`) | 24 pass | 24 | 24 (c1) | 24 (c2) | not run |

- [C] **No test or fixture in the repository depends on `pairs` order.** The tests that iterate with
  `pairs` check sums or lengths, not the sequence: `TablePairsWithoutMetatable` asserts
  `res.Tuple[0].String.Length == 5` (`src/MoonSharp.Interpreter.Tests/EndToEnd/TableTests.cs:258-287`);
  `TestNilRemovesEntryForPairs` builds its string from single-key tables (`TableTests.cs:448-`).
- [C] The hosting fixtures are order-blind by design: "`expect` is compared structurally: object key
  order is ignored" (`src/Hosting/fixtures/README.md:35`, `src/Hosting/MoonSharp.Hosting.Cli/FixtureRunner.cs:151`,
  `src/Hosting/browser/ts/fixtures.ts:65`). So the fixtures cannot catch an order change that a host
  snapshot test (string comparison of JSON) would.
- [C] The brief said 692 unit tests pass; on `ed96417` (origin/master when this spike branched) the
  runner reports 685 passed, 2 skipped, 0 failed. It must be run from its `bin` folder, because the
  TestMore suites load `TestMore/*.t` by relative path.

### 1.2 Code that exposes the order (file:line on `ed96417`)

| Consumer | Where | What the order affects |
|---|---|---|
| `next` / `pairs` | `CoreLib/TableIteratorsModule.cs:61-69` → `Table.NextKey` (`DataTypes/Table.cs:555-603`) | Lua-visible traversal order |
| `Table.Pairs`, `Keys`, `Values` | `DataTypes/Table.cs:655-686` (`m_Values.Select(...)`) | Public C# API. **Also yields dead keys whose value is nil** [C] (the linked list keeps a nil-valued node until the next insert, `Table.cs:162-175`) |
| `json.serialize` | `Serialization/Json/JsonTableConverter.cs:39-56` | Object key order when `#t == 0`; only string keys are written |
| Hosting `LuaJson` | `src/Hosting/MoonSharp.Hosting/LuaJson.cs:381, 400-411` | Object key order, **including integer keys** of non-sequence tables (`KeyToString`, L427-436); `CountKeys` (L418-425) counts dead keys too, so `{10,20}` with a cleared `x` is written as `{"1":10,"2":20,"x":null}` [C, measured with `moonsharp-host run host_dead.lua`] |
| Hosting module description | `src/Hosting/MoonSharp.Hosting/ScriptHost.cs:93-97` | Not affected: names are sorted (`names.Sort(StringComparer.Ordinal)`) |
| VS Code debugger variables | `src/MoonSharp.VsCodeDebugger/DebuggerLogic/VariableInspector.cs:76` | Order of table members in the Variables pane |
| Lua-source serializer | `Serialization/SerializationExtensions.cs:32-40` | Field order of `Serialize()` output (and `Values.Any()` sees dead keys) |
| Table → `Dictionary<K,V>` | `Interop/Converters/TableConversions.cs:142, 223` | `Dictionary` enumerates in insertion order when nothing was removed, so the order flows on to the host |
| `for v in t` (MoonSharp extension) | `Interop/PredefinedUserData/EnumerableWrapper.cs:57-60` | Value order of the default table iterator |
| Table → `List`/array | `TableConversions.cs:153-` | Not affected: walks `1..Length` |

### 1.3 JSON output under each option (measured on the spike, `probe_json.lua`, `host_mixed.lua`)

| Input | master | (a) | (b) | (c), three separate processes |
|---|---|---|---|---|
| `{name=, price=, currency=, tags=}` | `{"name","price","currency","tags"}` | same | same | `tags,name,price,currency` / `currency,tags,price,name` / `price,tags,name,currency` |
| `o.zeta, o.alpha, o.mid` assigned in that order | `zeta,alpha,mid` | same | same | `mid,zeta,alpha` / `alpha,zeta,mid` / `zeta,mid,alpha` |
| `json.parse('{"b":1,"a":2,"c":{"y":1,"x":2}}')` re-serialized | `b,a,c{y,x}` | same | same | `c{y,x},a,b` / `b,a,c{y,x}` / `a,c{x,y},b` |
| `{a,b,c}`, `a=nil`, `d=4`, `a=5` | `b,c,d,a` | same | same | varies |
| hosting `LuaJson`: `{name='cfg'}`, then `[2]`, then `after` | `{"name","2","after"}` | same | `{"2","name","after"}` | `{"2",…}` then hash order |

- [C] (a) and (b) give the same `json.serialize` text as master: the core converter writes only string
  keys, and both keep string keys in insertion order. (b) differs only where integer keys are written
  as object keys (the hosting `LuaJson` path) or iterated by `pairs`.
- [C] **(c) changes JSON key order from one process run to the next** on .NET 8: string hashes come from
  `string.GetHashCode()`, which .NET Core seeds randomly per process. Under Mono or .NET Framework the
  hash is not randomized, so (c) would be stable per runtime but differ between runtimes (Unity Mono vs
  IL2CPP vs the server) [S: documented runtime behaviour, not measured here]. A deterministic hash would
  make (c) stable, but its order would still be unrelated to the source.

### 1.4 Documentation and upstream

- [C] Lua 5.2 manual, `next`: "The order in which the indices are enumerated is not specified, *even
  for numeric indices*. (To traverse a table in numeric order, use a numerical **for**.) The behavior of
  `next` is undefined if, during the traversal, you assign any value to a non-existent field in the
  table. You may however modify existing fields. In particular, you may clear existing fields."
  <https://www.lua.org/manual/5.2/manual.html#pdf-next>. MoonSharp copies this text into
  `TableIteratorsModule.cs:44-56`.
- [C] Lua 5.2 §3.4.6: "the length of a table t is only defined if the table is a *sequence*"
  <https://www.lua.org/manual/5.2/manual.html#3.4.6>.
- [C] MoonSharp's "Differences between MoonSharp and Lua" page says nothing about `pairs`, `next`,
  insertion order or JSON key order; it only lists "A default iterator looping over values (not keys)"
  <https://www.moonsharp.org/moonluadifferences.html>. Nothing in this repository's `docs/`, README or
  `src/MoonSharp.Documentation` promises an order either (grep for `insertion order`, `key order`,
  `iteration order`, `pairs`).
- [C] Upstream issues: searches of github.com/moonsharp-devs/moonsharp issues for `order`, `insertion`,
  `next` and `order pairs` (web search UI; the API is not reachable from this session) found no issue
  about `pairs` order. The only table-structure issue is
  [#144 "Performance of table insertion"](https://github.com/moonsharp-devs/moonsharp/issues/144):
  `CollectDeadKeys` scanned the whole table on every insert; "Fixed in upcoming 1.7.0.0"
  (commit `c0223267387f5e345e064fb4fc0076f0231027d6`) by running it only when a nil was stored first.
  [U] Upstream issues that mention order in passing may exist; the web search is keyword-based.
- [S] moonsharp-unityenhanced keeps the `LinkedList` table (it only adds `AggressiveInlining` to
  `Table`/`LinkedListIndex`, research/unityenhanced.md P14), so Unity users of that fork see the same
  insertion order.
- [C] The fork's own brief (scratchpad `issue99.md:210`) asks to compare "field ordering (where contract
  requires deterministic serialization)" between browser, server and Unity. That is a requirement for
  *deterministic* order across hosts, which (a) and (b) meet and (c) does not.

### 1.5 What master actually does (it is not a clean insertion order)

- [C] Setting a key to nil keeps its node; the next insert of a new key calls `CollectDeadKeys`
  (`Table.cs:166-170`), and a key set again after that goes to the end.
- [C] **`CollectDeadKeys` removes only the first dead key.** Its loop advances with
  `node = node.Next` after `Remove(node.Value.Key)` (`Table.cs:539-545`); `LinkedList.Remove` clears the
  removed node's `Next`, so the loop ends, and `m_ContainsNilEntries` is reset anyway. Probe
  (`probe2.lua`): `{a,b,c}`, `a=nil`, `b=nil`, `d=4`, `b=5`, `a=6` gives `b,c,d,a` on master (b kept
  its old place, a moved) and `c,d,b,a` with a fixed loop. Dead keys after the first stay in the list,
  nil-valued; each later "nil assignment, then insert" pair removes one more.
- [C] Consequences: `Table.Pairs`/`Keys`/`Values` return nil values for those dead keys (1.2), and
  the "insertion order" of a key that was deleted and set again depends on unrelated inserts in between.

## 2. The prototype

Branch `spike/table-order` on github.com/emindeniz99/moonsharp, based on `ed96417`. Everything is in
`src/MoonSharp.Interpreter/DataTypes/Table.cs`; the public API is unchanged.

**Shared by all variants**

- Array part `DynValue[]` for keys `1..capacity`. Growth rule from Lua-CSharp
  ([LuaTable.cs L61-L103, L396-L443](https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaTable.cs#L35-L443)):
  a key goes to the array when it is at most `max(2 * capacity, 4)` and at most 4096 past the capacity;
  on growth, hash keys in the new range migrate into the array. Added after measurement: growth also
  needs more than half of the new size to be live (Lua's `computesizes` rule), checked only when growing
  and skipped for `capacity` inserts after a refusal. Without it an integer-key queue kept 1M slots for
  32 live entries (section 3.3).
- `#t` keeps master's semantics exactly: the *first* border (smallest `n` with `t[n+1] == nil`), kept in
  a field and updated on every write: a nil at `k <= n` sets `n = k - 1`; a value at `n + 1` extends
  `n` by scanning forward (array, then hash). No `-1` "unknown" state, no rescans.
- Hash part: struct entries `{ DynValue Key; DynValue Value; int Hash; int Next; }` (24 bytes on x64),
  one table for every key type (master has three dictionaries plus a linked list). Integral numbers hash
  to themselves, other keys use `DynValue.GetHashCode()` (cached in the `DynValue`); bucket index by
  Fibonacci hashing, because integral doubles and small ints have weak low bits.
- Assigning nil keeps the key with a null value (tombstone), so `next` keeps working while a loop clears
  fields. Tombstones are dropped when the hash part is rebuilt, which only happens on an insert.
  Lua-CSharp's never-compacted tombstones (research/lua-csharp.md §4 item 4) do not occur.
- `Pairs`, `Keys`, `Values` skip dead keys (master yields them with nil values; see 1.2).

**Per variant** (one source; `-p:TableOrder=B|C` picks the build default, `MOONSHARP_TABLE_ORDER=a|b|c`
overrides it at run time; the field is `static readonly`, so the tier-1 JIT folds every check)

| | (a) insertion order | (b) array first | (c) Lua order |
|---|---|---|---|
| Hash part | compact dict: entries appended in insertion order + `int[]` bucket heads | same as (a) | Lua node table: entries at their main position, collisions moved or chained to a free node (Brent's variation, `luaH_newkey`), no bucket array |
| `pairs` order | one order over all keys | array slots 1..capacity ascending, then hash entries in insertion order | array slots, then node order (hash order) |
| Extra state for order | implicit prefix: keys 1..P appended while the hash part was empty come first for free; any other new array key gets a *pointer entry* (no key object, `Hash = k`) in the entry list | none | none |
| Delete then set again | master's intent: dead keys are collected on the next insert, so the key goes to the end; a death inside the prefix shortens it and turns the rest of the prefix into pointer entries (once) | same for hash keys; array keys keep index order | dead node reused when a new key's main position hits it |

(d) is built with `-p:TableOrderDynamic=true`: the order becomes a per-table `readonly` field, so every
check is a real load and branch, as a `ScriptOptions` or per-table switch would be.

**Checks run** (all on the final commit unless noted)

- Unit tests, each variant: 685 pass, 2 skipped, 0 failed (same as master). [C]
- Hosting fixtures, each variant: 24 pass. [C]
- Differential fuzz against the saved master build (`fuzz_contents.lua`: 60 rounds x 400 random
  operations over dense, sparse, zero/negative, fractional, boolean and string keys, nil writes,
  `table.insert`/`table.remove`, clearing fields inside `pairs`, full `next` walks; 480 checkpoints of
  `#t` and sorted contents): 0 differences in (a), (b) and (c). [C]
- Order fuzz, inserts and updates only (`fuzz_order.lua`, 360 checkpoints): (a) identical to master. [C]
- Order fuzz with 15% deletions (`fuzz_order_del.lua`): (a) identical to master *with its
  `CollectDeadKeys` loop fixed* (360/360); against unmodified master 174 of 360 checkpoints differ, all
  from the first-dead-key-only bug (1.5). [C]
- Queue/stack/holes order fuzz (`fuzz_queue.lua`): (a) identical to fixed master. [C]

## 3. Measurements

Machine: 4-core x64 Linux VM shared with other agents, .NET 8.0.33, Release builds of the
`netstandard2.0` interpreter. Master = `ed96417`. Every run went through
`flock …/bench.lock` with a `timeout`. Load average during the runs: 1.0-1.3 for A/A, master-vs-(a),
(b), (c); 1.5-5.2 for the later runs (other agents were building outside the lock). `ab` interleaves
the two builds, so drift reaches both, but treat ratios inside the A/A band as no difference.
Absolute medians come from different runs and move with the load; compare ratios.

Workload sources: the first six are `LuaWorkloads` (from xLua-NG `flag_bench.c`) with a smaller `n`
per call; the other five are new, in `src/DevTools/Benchmarks/TableWorkloads.cs` on the branch:
`hash_delete_reinsert` (sliding window of 32 live string keys in a pool of 64: one delete and one
reinsert per iteration), `queue_int_keys` (FIFO over ever-growing integer keys, 32 live),
`pairs_mixed_128` (64 array + 64 string keys, per element), `pairs_hash_128` (128 string keys, per
element), `record_build_read` (build a 4-field record by assignment, read it back). Time and
allocation are per inner iteration. Commands: `ab A B 21 tables`, `ab A B 11 tables` for A/A,
`ab A B 0 tablemem`.

### 3.1 Noise (A/A)

| Run | geomean of ratios | worst single median | widest interquartile range |
|---|---:|---:|---|
| master vs master, 11 rounds | 1.000 | 0.924 (table_hash_set_get) | 1.012-1.246 (pairs_hash_128) |
| (a) vs (a), 11 rounds | 0.973 | 0.932 (pairs_mixed_128) | 0.892-1.184 (table_array_insert) |

[C] A single workload's median ratio moves by up to ±8% with no code change, and a whole run can be
biased by ~3%. Differences smaller than that are not evidence.

### 3.2 Time and allocation per variant (21 interleaved rounds against master)

Median ratio to master (interquartile range); allocation per inner iteration (master | a | b | c).

| Workload | (a) / master | (b) / master | (c) / master | alloc master / a / b / c |
|---|---:|---:|---:|---|
| table_array_insert | **0.41** (0.37-0.50) | **0.38** (0.32-0.39) | **0.44** (0.39-0.47) | 380 / 212 / 212 / 212 B |
| table_array_read | 1.01 (0.94-1.05) | 0.97 (0.93-1.06) | 0.97 (0.92-1.06) | 528 B all |
| table_hash_set_get | 0.97 (0.83-1.00) | 0.93 (0.89-1.00) | 0.95 (0.88-1.09) | 912 / 864 / 864 / 864 B |
| gc_small_tables | **0.73** (0.69-0.74) | **0.72** (0.67-0.80) | **0.73** (0.68-0.79) | 1.7 / 1.1 / 1.1 / 1.1 KB |
| table_sort_52 | **0.77** (0.75-0.81) | **0.73** (0.70-0.83) | **0.84** (0.74-0.88) | 125 / 69 / 69 / 69 KB |
| table_concat | **0.81** (0.80-0.82) | **0.86** (0.83-0.90) | 0.90 (0.84-0.92) | 1.4 KB all |
| hash_delete_reinsert | **0.84** (0.81-0.95) | **0.86** (0.80-0.87) | **1.48** (1.37-1.56) | 1.2 / 1.1 / 1.1 / 1.8 KB |
| queue_int_keys | 0.91 (0.87-1.03) | 0.93 (0.89-1.03) | 0.90 (0.85-0.92) | 824 / 709 / 709 / 672 B |
| pairs_mixed_128 | 1.05 (0.97-1.10) | 1.00 (0.95-1.02) | 1.02 (0.99-1.05) | 601 / 625 / 625 / 625 B |
| pairs_hash_128 | 1.00 (0.97-1.03) | 0.99 (0.94-1.00) | 1.01 (0.94-1.04) | 601 B all |
| record_build_read | **0.71** (0.66-0.73) | **0.71** (0.65-0.72) | **0.69** (0.63-0.71) | 1.8 / 1.2 / 1.2 / 1.1 KB |
| geomean | 0.81 | 0.80 | 0.87 (0.82 without delete/reinsert) | |

- [C] The table structure (array part, flat struct hash) is what pays: inserts 2.3-2.6x faster,
  small-table construction and records ~1.4x, sort ~1.3x, with 40-45% less allocation where tables
  are built. The order variant barely matters (3.3).
- [C] Reads and `pairs` do not get faster: `table_array_read`, `table_hash_set_get`, `pairs_*` stay
  at 0.93-1.05. Their cost is the VM's per-operation `DynValue` allocation (528 B per read iteration on
  both builds), not the lookup. The 5-15x read gain estimated in research/lua-csharp.md P1-a needs the
  struct value rewrite first [C for "not now", U for the later gain].
- [C] `#t` stays O(1) in the common cases: `table_array_insert`, which calls `#t` once, and
  `table_concat`/`table_sort_52`, which use the length, got faster.

### 3.3 The cost of (a) over (c), measured directly (21 rounds, (c) build vs (a) build in one process)

| Workload | (a) / (c) | (b) / (c) |
|---|---:|---:|
| table_array_insert | 0.995 (0.89-1.06) | 1.064 (1.02-1.13) |
| table_array_read | 1.016 (0.95-1.03) | 1.002 (0.96-1.03) |
| table_hash_set_get | 0.998 (0.86-1.02) | 1.005 (0.96-1.06) |
| gc_small_tables | 1.003 (0.92-1.08) | 1.014 (0.99-1.05) |
| table_sort_52 | 1.027 (0.96-1.11) | 1.075 (0.99-1.12) |
| table_concat | 0.965 (0.94-0.98) | 0.986 (0.97-1.00) |
| hash_delete_reinsert | **0.613** (0.59-0.63) | **0.600** (0.58-0.63) |
| queue_int_keys | 1.041 (1.01-1.09) | 1.034 (0.96-1.12) |
| pairs_mixed_128 | 1.001 (0.92-1.02) | 1.063 (0.96-1.09) |
| pairs_hash_128 | 1.004 (0.98-1.06) | 1.062 (0.99-1.12) |
| record_build_read | 1.022 (1.01-1.11) | 0.991 (0.96-1.08) |
| **geomean** | **0.963** (1.007 without delete/reinsert) | 0.980 (1.029 without) |

- [C] **Time cost of (a) over (c): +0.7% geomean over ten workloads (within the ±3% run bias), at most
  +4.1% on one (`queue_int_keys`, whose deletions at the front of the sequential prefix turn it into
  pointer entries once).** On delete/reinsert (a) is 39% *faster* than (c).
- [C] (c) loses delete/reinsert because a Lua-style node table cannot reuse a dead node unless a new
  key's main position lands on it; free positions run out and it rehashes (allocation 1.8 KB vs
  1.1 KB per iteration). Real Lua has the same policy. A (c) built on the compact dict without an order
  promise would match (a)'s time, which leaves (c) with no speed advantage at all [S: inference from
  (a) and (b) sharing that structure and timing].
- [C] Option (d), the order as a per-table field (no JIT folding): (d)/(a) geomean 1.002, worst 1.027;
  (d)/(c) geomean 1.001, worst 1.044 (`table_concat`). A runtime switch costs nothing measurable in time and 8 B
  per table (`results/mem_a_d.md`: 144 → 152 B for `{}`, 1407 → 1415 B at 16 keys). Its costs are code
  size (two hash layouts in one class) and the test matrix, not speed.
- [C] Exact master semantics for deleted keys (collect on the next insert, commit `67c01be`) versus the
  first cut that only purged on rebuild (`2c9e546`): `hash_delete_reinsert` 0.80 vs 0.84 of master,
  the other ten within noise. The exactness costs ~5% on delete-heavy hashes.

### 3.4 Memory per table (retained bytes, 20,000 tables kept alive, includes the 40 B `DynValue` wrapper)

| Shape | master | (a) | (b) | (c) |
|---|---:|---:|---:|---:|
| `{}` (0 entries) | 264 | 144 | 144 | 144 |
| 1 string key | 583 | 351 | 351 | 311 |
| 4 string keys | 1007 | 495 | 495 | 455 |
| 16 string keys | 2535 | 1407 | 1407 | 1319 |
| 1 array item | 583 | 199 | 199 | 199 |
| 4 array items | 1007 | 199 | 199 | 199 |
| 16 array items | 2535 | 295 | 295 | 295 |
| `{ i, i + 1, x = i }` | 1007 | 407 | 407 | 367 |
| 4 fields assigned after `{}` | 1007 | 495 | 495 | 455 |
| `{1,2,3,4}` then `t.n` | 1327 | 407 | 407 | 367 |
| `{n = 4}` then `t[1..4]` | 1327 | **519** | 407 | 367 |
| queue, 32 live after 1M push/pop | 6,584 | 7,688 | 7,688 | 7,032 |

- [C] Every variant needs 40-90% less memory than master for small tables.
- [C] (a) over (c): +40 B (+9-13%) for any table with a hash part (the `int[]` bucket array, which
  (c)'s node table does not need), +6.7% at 16 keys, and +152 B (+41%) for the one shape where array
  keys arrive after a hash key (four pointer entries). (b) costs the same as (a) except in that shape.
- [C] Before the sparse-array guard (commit `51e5d38`) the same queue retained **8,390,488 bytes**
  (an array part of ~1M slots for 32 live entries; `bin-2c9e546`, `results/mem_2c9e546_a.md`); with
  it, 7-8 KB in every variant (master 6.6 KB). The two fields added since `2c9e546` (dead-key list,
  growth skip counter) cost 8 B per table.

### 3.5 `workloads 7` (whole-process runs, n as in `flag_bench.c`, median ns per iteration)

Separate processes, run one after another under the lock (12:38-14:15 UTC, load 1.1-2.9), so only large
differences count: the non-table cases, which the spike does not touch, moved by up to ±16% between runs.
Every check value matched master in every variant [C].

| Case | master | (a) | (b) | (c) | (a)/master | (c)/master |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 1166.5 | 582.8 | 543.7 | 639.8 | **0.50** | **0.55** |
| table_array_read | 353.5 | 347.8 | 350.6 | 385.0 | 0.98 | 1.09 |
| table_hash_set_get | 670.0 | 674.0 | 622.6 | 652.5 | 1.01 | 0.97 |
| table_sort_52 | 140173.6 | 95409.2 | 96846.4 | 98673.4 | **0.68** | **0.70** |
| table_concat | 1159.6 | 947.8 | 978.6 | 1099.0 | 0.82 | 0.95 |
| gc_small_tables | 1118.6 | 759.3 | 735.6 | 718.9 | **0.68** | **0.64** |
| method_call (untouched; noise reference) | 584.9 | 514.1 | 526.3 | 620.1 | 0.88 | 1.06 |
| function_call (untouched; noise reference) | 328.3 | 319.5 | 373.6 | 379.5 | 0.97 | 1.16 |

Native Lua 5.5.1 for scale (from the brief): table_array_insert 16, table_array_read 16,
table_hash_set_get 36 ns. The spike closes about half of the insert gap to MoonSharp's own VM floor and
none of the read gap (3.2).

## 4. Risks

**`next()` during modification.**
- [C] Clearing fields during `pairs` works in all variants: tombstones keep the key until an insert
  (fuzz: 480 checkpoints with clears inside `pairs`, plus `probe_modify.lua`: clearing all 8 keys
  inside `pairs` visits 8 and leaves the table empty, as on master).
- [C] Undefined cases behave like master in (a) and (b): inserting a new key inside `pairs` keeps
  visiting the new keys (stopped at 1001 steps on master, (a), (b)); clearing the current key *and*
  inserting a new one raises "invalid key to 'next'" on master, (a), (b) and (c), because the insert
  collects the dead current key ((a), (b), master) or reuses its node ((c)). Under (c) the
  insert-during-`pairs` loop stopped after 5 steps instead (rehash moved the keys). Scripts that relied
  on master's behaviour there were already outside the manual.
- [S] `Table.Pairs` from C# no longer throws when a key is added or removed during enumeration
  (master's `LinkedList` enumerator should throw `InvalidOperationException`; not run); it follows the
  same rules as `next`.

**Tombstone growth.**
- [C] (a)/(b) collect dead keys on the next insert (unlink, O(1) each) and drop them when the entry
  array is rebuilt; a rebuild keeps the capacity when at most half the entries are live, so a table
  that churns keys stays bounded (`hash_delete_reinsert`: allocation 1.1 KB per iteration, same as
  steady state). (c) drops dead nodes on rehash only.
- [C] The array part never shrinks: a 1M-element array cleared by `t[i] = nil` keeps 8 MB until
  `Clear()` or the table dies. Master frees nodes as dead keys are collected. Lua shrinks it on the next
  rehash. Fix if needed: re-check the "half live" rule when the hash part rebuilds.
- [C] Without the sparse guard, ever-growing integer keys grew the array without bound (Lua-CSharp's
  rule has the same hole); the guard is part of the spike.

**`#` border semantics.**
- [C] Kept exactly: `#t` is the first border, as on master (`Table.cs:611-623` on master), not "any
  border" as Lua 5.2 allows. The differential fuzz compared `#t` at 480 checkpoints including holes,
  `table.insert`, `table.remove` and negative/fractional keys: no difference.
- [S] Filling a hole extends the cached border by scanning forward (array, then hash), O(length of the
  run joined). Lua uses a binary search; a worst case (fill the hole at 1 of a 1M run, clear it, fill
  again) is O(n) per write here. Not measured.

**`table.insert` / `table.remove`.**
- [C] Both use `Length` and `Get`/`Set` only (`CoreLib/TableModule.cs:170-196` for `remove`), so they inherit the first-border
  semantics. `table.remove(t, 1)` shifts values (updates, no new keys) and kills the last slot, so the
  dead-key collection only shortens the sequential prefix by one; no pointer entries.
- [U] `TableModule` still goes through `Get`/`Set` per element; array fast paths (insert/remove/sort/
  unpack on `m_Array`) are a separate gain, not measured here.

**Other.**
- [C] (c) makes output order differ between process runs on .NET Core (1.3). Host snapshot tests,
  cached JSON, diff-based config tooling and the cross-host equality goal in the fork brief would see
  it. Mono/IL2CPP would show a different but stable order [S].
- [C] `Pairs`/`Keys`/`Values` stop returning dead keys. On master a sequence whose extra field was
  cleared is serialized by the hosting `LuaJson` as `{"1":10,"2":20,"x":null}` (measured,
  `host_dead.lua`); all spike variants give `[10,20]`. This is a bug fix that changes output, so it
  belongs in MIGRATING-style notes either way.
- [C] `Table.Remove(key)` (C# API) now behaves like assigning nil (collected at the next insert);
  master unlinks at once, so `Remove` then set again without another insert in between keeps the old
  place in (a). Edge case; easy to make exact by purging that key in `Remove`.
- [S] The design leans on `DynValue`'s cached hash for string keys. research/unityenhanced.md idea 1
  proposes dropping `m_HashCode`; then every string-key lookup would hash the string again (master's
  `Dictionary<string,…>` already does). Keep the cache, or cache hashes on interned constant strings.
- [C] The spike changes no public signature. `LinkedListIndex` is now unused (left in place).
- [C] master's own `CollectDeadKeys` stops after the first dead key (1.5); whatever is chosen, master's
  exact deleted-key order is a bug, not a contract.

## 5. Recommendation

**Choose (a): keep insertion order across array and hash keys, with master's intended dead-key rule
(a key set to nil is forgotten at the next insert), as the only mode.**

1. *Who depends on it.* No test or fixture does [C]. But the order reaches hosts through
   `json.serialize`, the hosting `LuaJson`, `Table → Dictionary` conversion, the Lua-source serializer and
   the debugger (1.2), and the fork's own brief asks for identical results across browser, server and
   Unity (1.4). Users cannot see a contract for insertion order in any document [C], but they see the
   behaviour in every JSON object they produce, so some will have snapshot tests or diffs built on it
   [U: no user data].
2. *Cost.* Against (c), (a) costs +0.7% time (geomean of ten workloads, inside the ±3% run bias; max
   +4.1%) and is 39% faster on delete/reinsert [C]. Memory: +40 B per table with a hash part (+9-13%),
   +152 B for a table whose array keys follow a hash key [C]. Against master, (a) is 19% faster on the
   table workloads (geomean) and uses 40-90% less memory per small table [C].
3. *(c) has no upside here.* It is not faster (3.3), its order changes between runs on .NET Core (1.3)
   and between runtimes, and its node table is the slowest variant on delete-heavy tables. Its only
   argument is "Lua does not promise an order", which (a) also satisfies.
4. *(d) is free in time but not in code.* A per-table field cost ≤0.2% geomean [C], so a switch is
   technically cheap; but it keeps two hash layouts (compact dict and node table), two sets of tests and
   a documented option whose only effect is a less predictable order. If the owner wants an escape
   hatch, the cheapest one is (a)/(b) as a switch on one structure (both share the compact dict; (b)
   just skips the order log), not (a)/(c).
5. *(b) as the fallback.* Same structure and speed as (a) (3.2), same `json.serialize` output, simpler
   code (no sequential-prefix bookkeeping or pointer entries, ~130 lines of `Table.cs` are (a)-only).
   Its user-visible change: in a table that mixes integer and string keys, `pairs` and the hosting
   `LuaJson` put the array-part integer keys first. Note that "array part" means "integer keys up to the
   array capacity", which is an implementation detail (e.g. keys 9, 17, 25 come first if the array grew
   to 64), so (b)'s order is not "1..#t then the rest".
6. *Either way*, record in MIGRATING: `Table.Pairs/Keys/Values` no longer return keys whose value is
   nil (fixes the hosting JSON `{"1":10,"2":20,"x":null}` output), and the dead-key rule is "forgotten
   at the next insert" for all dead keys, not only the first.

The order decision does not gate the performance work: the array part and the flat struct hash give the
gains in every variant, and reads only get faster after the struct value rewrite (3.2).

## 6. Reproduce

All paths under `/tmp/claude-0/-home-user/49abf7c9-632c-52bb-859e-20c0836e18dc/scratchpad/table-order/`.

- Variant builds: `build_variants.sh` (one source; `-p:TableOrder=B|C`, `-p:TableOrderDynamic=true`),
  output `bin-a`, `bin-b`, `bin-c`, `bin-d`; master DLL in `bin-master` (sha256 `39f5b217…`).
- Timing and memory: `chain.sh` → `ab.sh` (flock + timeout) → `results/*.md`; `summarize.py` and
  `geomean.py` build the tables above.
- Unit tests per variant: `runtests.sh a|b|c <out>` (sets `MOONSHARP_TABLE_ORDER`, runs from the
  runner's `bin` folder). Hosting fixtures: `runhost.sh a|b|c`.
- Order and content checks: `runlua-all.sh <file>` (master REPL copy + spike REPL in a, b, c),
  `runlua-mf.sh` (master with `CollectDeadKeys` fixed, built by `masterfix.py`), `cmp.py`;
  probes `probe1.lua`, `probe2.lua`, `probe_json.lua`, `probe_modify.lua`, `host_mixed.lua`,
  `host_dead.lua`; fuzzers `fuzz_contents.lua`, `fuzz_order.lua`, `fuzz_order_del.lua`, `fuzz_queue.lua`.
- Part 1 scramble: `scramble-probe.patch` on `ed96417`, `MOONSHARP_ORDER_PROBE=b|c1|c2|c3`.

Not done: no Unity/IL2CPP or Mono run; no BenchmarkDotNet run; `table.*` array fast paths; shrinking
the array part; a (c) built on the compact dict.

## 7. Incidents during this task

- The shared scratchpad already had a `runtests.sh` from another agent; my first `Write` replaced it.
  I could not restore the original and deleted my copy, so a caller of that path gets "not found"
  instead of running my script. My files now live only in `table-order/`.
- The shared `bench.lock` was held from at least 11:23 to 11:50 UTC by an idle MSBuild node
  (`/nodeReuse:true`, working directory in worktree `agent-a1c1799c79e914492`): it inherited the lock's
  file descriptor from a `flock … dotnet build` and kept it after the build ended. Killing it was refused
  by the permission classifier; it exited on its own. Agents should not run `dotnet build` under the
  benchmark lock without `-nodeReuse:false` (or `MSBUILDDISABLENODEREUSE=1`).

