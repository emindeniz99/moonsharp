using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter.Execution;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	[TestFixture]
	public class TailCallTests
	{
		[Test]
		public void TcoTest_Pre()
		{
			// this just verifies the algorithm for TcoTest_Big
			string script = @"
				function recsum(num, partial)
					if (num == 0) then
						return partial
					else
						return recsum(num - 1, partial + num)
					end
				end
				
				return recsum(10, 0)";


			Script S = new Script();
			var res = S.DoString(script);

			Assert.AreEqual(DataType.Number, res.Type);
			Assert.AreEqual(55, res.Number);
		}

		[Test]
		public void TcoTest_Big()
		{
			// calc the sum of the first N numbers in the most stupid way ever to waste stack and trigger TCO..
			// (this could be a simple X*(X+1) / 2... )
			string script = @"
				function recsum(num, partial)
					if (num == 0) then
						return partial
					else
						return recsum(num - 1, partial + num)
					end
				end
				
				return recsum(70000, 0)";


			Script S = new Script();
			var res = S.DoString(script);

			Assert.AreEqual(DataType.Number, res.Type);
			Assert.AreEqual(2450035000.0, res.Number);
		}


		[Test]
		public void TailCallFromCLR()
		{
			string script = @"
				function getResult(x)
					return 156*x;  
				end

				return clrtail(9)";


			Script S = new Script();

			S.Globals.Set("clrtail", DynValue.NewCallback((xc, a) =>
			{
				DynValue fn = S.Globals.Get("getResult");
				DynValue k3 = DynValue.NewNumber(a[0].Number / 3);

				return DynValue.NewTailCallReq(fn, k3);
			}));

			var res = S.DoString(script);

			Assert.AreEqual(DataType.Number, res.Type);
			Assert.AreEqual(468, res.Number);
		}


		[Test]
		public void CheckToString()
		{
			string script = @"
				return tostring(9)";


			Script S = new Script(CoreModules.Basic);
			var res = S.DoString(script);

			Assert.AreEqual(DataType.String, res.Type);
			Assert.AreEqual("9", res.String);
		}

		[Test]
		public void CheckToStringMeta()
		{
			string script = @"
				t = {}
				m = {
					__tostring = function(v)
						return 'ciao';
					end
				}

				setmetatable(t, m);
				s = tostring(t);

				return (s);";


			Script S = new Script();
			var res = S.DoString(script);

			Assert.AreEqual(DataType.String, res.Type);
			Assert.AreEqual("ciao", res.String);
		}

		sealed class SingleFileLoader : Loaders.ScriptLoaderBase
		{
			public override object LoadFile(string file, Table globalContext) { return "return 'loaded'"; }
			public override bool ScriptFileExists(string name) { return true; }
		}

		// dofile ends with a tail call to the loaded chunk. A host calling it through Script.Call got the
		// internal tail call request back instead of the chunk's result.
		[Test]
		public void ClrFunctionTailCall_FinishedForHostCall()
		{
			var S = new Script();
			S.Options.ScriptLoader = new SingleFileLoader();

			DynValue res = S.Call(S.Globals.Get("dofile"), "a.lua");

			Assert.AreEqual(DataType.String, res.Type);
			Assert.AreEqual("loaded", res.String);
		}

		// A C# function that ends with a tail call can ask for a continuation: a C# function that receives
		// the callee's results (tostring checks what __tostring returned, pcall adds true). The VM ran it
		// when the callee was a Lua function and dropped it when the callee was a C# function, so the
		// caller got the callee's raw results.
		static Script WithTailCallingCallbacks()
		{
			Script s = new Script();

			// tail(f, ...) tail-calls f(...) and puts "continued" in front of its results.
			s.Globals["tail"] = DynValue.NewCallback((ctx, args) =>
			{
				DynValue req = DynValue.NewTailCallReq(args[0], args.GetArray(1));
				req.TailCallData.Continuation = new CallbackFunction((c, results) =>
				{
					var values = new List<DynValue> { DynValue.NewString("continued") };
					values.AddRange(results.GetArray());
					return DynValue.NewTuple(values.ToArray());
				}, "continuation");
				return req;
			}, "tail");

			s.Globals["clrvalues"] = DynValue.NewCallback((ctx, args) => DynValue.NewTuple(args.GetArray()), "clrvalues");
			s.Globals["clryield"] = DynValue.NewCallback((ctx, args) => DynValue.NewYieldReq(args.GetArray()), "clryield");
			s.Globals["clrerror"] = DynValue.NewCallback((ctx, args) => { throw new ScriptRuntimeException("clr failed"); }, "clrerror");
			return s;
		}

		[Test]
		public void ClrFunctionTailCall_ContinuationRunsForEveryCallee()
		{
			Script s = WithTailCallingCallbacks();

			DynValue r = s.DoString(@"
				local function luavalues(...) return ... end
				local function join(...) return table.concat({ ... }, ',') end
				return join(tail(luavalues, 1, 2)), join(tail(clrvalues, 1, 2)), join(tail(clrvalues)),
					join(tail(tail, clrvalues, 3)), join(tail(math.abs, -3))");

			Assert.AreEqual("continued,1,2", r.Tuple[0].String);
			Assert.AreEqual("continued,1,2", r.Tuple[1].String);
			Assert.AreEqual("continued", r.Tuple[2].String);
			// Nested requests: each continuation runs once, the inner one first.
			Assert.AreEqual("continued,continued,3", r.Tuple[3].String);
			// A library function the VM calls without a frame still gets the continuation.
			Assert.AreEqual("continued,3", r.Tuple[4].String);
		}

		[Test]
		public void ClrFunctionTailCall_ContinuationRunsAfterAYield()
		{
			Script s = WithTailCallingCallbacks();

			DynValue r = s.DoString(@"
				local co = coroutine.create(function(...)
					local got = { tail(clryield, 'out') }
					return table.concat(got, ',')
				end)
				local _, yielded = coroutine.resume(co)
				local _, result = coroutine.resume(co, 'in1', 'in2')
				return yielded, result, coroutine.status(co)");

			Assert.AreEqual("out", r.Tuple[0].String);
			// The values of the resume are the C# callee's results: the continuation sees them.
			Assert.AreEqual("continued,in1,in2", r.Tuple[1].String);
			Assert.AreEqual("dead", r.Tuple[2].String);
		}

		[Test]
		public void ClrFunctionTailCall_ErrorsReachPcallAndSkipTheContinuation()
		{
			Script s = WithTailCallingCallbacks();

			DynValue r = s.DoString(@"
				local ok, e = pcall(function() return tail(clrerror) end)
				local ok2, e2 = pcall(tail, clrerror)
				return ok, e, ok2, e2, select('#', tail(clrvalues, 1))");

			Assert.IsFalse(r.Tuple[0].Boolean);
			StringAssert.EndsWith("clr failed", r.Tuple[1].String);
			Assert.IsFalse(r.Tuple[2].Boolean);
			StringAssert.EndsWith("clr failed", r.Tuple[3].String);
			// The stacks are intact afterwards.
			Assert.AreEqual(2, r.Tuple[4].Number);
		}

		// pcall refused a C# function whose tail call carries a continuation ("cannot be called directly
		// by pcall"), so pcall(tostring, x) failed for any x with a __tostring metamethod, and
		// pcall(coroutine.yield) failed where Lua 5.2 yields. The expectations are lua5.2's output, except
		// for b: MoonSharp's tostring checks what __tostring returns, as Lua 5.3 does.
		[Test]
		public void Pcall_AroundCSharpFunctionsThatTailCallOrYield()
		{
			Script s = WithTailCallingCallbacks();

			DynValue r = s.DoString(@"
				local o = setmetatable({}, { __tostring = function() return 'obj' end })
				local bad = setmetatable({}, { __tostring = function() return {} end })
				local a = { pcall(tostring, o) }
				local b = { pcall(tostring, bad) }
				local c = { pcall(pcall, error, 'e') }
				local d = { pcall(tail, clrvalues, 1) }
				local co = coroutine.wrap(function()
					local y = { pcall(coroutine.yield, 'y') }
					return #y, y[1], y[2]
				end)
				co()
				return a[1], a[2], b[1], b[2], c[1], c[2], c[3], d[1], d[2], d[3], co('r')");

			Assert.AreEqual(true, r.Tuple[0].Boolean);
			Assert.AreEqual("obj", r.Tuple[1].String);
			// The check of tostring's continuation is an error that pcall catches.
			Assert.AreEqual(false, r.Tuple[2].Boolean);
			StringAssert.EndsWith("'tostring' must return a string", r.Tuple[3].String);
			Assert.AreEqual(true, r.Tuple[4].Boolean);
			Assert.AreEqual(false, r.Tuple[5].Boolean);
			Assert.AreEqual("e", r.Tuple[6].String);
			Assert.AreEqual(true, r.Tuple[7].Boolean);
			Assert.AreEqual("continued", r.Tuple[8].String);
			Assert.AreEqual(1, r.Tuple[9].Number);
			Assert.AreEqual(2, r.Tuple[10].Number);
			Assert.AreEqual(true, r.Tuple[11].Boolean);
			Assert.AreEqual("r", r.Tuple[12].String);
		}

		// A C# __tostring went through tostring's check once the continuation ran for C# callees; Lua 5.2
		// takes a number from __tostring (lua_isstring), so the check takes one too.
		[Test]
		public void ToString_CSharpMetamethodResultIsChecked()
		{
			Script s = new Script();
			s.Globals["clrnumber"] = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(7), "clrnumber");
			s.Globals["clrtable"] = DynValue.NewCallback((ctx, args) => DynValue.NewTable(ctx.GetScript()), "clrtable");

			DynValue r = s.DoString(@"
				local n = tostring(setmetatable({}, { __tostring = clrnumber }))
				local ok, e = pcall(function() return tostring(setmetatable({}, { __tostring = clrtable })) end)
				return type(n), n, ok, e");

			Assert.AreEqual("string", r.Tuple[0].String);
			Assert.AreEqual("7", r.Tuple[1].String);
			Assert.AreEqual(false, r.Tuple[2].Boolean);
			StringAssert.EndsWith("'tostring' must return a string", r.Tuple[3].String);
		}
	}
}
