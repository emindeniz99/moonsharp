using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MoonSharp.Interpreter.Loaders
{
	/// <summary>
	/// A base implementation of IScriptLoader, offering resolution of module names.
	/// </summary>
	public abstract class ScriptLoaderBase : IScriptLoader
	{
		/// <summary>
		/// Checks if a script file exists. 
		/// </summary>
		/// <param name="name">The script filename.</param>
		/// <returns></returns>
		public abstract bool ScriptFileExists(string name);

		/// <summary>
		/// Opens a file for reading the script code.
		/// It can return either a string, a byte[] or a Stream.
		/// If a byte[] is returned, the content is assumed to be a serialized (dumped) bytecode. If it's a string, it's
		/// assumed to be either a script or the output of a string.dump call. If a Stream, autodetection takes place.
		/// </summary>
		/// <param name="file">The file.</param>
		/// <param name="globalContext">The global context.</param>
		/// <returns>
		/// A string, a byte[] or a Stream.
		/// </returns>
		public abstract object LoadFile(string file, Table globalContext);


		/// <summary>
		/// Resolves the name of a module on a set of paths.
		/// </summary>
		/// <param name="modname">The modname.</param>
		/// <param name="paths">The paths.</param>
		/// <returns></returns>
		protected virtual string ResolveModuleName(string modname, string[] paths)
		{
			if (paths == null) 
				return null;

			foreach (string path in paths)
			{
				string file = ExpandPath(path, modname);

				if (ScriptFileExists(file))
					return file;
			}

			return null;
		}

		static string ExpandPath(string path, string modname)
		{
			return path.Replace("?", modname.Replace('.', '/'));
		}

		/// <summary>
		/// Resolves the name of a module to a filename (which will later be passed to OpenScriptFile).
		/// The resolution tries the paths in package.path first, then those of the LUA_PATH global variable,
		/// or of the ModulePaths array when that variable does not exist, that package.path does not list.
		/// With IgnoreLuaPathGlobal, only ModulePaths is used. Override to provide a different behaviour.
		/// </summary>
		/// <param name="modname">The modname.</param>
		/// <param name="globalContext">The global context.</param>
		/// <returns></returns>
		public virtual string ResolveModuleName(string modname, Table globalContext)
		{
			return ResolveModuleName(modname, GetModulePaths(globalContext));
		}

		string[] GetModulePaths(Table globalContext)
		{
			if (this.IgnoreLuaPathGlobal)
				return this.ModulePaths;

			string[] paths = this.ModulePaths;
			DynValue luaPath = globalContext.RawGet("LUA_PATH");

			if (luaPath != null && luaPath.Type == DataType.String)
				paths = UnpackStringPaths(luaPath.String);

			// package.path holds the script's own paths (it starts empty), searched before the host's.
			DynValue package = globalContext.RawGet("package");
			DynValue packagePath = (package != null && package.Type == DataType.Table) ? package.Table.RawGet("path") : null;

			if (packagePath == null || packagePath.Type != DataType.String)
				return paths;

			var all = new List<string>(UnpackStringPaths(packagePath.String));

			if (paths != null)
				foreach (string path in paths)
					if (!all.Contains(path))
						all.Add(path);

			return all.ToArray();
		}

		/// <summary>
		/// The files that the default resolution tries for a module, as require lists them when it fails.
		/// </summary>
		internal string DescribeMissingModule(string modname, Table globalContext)
		{
			var sb = new StringBuilder();
			string[] paths = GetModulePaths(globalContext);

			if (paths != null)
				foreach (string path in paths)
					sb.Append("\n\tno file '").Append(ExpandPath(path, modname)).Append('\'');

			return sb.ToString();
		}

		/// <summary>
		/// Gets or sets the modules paths used by the "require" function. If null, the default paths are used (using
		/// environment variables etc.). 
		/// </summary>
		public string[] ModulePaths { get; set; }

		/// <summary>
		/// Unpacks a string path in a form like "?;?.lua" to an array
		/// </summary>
		public static string[] UnpackStringPaths(string str)
		{
			return str.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => s.Trim())
				.Where(s => !string.IsNullOrEmpty(s))
				.ToArray();
		}

		/// <summary>
		/// Gets the default environment paths.
		/// </summary>
		public static string[] GetDefaultEnvironmentPaths()
		{
			string[] modulePaths = null;

			if (modulePaths == null)
			{
				string env = Script.GlobalOptions.Platform.GetEnvironmentVariable("MOONSHARP_PATH");
				if (!string.IsNullOrEmpty(env)) modulePaths = UnpackStringPaths(env);

				if (modulePaths == null)
				{
					env = Script.GlobalOptions.Platform.GetEnvironmentVariable("LUA_PATH");
					if (!string.IsNullOrEmpty(env)) modulePaths = UnpackStringPaths(env);
				}

				if (modulePaths == null)
					modulePaths = UnpackStringPaths("?;?.lua");
			}

			return modulePaths;
		}



		/// <summary>
		/// Resolves a filename [applying paths, etc.]
		/// </summary>
		/// <param name="filename">The filename.</param>
		/// <param name="globalContext">The global context.</param>
		/// <returns></returns>
		public virtual string ResolveFileName(string filename, Table globalContext)
		{
			return filename;
		}

		/// <summary>
		/// Gets or sets a value indicating whether the LUA_PATH global and package.path are checked or not to get the path where modules
		/// are contained. If true, neither is checked, and only ModulePaths is used.
		/// </summary>
		public bool IgnoreLuaPathGlobal { get; set; }
	}
}
