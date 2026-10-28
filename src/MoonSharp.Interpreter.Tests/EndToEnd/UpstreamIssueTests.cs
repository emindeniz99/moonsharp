using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MoonSharp.Interpreter.Loaders;
using MoonSharp.Interpreter.Platforms;
using System.Linq.Expressions;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	public static class UpstreamIssueWidgetSizeExtensions
	{
		public static UpstreamIssueTests.Widget Grow(this UpstreamIssueTests.Widget w) { return w; }
	}

	public static class UpstreamIssueWidgetColorExtensions
	{
		public static UpstreamIssueTests.Widget Paint(this UpstreamIssueTests.Widget w) { return w; }
		public static string Describe(this UpstreamIssueTests.Widget w) { return "painted"; }
	}

	/// <summary>
	/// Regression tests for bugs reported on upstream MoonSharp
	/// (https://github.com/moonsharp-devs/moonsharp/issues). Each test names its issue;
	/// docs/upstream-issues.md lists every issue and what this fork does about it.
	/// </summary>
	[TestFixture]
	public class UpstreamIssueTests
	{
		public class Widget
		{
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/102 (also #305, #328): a host that
		// splits its helpers over several extension classes could call only the first
		// extension method a script used; every later one failed with "cannot access field".
		[Test]
		public void Issue102_EveryRegisteredExtensionMethodIsFound()
		{
			UserData.RegisterType<Widget>();
			UserData.RegisterExtensionType(typeof(UpstreamIssueWidgetSizeExtensions));
			UserData.RegisterExtensionType(typeof(UpstreamIssueWidgetColorExtensions));

			Script s = new Script();
			s.Globals["w"] = new Widget();

			DynValue r = s.DoString("return w:Grow():Paint():Describe()");

			Assert.AreEqual("painted", r.String);
		}

		public class Connection
		{
			public string Name = "base";
		}

		public class SecureConnection : Connection
		{
			public SecureConnection() { Name = "secure"; }
		}

		public class Command
		{
			public Connection Connection { get; set; }
			public int Timeout = 10;
		}

		public class SecureCommand : Command
		{
			public new SecureConnection Connection { get; set; }
			public new int Timeout = 30;
		}

		public class LegacyCommand : Command
		{
			private new int Timeout = 99;
			public int PrivateTimeout() { return Timeout; }
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/286: a type that hides an inherited
		// property or field with "new" (common in database and UI libraries) could not be
		// registered at all: "Multiple members named Connection are being added".
		// Scripts see the member C# code sees through the derived type.
		[Test]
		public void Issue286_MembersHiddenWithNewUseTheDerivedMember()
		{
			UserData.RegisterType<SecureCommand>();
			UserData.RegisterType<SecureConnection>();

			Script s = new Script();
			s.Globals["cmd"] = new SecureCommand() { Connection = new SecureConnection() };

			DynValue r = s.DoString("return cmd.Connection.Name .. ' ' .. cmd.Timeout");

			Assert.AreEqual("secure 30", r.String);
		}

		// A private "new" member hides nothing from code outside the type, so scripts keep
		// seeing the inherited public one.
		[Test]
		public void Issue286_PrivateNewMemberKeepsTheInheritedOne()
		{
			UserData.RegisterType<LegacyCommand>();

			Script s = new Script();
			s.Globals["cmd"] = new LegacyCommand();

			Assert.AreEqual(10.0, s.DoString("return cmd.Timeout").Number);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/291: a delegate compiled from an
		// expression tree has a method without a declaring type, and passing it to a script threw
		// NullReferenceException. Hosts build such delegates for rules and generated accessors.
		[Test]
		public void Issue291_CompiledExpressionDelegateIsCallable()
		{
			ParameterExpression x = Expression.Parameter(typeof(int), "x");
			Func<int, int> twice = Expression.Lambda<Func<int, int>>(Expression.Multiply(x, Expression.Constant(2)), x).Compile();

			Script s = new Script();
			s.Globals["twice"] = twice;

			Assert.AreEqual(42.0, s.DoString("return twice(21)").Number);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/307: a CLR callback that returns null
		// (an easy mistake when a C# method has nothing to return) crashed the VM with
		// NullReferenceException instead of giving the script nil.
		[Test]
		public void Issue307_CallbackReturningNullGivesNil()
		{
			Script s = new Script();
			s.Globals["lookup"] = new CallbackFunction((ctx, args) => null);

			DynValue r = s.DoString("local v = lookup('missing') return v == nil, (lookup('missing'))");

			Assert.AreEqual(true, r.Tuple[0].Boolean);
			Assert.IsTrue(r.Tuple[1].IsNil());
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/173 (upstream PR): debug.getmetatable
		// threw NullReferenceException for any value without a metatable, so a debugging or
		// serialization helper could not even ask whether a value has one.
		[Test]
		public void Issue173_DebugGetmetatableReturnsNilWithoutMetatable()
		{
			Script s = new Script(CoreModules.Preset_Complete);

			DynValue r = s.DoString(@"
				local mt = {}
				return debug.getmetatable({}) == nil, debug.getmetatable(1) == nil,
					debug.getmetatable(setmetatable({}, mt)) == mt");

			Assert.AreEqual(true, r.Tuple[0].Boolean);
			Assert.AreEqual(true, r.Tuple[1].Boolean);
			Assert.AreEqual(true, r.Tuple[2].Boolean);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/219 (also #257): json.parse read "[["
		// as the start of a Lua long string, so valid JSON with nested arrays (matrices, rows of
		// a table) failed with "Unexpected token".
		[Test]
		public void Issue219_JsonParsesNestedArrays()
		{
			Script s = new Script();

			DynValue r = s.DoString(@"
				local m = json.parse('[[1,2],[3,4]]')
				local o = json.parse('{""rows"":[[""a"",""b""],[""c"",""d""]]}')
				local deep = json.parse('[[[5]]]')
				return m[2][1] + m[1][2], o.rows[2][2], deep[1][1][1]");

			Assert.AreEqual(5.0, r.Tuple[0].Number);
			Assert.AreEqual("d", r.Tuple[1].String);
			Assert.AreEqual(5.0, r.Tuple[2].Number);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/229: tonumber followed .NET number
		// parsing instead of Lua's numerals. Hexadecimal input such as colours or flags read from
		// a config ("0x23") gave nil, while strings Lua rejects ("1,000", "(5)") became numbers.
		[Test]
		public void Issue229_TonumberReadsLuaNumerals()
		{
			Script s = new Script();

			DynValue r = s.DoString(@"
				return tonumber('0x23'), tonumber(' -0X1f '), tonumber('0x.8'), tonumber('0x1p4'),
					tonumber('1e2'), tonumber(' .5 '), tonumber('5.')");

			double[] expected = { 35, -31, 0.5, 16, 100, 0.5, 5 };

			for (int i = 0; i < expected.Length; i++)
				Assert.AreEqual(expected[i], r.Tuple[i].Number, "value " + (i + 1));
		}

		[Test]
		public void Issue229_TonumberRejectsWhatLuaRejects()
		{
			string[] notNumerals = { "0x", "0xg", "0x1p", "1,000", "(5)", "nan", "inf", "", " ", "1e", "1 2", "0x1 2" };

			foreach (string text in notNumerals)
			{
				Script s = new Script();
				s.Globals["text"] = text;
				Assert.IsTrue(s.DoString("return tonumber(text)").IsNil(), "'" + text + "' is not a number");
			}
		}

		// Arithmetic coerces strings with the same rules as tonumber; DynValue.CastToNumber still
		// uses .NET parsing and should call BasicModule.TryParseLuaNumber.
		[Test]
		public void Issue229_ArithmeticCoercesLuaNumerals()
		{
			Script s = new Script();

			DynValue r = s.DoString("return '0x10' + 1, pcall(function() return '1,000' + 1 end)");

			Assert.AreEqual(17.0, r.Tuple[0].Number);
			Assert.AreEqual(false, r.Tuple[1].Boolean);
		}

		// The io module needs a full platform; Unity's default (the embedded runner's) leaves it out.
		private static Script NewScriptWithIo()
		{
			IPlatformAccessor saved = Script.GlobalOptions.Platform;

			try
			{
				Script.GlobalOptions.Platform = new StandardPlatformAccessor();
				return new Script(CoreModules.Preset_Complete);
			}
			finally
			{
				Script.GlobalOptions.Platform = saved;
			}
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/70: Script.Options.Stdout (and Stdin,
		// Stderr) were read only while the Script was constructed, so a host that redirected a
		// script's output afterwards (to a log or a UI console) still got it on the process console.
		[Test]
		public void Issue70_StandardStreamsSetAfterCreationAreUsed()
		{
			Script s = NewScriptWithIo();
			MemoryStream output = new MemoryStream();
			s.Options.Stdout = output;

			s.DoString("io.write('logged') io.flush()");

			Assert.AreEqual("logged", Encoding.UTF8.GetString(output.ToArray()));
		}

		// Found by the conformance corpus, no upstream issue: json.parse read strings with Lua's
		// escape rules, so JSON's "\u00e9" failed with "'{' expected near '\u'". Every JSON
		// writer escapes non-ASCII text this way, so such input failed whenever it held an accent.
		[Test]
		public void Json_ParsesUnicodeEscapes()
		{
			Script s = new Script();

			DynValue r = s.DoString(@"
				local t = json.parse('[""caf\\u00e9"", ""\\ud83d\\udc4b"", ""a\\/b\\u0041""]')
				return t[1], t[2], t[3]");

			Assert.AreEqual("caf\u00e9", r.Tuple[0].String);
			Assert.AreEqual("\ud83d\udc4b", r.Tuple[1].String);
			Assert.AreEqual("a/bA", r.Tuple[2].String);
		}

		[Test]
		public void Json_RejectsLuaOnlyEscapes()
		{
			string[] notJson = { "[\"\\x41\"]", "[\"\\65\"]", "[\"\\u{41}\"]", "[\"\\u12\"]", "[\"\\a\"]" };

			foreach (string text in notJson)
			{
				Script s = new Script();
				s.Globals["text"] = text;
				Assert.AreEqual(false, s.DoString("return (pcall(json.parse, text))").Boolean, text + " is not JSON");
			}
		}

		// Found by the conformance corpus, no upstream issue: json.serialize wrote a table with
		// keys 1..n as an array even when it also had string keys, and silently dropped them
		// ({1, name = "x"} became [1]). Such a table is an object; integer keys become strings,
		// as the host's JSON writer does.
		[Test]
		public void Json_SerializeKeepsStringKeysOfMixedTables()
		{
			Script s = new Script();

			DynValue r = s.DoString(@"
				local t = json.parse(json.serialize({ 10, 20, name = 'x' }))
				local sparse = json.parse(json.serialize({ [3] = 'c' }))
				return t['1'], t['2'], t.name, sparse['3'], json.serialize({ 1, 2 })");

			Assert.AreEqual(10.0, r.Tuple[0].Number);
			Assert.AreEqual(20.0, r.Tuple[1].Number);
			Assert.AreEqual("x", r.Tuple[2].String);
			Assert.AreEqual("c", r.Tuple[3].String);
			Assert.AreEqual("[1,2]", r.Tuple[4].String);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/161: io.write to the standard output
		// stayed in a StreamWriter buffer until io.flush. C flushes stdout when the process exits;
		// a .NET writer is never flushed for the script, so output was lost or showed up late.
		[Test]
		public void Issue161_StandardOutputIsWrittenWithoutFlush()
		{
			Script s = NewScriptWithIo();
			MemoryStream output = new MemoryStream();
			s.Options.Stdout = output;

			s.DoString("io.write('progress ', 50, '%')");

			Assert.AreEqual("progress 50%", Encoding.UTF8.GetString(output.ToArray()));
		}

		// ---------------------------------------------------------------------------------------
		// Bugs that reproduce in code owned by work running in parallel. Each test fails today and
		// is ignored until that work picks it up; remove the Ignore with the fix.
		// ---------------------------------------------------------------------------------------

		// https://github.com/moonsharp-devs/moonsharp/issues/318: trimming a string longer than a
		// few dozen characters with the common '^%s*(.-)%s*$' idiom failed with "pattern too complex".
		[Test]
		[Ignore("waits for string library work: https://github.com/moonsharp-devs/moonsharp/issues/318")]
		public void Issue318_TrimIdiomWorksOnLongStrings()
		{
			Script s = new Script();

			DynValue r = s.DoString("local body = string.rep('ab ', 100) return (('  ' .. body .. '  '):gsub('^%s*(.-)%s*$', '%1')) == body:sub(1, -2)");

			Assert.AreEqual(true, r.Boolean);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/187 (upstream PR #184): patterns compared
		// only the low byte of each UTF-16 unit, so '\n' (0x0A) also matched U+4E0A and the reverse.
		[Test]
		[Ignore("waits for string library work: https://github.com/moonsharp-devs/moonsharp/issues/187")]
		public void Issue187_PatternsCompareWholeCharacters()
		{
			Script s = new Script();
			s.Globals["text"] = "a\n上b";

			DynValue r = s.DoString("return (text:gsub('\\n', 'N')), (text:gsub('上', '@'))");

			Assert.AreEqual("aN上b", r.Tuple[0].String);
			Assert.AreEqual("a\n@b", r.Tuple[1].String);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/134: %p used Char.IsPunctuation, which
		// leaves out symbols C's ispunct counts ('=', '+', '$', '<', '|', '~', '^', '`').
		[Test]
		[Ignore("waits for string library work: https://github.com/moonsharp-devs/moonsharp/issues/134")]
		public void Issue134_PunctuationClassMatchesAsciiPunctuation()
		{
			Script s = new Script();

			DynValue r = s.DoString("return ('a=b+c$<|~^`'):gsub('%p', '')");

			Assert.AreEqual("abc", r.Tuple[0].String);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/275 (upstream PR #280): Lua 5.2 formats
		// any value with %s through tostring; MoonSharp rejected nil and tables.
		[Test]
		[Ignore("waits for string library work: https://github.com/moonsharp-devs/moonsharp/issues/275")]
		public void Issue275_FormatPercentSAcceptsAnyValue()
		{
			Script s = new Script();

			DynValue r = s.DoString("return string.format('%s %s %s', nil, true, {}):match('^nil true table: ') ~= nil");

			Assert.AreEqual(true, r.Boolean);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/315: a script can store t[0] and t[-1],
		// pairs from C# lists them, but Table.Get(0) and Table.Get(-1) returned nil.
		[Test]
		public void Issue315_TableGetReadsZeroAndNegativeKeys()
		{
			Script s = new Script();
			s.DoString("t = {} t[0] = 'zero' t[-1] = 'minus one'");
			Table t = s.Globals.Get("t").Table;

			Assert.AreEqual("zero", t.Get(0).String);
			Assert.AreEqual("minus one", t.Get(-1).String);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/308 (upstream PR #355): the error of a
		// multi-key Table.Get showed the template "Key '{0}' ..." instead of the key.
		[Test]
		public void Issue308_MultiKeyGetNamesTheMissingKey()
		{
			Script s = new Script();
			Table t = new Table(s);

			ScriptRuntimeException ex = Assert.Throws<ScriptRuntimeException>(() => t.Get("settings", "volume"));

			StringAssert.Contains("'settings'", ex.Message);
		}

		// Upstream PR #179 (https://github.com/moonsharp-devs/moonsharp/pull/179): Table.Set(key, null)
		// threw NullReferenceException deep in the table code; a null value means nil, as t[k] = nil.
		[Test]
		public void PR179_TableSetWithNullRemovesTheKey()
		{
			Script s = new Script();
			Table t = new Table(s);
			t.Set("name", DynValue.NewString("x"));

			t.Set("name", null);

			Assert.IsTrue(t.Get("name").IsNil());
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/287: an empty tuple became one nil, so
		// table.unpack({}, 1, 0) returned one value instead of none.
		[Test]
		[Ignore("waits for VM core work (DynValue.NewTuple): https://github.com/moonsharp-devs/moonsharp/issues/287")]
		public void Issue287_EmptyUnpackReturnsNoValues()
		{
			Script s = new Script();

			Assert.AreEqual(0.0, s.DoString("return select('#', table.unpack({}, 1, 0))").Number);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/198 (upstream PR #324): a vararg function
		// called with no arguments saw one nil, so select('#', ...) was 1.
		[Test]
		[Ignore("waits for VM core work: https://github.com/moonsharp-devs/moonsharp/issues/198")]
		public void Issue198_VarargsWithoutArgumentsAreEmpty()
		{
			Script s = new Script();

			Assert.AreEqual(0.0, s.DoString("local function count(...) return select('#', ...) end return count()").Number);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/263: "local x" without a value could
		// take a leftover value from the stack instead of nil.
		[Test]
		public void Issue263_LocalWithoutValueIsNil()
		{
			Script s = new Script();

			Assert.IsTrue(s.DoString("for k, v in pairs({ 1 }) do local unset; return unset end").IsNil());
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/258: a local assigned from a function
		// that returns nothing held "no value", so type(x) failed with "value expected".
		[Test]
		public void Issue258_LocalFromVoidCallIsNil()
		{
			Script s = new Script();

			Assert.AreEqual("nil", s.DoString("local function nothing() end local v = nothing() return type(v)").String);
		}

		// Upstream PR #327 (https://github.com/moonsharp-devs/moonsharp/pull/327): concatenation
		// formatted numbers with the process culture, so 1.5 .. '' was "1,5" under a decimal-comma
		// culture while tostring(1.5) was "1.5".
		[Test]
		public void PR327_ConcatenationIgnoresTheCulture()
		{
			CultureInfo old = Thread.CurrentThread.CurrentCulture;

			try
			{
				Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
				Script s = new Script();

				Assert.AreEqual("1.5", s.DoString("return 1.5 .. ''").String);
			}
			finally
			{
				Thread.CurrentThread.CurrentCulture = old;
			}
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/285: the package table had only loaded
		// and config. Code that registers modules in package.preload (module bundlers, compilers
		// to Lua) could not run.
		[Test]
		[Ignore("waits for module registration work (require): https://github.com/moonsharp-devs/moonsharp/issues/285")]
		public void Issue285_RequireUsesPackagePreload()
		{
			Script s = new Script();

			DynValue r = s.DoString("package.preload['app.config'] = function(name) return { name = name } end return require('app.config').name, type(package.path)");

			Assert.AreEqual("app.config", r.Tuple[0].String);
			Assert.AreEqual("string", r.Tuple[1].String);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/332: require ignored package.path and
		// read only the LUA_PATH global and ScriptLoader.ModulePaths.
		[Test]
		[Ignore("waits for module registration work (require): https://github.com/moonsharp-devs/moonsharp/issues/332")]
		public void Issue332_RequireUsesPackagePath()
		{
			string dir = Path.Combine(Path.GetTempPath(), "moonsharp-issue332-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			try
			{
				File.WriteAllText(Path.Combine(dir, "point.lua"), "return { x = 1 }");
				Script s = new Script();
				s.Options.ScriptLoader = new FileSystemScriptLoader();
				s.Globals["dir"] = dir;

				Assert.AreEqual(1.0, s.DoString("package.path = dir .. '/?.lua' return require('point').x").Number);
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}
	}
}
