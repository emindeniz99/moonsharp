using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter.Loaders;

namespace MoonSharp.Interpreter.Tests
{
	/// <summary>
	/// Runs every test fixture in this assembly and writes one line per test, so that a run on one
	/// platform (a Unity player) can be compared line by line with a run on another (.NET).
	/// Used by src/TestRunners/EmbeddedTestRunner and src/Unity/UnityTestBed.
	/// </summary>
	public static class SuiteReport
	{
		/// <summary>Tests that touch the real file system and process environment.</summary>
		public static readonly string[] SkipList = new string[]
		{
			"TestMore_308_io",
			"TestMore_309_os",
		};

		/// <summary>
		/// Runs the suite. Scripts are looked up by file name ("000-sanity.t", "More.lua"), as
		/// <see cref="UnityAssetsScriptLoader"/> does with Unity's text assets.
		/// Each results line is "OK|FAIL|SKIP", a tab, "Fixture.Test", a tab and the message.
		/// </summary>
		/// <returns>0 when at least one test ran and none failed, 1 otherwise.</returns>
		public static int Run(Dictionary<string, string> scripts, TextWriter results, Action<string> log)
		{
			Script.DefaultOptions.ScriptLoader = new UnityAssetsScriptLoader(scripts);

			TestRunner runner = new TestRunner(r =>
			{
				if (r.Type == TestResultType.Message)
					log(r.Message);
			});

			foreach (TestResult r in runner.IterateOnTests(null, SkipList))
			{
				if (r.Type == TestResultType.Message)
					continue;

				string outcome = r.Type == TestResultType.Ok ? "OK" : r.Type == TestResultType.Fail ? "FAIL" : "SKIP";
				string message = (r.Message ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
				results.WriteLine("{0}\t{1}.{2}\t{3}", outcome, r.FixtureName, r.TestName, message);

				if (r.Type == TestResultType.Fail)
					log(string.Format("[FAIL] {0}.{1} - {2}", r.FixtureName, r.TestName, r.Exception != null ? r.Exception.ToString() : message));
			}

			results.Flush();
			return runner.Fail == 0 && runner.Ok > 0 ? 0 : 1;
		}
	}
}
