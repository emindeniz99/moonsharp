using System;

namespace MoonSharp.Interpreter.Execution.VM
{
	// This part is practically written procedural style - it looks more like C than C#.
	// This is intentional so to avoid this-calls and virtual-calls as much as possible.
	// Same reason for the "sealed" declaration.
	sealed partial class Processor
	{
		public DynValue Coroutine_Create(DynValue function)
		{
			// create a processor instance
			Processor P = new Processor(this);

			// Put the function (a Lua closure or a C# function) as first value on the stack, for future reference
			P.m_ValueStack.Push(function);

			// Return the coroutine handle
			return DynValue.NewCoroutine(new Coroutine(P));
		}

		public DynValue Coroutine_Recycle(Processor mainProcessor, Closure closure)
		{
			// Clear the used parts of the stacks to prep for reuse. A coroutine that died with an error can
			// leave open upvalues behind; close them first, or closures would see the new coroutine's values.
			CloseUpvalues(0);
			this.m_ValueStack.ClearUsed();
			this.m_ExecutionStack.ClearUsed();

			// Create a new processor instance, recycling this one
			Processor P = new Processor(mainProcessor, this);

			// Put the closure as first value on the stack, for future reference
			P.m_ValueStack.Push(DynValue.NewClosure(closure));

			// Return the coroutine handle
			return DynValue.NewCoroutine(new Coroutine(P));
		}

		// The yield request of coroutine.yield, reused by every yield of this coroutine: Coroutine_Resume
		// takes its values before the coroutine can run again.
		DynValue m_YieldRequest;

		/// <summary>
		/// A request to yield <paramref name="values"/> from this coroutine.
		/// </summary>
		internal DynValue YieldRequestFor(DynValue[] values)
		{
			if (m_YieldRequest == null)
				m_YieldRequest = DynValue.NewYieldReq(values);
			else
				m_YieldRequest.YieldRequest.ReturnValues = values;

			return m_YieldRequest;
		}

		/// <summary>
		/// True when the top frame is a C# function's: when the coroutine is suspended, only a pending frame
		/// can be (a running C# function cannot yield).
		/// </summary>
		private bool PendingFrameOnTop()
		{
			return m_ExecutionStack.Count > 0 && m_ExecutionStack.Peek().ClrFunction != null;
		}

		public CoroutineState State { get { return m_State; } }
		public Coroutine AssociatedCoroutine { get; set; }

		public DynValue Coroutine_Resume(DynValue[] args)
		{
			EnterNative();
			EnterProcessor();
			bool completed = false;

			try
			{
				if (m_State != CoroutineState.NotStarted && m_State != CoroutineState.Suspended && m_State != CoroutineState.ForceSuspended)
					throw ScriptRuntimeException.CannotResumeNotSuspended(m_State);

				if (m_State == CoroutineState.ForceSuspended && args != null && args.Length > 0)
					throw new ArgumentException("When resuming a force-suspended coroutine, args must be empty.");

				CoroutineState previous = m_State;
				m_State = CoroutineState.Running;
				DynValue retVal;

				if (previous == CoroutineState.NotStarted && m_ValueStack.Peek().Type == DataType.ClrFunction)
				{
					// A C# function as the body (coroutine.create(select)): call it as the VM calls one. A tail
					// call it returns runs here as the coroutine's code; a yield it returns suspends it.
					int ip = Internal_ExecCall(PushHostArguments(args), -1);

					if (ip == YIELD_SPECIAL_TRAP)
						retVal = m_ValueStack.Pop().ToScalar().ToDynValue();
					else if (ip < 0)
						retVal = m_ValueStack.Pop().ToDynValue();
					else
						retVal = Processing_Loop(ip);
				}
				else if (previous == CoroutineState.NotStarted)
				{
					retVal = Processing_Loop(PushClrToScriptStackFrame(CallStackItemFlags.ResumeEntryPoint, null, args));
				}
				else if (m_SavedInstructionPtr < 0 && !PendingFrameOnTop())
				{
					// A C# body that yielded (coroutine.create(coroutine.yield)) has nothing left to run:
					// the values of this resume are its results.
					retVal = DynValue.NewTuple(args);
				}
				else
				{
					// The values of this resume are the results of the yield call; none is no value, so
					// select('#', coroutine.yield()) is 0 and a local assigned from it is nil.
					if (previous == CoroutineState.Suspended)
						m_ValueStack.Push(DynValue.NewTuple(args));

					// A C# function that yielded with handlers (pcall(coroutine.yield), a continuation)
					// left its frame pending: the values go through it first.
					m_ReturnToPending = PendingFrameOnTop();
					retVal = Processing_Loop(m_SavedInstructionPtr);
				}

				completed = true;

				if (retVal.Type == DataType.YieldRequest)
				{
					if (retVal.YieldRequest.Forced)
					{
						m_State = CoroutineState.ForceSuspended;
						return retVal;
					}
					else
					{
						m_State = CoroutineState.Suspended;
						DynValue[] values = retVal.YieldRequest.ReturnValues;

						// the reused request must not keep the values alive
						if (retVal == m_YieldRequest)
							retVal.YieldRequest.ReturnValues = null;

						return DynValue.NewTuple(values);
					}
				}
				else
				{
					m_State = CoroutineState.Dead;
					return retVal;
				}
			}
			finally
			{
				// An unhandled exception leaves the coroutine dead. A finally block, not catch and rethrow:
				// see Processing_Loop.
				if (!completed)
					m_State = CoroutineState.Dead;

				LeaveProcessor();
				t_NativeNesting--;
			}
		}



	}

}
