using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Library functions with a fast path (math.abs/ceil/floor/sqrt/sin/cos/exp/max/min, string.len/sub/byte,
	/// type, select('#'), table.insert(t, v)) must give the library's results and errors for every argument
	/// shape, and a script that replaces one must get its own function.
	/// </summary>
	[TestFixture]
	public class BuiltinFastCallTests
	{
		static DynValue Run(string code)
		{
			return new Script().DoString(code);
		}

		[Test]
		public void Builtin_ReplacedFunctionRunsAfterAFastCall()
		{
			DynValue r = Run(@"
				local function f(x) return math.floor(x) end
				local floor = math.floor
				local a = f(2.5)
				math.floor = function() return 42 end
				local s = 'abc'
				local b = s:len()
				string.len = function() return -1 end
				return a, f(2.5), floor(2.5), b, s:len(), ('xy'):len()");

			Assert.AreEqual(2, r.Tuple[0].Number);
			Assert.AreEqual(42, r.Tuple[1].Number);
			Assert.AreEqual(2, r.Tuple[2].Number, "a local alias still holds the library function");
			Assert.AreEqual(3, r.Tuple[3].Number);
			Assert.AreEqual(-1, r.Tuple[4].Number);
			Assert.AreEqual(-1, r.Tuple[5].Number);
		}

		[Test]
		public void Builtin_ResultsForEveryArgumentShape()
		{
			DynValue r = Run(@"
				local function two() return 4, 9 end
				local function none() end
				local out = {
					math.floor(-2.5), math.ceil(-2.5), math.abs(-3), math.sqrt(16), math.max(3, 7, 5), math.min(3, 7, 5),
					math.max(two()), math.floor('2.5'), math.max(1, '9'), string.len(123),
					string.sub('hello', 2), string.sub('hello', -3, -2), string.sub('hello', 0), string.sub('hello', 4, 2),
					string.byte('A'), string.byte('ABC', -1), select('#', string.byte('ABC', 1, 3)), select('#', string.byte('', 1)),
					type(nil), type(print), type({}), type(true),
					select('#'), select('#', 1, nil, nil), select('#', two()), select('#', 1, none()), select('#', none(), 1),
					string.sub('hello', '2', 3.9), string.byte('ABC', 2.7)
				}
				return table.concat((function() local s = {} for i = 1, 29 do s[i] = tostring(out[i]) end return s end)(), ',')");

			// The output of lua5.2 for the same table. select('#', string.byte('', 1)) is 0: a C# function's
			// empty result list is no value (upstream #287), on the fast path too.
			Assert.AreEqual("-3,-2,3,4,7,3,9,2,9,3,ello,ll,hello,,65,67,3,0,nil,function,table,boolean,0,3,2,1,2,el,66", r.String);
		}

		[Test]
		public void Builtin_ErrorsAreTheLibrarys()
		{
			string[] calls = { "math.floor('x')", "math.floor()", "math.max()", "string.sub()", "string.len({})", "type()",
				"select('x')", "table.insert(1, 2)", "table.insert({}, 1, 2, 3)" };
			string[] expected = { "bad argument #1 to 'floor' (number expected, got string)", "bad argument #1 to 'floor' (number expected, got no value)",
				"bad argument #1 to 'max' (number expected, got no value)", "bad argument #1 to 'sub' (string expected, got no value)",
				"bad argument #1 to 'len' (string expected, got table)", "bad argument #1 to 'type' (value expected)",
				"bad argument #1 to 'select' (number expected, got string)", "bad argument #1 to 'table.insert' (table expected, got number)",
				"wrong number of arguments to 'insert'" };

			for (int k = 0; k < calls.Length; k++)
			{
				DynValue r = Run("local ok, e = pcall(function() return " + calls[k] + " end) return e");
				StringAssert.Contains(expected[k], r.String, calls[k]);
			}
		}

		[Test]
		public void Builtin_TableInsertHonoursLen()
		{
			DynValue r = Run(@"
				local t = setmetatable({}, { __len = function() return 4 end })
				local u = {}
				local same = table.insert(u, 'a') == u
				table.insert(u, 'b')
				table.insert(t, 'x')
				table.insert(u, 1, 'z')
				return rawget(t, 5), table.concat(u, ','), same");

			Assert.AreEqual("x", r.Tuple[0].String);
			Assert.AreEqual("z,a,b", r.Tuple[1].String);
			Assert.AreEqual(true, r.Tuple[2].Boolean);
		}
	}
}
