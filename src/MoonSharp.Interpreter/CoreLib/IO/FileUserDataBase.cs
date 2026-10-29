using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MoonSharp.Interpreter.Compatibility;

namespace MoonSharp.Interpreter.CoreLib.IO
{
	/// <summary>
	/// Abstract class implementing a file Lua userdata. Methods are meant to be called by Lua code.
	/// </summary>
	internal abstract class FileUserDataBase : RefIdObject
	{
		public DynValue lines(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return LinesIterator(args, 0, false);
		}

		/// <summary>
		/// The iterator of file:lines and io.lines, as Lua 5.2's io_readline: each call reads with the formats
		/// in <paramref name="args"/> from <paramref name="firstFormat"/> on (a line without any), and returns
		/// nothing at the end of the file, closing it if <paramref name="closeAtEnd"/>. It reads as it goes, so
		/// a file that does not end (or a "*a" format, which never returns nil) does not fill the memory.
		/// </summary>
		internal DynValue LinesIterator(CallbackArguments args, int firstFormat, bool closeAtEnd)
		{
			var formats = new List<DynValue>();

			for (int i = firstFormat; i < args.Count; i++)
				formats.Add(args[i]);

			return DynValue.NewCallback((ctx, a) =>
			{
				if (!isopen())
					throw new ScriptRuntimeException("file is already closed");

				DynValue r = read(ctx, new CallbackArguments(formats, false));
				DynValue first = r.Type == DataType.Tuple ? (r.Tuple.Length > 0 ? r.Tuple[0] : DynValue.Nil) : r;

				if (first.IsNotNil())
					return r;

				if (closeAtEnd)
					Close();

				return DynValue.Void;
			}, "lines");
		}

		// What C's stdio gives for a read from a file opened only for writing, or the reverse: EBADF.
		static DynValue BadFileDescriptor()
		{
			return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("Bad file descriptor"), DynValue.NewNumber(9));
		}

		public DynValue read(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			// The reader of a write-only file is null: a NullReferenceException ended the host.
			if (isopen() && !CanRead)
				return BadFileDescriptor();

			if (args.Count == 0)
			{
				string str = ReadLine();

				if (str == null)
					return DynValue.Nil;

				str = str.TrimEnd('\n', '\r');
				return DynValue.NewString(str);
			}
			else
			{
				List<DynValue> rets = new List<DynValue>();

				for (int i = 0; i < args.Count; i++)
				{
					DynValue v;

					if (args[i].Type == DataType.Number)
					{
						if (Eof())
							return DynValue.Nil;

						double howmany = args[i].Number;

						// Lua 5.2 reads up to (size_t)n characters: a negative count reads the rest of the file.
						string str = howmany >= 0 && howmany <= int.MaxValue ? ReadBuffer((int)howmany) : ReadToEnd();
						v = DynValue.NewString(str);
					}
					else 
					{
						string opt = args.AsType(i, "read", DataType.String, false).String;

						if (Eof())
						{
							v = opt.StartsWith("*a") ? DynValue.NewString("") : DynValue.Nil;
						}
						else if (opt.StartsWith("*n"))
						{
							double? d = ReadNumber();

							if (d.HasValue)
								v = DynValue.NewNumber(d.Value);
							else
								v = DynValue.Nil;
						}
						else if (opt.StartsWith("*a"))
						{
							string str = ReadToEnd();
							v = DynValue.NewString(str);
						}
						else if (opt.StartsWith("*l"))
						{
							string str = ReadLine();
							str = str.TrimEnd('\n', '\r');
							v = DynValue.NewString(str);
						}
						else if (opt.StartsWith("*L"))
						{
							string str = ReadLine();

							str = str.TrimEnd('\n', '\r');
							str += "\n";
							
							v = DynValue.NewString(str);
						}
						else
						{
							throw ScriptRuntimeException.BadArgument(i, "read", "invalid option");
						}
					}

					rets.Add(v);
				}

				return DynValue.NewTuple(rets.ToArray());
			}
		}


		public DynValue write(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			if (isopen() && !CanWrite)
				return BadFileDescriptor();

			try
			{
				for (int i = 0; i < args.Count; i++)
				{
					//string str = args.AsStringUsingMeta(executionContext, i, "file:write");
					string str = args.AsType(i, "write", DataType.String, false).String;
					Write(str);
				}

				return UserData.Create(this);
			}
			catch (ScriptRuntimeException)
			{
				throw;
			}
			catch (Exception ex)
			{
				return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(ex.Message));
			}
		}

		public DynValue close(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			try
			{
				string msg = Close();
				if (msg == null)
					return DynValue.True;
				else
					return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(msg));
			}
			catch (ScriptRuntimeException)
			{
				throw;
			}
			catch (Exception ex)
			{
				return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(ex.Message));
			}
		}


		double? ReadNumber()
		{
			string chr = "";

			while (!Eof())
			{
				char c = Peek();
				if (char.IsWhiteSpace(c))
				{
					ReadBuffer(1);
				}
				else if (IsNumericChar(c, chr))
				{
					ReadBuffer(1);
					chr += c;
				}
				else break;
			}

			double d;

			if (double.TryParse(chr, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
			{
				return d;
			}
			else
			{
				return null;
			}
		}

		private bool IsNumericChar(char c, string numAsFar)
		{
			if (char.IsDigit(c))
				return true;

			if (c == '-')
				return numAsFar.Length == 0;

			if (c == '.')
				return !Framework.Do.StringContainsChar(numAsFar, '.');

			if (c == 'E' || c == 'e')
				return !(Framework.Do.StringContainsChar(numAsFar, 'E') || Framework.Do.StringContainsChar(numAsFar, 'e'));

			return false;
		}

		protected abstract bool Eof();
		protected abstract string ReadLine();
		protected abstract string ReadBuffer(int p);
		protected abstract string ReadToEnd();
		protected abstract char Peek();
		protected abstract void Write(string value);


		protected internal abstract bool isopen();
		protected abstract bool CanRead { get; }
		protected abstract bool CanWrite { get; }
		protected abstract string Close();

		public abstract bool flush();
		public abstract DynValue seek(string whence, long offset = 0);
		public abstract bool setvbuf(string mode);

		public override string ToString()
		{
			if (isopen())
				return string.Format("file ({0:X8})", base.ReferenceID);
			else
				return "file (closed)";
		}
	}
}
