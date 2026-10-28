using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;

namespace MoonSharp.Hosting.Browser
{
	/// <summary>
	/// JavaScript entry points. Values cross the boundary as JSON text, so the browser sees exactly the
	/// envelopes the .NET and Java hosts produce. Each context is an independent ScriptHost.
	/// </summary>
	public static partial class Exports
	{
		static readonly Dictionary<int, ScriptHost> s_Contexts = new Dictionary<int, ScriptHost>();
		static int s_NextId = 1;

		public static void Main()
		{
		}

		[JSExport]
		public static string Version() => ScriptHost.EngineVersion;

		[JSExport]
		public static int CreateContext(bool int64)
		{
			int id = s_NextId++;
			s_Contexts[id] = ScriptHost.Create(int64);
			return id;
		}

		[JSExport]
		public static bool DisposeContext(int context) => s_Contexts.Remove(context);

		[JSExport]
		public static string Validate(int context, string chunkName, string source) => Get(context).Validate(chunkName, source);

		[JSExport]
		public static string Load(int context, string moduleId, string source) => Get(context).Load(moduleId, source);

		[JSExport]
		public static bool Unload(int context, string moduleId) => Get(context).Unload(moduleId);

		[JSExport]
		public static string Invoke(int context, string moduleId, string functionName, string argsJson) => Get(context).Invoke(moduleId, functionName, argsJson);

		[JSExport]
		public static string Run(int context, string chunkName, string source, string inputJson) => Get(context).Run(chunkName, source, inputJson);

		static ScriptHost Get(int context)
		{
			ScriptHost host;

			if (!s_Contexts.TryGetValue(context, out host))
				throw new KeyNotFoundException("no script context " + context);

			return host;
		}
	}
}
