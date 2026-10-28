using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Compares two builds of MoonSharp.Interpreter.dll in one process. Each build loads into its own
	/// AssemblyLoadContext and the rounds alternate A, B, A, B, so machine noise and drift hit both builds
	/// equally. Run with: dotnet run -c Release -- ab path/to/A/MoonSharp.Interpreter.dll path/to/B/MoonSharp.Interpreter.dll [rounds]
	/// </summary>
	static class AbCompare
	{
		sealed class Build
		{
			public string Name;
			public Type ScriptType;
			public MethodInfo DoString, Call, CreateCoroutine;
			public object Script;

			public object SoftSandbox;

			public object NewScript() => Activator.CreateInstance(ScriptType);
			public object NewScript(object modules) => Activator.CreateInstance(ScriptType, modules);
			public object Run(string code) => DoString.Invoke(Script, new object[] { code, null, null });
			public object Invoke(object fn, object[] args) => Call.Invoke(Script, new object[] { fn, args });
		}

		sealed class Workload
		{
			public string Name;
			public int Iterations;
			// inner loop iterations per call: times and allocations are reported per inner iteration
			public int Scale = 1;
			public Func<Build, Action> Prepare;
		}

		public static int Run(string[] args)
		{
			if (args.Length < 2)
			{
				Console.Error.WriteLine("usage: ab <A/MoonSharp.Interpreter.dll> <B/MoonSharp.Interpreter.dll> [rounds] [tables|tablemem]");
				return 2;
			}

			int rounds = args.Length > 2 ? int.Parse(args[2]) : 15;
			var builds = new[] { Load("A", args[0]), Load("B", args[1]) };

			string set = args.Length > 3 ? args[3] : null;

			if (set == "tablemem")
				return TableWorkloads.RetainedBytes(builds.Select(b => (Func<string, object>)b.Run).ToArray(),
					builds.Select(b => (Func<object, object[], object>)b.Invoke).ToArray());

			var workloads = (set == "tables") ? TableWorkloads.Cases.Select(c => new Workload
			{
				Name = c.Name,
				Iterations = 3,
				Scale = c.Arg * c.PerArg,
				Prepare = b =>
				{
					object fn = b.Run(c.Source);
					var callArgs = new object[] { c.Arg };
					return () => b.Invoke(fn, callArgs);
				}
			}).ToList() : new List<Workload>
			{
				new Workload { Name = "NewScript_Default", Iterations = 200, Prepare = b => () => b.NewScript() },
				new Workload { Name = "NewScript_SoftSandbox", Iterations = 200, Prepare = b => () => b.NewScript(b.SoftSandbox) },
				new Workload { Name = "CreateCoroutine", Iterations = 500, Prepare = b =>
				{
					object body = b.Run("return function() coroutine.yield(1) end");
					return () => b.CreateCoroutine.Invoke(b.Script, new[] { body });
				}},
				new Workload { Name = "CallEligibility", Iterations = 20000, Prepare = b =>
				{
					object fn = b.Run(Workloads.Eligibility);
					object player = b.Run("return { level = 12, country = 'DE', flags = { beta = true } }");
					var callArgs = new object[] { player, "offers" };
					return () => b.Invoke(fn, callArgs);
				}},
				CallWorkload("Fib20", Workloads.Fib, 20, 20),
				CallWorkload("SumLoop100K", Workloads.SumLoop, 100000, 10),
				CallWorkload("TableInsertLookup1K", Workloads.TableWork, 1000, 200),
				CallWorkload("Concat1K", Workloads.Concat, 1000, 40),
				CallWorkload("StringFormat1K", Workloads.StringFormat, 1000, 100),
				CallWorkload("StringFormatMix1K", Workloads.StringFormatMix, 1000, 50),
				// Pattern workloads report time and bytes per inner iteration.
				CallWorkload("StringPattern", Workloads.StringPattern, 1000, 20, 1000),
				CallWorkload("Gsub", Workloads.Gsub, 200, 20, 200),
				CallWorkload("Gmatch", Workloads.Gmatch, 500, 20, 500),
				CallWorkload("GsubCapture", Workloads.GsubCapture, 400, 50, 400),
			};

			Console.WriteLine("A: {0}", Path.GetFullPath(args[0]));
			Console.WriteLine("B: {0}", Path.GetFullPath(args[1]));
			Console.WriteLine("{0} interleaved rounds per workload, .NET {1}", rounds, Environment.Version);
			Console.WriteLine();
			Console.WriteLine("| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |");
			Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|");

			foreach (Workload w in workloads)
			{
				var actions = builds.Select(b => w.Prepare(b)).ToArray();
				var times = new[] { new List<double>(), new List<double>() };
				var allocs = new long[2];

				// Warm up both builds, then measure.
				for (int k = 0; k < 2; k++)
					Measure(actions[k], Math.Max(1, w.Iterations / 4), out _);

				// Alternate the order (A then B, B then A) so neither build always runs on a warmer cache.
				for (int r = 0; r < rounds; r++)
				{
					for (int j = 0; j < 2; j++)
					{
						int k = (r % 2 == 0) ? j : 1 - j;
						long alloc;
						times[k].Add(Measure(actions[k], w.Iterations, out alloc) / w.Scale);
						allocs[k] = alloc / w.Scale;
					}
				}

				var ratios = Enumerable.Range(0, rounds).Select(r => times[1][r] / times[0][r]).OrderBy(x => x).ToList();
				double ma = Median(times[0]), mb = Median(times[1]);

				Console.WriteLine("| {0} | {1} | {2} | {3:0.000} | {4:0.000} to {5:0.000} | {6} | {7} |",
					w.Name, FormatTime(ma), FormatTime(mb), Median(ratios), ratios[rounds / 4], ratios[rounds * 3 / 4],
					FormatBytes(allocs[0]), FormatBytes(allocs[1]));
			}

			return 0;
		}

		static Workload CallWorkload(string name, string source, int arg, int iterations, int scale = 1)
		{
			return new Workload
			{
				Name = name,
				Iterations = iterations,
				Scale = scale,
				Prepare = b =>
				{
					object fn = b.Run(source);
					var callArgs = new object[] { arg };
					return () => b.Invoke(fn, callArgs);
				}
			};
		}

		static Build Load(string name, string dllPath)
		{
			string full = Path.GetFullPath(dllPath);
			var context = new AssemblyLoadContext("moonsharp-" + name, false);
			Assembly asm = context.LoadFromAssemblyPath(full);
			Type script = asm.GetType("MoonSharp.Interpreter.Script", true);
			Type dynValue = asm.GetType("MoonSharp.Interpreter.DynValue", true);
			Type table = asm.GetType("MoonSharp.Interpreter.Table", true);

			var b = new Build
			{
				Name = name,
				ScriptType = script,
				DoString = script.GetMethod("DoString", new[] { typeof(string), table, typeof(string) }),
				Call = script.GetMethod("Call", new[] { dynValue, typeof(object[]) }),
				CreateCoroutine = script.GetMethod("CreateCoroutine", new[] { dynValue }),
				SoftSandbox = Enum.Parse(asm.GetType("MoonSharp.Interpreter.CoreModules", true), "Preset_SoftSandbox"),
			};

			b.Script = b.NewScript();
			return b;
		}

		// Returns nanoseconds per operation; alloc receives bytes allocated per operation.
		static double Measure(Action action, int iterations, out long alloc)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			long a0 = GC.GetAllocatedBytesForCurrentThread();
			var sw = Stopwatch.StartNew();

			for (int i = 0; i < iterations; i++)
				action();

			sw.Stop();
			alloc = (GC.GetAllocatedBytesForCurrentThread() - a0) / iterations;
			return sw.Elapsed.TotalMilliseconds * 1e6 / iterations;
		}

		static double Median(List<double> values)
		{
			var s = values.OrderBy(x => x).ToList();
			return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
		}

		static string FormatTime(double ns)
		{
			if (ns >= 1e6) return (ns / 1e6).ToString("0.00") + " ms";
			if (ns >= 1e3) return (ns / 1e3).ToString("0.00") + " µs";
			return ns.ToString(ns < 100 ? "0.0" : "0") + " ns";
		}

		static string FormatBytes(long bytes)
		{
			if (bytes >= 1 << 20) return (bytes / (double)(1 << 20)).ToString("0.00") + " MB";
			if (bytes >= 1 << 10) return (bytes / 1024.0).ToString("0.0") + " KB";
			return bytes + " B";
		}
	}
}
