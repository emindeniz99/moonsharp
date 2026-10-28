using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;
using Sample;

namespace MoonSharp.LuaCats.Tests
{
	/// <summary>
	/// Checks the LuaCATS generator against the sample host (Sample.cs):
	/// 1. the definitions match the committed ones in expected/ (--update rewrites them);
	/// 2. the generator reports exactly the members the sample makes unreachable on purpose;
	/// 3. scripts/good.lua, which uses only declared API, runs on the sample script;
	/// 4. with --luals, lua-language-server finds nothing in the definitions or good.lua, and exactly the
	///    marked problems in bad.lua (each line with "-- expect: code").
	/// </summary>
	public static class Program
	{
		const string Usage = "usage: MoonSharp.LuaCats.Tests <expected-dir> [--update] [--luals <lua-language-server>]";

		// The members Sample.cs makes unreachable on purpose, and the standard functions lua-language-server
		// keeps offering although the soft sandbox lacks them.
		static readonly string[] ExpectedWarnings =
		{
			"Sample.Player.GetSecret: Sample.Secret is not registered: a script that reads one gets an error (\"cannot convert clr type\"); declared as any",
			"Sample.Player.OnTick: System.Action<System.Double>: a script cannot pass a function for this delegate type (MoonSharp converts functions only to Closure and ScriptFunctionDelegate, or through a custom converter)",
		};

		static int s_Failures;

		public static int Main(string[] args)
		{
			if (args.Length < 1)
			{
				Console.Error.WriteLine(Usage);
				return 2;
			}

			string expected = args[0];
			bool update = args.Contains("--update");
			int luals = Array.IndexOf(args, "--luals");

			LuaCatsResult result = LuaCatsGenerator.Generate(SampleApi.CreateScript());

			if (update)
				result.WriteTo(expected);

			CheckFiles(result, expected);
			CheckDeterministic(result);
			CheckWarnings(result);
			CheckGoodScriptRuns();

			if (luals >= 0 && luals + 1 < args.Length)
				CheckWithLanguageServer(result, args[luals + 1]);
			else
				Console.WriteLine("SKIP  lua-language-server checks (no --luals)");

			Console.WriteLine(s_Failures == 0 ? "LUACATS_TESTS_PASS" : "LUACATS_TESTS_FAIL: " + s_Failures);
			return s_Failures == 0 ? 0 : 1;
		}

		static void Check(bool ok, string what, string detail = null)
		{
			Console.WriteLine((ok ? "ok    " : "FAIL  ") + what + (ok || detail == null ? "" : "\n" + detail));

			if (!ok)
				++s_Failures;
		}

		static void CheckFiles(LuaCatsResult result, string expected)
		{
			IList<string> differences = result.Compare(expected);
			Check(differences.Count == 0, "definitions match " + expected + " (" + result.Files.Count + " files)",
				string.Join("\n", differences) + "\nRun with --update to rewrite them, then review the diff.");
		}

		static void CheckDeterministic(LuaCatsResult result)
		{
			// A second script from the same setup (registrations are already there) gives the same files.
			LuaCatsResult again = LuaCatsGenerator.Generate(SampleApi.CreateScript());
			Check(again.Files.SequenceEqual(result.Files), "a second run gives the same files");
		}

		static void CheckWarnings(LuaCatsResult result)
		{
			string[] got = result.Warnings.Where(w => !w.StartsWith("lua-language-server still offers")).ToArray();
			Check(got.SequenceEqual(ExpectedWarnings), "warnings are exactly the deliberate gaps",
				"got:\n  " + string.Join("\n  ", got));

			// The soft sandbox has no io and debug modules, so they are turned off, not reported.
			Check(!result.Warnings.Any(w => w.StartsWith("lua-language-server still offers")),
				"no standard library mismatches left", string.Join("\n", result.Warnings));
		}

		static void CheckGoodScriptRuns()
		{
			Script script = SampleApi.CreateScript();
			string good = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "good.lua"));

