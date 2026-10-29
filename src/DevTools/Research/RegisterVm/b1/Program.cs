// One engine per process: MoonSharp (the build it was compiled against) or, with LUACS, Lua-CSharp.
// Usage: b1 <runs> [off]   ("off": ScriptOptions.RegisterVm = false; SPIKE builds only)
// Prints "case<TAB>median ns" per workload: ns per inner iteration, or per chunk execution.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
#if LUACS
using Lua;
using Lua.Runtime;
using Lua.Standard;
#else
using MoonSharp.Interpreter;
#endif

static class Program
{
	sealed class Case { public string Name; public string Source; public long N; public int Execs; public string Check; public bool Chunk; }

	static List<Case> Cases()
	{
		return new List<Case>
		{
			new Case { Name = "empty_loop", N = 4000000, Source = "return function(n) for i = 1, n do end end" },
			new Case { Name = "int_math", N = 2000000, Source = "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end", Check = "967568" },
			new Case { Name = "float_math", N = 2000000, Source = "return function(n) local x, y = 0.5, 0.25 for i = 1, n do x = x * 0.5 + y * 0.25 y = y * 0.5 + x / 3.0 + 0.001 end return string.format('%.6f', x + y) end", Check = "0.004500" },
			new Case { Name = "function_call", N = 1000000, Source = "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end", Check = "1000000" },
			new Case { Name = "fib_recursive", N = 4000, Source = "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end", Check = "576000" },
			new Case { Name = "numeric-loop", Chunk = true, Execs = 200, Source = "local s=0; for i=1,20000 do s=s+i end; return s", Check = "200010000" },
			new Case { Name = "nbody", Chunk = true, Execs = 40, Source = MoonSharp.Benchmarks.LuaCSharpScripts.NBody.Replace("or 1000", "or 100") + "\nreturn energy(bodies,nbody)", Check = "-0.169050762382409" },
		};
	}

#if LUACS
	static Func<string> Prepare(Case c, bool off)
	{
		var state = LuaState.Create();
		state.OpenStandardLibraries();
		var results = new Lua.LuaValue[1];

		if (c.Chunk)
		{
			LuaClosure closure = state.Load(c.Source, "comparison");
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

		var fnTask = state.DoStringAsync(c.Source);
		Lua.LuaValue fn = (fnTask.IsCompletedSuccessfully ? fnTask.Result : fnTask.AsTask().GetAwaiter().GetResult())[0];
		Func<double, string> call = x =>
		{
			var t = state.CallAsync(fn, new Lua.LuaValue[] { x });
			var r = t.IsCompletedSuccessfully ? t.Result : t.AsTask().GetAwaiter().GetResult();
			return r.Length == 0 ? "void" : r[0].ToString();
		};
		call(Math.Max(1, c.N / 10));
		return () => call(c.N);
	}
#else
	static Func<string> Prepare(Case c, bool off)
	{
		var s = new Script(CoreModules.Preset_Complete);
#if SPIKE
		s.Options.RegisterVm = !off;
#endif
		if (c.Chunk)
		{
			DynValue fn = s.LoadString(c.Source, null, "comparison");
			return () =>
			{
				DynValue r = null;
				for (int k = 0; k < c.Execs; k++)
					r = s.Call(fn);
				return r.ToPrintString();
			};
		}

		DynValue f = s.DoString(c.Source);
		s.Call(f, DynValue.NewNumber(Math.Max(1, c.N / 10)));
		DynValue n = DynValue.NewNumber(c.N);
		return () => s.Call(f, n).ToPrintString();
	}
#endif

	static int Main(string[] args)
	{
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		if (args.Length < 1)
		{
			Console.Error.WriteLine("usage: b1 <runs> [off]");
			return 2;
		}
		int runs = int.Parse(args[0]);
		bool off = args.Length > 1 && args[1] == "off";
		int failures = 0;

		foreach (Case c in Cases())
		{
			Func<string> body = Prepare(c, off);

			for (int k = 0; k < 2; k++)
			{
				string got = body();
				if (c.Check != null && !got.StartsWith(c.Check)) { Console.Error.WriteLine("CHECK FAILED {0}: {1}", c.Name, got); failures++; }
			}

			var t = new List<double>();
			for (int r = 0; r < runs; r++)
			{
				var sw = Stopwatch.StartNew();
				body();
				sw.Stop();
				t.Add(sw.Elapsed.TotalMilliseconds * 1e6 / (c.Chunk ? c.Execs : c.N));
			}

			t.Sort();
			Console.WriteLine("{0}\t{1}", c.Name, t[t.Count / 2].ToString("R", CultureInfo.InvariantCulture));
		}

		return failures;
	}
}
