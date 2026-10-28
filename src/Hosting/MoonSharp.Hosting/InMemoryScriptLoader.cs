using System.Collections.Generic;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace MoonSharp.Hosting
{
	/// <summary>
	/// Resolves require(name) against module sources registered with the host, so scripts run the
	/// same way without a file system (browser) or with one (server).
	/// </summary>
	public sealed class InMemoryScriptLoader : IScriptLoader
	{
		readonly Dictionary<string, string> m_Sources = new Dictionary<string, string>();

		public void Set(string moduleId, string source)
		{
			m_Sources[moduleId] = source;
		}

		public bool Remove(string moduleId)
		{
			return m_Sources.Remove(moduleId);
		}

		public bool Contains(string moduleId)
		{
			return m_Sources.ContainsKey(moduleId);
		}

		public object LoadFile(string file, Table globalContext)
		{
			string source;

			if (!m_Sources.TryGetValue(file, out source))
				throw new ScriptRuntimeException("module '{0}' not found", file);

			return source;
		}

		public string ResolveFileName(string filename, Table globalContext)
		{
			return filename;
		}

		public string ResolveModuleName(string modname, Table globalContext)
		{
			return m_Sources.ContainsKey(modname) ? modname : null;
		}
	}
}
