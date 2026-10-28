using System;
using System.IO;
using MoonSharp.Interpreter.Platforms;

namespace MoonSharp.Interpreter.CoreLib.IO
{
	/// <summary>
	/// Stands in for a standard stream the platform cannot provide (for instance stdin on
	/// browser WebAssembly, where Console.OpenStandardInput throws PlatformNotSupportedException).
	/// Creating the stream always succeeds, so the io module can still be loaded; using it raises
	/// a ScriptRuntimeException that Lua code can catch with pcall.
	/// </summary>
	/// <remarks>
	/// It reports CanRead or CanWrite (as the file it replaces would), because StreamReader and
	/// StreamWriter refuse streams that lack the capability they wrap.
	/// </remarks>
	internal sealed class UnavailableStream : Stream
	{
		private readonly bool m_CanRead;
		private readonly string m_Message;

		public UnavailableStream(StandardFileType type)
		{
			m_CanRead = (type == StandardFileType.StdIn);
			m_Message = string.Format("{0} is not available on this platform", DescribeStandardFile(type));
		}

		private static string DescribeStandardFile(StandardFileType type)
		{
			switch (type)
			{
				case StandardFileType.StdIn: return "standard input";
				case StandardFileType.StdOut: return "standard output";
				default: return "standard error";
			}
		}

		public override bool CanRead { get { return m_CanRead; } }
		public override bool CanWrite { get { return !m_CanRead; } }
		public override bool CanSeek { get { return false; } }

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new ScriptRuntimeException(m_Message);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new ScriptRuntimeException(m_Message);
		}

		public override void Flush()
		{
			// A reader has nothing to flush; a writer reaches Flush only with buffered data
			// that could never be delivered, which has to be reported.
			if (!m_CanRead)
				throw new ScriptRuntimeException(m_Message);
		}

		public override long Length { get { throw new NotSupportedException(); } }
		public override long Position
		{
			get { throw new NotSupportedException(); }
			set { throw new NotSupportedException(); }
		}
		public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
		public override void SetLength(long value) { throw new NotSupportedException(); }
	}
}
