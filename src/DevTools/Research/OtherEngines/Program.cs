using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime;
#if WATTLE
using WattleScript.Interpreter;
#else
using MoonSharp.Interpreter;
#endif

// Usage: <exe> <workloads|interop|create> [innerRuns]
// Same source compiled per engine build: MoonSharp fork (ms), MoonSharp upstream (msup), WattleScript (ws, -DWATTLE),
// and WattleScript history checkpoints (ws-<sha>; -DWATTLE only after the namespace rename).
// Prints one JSON object per line in the format of ../LuaCSharp/Program.cs, so ../LuaCSharp/aggregate.py-style
// aggregation applies: per-run ns/op, per-run allocated bytes/op (current thread and global), check value.
public class Vec
{
	public double X;
	public double Y { get; set; }
	public double Add(double a) { X += a; return X; }
}

static class Bench
{
	const long K = 1000;

	// Copied verbatim from moonsharp fork origin/master 0c0e5d4 src/DevTools/Benchmarks/LuaWorkloads.cs.
	// name, iterations, source, expected check value (as tostring would print it), note
	static readonly (string Name, long Iterations, string Source, string Check, string Note)[] Cases =
	{
		("empty_loop", 4000 * K, "return function(n) for i = 1, n do end end", "void", "MoonSharp prints a missing return value as void"),
		("function_call", 1000 * K, "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end", "1000000", null),
		("method_call", 1000 * K, "local C = {} C.__index = C\nfunction C:inc() self.v = self.v + 1 end\nlocal o = setmetatable({ v = 0 }, C)\nreturn function(n) for i = 1, n do o:inc() end return o.v end", null, "check depends on prior runs"),
		("table_array_insert", 1000 * K, "return function(n) local t = {} for i = 1, n do t[i] = i end return #t end", "1000000", null),
		("table_array_read", 1000 * K, "local t = {} for i = 1, 1024 do t[i] = i end\nreturn function(n) local s, j = 0, 1 for i = 1, n do s = s + t[j] j = j + 1 if j > 1024 then j = 1 end end return s end", "512370976", null),
		("table_hash_set_get", 1000 * K, "local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\nreturn function(n) local t, s, j = {}, 0, 1 for i = 1, n do local k = keys[j] t[k] = i s = s + t[k] j = j + 1 if j > 64 then j = 1 end end return s end", "500000500000", null),
		("string_concat", 500 * K, "return function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = 'item' .. i j = j + 1 if j > 64 then j = 1 end end return buf[64] end", "item499968", null),
		("string_format", 500 * K, "local format = string.format\nreturn function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = format('%d:%s', i, 'x') j = j + 1 if j > 64 then j = 1 end end return buf[64] end", "499968:x", null),
		("closure_create_call", 1000 * K, "return function(n) local s = 0 for i = 1, n do local f = function() return i end s = s + f() end return s end", "500000500000", null),
		("int_math", 2000 * K, "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end", "967568", null),
		("fib_recursive", 20 * K, "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end", "2880000", null),
		("float_math", 2000 * K, "return function(n) local x, y = 0.5, 0.25 for i = 1, n do x = x * 0.5 + y * 0.25 y = y * 0.5 + x / 3.0 + 0.001 end return string.format('%.6f', x + y) end", "0.004500", null),
		("string_pattern", 100 * K, "local s = 'the quick brown fox 123 jumps over the lazy dog 4567'\nreturn function(n) local c = 0 for i = 1, n do local a, b = s:find('(%a+) (%d+)') local r, k = s:gsub('o', '0') c = c + a + k + #s:match('%d+$') end return c end", "2500000", null),
		// Lua 5.2 has no table.move: copy with a loop instead (one extra loop of 256 per iteration).
		("table_sort_52", 2 * K, "local src, x = {}, 12345 for i = 1, 256 do x = (x * 1103515245 + 12345) % 2147483648 src[i] = x end\nreturn function(n) local s = 0 for i = 1, n do local t = {} for k = 1, 256 do t[k] = src[k] end table.sort(t) s = s + t[1] % 1000 end return s end", null, "variant: copy loop instead of table.move; the seed arithmetic exceeds 2^53 under doubles"),
		("table_concat", 50 * K, "local parts = {} for i = 1, 32 do parts[i] = tostring(i) end\nreturn function(n) local s = 0 for i = 1, n do s = s + #table.concat(parts, ',') end return s end", "4300000", null),
		("coroutine_switch", 500 * K, "return function(n) local co = coroutine.wrap(function() local i = 0 while true do i = i + 1 coroutine.yield(i) end end)\nlocal s = 0 for i = 1, n do s = s + co() end return s end", "125000250000", null),
		("gc_small_tables", 500 * K, "return function(n) local keep, s = {}, 0 for i = 1, n do local t = { i, i + 1, x = i } keep[i % 128 + 1] = t s = s + t[2] end return s end", "125000750000", null),
		("oop_inherit", 1000 * K, "local Base = {} Base.__index = Base function Base:get() return self.v end\nlocal Derived = setmetatable({}, Base) Derived.__index = Derived\nlocal o = setmetatable({ v = 3 }, Derived)\nreturn function(n) local s = 0 for i = 1, n do s = s + o:get() end return s end", "3000000", null),
	};

