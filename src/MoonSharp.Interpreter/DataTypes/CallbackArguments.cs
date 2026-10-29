using System.Collections.Generic;
using MoonSharp.Interpreter.DataStructs;
using MoonSharp.Interpreter.Execution.VM;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// This class is a container for arguments received by a CallbackFunction
	/// </summary>
	public class CallbackArguments
	{
		// The arguments: a list, or (m_Args null) a StackArguments view over the top slots of the VM's value
		// stack, which the standard library reads in place while the call runs. A view is valid only during
		// the call; library functions never keep it. The view is a subclass so that list arguments, which
		// any host callback gets, stay as small as they were.
		static readonly DynValue[] s_NoValues = new DynValue[0];

		IList<DynValue> m_Args;
		int m_Count;
		bool m_LastIsTuple = false;

		/// <summary>
		/// Initializes a new instance of the <see cref="CallbackArguments" /> class.
		/// </summary>
		/// <param name="args">The arguments.</param>
		/// <param name="isMethodCall">if set to <c>true</c> [is method call].</param>
		public CallbackArguments(IList<DynValue> args, bool isMethodCall)
		{
			m_Args = args;

			if (m_Args.Count > 0)
			{
				var last = m_Args[m_Args.Count - 1];
				CountArguments(m_Args.Count, last.Type, last.Type == DataType.Tuple ? last.Tuple.Length : 0);
			}

			IsMethodCall = isMethodCall;
		}

		private protected CallbackArguments(bool isMethodCall)
		{
			IsMethodCall = isMethodCall;
		}

		private protected void CountArguments(int slots, DataType lastType, int lastTupleLength)
		{
			m_LastIsTuple = false;

			if (lastType == DataType.Tuple)
			{
				m_Count = lastTupleLength - 1 + slots;
				m_LastIsTuple = true;
			}
			else if (lastType == DataType.Void)
			{
				m_Count = slots - 1;
			}
			else
			{
				m_Count = slots;
			}
		}

		/// <summary>
		/// The arguments of a call to a C# function that uses them only during the call, read in place from
		/// the value stack: the <c>slots</c> values from <c>first</c>, one argument each (the call expanded a
		/// last argument holding several values or none). The processor reuses one view per call depth
		/// (<see cref="Reuse"/>).
		/// </summary>
		internal sealed class StackArguments : CallbackArguments
		{
			ValueStack m_Stack;
			int m_First;

			internal StackArguments(ValueStack stack, int first, int slots, bool isMethodCall)
				: base(isMethodCall)
			{
				Reuse(stack, first, slots, isMethodCall);
			}

			internal void Reuse(ValueStack stack, int first, int slots, bool isMethodCall)
			{
				m_Stack = stack;
				m_First = first;
				IsMethodCall = isMethodCall;
				CountArguments(slots, DataType.Nil, 0);
			}

			// The same value RawGet reads in list mode: a tuple (a call or '...' in the middle of the argument
			// list) gives its first value, or nil when empty.
			internal LuaValue Value(int index)
			{
				LuaValue v = m_Stack.Items[m_First + index];

				if (v.Type == DataType.Tuple)
				{
					ValueStack varArgs = v.Object as ValueStack;

					if (varArgs != null)
						return ValueStack.VarArgsCount(v.Number) > 0 ? varArgs.VarArgsItem(v.Number, 0) : LuaValue.Nil;

					DynValue[] tuple = v.TupleValue.Tuple;
					v = tuple.Length > 0 ? tuple[0].ToLuaValue() : LuaValue.Nil;
				}

				return v;
			}
		}

		/// <summary>
		/// Gets the count of arguments
		/// </summary>
		public int Count
		{
			get { return m_Count; }
		}

		/// <summary>
		/// Gets or sets a value indicating whether this is a method call.
		/// </summary>
		public bool IsMethodCall { get; private set; }


		/// <summary>
		/// Gets the <see cref="DynValue"/> at the specified index, or Void if not found 
		/// </summary>
		public DynValue this[int index]
		{
			get
			{
				return RawGet(index, true) ?? DynValue.Void;
			}
		}

		/// <summary>
		/// Gets the <see cref="DynValue" /> at the specified index, or null.
		/// </summary>
		/// <param name="index">The index.</param>
		/// <param name="translateVoids">if set to <c>true</c> all voids are translated to nils.</param>
		/// <returns></returns>
		public DynValue RawGet(int index, bool translateVoids)
		{
			DynValue v;

			if (index >= m_Count)
				return null;

			if (m_Args == null)
			{
				LuaValue lv = ((StackArguments)this).Value(index);

				if (translateVoids && lv.Type == DataType.Void)
					return DynValue.Nil;

				return lv.ToDynValue();
			}

			if (!m_LastIsTuple || index < m_Args.Count - 1)
				v = m_Args[index];
			else
				v = m_Args[m_Args.Count - 1].Tuple[index - (m_Args.Count - 1)];

			if (v.Type == DataType.Tuple)
			{
				if (v.Tuple.Length > 0)
					v = v.Tuple[0];
				else
					v = DynValue.Nil;
			}

			if (translateVoids && v.Type == DataType.Void)
			{
				v = DynValue.Nil;
			}

			return v;
		}

		/// <summary>
		/// The argument at <paramref name="index"/> as the VM holds it, without creating a DynValue: nil when
		/// absent or void, the first value of a tuple.
		/// </summary>
		internal LuaValue GetLuaValue(int index)
		{
			if (index >= m_Count)
				return LuaValue.Nil;

			LuaValue v = m_Args == null ? ((StackArguments)this).Value(index) : RawGet(index, false).ToLuaValue();
			return v.Type == DataType.Void ? LuaValue.Nil : v;
		}

		// The argument at index as RawGet(index, false) ?? DynValue.Void gives it, without creating a DynValue.
		LuaValue ValueAt(int index)
		{
			if (index >= m_Count)
				return LuaValue.Void;

			return m_Args == null ? ((StackArguments)this).Value(index) : RawGet(index, false).ToLuaValue();
		}

		/// <summary>
		/// The type of the argument at <paramref name="index"/>, without creating a <see cref="DynValue"/>:
		/// <see cref="DataType.Void"/> when it is missing or is a call that returned no value (the type of
		/// <c>RawGet(index, false) ?? DynValue.Void</c>). With <see cref="NumberAt"/>, <see cref="StringAt"/> and
		/// the other readers below, a callback reads its arguments without allocating.
		/// </summary>
		public DataType TypeAt(int index)
		{
			return ValueAt(index).Type;
		}

		/// <summary>
		/// The <see cref="DynValue.Number"/> of the argument at <paramref name="index"/>, without creating a
		/// <see cref="DynValue"/>: the number, 1 or 0 for a boolean, 0 for anything else.
		/// </summary>
		public double NumberAt(int index)
		{
			return ValueAt(index).Number;
		}

		/// <summary>
		/// The <see cref="DynValue.Boolean"/> of the argument at <paramref name="index"/>, without creating a
		/// <see cref="DynValue"/>.
		/// </summary>
		public bool BooleanAt(int index)
		{
			return ValueAt(index).Number != 0;
		}

		/// <summary>
		/// The string at <paramref name="index"/>, or null when that argument is not a string.
		/// </summary>
		public string StringAt(int index)
		{
			return ValueAt(index).Object as string;
		}

		/// <summary>
		/// The table at <paramref name="index"/>, or null when that argument is not a table.
		/// </summary>
		public Table TableAt(int index)
		{
			return ValueAt(index).Object as Table;
		}

		/// <summary>
		/// The Lua function at <paramref name="index"/>, or null when that argument is not one.
		/// </summary>
		public Closure FunctionAt(int index)
		{
			return ValueAt(index).Object as Closure;
		}

		/// <summary>
		/// The userdata at <paramref name="index"/>, or null when that argument is not a userdata.
		/// </summary>
		public UserData UserDataAt(int index)
		{
			return ValueAt(index).Object as UserData;
		}

		/// <summary>
		/// Converts the arguments to an array
		/// </summary>
		/// <param name="skip">The number of elements to skip (default= 0).</param>
		/// <returns></returns>
		public DynValue[] GetArray(int skip = 0)
		{
			if (skip >= m_Count)
				return s_NoValues;

			DynValue[] vals = new DynValue[m_Count - skip];

			for (int i = skip; i < m_Count; i++)
				vals[i - skip] = this[i];

			return vals;
		}

		/// <summary>
		/// Gets the specified argument as as an argument of the specified type. If not possible,
		/// an exception is raised.
		/// </summary>
		/// <param name="argNum">The argument number.</param>
		/// <param name="funcName">Name of the function.</param>
		/// <param name="type">The type desired.</param>
		/// <param name="allowNil">if set to <c>true</c> nil values are allowed.</param>
		/// <returns></returns>
		public DynValue AsType(int argNum, string funcName, DataType type, bool allowNil = false)
		{
			return this[argNum].CheckType(funcName, type, argNum, allowNil ? TypeValidationFlags.AllowNil | TypeValidationFlags.AutoConvert : TypeValidationFlags.AutoConvert);
		}

		/// <summary>
		/// Gets the specified argument as as an argument of the specified user data type. If not possible,
		/// an exception is raised.
		/// </summary>
		/// <typeparam name="T">The desired userdata type</typeparam>
		/// <param name="argNum">The argument number.</param>
		/// <param name="funcName">Name of the function.</param>
		/// <param name="allowNil">if set to <c>true</c> nil values are allowed.</param>
		/// <returns></returns>
		public T AsUserData<T>(int argNum, string funcName, bool allowNil = false)
		{
			return this[argNum].CheckUserDataType<T>(funcName, argNum, allowNil ? TypeValidationFlags.AllowNil : TypeValidationFlags.None);
		}

		/// <summary>
		/// Gets the specified argument as an integer
		/// </summary>
		/// <param name="argNum">The argument number.</param>
		/// <param name="funcName">Name of the function.</param>
		/// <returns></returns>
		public int AsInt(int argNum, string funcName)
		{
			DynValue v = AsType(argNum, funcName, DataType.Number, false);
			double d = v.Number;
			return (int)d;
		}

		/// <summary>
		/// Gets the specified argument as a long integer
		/// </summary>
		/// <param name="argNum">The argument number.</param>
		/// <param name="funcName">Name of the function.</param>
		/// <returns></returns>
		public long AsLong(int argNum, string funcName)
		{
			DynValue v = AsType(argNum, funcName, DataType.Number, false);
			double d = v.Number;
			return (long)d;
		}


		/// <summary>
		/// Gets the specified argument as a string, calling the __tostring metamethod if needed, in a NON
		/// yield-compatible way.
		/// </summary>
		/// <param name="executionContext">The execution context.</param>
		/// <param name="argNum">The argument number.</param>
		/// <param name="funcName">Name of the function.</param>
		/// <returns></returns>
		/// <exception cref="ScriptRuntimeException">'tostring' must return a string to '{0}'</exception>
		public string AsStringUsingMeta(ScriptExecutionContext executionContext, int argNum, string funcName)
		{
			if ((this[argNum].Type == DataType.Table) && (this[argNum].Table.MetaTable != null) &&
				(this[argNum].Table.MetaTable.RawGet("__tostring") != null))
			{
				var v = executionContext.GetScript().Call(this[argNum].Table.MetaTable.RawGet("__tostring"), this[argNum]);

				if (v.Type != DataType.String)
					throw new ScriptRuntimeException("'tostring' must return a string to '{0}'", funcName);

				return v.ToPrintString();
			}
			else
			{
				return (this[argNum].ToPrintString());
			}
		}


		/// <summary>
		/// Returns a copy of CallbackArguments where the first ("self") argument is skipped if this was a method call,
		/// otherwise returns itself. Arguments read in place on the value stack are copied, so the result stays
		/// valid after the call: CLR methods that take a <see cref="CallbackArguments"/> parameter get it from here.
		/// </summary>
		/// <returns></returns>
		public CallbackArguments SkipMethodCall()
		{
			if (m_Args == null)
			{
				int skip = this.IsMethodCall ? 1 : 0;
				DynValue[] values = new DynValue[System.Math.Max(0, m_Count - skip)];

				for (int i = 0; i < values.Length; i++)
					values[i] = RawGet(i + skip, false);

				return new CallbackArguments(values, false);
			}

			if (this.IsMethodCall)
			{
				Slice<DynValue> slice = new Slice<DynValue>(m_Args, 1, m_Args.Count - 1, false);
				return new CallbackArguments(slice, false);
			}
			else return this;
		}
	}
}
