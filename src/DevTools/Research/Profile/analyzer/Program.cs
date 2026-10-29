using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Symbols;

// Reads a .nettrace and prints, for the busiest thread:
//  - exclusive (self) and inclusive CPU sample shares per method
//  - hot caller->callee edges for the top self methods
//  - allocation-tick bytes per type with the first two MoonSharp frames of the allocating stack
static class Program
{
	static string Name(TraceCodeAddress ca)
	{
		var m = ca.Method;
		if (m == null) return "[" + (ca.ModuleName ?? "native") + "]";
		string n = m.FullMethodName;
		int p = n.IndexOf('(');
		if (p > 0) n = n.Substring(0, p);
		return n.Replace("MoonSharp.Interpreter.", "MS.");
	}

	static int Main(string[] args)
	{
		if (args.Length < 1)
		{
			Console.Error.WriteLine("usage: Analyzer <trace.nettrace> [top N] [symbol folder]");
			return 2;
		}
		string path = args[0];
		int top = args.Length > 1 ? int.Parse(args[1]) : 25;
		string etlx = TraceLog.CreateFromEventPipeDataFile(path, Path.ChangeExtension(path, ".etlx"));
		using var log = new TraceLog(etlx);

		// --- CPU samples --------------------------------------------------------------------
		var perThread = new Dictionary<int, int>();
		var samples = new List<(int Tid, TraceCallStack Stack, bool External)>();
		foreach (var ev in log.Events)
		{
			if (ev.ProviderName != "Microsoft-DotNETCore-SampleProfiler") continue;
			var st = ev.CallStack();
			if (st == null) continue;
			bool ext = false;
			try { ext = Convert.ToInt32(ev.PayloadByName("Type")) == 1; } catch { }
			samples.Add((ev.ThreadID, st, ext));
			perThread.TryGetValue(ev.ThreadID, out int c); perThread[ev.ThreadID] = c + 1;
		}
		// busiest thread = the one with most samples whose stack contains MoonSharp frames
		var mtCounts = new Dictionary<int, int>();
		foreach (var s in samples)
		{
			for (var f = s.Stack; f != null; f = f.Caller)
				if (Name(f.CodeAddress).StartsWith("MS.")) { mtCounts.TryGetValue(s.Tid, out int c); mtCounts[s.Tid] = c + 1; break; }
		}
		int tid = mtCounts.OrderByDescending(k => k.Value).First().Key;
		var excl = new Dictionary<string, int>();
		var lines = new Dictionary<string, int>();
		var reader = new SymbolReader(TextWriter.Null, args.Length > 2 ? args[2] : Path.GetDirectoryName(path));
		reader.SecurityCheck = _ => true;
		var incl = new Dictionary<string, int>();
		var edges = new Dictionary<(string, string), int>();
		int total = 0, external = 0;
		foreach (var s in samples)
		{
			if (s.Tid != tid) continue;
			// keep only samples inside the workload (stack contains a MoonSharp frame)
			bool inMs = false;
			for (var f = s.Stack; f != null; f = f.Caller) if (Name(f.CodeAddress) == "MS.Script.Call") { inMs = true; break; }
			if (!inMs) continue;
			total++;
			if (s.External) external++;
			// first managed frame name
			string leaf = Name(s.Stack.CodeAddress);
			string self = s.External ? "[runtime/native] under " + leaf : leaf;
			excl.TryGetValue(self, out int e); excl[self] = e + 1;
			for (var f = s.Stack; f != null; f = f.Caller)
			{
				string n = Name(f.CodeAddress);
				if (!n.StartsWith("MS.")) continue;
				string line = n;
				try
				{
					var loc = log.CodeAddresses.GetSourceLine(reader, f.CodeAddress.CodeAddressIndex);
					if (loc != null) line = Path.GetFileName(loc.SourceFile.BuildTimeFilePath) + ":" + loc.LineNumber + "  " + n;
				}
				catch { }
				lines.TryGetValue(line, out int li); lines[line] = li + 1;
				break;
			}
			var seen = new HashSet<string>();
			string child = null;
			for (var f = s.Stack; f != null; f = f.Caller)
			{
				string n = Name(f.CodeAddress);
				if (seen.Add(n)) { incl.TryGetValue(n, out int i); incl[n] = i + 1; }
				if (child != null && child != n)
				{
					edges.TryGetValue((n, child), out int ed); edges[(n, child)] = ed + 1;
				}
				child = n;
			}
		}
		Console.WriteLine($"CPU: thread {tid}, {total} samples in MoonSharp stacks ({100.0 * external / Math.Max(1, total):0.0}% External/native-runtime)");
		Console.WriteLine("Self (exclusive):");
		foreach (var kv in excl.OrderByDescending(k => k.Value).Take(top))
			Console.WriteLine($"  {100.0 * kv.Value / total,5:0.0}%  {kv.Key}");
		Console.WriteLine("Source lines (first MoonSharp frame of each sample):");
		foreach (var kv in lines.OrderByDescending(k => k.Value).Take(top + 10))
			Console.WriteLine($"  {100.0 * kv.Value / total,5:0.0}%  {kv.Key}");
		Console.WriteLine("Inclusive:");
		foreach (var kv in incl.OrderByDescending(k => k.Value).Where(k => !k.Key.StartsWith("System.Threading") && !k.Key.StartsWith("[")).Take(top))
			Console.WriteLine($"  {100.0 * kv.Value / total,5:0.0}%  {kv.Key}");
		Console.WriteLine("Hot call edges (caller -> callee, % of samples with that edge on stack):");
		foreach (var kv in edges.OrderByDescending(k => k.Value).Where(k => k.Key.Item1.StartsWith("MS.")).Take(top))
			Console.WriteLine($"  {100.0 * kv.Value / total,5:0.0}%  {kv.Key.Item1} -> {kv.Key.Item2}");

		// --- Allocation ticks with stacks ----------------------------------------------------
		var alloc = new Dictionary<string, long>();
		var allocType = new Dictionary<string, long>();
		long allocTotal = 0;
		foreach (var ev in log.Events)
		{
			if (!(ev is GCAllocationTickTraceData at)) continue;
			if (ev.ThreadID != tid) continue;
			long amt = at.AllocationAmount64 != 0 ? at.AllocationAmount64 : at.AllocationAmount;
			var st = ev.CallStack();
			var frames = new List<string>();
			for (var f = st; f != null && frames.Count < 2; f = f.Caller)
			{
				string n = Name(f.CodeAddress);
				if (!n.StartsWith("MS.")) continue;
				try
				{
					var loc = log.CodeAddresses.GetSourceLine(reader, f.CodeAddress.CodeAddressIndex);
					if (loc != null) n = n + " (" + Path.GetFileName(loc.SourceFile.BuildTimeFilePath) + ":" + loc.LineNumber + ")";
				}
				catch { }
				frames.Add(n);
			}
			string type = (at.TypeName ?? "?").Replace("MoonSharp.Interpreter.", "MS.");
			string key = type + "  @ " + string.Join(" <- ", frames);
			alloc.TryGetValue(key, out long v); alloc[key] = v + amt;
			allocType.TryGetValue(type, out long w); allocType[type] = w + amt;
			allocTotal += amt;
		}
		Console.WriteLine($"alloc tick events on thread: {allocTotal} bytes sampled");
		if (allocTotal > 0)
		{
			Console.WriteLine("Allocation sites (sampled bytes share; type @ allocating MoonSharp frame <- its caller):");
			foreach (var kv in alloc.OrderByDescending(k => k.Value).Take(top))
				Console.WriteLine($"  {100.0 * kv.Value / allocTotal,5:0.0}%  {kv.Key}");
		}
		return 0;
	}
}
