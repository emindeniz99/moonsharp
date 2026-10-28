using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using MoonSharp.Interpreter.DataStructs;
using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		// Maximum stack depths. The stacks start small and grow on demand, so an idle Script or
		// coroutine does not hold two arrays of this size.
		// Slots are 24-byte structs (values) and frames are structs too, so the initial arrays stay small:
		// 64 values and 8 frames are 1.5 KB and about 0.6 KB, as much as 256 and 64 references were.
		const int STACK_SIZE = 131072;
		const int INITIAL_VALUE_STACK_SIZE = 64;
		const int INITIAL_EXECUTION_STACK_SIZE = 8;

		ByteCode m_RootChunk;

		ValueStack m_ValueStack;
		CallStack m_ExecutionStack;
		List<Processor> m_CoroutinesStack;

		Table m_GlobalTable;
		Script m_Script;
		Processor m_Parent = null;
		CoroutineState m_State;
		bool m_CanYield = true;
		int m_SavedInstructionPtr = -1;
		DebugContext m_Debug;

		// Open upvalues of this processor's stack, sorted by stack index, highest first (lfunc.c's openupval).
		Upvalue m_OpenUpvalues;


		public Processor(Script script, Table globalContext, ByteCode byteCode)
		{
			m_ValueStack = new ValueStack(INITIAL_VALUE_STACK_SIZE, STACK_SIZE);
			m_ExecutionStack = new CallStack(INITIAL_EXECUTION_STACK_SIZE, STACK_SIZE);
			m_CoroutinesStack = new List<Processor>();

			m_Debug = new DebugContext();
			m_RootChunk = byteCode;
			m_GlobalTable = globalContext;
			m_Script = script;
			m_State = CoroutineState.Main;
			DynValue.NewCoroutine(new Coroutine(this)); // creates an associated coroutine for the main processor
		}

		private Processor(Processor parentProcessor)
		{
			m_ValueStack = new ValueStack(INITIAL_VALUE_STACK_SIZE, STACK_SIZE);
			m_ExecutionStack = new CallStack(INITIAL_EXECUTION_STACK_SIZE, STACK_SIZE);
			m_Debug = parentProcessor.m_Debug;
			m_RootChunk = parentProcessor.m_RootChunk;
			m_GlobalTable = parentProcessor.m_GlobalTable;
			m_Script = parentProcessor.m_Script;
			m_Parent = parentProcessor;
			m_State = CoroutineState.NotStarted;
		}

		//Takes the value and execution stack from recycleProcessor
		internal Processor(Processor parentProcessor, Processor recycleProcessor)
		{
			m_ValueStack = recycleProcessor.m_ValueStack;
			m_ExecutionStack = recycleProcessor.m_ExecutionStack;

			m_Debug = parentProcessor.m_Debug;
			m_RootChunk = parentProcessor.m_RootChunk;
			m_GlobalTable = parentProcessor.m_GlobalTable;
			m_Script = parentProcessor.m_Script;
			m_Parent = parentProcessor;
			m_State = CoroutineState.NotStarted;
		}

		public DynValue Call(DynValue function, DynValue[] args)
		{
			List<Processor> coroutinesStack = m_Parent != null ? m_Parent.m_CoroutinesStack : this.m_CoroutinesStack;

			if (coroutinesStack.Count > 0 && coroutinesStack[coroutinesStack.Count - 1] != this)
				return coroutinesStack[coroutinesStack.Count - 1].Call(function, args);

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
					int entrypoint = PushClrToScriptStackFrame(CallStackItemFlags.CallEntryPoint, function, args);
					DynValue result = Processing_Loop(entrypoint);
					completed = true;
					return result;
				}
				finally
				{
					// On an error, unwinding stops at the entry point frame and leaves the function, its
					// arguments and whatever the failing instruction pushed. Drop them, or every failed call
					// shrinks the stack. A finally block, not catch and rethrow: see Processing_Loop.
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

		// pushes all what's required to perform a clr-to-script function call. function can be null if it's already
		// at vstack top.
		private int PushClrToScriptStackFrame(CallStackItemFlags flags, DynValue function, DynValue[] args)
		{
			if (function == null)
				function = m_ValueStack.Peek().ToDynValue();
			else
				m_ValueStack.Push(function);  // func val

			args = Internal_AdjustTuple(args);

			for (int i = 0; i < args.Length; i++)
				m_ValueStack.Push(args[i]);

			ref CallStackItem frame = ref m_ExecutionStack.Push();
			frame.BasePointer = m_ValueStack.Count;
			frame.ArgCount = args.Length;
			frame.Debug_EntryPoint = function.Function.EntryPointByteCodeLocation;
			frame.ReturnAddress = -1;
			frame.Function = function.Function;
			frame.Upvalues = function.Function.Upvalues;
			frame.CallingSourceRef = SourceRef.GetClrLocation();
			frame.Flags = flags;

			return function.Function.EntryPointByteCodeLocation;
		}


		int m_OwningThreadID = -1;
		int m_ExecutionNesting = 0;

		private void LeaveProcessor()
		{
			m_ExecutionNesting -= 1;
			m_Debug.EntryDepth -= 1;
			m_OwningThreadID = -1;

			if (m_Parent != null)
			{
				m_Parent.m_CoroutinesStack.RemoveAt(m_Parent.m_CoroutinesStack.Count - 1);
			}

			if (m_ExecutionNesting == 0 && m_Debug != null && m_Debug.DebuggerEnabled
				&& m_Debug.DebuggerAttached != null)
			{
				m_Debug.DebuggerAttached.SignalExecutionEnded();
			}
		}

		int GetThreadId()
		{
			return Thread.CurrentThread.ManagedThreadId;
		}

		private void EnterProcessor()
		{
			int threadID = GetThreadId();

			if (m_OwningThreadID >= 0 && m_OwningThreadID != threadID && m_Script.Options.CheckThreadAccess)
			{
				string msg = string.Format("Cannot enter the same MoonSharp processor from two different threads : {0} and {1}", m_OwningThreadID, threadID);
				throw new InvalidOperationException(msg);
			}

			m_OwningThreadID = threadID;

			m_ExecutionNesting += 1;

			// The host's outermost call into this Script starts a new instruction count; the countdown
			// at zero makes the first instruction read the interrupt handler and the debugger state.
			if (m_Debug.EntryDepth++ == 0)
			{
				m_Debug.Executed = m_Debug.Armed = m_Debug.Countdown = 0;
				m_Debug.NextInterruptCheck = INTERRUPT_INTERVAL;
			}

			if (m_Parent != null)
			{
				m_Parent.m_CoroutinesStack.Add(this);
			}
		}

		/// <summary>
		/// How deep C# may nest into the VM, the parser and json on one thread: Lua 5.2's LUAI_MAXCCALLS.
		/// A count, unlike the remaining stack size, gives the same answer on every host.
		/// </summary>
		internal const int MAX_NATIVE_NESTING = 200;

		[ThreadStatic]
		static int t_NativeNesting;

		// Lua calls do not nest on the native stack, but a CLR function that calls back into Lua does
		// (string.gsub with a function replacement, table.sort with a comparator, a C# __index): each level
		// runs its own Processing_Loop. Raise a script error while there is still stack to unwind and run
		// error handlers, instead of letting the runtime end the process with a StackOverflowException.
		// The stack probe covers hosts that run scripts on a thread with a small stack.
		private static void EnterNative()
		{
			if (t_NativeNesting >= MAX_NATIVE_NESTING)
				throw new ScriptRuntimeException("C stack overflow");

			try
			{
				RuntimeHelpers.EnsureSufficientExecutionStack();
			}
			catch (InsufficientExecutionStackException)
			{
				throw new ScriptRuntimeException("C stack overflow");
			}

			t_NativeNesting++;
		}

		/// <summary>
		/// Returns the open upvalue for the value stack slot at <paramref name="index"/>, creating it if no
		/// closure captured that slot yet, so closures that capture one variable share one upvalue.
		/// </summary>
		private Upvalue FindUpvalue(int index)
		{
			Upvalue previous = null;
			Upvalue current = m_OpenUpvalues;

			while (current != null && current.Index > index)
			{
				previous = current;
				current = current.NextOpen;
			}

			if (current != null && current.Index == index)
				return current;

			Upvalue created = new Upvalue(m_ValueStack, index) { NextOpen = current };

			if (previous == null)
				m_OpenUpvalues = created;
			else
				previous.NextOpen = created;

			return created;
		}

		/// <summary>
		/// Closes the open upvalues of the value stack slots at <paramref name="level"/> and above: their
		/// variables go out of scope, so the upvalues keep the values from now on.
		/// </summary>
		private void CloseUpvalues(int level)
		{
			Upvalue u = m_OpenUpvalues;

			while (u != null && u.Index >= level)
			{
				Upvalue next = u.NextOpen;
				u.Close();
				u.NextOpen = null;
				u = next;
			}

			m_OpenUpvalues = u;
		}

		internal SourceRef GetCoroutineSuspendedLocation()
		{
			return GetCurrentSourceRef(m_SavedInstructionPtr);
		}
	}
}
