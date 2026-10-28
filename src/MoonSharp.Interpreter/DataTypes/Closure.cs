using System.Collections.Generic;
using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;

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

		// The upvalue symbols of the function prototype, shared by every closure made from it; only the
		// debugger and the debug library read their names.
		SymbolRef[] m_UpvalueSymbols;


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
			m_UpvalueSymbols = symbols.Length > 0 ? symbols : s_NoSymbols;
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
			m_UpvalueSymbols = symbols ?? s_NoSymbols;
		}

		/// <summary>
		/// Calls this function with the specified args
		/// </summary>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">Thrown if function is not of DataType.Function</exception>
		public DynValue Call()
		{
			return OwnerScript.Call(this);
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
			return m_UpvalueSymbols[idx].i_Name;
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
