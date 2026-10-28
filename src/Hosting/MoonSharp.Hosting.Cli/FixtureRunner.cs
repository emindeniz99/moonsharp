using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoonSharp.Interpreter;

namespace MoonSharp.Hosting.Cli
{
	/// <summary>
	/// Runs fixture suites (see src/Hosting/fixtures/README.md) and compares each result with the expected
	/// value. The browser and Java runners implement the same comparison and the same known-failures check,
	/// so a corpus that passes on all of them proves the hosts agree.
	/// </summary>
	public static class FixtureRunner
	{
		const string Usage = "usage: moonsharp-host test [--bytecode] [--host name] [--report out.json] <suite.json | directory>...";

		public static int Run(string[] args)
		{
			string reportPath = null, hostName = null;
			bool bytecode = false;
			var suites = new List<string>();

			for (int i = 0; i < args.Length; i++)
			{
				if (args[i] == "--report" && i + 1 < args.Length)
					reportPath = args[++i];
				else if (args[i] == "--host" && i + 1 < args.Length)
					hostName = args[++i];
				else if (args[i] == "--bytecode")
					bytecode = true;
				else if (Directory.Exists(args[i]))
					suites.AddRange(Directory.GetFiles(args[i], "*.suite.json").OrderBy(f => f, StringComparer.Ordinal));
				else
					suites.Add(args[i]);
			}

			string[] dirs = suites.Select(s => Path.GetDirectoryName(Path.GetFullPath(s))).Distinct().ToArray();

			if (suites.Count == 0 || dirs.Length > 1)
			{
				Console.Error.WriteLine(dirs.Length > 1 ? "all suites must be in one directory (it holds known-failures/)" : Usage);
				return 2;
			}

			// The bytecode run is a host of its own: it may fail where source passes, and needs its own file.
			// --host names other variants, such as a run under another culture.
			hostName = hostName ?? (bytecode ? "dotnet-bytecode" : "dotnet");
			var known = new KnownFailures(dirs[0], hostName);

			var report = new JsonObject
			{
				["engine"] = ScriptHost.EngineVersion,
				["platform"] = ".NET " + Environment.Version,
				["host"] = hostName,
			};

			var suiteReports = new JsonArray();

			foreach (string suitePath in suites)
			{
				JsonObject suiteReport = RunSuite(suitePath, bytecode);
				suiteReports.Add(suiteReport);
				string suite = (string)suiteReport["suite"];

				foreach (JsonNode c in suiteReport["cases"].AsArray())
				{
					string line = known.Record(suite, (string)c["name"], (string)c["detail"]);
					c["outcome"] = line.Substring(0, line.IndexOf(' '));
					Console.WriteLine(line);
				}
			}

			foreach (string line in known.Stale())
				Console.WriteLine(line);

			report["suites"] = suiteReports;
			report["summary"] = known.Summary();

			if (reportPath != null)
				File.WriteAllText(reportPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

			Console.WriteLine(known.Summary());

			if (known.Ok)
				return 0;

			Console.WriteLine("The known failures changed. If that is intended, write this to " + known.Path + ":");
			Console.WriteLine(known.Expected());
			return 1;
		}

		// Cases go up to the host's nesting limit (128), beyond System.Text.Json's default of 64.
		static readonly JsonDocumentOptions Deep = new JsonDocumentOptions { MaxDepth = 256 };

		public static JsonObject RunSuite(string suitePath, bool bytecode)
		{
			JsonNode suite = JsonNode.Parse(File.ReadAllText(suitePath), null, Deep);
			string baseDir = Path.GetDirectoryName(Path.GetFullPath(suitePath));
			JsonNode context = suite["context"];
			var host = ScriptHost.Create((bool?)context?["int64"] ?? false);
			host.InstructionLimit = (long?)context?["instructionLimit"] ?? 0;
			host.TimeLimitMs = (int?)context?["timeLimitMs"] ?? 0;
			var cases = new JsonArray();

			foreach (var module in suite["modules"]?.AsObject() ?? new JsonObject())
			{
				string source = File.ReadAllText(Path.Combine(baseDir, (string)module.Value));
				string compileError;
				string code = Code(host, module.Key, source, bytecode, out compileError);
				JsonNode loaded = JsonNode.Parse(compileError ?? host.Load(module.Key, code));

				if (!(bool)loaded["ok"])
					cases.Add(new JsonObject { ["name"] = "load " + module.Key, ["pass"] = false, ["detail"] = loaded["error"].ToJsonString() });
			}

			var names = new HashSet<string>(StringComparer.Ordinal);

			foreach (JsonNode c in suite["cases"].AsArray())
			{
				string name = (string)c["name"];

				if (!names.Add(name))
					throw new InvalidDataException(suitePath + ": duplicate case name '" + name + "'");

				string detail;

				try
				{
					detail = Check(c, JsonNode.Parse(Execute(host, c, bytecode), null, Deep));
				}
				catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is ArgumentException)
				{
					// The envelope could not be read, for example a string with an unpaired surrogate.
					detail = "runner: " + ex.GetType().Name + ": " + ex.Message;
				}

				cases.Add(new JsonObject { ["name"] = name, ["pass"] = detail == null, ["detail"] = detail });
			}

			return new JsonObject { ["suite"] = (string)suite["suite"], ["cases"] = cases };
		}

