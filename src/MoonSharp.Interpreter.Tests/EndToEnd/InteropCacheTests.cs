using System;
using System.Linq;
using MoonSharp.Interpreter.Diagnostics;
using MoonSharp.Interpreter.Interop;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	public static class SignatureProbeExtensions
	{
		public static string G(this InteropCacheTests.SignatureProbe p, bool b) { return "ext bool"; }
	}

	/// <summary>
	/// The interop caches (descriptors resolved through base types and interfaces, custom converter
	/// lookups, delegate descriptors) must give the same answers as computing everything again: a
	/// registration, an unregistration, a policy change or a converter added after first use must be
	/// seen at once.
	/// </summary>
	[TestFixture]
	public class InteropCacheTests
	{
		public class CacheBase { public int Id { get { return 7; } } }
		public class CacheDerived : CacheBase { }

		public interface ICacheProbeA { int A { get; } }
		public interface ICacheProbeB { int B { get; } }
		public class CacheProbeImpl : ICacheProbeA, ICacheProbeB
		{
			public int A { get { return 1; } }
			public int B { get { return 2; } }
		}

		public class CachePolicyBase { }
		public class CachePolicyDerived : CachePolicyBase { public int Own { get { return 3; } } }

		public enum CacheEnum { Zero, One, Two }

		public class NumericEcho
		{
			public int LastInt;

			public byte EchoByte(byte v) { return v; }
			public sbyte EchoSByte(sbyte v) { return v; }
			public short EchoShort(short v) { return v; }
			public ushort EchoUShort(ushort v) { return v; }
			public int EchoInt(int v) { LastInt = v; return v; }
			public uint EchoUInt(uint v) { return v; }
			public long EchoLong(long v) { return v; }
			public ulong EchoULong(ulong v) { return v; }
			public float EchoFloat(float v) { return v; }
			public double EchoDouble(double v) { return v; }
			public decimal EchoDecimal(decimal v) { return v; }
			public int? EchoNullableInt(int? v) { return v; }
			public CacheEnum EchoEnum(CacheEnum v) { return v; }
			public string TypeOf(object v) { return v == null ? "null" : v.GetType().Name; }
		}

		[Test]
		public void Interop_Cache_BaseRegisteredAfterDerivedPushIsSeen()
		{
			// Unregistering must take effect; test runners may install a policy that keeps old descriptors.
			var oldPolicy = UserData.RegistrationPolicy;
			UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;

			try
			{
				Assert.IsNull(UserData.GetDescriptorForType(typeof(CacheDerived), true));
				Assert.IsNull(UserData.Create(new CacheDerived()));

				IUserDataDescriptor baseDescriptor = UserData.RegisterType<CacheBase>();
				Assert.AreSame(baseDescriptor, UserData.GetDescriptorForType(typeof(CacheDerived), true));

				Script s = new Script();
				s.Globals["obj"] = new CacheDerived();
				Assert.AreEqual(7, s.DoString("return obj.Id").Number);

				UserData.UnregisterType<CacheBase>();
				Assert.IsNull(UserData.GetDescriptorForType(typeof(CacheDerived), true));
				Assert.IsNull(UserData.GetDescriptorForType(typeof(CacheDerived), false));
				Assert.IsNull(UserData.Create(new CacheDerived()));
			}
			finally
			{
				UserData.UnregisterType<CacheBase>();
				UserData.RegistrationPolicy = oldPolicy;
			}
		}

		[Test]
		public void Interop_Cache_PushesDoNotRegisterTypes()
		{
			try
			{
				UserData.RegisterType<CacheBase>();

				for (int i = 0; i < 3; i++)
					Assert.IsNotNull(UserData.Create(new CacheDerived()));

				Assert.IsFalse(UserData.IsTypeRegistered<CacheDerived>());
				Assert.IsFalse(UserData.GetRegisteredTypes().Contains(typeof(CacheDerived)));
				Assert.IsFalse(UserData.GetRegisteredTypes(true).Contains(typeof(CacheDerived)));
			}
			finally
			{
				UserData.UnregisterType<CacheBase>();
			}
		}

		[Test]
		public void Interop_Cache_RegistrationPolicyChangeIsSeen()
		{
			var oldPolicy = UserData.RegistrationPolicy;

			try
			{
				UserData.RegisterType<CachePolicyBase>();
				Script s = new Script();

				// Resolved through the base type: the derived member is not visible.
				s.Globals["obj"] = new CachePolicyDerived();
				Assert.AreEqual(DataType.Nil, s.DoString("return pcall(function() return obj.Own end) and 1 or nil").Type);

				// With automatic registration the derived type gets its own descriptor.
				UserData.RegistrationPolicy = InteropRegistrationPolicy.Automatic;
				s.Globals["obj"] = new CachePolicyDerived();
				Assert.AreEqual(3, s.DoString("return obj.Own").Number);
			}
			finally
			{
				UserData.RegistrationPolicy = oldPolicy;
				UserData.UnregisterType<CachePolicyDerived>();
				UserData.UnregisterType<CachePolicyBase>();
			}
		}

		[Test]
		public void Interop_Cache_InterfaceDescriptorsAreResolvedOnce()
		{
			try
			{
				UserData.RegisterType<ICacheProbeA>();
				UserData.RegisterType<ICacheProbeB>();

				var obj = new CacheProbeImpl();
				Script s = new Script();
				s.Globals["x"] = obj;
				s.Globals["y"] = obj;

				Assert.AreEqual(3, s.DoString("return x.A + y.B").Number);
				Assert.IsTrue(s.DoString("return x == y").Boolean);
				// Two pushes of one object share the descriptor, so they are the same table key.
				Assert.AreEqual(1, s.DoString("local t = {} t[x] = 1 return t[y]").Number);
				Assert.AreSame(UserData.GetDescriptorForObject(obj), UserData.GetDescriptorForObject(obj));

				UserData.UnregisterType<ICacheProbeB>();
				s.Globals["z"] = new CacheProbeImpl();
				Assert.AreEqual(1, s.DoString("return z.A").Number);
				Assert.IsFalse(s.DoString("return pcall(function() return z.B end)").Boolean);
			}
			finally
			{
				UserData.UnregisterType<ICacheProbeA>();
				UserData.UnregisterType<ICacheProbeB>();
			}
		}

		[Test]
		public void Interop_Cache_CustomConvertersAddedAfterFirstUseWin()
		{
			try
			{
				UserData.RegisterType<NumericEcho>();
				var echo = new NumericEcho();
				Script s = new Script();
				s.Globals["e"] = echo;

				// First use, no converters: plain conversions.
				Assert.AreEqual(5, s.DoString("return e:EchoInt(5)").Number);
				Assert.AreEqual(5, echo.LastInt);

				Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion(DataType.Number, typeof(int), v => 42);
				Assert.AreEqual(42, s.DoString("return e:EchoInt(5)").Number);
				Assert.AreEqual(42, echo.LastInt);

				Script.GlobalOptions.CustomConverters.SetClrToScriptCustomConversion<int>((sc, i) => DynValue.NewString("int:" + i));
				Assert.AreEqual("int:42", s.DoString("return e:EchoInt(5)").String);

				Script.GlobalOptions.CustomConverters.Clear();
				Assert.AreEqual(5, s.DoString("return e:EchoInt(5)").Number);
			}
			finally
			{
				Script.GlobalOptions.CustomConverters.Clear();
				UserData.UnregisterType<NumericEcho>();
			}
		}

		public class SignatureProbe
		{
			public string F(int a) { return "int"; }
			public string F(double a) { return "double"; }
			public string F(string a) { return "string"; }
			public string F(bool a) { return "bool"; }
			public string F(Closure a) { return "closure"; }
			public string F(int a, string b) { return "int,string"; }
			public string F(params int[] a) { return "params"; }
			public string G(int a) { return "int"; }
			public string G(string a) { return "string"; }
			public static string S(int a) { return "static int"; }
			public static string S(string a) { return "static string"; }
		}

		[Test]
		public void Interop_Cache_OverloadSignaturesChooseAsScoringDoes()
		{
			// Overloaded calls look their overload up by the argument signature (one entry per signature,
			// more than the five recent ones the old cache kept). Every call must pick what MoonSharp's
			// weights pick (#293: a whole number weighs most for double, a fraction never picks int), in any
			// order and however many signatures alternate. (F(1, 2, 3) takes F(double) and ignores two arguments:
			// an unused argument costs less than the params malus.)
			string[] calls = { "o:F(1)", "o:F(1.5)", "o:F('x')", "o:F(true)", "o:F(function() end)", "o:F(1, 'x')", "o:F()", "o:F(1, 2, 3)", "o:G(2)", "o:G('2')", "P.S(1)", "P.S('1')" };
			string[] expected = { "double", "double", "string", "bool", "closure", "int,string", "params", "double", "int", "string", "static int", "static string" };

			foreach (InteropAccessMode mode in new[] { InteropAccessMode.Reflection, InteropAccessMode.LazyOptimized })
			{
				UserData.UnregisterType<SignatureProbe>();
				UserData.RegisterType<SignatureProbe>(mode);

				try
				{
					Script s = new Script();
					s.Globals["o"] = new SignatureProbe();
					s.Globals["P"] = typeof(SignatureProbe);

					for (int round = 0; round < 3; round++)
						for (int k = 0; k < calls.Length; k++)
						{
							int i = round == 1 ? calls.Length - 1 - k : k;
							Assert.AreEqual(expected[i], s.DoString("return " + calls[i]).String, mode + " " + calls[i]);
						}
				}
				finally
				{
					UserData.UnregisterType<SignatureProbe>();
				}
			}
		}

		[Test]
		public void Interop_Cache_OverloadSignaturesGiveWayToConvertersAndExtensions()
		{
			// A custom converter or an extension method registered after the first call changes the weights:
			// neither the signature lookup nor the cache of recent argument types may keep the old choice.
			// The cache did, for converters: o:G(2) stayed on G(int) after a number-to-string converter.
			UserData.RegisterType<SignatureProbe>();

			try
			{
				Script s = new Script();
				s.Globals["o"] = new SignatureProbe();
				Assert.AreEqual("string", s.DoString("return o:G('2')").String);
				Assert.AreEqual("int", s.DoString("return o:G(2)").String);

				Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion(DataType.String, typeof(int), v => 7);
				Assert.AreEqual("string", s.DoString("return o:G('2')").String, "an exact match beats a converter (#294)");
				Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion(DataType.Number, typeof(string), v => "n");
				Assert.AreEqual("string", s.DoString("return o:G(2)").String, "the converter now outweighs the number downcast");
				Script.GlobalOptions.CustomConverters.Clear();
				Assert.AreEqual("int", s.DoString("return o:G(2)").String);

				Assert.IsFalse(s.DoString("return pcall(function() return o:G(true) end)").Boolean);
				UserData.RegisterExtensionType(typeof(SignatureProbeExtensions));
				Assert.AreEqual("ext bool", s.DoString("return o:G(true)").String);
				Assert.AreEqual("int", s.DoString("return o:G(2)").String);
			}
			finally
			{
				Script.GlobalOptions.CustomConverters.Clear();
				UserData.UnregisterType<SignatureProbe>();
			}
		}

		[Test]
		public void Interop_Cache_EveryNumericTypeRoundTrips()
		{
			foreach (InteropAccessMode mode in new[] { InteropAccessMode.Reflection, InteropAccessMode.LazyOptimized, InteropAccessMode.Preoptimized })
			{
				try
				{
					UserData.RegisterType<NumericEcho>(mode);
					Script s = new Script();
					s.Globals["e"] = new NumericEcho();

					DynValue r = s.DoString(@"
						return e:EchoByte(200), e:EchoSByte(-100), e:EchoShort(-30000), e:EchoUShort(60000),
							e:EchoInt(-2000000000), e:EchoUInt(4000000000), e:EchoLong(-9007199254740991),
							e:EchoULong(9007199254740991), e:EchoFloat(0.5), e:EchoDouble(0.1), e:EchoDecimal(12.25),
							e:EchoNullableInt(7), e:EchoNullableInt(nil), e:EchoEnum(2), e:TypeOf(3)");

					double[] expected = { 200, -100, -30000, 60000, -2000000000, 4000000000, -9007199254740991,
						9007199254740991, 0.5, 0.1, 12.25, 7 };

					for (int i = 0; i < expected.Length; i++)
						Assert.AreEqual(expected[i], r.Tuple[i].Number, mode + " value " + i);

					Assert.AreEqual(DataType.Nil, r.Tuple[12].Type, mode.ToString());
					// An unregistered enum goes back to scripts as its number.
					Assert.AreEqual(2, r.Tuple[13].Number, mode.ToString());
					Assert.AreEqual("Double", r.Tuple[14].String, mode.ToString());
				}
				finally
				{
					UserData.UnregisterType<NumericEcho>();
				}
			}
		}

		[Test]
		public void Interop_Cache_SameDelegateBuildsOneDescriptor()
		{
			Func<int, int, int> add = (a, b) => a + b;
			Script s = new Script();
			s.PerformanceStats.Enabled = true;

			try
			{
				s.Globals["f1"] = CallbackFunction.FromDelegate(s, add, InteropAccessMode.LazyOptimized);
				s.Globals["f2"] = CallbackFunction.FromDelegate(s, add, InteropAccessMode.LazyOptimized);

				int before = s.PerformanceStats.GetPerformanceCounterResult(PerformanceCounter.AdaptersCompilation).Instances;
				Assert.AreEqual(7, s.DoString("return f1(3, 4)").Number);
				Assert.AreEqual(9, s.DoString("return f2(4, 5)").Number);
				int compiled = s.PerformanceStats.GetPerformanceCounterResult(PerformanceCounter.AdaptersCompilation).Instances - before;

				// The second conversion reuses the descriptor, and with it the adapter compiled by the first
				// call. AOT platforms never compile adapters.
				Assert.AreEqual(Script.GlobalOptions.Platform.IsRunningOnAOT() ? 0 : 1, compiled);
			}
			finally
			{
				s.PerformanceStats.Enabled = false;
			}
		}
	}
}
