using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics.Tracing;
using System.Collections.Concurrent;

// Usage: CFlow <path to MoonSharp.Interpreter.dll> <variant> <repeats> [batches] [batch|fresh]
// Loads the given MoonSharp build in its own AssemblyLoadContext and times Script.LoadString of a
// generated source (fresh Script per batch of 64 loads, as Lua-CSharp's CompilationComparison does).
// Prints: variant repeats us/load bytes/load gen0 gen1 gen2 (per 64-load batch, medians).
static class Program
{
	// Lua-CSharp's "control-flow" block; the copy and its licence are in src/DevTools/Benchmarks/LuaCSharpScripts.cs.
	const string Block = MoonSharp.Benchmarks.LuaCSharpScripts.ControlFlowBlock;

	static string Source(string variant, int n)
	{
		string block = variant switch
		{
			"full" => Block,
			"localfn" => "do local function choose(value) if not value or value==false then return nil elseif value==true then return 1 else return value end end; total=total+choose(2) end;",
			"repeat" => "do local value=2; repeat value=value-1 until value==0; total=total+value end;",
			"whilegoto" => "do local value=0; while value<2 do value=value+1; if value==1 then goto skip end; total=total+value; ::skip:: end end;",
			"while" => "do local value=0; while value<2 do value=value+1; if value==1 then total=total+1 end; total=total+value end end;",
			"gotoonly" => "do local value=0; if value==0 then goto skip end; total=total+value; ::skip:: end;",
			"forbreak" => "do for index=1,2 do if index==2 then break end; total=total+index end end;",
			"plain" => "do local value=1; total=total+value end;",
			_ => throw new ArgumentException(variant),
		};
		return "local total=0;" + string.Concat(Enumerable.Repeat(block, n)) + "return total";
	}

	static int Main(string[] args)
	{
		if (args.Length < 3)
		{
			Console.Error.WriteLine("usage: CFlow <MoonSharp.Interpreter.dll> <full|localfn|repeat|whilegoto|while|gotoonly|forbreak|plain> <repeats> [batches] [batch|fresh]");
			return 2;
		}
		string dll = args[0], variant = args[1];
		int n = int.Parse(args[2]);
		int batches = args.Length > 3 ? int.Parse(args[3]) : 15;
		bool fresh = args.Length > 4 && args[4] == "fresh";
		bool clean = Environment.GetEnvironmentVariable("CFLOW_CLEAN") == "1"; // full GC before each batch, as BenchmarkDotNet does // fresh Script for every load (creation outside timing)
		if (Environment.GetEnvironmentVariable("CFLOW_TICKS") == "1") s_Listener = new TickListener();
		var alc = new AssemblyLoadContext("ms", false);
		Assembly asm = alc.LoadFromAssemblyPath(System.IO.Path.GetFullPath(dll));
		Type script = asm.GetType("MoonSharp.Interpreter.Script", true);
		Type core = asm.GetType("MoonSharp.Interpreter.CoreModules", true);
		Type table = asm.GetType("MoonSharp.Interpreter.Table", true);
		object complete = Enum.Parse(core, "Preset_Complete");
		MethodInfo load = script.GetMethod("LoadString", new[] { typeof(string), table, typeof(string) });
		ConstructorInfo ctor = script.GetConstructor(new[] { core });
		string src = Source(variant, n);
		const int Batch = 64;

		double[] us = new double[batches]; double[] bytes = new double[batches]; double[] cpu = new double[batches]; double[] pause = new double[batches];
		int[] g0 = new int[batches], g1 = new int[batches], g2 = new int[batches];
		// warm-up: 3 batches
		for (int b = -10; b < batches; b++)
		{
			object s = ctor.Invoke(new[] { complete });
			if (clean) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
			TimeSpan p0 = GC.GetTotalPauseDuration();
			int c0 = GC.CollectionCount(0), c1 = GC.CollectionCount(1), c2 = GC.CollectionCount(2);
			long a0 = GC.GetAllocatedBytesForCurrentThread();
			var sw = new Stopwatch(); long t0 = 0, tsum = 0;
			if (fresh)
			{
				object[] states = new object[Batch];
				for (int i = 0; i < Batch; i++) states[i] = ctor.Invoke(new[] { complete });
				a0 = GC.GetAllocatedBytesForCurrentThread();
				for (int i = 0; i < Batch; i++) { t0 = ThreadCpuNs(); sw.Start(); load.Invoke(states[i], new object[] { src, null, "comparison" }); sw.Stop(); tsum += ThreadCpuNs() - t0; }
			}
			else
			{
				t0 = ThreadCpuNs(); sw.Start();
				for (int i = 0; i < Batch; i++) load.Invoke(s, new object[] { src, null, "comparison" });
				sw.Stop(); tsum = ThreadCpuNs() - t0;
			}
			long a1 = GC.GetAllocatedBytesForCurrentThread();
			if (b < 0) continue;
			pause[b] = (GC.GetTotalPauseDuration() - p0).TotalMilliseconds * 1000 / Batch;
			us[b] = sw.Elapsed.TotalMilliseconds * 1000 / Batch;
			bytes[b] = (double)(a1 - a0) / Batch; cpu[b] = tsum / 1000.0 / Batch;
			g0[b] = GC.CollectionCount(0) - c0; g1[b] = GC.CollectionCount(1) - c1; g2[b] = GC.CollectionCount(2) - c2;
		}
		if (s_Listener != null)
		{
			foreach (var kv in s_Listener.Ticks.OrderByDescending(k => k.Value).Where(k => !k.Key.StartsWith("SOH")).Concat(s_Listener.Ticks.OrderByDescending(k => k.Value).Take(4)))
				Console.WriteLine($"  alloc-tick {kv.Key}: {kv.Value}");
		}
		double cpuMean = cpu.Average(), pauseMean = pause.Average();
		Array.Sort(us); Array.Sort(bytes); Array.Sort(cpu); Array.Sort(pause);
		Console.WriteLine($"{variant} {(fresh ? "fresh" : "batch")}{(clean ? "+clean" : "")} n={n} cpu-us/load={cpu[batches / 2]:F1} (min {cpu[0]:F1}, mean {cpuMean:F1}) gcpause-mean={pauseMean:F1} gcpause-us/load={pause[batches / 2]:F1} wall-us/load={us[batches / 2]:F1} (min {us[0]:F1} max {us[^1]:F1}) bytes/load={bytes[batches / 2]:F0} gen0/batch={g0.Average():F1} gen1/batch={g1.Average():F1} gen2/batch={g2.Average():F1} srcLen={src.Length}");
		return 0;
	}

