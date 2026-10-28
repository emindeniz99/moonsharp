using System;
using System.Collections.Generic;
using System.Reflection;
using MoonSharp.Interpreter.Execution.VM;
using MoonSharp.Interpreter.Interop.Converters;

namespace MoonSharp.Interpreter.Interop
{
	/// <summary>
	/// Turns Lua functions into CLR delegates of any type whose Invoke takes up to four parameters, none by
	/// reference (upstream #186). Each delegate type has one factory: a registered one (the source
	/// generator registers typed factories, which AOT compilers see), or one built on first use from the
	/// generic methods of <see cref="ClosureDelegateBridge"/>. The delegates are cached on the <see cref="Closure"/>.
	/// The scheme is xLua's delegate bridge (docs/research/xlua.md, idea 8).
	/// </summary>
	internal static class ClosureDelegates
	{
		const int MAX_PARAMETERS = 4;

		static readonly object s_Lock = new object();

		// A null value: the type cannot be built (see s_Errors).
		static readonly Dictionary<Type, Func<Closure, Delegate>> s_Factories = new Dictionary<Type, Func<Closure, Delegate>>();
		static readonly Dictionary<Type, string> s_Errors = new Dictionary<Type, string>();

		internal static void Register(Type delegateType, Func<Closure, Delegate> factory)
		{
			lock (s_Lock)
			{
				s_Factories[delegateType] = factory;
				s_Errors.Remove(delegateType);
			}
		}

		/// <summary>
		/// Whether a Lua function converts to <paramref name="delegateType"/>; overload resolution asks this.
		/// </summary>
		internal static bool CanCreate(Type delegateType)
		{
			string error;
			return GetFactory(delegateType, out error) != null;
		}

		internal static Delegate Create(Closure closure, Type delegateType)
		{
			string error;
			Func<Closure, Delegate> factory = GetFactory(delegateType, out error);

			if (factory == null)
				throw new ScriptRuntimeException(error);

			return factory(closure);
		}

		static Func<Closure, Delegate> GetFactory(Type delegateType, out string error)
		{
			lock (s_Lock)
			{
				Func<Closure, Delegate> factory;

				if (!s_Factories.TryGetValue(delegateType, out factory))
				{
					factory = Build(delegateType, out error);
					s_Factories[delegateType] = factory;

					if (factory == null)
						s_Errors[delegateType] = error;
				}

				error = factory == null ? s_Errors[delegateType] : null;
				return factory;
			}
		}

		static Func<Closure, Delegate> Build(Type delegateType, out string error)
		{
			error = null;
			MethodInfo invoke = delegateType.GetMethod("Invoke");
			ParameterInfo[] parameters = invoke != null ? invoke.GetParameters() : null;

			if (parameters == null || parameters.Length > MAX_PARAMETERS || invoke.ReturnType.IsByRef || Array.Exists(parameters, p => p.ParameterType.IsByRef || p.ParameterType.IsPointer))
			{
				error = string.Format("cannot convert a Lua function to delegate type {0}: only delegates with up to {1} parameters, none by reference, are supported", delegateType, MAX_PARAMETERS);
				return null;
			}

			bool returnsValue = invoke.ReturnType != typeof(void);
			var typeArgs = new List<Type>();

			foreach (ParameterInfo p in parameters)
				typeArgs.Add(p.ParameterType);

			if (returnsValue)
				typeArgs.Add(invoke.ReturnType);

			try
			{
				MethodInfo method = typeof(ClosureDelegateBridge).GetMethod((returnsValue ? "Func" : "Action") + parameters.Length);

				if (typeArgs.Count > 0)
					method = method.MakeGenericMethod(typeArgs.ToArray());

				// Fails here, not at the first call, when an AOT compiler made no code for these types.
				Delegate.CreateDelegate(delegateType, new ClosureDelegateBridge(null), method);

				return closure => Delegate.CreateDelegate(delegateType, new ClosureDelegateBridge(closure), method);
			}
			catch (Exception ex)
			{
				error = string.Format("cannot convert a Lua function to delegate type {0} on this platform ({1}); list the type in [assembly: MoonSharpUserDataTypes] and call GeneratedUserData.RegisterAll(), or register a factory with UserData.RegisterDelegateType",
					delegateType, ex.Message);
				return null;
			}
		}
	}

	/// <summary>
	/// The target of a delegate built by reflection: one generic method per shape, closed over the
	/// delegate's parameter and return types.
	/// </summary>
	internal sealed class ClosureDelegateBridge
	{
		readonly Closure m_Closure;

		internal ClosureDelegateBridge(Closure closure)
		{
			m_Closure = closure;
		}

		LuaValue Run(int argCount, LuaValue a1, LuaValue a2, LuaValue a3, LuaValue a4)
		{
			return m_Closure.OwnerScript.MainProcessor.CallFromHost(m_Closure, argCount, a1, a2, a3, a4);
		}

		LuaValue Arg<T>(T value)
		{
			return ClrToScript<T>.Convert(m_Closure.OwnerScript, value);
		}

		public void Action0() { Run(0, LuaValue.Nil, LuaValue.Nil, LuaValue.Nil, LuaValue.Nil); }
		public void Action1<T1>(T1 a1) { Run(1, Arg(a1), LuaValue.Nil, LuaValue.Nil, LuaValue.Nil); }
		public void Action2<T1, T2>(T1 a1, T2 a2) { Run(2, Arg(a1), Arg(a2), LuaValue.Nil, LuaValue.Nil); }
		public void Action3<T1, T2, T3>(T1 a1, T2 a2, T3 a3) { Run(3, Arg(a1), Arg(a2), Arg(a3), LuaValue.Nil); }
		public void Action4<T1, T2, T3, T4>(T1 a1, T2 a2, T3 a3, T4 a4) { Run(4, Arg(a1), Arg(a2), Arg(a3), Arg(a4)); }

		public TResult Func0<TResult>() { return m_Closure.Call<TResult>(); }
		public TResult Func1<T1, TResult>(T1 a1) { return m_Closure.Call<T1, TResult>(a1); }
		public TResult Func2<T1, T2, TResult>(T1 a1, T2 a2) { return m_Closure.Call<T1, T2, TResult>(a1, a2); }
		public TResult Func3<T1, T2, T3, TResult>(T1 a1, T2 a2, T3 a3) { return m_Closure.Call<T1, T2, T3, TResult>(a1, a2, a3); }
		public TResult Func4<T1, T2, T3, T4, TResult>(T1 a1, T2 a2, T3 a3, T4 a4) { return m_Closure.Call<T1, T2, T3, T4, TResult>(a1, a2, a3, a4); }
	}
}
