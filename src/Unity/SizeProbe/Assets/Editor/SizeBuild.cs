using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonSharp.SizeProbe
{
	/// <summary>
	/// Builds the Linux IL2CPP player whose size .github/workflows/unity.yml reports:
	///   -executeMethod MoonSharp.SizeProbe.SizeBuild.BuildLinuxIl2cpp -moonsharpBuild FILE
	/// The workflow builds this project as committed and again without Assets/Probe and the
	/// MoonSharp package, so the difference is what MoonSharp adds to a player. Every player
	/// setting except the scripting backend stays at the editor's default; the log line starting
	/// with "SizeBuild:" names the ones that change the size.
	/// </summary>
	public static class SizeBuild
	{
		const string BuildArg = "-moonsharpBuild";
		const string ScenePath = "Assets/Empty.unity";

		public static void BuildLinuxIl2cpp()
		{
			int code = 1;
			try
			{
				string output = GetArg(BuildArg);
				if (string.IsNullOrEmpty(output))
					throw new ArgumentException(BuildArg + " <path to the player executable> is required");

				NamedBuildTarget target = NamedBuildTarget.Standalone;
				EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single), ScenePath);
				PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
				Debug.Log("SizeBuild: Unity " + Application.unityVersion
					+ ", managed stripping " + PlayerSettings.GetManagedStrippingLevel(target)
					+ ", engine code stripping " + (PlayerSettings.stripEngineCode ? "on" : "off")
					+ ", C++ compiler configuration " + PlayerSettings.GetIl2CppCompilerConfiguration(target));

				BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
				{
					scenes = new[] { ScenePath },
					locationPathName = output,
					target = BuildTarget.StandaloneLinux64,
					targetGroup = BuildTargetGroup.Standalone,
					options = BuildOptions.None,
				});

				Debug.Log("SizeBuild: build " + report.summary.result + ", " + report.summary.totalErrors + " error(s)");
				code = report.summary.result == BuildResult.Succeeded ? 0 : 1;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			EditorApplication.Exit(code);
		}

		static string GetArg(string name)
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
				if (args[i] == name)
					return args[i + 1];
			return null;
		}
	}
}
