using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

// Profiling driver for MoonSharp. Workload sources are the same as
// src/DevTools/Benchmarks/LuaWorkloads.cs (branch chore/devtools-benchmarks),
// which mirror xLua-NG Test/Native/flag_bench.c.
//
// Usage:
//   Profile bench [runs]                 time + bytes/iter for every workload
//   Profile loop <name> <seconds>        run one workload repeatedly (for dotnet-trace)
//   Profile alloc <name>                 allocation-tick histogram by type (in-process EventListener)
//   Profile counts <name>                GC counts per gen + bytes/iter over a fixed number of calls

public class Calc
{
	public int Value { get; set; } = 1;
	public int Add(int a, int b) { return a + b; }
}

static class Program
{
	const long K = 1000;

	static readonly (string Name, long Iterations, string Source, string Check)[] Cases =
	{
		("empty_loop", 4000 * K, "return function(n) for i = 1, n do end end", null),
		("function_call", 1000 * K, "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end", "1000000"),
		("method_call", 1000 * K, "local C = {} C.__index = C\nfunction C:inc() self.v = self.v + 1 end\nlocal o = setmetatable({ v = 0 }, C)\nreturn function(n) for i = 1, n do o:inc() end return o.v end", null),
		("table_array_insert", 1000 * K, "return function(n) local t = {} for i = 1, n do t[i] = i end return #t end", "1000000"),
		("table_array_read", 1000 * K, "local t = {} for i = 1, 1024 do t[i] = i end\nreturn function(n) local s, j = 0, 1 for i = 1, n do s = s + t[j] j = j + 1 if j > 1024 then j = 1 end end return s end", "512370976"),
		("table_hash_set_get", 1000 * K, "local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\nreturn function(n) local t, s, j = {}, 0, 1 for i = 1, n do local k = keys[j] t[k] = i s = s + t[k] j = j + 1 if j > 64 then j = 1 end end return s end", "500000500000"),
		("string_format", 500 * K, "local format = string.format\nreturn function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = format('%d:%s', i, 'x') j = j + 1 if j > 64 then j = 1 end end return buf[64] end", "499968:x"),
		("int_math", 2000 * K, "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end", "967568"),
		("fib_recursive", 20 * K, "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end", "2880000"),
		("coroutine_switch", 500 * K, "return function(n) local co = coroutine.wrap(function() local i = 0 while true do i = i + 1 coroutine.yield(i) end end)\nlocal s = 0 for i = 1, n do s = s + co() end return s end", "125000250000"),
		("closure_create_call", 1000 * K, "return function(n) local s = 0 for i = 1, n do local f = function() return i end s = s + f() end return s end", "500000500000"),
		// CLR interop: Lua calls an instance method with 2 int args plus a property get, under the default
		// registration (InteropAccessMode.Default -> LazyOptimized on this JIT platform).
		("clr_interop", 1000 * K, "return function(n) local s = 0 for i = 1, n do s = s + obj:Add(i, 2) + obj.Value end return s end", "500003500000"),
		// The same with InteropAccessMode.Reflection: what IL2CPP/AOT gets (no expression-tree compilation).
		("clr_interop_reflection", 1000 * K, "return function(n) local s = 0 for i = 1, n do s = s + obj:Add(i, 2) + obj.Value end return s end", "500003500000"),
	};

	static (Script, DynValue) Setup((string Name, long Iterations, string Source, string Check) c)
	{
		var script = new Script();
		if (c.Name.StartsWith("clr_interop"))
		{
			var mode = c.Name == "clr_interop_reflection" ? InteropAccessMode.Reflection : InteropAccessMode.Default;
			UserData.UnregisterType<Calc>();
			UserData.RegisterType<Calc>(mode);
			script.Globals["obj"] = new Calc();
		}
		DynValue fn = script.DoString(c.Source);
		script.Call(fn, DynValue.NewNumber(Math.Max(1, c.Iterations / 10))); // warm-up
		return (script, fn);
	}

	static int Main(string[] args)
	{
		string mode = args.Length > 0 ? args[0] : "bench";
		switch (mode)
		{
			case "bench": return Bench(args.Length > 1 ? int.Parse(args[1]) : 5, args.Skip(2).ToArray());
			case "loop": return Loop(args[1], double.Parse(args[2]));
			case "alloc": return Alloc(args[1]);
			default: Console.Error.WriteLine("unknown mode"); return 2;
		}
	}

