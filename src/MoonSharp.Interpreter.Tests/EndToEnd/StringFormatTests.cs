using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// string.format follows Lua 5.2.4 and C's printf (glibc): the golden file holds results recorded from
	/// reference Lua (Lua52Differential/gen_format_golden.py), and the tests below pin the fixes made against
	/// the old KopiLua port, each with what lua5.2 prints. Floating-point text is exact, so the same on every
	/// runtime: these tests run unchanged on .NET Framework and Mono.
	/// </summary>
	[TestFixture]
	public class StringFormatTests
	{
		/// <summary>Runs string.format with <paramref name="args"/> and describes the result or the error.</summary>
		internal static string Run(Script script, DynValue fn, string format, DynValue[] args)
		{
			try
			{
				var all = new DynValue[args.Length + 1];
				all[0] = DynValue.NewString(format);
				Array.Copy(args, 0, all, 1, args.Length);
				DynValue r = script.Call(fn, all);
				return "ok:" + r.String;
			}
			catch (InterpreterException ex)
			{
				return "err:" + ex.GetType().Name + ":" + ex.Message;
			}
			catch (Exception ex)
			{
				// Not a script error: something a script cannot catch with pcall.
				return "err:" + ex.GetType().Name;
			}
		}

		static DynValue StringFormat(Script script)
		{
			return script.Globals.Get("string").Table.Get("format");
		}

		static string Format(string code)
		{
			return new Script(CoreModules.Preset_Complete).DoString(code).String;
		}

		[Test]
		public void StringFormat_MatchesGoldenFile()
		{
			string[] golden = StringFormatGolden.Data;
			Assert.Greater(golden.Length, 3 * 3000, "golden file missing");

			foreach (string culture in new[] { "", "tr-TR", "de-DE" })
			{
				Utils.WithCulture(culture == "" ? "en-US" : culture, () =>
				{
					if (culture == "")
						System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

					var script = new Script(CoreModules.Preset_Complete);
					DynValue format = StringFormat(script);
					int failures = 0;
					var report = new StringBuilder();

					for (int i = 0; i < golden.Length; i += 3)
					{
						string actual = Run(script, format, golden[i], DecodeArgs(golden[i + 1]));
						if (actual != golden[i + 2] && failures++ < 20)
							report.AppendFormat("[{0}] format {1} args {2}: expected {3} got {4}\n",
								culture, Escape(golden[i]), Escape(golden[i + 1]), Escape(golden[i + 2]), Escape(actual));
					}

					Assert.AreEqual(0, failures, report.ToString());
					return 0;
				});
			}
		}

		// These ended the host process with a CLR exception that pcall could not catch.
		[Test]
		public void StringFormat_NoInputEndsTheHost()
		{
			// Lua 5.2: "[ab]", "\255", "\0", a 400-character number, "\255" padded to 8.
			Assert.AreEqual("[ab]", Format("return string.format('[%.10s]', 'ab')"));
			Assert.AreEqual("ÿ", Format("return string.format('%c', -1)"));
			Assert.AreEqual("\0", Format("return string.format('%c', 1e15)"));
			Assert.AreEqual(1 + 309 + 1 + 99, Format("return string.format('%99.99f', -1e308)").Length);
			Assert.AreEqual("       ÿ", Format("return string.format('%8c', -1)"));
			// Zero padding after '+' to a width of 0 or 1 went through PadLeft with a negative width.
			Assert.AreEqual("+1|0x1|01", Format("return string.format('%+00d|%#00x|%#0o', 1, 1, 1)"));
		}

		// Lua 5.2 results the old port got wrong.
		[Test]
		public void StringFormat_FollowsLua52()
		{
			// The precision of integer conversions is a minimum digit count.
			Assert.AreEqual("007|-001|", Format("return string.format('%.3d|%.3d|%.0d', 7, -1, 0)"));
			// Exponents have at least two digits, not .NET's three.
			Assert.AreEqual("1.500000e+00|1.000000E-05|1e+100", Format("return string.format('%e|%E|%g', 1.5, 1e-5, 1e100)"));
			// %g removes trailing zeros; '#' keeps them.
			Assert.AreEqual("0.0001|1e-05|100000|1e+06|2.50000", Format("return string.format('%g|%g|%g|%g|%#g', 1e-4, 1e-5, 1e5, 1e6, 2.5)"));
			// Non-finite numbers are written as C writes them.
			Assert.AreEqual("inf|-inf|  INF", Format("return string.format('%f|%e|%5G', 1/0, -1/0, 1/0)"));
			// %a writes the bits in hexadecimal.
			Assert.AreEqual("0x1.8p+1|0X1.4P-2|0x0p+0", Format("return string.format('%a|%A|%a', 3, 0.3125, 0)"));
			// %c honours the width, and 0 is a character.
			Assert.AreEqual("  A|\0", Format("return string.format('%3c|%c', 65, 0)"));
			// %q escapes every control character as \ddd, as Lua 5.2's loader reads them back.
			Assert.AreEqual("\"\\13\\0\\0001\\127\"", Format("return string.format('%q', '\\r\\0\\0' .. '1\\127')"));
		}

		// %d and %i take numbers in [-2^63, 2^63), the unsigned conversions (-1, 2^64): outside it is an error, not a
		// value that depends on how the runtime converts the double (.NET 9 saturates negative numbers to 0).
		[Test]
		public void StringFormat_IntegerRangeIsChecked()
		{
			var script = new Script(CoreModules.Preset_Complete);
			DynValue r = script.DoString(@"
				local out = {}
				for _, c in ipairs({ {'%d', 2^63}, {'%d', 0/0}, {'%x', -1}, {'%u', 2^64}, {'%d', -2^63}, {'%x', 2^64 - 2048}, {'%u', -0.5} }) do
					local ok, r = pcall(string.format, c[1], c[2])
					out[#out + 1] = tostring(ok) .. ':' .. r
				end
				return table.concat(out, '|')");

			Assert.AreEqual("false:bad argument #2 to 'format' (not a number in proper range)|" +
				"false:bad argument #2 to 'format' (not a number in proper range)|" +
				"false:bad argument #2 to 'format' (not a non-negative number in proper range)|" +
				"false:bad argument #2 to 'format' (not a non-negative number in proper range)|" +
				"true:-9223372036854775808|true:fffffffffffff800|true:0", r.String);
		}

		// %s takes any value through tostring and __tostring, as luaL_tolstring does.
		[Test]
		public void StringFormat_StringConversionTakesAnyValue()
		{
			Assert.AreEqual("nil|true|T|x 5", Format(@"
				local t = setmetatable({}, { __tostring = function() return 'T' end })
				return string.format('%s|%s|%s|%s %s', nil, true, t, 'x', 5)"));

			var script = new Script(CoreModules.Preset_Complete);
			DynValue r = script.DoString(@"return pcall(string.format, '%s', setmetatable({}, { __tostring = function() return {} end }))");
			Assert.AreEqual("'__tostring' must return a string", r.Tuple[1].String);

			// Not Lua 5.2, which has no int64: its decimal digits, as tostring gives them.
			var withInt64 = new Script(CoreModules.Preset_Complete | CoreModules.Int64);
			Assert.AreEqual("-9223372036854775808", withInt64.DoString("return string.format('%s', int64('-9223372036854775808'))").String);
		}

		// The digits are those of the double's exact value, rounded half to even, on every runtime. .NET Framework
		// and Mono rounded a 15-digit text: 1.005 is 1.00499999999999989..., 0.125 and 2.5 are exact halves.
		[Test]
		public void StringFormat_RoundsTheExactValue()
		{
			Assert.AreEqual("1.00|0.12|2|4|0.30000000000000004441|1.0000000000000000000e+22",
				Format("return string.format('%.2f|%.2f|%.0f|%.0f|%.20f|%.19e', 1.005, 0.125, 2.5, 3.5, 0.1 + 0.2, 1e22)"));
			Assert.AreEqual("4.9406564584124654418e-324|0.1000000000000000055511151231257827021181583404541015625",
				Format("return string.format('%.19e|%.99g', 5e-324, 0.1)"));
		}

		// The format argument itself: numbers are converted, anything else is an error.
		[Test]
		public void StringFormat_FormatArgument()
		{
			var script = new Script(CoreModules.Preset_Complete);
			DynValue res = script.DoString(@"
				local t = {}
				for _, args in ipairs({ {12}, {1.5}, {}, {true}, {{}}, {'%d', '0x1F'}, {'%5.1f', ' 2e1 '} }) do
					local ok, r = pcall(string.format, table.unpack(args))
					t[#t + 1] = tostring(ok) .. ':' .. tostring(r)
				end
				return table.concat(t, '|')");

			Assert.AreEqual("true:12|true:1.5|false:bad argument #1 to 'format' (string expected, got no value)|" +
				"false:bad argument #1 to 'format' (string expected, got boolean)|" +
				"false:bad argument #1 to 'format' (string expected, got table)|true:31|true: 20.0", res.String);
		}

		// Numeric conversions never depend on the culture, %s and %q of strings neither.
		[Test]
		public void StringFormat_NumbersIgnoreCulture()
		{
			const string code = @"return string.format('%9.2f|%e|%g|%G|%d|%x|%o|%12.3E|%-8.1f|%+.0f|%s|%q',
				1234.5, -0.000123, 1e-5, 1.5e300, -42, 255, 8, 6.02e23, -2.5, 0.75, 'a,b', 'c.d')";

			string expected = Utils.WithCulture("en-US", () => new Script(CoreModules.String).DoString(code).String);
			Assert.AreEqual("  1234.50|-1.230000e-04|1e-05|1.5E+300|-42|ff|10|   6.020E+23|-2.5    |+1|a,b|\"c.d\"", expected);

			foreach (string culture in new[] { "tr-TR", "de-DE", "fr-FR", "ar-SA", "fa-IR", "sv-SE" })
				Assert.AreEqual(expected, Utils.WithCulture(culture, () => new Script(CoreModules.String).DoString(code).String), culture);
		}

		// The managed formatter must not pay for the old emulation's buffers and regular expressions:
		// ~8,000 bytes per call before it existed.
		[Test]
		public void StringFormat_AllocatesLittle()
		{
			MethodInfo allocated = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
			if (allocated == null)
			{
				TestRunner.Skip();
				Assert.Ignore("GC.GetAllocatedBytesForCurrentThread is not available on this runtime.");
			}

			var script = new Script(CoreModules.Preset_Complete);
			DynValue fn = script.DoString(@"local format = string.format
				return function(n) local s for i = 1, n do s = format('%d:%s|%5.2f|%-4x', i, 'x', 1.5, 255) end return s end");

			script.Call(fn, 100);
			long before = (long)allocated.Invoke(null, null);
			DynValue r = script.Call(fn, 1000);
			long perCall = ((long)allocated.Invoke(null, null) - before) / 1000;

			Assert.AreEqual("1000:x| 1.50|ff  ", r.String);
			Assert.Less(perCall, 1500, "bytes allocated per string.format call");
		}

		// ---------------------------------------------------------------------------------------
		// Encoding helpers
		// ---------------------------------------------------------------------------------------

		/// <summary>
		/// Arguments as gen_format_golden.py writes them, separated by U+001F: "n:" and a number ("nan", "inf",
		/// "-inf", "-0" or a decimal), "s:" and a string, "b:true", "b:false", "nil" or "table".
		/// </summary>
		internal static DynValue[] DecodeArgs(string text)
		{
			if (text.Length == 0)
				return new DynValue[0];

			string[] parts = text.Split('\u001f');
			var res = new DynValue[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				string p = parts[i];
				if (p.StartsWith("n:"))
				{
					string v = p.Substring(2);
					double d = v == "nan" ? double.NaN : v == "inf" ? double.PositiveInfinity : v == "-inf" ? double.NegativeInfinity
						: v == "-0" ? -0.0 : double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
					res[i] = DynValue.NewNumber(d);
				}
				else if (p.StartsWith("s:")) res[i] = DynValue.NewString(p.Substring(2));
				else if (p == "b:true") res[i] = DynValue.True;
				else if (p == "b:false") res[i] = DynValue.False;
				else if (p == "nil") res[i] = DynValue.Nil;
				else if (p == "table") res[i] = DynValue.NewTable(new Table(null));
				else throw new FormatException(p);
			}
			return res;
		}

		static string Escape(string s)
		{
			var sb = new StringBuilder("\"");
			foreach (char ch in s)
			{
				if (ch == '"') sb.Append("\\\"");
				else if (ch == '\\') sb.Append("\\\\");
				else if (ch < 0x20 || ch >= 0x7f) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)ch);
				else sb.Append(ch);
			}
			return sb.Append('"').ToString();
		}
	}
}
