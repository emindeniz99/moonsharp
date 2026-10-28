using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Lets a test register its own descriptors for some types whatever the runner registered before
	/// (the Mono runner registers hardwired descriptors and keeps them with its policy), and puts the
	/// registry and the policy back afterwards.
	/// </summary>
	sealed class RegistrySnapshot : IDisposable
	{
		readonly IRegistrationPolicy m_Policy;
		readonly HashSet<Type> m_Registered;
		readonly Dictionary<Type, IUserDataDescriptor> m_Saved = new Dictionary<Type, IUserDataDescriptor>();

		/// <summary>Every type registered outside the interpreter itself, for a test that needs an empty registry.</summary>
		public static Type[] UserTypes()
		{
			return UserData.GetRegisteredTypes().Where(t => t.Assembly != typeof(Script).Assembly).ToArray();
		}

		public RegistrySnapshot(params Type[] types)
		{
			m_Policy = UserData.RegistrationPolicy;
			m_Registered = new HashSet<Type>(UserData.GetRegisteredTypes());
			UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;

			foreach (Type t in types)
			{
				if (UserData.IsTypeRegistered(t))
					m_Saved[t] = UserData.GetDescriptorForType(t, false);

				UserData.UnregisterType(t);
			}
		}

		public void Dispose()
		{
			UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;

			foreach (Type t in UserData.GetRegisteredTypes().ToList())
				if (!m_Registered.Contains(t) || m_Saved.ContainsKey(t))
					UserData.UnregisterType(t);

			foreach (var pair in m_Saved)
				UserData.RegisterType(pair.Key, pair.Value);

			UserData.RegistrationPolicy = m_Policy;
		}
	}
}
