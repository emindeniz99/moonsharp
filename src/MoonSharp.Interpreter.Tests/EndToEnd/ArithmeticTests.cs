using System;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Arithmetic against Lua 5.2 (reference manual 3.4.1): a % b is a - math.floor(a/b)*b, so the
	/// result takes the sign of the divisor.
	/// https://www.lua.org/manual/5.2/manual.html#3.4.1
	/// </summary>
	[TestFixture]
	public class ArithmeticTests
	{
		static double LuaMod(double a, double b)
		{
			return a - Math.Floor(a / b) * b;
		}

		[Test]
		public void Arithmetic_ModuloTakesTheSignOfTheDivisor()
		{
			DynValue r = new Script().DoString(@"
				return 5 % 3, -5 % 3, 5 % -3, -5 % -3, 5.5 % 2, -5.5 % 2, 5.5 % -2, 6 % -3, -6 % 3, 0 % -3, 7.25 % 0.5");

			double[] expected = { 2, 1, -1, -2, 1.5, 0.5, -0.5, 0, 0, 0, 0.25 };

			for (int i = 0; i < expected.Length; i++)
				Assert.AreEqual(expected[i], r.Tuple[i].Number, "result " + (i + 1));
		}

		[Test]
		public void Arithmetic_ModuloSpecialValues()
		{
			// Whatever the formula gives for zero, infinite and NaN operands, as in Lua 5.2.
			DynValue r = new Script().DoString(@"
				local inf, nan = 1/0, 0/0
				return 5 % 0, 5 % inf, -5 % inf, inf % 5, nan % 5, 5 % nan, -0.0 % 5, 5 % -inf");

			double[,] operands = { { 5, 0 }, { 5, double.PositiveInfinity }, { -5, double.PositiveInfinity },
				{ double.PositiveInfinity, 5 }, { double.NaN, 5 }, { 5, double.NaN }, { -0.0, 5 }, { 5, double.NegativeInfinity } };

			for (int i = 0; i < operands.GetLength(0); i++)
			{
				double expected = LuaMod(operands[i, 0], operands[i, 1]);
				double actual = r.Tuple[i].Number;
				Assert.IsTrue(expected.Equals(actual), string.Format("result {0}: expected {1}, got {2}", i + 1, expected, actual));
			}
		}

		[Test]
		public void Arithmetic_ModuloInDynamicExpressions()
		{
			Script s = new Script();
			Assert.AreEqual(-1, s.CreateDynamicExpression("5 % -3").Evaluate().Number);
			Assert.AreEqual(-2, s.CreateDynamicExpression("-5 % -3").Evaluate().Number);
			Assert.AreEqual(1, s.CreateDynamicExpression("-5 % 3").Evaluate().Number);
		}

		[Test]
		public void Arithmetic_StringsCoerceToNumbers()
		{
			DynValue r = new Script().DoString(@"return '10' + 1, 2 * '3', '7' % '4', -'2', '2' ^ 3");

			Assert.AreEqual(11, r.Tuple[0].Number);
			Assert.AreEqual(6, r.Tuple[1].Number);
			Assert.AreEqual(3, r.Tuple[2].Number);
			Assert.AreEqual(-2, r.Tuple[3].Number);
			Assert.AreEqual(8, r.Tuple[4].Number);
		}

		[Test]
		public void Arithmetic_NumbersCoerceToStringsWithoutCulture()
		{
			// .. and string functions turn numbers into the text tostring gives, on every device.
			foreach (string culture in new[] { "de-DE", "tr-TR", "fr-FR" })
			{
				DynValue r = Utils.WithCulture(culture, () => new Script().DoString(
					"return 1.5 .. '', 'x' .. -0.25, string.format('%s', 2.75), string.rep(1.5, 2), ('a'):rep(2, 0.5), tostring(1.5)"));

				Assert.AreEqual("1.5", r.Tuple[0].String, culture);
				Assert.AreEqual("x-0.25", r.Tuple[1].String, culture);
				Assert.AreEqual("2.75", r.Tuple[2].String, culture);
				Assert.AreEqual("1.51.5", r.Tuple[3].String, culture);
				Assert.AreEqual("a0.5a", r.Tuple[4].String, culture);
				Assert.AreEqual("1.5", r.Tuple[5].String, culture);
			}
		}

		[Test]
		public void Arithmetic_MetamethodsOnTables()
		{
			DynValue r = new Script().DoString(@"
				local mt = {}
				for _, op in ipairs({ 'add', 'sub', 'mul', 'div', 'mod', 'pow' }) do
					mt['__' .. op] = function(a, b)
						local x = type(a) == 'table' and a.v or a
						local y = type(b) == 'table' and b.v or b
						return op .. ':' .. x .. ':' .. y
					end
				end
				mt.__unm = function(a) return 'unm:' .. a.v end
				local t = setmetatable({ v = 7 }, mt)
				return t + 1, 2 - t, t * t, t / 2, 3 % t, t ^ 2, -t");

			string[] expected = { "add:7:1", "sub:2:7", "mul:7:7", "div:7:2", "mod:3:7", "pow:7:2", "unm:7" };

			for (int i = 0; i < expected.Length; i++)
				Assert.AreEqual(expected[i], r.Tuple[i].String);
		}

		[Test]
		public void Arithmetic_ErrorsOnNonNumbers()
		{
			Assert.AreEqual("attempt to perform arithmetic on a nil value",
				Assert.Throws<ScriptRuntimeException>(() => new Script().DoString("local x return x + 1")).Message);
			Assert.AreEqual("attempt to perform arithmetic on a table value",
				Assert.Throws<ScriptRuntimeException>(() => new Script().DoString("return 1 % {}")).Message);
			Assert.AreEqual("attempt to perform arithmetic on a string value",
				Assert.Throws<ScriptRuntimeException>(() => new Script().DoString("return 'x' * 2")).Message);
			Assert.AreEqual("attempt to perform arithmetic on a boolean value",
				Assert.Throws<ScriptRuntimeException>(() => new Script().DoString("return -true")).Message);
		}
	}
}
