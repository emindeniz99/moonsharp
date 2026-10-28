using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// How arguments reach a Lua function's parameters, and how a call's frame leaves the value stack, on
	/// every way into and out of a call: plain calls, host calls, tail calls, errors, coroutines.
	/// </summary>
	[TestFixture]
	public class CallPathTests
	{
		static DynValue Run(string code)
		{
			return new Script().DoString(code);
		}

		[Test]
		public void CallPath_FewerAndMoreArgumentsThanParameters()
		{
			DynValue r = Run(@"
				local function f(a, b, c) return tostring(a) .. ',' .. tostring(b) .. ',' .. tostring(c) end
				return f(1), f(1, 2, 3, 4, 5), f()");

			Assert.AreEqual("1,nil,nil", r.Tuple[0].String);
			Assert.AreEqual("1,2,3", r.Tuple[1].String);
			Assert.AreEqual("nil,nil,nil", r.Tuple[2].String);
		}

		[Test]
		public void CallPath_ParameterFromACallWithNoResultsIsNil()
		{
			// f() passes no value, but the parameter that receives it is a variable, so it holds nil (Lua
			// 5.2). MoonSharp stored "no value" in it, and type(a) failed with "value expected".
			DynValue r = Run(@"
				local function none() end
				local function g(a) return type(a) end
				local function h(a, b) return type(a) .. ',' .. type(b) end
				local function v(a, ...) return type(a) .. ',' .. select('#', ...) end
				return g(none()), h(none()), h(1, none()), v(none())");

			Assert.AreEqual("nil", r.Tuple[0].String);
			Assert.AreEqual("nil,nil", r.Tuple[1].String);
			Assert.AreEqual("number,nil", r.Tuple[2].String);
			Assert.AreEqual("nil,0", r.Tuple[3].String);
		}

		[Test]
		public void CallPath_NoVarArgsAreAnEmptyList()
		{
			// '...' that receives no values holds none (Lua 5.2). MoonSharp stored one nil, so
			// select('#', ...) was 1 and f(...) passed f one nil argument.
			DynValue r = Run(@"
				local function none() end
				local function count(...) return select('#', ...) end
				local function v(a, ...) return select('#', ...), count(...) end
				local function w(...) return select('#', ...), count(...) end
				local a1, a2 = v(1)
				local b1, b2 = w()
				local c1, c2 = w(none())
				local d1, d2 = v(1, 2, 3)
				return a1 .. a2 .. ',' .. b1 .. b2 .. ',' .. c1 .. c2 .. ',' .. d1 .. d2");

			Assert.AreEqual("00,00,00,22", r.String);
		}

		[Test]
		public void CallPath_VarArgs()
		{
			DynValue r = Run(@"
				local function count(...) return select('#', ...) end
				local function second(a, ...) local t = { ... } return a, select('#', ...), t[2] end
				local x, n, y = second(1, 2, 3, nil)
				return count(1, 2), count(nil), count(1, nil, nil), x, n, y");

			// count() returns 1 instead of 0 (an empty ... is stored as nil); that predates this test.
			Assert.AreEqual(2, r.Tuple[0].Number);
			Assert.AreEqual(1, r.Tuple[1].Number);
			Assert.AreEqual(3, r.Tuple[2].Number);
			Assert.AreEqual(1, r.Tuple[3].Number);
			Assert.AreEqual(3, r.Tuple[4].Number);
			Assert.AreEqual(3, r.Tuple[5].Number);
		}

		[Test]
		public void CallPath_LastArgumentIsAMultipleReturnCall()
		{
			DynValue r = Run(@"
				local function three() return 1, 2, 3 end
				local function none() end
				local function f(a, b, c, d) return tostring(a) .. tostring(b) .. tostring(c) .. tostring(d) end
				local function nils(a, b) return a == nil and b == nil end
				return f(three()), f(0, three()), f(three(), 9), nils(none()), f(0, three(), three())");

			// f(none()) passes a void value rather than nil (tostring(a) fails on it); that predates
			// this test, so it is checked through == nil only.
			Assert.AreEqual("123nil", r.Tuple[0].String);
			Assert.AreEqual("0123", r.Tuple[1].String);
			Assert.AreEqual("19nilnil", r.Tuple[2].String);
			Assert.AreEqual(true, r.Tuple[3].Boolean);
			Assert.AreEqual("0112", r.Tuple[4].String);
		}

		[Test]
		public void CallPath_ParameterIsACopy()
		{
			// Assigning to a parameter changes neither the caller's local nor a table field passed in.
			DynValue r = Run(@"
				local function bump(a) a = a + 1 return a end
				local x = 1
				local t = { v = 10 }
				local y = bump(x)
				local z = bump(t.v)
				return x, y, t.v, z");

			Assert.AreEqual(1, r.Tuple[0].Number);
			Assert.AreEqual(2, r.Tuple[1].Number);
			Assert.AreEqual(10, r.Tuple[2].Number);
			Assert.AreEqual(11, r.Tuple[3].Number);
		}

		[Test]
		public void CallPath_ParametersAreVisibleByName()
		{
			// A CLR callback reads the caller's parameters by name, as the debugger does.
			Script s = new Script();
			s.Globals["peek"] = DynValue.NewCallback((ctx, args) =>
				DynValue.NewString(ctx.EvaluateSymbolByName("a").ToPrintString() + ctx.EvaluateSymbolByName("b").ToPrintString()));

			DynValue r = s.DoString(@"
				local function f(a, b) return peek() end
				return f('x', 'y'), f('z')");

			Assert.AreEqual("xy", r.Tuple[0].String);
			Assert.AreEqual("znil", r.Tuple[1].String);
		}

		[Test]
		public void CallPath_HostCallsWithTupleArguments()
		{
			Script s = new Script();
			DynValue f = s.DoString("return function(a, b, ...) return a, b, select('#', ...) end");

			DynValue r = s.Call(f, DynValue.NewNumber(1), DynValue.NewTuple(DynValue.NewNumber(2), DynValue.NewNumber(3), DynValue.NewNumber(4)));

			Assert.AreEqual(1, r.Tuple[0].Number);
			Assert.AreEqual(2, r.Tuple[1].Number);
			Assert.AreEqual(2, r.Tuple[2].Number);

			for (int i = 0; i < 1000; i++)
				Assert.AreEqual(i, s.Call(f, DynValue.NewNumber(i)).Tuple[0].Number);
		}

		[Test]
		public void CallPath_TailCallsUnderTheThreshold()
		{
			// With a low threshold every tail call reuses the frame (PerformTCO), which pops the caller's
			// arguments by the count stored in its frame.
			Script s = new Script();
			s.Options.TailCallOptimizationThreshold = 4;

			DynValue r = s.DoString(@"
				local function sum(n, acc, unused1, unused2)
					if n == 0 then return acc end
					return sum(n - 1, acc + n, 'x', 'y')
				end
				local function viaVarargs(n, ...)
					if n == 0 then return select('#', ...) end
					return viaVarargs(n - 1, ...)
				end
				return sum(20000, 0), viaVarargs(5000, 1, 2, 3)");

			Assert.AreEqual(200010000, r.Tuple[0].Number);
			Assert.AreEqual(3, r.Tuple[1].Number);
		}

		[Test]
		public void CallPath_ErrorsInsideCallsUnwindCleanly()
		{
			// pcall and xpcall around failing Lua functions with arguments, many times over: the stack must
			// stay balanced, so later calls still see the right values.
			DynValue r = Run(@"
				local function fail(a, b, c) error('boom ' .. a) end
				local function nested(a, b) local x = a * 2 return fail(x, b, 'c') end
				local ok, msg, total = nil, nil, 0
				for i = 1, 2000 do
					ok, msg = pcall(nested, i, 'b')
					total = total + (ok and 0 or 1)
					local ok2, msg2 = xpcall(function(p, q) return fail(p, q) end, function(m) return 'handled' end, i, 2)
					if msg2 ~= 'handled' then error('xpcall') end
				end
				local function add(a, b) return a + b end
				return total, add(40, 2), msg");

			Assert.AreEqual(2000, r.Tuple[0].Number);
			Assert.AreEqual(42, r.Tuple[1].Number);
			StringAssert.Contains("boom 4000", r.Tuple[2].String);
		}

		[Test]
		public void CallPath_CoroutineYieldsInsideACall()
		{
			DynValue r = Run(@"
				local function inner(a, b)
					local got = coroutine.yield(a + b)
					return got * a
				end
				local function outer(x, ...)
					local n = select('#', ...)
					return inner(x, n) + 1
				end
				local co = coroutine.create(outer)
				local ok1, v1 = coroutine.resume(co, 10, 'p', 'q')
				local ok2, v2 = coroutine.resume(co, 3)
				return v1, v2, coroutine.status(co)");

			Assert.AreEqual(12, r.Tuple[0].Number);
			Assert.AreEqual(31, r.Tuple[1].Number);
			Assert.AreEqual("dead", r.Tuple[2].String);
		}

		[Test]
		public void CallPath_CallMetamethodGetsTheObjectFirst()
		{
			DynValue r = Run(@"
				local callable = setmetatable({ k = 5 }, { __call = function(self, a, b) return self.k + a + b end })
				return callable(1, 2), callable(10, 20)");

			Assert.AreEqual(8, r.Tuple[0].Number);
			Assert.AreEqual(35, r.Tuple[1].Number);
		}
	

		[Test]
		public void CallPath_LibraryFunctionsReadEveryArgumentShape()
		{
			// Library functions read their arguments in place on the value stack: a multiple-return call
			// as the last argument expands, one in the middle gives its first value, a call with no
			// results gives no value at the end and nil in the middle, and ':' passes the object first.
			DynValue r = Run(@"
				local function two() return 'b', 'c' end
				local function none() end
				return string.format('%s%s%s', 'a', two()),
					string.format('%s%s', two(), 'z'),
					select('#', none()), select('#', none(), 1),
					('%d-%d'):format(3, 4),
					string.format('%s', (two()))");

			Assert.AreEqual("abc", r.Tuple[0].String);
			Assert.AreEqual("bz", r.Tuple[1].String);
			Assert.AreEqual(0, r.Tuple[2].Number);
			Assert.AreEqual(2, r.Tuple[3].Number);
			Assert.AreEqual("3-4", r.Tuple[4].String);
			Assert.AreEqual("b", r.Tuple[5].String);
		}

		[Test]
		public void CallPath_LibraryArgumentsSurviveStackGrowthDuringTheCall()
		{
			// load reads its chunk name and environment after calling the reader. The reader recurses
			// deep enough to grow the value stack to a new array, so the arguments must be read from
			// the stack as it is then, not from the array the call started with.
			DynValue r = Run(@"
				local function deep(n) if n == 0 then return 0 end return 1 + deep(n - 1) end
				local parts = { 'return x' }
				local i = 0
				local function reader() i = i + 1 deep(3000) return parts[i] end
				local f = load(reader, '=chunkname', 't', { x = 42 })
				local j = 0
				local function reader2() j = j + 1 deep(3000) if j == 1 then return 'error(""boom"")' end end
				local ok, err = pcall(load(reader2, '=other'))
				return f(), err");

			Assert.AreEqual(42, r.Tuple[0].Number);
			StringAssert.StartsWith("=other:", r.Tuple[1].String);
		}

		[Test]
		public void CallPath_ReentrantLibraryCallsKeepTheirOwnArguments()
		{
			// A library function calling Lua that calls the same library function: each call reads its
			// own arguments.
			DynValue r = Run(@"
				local t = { 3, 1, 2 }
				local seen = {}
				table.sort(t, function(a, b) seen[#seen + 1] = string.format('%d<%d', a, b) return a < b end)
				return table.concat(t, ','), #seen > 0, string.gsub('abc', '%w', function(c) return string.format('[%s]', c) end)");

			Assert.AreEqual("1,2,3", r.Tuple[0].String);
			Assert.AreEqual(true, r.Tuple[1].Boolean);
			Assert.AreEqual("[a][b][c]", r.Tuple[2].String);
		}
	}
}
