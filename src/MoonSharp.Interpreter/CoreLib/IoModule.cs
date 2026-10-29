// Disable warnings about XML documentation
#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter.Compatibility;
using MoonSharp.Interpreter.CoreLib.IO;
using MoonSharp.Interpreter.Platforms;

namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing io Lua functions. Proper support requires a compatible IPlatformAccessor
	/// </summary>
	[MoonSharpModule(Namespace = "io")]
	public class IoModule
	{
		public static void MoonSharpInit(Table globalTable, Table ioTable)
		{
			UserData.RegisterType<FileUserDataBase>(InteropAccessMode.Default, "file");

			Table meta = new Table(ioTable.OwnerScript);
			DynValue __index = CallbackFunction.NewInternal(__index_callback, "__index_callback");
			meta.Set("__index", __index);
			ioTable.MetaTable = meta;

			SetStandardFile(globalTable.OwnerScript, StandardFileType.StdIn, GetOptionsStream(globalTable.OwnerScript, StandardFileType.StdIn));
			SetStandardFile(globalTable.OwnerScript, StandardFileType.StdOut, GetOptionsStream(globalTable.OwnerScript, StandardFileType.StdOut));
			SetStandardFile(globalTable.OwnerScript, StandardFileType.StdErr, GetOptionsStream(globalTable.OwnerScript, StandardFileType.StdErr));
		}

		private static DynValue __index_callback(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			string name = args[1].CastToString();

			if (name == "stdin")
				return GetStandardFile(executionContext.GetScript(), StandardFileType.StdIn);
			else if (name == "stdout")
				return GetStandardFile(executionContext.GetScript(), StandardFileType.StdOut);
			else if (name == "stderr")
				return GetStandardFile(executionContext.GetScript(), StandardFileType.StdErr);
			else
				return DynValue.Nil;
		}

		private static DynValue GetStandardFile(Script S, StandardFileType file)
		{
			Table R = S.Registry;

			DynValue ff = R.Get("853BEAAF298648839E2C99D005E1DF94_STD_" + file.ToString());

			// The host may set Script.Options.Stdin/Stdout/Stderr after the script was created.
			Stream optionsStream = GetOptionsStream(S, file);

			if (optionsStream != null && !ff.CheckUserDataType<StandardIOFileUserDataBase>("getstandardfile").Wraps(optionsStream))
			{
				SetStandardFile(S, file, optionsStream);
				ff = R.Get("853BEAAF298648839E2C99D005E1DF94_STD_" + file.ToString());
			}

			return ff;
		}

		private static Stream GetOptionsStream(Script S, StandardFileType file)
		{
			switch (file)
			{
				case StandardFileType.StdIn: return S.Options.Stdin;
				case StandardFileType.StdOut: return S.Options.Stdout;
				default: return S.Options.Stderr;
			}
		}

		private static Stream GetPlatformStandardStream(StandardFileType file)
		{
			try
			{
				return Script.GlobalOptions.Platform.IO_GetStandardStream(file);
			}
			catch (PlatformNotSupportedException)
			{
				// E.g. stdin on browser WebAssembly. Creating a script must not fail because
				// of it: the stream is replaced by one that fails only when it is used.
				return new UnavailableStream(file);
			}
		}

		private static void SetStandardFile(Script S, StandardFileType file, Stream optionsStream)
		{
			Table R = S.Registry;

			optionsStream = optionsStream ?? GetPlatformStandardStream(file);

			FileUserDataBase udb = null;

			if (file == StandardFileType.StdIn)
				udb = StandardIOFileUserDataBase.CreateInputStream(optionsStream);
			else
				udb = StandardIOFileUserDataBase.CreateOutputStream(optionsStream);

			R.Set("853BEAAF298648839E2C99D005E1DF94_STD_" + file.ToString(), UserData.Create(udb));
		}


		static FileUserDataBase GetDefaultFile(ScriptExecutionContext executionContext, StandardFileType file)
		{
			Table R = executionContext.GetScript().Registry;

			DynValue ff = R.Get("853BEAAF298648839E2C99D005E1DF94_" + file.ToString());

			if (ff.IsNil())
			{
				ff = GetStandardFile(executionContext.GetScript(), file);
			}

			return ff.CheckUserDataType<FileUserDataBase>("getdefaultfile(" + file.ToString() + ")");
		}


		static void SetDefaultFile(ScriptExecutionContext executionContext, StandardFileType file, FileUserDataBase fileHandle)
		{
			SetDefaultFile(executionContext.GetScript(), file, fileHandle);
		}

		internal static void SetDefaultFile(Script script, StandardFileType file, FileUserDataBase fileHandle)
		{
			Table R = script.Registry;
			R.Set("853BEAAF298648839E2C99D005E1DF94_" + file.ToString(), UserData.Create(fileHandle));
		}

		public static void SetDefaultFile(Script script, StandardFileType file, Stream stream)
		{
			if (file == StandardFileType.StdIn)
				SetDefaultFile(script, file, StandardIOFileUserDataBase.CreateInputStream(stream));
			else
				SetDefaultFile(script, file, StandardIOFileUserDataBase.CreateOutputStream(stream));
		}


		[MoonSharpModuleMethod]
		public static DynValue close(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			FileUserDataBase outp = args.AsUserData<FileUserDataBase>(0, "close", true) ?? GetDefaultFile(executionContext, StandardFileType.StdOut);
			return outp.close(executionContext, args);
		}

		[MoonSharpModuleMethod]
		public static DynValue flush(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			FileUserDataBase outp = args.AsUserData<FileUserDataBase>(0, "close", true) ?? GetDefaultFile(executionContext, StandardFileType.StdOut);
			outp.flush();
			return DynValue.True;
		}


		[MoonSharpModuleMethod]
		public static DynValue input(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return HandleDefaultStreamSetter(executionContext, args, StandardFileType.StdIn);
		}

		[MoonSharpModuleMethod]
		public static DynValue output(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return HandleDefaultStreamSetter(executionContext, args, StandardFileType.StdOut);
		}

		private static DynValue HandleDefaultStreamSetter(ScriptExecutionContext executionContext, CallbackArguments args, StandardFileType defaultFiles)
		{
			if (args.Count == 0 || args[0].IsNil())
			{
				var file = GetDefaultFile(executionContext, defaultFiles);
				return UserData.Create(file);
			}

			FileUserDataBase inp = null;

			if (args[0].Type == DataType.String || args[0].Type == DataType.Number)
			{
				string fileName = args[0].CastToString();
				inp = OpenOrRaise(executionContext, fileName, defaultFiles == StandardFileType.StdIn ? "r" : "w");
			}
			else
			{
				inp = args.AsUserData<FileUserDataBase>(0, defaultFiles == StandardFileType.StdIn ? "input" : "output", false);
			}

			SetDefaultFile(executionContext, defaultFiles, inp);

			return UserData.Create(inp);
		}

		private static Encoding GetUTF8Encoding()
		{
			return new System.Text.UTF8Encoding(false); 
		}

		// io.lines ([filename, ...]): an iterator over the file's lines (or the given read formats) that closes the
		// file at its end; without a file name, over the default input, which stays open.
		[MoonSharpModuleMethod]
		public static DynValue lines(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			if (args.Count == 0 || args[0].IsNil())
				return GetDefaultFile(executionContext, StandardFileType.StdIn).LinesIterator(args, 1, false);

			string filename = args.AsType(0, "lines", DataType.String, false).String;
			return OpenOrRaise(executionContext, filename, "r").LinesIterator(args, 1, true);
		}

		// What io.input, io.output and io.lines raise when a file does not open, as Lua 5.2's opencheckfile.
		private static FileUserDataBase OpenOrRaise(ScriptExecutionContext executionContext, string filename, string mode)
		{
			try
			{
				return Open(executionContext, filename, GetUTF8Encoding(), mode);
			}
			catch (Exception ex) when (!(ex is InterpreterException))
			{
				throw new ScriptRuntimeException("cannot open file '{0}' ({1})", filename, IoErrorText(ex));
			}
		}

		// Lua 5.2's l_checkmode: one of r, w, a, then an optional '+', then only 'b's.
		private static bool IsValidMode(string mode)
		{
			int i = 0;

			if (mode.Length == 0 || "rwa".IndexOf(mode[i++]) < 0)
				return false;

			if (i < mode.Length && mode[i] == '+')
				i++;

			while (i < mode.Length && mode[i] == 'b')
				i++;

			return i == mode.Length;
		}

		[MoonSharpModuleMethod]
		public static DynValue open(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			string filename = args.AsType(0, "open", DataType.String, false).String;
			DynValue vmode = args.AsType(1, "open", DataType.String, true);
			DynValue vencoding = args.AsType(2, "open", DataType.String, true);

			string mode = vmode.IsNil() ? "r" : vmode.String;

			if (!IsValidMode(mode))
				throw ScriptRuntimeException.BadArgument(1, "open", "invalid mode");


			try
			{
				string encoding = vencoding.IsNil() ? null : vencoding.String;

				// list of codes: http://msdn.microsoft.com/en-us/library/vstudio/system.text.encoding%28v=vs.90%29.aspx.
				// In addition, "binary" is available.
				Encoding e = null;
				bool isBinary = Framework.Do.StringContainsChar(mode, 'b');

				if (encoding == "binary")
				{
					isBinary = true;
					e = new BinaryEncoding();
				}
				else if (encoding == null)
				{
					if (!isBinary) e = GetUTF8Encoding();
					else e = new BinaryEncoding();
				}
				else
				{
					if (isBinary)
						throw new ScriptRuntimeException("Can't specify encodings other than nil or 'binary' for binary streams.");

					e = Encoding.GetEncoding(encoding);
				}

				return UserData.Create(Open(executionContext, filename, e, mode));
			}
			catch (Exception ex)
			{
				return DynValue.NewTuple(DynValue.Nil,
					DynValue.NewString(IoExceptionToLuaMessage(ex, filename)));
			}

		}

		public static string IoExceptionToLuaMessage(Exception ex, string filename)
		{
			return filename + ": " + IoErrorText(ex);
		}

		/// <summary>
		/// The text C's strerror gives for what a file name can go wrong with; other exceptions keep their message.
		/// </summary>
		internal static string IoErrorText(Exception ex)
		{
			// An empty or malformed name is a name no file has.
			if (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is ArgumentException)
				return "No such file or directory";
			else if (ex is UnauthorizedAccessException)
				return "Permission denied";
			else
				return ex.Message;
		}

		[MoonSharpModuleMethod]
		public static DynValue type(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			if (args[0].Type != DataType.UserData)
				return DynValue.Nil;

			FileUserDataBase file = args[0].UserData.Object as FileUserDataBase;

			if (file == null)
				return DynValue.Nil;
			else if (file.isopen())
				return DynValue.NewString("file");
			else
				return DynValue.NewString("closed file");
		}

		[MoonSharpModuleMethod]
		public static DynValue read(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			FileUserDataBase file = GetDefaultFile(executionContext, StandardFileType.StdIn);
			return file.read(executionContext, args);
		}

		[MoonSharpModuleMethod]
		public static DynValue write(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			FileUserDataBase file = GetDefaultFile(executionContext, StandardFileType.StdOut);
			return file.write(executionContext, args);
		}

		[MoonSharpModuleMethod]
		public static DynValue tmpfile(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			string tmpfilename = Script.GlobalOptions.Platform.IO_OS_GetTempFilename();
			FileUserDataBase file = Open(executionContext, tmpfilename, GetUTF8Encoding(), "w");
			return UserData.Create(file);
		}

		private static FileUserDataBase Open(ScriptExecutionContext executionContext, string filename, Encoding encoding, string mode)
		{
			return new FileUserData(executionContext.GetScript(), filename, encoding, mode);
		}


	}

}
