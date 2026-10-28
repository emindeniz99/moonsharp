using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Order comparisons against Lua 5.2 (reference manual 3.4.3 and 2.4): a &gt; b is b &lt; a and
	/// a &gt;= b is b &lt;= a, not "not (a &lt;= b)" and "not (a &lt; b)". The two differ for NaN and for
	/// tables whose __le and __lt are not each other's complement.
	/// https://www.lua.org/manual/5.2/manual.html#3.4.3
	/// </summary>
	[TestFixture]
	public class ComparisonOperatorTests
	{
		static DynValue Run(string code)
		{
			return new Script().DoString(code);
		}

		[Test]
		public void Comparison_NaNIsNeverGreater()
		{
			// Every order comparison with NaN is false in IEEE 754 and in Lua.
			DynValue r = Run(@"
				local nan = 0/0
				return nan > 1, nan >= 1, 1 > nan, 1 >= nan, nan < 1, nan <= 1, nan > nan, nan >= nan");

			Assert.AreEqual(DataType.Tuple, r.Type);
			Assert.AreEqual(8, r.Tuple.Length);

			for (int i = 0; i < r.Tuple.Length; i++)
				Assert.AreEqual(DataType.Boolean, r.Tuple[i].Type, "result " + (i + 1));

			for (int i = 0; i < r.Tuple.Length; i++)
				Assert.IsFalse(r.Tuple[i].Boolean, "result " + (i + 1));
		}

		[Test]
		public void Comparison_NaNBranches()
		{
			// The same through branches, where a wrong "not (a <= b)" would take the then-branch.
			DynValue r = Run(@"
				local nan, hits = 0/0, 0
				if nan > 1 then hits = hits + 1 end
				if nan >= 1 then hits = hits + 1 end
				if 1 > nan then hits = hits + 1 end
				if 1 >= nan then hits = hits + 1 end
				return hits");

			Assert.AreEqual(0, r.Number);
		}

		[Test]
		public void Comparison_GreaterCallsLtWithSwappedOperands()
		{
			// x > y is __lt(y, x), even when __le exists.
			DynValue r = Run(@"
				local log = {}
				local mt = {
					__lt = function(a, b) log[#log + 1] = 'lt ' .. a.n .. ' ' .. b.n return true end,
					__le = function(a, b) log[#log + 1] = 'le ' .. a.n .. ' ' .. b.n return true end,
				}
				local x = setmetatable({ n = 'x' }, mt)
				local y = setmetatable({ n = 'y' }, mt)
				local r = x > y
				return r, table.concat(log, ',')");

			Assert.AreEqual(true, r.Tuple[0].Boolean);
			Assert.AreEqual("lt y x", r.Tuple[1].String);
		}

		[Test]
		public void Comparison_GreaterOrEqualCallsLeWithSwappedOperands()
		{
			// x >= y is __le(y, x), even when __lt exists.
			DynValue r = Run(@"
				local log = {}
				local mt = {
					__lt = function(a, b) log[#log + 1] = 'lt ' .. a.n .. ' ' .. b.n return true end,
					__le = function(a, b) log[#log + 1] = 'le ' .. a.n .. ' ' .. b.n return true end,
				}
				local x = setmetatable({ n = 'x' }, mt)
				local y = setmetatable({ n = 'y' }, mt)
				local r = x >= y
				return r, table.concat(log, ',')");

			Assert.AreEqual(true, r.Tuple[0].Boolean);
			Assert.AreEqual("le y x", r.Tuple[1].String);
		}

		[Test]
		public void Comparison_GreaterOrEqualWithOnlyLtFallsBack()
		{
			// Without __le, a <= b is "not __lt(b, a)" (Lua 5.2), so x >= y is "not __lt(x, y)".
			DynValue r = Run(@"
				local log = {}
				local mt = { __lt = function(a, b) log[#log + 1] = a.n .. '<' .. b.n return a.v < b.v end }
				local x = setmetatable({ n = 'x', v = 2 }, mt)
				local y = setmetatable({ n = 'y', v = 1 }, mt)
				return x >= y, y >= x, x > y, y > x, table.concat(log, ',')");

			Assert.AreEqual(true, r.Tuple[0].Boolean);
			Assert.AreEqual(false, r.Tuple[1].Boolean);
			Assert.AreEqual(true, r.Tuple[2].Boolean);
			Assert.AreEqual(false, r.Tuple[3].Boolean);
			Assert.AreEqual("x<y,y<x,y<x,x<y", r.Tuple[4].String);
		}

		[Test]
		public void Comparison_GreaterUsesLeOnlyThroughLt()
		{
			// With only __le, x > y has no metamethod to call (it needs __lt), as in Lua 5.2.
			Assert.Throws<ScriptRuntimeException>(() => Run(@"
				local mt = { __le = function(a, b) return true end }
				local x, y = setmetatable({}, mt), setmetatable({}, mt)
				return x > y"));

			DynValue r = Run(@"
				local mt = { __le = function(a, b) return a.v <= b.v end }
				local x = setmetatable({ v = 2 }, mt)
				local y = setmetatable({ v = 1 }, mt)
				return x >= y, y >= x");

			Assert.AreEqual(true, r.Tuple[0].Boolean);
			Assert.AreEqual(false, r.Tuple[1].Boolean);
		}

		[Test]
		public void Comparison_NumbersAndStrings()
		{
			DynValue r = Run(@"
				return 2 > 1, 1 > 2, 2 >= 2, 1 >= 2, 'b' > 'a', 'a' > 'b', 'a' >= 'a', 'a' >= 'b', 'Z' > 'a', 'abc' > 'ab'");

			bool[] expected = { true, false, true, false, true, false, true, false, false, true };

			for (int i = 0; i < expected.Length; i++)
				Assert.AreEqual(expected[i], r.Tuple[i].Boolean, "result " + (i + 1));
		}

		[Test]
		public void Comparison_EvaluatesOperandsLeftToRight()
		{
			DynValue r = Run(@"
				local log = {}
				local function v(name, x) log[#log + 1] = name return x end
				local a = v('a', 1) > v('b', 2)
				local b = v('c', 1) >= v('d', 2)
				return table.concat(log)");

			Assert.AreEqual("abcd", r.String);
		}

		[Test]
		public void Comparison_ErrorNamesOperandsInSourceOrder()
		{
			// The message keeps the operand order of the source, as before.
			Assert.AreEqual("attempt to compare number with string", CompareError("return 1 > 'x'"));
			Assert.AreEqual("attempt to compare number with string", CompareError("return 1 >= 'x'"));
			Assert.AreEqual("attempt to compare string with number", CompareError("return 'x' > 1"));
			Assert.AreEqual("attempt to compare number with nil", CompareError("return 1 < nil"));
			Assert.AreEqual("attempt to compare number with nil", CompareError("return 1 <= nil"));
			Assert.AreEqual("attempt to compare two table values", CompareError("return {} > {}"));
		}

		static string CompareError(string code)
		{
			ScriptRuntimeException ex = Assert.Throws<ScriptRuntimeException>(() => Run(code));
			return ex.Message;
		}

		[Test]
		public void Comparison_ConstantEvaluator()
		{
			// Dynamic expressions take the same definition.
			Script s = new Script();
			s.Globals["nan"] = double.NaN;

			Assert.IsFalse(s.CreateDynamicExpression("nan > 1").Evaluate().Boolean);
			Assert.IsFalse(s.CreateDynamicExpression("nan >= 1").Evaluate().Boolean);
			Assert.IsFalse(s.CreateDynamicExpression("1 > nan").Evaluate().Boolean);
			Assert.IsTrue(s.CreateDynamicExpression("2 > 1").Evaluate().Boolean);
			Assert.IsTrue(s.CreateDynamicExpression("2 >= 2").Evaluate().Boolean);
			Assert.IsFalse(s.CreateDynamicExpression("1 >= 2").Evaluate().Boolean);
		}

		[Test]
		public void Comparison_ResultsAreReadOnly()
		{
			// Comparison results are the shared read-only booleans, like the literals true and false.
			// Assigning to one throws instead of changing it.
			DynValue lt = Run("return 1 < 2");
			DynValue eq = Run("return 1 == 2");
			DynValue not = Run("local x = nil return not x");

			Assert.IsTrue(lt.ReadOnly);
			Assert.IsTrue(eq.ReadOnly);
			Assert.IsTrue(not.ReadOnly);
			Assert.Throws<ScriptRuntimeException>(() => lt.Assign(DynValue.False));
			Assert.IsTrue(lt.Boolean);
			Assert.IsTrue(DynValue.True.Boolean);
			Assert.IsTrue(Run("return 1 < 2").Boolean);
		}
	}
}
