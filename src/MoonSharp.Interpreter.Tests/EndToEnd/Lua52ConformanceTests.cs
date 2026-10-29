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

		// Entries 21 and 22: math.fmod used Math.IEEERemainder (fmod(7, 4) was -1) and math.modf floored.
		[Test]
		public void MathFmodAndModf_FollowC()
		{
			Assert.AreEqual("3|-3|3|1.5|2|-0|-3|-0.7|3|0.7|-inf|-0",
				RunString(@"
					local function s(x) if x == 0 then return 1 / x < 0 and '-0' or '0' end return string.format('%.14g', x) end
					local a, b = math.modf(-3.7)
					local c, d = math.modf(3.7)
					local e, f = math.modf(-1/0)
					return table.concat({ s(math.fmod(7, 4)), s(math.fmod(-7, 4)), s(math.fmod(7, -4)), s(math.fmod(5.5, 2)),
						s(math.fmod(2^53, 3)), s(math.fmod(-0.0, 1)), s(a), s(b), s(c), s(d), s(e), s(f) }, '|')"));
		}

		// Entry 24: bit32 as Lua 5.2's lbitlib. Negative numbers went through a (uint) cast of a double, which
		// .NET 9 and ARM64 saturate to 0 (bit32.band(-1) was 0 there); fractions truncated instead of rounding;
		// shifts wrapped modulo 32; arshift returned negative numbers; extract and replace refused the top bit
		// and replace did not shift; band() and friends needed an argument.
		[Test]
		public void Bit32_FollowsLua52()
		{
			Assert.AreEqual("4294967295|4294967294|4294967292|2|2|4294967294|0|0|2|0|4294967295|4160749568|10|1|4|16|4294967295|0|true",
				RunString(@"
					local b = bit32
					return table.concat({ b.band(-1), b.bor(-2), b.band(2^40 - 4), b.band(1.5), b.band(2.5), b.band(-1.5),
						b.lshift(1, 32), b.rshift(1, -32), b.lshift(1, 1.9), b.lshift(-1, 64), b.arshift(-1, 1),
						b.arshift(0x80000000, 4), b.extract(0xa0001111, 28, 4), b.extract(0xa0001111, 31),
						b.replace(0, 1, 2), b.replace(0, 3, 4), b.band(), b.bor(), tostring(b.btest()) }, '|')"));
		}

		// Entry 30: load treated "" from a reader as an error, let a reader's error through, ignored mode and
		// took only a table as env.
		[Test]
		public void Load_ReaderModeAndEnv()
		{
			Assert.AreEqual("5|nil rd|nil attempt to load a text chunk (mode is 'b')|nil attempt to load a binary chunk (mode is 't')|5|false|42",
				RunString(@"
					local parts, i = { 'return 5', '' }, 0
					local r1 = load(function() i = i + 1 return parts[i] end)()
					local f2, e2 = load(function() error('rd', 0) end)
					local f3, e3 = load('return 1', '=e', 'b')
					local f4, e4 = load(string.dump(function() return 1 end), '=e', 't')
					local r5 = load('return _ENV', '=e', 't', 5)()
					local ok6 = pcall(load('return x', '=e', 't', nil))
					local r7 = load(function() local n = 0 return function() n = n + 1 if n == 1 then return 42 end end end)
					return table.concat({ r1, tostring(f2) .. ' ' .. e2, tostring(f3) .. ' ' .. e3, tostring(f4) .. ' ' .. e4,
						r5, tostring(ok6), tostring(load('return ' .. 42)()) }, '|')"));
		}

		// Entry 20: package.loaded had no _G and no package entry; cpath, loadlib and searchpath were missing.
		[Test]
		public void Package_LoadedAndCLibraryFields()
		{
			DynValue r = Run(@"
				local f, e, w = package.loadlib('x', 'y')
				local p, tried = package.searchpath('no.such.mod', './?.lua;./?/init.lua')
				return package.loaded._G == _G, package.loaded.package == package, package.cpath, f, e, w, p, tried");

			Assert.IsTrue(r.Tuple[0].Boolean);
			Assert.IsTrue(r.Tuple[1].Boolean);
			Assert.AreEqual("", r.Tuple[2].String);
			Assert.IsTrue(r.Tuple[3].IsNil());
			Assert.AreEqual("dynamic libraries not enabled; check your Lua installation", r.Tuple[4].String);
			Assert.AreEqual("absent", r.Tuple[5].String);
			Assert.IsTrue(r.Tuple[6].IsNil());
			StringAssert.Contains("no file './no" + System.IO.Path.DirectorySeparatorChar + "such" + System.IO.Path.DirectorySeparatorChar + "mod.lua'", r.Tuple[7].String);
		}

		// Entry 10: collectgarbage("count") returned nothing, so scripts that log memory failed with "attempt to
		// perform arithmetic on a nil value". It is now the managed heap of the process, in KB, plus the
		// remainder in bytes as Lua 5.2's second result.
		[Test]
		public void CollectGarbage_CountIsTheManagedHeapInKilobytes()
		{
			// Settle the heap, so that collecting earlier tests' garbage cannot hide the growth measured below.
			System.GC.Collect();
			System.GC.WaitForPendingFinalizers();
			System.GC.Collect();

			DynValue r = Run(@"
				local before = collectgarbage('count')
				local t = {}
				for i = 1, 1000000 do t[i] = i end
				local after, bytes = collectgarbage('count')
				return before, after, bytes, select('#', collectgarbage('count')), #t");

			double before = r.Tuple[0].Number, after = r.Tuple[1].Number, bytes = r.Tuple[2].Number;

			Assert.Greater(before, 0);
			// A million live array slots take at least 8 bytes each on every runtime.
			Assert.Greater(after - before, 1000000 * 8 / 1024.0, "the count must grow with what the script allocates");
			Assert.AreEqual(2, r.Tuple[3].Number);
			// As in Lua 5.2: the KB as a float, and the bytes past the last whole KB as an integer.
			Assert.AreEqual(System.Math.Floor(bytes), bytes);
			Assert.IsTrue(bytes >= 0 && bytes < 1024);
			Assert.AreEqual(after * 1024, System.Math.Floor(after) * 1024 + bytes);
			Assert.AreEqual(1000000, r.Tuple[4].Number);
		}

		// Entry 10: collectgarbage() and "collect" returned nothing and forced a blocking full collection, a
		// stall for a script that calls it every frame. They ask for an optimized collection and return 0.
		[Test]
		public void CollectGarbage_CollectReturnsZero()
		{
			Assert.AreEqual("0 0 1 0",
				RunString("return table.concat({ collectgarbage(), collectgarbage('collect'), select('#', collectgarbage()), collectgarbage(nil) }, ' ')"));
		}

		// Entry 10: the options that tune or stop Lua's collector have nothing to act on in the CLR. They return
		// what Lua 5.2 returns for them, so code written for it runs; the differences are commented.
		[Test]
		public void CollectGarbage_TuningOptionsAreNoOpsWithLua52Results()
		{
			Assert.AreEqual("true true 0 true 0 200 200 200 200 0 0 1",
				RunString(@"
					local r = {
						-- Lua 5.2: false until a step ends a cycle. True here, so loops that wait for one end.
						tostring(collectgarbage('step')),
						tostring(collectgarbage('isrunning')),
						collectgarbage('stop'),
						-- Lua 5.2: false after 'stop'. The CLR collector cannot be stopped.
						tostring(collectgarbage('isrunning')),
						collectgarbage('restart'),
						-- Lua 5.2 returns the previous setting (200 at first); nothing is stored here.
						collectgarbage('setpause', 100),
						collectgarbage('setpause'),
						collectgarbage('setstepmul', 400),
						collectgarbage('setmajorinc'),
						collectgarbage('generational'),
						collectgarbage('incremental'),
					}
					local n = 0
					repeat n = n + 1 until collectgarbage('step') or n > 10
					r[#r + 1] = n
					return table.concat(r, ' ')"));
		}

		// Entry 10: any option was accepted, so a typo went unnoticed. Lua 5.2 checks the option before the
		// second argument, and the second argument for every option.
		[Test]
		public void CollectGarbage_InvalidArgumentsAreErrors()
		{
			Assert.AreEqual(
				"bad argument #1 to 'collectgarbage' (invalid option 'x')|" +
				"bad argument #1 to 'collectgarbage' (invalid option 'COUNT')|" +
				"bad argument #1 to 'collectgarbage' (invalid option '1')|" +
				"bad argument #1 to 'collectgarbage' (string expected, got table)|" +
				"bad argument #1 to 'collectgarbage' (string expected, got boolean)|" +
				"bad argument #2 to 'collectgarbage' (number expected, got string)|" +
				"bad argument #2 to 'collectgarbage' (number expected, got table)|" +
				"bad argument #1 to 'collectgarbage' (invalid option 'x')|" +
				"true true",
				RunString(@"
					local function e(...) local ok, m = pcall(collectgarbage, ...) return ok and 'no error' or m end
					return table.concat({ e('x'), e('COUNT'), e(1), e({}), e(true), e('count', 'x'), e(nil, {}), e('x', {}),
						tostring(pcall(collectgarbage, 'count', '10')) .. ' ' .. tostring(pcall(collectgarbage, 'collect', 2.7)) }, '|')"));
		}

		// Entry 29: Lua 5.2's default build (LUA_COMPAT_ALL) keeps loadstring as another name of load, and
		// 5.1-era scripts call it everywhere; here it was nil.
		[Test]
		public void Loadstring_IsLoadUnderItsLua51Name()
		{
			Assert.AreEqual("2|4 5|nil string|7|nil attempt to load a text chunk (mode is 'b')|" +
				"bad argument #1 to 'loadstring' (function expected, got no value)|" +
				// Lua 5.2: "(string expected, got table)"; MoonSharp names nil for every optional argument (entry 13).
				"bad argument #2 to 'loadstring' (nil or string expected, got table)",
				RunString(@"
					local f, e = loadstring('x x')
					local i = 0
					local g = loadstring(function() i = i + 1 if i == 1 then return 'return 7' end end)
					local h, he = loadstring('return 1', '=n', 'b')
					return table.concat({ loadstring('return 1 + 1')(), table.concat({ loadstring('return ...', '=x')(4, 5) }, ' '),
						tostring(f) .. ' ' .. type(e), g(), tostring(h) .. ' ' .. he,
						select(2, pcall(loadstring)), select(2, pcall(loadstring, 'return 1', {})) }, '|')"));
		}

		// Entry 29: loadstring compiles code, so it comes with load (CoreModules.LoadMethods): a sandbox without
		// load must not gain it. table.maxn and math.log10 come with their libraries.
		[Test]
		public void Lua51Names_FollowTheirLibraries()
		{
			Script soft = new Script(CoreModules.Preset_SoftSandbox);
			Assert.IsTrue(soft.Globals.Get("load").IsNil());
			Assert.IsTrue(soft.Globals.Get("loadstring").IsNil());

			Script full = new Script(CoreModules.Preset_Default);
			Assert.AreEqual(DataType.ClrFunction, full.Globals.Get("loadstring").Type);

			Script hard = new Script(CoreModules.Preset_HardSandbox);
			Assert.AreEqual(DataType.ClrFunction, hard.Globals.Get("table").Table.Get("maxn").Type);
			Assert.AreEqual(DataType.ClrFunction, hard.Globals.Get("math").Table.Get("log10").Type);

			Assert.IsTrue(new Script(CoreModules.Preset_HardSandbox & ~CoreModules.Table).Globals.Get("table").IsNil());
		}

		// Entry 29: table.maxn, from Lua 5.1 and kept by Lua 5.2's default build: the largest positive numeric key,
		// fractional keys included, read raw.
		[Test]
		public void TableMaxn_FollowsLua52()
		{
			Assert.AreEqual("0 3 1.5 0 100 0 0 true 2 9007199254740992|" +
				"bad argument #1 to 'maxn' (table expected, got no value)|" +
				"bad argument #1 to 'maxn' (table expected, got number)",
				RunString(@"
					local m = table.maxn
					local t = { 1, 2, 3 } t[3] = nil
					local r = { m({}), m({ 1, 2, 3 }), m({ [1.5] = 1 }), m({ [-3] = 1 }), m({ [100] = 1, 2 }), m({ a = 1, ['5'] = 2 }),
						m(setmetatable({}, { __index = { [5] = 1 }, __len = function() return 9 end })), m({ [1/0] = 1 }) == 1/0, m(t) }
					for i = 1, #r do r[i] = tostring(r[i]) end
					r[#r + 1] = string.format('%.0f', m({ [2^53] = 1, [-1] = 2, [0.5] = 3 }))
					return table.concat(r, ' ') .. '|' .. select(2, pcall(m)) .. '|' .. select(2, pcall(m, 5))"));
		}

		// Entry 29: math.log10, from Lua 5.1 and kept by Lua 5.2's default build.
		[Test]
		public void MathLog10_FollowsLua52()
		{
			DynValue r = Run(@"
				return math.log10(1000), math.log10(1e-3), math.log10('100'), math.log10(0), math.log10(-1),
					select(2, pcall(math.log10)), select(2, pcall(math.log10, 'a'))");

			Assert.AreEqual(3, r.Tuple[0].Number);
			Assert.AreEqual(-3, r.Tuple[1].Number);
			Assert.AreEqual(2, r.Tuple[2].Number);
			Assert.AreEqual(double.NegativeInfinity, r.Tuple[3].Number);
			Assert.IsTrue(double.IsNaN(r.Tuple[4].Number));
			Assert.AreEqual("bad argument #1 to 'log10' (number expected, got no value)", r.Tuple[5].String);
			Assert.AreEqual("bad argument #1 to 'log10' (number expected, got string)", r.Tuple[6].String);
		}
	}
}
