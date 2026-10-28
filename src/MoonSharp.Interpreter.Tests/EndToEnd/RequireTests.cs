using System.Collections.Generic;
using MoonSharp.Interpreter.Loaders;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// require and the package table as Lua 5.2 defines them (manual §6.3), where hosts depend on it:
	/// package.preload, package.path, package.searchers, package.loaded and the "not found" message.
	/// </summary>
	[TestFixture]
	public class RequireTests
	{
		/// <summary>Serves module sources from a dictionary, through ScriptLoaderBase's path templates.</summary>
		sealed class FilesLoader : ScriptLoaderBase
		{
			readonly Dictionary<string, string> m_Files;

			public FilesLoader(Dictionary<string, string> files, params string[] paths)
			{
				m_Files = files;
				ModulePaths = paths.Length > 0 ? paths : null;
			}

			public override object LoadFile(string file, Table globalContext) { return m_Files[file]; }
			public override bool ScriptFileExists(string name) { return m_Files.ContainsKey(name); }
		}

		static Script NewScript(IScriptLoader loader)
		{
			var s = new Script();
			s.Options.ScriptLoader = loader;
			return s;
		}

		static Table Package(Script s)
		{
			return s.Globals.Get("package").Table;
		}

		// Hosts register modules written in C# in package.preload, and they are found before any file.
		[Test]
		public void Preload_HostFunctionIsTheLoader()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string> { { "config", "return 'file'" } }, "?"));
			int calls = 0;

			Package(s).Get("preload").Table["config"] = DynValue.NewCallback((c, a) =>
			{
				calls++;
				return DynValue.NewString("host " + a[0].String);
			});

			DynValue r = s.DoString("return require('config'), require('config'), package.loaded.config");

			Assert.AreEqual("host config", r.Tuple[0].String);
			Assert.AreEqual("host config", r.Tuple[1].String);
			Assert.AreEqual("host config", r.Tuple[2].String);
			Assert.AreEqual(1, calls, "a loaded module is not loaded again");
		}

		// The message names everything require tried, so a wrong path is visible, and the position is
		// the caller's, not a line inside require.
		[Test]
		public void NotFound_ListsWhatWasTriedAtTheCallersPosition()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string>(), "lib/?.lua", "?"));

			DynValue r = s.DoString(@"
				local ok, e = pcall(function()
					return require('a.b')
				end)
				local ok2, e2 = pcall(require, 'c')
				return e, e2", null, "main");

			StringAssert.StartsWith("main:(3,", r.Tuple[0].String);
			StringAssert.EndsWith("): module 'a.b' not found:\n\tno field package.preload['a.b']\n\tno file 'lib/a/b.lua'\n\tno file 'a/b'", r.Tuple[0].String);
			Assert.AreEqual("module 'c' not found:\n\tno field package.preload['c']\n\tno file 'lib/c.lua'\n\tno file 'c'", r.Tuple[1].String);
		}

		/// <summary>Resolves module names without paths, as hosts that keep modules in memory do.</summary>
		sealed class NamesLoader : IScriptLoader
		{
			public object LoadFile(string file, Table globalContext) { return "return 'named'"; }
			public string ResolveFileName(string filename, Table globalContext) { return filename; }
			public string ResolveModuleName(string modname, Table globalContext) { return modname == "known" ? modname : null; }
		}

		// A loader that does not resolve names through paths still says which loader was asked.
		[Test]
		public void NotFound_NamesALoaderWithoutPaths()
		{
			Script s = NewScript(new NamesLoader());

			DynValue r = s.DoString("local ok, e = pcall(require, 'x') return require('known'), e");

			Assert.AreEqual("named", r.Tuple[0].String);
			Assert.AreEqual("module 'x' not found:\n\tno field package.preload['x']\n\tno module 'x' in NamesLoader", r.Tuple[1].String);
		}

		// Lua 5.2: when the module stores its value in package.loaded itself and returns nothing, require
		// returns that value; only when there is none does it store true.
		[Test]
		public void Loaded_ModuleThatFillsPackageLoadedItself()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string>
			{
				{ "self", "package.loaded[...] = { tag = 'self' }" },
				{ "none", "local x = 1" },
			}, "?"));

			DynValue r = s.DoString("return require('self').tag, require('self') == package.loaded.self, require('none')");

			Assert.AreEqual("self", r.Tuple[0].String);
			Assert.AreEqual(true, r.Tuple[1].Boolean);
			Assert.AreEqual(true, r.Tuple[2].Boolean);
		}

		// A module that fails is not cached, so a later require runs it again (Lua 5.2), and false in
		// package.loaded does not count as loaded.
		[Test]
		public void Loaded_FailedAndFalseModulesLoadAgain()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string>
			{
				{ "flaky", "tries = (tries or 0) + 1 if tries == 1 then error('first') end return tries" },
			}, "?"));

			DynValue r = s.DoString(@"
				local ok = pcall(require, 'flaky')
				local second = require('flaky')
				package.loaded.flaky = false
				return ok, second, require('flaky')");

			Assert.AreEqual(false, r.Tuple[0].Boolean);
			Assert.AreEqual(2.0, r.Tuple[1].Number);
			Assert.AreEqual(3.0, r.Tuple[2].Number);
		}

		// A module may require other modules while it loads. A cycle ends in an error, as in Lua 5.2,
		// instead of hanging or crashing the host.
		[Test]
		public void Loaded_NestedAndCyclicRequires()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string>
			{
				{ "outer", "return { inner = require('inner'), name = 'outer' }" },
				{ "inner", "return { name = 'inner', outerLoaded = package.loaded.outer ~= nil }" },
				{ "a", "return require('b')" },
				{ "b", "return require('a')" },
			}, "?"));

			DynValue r = s.DoString(@"
				local o = require('outer')
				local ok, e = pcall(require, 'a')
				return o.name, o.inner.name, o.inner.outerLoaded, ok, e");

			Assert.AreEqual("outer", r.Tuple[0].String);
			Assert.AreEqual("inner", r.Tuple[1].String);
			Assert.AreEqual(false, r.Tuple[2].Boolean);
			Assert.AreEqual(false, r.Tuple[3].Boolean);
			StringAssert.EndsWith("stack overflow", r.Tuple[4].String);
		}

		// Searchers written in Lua extend require (module bundlers, other languages compiled to Lua);
		// package.loaders is the Lua 5.1 name that Lua 5.2 still provides.
		[Test]
		public void Searchers_LuaSearcherFindsModules()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string>(), "?"));

			DynValue r = s.DoString(@"
				table.insert(package.searchers, function(name)
					if name == 'virtual' then
						return function(n, extra) return n .. extra end, '!'
					end
					return '\n\tno virtual module ' .. name
				end)
				local ok, e = pcall(require, 'missing')
				return require('virtual'), e, rawequal(package.loaders, package.searchers), #package.searchers");

			Assert.AreEqual("virtual!", r.Tuple[0].String);
			Assert.AreEqual("module 'missing' not found:\n\tno field package.preload['missing']\n\tno file 'missing'\n\tno virtual module missing", r.Tuple[1].String);
			Assert.AreEqual(true, r.Tuple[2].Boolean);
			Assert.AreEqual(3.0, r.Tuple[3].Number);
		}

		[Test]
		public void Searchers_MustBeATable()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string>(), "?"));

			DynValue r = s.DoString("package.searchers = nil local ok, e = pcall(require, 'x') return e");

			Assert.AreEqual("'package.searchers' must be a table", r.String);
		}

		// A module can yield while it loads, wherever it comes from.
		[Test]
		public void Preload_LoaderCanYield()
		{
			Script s = new Script();

			DynValue r = s.DoString(@"
				package.preload.slow = function() return coroutine.yield(1) * 2 end
				local co = coroutine.wrap(function() return require('slow') end)
				local first = co()
				local second = co(21)
				return first, second, package.loaded.slow");

			Assert.AreEqual(1.0, r.Tuple[0].Number);
			Assert.AreEqual(42.0, r.Tuple[1].Number);
			Assert.AreEqual(42.0, r.Tuple[2].Number);
		}

		// Hosts often set ModulePaths, or a new loader, after creating the Script. Those paths stay in use
		// when a script adds its own to package.path, after the ones in package.path.
		[Test]
		public void Path_ScriptPathsAddToPathsSetLaterByTheHost()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string>
			{
				{ "host/h.lua", "return 'h'" },
				{ "extra/e.lua", "return 'e'" },
			}, "host/?.lua"));

			DynValue r = s.DoString("package.path = package.path .. ';extra/?.lua' return require('h'), require('e'), package.path");

			Assert.AreEqual("h", r.Tuple[0].String);
			Assert.AreEqual("e", r.Tuple[1].String);
			Assert.AreEqual(";extra/?.lua", r.Tuple[2].String);
		}

		// package.path is searched in its own order, and the LUA_PATH global keeps replacing ModulePaths.
		[Test]
		public void Path_OrderAndTheLuaPathGlobal()
		{
			Script s = NewScript(new FilesLoader(new Dictionary<string, string>
			{
				{ "host/m.lua", "return 'host'" },
				{ "mine/m.lua", "return 'mine'" },
				{ "old/o.lua", "return 'old'" },
			}, "host/?.lua"));

			DynValue r = s.DoString(@"
				package.path = 'mine/?.lua'
				LUA_PATH = 'old/?.lua'
				local ok, e = pcall(require, 'none')
				return require('m'), require('o'), e");

			Assert.AreEqual("mine", r.Tuple[0].String);
			Assert.AreEqual("old", r.Tuple[1].String);
			Assert.AreEqual("module 'none' not found:\n\tno field package.preload['none']\n\tno file 'mine/none.lua'\n\tno file 'old/none.lua'", r.Tuple[2].String);
		}

		// IgnoreLuaPathGlobal keeps the search to the host's ModulePaths, against package.path as well.
		[Test]
		public void Path_IgnoredWithTheLuaPathGlobal()
		{
			var loader = new FilesLoader(new Dictionary<string, string> { { "extra/e.lua", "return 'e'" } }, "host/?.lua");
			loader.IgnoreLuaPathGlobal = true;
			Script s = NewScript(loader);

			DynValue r = s.DoString("package.path = 'extra/?.lua' local ok, e = pcall(require, 'e') return ok, e");

			Assert.AreEqual(false, r.Tuple[0].Boolean);
			Assert.AreEqual("module 'e' not found:\n\tno field package.preload['e']\n\tno file 'host/e.lua'", r.Tuple[1].String);
		}

		// The Unity loader keeps only the file name of a path, so its modules are found with "?".
		[Test]
		public void UnityAssetsLoader_PathAndPreload()
		{
			Script s = NewScript(new UnityAssetsScriptLoader(new Dictionary<string, string> { { "util", "return 'util'" } }));

			DynValue r = s.DoString(@"
				package.preload.game = function() return 'game' end
				local ok = pcall(require, 'util')
				package.path = '?'
				return ok, require('util'), require('game')");

			Assert.AreEqual(false, r.Tuple[0].Boolean, "the default loader has no paths");
			Assert.AreEqual("util", r.Tuple[1].String);
			Assert.AreEqual("game", r.Tuple[2].String);
		}
	}
}
