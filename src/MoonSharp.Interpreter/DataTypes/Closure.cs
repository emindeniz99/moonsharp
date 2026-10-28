using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.Converters;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// A class representing a script function
	/// </summary>
	public class Closure : RefIdObject, IScriptPrivateResource
	{
		/// <summary>
		/// Type of closure based on upvalues
		/// </summary>
		public enum UpvaluesType
		{
			/// <summary>
			/// The closure has no upvalues (thus, technically, it's a function and not a closure!)
			/// </summary>
			None,
			/// <summary>
			/// The closure has _ENV as its only upvalue
			/// </summary>
			Environment,
			/// <summary>
			/// The closure is a "real" closure, with multiple upvalues
			/// </summary>
			Closure
		}


		/// <summary>
		/// Gets the entry point location in bytecode .
		/// </summary>
		public int EntryPointByteCodeLocation { get; private set; }


		/// <summary>
		/// Gets the script owning this function
		/// </summary>
		public Script OwnerScript { get; private set; }


		static readonly Upvalue[] s_NoUpvalues = new Upvalue[0];
		static readonly SymbolRef[] s_NoSymbols = new SymbolRef[0];

		/// <summary>
		/// The captured variables, in the order of the function's upvalue symbols.
		/// </summary>
		internal readonly Upvalue[] Upvalues;

		// The upvalue symbols of the function prototype (a SymbolRef[]), shared by every closure made from it;
		// only the debugger and the debug library read their names. Once ToDelegate has made a delegate, a
		// DelegateCache that holds the symbols and the delegates: closures that never become delegates pay
		// no field for the cache.
		object m_SymbolsOrDelegates;

		sealed class DelegateCache
		{
			public SymbolRef[] Symbols;
			public Delegate First;
			public Dictionary<Type, Delegate> More;
		}

		SymbolRef[] UpvalueSymbols
		{
			get { return m_SymbolsOrDelegates as SymbolRef[] ?? ((DelegateCache)m_SymbolsOrDelegates).Symbols; }
		}


		/// <summary>
		/// Initializes a new instance of the <see cref="Closure"/> class whose upvalues hold the given values.
		/// </summary>
		/// <param name="script">The script.</param>
		/// <param name="idx">The index.</param>
		/// <param name="symbols">The symbols.</param>
		/// <param name="resolvedLocals">The resolved locals.</param>
		internal Closure(Script script, int idx, SymbolRef[] symbols, IEnumerable<DynValue> resolvedLocals)
		{
			OwnerScript = script;

			EntryPointByteCodeLocation = idx;

			List<Upvalue> upvalues = new List<Upvalue>();

			foreach (DynValue v in resolvedLocals)
				upvalues.Add(Upvalue.CreateClosed(v.ToLuaValue()));

			Upvalues = upvalues.Count > 0 ? upvalues.ToArray() : s_NoUpvalues;
			m_SymbolsOrDelegates = symbols.Length > 0 ? symbols : s_NoSymbols;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Closure"/> class over captured upvalues, one per
		/// symbol.
		/// </summary>
		internal Closure(Script script, int idx, SymbolRef[] symbols, Upvalue[] upvalues)
		{
			OwnerScript = script;
			EntryPointByteCodeLocation = idx;
			Upvalues = upvalues ?? s_NoUpvalues;
			m_SymbolsOrDelegates = symbols ?? s_NoSymbols;
		}

		/// <summary>
		/// Calls this function with the specified args
		/// </summary>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">Thrown if function is not of DataType.Function</exception>
		public DynValue Call()
		{
			return OwnerScript.Call(DynValue.NewClosure(this), DynValue.NoValues);
		}

		/// <summary>
		/// Calls this function with the specified args
		/// </summary>
		/// <param name="args">The arguments to pass to the function.</param>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">Thrown if function is not of DataType.Function</exception>
		public DynValue Call(params object[] args)
		{
			return OwnerScript.Call(this, args);
		}

		/// <summary>
		/// Calls this function with the specified args
		/// </summary>
		/// <param name="args">The arguments to pass to the function.</param>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">Thrown if function is not of DataType.Function</exception>
		public DynValue Call(params DynValue[] args)
		{
			return OwnerScript.Call(this, args);
		}


		/// <summary>
		/// Calls this function and returns its first result as a <typeparamref name="TResult"/>, converted as
		/// <see cref="DynValue.ToObject{T}"/> converts it. With <see cref="DynValue"/> as
		/// <typeparamref name="TResult"/>, all the results come back, as <see cref="Call()"/> returns them.
		/// </summary>
		/// <remarks>
		/// The typed calls convert arguments as <see cref="Call(object[])"/> does, but numbers, booleans,
		/// strings, tables, functions and <see cref="DynValue"/>s go to the function without boxing, without an
		/// argument array and without a <see cref="DynValue"/> each; a result of type double, float, int, long,
		/// bool, string, <see cref="Table"/> or <see cref="Closure"/> comes back without a <see cref="DynValue"/>.
		/// </remarks>
		public TResult Call<TResult>()
		{
			return Result<TResult>(OwnerScript.MainProcessor.CallFromHost(this, 0, LuaValue.Nil, LuaValue.Nil, LuaValue.Nil, LuaValue.Nil));
		}

		/// <summary>
		/// Calls this function with one argument and returns its first result as a <typeparamref name="TResult"/>
		/// (see <see cref="Call{TResult}()"/>).
		/// </summary>
		public TResult Call<T1, TResult>(T1 arg1)
		{
			Script s = OwnerScript;
			LuaValue a1 = ClrToScript<T1>.Convert(s, arg1);
			return Result<TResult>(s.MainProcessor.CallFromHost(this, 1, a1, LuaValue.Nil, LuaValue.Nil, LuaValue.Nil));
		}

		/// <summary>
		/// Calls this function with two arguments and returns its first result as a <typeparamref name="TResult"/>
		/// (see <see cref="Call{TResult}()"/>).
		/// </summary>
		public TResult Call<T1, T2, TResult>(T1 arg1, T2 arg2)
		{
			Script s = OwnerScript;
			LuaValue a1 = ClrToScript<T1>.Convert(s, arg1);
			LuaValue a2 = ClrToScript<T2>.Convert(s, arg2);
			return Result<TResult>(s.MainProcessor.CallFromHost(this, 2, a1, a2, LuaValue.Nil, LuaValue.Nil));
		}

		/// <summary>
		/// Calls this function with three arguments and returns its first result as a
		/// <typeparamref name="TResult"/> (see <see cref="Call{TResult}()"/>).
		/// </summary>
		public TResult Call<T1, T2, T3, TResult>(T1 arg1, T2 arg2, T3 arg3)
		{
			Script s = OwnerScript;
			LuaValue a1 = ClrToScript<T1>.Convert(s, arg1);
			LuaValue a2 = ClrToScript<T2>.Convert(s, arg2);
			LuaValue a3 = ClrToScript<T3>.Convert(s, arg3);
			return Result<TResult>(s.MainProcessor.CallFromHost(this, 3, a1, a2, a3, LuaValue.Nil));
		}

		/// <summary>
		/// Calls this function with four arguments and returns its first result as a
		/// <typeparamref name="TResult"/> (see <see cref="Call{TResult}()"/>).
		/// </summary>
		public TResult Call<T1, T2, T3, T4, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
		{
			Script s = OwnerScript;
			LuaValue a1 = ClrToScript<T1>.Convert(s, arg1);
			LuaValue a2 = ClrToScript<T2>.Convert(s, arg2);
			LuaValue a3 = ClrToScript<T3>.Convert(s, arg3);
			LuaValue a4 = ClrToScript<T4>.Convert(s, arg4);
			return Result<TResult>(s.MainProcessor.CallFromHost(this, 4, a1, a2, a3, a4));
		}

		static TResult Result<TResult>(LuaValue result)
		{
			return ScriptToClr<TResult>.Convert(typeof(TResult) == typeof(DynValue) ? result : result.ToScalar());
		}

		/// <summary>
		/// Gets a delegate of type <typeparamref name="TDelegate"/> that calls this function, converting its
		/// arguments and result as the typed <see cref="Call{T1, TResult}"/> calls do. The delegate type may
		/// be any whose Invoke has up to four parameters, none by reference. The same function returns the
		/// same delegate for a type every time, so it can be removed from an event it was added to.
		/// </summary>
		/// <remarks>
		/// On AOT platforms (IL2CPP, NativeAOT), a delegate type with value-type parameters or result needs
		/// code compiled ahead of time: list the type in <c>[assembly: MoonSharpUserDataTypes]</c> and call
		/// <c>GeneratedUserData.RegisterAll()</c>, or register a factory with
		/// <see cref="UserData.RegisterDelegateType{TDelegate}"/>. The delegate keeps this function and its
		/// script alive.
		/// </remarks>
		public TDelegate ToDelegate<TDelegate>() where TDelegate : Delegate
		{
			return (TDelegate)ToDelegate(typeof(TDelegate));
		}

		internal Delegate ToDelegate(Type delegateType)
		{
			DelegateCache cache = m_SymbolsOrDelegates as DelegateCache;
			Delegate d;

			if (cache != null)
			{
				if (cache.First.GetType() == delegateType)
					return cache.First;

				if (cache.More != null && cache.More.TryGetValue(delegateType, out d))
					return d;
			}

			d = ClosureDelegates.Create(this, delegateType);

			if (cache == null)
			{
				m_SymbolsOrDelegates = new DelegateCache() { Symbols = (SymbolRef[])m_SymbolsOrDelegates, First = d };
			}
			else
			{
				if (cache.More == null)
					cache.More = new Dictionary<Type, Delegate>();

				cache.More.Add(delegateType, d);
			}

			return d;
		}

		/// <summary>
		/// Gets a delegate wrapping calls to this scripted function
		/// </summary>
		/// <returns></returns>
		public ScriptFunctionDelegate GetDelegate()
		{
			return args => this.Call(args).ToObject();
		}

		/// <summary>
		/// Gets a delegate wrapping calls to this scripted function
		/// </summary>
		/// <typeparam name="T">The type of return value of the delegate.</typeparam>
		/// <returns></returns>
		public ScriptFunctionDelegate<T> GetDelegate<T>()
		{
			return args => this.Call(args).ToObject<T>();
		}

		/// <summary>
		/// Gets the number of upvalues in this closure
		/// </summary>
		/// <returns>The number of upvalues in this closure</returns>
		public int GetUpvaluesCount()
		{
			return Upvalues.Length;
		}

		/// <summary>
		/// Gets the name of the specified upvalue.
		/// </summary>
		/// <param name="idx">The index of the upvalue.</param>
		/// <returns>The upvalue name</returns>
		public string GetUpvalueName(int idx)
		{
			return UpvalueSymbols[idx].i_Name;
		}

		/// <summary>
		/// Gets the value of an upvalue, as a read-only copy; to change the variable, use
		/// <see cref="SetUpvalue"/>.
		/// </summary>
		/// <param name="idx">The index of the upvalue.</param>
		/// <returns>The value of an upvalue </returns>
		public DynValue GetUpvalue(int idx)
		{
			return Upvalues[idx].Get().ToDynValue();
		}

		/// <summary>
		/// Sets the value of an upvalue, as debug.setupvalue does. Every closure that shares the variable,
		/// and the function that declared it while it runs, sees the new value.
		/// </summary>
		/// <param name="idx">The index of the upvalue.</param>
		/// <param name="value">The new value.</param>
		public void SetUpvalue(int idx, DynValue value)
		{
			Upvalues[idx].Set(value.ToLuaValue());
		}

		/// <summary>
		/// Gets the type of the upvalues contained in this closure
		/// </summary>
		/// <returns></returns>
		public UpvaluesType GetUpvaluesType()
		{
			int count = GetUpvaluesCount();

			if (count == 0)
				return UpvaluesType.None;
			else if (count == 1 && GetUpvalueName(0) == WellKnownSymbols.ENV)
				return UpvaluesType.Environment;
			else
				return UpvaluesType.Closure;
		}


	}
}
