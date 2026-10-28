using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		private SourceRef GetCurrentSourceRef(int instructionPtr)
		{
			if (instructionPtr >= 0 && instructionPtr < m_RootChunk.Code.Count)
			{
				return m_RootChunk.Code[instructionPtr].SourceCodeRef;
			}
			return null;
		}


		private void FillDebugData(InterpreterException ex, int ip)
		{
			// adjust IP
			if (ip == YIELD_SPECIAL_TRAP)
				ip = m_SavedInstructionPtr;
			else
				ip -= 1;

			ex.InstructionPtr = ip;

			SourceRef sref = GetCurrentSourceRef(ip);

			ex.DecorateMessage(m_Script, sref, ip);

			// The innermost level sees every frame; outer levels see what is left after the inner ones
			// unwound. Building the list again at each of N nested levels costs O(N^2).
			if (ex.CallStack == null)
				ex.CallStack = Debugger_GetCallStack(sref);
		}


	}
}
