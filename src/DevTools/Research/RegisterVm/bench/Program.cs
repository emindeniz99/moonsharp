using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Lua;
using Lua.Standard;
using Lua.Runtime;

// Interleaved comparison of MoonSharp builds (each in its own AssemblyLoadContext) and Lua-CSharp.
// Usage: bench <rounds> <label=path/MoonSharp.Interpreter.dll[:off]>... [luacs]
//   ":off" sets ScriptOptions.RegisterVm = false (spike builds only).
// Each round runs every engine on every workload once, in a rotating engine order.
static class Program
{
	sealed class Case { public string Name; public string Source; public long N; public int Execs; public string Check; public bool Chunk; }

	static readonly List<Case> Cases = new List<Case>
	{
		// LuaWorkloads.cs (src/DevTools/Benchmarks), same sources and iteration counts; fib_recursive at 4K
		// iterations per round (20K in LuaWorkloads) to keep rounds short. Times are per inner iteration.
		new Case { Name = "empty_loop", N = 4000000, Source = "return function(n) for i = 1, n do end end" },
		new Case { Name = "int_math", N = 2000000, Source = "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end", Check = "967568" },
		new Case { Name = "float_math", N = 2000000, Source = "return function(n) local x, y = 0.5, 0.25 for i = 1, n do x = x * 0.5 + y * 0.25 y = y * 0.5 + x / 3.0 + 0.001 end return string.format('%.6f', x + y) end", Check = "0.004500" },
		new Case { Name = "function_call", N = 1000000, Source = "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end", Check = "1000000" },
		new Case { Name = "fib_recursive", N = 4000, Source = "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end", Check = "576000" },
		// Lua-CSharp sandbox/Benchmark ComparisonWorkloads.cs: a retained compiled chunk, one execution per op.
		new Case { Name = "numeric-loop", Chunk = true, Execs = 200, Source = "local s=0; for i=1,20000 do s=s+i end; return s", Check = "200010000" },
		new Case { Name = "nbody", Chunk = true, Execs = 40, Source = MoonSharp.Benchmarks.LuaCSharpScripts.NBody.Replace("or 1000", "or 100") + "\nreturn energy(bodies,nbody)", Check = "-0.169050762382409" },
	};

	interface IEngine { string Label { get; } Func<string> Prepare(Case c); }

	sealed class MoonSharpEngine : IEngine
	{
		public string Label { get; }
		readonly Type m_Script, m_DynValue, m_Modules;
		readonly MethodInfo m_DoString, m_LoadString, m_Call, m_NewNumber, m_ToPrintString;
		readonly bool m_Off;

		public MoonSharpEngine(string label, string path, bool off)
		{
			Label = label;
			m_Off = off;
			var ctx = new AssemblyLoadContext("ms-" + label, false);
			Assembly asm = ctx.LoadFromAssemblyPath(Path.GetFullPath(path));
			m_Script = asm.GetType("MoonSharp.Interpreter.Script", true);
			m_DynValue = asm.GetType("MoonSharp.Interpreter.DynValue", true);
			m_Modules = asm.GetType("MoonSharp.Interpreter.CoreModules", true);
			Type table = asm.GetType("MoonSharp.Interpreter.Table", true);
			m_DoString = m_Script.GetMethod("DoString", new[] { typeof(string), table, typeof(string) });
			m_LoadString = m_Script.GetMethod("LoadString", new[] { typeof(string), table, typeof(string) });
			m_Call = m_Script.GetMethod("Call", new[] { m_DynValue, m_DynValue.MakeArrayType() });
			m_NewNumber = m_DynValue.GetMethod("NewNumber", new[] { typeof(double) });
			m_ToPrintString = m_DynValue.GetMethod("ToPrintString", Type.EmptyTypes);
		}

		object NewScript()
		{
			object s = Activator.CreateInstance(m_Script, Enum.Parse(m_Modules, "Preset_Complete"));
			if (m_Off)
			{
				object options = m_Script.GetProperty("Options").GetValue(s);
				options.GetType().GetProperty("RegisterVm").SetValue(options, false);
			}
			return s;
		}

		public Func<string> Prepare(Case c)
		{
			object s = NewScript();

			if (c.Chunk)
			{
				object fn = m_LoadString.Invoke(s, new object[] { c.Source, null, "comparison" });
				var args = new object[] { fn, Array.CreateInstance(m_DynValue, 0) };
				return () =>
				{
					object r = null;
					for (int k = 0; k < c.Execs; k++)
						r = m_Call.Invoke(s, args);
					return (string)m_ToPrintString.Invoke(r, null);
				};
			}
			else
			{
				object fn = m_DoString.Invoke(s, new object[] { c.Source, null, null });
				Array a = Array.CreateInstance(m_DynValue, 1);
				a.SetValue(m_NewNumber.Invoke(null, new object[] { (double)c.N }), 0);
				var args = new object[] { fn, a };
				Array w = Array.CreateInstance(m_DynValue, 1);
				w.SetValue(m_NewNumber.Invoke(null, new object[] { (double)Math.Max(1, c.N / 10) }), 0);
				m_Call.Invoke(s, new object[] { fn, w }); // warm-up, as LuaWorkloads.cs
				return () => (string)m_ToPrintString.Invoke(m_Call.Invoke(s, args), null);
			}
		}
	}

