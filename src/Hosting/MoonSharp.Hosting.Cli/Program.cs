using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace MoonSharp.Hosting.Cli
{
	/// <summary>
	/// moonsharp-host: validate scripts, run them, check fixture suites, or serve the JVM worker protocol.
	/// </summary>
	public static class Program
	{
		const string Usage = @"usage:
  moonsharp-host validate <file.lua>...           compile without running; exit 1 on any syntax error
  moonsharp-host run <file.lua> [input.json]      run a chunk; if it returns a function, call it with the input
  moonsharp-host test <suite.json>... [--report out.json]
                                                  run fixture suites and compare results; exit 1 on any mismatch
  moonsharp-host worker                           serve JSON requests on stdin, one per line (see docs/hosting.md)
  moonsharp-host version";

		public static int Main(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine(Usage);
				return 2;
			}

			switch (args[0])
			{
				case "validate":
					return Validate(args.Skip(1).ToArray());
				case "run":
					return Run(args.Skip(1).ToArray());
				case "test":
					return FixtureRunner.Run(args.Skip(1).ToArray());
				case "worker":
					return new Worker(Console.In, Console.Out).Serve();
				case "version":
					Console.WriteLine(ScriptHost.EngineVersion);
					return 0;
				default:
					Console.Error.WriteLine(Usage);
					return 2;
			}
		}

		static int Validate(string[] files)
		{
			if (files.Length == 0)
			{
				Console.Error.WriteLine(Usage);
				return 2;
			}

			int failures = 0;

			foreach (string file in files)
			{
				JsonNode result = JsonNode.Parse(new ScriptHost().Validate(Path.GetFileName(file), File.ReadAllText(file)));

				if ((bool)result["ok"])
				{
					Console.WriteLine("ok    " + file);
				}
				else
				{
					++failures;
					Console.WriteLine("FAIL  " + file + ": " + (string)result["error"]["message"]);
				}
			}

			return failures == 0 ? 0 : 1;
		}

		static int Run(string[] args)
		{
			if (args.Length < 1 || args.Length > 2)
			{
				Console.Error.WriteLine(Usage);
				return 2;
			}

			string input = args.Length == 2 ? File.ReadAllText(args[1]) : null;
			string result = new ScriptHost().Run(Path.GetFileName(args[0]), File.ReadAllText(args[0]), input);
			Console.WriteLine(result);
			return (bool)JsonNode.Parse(result)["ok"] ? 0 : 1;
		}
	}
}
