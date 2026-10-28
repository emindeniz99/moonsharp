using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// One call frame. A struct stored in <see cref="CallStack"/> and used through ref, so a call allocates
	/// no frame object. Fields that only a few calls set (pcall/xpcall handlers, continuations) live in a
	/// side object, which keeps the struct small for every other call.
	/// </summary>
	internal struct CallStackItem
	{
		public int Debug_EntryPoint;
		public SymbolRef[] Debug_Symbols;

		public SourceRef CallingSourceRef;

		public CallbackFunction ClrFunction;
		public CallStackItemHandlers Handlers;

		// For a Lua function: the value stack holds the function, then ArgCount arguments, then the
		// function's locals (BeginFn reserves one slot per local symbol), then its temporaries. BasePointer
		// is the index of the first local, just past the last argument. -1 for a C# function, whose
		// function and ArgCount arguments are the top of the value stack while it runs; once it has
		// returned a tail call or a yield, and waits for its result (a pending frame), ArgCount is -1.
		public int BasePointer;
		public int ReturnAddress;
		public int ArgCount;
		public CallStackItemFlags Flags;

		// Metamethod calls nested below and in this frame since the host entered the VM: Lua's nCcalls,
		// which Lua raises for each metamethod call. Normal calls keep the caller's count.
		public int CCalls;

		// SPIKE (register VM): a register frame's next pc while it waits for a call or a slow path.
		public int SavedPc;

		// The running closure and its upvalues (the same array; kept here to save a load per access).
		public Closure Function;
		public Upvalue[] Upvalues;

		public CallbackFunction Continuation { get { return Handlers != null ? Handlers.Continuation : null; } }
		public CallbackFunction ErrorHandler { get { return Handlers != null ? Handlers.ErrorHandler : null; } }
		public DynValue ErrorHandlerBeforeUnwind { get { return Handlers != null ? Handlers.ErrorHandlerBeforeUnwind : null; } }
	}

	/// <summary>
	/// The rarely set parts of a call frame: a continuation (a C# function that receives the callee's
	/// result), the error handlers of pcall and xpcall, and what happens to the result on return. A frame
	/// with handlers returns through the slow path of ExecRet.
	/// </summary>
	internal sealed class CallStackItemHandlers
	{
		public CallbackFunction Continuation;
		public CallbackFunction ErrorHandler;
		public DynValue ErrorHandlerBeforeUnwind;

		// A metamethod call: its result is adjusted to one value, as Lua does (t.x is nil, not nothing,
		// when __index returns nothing).
		public bool Metamethod;

		// The frame below is a pending C# frame (see CallStackItem.ArgCount): this call's result is its
		// result, so it returns next.
		public bool ReturnsToPending;

		static readonly CallStackItemHandlers s_Metamethod = new CallStackItemHandlers() { Metamethod = true };

		/// <summary>
		/// Returns the handlers object for a frame, or null when none of them is set.
		/// </summary>
		public static CallStackItemHandlers Create(CallbackFunction continuation, CallbackFunction errorHandler, DynValue errorHandlerBeforeUnwind,
			bool metamethod = false, bool returnsToPending = false)
		{
			if (continuation == null && errorHandler == null && errorHandlerBeforeUnwind == null && !returnsToPending)
				return metamethod ? s_Metamethod : null;

			return new CallStackItemHandlers()
			{
				Continuation = continuation,
				ErrorHandler = errorHandler,
				ErrorHandlerBeforeUnwind = errorHandlerBeforeUnwind,
				Metamethod = metamethod,
				ReturnsToPending = returnsToPending,
			};
		}
	}

}
