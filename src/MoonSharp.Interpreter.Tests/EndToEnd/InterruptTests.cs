using System.Collections.Generic;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// ScriptOptions.InterruptHandler: a host can stop a script that does not return, and pcall cannot
	/// swallow the stop.
	/// </summary>
	[TestFixture]
	public class InterruptTests
	{
		const long Budget = 1000000;

		static Script WithBudget(long budget, CoreModules modules = CoreModules.Preset_Default)
		{
			Script s = new Script(modules);
			s.Options.InterruptHandler = n => n >= budget;
			return s;
		}

		static ScriptInterruptedException AssertInterrupted(Script s, string code)
		{
			ScriptInterruptedException ex = Assert.Throws<ScriptInterruptedException>(() => s.DoString(code));
			Assert.IsTrue(ex.Instructions >= Budget, "instructions before the interrupt");
			Assert.Less(ex.Instructions, Budget + 10000);
			return ex;
		}

		[Test]
		public void Interrupt_StopsEndlessLoops()
		{
			AssertInterrupted(WithBudget(Budget), "while true do end");
			AssertInterrupted(WithBudget(Budget), "repeat until false");
			AssertInterrupted(WithBudget(Budget), "local i = 0 while true do i = i + 1 end");
			AssertInterrupted(WithBudget(Budget), "local function f() return f() end f()");
		}

		[Test]
		public void Interrupt_IsNotCaughtByScriptErrorHandlers()
		{
			Script s = WithBudget(Budget);
			s.Globals["reached"] = false;

			AssertInterrupted(s, "pcall(function() while true do end end) reached = true");
			AssertInterrupted(s, "xpcall(function() while true do end end, function(m) return m end) reached = true");
			AssertInterrupted(s, "local co = coroutine.create(function() while true do end end) coroutine.resume(co) reached = true");
			AssertInterrupted(s, "coroutine.wrap(function() while true do end end)() reached = true");

			Assert.IsFalse(s.Globals.Get("reached").Boolean);
		}

		[Test]
		public void Interrupt_StopsLoopsRunFromCSharpCallbacks()
		{
			// The count spans the whole call from the host, through C# and back into Lua.
			Script s = WithBudget(Budget);
			s.Globals["callLua"] = DynValue.NewCallback((ctx, args) => ctx.Call(args[0]));

			AssertInterrupted(s, "callLua(function() while true do end end)");
			AssertInterrupted(s, "local t = setmetatable({}, { __index = function() while true do end end }) return t.x");
		}

		[Test]
		public void Interrupt_ScriptStaysUsable()
		{
			Script s = WithBudget(Budget);
			AssertInterrupted(s, "while true do end");

			// A later call starts a new count.
			Assert.AreEqual(5050, s.DoString("local s = 0 for i = 1, 100 do s = s + i end return s").Number);

			DynValue co = s.CreateCoroutine(s.DoString("return function() while true do end end"));
			Assert.Throws<ScriptInterruptedException>(() => co.Coroutine.Resume());
			Assert.AreEqual(CoroutineState.Dead, co.Coroutine.State);
		}

		[Test]
		public void Interrupt_CountIsDeterministic()
		{
			// The handler sees the same counts for the same script, every run, every host.
			var first = new List<long>();
			var second = new List<long>();

			foreach (var seen in new[] { first, second })
			{
				Script s = new Script();
				s.Options.InterruptHandler = n => { seen.Add(n); return false; };
				s.DoString("local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end return fib(20)");
			}

			CollectionAssert.AreEqual(first, second);
			Assert.Greater(first.Count, 10);

			// Every 10,000 instructions; the count includes the instruction about to run.
			for (int i = 0; i < first.Count; i++)
				Assert.AreEqual(10000L * (i + 1) + 1, first[i]);
		}

		[Test]
		public void Interrupt_NoHandlerNoEffect()
		{
			Script s = new Script();
			Assert.AreEqual(2584, s.DoString("local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end return fib(18)").Number);
		}
	}
}
