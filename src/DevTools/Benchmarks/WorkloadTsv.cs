using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using MoonSharp.Interpreter;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Runs LuaWorkloads cases and writes one tab-separated row per case: name, iterations, median, minimum
	/// and maximum ns per iteration over the timed runs, the check value, and OK or FAIL. The Unity test bed
	/// compiles this file and LuaWorkloads.cs too (src/Unity/UnityTestBed/sync.sh), so the .NET, Unity Mono
	/// and IL2CPP numbers come from the same code. No allocation counts: IL2CPP may not implement
	/// GC.GetAllocatedBytesForCurrentThread.
	/// Run with: dotnet run -c Release -- workloads-tsv FILE [runs] [case ...]
	/// </summary>
	static class WorkloadTsv
	{
		/// <summary>The workloads of the register VM measurement (docs/research/register-vm-spike.md).</summary>
		internal static readonly string[] RegisterVmCases =
		{
			"empty_loop", "int_math", "float_math", "function_call", "fib_recursive", "numeric_loop", "nbody",
		};

		/// <returns>0 when every case returned its check value.</returns>
		internal static int Run(TextWriter tsv, int runs, IEnumerable<string> names, Action<string> log)
		{
			int failures = 0;

			foreach (string name in names)
			{
				LuaWorkloads.Case c = LuaWorkloads.Cases.FirstOrDefault(x => x.Name == name);
				if (c == null)
					throw new ArgumentException("unknown workload " + name);

				var script = new Script();
				Func<long, string> run = c.Prepare(script);
				run(Math.Max(1, c.Iterations / 10)); // warm-up

				var times = new double[runs];
				string check = null;

				for (int r = 0; r < runs; r++)
				{
					var sw = Stopwatch.StartNew();
					check = run(c.Iterations);
					sw.Stop();
					times[r] = sw.Elapsed.TotalMilliseconds * 1e6 / c.Iterations;
				}

				Array.Sort(times);
				bool ok = c.Check == null || c.Check == check;
				if (!ok)
					failures++;

				string row = string.Join("\t", c.Name, c.Iterations.ToString(CultureInfo.InvariantCulture),
					Ns(times[runs / 2]), Ns(times[0]), Ns(times[runs - 1]), check, ok ? "OK" : "FAIL");
				tsv.WriteLine(row);
				log(ok ? row : row + " (expected " + c.Check + ")");
			}

			return failures == 0 ? 0 : 1;
		}

		static string Ns(double ns) => ns.ToString("0.0", CultureInfo.InvariantCulture);
	}
}
