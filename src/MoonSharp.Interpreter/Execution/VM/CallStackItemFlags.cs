using System;

namespace MoonSharp.Interpreter.Execution.VM
{
	[Flags]
	internal enum CallStackItemFlags
	{
		None = 0,

		EntryPoint = 1,
		ResumeEntryPoint = 3,
		CallEntryPoint = 5,

		TailCall = 0x10,
		MethodCall = 0x20,

		// SPIKE (register VM): a register function called by a register function, in the register loop.
		RegisterCall = 0x40,
		// SPIKE (register VM): the frame runs register code; SavedPc locates it for error positions.
		RegisterFrame = 0x80,
	}
}
