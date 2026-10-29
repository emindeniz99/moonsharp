using System.IO;

namespace MoonSharp.Interpreter.CoreLib.IO
{
	/// <summary>
	/// Abstract class implementing a file Lua userdata. Methods are meant to be called by Lua code.
	/// </summary>
	internal abstract class StreamFileUserDataBase : FileUserDataBase
	{
		protected Stream m_Stream;
		protected StreamReader m_Reader;
		protected StreamWriter m_Writer;
		protected bool m_Closed = false;


		protected void Initialize(Stream stream, StreamReader reader, StreamWriter writer)
		{
			m_Stream = stream;
			m_Reader = reader;
			m_Writer = writer;
		}


		private void CheckFileIsNotClosed()
		{
			if (m_Closed)
				throw new ScriptRuntimeException("attempt to use a closed file");
		}


		protected override bool Eof()
		{
			CheckFileIsNotClosed();

			if (m_Reader != null)
				return m_Reader.EndOfStream;
			else
				return false;
		}

		protected override string ReadLine()
		{
			CheckFileIsNotClosed();
			return m_Reader.ReadLine();
		}

		protected override string ReadToEnd()
		{
			CheckFileIsNotClosed();
			return m_Reader.ReadToEnd();
		}

		protected override string ReadBuffer(int p)
		{
			CheckFileIsNotClosed();
			char[] buffer = new char[p];
			int length = m_Reader.ReadBlock(buffer, 0, p);
			return new string(buffer, 0, length);
		}

		protected override char Peek()
		{
			CheckFileIsNotClosed();
			return (char)m_Reader.Peek();
		}

		protected override void Write(string value)
		{
			CheckFileIsNotClosed();
			m_Writer.Write(value);
		}

		protected override string Close()
		{
			CheckFileIsNotClosed();

			if (m_Writer != null)
				m_Writer.Dispose();

			if (m_Reader != null)
				m_Reader.Dispose();

			m_Stream.Dispose();

			m_Closed = true;

			return null;
		}

		public override bool flush()
		{
			CheckFileIsNotClosed();

			if (m_Writer != null)
				m_Writer.Flush();

			return true;
		}

		public override DynValue seek(string whence, long offset = 0)
		{
			CheckFileIsNotClosed();
			if (whence != null)
			{
				SeekOrigin origin;

				if (whence == "set")
					origin = SeekOrigin.Begin;
				else if (whence == "cur")
					origin = SeekOrigin.Current;
				else if (whence == "end")
					origin = SeekOrigin.End;
				else
					throw ScriptRuntimeException.BadArgument(0, "seek", "invalid option '" + whence + "'");

				try
				{
					if (m_Writer != null)
						m_Writer.Flush();

					m_Stream.Seek(offset, origin);

					// The reader holds text read ahead from the old position.
					if (m_Reader != null)
						m_Reader.DiscardBufferedData();
				}
				catch (IOException)
				{
					// Before the start of the file: C's fseek fails with EINVAL, and Lua returns nil and the message.
					return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("Invalid argument"), DynValue.NewNumber(22));
				}
			}

			return DynValue.NewNumber(m_Stream.Position);
		}

		public override bool setvbuf(string mode)
		{
			CheckFileIsNotClosed();
			if (m_Writer != null)
				m_Writer.AutoFlush = (mode == "no" || mode == "line");
			return true;
		}

		protected internal override bool isopen()
		{
			return !m_Closed;
		}

		protected override bool CanRead
		{
			get { return m_Reader != null; }
		}

		protected override bool CanWrite
		{
			get { return m_Writer != null; }
		}

	}
}
