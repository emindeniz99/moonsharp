using System.Collections.Generic;
using MoonSharp.Interpreter.Debugging;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Locals, upvalues and the values read from them must behave as separate variables and values, as in
	/// Lua 5.2: a value read from a variable keeps its value when the variable is assigned again, each loop
	/// iteration declares a new local, and closures that capture one variable share it. These tests pin
	/// that behaviour for the VM's storage of locals (value stack slots) and upvalues (open and closed
	/// upvalues).
	/// </summary>
	[TestFixture]
	public class VariableAliasingTests
	{
		static string Run(string code)
		{
			return new Script(CoreModules.Preset_Complete).DoString(code).ToPrintString();
		}

		[Test]
		public void Aliasing_ValueReadFromLocalKeepsItsValueWhenTheLocalChanges()
		{
			Assert.AreEqual("1\t1\t2", Run(@"
				local a = 1
				local t = { a }
				local b = a
				a = 2
				return t[1], b, a"));
		}

		[Test]
		public void Aliasing_LoopCounterCopiedToALocalDoesNotAdvance()
		{
			Assert.AreEqual("1\t3", Run(@"
				local first, last
				for i = 1, 3 do
					if i == 1 then first = i end
					last = i
				end
				return first, last"));
		}

		[Test]
		public void Aliasing_ParameterChangedInTheCalleeLeavesTheCallerAlone()
		{
			Assert.AreEqual("5\t6", Run(@"
				local function f(p) p = p + 1 return p end
				local x = 5
				local y = f(x)
				return x, y"));
		}

		[Test]
		public void Aliasing_ClosuresInANumericForSeeTheirOwnIteration()
		{
			Assert.AreEqual("123", Run(@"
				local fs = {}
				for i = 1, 3 do fs[i] = function() return i end end
				return fs[1]() .. fs[2]() .. fs[3]()"));
		}

		[Test]
		public void Aliasing_ClosuresInAGenericForSeeTheirOwnIteration()
		{
			Assert.AreEqual("a1b2c3", Run(@"
				local fs = {}
				for k, v in ipairs({ 'a', 'b', 'c' }) do fs[k] = function() return v .. k end end
				return fs[1]() .. fs[2]() .. fs[3]()"));
		}

		[Test]
		public void Aliasing_ClosuresInWhileAndRepeatBodiesSeeTheirOwnLocal()
		{
			Assert.AreEqual("123|123", Run(@"
				local fs, gs = {}, {}
				local i = 1
				while i <= 3 do local j = i fs[i] = function() return j end i = i + 1 end
				i = 1
				repeat local j = i gs[i] = function() return j end i = i + 1 until j >= 3
				return fs[1]() .. fs[2]() .. fs[3]() .. '|' .. gs[1]() .. gs[2]() .. gs[3]()"));
		}

		[Test]
		public void Aliasing_ClosureSeesLaterAssignmentsInTheSameIteration()
		{
			Assert.AreEqual("10\t20", Run(@"
				local fs = {}
				for i = 1, 2 do local v = i fs[i] = function() return v end v = v * 10 end
				return fs[1](), fs[2]()"));
		}

		[Test]
		public void Aliasing_ClosureCreatedBeforeBreakKeepsItsValue()
		{
			Assert.AreEqual("2\tz", Run(@"
				local f
				for i = 1, 10 do
					local v = i
					f = function() return v end
					if i == 2 then break end
				end
				local a, b, c = 'x', 'y', 'z'
				return f(), c"));
		}

		[Test]
		public void Aliasing_LoopEnteredAgainAfterAGotoOutOfItsBody()
		{
			// The goto leaves the inner loop without running its end-of-iteration clear; entering the loop
			// again must still leave the closures already made their own values. (While loops: a goto out
			// of a for loop leaves the loop's control values on the stack, a separate bug.)
			Assert.AreEqual("1224", Run(@"
				local fs = {}
				for round = 1, 2 do
					local i = 0
					while i < 3 do
						i = i + 1
						local v = i * round
						fs[#fs + 1] = function() return v end
						if i == 2 then goto next end
					end
					::next::
				end
				return fs[1]() .. fs[2]() .. fs[3]() .. fs[4]()"));
		}

		[Test]
		public void Aliasing_ClosuresInAGotoLoopSeeTheirOwnLocal()
		{
			Assert.AreEqual("123", Run(@"
				local fs = {}
				local i = 1
				::top::
				do
					local x = i
					fs[i] = function() return x end
				end
				i = i + 1
				if i <= 3 then goto top end
				return fs[1]() .. fs[2]() .. fs[3]()"));
		}

		[Test]
		public void Aliasing_TwoClosuresShareOneUpvalue()
		{
			Assert.AreEqual("2\t2\t3", Run(@"
				local function make()
					local n = 0
					local function inc() n = n + 1 return n end
					local function get() return n end
					inc()
					local inside = n
					return inc, get, inside
				end
				local inc, get, inside = make()
				inc()
				local afterReturn = get()
				inc()
				return inside + 1, afterReturn, get()"));
		}

		[Test]
		public void Aliasing_NestedClosureSharesTheUpvalueOfItsParent()
		{
			Assert.AreEqual("3\t3", Run(@"
				local n = 1
				local function outer()
					return function() n = n + 1 return n end
				end
				local inner = outer()
				inner()
				inner()
				return n, (function() return n end)()"));
		}

		[Test]
		public void Aliasing_LocalFunctionCanCallItselfThroughItsUpvalue()
		{
			Assert.AreEqual("120", Run(@"
				local function fact(n) if n <= 1 then return 1 end return n * fact(n - 1) end
				return fact(5)"));
		}

		[Test]
		public void Aliasing_ClosureKeepsTheValueAfterAnErrorUnwindsItsFunction()
		{
			Assert.AreEqual("7\t3", Run(@"
				local f
				pcall(function()
					local v = 7
					f = function() return v end
					error('boom')
				end)
				local a, b, c = 1, 2, 3
				return f(), c"));
		}

		[Test]
		public void Aliasing_ClosureKeepsTheValueAfterAFailedHostCall()
		{
			Script s = new Script();
			DynValue fn = s.DoString(@"
				return function()
					local v = 'kept'
					captured = function() return v end
					error('boom')
				end");

			Assert.Throws<ScriptRuntimeException>(() => s.Call(fn));
			s.DoString("local a, b, c, d = 1, 2, 3, 4");

			Assert.AreEqual("kept", s.Call(s.Globals.Get("captured")).String);
		}

		[Test]
		public void Aliasing_CoroutineLocalsCapturedByClosures()
		{
			Assert.AreEqual("1\t2\t20\t20", Run(@"
				local get, set
				local co = coroutine.create(function()
					local v = 1
					get = function() return v end
					set = function(x) v = x end
					coroutine.yield()
					v = v + 1
					coroutine.yield()
					return v
				end)
				coroutine.resume(co)
				local a = get()
				coroutine.resume(co)
				local b = get()
				set(20)
				local _, r = coroutine.resume(co)
				return a, b, r, get()"));
		}

		[Test]
		public void Aliasing_RecycledCoroutineDoesNotChangeTheDeadOnesUpvalues()
		{
			Script s = new Script();
			DynValue body = s.DoString(@"
				return function()
					local v = 'old'
					captured = function() return v end
					error('boom')
				end");
			DynValue other = s.DoString("return function(a, b, c) local x, y, z = 'new', 'new', 'new' coroutine.yield(x) end");

			DynValue co = s.CreateCoroutine(body);
			Assert.Throws<ScriptRuntimeException>(() => co.Coroutine.Resume());

			DynValue recycled = s.RecycleCoroutine(co.Coroutine, other);
			recycled.Coroutine.Resume(1, 2, 3);

			Assert.AreEqual("old", s.Call(s.Globals.Get("captured")).String);
		}

		[Test]
		public void Aliasing_DebugSetupvalueChangesTheSharedVariable()
		{
			Assert.AreEqual("v\t9\t9", Run(@"
				local v = 1
				local function get() return v end
				local n = 1
				while debug.getupvalue(get, n) ~= 'v' do n = n + 1 end
				local name = debug.setupvalue(get, n, 9)
				return name, v, get()"));
		}

		[Test]
		public void Aliasing_HostSetUpvalueChangesTheSharedVariable()
		{
			Script s = new Script();
			DynValue get = s.DoString("local v = 1 setter = function() return v end return function() return v end");

			Closure c = get.Function;
			int v = 0;

			while (c.GetUpvalueName(v) != "v")
				v++;

			Assert.Throws<ScriptRuntimeException>(() => c.GetUpvalue(v).Assign(DynValue.NewNumber(5)));

			c.SetUpvalue(v, DynValue.NewNumber(5));

			Assert.AreEqual(5.0, s.Call(get).Number);
			Assert.AreEqual(5.0, s.Call(s.Globals.Get("setter")).Number);
		}

		[Test]
		public void Aliasing_HostCannotChangeALocalThroughAnArgument()
		{
			Script s = new Script();
			s.Globals["poke"] = DynValue.NewCallback((ctx, args) =>
			{
				try { args[0].Assign(DynValue.NewNumber(99)); } catch (ScriptRuntimeException) { }
				return DynValue.Nil;
			});

			Assert.AreEqual(1.0, s.DoString("local x = 1 poke(x) return x").Number);
		}

		[Test]
		public void Aliasing_SymbolLookupSkipsLocalsOfBlocksThatEnded()
		{
			Script s = new Script();
			s.Globals["probe"] = DynValue.NewCallback((ctx, args) => ctx.EvaluateSymbolByName("x"));

			Assert.AreEqual("1\t2", s.DoString(@"
				local x = 1
				local inner
				do local x = 2 inner = probe() end
				return probe(), inner").ToPrintString());
		}

		sealed class LocalsDebugger : IDebugger
		{
			public bool PauseRequested;
			public bool Record;
			public Dictionary<string, string> Locals;

			public DebuggerCaps GetDebuggerCaps() { return DebuggerCaps.CanDebugSourceCode; }
			public void SetDebugService(DebugService debugService) { }
			public void SetSourceCode(SourceCode sourceCode) { }
			public void SetByteCode(string[] byteCode) { }
			public bool IsPauseRequested() { return PauseRequested; }
			public bool SignalRuntimeException(ScriptRuntimeException ex) { return false; }
			public void SignalExecutionEnded() { }
			public List<DynamicExpression> GetWatchItems() { return new List<DynamicExpression>(); }
			public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }

			public void Update(WatchType watchType, IEnumerable<WatchItem> items, int stackFrameIndex)
			{
				if (watchType != WatchType.Locals || !Record)
					return;

				Record = false;

				Locals = new Dictionary<string, string>();

				foreach (WatchItem item in items)
					if (item.Value != null)
						Locals[item.Name] = item.Value.ToPrintString();
			}

			public DebuggerAction GetAction(int ip, SourceRef sourceref)
			{
				PauseRequested = false;
				return new DebuggerAction() { Action = DebuggerAction.ActionType.Run };
			}
		}

		[Test]
		public void Aliasing_DebuggerSeesTheCurrentValuesOfLocals()
		{
			Script s = new Script();
			var debugger = new LocalsDebugger();
			s.AttachDebugger(debugger);
			s.Globals["pause"] = DynValue.NewCallback((ctx, args) => { debugger.PauseRequested = debugger.Record = true; return DynValue.Nil; });

			s.DoString(@"
				local function f(p)
					local a = p
					local b = 'x'
					a = a + 1
					pause()
					local c = a
					return c
				end
				return f(1)");

			Assert.IsNotNull(debugger.Locals, "the debugger never paused");
			Assert.AreEqual("1", debugger.Locals["p"]);
			Assert.AreEqual("2", debugger.Locals["a"]);
			Assert.AreEqual("x", debugger.Locals["b"]);
			Assert.IsFalse(debugger.Locals.ContainsKey("c"), "c is not declared yet");
		}
	}
}
