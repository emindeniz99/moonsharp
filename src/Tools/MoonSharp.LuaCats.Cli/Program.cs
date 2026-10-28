using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter;

namespace MoonSharp.LuaCats.Cli
{
	/// <summary>
	/// moonsharp-luacats: writes or checks LuaCATS definitions for the script a host's setup method returns.
	/// </summary>
	public static class Program
	{
		const string Usage = @"usage: moonsharp-luacats <host.dll> <Namespace.Type.Method> <out-dir> [--luarc <file>] [--check]

Loads host.dll and calls the method: public, static, no parameters, returning a configured
MoonSharp Script (types registered, globals set, the modules the host uses). Writes LuaCATS
definitions of what that script can reach into out-dir, replacing earlier generated files.

  --luarc <file>  also write a .luarc.json for lua-language-server that uses out-dir
  --check         write nothing; exit 1 when out-dir (and the .luarc.json) are not up to date";

		public static int Main(string[] args)
		{
			var positional = new List<string>();
			string luarc = null;
			bool check = false;

			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == "--check")
					check = true;
				else if (args[i] == "--luarc" && i + 1 < args.Length)
					luarc = args[++i];
				else if (args[i].StartsWith("--"))
					return Fail(Usage);
				else
					positional.Add(args[i]);
			}

			if (positional.Count != 3)
				return Fail(Usage);

			Script script;

			try
			{
				script = CreateScript(positional[0], positional[1]);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is BadImageFormatException || ex is TypeLoadException)
			{
				return Fail("error: " + ex.Message);
			}

			LuaCatsResult result = LuaCatsGenerator.Generate(script);

			foreach (string warning in result.Warnings)
				Console.Error.WriteLine("warning: " + warning);

			string outDir = positional[2];
			string luarcText = null;

			if (luarc != null)
			{
				string from = Path.GetDirectoryName(Path.GetFullPath(luarc));
				luarcText = result.CreateLuarc(Path.GetRelativePath(from, Path.GetFullPath(outDir)).Replace('\\', '/'));
			}

			if (check)
			{
				List<string> stale = result.Compare(outDir).ToList();

				if (luarc != null && (!File.Exists(luarc) || File.ReadAllText(luarc).Replace("\r\n", "\n") != luarcText))
					stale.Add(luarc + ": differs");

				foreach (string s in stale)
					Console.WriteLine("stale: " + s);

				if (stale.Count > 0)
				{
					Console.WriteLine("Run the same command without --check to regenerate.");
					return 1;
				}

				Console.WriteLine("up to date: " + result.Files.Count + " files in " + outDir);
				return 0;
			}

			result.WriteTo(outDir);

			if (luarc != null)
				File.WriteAllText(luarc, luarcText);

			Console.WriteLine("wrote " + result.Files.Count + " files to " + outDir);
			return 0;
		}

		static Script CreateScript(string assemblyPath, string method)
		{
			// Resolves MoonSharp.Interpreter to this tool's copy, so the host registers its types in the same
			// registry the generator reads.
			Assembly asm = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
			int dot = method.LastIndexOf('.');

			if (dot <= 0)
				throw new ArgumentException("expected Namespace.Type.Method, got " + method);

			Type type = asm.GetType(method.Substring(0, dot), true);
			MethodInfo mi = type.GetMethod(method.Substring(dot + 1), BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

			if (mi == null || !typeof(Script).IsAssignableFrom(mi.ReturnType))
				throw new ArgumentException(method + " must be a public static method without parameters that returns a Script");

			return (Script)mi.Invoke(null, null);
		}

		static int Fail(string message)
		{
			Console.Error.WriteLine(message);
			return 2;
		}
	}
}
