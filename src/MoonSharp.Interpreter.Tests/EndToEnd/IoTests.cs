using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MoonSharp.Interpreter.Platforms;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Fork tests for the io library. The TestMore io suites are skipped by the runner, so
	/// anything the io module must keep doing is pinned here.
	/// </summary>
	[TestFixture]
	public class IoTests
	{
		/// <summary>
		/// A German-style culture (comma decimal separator, dot group separator) built by hand,
		/// so the test means the same thing on a machine without ICU or with invariant globalization.
		/// </summary>
		private static CultureInfo GermanLikeCulture()
		{
			CultureInfo culture = (CultureInfo)new CultureInfo("de-DE").Clone();
			culture.NumberFormat.NumberDecimalSeparator = ",";
			culture.NumberFormat.NumberGroupSeparator = ".";
			return culture;
		}

		private static DynValue ReadFromFile(string content, string readCode)
		{
			string path = Path.GetTempFileName();

			try
			{
				File.WriteAllText(path, content);

				Script script = new Script(CoreModules.Preset_Complete);
				script.Globals.Set("path", DynValue.NewString(path));

				return script.DoString(
					"local f = assert(io.open(path, 'r'))\n" +
					"local a, b = " + readCode + "\n" +
					"f:close()\n" +
					"return a, b");
			}
			finally
			{
				File.Delete(path);
			}
		}

		[Test]
		public void Read_Number_IgnoresCurrentCulture()
		{
			// io.read('*n') parsed with the thread's culture, so under de-DE "1.5" was read as
			// 15 (the dot is a group separator there) and "1,5" was accepted as 1.5. A Lua
			// number in a file is written with a dot whatever the host's locale is.
			CultureInfo saved = Thread.CurrentThread.CurrentCulture;

			try
			{
				Thread.CurrentThread.CurrentCulture = GermanLikeCulture();

				Assert.AreEqual("1,5", (1.5).ToString(), "precondition: the test culture must use a decimal comma");

				DynValue r = ReadFromFile("1.5 -2.5e1", "f:read('*n'), f:read('*n')");

				Assert.AreEqual(1.5, r.Tuple[0].Number);
				Assert.AreEqual(-25.0, r.Tuple[1].Number);
			}
			finally
			{
				Thread.CurrentThread.CurrentCulture = saved;
			}
		}

		/// <summary>
		/// A platform like browser WebAssembly: asking for stdin throws PlatformNotSupportedException.
		/// </summary>
		private class NoStdinPlatform : StandardPlatformAccessor
		{
			public readonly MemoryStream Stdout = new MemoryStream();
			public bool StdoutAndStderrAlsoUnsupported;

			public override Stream IO_GetStandardStream(StandardFileType type)
			{
				if (type == StandardFileType.StdIn || StdoutAndStderrAlsoUnsupported)
					throw new PlatformNotSupportedException();

				return type == StandardFileType.StdOut ? Stdout : new MemoryStream();
			}
		}

		private static void WithPlatform(IPlatformAccessor platform, Action action)
		{
			IPlatformAccessor saved = Script.GlobalOptions.Platform;

			try
			{
				Script.GlobalOptions.Platform = platform;
				action();
			}
			finally
			{
				Script.GlobalOptions.Platform = saved;
			}
		}

		[Test]
		public void Create_WhenStandardStreamsAreUnsupported_StillBuildsTheIoLibrary()
		{
			// On browser WebAssembly Console.OpenStandardInput throws PlatformNotSupportedException,
			// which made new Script(CoreModules.Preset_Complete) fail while loading the io module.
			NoStdinPlatform platform = new NoStdinPlatform { StdoutAndStderrAlsoUnsupported = true };

			WithPlatform(platform, () =>
			{
				Script script = new Script(CoreModules.Preset_Complete);

				Assert.AreEqual(DataType.Table, script.Globals.Get("io").Type);
				Assert.AreEqual("function", script.DoString("return type(io.read)").String);
				Assert.AreEqual(DataType.UserData, script.DoString("return io.stdin").Type);
				Assert.AreEqual(DataType.UserData, script.DoString("return io.stdout").Type);
				Assert.AreEqual(DataType.UserData, script.DoString("return io.stderr").Type);
			});
		}

		[Test]
		public void Read_FromUnsupportedStdin_FailsAtCallTimeWithClearMessage()
		{
			WithPlatform(new NoStdinPlatform(), () =>
			{
				Script script = new Script(CoreModules.Preset_Complete);

				ScriptRuntimeException ex = Assert.Throws<ScriptRuntimeException>(() => script.DoString("return io.read()"));
				StringAssert.Contains("standard input is not available", ex.Message);

				// Lua code can handle it like any other error.
				DynValue r = script.DoString("local ok, err = pcall(io.read, '*l'); return ok, err");
				Assert.IsFalse(r.Tuple[0].Boolean);
				StringAssert.Contains("standard input is not available", r.Tuple[1].String);
			});
		}

		[Test]
		public void Write_WhenOnlyStdinIsUnsupported_StillReachesStdout()
		{
			NoStdinPlatform platform = new NoStdinPlatform();

			WithPlatform(platform, () =>
			{
				Script script = new Script(CoreModules.Preset_Complete);
				script.DoString("io.write('hello'); io.flush()");

				Assert.AreEqual("hello", Encoding.UTF8.GetString(platform.Stdout.ToArray()));
			});
		}
	}
}
