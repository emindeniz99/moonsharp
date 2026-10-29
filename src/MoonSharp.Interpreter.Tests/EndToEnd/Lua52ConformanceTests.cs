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
	}
}
