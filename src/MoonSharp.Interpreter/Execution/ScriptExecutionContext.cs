using System;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Execution.VM;
using MoonSharp.Interpreter.Interop.LuaStateInterop;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Class giving access to details of the environment where the script is executing
	/// </summary>
	public class ScriptExecutionContext : IScriptPrivateResource
	{
		Processor m_Processor;
		CallbackFunction m_Callback;
		int StackFrameIndex;

		internal ScriptExecutionContext(Processor p, CallbackFunction callBackFunction, SourceRef sourceRef, bool isDynamic = false, int stackFrameIndex = -1)
		{
			IsDynamicExecution = isDynamic;
			m_Processor = p;
			m_Callback = callBackFunction;
			CallingLocation = sourceRef;
			StackFrameIndex = stackFrameIndex;
		}

		/// <summary>
		/// Prepares a context the processor reuses for every C# function call at one call depth.
		/// </summary>
		internal void Reuse(CallbackFunction callBackFunction, SourceRef sourceRef)
		{
			m_Callback = callBackFunction;
			CallingLocation = sourceRef;
		}

		// A context the processor lends to the C# functions it calls keeps their result here when they return
		// it with Return, and gives the processor ResultInContext instead of a new DynValue.
		internal bool LentByProcessor;
		LuaValue m_Result;

		/// <summary>
		/// What <see cref="Return(double)"/> and <see cref="Return(string)"/> give the processor: the result is
		/// in the context.
		/// </summary>
		internal static readonly DynValue ResultInContext = DynValue.NewNil();

		/// <summary>
		/// The result of the running callback, as <c>return context.Return(number);</c>: when the VM called the
		/// callback, the number goes onto the value stack without a new <see cref="DynValue"/>; otherwise (a call
		/// from C#) it is <see cref="DynValue.NewNumber"/>. Use it only as the callback's return value, and
		/// only from the callback this context was passed to: what it returns is a placeholder that is
		/// valid until the next call of Return on this context.
		/// </summary>
		public DynValue Return(double number)
		{
			if (!LentByProcessor)
				return DynValue.NewNumber(number);

			m_Result = LuaValue.FromNumber(number);
			return ResultInContext;
		}

		/// <summary>
		/// As <see cref="Return(double)"/>, for a string; null is nil.
		/// </summary>
		public DynValue Return(string str)
		{
			if (str == null)
				return DynValue.Nil;

			if (!LentByProcessor)
				return DynValue.NewString(str);

			m_Result = LuaValue.FromString(str);
			return ResultInContext;
		}

		/// <summary>
		/// <see cref="Return(double)"/> for any VM value.
		/// </summary>
		internal DynValue Return(LuaValue value)
		{
			if (!LentByProcessor)
				return value.ToDynValue();

			m_Result = value;
			return ResultInContext;
		}

		/// <summary>
		/// A context for <paramref name="callback"/>, called from the function this context belongs to (pcall,
		/// <see cref="Call"/>): the same processor and calling location, with the callee's own
		/// <see cref="AdditionalData"/>.
		/// </summary>
		internal ScriptExecutionContext For(CallbackFunction callback)
		{
			return new ScriptExecutionContext(m_Processor, callback, CallingLocation, IsDynamicExecution, StackFrameIndex);
		}

		/// <summary>
		/// This context, or a copy for code that may keep it after the call (a userdata method with a
		/// ScriptExecutionContext parameter): the processor lends a context it reuses for later calls. The
		/// copy is not lent, so Return in that code gives an ordinary DynValue.
		/// </summary>
		internal ScriptExecutionContext ToKeep()
		{
			return LentByProcessor ? new ScriptExecutionContext(m_Processor, m_Callback, CallingLocation, IsDynamicExecution, StackFrameIndex) : this;
		}

		/// <summary>
		/// The result a callback left with Return, as a VM value; the context lets go of it.
		/// </summary>
		internal LuaValue TakeResult()
		{
			LuaValue v = m_Result;
			m_Result = default(LuaValue);
			return v;
		}

		/// <summary>
		/// <paramref name="ret"/>, or the DynValue of the result a callback left with Return.
		/// </summary>
		internal DynValue Resolve(DynValue ret)
		{
			return ReferenceEquals(ret, ResultInContext) ? TakeResult().ToDynValue() : ret;
		}

		/// <summary>
		/// Gets a value indicating whether this instance is running a dynamic execution.
		/// Under a dynamic execution, most methods of ScriptExecutionContext are not reliable as the
		/// processing engine of the script is not "really" running or is not available.
		/// </summary>
		public bool IsDynamicExecution
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the location of the code calling back
		/// </summary>
		public SourceRef CallingLocation
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets or sets the additional data associated to this CLR function call.
		/// </summary>
		public object AdditionalData
		{
			get { return (m_Callback != null) ? m_Callback.AdditionalData : null; }
			set
			{
				if (m_Callback == null) throw new InvalidOperationException("Cannot set additional data on a context which has no callback");
				m_Callback.AdditionalData = value;
			}
		}


		/// <summary>
		/// Gets the metatable associated with the given value.
		/// </summary>
		/// <param name="value">The value.</param>
		/// <returns></returns>
		public Table GetMetatable(DynValue value)
		{
			return m_Processor.GetMetatable(value);
		}


		/// <summary>
		/// Gets the specified metamethod associated with the given value.
		/// </summary>
		/// <param name="value">The value.</param>
		/// <param name="metamethod">The metamethod name.</param>
		/// <returns></returns>
		public DynValue GetMetamethod(DynValue value, string metamethod)
		{
			return m_Processor.GetMetamethod(value, metamethod);
		}

		/// <summary>
		/// prepares a tail call request for the specified metamethod, or null if no metamethod is found.
		/// </summary>
		public DynValue GetMetamethodTailCall(DynValue value, string metamethod, params DynValue[] args)
		{
			DynValue meta = this.GetMetamethod(value, metamethod);
			if (meta == null) return null;
			return DynValue.NewTailCallReq(meta, args);
		}

		/// <summary>
		/// Gets the metamethod to be used for a binary operation using op1 and op2.
		/// </summary>
		public DynValue GetBinaryMetamethod(DynValue op1, DynValue op2, string eventName)
		{
			return m_Processor.GetBinaryMetamethod(op1, op2, eventName);
		}

		/// <summary>
		/// Gets the script object associated with this request
		/// </summary>
		/// <returns></returns>
		public Script GetScript()
		{
			return m_Processor.GetScript();
		}

		/// <summary>
		/// A request to yield the values of <paramref name="args"/> from the running coroutine (reused for every
		/// yield, and the values copied into a buffer the coroutine keeps).
		/// </summary>
		internal DynValue YieldRequestFor(CallbackArguments args)
		{
			return m_Processor.YieldRequestFor(args);
		}

		/// <summary>
		/// Reports <paramref name="units"/> steps of work done by a long library loop (pattern matching,
		/// sorting, concatenation), so that <see cref="ScriptOptions.InterruptHandler"/> can stop it.
		/// </summary>
		internal void LibraryWork(int units)
		{
			if (m_Processor != null)
				m_Processor.LibraryWork(units);
		}

		/// <summary>
		/// Gets the coroutine which is performing the call
		/// </summary>
		public Coroutine GetCallingCoroutine()
		{
			return m_Processor.AssociatedCoroutine;
		}

		/// <summary>
		/// Calls a callback function implemented in "classic way".
		/// Useful to port C code from Lua, or C# code from UniLua and KopiLua.
		/// Lua : http://www.lua.org/
		/// UniLua : http://github.com/xebecnan/UniLua
		/// KopiLua : http://github.com/NLua/KopiLua
		/// </summary>
		/// <param name="args">The arguments.</param>
		/// <param name="functionName">Name of the function - for error messages.</param>
		/// <param name="callback">The callback.</param>
		/// <returns></returns>
		public DynValue EmulateClassicCall(CallbackArguments args, string functionName, Func<LuaState, int> callback)
		{
			LuaState L = new LuaState(this, args, functionName);
			int retvals = callback(L);
			return L.GetReturnValue(retvals);
		}

		/// <summary>
		/// Calls the specified function, supporting most cases. The called function must not yield.
		/// </summary>
		/// <param name="func">The function; it must be a Function or ClrFunction or have a call metamethod defined.</param>
		/// <param name="args">The arguments.</param>
		/// <returns></returns>
		/// <exception cref="ScriptRuntimeException">If the function yields, returns a tail call request with continuations/handlers or, of course, if it encounters errors.</exception>
		public DynValue Call(DynValue func, params DynValue[] args)
		{
			if (func.Type == DataType.Function)
			{
				return this.GetScript().Call(func, args);
			}
			else if (func.Type == DataType.ClrFunction)
			{
				while (true)
				{
					DynValue ret = func.Callback.Invoke(For(func.Callback), args, false);

					if (ret.Type == DataType.YieldRequest)
					{
						throw ScriptRuntimeException.CannotYield();
					}
					else if (ret.Type == DataType.TailCallRequest)
					{
						var tail = ret.TailCallData;

						if (tail.Continuation != null || tail.ErrorHandler != null)
						{
							throw new ScriptRuntimeException("the function passed cannot be called directly. wrap in a script function instead.");
						}
						else
						{
							args = tail.Args;
							func = tail.Function;
						}
					}
					else
					{
						return ret;
					}
				}
			}
			else
			{
				int maxloops = 10;

				while (maxloops > 0)
				{
					DynValue v = this.GetMetamethod(func, "__call");

					if (v == null && v.IsNil())
					{
						throw ScriptRuntimeException.AttemptToCallNonFunc(func.Type);
					}

					func = v;

					if (func.Type == DataType.Function || func.Type == DataType.ClrFunction)
					{
						return Call(func, args);
					}
				}

				throw ScriptRuntimeException.LoopInCall();
			}
		}

		/// <summary>
		/// Tries to get the reference of a symbol in the current execution state
		/// </summary>
		public DynValue EvaluateSymbol(SymbolRef symref)
		{
			if (symref == null)
				return DynValue.Nil;

			return m_Processor.GetGenericSymbol(symref, StackFrameIndex);
		}

		/// <summary>
		/// Tries to get the value of a symbol in the current execution state
		/// </summary>
		public DynValue EvaluateSymbolByName(string symbol)
		{
			return this.EvaluateSymbol(this.FindSymbolByName(symbol));
		}

		/// <summary>
		/// Finds a symbol by name in the current execution state
		/// </summary>
		public SymbolRef FindSymbolByName(string symbol)
		{
			return m_Processor.FindSymbolByName(symbol, StackFrameIndex);
		}

		/// <summary>
		/// Gets the current global env, or null if not found.
		/// </summary>
		public Table CurrentGlobalEnv
		{
			get
			{
				DynValue env = EvaluateSymbolByName(WellKnownSymbols.ENV);

				if (env == null || env.Type != DataType.Table)
					return null;
				else return env.Table;
			}
		}

		/// <summary>
		/// Performs a message decoration before unwinding after an error. To be used in the implementation of xpcall like functions.
		/// </summary>
		/// <param name="messageHandler">The message handler.</param>
		/// <param name="exception">The exception.</param>
		public void PerformMessageDecorationBeforeUnwind(DynValue messageHandler, ScriptRuntimeException exception)
		{
			if (messageHandler != null)
				exception.DecoratedMessage = m_Processor.PerformMessageDecorationBeforeUnwind(messageHandler, exception.Message, CallingLocation);
			else
				exception.DecoratedMessage = exception.Message;
		}


		/// <summary>
		/// Gets the script owning this resource.
		/// </summary>
		/// <value>
		/// The script owning this resource.
		/// </value>
		public Script OwnerScript
		{
			get { return this.GetScript(); }
		}

	}
}
