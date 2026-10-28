using System;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// The typed calls from C# to Lua (<see cref="Closure.Call{T1, TResult}"/> and its arities) skip the
	/// boxing, the argument array and the DynValues of <see cref="Closure.Call(object[])"/>, but must convert
	/// arguments and results exactly as it does: a host that switches to them must see the same values,
	/// the same errors and the same custom converters.
	/// </summary>
	[TestFixture]
	public class TypedCallTests
	{
		static Closure Fn(Script s, string code)
		{
			return s.DoString(code).Function;
		}

		[Test]
		public void TypedCall_ArgumentsArriveAsTheUntypedCallPassesThem()
		{
			Script s = new Script();
			Closure describe = Fn(s, "return function(...) local t = {} for i = 1, select('#', ...) do local v = select(i, ...) t[i] = type(v) .. ':' .. tostring(v) end return table.concat(t, ',') end");
			Table table = new Table(s);
			Closure f = Fn(s, "return function() end");

			Assert.AreEqual(describe.Call(1, 2.5, true, "x").String, describe.Call<int, double, bool, string, string>(1, 2.5, true, "x"));
			Assert.AreEqual(describe.Call(1.5f, 7L, false, null).String, describe.Call<float, long, bool, string, string>(1.5f, 7L, false, null));
			Assert.AreEqual(describe.Call(table, f).String, describe.Call<Table, Closure, string>(table, f));
			Assert.AreEqual("number:3,nil:nil,string:4", describe.Call<DynValue, DynValue, object, string>(DynValue.NewNumber(3), DynValue.Nil, "4"));
			Assert.AreEqual("", describe.Call<string>());
		}

		[Test]
		public void TypedCall_ATupleArgumentExpandsOnlyInLastPlace()
		{
			// Internal_AdjustTuple's rule for Script.Call: a tuple gives all its values as the last
			// argument and its first value anywhere else.
			Script s = new Script();
			Closure count = Fn(s, "return function(...) return select('#', ...) end");
			DynValue tuple = DynValue.NewTuple(DynValue.NewNumber(1), DynValue.NewNumber(2), DynValue.NewNumber(3));

			Assert.AreEqual(count.Call(1, tuple).Number, count.Call<int, DynValue, int>(1, tuple));
			Assert.AreEqual(4, count.Call<int, DynValue, int>(1, tuple));
			Assert.AreEqual(count.Call(tuple, 1).Number, count.Call<DynValue, int, int>(tuple, 1));
			Assert.AreEqual(2, count.Call<DynValue, int, int>(tuple, 1));
		}

		[Test]
		public void TypedCall_ResultsConvertAsToObjectConvertsThem()
		{
			Script s = new Script();

			Assert.AreEqual(7.5, Fn(s, "return function() return 7.5 end").Call<double>());
			Assert.AreEqual(7.5f, Fn(s, "return function() return 7.5 end").Call<float>());
			Assert.AreEqual(8, Fn(s, "return function() return 7.5 end").Call<int>(), "Convert.ToInt32 rounds half to even, as DynValue.ToObject<int> does");
			Assert.AreEqual(1L << 40, Fn(s, "return function() return 2^40 end").Call<long>());
			Assert.AreEqual(true, Fn(s, "return function() return true end").Call<bool>());
			Assert.AreEqual("12", Fn(s, "return function() return 12 end").Call<string>(), "a number converts to a string, as ToObject<string> does");
			Assert.AreEqual(null, Fn(s, "return function() return nil end").Call<string>());
			Assert.AreEqual(null, Fn(s, "return function() end").Call<Table>());
			Assert.AreEqual(3.0, Fn(s, "return function() return 3 end").Call<object>());

			Table t = Fn(s, "return function() return { 1, 2 } end").Call<Table>();
			Assert.AreEqual(2, t.Length);
			Assert.IsNotNull(Fn(s, "return function() return function() end end").Call<Closure>());
		}

		[Test]
		public void TypedCall_FirstResultForTypedValues_AllResultsForDynValue()
		{
			Script s = new Script();
			Closure two = Fn(s, "return function() return 1, 2 end");

			Assert.AreEqual(1.0, two.Call<double>());

			DynValue all = two.Call<DynValue>();
			Assert.AreEqual(DataType.Tuple, all.Type);
			Assert.AreEqual(2, all.Tuple.Length);
			Assert.AreEqual(two.Call().Tuple.Length, all.Tuple.Length);
		}

		[Test]
		public void TypedCall_ConversionErrorsAreThoseOfToObject()
		{
			Script s = new Script();
			Closure text = Fn(s, "return function() return 'abc' end");
			Closure huge = Fn(s, "return function() return 2^40 end");
			Closure none = Fn(s, "return function() end");

			Assert.AreEqual(Message(() => text.Call().ToObject<double>()), Message(() => text.Call<double>()));
			Assert.AreEqual(Message(() => huge.Call().ToObject<int>()), Message(() => huge.Call<int>()));
			Assert.AreEqual(Message(() => none.Call().ToObject<bool>()), Message(() => none.Call<bool>()));
		}

		static string Message(Action a)
		{
			try
			{
				a();
			}
			catch (Exception ex)
			{
				return ex.GetType().Name + ": " + ex.Message;
			}

			return "no exception";
		}

		[Test]
		public void TypedCall_LongFollowsExactInt64Interop()
		{
			Script s = new Script(CoreModules.Preset_Complete | CoreModules.Int64);
			Closure type = Fn(s, "return function(v) return type(v) .. ':' .. tostring(v) end");

			Assert.AreEqual(type.Call(long.MaxValue).String, type.Call<long, string>(long.MaxValue));

			s.Options.ExactInt64Interop = true;
			Assert.AreEqual(type.Call(long.MaxValue).String, type.Call<long, string>(long.MaxValue));
			Assert.AreEqual("userdata:9223372036854775807", type.Call<long, string>(long.MaxValue));
		}

		[Test]
		public void TypedCall_CustomConvertersStillApply()
		{
			Script s = new Script();
			Closure echo = Fn(s, "return function(v) return v end");

			Script.GlobalOptions.CustomConverters.SetClrToScriptCustomConversion<int>((sc, v) => DynValue.NewString("int " + v));
			Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion(DataType.Number, typeof(double), v => v.Number * 10);

			try
			{
				Assert.AreEqual("int 5", echo.Call<int, string>(5));
				Assert.AreEqual(echo.Call(5).String, echo.Call<int, string>(5));
				Assert.AreEqual(25.0, echo.Call<double, double>(2.5));
				Assert.AreEqual(echo.Call(2.5).ToObject<double>(), echo.Call<double, double>(2.5));
			}
			finally
			{
				Script.GlobalOptions.CustomConverters.Clear();
			}

			Assert.AreEqual(5, echo.Call<int, int>(5));
		}

		[Test]
		public void TypedCall_RefusesValuesOfAnotherScript()
		{
			Script s = new Script();
			Script other = new Script();
			Closure echo = Fn(s, "return function(v) return v end");
			Table foreign = new Table(other);

			string expected = Message(() => echo.Call(foreign));
			Assert.AreNotEqual("no exception", expected);
			Assert.AreEqual(expected, Message(() => echo.Call<Table, Table>(foreign)));
			Assert.AreEqual(expected, Message(() => echo.Call<DynValue, Table>(DynValue.NewTable(foreign))));
			Assert.AreEqual(expected, Message(() => echo.Call<object, Table>(foreign)));
		}

		[Test]
		public void TypedCall_AnErrorLeavesTheScriptUsable()
		{
			// A failed call must drop the function, arguments and frame it pushed, as Script.Call does
			// (ErrorHandlingTests): 4 arguments leak 5 of the 131072 stack slots per call, so this loop would
			// overflow the stack.
			Script s = new Script();
			Closure fail = Fn(s, "return function(a, b, c, d) error('boom ' .. a) end");
			Closure add = Fn(s, "return function(a, b) return a + b end");

			StringAssert.Contains("boom 7", Assert.Throws<ScriptRuntimeException>(() => fail.Call<int, int, int, int, int>(7, 2, 3, 4)).Message);

			for (int i = 0; i < 27000; i++)
				Assert.Throws<ScriptRuntimeException>(() => fail.Call<int, int, int, int, int>(i, 2, 3, 4));

			Assert.AreEqual(3, add.Call<int, int, int>(1, 2));
		}

		[Test]
		public void TypedCall_FromInsideACoroutine()
		{
			// A C# function running inside a coroutine calls back into Lua: the call must run on the
			// coroutine's stack, as Script.Call does.
			Script s = new Script();
			Closure square = Fn(s, "return function(x) return x * x end");
			s.Globals["square"] = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(square.Call<double, double>(args[0].Number)));

			DynValue result = s.DoString(@"
				local co = coroutine.create(function(a)
					local b = coroutine.yield(square(a))
					return square(b) + 1
				end)
				local _, first = coroutine.resume(co, 3)
				local _, second = coroutine.resume(co, 4)
				return first, second");

			Assert.AreEqual(9, result.Tuple[0].Number);
			Assert.AreEqual(17, result.Tuple[1].Number);
		}
	}
}
