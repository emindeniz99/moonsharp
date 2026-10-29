using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Lua;
using Lua.Standard;
using MoonSharp.Interpreter;

// Usage: LuaCsBench <workloads|boundary|create> <ms|luacs> [innerRuns]
// Prints one JSON object per line: engine, kind, case, per-run ns/op, per-run allocated bytes/op, check.
static class Program
{
	const long K = 1000;

	// Copied verbatim from moonsharp origin/master src/DevTools/Benchmarks/LuaWorkloads.cs (blob 0930391).
	// name, iterations, source, expected check value (as MoonSharp's ToPrintString prints it), note
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

	static int s_SyncCompletions, s_AsyncCompletions;
	static string s_Only;

	// Drives a Lua-CSharp ValueTask synchronously. Fast path: the VM completed synchronously, read Result.
	// Otherwise fall back to .AsTask().GetAwaiter().GetResult(). Counts which path was taken.
	static T Sync<T>(ValueTask<T> vt)
	{
		if (vt.IsCompletedSuccessfully) { s_SyncCompletions++; return vt.Result; }
		s_AsyncCompletions++;
		return vt.AsTask().GetAwaiter().GetResult();
	}

	static int Main(string[] args)
	{
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		string mode = args[0], engine = args[1];
		int runs = args.Length > 2 ? int.Parse(args[2]) : 7;
		s_Only = args.Length > 3 ? args[3] : null; // create mode: run one variant per process
		Meta(engine, mode, runs);
		int failures = mode switch
		{
			"workloads" => engine == "ms" ? WorkloadsMs(runs) : WorkloadsLuaCs(runs),
			"boundary" => engine == "ms" ? BoundaryMs(runs) : BoundaryLuaCs(runs),
			"create" => Create(engine, runs),
			_ => throw new ArgumentException(mode),
		};
		Console.WriteLine("{{\"kind\":\"end\",\"engine\":\"{0}\",\"failures\":{1},\"syncCompletions\":{2},\"asyncCompletions\":{3}}}",
			engine, failures, s_SyncCompletions, s_AsyncCompletions);
		return failures == 0 ? 0 : 1;
	}

	static void Meta(string engine, string mode, int runs)
	{
		Console.WriteLine("{{\"kind\":\"meta\",\"engine\":\"{0}\",\"mode\":\"{1}\",\"runs\":{2},\"runtime\":\"{3}\",\"serverGC\":{4},\"latency\":\"{5}\",\"moonsharp\":\"{6}\",\"luacs\":\"{7}\"}}",
			engine, mode, runs, Environment.Version, GCSettings.IsServerGC ? "true" : "false", GCSettings.LatencyMode,
			Script.VERSION, typeof(LuaState).Assembly.GetName().Version);
	}

	static void Emit(string engine, string kind, string name, long ops, List<double> ns, List<double> alloc, List<double> allocGlobal, string check, string expected)
	{
		bool ok = expected == null || expected == check;
		Console.WriteLine("{{\"kind\":\"{0}\",\"engine\":\"{1}\",\"case\":\"{2}\",\"ops\":{3},\"ns\":[{4}],\"alloc\":[{5}],\"allocGlobal\":[{6}],\"check\":\"{7}\",\"expected\":{8},\"ok\":{9}}}",
			kind, engine, name, ops,
			string.Join(",", ns.Select(v => v.ToString("R", CultureInfo.InvariantCulture))),
			string.Join(",", alloc.Select(v => v.ToString("R", CultureInfo.InvariantCulture))),
			string.Join(",", allocGlobal.Select(v => v.ToString("R", CultureInfo.InvariantCulture))),
			Escape(check), expected == null ? "null" : "\"" + Escape(expected) + "\"", ok ? "true" : "false");
	}

	static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

