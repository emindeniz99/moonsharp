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
				DynValue result = null;

				// A full platform, because the default one on Unity leaves the io module out.
				WithPlatform(new StandardPlatformAccessor(), () =>
				{
					Script script = new Script(CoreModules.Preset_Complete);
					script.Globals.Set("path", DynValue.NewString(path));

					result = script.DoString(
						"local f = assert(io.open(path, 'r'))\n" +
						"local a, b = " + readCode + "\n" +
						"f:close()\n" +
						"return a, b");
				});

				return result;
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

		/// <summary>Runs <paramref name="code"/> with a full platform and the global "dir", a new empty directory.</summary>
		private static DynValue InTempDirectory(string code)
		{
			string dir = Path.Combine(Path.GetTempPath(), "moonsharp-io-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			try
			{
				DynValue result = null;

				WithPlatform(new StandardPlatformAccessor(), () =>
				{
					Script script = new Script(CoreModules.Preset_Complete);
					script.Globals.Set("dir", DynValue.NewString(dir + Path.DirectorySeparatorChar));
					result = script.DoString(code);
				});

				return result;
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		// These ended the host with a CLR exception (conformance survey entry 31); Lua 5.2's results.
		[Test]
		public void FileErrors_AreLuaErrors()
		{
			DynValue r = InTempDirectory(@"
				local ok1, e1 = pcall(io.input, dir .. 'missing')
				local ok2, e2 = pcall(io.output, dir .. 'no/such/dir')
				local ok3, e3 = pcall(io.lines, dir .. 'missing')
				local f = io.open(dir .. 'f', 'w') f:write('12 ab\n3\n') f:close()
				f = io.open(dir .. 'f')
				local a, b, c = f:seek('set', -1)
				local rest = f:read(-1)
				f:close()
				return ok1, e1 == 'cannot open file \'' .. dir .. 'missing\' (No such file or directory)',
					ok2, e2:find('No such file or directory', 1, true) ~= nil, ok3, e3 == e1, a, b, c, rest");

			Assert.IsFalse(r.Tuple[0].Boolean);
			Assert.IsTrue(r.Tuple[1].Boolean);
			Assert.IsFalse(r.Tuple[2].Boolean);
			Assert.IsTrue(r.Tuple[3].Boolean);
			Assert.IsFalse(r.Tuple[4].Boolean);
			Assert.IsTrue(r.Tuple[5].Boolean);
			// Seeking before the start: nil, "Invalid argument", 22 (EINVAL).
			Assert.IsTrue(r.Tuple[6].IsNil());
			Assert.AreEqual("Invalid argument", r.Tuple[7].String);
			Assert.AreEqual(22, r.Tuple[8].Number);
			// A negative count reads the rest (Lua 5.2 asks for (size_t)-1 bytes and fails to allocate them).
			Assert.AreEqual("12 ab\n3\n", r.Tuple[9].String);
		}

		// The reader of a write-only file and the writer of a read-only one are null: reading or writing ended
		// the host with a NullReferenceException. Lua 5.2 returns nil, "Bad file descriptor", 9 (EBADF).
		[Test]
		public void ReadWriteOnTheWrongMode_IsBadFileDescriptor()
		{
			DynValue r = InTempDirectory(@"
				local f = io.open(dir .. 'f', 'w')
				local a, b, c = f:read('*l')
				f:close()
				f = io.open(dir .. 'f')
				local d, e, g = f:write('x')
				f:close()
				return a, b, c, d, e, g");

			Assert.IsTrue(r.Tuple[0].IsNil());
			Assert.AreEqual("Bad file descriptor", r.Tuple[1].String);
			Assert.AreEqual(9, r.Tuple[2].Number);
			Assert.IsTrue(r.Tuple[3].IsNil());
			Assert.AreEqual("Bad file descriptor", r.Tuple[4].String);
			Assert.AreEqual(9, r.Tuple[5].Number);
		}

		// io.lines and file:lines read as they go and return a function, as in Lua 5.2. They read the whole file
		// into a list first, and the exhausted iterator threw NotSupportedException; file:lines('*a'), whose
		// result never ends, filled the memory.
		[Test]
		public void Lines_AreLazyIterators()
		{
			DynValue r = InTempDirectory(@"
				local f = io.open(dir .. 'f', 'w') f:write('a\nb\n') f:close()
				local it = io.lines(dir .. 'f')
				local seen = {}
				for l in it do seen[#seen + 1] = l end
				local ok, e = pcall(it)
				f = io.open(dir .. 'f')
				local n = 0
				for all in f:lines('*a') do n = n + 1 if n == 3 then break end end
				f:close()
				return type(it), table.concat(seen, ','), ok, e, n");

			Assert.AreEqual("function", r.Tuple[0].String);
			Assert.AreEqual("a,b", r.Tuple[1].String);
			Assert.IsFalse(r.Tuple[2].Boolean);
			Assert.AreEqual("file is already closed", r.Tuple[3].String);
			Assert.AreEqual(3, r.Tuple[4].Number);
		}

		// fopen's modes: "w+" creates the file (it was FileMode.Truncate), "a" and "a+" open (Append with read
		// access threw), "r+" needs the file, and a mode outside r/w/a[+][b...] is an error.
		[Test]
		public void Open_FollowsFopenModes()
		{
			DynValue r = InTempDirectory(@"
				local f = io.open(dir .. 'g', 'w+') f:write('hello') f:seek('set') local wplus = f:read('*a') f:close()
				f = io.open(dir .. 'g', 'a') f:write('!') f:close()
				f = io.open(dir .. 'g', 'a+') f:write('?') f:seek('set') local aplus = f:read('*a') f:close()
				local rplus, msg = io.open(dir .. 'missing', 'r+')
				local ok, e = pcall(io.open, dir .. 'g', 'rw')
				return wplus, aplus, rplus, msg == dir .. 'missing: No such file or directory', ok, e");

			Assert.AreEqual("hello", r.Tuple[0].String);
			Assert.AreEqual("hello!?", r.Tuple[1].String);
			Assert.IsTrue(r.Tuple[2].IsNil());
			Assert.IsTrue(r.Tuple[3].Boolean);
			Assert.IsFalse(r.Tuple[4].Boolean);
			Assert.AreEqual("bad argument #2 to 'open' (invalid mode)", r.Tuple[5].String);
		}
	}
}
