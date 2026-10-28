using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace MoonSharp.Hosting.Cli
{
	/// <summary>
	/// A long-running worker that a JVM (or any other process) drives over stdin/stdout.
	///
	/// Each request is one line of JSON:
	///   { "id": 1, "op": "load", "context": "c1", "module": "offers", "source": "..." }
	///   { "id": 2, "op": "invoke", "context": "c1", "module": "offers", "function": "select", "args": [ ... ] }
	///   ops: load, invoke, run, validate, unload, dispose, version
	/// Each response is one line: { "id": 1, "result": { ...ScriptHost envelope... } }
	/// The worker answers requests in order. Run several workers for parallelism.
	/// </summary>
	public sealed class Worker
	{
		readonly TextReader m_In;
		readonly TextWriter m_Out;
		readonly Dictionary<string, ScriptHost> m_Contexts = new Dictionary<string, ScriptHost>();

		public Worker(TextReader input, TextWriter output)
		{
			m_In = input;
			m_Out = output;
		}

		public int Serve()
		{
			string line;

			while ((line = m_In.ReadLine()) != null)
			{
				if (line.Length == 0)
					continue;

				m_Out.Write(Handle(line));
				m_Out.Write('\n');
				m_Out.Flush();
			}

			return 0;
		}

		public string Handle(string line)
		{
			string id = "null";
			string result;

			try
			{
				JsonNode request = JsonNode.Parse(line);
				id = request["id"]?.ToJsonString() ?? "null";
				result = Dispatch(request);
			}
			catch (Exception ex)
			{
				var sb = new StringBuilder("{\"ok\":false,\"error\":{\"kind\":\"host\",\"message\":");
				LuaJson.WriteString(ex.GetType().Name + ": " + ex.Message, sb);
				sb.Append(",\"chunk\":null,\"line\":null,\"stack\":[]},\"output\":\"\"}");
				result = sb.ToString();
			}

			return "{\"id\":" + id + ",\"result\":" + result + "}";
		}

		string Dispatch(JsonNode r)
		{
			string op = (string)r["op"];
			string contextId = (string)r["context"] ?? "default";

			switch (op)
			{
				case "version":
					return Ok("\"" + ScriptHost.EngineVersion + "\"");
				case "dispose":
					return Ok(m_Contexts.Remove(contextId) ? "true" : "false");
				case "unload":
					return Ok(Context(contextId).Unload((string)r["module"]) ? "true" : "false");
				case "load":
					return Context(contextId).Load((string)r["module"], (string)r["source"]);
				case "invoke":
					return Context(contextId).Invoke((string)r["module"], (string)r["function"], Raw(r, "args") ?? "[]");
				case "run":
					return Context(contextId).Run((string)r["chunk"] ?? "chunk", (string)r["source"], Raw(r, "input"));
				case "validate":
					return Context(contextId).Validate((string)r["chunk"] ?? "chunk", (string)r["source"]);
				default:
					throw new InvalidDataException("unknown op '" + op + "'");
			}
		}

		// Accepts either embedded JSON ("args": [...]) or JSON text ("argsJson": "[...]"). Embedded values
		// keep their original number text, so integers beyond 2^53 still reach the host's range check.
		static string Raw(JsonNode r, string name)
		{
			string text = (string)r[name + "Json"];
			return text ?? r[name]?.ToJsonString();
		}

		ScriptHost Context(string id)
		{
			ScriptHost host;

			if (!m_Contexts.TryGetValue(id, out host))
				m_Contexts[id] = host = new ScriptHost();

			return host;
		}

		static string Ok(string valueJson)
		{
			return "{\"ok\":true,\"value\":" + valueJson + ",\"output\":\"\",\"timing\":{}}";
		}
	}
}
