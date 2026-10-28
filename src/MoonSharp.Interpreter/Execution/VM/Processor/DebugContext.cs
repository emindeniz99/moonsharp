using System.Collections.Generic;
using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		// Shared by a Script's main processor and all its coroutines.
		private class DebugContext
		{
			public bool DebuggerEnabled = true;
			public IDebugger DebuggerAttached = null;
			public DebuggerAction.ActionType DebuggerCurrentAction = DebuggerAction.ActionType.None;
			public int DebuggerCurrentActionTarget = -1;
			public SourceRef LastHlRef = null;
			public int ExStackDepthAtStep = -1;
			public List<SourceRef> BreakPoints = new List<SourceRef>();
			public bool LineBasedBreakPoints = false;

			// The loop decrements Countdown on every instruction and takes the slow path
			// (ExecutionCheckpoint) when it reaches zero. Armed is what it was last set to, so
			// Armed - Countdown instructions have run since then.
			public int Countdown;
			public int Armed;
			// Instructions run since the host's outermost call into the Script began, and when the
			// interrupt handler is due next.
			public long Executed;
			public long NextInterruptCheck;
			// Calls into the Script that have not returned yet (host calls, callbacks, resumes).
			public int EntryDepth;

			public void Rearm(int countdown)
			{
				Executed += Armed - Countdown;
				Armed = Countdown = countdown;
			}
		}
	}
}
