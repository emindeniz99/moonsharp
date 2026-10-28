using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.BasicDescriptors;
using MoonSharp.Interpreter.Interop.StandardDescriptors.HardwiredDescriptors;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Lua -> CLR interop workloads: Lua calls a registered C# instance method with two int arguments
	/// and reads an int property, under several InteropAccessMode values; plus enum and derived-type pushes.
	/// Reports ns and exact bytes per iteration.
	///   dotnet run -c Release -- interop [runs]
	///   dotnet run -c Release -- ab-interop A/MoonSharp.Interpreter.dll B/MoonSharp.Interpreter.dll [rounds]
	/// The A/B mode loads each build into its own AssemblyLoadContext and alternates the order per round.
	/// The hardwired case needs compile-time MoonSharp types, so it runs only in the in-process mode.
	/// </summary>
	static class InteropWorkloads
	{
		public enum Color { Red, Green, Blue }

		public class BaseThing { public int Id { get { return 1; } } }
		public sealed class DerivedThing : BaseThing { }

		public sealed class Lazy { }
		public sealed class Refl { }
		public sealed class Pre { }
		public sealed class Hw { }

		// One closed type per access mode, because registration is global per Type.
		public sealed class Calc<TTag>
		{
			static readonly DerivedThing s_Derived = new DerivedThing();
			static readonly Func<int, int> s_Inc = x => x + 1;
			public int Value { get { return 1; } }
			public int Add(int a, int b) { return a + b; }
			public Color GetColor() { return Color.Green; }
			public BaseThing MakeDerived() { return s_Derived; }
			public Func<int, int> GetFunc() { return s_Inc; }
			public BaseThing NewKey() { return new BaseThing(); }
		}

		const string CallSource = "return function(obj, n) local s = 0 for i = 1, n do s = s + obj:Add(i, 2) + obj.Value end return s end";
		const string EnumSource = "return function(obj, n) local s, g = 0, obj:GetColor() for i = 1, n do if obj:GetColor() == g then s = s + 1 end end return s end";
		const string DelegateSource = "return function(obj, n) local s = 0 for i = 1, n do s = s + obj:GetFunc()(1) end return s end";
		// 1000 distinct userdata as table keys, then lookups.
		const string KeysSource = "return function(obj, n) local keys, t = {}, {} for k = 1, 1000 do local u = obj:NewKey() keys[k] = u t[u] = k end local s = 0 for i = 1, n do s = s + t[keys[(i % 1000) + 1]] end return s end";
		const string DerivedSource = "return function(obj, n) local s = 0 for i = 1, n do s = s + obj:MakeDerived().Id end return s end";

		sealed class Case
		{
			public string Name;
			public long Iterations;
			public string Source;
			public Type CalcType;
			public string Mode; // InteropAccessMode name, or null for the hand-written hardwired descriptor
			public string Check;
		}

		static readonly Case[] Cases =
		{
			new Case { Name = "clr_interop", Iterations = 1000000, Source = CallSource, CalcType = typeof(Calc<Lazy>), Mode = "Default", Check = "500003500000" },
			new Case { Name = "clr_interop_reflection", Iterations = 1000000, Source = CallSource, CalcType = typeof(Calc<Refl>), Mode = "Reflection", Check = "500003500000" },
			new Case { Name = "clr_interop_preoptimized", Iterations = 1000000, Source = CallSource, CalcType = typeof(Calc<Pre>), Mode = "Preoptimized", Check = "500003500000" },
			new Case { Name = "clr_interop_hardwired", Iterations = 1000000, Source = CallSource, CalcType = typeof(Calc<Hw>), Mode = null, Check = "500003500000" },
			new Case { Name = "enum_push", Iterations = 500000, Source = EnumSource, CalcType = typeof(Calc<Lazy>), Mode = "Default", Check = "500000" },
			new Case { Name = "delegate_push", Iterations = 20000, Source = DelegateSource, CalcType = typeof(Calc<Lazy>), Mode = "Default", Check = "40000" },
			new Case { Name = "userdata_keys", Iterations = 100000, Source = KeysSource, CalcType = typeof(Calc<Lazy>), Mode = "Default", Check = "50050000" },
			new Case { Name = "derived_type_push", Iterations = 500000, Source = DerivedSource, CalcType = typeof(Calc<Lazy>), Mode = "Default", Check = "500000" },
		};

		sealed class Build
		{
			public string Name;
			public bool InProcess;
			public Type ScriptType, UserDataType, AccessModeType;
			public MethodInfo DoString, Call, RegisterType, ToPrintString;
			readonly HashSet<Type> m_Registered = new HashSet<Type>();

			public void Register(Type t, string mode)
			{
				if (!m_Registered.Add(t)) return;

				if (mode == null)
				{
					// The hand-written descriptor is compiled against the in-process MoonSharp only.
					UserData.RegisterType(new CalcHwDescriptor());
					return;
				}

				RegisterType.Invoke(null, new object[] { t, Enum.Parse(AccessModeType, mode), null });
			}

			// Returns a delegate that runs the workload once with n iterations and returns the check value.
			public Func<long, string> Prepare(Case c)
			{
				Register(typeof(Color), "Default");
				Register(typeof(BaseThing), "Default");
				Register(c.CalcType, c.Mode);

				object script = Activator.CreateInstance(ScriptType);
				object fn = DoString.Invoke(script, new object[] { c.Source, null, null });
				object calc = Activator.CreateInstance(c.CalcType);

				return n =>
				{
					object r = Call.Invoke(script, new object[] { fn, new object[] { calc, (double)n } });
					return (string)ToPrintString.Invoke(r, null);
				};
			}
		}

		static Build Load(string name, Assembly asm, bool inProcess)
		{
			Type script = asm.GetType("MoonSharp.Interpreter.Script", true);
			Type dynValue = asm.GetType("MoonSharp.Interpreter.DynValue", true);
			Type table = asm.GetType("MoonSharp.Interpreter.Table", true);
			Type userData = asm.GetType("MoonSharp.Interpreter.UserData", true);
			Type mode = asm.GetType("MoonSharp.Interpreter.InteropAccessMode", true);

			return new Build
			{
				Name = name,
				InProcess = inProcess,
				ScriptType = script,
				UserDataType = userData,
				AccessModeType = mode,
				DoString = script.GetMethod("DoString", new[] { typeof(string), table, typeof(string) }),
				Call = script.GetMethod("Call", new[] { dynValue, typeof(object[]) }),
				RegisterType = userData.GetMethod("RegisterType", new[] { typeof(Type), mode, typeof(string) }),
				ToPrintString = dynValue.GetMethod("ToPrintString", Type.EmptyTypes),
			};
		}

		// Runs once; returns ns per iteration and sets bytes per iteration (exact, this thread).
		static double Measure(Func<long, string> run, long n, out double bytesPerIter, out string check)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			long a0 = GC.GetAllocatedBytesForCurrentThread();
			var sw = Stopwatch.StartNew();
			check = run(n);
			sw.Stop();
			bytesPerIter = (GC.GetAllocatedBytesForCurrentThread() - a0) / (double)n;
			return sw.Elapsed.TotalMilliseconds * 1e6 / n;
		}

		public static int Run(string[] args)
		{
			int runs = args.Length > 0 ? int.Parse(args[0]) : 7;
			Build b = Load("in-process", typeof(Script).Assembly, true);
			Console.WriteLine("MoonSharp {0} on .NET {1}, {2} runs per case", Script.VERSION, Environment.Version, runs);
			Console.WriteLine();
			Console.WriteLine("| Case | Iterations | Median ns | Min ns | Bytes/iter | Check |");
			Console.WriteLine("|---|---:|---:|---:|---:|---|");
			int failures = 0;

			foreach (Case c in Cases)
			{
				Func<long, string> run = b.Prepare(c);
				run(Math.Max(1, c.Iterations / 10)); // warm-up

				var times = new List<double>();
				double bytes = 0;
				string check = null;

				for (int r = 0; r < runs; r++)
					times.Add(Measure(run, c.Iterations, out bytes, out check));

				times.Sort();
				bool ok = c.Check == check;
				if (!ok) failures++;

				Console.WriteLine("| {0} | {1} | {2:0.0} | {3:0.0} | {4:0.0} | {5}{6} |", c.Name, c.Iterations,
					times[times.Count / 2], times[0], bytes, check, ok ? "" : " (expected " + c.Check + ")");
			}

			return failures == 0 ? 0 : 1;
		}

		public static int RunAb(string[] args)
		{
			if (args.Length < 2)
			{
				Console.Error.WriteLine("usage: ab-interop <A/MoonSharp.Interpreter.dll> <B/MoonSharp.Interpreter.dll> [rounds]");
				return 2;
			}

			int rounds = args.Length > 2 ? int.Parse(args[2]) : 15;
			var builds = new[] { LoadFile("A", args[0]), LoadFile("B", args[1]) };

			Console.WriteLine("A: {0}", Path.GetFullPath(args[0]));
			Console.WriteLine("B: {0}", Path.GetFullPath(args[1]));
			Console.WriteLine("{0} interleaved rounds per case, .NET {1}", rounds, Environment.Version);
			Console.WriteLine();
			Console.WriteLine("| Case | A median ns | B median ns | B/A (median of rounds) | B/A interquartile range | A bytes/iter | B bytes/iter |");
			Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|");
			int failures = 0;

			foreach (Case c in Cases.Where(x => x.Mode != null))
			{
				var runs = builds.Select(b => b.Prepare(c)).ToArray();
				var times = new[] { new List<double>(), new List<double>() };
				var bytes = new double[2];
				long n = c.Iterations / 2;

				foreach (var run in runs)
					run(Math.Max(1, n / 10));

				for (int r = 0; r < rounds; r++)
				{
					for (int j = 0; j < 2; j++)
					{
						int k = (r % 2 == 0) ? j : 1 - j;
						string check;
						times[k].Add(Measure(runs[k], n, out bytes[k], out check));
					}
				}

				var ratios = Enumerable.Range(0, rounds).Select(r => times[1][r] / times[0][r]).OrderBy(x => x).ToList();

				Console.WriteLine("| {0} | {1:0.0} | {2:0.0} | {3:0.000} | {4:0.000} to {5:0.000} | {6:0.0} | {7:0.0} |",
					c.Name, Median(times[0]), Median(times[1]), Median(ratios), ratios[rounds / 4], ratios[rounds * 3 / 4],
					bytes[0], bytes[1]);
			}

			return failures;
		}

		static Build LoadFile(string name, string dllPath)
		{
			var context = new AssemblyLoadContext("moonsharp-interop-" + name, false);
			return Load(name, context.LoadFromAssemblyPath(Path.GetFullPath(dllPath)), false);
		}

		static double Median(List<double> values)
		{
			var s = values.OrderBy(x => x).ToList();
			return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
		}

		// What MoonSharp.Hardwire generates for Calc<Hw>, written by hand.
		sealed class CalcHwDescriptor : HardwiredUserDataDescriptor
		{
			public CalcHwDescriptor() : base(typeof(Calc<Hw>))
			{
				AddMember("Add", new AddMethod());
				AddMember("Value", new ValueProperty());
			}

			sealed class AddMethod : HardwiredMethodMemberDescriptor
			{
				public AddMethod()
				{
					Initialize("Add", false, new[] { new ParameterDescriptor("a", typeof(int)), new ParameterDescriptor("b", typeof(int)) }, false);
				}

				protected override object Invoke(Script script, object obj, object[] pars, int argscount)
				{
					return ((Calc<Hw>)obj).Add((int)pars[0], (int)pars[1]);
				}
			}

			sealed class ValueProperty : HardwiredMemberDescriptor
			{
				public ValueProperty() : base(typeof(int), "Value", false, MemberDescriptorAccess.CanRead) { }

				protected override object GetValueImpl(Script script, object obj)
				{
					return ((Calc<Hw>)obj).Value;
				}
			}
		}
	}
}
