# Migrating

Changes in this fork that a script or a host can observe, newest last. Each
entry says what changed, what it was before and who is affected.

## One descriptor per CLR type resolved through interfaces

A CLR object whose type is not registered, but whose interfaces are, gets a
descriptor that combines the interface descriptors. MoonSharp used to build a
new combined descriptor on every push, so two pushes of the same object were
different values to `DynValue.Equals`, and therefore different table keys.
The combined descriptor is now built once per type and reused.

```lua
-- x and y are the same C# object, pushed twice
local t = {}
t[x] = 1
print(t[y])   -- before: nil   now: 1
```

`x == y` was already true and does not change. Affected: hosts that push
objects of unregistered types implementing registered interfaces and use
them as table keys, or compare `UserData.Descriptor` references.

## Userdata and coroutines no longer share one hash code

`DynValue.GetHashCode` returned 999 for every userdata (other than int64 and
uint64) and every coroutine, so a table keyed by many of them searched all
keys on every lookup. Now:

- a userdata whose CLR type compares by reference (does not override
  `Equals`) hashes by object identity;
- any other userdata hashes by its descriptor, so a key whose `Equals`
  follows state that changes is still found after it changes;
- a coroutine hashes by identity.

Equality is unchanged, and `pairs` order does not depend on hashes.
Affected: hosts that stored `DynValue.GetHashCode()` values.

## Overloaded methods called with many userdata signatures

The overload resolver keeps the last five argument signatures of a method.
When it reused a slot it kept the previous signature's userdata types, so a
later call could match the wrong slot and run the wrong overload or fail
with "cannot convert". It now clears the slot. Affected: scripts calling an
overloaded method with more than five different userdata argument types.

## Methods on structs mutate the userdata in every access mode

A method on a struct called from a script, for example `v:Increment()`,
changed the boxed struct inside the userdata under
`InteropAccessMode.Reflection`, but only a copy of it under
`LazyOptimized`, `Preoptimized` and `BackgroundOptimized`, so the change
was lost. The compiled adapters now call the method on the struct inside
its box, so every mode changes the userdata.

```lua
v:Increment()
print(v.Count)   -- before: 0 in the optimized modes, 1 with Reflection   now: 1
```

Affected: hosts exposing structs with mutating methods. Hardwired
descriptors are generated code that casts the receiver to a copy; they keep
the old behaviour.

## Reading a method from a userdata returns the same callback

Each read of a method from a userdata, which includes every
`obj:Method()` call, built a new callback bound to that userdata. The
userdata now keeps its last two bound callbacks and returns them again, so:

```lua
print(obj.Method == obj.Method)          -- before: false   now: true
print(rawequal(obj.Method, obj.Method))  -- before: false   now: true
print(obj.Method == other.Method)        -- false, as before
```

The kept callback is read-only and is dropped when the descriptor's members
change (`AddMember`, `RemoveMember`, a new extension method found by
name). Overloads and extension methods are still resolved at each call.
Affected: scripts that compared method values, and hosts that set
`CallbackFunction.AdditionalData` on a callback read from a userdata
(the object is now shared by later reads).

## Named enum values pushed from the CLR share one userdata

A CLR enum value with a name in a registered enum, returned by a method or
pushed with `UserData.Create` or `DynValue.FromObject`, now reuses the
read-only value the enum descriptor holds for that name, as `MyEnum.A` in a
script already did, instead of a new userdata each time. Equality,
`tostring` and table keys are unchanged. Affected: hosts that call
`Assign` on such a value (it is read-only, like `DynValue.Nil`), or that
attach a `UserValue` (`debug.setuservalue`) to one enum value and expect
other pushes of the same value not to see it.

## Flags functions return enum values

`flagsAnd`, `flagsOr`, `flagsXor` and `flagsNot` on a registered `[Flags]`
enum returned a userdata wrapping the bare underlying number (a boxed
`int`, `ushort`, ...), not a value of the enum type. So it printed as a
number and never compared equal to the enum's members:

```lua
print(MyFlags.flagsOr(MyFlags.A, MyFlags.B))                  -- before: 3      now: A, B
print(MyFlags.flagsOr(MyFlags.A, MyFlags.B) == MyFlags.AB)    -- before: false  now: true
```

