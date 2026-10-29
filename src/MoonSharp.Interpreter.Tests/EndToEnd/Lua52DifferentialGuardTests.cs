using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Runs every case of the Lua 5.2 differential suite (Lua52Differential/cases) and fails when one ends with an
	/// exception a script cannot catch: anything but an InterpreterException. A script error must always be a Lua
	/// error that pcall can handle, never something that ends the host. The values are compared with Lua 5.2 by
	/// Lua52Differential/compare.py, not here.
	/// </summary>
	[TestFixture]
	public class Lua52DifferentialGuardTests
	{
		static readonly string[] CaseFiles =
		{
			"bit32", "coroutines", "errors", "format", "hostcrash", "load", "math", "metamethods", "misc", "select",
			"strings", "syntax", "tables", "tonumber", "tostring",
		};

		/// <summary>
		/// Cases that still end the host, each waiting for work outside this branch. The list may only shrink:
		/// the test fails when a case listed here stops failing, so the entry has to go with the fix.
		/// </summary>
		static readonly HashSet<string> KnownCrashes = new HashSet<string>
		{
			// table.unpack with i > j + 1: new DynValue[j - i + 1] overflows (CoreLib/TableModule.cs, the Table work).
			"tables/unpack i > j + 1",
			"tables/unpack i much larger",
			"tables/unpack huge",
		};

		// A case that runs this many instructions is reported as a hang.
		const long InstructionLimit = 50 * 1000 * 1000;

		[Test]
		public void Lua52Differential_NoCaseEndsTheHost()
		{
			var crashed = new List<string>();
			int total = 0;

			foreach (string file in CaseFiles)
			{
				foreach (KeyValuePair<string, string> c in ReadCases(file))
				{
					total++;
					string name = file + "/" + c.Key;
					string failure = Run(c.Value);

					if (failure != null)
						crashed.Add(name + ": " + failure);

					if ((failure != null) != KnownCrashes.Contains(name))
						Assert.Fail(failure != null
							? string.Format("{0} ended with {1}, which pcall cannot catch", name, failure)
							: string.Format("{0} no longer ends the host: remove it from KnownCrashes", name));
				}
			}

			Assert.Greater(total, 5000, "the case files were not found");
			Assert.AreEqual(KnownCrashes.Count, crashed.Count, string.Join("\n", crashed.ToArray()));
		}

		/// <summary>Null when the case returns or raises a script error, else the exception type.</summary>
		static string Run(string code)
		{
			var script = new Script(CoreModules.Preset_Complete);
			script.Options.InterruptHandler = executed => executed > InstructionLimit;

			try
			{
				script.DoString(code, null, "=c");
				return null;
			}
			catch (ScriptInterruptedException)
			{
				return "a hang (" + InstructionLimit + " instructions)";
			}
			catch (InterpreterException)
			{
				return null;
			}
			catch (Exception ex)
			{
				return ex.GetType().FullName;
			}
		}

		/// <summary>
		/// The cases of one file, as run.lua splits them: a line "--@@ name" starts a case. The file is read
		/// through the script loader, so the Unity runs find it among the test bed's text assets.
		/// </summary>
		static IEnumerable<KeyValuePair<string, string>> ReadCases(string file)
		{
			var script = new Script(CoreModules.None);
			object content = script.Options.ScriptLoader.LoadFile("Lua52Differential/cases/" + file + ".lua", script.Globals);
			string text = content as string;

			if (text == null)
				using (var reader = new StreamReader((Stream)content, Encoding.UTF8))
					text = reader.ReadToEnd();

			string name = null;
			var body = new StringBuilder();

			foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
			{
				if (line.StartsWith("--@@ "))
				{
					if (name != null)
						yield return new KeyValuePair<string, string>(name, body.ToString());

					name = line.Substring(5);
					body.Length = 0;
				}
				else if (name != null)
				{
					body.Append(line).Append('\n');
				}
			}

			if (name != null)
				yield return new KeyValuePair<string, string>(name, body.ToString());
		}
	}
}
