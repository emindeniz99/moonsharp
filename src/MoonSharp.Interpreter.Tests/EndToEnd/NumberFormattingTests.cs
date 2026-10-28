using System;
using System.Globalization;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Numbers must print the same text on every host: the server (.NET), browsers (.NET WebAssembly) and
	/// Unity (Mono, IL2CPP). Before, Mono printed 2^53 as 9.00719925474099E+15 where .NET printed
	/// 9007199254740992, and concatenation followed the process culture (1,5 under tr-TR).
	/// </summary>
	[TestFixture]
	public class NumberFormattingTests
	{
		static readonly object[][] Cases =
		{
			new object[] { 9007199254740992.0, "9007199254740992" },
			new object[] { 0.1 + 0.2, "0.30000000000000004" },
			new object[] { 1e15, "1000000000000000" },
			new object[] { 1e16, "10000000000000000" },
			new object[] { 1e17, "1E+17" },
			new object[] { 75987868744874290.0, "75987868744874290" },
			new object[] { 1e-4, "0.0001" },
			new object[] { 1e-5, "1E-05" },
			new object[] { -0.0, "-0" },
			new object[] { 123456789012345680.0, "1.2345678901234568E+17" },
			new object[] { 1234567890123456.0, "1234567890123456" },
			new object[] { 123456789012345.0, "123456789012345" },
			new object[] { 4.9406564584124654E-324, "5E-324" },
			new object[] { double.MaxValue, "1.7976931348623157E+308" },
			new object[] { 1.0 / 3, "0.3333333333333333" },
			new object[] { 0.1, "0.1" },
			new object[] { -1.5, "-1.5" },
			new object[] { 100.0, "100" },
		};

		[Test]
		public void Number_PrintsTheSameTextOnEveryRuntime()
		{
			Script s = new Script();

			foreach (object[] c in Cases)
			{
				s.Globals["x"] = (double)c[0];
				string expected = (string)c[1];

				Assert.AreEqual(expected, s.DoString("return tostring(x)").String, "tostring");
				Assert.AreEqual(expected, s.DoString("return x .. ''").String, "concatenation");
				Assert.AreEqual(expected, DynValue.NewNumber((double)c[0]).ToString(), "DynValue.ToString");
			}
		}

		[Test]
		public void Number_ConcatenationIgnoresTheProcessCulture()
		{
			string res = Utils.WithCulture("tr-TR", () => new Script().DoString("return 1.5 .. ' ' .. string.format('%s', 2.25)").String);

			Assert.AreEqual("1.5 2.25", res);
		}

		[Test]
		public void Number_ShortestTextMatchesDotNetCoreOnRandomDoubles()
		{
			// .NET Core 3.0+ prints the shortest round-trip text; older runtimes are what the fix is for,
			// so they cannot serve as the reference. (Assert.Ignore counts as a failure in this repo's
			// own runner, so return instead.)
			if (Environment.Version.Major < 5)
				return;

			Script s = new Script();
			s.Globals["x"] = 0.0;
			Random rnd = new Random(12345);
			byte[] bytes = new byte[8];

			for (int i = 0; i < 20000; i++)
			{
				rnd.NextBytes(bytes);
				double d = i % 2 == 0 ? BitConverter.ToDouble(bytes, 0) : (rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-20, 21));

				if (double.IsNaN(d) || double.IsInfinity(d))
					continue;

				s.Globals["x"] = d;
				Assert.AreEqual(d.ToString("R", CultureInfo.InvariantCulture), s.DoString("return tostring(x)").String, "bits " + BitConverter.DoubleToInt64Bits(d).ToString("X16"));
			}
		}
	}
}
