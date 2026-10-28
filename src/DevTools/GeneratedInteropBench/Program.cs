using System;
using System.Collections.Generic;
using System.Diagnostics;
using MoonSharp.Interpreter;

namespace MoonSharp.GeneratedInteropBench
{
	/// <summary>
	/// The sample type: about 20 members of the kinds hosts bind (methods, an overload pair, a static
	/// method, properties, fields, a constructor, an operator). Its generated descriptor is what the
	/// size measurement in README.md counts.
	/// </summary>
	[MoonSharpUserData]
	public sealed class Calc
	{
		public int Field;
		public double Ratio = 0.5;
		public string Label = "calc";

		public Calc() { }
		public Calc(int field) { Field = field; }

		public int Value { get { return 1; } }
		public int Counter { get; set; }
		public string Name { get { return Label; } }
		public static int Instances { get { return 0; } }

		public int Add(int a, int b) { return a + b; }
		public double Scale(double x) { return x * Ratio; }
		public string Greet(string who) { return "hi " + who; }
		public bool IsPositive(int x) { return x > 0; }
		public long Wide(long x) { return x + 1; }
		public int Pick(int x) { return x; }
		public int Pick(string x) { return x.Length; }
		public void Reset() { Counter = 0; }
		public static int Twice(int x) { return x * 2; }
		public static Calc operator +(Calc a, Calc b) { return new Calc(a.Field + b.Field); }
	}

	static class Program
	{
		const string CallSource = "return function(obj, n) local s = 0 for i = 1, n do s = s + obj:Add(i, 2) end return s end";
		const string GetSource = "return function(obj, n) local s = 0 for i = 1, n do s = s + obj.Value end return s end";
		const string LoopSource = "return function(obj, n) local s = 0 for i = 1, n do s = s + i end return s end";

		static int Main(string[] args)
		{
			int runs = args.Length > 0 ? int.Parse(args[0]) : 7;
			const long n = 1000000;
			bool aot = !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

			Console.WriteLine("MoonSharp {0}, .NET {1}, {2}, {3} runs of {4} iterations, median", Script.VERSION, Environment.Version,
				aot ? "NativeAOT" : "JIT", runs, n);
			Console.WriteLine();
			Console.WriteLine("| Case | Descriptor | ns/iter | bytes/iter |");
			Console.WriteLine("|---|---|---:|---:|");

			var modes = new List<string> { "Reflection", "LazyOptimized" };
#if !NO_GENERATOR
			modes.Add("Generated");
#endif
			Run("loop (no interop)", LoopSource, "Reflection", n, runs, "500000500000");

			foreach (string mode in modes)
				Run("call obj:Add(i, 2)", CallSource, mode, n, runs, "500002500000");

			foreach (string mode in modes)
				Run("get obj.Value", GetSource, mode, n, runs, "1000000");

			return 0;
		}

		static void Register(string mode)
		{
			UserData.UnregisterType<Calc>();

#if !NO_GENERATOR
			if (mode == "Generated")
			{
				UserData.RegisterType(MoonSharp.Generated.GeneratedUserData.CreateDescriptor(typeof(Calc)));
				return;
			}
#endif
			UserData.RegisterType<Calc>((InteropAccessMode)Enum.Parse(typeof(InteropAccessMode), mode));
		}

		static void Run(string name, string source, string mode, long n, int runs, string check)
		{
			Register(mode);
			Script script = new Script();
			DynValue fn = script.DoString(source);
			var calc = new Calc();

			script.Call(fn, calc, (double)(n / 10)); // warm-up, and the lazy modes compile here

			var times = new List<double>();
			double bytes = 0;

			for (int r = 0; r < runs; r++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
				long a0 = GC.GetAllocatedBytesForCurrentThread();
				var sw = Stopwatch.StartNew();
				string result = script.Call(fn, calc, (double)n).ToPrintString();
				sw.Stop();
				bytes = (GC.GetAllocatedBytesForCurrentThread() - a0) / (double)n;
				times.Add(sw.Elapsed.TotalMilliseconds * 1e6 / n);

				if (result != check)
					throw new InvalidOperationException(name + " " + mode + ": got " + result + ", expected " + check);
			}

			times.Sort();
			Console.WriteLine("| {0} | {1} | {2:0.0} | {3:0.0} |", name, mode, times[times.Count / 2], bytes);
		}
	}
}