	static TickListener s_Listener;

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	struct Timespec { public long Sec; public long Nsec; }
	[System.Runtime.InteropServices.DllImport("libc")] static extern int clock_gettime(int clk, out Timespec ts);
	// CLOCK_THREAD_CPUTIME_ID: CPU time of this thread only; unaffected by time spent waiting for a core.
	static long ThreadCpuNs() { clock_gettime(3, out var t); return t.Sec * 1_000_000_000L + t.Nsec; }

	// Counts GCAllocationTick events (one per ~100 KB allocated) by kind and type name.
	sealed class TickListener : EventListener
	{
		public readonly ConcurrentDictionary<string, int> Ticks = new ConcurrentDictionary<string, int>();
		protected override void OnEventSourceCreated(EventSource source)
		{
			if (source.Name == "Microsoft-Windows-DotNETRuntime")
				EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
		}
		protected override void OnEventWritten(EventWrittenEventArgs e)
		{
			if (e.EventName != null && e.EventName.StartsWith("GCStart"))
			{
				string depth = e.Payload[e.PayloadNames.IndexOf("Depth")].ToString();
				string reason = e.Payload[e.PayloadNames.IndexOf("Reason")].ToString();
				string gctype = e.PayloadNames.Contains("Type") ? e.Payload[e.PayloadNames.IndexOf("Type")].ToString() : "?";
				Ticks.AddOrUpdate($"GC gen{depth} reason={reason} type={gctype}", 1, (_, v) => v + 1);
				return;
			}
			if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick")) return;
			int kindIdx = e.PayloadNames.IndexOf("AllocationKind");
			int typeIdx = e.PayloadNames.IndexOf("TypeName");
			string kind = kindIdx >= 0 ? (Convert.ToInt32(e.Payload[kindIdx]) == 1 ? "LOH" : (Convert.ToInt32(e.Payload[kindIdx]) == 2 ? "POH" : "SOH")) : "?";
			string type = typeIdx >= 0 ? e.Payload[typeIdx] as string : "?";
			Ticks.AddOrUpdate(kind + " " + type, 1, (_, v) => v + 1);
		}
	}
}
