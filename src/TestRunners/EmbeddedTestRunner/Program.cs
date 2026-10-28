using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Platforms;
using MoonSharp.Interpreter.Tests;

namespace EmbeddedTestRunner
{
	static class Program
	{
		// Usage: EmbeddedTestRunner [results.tsv]
		static int Main(string[] args)
		{
			// What MoonSharp picks on Unity: no io or os.execute, no environment.
			Script.GlobalOptions.Platform = new LimitedPlatformAccessor();

			var scripts = new Dictionary<string, string>();
			foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestMore"), "*", SearchOption.AllDirectories))
				scripts.Add(Path.GetFileName(file), File.ReadAllText(file));

			using (TextWriter results = args.Length > 0 ? new StreamWriter(args[0]) : TextWriter.Null)
				return SuiteReport.Run(scripts, results, Console.WriteLine);
		}
	}
}
