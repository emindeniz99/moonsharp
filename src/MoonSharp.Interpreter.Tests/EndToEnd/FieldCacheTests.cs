using System;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Field and global accesses with a constant name cache where they found the key. Each test runs the
	/// same instruction (inside one function) before and after a change that must make the cached place
	/// wrong, or on another table where the cached place is wrong, and checks that the second run sees the
	/// change. The cache is keyed by layout (the entry index of the key), so the tests after
	/// FieldCache_SharedByCoroutines run one instruction over several tables; each names the check in
	/// FieldCache or Table that it needs.
	/// </summary>
	[TestFixture]
	public class FieldCacheTests
	{
		static DynValue Run(string code)
		{
			return new Script().DoString(code);
		}

		static void AssertValues(DynValue r, params object[] expected)
		{
			for (int k = 0; k < expected.Length; k++)
			{
				DynValue v = r.Tuple[k];

				if (expected[k] == null)
					Assert.IsTrue(v.IsNil(), "value " + (k + 1) + ": expected nil, got " + v);
				else if (expected[k] is string)
					Assert.AreEqual(expected[k], v.String, "value " + (k + 1));
				else
					Assert.AreEqual(System.Convert.ToDouble(expected[k]), v.Number, "value " + (k + 1));
			}
		}

		[Test]
		public void FieldCache_FieldRemovedAndAddedAgain()
		{
			DynValue r = Run(@"
				local t = { x = 1, y = 2 }
				local function get() return t.x end
				local a = get()
				t.x = nil
				local b = get()
				t.z = 3 -- a new key: the dead x is forgotten
				t.x = 4
				return a, b, get()");

			AssertValues(r, 1, null, 4);
		}

		[Test]
		public void FieldCache_RawsetAndHostSet()
		{
			Script s = new Script();
			DynValue r = s.DoString(@"
				t = { x = 1 }
				get = function() return t.x end
				local a = get()
				rawset(t, 'x', 2)
				local b = get()
				rawset(t, 'x', nil)
				return a, b, get()");

			AssertValues(r, 1, 2, null);

			s.Globals.Get("t").Table.Set("x", DynValue.NewNumber(5));
			Assert.AreEqual(5, s.DoString("return get()").Number);

			s.Globals.Get("t").Table.Remove("x");
			Assert.IsTrue(s.DoString("return get()").IsNil());

			s.Globals.Get("t").Table.Set("x", DynValue.NewNumber(6));
			Assert.AreEqual(6, s.DoString("return get()").Number);
			s.Globals.Get("t").Table.Clear();
			Assert.IsTrue(s.DoString("return get()").IsNil());
		}

		[Test]
		public void FieldCache_InstanceShadowsAMethodFoundThroughIndex()
		{
			DynValue r = Run(@"
				local C = {} C.__index = C
				function C:name() return 'class' end
				local o = setmetatable({}, C)
				local function call() return o:name() end
				local a = call()
				o.name = function() return 'instance' end
				local b = call()
				o.name = nil
				return a, b, call()");

			AssertValues(r, "class", "instance", "class");
		}

		[Test]
		public void FieldCache_DeadInstanceKeyGetsAValueAgain()
		{
			// The instance's own key died; the lookup went on to the class. Storing into the dead key
			// revives it in place, without adding a key, and must still win over the class.
			DynValue r = Run(@"
				local C = { v = 'class' } C.__index = C
				local o = setmetatable({ v = 'own' }, C)
				local function get() return o.v end
				local a = get()
				o.v = nil
				local b = get()
				rawset(o, 'v', 'again')
				return a, b, get()");

			AssertValues(r, "own", "class", "again");
		}

		[Test]
		public void FieldCache_MethodReplacedInTheClass()
		{
			DynValue r = Run(@"
				local C = {} C.__index = C
				function C:f() return 1 end
				local o = setmetatable({}, C)
				local function call() return o:f() end
				local a = call()
				function C:f() return 2 end
				return a, call()");

			AssertValues(r, 1, 2);
		}

		[Test]
		public void FieldCache_IndexReplacedOrRemoved()
		{
			DynValue r = Run(@"
				local A = { k = 'A' }
				local B = { k = 'B' }
				local mt = { __index = A }
				local o = setmetatable({}, mt)
				local function get() return o.k end
				local a = get()
				mt.__index = B
				local b = get()
				mt.__index = nil
				local c = get()
				mt.__index = A -- the dead __index entry gets a value again
				local d = get()
				mt.__index = function(t, k) return 'fn' end
				local e = get()
				mt.__index = B
				return a, b, c, d, e, get()");

			AssertValues(r, "A", "B", null, "A", "fn", "B");
		}

		[Test]
		public void FieldCache_MetatableReplaced()
		{
			DynValue r = Run(@"
				local A = { __index = { k = 'A' } }
				local B = { __index = { k = 'B' } }
				local o = setmetatable({}, A)
				local function get() return o.k end
				local a = get()
				setmetatable(o, B)
				local b = get()
				setmetatable(o, nil)
				return a, b, get()");

			AssertValues(r, "A", "B", null);
		}

		[Test]
		public void FieldCache_InheritanceChainChangesAtEveryLevel()
		{
			DynValue r = Run(@"
				local Base = {} Base.__index = Base
				function Base:who() return 'base' end
				local Derived = setmetatable({}, Base) Derived.__index = Derived
				local o = setmetatable({}, Derived)
				local function call() return o:who() end
				local a = call()
				function Derived:who() return 'derived' end
				local b = call()
				Derived.who = nil
				local c = call()
				function Base:who() return 'base2' end
				local d = call()
				local Other = { who = function() return 'other' end } Other.__index = Other
				setmetatable(Derived, Other)
				return a, b, c, d, call()");

			AssertValues(r, "base", "derived", "base", "base2", "other");
		}

		[Test]
		public void FieldCache_GlobalReassignedAndRemoved()
		{
			DynValue r = Run(@"
				function f() return 1 end
				local function call() return f() end
				local a = call()
				f = function() return 2 end
				local b = call()
				_G.f = function() return 3 end
				local c = call()
				rawset(_G, 'f', function() return 4 end)
				return a, b, c, call(), pcall(function() f = nil return call() end)");

			AssertValues(r, 1, 2, 3, 4);
			Assert.AreEqual(false, r.Tuple[4].Boolean);
		}

		[Test]
		public void FieldCache_SameInstructionOnManyTables()
		{
			// One field read on tables of the same and of different shapes, in turn.
			DynValue r = Run(@"
				local C = { x = 'c' } C.__index = C
				local list = { { x = 1 }, { y = 0, x = 2 }, setmetatable({}, C), { x = 4 }, setmetatable({ x = 5 }, C), {} }
				local out = {}
				for round = 1, 3 do
					for k, t in ipairs(list) do out[#out + 1] = tostring(t.x) end
				end
				return table.concat(out, ',')");

			Assert.AreEqual("1,2,c,4,5,nil,1,2,c,4,5,nil,1,2,c,4,5,nil", r.String);
		}

		[Test]
		public void FieldCache_TablesWithTheSameHistoryLengthButOtherLayouts()
		{
			// Both tables had two keys added, so their shape counters agree; x sits in another entry.
			DynValue r = Run(@"
				local t1 = {} t1.a = 1 t1.x = 'x1'
				local t2 = {} t2.x = 'x2' t2.b = 'b2'
				local function get(t) return t.x end
				return get(t1), get(t2), get(t1)");

			AssertValues(r, "x1", "x2", "x1");
		}

		[Test]
		public void FieldCache_HostCollectDeadKeysMovesEntries()
		{
			Script s = new Script();
			s.DoString("t = { a = 1, b = 2, c = 3 } get = function() return t.b end");
			Assert.AreEqual(2, s.DoString("return get()").Number);

			s.DoString("t.a = nil");
			s.Globals.Get("t").Table.CollectDeadKeys();
			Assert.AreEqual(2, s.DoString("return get()").Number);
		}

		[Test]
		public void FieldCache_HostRemoveOfAnArrayKeyMovesEntries()
		{
			// Removing a key of the array prefix turns the keys after it into entries placed before x.
			Script s = new Script();
			s.DoString("t = { 10, 20, 30 } t.x = 'x' get = function() return t.x end");
			Assert.AreEqual("x", s.DoString("return get()").String);

			s.Globals.Get("t").Table.Remove(2);
			Assert.AreEqual("x", s.DoString("return get()").String);
			Assert.AreEqual(30, s.DoString("return t[3]").Number);
		}

		[Test]
		public void FieldCache_EntriesMoveWhenTheTableGrows()
		{
			// Adding many keys rebuilds the hash part, so every cached entry index moves.
			DynValue r = Run(@"
				local t = { a = 1, b = 2 }
				local function get() return t.b end
				local x = get()
				for k = 1, 100 do t['k' .. k] = k end
				t.a = nil
				for k = 101, 200 do t['k' .. k] = k end
				t.b = 7
				return x, get(), t.k150");

			AssertValues(r, 2, 7, 150);
		}

		[Test]
		public void FieldCache_StoresSeeEveryChange()
		{
			DynValue r = Run(@"
				local t = { v = 0 }
				local log = {}
				local function set(x) t.v = x end
				set(1)
				local a = t.v
				set(nil)
				local b = rawget(t, 'v')
				set(2)
				local c = t.v
				setmetatable(t, { __newindex = function(tt, k, x) log[#log + 1] = x end })
				t.v = nil
				set(3) -- v is absent now: __newindex runs
				local d = rawget(t, 'v')
				return a, b, c, d, log[1]");

			AssertValues(r, 1, null, 2, null, 3);
		}

		[Test]
		public void FieldCache_CachedStoreIntoAMetatable()
		{
			// The same store instruction replaces __index twice, the second time through its cache.
			DynValue r = Run(@"
				local mt = { __index = false }
				local o = setmetatable({}, mt)
				local function setIndex(x) mt.__index = x end
				setIndex({ k = 1 })
				local a = o.k
				setIndex({ k = 2 })
				return a, o.k");

			AssertValues(r, 1, 2);
		}

		[Test]
		public void FieldCache_SharedByCoroutines()
		{
			DynValue r = Run(@"
				local A = { k = 'a' }
				local B = { k = 'b' }
				local function body(t) while true do coroutine.yield(t.k) end end
				local ca = coroutine.wrap(body)
				local cb = coroutine.wrap(body)
				local out = { ca(A), cb(B), ca(), cb() }
				A.k = 'a2'
				out[#out + 1] = ca()
				return table.concat(out, ',')");

			Assert.AreEqual("a,b,a,b,a2", r.String);
		}

		[Test]
		public void FieldCache_OneSiteOverManyObjectsOfOneLayout()
		{
			// Tables built with the same keys in the same order keep each key in the same entry, so one read
			// and one write site hit for all of them; each object keeps its own values.
			DynValue r = Run(@"
				local list = {}
				for k = 1, 8 do list[k] = { x = k, y = k * 10, z = 0.5 } end
				local function step(t) t.z = t.z + t.x * 0.25 return t.y end
				local s = 0
				for round = 1, 3 do
					for k = 1, 8 do s = s + step(list[k]) end
				end
				return s, list[1].z, list[8].z");

			AssertValues(r, 1080, 1.25, 6.5);
		}

		[Test]
		public void FieldCache_AnotherInstanceShadowsTheMethod()
		{
			// The path went through a's metatable because a has no 'name'. b, another instance of the same
			// class, has its own: the level check must look the key up in b (Table.FindStringEntry in
			// FieldCache.TryGet), not only compare it with a.
			DynValue r = Run(@"
				local C = {} C.__index = C
				function C:name() return 'class' end
				local a = setmetatable({}, C)
				local b = setmetatable({}, C)
				b.name = function() return 'own' end
				local c = setmetatable({ name = 1 }, C)
				c.name = nil -- a dead key: the lookup goes on to the class
				local function call(o) return o:name() end
				return call(a), call(b), call(a), call(c)");

			AssertValues(r, "class", "own", "class", "class");
		}

		[Test]
		public void FieldCache_InstanceWithoutAMetatableAfterOneWithIt()
		{
			// The cached path goes through a metatable; the next table has none (the null check).
			DynValue r = Run(@"
				local C = { k = 'class' } C.__index = C
				local list = { setmetatable({}, C), {}, setmetatable({}, C), { k = 'own' } }
				local out = {}
				for round = 1, 2 do
					for i, t in ipairs(list) do out[#out + 1] = tostring(t.k) end
				end
				return table.concat(out, ',')");

			Assert.AreEqual("class,nil,class,own,class,nil,class,own", r.String);
		}

		[Test]
		public void FieldCache_MetatablesWithIndexInAnotherEntry()
		{
			// mt2's first entry holds a table under another key; the cached __index entry of mt1 must be
			// checked for its key (Table.TryGetEntry), not only read.
			DynValue r = Run(@"
				local A = { k = 'A' }
				local B = { k = 'B' }
				local other = { k = 'other' }
				local o1 = setmetatable({}, { __index = A })
				local o2 = setmetatable({}, { other = other, __index = B })
				local function get(o) return o.k end
				return get(o1), get(o2), get(o1)");

			AssertValues(r, "A", "B", "A");
		}

		[Test]
		public void FieldCache_ClassesWithTheMethodInAnotherEntry()
		{
			// The holder of f is the class. B keeps another method where A keeps f, so the cached entry
			// must be checked for its key (Table.TryGetEntry).
			DynValue r = Run(@"
				local A = {} A.__index = A
				function A:f() return 'A' end
				local B = {} B.__index = B
				function B:g() return 'g' end
				function B:f() return 'B' end
				local function call(o) return o:f() end
				return call(setmetatable({}, A)), call(setmetatable({}, B)), call(setmetatable({}, A))");

			AssertValues(r, "A", "B", "A");
		}

		[Test]
		public void FieldCache_AnotherObjectWithTheKeyDead()
		{
			// b has v in the cached entry, dead: the read goes on to the class (the value check in
			// FieldCache.TryGet).
			DynValue r = Run(@"
				local C = { v = 'class' } C.__index = C
				local a = setmetatable({ v = 'a' }, C)
				local b = setmetatable({ v = 'b' }, C)
				b.v = nil
				local function get(o) return o.v end
				return get(a), get(b), get(a)");

			AssertValues(r, "a", "class", "a");
		}

		[Test]
		public void FieldCache_SmallerTableAfterALargerOne()
		{
			// The cached entry index is past the end of the next table's entries (the bounds check).
			DynValue r = Run(@"
				local big = { a = 1, b = 2, c = 3, d = 4, e = 5, x = 'big' }
				local small = { x = 'small' }
				local empty = {}
				local function get(t) return t.x end
				return get(big), get(small), get(big), get(empty)");

			AssertValues(r, "big", "small", "big", null);
		}

		[Test]
		public void FieldCache_StoreSiteOverTablesOfOtherLayouts()
		{
			// The cached entry of x in t1 holds b in t2: the store must check the key (Table.TrySetEntry).
			DynValue r = Run(@"
				local t1 = {} t1.a = 1 t1.x = 1
				local t2 = {} t2.x = 2 t2.b = 'b'
				local function set(t, v) t.x = v end
				set(t1, 10)
				set(t2, 20)
				set(t1, 11)
				return t1.x, t2.x, t2.b, t1.a");

			AssertValues(r, 11, 20, "b", 1);
		}

		[Test]
		public void FieldCache_StoreIntoAnotherObjectsDeadKeyRunsNewIndex()
		{
			// b's v is dead, so storing into it is a store to an absent key: __newindex runs (the value
			// check in Table.TrySetEntry).
			DynValue r = Run(@"
				local log = {}
				local mt = { __newindex = function(t, k, v) log[#log + 1] = v end }
				local a = setmetatable({ v = 1 }, mt)
				local b = setmetatable({ v = 2 }, mt)
				b.v = nil
				local function set(o, x) o.v = x end
				set(a, 10)
				set(b, 20)
				return a.v, rawget(b, 'v'), log[1]");

			AssertValues(r, 10, null, 20);
		}

		[Test]
		public void FieldCache_CachedStoreChecksScriptOwnership()
		{
			// A value owned by another script stored through the cached path fails as the uncached one
			// does (the ownership check in Table.TrySetEntry).
			Script other = new Script();
			Table foreign = new Table(other);
			Script s = new Script();
			s.Globals["foreign"] = DynValue.NewCallback((ctx, args) => DynValue.NewTable(foreign));
			s.DoString("t = { x = 1 } function set(v) t.x = v end set(2) set(3)");

			Assert.Throws<ScriptRuntimeException>(() => s.DoString("set(foreign())"));
			Assert.AreEqual(3, s.DoString("return t.x").Number);
		}

		[Test]
		public void FieldCache_HostChangesToOneOfManyObjects()
		{
			// Host-side Remove, Set, Clear and CollectDeadKeys on one object of a shared layout; the other
			// keeps hitting.
			Script s = new Script();
			s.DoString(@"
				C = { k = 'class' } C.__index = C
				a = setmetatable({ z = 1, k = 'a' }, C)
				b = setmetatable({ z = 2, k = 'b' }, C)
				get = function(o) return o.k end");
			Func<string, string> get = name => s.DoString("return tostring(get(" + name + "))").String;
			Table b = s.Globals.Get("b").Table;

			Assert.AreEqual("a", get("a"));
			Assert.AreEqual("b", get("b"));

			b.Remove("k");
			Assert.AreEqual("class", get("b"));
			Assert.AreEqual("a", get("a"));

			b.Set("k", DynValue.NewString("b2")); // now after z's entry and the hole k left
			Assert.AreEqual("b2", get("b"));
			Assert.AreEqual("a", get("a"));

			b.Set("z", DynValue.Nil);
			b.CollectDeadKeys(); // k moves to the first entry
			Assert.AreEqual("b2", get("b"));
			Assert.AreEqual("a", get("a"));

			b.Clear();
			Assert.AreEqual("class", get("b"));
			Assert.AreEqual("a", get("a"));

			s.DoString("rawset(a, 'k', nil)");
			Assert.AreEqual("class", get("a"));

			s.DoString("setmetatable(a, { __index = { k = 'other' } })");
			Assert.AreEqual("other", get("a"));
		}

		[Test]
		public void FieldCache_HostCollectDeadKeysMovesTheIndexEntryOfAMetatable()
		{
			// The metatable is checked by identity while its shape stays the same; collecting its dead key
			// moves "__index" one entry down and "other" into its place (the shape change in Allocate).
			Script s = new Script();
			s.DoString(@"
				A = { k = 'A' }
				B = { k = 'B' }
				mt = { a = 1, __index = A, other = B }
				o = setmetatable({}, mt)
				get = function() return o.k end
				mt.a = nil");
			Assert.AreEqual("A", s.DoString("return get()").String);

			s.Globals.Get("mt").Table.CollectDeadKeys();
			Assert.AreEqual("A", s.DoString("return get()").String);
		}

		[Test]
		public void FieldCache_HostClearOfAMetatableAndOfAnInstance()
		{
			// Clear drops the entries a cached path names (the shape change in Clear).
			Script s = new Script();
			s.DoString(@"
				C = { k = 'class' } C.__index = C
				o = setmetatable({ k = 'own' }, C)
				get = function() return o.k end
				o.k = nil");
			Assert.AreEqual("class", s.DoString("return get()").String);

			s.Globals.Get("o").Table.Clear();
			Assert.AreEqual("class", s.DoString("return get()").String);

			s.Globals.Get("C").Table.Clear();
			Assert.IsTrue(s.DoString("return get()").IsNil());
		}
	}
}
