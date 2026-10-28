using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.Interop;
using NUnit.Framework;

// Delegate types the tests convert Lua functions to with ToDelegate. The generator writes factories for
// these and for the delegate parameters of Host, so NativeAOT (and IL2CPP) need no MakeGenericMethod.
[assembly: MoonSharp.Interpreter.MoonSharpUserDataTypes(typeof(System.Func<int, int>), typeof(System.Func<int, bool, string>))]

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Lua functions as CLR delegates (upstream #186,
	/// https://github.com/moonsharp-devs/moonsharp/issues/186): a host API that takes an Action or Func
	/// callback could not be called from Lua, and Closure.GetDelegate gave a new delegate every time, so a
	/// handler added with += could never be removed with -=. Every test first registers the factories the
	/// source generator wrote; Delegate_BuiltByReflectionWithoutAFactory covers the path without them.
	/// </summary>
	[TestFixture]
	public class ClosureDelegateTests
	{
		public delegate string Formatter(string name, int count);
		public delegate void ByRef(ref int value);
		public delegate int FiveArgs(int a, int b, int c, int d, int e);

		[MoonSharpUserData]
		public class Host
		{
			readonly List<string> m_Log = new List<string>();
			event Action<int> Changed;

			public void Each(int count, Action<int> action)
			{
				for (int i = 1; i <= count; i++)
					action(i);
			}

			public double Apply(Func<double, double> f, double x) { return f(x); }
			public string Format(Formatter f) { return f("apples", 3); }
			public void Subscribe(Action<int> handler) { Changed += handler; }
			public void Unsubscribe(Action<int> handler) { Changed -= handler; }
			public void Raise(int value) { if (Changed != null) Changed(value); }
			public void Log(string s) { m_Log.Add(s); }
			public string Logged { get { return string.Join(",", m_Log.ToArray()); } }

			public string Pick(Closure c) { return "closure"; }
			public string Pick(Action a) { return "action"; }
			public string Pick2(int n) { return "int"; }
			public string Pick2(Action<int> a) { return "action"; }
			public void TakeByRef(ByRef f) { }
		}

		static Script NewScript(Host host)
		{
			MoonSharp.Generated.GeneratedUserData.RegisterDelegateTypes();
			UserData.RegisterType<Host>();
			Script s = new Script();
			s.Globals["host"] = host;
			return s;
		}

		[Test]
		public void Delegate_LuaFunctionsAsActionFuncAndCustomDelegateArguments()
		{
			Host host = new Host();
			Script s = NewScript(host);

			s.DoString(@"
				host:Each(3, function(i) host:Log('item ' .. i) end)
				host:Log(tostring(host:Apply(function(x) return x * 2 end, 21)))
				host:Log(host:Format(function(name, count) return count .. ' ' .. name end))");

			Assert.AreEqual("item 1,item 2,item 3,42,3 apples", host.Logged);
		}

		[Test]
		public void Delegate_SameFunctionGivesTheSameDelegate_SoMinusEqualsRemovesIt()
		{
			Host host = new Host();
			Script s = NewScript(host);

			s.DoString(@"
				local function onChanged(v) host:Log('changed ' .. v) end
				host:Subscribe(onChanged)
				host:Raise(1)
				host:Unsubscribe(onChanged)
				host:Raise(2)");

			Assert.AreEqual("changed 1", host.Logged);

			Closure f = s.DoString("return function(x) return x end").Function;
			Assert.AreSame(f.ToDelegate<Func<int, int>>(), f.ToDelegate<Func<int, int>>());
			Assert.AreSame(f.ToDelegate<Action<int>>(), f.ToDelegate<Action<int>>());
			Assert.AreSame(f.ToDelegate<Func<int, int>>(), f.ToDelegate<Func<int, int>>(), "the first type survives adding a second");
			Assert.AreNotSame(f.ToDelegate<Func<int, int>>(), s.DoString("return function(x) return x end").Function.ToDelegate<Func<int, int>>());
		}

		[Test]
		public void Delegate_ConvertsArgumentsAndResultsAsTheTypedCalls()
		{
			Script s = NewScript(new Host());
			Closure f = s.DoString("return function(a, b) return a .. ':' .. tostring(b) end").Function;

			Assert.AreEqual(f.Call<int, bool, string>(5, true), f.ToDelegate<Func<int, bool, string>>()(5, true));
			Assert.AreEqual("5:true", f.ToDelegate<Func<int, bool, string>>()(5, true));
			Assert.AreEqual("apples:3", f.ToDelegate<Formatter>()("apples", 3));

			Closure fail = s.DoString("return function() error('boom') end").Function;
			StringAssert.Contains("boom", Assert.Throws<ScriptRuntimeException>(() => fail.ToDelegate<Action>()()).Message);
		}

		[Test]
		public void Delegate_OverloadsPreferClosureAndTakeDelegatesOverMismatches()
		{
			Host host = new Host();
			Script s = NewScript(host);

			Assert.AreEqual("closure", s.DoString("return host:Pick(function() end)").String);
			Assert.AreEqual("action", s.DoString("return host:Pick2(function() end)").String);
			Assert.AreEqual("int", s.DoString("return host:Pick2(1)").String);
		}

		[Test]
		public void Delegate_UnsupportedSignaturesFailWithTheReason()
		{
			Host host = new Host();
			Script s = NewScript(host);
			Closure f = s.DoString("return function() end").Function;

			StringAssert.Contains("cannot convert a Lua function to delegate type", Assert.Throws<ScriptRuntimeException>(() => f.ToDelegate<ByRef>()).Message);
			StringAssert.Contains("up to 4 parameters", Assert.Throws<ScriptRuntimeException>(() => f.ToDelegate<FiveArgs>()).Message);
			Assert.Throws<ScriptRuntimeException>(() => s.DoString("host:TakeByRef(function() end)"));
		}

		public delegate string NotGenerated(string x);

		[Test]
		public void Delegate_BuiltByReflectionWithoutAFactory()
		{
			// No factory for this type: the interpreter closes its generic bridge by reflection. AOT
			// compilers may have no code for that; then the conversion fails at once, naming the fix.
			Script s = new Script();
			Closure f = s.DoString("return function(x) return x .. '!' end").Function;

			try
			{
				Assert.AreEqual("hi!", f.ToDelegate<NotGenerated>()("hi"));
			}
			catch (ScriptRuntimeException ex)
			{
				Assert.IsTrue(Script.GlobalOptions.Platform.IsRunningOnAOT(), ex.Message);
				StringAssert.Contains("MoonSharpUserDataTypes", ex.Message);
			}
		}

		public delegate int Registered(int x);

		[Test]
		public void Delegate_ARegisteredFactoryIsUsed()
		{
			// The source generator registers typed factories so AOT platforms need no reflection.
			int created = 0;
			UserData.RegisterDelegateType<Registered>(c => { created++; return x => c.Call<int, int>(x) + 1000; });

			Script s = new Script();
			Closure f = s.DoString("return function(x) return x * 2 end").Function;

			Assert.AreEqual(1010, f.ToDelegate<Registered>()(5));
			Assert.AreEqual(1010, f.ToDelegate<Registered>()(5));
			Assert.AreEqual(1, created);
		}
	}
}
