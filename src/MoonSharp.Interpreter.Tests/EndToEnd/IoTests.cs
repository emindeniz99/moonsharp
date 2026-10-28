using System.Globalization;
using System.IO;
using System.Threading;
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
	}
}
