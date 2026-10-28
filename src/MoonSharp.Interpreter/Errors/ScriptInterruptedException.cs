namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Thrown when <see cref="ScriptOptions.InterruptHandler"/> stops a running script. It is not a
	/// <see cref="ScriptRuntimeException"/>, so pcall, xpcall and coroutine.resume cannot catch it: the script
	/// unwinds to the host. The Script stays usable for later calls.
	/// </summary>
	public class ScriptInterruptedException : InterpreterException
	{
		internal ScriptInterruptedException(long instructions)
			: base("script interrupted by the host after {0} instructions", instructions)
		{
			Instructions = instructions;
		}

		/// <summary>
		/// Gets the number of instructions the script had run in the interrupted call.
		/// </summary>
		public long Instructions { get; private set; }
	}
}
