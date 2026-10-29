using System;
using System.IO;
using MoonSharp.Benchmarks;
static class MainClass
{
	// Usage: bench <out.tsv> <runs>
	static int Main(string[] args)
	{
		if (args.Length < 2)
		{
			Console.Error.WriteLine("usage: bench <out.tsv> <runs>");
			return 2;
		}
		using (var tsv = new StreamWriter(args[0]))
			return WorkloadTsv.Run(tsv, int.Parse(args[1]), WorkloadTsv.RegisterVmCases, Console.WriteLine);
	}
}
