using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Running;

namespace MoonSharp.Benchmarks
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			if (args.Length > 0 && args[0] == "retained")
				return RetainedMemory.Run();

			if (args.Length > 0 && args[0] == "ab")
				return AbCompare.Run(args.Skip(1).ToArray());

			if (args.Length > 0 && args[0] == "workloads")
				return LuaWorkloads.Run(args.Skip(1).ToArray());

			if (args.Length > 1 && args[0] == "workloads-tsv")
			{
				int runs = args.Length > 2 ? int.Parse(args[2]) : 5;
				string[] names = args.Length > 3 ? args.Skip(3).ToArray() : WorkloadTsv.RegisterVmCases;
				using (var tsv = new StreamWriter(args[1]))
					return WorkloadTsv.Run(tsv, runs, names, Console.WriteLine);
			}

			if (args.Length > 0 && args[0] == "alloc")
				return AllocTypeProbe.Run(args.Skip(1).ToArray());

			if (args.Length > 0 && args[0] == "interop")
				return InteropWorkloads.Run(args.Skip(1).ToArray());

			if (args.Length > 0 && args[0] == "ab-interop")
				return InteropWorkloads.RunAb(args.Skip(1).ToArray());

			BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
			return 0;
		}
	}
}
