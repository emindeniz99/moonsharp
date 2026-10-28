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

			// A chunk starts with instructions that have no position (BeginFn, then _ENV set up). An error
			// there, the value stack overflowing as a chunk starts (a cyclic require), is reported at the
			// chunk's first statement instead of as "bytecode:<n>".
			if (sref == null && ip >= 0 && ip < m_RootChunk.Code.Count && m_RootChunk.Code[ip].OpCode == OpCode.BeginFn)
			{
				for (int i = ip + 1; sref == null && i < m_RootChunk.Code.Count && m_RootChunk.Code[i].OpCode != OpCode.Ret; i++)
					sref = m_RootChunk.Code[i].SourceCodeRef;
			}

			ex.DecorateMessage(m_Script, sref, ip);

			// The innermost level sees every frame; outer levels see what is left after the inner ones
			// unwound. Building the list again at each of N nested levels costs O(N^2).
			if (ex.CallStack == null)
				ex.CallStack = Debugger_GetCallStack(sref);
		}


	}
}