	// Timed region: Stopwatch around the call only. Allocation counters are read just outside it.
	static int Measure(string engine, string kind, string name, long ops, int runs, Func<string> body, string expected)
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
		Emit(engine, kind, name, ops, ns, alloc, allocG, check, expected);
		return expected == null || expected == check ? 0 : 1;
	}

	// ---------------- pure Lua workloads ----------------

	static int WorkloadsMs(int runs)
	{
		int failures = 0;
		foreach (var c in Cases)
		{
			var script = new Script();
			DynValue fn = script.DoString(c.Source);
			var n = DynValue.NewNumber(c.Iterations);
			script.Call(fn, DynValue.NewNumber(Math.Max(1, c.Iterations / 10))); // warm-up, as LuaWorkloads.cs
			failures += Measure("ms", "workload", c.Name, c.Iterations, runs,
				() => script.Call(fn, n).ToPrintString(), c.Check);
		}
		return failures;
	}

	static string LuaCsCheck(LuaValue[] results) => results.Length == 0 ? "void" : results[0].ToString();

	static int WorkloadsLuaCs(int runs)
	{
		int failures = 0;
		foreach (var c in Cases)
		{
			var state = LuaState.Create();
			state.OpenStandardLibraries();
			LuaValue fn = Sync(state.DoStringAsync(c.Source))[0];
			LuaValue n = (double)c.Iterations;
			Sync(state.CallAsync(fn, [(double)Math.Max(1, c.Iterations / 10)])); // warm-up
			// empty_loop: 0 results is mapped to "void" so the MoonSharp expectation applies unchanged.
			failures += Measure("luacs", "workload", c.Name, c.Iterations, runs,
				() => LuaCsCheck(Sync(state.CallAsync(fn, [n]))), c.Check);
		}
		return failures;
	}

	// ---------------- host boundary ----------------

	const int BoundaryN = 1_000_000;
	const string Add3 = "return function(a, b, c) return a * b + c end";
	const string CallHost = "local f = f\nreturn function(n) local s = 0 for i = 1, n do s = s + f(i) end return s end";
	static readonly string Add3Expected = (((double)BoundaryN * (BoundaryN - 1)) + 3.0 * BoundaryN).ToString("R", CultureInfo.InvariantCulture);
	static readonly string CallHostExpected = ((double)BoundaryN * (BoundaryN + 1) / 2 + BoundaryN).ToString("R", CultureInfo.InvariantCulture);

	static string D(double d) => d.ToString("R", CultureInfo.InvariantCulture);

	static int BoundaryMs(int runs)
	{
		int failures = 0;

		// (a) C# -> Lua: compiled function, 3 number args, 1 number result.
		{
			var script = new Script();
			DynValue fn = script.DoString(Add3);
			DynValue two = DynValue.NewNumber(2), three = DynValue.NewNumber(3);
			Func<string> body = () =>
			{
				double sum = 0;
				for (int i = 0; i < BoundaryN; i++)
					sum += script.Call(fn, DynValue.NewNumber(i), two, three).Number;
				return D(sum);
			};
			body();
			failures += Measure("ms", "boundary", "a_cs_to_lua_call3", BoundaryN, runs, body, Add3Expected);
		}

		// (b) Lua -> C#: Lua loop calling a CallbackFunction.
		{
			var script = new Script();
			script.Globals["f"] = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(args[0].Number + 1));
			DynValue fn = script.DoString(CallHost);
			var n = DynValue.NewNumber(BoundaryN);
			script.Call(fn, DynValue.NewNumber(BoundaryN / 10));
			failures += Measure("ms", "boundary", "b_lua_to_cs_call1", BoundaryN, runs,
				() => script.Call(fn, n).ToPrintString(), CallHostExpected);
		}
		return failures;
	}

	static int BoundaryLuaCs(int runs)
	{
		int failures = 0;

		// (a1) C# -> Lua through CallAsync(function, ReadOnlySpan<LuaValue>) returning LuaValue[] (README's first form).
		{
			var state = LuaState.Create(); state.OpenStandardLibraries();
			LuaValue fn = Sync(state.DoStringAsync(Add3))[0];
			Func<string> body = () =>
			{
				double sum = 0;
				for (int i = 0; i < BoundaryN; i++)
					sum += Sync(state.CallAsync(fn, [(double)i, 2.0, 3.0]))[0].Read<double>();
				return D(sum);
			};
			body();
			failures += Measure("luacs", "boundary", "a_cs_to_lua_call3", BoundaryN, runs, body, Add3Expected);
		}

		// (a2) C# -> Lua through the stack API (README's "avoid array allocation" form).
		{
			var state = LuaState.Create(); state.OpenStandardLibraries();
			LuaValue fn = Sync(state.DoStringAsync(Add3))[0];
			Func<string> body = () =>
			{
				double sum = 0;
				for (int i = 0; i < BoundaryN; i++)
				{
					int basePos = state.Stack.Count;
					state.Push(fn); state.Push((double)i); state.Push(2.0); state.Push(3.0);
					int count = Sync(state.CallAsync(funcIndex: basePos, returnBase: basePos));
					using (var reader = state.ReadStack(count))
						sum += reader[0].Read<double>();
				}
				return D(sum);
			};
			body();
			failures += Measure("luacs", "boundary", "a_cs_to_lua_call3_stackapi", BoundaryN, runs, body, Add3Expected);
		}

		// (a3) same as a1 but driven with `await` inside an async method, to see what the sync wrapper costs.
		{
			var state = LuaState.Create(); state.OpenStandardLibraries();
			LuaValue fn = Sync(state.DoStringAsync(Add3))[0];
			async Task<double> Loop()
			{
				double sum = 0;
				for (int i = 0; i < BoundaryN; i++)
					sum += (await state.CallAsync(fn, [(double)i, 2.0, 3.0]))[0].Read<double>();
				return sum;
			}
			Func<string> body = () => D(Loop().GetAwaiter().GetResult());
			body();
			failures += Measure("luacs", "boundary", "a_cs_to_lua_call3_await", BoundaryN, runs, body, Add3Expected);
		}

		// (b) Lua -> C#: Lua loop calling a LuaFunction.
		{
			var state = LuaState.Create(); state.OpenStandardLibraries();
			state.Environment["f"] = new LuaFunction((ctx, ct) => new ValueTask<int>(ctx.Return(ctx.GetArgument<double>(0) + 1)));
			LuaValue fn = Sync(state.DoStringAsync(CallHost))[0];
			LuaValue n = (double)BoundaryN;
			Sync(state.CallAsync(fn, [(double)(BoundaryN / 10)]));
			failures += Measure("luacs", "boundary", "b_lua_to_cs_call1", BoundaryN, runs,
				() => LuaCsCheck(Sync(state.CallAsync(fn, [n]))), CallHostExpected);
		}
		return failures;
	}

	// ---------------- engine creation ----------------

	const int CreateBatch = 200;
	const int RetainCount = 100;

	static int Create(string engine, int runs)
	{
		var variants = engine == "ms"
			? new (string, Func<object>)[]
			{
				("c_create_default", () => new Script()),
				("c_create_complete", () => new Script(CoreModules.Preset_Complete)),
			}
			: new (string, Func<object>)[]
			{
				("c_create_stdlib", () => { var s = LuaState.Create(); s.OpenStandardLibraries(); return s; }),
			};

		foreach (var (name, create) in variants)
		{
			if (s_Only != null && s_Only != name) continue;
			for (int i = 0; i < 20; i++) GC.KeepAlive(create()); // warm-up
			var sink = new object[CreateBatch];
			Measure(engine, "create", name, CreateBatch, runs, () =>
			{
				for (int i = 0; i < CreateBatch; i++) sink[i] = create();
				return "ok";
			}, "ok");
			Array.Clear(sink);

			// Retained: managed heap growth with RetainCount live instances (method of RetainedMemory.cs).
			var retained = new List<double>();
			for (int r = 0; r < runs; r++)
				retained.Add(RetainedOnce(create));
			Console.WriteLine("{{\"kind\":\"retained\",\"engine\":\"{0}\",\"case\":\"{1}\",\"count\":{2},\"bytes\":[{3}]}}",
				engine, name, RetainCount, string.Join(",", retained.Select(D)));
		}
		return 0;
	}

	// Own method, fully optimised up front: when this loop was inline in Create(), on-stack replacement
	// of the outer loop let the GC collect `keep` early and runs 2..N reported ~0 bytes retained.
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
