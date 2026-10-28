using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// What reflection finds on a module type is cached per type, so only the first Script pays for it.
	/// Every Script must still get its own callback objects and tables, and the same globals, in the same
	/// order (pairs is insertion-ordered), as before the cache.
	/// </summary>
	[TestFixture]
	public class ScriptCreationTests
	{
		static readonly CoreModules[] Presets =
		{
			CoreModules.Preset_HardSandbox, CoreModules.Preset_SoftSandbox, CoreModules.Preset_Default, CoreModules.Preset_Complete,
		};

		static bool PlatformSupportsAll(CoreModules modules)
		{
			return Script.GlobalOptions.Platform.FilterSupportedCoreModules(modules) == modules;
		}

		/// <summary>Names and types of the globals, and of the members of global tables, in pairs order.</summary>
		internal static string Snapshot(Script script)
		{
			var sb = new StringBuilder();
			foreach (TablePair p in script.Globals.Pairs)
			{
				sb.Append(p.Key.ToPrintString()).Append(':').Append(p.Value.Type.ToLuaTypeString());
				if (p.Value.Type == DataType.Table && p.Key.String != "_G")
				{
					sb.Append('{');
					foreach (TablePair q in p.Value.Table.Pairs)
						sb.Append(q.Key.ToPrintString()).Append(':').Append(q.Value.Type.ToLuaTypeString()).Append(' ');
					sb.Append('}');
				}
				sb.Append('\n');
			}
			return sb.ToString();
		}

		[Test]
		public void NewScript_GlobalsMatchTheRecordedSnapshot()
		{
			string path = Environment.GetEnvironmentVariable("MOONSHARP_WRITE_GLOBALS_SNAPSHOT");
			if (!string.IsNullOrEmpty(path))
			{
				var sb = new StringBuilder();
				foreach (CoreModules preset in Presets)
					sb.Append("\t\t\t{ CoreModules.").Append(preset).Append(", ").Append(Literal(Snapshot(new Script(preset)))).Append(" },\n");
				System.IO.File.WriteAllText(path, sb.ToString());
				return;
			}

			if (!PlatformSupportsAll(CoreModules.Preset_Complete))
			{
				TestRunner.Skip();
				Assert.Ignore("the platform leaves some modules out");
			}

			foreach (CoreModules preset in Presets)
			{
				// Twice: the first Script may build the cache, the second one uses it.
				Assert.AreEqual(ScriptCreationSnapshots.Globals[preset], Snapshot(new Script(preset)), preset + ", first");
				Assert.AreEqual(ScriptCreationSnapshots.Globals[preset], Snapshot(new Script(preset)), preset + ", second");
			}
		}

		[Test]
		public void NewScript_ScriptsDoNotShareCallbacksOrTables()
		{
			var a = new Script(CoreModules.Preset_Complete);
			var b = new Script(CoreModules.Preset_Complete);

			int callbacks = 0;
			foreach (TablePair p in a.Globals.Pairs)
			{
				DynValue other = b.Globals.Get(p.Key);
				if (p.Value.Type == DataType.ClrFunction)
				{
					callbacks++;
					Assert.AreNotSame(p.Value.Callback, other.Callback, p.Key.String);
				}
				else if (p.Value.Type == DataType.Table)
				{
					Assert.AreNotSame(p.Value.Table, other.Table, p.Key.String);
					foreach (TablePair q in p.Value.Table.Pairs)
						if (q.Value.Type == DataType.ClrFunction)
							Assert.AreNotSame(q.Value.Callback, other.Table.Get(q.Key).Callback, p.Key.String + "." + q.Key.String);
				}
				else if (p.Value.Type == DataType.Function)
				{
					Assert.AreNotSame(p.Value.Function, other.Function, p.Key.String);
					Assert.AreSame(a, p.Value.Function.OwnerScript, p.Key.String);
				}
			}
			Assert.Greater(callbacks, 20);

			// State a script attaches to a callback stays in that script.
			a.Globals.Get("string").Table.Get("format").Callback.AdditionalData = "a";
			Assert.IsNull(b.Globals.Get("string").Table.Get("format").Callback.AdditionalData);

			// Each script's require is compiled into, and runs in, that script.
			a.Globals["x"] = 1;
			b.Globals["x"] = 2;
			a.Options.ScriptLoader = new ModuleLoader("return x");
			b.Options.ScriptLoader = new ModuleLoader("return x * 10");
			Assert.AreEqual(1.0, a.DoString("return require('m')").Number);
			Assert.AreEqual(20.0, b.DoString("return require('m')").Number);
		}

		sealed class ModuleLoader : Loaders.ScriptLoaderBase
		{
			readonly string m_Code;
			public ModuleLoader(string code) { m_Code = code; ModulePaths = new[] { "?" }; }
			public override object LoadFile(string file, Table globalContext) { return m_Code; }
			public override bool ScriptFileExists(string name) { return true; }
		}

		[Test]
		public void NewScript_InParallel()
		{
			string expected = Snapshot(new Script(CoreModules.Preset_Complete));
			var errors = new List<string>();
			var threads = new Thread[4];

			for (int t = 0; t < threads.Length; t++)
			{
				threads[t] = new Thread(() =>
				{
					try
					{
						for (int i = 0; i < 25; i++)
						{
							var s = new Script(CoreModules.Preset_Complete);
							if (Snapshot(s) != expected)
								throw new Exception("different globals");
							if (s.DoString("return string.format('%d-%s', 7, type(require))").String != "7-function")
								throw new Exception("script does not work");
						}
					}
					catch (Exception ex)
					{
						lock (errors) errors.Add(ex.ToString());
					}
				});
				threads[t].Start();
			}

			foreach (Thread th in threads)
				th.Join();

			Assert.AreEqual(0, errors.Count, string.Join("\n", errors.ToArray()));
		}

		sealed class ModulesLoader : Loaders.ScriptLoaderBase
		{
			public ModulesLoader() { ModulePaths = new[] { "?" }; }
			public override object LoadFile(string file, Table globalContext)
			{
				switch (file)
				{
					case "counter": loads++; return "return { n = " + loads + " }";
					case "nothing": return "local x = 1";
					case "yields": return "local v = coroutine.yield(5) return v * 2";
					default: return "error('module ' .. ... .. ' failed')";
				}
			}
			public override bool ScriptFileExists(string name) { return name != "missing"; }
			int loads;
		}

		// require is compiled from Lua on its first call, not by every new Script. It must behave as before.
		[Test]
		public void Require_BehavesAsTheLuaImplementation()
		{
			var S = new Script();
			S.Options.ScriptLoader = new ModulesLoader();

			DynValue res = S.DoString(@"
				local a = require('counter')
				local b = require('counter')
				local n = require('nothing')
				local co = coroutine.wrap(function() return require('yields') end)
				local y1 = co()
				local y2 = co(21)
				local ok1, e1 = pcall(require, 'missing')
				local ok2, e2 = pcall(require, 'failing')
				local r = require
				return a == b, a.n, n, package.loaded.nothing, y1, y2, package.loaded.yields,
					ok1, e1, ok2, e2, type(r), r('counter') == a, rawequal(r, require)");

			Assert.AreEqual(true, res.Tuple[0].Boolean, "cached in package.loaded");
			Assert.AreEqual(1.0, res.Tuple[1].Number);
			Assert.AreEqual(true, res.Tuple[2].Boolean, "a module returning nothing gives true");
			Assert.AreEqual(true, res.Tuple[3].Boolean);
			Assert.AreEqual(5.0, res.Tuple[4].Number, "a module can yield while it loads");
			Assert.AreEqual(42.0, res.Tuple[5].Number);
			Assert.AreEqual(42.0, res.Tuple[6].Number);
			Assert.AreEqual(false, res.Tuple[7].Boolean);
			Assert.AreEqual(RequireErrors[0], res.Tuple[8].String);
			Assert.AreEqual(false, res.Tuple[9].Boolean);
			Assert.AreEqual(RequireErrors[1], res.Tuple[10].String);
			Assert.AreEqual("function", res.Tuple[11].String);
			Assert.AreEqual(true, res.Tuple[12].Boolean);
			Assert.AreEqual(true, res.Tuple[13].Boolean);

			// From the host as well.
			Assert.AreEqual(1.0, S.Call(S.Globals.Get("require"), "counter").Table.Get("n").Number);
		}

		// Recorded from the Lua require before it became lazy; the first one since then in Lua 5.2's form, listing
		// what require tried (see RequireTests).
		static readonly string[] RequireErrors =
		{
			"module 'missing' not found:\n\tno field package.preload['missing']\n\tno file 'missing'",
			"failing:(1,5-36): module failing failed",
		};

		// require's source is still registered when the Script is created, so chunk numbers in error
		// messages stay as they were; its code is compiled on first use.
		[Test]
		public void NewScript_ChunkNamesUnchanged()
		{
			var S = new Script();
			Assert.AreEqual(1, S.SourceCodeCount);
			Assert.AreEqual("libfunc_require", S.GetSourceCode(0).Name);

			var ex = Assert.Throws<ScriptRuntimeException>(() => S.DoString("error('x')"));
			StringAssert.StartsWith("chunk_1:", ex.DecoratedMessage);
		}

		// Reflection over the module types and compiling require were most of the cost of a Script:
		// ~186 KB per default Script before.
		[Test]
		public void NewScript_AllocatesLittle()
		{
			MethodInfo allocated = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
			if (allocated == null)
			{
				TestRunner.Skip();
				Assert.Ignore("GC.GetAllocatedBytesForCurrentThread is not available on this runtime.");
			}

			new Script();
			long before = (long)allocated.Invoke(null, null);
			for (int i = 0; i < 20; i++)
				new Script();
			long perScript = ((long)allocated.Invoke(null, null) - before) / 20;

			Assert.Less(perScript, 120 * 1024, "bytes allocated per new Script()");
		}

		static string Literal(string s)
		{
			return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
		}
	}
}
