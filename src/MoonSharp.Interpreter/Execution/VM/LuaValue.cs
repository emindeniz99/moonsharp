using System;
using System.Runtime.CompilerServices;

namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// The VM's own representation of a Lua value: a 24-byte struct kept on the value stack, in locals and
	/// in upvalues, so arithmetic, comparisons and variable traffic allocate nothing. <see cref="DynValue"/>
	/// stays the public type and is created only where a value leaves the VM (C# functions, tables, the
	/// host API, the debugger).
	///
	/// Number and Boolean keep their payload in <see cref="Number"/> (a boolean as 0 or 1) and null in
	/// <see cref="Object"/>. String, Function (a <see cref="Closure"/>), ClrFunction, Table, UserData and
	/// Thread keep the object itself. Tuple, TailCallRequest and YieldRequest keep the
	/// <see cref="DynValue"/> that carries them, except the generic for's iterator state, a Tuple whose
	/// object is a <see cref="LuaValue"/>[3], and the '...' of a running function, a Tuple whose object is
	/// the <see cref="ValueStack"/> that holds the values (<see cref="ValueStack.VarArgs"/>).
	/// default(LuaValue) is nil.
	///
	/// The design follows Lua-CSharp's LuaValue and WattleScript's struct DynValue on MoonSharp's own code
	/// (ideas only):
	/// https://github.com/nuskey8/Lua-CSharp/blob/a8d84c7c5e858a6118e2a9747f08856f5224793a/src/Lua/LuaValue.cs#L23-L37
	/// https://github.com/WattleScript/wattlescript/commit/abbf807d0aa013a1cfc2292b7be02d23a04886b1
	/// </summary>
	internal struct LuaValue
	{
		public object Object;
		public double Number;
		public DataType Type;

		public static readonly LuaValue Nil = default(LuaValue);
		public static readonly LuaValue Void = new LuaValue() { Type = DataType.Void };
		public static readonly LuaValue True = new LuaValue() { Type = DataType.Boolean, Number = 1 };
		public static readonly LuaValue False = new LuaValue() { Type = DataType.Boolean, Number = 0 };

		// The value of a local slot whose variable is not declared yet or went out of scope. It reads as nil;
		// only the debugger and symbol lookup tell it apart, to skip locals that are not in scope.
		static readonly object s_UndeclaredMarker = new object();
		public static readonly LuaValue Undeclared = new LuaValue() { Object = s_UndeclaredMarker };

		public bool IsUndeclared
		{
			get { return Type == DataType.Nil && Object == s_UndeclaredMarker; }
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static LuaValue FromNumber(double number)
		{
			LuaValue v;
			v.Object = null;
			v.Number = number;
			v.Type = DataType.Number;
			return v;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static LuaValue FromBoolean(bool value)
		{
			return value ? True : False;
		}

		public static LuaValue FromObject(DataType type, object obj)
		{
			LuaValue v;
			v.Object = obj;
			v.Number = 0;
			v.Type = type;
			return v;
		}

		public static LuaValue FromString(string str)
		{
			return FromObject(DataType.String, str);
		}

		/// <summary>
		/// The value a <see cref="DynValue"/> holds (a copy: changing either does not change the other).
		/// </summary>
		public static LuaValue FromDynValue(DynValue value)
		{
			return value.ToLuaValue();
		}

		/// <summary>
		/// A read-only <see cref="DynValue"/> for this value. Nil, booleans and small integers come from
		/// shared instances; other values allocate.
		/// </summary>
		public DynValue ToDynValue()
		{
			return DynValue.FromLuaValue(ref this);
		}

		public string String { get { return Object as string; } }
		public Table Table { get { return Object as Table; } }
		public Closure Function { get { return Object as Closure; } }
		public CallbackFunction Callback { get { return Object as CallbackFunction; } }
		public UserData UserData { get { return Object as UserData; } }

		/// <summary>
		/// The values of a Tuple, TailCallRequest or YieldRequest that came from a <see cref="DynValue"/>.
		/// </summary>
		public DynValue TupleValue { get { return Object as DynValue; } }

		public bool IsNil()
		{
			return Type == DataType.Nil || Type == DataType.Void;
		}

		public bool IsNotNil()
		{
			return Type != DataType.Nil && Type != DataType.Void;
		}

		/// <summary>
		/// The first value of a tuple (void for an empty one); any other value itself.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public LuaValue ToScalar()
		{
			if (Type != DataType.Tuple)
				return this;

			ValueStack stack = Object as ValueStack;

			// A ref, not a LuaValue: a struct returned by a call that is not inlined made the JIT keep the
			// result of every inlined ToScalar in memory, which slowed the instruction loop by about 10%
			// (TableInsertLookup1K in the A/B run, which calls nothing).
			if (stack != null)
				return stack.FirstVarArg(Number);

			DynValue[] values = ((DynValue)Object).Tuple;

			if (values.Length == 0)
				return Void;

			return values[0].ToScalar().ToLuaValue();
		}

		/// <summary>
		/// False for false, nil and void; true for everything else (call on a scalar).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool CastToBool()
		{
			if (Type == DataType.Boolean)
				return Number != 0;

			return Type != DataType.Nil && Type != DataType.Void;
		}

		/// <summary>
		/// The number, or a string that reads as a Lua numeral; null otherwise (call on a scalar).
		/// </summary>
		public double? CastToNumber()
		{
			if (Type == DataType.Number)
				return Number;

			if (Type == DataType.String)
			{
				double num;
				if (CoreLib.BasicModule.TryParseLuaNumber((string)Object, out num))
					return num;
			}

			return null;
		}

		/// <summary>
		/// The string, or a number as text; null otherwise (call on a scalar).
		/// </summary>
		public string CastToString()
		{
			if (Type == DataType.String)
				return (string)Object;

			if (Type == DataType.Number)
				return NumberFormatting.ToShortestString(Number);

			return null;
		}

		/// <summary>
		/// Raw equality, as <see cref="DynValue.Equals(object)"/> defines it: nil equals void, numbers,
		/// strings and booleans by value, userdata by descriptor and object, everything else by reference.
		/// </summary>
		public static bool RawEquals(ref LuaValue a, ref LuaValue b)
		{
			if (a.Type != b.Type)
				return (a.Type == DataType.Nil && b.Type == DataType.Void) || (a.Type == DataType.Void && b.Type == DataType.Nil);

			switch (a.Type)
			{
				case DataType.Nil:
				case DataType.Void:
					return true;
				case DataType.Boolean:
				case DataType.Number:
					return a.Number == b.Number;
				case DataType.String:
					return (string)a.Object == (string)b.Object;
				case DataType.UserData:
					{
						UserData ud1 = (UserData)a.Object;
						UserData ud2 = (UserData)b.Object;

						if (ud1 == null || ud2 == null)
							return false;

						if (ud1.Descriptor != ud2.Descriptor)
							return false;

						if (ud1.Object == null && ud2.Object == null)
							return true;

						if (ud1.Object != null && ud2.Object != null)
							return ud1.Object.Equals(ud2.Object);

						return false;
					}
				case DataType.Tuple:
				case DataType.TailCallRequest:
					return ReferenceEquals(TupleArray(ref a), TupleArray(ref b));
				default:
					return ReferenceEquals(a.Object, b.Object);
			}
		}

		static object TupleArray(ref LuaValue v)
		{
			DynValue d = v.Object as DynValue;
			return d != null ? (object)d.Tuple : v.Object;
		}

		public override string ToString()
		{
			return ToDynValue().ToString();
		}
	}
}
