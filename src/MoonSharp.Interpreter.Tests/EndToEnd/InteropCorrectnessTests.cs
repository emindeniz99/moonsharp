using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	[TestFixture]
	public class InteropCorrectnessTests
	{
		public class HashProbe { }
		public class HashProbeOther { }

		public struct HashPoint
		{
			public int X;
			public HashPoint(int x) { X = x; }
		}

		public class OverloadA { }
		public class OverloadB { }
		public class OverloadC { }
		public class OverloadD { }
		public class OverloadE { }
		public class OverloadF { }
		public class OverloadG { }

		public class OverloadPicker
		{
			public string Pick(OverloadA a) { return "A"; }
			public string Pick(OverloadB b) { return "B"; }
			public string Pick(OverloadC c) { return "C"; }
			public string Pick(OverloadD d) { return "D"; }
			public string Pick(OverloadE e) { return "E"; }
			public string Pick(OverloadF f) { return "F"; }
			public string Pick(OverloadG g) { return "G"; }
		}

		static readonly Type[] OverloadArgTypes =
		{
			typeof(OverloadA), typeof(OverloadB), typeof(OverloadC), typeof(OverloadD),
			typeof(OverloadE), typeof(OverloadF), typeof(OverloadG),
		};

		[Test]
		public void Interop_Hash_DistinctUserDataHashApart()
		{
			try
			{
				UserData.RegisterType<HashProbe>();
				var hashes = new HashSet<int>();

				for (int i = 0; i < 1000; i++)
					hashes.Add(UserData.Create(new HashProbe()).GetHashCode());

				// They used to share the constant 999, which made a table keyed by userdata a linked list.
				Assert.Greater(hashes.Count, 990);
			}
			finally
			{
				UserData.UnregisterType<HashProbe>();
			}
		}

		[Test]
		public void Interop_Hash_DistinctCoroutinesHashApart()
		{
			Script s = new Script();
			DynValue body = s.DoString("return function() end");
			var hashes = new HashSet<int>();

			for (int i = 0; i < 200; i++)
				hashes.Add(s.CreateCoroutine(body).GetHashCode());

			Assert.Greater(hashes.Count, 190);
		}

		[Test]
		public void Interop_Hash_StaticUserDataOfDifferentTypesHashApart()
		{
			try
			{
				UserData.RegisterType<HashProbe>();
				UserData.RegisterType<HashProbeOther>();

				Assert.AreNotEqual(UserData.CreateStatic<HashProbe>().GetHashCode(), UserData.CreateStatic<HashProbeOther>().GetHashCode());
				Assert.AreEqual(UserData.CreateStatic<HashProbe>().GetHashCode(), UserData.CreateStatic<HashProbe>().GetHashCode());
			}
			finally
			{
				UserData.UnregisterType<HashProbe>();
				UserData.UnregisterType<HashProbeOther>();
			}
		}

		[Test]
		public void Interop_Hash_EqualUserDataHashEqual()
		{
			try
			{
				UserData.RegisterType<HashProbe>();
				UserData.RegisterType<HashPoint>();

				var obj = new HashProbe();
				DynValue a = UserData.Create(obj), b = UserData.Create(obj);
				Assert.IsTrue(a.Equals(b));
				Assert.AreEqual(a.GetHashCode(), b.GetHashCode());

				// Two boxes of equal structs are equal values, so they must hash alike.
				DynValue p = UserData.Create(new HashPoint(3)), q = UserData.Create(new HashPoint(3));
				Assert.IsTrue(p.Equals(q));
				Assert.AreEqual(p.GetHashCode(), q.GetHashCode());

				Script s = new Script();
				s.Globals["a"] = a;
				s.Globals["b"] = b;
				s.Globals["p"] = p;
				s.Globals["q"] = q;
				Assert.AreEqual(3, s.DoString("local t = {} t[a] = 1 t[p] = 2 return t[b] + t[q]").Number);
			}
			finally
			{
				UserData.UnregisterType<HashProbe>();
				UserData.UnregisterType<HashPoint>();
			}
		}

		[Test]
		public void Interop_OverloadCache_SevenUserDataSignaturesPickTheRightOverload()
		{
			try
			{
				UserData.RegisterType<OverloadPicker>();
				foreach (Type t in OverloadArgTypes)
					UserData.RegisterType(t);

				Script s = new Script();
				s.Globals["picker"] = new OverloadPicker();
				string[] names = { "A", "B", "C", "D", "E", "F", "G" };

				for (int i = 0; i < names.Length; i++)
					s.Globals["arg" + names[i]] = Activator.CreateInstance(OverloadArgTypes[i]);

				// More signatures than the 5 cache slots, in several orders, so slots get reused.
				string[] order = "ABCDEFGABCDEFGGFEDCBAACEGBDFAGBFCED".Select(c => c.ToString()).ToArray();

				foreach (string n in order)
					Assert.AreEqual(n, s.DoString("return picker:Pick(arg" + n + ")").String, "call with " + n);
			}
			finally
			{
				UserData.UnregisterType<OverloadPicker>();
				foreach (Type t in OverloadArgTypes)
					UserData.UnregisterType(t);
			}
		}
	}
}
