using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Field and global accesses with a constant name cache where they found the key. Each test runs the
	/// same instruction (inside one function) before and after a change that must make the cached place
	/// wrong, and checks that the second run sees the change.
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
	}
}
