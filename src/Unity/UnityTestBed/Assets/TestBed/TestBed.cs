using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using MoonSharp.Interpreter.Tests;
using UnityEngine;

namespace MoonSharp.UnityTestBed
{
	/// <summary>
	/// Runs the MoonSharp test suite inside Unity and writes the results file that CI compares
	/// with the .NET run (src/TestRunners/EmbeddedTestRunner). Used by the Editor entry point
	/// (Editor/BatchRunner.cs) and by the player, which runs it on start and quits.
	/// </summary>
	public static class TestBed
	{
		/// <summary>Command-line argument naming the results file.</summary>
		public const string ResultsArg = "-moonsharpResults";

		/// <returns>The process exit code: 0 when every test and check passed.</returns>
		public static int Run()
		{
			Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

			string path = GetArg(ResultsArg) ?? Path.Combine(Application.persistentDataPath, "moonsharp-results.tsv");
			Debug.Log("MoonSharp test bed: Unity " + Application.unityVersion + ", platform "
				+ Script.GlobalOptions.Platform.GetPlatformName() + ", results in " + path);

			var scripts = new Dictionary<string, string>();
			foreach (TextAsset asset in Resources.LoadAll<TextAsset>(UnityAssetsScriptLoader.DEFAULT_PATH))
				scripts.Add(asset.name, asset.text);

			using (var results = new StreamWriter(path))
			{
				int code = SuiteReport.Run(scripts, results, Debug.Log);

				// What a game gets without configuring anything: the platform is recognised and the
				// default loader reads scripts from Resources/MoonSharp/Scripts.
				// IL2CPP must be seen as AOT, or interop would try to compile expression trees.
				string platform = Script.GlobalOptions.Platform.GetPlatformName();
				bool aot = platform.EndsWith(".aot");
#if ENABLE_IL2CPP
				bool expectAot = true;
#else
				bool expectAot = false;
#endif
				code |= Check(results, "Unity.PlatformName", platform.StartsWith("limited.unity.") && aot == expectAot, platform);
				code |= Check(results, "Unity.DefaultScriptLoader", new UnityAssetsScriptLoader().ScriptFileExists("000-sanity.t"),
					"new UnityAssetsScriptLoader() finds 000-sanity.t");
				return code;
			}
		}

		static int Check(TextWriter results, string name, bool ok, string message)
		{
			results.WriteLine("{0}\t{1}\t{2}", ok ? "OK" : "FAIL", name, message);
			if (!ok)
				Debug.Log("[FAIL] " + name + " - " + message);
			return ok ? 0 : 1;
		}

		public static string GetArg(string name)
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
				if (args[i] == name)
					return args[i + 1];
			return null;
		}
	}

#if !UNITY_EDITOR
	static class PlayerEntry
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void RunAndQuit()
		{
			int code = 2;
			try
			{
				code = TestBed.Run();
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			Application.Quit(code);
		}
	}
#endif
}
