using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Hosting
{
	/// <summary>
	/// One script context with a JSON-in, JSON-out API. The browser (WebAssembly), the JVM worker and the
	/// .NET CLI all wrap this class, so the same script sees the same values everywhere.
	///
	/// Every method returns a result envelope as JSON text and never throws for script problems:
	///   { "ok": true,  "value": ..., "output": "...", "timing": { "compileMs": n, "runMs": n } }
	///   { "ok": false, "error": { "kind": "...", "message": "...", "chunk": "...", "line": n, "stack": [...] }, "output": "..." }
	/// kind is "syntax", "runtime", "input", "output", "host" or "interrupted".
	///
	/// A ScriptHost is not thread-safe. Use one per thread, or a pool.
	/// </summary>
	public sealed class ScriptHost
	{
		/// <summary>Version of the JSON contract. Bump it when a value mapping or the envelope changes.</summary>
		public const int ContractVersion = 1;

		readonly Script m_Script;
		readonly LuaJson m_Json;
		readonly InMemoryScriptLoader m_Loader = new InMemoryScriptLoader();
		readonly Dictionary<string, DynValue> m_FunctionCache = new Dictionary<string, DynValue>();
		readonly StringBuilder m_Output = new StringBuilder();
		long m_Deadline;
		string m_Interruption;

		/// <summary>
		/// The modules every host supports: everything except io, os system calls and debug, which have
		/// no browser equivalent (opening stdin throws PlatformNotSupportedException on WebAssembly).
		/// Using one set everywhere keeps a script from working on the server and failing in the browser.
		/// </summary>
		public const CoreModules PortableModules = CoreModules.Preset_SoftSandbox | CoreModules.LoadMethods;

		public ScriptHost()
			: this(PortableModules)
		{
		}

		/// <summary>
		/// A context with <see cref="PortableModules"/>, plus the int64 and uint64 modules when int64 is true
		/// (see docs/int64.md). Every transport creates contexts through this method, so they offer the same choice.
		/// </summary>
		public static ScriptHost Create(bool int64)
		{
			return new ScriptHost(int64 ? PortableModules | CoreModules.Int64 : PortableModules);
		}

		public ScriptHost(CoreModules modules)
		{
			m_Script = new Script(modules);
			m_Script.Options.ScriptLoader = m_Loader;
			m_Script.Options.DebugPrint = s => m_Output.Append(s).Append('\n');
			m_Script.Options.InterruptHandler = CheckLimits;
			m_Json = new LuaJson(m_Script);
		}

		/// <summary>
		/// Instructions one call into the script may run, or 0 for no limit. Every operation that runs Lua counts
		/// on its own: load runs the module body, invoke the function, and run the chunk and then the function it
		/// returns. A script over the limit stops with error kind "interrupted"; pcall cannot catch it, and the
		/// context stays usable. The VM checks every 10,000 instructions, and the count is the same on every host.
		/// </summary>
		public long InstructionLimit { get; set; }

		/// <summary>
		/// Milliseconds one operation may run Lua, or 0 for no limit. Checked with the instruction limit, every
		/// 10,000 instructions; a single long library call (one string.rep or gsub) is not interrupted.
		/// </summary>
		public int TimeLimitMs { get; set; }

		/// <summary>The underlying script, for hosts that also expose CLR objects or call functions directly.</summary>
		public Script Script { get { return m_Script; } }

		public static string EngineVersion
		{
			get { return string.Format(CultureInfo.InvariantCulture, "moonsharp {0} (lua {1}, contract {2})", Script.VERSION, Script.LUA_VERSION, ContractVersion); }
		}

		/// <summary>Compiles source without running it. Use it to reject scripts with syntax errors before publishing.</summary>
		public string Validate(string chunkName, string source)
		{
			return Execute(() =>
			{
				m_Script.LoadString(source, null, chunkName);
				return null;
			}, true);
		}

		/// <summary>
		/// Registers a module and runs it, as require(moduleId) would. Loading the same id again replaces the
		/// module and drops cached functions, so a new script version takes effect on the next call.
		/// The result lists the function names the module exports: { "module": id, "functions": [...] }.
		/// </summary>
		public string Load(string moduleId, string source)
		{
			return Execute(() =>
			{
				Unload(moduleId);
				m_Loader.Set(moduleId, source);
				return Describe(moduleId, Require(moduleId));
			}, false);
		}

		DynValue Describe(string moduleId, DynValue module)
		{
			var names = new List<string>();

			if (module.Type == DataType.Table)
				foreach (TablePair pair in module.Table.Pairs)
					if (pair.Key.Type == DataType.String && (pair.Value.Type == DataType.Function || pair.Value.Type == DataType.ClrFunction))
						names.Add(pair.Key.String);

			names.Sort(StringComparer.Ordinal);

			var functions = new Table(m_Script);
			functions.MetaTable = m_Json.ArrayMetatable;

			for (int i = 0; i < names.Count; i++)
				functions.Set(i + 1, DynValue.NewString(names[i]));

			var result = new Table(m_Script);
			result.Set("module", DynValue.NewString(moduleId));
			result.Set("functions", DynValue.NewTable(functions));
			return DynValue.NewTable(result);
		}

		public bool Unload(string moduleId)
		{
			Table loaded = PackageLoaded();

			if (loaded != null)
				loaded.Set(moduleId, DynValue.Nil);

			var stale = new List<string>();

			foreach (string key in m_FunctionCache.Keys)
				if (key.StartsWith(moduleId + "\n", StringComparison.Ordinal))
					stale.Add(key);

			foreach (string key in stale)
				m_FunctionCache.Remove(key);

			return m_Loader.Remove(moduleId);
		}

		/// <summary>
		/// Calls a function exported by a loaded module. argsJson is a JSON array of arguments.
		/// When functionName is null or empty, the module's return value itself is called.
		/// </summary>
		public string Invoke(string moduleId, string functionName, string argsJson)
		{
			return Execute(() =>
			{
				DynValue[] args = m_Json.ParseArguments(argsJson);
				DynValue function = ResolveFunction(moduleId, functionName);
				return m_Script.Call(function, args);
			}, false);
		}

		/// <summary>
		/// Compiles and runs a chunk once. If the chunk returns a function, it is called with the parsed
		/// input (a JSON value, or nothing when inputJson is empty) and its result is returned.
		/// </summary>
		public string Run(string chunkName, string source, string inputJson)
		{
			return Execute(() =>
			{
				DynValue chunk = m_Script.LoadString(source, null, chunkName);
				DynValue result = m_Script.Call(chunk);

				if (result.Type == DataType.Function || result.Type == DataType.ClrFunction)
				{
					DynValue[] args = string.IsNullOrEmpty(inputJson) ? new DynValue[0] : new[] { m_Json.Parse(inputJson) };
					result = m_Script.Call(result, args);
				}

				return result;
			}, false);
		}

		DynValue Require(string moduleId)
		{
			DynValue require = m_Script.Globals.Get("require");

			if (require.Type != DataType.Function && require.Type != DataType.ClrFunction)
				throw new HostException("host", "require is not available: enable CoreModules.LoadMethods");

			return m_Script.Call(require, DynValue.NewString(moduleId));
		}

		Table PackageLoaded()
		{
			DynValue package = m_Script.Globals.Get("package");

			if (package.Type != DataType.Table)
				return null;

			DynValue loaded = package.Table.Get("loaded");
			return loaded.Type == DataType.Table ? loaded.Table : null;
		}

		DynValue ResolveFunction(string moduleId, string functionName)
		{
			string key = moduleId + "\n" + functionName;
			DynValue cached;

			if (m_FunctionCache.TryGetValue(key, out cached))
				return cached;

			if (!m_Loader.Contains(moduleId))
				throw new HostException("host", "module '" + moduleId + "' is not loaded");

			DynValue module = Require(moduleId);
			DynValue function = string.IsNullOrEmpty(functionName)
				? module
				: (module.Type == DataType.Table ? module.Table.Get(functionName) : DynValue.Nil);

			if (function.Type != DataType.Function && function.Type != DataType.ClrFunction)
				throw new HostException("host", string.IsNullOrEmpty(functionName)
					? "module '" + moduleId + "' did not return a function"
					: "module '" + moduleId + "' has no function '" + functionName + "'");

			m_FunctionCache[key] = function;
			return function;
		}

		bool CheckLimits(long executed)
		{
			if (InstructionLimit > 0 && executed >= InstructionLimit)
				m_Interruption = string.Format(CultureInfo.InvariantCulture, "instruction limit of {0} exceeded", InstructionLimit);
			else if (m_Deadline != 0 && Stopwatch.GetTimestamp() >= m_Deadline)
				m_Interruption = string.Format(CultureInfo.InvariantCulture, "time limit of {0} ms exceeded", TimeLimitMs);

			return m_Interruption != null;
		}

		string Execute(Func<DynValue> action, bool compileOnly)
		{
			m_Output.Length = 0;
			m_Interruption = null;
			var sb = new StringBuilder();
			long start = Stopwatch.GetTimestamp();
			m_Deadline = TimeLimitMs > 0 ? start + TimeLimitMs * Stopwatch.Frequency / 1000 : 0;

			try
			{
				DynValue value = action();
				double elapsedMs = ElapsedMs(start);

				// Serialize first: an unconvertible result must become an error envelope, not half a document.
				string valueJson = value == null ? "null" : m_Json.Serialize(value);

				sb.Append("{\"ok\":true,\"value\":").Append(valueJson);
				AppendOutput(sb);
				sb.Append(",\"timing\":{\"").Append(compileOnly ? "compileMs" : "runMs").Append("\":")
					.Append(elapsedMs.ToString("0.###", CultureInfo.InvariantCulture)).Append("}}");
			}
			catch (Exception ex)
			{
				sb.Length = 0;
				AppendError(sb, ex);
			}

			return sb.ToString();
		}

		void AppendOutput(StringBuilder sb)
		{
			sb.Append(",\"output\":");
			LuaJson.WriteString(m_Output.ToString(), sb);
		}

		void AppendError(StringBuilder sb, Exception ex)
		{
			string kind;
			string message = ex.Message;
			string chunk = null;
			int line = -1;
			var stack = new List<string>();

			var interpreterEx = ex as InterpreterException;
			var hostEx = ex as HostException;

			if (hostEx != null)
			{
				kind = hostEx.Kind;
			}
			else if (interpreterEx != null)
			{
				kind = ex is SyntaxErrorException ? "syntax" : ex is ScriptInterruptedException ? "interrupted" : "runtime";
				message = (ex is ScriptInterruptedException ? m_Interruption : interpreterEx.DecoratedMessage) ?? ex.Message;

				ParseLocation(message, out chunk, out line);

				if (interpreterEx.CallStack != null)
					foreach (var frame in interpreterEx.CallStack)
						stack.Add((frame.Name ?? "?") + " " + (frame.Location != null ? frame.Location.FormatLocation(m_Script) : "[clr]"));

				// The interruption message has no position; report where the script was stopped.
				if (ex is ScriptInterruptedException && interpreterEx.CallStack != null && interpreterEx.CallStack.Count > 0)
				{
					SourceRef at = interpreterEx.CallStack[0].Location;

					if (at != null && !at.IsClrLocation)
					{
						chunk = m_Script.GetSourceCode(at.SourceIdx).Name;
						line = at.FromLine;
					}
				}
			}
			else
			{
				// Trusted host code threw (for example a CLR callback). Report it instead of crashing the host.
				kind = "host";
				message = ex.GetType().FullName + ": " + ex.Message;
			}

			sb.Append("{\"ok\":false,\"error\":{\"kind\":");
			LuaJson.WriteString(kind, sb);
			sb.Append(",\"message\":");
			LuaJson.WriteString(message, sb);
			sb.Append(",\"chunk\":");

			if (chunk != null)
				LuaJson.WriteString(chunk, sb);
			else
				sb.Append("null");

			sb.Append(",\"line\":").Append(line >= 0 ? line.ToString(CultureInfo.InvariantCulture) : "null");
			sb.Append(",\"stack\":[");

			for (int i = 0; i < stack.Count; i++)
			{
				if (i > 0) sb.Append(',');
				LuaJson.WriteString(stack[i], sb);
			}

			sb.Append("]}");
			AppendOutput(sb);
			sb.Append('}');
		}

		/// <summary>
		/// Reads the position MoonSharp puts in front of a message, "chunk:(line,from-to): text": the chunk is
		/// everything before the first ":(" that is followed by digits and a comma. Plain code rather than a
		/// Regex keeps System.Text.RegularExpressions (94 KB brotli) out of the browser download.
		/// </summary>
		static void ParseLocation(string message, out string chunk, out int line)
		{
			for (int at = message.IndexOf(":(", StringComparison.Ordinal); at >= 0; at = message.IndexOf(":(", at + 1, StringComparison.Ordinal))
			{
				int end = at + 2;
				int value = 0;

				while (end < message.Length && message[end] >= '0' && message[end] <= '9' && end - at < 11)
					value = value * 10 + (message[end++] - '0');

				if (end > at + 2 && end < message.Length && message[end] == ',')
				{
					chunk = message.Substring(0, at);
					line = value;
					return;
				}
			}

			chunk = null;
			line = -1;
		}

		static double ElapsedMs(long start)
		{
			return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
		}
	}
}
