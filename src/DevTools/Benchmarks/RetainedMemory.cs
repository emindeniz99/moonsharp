using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Measures managed heap retained by live Script and coroutine objects, which BenchmarkDotNet's
	/// allocation counter does not show. Run with: dotnet run -c Release -- retained
	/// </summary>
	static class RetainedMemory
	{
		public static int Run()
		{
			Console.WriteLine("| Objects kept alive | Count | Retained bytes | Bytes per object |");
			Console.WriteLine("|---|---:|---:|---:|");

			foreach (int n in new[] { 1, 10, 100, 1000 })
				Report("Script (default modules)", n, () => new Script());

			var host = new Script();
			var body = host.DoString("return function() coroutine.yield(1) end");

			foreach (int n in new[] { 1, 10, 100, 1000 })
				Report("Coroutine", n, () => host.CreateCoroutine(body));

			return 0;
		}

		static void Report(string name, int count, Func<object> create)
		{
			var keep = new List<object>(count);
			create();
			long before = Settle();

			for (int i = 0; i < count; i++)
				keep.Add(create());

			long after = Settle();
			GC.KeepAlive(keep);

			long retained = after - before;
			Console.WriteLine("| {0} | {1} | {2:N0} | {3:N0} |", name, count, retained, retained / count);
		}

		static long Settle()
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			return GC.GetTotalMemory(true);
		}
	}
}
