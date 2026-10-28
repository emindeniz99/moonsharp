using System.Collections.Generic;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Tests for the int64 and uint64 values and their Lua modules.
	/// MoonSharp numbers are doubles, which hold integers exactly only up to 2^53-1. Scripts that
	/// handle 64-bit ids and bitfields need values that never round, so every test here checks
	/// either that a value survives exactly or that an inexact conversion fails loudly.
	/// </summary>
	[TestFixture]
	public class ExactInt64Tests
	{
		private static Script NewScript()
		{
			return new Script(CoreModules.Preset_Complete | CoreModules.Int64);
		}

		private static DynValue Run(string code)
		{
			return NewScript().DoString(code);
		}

		private static string Str(string code)
		{
			DynValue v = Run(code);
			Assert.AreEqual(DataType.String, v.Type, "expected a string, got " + v);
			return v.String;
		}

		private static bool Bool(string code)
		{
			DynValue v = Run(code);
			Assert.AreEqual(DataType.Boolean, v.Type, "expected a boolean, got " + v);
			return v.Boolean;
		}

		private static string Error(string code)
		{
			try
			{
				Run(code);
			}
			catch (ScriptRuntimeException ex)
			{
				return ex.DecoratedMessage ?? ex.Message;
			}

			Assert.Fail("expected a script error from: " + code);
			return null;
		}

		[Test]
		public void Int64_NotInAnyPreset()
		{
			// The module adds the globals int64 and uint64. Existing scripts may already use
			// those names, so only an explicit CoreModules.Int64 may add them.
			Assert.IsTrue(new Script().DoString("return int64 == nil and uint64 == nil").Boolean);
			Assert.IsTrue(new Script(CoreModules.Preset_Complete).DoString("return int64 == nil and uint64 == nil").Boolean);
			Assert.IsFalse(CoreModules.Preset_Complete.HasFlag(CoreModules.Int64));
		}

		[Test]
		public void Int64_DecimalStringsRoundTripExactly()
		{
			// Every boundary value must come back digit for digit; 2^53+1 is the first integer a
			// double cannot hold and the one that silently became 2^53 before.
			string[] values =
			{
				"-9223372036854775808", "9223372036854775807", "0", "-1", "-42",
				"9007199254740991", "9007199254740992", "9007199254740993", "-9007199254740993",
			};

			foreach (string s in values)
				Assert.AreEqual(s, Str("return tostring(int64('" + s + "'))"), s);

			foreach (string s in new[] { "0", "9007199254740993", "9223372036854775808", "18446744073709551615" })
				Assert.AreEqual(s, Str("return tostring(uint64('" + s + "'))"), s);
		}

		[Test]
		public void Int64_Constants()
		{
			Assert.AreEqual("9223372036854775807", Str("return tostring(int64.max)"));
			Assert.AreEqual("-9223372036854775808", Str("return tostring(int64.min)"));
			Assert.AreEqual("18446744073709551615", Str("return tostring(uint64.max)"));
		}

		[Test]
		public void Int64_FromNumber_AcceptsOnlySafeIntegers()
		{
			// Within +/-(2^53-1) a number is known to be exact, so it converts.
			Assert.AreEqual("9007199254740991", Str("return tostring(int64(2^53 - 1))"));
			Assert.AreEqual("-9007199254740991", Str("return tostring(int64(-(2^53 - 1)))"));
			Assert.AreEqual("0", Str("return tostring(int64(0))"));
			Assert.AreEqual("-5", Str("return tostring(int64(-5))"));
			Assert.AreEqual("5", Str("return tostring(uint64(5))"));

			// 2^53 is refused even though the double is exact: the literal 9007199254740993 also
			// becomes 2^53, and accepting it would hide that rounding.
			StringAssert.Contains("outside the exact integer range", Error("return int64(2^53)"));
			StringAssert.Contains("outside the exact integer range", Error("return int64(9007199254740993)"));
			StringAssert.Contains("outside the exact integer range", Error("return uint64(2^60)"));
			StringAssert.Contains("outside the exact integer range", Error("return int64(math.huge)"));

			StringAssert.Contains("no integer representation", Error("return int64(1.5)"));
			StringAssert.Contains("no integer representation", Error("return int64(0/0)"));
			StringAssert.Contains("out of the uint64 range", Error("return uint64(-1)"));
		}

		[Test]
		public void Int64_FromString_ChecksSyntaxAndRange()
		{
			Assert.AreEqual("42", Str("return tostring(int64(' 42 '))"));
			Assert.AreEqual("42", Str("return tostring(int64('+42'))"));
			Assert.AreEqual("0", Str("return tostring(uint64('-0'))"));

			// Out-of-range text must fail rather than wrap or saturate.
			StringAssert.Contains("out of the int64 range", Error("return int64('9223372036854775808')"));
			StringAssert.Contains("out of the int64 range", Error("return int64('-9223372036854775809')"));
			StringAssert.Contains("out of the uint64 range", Error("return uint64('18446744073709551616')"));
			StringAssert.Contains("out of the uint64 range", Error("return uint64('-1')"));

			// Only decimal integers: a fraction or exponent would mean the text was a double.
			StringAssert.Contains("not a decimal integer", Error("return int64('1.0')"));
			StringAssert.Contains("not a decimal integer", Error("return int64('1e3')"));
			StringAssert.Contains("not a decimal integer", Error("return int64('12abc')"));
			StringAssert.Contains("not a decimal integer", Error("return int64('')"));

			StringAssert.Contains("got table", Error("return int64({})"));
			StringAssert.Contains("got nil", Error("return int64(nil)"));
		}

		[Test]
		public void Int64_ConvertsBetweenSignednessWithRangeCheck()
		{
			Assert.AreEqual("5", Str("return tostring(int64(uint64(5)))"));
			Assert.AreEqual("9223372036854775807", Str("return tostring(uint64(int64.max))"));
			Assert.IsTrue(Bool("local x = int64(7) return int64(x) == x"));

			StringAssert.Contains("out of the int64 range", Error("return int64(uint64.max)"));
			StringAssert.Contains("out of the uint64 range", Error("return uint64(int64(-1))"));
		}

		[Test]
		public void Int64_ArithmeticWrapsLikeLua53Integers()
		{
			// Two's complement wraparound, as Lua 5.3 integers and C# unchecked arithmetic do.
			Assert.IsTrue(Bool("return int64.max + 1 == int64.min"));
			Assert.IsTrue(Bool("return int64.min - 1 == int64.max"));
			Assert.AreEqual("-2", Str("return tostring(int64.max * 2)"));
			Assert.IsTrue(Bool("return -int64.min == int64.min"));
			Assert.IsTrue(Bool("return uint64.max + 1 == uint64(0)"));
			Assert.IsTrue(Bool("return uint64(0) - 1 == uint64.max"));
			Assert.IsTrue(Bool("return -uint64(1) == uint64.max"));
		}

		[Test]
		public void Int64_ArithmeticIsExactWhereNumbersRound()
		{
			// The motivating case: one past 2^53 is lost by a double but kept by int64.
			Assert.AreEqual("9007199254740994", Str("return tostring(int64('9007199254740993') + 1)"));
			Assert.AreEqual("9007199254740992", Str("return tostring(int64('9007199254740993') - 1)"));
			Assert.AreEqual("4611686018427387904", Str("return tostring(int64(2) * int64('2305843009213693952'))"));
			Assert.IsTrue(Bool("return 9007199254740993 == 9007199254740992"), "doubles round; this is why int64 exists");
		}

		[Test]
		public void Int64_MixedWithNumbers_RequiresExactIntegers()
		{
			// A safe integer number on either side is exact, so the result stays exact.
			Assert.AreEqual("15", Str("return tostring(int64(10) + 5)"));
			Assert.AreEqual("-5", Str("return tostring(5 - int64(10))"));
			Assert.IsTrue(Bool("return int64.is(int64(10) * 2) and uint64.is(2 * uint64(10))"));

			// Anything that could already be rounded, or is fractional, must not be folded in.
			StringAssert.Contains("no integer representation", Error("return int64(1) + 1.5"));
			StringAssert.Contains("outside the exact integer range", Error("return int64(1) + 2^53"));
			StringAssert.Contains("negative", Error("return uint64(1) + -1"));

			// Signed and unsigned are kept apart, as there is no type both fit.
			StringAssert.Contains("cannot mix", Error("return int64(1) + uint64(1)"));
			StringAssert.Contains("cannot mix", Error("return uint64(1) * int64(1)"));

			// No string coercion: '9007199254740993' would go through a double.
			StringAssert.Contains("arithmetic on a string value", Error("return int64(1) + '1'"));
			StringAssert.Contains("arithmetic on a table value", Error("return int64(1) + {}"));
			StringAssert.Contains("power", Error("return int64(2) ^ 2"));
		}

		[Test]
		public void Int64_DivisionIsFloorDivisionAndModIsFloorMod()
		{
			// '/' keeps the int64 type, so it divides like Lua 5.3 '//' and pairs with '%' such
			// that a == (a / b) * b + a % b holds for every non-zero b.
			Assert.AreEqual("3", Str("return tostring(int64(7) / 2)"));
			Assert.AreEqual("-4", Str("return tostring(int64(-7) / 2)"));
			Assert.AreEqual("-4", Str("return tostring(int64(7) / -2)"));
			Assert.AreEqual("1", Str("return tostring(int64(-7) % 2)"));
			Assert.AreEqual("-1", Str("return tostring(int64(7) % -2)"));
			Assert.AreEqual("9223372036854775807", Str("return tostring(uint64.max / 2)"));
			Assert.AreEqual("1", Str("return tostring(uint64.max % 2)"));

			Assert.IsTrue(Bool(@"
				for _, a in ipairs({7, -7, 0, 9007199254740991, -9007199254740991}) do
					for _, b in ipairs({2, -2, 3, -3, 1, -1}) do
						local x, y = int64(a), int64(b)
						if (x / y) * y + x % y ~= x then return false end
					end
				end
				return true"));

			// min / -1 overflows; the CLR throws, Lua 5.3 wraps. We wrap.
			Assert.IsTrue(Bool("return int64.min / -1 == int64.min"));
			Assert.IsTrue(Bool("return int64.min % -1 == int64(0)"));

			StringAssert.Contains("division by zero", Error("return int64(1) / 0"));
			StringAssert.Contains("'n%0'", Error("return int64(1) % 0"));
			StringAssert.Contains("division by zero", Error("return uint64(1) / 0"));
		}

		[Test]
		public void Int64_BitOperations()
		{
			Assert.AreEqual("255", Str("return tostring(int64.band(-1, 0xFF))"));
			Assert.AreEqual("48", Str("return tostring(int64.band(0xF0, 0x3C, 0xFF))"));
			Assert.AreEqual("252", Str("return tostring(int64.bor(0xF0, 0x0C))"));
			Assert.AreEqual("204", Str("return tostring(int64.bxor(0xF0, 0x3C))"));
			Assert.AreEqual("-1", Str("return tostring(int64.bnot(0))"));

			// Bit 63 is reachable, which a double-based bit32 cannot do.
			Assert.IsTrue(Bool("return int64.shl(1, 63) == int64.min"));
			Assert.AreEqual("9223372036854775807", Str("return tostring(int64.shr(-1, 1))"));
			Assert.AreEqual("-4", Str("return tostring(int64.sar(-8, 1))"));
			Assert.AreEqual("-1", Str("return tostring(int64.sar(-1, 100))"));

			// Counts of 64 or more shift everything out (the CLR would use count mod 64), and a
			// negative count shifts the other way, as in Lua 5.3.
			Assert.AreEqual("0", Str("return tostring(int64.shl(1, 64))"));
			Assert.AreEqual("0", Str("return tostring(int64.shr(int64.min, 64))"));
			Assert.AreEqual("2", Str("return tostring(int64.shl(4, -1))"));
			Assert.AreEqual("8", Str("return tostring(int64.shr(4, -1))"));

			Assert.AreEqual("1", Str("return tostring(uint64.shr(uint64.max, 63))"));
			Assert.AreEqual("9223372036854775808", Str("return tostring(uint64.shl(1, 63))"));
			Assert.IsTrue(Bool("return uint64.bnot(0) == uint64.max"));
			Assert.AreEqual("4294967295", Str("return tostring(uint64.band(uint64.max, 0xFFFFFFFF))"));

			// Building a 64-bit flag word from parts is the bitfield use case.
			Assert.AreEqual("-9223372036854775807", Str("return tostring(int64.bor(int64.shl(1, 63), 1))"));

			StringAssert.Contains("outside the exact integer range", Error("return int64.band(2^60, 1)"));
			StringAssert.Contains("cannot mix", Error("return int64.band(int64(1), uint64(1))"));
		}

		[Test]
		public void Int64_ComparisonsAreExact()
		{
			Assert.IsTrue(Bool("return int64(1) < int64(2) and int64.min < int64.max"));
			Assert.IsTrue(Bool("return int64(2) <= int64(2) and not (int64(3) <= int64(2))"));
			Assert.IsTrue(Bool("return uint64(int64.max) < uint64.max"));

			// MoonSharp dispatches __lt/__le when either side has the handler, so int64 and
			// numbers compare in both orders.
			Assert.IsTrue(Bool("return int64(5) < 6 and 4 < int64(5) and int64(5) <= 5 and 5 >= int64(5)"));

			// Compared mathematically, not through a double: 2^53+1 is greater than 2^53.
			Assert.IsTrue(Bool("return int64('9007199254740993') > 2^53"));
			Assert.IsTrue(Bool("return int64.max < 2^63 and int64.min >= -2^63 and uint64.max < 2^64"));
			Assert.IsTrue(Bool("return int64(2) < 2.5 and int64(3) > 2.5 and int64(-3) < -2.5"));

			// Signed against unsigned by value.
			Assert.IsTrue(Bool("return int64(-1) < uint64(0) and uint64.max > int64.max"));

			// NaN is unordered: < and <= are false. MoonSharp compiles a >= b as not (a < b), so
			// >= and > against NaN come out true, for plain numbers too; int64 matches numbers.
			Assert.IsTrue(Bool("local n = 0/0 return not (int64(1) < n) and not (n <= int64(1)) and not (int64(1) <= n)"));
			Assert.IsTrue(Bool("local n = 0/0 return (int64(1) >= n) == (1 >= n) and (int64(1) > n) == (1 > n)"));

			StringAssert.Contains("attempt to compare int64 with string", Error("return int64(1) < '2'"));
		}

		[Test]
		public void Int64_EqualityFollowsLuaTypeRules()
		{
			// Distinct objects with the same value are equal.
			Assert.IsTrue(Bool("return int64('9007199254740993') == int64('9007199254740993')"));
			Assert.IsTrue(Bool("return int64(1) ~= int64(2)"));

			// Lua 5.2 == never equates different types, and t[int64(5)] and t[5] are different
			// keys; == agrees with that. int64.eq compares values across types.
			Assert.IsFalse(Bool("return int64(5) == 5"));
			Assert.IsTrue(Bool("return int64(5) ~= 5"));
			Assert.IsFalse(Bool("return int64(5) == uint64(5)"));
			Assert.IsFalse(Bool("return int64(5) == nil"));
			Assert.IsTrue(Bool("return int64.eq(int64(5), 5) and int64.eq(5, int64(5)) and uint64.eq(uint64(5), int64(5))"));
			Assert.IsFalse(Bool("return int64.eq(int64('9007199254740993'), 2^53)"));
			StringAssert.Contains("got string", Error("return int64.eq(int64(1), '1')"));
		}

		[Test]
		public void Int64_StringConversions()
		{
			Assert.AreEqual("id=9007199254740993", Str("return 'id=' .. int64('9007199254740993')"));
			Assert.AreEqual("-12", Str("return int64(-1) .. 2"));
			Assert.AreEqual("18446744073709551615!", Str("return uint64.max .. '!'"));
			// MoonSharp's string.format('%s') takes only strings and numbers, so use tostring.
			// '%d' must fail rather than format a rounded double.
			Assert.AreEqual("9007199254740993", Str("return string.format('%s', tostring(int64('9007199254740993')))"));
			StringAssert.Contains("got userdata", Error("return string.format('%d', int64(1))"));
			Assert.AreEqual("7", Str("return int64.tostring(7)"));
			StringAssert.Contains("attempt to concatenate a table value", Error("return int64(1) .. {}"));

			// print goes through the same text as tostring.
			string printed = null;
			Script s = NewScript();
			s.Options.DebugPrint = t => printed = t;
			s.DoString("print(int64.min)");
			Assert.AreEqual("-9223372036854775808", printed);
		}

		[Test]
		public void Int64_ToNumber_FailsInsteadOfRounding()
		{
			DynValue v = Run("return int64.tonumber(int64(2^53 - 1))");
			Assert.AreEqual(DataType.Number, v.Type);
			Assert.AreEqual(9007199254740991.0, v.Number);

			// Lua's convention for a failed conversion: nil plus a message, so assert() works.
			DynValue r = Run("return int64.tonumber(int64('9007199254740992'))");
			Assert.AreEqual(DataType.Tuple, r.Type);
			Assert.IsTrue(r.Tuple[0].IsNil());
			StringAssert.Contains("no exact number representation", r.Tuple[1].String);

			Assert.IsTrue(Run("return uint64.tonumber(uint64.max)").Tuple[0].IsNil());
			StringAssert.Contains("no exact number representation", Error("return assert(int64.tonumber(int64.max))"));

			// The global tonumber does not know int64 and returns nil rather than a rounded value.
			Assert.IsTrue(Run("return tonumber(int64(5))").IsNil());
		}

		[Test]
		public void Int64_IsAndType()
		{
			Assert.IsTrue(Bool("return int64.is(int64(1)) and not int64.is(1) and not int64.is(uint64(1))"));
			Assert.IsTrue(Bool("return uint64.is(uint64(1)) and not uint64.is(int64(1)) and not uint64.is('1')"));
			Assert.AreEqual("userdata", Str("return type(int64(1))"));
		}

		[Test]
		public void Int64_IsImmutable()
		{
			// Values are shared freely (table keys, constants), so they must not be writable.
			StringAssert.Contains("cannot access field", Error("return int64(1).x"));
			StringAssert.Contains("cannot access field", Error("local v = int64(1) v.x = 2"));
		}

		[Test]
		public void Int64_TableKeysCompareByValue()
		{
			// Two separately built int64 values with the same number index the same slot, and
			// values that only round to the same double stay apart.
			Assert.AreEqual("a", Str(@"
				local t = {}
				t[int64('9007199254740993')] = 'a'
				t[int64('9007199254740992')] = 'b'
				return t[int64('9007199254740993')]"));

			// int64(5), uint64(5) and 5 are different keys, matching ==.
			Assert.AreEqual("i,u,n,3", Str(@"
				local t = {}
				t[int64(5)] = 'i' t[uint64(5)] = 'u' t[5] = 'n'
				local c = 0 for _ in pairs(t) do c = c + 1 end
				return t[int64(5)] .. ',' .. t[uint64(5)] .. ',' .. t[5] .. ',' .. c"));

			// Many keys: every one is still found after all are inserted, and removal works.
			Assert.IsTrue(Bool(@"
				local t, base = {}, int64('9007199254740993')
				for i = 1, 2000 do t[base + i] = i end
				for i = 1, 2000 do if t[base + i] ~= i then return false end end
				t[base + 1] = nil
				return t[base + 1] == nil and t[base + 2] == 2"));
		}

		[Test]
		public void Int64_HashCodeFollowsValue()
		{
			// Table lookups use DynValue.GetHashCode. Equal values must hash equally, and int64
			// keys hash by value instead of sharing one bucket with every other userdata.
			DynValue a = LuaInt64.NewDynValue(long.MaxValue);
			DynValue b = LuaInt64.NewDynValue(long.MaxValue);
			Assert.IsTrue(a.Equals(b));
			Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
			Assert.AreNotEqual(LuaInt64.NewDynValue(1).GetHashCode(), LuaInt64.NewDynValue(2).GetHashCode());
			Assert.AreEqual(LuaUInt64.NewDynValue(ulong.MaxValue).GetHashCode(), LuaUInt64.NewDynValue(ulong.MaxValue).GetHashCode());
			Assert.IsFalse(LuaInt64.NewDynValue(5).Equals(LuaUInt64.NewDynValue(5)));
		}

		public class MutableKey
		{
			public int Value;
			public override bool Equals(object obj) { return obj is MutableKey && ((MutableKey)obj).Value == Value; }
			public override int GetHashCode() { return Value; }
		}

		[Test]
		public void Int64_OtherUserDataKeepConstantHash()
		{
			// Hashing by value is limited to the immutable int64/uint64 types. A userdata whose
			// Equals/GetHashCode follow mutable state must still be found after it changes,
			// which a value-based (and cached) hash would break.
			UserData.RegisterType<MutableKey>();
			Script s = NewScript();
			MutableKey key = new MutableKey { Value = 1 };
			s.Globals["key"] = key;
			s.DoString("t = {} t[key] = 'found'");
			key.Value = 2;

			// A fresh script value for the same object, so no hash computed before the change
			// is reused.
			s.Globals["sameKey"] = key;
			Assert.AreEqual("found", s.DoString("return t[sameKey]").String);
		}

		[Test]
		public void Int64_HostApi()
		{
			// Hosts can hand exact values to scripts without registering anything.
			Script s = NewScript();
			s.Globals["big"] = LuaInt64.NewDynValue(long.MaxValue);
			s.Globals["ubig"] = new LuaUInt64(ulong.MaxValue);
			s.Globals["fromObject"] = DynValue.FromObject(s, new LuaInt64(long.MinValue));

			Assert.AreEqual("9223372036854775807", s.DoString("return tostring(big)").String);
			Assert.AreEqual("18446744073709551615", s.DoString("return tostring(ubig)").String);
			Assert.AreEqual("-9223372036854775808", s.DoString("return tostring(fromObject)").String);

			DynValue r = s.DoString("return big - 1");
			Assert.AreEqual(long.MaxValue - 1, r.ToObject<LuaInt64>().Value);
			// Equals, not Assert.AreEqual: NUnit compares IEquatable values through Type.GetInterfaceMap,
			// which NativeAOT does not support, and the suite also runs there.
			Assert.IsTrue(new LuaInt64(long.MaxValue - 1).Equals(r.ToObject()));

			// The CLR-side types are values too.
			Assert.IsTrue(new LuaInt64(3).Equals((object)new LuaInt64(3)));
			Assert.IsFalse(new LuaInt64(3).Equals((object)new LuaUInt64(3)));
			Assert.AreEqual("-3", new LuaInt64(-3).ToString());
			var set = new HashSet<LuaInt64> { new LuaInt64(1), new LuaInt64(1) };
			Assert.AreEqual(1, set.Count);
		}
	}
}