			try
			{
				script.DoString(good, null, "good.lua");
				Check(true, "good.lua runs on the sample script");
			}
			catch (InterpreterException ex)
			{
				Check(false, "good.lua runs on the sample script", ex.DecoratedMessage ?? ex.Message);
			}
		}

		static void CheckWithLanguageServer(LuaCatsResult result, string luals)
		{
			string root = Path.Combine(Path.GetTempPath(), "moonsharp-luacats-" + Guid.NewGuid().ToString("N"));
			string scripts = Path.Combine(AppContext.BaseDirectory, "scripts");

			try
			{
				// The definitions on their own, as workspace files rather than a library, so they are checked too.
				string defs = Path.Combine(root, "defs");
				result.WriteTo(defs);
				File.WriteAllText(Path.Combine(defs, ".luarc.json"), result.CreateLuarc("../none"));
				var found = RunLanguageServer(luals, defs, Path.Combine(root, "log-defs"));
				Check(found.Count == 0, "lua-language-server: definitions have no diagnostics", string.Join("\n", found));

				found = RunWorkspace(luals, result, root, Path.Combine(scripts, "good.lua"));
				Check(found.Count == 0, "lua-language-server: good.lua has no diagnostics", string.Join("\n", found));

				string bad = Path.Combine(scripts, "bad.lua");
				var want = File.ReadAllLines(bad)
					.Select((text, i) => new { Line = i + 1, Match = Regex.Match(text, "-- expect: ([a-z-]+)") })
					.Where(x => x.Match.Success)
					.Select(x => x.Line + " " + x.Match.Groups[1].Value)
					.ToList();
				found = RunWorkspace(luals, result, root, bad);
				var got = found.Select(f => string.Join(" ", f.Split(' ').Take(2))).Distinct().ToList();
				Check(got.SequenceEqual(want), "lua-language-server: bad.lua gets exactly the " + want.Count + " expected diagnostics",
					"expected:\n  " + string.Join("\n  ", want) + "\ngot:\n  " + string.Join("\n  ", found));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		static List<string> RunWorkspace(string luals, LuaCatsResult result, string root, string script)
		{
			string ws = Path.Combine(root, "ws-" + Path.GetFileNameWithoutExtension(script));
			result.WriteTo(Path.Combine(ws, "types"));
			File.WriteAllText(Path.Combine(ws, ".luarc.json"), result.CreateLuarc("types"));
			File.Copy(script, Path.Combine(ws, Path.GetFileName(script)));
			return RunLanguageServer(luals, ws, Path.Combine(root, "log-" + Path.GetFileNameWithoutExtension(script)));
		}

		/// <summary>Runs check mode; returns "line code message" for each Warning or Error, sorted by line.</summary>
		static List<string> RunLanguageServer(string luals, string workspace, string logs)
		{
			var psi = new ProcessStartInfo(luals)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			psi.ArgumentList.Add("--check=" + workspace);
			psi.ArgumentList.Add("--checklevel=Warning");
			psi.ArgumentList.Add("--check_format=json");
			psi.ArgumentList.Add("--logpath=" + logs);

			using (Process p = Process.Start(psi))
			{
				p.StandardOutput.ReadToEndAsync();
				p.StandardError.ReadToEndAsync();

				if (!p.WaitForExit(180000))
				{
					p.Kill(true);
					throw new TimeoutException("lua-language-server did not finish in 180 s: " + workspace);
				}
			}

			// LuaLS writes [] when it finds nothing and {uri: [diagnostic]} otherwise. No file means it did
			// not run, which must not pass as "no diagnostics".
			string report = Path.Combine(logs, "check.json");
			if (!File.Exists(report))
				throw new InvalidOperationException("lua-language-server wrote no " + report);

			var found = new List<string>();
			using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(report)))
			{
				if (doc.RootElement.ValueKind == JsonValueKind.Object)
				{
					foreach (JsonProperty file in doc.RootElement.EnumerateObject())
					{
						foreach (JsonElement d in file.Value.EnumerateArray())
						{
							int line = d.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32() + 1;
							string message = d.GetProperty("message").GetString().Split('\n')[0];
							found.Add(string.Format("{0} {1} {2}: {3}", line, d.GetProperty("code").GetString(),
								Path.GetFileName(Uri.UnescapeDataString(file.Name)), message));
						}
					}
				}
			}

			return found.OrderBy(f => int.Parse(f.Split(' ')[0])).ThenBy(f => f, StringComparer.Ordinal).ToList();
		}
	}
}
