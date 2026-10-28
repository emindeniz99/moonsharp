using System.Collections.Generic;
using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		// The calling location of every typed call's frame. Call creates one per call (48 bytes); nothing
		// changes a CLR location (the debugger only shows it), so one instance serves all.
		static readonly SourceRef s_HostCallLocation = SourceRef.GetClrLocation();

		/// <summary>
		/// Calls a Lua function from C# with up to four arguments already converted to VM values, and returns
		/// its result as a VM value: what <see cref="Call"/> does with a <see cref="DynValue"/> array, without
		/// the array and without a <see cref="DynValue"/> per argument or for the result. Used by the typed
		/// calls of <see cref="Closure"/>.
		/// </summary>
		internal LuaValue CallFromHost(Closure function, int argCount, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4)
		{
			List<Processor> coroutinesStack = m_Parent != null ? m_Parent.m_CoroutinesStack : this.m_CoroutinesStack;

			if (coroutinesStack.Count > 0 && coroutinesStack[coroutinesStack.Count - 1] != this)
				return coroutinesStack[coroutinesStack.Count - 1].CallFromHost(function, argCount, a1, a2, a3, a4);

			EnterNative();
			EnterProcessor();

			int valueStackBase = m_ValueStack.Count;
			int executionStackBase = m_ExecutionStack.Count;

			try
			{
				var stopwatch = this.m_Script.PerformanceStats.StartStopwatch(Diagnostics.PerformanceCounter.Execution);

				m_CanYield = false;
				bool completed = false;

				try
				{
					m_ValueStack.Push(LuaValue.FromObject(DataType.Function, function));

					// As Internal_AdjustTuple: a tuple (only a DynValue argument can be one) gives its first
					// value, or all of them when it is the last argument.
					int pushed = 0;
					if (argCount >= 1) pushed += PushHostArgument(ref a1, argCount == 1);
					if (argCount >= 2) pushed += PushHostArgument(ref a2, argCount == 2);
					if (argCount >= 3) pushed += PushHostArgument(ref a3, argCount == 3);
					if (argCount >= 4) pushed += PushHostArgument(ref a4, argCount == 4);

					ref CallStackItem frame = ref m_ExecutionStack.Push();
					frame.BasePointer = m_ValueStack.Count;
					frame.ArgCount = pushed;
					frame.Debug_EntryPoint = function.EntryPointByteCodeLocation;
					frame.ReturnAddress = -1;
					frame.Function = function;
					frame.Upvalues = function.Upvalues;
					frame.CallingSourceRef = s_HostCallLocation;
					frame.Flags = CallStackItemFlags.CallEntryPoint;

					LuaValue result = Processing_LoopValue(function.EntryPointByteCodeLocation);
					completed = true;
					return result;
				}
				finally
				{
					// As in Call: drop what a failed call left on the stacks.
					if (!completed)
					{
						CloseUpvalues(valueStackBase);
						m_ValueStack.CropAtCount(valueStackBase);
						m_ExecutionStack.CropAtCount(executionStackBase);
					}

					m_CanYield = true;

					if (stopwatch != null)
						stopwatch.Dispose();
				}
			}
			finally
			{
				LeaveProcessor();
				t_NativeNesting--;
			}
		}

		private int PushHostArgument(ref LuaValue value, bool last)
		{
			if (value.Type != DataType.Tuple)
			{
				m_ValueStack.Push(value);
				return 1;
			}

			if (!last)
			{
				m_ValueStack.Push(value.ToScalar());
				return 1;
			}

			DynValue[] values = Internal_AdjustTuple(new DynValue[] { value.ToDynValue() });

			for (int i = 0; i < values.Length; i++)
				m_ValueStack.Push(values[i]);

			return values.Length;
		}
	}
}
