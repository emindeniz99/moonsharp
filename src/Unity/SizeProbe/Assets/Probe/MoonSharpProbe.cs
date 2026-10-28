using System;
using MoonSharp.Interpreter;
using UnityEngine;

namespace MoonSharp.SizeProbe
{
	/// <summary>
	/// The smallest use of MoonSharp: one script through a default Script, which registers the
	/// standard library, and one library call, so stripping keeps what a game that runs scripts
	/// keeps. The player logs the result and quits; .github/workflows/unity.yml checks the line
	/// and deletes this folder for the build without MoonSharp.
	/// </summary>
	static class MoonSharpProbe
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void Run()
		{
			int code = 1;
			try
			{
				string result = Script.RunString("return string.rep('ab', 2) .. 6 * 7").String;
				Debug.Log("MoonSharp size probe: " + result);
				code = result == "abab42" ? 0 : 1;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
#if !UNITY_EDITOR
			Application.Quit(code);
#endif
		}
	}
}
