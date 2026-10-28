using System.Text;
using MoonSharp.Interpreter.Serialization.Json;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Recursion that nests on the native stack (C# calling back into Lua, the parser on deeply nested
	/// source, json on deeply nested data) must end in an error the host or pcall can catch. Before the
	/// guard each case below ended the whole process with a StackOverflowException, which no catch block
	/// can stop. The limit is a count, Lua 5.2's 200, so every host stops at the same depth.
	/// </summary>
	[TestFixture]
	public class NativeStackGuardTests
	{
		static string Nest(string open, string middle, string close, int depth)
		{
			var sb = new StringBuilder();
			for (int i = 0; i < depth; i++) sb.Append(open);
			sb.Append(middle);
			for (int i = 0; i < depth; i++) sb.Append(close);
			return sb.ToString();
		}

		[Test]
		public void NativeStack_RecursionThroughStdlibCallbackIsAScriptError()
		{
			Script s = new Script();

			DynValue r = s.DoString(@"
				local depth = 0
				local function f(str) depth = depth + 1 return (string.gsub(str, '.', f)) end
				local ok, err = pcall(f, 'ab')
				return ok, err, depth");

			Assert.IsFalse(r.Tuple[0].Boolean);
			StringAssert.EndsWith("C stack overflow", r.Tuple[1].String);
			// One level for the host's DoString, then one per gsub: the 200th call of f is refused.
			Assert.AreEqual(200, r.Tuple[2].Number);

			// The script still works afterwards.
			Assert.AreEqual(3, s.DoString("return 1 + 2").Number);
		}

		[Test]
		public void NativeStack_RecursionThroughHostCallbackIsCatchable()
		{
			Script s = new Script();
			s.Globals["host"] = DynValue.NewCallback((ctx, args) => ctx.Call(args[0], args[1]));

			DynValue f = s.DoString("local function f(n) return host(f, n + 1) end return f");

			ScriptRuntimeException ex = Assert.Throws<ScriptRuntimeException>(() => s.Call(f, 0));
			Assert.AreEqual("C stack overflow", ex.Message);
			Assert.AreEqual(3, s.DoString("return 1 + 2").Number);
		}

		[Test]
		public void NativeStack_NestedCoroutinesAreAScriptError()
		{
			DynValue r = new Script().DoString(@"
				local function f() return coroutine.wrap(f)() end
				return pcall(f)");

			Assert.IsFalse(r.Tuple[0].Boolean);
			StringAssert.EndsWith("C stack overflow", r.Tuple[1].String);
		}

		static void AssertTooDeep(string code)
		{
			SyntaxErrorException ex = Assert.Throws<SyntaxErrorException>(() => new Script().LoadString(code));
			StringAssert.Contains("chunk has too many syntax levels", ex.Message);
		}

		[Test]
		public void NativeStack_DeeplyNestedSourceIsASyntaxError()
		{
			const int depth = 100000;

			AssertTooDeep("return " + Nest("(", "1", ")", depth));
			AssertTooDeep("return " + Nest("{", "1", "}", depth));
			AssertTooDeep("return " + Nest("- ", "1", "", depth));
			AssertTooDeep("return " + Nest("not ", "1", "", depth));
			AssertTooDeep(Nest("do ", "local x = 1", " end", depth));
			AssertTooDeep("return " + Nest("f(", "1", ")", depth));
			AssertTooDeep(Nest("function f() ", "return 1", " end", depth));
			AssertTooDeep("return " + Nest("(", "1", ")", 300));

			// load() reports it as an ordinary error.
			DynValue r = new Script().DoString("return load('return ' .. string.rep('(', 1e5) .. '1' .. string.rep(')', 1e5))");
			Assert.AreEqual(DataType.Nil, r.Tuple[0].Type);
			StringAssert.Contains("chunk has too many syntax levels", r.Tuple[1].String);
		}

		[Test]
		public void NativeStack_LongOperatorChainIsASyntaxError()
		{
			// Parsed in a loop, but compiled as a tree as deep as the chain; the stack probe stops it.
			var sb = new StringBuilder("return 1");
			for (int i = 0; i < 1000000; i++) sb.Append("+1");

			AssertTooDeep(sb.ToString());
		}

		[Test]
		public void NativeStack_DeeplyNestedJsonIsAScriptError()
		{
			DynValue r = new Script(CoreModules.Preset_Complete).DoString(@"
				local ok1, err1 = pcall(json.parse, string.rep('[ ', 100000))
				local ok2, err2 = pcall(json.parse, string.rep('{""a"":', 100000) .. '1' .. string.rep('}', 100000))

				local deep = {}
				for i = 1, 100000 do deep = { deep } end
				local ok3, err3 = pcall(json.serialize, deep)

				local cycle = {}
				cycle.self = cycle
				local ok4, err4 = pcall(json.serialize, cycle)

				return ok1, err1, ok2, ok3, err3, ok4");

			Assert.IsFalse(r.Tuple[0].Boolean);
			StringAssert.Contains("json nesting exceeds 200 levels", r.Tuple[1].String);
			Assert.IsFalse(r.Tuple[2].Boolean);
			Assert.IsFalse(r.Tuple[3].Boolean);
			StringAssert.Contains("json nesting exceeds 200 levels", r.Tuple[4].String);
			Assert.IsFalse(r.Tuple[5].Boolean);

			Assert.Throws<SyntaxErrorException>(() => JsonTableConverter.ParseString(Nest("[ ", "", " ]", 100000)));
		}

		[Test]
		public void NativeStack_ModerateNestingStillWorks()
		{
			Assert.AreEqual(1, new Script().DoString("return " + Nest("(", "1", ")", 150)).Number);
			Assert.AreEqual(-1, new Script().DoString("return " + Nest("- ", "1", "", 151)).Number);

			var sb = new StringBuilder("return 0");
			for (int i = 0; i < 2000; i++) sb.Append("+1");
			Assert.AreEqual(2000, new Script().DoString(sb.ToString()).Number);

			// 50 levels of Lua -> string.gsub -> Lua are fine.
			DynValue r = new Script().DoString(@"
				local depth = 0
				local function f(m) depth = depth + 1 if depth > 50 then return m end return (string.gsub(m, '.', f)) end
				return f('a'), depth");
			Assert.AreEqual("a", r.Tuple[0].String);
			Assert.AreEqual(51, r.Tuple[1].Number);

			// json 150 levels deep round-trips.
			// (Spaces keep "[[" from reading as a Lua long string in the json lexer.)
			Assert.AreEqual(Nest("[", "1", "]", 150), JsonTableConverter.ParseString(Nest("[ ", "1", " ]", 150)).Table.TableToJson());
		}
	}
}
