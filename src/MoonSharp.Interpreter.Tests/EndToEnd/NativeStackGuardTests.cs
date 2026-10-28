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

		static string Chain(string first, string link, int length)
		{
			var sb = new StringBuilder(first);
			for (int i = 0; i < length; i++) sb.Append(link);
			return sb.ToString();
		}

		[Test]
		public void NativeStack_LongOperatorChainsCompileAndRun()
		{
			// A chain parses in a loop into a tree as deep as the chain; the compiler walks that tree
			// without recursion, so no chain length reaches the native stack (Mono's stack probe let a
			// million-term chain overflow it). Lua compiles such chains too.
			const int length = 200000;
			Script s = new Script();

			Assert.AreEqual(length + 1, s.DoString(Chain("return 1", "+1", length)).Number);
			// ^ and .. are right associative: every operand waits on the value stack, which holds 10000 easily.
			Assert.AreEqual(2, s.DoString(Chain("return 2", "^1", 10000)).Number);
			Assert.AreEqual(true, s.DoString(Chain("return true", " and true", length)).Boolean);
			Assert.AreEqual(1, s.DoString(Chain("return false", " or 1", length)).Number);
			Assert.AreEqual("aaaa", s.DoString(Chain("return 'a'", " .. 'a'", 3)).String);
			Assert.AreEqual(true, s.DoString(Chain("return 2 > 1", " == true", length)).Boolean);

			// Dynamic expressions evaluate the same trees.
			Assert.AreEqual(length + 1, s.CreateDynamicExpression(Chain("1", "+1", length)).Evaluate().Number);
			Assert.AreEqual(1, s.CreateDynamicExpression(Chain("false", " or 1", length)).Evaluate().Number);
		}

		[Test]
		public void NativeStack_LongSuffixChainsCompileAndRun()
		{
			// a.b.c... and f()()... also parse in a loop into a tree as deep as the chain; 300000 links
			// ended the process.
			const int length = 300000;
			Script s = new Script();
			s.DoString("t = { v = 1 } t.t = t function f() return f end o = { m = function(self) return self end, v = 2 }");

			Assert.AreEqual(1, s.DoString(Chain("return t", ".t", length) + ".v").Number);
			Assert.AreEqual(1, s.DoString(Chain("return t", "['t']", length) + ".v").Number);
			Assert.AreEqual(DataType.Function, s.DoString(Chain("return f", "()", length)).Type);
			Assert.AreEqual(2, s.DoString(Chain("return o", ":m()", length) + ".v").Number);

			s.DoString(Chain("t", ".t", length) + ".v = 3");
			Assert.AreEqual(3, s.DoString("return t.v").Number);

			Assert.AreEqual(3, s.CreateDynamicExpression(Chain("t", ".t", length) + ".v").Evaluate().Number);
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

		// Lua 5.2 counts metamethod calls as C calls (luaD_call) and raises "C stack overflow" at 200 nested
		// ones. MoonSharp runs a Lua metamethod in a frame of the same loop, and an __index function that
		// indexes its own table ran forever: at 65,536 frames the tail call optimisation reused the frame.
		// lua5.2 gives "C stack overflow" for each of these.
		[Test]
		public void NativeStack_MetamethodRecursionIsACStackOverflow()
		{
			Script s = new Script();
			// Without the limit the first case never returns; stop it so the test fails instead of hanging.
			s.Options.InterruptHandler = n => n > 50000000;

			DynValue r = s.DoString(@"
				local function try(f) local ok, e = pcall(f) return (ok and 'ok' or e:match('C stack overflow$') or e) end
				local function mt(name, f) local m = {} m[name] = f return setmetatable({}, m) end
				local out = {}
				out[#out + 1] = try(function() local t = mt('__index', function(t, k) return t[k] end) return t.x end)
				out[#out + 1] = try(function() local t = mt('__index', function(t, k) local v = t[k] return v end) return t.x end)
				out[#out + 1] = try(function() local t = mt('__newindex', function(t, k, v) t[k] = v end) t.x = 1 end)
				out[#out + 1] = try(function() local a = mt('__add', function(a, b) return a + b end) return a + 1 end)
				out[#out + 1] = try(function() local a = mt('__concat', function(a, b) return a .. b end) return a .. 'x' end)
				out[#out + 1] = try(function() local a = mt('__len', function(a) return #a end) return #a end)
				out[#out + 1] = try(function() local a = mt('__unm', function(a) return -a end) return -a end)
				local cmp = {}
				cmp.__eq = function(a, b) return a == b end
				cmp.__lt = function(a, b) return a < b end
				cmp.__le = function(a, b) return a <= b end
				local a, b = setmetatable({}, cmp), setmetatable({}, cmp)
				out[#out + 1] = try(function() return a == b end)
				out[#out + 1] = try(function() return a < b end)
				out[#out + 1] = try(function() return a <= b end)
				return table.concat(out, ',')");

			Assert.AreEqual("C stack overflow,C stack overflow,C stack overflow,C stack overflow,C stack overflow,"
				+ "C stack overflow,C stack overflow,C stack overflow,C stack overflow,C stack overflow", r.String);

			// The error is at the indexing expression, and the Script keeps working.
			ScriptRuntimeException ex = Assert.Throws<ScriptRuntimeException>(() =>
				s.DoString("local t = setmetatable({}, { __index = function(t, k) return t[k] end })\nreturn t.x"));
			Assert.AreEqual("C stack overflow", ex.Message);
			StringAssert.Contains(":(1,", ex.DecoratedMessage);
			Assert.AreEqual(3, s.DoString("return 1 + 2").Number);
		}

		// The limit counts nested metamethod calls only. 150 levels pass (Lua 5.2 too); calls made by a
		// metamethod function, and __call, which Lua 5.2 does not count either, are not limited.
		[Test]
		public void NativeStack_MetamethodNestingBelowTheLimitWorks()
		{
			DynValue r = new Script().DoString(@"
				local depth = 0
				local t = setmetatable({}, { __index = function(t, k) depth = depth + 1 if depth < 150 then return t[k] end return depth end })
				local function deep(n) if n == 0 then return t.x end return deep(n - 1) end
				local d = setmetatable({}, { __index = function(_, k) return deep(1000) end })
				local c = setmetatable({}, { __call = function(self, n) if n == 0 then return 'called' end return self(n - 1) end })
				return t.x, d.y, c(5000)");

			Assert.AreEqual(150, r.Tuple[0].Number);
			Assert.AreEqual(151, r.Tuple[1].Number);
			Assert.AreEqual("called", r.Tuple[2].String);
		}

		// A metamethod's results are adjusted to one value, as in Lua: t.x is nil, not nothing, when __index
		// returns nothing, and one value when it returns two.
		[Test]
		public void Metamethod_ResultIsOneValue()
		{
			DynValue r = new Script().DoString(@"
				local none = setmetatable({}, { __index = function() end, __add = function() end })
				local two = setmetatable({}, { __index = function() return 1, 2 end, __concat = function() return 'a', 'b' end })
				return select('#', none.x), select('#', none + 1), select('#', two.x), select('#', two .. 'x'), two.x");

			Assert.AreEqual(1, r.Tuple[0].Number);
			Assert.AreEqual(1, r.Tuple[1].Number);
			Assert.AreEqual(1, r.Tuple[2].Number);
			Assert.AreEqual(1, r.Tuple[3].Number);
			Assert.AreEqual(1, r.Tuple[4].Number);
		}
	}
}
