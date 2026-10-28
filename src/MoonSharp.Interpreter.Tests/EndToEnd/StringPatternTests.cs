using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// string.find, match, gmatch and gsub moved from a port of KopiLua's matcher to LuaPatterns, a port of
	/// Lua 5.2.4's lstrlib.c over string indices. These tests compare the two live (KopiLuaPatternOracle
	/// keeps the old code) over a generated corpus, and allow only the differences listed in
	/// <see cref="IntendedDifference"/>, each of which is a bug of the old matcher or a Lua 5.2 rule it
	/// did not follow. The hand-written tests pin the intended behaviour itself.
	/// </summary>
	[TestFixture]
	public class StringPatternTests
	{
		// ---------------------------------------------------------------------------------------
		// Intended behaviour
		// ---------------------------------------------------------------------------------------

		static DynValue Run(string code)
		{
			return new Script(CoreModules.Preset_Complete).DoString(code);
		}

		[Test]
		public void Patterns_TrimIdiomWorksOnLongStrings()
		{
			// The old matcher never gave back its recursion budget, so a long subject ran out of it.
			DynValue r = Run("local s = string.rep('ab ', 2000) return (('  ' .. s .. '  '):match('^%s*(.-)%s*$')) == s:sub(1, -2)");
			Assert.AreEqual(true, r.Boolean);
		}

		[Test]
		public void Patterns_DeepRecursionStillFails()
		{
			// Each '?' item nests one Match call: 300 of them exceed Lua's limit of 200.
			var ex = Assert.Throws<ScriptRuntimeException>(() => Run("return string.rep('a', 300):match(string.rep('a?', 300))"));
			StringAssert.Contains("pattern too complex", ex.Message);
		}

		[Test]
		public void Patterns_CompareWholeCodeUnits()
		{
			// U+4E0A has 0x0A ('\n') as its low byte, U+0130 has 0x30 ('0'), U+0161 has 0x61 ('a').
			DynValue r = Run(@"local s = 'a\n上b'
				return { (s:gsub('\n', 'N')), (s:gsub('上', '@')), (s:gsub('%s', '_')), ('İ0'):find('0'), ('ša'):find('a'),
					('x上y'):match('[一-龥]'), ('上'):match('[%z\1-\127]') == nil, ('上'):match('.'), ('上'):find('%上') }");
			CollectionAssert.AreEqual(new object[] { "aN上b", "a\n@b", "a_上b", 2.0, 2.0, "上", true, "上", 1.0, 1.0 },
				r.Table.Values.Select(v => v.ToObject()).ToArray());
		}

		[Test]
		public void Patterns_ClassesCoverCodeUnitsBelow256()
		{
			// .NET's classification below 256; a code unit above belongs to no class (Lua sees UTF-8 bytes,
			// which in the C locale are in no class either), so it matches only its complement.
			DynValue r = Run(@"return { (('éa1 ğİ€'):gsub('%a', 'A')), (('éa1 ğİ€'):gsub('%A', '.')), ('ğ'):match('%l') == nil,
				('ğ'):match('%W'), ('€'):match('%p') == nil, ('€'):match('%g') == nil, ('x\0y'):find('%z') }");
			CollectionAssert.AreEqual(new object[] { "AA1 ğİ€", "éa.....", true, "ğ", true, true, 2.0, 2.0 },
				r.Table.Values.Select(v => v.ToObject()).ToArray());
		}

		[Test]
		public void Patterns_PunctuationIsCIspunct()
		{
			// %p is C's ispunct: every printable ASCII character that is not a letter, a digit or a space.
			// .NET's IsPunctuation left out symbols such as '$', '+', '<', '=', '>', '^', '`', '|' and '~'.
			var ascii = new StringBuilder();
			for (char c = ' '; c < (char)127; c++)
				ascii.Append(c);

			Script s = new Script(CoreModules.Preset_Complete);
			s.Globals["ascii"] = ascii.ToString();
			DynValue r = s.DoString("return (ascii:gsub('[^%p]', '')), (('a\\194\\162b'):gsub('%p', ''))");

			Assert.AreEqual("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~", r.Tuple[0].String);
			// Below 256, other printable code units that are neither letters nor digits count too (U+00A2).
			Assert.AreEqual("aÂb", r.Tuple[1].String);
		}

		[Test]
		public void Patterns_InitPastTheEndFindsNothing()
		{
			// Lua 5.2: an init after the end of the subject finds nothing, even the empty string.
			DynValue r = Run("return { ('abc'):find('', 4), ('abc'):find('', 5) == nil, ('abc'):match('', 10) == nil, ('abc'):find('c', 3, true) }");
			CollectionAssert.AreEqual(new object[] { 4.0, true, true, 3.0, 3.0 }, r.Table.Values.Select(v => v.ToObject()).ToArray());
		}

		[Test]
		public void Patterns_NulIsAnOrdinaryCharacter()
		{
			// The old matcher rewrote every '\0' in a pattern to %z, which broke ranges such as [\0-\31].
			DynValue r = Run(@"return { (('a\0\1-b'):gsub('[\0-\31]', '.')), (('a\0b'):gsub('\0', '0')), ('\0'):find('%z') }");
			CollectionAssert.AreEqual(new object[] { "a..-b", "a0b", 1.0, 1.0 }, r.Table.Values.Select(v => v.ToObject()).ToArray());
		}

		[Test]
		public void Patterns_ErrorMessagesAreLua52()
		{
			string[,] cases =
			{
				{ "('a'):find('%b')", "malformed pattern (missing arguments to '%b')" },
				{ "('a'):find('(a)%2')", "invalid capture index %2" },
				{ "('a'):find('[a')", "malformed pattern (missing ']')" },
				{ "('a'):find('a%')", "malformed pattern (ends with '%')" },
				{ "('a'):find('%f')", "missing '[' after '%f' in pattern" },
				{ "('a'):match('a)')", "invalid pattern capture" },
				{ "('a'):match('(a')", "unfinished capture" },
				{ "('a'):gsub('a', '%2')", "invalid capture index" },
				{ "('a'):gsub('a', '%')", "invalid use of '%' in replacement string" },
				{ "('a'):gsub('a', {a = {}})", "invalid replacement value (a table)" },
				{ "('a'):gsub('a', true)", "bad argument #3 to 'gsub' (string/function/table expected)" },
				{ "('a'):find(string.rep('()', 33))", "too many captures" },
			};

			for (int i = 0; i < cases.GetLength(0); i++)
			{
				var ex = Assert.Throws<ScriptRuntimeException>(() => Run("return " + cases[i, 0]), cases[i, 0]);
				StringAssert.Contains(cases[i, 1], ex.Message, cases[i, 0]);
			}
		}

		[Test]
		public void Patterns_GsubReplacements()
		{
			DynValue r = Run(@"local t = { hello = 'HI', world = false }
				return { (('hello world'):gsub('%w+', t)), (('hello world'):gsub('(%w+) (%w+)', '%2 %1 %0 %%')),
					(('abc'):gsub('', '-')), (('abc'):gsub('%w', '%1')), (('abc'):gsub('()b', '%1')),
					(('hello world'):gsub('o', function(o) return nil end)), (('a b'):gsub('%w', string.upper)),
					(('abc'):gsub('.', { a = 1 })), select(2, ('aaa'):gsub('a', 'b', 2)), (('x'):gsub('x', 2.5)) }");
			CollectionAssert.AreEqual(new object[] { "HI world", "world hello hello world %", "-a-b-c-", "abc", "a2c", "hello world", "A B", "1bc", 2.0, "2.5" },
				r.Table.Values.Select(v => v.ToObject()).ToArray());
		}

		[Test]
		public void Patterns_GsubWithoutMatchReturnsTheSubject()
		{
			Script s = new Script(CoreModules.Preset_Complete);
			string subject = "no digits here";
			s.Globals["s"] = subject;
			DynValue r = s.DoString("return s:gsub('%d', '')");

			Assert.AreSame(subject, r.Tuple[0].String);
			Assert.AreEqual(0.0, r.Tuple[1].Number);
		}

		[Test]
		public void Patterns_GsubCallbackCanUsePatterns()
		{
			// The match state is reused per thread; a callback that matches again must get its own.
			DynValue r = Run("return (('a1 b22 c333'):gsub('(%a)(%d+)', function(l, d) return l .. #d .. (d:match('^(%d)') or '') .. select(2, d:gsub('%d', '')) end))");
			Assert.AreEqual("a111 b222 c333", r.String);
		}

		[Test]
		public void Patterns_Gmatch()
		{
			DynValue r = Run(@"local out = {}
				for k, v in ('a=1, bb=22, c='):gmatch('(%w+)=(%w*)') do out[#out + 1] = k .. ':' .. v end
				for w in ('one two'):gmatch('%a*') do out[#out + 1] = '[' .. w .. ']' end
				for p in ('abc'):gmatch('()') do out[#out + 1] = p end
				for x in ('^a^b'):gmatch('^%a') do out[#out + 1] = x end
				return table.concat(out, ' ')");
			Assert.AreEqual("a:1 bb:22 c: [one] [] [two] [] 1 2 3 4 ^a ^b", r.String);
		}

		[Test]
		public void Patterns_GmatchIteratorWorksThroughPcall()
		{
			// The old iterator read its state from the calling context, which under pcall is pcall's:
			// a NullReferenceException escaped the script.
			DynValue r = Run("local it = ('ab'):gmatch('.') local ok1, a = pcall(it) local ok2, b = pcall(it) return a .. b .. tostring(ok1 and ok2)");
			Assert.AreEqual("abtrue", r.String);
		}

		// ---------------------------------------------------------------------------------------
		// Differential test against the old matcher
		// ---------------------------------------------------------------------------------------

		static readonly string[] Subjects =
		{
			"", "a", "aaa", "abc", "hello world", "the quick brown fox 123 jumps over the lazy dog 4567",
			"  padded text  ", "key = value, k2=v2", "(a(b)c) [x[y]] (unclosed", "THE (quick) fox", "a.b.c",
			"a\0b\0", "x+y=z $1 ^2 `3 |4 ~5 <6>", "\t\r\n\v\f end", "0123456789abcdefABCDEF",
			"ünïcödé ¡¿ «»§¶ ²³ ×÷  ­¢",
			"a\n上b", "ğİı€ša", "%%%-[]^$", new string('a', 40) + "b",
			"abcabcabc", "))((", "aAbBcC_09", string.Concat(Enumerable.Repeat("ab ", 60)),
		};

		static readonly string[] Atoms =
		{
			"a", "b", "o", " ", "e", "1", ".", "%a", "%A", "%d", "%D", "%s", "%S", "%w", "%W", "%p", "%P", "%l",
			"%L", "%u", "%U", "%x", "%X", "%c", "%C", "%g", "%G", "%z", "%.", "%%", "%-", "%(", "%]", "[aeiou]",
			"[^%s]", "[a-f]", "[%a_]", "[%]%-]", "[^%d]", "[%p%s]", "[0-9]", "[]]", "[^]]", "[%w_]", "[-a]",
			"[a-]", "[^a-c%d]", "x", "上", "é", "[一-龥]", "\0", "[\0-\x1f]", "$", "^",
		};

		static readonly string[] Specials = { "%b()", "%b[]", "%bab", "%f[%w]", "%f[%W]", "%f[%a]", "%f[%s]", "()", "%1", "%2" };

		static readonly string[] Quantifiers = { "", "", "", "*", "+", "-", "?" };

		static readonly string[] RawAlphabet = { "a", ".", "%", "(", ")", "[", "]", "^", "$", "*", "+", "-", "?", "b", "f", "1", "%a", "\0" };

		static readonly string[] StringReplacements = { "%0", "<%1>", "%2", "%%", "x%", "", "[%1|%0]" };

		// A tiny linear congruential generator, so the corpus is the same on every runtime.
		sealed class Lcg
		{
			uint m_State;
			public Lcg(uint seed) { m_State = seed; }
			public int Next(int n) { m_State = m_State * 1664525u + 1013904223u; return (int)((m_State >> 8) % (uint)n); }
			public T Pick<T>(T[] items) { return items[Next(items.Length)]; }
		}

		internal static List<string> GeneratePatterns(int structured, int raw)
		{
			var rng = new Lcg(20261028);
			var patterns = new List<string>();

			for (int i = 0; i < structured; i++)
			{
				var sb = new StringBuilder();
				if (rng.Next(4) == 0)
					sb.Append('^');

				int items = 1 + rng.Next(4);
				int open = 0;

				for (int k = 0; k < items; k++)
				{
					if (rng.Next(4) == 0)
					{
						sb.Append('(');
						open++;
					}

					if (rng.Next(6) == 0)
					{
						sb.Append(rng.Pick(Specials));
					}
					else
					{
						sb.Append(rng.Pick(Atoms));
						sb.Append(rng.Pick(Quantifiers));
					}

					if (open > 0 && rng.Next(3) == 0)
					{
						sb.Append(')');
						open--;
					}
				}

				// Leave a capture unclosed now and then.
				while (open > 0 && rng.Next(8) != 0)
				{
					sb.Append(')');
					open--;
				}

				if (rng.Next(4) == 0)
					sb.Append('$');

				patterns.Add(sb.ToString());
			}

			for (int i = 0; i < raw; i++)
			{
				var sb = new StringBuilder();
				int len = 1 + rng.Next(6);
				for (int k = 0; k < len; k++)
					sb.Append(rng.Pick(RawAlphabet));
				patterns.Add(sb.ToString());
			}

			return patterns;
		}

		sealed class Library
		{
			public DynValue Find, Match, GMatch, GSub;
		}

		static Library NewLibrary(Script script)
		{
			Table t = script.Globals.Get("string").Table;
			return new Library { Find = t.Get("find"), Match = t.Get("match"), GMatch = t.Get("gmatch"), GSub = t.Get("gsub") };
		}

		static Library OldLibrary()
		{
			return new Library
			{
				Find = DynValue.NewCallback((ctx, args) => ctx.EmulateClassicCall(args, "find", KopiLuaPatternOracle.str_find), "find"),
				Match = DynValue.NewCallback((ctx, args) => ctx.EmulateClassicCall(args, "match", KopiLuaPatternOracle.str_match), "match"),
				GMatch = DynValue.NewCallback((ctx, args) => ctx.EmulateClassicCall(args, "gmatch", KopiLuaPatternOracle.str_gmatch), "gmatch"),
				GSub = DynValue.NewCallback((ctx, args) => ctx.EmulateClassicCall(args, "gsub", KopiLuaPatternOracle.str_gsub), "gsub"),
			};
		}

		internal sealed class Case
		{
			public string Op;
			public string Subject;
			public string Pattern;
			public DynValue Extra;
			public string ExtraText;

			public override string ToString()
			{
				return string.Format("{0}({1}, {2}{3})", Op, Quote(Subject), Quote(Pattern), ExtraText == null ? "" : ", " + ExtraText);
			}
		}

		static string Quote(string s)
		{
			var sb = new StringBuilder("\"");
			foreach (char c in s)
			{
				if (c < ' ' || c > '~' || c == '"' || c == '\\')
					sb.AppendFormat("\\u{0:x4}", (int)c);
				else
					sb.Append(c);
			}
			return sb.Append('"').ToString();
		}

		static string Describe(DynValue v)
		{
			switch (v.Type)
			{
				case DataType.Tuple: return "(" + string.Join(",", v.Tuple.Select(Describe).ToArray()) + ")";
				case DataType.String: return Quote(v.String);
				case DataType.Number: return v.Number.ToString("R", CultureInfo.InvariantCulture);
				case DataType.Boolean: return v.Boolean ? "true" : "false";
				default: return v.Type.ToString().ToLowerInvariant();
			}
		}

		static string Execute(Script script, Library lib, Case c)
		{
			try
			{
				DynValue s = DynValue.NewString(c.Subject), p = DynValue.NewString(c.Pattern);

				switch (c.Op)
				{
					case "find":
						return Describe(c.Extra == null ? script.Call(lib.Find, s, p) : script.Call(lib.Find, s, p, c.Extra));
					case "match":
						return Describe(c.Extra == null ? script.Call(lib.Match, s, p) : script.Call(lib.Match, s, p, c.Extra));
					case "gsub":
						return Describe(script.Call(lib.GSub, s, p, c.Extra));
					case "gsub1":
						return Describe(script.Call(lib.GSub, s, p, c.Extra, DynValue.NewNumber(1)));
					default:
						DynValue iter = script.Call(lib.GMatch, s, p);
						var sb = new StringBuilder();
						for (int i = 0; i < 100; i++)
						{
							DynValue r = script.Call(iter);
							if (r.IsNil())
								break;
							sb.Append(Describe(r)).Append(';');
						}
						return sb.ToString();
				}
			}
			catch (ScriptRuntimeException ex)
			{
				return "error: " + ex.Message;
			}
			catch (Exception ex)
			{
				return "exception: " + ex.GetType().Name;
			}
		}

		static bool HasCodeUnitAbove255(string s)
		{
			return s != null && s.Any(ch => ch > 'ÿ');
		}

		// Code units below 256 whose %p membership changed: C's ispunct (printable, not alphanumeric)
		// instead of .NET's IsPunctuation.
		static bool HasPunctuationChange(string s)
		{
			return s.Any(ch => ch < 256 && char.IsPunctuation(ch) != (!char.IsControl(ch) && !char.IsWhiteSpace(ch) && !char.IsLetterOrDigit(ch)));
		}

		/// <summary>
		/// Why the old and the new result of a case may differ, or null if they must be equal.
		/// </summary>
		internal static string IntendedDifference(Case c, string oldResult, string newResult)
		{
			if (oldResult == "error: pattern too complex")
				return "#318: the old matcher counted every Match call against the depth limit and never gave it back";

			if (HasCodeUnitAbove255(c.Subject) || HasCodeUnitAbove255(c.Pattern))
				return "#187: the old matcher compared only the low byte of each code unit";

			if (c.Pattern.IndexOf('\0') >= 0)
				return "the old matcher rewrote '\\0' in a pattern to %z";

			if ((c.Pattern.Contains("%p") || c.Pattern.Contains("%P")) && HasPunctuationChange(c.Subject))
				return "#134: %p is C's ispunct";

			if ((c.Op == "find" || c.Op == "match") && c.Extra != null && c.Extra.Number > c.Subject.Length + 1)
				return "Lua 5.2: an init past the end finds nothing";

			if (oldResult == "error: unbalanced pattern" && newResult == "error: malformed pattern (missing arguments to '%b')")
				return "Lua 5.2 error message for %b";

			if (oldResult.StartsWith("error: invalid capture index ") && newResult == oldResult.Replace("index ", "index %"))
				return "Lua 5.2 error message for a back reference";

			return null;
		}

		internal static IEnumerable<Case> Cases(Script script)
		{
			DynValue table = script.DoString("return { a = 'A', the = 1, o = false, e = 'E', [1] = 'one', [3] = 'three', ab = {} }");
			DynValue function = script.DoString(@"return function(a, b)
				if a == 'o' or a == 2 then return nil end
				if a == 'e' then return false end
				if a == 'x' then return {} end
				if b then return a .. '|' .. b end
				return '<' .. a .. '>'
			end");
			DynValue upper = script.Globals.Get("string").Table.Get("upper");

			var replacements = new List<KeyValuePair<DynValue, string>>();
			foreach (string r in StringReplacements)
				replacements.Add(new KeyValuePair<DynValue, string>(DynValue.NewString(r), Quote(r)));
			replacements.Add(new KeyValuePair<DynValue, string>(table, "table"));
			replacements.Add(new KeyValuePair<DynValue, string>(function, "function"));
			replacements.Add(new KeyValuePair<DynValue, string>(upper, "string.upper"));
			replacements.Add(new KeyValuePair<DynValue, string>(DynValue.NewNumber(7), "7"));

			double[] inits = { 1, 2, -3, 5, -100, 30, 100 };
			int k = 0;

			foreach (string p in GeneratePatterns(1500, 400))
			{
				foreach (string s in Subjects)
				{
					k++;
					double init = inits[k % inits.Length];
					KeyValuePair<DynValue, string> repl = replacements[k % replacements.Count];

					yield return new Case { Op = "find", Subject = s, Pattern = p };
					yield return new Case { Op = (k % 2 == 0) ? "find" : "match", Subject = s, Pattern = p, Extra = DynValue.NewNumber(init), ExtraText = init.ToString(CultureInfo.InvariantCulture) };
					yield return new Case { Op = "match", Subject = s, Pattern = p };
					yield return new Case { Op = "gmatch", Subject = s, Pattern = p };
					yield return new Case { Op = (k % 5 == 0) ? "gsub1" : "gsub", Subject = s, Pattern = p, Extra = repl.Key, ExtraText = repl.Value };
				}
			}
		}

		[Test]
		public void Patterns_MatchTheOldMatcherExceptForIntendedDifferences()
		{
			Script script = new Script(CoreModules.Preset_Complete);
			Library newLib = NewLibrary(script);
			Library oldLib = OldLibrary();

			int total = 0, same = 0, errors = 0;
			var reasons = new Dictionary<string, int>();
			var unexpected = new List<string>();

			foreach (Case c in Cases(script))
			{
				total++;
				string oldResult = Execute(script, oldLib, c);
				string newResult = Execute(script, newLib, c);

				if (newResult.StartsWith("error: "))
					errors++;

				if (oldResult == newResult)
				{
					same++;
					continue;
				}

				string reason = IntendedDifference(c, oldResult, newResult);

				if (reason == null)
				{
					if (unexpected.Count < 30)
						unexpected.Add(string.Format("{0}\n  old: {1}\n  new: {2}", c, oldResult, newResult));
					continue;
				}

				int n;
				reasons.TryGetValue(reason, out n);
				reasons[reason] = n + 1;
			}

			string summary = string.Format("{0} cases, {1} equal, {2} errors under the new matcher; intended differences:\n{3}", total, same, errors,
				string.Join("\n", reasons.OrderBy(kv => kv.Key).Select(kv => "  " + kv.Value + "  " + kv.Key).ToArray()));

			if (Environment.GetEnvironmentVariable("MOONSHARP_PATTERN_REPORT") != null)
				Console.WriteLine(summary);

			Assert.AreEqual(0, unexpected.Count, summary + "\nunexpected differences:\n" + string.Join("\n", unexpected.ToArray()));
			Assert.Greater(total, 200000);
			Assert.Greater(same, total * 9 / 10);
		}
	}
}