	// Label = the harness exe's assembly name (ms, msup, ws, ws-<checkpoint>).
	static readonly string Engine = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;

	public static int Main(string[] args)
	{
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		if (args.Length < 1)
		{
			Console.Error.WriteLine("usage: OtherEngines <workloads|interop|create> [innerRuns]");
			return 2;
		}
		string mode = args[0];
		int runs = args.Length > 1 ? int.Parse(args[1]) : 7;
		Console.WriteLine("{{\"kind\":\"meta\",\"engine\":\"{0}\",\"mode\":\"{1}\",\"runs\":{2},\"runtime\":\"{3}\",\"serverGC\":{4},\"version\":\"{5}\",\"asm\":\"{6}\"}}",
			Engine, mode, runs, Environment.Version, GCSettings.IsServerGC ? "true" : "false", Script.VERSION, typeof(Script).Assembly.Location);
		int failures = mode switch
		{
			"workloads" => Workloads(runs),
			"interop" => Interop(runs),
			"create" => Create(runs),
			_ => throw new ArgumentException(mode),
		};
		Console.WriteLine("{{\"kind\":\"end\",\"engine\":\"{0}\",\"failures\":{1}}}", Engine, failures);
		return failures == 0 ? 0 : 1;
	}

	static string D(double d) => d.ToString("R", CultureInfo.InvariantCulture);
	static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

	static void Emit(string kind, string name, long ops, List<double> ns, List<double> alloc, List<double> allocG, string check, string expected)
	{
		bool ok = expected == null || expected == check;
		Console.WriteLine("{{\"kind\":\"{0}\",\"engine\":\"{1}\",\"case\":\"{2}\",\"ops\":{3},\"ns\":[{4}],\"alloc\":[{5}],\"allocGlobal\":[{6}],\"check\":\"{7}\",\"expected\":{8},\"ok\":{9}}}",
			kind, Engine, name, ops, string.Join(",", ns.Select(D)), string.Join(",", alloc.Select(D)), string.Join(",", allocG.Select(D)),
			Escape(check ?? "null"), expected == null ? "null" : "\"" + Escape(expected) + "\"", ok ? "true" : "false");
	}

	static void EmitError(string kind, string name, Exception e)
	{
		Console.WriteLine("{{\"kind\":\"error\",\"engine\":\"{0}\",\"case\":\"{1}\",\"of\":\"{2}\",\"error\":\"{3}\"}}",
			Engine, name, kind, Escape(e.GetType().Name + ": " + e.Message));
	}

	// Timed region: Stopwatch around the body only; allocation counters read just outside it.
	static int Measure(string kind, string name, long ops, int runs, Func<string> body, string expected)
	{
		var ns = new List<double>(); var alloc = new List<double>(); var allocG = new List<double>();
		string check = null;
		for (int r = 0; r < runs; r++)
		{
			long g0 = GC.GetTotalAllocatedBytes(true);
			long a0 = GC.GetAllocatedBytesForCurrentThread();
			var sw = Stopwatch.StartNew();
			check = body();
			sw.Stop();
			long a1 = GC.GetAllocatedBytesForCurrentThread();
			long g1 = GC.GetTotalAllocatedBytes(true);
			ns.Add(sw.Elapsed.TotalMilliseconds * 1e6 / ops);
			alloc.Add((double)(a1 - a0) / ops);
			allocG.Add((double)(g1 - g0) / ops);
		}
		Emit(kind, name, ops, ns, alloc, allocG, check, expected);
		return expected == null || expected == check ? 0 : 1;
	}

	// Each case runs isolated: an engine failure is recorded and the run continues.
	static int Guard(string kind, string name, Func<int> f)
	{
		try { return f(); }
		catch (Exception e) { EmitError(kind, name, e); return 1; }
	}

	static int Workloads(int runs)
	{
		int failures = 0;
		foreach (var c in Cases)
		{
			failures += Guard("workload", c.Name, () =>
			{
				var script = new Script();
				DynValue fn = script.DoString(c.Source);
				var n = DynValue.NewNumber(c.Iterations);
				script.Call(fn, DynValue.NewNumber(Math.Max(1, c.Iterations / 10))); // warm-up, as LuaWorkloads.cs
				return Measure("workload", c.Name, c.Iterations, runs, () => script.Call(fn, n).ToPrintString(), c.Check);
			});
		}
		return failures;
	}

