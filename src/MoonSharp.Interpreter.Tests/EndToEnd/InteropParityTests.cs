using System;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// The same interop probes under every InteropAccessMode. The access mode is a speed/memory choice,
	/// so it must never change what a script sees; every mode must print the same line.
	/// </summary>
	[TestFixture]
	public class InteropParityTests
	{
		public class ParityClass
		{
			public int Field;
			public int Prop { get; set; }

			public int Add(int a, int b) { return a + b; }
			public string Concat(string a, int b) { return a + b; }
			public static int Twice(int x) { return x * 2; }
			public string Over(int x) { return "i" + x; }
			public string Over(string x) { return "s" + x; }
			public int Sum(params int[] xs) { int s = 0; foreach (int x in xs) s += x; return s; }
			public void OutPair(int x, out int y, ref int z) { y = x * 2; z = z + 1; }
			public ParityStruct MakeStruct(int x) { return new ParityStruct { X = x }; }
		}

		public struct ParityStruct
		{
			public int X;
			public int Y { get; set; }

			public void Bump() { X++; }
			public int Read() { return X; }
			public int BumpAndGet() { X += 10; return X; }
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
			return table.concat(r, ',')";

		const string Expected = "5,a1,5,6,8,i1sx,6,nil,2,6,1,2,2,12,12,7,10";

		static string RunProbe(InteropAccessMode mode)
		{
			try
			{
				UserData.UnregisterType<ParityClass>();
				UserData.UnregisterType<ParityStruct>();
				UserData.RegisterType<ParityClass>(mode);
				UserData.RegisterType<ParityStruct>(mode);

				Script s = new Script();
				s.Globals["obj"] = new ParityClass();
				s.Globals["vt"] = new ParityStruct();
				s.Globals["Cls"] = typeof(ParityClass);

				// Twice: the lazy modes compile adapters on first use, so the second pass runs them.
				string first = s.DoString(Probe).String;
				s.Globals["vt"] = new ParityStruct();
				string second = s.DoString(Probe).String;
				Assert.AreEqual(first, second, mode + ": first and second pass differ");
				return second;
			}
			finally
			{
				UserData.UnregisterType<ParityClass>();
				UserData.UnregisterType<ParityStruct>();
			}
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
	}
}