	static int Bench(int runs, string[] only)
	{
		Console.WriteLine("MoonSharp {0} on .NET {1}, {2} runs (median ns/iter, bytes/iter, gen0 GCs per 1M iter)", Script.VERSION, Environment.Version, runs);
		Console.WriteLine("| Case | Iter | Median ns | Min ns | Bytes/iter | Gen0/1M | Gen1/1M | Gen2/1M | Check |");
		Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---|");
		int fails = 0;
		foreach (var c in Cases)
		{
			if (only.Length > 0 && !only.Contains(c.Name)) continue;
			var (script, fn) = Setup(c);
			var n = DynValue.NewNumber(c.Iterations);
			var times = new List<double>();
			var bytes = new List<double>();
			int g0 = 0, g1 = 0, g2 = 0;
			string check = null;
			for (int r = 0; r < runs; r++)
			{
				int a0 = GC.CollectionCount(0), a1 = GC.CollectionCount(1), a2 = GC.CollectionCount(2);
				long b0 = GC.GetAllocatedBytesForCurrentThread();
				var sw = Stopwatch.StartNew();
				var res = script.Call(fn, n);
				sw.Stop();
				long b1 = GC.GetAllocatedBytesForCurrentThread();
				g0 += GC.CollectionCount(0) - a0; g1 += GC.CollectionCount(1) - a1; g2 += GC.CollectionCount(2) - a2;
				times.Add(sw.Elapsed.TotalMilliseconds * 1e6 / c.Iterations);
				bytes.Add((double)(b1 - b0) / c.Iterations);
				check = res.ToPrintString();
			}
			times.Sort(); bytes.Sort();
			double per = 1e6 / ((double)c.Iterations * runs);
			bool ok = c.Check == null || c.Check == check;
			if (!ok) fails++;
			Console.WriteLine("| {0} | {1} | {2:0.0} | {3:0.0} | {4:0.0} | {5:0.0} | {6:0.00} | {7:0.00} | {8}{9} |", c.Name, c.Iterations,
				times[times.Count / 2], times[0], bytes[bytes.Count / 2], g0 * per, g1 * per, g2 * per, check, ok ? "" : " (expected " + c.Check + ")");
		}
		return fails == 0 ? 0 : 1;
	}

	static int Loop(string name, double seconds)
	{
		var c = Cases.First(x => x.Name == name);
		var (script, fn) = Setup(c);
		var n = DynValue.NewNumber(c.Iterations);
		// extra warm-up so tier-1 code is in place before tracing matters
		for (int i = 0; i < 3; i++) script.Call(fn, n);
		Console.WriteLine("READY " + name);
		var sw = Stopwatch.StartNew();
		long calls = 0;
		while (sw.Elapsed.TotalSeconds < seconds) { script.Call(fn, n); calls++; }
		Console.WriteLine("DONE {0} calls={1} ns/iter={2:0.0}", name, calls, sw.Elapsed.TotalMilliseconds * 1e6 / (calls * c.Iterations));
		return 0;
	}

	sealed class AllocListener : EventListener
	{
		public readonly Dictionary<string, (long Bytes, long Ticks)> ByType = new Dictionary<string, (long, long)>();
		public volatile bool On;
		protected override void OnEventSourceCreated(EventSource source)
		{
			if (source.Name == "Microsoft-Windows-DotNETRuntime")
				EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1 /* GC */);
		}
		protected override void OnEventWritten(EventWrittenEventArgs e)
		{
			if (!On || e.EventName == null || !e.EventName.StartsWith("GCAllocationTick")) return;
			string type = null; long amount = 0;
			for (int i = 0; i < e.PayloadNames.Count; i++)
			{
				if (e.PayloadNames[i] == "TypeName") type = e.Payload[i] as string;
				else if (e.PayloadNames[i] == "AllocationAmount64") amount = Convert.ToInt64(e.Payload[i]);
				else if (e.PayloadNames[i] == "AllocationAmount" && amount == 0) amount = Convert.ToInt64(e.Payload[i]);
			}
			type ??= "?";
			lock (ByType)
			{
				ByType.TryGetValue(type, out var v);
				ByType[type] = (v.Bytes + amount, v.Ticks + 1);
			}
		}
	}

	static int Alloc(string name)
	{
		var c = Cases.First(x => x.Name == name);
		var (script, fn) = Setup(c);
		var n = DynValue.NewNumber(c.Iterations);
		using var l = new AllocListener();
		Thread.Sleep(200);
		int calls = Math.Max(2, (int)(4_000_000 / c.Iterations));
		if (c.Name == "fib_recursive") calls = 10;
		long b0 = GC.GetAllocatedBytesForCurrentThread();
		l.On = true;
		for (int i = 0; i < calls; i++) script.Call(fn, n);
		l.On = false;
		long b1 = GC.GetAllocatedBytesForCurrentThread();
		Thread.Sleep(500);
		long iters = calls * c.Iterations;
		long total = 0; lock (l.ByType) total = l.ByType.Values.Sum(v => v.Bytes);
		Console.WriteLine("ALLOC {0}: {1:0.0} bytes/iter (exact), {2} iterations; sampled type shares (AllocationTick ~100KB):", name, (double)(b1 - b0) / iters, iters);
		foreach (var kv in l.ByType.OrderByDescending(k => k.Value.Bytes).Take(15))
			Console.WriteLine("  {0,6:0.0}%  ~{1,7:0.0} B/iter  {2}", 100.0 * kv.Value.Bytes / total, (double)(b1 - b0) / iters * kv.Value.Bytes / total, kv.Key);
		return 0;
	}
}
