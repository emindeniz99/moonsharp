using System.IO;

namespace MoonSharp.Interpreter.CoreLib.IO
{
	/// <summary>
	/// Abstract class implementing an unclosable file Lua userdata. Methods are meant to be called by Lua code.
	/// </summary>
	internal class StandardIOFileUserDataBase : StreamFileUserDataBase
	{
		internal bool Wraps(Stream stream)
		{
			return m_Stream == stream;
		}

		protected override string Close()
		{
			return ("cannot close standard file");
		}

		public static StandardIOFileUserDataBase CreateInputStream(Stream stream)
		{
			var f = new StandardIOFileUserDataBase();
			f.Initialize(stream, new StreamReader(stream), null);
			return f;
		}

		public static StandardIOFileUserDataBase CreateOutputStream(Stream stream)
		{
			var f = new StandardIOFileUserDataBase();
			// Unbuffered, as C's stderr and an interactive stdout: nothing flushes a .NET writer
			// when the script or the process ends. io.stdout:setvbuf("full") buffers again.
			f.Initialize(stream, null, new StreamWriter(stream) { AutoFlush = true });
			return f;
		}

	}

}
