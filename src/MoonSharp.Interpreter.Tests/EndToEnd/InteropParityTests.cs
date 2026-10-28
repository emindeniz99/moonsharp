using System;
using System.Collections.Generic;
using MoonSharp.Generated;
using MoonSharp.Interpreter.Interop;
using NUnit.Framework;

// A struct the tests treat as if it came from another assembly: not partial, so the generated
// descriptor cannot reach inside its box and binds its mutating members by reflection.
[assembly: MoonSharp.Interpreter.MoonSharpUserDataTypes(
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropParityTests.ForeignVector),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropParityTests.ForeignPoint))]

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// The same interop probes under every InteropAccessMode and under the descriptors written by
	/// MoonSharp.SourceGenerator. The binding mode is a speed/memory choice, so it must never change what
	/// a script sees; every mode must print the same line and raise the same errors.
	/// </summary>
	[TestFixture]
	public partial class InteropParityTests
	{
		[MoonSharpUserData]
		public class ParityClass
		{
			public int Field;
			public int Prop { get; set; }
			public string Name { get { return "parity"; } }

			public int Add(int a, int b) { return a + b; }
			public string Concat(string a, int b) { return a + b; }
			public static int Twice(int x) { return x * 2; }
			public string Over(int x) { return "i" + x; }
			public string Over(string x) { return "s" + x; }
			public int Sum(params int[] xs) { int s = 0; foreach (int x in xs) s += x; return s; }
			public void OutPair(int x, out int y, ref int z) { y = x * 2; z = z + 1; }
			public ParityStruct MakeStruct(int x) { return new ParityStruct { X = x }; }
			public int TakesStruct(ParityStruct s) { return s.X; }
			public string Optional(int a, string b = "d", int c = 3) { return a + b + c; }
			public void Throw() { throw new InvalidOperationException("thrown by C#"); }
		}

		/// <summary>Partial: the generated descriptor changes it inside its box through a generated interface.</summary>
		[MoonSharpUserData]
		public partial struct ParityStruct
		{
			public int X;
			public int Y { get; set; }

			public void Bump() { X++; }
			public int Read() { return X; }
			public int BumpAndGet() { X += 10; return X; }
		}

		public struct ForeignVector
		{
			public int X, Y;

			public ForeignVector(int x, int y) { X = x; Y = y; }

			public static ForeignVector Zero { get { return new ForeignVector(0, 0); } }
			public int LengthSquared() { return X * X + Y * Y; }
			public void Scale(int k) { X *= k; Y *= k; }
			public override string ToString() { return X + ";" + Y; }
			public static ForeignVector operator +(ForeignVector a, ForeignVector b) { return new ForeignVector(a.X + b.X, a.Y + b.Y); }
		}

		public readonly struct ForeignPoint
		{
			public readonly int X, Y;

			public ForeignPoint(int x, int y) { X = x; Y = y; }

			public int Sum() { return X + Y; }
		}

		// Field and property set, a mutating method and a read back on a struct receiver: the userdata
		// holds one boxed struct, and every member access works on that box.
		const string Probe = @"
			local r = {}
			local function add(v) r[#r + 1] = tostring(v) end
			add(obj:Add(2, 3))
			add(obj:Concat('a', 1))
			obj.Prop = 5 add(obj.Prop)
			obj.Field = 6 add(obj.Field)
			add(Cls.Twice(4))
			add(obj:Over(1) .. obj:Over('x'))
			add(obj:Sum(1, 2, 3))
			local a, y, z = obj:OutPair(1, 5) add(a) add(y) add(z)
			vt.X = 1 add(vt.X)
			vt:Bump() add(vt.X)
			add(vt:Read())
			add(vt:BumpAndGet()) add(vt.X)
			vt.Y = 7 add(vt.Y)
			local made = obj:MakeStruct(9) made:Bump() add(made.X)
			add(obj:TakesStruct(made))
			add(obj:Optional(1) .. obj:Optional(1, 'x') .. obj:Optional(1, nil, 9))
			local v = FV.__new(3, 4)
			add(v.X) add(v:LengthSquared())
			v:Scale(2) add(v.X .. ',' .. v.Y)
			v.Y = 1 add(v.Y)
			add(v:ToString()) add(tostring(v))
			add((v + v).X)
			add(FV.Zero.Y)
			local p = FP.__new(1, 2) add(p:Sum() .. p.X)
			return table.concat(r, ',')";

		const string Expected = "5,a1,5,6,8,i1sx,6,nil,2,6,1,2,2,12,12,7,10,10,1d31x319,3,25,6,8,1,6;1,6;1,12,0,31";

		// Each runs alone; what it raises (exception type and message) must not depend on the mode.
		static readonly string[] ErrorProbes =
		{
			"obj:Add('x', 1)",
			"obj:Add()",
			"obj:Add(1e300, 1)",
			"obj:Over(true)",
			"obj.Prop = 'x'",
			"obj.Name = 'x'",
			"obj.Nope = 1",
			"Cls.Add(1, 2)",
			"Cls.Twice()",
			"obj:TakesStruct(nil)",
			"obj:TakesStruct(obj)",
			"obj:Sum(1, 'x')",
			"vt.X = 'a'",
			"vt:Bump(1, 2)",
			"FV.__new(1, 2):Scale('a')",
			"local v = FV.__new(1, 2) v.X = {}",
			"FP.__new(1, 2).X = 3",
		};

		// An exception thrown by the C# member reaches the host as itself.
		static readonly string[] ThrowProbes =
		{
			"obj:Throw()",
			"pcall(obj.Throw, obj)",
		};

		static readonly Type[] ProbeTypes = { typeof(ParityClass), typeof(ParityStruct), typeof(ForeignVector), typeof(ForeignPoint) };

		/// <summary>Registers the probe types under a mode, or with their generated descriptors when mode is null.</summary>
		static RegistrySnapshot Register(InteropAccessMode? mode)
		{
			var snapshot = new RegistrySnapshot(ProbeTypes);

			foreach (Type t in ProbeTypes)
			{
				if (mode.HasValue)
					UserData.RegisterType(t, mode.Value);
				else
					UserData.RegisterType(GeneratedUserData.CreateDescriptor(t));
			}

			return snapshot;
		}

		static Script NewScript()
		{
			Script s = new Script();
			s.Globals["obj"] = new ParityClass();
			s.Globals["vt"] = new ParityStruct();
			s.Globals["Cls"] = typeof(ParityClass);
			s.Globals["FV"] = typeof(ForeignVector);
			s.Globals["FP"] = typeof(ForeignPoint);
			return s;
		}

		static string RunProbe(InteropAccessMode? mode)
		{
			using (Register(mode))
			{
				Script s = NewScript();

				// Twice: the lazy modes compile adapters on first use, so the second pass runs them.
				string first = s.DoString(Probe).String;
				s.Globals["vt"] = new ParityStruct();
				string second = s.DoString(Probe).String;
				Assert.AreEqual(first, second, mode + ": first and second pass differ");
				return second;
			}
		}

		static string RunErrorProbes(InteropAccessMode? mode, string[] probes)
		{
			var lines = new List<string>();

			using (Register(mode))
			{
				foreach (string code in probes)
				{
					string outcome;

					try
					{
						NewScript().DoString(code);
						outcome = "no error";
					}
					catch (Exception ex)
					{
						outcome = ex.GetType().Name + ": " + ex.Message;
					}

					lines.Add(code + " => " + outcome);
				}
			}

			return string.Join("\n", lines.ToArray());
		}

		[Test]
		public void Interop_Parity_Reflection()
		{
			Assert.AreEqual(Expected, RunProbe(InteropAccessMode.Reflection));
		}

		[Test]
		public void Interop_Parity_LazyOptimized()
		{
			Assert.AreEqual(Expected, RunProbe(InteropAccessMode.LazyOptimized));
		}

		[Test]
		public void Interop_Parity_Preoptimized()
		{
			Assert.AreEqual(Expected, RunProbe(InteropAccessMode.Preoptimized));
		}

		[Test]
		public void Interop_Parity_BackgroundOptimized()
		{
			Assert.AreEqual(Expected, RunProbe(InteropAccessMode.BackgroundOptimized));
		}

		[Test]
		public void Interop_Parity_Hardwired()
		{
			Assert.AreEqual(Expected, RunProbe(InteropAccessMode.Hardwired));
		}

		[Test]
		public void Interop_Parity_Generated()
		{
			Assert.AreEqual(Expected, RunProbe(null));
		}

		[Test]
		public void Interop_Parity_Errors_Generated()
		{
			Assert.AreEqual(RunErrorProbes(InteropAccessMode.Reflection, ErrorProbes), RunErrorProbes(null, ErrorProbes));
		}

		[Test]
		public void Interop_Parity_Errors_LazyOptimized()
		{
			Assert.AreEqual(RunErrorProbes(InteropAccessMode.Reflection, ErrorProbes), RunErrorProbes(InteropAccessMode.LazyOptimized, ErrorProbes));
		}

		/// <summary>
		/// An exception thrown by the C# member reaches the host as itself, as in the compiled modes.
		/// (MethodBase.Invoke wraps it in a TargetInvocationException, so reflection mode differs.)
		/// </summary>
		[Test]
		public void Interop_Parity_Throw_Generated()
		{
			Assert.AreEqual("obj:Throw() => InvalidOperationException: thrown by C#\n"
				+ "pcall(obj.Throw, obj) => InvalidOperationException: thrown by C#", RunErrorProbes(null, ThrowProbes));
		}

		/// <summary>
		/// The background optimizer walks the overload list while the first call sorts it. That race
		/// threw on a thread-pool thread, which ends the process.
		/// </summary>
		[Test]
		public void Interop_Parity_BackgroundOptimizationRacesFirstCall()
		{
			try
			{
				for (int i = 0; i < 300; i++)
				{
					UserData.UnregisterType<ParityClass>();
					UserData.RegisterType<ParityClass>(InteropAccessMode.BackgroundOptimized);
					Script s = new Script();
					s.Globals["obj"] = new ParityClass();
					Assert.AreEqual("i1sx", s.DoString("return obj:Over(1) .. obj:Over('x')").String);
				}

				// Let the last optimizations finish while the test is still running.
				System.Threading.Thread.Sleep(200);
			}
			finally
			{
				UserData.UnregisterType<ParityClass>();
			}
		}
	}
}
