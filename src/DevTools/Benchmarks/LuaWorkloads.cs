using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Micro-workloads. The pure Lua cases have the same sources and iteration counts as the pure-Lua cases of
	/// xLua-NG's Test/Native/flag_bench.c (MIT licence), so MoonSharp and a native Lua can be compared on the
	/// same machine. The host cases cross the C#/Lua boundary or exercise table paths with known
	/// super-linear costs. Each case reports time and exact allocated bytes per iteration.
	/// Run with: dotnet run -c Release -- workloads [runs] [case ...]
	/// </summary>
	static class LuaWorkloads
	{
		const long K = 1000;

		internal sealed class Case
		{
			public string Name;
			public long Iterations;
			public string Check;
			public string Note;

			// Builds the runner on a fresh Script: the runner executes n iterations and returns the check value.
			public Func<Script, Func<long, string>> Prepare;
		}

		static Case Lua(string name, long iterations, string source, string check, string note)
		{
			return new Case
			{
				Name = name,
				Iterations = iterations,
				Check = check,
				Note = note,
				Prepare = script =>
				{
					DynValue fn = script.DoString(source);
					return n => script.Call(fn, DynValue.NewNumber(n)).ToPrintString();
				},
			};
		}

		static Case Host(string name, long iterations, string check, string note, Func<Script, Func<long, string>> prepare)
		{
			return new Case { Name = name, Iterations = iterations, Check = check, Note = note, Prepare = prepare };
		}

		static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);

		internal static readonly Case[] Cases =
		{
			Lua("empty_loop", 4000 * K, "return function(n) for i = 1, n do end end", "void", "MoonSharp prints a missing return value as void"),
			Lua("function_call", 1000 * K, "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end", "1000000", null),
			Lua("method_call", 1000 * K, "local C = {} C.__index = C\nfunction C:inc() self.v = self.v + 1 end\nlocal o = setmetatable({ v = 0 }, C)\nreturn function(n) for i = 1, n do o:inc() end return o.v end", null, "check depends on prior runs"),
			Lua("table_array_insert", 1000 * K, "return function(n) local t = {} for i = 1, n do t[i] = i end return #t end", "1000000", null),
			Lua("table_array_read", 1000 * K, "local t = {} for i = 1, 1024 do t[i] = i end\nreturn function(n) local s, j = 0, 1 for i = 1, n do s = s + t[j] j = j + 1 if j > 1024 then j = 1 end end return s end", "512370976", null),
			Lua("table_hash_set_get", 1000 * K, "local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\nreturn function(n) local t, s, j = {}, 0, 1 for i = 1, n do local k = keys[j] t[k] = i s = s + t[k] j = j + 1 if j > 64 then j = 1 end end return s end", "500000500000", null),
			Lua("string_concat", 500 * K, "return function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = 'item' .. i j = j + 1 if j > 64 then j = 1 end end return buf[64] end", "item499968", null),
			Lua("string_format", 500 * K, "local format = string.format\nreturn function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = format('%d:%s', i, 'x') j = j + 1 if j > 64 then j = 1 end end return buf[64] end", "499968:x", null),
			Lua("closure_create_call", 1000 * K, "return function(n) local s = 0 for i = 1, n do local f = function() return i end s = s + f() end return s end", "500000500000", null),
			Lua("int_math", 2000 * K, "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end", "967568", null),
			Lua("fib_recursive", 20 * K, "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end", "2880000", null),
			Lua("float_math", 2000 * K, "return function(n) local x, y = 0.5, 0.25 for i = 1, n do x = x * 0.5 + y * 0.25 y = y * 0.5 + x / 3.0 + 0.001 end return string.format('%.6f', x + y) end", "0.004500", null),
			Lua("string_pattern", 100 * K, "local s = 'the quick brown fox 123 jumps over the lazy dog 4567'\nreturn function(n) local c = 0 for i = 1, n do local a, b = s:find('(%a+) (%d+)') local r, k = s:gsub('o', '0') c = c + a + k + #s:match('%d+$') end return c end", "2500000", null),
			// Lua 5.2 has no table.move: copy with a loop instead (one extra loop of 256 per iteration).
			Lua("table_sort_52", 2 * K, "local src, x = {}, 12345 for i = 1, 256 do x = (x * 1103515245 + 12345) % 2147483648 src[i] = x end\nreturn function(n) local s = 0 for i = 1, n do local t = {} for k = 1, 256 do t[k] = src[k] end table.sort(t) s = s + t[1] % 1000 end return s end", null, "variant: copy loop instead of table.move; the seed arithmetic exceeds 2^53 under doubles"),
			Lua("table_concat", 50 * K, "local parts = {} for i = 1, 32 do parts[i] = tostring(i) end\nreturn function(n) local s = 0 for i = 1, n do s = s + #table.concat(parts, ',') end return s end", "4300000", null),
			Lua("coroutine_switch", 500 * K, "return function(n) local co = coroutine.wrap(function() local i = 0 while true do i = i + 1 coroutine.yield(i) end end)\nlocal s = 0 for i = 1, n do s = s + co() end return s end", "125000250000", null),
			Lua("gc_small_tables", 500 * K, "return function(n) local keep, s = {}, 0 for i = 1, n do local t = { i, i + 1, x = i } keep[i % 128 + 1] = t s = s + t[2] end return s end", "125000750000", null),
			Lua("oop_inherit", 1000 * K, "local Base = {} Base.__index = Base function Base:get() return self.v end\nlocal Derived = setmetatable({}, Base) Derived.__index = Derived\nlocal o = setmetatable({ v = 3 }, Derived)\nreturn function(n) local s = 0 for i = 1, n do s = s + o:get() end return s end", "3000000", null),

			// MoonSharp-only cases (not in flag_bench.c): table operations that were quadratic in the table size.
			Lua("table_insert_len_10k", 10, "return function(n) local s = 0 for r = 1, n do local t = {} for i = 1, 10000 do table.insert(t, i) end s = s + #t end return s end", "100000", "MoonSharp-only; one iteration = 10k table.insert then #t"),
			Lua("table_remove_readd_10k", 5 * K, "local t, keys = {}, {} for i = 1, 10000 do t[i] = i end for i = 1, 64 do keys[i] = 'k' .. i t[keys[i]] = i end\nreturn function(n) local s, j = 0, 1 for i = 1, n do local v = table.remove(t) t[#t + 1] = v local k = keys[j] t[k] = nil t[k] = i j = j % 64 + 1 s = s + #t end return s end", "50000000", "MoonSharp-only; delete/reinsert churn on a 10k-entry table"),

			// ---- host boundary and interop (not in flag_bench) ----

			// C# -> Lua: Script.Call on a compiled function with 3 number arguments and 1 number result.
			Host("cs_to_lua_call3", 1000 * K, "1000002000000", "host loop calls a Lua function", script =>
			{
				DynValue fn = script.DoString("return function(a, b, c) return a * b + c end");
				DynValue two = DynValue.NewNumber(2), three = DynValue.NewNumber(3);
				return n =>
				{
					double sum = 0;
					for (long i = 0; i < n; i++)
						sum += script.Call(fn, DynValue.NewNumber(i), two, three).Number;
					return R(sum);
				};
			}),
			// Lua -> C#: a Lua loop calling a CallbackFunction with one argument.
			Host("lua_to_cs_call1", 1000 * K, "500001500000", null, script =>
			{
				script.Globals["f"] = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(args[0].Number + 1));
				DynValue fn = script.DoString("local f = f\nreturn function(n) local s = 0 for i = 1, n do s = s + f(i) end return s end");
				return n => script.Call(fn, DynValue.NewNumber(n)).ToPrintString();
			}),
			// Lua calls an instance method with 2 int arguments and reads an int property, default access mode
			// (LazyOptimized: compiles expression trees on first use where the platform allows it).
			Host("clr_interop", 1000 * K, "500003500000", "InteropAccessMode.Default", script => InteropLoop(script, InteropAccessMode.Default,
				"return function(n) local s = 0 for i = 1, n do s = s + obj:Add(i, 2) + obj.Value end return s end")),
			// The same through plain reflection: what IL2CPP/AOT gets.
			Host("clr_interop_reflection", 1000 * K, "500003500000", "InteropAccessMode.Reflection", script => InteropLoop(script, InteropAccessMode.Reflection,
				"return function(n) local s = 0 for i = 1, n do s = s + obj:Add(i, 2) + obj.Value end return s end")),
			// A property returning a value of a registered enum type, pushed to Lua on every read.
			Host("enum_push", 1000 * K, "1000000", "registered enum, InteropAccessMode.Reflection", script => InteropLoop(script, InteropAccessMode.Reflection,
				"return function(n) local c = 0 for i = 1, n do if obj.Mode then c = c + 1 end end return c end")),
			// A property returning an instance of an unregistered subclass of a registered type: the descriptor
			// lookup walks the base types on every push.
			Host("derived_type_push", 1000 * K, "1000000", "unregistered subclass of a registered type", script => InteropLoop(script, InteropAccessMode.Reflection,
				"return function(n) local c = 0 for i = 1, n do if obj.Child then c = c + 1 end end return c end")),

		};

		public enum Mode { A, B }

		public class Calc
		{
			readonly DerivedCalc m_Child;

			public Calc() { m_Child = this as DerivedCalc ?? new DerivedCalc(); }

			public int Value { get; set; } = 1;
			public Mode Mode { get; set; } = Mode.B;
			public Calc Child { get { return m_Child; } }

			public int Add(int a, int b) { return a + b; }
		}

		// Deliberately never registered.
		public class DerivedCalc : Calc { }

		static Func<long, string> InteropLoop(Script script, InteropAccessMode mode, string source)
		{
			UserData.UnregisterType<Calc>();
			UserData.UnregisterType<Mode>();
			UserData.RegisterType<Calc>(mode);
			UserData.RegisterType<Mode>(mode);
			script.Globals["obj"] = new Calc();
			DynValue fn = script.DoString(source);
			return n => script.Call(fn, DynValue.NewNumber(n)).ToPrintString();
		}

		public static int Run(string[] args)
		{
			int runs = args.Length > 0 ? int.Parse(args[0]) : 7;
			var only = new HashSet<string>(args.Skip(1));

			Console.WriteLine("MoonSharp {0} on .NET {1}, {2} runs per case (median, min, max ns per iteration; bytes allocated per iteration)", Script.VERSION, Environment.Version, runs);
			Console.WriteLine();
			Console.WriteLine("| Case | Iterations | Median ns | Min ns | Max ns | Bytes/iter | Check | Note |");
			Console.WriteLine("|---|---:|---:|---:|---:|---:|---|---|");
			int failures = 0;

			foreach (var c in Cases)
			{
				if (only.Count > 0 && !only.Contains(c.Name))
					continue;

				var script = new Script();
				Func<long, string> run = c.Prepare(script);
				run(Math.Max(1, c.Iterations / 10)); // warm-up

				var times = new List<double>();
				var bytes = new List<double>();
				string check = null;

				for (int r = 0; r < runs; r++)
				{
					long b0 = GC.GetAllocatedBytesForCurrentThread();
					var sw = Stopwatch.StartNew();
					check = run(c.Iterations);
					sw.Stop();
					long b1 = GC.GetAllocatedBytesForCurrentThread();
					times.Add(sw.Elapsed.TotalMilliseconds * 1e6 / c.Iterations);
					bytes.Add((double)(b1 - b0) / c.Iterations);
				}

				times.Sort();
				bytes.Sort();
				bool ok = c.Check == null || c.Check == check;
				if (!ok) failures++;

				Console.WriteLine("| {0} | {1} | {2:0.0} | {3:0.0} | {4:0.0} | {5:0.0} | {6}{7} | {8} |",
					c.Name, c.Iterations, times[times.Count / 2], times[0], times[times.Count - 1], bytes[bytes.Count / 2],
					check, ok ? "" : " (expected " + c.Check + ")", c.Note ?? "");
			}

			return failures == 0 ? 0 : 1;
		}
	}
}
