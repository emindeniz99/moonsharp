using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MoonSharp.Interpreter;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Pure Lua micro-workloads with the same sources and iteration counts as the pure-Lua cases of
	/// xLua-NG's Test/Native/flag_bench.c (MIT licence), so MoonSharp and a native Lua can be compared
	/// on the same machine. Each chunk returns function(n); the result is checked against the expected value.
	/// Run with: dotnet run -c Release -- workloads [runs]
	/// </summary>
	static class LuaWorkloads
	{
		const long K = 1000;

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

		public static int Run(string[] args)
		{
			int runs = args.Length > 0 ? int.Parse(args[0]) : 7;
			Console.WriteLine("MoonSharp {0} on .NET {1}, {2} runs per case (median, min, max ns per iteration)", Script.VERSION, Environment.Version, runs);
			Console.WriteLine();
			Console.WriteLine("| Case | Iterations | Median ns | Min ns | Max ns | Check | Note |");
			Console.WriteLine("|---|---:|---:|---:|---:|---|---|");
			int failures = 0;

			foreach (var c in Cases)
			{
				var script = new Script();
				DynValue fn = script.DoString(c.Source);
				var n = DynValue.NewNumber(c.Iterations);
				script.Call(fn, DynValue.NewNumber(Math.Max(1, c.Iterations / 10))); // warm-up

				var times = new List<double>();
				string check = null;

				for (int r = 0; r < runs; r++)
				{
					var sw = Stopwatch.StartNew();
					DynValue result = script.Call(fn, n);
					sw.Stop();
					times.Add(sw.Elapsed.TotalMilliseconds * 1e6 / c.Iterations);
					check = result.ToPrintString();
				}

				times.Sort();
				bool ok = c.Check == null || c.Check == check;
				if (!ok) failures++;

				Console.WriteLine("| {0} | {1} | {2:0.0} | {3:0.0} | {4:0.0} | {5}{6} | {7} |",
					c.Name, c.Iterations, times[times.Count / 2], times[0], times[times.Count - 1],
					check, ok ? "" : " (expected " + c.Check + ")", c.Note ?? "");
			}

			return failures == 0 ? 0 : 1;
		}
	}
}
