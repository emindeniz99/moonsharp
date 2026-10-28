using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonSharp.UnityTestBed
{
	/// <summary>
	/// Batch-mode entry points for CI (.github/workflows/unity.yml):
	///   -executeMethod MoonSharp.UnityTestBed.BatchRunner.RunTests -moonsharpResults FILE
	///   -executeMethod MoonSharp.UnityTestBed.BatchRunner.BuildLinuxIl2cpp -moonsharpBuild FILE
	///   -executeMethod MoonSharp.UnityTestBed.BatchRunner.RunBenchmarks -moonsharpBench FILE
	/// All exit the editor with a non-zero code on failure.
	/// </summary>
	public static class BatchRunner
	{
		const string BuildArg = "-moonsharpBuild";
		const string ScenePath = "Assets/TestBed.unity";

		/// <summary>Runs the suite in the editor (Mono), outside play mode.</summary>
		public static void RunTests()
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
			EditorApplication.Exit(code);
		}

		/// <summary>
		/// Runs the Lua workloads in the editor (Mono). Start the editor with -releaseCodeOptimization,
		/// or the scripts run as debug builds; the log line names the mode.
		/// </summary>
		public static void RunBenchmarks()
		{
			int code = 2;
			try
			{
				string path = TestBed.GetArg(TestBed.BenchArg);
				if (string.IsNullOrEmpty(path))
					throw new ArgumentException(TestBed.BenchArg + " <path to the results file> is required");

				Debug.Log("BatchRunner: code optimization " + CompilationPipeline.codeOptimization);
				code = TestBed.RunBenchmarks(path);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			EditorApplication.Exit(code);
		}

		/// <summary>Builds a Linux IL2CPP player that runs the suite on start and quits.</summary>
		public static void BuildLinuxIl2cpp()
		{
			int code = 1;
			NamedBuildTarget target = NamedBuildTarget.Standalone;
			ScriptingImplementation saved = PlayerSettings.GetScriptingBackend(target);
			try
			{
				string output = TestBed.GetArg(BuildArg);
				if (string.IsNullOrEmpty(output))
					throw new ArgumentException(BuildArg + " <path to the player executable> is required");

				// The player needs a scene to start; the suite itself runs from PlayerEntry.
				EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single), ScenePath);
				PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);

				BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
				{
					scenes = new[] { ScenePath },
					locationPathName = output,
					target = BuildTarget.StandaloneLinux64,
					targetGroup = BuildTargetGroup.Standalone,
					options = BuildOptions.None,
				});

				Debug.Log("BatchRunner: build " + report.summary.result + ", " + report.summary.totalErrors + " error(s), "
					+ report.summary.totalSize + " bytes");
				code = report.summary.result == BuildResult.Succeeded ? 0 : 1;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			finally
			{
				// Keep the project on Mono, so the editor run (and a developer's editor) stays Mono.
				PlayerSettings.SetScriptingBackend(target, saved);
			}
			EditorApplication.Exit(code);
		}
	}
}
