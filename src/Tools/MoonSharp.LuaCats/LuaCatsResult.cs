using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace MoonSharp.LuaCats
{
	/// <summary>
	/// The output of <see cref="LuaCatsGenerator.Generate"/>: definition files, the lua-language-server settings
	/// they need, and what could not be described exactly.
	/// </summary>
	public sealed class LuaCatsResult
	{
		readonly IList<string> m_DisabledBuiltins;

		internal LuaCatsResult(IDictionary<string, string> files, IList<string> warnings, IList<string> disabledBuiltins)
		{
			Files = new ReadOnlyDictionary<string, string>(files);
			Warnings = new ReadOnlyCollection<string>(warnings);
			m_DisabledBuiltins = disabledBuiltins;
		}

		/// <summary>File name to content, in name order. Every file is a LuaCATS meta file.</summary>
		public IDictionary<string, string> Files { get; private set; }

		/// <summary>
		/// Members declared as <c>any</c> or <c>nil</c> because a script cannot use them as their CLR types
		/// suggest (for example a return type that is not registered), and standard functions lua-language-server
		/// still offers although the script lacks them. A host that registers an explicit list of types should
		/// keep this empty.
		/// </summary>
		public IList<string> Warnings { get; private set; }

		/// <summary>
		/// A <c>.luarc.json</c> for the workspace: Lua 5.2, the definitions folder as a library, and lua-language-server's
		/// own standard library turned off for the modules the script does not have (and for the ones these files
		/// declare themselves).
		/// </summary>
		/// <param name="libraryPath">The definitions folder, relative to the <c>.luarc.json</c> (use '/').</param>
		public string CreateLuarc(string libraryPath)
		{
			var sb = new StringBuilder();
			sb.Append("{\n  \"runtime.version\": \"Lua 5.2\",\n  \"runtime.builtin\": {\n");
			sb.Append(string.Join(",\n", m_DisabledBuiltins.Select(b => "    \"" + b + "\": \"disable\"").ToArray()));
			sb.Append("\n  },\n  \"workspace.library\": [\n    \"");
			sb.Append(libraryPath.Replace("\\", "\\\\").Replace("\"", "\\\""));
			sb.Append("\"\n  ]\n}\n");
			return sb.ToString();
		}

		/// <summary>
		/// Writes the files into a folder, creating it if needed, and deletes generated files (those starting with
		/// <see cref="LuaCatsGenerator.Banner"/> on their second line) that are no longer produced.
		/// </summary>
		public void WriteTo(string directory)
		{
			Directory.CreateDirectory(directory);

			foreach (string stale in GeneratedFiles(directory).Where(f => !Files.ContainsKey(Path.GetFileName(f))))
				File.Delete(stale);

			foreach (var file in Files)
				File.WriteAllText(Path.Combine(directory, file.Key), file.Value, new UTF8Encoding(false));
		}

		/// <summary>
		/// Compares a folder with these files: the names of files that are missing, different or no longer
		/// produced. Empty when the folder is up to date.
		/// </summary>
		public IList<string> Compare(string directory)
		{
			var differences = new List<string>();

			foreach (var file in Files)
			{
				string path = Path.Combine(directory, file.Key);

				if (!File.Exists(path))
					differences.Add(file.Key + ": missing");
				else if (File.ReadAllText(path).Replace("\r\n", "\n") != file.Value)
					differences.Add(file.Key + ": differs");
			}

			foreach (string stale in GeneratedFiles(directory).Where(f => !Files.ContainsKey(Path.GetFileName(f))))
				differences.Add(Path.GetFileName(stale) + ": no longer generated");

			return differences;
		}

		static IEnumerable<string> GeneratedFiles(string directory)
		{
			if (!Directory.Exists(directory))
				return Enumerable.Empty<string>();

			return Directory.GetFiles(directory, "*.lua").Where(f =>
			{
				string[] lines = File.ReadLines(f).Take(2).ToArray();
				return lines.Length == 2 && lines[1] == LuaCatsGenerator.Banner;
			}).ToList();
		}
	}
}
