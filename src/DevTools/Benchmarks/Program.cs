using System;
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

			if (args.Length > 0 && args[0] == "interop")
				return InteropWorkloads.Run(args.Skip(1).ToArray());

			if (args.Length > 0 && args[0] == "ab-interop")
				return InteropWorkloads.RunAb(args.Skip(1).ToArray());

			BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
			return 0;
		}
	}
}
