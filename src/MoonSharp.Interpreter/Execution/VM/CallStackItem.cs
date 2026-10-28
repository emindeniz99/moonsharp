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
		// is the index of the first local, just past the last argument. -1 for a C# function.
		public int BasePointer;
		public int ReturnAddress;
		public int ArgCount;
		public CallStackItemFlags Flags;

		// The running closure and its upvalues (the same array; kept here to save a load per access).
		public Closure Function;
		public Upvalue[] Upvalues;

		public CallbackFunction Continuation { get { return Handlers != null ? Handlers.Continuation : null; } }
		public CallbackFunction ErrorHandler { get { return Handlers != null ? Handlers.ErrorHandler : null; } }
		public DynValue ErrorHandlerBeforeUnwind { get { return Handlers != null ? Handlers.ErrorHandlerBeforeUnwind : null; } }
	}

	/// <summary>
	/// The rarely set parts of a call frame: a continuation (a C# function that receives the callee's
	/// result) and the error handlers of pcall and xpcall.
	/// </summary>
	internal sealed class CallStackItemHandlers
	{
		public CallbackFunction Continuation;
		public CallbackFunction ErrorHandler;
		public DynValue ErrorHandlerBeforeUnwind;

		/// <summary>
		/// Returns the handlers object for a frame, or null when none of them is set.
		/// </summary>
		public static CallStackItemHandlers Create(CallbackFunction continuation, CallbackFunction errorHandler, DynValue errorHandlerBeforeUnwind)
		{
			if (continuation == null && errorHandler == null && errorHandlerBeforeUnwind == null)
				return null;

			return new CallStackItemHandlers()
			{
				Continuation = continuation,
				ErrorHandler = errorHandler,
				ErrorHandlerBeforeUnwind = errorHandlerBeforeUnwind,
			};
		}
	}

}
