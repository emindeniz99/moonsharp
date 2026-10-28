using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	internal class CallStackItem
	{
		public int Debug_EntryPoint;
		public SymbolRef[] Debug_Symbols;

		public SourceRef CallingSourceRef;

		public CallbackFunction ClrFunction;
		public CallbackFunction Continuation;
		public CallbackFunction ErrorHandler;
		public DynValue ErrorHandlerBeforeUnwind;

		// For a Lua function: the value stack holds the function, then ArgCount arguments, and BasePointer is
		// the index just past the last argument (where the callee's own values start).
		public int BasePointer;
		public int ReturnAddress;
		public DynValue[] LocalScope;
		public ClosureContext ClosureScope;

		// Flags in the low 8 bits, the argument count above them. A separate int field would grow every
		// frame from 96 to 104 bytes (object sizes round up to 8), including the frames of CLR calls,
		// which do not use the count. The count is at most the value stack size (131072), so it fits.
		int m_FlagsAndArgCount;

		public CallStackItemFlags Flags
		{
			get { return (CallStackItemFlags)(m_FlagsAndArgCount & 0xFF); }
			set { m_FlagsAndArgCount = (m_FlagsAndArgCount & ~0xFF) | ((int)value & 0xFF); }
		}

		public int ArgCount
		{
			get { return m_FlagsAndArgCount >> 8; }
			set { m_FlagsAndArgCount = (m_FlagsAndArgCount & 0xFF) | (value << 8); }
		}
	}

}
