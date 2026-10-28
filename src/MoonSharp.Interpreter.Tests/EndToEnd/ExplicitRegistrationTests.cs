using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Mobile hosts expose only the C# types they list (docs/registration.md): a downloaded script must not reach
	/// any other type, whatever the listed types return. These tests pin down what the default policy already
	/// guarantees and what the documented allow-list policy adds.
	/// </summary>
	[TestFixture]
	public class ExplicitRegistrationTests
	{
		public class Listed
		{
			public int Value = 3;
			public Unlisted GetUnlisted() { return new Unlisted(); }
			public object GetUnlistedAsObject() { return new Unlisted(); }
			public Type GetUnlistedType() { return typeof(Unlisted); }

			public class Nested { }
		}

		public class Unlisted
		{
			public int Secret = 42;
		}

		[MoonSharpUserData]
		public class Marked
		{
			public int Secret = 42;
		}

		/// <summary>The policy docs/registration.md shows, copied here because tests cannot reference docs.</summary>
		sealed class AllowListRegistrationPolicy : IRegistrationPolicy
		{
			readonly HashSet<Type> m_Allowed;

			public AllowListRegistrationPolicy(params Type[] allowed)
			{
				m_Allowed = new HashSet<Type>(allowed);
			}

			public IUserDataDescriptor HandleRegistration(IUserDataDescriptor newDescriptor, IUserDataDescriptor oldDescriptor)
			{
				if (newDescriptor == null || oldDescriptor != null)
					return oldDescriptor;

				Type t = newDescriptor.Type;
				bool allowed = m_Allowed.Contains(t)
					|| (t.IsGenericType && m_Allowed.Contains(t.GetGenericTypeDefinition()))
					|| t.Assembly == typeof(Script).Assembly;

				return allowed ? newDescriptor : null;
			}

			public bool AllowTypeAutoRegistration(Type type)
			{
				return false;
			}
		}

		static void AssertScriptError(Script s, string code, string expected)
		{
			var ex = Assert.Throws<ScriptRuntimeException>(() => s.DoString(code), code);
			StringAssert.Contains(expected, ex.Message, code);
		}

		[Test]
		public void Registration_DefaultPolicyKeepsUnregisteredTypesAway()
		{
			IRegistrationPolicy oldPolicy = UserData.RegistrationPolicy;

			try
			{
				UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;
				UserData.RegisterType<Listed>();

				Script s = new Script();
				s.Globals["obj"] = new Listed();
				Assert.AreEqual(3, s.DoString("return obj.Value").Number);

				// A value of an unregistered type cannot reach the script, whatever the declared type.
				AssertScriptError(s, "return obj:GetUnlisted()", "cannot convert clr type");
				AssertScriptError(s, "return obj:GetUnlistedAsObject()", "cannot convert clr type");
				AssertScriptError(s, "return obj:GetUnlistedType()", "cannot convert clr type");

				// GetType() gives the registered type's static userdata, not a System.Type to reflect on.
				Assert.AreEqual(DataType.UserData, s.DoString("return obj:GetType()").Type);
				AssertScriptError(s, "return obj:GetType().Assembly", "cannot access field Assembly");

				// [MoonSharpUserData] alone registers nothing; only UserData.RegisterAssembly reads it.
				Assert.IsFalse(UserData.IsTypeRegistered<Marked>());
				Assert.Throws<ScriptRuntimeException>(() => s.Globals["marked"] = new Marked());
			}
			finally
			{
				// Unregister under the default policy: the runner's policy may keep descriptors.
				UserData.UnregisterType<Listed.Nested>();
				UserData.UnregisterType<Listed>();
				UserData.RegistrationPolicy = oldPolicy;
			}
		}

		[Test]
		public void Registration_AllowListPolicyRefusesUnlistedTypes()
		{
			IRegistrationPolicy oldPolicy = UserData.RegistrationPolicy;
			bool listWasRegistered = UserData.IsTypeRegistered(typeof(List<>));
			bool listOfIntWasRegistered = UserData.IsTypeRegistered(typeof(List<int>));
			Type dynamicWrapper = typeof(Script).Assembly.GetType("MoonSharp.Interpreter.CoreLib.DynamicModule+DynamicExprWrapper", true);

			try
			{
				// Registered again below, under the policy, when a script loads the dynamic module.
				UserData.UnregisterType(dynamicWrapper);
				UserData.RegistrationPolicy = new AllowListRegistrationPolicy(typeof(Listed), typeof(List<>));

				Assert.IsNotNull(UserData.RegisterType<Listed>());
				Assert.IsNotNull(UserData.RegisterType(typeof(List<>)));
				Assert.IsNull(UserData.RegisterType<Unlisted>());
				Assert.IsNull(UserData.RegisterType<Marked>());
				Assert.IsFalse(UserData.IsTypeRegistered<Unlisted>());

				// A public nested type is normally registered with its parent; the list refuses it.
				Assert.IsFalse(UserData.IsTypeRegistered<Listed.Nested>());

				Script s = new Script(CoreModules.Preset_Complete);
				s.Globals["obj"] = new Listed();
				s.Globals["Listed"] = typeof(Listed);
				AssertScriptError(s, "return obj:GetUnlisted()", "cannot convert clr type");
				AssertScriptError(s, "return Listed.Nested", "cannot access field Nested");

				// Closed versions of a listed generic definition are allowed.
				s.Globals["list"] = new List<int> { 1, 2 };
				Assert.AreEqual(2, s.DoString("return list.Count").Number);

				// MoonSharp's own helper types stay allowed, so its modules keep working.
				Assert.AreEqual(3, s.DoString("return dynamic.eval(dynamic.prepare('1 + 2'))").Number);
			}
			finally
			{
				// Unregister under the default policy: the allow-list policy and the runner's policy keep
				// descriptors on UnregisterType.
				UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;
				UserData.UnregisterType<Listed.Nested>();
				UserData.UnregisterType<Listed>();

				if (!listOfIntWasRegistered)
					UserData.UnregisterType<List<int>>();

				if (!listWasRegistered)
					UserData.UnregisterType(typeof(List<>));

				UserData.RegisterType(dynamicWrapper, InteropAccessMode.HideMembers);
				UserData.RegistrationPolicy = oldPolicy;
			}
		}
	}
}
