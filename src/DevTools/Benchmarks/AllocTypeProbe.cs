using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using MoonSharp.Interpreter;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Shows which CLR types a workload allocates. The exact total comes from
	/// GC.GetAllocatedBytesForCurrentThread; the split by type comes from the runtime's GCAllocationTick
	/// events (one sample per ~100 KB allocated), so it is a share, not an exact count.
	/// Run with: dotnet run -c Release -- alloc case [case ...]
	/// The idea of an in-process allocation-tick probe comes from Jint's benchmark project:
	/// https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint.Benchmark/AllocTypeProbe.cs#L7-L20
	/// </summary>
	static class AllocTypeProbe
	{
		sealed class Listener : EventListener
		{
			public readonly Dictionary<string, long> BytesByType = new Dictionary<string, long>();
			public volatile bool On;

			protected override void OnEventSourceCreated(EventSource source)
			{
				if (source.Name == "Microsoft-Windows-DotNETRuntime")
					EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1 /* GC keyword */);
			}

			protected override void OnEventWritten(EventWrittenEventArgs e)
			{
				if (!On || e.EventName == null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal))
					return;

				string type = "?";
				long amount = 0;

				for (int i = 0; i < e.PayloadNames.Count; i++)
				{
					if (e.PayloadNames[i] == "TypeName")
						type = e.Payload[i] as string ?? "?";
					else if (e.PayloadNames[i] == "AllocationAmount64")
						amount = Convert.ToInt64(e.Payload[i]);
					else if (e.PayloadNames[i] == "AllocationAmount" && amount == 0)
						amount = Convert.ToInt64(e.Payload[i]);
				}

				lock (BytesByType)
				{
					BytesByType.TryGetValue(type, out long v);
					BytesByType[type] = v + amount;
				}
			}
		}

		public static int Run(string[] args)
		{
			if (args.Length == 0)
			{
				Console.Error.WriteLine("usage: alloc <case> [case ...]; cases: " + string.Join(", ", LuaWorkloads.Cases.Select(c => c.Name)));
				return 2;
			}

			using (var listener = new Listener())
			{
				Thread.Sleep(200); // let the listener attach

				foreach (string name in args)
				{
					LuaWorkloads.Case c = LuaWorkloads.Cases.FirstOrDefault(x => x.Name == name);

					if (c == null)
					{
						Console.Error.WriteLine("unknown case " + name);
						return 2;
					}

					var script = new Script();
					Func<long, string> run = c.Prepare(script);
					run(Math.Max(1, c.Iterations / 10)); // warm-up

					lock (listener.BytesByType)
						listener.BytesByType.Clear();

					// Enough work for a few hundred allocation ticks.
					int calls = (int)Math.Max(2, 4000000 / c.Iterations);
					long b0 = GC.GetAllocatedBytesForCurrentThread();
					listener.On = true;
					for (int i = 0; i < calls; i++)
						run(c.Iterations);
					listener.On = false;
					long b1 = GC.GetAllocatedBytesForCurrentThread();
					Thread.Sleep(300); // drain pending events

					double perIter = (double)(b1 - b0) / (calls * c.Iterations);
					List<KeyValuePair<string, long>> rows;
					lock (listener.BytesByType)
						rows = listener.BytesByType.OrderByDescending(kv => kv.Value).ToList();
					long sampled = rows.Sum(kv => kv.Value);

					Console.WriteLine("{0}: {1:0.0} bytes/iter (exact), {2} iterations", c.Name, perIter, calls * c.Iterations);
					Console.WriteLine();
					Console.WriteLine("| Type | Share | ~Bytes/iter |");
					Console.WriteLine("|---|---:|---:|");

					foreach (var kv in rows.Take(15))
						Console.WriteLine("| {0} | {1:0.0}% | {2:0.0} |", kv.Key, 100.0 * kv.Value / sampled, perIter * kv.Value / sampled);

					Console.WriteLine();
				}
			}

			return 0;
		}
	}
}
