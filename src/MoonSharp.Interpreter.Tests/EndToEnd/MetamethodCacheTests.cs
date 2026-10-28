using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// A metatable remembers which metamethods it was found without, so the next lookup skips the hash
	/// lookup. Every way of giving it the metamethod afterwards must be seen: assignment, rawset, a table
	/// constructor cannot (it makes a new table), the host's Table.Set, and removing then re-adding.
	/// Each test first runs the operation without the metamethod, so the absence is remembered, then adds
	/// the metamethod and runs it again.
	/// </summary>
	[TestFixture]
	public class MetamethodCacheTests
	{
		static DynValue Run(string code)
		{
			return new Script().DoString(code);
		}

		[Test]
		public void MetamethodCache_IndexAddedByRawsetAfterAMiss()
		{
			DynValue r = Run(@"
				local mt = {}
				local a = setmetatable({}, mt)
				local before = a.x
				rawset(mt, '__index', function(t, k) return k .. '!' end)
				return before, a.x");

			Assert.IsTrue(r.Tuple[0].IsNil());
			Assert.AreEqual("x!", r.Tuple[1].String);
		}

		[Test]
		public void MetamethodCache_NewIndexAddedAfterAMiss()
		{
			DynValue r = Run(@"
				local mt = {}
				local log = {}
				local a = setmetatable({}, mt)
				a.y = 1
				mt.__newindex = function(t, k, v) log[#log + 1] = k end
				a.z = 2
				return rawget(a, 'y'), rawget(a, 'z'), log[1]");

			Assert.AreEqual(1, r.Tuple[0].Number);
			Assert.IsTrue(r.Tuple[1].IsNil());
			Assert.AreEqual("z", r.Tuple[2].String);
		}

		[Test]
		public void MetamethodCache_OperatorsAddedAfterAMiss()
		{
			// Each operator runs once without its metamethod (raw result or a caught error), then with it.
			DynValue r = Run(@"
				local mt = {}
				local a, b = setmetatable({ 1, 2 }, mt), setmetatable({}, mt)
				local before = { a == b, #a, pcall(function() return a + 1 end), pcall(function() return -a end),
					pcall(function() return a .. 'x' end), pcall(function() return a < b end),
					pcall(function() return a <= b end), pcall(function() return a() end) }
				mt.__eq = function() return true end
				mt.__len = function() return 42 end
				mt.__add = function() return 'add' end
				mt.__unm = function() return 'unm' end
				mt.__concat = function() return 'concat' end
				mt.__lt = function() return true end
				mt.__le = function() return true end
				mt.__call = function() return 'call' end
				return before[1], before[2], before[3], before[5], before[7], before[9], before[11], before[13],
					a == b, #a, a + 1, -a, a .. 'x', a < b, a <= b, a()");

			Assert.AreEqual(false, r.Tuple[0].Boolean);
			Assert.AreEqual(2, r.Tuple[1].Number);

			for (int k = 2; k < 8; k++)
				Assert.AreEqual(false, r.Tuple[k].Boolean, "operator " + k + " succeeded without its metamethod");

			Assert.AreEqual(true, r.Tuple[8].Boolean);
			Assert.AreEqual(42, r.Tuple[9].Number);
			Assert.AreEqual("add", r.Tuple[10].String);
			Assert.AreEqual("unm", r.Tuple[11].String);
			Assert.AreEqual("concat", r.Tuple[12].String);
			Assert.AreEqual(true, r.Tuple[13].Boolean);
			Assert.AreEqual(true, r.Tuple[14].Boolean);
			Assert.AreEqual("call", r.Tuple[15].String);
		}

		[Test]
		public void MetamethodCache_ArithmeticEventsAreSeparate()
		{
			// A miss on one event must not hide another one the metatable has.
			DynValue r = Run(@"
				local mt = { __mul = function() return 'mul' end }
				local a = setmetatable({}, mt)
				local ok = pcall(function() return a + 1 end)
				return ok, a * 2, pcall(function() return a / 2 end)");

			Assert.AreEqual(false, r.Tuple[0].Boolean);
			Assert.AreEqual("mul", r.Tuple[1].String);
			Assert.AreEqual(false, r.Tuple[2].Boolean);
		}

		[Test]
		public void MetamethodCache_HostSetAfterAMiss()
		{
			Script s = new Script();
			s.DoString("mt = {} a = setmetatable({}, mt) before = a.x");

			Table mt = s.Globals.Get("mt").Table;
			Table proxy = new Table(s);
			proxy.Set("x", DynValue.NewNumber(7));
			mt.Set("__index", DynValue.NewTable(proxy));

			Assert.AreEqual(7, s.DoString("return a.x").Number);
		}

		[Test]
		public void MetamethodCache_RemovedThenAddedAgain()
		{
			DynValue r = Run(@"
				local mt = { __index = function() return 1 end }
				local a = setmetatable({}, mt)
				local first = a.x
				mt.__index = nil
				local second = a.x
				mt.__index = function() return 3 end
				return first, second, a.x");

			Assert.AreEqual(1, r.Tuple[0].Number);
			Assert.IsTrue(r.Tuple[1].IsNil());
			Assert.AreEqual(3, r.Tuple[2].Number);
		}

		[Test]
		public void MetamethodCache_HostClearThenSet()
		{
			Script s = new Script();
			s.DoString("mt = { __index = function() return 1 end } a = setmetatable({}, mt)");
			Table mt = s.Globals.Get("mt").Table;

			mt.Clear();
			Assert.IsTrue(s.DoString("return a.x").IsNil());

			mt.Set("__index", s.DoString("return function() return 2 end"));
			Assert.AreEqual(2, s.DoString("return a.x").Number);
		}

		[Test]
		public void MetamethodCache_SharedMetatable()
		{
			// The absence belongs to the metatable, so every table using it sees the new metamethod.
			DynValue r = Run(@"
				local mt = {}
				local a, b = setmetatable({}, mt), setmetatable({}, mt)
				local before = a.k
				mt.__index = { k = 'v' }
				return before, a.k, b.k");

			Assert.IsTrue(r.Tuple[0].IsNil());
			Assert.AreEqual("v", r.Tuple[1].String);
			Assert.AreEqual("v", r.Tuple[2].String);
		}
	}
}