	sealed class LuaCsEngine : IEngine
	{
		public string Label => "luacs";

		static T Sync<T>(ValueTask<T> vt) => vt.IsCompletedSuccessfully ? vt.Result : vt.AsTask().GetAwaiter().GetResult();

		public Func<string> Prepare(Case c)
		{
			var state = LuaState.Create();
			state.OpenStandardLibraries();

			if (c.Chunk)
			{
				LuaClosure closure = state.Load(c.Source, "comparison");
				var results = new Lua.LuaValue[1];
				return () =>
				{
					for (int k = 0; k < c.Execs; k++)
					{
						var pending = state.ExecuteAsync(closure, results);
						if (pending.IsCompleted) pending.GetAwaiter().GetResult(); else pending.AsTask().GetAwaiter().GetResult();
					}
					return results[0].Read<double>().ToString("R", CultureInfo.InvariantCulture);
				};
			}
			else
			{
				Lua.LuaValue fn = Sync(state.DoStringAsync(c.Source))[0];
				Lua.LuaValue n = (double)c.N;
				Sync(state.CallAsync(fn, new Lua.LuaValue[] { (double)Math.Max(1, c.N / 10) }));
				return () =>
				{
					var r = Sync(state.CallAsync(fn, new Lua.LuaValue[] { n }));
					return r.Length == 0 ? "void" : r[0].ToString();
				};
			}
		}
	}

	static int Main(string[] args)
	{
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		if (args.Length < 2)
		{
			Console.Error.WriteLine("usage: bench <rounds> <label=path/MoonSharp.Interpreter.dll[:off]>... [luacs]");
			return 2;
		}
		int rounds = int.Parse(args[0]);
		var engines = new List<IEngine>();

		foreach (string a in args.Skip(1))
		{
			if (a == "luacs") { engines.Add(new LuaCsEngine()); continue; }
			int eq = a.IndexOf('=');
			string label = a.Substring(0, eq), path = a.Substring(eq + 1);
			bool off = path.EndsWith(":off");
			if (off) path = path.Substring(0, path.Length - 4);
			engines.Add(new MoonSharpEngine(label, path, off));
		}

		Console.WriteLine("# .NET {0}, {1} rounds, load {2}", Environment.Version, rounds, File.ReadAllText("/proc/loadavg").Trim());
		var times = new Dictionary<(string, string), List<double>>();
		int failures = 0;

		foreach (Case c in Cases)
		{
			var bodies = engines.Select(e => e.Prepare(c)).ToArray();
			double perOp = c.Chunk ? c.Execs : c.N;

			// Warm-up: three runs each, checks included.
			for (int e = 0; e < engines.Count; e++)
				for (int k = 0; k < 3; k++)
				{
					string got = bodies[e]();
					if (c.Check != null && !got.StartsWith(c.Check)) { Console.WriteLine("CHECK FAILED {0} {1}: {2}", engines[e].Label, c.Name, got); failures++; }
				}

			for (int r = 0; r < rounds; r++)
				for (int j = 0; j < engines.Count; j++)
				{
					int e = (j + r) % engines.Count;
					var sw = Stopwatch.StartNew();
					bodies[e]();
					sw.Stop();
					var key = (engines[e].Label, c.Name);
					if (!times.TryGetValue(key, out var list)) times[key] = list = new List<double>();
					list.Add(sw.Elapsed.TotalMilliseconds * 1e6 / perOp);
				}
		}

		Console.WriteLine("# ns per iteration (LuaWorkloads cases) or per execution (numeric-loop, nbody): median of rounds");
		Console.WriteLine("case\t" + string.Join("\t", engines.Select(e => e.Label)));
		foreach (Case c in Cases)
			Console.WriteLine(c.Name + "\t" + string.Join("\t", engines.Select(e => Median(times[(e.Label, c.Name)]).ToString("0.0"))));

		Console.WriteLine("# ratios to the first engine: median of per-round ratios (interquartile range)");
		Console.WriteLine("case\t" + string.Join("\t", engines.Skip(1).Select(e => e.Label + "/" + engines[0].Label)));
		foreach (Case c in Cases)
		{
			var a = times[(engines[0].Label, c.Name)];
			Console.WriteLine(c.Name + "\t" + string.Join("\t", engines.Skip(1).Select(e =>
			{
				var b = times[(e.Label, c.Name)];
				var ratios = a.Select((x, i) => b[i] / x).OrderBy(x => x).ToList();
				return string.Format("{0:0.000} ({1:0.000}-{2:0.000})", Median(ratios), ratios[ratios.Count / 4], ratios[ratios.Count * 3 / 4]);
			})));
		}

		Console.WriteLine("# load {0}", File.ReadAllText("/proc/loadavg").Trim());
		return failures;
	}

	static double Median(List<double> v)
	{
		var s = v.OrderBy(x => x).ToList();
		return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
	}
}
