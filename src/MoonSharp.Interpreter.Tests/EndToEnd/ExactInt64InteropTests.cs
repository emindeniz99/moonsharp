using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Tests for passing int64/uint64 values between Lua and CLR long/ulong members.
	/// Before this, a C# method returning long.MaxValue gave Lua 9.223372036854776E+18, and passing
	/// that back to a long parameter failed; values between 2^53 and 2^63 came back changed.
	/// </summary>
	[TestFixture]
	public class ExactInt64InteropTests
	{
		public class Int64Api
		{
			public long LastLong;
			public ulong LastULong;

			public long LongProp { get; set; }
			public long LongField;
			public ulong ULongProp { get; set; }

			public long MaxLong() { return long.MaxValue; }
			public long MinLong() { return long.MinValue; }
			public ulong MaxULong() { return ulong.MaxValue; }
			public int MaxInt() { return int.MaxValue; }

			public long Echo(long v) { LastLong = v; return v; }
			public ulong EchoU(ulong v) { LastULong = v; return v; }
			public long? EchoNullable(long? v) { return v; }
			public int EchoInt(int v) { return v; }
			public byte EchoByte(byte v) { return v; }
			public decimal EchoDecimal(decimal v) { return v; }
			public double EchoDouble(double v) { return v; }
			public object EchoObject(object v) { return v; }

			public string Overload(long v) { return "long"; }
			public string Overload(double v) { return "double"; }
			public string Overload(string v) { return "string"; }

			public string Narrow(int v) { return "int"; }
			public string Narrow(string v) { return "string"; }

			public bool TryGetId(out long id) { id = long.MaxValue; return true; }
		}

		private static Script NewScript(bool exact, Int64Api api)
		{
			UserData.RegisterType<Int64Api>();
			Script s = new Script(CoreModules.Preset_Complete | CoreModules.Int64);
			s.Options.ExactInt64Interop = exact;
			s.Globals["api"] = api;
			return s;
		}

		[Test]
		public void Interop_Default_LongStillBecomesNumber()
		{
			// The opt-in exists so that existing hosts see no change: by default a long result is
			// still a (rounded) number, and passing it back above 2^63-1 still fails.
			Int64Api api = new Int64Api();
			Script s = NewScript(false, api);

			DynValue v = s.DoString("return api:MaxLong()");
			Assert.AreEqual(DataType.Number, v.Type);
			Assert.AreEqual(9.223372036854776E+18, v.Number);
			Assert.AreEqual(DataType.Number, s.DoString("return api:MaxULong()").Type);

			Assert.Throws<ScriptRuntimeException>(() => s.DoString("return api:Echo(api:MaxLong())"));
			Assert.IsFalse(Script.DefaultOptions.ExactInt64Interop);
		}

		[Test]
		public void Interop_Default_AcceptsInt64Arguments()
		{
			// Accepting int64 for long parameters needs no option: a script can always build an
			// exact value and hand it to the host.
			Int64Api api = new Int64Api();
			Script s = NewScript(false, api);

			s.DoString("api:Echo(int64('9007199254740993'))");
			Assert.AreEqual(9007199254740993L, api.LastLong);

			s.DoString("api:EchoU(uint64.max)");
			Assert.AreEqual(ulong.MaxValue, api.LastULong);

			// Plain numbers keep working for long parameters.
			s.DoString("api:Echo(42)");
			Assert.AreEqual(42L, api.LastLong);
		}

		[Test]
		public void Interop_OptIn_LongRoundTripsExactly()
		{
			Int64Api api = new Int64Api();
			Script s = NewScript(true, api);

			long[] values = { long.MinValue, long.MaxValue, 9007199254740991L, 9007199254740992L, 9007199254740993L, 0, -1, -9007199254740993L };

			foreach (long value in values)
			{
				s.Globals["text"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
				DynValue r = s.DoString("local v = api:Echo(int64(text)) return int64.is(v) and tostring(v)");
				Assert.AreEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture), r.String);
				Assert.AreEqual(value, api.LastLong);
			}

			// A value that starts in the CLR survives the trip through Lua and back.
			s.DoString("api:Echo(api:MaxLong())");
			Assert.AreEqual(long.MaxValue, api.LastLong);
			s.DoString("api:Echo(api:MinLong())");
			Assert.AreEqual(long.MinValue, api.LastLong);
			s.DoString("api:Echo(api:MaxLong() - 1)");
			Assert.AreEqual(long.MaxValue - 1, api.LastLong);
		}

		[Test]
		public void Interop_OptIn_ULongRoundTripsExactly()
		{
			Int64Api api = new Int64Api();
			Script s = NewScript(true, api);

			Assert.AreEqual("18446744073709551615", s.DoString("return tostring(api:MaxULong())").String);
			Assert.IsTrue(s.DoString("return uint64.is(api:MaxULong())").Boolean);

			s.DoString("api:EchoU(api:MaxULong())");
			Assert.AreEqual(ulong.MaxValue, api.LastULong);
			s.DoString("api:EchoU(uint64('9007199254740993'))");
			Assert.AreEqual(9007199254740993UL, api.LastULong);
			s.DoString("api:EchoU(uint64(0))");
			Assert.AreEqual(0UL, api.LastULong);
		}

		[Test]
		public void Interop_OptIn_OnlyAffectsLongAndULong()
		{
			// int and smaller fit in a double exactly, so they stay numbers.
			Script s = NewScript(true, new Int64Api());
			Assert.AreEqual(DataType.Number, s.DoString("return api:MaxInt()").Type);
		}

		[Test]
		public void Interop_OptIn_PropertiesFieldsAndOutParameters()
		{
			Int64Api api = new Int64Api();
			Script s = NewScript(true, api);

			s.DoString("api.LongProp = int64.max api.LongField = int64.min api.ULongProp = uint64.max");
			Assert.AreEqual(long.MaxValue, api.LongProp);
			Assert.AreEqual(long.MinValue, api.LongField);
			Assert.AreEqual(ulong.MaxValue, api.ULongProp);

			Assert.IsTrue(s.DoString("return api.LongProp == int64.max and api.LongField == int64.min and api.ULongProp == uint64.max").Boolean);

			DynValue r = s.DoString("local ok, id = api:TryGetId() return tostring(id)");
			Assert.AreEqual("9223372036854775807", r.String);
		}

		[Test]
		public void Interop_OptIn_IsPerScript()
		{
			// Each Script carries its own setting, copied from Script.DefaultOptions when created.
			Script exact = NewScript(true, new Int64Api());
			Script plain = NewScript(false, new Int64Api());
			Assert.AreEqual(DataType.UserData, exact.DoString("return api:MaxLong()").Type);
			Assert.AreEqual(DataType.Number, plain.DoString("return api:MaxLong()").Type);

			Script.DefaultOptions.ExactInt64Interop = true;
			try
			{
				Assert.IsTrue(new Script().Options.ExactInt64Interop);
			}
			finally
			{
				Script.DefaultOptions.ExactInt64Interop = false;
			}
		}

		[Test]
		public void Interop_OptIn_HostCallsAndTableWrites()
		{
			Script s = NewScript(true, new Int64Api());

			// Arguments of Script.Call and values written through Table indexers also convert.
			DynValue f = s.DoString("return function(v) return int64.is(v) and tostring(v) end");
			Assert.AreEqual("9223372036854775807", s.Call(f, long.MaxValue).String);

			s.Globals["id"] = 9007199254740993L;
			Assert.AreEqual("9007199254740993", s.DoString("return tostring(id)").String);

			// The host reads values back with the usual conversions.
			Assert.AreEqual(9007199254740994L, s.DoString("return id + 1").ToObject<long>());
		}

		[Test]
		public void Interop_NarrowingIsRangeChecked()
		{
			Int64Api api = new Int64Api();
			Script s = NewScript(false, api);

			// Narrower integral types and decimal accept values that fit...
			Assert.AreEqual(5.0, s.DoString("return api:EchoInt(int64(5))").Number);
			Assert.AreEqual(255.0, s.DoString("return api:EchoByte(uint64(255))").Number);
			Assert.AreEqual(9.223372036854776E+18, s.DoString("return api:EchoDecimal(int64.max)").Number);
			s.DoString("api:Echo(uint64(int64.max))");
			Assert.AreEqual(long.MaxValue, api.LastLong);

			// ...and fail loudly on values that do not, instead of wrapping.
			StringAssert.Contains("out of range for Int32", Assert.Throws<ScriptRuntimeException>(() => s.DoString("api:EchoInt(int64('3000000000'))")).Message);
			StringAssert.Contains("out of range for Byte", Assert.Throws<ScriptRuntimeException>(() => s.DoString("api:EchoByte(int64(-1))")).Message);
			StringAssert.Contains("out of range for Int64", Assert.Throws<ScriptRuntimeException>(() => s.DoString("api:Echo(uint64.max)")).Message);
			StringAssert.Contains("out of range for UInt64", Assert.Throws<ScriptRuntimeException>(() => s.DoString("api:EchoU(int64(-1))")).Message);

			// double would round the value, which is the bug int64 exists to avoid.
			Assert.Throws<ScriptRuntimeException>(() => s.DoString("api:EchoDouble(int64(1))"));

			StringAssert.Contains("System.Int64", Assert.Throws<ScriptRuntimeException>(() => s.DoString("api:Echo({})")).Message);
		}

		[Test]
		public void Interop_NullableAndObjectParameters()
		{
			Script s = NewScript(true, new Int64Api());

			Assert.AreEqual("9223372036854775807", s.DoString("return tostring(api:EchoNullable(int64.max))").String);
			Assert.IsTrue(s.DoString("return api:EchoNullable(nil) == nil").Boolean);

			// An object parameter receives the LuaInt64 itself, which converts back unchanged.
			Assert.IsTrue(s.DoString("local v = int64('9007199254740993') return api:EchoObject(v) == v").Boolean);
		}

		[Test]
		public void Interop_OverloadResolution()
		{
			// int64 picks the long overload, while plain numbers still prefer double and strings
			// still prefer string: supporting int64 must not change existing overload choices.
			Script s = NewScript(false, new Int64Api());

			Assert.AreEqual("long", s.DoString("return api:Overload(int64(1))").String);
			Assert.AreEqual("double", s.DoString("return api:Overload(1)").String);
			Assert.AreEqual("string", s.DoString("return api:Overload('1')").String);

			// A narrower integer overload still beats the userdata-to-string fallback.
			Assert.AreEqual("int", s.DoString("return api:Narrow(int64(1))").String);
		}
	}
}
