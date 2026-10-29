using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Fixes from the Lua 5.2 conformance survey (docs/research/lua52-conformance.md, entry numbers there). Each
	/// expected value is what lua5.2 (5.2.4) prints for the same script, unless a comment says otherwise.
	/// </summary>
	[TestFixture]
	public class Lua52ConformanceTests
	{
		static DynValue Run(string code)
		{
			return new Script(CoreModules.Preset_Complete).DoString(code);
		}

		static string RunString(string code)
		{
			return Run(code).String;
		}

		// Entry 8: pcall() with no arguments ended the host with an OverflowException.
		[Test]
		public void Pcall_WithoutArgumentsIsAnError()
		{
			Assert.AreEqual("false bad argument #1 to 'pcall' (value expected)",
				RunString("local ok, e = pcall(pcall) return tostring(ok) .. ' ' .. e"));
		}

		[Test]
		public void Pcall_OfANonFunctionNamesItsType()
		{
			Assert.AreEqual("false attempt to call a nil value|false attempt to call a number value",
				RunString("local a, b = pcall(nil) local c, d = pcall(5) return tostring(a) .. ' ' .. b .. '|' .. tostring(c) .. ' ' .. d"));
		}

		// Entry 9: pcall called the function coroutine.wrap returns with pcall's own context, whose
		// AdditionalData is not the coroutine: a NullReferenceException ended the host.
		[Test]
		public void Pcall_OfAWrappedCoroutine()
		{
			Assert.AreEqual("false true|true 5|true 9|false cannot resume dead coroutine",
				RunString(@"
					local fails = coroutine.wrap(function() error('boom') end)
					local ok1, e1 = pcall(fails)
					local w = coroutine.wrap(function(a) coroutine.yield(a) return 9 end)
					local ok2, v2 = pcall(w, 5)
					local ok3, v3 = pcall(w)
					local ok4, e4 = pcall(w)
					return tostring(ok1) .. ' ' .. tostring(e1:find('boom') ~= nil) .. '|' .. tostring(ok2) .. ' ' .. v2 .. '|' ..
						tostring(ok3) .. ' ' .. v3 .. '|' .. tostring(ok4) .. ' ' .. e4"));
		}

		// Entry 31: dofile and loadfile of a missing file let the loader's FileNotFoundException end the host.
		[Test]
		public void DofileAndLoadfile_OfAMissingFile()
		{
			DynValue r = Run(@"
				local ok, e = pcall(dofile, 'no-such-file.lua')
				local f, e2 = loadfile('no-such-file.lua')
				return ok, e, f, e2");

			Assert.IsFalse(r.Tuple[0].Boolean);
			// The reason after the colon is the loader's: FileSystemScriptLoader's is "No such file or directory", as in Lua.
			StringAssert.StartsWith("cannot open no-such-file.lua", r.Tuple[1].String);
			Assert.IsTrue(r.Tuple[2].IsNil());
			StringAssert.StartsWith("cannot open no-such-file.lua", r.Tuple[3].String);
		}

		// Entry 27: string.rep built a StringBuilder of a negative capacity (2^31 copies) or more than a CLR string
		// holds, and ended the host; it also left out the separators of an empty string.
		[Test]
		public void StringRep_TooLargeIsAnErrorAndSeparatorsStay()
		{
			// Lua 5.2 tries to allocate and raises "not enough memory"; MoonSharp checks the size first.
			Assert.AreEqual("false resulting string too large|false resulting string too large",
				RunString(@"
					local a, e1 = pcall(string.rep, 'x', 2^31)
					local b, e2 = pcall(string.rep, 'ab', 1e10, ',')
					return tostring(a) .. ' ' .. e1 .. '|' .. tostring(b) .. ' ' .. e2"));
			Assert.AreEqual("..|ab,ab|", RunString("return string.rep('', 3, '.') .. '|' .. string.rep('ab', 2.9, ',') .. '|' .. string.rep('x', 1/0)"));
		}

		// os.time with a field out of its usual range (month 13, day 0) threw ArgumentOutOfRangeException from
		// the DateTime constructor; Lua normalises it as C's mktime does.
		[Test]
		public void OsTime_NormalisesFields()
		{
			Assert.AreEqual("true true",
				RunString(@"
					local a = os.time({year = 2020, month = 13, day = 1, hour = 0}) == os.time({year = 2021, month = 1, day = 1, hour = 0})
					local b = os.time({year = 2020, month = 3, day = 0, hour = 0}) == os.time({year = 2020, month = 2, day = 29, hour = 0})
					return tostring(a) .. ' ' .. tostring(b)"));
		}

		// A time DateTime cannot hold ended the host with an OverflowException (TimeSpan) or ArgumentException
		// (NaN). Not as in Lua 5.2 on Linux, which has dates up to year 2^31: nil, as Lua gives for a time the C
		// library cannot convert.
		[Test]
		public void OsDateAndTime_OutOfRangeAreNil()
		{
			DynValue r = Run("return os.date('!%Y', 2^53), os.date('!%Y', 0/0), os.time({year = 1e10, month = 1, day = 1}), os.date('!%Y-%m-%d', 86400)");

			Assert.IsTrue(r.Tuple[0].IsNil());
			Assert.IsTrue(r.Tuple[1].IsNil());
			Assert.IsTrue(r.Tuple[2].IsNil());
			Assert.AreEqual("1970-01-02", r.Tuple[3].String);
		}

		// Entry 1: the right operand of and/or kept every result of a call or '...' (the left one too when it was
		// returned): x or f() passed all of f's results on, and nil or f() with no results gave no value.
		[Test]
		public void AndOr_GiveOneValue()
		{
			Assert.AreEqual("1|1|1|1|1|1|7|1|0",
				RunString(@"
					local function f() return 1, 2, 3 end
					local function none() end
					local function count(...) return select('#', ...) end
					local function va(...) return false or ... end
					local t = {nil or f()}
					return table.concat({ count(nil or f()), count(true and f()), count(f() or 1), count(f() and f()),
						count(nil or none()), count(va(7, 8)), va(7, 8), #t, count() }, '|')"));
		}


		// Entry 2: a unary operator after a binary one closed before '^' joined: 1 + -x^2 was 1 + (-x)^2.
		[Test]
		public void UnaryMinus_BindsLooserThanPowerEverywhere()
		{
			Assert.AreEqual("-8|-18|-4|6|true|false|-255|-0.25|-4",
				RunString(@"
					local x = 3
					return table.concat({ 1 + -x^2, 2 * -x^2, tostring(true and -2^2), 2 - -2^2, tostring(2^-3^2 == 2^-9),
						tostring(1 == not 2^2), 1 + -2^2^3, -2^-2, - -2^2 * -1 }, '|')"));
		}


		// Entry 3: a multiple assignment evaluated each target's table and key right before storing into it, after
		// the earlier targets were stored. Lua 5.2, 3.3.3: "The assignment statement first evaluates all its
		// expressions and only then are the assignments performed"; reference Lua then stores from the last target.
		[Test]
		public void MultipleAssignment_EvaluatesTargetsFirst()
		{
			Assert.AreEqual("20,nil,4|10,nil,2|nil,1|1,2,3,4|y,x|1|v,nil",
				RunString(@"
					local i, a = 3, {}
					i, a[i] = i + 1, 20
					local r1 = tostring(a[3]) .. ',' .. tostring(a[4]) .. ',' .. i
					local c, j = {}, 1
					c[j], j = 10, 2
					local r2 = c[1] .. ',' .. tostring(c[2]) .. ',' .. j
					T = {} local old = T
					T, T.x = {}, 1
					local r3 = tostring(T.x) .. ',' .. old.x
					local calls, d = {}, {}
					local function f(n) calls[#calls + 1] = n return n end
					d[f(1)], d[f(2)] = f(3), f(4)
					local log = {}
					local m = setmetatable({}, { __newindex = function(t, k, v) log[#log + 1] = k rawset(t, k, v) end })
					m.x, m.y = 1, 2
					local z z, z = 1, 2
					local w, k = {}, 'key'
					w[k], k = 'v', 'other'
					return table.concat({ r1, r2, r3, table.concat(calls, ','), table.concat(log, ','), z, w.key .. ',' .. tostring(w.other) }, '|')"));
		}


		// Entry 4: goto to a label at the end of a block was rejected after a local ("jumps into the scope of
		// local"); Lua 5.2 treats such a label as outside the block's locals, except before 'until'.
		[Test]
		public void Goto_LabelAtTheEndOfABlock()
		{
			Assert.AreEqual("10,30,50",
				RunString(@"
					local out = {}
					for i = 1, 5 do
						if i % 2 == 0 then goto continue end
						local x = i * 10
						out[#out + 1] = x
						::continue::
					end
					return table.concat(out, ',')"));

			StringAssert.Contains("jumps into the scope of local 'x'",
				RunString("local f, e = load('repeat goto e; local x = 1; ::e:: until x') return e"));
			StringAssert.Contains("jumps into the scope of local 'x'",
				RunString("local f, e = load('do goto e; local x = 1; ::e:: print(x) end') return e"));
		}


		// Entry 35: __eq ran only when both tables had the same metatable; Lua 5.2 runs it when the two metatables'
		// __eq fields are the same value, as in class hierarchies that copy it into each class.
		[Test]
		public void Eq_SharedHandlerAcrossMetatables()
		{
			Assert.AreEqual("true|false|false|false",
				RunString(@"
					local eq = function(a, b) return a.v == b.v end
					local A = setmetatable({ v = 1 }, { __eq = eq })
					local B = setmetatable({ v = 1 }, { __eq = eq })
					local C = setmetatable({ v = 1 }, { __eq = function() return true end })
					local D = setmetatable({ v = 1 }, {})
					return table.concat({ tostring(A == B), tostring(A == C), tostring(A == D), tostring(D == A) }, '|')"));
		}
	}
}
