using MoonSharp.Interpreter.Serialization.Json;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Tests for exact integers in JSON. Backends send 64-bit ids as JSON integer literals, and
	/// json.parse rounded every one above 2^53-1 to a double; json.serialize had no way to write
	/// an exact 64-bit integer back.
	/// </summary>
	[TestFixture]
	public class ExactInt64JsonTests
	{
		private const string Ids = @"{
			""small"": 42,
			""safe"": 9007199254740991,
			""twoPow53"": 9007199254740992,
			""id"": 9007199254740993,
			""neg"": -9007199254740993,
			""max"": 9223372036854775807,
			""min"": -9223372036854775808,
			""umax"": 18446744073709551615,
			""aboveInt64"": 9223372036854775808,
			""float"": 1.5,
			""bigFloat"": 9007199254740993.0,
			""exp"": 1e300,
			""list"": [1, 9007199254740993, { ""nested"": 9007199254740995 }]
		}";

		private static Script NewScript()
		{
			Script s = new Script(CoreModules.Preset_Complete | CoreModules.Int64);
			s.Globals["ids"] = Ids;
			return s;
		}

		[Test]
		public void Json_Default_StillParsesNumbers()
		{
			// Without the option nothing changes for existing scripts, including the rounding.
			Script s = NewScript();
			Assert.IsTrue(s.DoString(@"
				local t = json.parse(ids)
				return type(t.id) == 'number' and t.id == 9007199254740992 and t.small == 42").Boolean);

			Assert.IsTrue(s.DoString("return json.parse(ids, false, 'number').id == 2^53").Boolean);
		}

		[Test]
		public void Json_Int64Mode_KeepsLargeIntegersExact()
		{
			Script s = NewScript();
			s.DoString("t = json.parse(ids, false, 'int64')");

			Assert.AreEqual("9007199254740993", s.DoString("return tostring(t.id)").String);
			Assert.AreEqual("-9007199254740993", s.DoString("return tostring(t.neg)").String);
			Assert.AreEqual("9223372036854775807", s.DoString("return tostring(t.max)").String);
			Assert.AreEqual("-9223372036854775808", s.DoString("return tostring(t.min)").String);
			Assert.AreEqual("9007199254740993", s.DoString("return tostring(t.list[2])").String);
			Assert.AreEqual("9007199254740995", s.DoString("return tostring(t.list[3].nested)").String);
			Assert.IsTrue(s.DoString("return int64.is(t.id) and int64.is(t.max) and int64.is(t.min)").Boolean);

			// Above int64.max only uint64 holds the value.
			Assert.IsTrue(s.DoString("return uint64.is(t.umax) and t.umax == uint64.max").Boolean);
			Assert.AreEqual("9223372036854775808", s.DoString("return tostring(t.aboveInt64)").String);

			// 2^53 is not safe (2^53+1 also rounds to it), so it is exact too.
			Assert.IsTrue(s.DoString("return int64.is(t.twoPow53)").Boolean);

			// Integers a number holds exactly stay numbers, so ordinary JSON reads as before.
			Assert.IsTrue(s.DoString("return type(t.small) == 'number' and type(t.safe) == 'number' and t.list[1] == 1").Boolean);

			// Literals with a fraction or exponent are JSON floats and stay numbers.
			Assert.IsTrue(s.DoString("return type(t.float) == 'number' and type(t.bigFloat) == 'number' and type(t.exp) == 'number'").Boolean);
		}

		[Test]
		public void Json_Int64Mode_RejectsIntegersBeyond64Bits()
		{
			// Rounding these to a double would be the silent loss the mode exists to prevent.
			Script s = NewScript();
			StringAssert.Contains("does not fit in int64 or uint64",
				Assert.Throws<ScriptRuntimeException>(() => s.DoString("return json.parse('[18446744073709551616]', false, 'int64')")).Message);
			StringAssert.Contains("does not fit in int64 or uint64",
				Assert.Throws<ScriptRuntimeException>(() => s.DoString("return json.parse('[-9223372036854775809]', false, 'int64')")).Message);
		}

		[Test]
		public void Json_StringMode_KeepsDigits()
		{
			// For scripts that only pass ids through, a string needs no int64 module at all.
			Script s = new Script();
			s.Globals["ids"] = Ids;
			s.DoString("t = json.parse(ids, false, 'string')");

			Assert.AreEqual("9007199254740993", s.DoString("return t.id").String);
			Assert.AreEqual("-9223372036854775808", s.DoString("return t.min").String);
			Assert.AreEqual("18446744073709551615", s.DoString("return t.umax").String);
			Assert.AreEqual("-99999999999999999999999", s.DoString("return json.parse('[-99999999999999999999999]', false, 'string')[1]").String);
			Assert.IsTrue(s.DoString("return type(t.small) == 'number' and type(t.float) == 'number'").Boolean);
		}

		[Test]
		public void Json_InvalidModeIsAnError()
		{
			Script s = NewScript();
			StringAssert.Contains("invalid option 'int'",
				Assert.Throws<ScriptRuntimeException>(() => s.DoString("return json.parse('[1]', false, 'int')")).Message);
		}

		[Test]
		public void Json_SerializeWritesExactLiterals()
		{
			Script s = NewScript();
			string json = s.DoString(@"
				return json.serialize({ id = int64('9007199254740993'), min = int64.min, umax = uint64.max })").String;

			StringAssert.Contains("\"id\":9007199254740993", json);
			StringAssert.Contains("\"min\":-9223372036854775808", json);
			StringAssert.Contains("\"umax\":18446744073709551615", json);

			Assert.AreEqual("[1,9007199254740993,2.5]", s.DoString("return json.serialize({ int64(1), int64('9007199254740993'), 2.5 })").String);

			// Serialize then parse with 'int64' gives the same values back.
			Assert.IsTrue(s.DoString(@"
				local t = { id = int64('9007199254740993'), min = int64.min, umax = uint64.max, n = 7 }
				local back = json.parse(json.serialize(t), false, 'int64')
				return back.id == t.id and back.min == t.min and back.umax == t.umax and back.n == 7").Boolean);
		}

		[Test]
		public void Json_SerializeWorksWithoutTheModule()
		{
			// int64 values can come from the host (ExactInt64Interop) into a script that never
			// enabled CoreModules.Int64; serializing them must not need the module.
			Script s = new Script();
			s.Globals["id"] = LuaInt64.NewDynValue(long.MaxValue);
			Assert.AreEqual("{\"id\":9223372036854775807}", s.DoString("return json.serialize({ id = id })").String);
		}

		[Test]
		public void Json_HostApi()
		{
			DynValue v = JsonTableConverter.ParseString("{\"id\": 9223372036854775807, \"n\": 1}", null, false, JsonBigIntegerMode.Int64);
			Assert.AreEqual(new LuaInt64(long.MaxValue), v.Table.Get("id").UserData.Object);
			Assert.AreEqual(1.0, v.Table.Get("n").Number);
			Assert.AreEqual("{\"id\":9223372036854775807,\"n\":1}", v.Table.TableToJson());

			// The old overload keeps its behaviour.
			Assert.AreEqual(DataType.Number, JsonTableConverter.ParseString("{\"id\": 9223372036854775807}").Table.Get("id").Type);
		}
	}
}
