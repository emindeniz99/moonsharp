using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoonSharp.Hosting.Cli
{
	/// <summary>
	/// Runs fixture suites (see src/Hosting/fixtures/README.md) and compares each result with the expected
	/// value. The browser and Java runners implement the same comparison, so a suite that passes on all
	/// three proves the hosts agree.
	/// </summary>
	public static class FixtureRunner
	{
		public static int Run(string[] args)
		{
			string reportPath = null;
			var suites = new List<string>();

			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == "--report" && i + 1 < args.Length)
					reportPath = args[++i];
				else
					suites.Add(args[i]);
			}

			if (suites.Count == 0)
			{
				Console.Error.WriteLine("usage: moonsharp-host test <suite.json>... [--report out.json]");
				return 2;
			}

			var report = new JsonObject
			{
				["engine"] = ScriptHost.EngineVersion,
				["platform"] = ".NET " + Environment.Version,
			};

			var suiteReports = new JsonArray();
			int passed = 0, failed = 0;

			foreach (string suitePath in suites)
			{
				JsonObject suiteReport = RunSuite(suitePath);
				suiteReports.Add(suiteReport);

				foreach (JsonNode c in suiteReport["cases"].AsArray())
				{
					bool ok = (bool)c["pass"];
					if (ok) ++passed; else ++failed;
					Console.WriteLine("{0}  {1} / {2}{3}", ok ? "pass" : "FAIL", suiteReport["suite"], c["name"], ok ? "" : ": " + c["detail"]);
				}
			}

			report["suites"] = suiteReports;
			report["passed"] = passed;
			report["failed"] = failed;

			if (reportPath != null)
				File.WriteAllText(reportPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

			Console.WriteLine("{0} passed, {1} failed", passed, failed);
			return failed == 0 ? 0 : 1;
		}

		public static JsonObject RunSuite(string suitePath)
		{
			JsonNode suite = JsonNode.Parse(File.ReadAllText(suitePath));
			string baseDir = Path.GetDirectoryName(Path.GetFullPath(suitePath));
			var host = new ScriptHost();
			var cases = new JsonArray();

			foreach (var module in suite["modules"].AsObject())
			{
				string source = File.ReadAllText(Path.Combine(baseDir, (string)module.Value));
				JsonNode loaded = JsonNode.Parse(host.Load(module.Key, source));

				if (!(bool)loaded["ok"])
					cases.Add(new JsonObject { ["name"] = "load " + module.Key, ["pass"] = false, ["detail"] = loaded["error"].ToJsonString() });
			}

			foreach (JsonNode c in suite["cases"].AsArray())
			{
				string actual = Execute(host, c);
				string detail = Check(c, JsonNode.Parse(actual));
				cases.Add(new JsonObject { ["name"] = (string)c["name"], ["pass"] = detail == null, ["detail"] = detail });
			}

			return new JsonObject { ["suite"] = (string)suite["suite"], ["cases"] = cases };
		}

		static string Execute(ScriptHost host, JsonNode c)
		{
			string op = (string)c["op"] ?? "invoke";

			switch (op)
			{
				case "invoke":
					return host.Invoke((string)c["module"], (string)c["function"], ArgsJson(c));
				case "validate":
					return host.Validate((string)c["chunk"] ?? (string)c["name"], (string)c["source"]);
				case "run":
					return host.Run((string)c["chunk"] ?? (string)c["name"], (string)c["source"], c["input"]?.ToJsonString());
				default:
					throw new InvalidDataException("unknown op '" + op + "'");
			}
		}

		// argsJson carries raw JSON text, for inputs a JSON parser would change (integers beyond 2^53).
		static string ArgsJson(JsonNode c)
		{
			return (string)c["argsJson"] ?? c["args"]?.ToJsonString() ?? "[]";
		}

		/// <summary>Returns null when the envelope matches the case's expectation, or a description of the mismatch.</summary>
		public static string Check(JsonNode c, JsonNode envelope)
		{
			JsonNode expectError = c["expectError"];

			if (expectError == null)
			{
				if (!(bool)envelope["ok"])
					return "unexpected error: " + envelope["error"].ToJsonString();

				return Diff(c["expect"], envelope["value"], "$");
			}

			if ((bool)envelope["ok"])
				return "expected an error, got " + (envelope["value"]?.ToJsonString() ?? "null");

			JsonNode error = envelope["error"];
			string kind = (string)expectError["kind"];
			string contains = (string)expectError["messageContains"];

			if (kind != null && kind != (string)error["kind"])
				return string.Format("error kind: expected {0}, got {1} ({2})", kind, error["kind"], error["message"]);

			if (contains != null && !((string)error["message"]).Contains(contains, StringComparison.Ordinal))
				return string.Format("error message: expected to contain \"{0}\", got \"{1}\"", contains, error["message"]);

			if (expectError["line"] != null && (int)expectError["line"] != ((int?)error["line"] ?? -1))
				return string.Format("error line: expected {0}, got {1}", expectError["line"], error["line"]?.ToJsonString() ?? "null");

			return null;
		}

		/// <summary>Structural comparison: object key order is ignored, numbers compare by value.</summary>
		public static string Diff(JsonNode expected, JsonNode actual, string path)
		{
			JsonValueKind ek = Kind(expected), ak = Kind(actual);

			if (ek != ak && !(IsBool(ek) && IsBool(ak)))
				return string.Format("{0}: expected {1}, got {2}", path, Show(expected), Show(actual));

			switch (ek)
			{
				case JsonValueKind.Object:
				{
					var eo = expected.AsObject();
					var ao = actual.AsObject();

					foreach (var p in eo)
					{
						if (!ao.ContainsKey(p.Key))
							return string.Format("{0}.{1}: missing (expected {2})", path, p.Key, Show(p.Value));

						string d = Diff(p.Value, ao[p.Key], path + "." + p.Key);
						if (d != null) return d;
					}

					foreach (var p in ao)
						if (!eo.ContainsKey(p.Key))
							return string.Format("{0}.{1}: unexpected key (value {2})", path, p.Key, Show(p.Value));

					return null;
				}
				case JsonValueKind.Array:
				{
					var ea = expected.AsArray();
					var aa = actual.AsArray();

					if (ea.Count != aa.Count)
						return string.Format("{0}: expected {1} items, got {2} ({3})", path, ea.Count, aa.Count, Show(actual));

					for (int i = 0; i < ea.Count; i++)
					{
						string d = Diff(ea[i], aa[i], path + "[" + i + "]");
						if (d != null) return d;
					}

					return null;
				}
				case JsonValueKind.Number:
					return NumbersEqual(expected.ToJsonString(), actual.ToJsonString()) ? null
						: string.Format("{0}: expected {1}, got {2}", path, Show(expected), Show(actual));
				case JsonValueKind.Null:
					return null;
				default:
					return expected.ToJsonString() == actual.ToJsonString() ? null
						: string.Format("{0}: expected {1}, got {2}", path, Show(expected), Show(actual));
			}
		}

		static bool NumbersEqual(string a, string b)
		{
			decimal da, db;

			if (decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out da) &&
				decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out db))
				return da == db;

			return double.Parse(a, CultureInfo.InvariantCulture) == double.Parse(b, CultureInfo.InvariantCulture);
		}

		static JsonValueKind Kind(JsonNode n)
		{
			return n == null ? JsonValueKind.Null : n.GetValueKind();
		}

		static bool IsBool(JsonValueKind k)
		{
			return k == JsonValueKind.True || k == JsonValueKind.False;
		}

		static string Show(JsonNode n)
		{
			return n == null ? "null" : n.ToJsonString();
		}
	}
}
