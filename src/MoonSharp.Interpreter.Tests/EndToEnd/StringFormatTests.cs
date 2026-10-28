using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// string.format replaced a port of C's sprintf (KopiLua) with a one-pass formatter. Scripts must not
	/// see the difference: every output, every error and every quirk of the port must stay the same.
	/// These tests check that in two ways: against a golden file recorded from the port before the
	/// replacement, and live against the port itself (KopiLuaFormatOracle) over a much larger corpus.
	/// </summary>
	[TestFixture]
	public class StringFormatTests
	{
		// ---------------------------------------------------------------------------------------
		// Corpus
		// ---------------------------------------------------------------------------------------

		internal static readonly string[] Flags = { "", "-", "+", " ", "#", "0", "-0", "0-", "+0", " 0", "#0", "+ ", "-+", "-#", "#-", "+-", " -", "-+ #0", "00", "0#" };
		internal static readonly string[] Widths = { "", "1", "5", "12", "30" };
		internal static readonly string[] Precisions = { "", ".", ".0", ".1", ".3", ".10", ".20" };
		internal static readonly char[] NumericConversions = { 'd', 'i', 'u', 'c', 'x', 'X', 'o', 'e', 'E', 'f', 'g', 'G' };
		internal static readonly char[] StringConversions = { 's', 'q' };

		internal static readonly double[] Numbers =
		{
			0, -0.0, 1, -1, 7, 42, -42, 65, 255, 4096, 0.5, -0.5, 0.1, 2.5, -2.5, 3.14159265358979, 1.0 / 3.0, 99.999,
			123456.789, 1e-5, 1e-300, 1e15, 1e16, 1e20, -1e20, 1e100, 1e308, -1e308,
			2147483648.0, 4294967296.0, -4294967296.0,
			9007199254740991.0, 9007199254740992.0, 9007199254740993.0, // 2^53-1, 2^53, 2^53+1 (rounds to 2^53)
			9223372036854775808.0, -9223372036854775808.0, 18446744073709551616.0,  // 2^63, -2^63, 2^64
			double.NaN, double.PositiveInfinity, double.NegativeInfinity,
		};

		internal static readonly string[] Strings =
		{
			"", "a", "abc", "hello world", "1.5", " 0x10 ", "ünïcödé", "İı",
			"a\0b", "\0", "\0" + "1", "quote\"back\\slash", "\r\n\t", "\u0001\u00012\u000f9\u0010\u001f",
			new string('x', 99), new string('y', 100), new string('z', 150),
			"tab\there" + new string('w', 120) + "\0tail",
		};

		/// <summary>One string.format call: a format string and its arguments.</summary>
		internal sealed class Case
		{
			public string Format;
			public DynValue[] Args;
			public string ArgsText;

			public Case(string format, params DynValue[] args)
			{
				Format = format;
				Args = args;
				ArgsText = EncodeArgs(args);
			}
		}

		internal static IEnumerable<string> NumericFormats()
		{
			foreach (char c in NumericConversions)
				foreach (string f in Flags)
					foreach (string w in Widths)
						foreach (string p in Precisions)
							yield return "%" + f + w + p + c;
		}

		internal static IEnumerable<string> StringFormats()
		{
			foreach (char c in StringConversions)
				foreach (string f in Flags)
					foreach (string w in Widths)
						foreach (string p in Precisions)
							yield return "%" + f + w + p + c;
		}

		/// <summary>The full corpus: every generated format with every value, plus the hand-written cases.</summary>
		internal static IEnumerable<Case> Corpus()
		{
			foreach (Case c in Generated())
				yield return c;
			foreach (Case c in HandWritten())
				yield return c;
		}

		/// <summary>Every format with every value.</summary>
		internal static IEnumerable<Case> Generated()
		{
			foreach (string fmt in NumericFormats())
				foreach (double d in Numbers)
					yield return new Case(fmt, DynValue.NewNumber(d));

			foreach (string fmt in StringFormats())
			{
				foreach (string s in Strings)
					yield return new Case(fmt, DynValue.NewString(s));
				foreach (double d in new[] { 42, -0.0, 1.5, 1e20, double.NaN, double.NegativeInfinity })
					yield return new Case(fmt, DynValue.NewNumber(d));
			}
		}

		static DynValue N(double d) { return DynValue.NewNumber(d); }
		static DynValue S(string s) { return DynValue.NewString(s); }

		internal static IEnumerable<Case> HandWritten()
		{
			// The benchmark's shape and other multi-item formats.
			yield return new Case("%d:%s", N(499968), S("x"));
			yield return new Case("%s %s", N(1), N(2), N(3));
			yield return new Case("%5.2s|%-5d|%05.1f|%x|%%|%c", S("abcdef"), N(-3), N(-2.25), N(255), N(66));
			yield return new Case("pi = %.4f", N(Math.PI));
			yield return new Case("%02d/%02d/%04d", N(5), N(11), N(1990));
			yield return new Case("%X %x", N(126), N(126));
			yield return new Case("<%s>%s</%s>", S("h1"), S("a title"), S("h1"));
			yield return new Case("%% %c %%", N(65));
			yield return new Case("no specifiers at all");
			yield return new Case("");
			yield return new Case("100%%");
			yield return new Case("a\0b%sc\0", S("\0"));
			yield return new Case("%sé%d中", S("中"), N(1));
			yield return new Case("%5.3s|%.0s|%.2s", S("abcdef"), S("abc"), S("abc"));
			yield return new Case("%.10s", S("abc"));
			yield return new Case("%q", S("a string with \"quotes\" and \n new line"));
			yield return new Case("%q", S("a string with \b and \b2"));
			yield return new Case("%q", S("\0\0001\r\n\\\"\u0007\u0010ÿ٠²"));
			yield return new Case("%q%q", N(1.5), N(-0.0));

			// Arguments converted from strings.
			yield return new Case("%d", S("10"));
			yield return new Case("%d", S(" 0x10 "));
			yield return new Case("%5.1f", S("1e2"));
			yield return new Case("%x", S("255"));
			yield return new Case("%c", S("65"));
			yield return new Case("%s", N(12345678901234567890.0));

			// Errors: their type and message must not change.
			yield return new Case("%s %s", N(1));
			yield return new Case("%d");
			yield return new Case("%d", S("toto"));
			yield return new Case("%d", DynValue.True);
			yield return new Case("%d", DynValue.Nil);
			yield return new Case("%f", DynValue.NewTable(new Table(null)));
			yield return new Case("%s", DynValue.False);
			yield return new Case("%s", DynValue.Nil);
			yield return new Case("%q", DynValue.NewTable(new Table(null)));
			yield return new Case("%d", LuaInt64.NewDynValue(5));
			yield return new Case("%s", LuaInt64.NewDynValue(5));
			yield return new Case("%x", LuaUInt64.NewDynValue(5));
			yield return new Case("%k", S("toto"));
			yield return new Case("%------s", S("toto"));
			yield return new Case("%-----s", S("toto"));
			yield return new Case("pi = %.123f", N(Math.PI));
			yield return new Case("% 123s", S("toto"));
			yield return new Case("%100d", N(1));
			yield return new Case("%5", N(1));
			yield return new Case("%", N(1));
			yield return new Case("abc%");
			yield return new Case("%5%", N(1));
			yield return new Case("%a", N(1));
			yield return new Case("%A", N(1));
			yield return new Case("%p", N(1));
			yield return new Case("%n", N(1));
			yield return new Case("%ld", N(1));
			yield return new Case("%hd", N(1));
			yield return new Case("%İd", N(1));
			yield return new Case("%1İd", N(1));
			yield return new Case("%\0d", N(1));
			yield return new Case("%c", N(-1));
			yield return new Case("%c", N(65536));
			yield return new Case("%c", N(-0.5));
			yield return new Case("%c", N(0));
			yield return new Case("%c", N(0x4e2d));
			yield return new Case("%+00d", N(1));
			yield return new Case("%#00x", N(1));
			yield return new Case("%#0o", N(1));
			yield return new Case("%#00o", N(0));
			yield return new Case("%#05o", N(0));
			yield return new Case("%{", N(1));
			yield return new Case("%}", N(1));
			yield return new Case("%5\u0131d", N(1));
			yield return new Case("%12\u0131d", N(1));
			yield return new Case("%.\u0131f", N(1));
			yield return new Case("%1.2\u0131f", N(1));
			yield return new Case("%-\0d", N(1));
			yield return new Case("%-0c", N(65));
			yield return new Case("%0-s", S("abc"));
			yield return new Case("%99.99f", N(-1e308));
			yield return new Case("%+99.99f", N(1e308));
			yield return new Case("%99s", S(new string('a', 99)));
			yield return new Case("%-99.99s", S(new string('a', 200)));
			yield return new Case("%c%c%c", N(72), N(105), S("33"));
		}

		// ---------------------------------------------------------------------------------------
		// Running a case
		// ---------------------------------------------------------------------------------------

		/// <summary>Runs one case through <paramref name="fn"/> and describes the result or the error.</summary>
		internal static string Run(Script script, DynValue fn, Case c)
		{
			try
			{
				var args = new DynValue[c.Args.Length + 1];
				args[0] = DynValue.NewString(c.Format);
				Array.Copy(c.Args, 0, args, 1, c.Args.Length);
				DynValue r = script.Call(fn, args);
				return "ok:" + r.String;
			}
			catch (InterpreterException ex)
			{
				return "err:" + ex.GetType().Name + ":" + ex.Message;
			}
			catch (Exception ex)
			{
				// CLR exceptions escape from the emulation for some inputs; their messages are localised
				// and differ between runtimes, so only the type is compared.
				return "err:" + ex.GetType().Name;
			}
		}

		/// <summary>The KopiLua port of sprintf, which string.format used before LuaFormatter.</summary>
		internal static DynValue KopiLuaFormat()
		{
			return DynValue.NewCallback((ctx, args) => ctx.EmulateClassicCall(args, "format", KopiLuaFormatOracle.str_format), "format");
		}

		static DynValue StringFormat(Script script)
		{
			return script.Globals.Get("string").Table.Get("format");
		}

		// ---------------------------------------------------------------------------------------
		// Golden file
		// ---------------------------------------------------------------------------------------

		/// <summary>
		/// True when doubles format exactly (.NET Core 3.0 and later). The golden file was recorded there;
		/// .NET Framework and Mono print at most 15 significant digits, so high precisions differ.
		/// </summary>
		static bool IsIeeeExactFormattingRuntime()
		{
			return 0.1.ToString("F20", CultureInfo.InvariantCulture) == "0.10000000000000000555";
		}

		/// <summary>
		/// Cases whose result depends on the runtime rather than on MoonSharp: a double outside the range of
		/// the integer type it is cast to (NaN, infinities, beyond 2^63, negative to unsigned). The C# cast
		/// is unspecified there and differs between x64, ARM64 and .NET 9's saturating conversions.
		/// </summary>
		internal static bool IsRuntimeDependent(Case c)
		{
			for (int i = 0, a = 0; i < c.Format.Length; i++)
			{
				if (c.Format[i] != '%')
					continue;
				int j = i + 1;
				if (j < c.Format.Length && c.Format[j] == '%') { i = j; continue; }
				while (j < c.Format.Length && "-+ #0123456789.".IndexOf(c.Format[j]) >= 0)
					j++;
				if (j >= c.Format.Length || a >= c.Args.Length)
					return false;
				char conv = c.Format[j];
				DynValue arg = c.Args[a++];
				i = j;
				if (arg.Type != DataType.Number)
					continue;
				double d = arg.Number;
				bool bad = false;
				switch (conv)
				{
					case 'd':
					case 'i':
						bad = !(d > -9223372036854775808.0 - 1025 && d < 9223372036854775808.0);
						break;
					case 'o':
					case 'u':
					case 'x':
					case 'X':
						bad = !(d > -1 && d < 18446744073709551616.0);
						break;
					case 'c':
						bad = !(d > -1 && d < 65536);
						break;
				}
				if (bad)
					return true;
			}
			return false;
		}

		/// <summary>The golden subset: every 100th generated case plus every hand-written one.</summary>
		internal static IEnumerable<Case> GoldenCases()
		{
			int i = 0;
			foreach (Case c in Generated())
			{
				if (IsRuntimeDependent(c))
					continue;
				if ((i++ % 100) == 0)
					yield return c;
			}
			foreach (Case c in HandWritten())
				if (!IsRuntimeDependent(c))
					yield return c;
		}

		[Test]
		public void StringFormat_MatchesGoldenFile()
		{
			string path = Environment.GetEnvironmentVariable("MOONSHARP_WRITE_FORMAT_GOLDEN");
			if (!string.IsNullOrEmpty(path))
			{
				WriteGolden(path);
				return;
			}

			if (!IsIeeeExactFormattingRuntime())
			{
				TestRunner.Skip();
				Assert.Ignore("The golden file was recorded with .NET Core double formatting.");
			}

			string[] golden = StringFormatGolden.Data;
			Assert.Greater(golden.Length, 3 * 1000, "golden file missing");

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
						var c = new Case(golden[i], DecodeArgs(golden[i + 1]));
						if (culture != "" && UsesCultureDependentNumberText(c))
							continue;
						string actual = Run(script, format, c);
						if (actual != golden[i + 2] && failures++ < 20)
							report.AppendFormat("[{0}] format {1} args {2}: expected {3} got {4}\n",
								culture, Escape(c.Format), Escape(c.ArgsText), Escape(golden[i + 2]), Escape(actual));
					}

					Assert.AreEqual(0, failures, report.ToString());
					return 0;
				});
			}
		}

		/// <summary>
		/// %s and %q turn a number argument into text with the same coercion as the `..` operator,
		/// which uses the current culture for non-integral values (a separate, known issue).
		/// </summary>
		static bool UsesCultureDependentNumberText(Case c)
		{
			foreach (DynValue a in c.Args)
				if (a.Type == DataType.Number && a.Number.ToString() != a.Number.ToString(CultureInfo.InvariantCulture))
					if (c.Format.IndexOf('s') >= 0 || c.Format.IndexOf('q') >= 0)
						return true;
			return false;
		}

		static void WriteGolden(string path)
		{
			var script = new Script(CoreModules.Preset_Complete);
			DynValue format = StringFormat(script);
			var sb = new StringBuilder();
			sb.Append("// Generated by StringFormatTests.WriteGolden (set MOONSHARP_WRITE_FORMAT_GOLDEN to a path and run\n");
			sb.Append("// StringFormat_MatchesGoldenFile). Recorded from the KopiLua string.format before the managed\n");
			sb.Append("// formatter existed, on .NET ").Append(Environment.Version).Append(", invariant culture. Do not edit by hand.\n");
			sb.Append("namespace MoonSharp.Interpreter.Tests.EndToEnd\n{\n\tinternal static class StringFormatGolden\n\t{\n");
			sb.Append("\t\t// Triples: format, arguments (see StringFormatTests.EncodeArgs), result (\"ok:\" or \"err:\").\n");
			sb.Append("\t\tinternal static readonly string[] Data =\n\t\t{\n");

			var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
			System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
			try
			{
				foreach (Case c in GoldenCases())
				{
					string result = Run(script, format, c);
					sb.Append("\t\t\t").Append(CSharpLiteral(c.Format)).Append(", ")
						.Append(CSharpLiteral(c.ArgsText)).Append(", ")
						.Append(CSharpLiteral(result)).Append(",\n");
				}
			}
			finally
			{
				System.Threading.Thread.CurrentThread.CurrentCulture = prev;
			}

			sb.Append("\t\t};\n\t}\n}\n");
			File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
		}

		// ---------------------------------------------------------------------------------------
		// Live differential test against the emulation
		// ---------------------------------------------------------------------------------------

		[Test]
		public void StringFormat_MatchesKopiLuaEmulation()
		{
			foreach (string culture in new[] { "", "tr-TR", "de-DE" })
			{
				Utils.WithCulture(culture == "" ? "en-US" : culture, () =>
				{
					if (culture == "")
						System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

					var script = new Script(CoreModules.Preset_Complete);
					DynValue format = StringFormat(script);
					DynValue kopi = KopiLuaFormat();
					int failures = 0, count = 0;
					var report = new StringBuilder();

					foreach (Case c in Corpus())
					{
						// The full corpus once; a third of it under the other cultures to keep the test quick.
						if (culture != "" && (count++ % 3) != 0)
							continue;
						string expected = Run(script, kopi, c);
						string actual = Run(script, format, c);
						if (actual != expected && failures++ < 20)
							report.AppendFormat("[{0}] format {1} args {2}: expected {3} got {4}\n",
								culture, Escape(c.Format), Escape(c.ArgsText), Escape(expected), Escape(actual));
					}

					Assert.AreEqual(0, failures, report.ToString());
					return 0;
				});
			}
		}

		// The format argument itself: numbers are converted, anything else is an error.
		[Test]
		public void StringFormat_FormatArgumentMatchesKopiLua()
		{
			var script = new Script(CoreModules.Preset_Complete);
			script.Globals["kformat"] = KopiLuaFormat();
			DynValue res = script.DoString(@"
				local out = {}
				for _, f in ipairs({ string.format, kformat }) do
					local t = {}
					for _, args in ipairs({ {12}, {1.5}, {}, {true}, {{}}, {'%d', '0x1F'}, {'%5.1f', ' 2e1 '} }) do
						local ok, r = pcall(f, table.unpack(args))
						t[#t + 1] = tostring(ok) .. ':' .. tostring(r)
					end
					out[#out + 1] = table.concat(t, '|')
				end
				return out[1], out[2]");

			Assert.AreEqual(res.Tuple[1].String, res.Tuple[0].String);
			StringAssert.StartsWith("true:12|true:1.5|false:", res.Tuple[0].String);
		}

		// Numeric conversions never depend on the culture, %s and %q of strings neither.
		[Test]
		public void StringFormat_NumbersIgnoreCulture()
		{
			const string code = @"return string.format('%9.2f|%e|%g|%G|%d|%x|%o|%12.3E|%-8.1f|%+.0f|%s|%q',
				1234.5, -0.000123, 1e-5, 1.5e300, -42, 255, 8, 6.02e23, -2.5, 0.75, 'a,b', 'c.d')";

			string expected = Utils.WithCulture("en-US", () => new Script(CoreModules.String).DoString(code).String);
			Assert.AreEqual("  1234.50|-1.230000e-004|1e-05|1.5E+300|-42|ff|10|  6.020E+023|-2.5    |+1|a,b|\"c.d\"", expected);

			foreach (string culture in new[] { "tr-TR", "de-DE", "fr-FR", "ar-SA", "fa-IR", "sv-SE" })
				Assert.AreEqual(expected, Utils.WithCulture(culture, () => new Script(CoreModules.String).DoString(code).String), culture);
		}

		// The managed formatter must not pay for the emulation's buffers and regular expressions:
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

		internal static string EncodeArgs(DynValue[] args)
		{
			var sb = new StringBuilder();
			for (int i = 0; i < args.Length; i++)
			{
				if (i > 0)
					sb.Append('\u001f');
				DynValue a = args[i];
				switch (a.Type)
				{
					case DataType.Number:
						double d = a.Number;
						sb.Append("n:");
						if (double.IsNaN(d)) sb.Append("nan");
						else if (double.IsPositiveInfinity(d)) sb.Append("inf");
						else if (double.IsNegativeInfinity(d)) sb.Append("-inf");
						else if (d == 0 && 1 / d < 0) sb.Append("-0");
						else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
						break;
					case DataType.String:
						sb.Append("s:").Append(a.String);
						break;
					case DataType.Boolean:
						sb.Append(a.Boolean ? "b:true" : "b:false");
						break;
					case DataType.Nil:
						sb.Append("nil");
						break;
					case DataType.Table:
						sb.Append("table");
						break;
					case DataType.UserData:
						if (a.UserData.Object is LuaInt64)
							sb.Append("i64:").Append(((LuaInt64)a.UserData.Object).Value.ToString(CultureInfo.InvariantCulture));
						else if (a.UserData.Object is LuaUInt64)
							sb.Append("u64:").Append(((LuaUInt64)a.UserData.Object).Value.ToString(CultureInfo.InvariantCulture));
						else
							throw new NotSupportedException();
						break;
					default:
						throw new NotSupportedException(a.Type.ToString());
				}
			}
			return sb.ToString();
		}

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
				else if (p.StartsWith("i64:")) res[i] = LuaInt64.NewDynValue(long.Parse(p.Substring(4), CultureInfo.InvariantCulture));
				else if (p.StartsWith("u64:")) res[i] = LuaUInt64.NewDynValue(ulong.Parse(p.Substring(4), CultureInfo.InvariantCulture));
				else throw new FormatException(p);
			}
			return res;
		}

		static string CSharpLiteral(string s)
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

		static string Escape(string s)
		{
			return CSharpLiteral(s);
		}
	}
}