		static string Execute(ScriptHost host, JsonNode c, bool bytecode)
		{
			string op = (string)c["op"] ?? "invoke";
			string chunk = (string)c["chunk"] ?? (string)c["name"];
			string compileError;

			switch (op)
			{
				case "invoke":
					return host.Invoke((string)c["module"], (string)c["function"], ArgsJson(c));
				case "validate":
				{
					string code = Code(host, chunk, (string)c["source"], bytecode, out compileError);
					return compileError ?? host.Validate(chunk, code);
				}
				case "run":
				{
					string code = Code(host, chunk, (string)c["source"], bytecode, out compileError);
					return compileError ?? host.Run(chunk, code, (string)c["inputJson"] ?? c["input"]?.ToJsonString());
				}
				default:
					throw new InvalidDataException("unknown op '" + op + "'");
			}
		}

		/// <summary>
		/// The code a host receives: the source, or in bytecode mode the chunk compiled as `moonsharp-host compile`
		/// does it at publish time. A chunk that does not compile yields the envelope a host reports for it.
		/// </summary>
		static string Code(ScriptHost host, string chunk, string source, bool bytecode, out string compileError)
		{
			compileError = null;

			if (!bytecode)
				return source;

			try
			{
				return Program.DumpText(Program.Compile(chunk, source));
			}
			catch (SyntaxErrorException)
			{
				compileError = host.Validate(chunk, source);
				return null;
			}
		}

		// argsJson and inputJson carry raw JSON text, for inputs a JSON parser would change (integers beyond
		// 2^53) or reject (invalid JSON).
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

	/// <summary>
	/// The two-way check against known-failures/&lt;host&gt;.json, as quickjs-ng checks test262: a new failure,
	/// a failure whose detail changed, a listed failure that now passes and a listed case that no longer
	/// exists all fail the run, so the file always says exactly what fails on the host.
	/// The browser (test/smoke.mjs) and Java (FixtureConformanceTest) runners apply the same rules.
	/// </summary>
	public sealed class KnownFailures
	{
		readonly SortedDictionary<string, string> m_Known = new SortedDictionary<string, string>(StringComparer.Ordinal);
		readonly SortedDictionary<string, string> m_Actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
		readonly HashSet<string> m_Seen = new HashSet<string>(StringComparer.Ordinal);
		readonly HashSet<string> m_Suites = new HashSet<string>(StringComparer.Ordinal);
		readonly string m_Host;
		int m_Cases, m_Passed, m_KnownFailed, m_New, m_Changed, m_Fixed, m_Stale;

		public KnownFailures(string fixturesDir, string host)
		{
			m_Host = host;
			Path = System.IO.Path.Combine(fixturesDir, "known-failures", host + ".json");

			if (File.Exists(Path))
				foreach (var entry in JsonNode.Parse(File.ReadAllText(Path)).AsObject())
					m_Known[entry.Key] = (string)entry.Value;
		}

		public string Path { get; private set; }

		public bool Ok { get { return m_New + m_Changed + m_Fixed + m_Stale == 0; } }

		/// <summary>Records one case result (detail is null when it passed) and returns the line to print.</summary>
		public string Record(string suite, string name, string detail)
		{
			string key = suite + " / " + name;
			string expected;
			bool listed = m_Known.TryGetValue(key, out expected);

			m_Suites.Add(suite);
			m_Seen.Add(key);
			++m_Cases;

			if (detail != null)
				m_Actual[key] = detail;

			if (detail == null && !listed)
			{
				++m_Passed;
				return "pass  " + key;
			}

			if (detail == null)
			{
				++m_Fixed;
				return "FIXED " + key + ": passes now, remove it from known-failures/" + m_Host + ".json";
			}

			if (!listed)
			{
				++m_New;
				return "FAIL  " + key + ": " + detail;
			}

			if (expected != detail)
			{
				++m_Changed;
				return "CHANGED " + key + ": " + detail + " (known: " + expected + ")";
			}

			++m_KnownFailed;
			return "known " + key + ": " + detail;
		}

		/// <summary>Listed cases of the suites that ran which do not exist any more.</summary>
		public List<string> Stale()
		{
			var lines = new List<string>();

			foreach (string key in m_Known.Keys)
				if (m_Suites.Contains(SuiteOf(key)) && !m_Seen.Contains(key))
					lines.Add("STALE " + key + ": listed in known-failures/" + m_Host + ".json, but there is no such case");

			m_Stale = lines.Count;
			return lines;
		}

		static string SuiteOf(string key)
		{
			int sep = key.IndexOf(" / ", StringComparison.Ordinal);
			return sep < 0 ? key : key.Substring(0, sep);
		}

		public string Summary()
		{
			return string.Format(CultureInfo.InvariantCulture,
				"{0}: {1} cases, {2} passed, {3} known failures; {4} new, {5} changed, {6} fixed, {7} stale",
				m_Host, m_Cases, m_Passed, m_KnownFailed, m_New, m_Changed, m_Fixed, m_Stale);
		}

		/// <summary>The file content that matches this run: its failures, plus listed failures of suites that did not run.</summary>
		public string Expected()
		{
			var o = new JsonObject();

			foreach (var e in m_Known)
				if (!m_Suites.Contains(SuiteOf(e.Key)))
					m_Actual[e.Key] = e.Value;

			foreach (var e in m_Actual)
				o[e.Key] = e.Value;

			return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
		}
	}
}
