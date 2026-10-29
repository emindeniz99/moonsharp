using System;
using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		// The ScriptExecutionContext and the argument view of C# function calls, one of each per call depth
		// (the index of the function's frame), created on first use and reused by every later call at that
		// depth: a C# function running at one depth can call Lua, which calls C# functions only at greater
		// depths. The context is lent only to MoonSharp's own functions (CallbackFunction.ReusesContext),
		// which never let host code keep it; a host callback may keep its context and read it after it
		// returned, so it gets a new one.
		ScriptExecutionContext[] m_CallContexts;
		CallbackArguments.StackArguments[] m_CallArguments;

		/// <summary>
		/// The result a C# function called by this processor gave with ScriptExecutionContext.Return, until the
		/// processor takes it as the function returns. One slot per processor (per coroutine) is enough: the
		/// function returns the placeholder right after Return, before any other call on this processor; in
		/// the context it would make every context 24 bytes larger.
		/// </summary>
		internal LuaValue CallResult;

		/// <summary>
		/// The execution context for a call of <paramref name="callback"/> whose frame is at
		/// <paramref name="depth"/>.
		/// </summary>
		private ScriptExecutionContext CallContext(int depth, CallbackFunction callback, SourceRef sourceRef)
		{
			if (!callback.ReusesContext)
				return new ScriptExecutionContext(this, callback, sourceRef) { LentByProcessor = true };

			if (m_CallContexts == null || depth >= m_CallContexts.Length)
				Array.Resize(ref m_CallContexts, Math.Max(8, (depth + 1) * 2));

			ScriptExecutionContext ctx = m_CallContexts[depth];

			if (ctx == null)
				m_CallContexts[depth] = ctx = new ScriptExecutionContext(this, callback, sourceRef) { LentByProcessor = true };
			else
				ctx.Reuse(callback, sourceRef);

			return ctx;
		}

		/// <summary>
		/// The argument view for a call whose frame is at <paramref name="depth"/>, for a C# function that
		/// uses its arguments only during the call (<see cref="CallbackFunction.UsesArgumentsOnlyDuringCall"/>).
		/// </summary>
		private CallbackArguments.StackArguments CallArguments(int depth)
		{
			if (m_CallArguments == null || depth >= m_CallArguments.Length)
				Array.Resize(ref m_CallArguments, Math.Max(8, (depth + 1) * 2));

			CallbackArguments.StackArguments args = m_CallArguments[depth];

			if (args == null)
				m_CallArguments[depth] = args = new CallbackArguments.StackArguments(m_ValueStack, 0, 0, false);

			return args;
		}
	}
}
