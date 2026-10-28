using System;
using MoonSharp.Interpreter.Interop;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	public static class BoundProbeExtensions
	{
		public static string Twice(this InteropBoundMethodTests.BoundProbe p) { return "twice"; }
		public static string Scale(this InteropBoundMethodTests.BoundProbe p, string s) { return "ext:" + s; }
	}

	/// <summary>
	/// Reading a method from a userdata gives a callback bound to that userdata. The interpreter keeps
	/// that callback on the userdata, so these check that every way of reading and calling a method
	/// behaves as when a new callback was built each time.
	/// </summary>
	[TestFixture]
	public class InteropBoundMethodTests
	{
		public class BoundProbe
		{
			public int Factor = 3;
			public int Scale(int x) { return x * Factor; }
			public int Value { get { return Factor; } }
		}

		[Flags]
		public enum BoundFlags { None = 0, A = 1, B = 2, C = 4 }

		public class EnumSource
		{
			public BoundFlags Get(int i) { return (BoundFlags)i; }
		}

		// Each call form, twice, so the second run uses whatever the first one kept.
		const string CallForms = @"
			local r = {}
			local function try(f) local ok, v = pcall(f) r[#r + 1] = ok and tostring(v) or 'error' end
			for pass = 1, 2 do
				try(function() return obj:Scale(2) end)
				try(function() return obj.Scale(obj, 2) end)
				try(function() local f = obj.Scale return f(obj, 2) end)
				try(function() local f = obj.Scale return f(2) end)
				try(function() return obj.Scale(2) end)
				try(function() return obj:scale(2) end)
				try(function() return obj.Value end)
				try(function() return obj.missing end)
			end
			return table.concat(r, ',')";

		static string RunCallForms(ColonOperatorBehaviour colon)
		{
			try
			{
				UserData.RegisterType<BoundProbe>();
				Script s = new Script();
				s.Options.ColonOperatorClrCallbackBehaviour = colon;
				s.Globals["obj"] = new BoundProbe();
				return s.DoString(CallForms).String;
			}
			finally
			{
				UserData.UnregisterType<BoundProbe>();
			}
		}

		// The answers the interpreter gave when every read built a new callback.
		[Test]
		public void Interop_Bound_CallFormsTreatAsDot()
		{
			Assert.AreEqual("6,error,error,6,6,6,3,error,6,error,error,6,6,6,3,error", RunCallForms(ColonOperatorBehaviour.TreatAsDot));
		}

		[Test]
		public void Interop_Bound_CallFormsTreatAsDotOnUserData()
		{
			Assert.AreEqual("6,error,error,6,6,6,3,error,6,error,error,6,6,6,3,error", RunCallForms(ColonOperatorBehaviour.TreatAsDotOnUserData));
		}

		[Test]
		public void Interop_Bound_CallFormsTreatAsColon()
		{
			Assert.AreEqual("error,error,error,6,6,error,3,error,error,error,error,6,6,error,3,error", RunCallForms(ColonOperatorBehaviour.TreatAsColon));
		}

		[Test]
		public void Interop_Bound_RepeatedReadsShareOneCallback()
		{
			try
			{
				UserData.RegisterType<BoundProbe>();
				Script s = new Script();
				s.Globals["obj"] = new BoundProbe();
				s.Globals["other"] = new BoundProbe();

				Assert.IsTrue(s.DoString("return obj.Scale == obj.Scale").Boolean);
				Assert.IsTrue(s.DoString("return rawequal(obj.Scale, obj.Scale)").Boolean);
				// Bound to different objects, so still different callbacks.
				Assert.IsFalse(s.DoString("return obj.Scale == other.Scale").Boolean);
				Assert.AreEqual(12, s.DoString("other.Factor = 4 return other:Scale(3)").Number);
			}
			finally
			{
				UserData.UnregisterType<BoundProbe>();
			}
		}

		[Test]
		public void Interop_Bound_ExtensionMethodsRegisteredLaterAreFound()
		{
			try
			{
				UserData.RegisterType<BoundProbe>();
				Script s = new Script();
				s.Globals["obj"] = new BoundProbe();

				Assert.AreEqual(6, s.DoString("return obj:Scale(2)").Number);
				Assert.IsFalse(s.DoString("return pcall(function() return obj:Twice() end)").Boolean);

				UserData.RegisterExtensionType(typeof(BoundProbeExtensions));

				// A new method, and a new overload of a method whose bound callback was already kept.
				Assert.AreEqual("twice", s.DoString("return obj:Twice()").String);
				Assert.AreEqual("ext:x", s.DoString("return obj:Scale('x')").String);
				Assert.AreEqual(6, s.DoString("return obj:Scale(2)").Number);
			}
			finally
			{
				UserData.UnregisterType<BoundProbe>();
			}
		}

		[Test]
		public void Interop_Bound_MembersChangedLaterAreSeen()
		{
			try
			{
				var descr = (StandardUserDataDescriptor)UserData.RegisterType<BoundProbe>();
				Script s = new Script();
				s.Globals["obj"] = new BoundProbe();

				// 'scale' resolves to Scale through fuzzy matching, and is kept.
				Assert.AreEqual(6, s.DoString("return obj:scale(2)").Number);
				Assert.IsFalse(s.DoString("return pcall(function() return obj.extra end)").Boolean);

				// An exact member added later wins over the fuzzy match and fills the missing name.
				descr.AddDynValue("scale", DynValue.NewCallback((c, a) => DynValue.NewString("exact")));
				descr.AddDynValue("extra", DynValue.NewNumber(99));
				Assert.AreEqual("exact", s.DoString("return obj:scale(2)").String);
				Assert.AreEqual(99, s.DoString("return obj.extra").Number);

				descr.RemoveMember("Scale");
				Assert.IsFalse(s.DoString("return pcall(function() return obj:Scale(2) end)").Boolean);
			}
			finally
			{
				UserData.UnregisterType<BoundProbe>();
			}
		}

		[Test]
		public void Interop_Bound_FuzzyMatchingFlagsChangeIsSeen()
		{
			var oldFlags = Script.GlobalOptions.FuzzySymbolMatching;
			// A fresh descriptor per registration; test runners may install a policy that keeps old ones.
			var oldPolicy = UserData.RegistrationPolicy;
			UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;

			try
			{
				UserData.RegisterType<BoundProbe>();
				Script s = new Script();
				s.Globals["obj"] = new BoundProbe();
				s.Globals["obj2"] = new BoundProbe();

				Assert.AreEqual(3, s.DoString("return obj.value").Number);

				Script.GlobalOptions.FuzzySymbolMatching = FuzzySymbolMatchingBehavior.None;
				Assert.IsFalse(s.DoString("return pcall(function() return obj.value end)").Boolean);
				Assert.IsFalse(s.DoString("return pcall(function() return obj2:scale(2) end)").Boolean);

				Script.GlobalOptions.FuzzySymbolMatching = oldFlags;
				Assert.AreEqual(6, s.DoString("return obj2:scale(2)").Number);
			}
			finally
			{
				Script.GlobalOptions.FuzzySymbolMatching = oldFlags;
				UserData.UnregisterType<BoundProbe>();
				UserData.RegistrationPolicy = oldPolicy;
			}
		}

		[Test]
		public void Interop_Bound_EnumValuesFromClrCompareAndPrint()
		{
			try
			{
				UserData.RegisterType<BoundFlags>();
				UserData.RegisterType<EnumSource>();
				Script s = new Script();
				s.Globals["src"] = new EnumSource();
				s.Globals["BoundFlags"] = UserData.CreateStatic<BoundFlags>();

				Assert.IsTrue(s.DoString("return src:Get(2) == BoundFlags.B").Boolean);
				Assert.IsTrue(s.DoString("return src:Get(2) == src:Get(2)").Boolean);
				Assert.IsFalse(s.DoString("return src:Get(1) == src:Get(2)").Boolean);
				Assert.AreEqual("B", s.DoString("return tostring(src:Get(2))").String);
				// A combination has no name of its own and still works.
				Assert.AreEqual("A, B", s.DoString("return tostring(src:Get(3))").String);
				Assert.AreEqual(1, s.DoString("local t = {} t[src:Get(4)] = 1 return t[BoundFlags.C]").Number);
			}
			finally
			{
				UserData.UnregisterType<BoundFlags>();
				UserData.UnregisterType<EnumSource>();
			}
		}
	}
}
