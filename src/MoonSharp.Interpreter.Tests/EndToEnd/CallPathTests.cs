using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// How arguments reach a Lua function's parameters, and how a call's frame leaves the value stack, on
	/// every way into and out of a call: plain calls, host calls, tail calls, errors, coroutines.
	/// </summary>
	[TestFixture]
	public class CallPathTests
	{
		static DynValue Run(string code)
		{
			return new Script().DoString(code);
		}

		[Test]
		public void CallPath_FewerAndMoreArgumentsThanParameters()
		{
			DynValue r = Run(@"
				local function f(a, b, c) return tostring(a) .. ',' .. tostring(b) .. ',' .. tostring(c) end
				return f(1), f(1, 2, 3, 4, 5), f()");

			Assert.AreEqual("1,nil,nil", r.Tuple[0].String);
			Assert.AreEqual("1,2,3", r.Tuple[1].String);
			Assert.AreEqual("nil,nil,nil", r.Tuple[2].String);
		}

		[Test]
		public void CallPath_ParameterFromACallWithNoResultsIsNil()
		{
			// f() passes no value, but the parameter that receives it is a variable, so it holds nil (Lua
			// 5.2). MoonSharp stored "no value" in it, and type(a) failed with "value expected".
			DynValue r = Run(@"
				local function none() end
				local function g(a) return type(a) end
				local function h(a, b) return type(a) .. ',' .. type(b) end
				local function v(a, ...) return type(a) .. ',' .. select('#', ...) end
				return g(none()), h(none()), h(1, none()), v(none())");

			Assert.AreEqual("nil", r.Tuple[0].String);
			Assert.AreEqual("nil,nil", r.Tuple[1].String);
			Assert.AreEqual("number,nil", r.Tuple[2].String);
			Assert.AreEqual("nil,0", r.Tuple[3].String);
		}

		[Test]
		public void CallPath_NoVarArgsAreAnEmptyList()
		{
			// '...' that receives no values holds none (Lua 5.2). MoonSharp stored one nil, so
			// select('#', ...) was 1 and f(...) passed f one nil argument.
			DynValue r = Run(@"
				local function none() end
				local function count(...) return select('#', ...) end
				local function v(a, ...) return select('#', ...), count(...) end
				local function w(...) return select('#', ...), count(...) end
				local a1, a2 = v(1)
				local b1, b2 = w()
				local c1, c2 = w(none())
				local d1, d2 = v(1, 2, 3)
				return a1 .. a2 .. ',' .. b1 .. b2 .. ',' .. c1 .. c2 .. ',' .. d1 .. d2");

			Assert.AreEqual("00,00,00,22", r.String);
		}

		[Test]
		public void CallPath_VarArgs()
		{
			DynValue r = Run(@"
				local function count(...) return select('#', ...) end
				local function second(a, ...) local t = { ... } return a, select('#', ...), t[2] end
				local x, n, y = second(1, 2, 3, nil)
				return count(1, 2), count(nil), count(1, nil, nil), x, n, y");

			// count() returns 1 instead of 0 (an empty ... is stored as nil); that predates this test.
			Assert.AreEqual(2, r.Tuple[0].Number);
			Assert.AreEqual(1, r.Tuple[1].Number);
			Assert.AreEqual(3, r.Tuple[2].Number);
			Assert.AreEqual(1, r.Tuple[3].Number);
			Assert.AreEqual(3, r.Tuple[4].Number);
			Assert.AreEqual(3, r.Tuple[5].Number);
		}

		[Test]
		public void CallPath_LastArgumentIsAMultipleReturnCall()
		{
			DynValue r = Run(@"
				local function three() return 1, 2, 3 end
				local function none() end
				local function f(a, b, c, d) return tostring(a) .. tostring(b) .. tostring(c) .. tostring(d) end
				local function nils(a, b) return a == nil and b == nil end
				return f(three()), f(0, three()), f(three(), 9), nils(none()), f(0, three(), three())");

			// f(none()) passes a void value rather than nil (tostring(a) fails on it); that predates
			// this test, so it is checked through == nil only.
			Assert.AreEqual("123nil", r.Tuple[0].String);
			Assert.AreEqual("0123", r.Tuple[1].String);
			Assert.AreEqual("19nilnil", r.Tuple[2].String);
			Assert.AreEqual(true, r.Tuple[3].Boolean);
			Assert.AreEqual("0112", r.Tuple[4].String);
		}

		[Test]
		public void CallPath_ParameterIsACopy()
		{
			// Assigning to a parameter changes neither the caller's local nor a table field passed in.
			DynValue r = Run(@"
				local function bump(a) a = a + 1 return a end
				local x = 1
				local t = { v = 10 }
				local y = bump(x)
				local z = bump(t.v)
				return x, y, t.v, z");

			Assert.AreEqual(1, r.Tuple[0].Number);
			Assert.AreEqual(2, r.Tuple[1].Number);
			Assert.AreEqual(10, r.Tuple[2].Number);
			Assert.AreEqual(11, r.Tuple[3].Number);
		}

		[Test]
		public void CallPath_ParametersAreVisibleByName()
		{
			// A CLR callback reads the caller's parameters by name, as the debugger does.
			Script s = new Script();
			s.Globals["peek"] = DynValue.NewCallback((ctx, args) =>
				DynValue.NewString(ctx.EvaluateSymbolByName("a").ToPrintString() + ctx.EvaluateSymbolByName("b").ToPrintString()));

			DynValue r = s.DoString(@"
				local function f(a, b) return peek() end
				return f('x', 'y'), f('z')");

			Assert.AreEqual("xy", r.Tuple[0].String);
			Assert.AreEqual("znil", r.Tuple[1].String);
		}

		[Test]
		public void CallPath_HostCallsWithTupleArguments()
		{
			Script s = new Script();
			DynValue f = s.DoString("return function(a, b, ...) return a, b, select('#', ...) end");

			DynValue r = s.Call(f, DynValue.NewNumber(1), DynValue.NewTuple(DynValue.NewNumber(2), DynValue.NewNumber(3), DynValue.NewNumber(4)));

			Assert.AreEqual(1, r.Tuple[0].Number);
			Assert.AreEqual(2, r.Tuple[1].Number);
			Assert.AreEqual(2, r.Tuple[2].Number);

			for (int i = 0; i < 1000; i++)
				Assert.AreEqual(i, s.Call(f, DynValue.NewNumber(i)).Tuple[0].Number);
		}

		[Test]
		public void CallPath_TailCallsUnderTheThreshold()
		{
			// With a low threshold every tail call reuses the frame (PerformTCO), which pops the caller's
			// arguments by the count stored in its frame.
			Script s = new Script();
			s.Options.TailCallOptimizationThreshold = 4;

			DynValue r = s.DoString(@"
				local function sum(n, acc, unused1, unused2)
					if n == 0 then return acc end
					return sum(n - 1, acc + n, 'x', 'y')
				end
				local function viaVarargs(n, ...)
					if n == 0 then return select('#', ...) end
					return viaVarargs(n - 1, ...)
				end
				return sum(20000, 0), viaVarargs(5000, 1, 2, 3)");

			Assert.AreEqual(200010000, r.Tuple[0].Number);
			Assert.AreEqual(3, r.Tuple[1].Number);
		}

		[Test]
		public void CallPath_TailCallsKeepTheEntryFrame()
		{
			// The threshold also counts value stack slots, so a tail call in the function C# called could
			// remove that entry frame: a tail-called C# function then returned to address -1 and the loop
			// failed with IndexOutOfRangeException, and an error in a tail-called Lua function unwound past
			// the entry frame into the frames of the C# caller's own loop.
			Script s = new Script();
			s.Options.TailCallOptimizationThreshold = 1;
			s.Globals["callLua"] = DynValue.NewCallback((ctx, args) => ctx.GetScript().Call(args[0]));

			DynValue r = s.DoString(@"
				local function boom() error('boom') end
				local function viaTail() return boom() end
				local ok, err = pcall(callLua, viaTail)
				local t = { 'a', 'b' }
				return ok, err, table.concat(t, ',')");

			Assert.AreEqual(false, r.Tuple[0].Boolean);
			StringAssert.EndsWith("boom", r.Tuple[1].String);
			Assert.AreEqual("a,b", r.Tuple[2].String);
			Assert.AreEqual("1", s.DoString("return tostring(1)").String);
		}

		[Test]
		public void CallPath_ErrorsInsideCallsUnwindCleanly()
		{
			// pcall and xpcall around failing Lua functions with arguments, many times over: the stack must
			// stay balanced, so later calls still see the right values.
			DynValue r = Run(@"
				local function fail(a, b, c) error('boom ' .. a) end
				local function nested(a, b) local x = a * 2 return fail(x, b, 'c') end
				local ok, msg, total = nil, nil, 0
				for i = 1, 2000 do
					ok, msg = pcall(nested, i, 'b')
					total = total + (ok and 0 or 1)
					local ok2, msg2 = xpcall(function(p, q) return fail(p, q) end, function(m) return 'handled' end, i, 2)
					if msg2 ~= 'handled' then error('xpcall') end
				end
				local function add(a, b) return a + b end
				return total, add(40, 2), msg");

			Assert.AreEqual(2000, r.Tuple[0].Number);
			Assert.AreEqual(42, r.Tuple[1].Number);
			StringAssert.Contains("boom 4000", r.Tuple[2].String);
		}

		[Test]
		public void CallPath_CoroutineYieldsInsideACall()
		{
			DynValue r = Run(@"
				local function inner(a, b)
					local got = coroutine.yield(a + b)
					return got * a
				end
				local function outer(x, ...)
					local n = select('#', ...)
					return inner(x, n) + 1
				end
				local co = coroutine.create(outer)
				local ok1, v1 = coroutine.resume(co, 10, 'p', 'q')
				local ok2, v2 = coroutine.resume(co, 3)
				return v1, v2, coroutine.status(co)");

			Assert.AreEqual(12, r.Tuple[0].Number);
			Assert.AreEqual(31, r.Tuple[1].Number);
			Assert.AreEqual("dead", r.Tuple[2].String);
		}

		[Test]
		public void CallPath_CallMetamethodGetsTheObjectFirst()
		{
			DynValue r = Run(@"
				local callable = setmetatable({ k = 5 }, { __call = function(self, a, b) return self.k + a + b end })
				return callable(1, 2), callable(10, 20)");

			Assert.AreEqual(8, r.Tuple[0].Number);
			Assert.AreEqual(35, r.Tuple[1].Number);
		}
	

		[Test]
		public void CallPath_LibraryFunctionsReadEveryArgumentShape()
		{
			// Library functions read their arguments in place on the value stack: a multiple-return call
			// as the last argument expands, one in the middle gives its first value, a call with no
			// results gives no value at the end and nil in the middle, and ':' passes the object first.
			DynValue r = Run(@"
				local function two() return 'b', 'c' end
				local function none() end
				return string.format('%s%s%s', 'a', two()),
					string.format('%s%s', two(), 'z'),
					select('#', none()), select('#', none(), 1),
					('%d-%d'):format(3, 4),
					string.format('%s', (two()))");

			Assert.AreEqual("abc", r.Tuple[0].String);
			Assert.AreEqual("bz", r.Tuple[1].String);
			Assert.AreEqual(0, r.Tuple[2].Number);
			Assert.AreEqual(2, r.Tuple[3].Number);
			Assert.AreEqual("3-4", r.Tuple[4].String);
			Assert.AreEqual("b", r.Tuple[5].String);
		}

		[Test]
		public void CallPath_LibraryArgumentsSurviveStackGrowthDuringTheCall()
		{
			// load reads its chunk name and environment after calling the reader. The reader recurses
			// deep enough to grow the value stack to a new array, so the arguments must be read from
			// the stack as it is then, not from the array the call started with.
			DynValue r = Run(@"
				local function deep(n) if n == 0 then return 0 end return 1 + deep(n - 1) end
				local parts = { 'return x' }
				local i = 0
				local function reader() i = i + 1 deep(3000) return parts[i] end
				local f = load(reader, '=chunkname', 't', { x = 42 })
				local j = 0
				local function reader2() j = j + 1 deep(3000) if j == 1 then return 'error(""boom"")' end end
				local ok, err = pcall(load(reader2, '=other'))
				return f(), err");

			Assert.AreEqual(42, r.Tuple[0].Number);
			StringAssert.StartsWith("=other:", r.Tuple[1].String);
		}

		[Test]
		public void CallPath_ReentrantLibraryCallsKeepTheirOwnArguments()
		{
			// A library function calling Lua that calls the same library function: each call reads its
			// own arguments.
			DynValue r = Run(@"
				local t = { 3, 1, 2 }
				local seen = {}
				table.sort(t, function(a, b) seen[#seen + 1] = string.format('%d<%d', a, b) return a < b end)
				return table.concat(t, ','), #seen > 0, string.gsub('abc', '%w', function(c) return string.format('[%s]', c) end)");

			Assert.AreEqual("1,2,3", r.Tuple[0].String);
			Assert.AreEqual(true, r.Tuple[1].Boolean);
			Assert.AreEqual("[a][b][c]", r.Tuple[2].String);
		}
			[Test]
		public void CallPath_NestedCSharpCallsKeepTheirOwnContext()
		{
			// The processor reuses one execution context per call depth. A C# function that calls Lua, which
			// calls another C# function, still sees its own context after the inner call returns.
			Script script = new Script();
			var inner = new CallbackFunction((ctx, args) => DynValue.NewString((string)ctx.AdditionalData), "inner") { AdditionalData = "inner" };
			var outer = new CallbackFunction((ctx, args) =>
			{
				MoonSharp.Interpreter.Debugging.SourceRef before = ctx.CallingLocation;
				DynValue innerResult = ctx.Call(args[0]);
				Assert.AreSame(before, ctx.CallingLocation);
				return DynValue.NewString((string)ctx.AdditionalData + "/" + innerResult.String);
			}, "outer") { AdditionalData = "outer" };

			script.Globals["inner"] = inner;
			script.Globals["outer"] = outer;

			DynValue r = script.DoString("return outer(function() return inner() end), outer(function() return outer(function() return inner() end) end)");

			Assert.AreEqual("outer/inner", r.Tuple[0].String);
			Assert.AreEqual("outer/outer/inner", r.Tuple[1].String);
		}

		[Test]
		public void CallPath_HostCallbacksMayKeepTheirArguments()
		{
			// A host callback gets argument objects of its own by default, which it may keep and read after it
			// returned (code written for MoonSharp 2.0 does). Only callbacks that declare they do not keep them
			// read the value stack in place.
			Script script = new Script();
			var kept = new System.Collections.Generic.List<CallbackArguments>();
			script.Globals["keep"] = DynValue.NewCallback((ctx, args) => { kept.Add(args); return DynValue.Nil; });

			script.DoString("local function two() return 'b', 'c' end keep(1, 'a') keep(2, two()) local function f(...) keep(...) end f(3, 4, 5)");

			Assert.AreEqual(3, kept.Count);
			Assert.AreEqual("1,a", kept[0][0].Number + "," + kept[0][1].String);
			Assert.AreEqual("2,b,c", kept[1][0].Number + "," + kept[1][1].String + "," + kept[1][2].String);
			Assert.AreEqual(3, kept[2].Count);
			Assert.AreEqual(5, kept[2][2].Number);
		}

		[Test]
		public void CallPath_CallbacksThatUseArgumentsOnlyDuringTheCall()
		{
			// Arguments read in place: every argument shape, ':' calls, and a callback that calls itself
			// through Lua reads its own arguments again after the inner call returned.
			Script script = new Script();
			script.Globals["f"] = new CallbackFunction((ctx, args) =>
			{
				string own = string.Join(",", System.Array.ConvertAll(args.GetArray(), v => v.ToPrintString()));

				if (args.Count > 0 && args[0].Type == DataType.Function)
				{
					string inner = ctx.Call(args[0]).String;
					string again = string.Join(",", System.Array.ConvertAll(args.GetArray(1), v => v.ToPrintString()));
					return DynValue.NewString(inner + "|" + again);
				}

				return DynValue.NewString(own + ";" + args.IsMethodCall);
			}, "f") { UsesArgumentsOnlyDuringCall = true };

			DynValue r = script.DoString(@"
				local function two() return 'b', 'c' end
				local function none() end
				local t = { f = f }
				return f(1, two()), f(two(), 'z'), f(1, none()), t:f(9), f(function() return f('x', 'y') end, 7, 8)");

			Assert.AreEqual("1,b,c;False", r.Tuple[0].String);
			Assert.AreEqual("b,z;False", r.Tuple[1].String);
			Assert.AreEqual("1;False", r.Tuple[2].String);
			StringAssert.EndsWith(",9;True", r.Tuple[3].String);
			Assert.AreEqual("x,y;False|7,8", r.Tuple[4].String);
		}

		public class ArgumentKeeper
		{
			public CallbackArguments Kept;

			public int Keep(CallbackArguments args)
			{
				if (Kept == null)
					Kept = args;

				return args.Count;
			}
		}

		[Test]
		public void CallPath_UserDataMethodsKeepACopyOfTheirArguments()
		{
			// Userdata methods read their arguments in place, but a CallbackArguments parameter gets a copy
			// that stays valid after the call.
			UserData.RegisterType<ArgumentKeeper>();
			Script script = new Script();
			var keeper = new ArgumentKeeper();
			script.Globals["k"] = keeper;

			DynValue r = script.DoString("local n = k:Keep(1, 'two', 3) k:Keep(4, 5) return n + #string.rep('x', 3)");

			Assert.AreEqual(6, r.Number);
			Assert.AreEqual(3, keeper.Kept.Count);
			Assert.AreEqual(1, keeper.Kept[0].Number);
			Assert.AreEqual("two", keeper.Kept[1].String);
			Assert.AreEqual(3, keeper.Kept[2].Number);
		}
			// Every shape of '...' (Lua 5.2's results, from lua5.2 5.2.4): the values stay on the value stack
		// where the caller pushed them, and each consumer reads them there or copies them out before the
		// frame goes.
		const string VarArgsShapes = @"
				local out = {}
				local function put(...) local t = {} for i = 1, select('#', ...) do t[i] = tostring((select(i, ...))) end out[#out + 1] = table.concat(t, ',') end
				local function none() end
				local function two() return 'b', 'c' end
				local function f(a, ...) local b, c = ... return a + b + c end
				put(f(1, 2, 3), f(1, 2, 3, 4))
				local function count(...) return select('#', ...) end
				put(count(), count(nil), count(nil, nil), count(1, none()), count(none(), 1), count(two()), count(1, two()))
				local function pack(...) return {...} end
				local function packn(...) return {n = select('#', ...), ...} end
				put(#pack(1, 2, 3), #pack(), packn(1, nil, 3).n, packn(1, nil, 3)[3])
				local function mixed(...) local t = {0, ...} local u = {..., 9} return #t, t[4], #u, u[1], u[2] end
				put(mixed(1, 2, 3))
				local function fwd(...) return count(...), string.format(string.rep('%s', select('#', ...)), ...) end
				put(fwd('x', 'y', 'z'))
				local function ret(...) return ... end
				put(ret()) put(ret(1)) put(ret(1, 2, 3)) put(ret(nil, nil))
				local function scalar(...) return (...) end
				put(scalar(4, 5, 6), scalar())
				local function mid(...) return count(..., 'z'), (select(2, ..., 'z')) end
				put(mid(1, 2, 3)) put(mid())
				local function orv(...) local a = ... or 5 return a end
				put(orv(), orv(false, 1), orv(7))
				local function arith(...) return ... + 1, ... .. '!' end
				put(arith(10, 20))
				local function loop(n, ...) if n == 0 then return select('#', ...), ... end return loop(n - 1, ...) end
				put(loop(300, 'a', nil, 'c'))
				local function loopmid(n, ...) if n == 0 then return select('#', ...), ... end return loopmid(n - 1, ..., n) end
				put(loopmid(3, 'q'))
				local function iter(...) local r = {} for i, v in ... do r[#r + 1] = v end return table.concat(r, ',') end
				put(iter(ipairs({'a', 'b'})))
				local function pc(...) return pcall(count, ...) end
				put(pc(1, 2, 3))
				local co = coroutine.wrap(function(...) local a, b = ... local x = coroutine.yield(select('#', ...), ...) return x, ... end)
				put(co(1, 2)) put(co('r'))
				local function nested(...) local function g(...) return select('#', ...), ... end return g(g(...)) end
				put(nested(5, 6))
				local function holes(a, b, ...) return a, b, select('#', ...), ... end
				put(holes(1)) put(holes(1, 2, nil)) put(holes(1, 2, nil, 4))
				local function asargs(...) return math.max(...), select(-1, ...) end
				put(asargs(3, 9, 4))
				local function unpacked(...) return table.unpack({...}) end
				put(unpacked(1, 2, 3))
				local function deep(...) local a = {...} local function inner(...) return #a + select('#', ...) end return inner(...) end
				put(deep(1, 2))
				return table.concat(out, '\n')";

		const string VarArgsShapesExpected =
				"6,6\n" +
				"0,1,2,1,2,2,3\n" +
				"3,0,3,3\n" +
				"4,3,2,1,9\n" +
				"3,xyz\n" +
				"\n" +
				"1\n" +
				"1,2,3\n" +
				"nil,nil\n" +
				"4,nil\n" +
				"2,z\n" +
				"2,z\n" +
				"5,5,7\n" +
				"11,10!\n" +
				"3,a,nil,c\n" +
				"2,q,1\n" +
				"a,b\n" +
				"true,3\n" +
				"2,1,2\n" +
				"r,1,2\n" +
				"3,2,5,6\n" +
				"1,nil,0\n" +
				"1,2,1,nil\n" +
				"1,2,2,nil,4\n" +
				"9,4\n" +
				"1,2,3\n" +
				"4\n";

		[Test]
		public void CallPath_VarArgsShapesFollowLua52()
		{
			Assert.AreEqual(VarArgsShapesExpected.TrimEnd('\n'), new Script().DoString(VarArgsShapes).String);
		}

		[Test]
		public void CallPath_VarArgsShapesFollowLua52UnderTailCalls()
		{
			// Every tail call reuses its frame: '...' values passed on must not point into the frame it removes.
			Script s = new Script();
			s.Options.TailCallOptimizationThreshold = 1;
			Assert.AreEqual(VarArgsShapesExpected.TrimEnd('\n'), s.DoString(VarArgsShapes).String);
		}
			[Test]
		public void CallPath_ArgumentReadersAndReturnWithoutDynValues()
		{
			// TypeAt, NumberAt and the other readers give what RawGet(i, false) ?? DynValue.Void would, in
			// both argument forms; Return gives the result to the VM, to pcall, to a C# caller through
			// ScriptExecutionContext.Call and to the host through Script.Call.
			foreach (bool inPlace in new[] { false, true })
			{
				Script script = new Script();
				var describe = new CallbackFunction((ctx, args) =>
				{
					var parts = new System.Collections.Generic.List<string>();

					for (int i = 0; i <= args.Count; i++)
					{
						parts.Add(args.TypeAt(i) + ":" + args.NumberAt(i) + ":" + args.BooleanAt(i) + ":" + (args.StringAt(i) ?? "-")
							+ ":" + (args.TableAt(i) != null) + ":" + (args.FunctionAt(i) != null) + ":" + (args.UserDataAt(i) != null)
							+ ":" + (args.RawGet(i, false) ?? DynValue.Void).Type);
					}

					return ctx.Return(string.Join(" ", parts));
				}, "describe") { UsesArgumentsOnlyDuringCall = inPlace };
				var add = new CallbackFunction((ctx, args) => ctx.Return(args.NumberAt(0) + args.NumberAt(1)), "add") { UsesArgumentsOnlyDuringCall = inPlace };
				var viaContext = new CallbackFunction((ctx, args) => DynValue.NewNumber(ctx.Call(DynValue.NewCallback(add), args[0], args[1]).Number * 10), "viaContext");

				script.Globals["describe"] = describe;
				script.Globals["add"] = add;
				script.Globals["viaContext"] = viaContext;
				UserData.RegisterType<ArgumentKeeper>();
				script.Globals["ud"] = UserData.Create(new ArgumentKeeper());

				DynValue r = script.DoString(@"
					local function none() end
					local function two() return 'x', 'y' end
					local ok, sum = pcall(add, 2, 3)
					return describe(5, true, 's', {}, print, function() end, ud, none(), nil, two()),
						add(1e10, 0.5), ok, sum, viaContext(4, 5), { add(1, 2) }");

				Assert.AreEqual("Number:5:True:-:False:False:False:Number Boolean:1:True:-:False:False:False:Boolean " +
					"String:0:False:s:False:False:False:String Table:0:False:-:True:False:False:Table " +
					"ClrFunction:0:False:-:False:False:False:ClrFunction Function:0:False:-:False:True:False:Function " +
					"UserData:0:False:-:False:False:True:UserData Void:0:False:-:False:False:False:Void " +
					"Nil:0:False:-:False:False:False:Nil String:0:False:x:False:False:False:String " +
					"String:0:False:y:False:False:False:String Void:0:False:-:False:False:False:Void", r.Tuple[0].String, "in place: " + inPlace);
				Assert.AreEqual(1e10 + 0.5, r.Tuple[1].Number);
				Assert.AreEqual(true, r.Tuple[2].Boolean);
				Assert.AreEqual(5, r.Tuple[3].Number);
				Assert.AreEqual(90, r.Tuple[4].Number);
				Assert.AreEqual(3, r.Tuple[5].Table.Get(1).Number);
				Assert.AreEqual(7, script.Call(script.Globals.Get("add"), 3, 4).Number);
			}
		}
			[Test]
		public void CallPath_CSharpFunctionsCalledByPcallSeeTheirOwnAdditionalData()
		{
			// pcall and ScriptExecutionContext.Call ran a C# function with the caller's context, so
			// AdditionalData was pcall's (null) or the calling callback's.
			Script script = new Script();
			var tagged = new CallbackFunction((ctx, args) => DynValue.NewString((string)ctx.AdditionalData), "tagged") { AdditionalData = "mine" };
			var caller = new CallbackFunction((ctx, args) => ctx.Call(args[0]), "caller") { AdditionalData = "caller" };
			script.Globals["tagged"] = tagged;
			script.Globals["caller"] = caller;

			DynValue r = script.DoString("local ok, v = pcall(tagged) return v, caller(tagged), tagged()");

			Assert.AreEqual("mine", r.Tuple[0].String);
			Assert.AreEqual("mine", r.Tuple[1].String);
			Assert.AreEqual("mine", r.Tuple[2].String);
		}

		[Test]
		public void CallPath_HostCallbacksMayKeepTheirContext()
		{
			// A host callback may keep its context and read it after it returned (a coroutine, a timer or an
			// async continuation does). The processor reuses one context per call depth for MoonSharp's own
			// functions only, so a later call at the same depth, of a host callback or of the library, does
			// not change what the kept context reports.
			Script script = new Script();
			var kept = new System.Collections.Generic.List<ScriptExecutionContext>();
			script.Globals["first"] = new CallbackFunction((ctx, args) => { kept.Add(ctx); return DynValue.Nil; }, "first") { AdditionalData = "first" };
			script.Globals["second"] = new CallbackFunction((ctx, args) => { kept.Add(ctx); return DynValue.Nil; }, "second")
				{ AdditionalData = "second", UsesArgumentsOnlyDuringCall = true };

			script.DoString("first()\nsecond()\nlocal s = tostring(1)\nfirst()");

			Assert.AreEqual(3, kept.Count);
			Assert.AreEqual("first", kept[0].AdditionalData);
			Assert.AreEqual(1, kept[0].CallingLocation.FromLine);
			Assert.AreEqual("second", kept[1].AdditionalData);
			Assert.AreEqual(2, kept[1].CallingLocation.FromLine);
			Assert.AreEqual(4, kept[2].CallingLocation.FromLine);
			Assert.AreNotSame(kept[0], kept[2]);
		}

		[MoonSharpUserData]
		public class ContextKeeper
		{
			public System.Collections.Generic.List<ScriptExecutionContext> Kept = new System.Collections.Generic.List<ScriptExecutionContext>();

			public int Keep(ScriptExecutionContext ctx, int x)
			{
				Kept.Add(ctx);
				return x;
			}

			public int Plain(int x) { return x; }

			public DynValue Twice(ScriptExecutionContext ctx, int x) { return ctx.Return(x * 2); }
		}

		[Test]
		public void CallPath_UserDataMethodsMayKeepTheirContext_Reflection()
		{
			UserDataMethodsMayKeepTheirContext("reflection");
		}

		[Test]
		public void CallPath_UserDataMethodsMayKeepTheirContext_Generated()
		{
			UserDataMethodsMayKeepTheirContext("generated");
		}

		[Test]
		public void CallPath_ConvertedDelegatesMayKeepTheirContext()
		{
			UserDataMethodsMayKeepTheirContext("delegate");
		}

		static void UserDataMethodsMayKeepTheirContext(string path)
		{
			// A CLR method with a ScriptExecutionContext parameter is host code that may keep the context, like
			// a host callback; methods without one share the context the processor reuses. Return in such a
			// method gives an ordinary DynValue: its context is not the one the processor reads results from.
			using (new RegistrySnapshot(typeof(ContextKeeper)))
			{
				if (path == "generated")
					UserData.RegisterType(MoonSharp.Generated.GeneratedUserData.CreateDescriptor(typeof(ContextKeeper)));
				else
					UserData.RegisterType<ContextKeeper>(InteropAccessMode.Reflection);

				Script script = new Script();
				var keeper = new ContextKeeper();
				script.Globals["k"] = keeper;

				string first = "local a = k:Keep(1)";

				if (path == "delegate")
				{
					script.Globals["keep"] = (System.Func<ScriptExecutionContext, int, int>)keeper.Keep;
					first = "local a = keep(1)";
				}

				// Every call at the same depth, so a reused context would move on to line 3.
				DynValue r = script.DoString(first + "\nlocal b = k:Plain(2) + #tostring(3)\nlocal c = k:Twice(4)\nreturn a + b + c");

				Assert.AreEqual(12, r.Number);
				Assert.AreEqual(1, keeper.Kept.Count);
				Assert.AreEqual(1, keeper.Kept[0].CallingLocation.FromLine);
			}
		}
	}
}
