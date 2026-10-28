using System.Collections.Generic;
using MoonSharp.Interpreter.Debugging;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// What the processor loop must keep doing however it fetches instructions: run code that load()
	/// adds while it runs, notice a debugger attached mid-run on the next instruction, and auto-yield
	/// after exactly the same number of instructions.
	/// </summary>
	[TestFixture]
	public class ProcessorLoopTests
	{
		[Test]
		public void ProcessorLoop_RunsCodeLoadedWhileRunning()
		{
			// Each load() appends a chunk to the bytecode while this loop runs; 400 of them grow it past
			// several sizes. The loop calls new and old functions alike.
			DynValue r = new Script().DoString(@"
				local fs, s = {}, 0
				for i = 1, 400 do
					fs[i] = load('return function(x) local y = x * 2 return y + ' .. i .. ' end')()
					s = s + fs[i](1) + fs[i > 50 and i - 50 or 1](0)
				end
				return s, fs[1](10), fs[400](10)");

			double expected = 0;
			for (int i = 1; i <= 400; i++)
				expected += 2 + i + (i > 50 ? i - 50 : 1);

			Assert.AreEqual(expected, r.Tuple[0].Number);
			Assert.AreEqual(21, r.Tuple[1].Number);
			Assert.AreEqual(420, r.Tuple[2].Number);
		}

		[Test]
		public void ProcessorLoop_RunsCodeLoadedByTheHostFromACallback()
		{
			Script s = new Script();
			int n = 0;
			s.Globals["make"] = DynValue.NewCallback((ctx, args) => s.LoadString("return " + (++n) + " * ..."));

			DynValue r = s.DoString(@"
				local total = 0
				for i = 1, 200 do total = total + make()(i) end
				return total");

			double expected = 0;
			for (int i = 1; i <= 200; i++)
				expected += i * i;

			Assert.AreEqual(expected, r.Number);
		}

		sealed class RecordingDebugger : IDebugger
		{
			public int Actions;
			public bool MarkedBeforeFirstAction;
			public bool Marked;

			public DebuggerCaps GetDebuggerCaps() { return DebuggerCaps.CanDebugSourceCode; }
			public void SetDebugService(DebugService debugService) { }
			public void SetSourceCode(SourceCode sourceCode) { }
			public void SetByteCode(string[] byteCode) { }
			public bool IsPauseRequested() { return false; }
			public bool SignalRuntimeException(ScriptRuntimeException ex) { return false; }
			public void SignalExecutionEnded() { }
			public void Update(WatchType watchType, IEnumerable<WatchItem> items, int stackFrameIndex) { }
			public List<DynamicExpression> GetWatchItems() { return new List<DynamicExpression>(); }
			public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }

			public DebuggerAction GetAction(int ip, SourceRef sourceref)
			{
				if (Actions++ == 0)
					MarkedBeforeFirstAction = Marked;

				return new DebuggerAction() { Action = DebuggerAction.ActionType.Run };
			}
		}

		[Test]
		public void ProcessorLoop_DebuggerAttachedMidRunStopsAtTheNextInstruction()
		{
			Script s = new Script();
			var debugger = new RecordingDebugger();
			s.Globals["attach"] = DynValue.NewCallback((ctx, args) => { s.AttachDebugger(debugger); return DynValue.Nil; });
			s.Globals["mark"] = DynValue.NewCallback((ctx, args) => { debugger.Marked = true; return DynValue.Nil; });

			s.DoString(@"
				local x = 0
				for i = 1, 10 do x = x + i end
				attach()
				mark()
				for i = 1, 10 do x = x + i end
				return x");

			Assert.Greater(debugger.Actions, 0, "the debugger was never asked for an action");
			Assert.IsFalse(debugger.MarkedBeforeFirstAction, "the next statement ran before the debugger was consulted");
		}

		const string AutoYieldCode = @"
			local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end
			return function() local s = 0 for i = 1, 3 do s = s + fib(10) end return s end";

		static int AutoYieldCycles(long counter)
		{
			Script s = new Script(CoreModules.None);
			DynValue fn = s.DoString(AutoYieldCode);
			DynValue co = s.CreateCoroutine(fn);
			co.Coroutine.AutoYieldCounter = counter;

			int cycles = 0;
			DynValue r = co.Coroutine.Resume();

			while (r.Type == DataType.YieldRequest)
			{
				cycles++;
				r = co.Coroutine.Resume();
			}

			Assert.AreEqual(165, r.Number);
			return cycles;
		}

		[Test]
		public void ProcessorLoop_AutoYieldCountsEveryInstruction()
		{
			// A yield comes after exactly AutoYieldCounter instructions, so the totals are the same however
			// the loop fetches. 8270 with the List-based loop; 2 fewer since the for loop clears its block
			// once before its 3 iterations instead of at the start of each; 8257 since the ForLoop opcode
			// ends an iteration in one instruction instead of six; 6133 since `n < 2` and its branch are one
			// fused instruction instead of five.
			Assert.AreEqual(6133, AutoYieldCycles(1));
			Assert.AreEqual(876, AutoYieldCycles(7));
			Assert.AreEqual(61, AutoYieldCycles(100));
		}
	}
}