	const int InteropN = 1_000_000;

	static int Interop(int runs)
	{
		UserData.RegisterType<Vec>();
		int failures = 0;

		// Lua -> C# method on a registered userdata (reflection descriptors, default access mode).
		failures += Guard("interop", "i_clr_method_call", () =>
		{
			var script = new Script();
			script.Globals["vec"] = new Vec();
			DynValue fn = script.DoString("return function(n) local v = vec for i = 1, n do v:Add(1) end return v.X end");
			script.Call(fn, DynValue.NewNumber(InteropN / 10));
			var n = DynValue.NewNumber(InteropN);
			return Measure("interop", "i_clr_method_call", InteropN, runs, () => script.Call(fn, n).ToPrintString(), null);
		});

		// Lua -> C# property get on a registered userdata.
		failures += Guard("interop", "i_clr_prop_get", () =>
		{
			var script = new Script();
			script.Globals["vec"] = new Vec { Y = 2.5 };
			DynValue fn = script.DoString("return function(n) local v = vec local s = 0 for i = 1, n do s = s + v.Y end return s end");
			script.Call(fn, DynValue.NewNumber(InteropN / 10));
			var n = DynValue.NewNumber(InteropN);
			return Measure("interop", "i_clr_prop_get", InteropN, runs, () => script.Call(fn, n).ToPrintString(), "2500000");
		});

		// C# -> Lua: compiled function, 3 number args, 1 number result (same as luacs-bench a_cs_to_lua_call3).
		failures += Guard("interop", "a_cs_to_lua_call3", () =>
		{
			var script = new Script();
			DynValue fn = script.DoString("return function(a, b, c) return a * b + c end");
			DynValue two = DynValue.NewNumber(2), three = DynValue.NewNumber(3);
			Func<string> body = () =>
			{
				double sum = 0;
				for (int i = 0; i < InteropN; i++)
					sum += script.Call(fn, DynValue.NewNumber(i), two, three).Number;
				return D(sum);
			};
			body();
			string expected = (((double)InteropN * (InteropN - 1)) + 3.0 * InteropN).ToString("R", CultureInfo.InvariantCulture);
			return Measure("interop", "a_cs_to_lua_call3", InteropN, runs, body, expected);
		});

		// Lua -> C# CallbackFunction, 1 arg (same as luacs-bench b_lua_to_cs_call1; comparable with Lua-CSharp).
		failures += Guard("interop", "b_lua_to_cs_call1", () =>
		{
			var script = new Script();
			script.Globals["f"] = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(args[0].Number + 1));
			DynValue fn = script.DoString("local f = f\nreturn function(n) local s = 0 for i = 1, n do s = s + f(i) end return s end");
			script.Call(fn, DynValue.NewNumber(InteropN / 10));
			var n = DynValue.NewNumber(InteropN);
			string expected = ((double)InteropN * (InteropN + 1) / 2 + InteropN).ToString("R", CultureInfo.InvariantCulture);
			return Measure("interop", "b_lua_to_cs_call1", InteropN, runs, () => script.Call(fn, n).ToPrintString(), expected);
		});
		return failures;
	}

	const int CreateBatch = 200;
	const int RetainCount = 100;

	// `new Script()` (default modules), as luacs-bench c_create_default: batch time/alloc plus retained heap per instance.
	static int Create(int runs)
	{
		return Guard("create", "c_create_default", () =>
		{
			Func<object> create = () => new Script();
			for (int i = 0; i < 20; i++) GC.KeepAlive(create());
			var sink = new object[CreateBatch];
			Measure("create", "c_create_default", CreateBatch, runs, () =>
			{
				for (int i = 0; i < CreateBatch; i++) sink[i] = create();
				return "ok";
			}, "ok");
			Array.Clear(sink, 0, sink.Length);
			var retained = new List<double>();
			for (int r = 0; r < runs; r++) retained.Add(RetainedOnce(create));
			Console.WriteLine("{{\"kind\":\"retained\",\"engine\":\"{0}\",\"case\":\"c_create_default\",\"count\":{1},\"bytes\":[{2}]}}",
				Engine, RetainCount, string.Join(",", retained.Select(D)));
			return 0;
		});
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
	static double RetainedOnce(Func<object> create)
	{
		var keep = new object[RetainCount];
		long before = Settle();
		for (int i = 0; i < RetainCount; i++) keep[i] = create();
		long after = Settle();
		GC.KeepAlive(keep);
		return (double)(after - before) / RetainCount;
	}

	static long Settle()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		return GC.GetTotalMemory(true);
	}
}
