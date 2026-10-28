using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.CoreLib;

namespace MoonSharp.Hosting.Cli
{
	/// <summary>
	/// moonsharp-host: validate scripts, run them, check fixture suites, or serve the JVM worker protocol.
	/// </summary>
	public static class Program
	{
		const string Usage = @"usage:
  moonsharp-host validate <file.lua>...           compile without running; exit 1 on any syntax error
  moonsharp-host compile <file.lua> -o <out>      compile to a binary chunk that keeps source locations
  moonsharp-host run <file> [input.json]          run a chunk (source or compiled); if it returns a function, call it with the input
  moonsharp-host test [--bytecode] [--host name] [--report out.json] <suite.json | directory>...
                                                  run fixture suites against known-failures/<host>.json; exit 1 on any difference
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
				case "compile":
					return Compile(args.Skip(1).ToArray());
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
			string result = new ScriptHost().Run(Path.GetFileName(args[0]), ReadScript(args[0]), input);
			Console.WriteLine(result);
			return (bool)JsonNode.Parse(result)["ok"] ? 0 : 1;
		}

		static int Compile(string[] args)
		{
			if (args.Length != 3 || args[1] != "-o")
			{
				Console.Error.WriteLine(Usage);
				return 2;
			}

			byte[] dump;

			try
			{
				dump = Compile(Path.GetFileName(args[0]), File.ReadAllText(args[0]));
			}
			catch (SyntaxErrorException ex)
			{
				Console.WriteLine("FAIL  " + args[0] + ": " + (ex.DecoratedMessage ?? ex.Message));
				return 1;
			}

			File.WriteAllBytes(args[2], dump);
			Console.WriteLine("ok    " + args[0] + " -> " + args[2] + " (" + dump.Length + " bytes)");
			return 0;
		}

		/// <summary>
		/// Compiles a chunk to a binary dump (Script.Dump) that keeps source locations, so runtime errors still
		/// name the line. Load it with Script.LoadStream or LoadFile, or pass <see cref="DumpText"/> to a host.
		/// </summary>
		public static byte[] Compile(string chunkName, string source)
		{
			var script = new Script(CoreModules.None);
			DynValue chunk = script.LoadString(source, null, chunkName);

			using (var stream = new MemoryStream())
			{
				script.Dump(chunk, stream, true);
				return stream.ToArray();
			}
		}

		/// <summary>A dump as text: Script.LoadString, and so every ScriptHost operation that takes source, accepts this form.</summary>
		public static string DumpText(byte[] dump)
		{
			return StringModule.BASE64_DUMP_HEADER + Convert.ToBase64String(dump);
		}

		// The first eight bytes of a dump (DUMP_CHUNK_MAGIC in Processor_BinaryDump.cs, little-endian).
		const ulong DumpMagic = 0x1A0D234E4F4F4D1D;

		static string ReadScript(string path)
		{
			byte[] bytes = File.ReadAllBytes(path);
			return bytes.Length >= 8 && BinaryPrimitives.ReadUInt64LittleEndian(bytes) == DumpMagic ? DumpText(bytes) : File.ReadAllText(path);
		}
	}
}