They now return values of the enum type. Affected: scripts that printed or
compared these results, and hosts that received them as `object`.

## Objects with script event handlers can be collected

An event descriptor is shared by every script and lives as long as the
type's registration. It kept each object that had a script handler, the
handler closures and so their scripts, in plain dictionaries, so none of
them were ever collected unless the script removed every handler. It now
holds the object weakly: while the object lives its handlers live and
fire as before; once the host drops the object, the object, its handlers
and a script referenced only by them can be collected. Affected: hosts
that relied on a script staying alive only because one of its functions
handled an event of an object nobody else referenced (such an event could
never fire again anyway).

## Background optimization no longer ends the process

With `InteropAccessMode.BackgroundOptimized`, the thread-pool work item
that compiles adapters walked a type's overload list while the first
script call sorted it. The resulting exception was unhandled on a pool
thread, which ends the process. The work item now walks copies of the
lists and ignores a failure: members it did not optimize keep running
through reflection, as they do before it finishes. Affected: hosts using
`BackgroundOptimized` with overloaded methods.

## `table.remove` on an empty table follows Lua 5.2

With `#t == 0`, `table.remove(t)` stored nil under an internal key 0 that
script key 0 cannot reach. Once that entry came first in the table,
`pairs(t)` failed with "invalid key to 'next'". It now does what Lua 5.2
does: it returns `t[0]` and sets `t[0]` to nil, and a position below 1
other than `#t` is an error.

```lua
local t = {}
table.remove(t); t[1] = 'a'
for k, v in pairs(t) do end   -- before: error "invalid key to 'next'"   now: works

local u = { [0] = 'x' }
print(table.remove(u), u[0])  -- before: nil  x   now: x  nil

table.remove({}, -2)          -- before: returned nil   now: error "position out of bounds"
```

Affected: scripts that call `table.remove` on tables that may be empty
(stacks, queues). `Table.Pairs` no longer returns a `0 = nil` entry to
hosts after such a call.

## Keys set to nil are gone for hosts, and every one of them is forgotten

Two dead-key bugs are fixed:

- `Table.Pairs`, `Table.Keys` and `Table.Values` returned keys whose value
  had been set to nil, as pairs with a nil value. Hosts that convert tables
  saw fields the script had removed: the hosting JSON writer turned a
  sequence with a cleared field into an object, and `json.serialize` wrote
  `null` for it.
- A key set to nil is forgotten when the next new key is stored, so it goes
  to the end of the `pairs` order when it gets a value again. Only the first
  such key was forgotten; the others kept their old place. Now every one is.

```lua
local t = { 10, 20 }
t.x = 1; t.x = nil
-- hosting JSON before: {"1":10,"2":20,"x":null}   now: [10,20]

local o = { a = 1, b = 2 }; o.b = nil
print(json.serialize(o))   -- before: {"a":1,"b":null}   now: {"a":1}

local u = {}
u.a = 1; u.b = 2; u.c = 3
u.a = nil; u.b = nil
u.d = 4                    -- forgets a and b (before: only a)
u.b = 5; u.a = 6
for k in pairs(u) do io.write(k, ' ') end
-- before: b c d a   now: c d b a
```

`pairs` in a script never returned the nil keys and still does not. Affected:
hosts that enumerate tables (`Table.Pairs`, `Keys`, `Values`, JSON,
`Dictionary` conversion, the Lua-source serializer) and scripts or snapshot
tests that depend on the `pairs` order after clearing more than one key.

## Tables have an array part

Tables now keep integer keys 1..n in an array and every other key in one
hash part, instead of a linked list indexed by three dictionaries. Values,
`#t` and the `pairs` order stay as they were (insertion order, with the
dead-key rule above). What a host can see:

- Integer keys below 1 stored with `Table.Set(int, ...)` are the same keys
  a script uses. They used to live in an index that `t[0]` and `t[-1]` never
  reached, so a host's `t.Set(0, v)` was invisible to the script, and a
  later `pairs(t)` failed with "invalid key to 'next'".
- `Table.Remove(key)` returns false for a key whose value was set to nil.
  It returned true while the dead key was still stored.
- `Table.Pairs`, `Keys` and `Values` follow the rules of `next`: clearing
  fields while enumerating is allowed; whether a key added during the
  enumeration is visited is not defined.
