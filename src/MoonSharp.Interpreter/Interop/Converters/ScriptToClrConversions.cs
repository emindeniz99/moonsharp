using System;
using MoonSharp.Interpreter.Compatibility;

namespace MoonSharp.Interpreter.Interop.Converters
{
	internal static class ScriptToClrConversions
	{
		internal const int WEIGHT_MAX_VALUE = 101;
		// Below an exact match (upstream #294), above every other conversion.
		internal const int WEIGHT_CUSTOM_CONVERTER_MATCH = 100;
		internal const int WEIGHT_EXACT_MATCH = 101;
		internal const int WEIGHT_STRING_TO_STRINGBUILDER = 99;
		internal const int WEIGHT_STRING_TO_CHAR = 98;
		internal const int WEIGHT_NIL_TO_NULLABLE = 101;
		internal const int WEIGHT_NIL_TO_REFTYPE = 101;
		internal const int WEIGHT_VOID_WITH_DEFAULT = 50;
		internal const int WEIGHT_VOID_WITHOUT_DEFAULT = 25;
		internal const int WEIGHT_NIL_WITH_DEFAULT = 25;
		internal const int WEIGHT_BOOL_TO_STRING = 5;
		internal const int WEIGHT_NUMBER_TO_STRING = 50;
		internal const int WEIGHT_NUMBER_TO_ENUM = 90;
		internal const int WEIGHT_USERDATA_TO_STRING = 5;
		internal const int WEIGHT_TABLE_CONVERSION = 90;
		internal const int WEIGHT_NUMBER_DOWNCAST = 99;
		internal const int WEIGHT_FRACTION_TO_INTEGER = 98;
		// A Lua function as a delegate (upstream #186): below Closure and ScriptFunctionDelegate.
		internal const int WEIGHT_FUNCTION_TO_DELEGATE = 90;
		internal const int WEIGHT_NO_MATCH = 0;
		internal const int WEIGHT_NO_EXTRA_PARAMS_BONUS = 100;
		internal const int WEIGHT_EXTRA_PARAMS_MALUS = 2;
		internal const int WEIGHT_BYREF_BONUSMALUS = -10;
		internal const int WEIGHT_VARARGS_MALUS = 1;
		internal const int WEIGHT_VARARGS_EMPTY = 40;

		/// <summary>
		/// Converts a DynValue to a CLR object [simple conversion]
		/// </summary>
		internal static object DynValueToObject(DynValue value)
		{
			var converter = Script.GlobalOptions.CustomConverters.GetScriptToClrCustomConversion(value.Type, typeof(System.Object));
			if (converter != null)
			{
				var v = converter(value);
				if (v != null)
					return v;
			}

			switch (value.Type)
			{
				case DataType.Void:
				case DataType.Nil:
					return null;
				case DataType.Boolean:
					return value.Boolean;
				case DataType.Number:
					return value.Number;
				case DataType.String:
					return value.String;
				case DataType.Function:
					return value.Function;
				case DataType.Table:
					return value.Table;
				case DataType.Tuple:
					return value.Tuple;
				case DataType.UserData:
					if (value.UserData.Object != null)
						return value.UserData.Object;
					else if (value.UserData.Descriptor != null)
						return value.UserData.Descriptor.Type;
					else
						return null;
				case DataType.ClrFunction:
					return value.Callback;
				default:
					throw ScriptRuntimeException.ConvertObjectFailed(value.Type);
			}
		}


		/// <summary>
		/// Converts a DynValue to a CLR object of a specific type
		/// </summary>
		internal static object DynValueToObjectOfType(DynValue value, Type desiredType, object defaultValue, bool isOptional)
		{
			if (desiredType.IsByRef)
				desiredType = desiredType.GetElementType();

			return DynValueToObjectOfNonByRefType(value, desiredType, defaultValue, isOptional);
		}

		/// <summary>
		/// <see cref="DynValueToObjectOfType"/> for a type already known not to be by-reference.
		/// </summary>
		internal static object DynValueToObjectOfNonByRefType(DynValue value, Type desiredType, object defaultValue, bool isOptional)
		{
			var converter = Script.GlobalOptions.CustomConverters.GetScriptToClrCustomConversion(value.Type, desiredType);
			if (converter != null)
			{
				var v = converter(value);
				if (v != null) return v;
			}

			if (desiredType == typeof(DynValue))
				return value;

			if (desiredType == typeof(object))
				return DynValueToObject(value);

			StringConversions.StringSubtype stringSubType = StringConversions.GetStringSubtype(desiredType);
			string str = null;

			Type nt = Nullable.GetUnderlyingType(desiredType);
			Type nullableType = null;

			if (nt != null)
			{
				nullableType = desiredType;
				desiredType = nt;
			}

			switch (value.Type)
			{
				case DataType.Void:
					if (isOptional)
						return defaultValue;
					else if ((!Framework.Do.IsValueType(desiredType)) || (nullableType != null))
						return null;
					break;
				case DataType.Nil:
					if (Framework.Do.IsValueType(desiredType))
					{
						if (nullableType != null)
							return null;

						if (isOptional)
							return defaultValue;
					}
					else
					{
						return null;
					}
					break;
				case DataType.Boolean:
					if (desiredType == typeof(bool))
						return value.Boolean;
					if (stringSubType != StringConversions.StringSubtype.None)
						str = value.Boolean.ToString();
					break;
				case DataType.Number:
					if (Framework.Do.IsEnum(desiredType))
					{	// number to enum conv
						Type underType = Enum.GetUnderlyingType(desiredType);
						return NumericConversions.DoubleToType(underType, value.Number);
					}
					if (NumericConversions.IsNumericType(desiredType))
					{
						object d = NumericConversions.DoubleToType(desiredType, value.Number);
						if (d.GetType() == desiredType)
							return d;
						break;
					}
					if (stringSubType != StringConversions.StringSubtype.None)
						str = NumberFormatting.ToShortestString(value.Number);
					break;
				case DataType.String:
					if (stringSubType != StringConversions.StringSubtype.None)
						str = value.String;
					break;
				case DataType.Function:
					if (desiredType == typeof(Closure)) return value.Function;
					else if (desiredType == typeof(ScriptFunctionDelegate)) return value.Function.GetDelegate();
					else if (IsDelegateType(desiredType)) return value.Function.ToDelegate(desiredType);
					break;
				case DataType.ClrFunction:
					if (desiredType == typeof(CallbackFunction)) return value.Callback;
					else if (desiredType == typeof(Func<ScriptExecutionContext, CallbackArguments, DynValue>)) return value.Callback.ClrCallback;
					break;
				case DataType.UserData:
					if (value.UserData.Object != null)
					{
						var udObj = value.UserData.Object;
						var udDesc = value.UserData.Descriptor;

						if (udDesc.IsTypeCompatible(desiredType, udObj))
							return udObj;

						object exact;
						if (TryConvertExactInteger(udObj, desiredType, out exact))
							return exact;

						if (stringSubType != StringConversions.StringSubtype.None)
							str = udDesc.AsString(udObj);
					}
					break;
				case DataType.Table:
					if (desiredType == typeof(Table) || Framework.Do.IsAssignableFrom(desiredType, typeof(Table)))
						return value.Table;
					else
					{
						object o = TableConversions.ConvertTableToType(value.Table, desiredType);
						if (o != null)
							return o;
					}
					break;
				case DataType.Tuple:
					break;
			}

			if (stringSubType != StringConversions.StringSubtype.None && str != null)
				return StringConversions.ConvertString(stringSubType, str, desiredType, value.Type);

			throw ScriptRuntimeException.ConvertObjectFailed(value.Type, desiredType);
		}

		/// <summary>
		/// Gets a relative weight of how much the conversion is matching the given types.
		/// Implementation must follow that of DynValueToObjectOfType.. it's not very DRY in that sense.
		/// However here we are in perf-sensitive path.. TODO : double-check the gain and see if a DRY impl is better.
		/// </summary>
		internal static int DynValueToObjectOfTypeWeight(DynValue value, Type desiredType, bool isOptional)
		{
			if (desiredType.IsByRef)
				desiredType = desiredType.GetElementType();

			var customConverter = Script.GlobalOptions.CustomConverters.GetScriptToClrCustomConversion(value.Type, desiredType);
			if (customConverter != null)
				return WEIGHT_CUSTOM_CONVERTER_MATCH;

			if (desiredType == typeof(DynValue))
				return WEIGHT_EXACT_MATCH;

			if (desiredType == typeof(object))
				return WEIGHT_EXACT_MATCH;

			StringConversions.StringSubtype stringSubType = StringConversions.GetStringSubtype(desiredType);

			Type nt = Nullable.GetUnderlyingType(desiredType);
			Type nullableType = null;

			if (nt != null)
			{
				nullableType = desiredType;
				desiredType = nt;
			}

			switch (value.Type)
			{
				case DataType.Void:
					if (isOptional)
						return WEIGHT_VOID_WITH_DEFAULT;
					else if ((!Framework.Do.IsValueType(desiredType)) || (nullableType != null))
						return WEIGHT_VOID_WITHOUT_DEFAULT;
					break;
				case DataType.Nil:
					if (Framework.Do.IsValueType(desiredType))
					{
						if (nullableType != null)
							return WEIGHT_NIL_TO_NULLABLE;

						if (isOptional)
							return WEIGHT_NIL_WITH_DEFAULT;
					}
					else
					{
						return WEIGHT_NIL_TO_REFTYPE;
					}
					break;
				case DataType.Boolean:
					if (desiredType == typeof(bool))
						return WEIGHT_EXACT_MATCH;
					if (stringSubType != StringConversions.StringSubtype.None)
						return WEIGHT_BOOL_TO_STRING;
					break;
				case DataType.Number:
					if (Framework.Do.IsEnum(desiredType))
					{	// number to enum conv
						return WEIGHT_NUMBER_TO_ENUM;
					}
					if (NumericConversions.IsNumericType(desiredType))
						return GetNumericTypeWeight(desiredType, value.Number);
					if (stringSubType != StringConversions.StringSubtype.None)
						return WEIGHT_NUMBER_TO_STRING;
					break;
				case DataType.String:
					if (stringSubType == StringConversions.StringSubtype.String)
						return WEIGHT_EXACT_MATCH;
					else if (stringSubType == StringConversions.StringSubtype.StringBuilder)
						return WEIGHT_STRING_TO_STRINGBUILDER;
					else if (stringSubType == StringConversions.StringSubtype.Char)
						return WEIGHT_STRING_TO_CHAR;
					break;
				case DataType.Function:
					if (desiredType == typeof(Closure)) return WEIGHT_EXACT_MATCH;
					else if (desiredType == typeof(ScriptFunctionDelegate)) return WEIGHT_EXACT_MATCH;
					else if (IsDelegateType(desiredType) && ClosureDelegates.CanCreate(desiredType)) return WEIGHT_FUNCTION_TO_DELEGATE;
					break;
				case DataType.ClrFunction:
					if (desiredType == typeof(CallbackFunction)) return WEIGHT_EXACT_MATCH;
					else if (desiredType == typeof(Func<ScriptExecutionContext, CallbackArguments, DynValue>)) return WEIGHT_EXACT_MATCH;
					break;
				case DataType.UserData:
					if (value.UserData.Object != null)
					{
						var udObj = value.UserData.Object;
						var udDesc = value.UserData.Descriptor;

						if (udDesc.IsTypeCompatible(desiredType, udObj))
							return WEIGHT_EXACT_MATCH;

						int exactWeight = GetExactIntegerWeight(udObj, desiredType);
						if (exactWeight != WEIGHT_NO_MATCH)
							return exactWeight;

						if (stringSubType != StringConversions.StringSubtype.None)
							return WEIGHT_USERDATA_TO_STRING;
					}
					break;
				case DataType.Table:
					if (desiredType == typeof(Table) || Framework.Do.IsAssignableFrom(desiredType, typeof(Table)))
						return WEIGHT_EXACT_MATCH;
					else if (TableConversions.CanConvertTableToType(value.Table, desiredType))
						return WEIGHT_TABLE_CONVERSION;
					break;
				case DataType.Tuple:
					break;
			}

			return WEIGHT_NO_MATCH;
		}

		/// <summary>
		/// A concrete delegate type: not Delegate or MulticastDelegate themselves, which have no signature.
		/// </summary>
		private static bool IsDelegateType(Type t)
		{
			return t != typeof(Delegate) && t != typeof(MulticastDelegate) && Framework.Do.IsAssignableFrom(typeof(Delegate), t);
		}

		/// <summary>
		/// A fractional number weighs less for an integral type than for float, so F(1.5) does not
		/// pick F(int) over F(float) and lose the fraction (upstream #293).
		/// </summary>
		private static int GetNumericTypeWeight(Type desiredType, double number)
		{
			if (desiredType == typeof(double) || desiredType == typeof(decimal))
				return WEIGHT_EXACT_MATCH;
			else if (desiredType == typeof(float) || !IsFractional(number))
				return WEIGHT_NUMBER_DOWNCAST;
			else
				return WEIGHT_FRACTION_TO_INTEGER;
		}

		/// <summary>
		/// Whether a number has a fractional part (NaN counts as one). Overload resolution weighs
		/// such numbers differently, so its cache keys on this too.
		/// </summary>
		internal static bool IsFractional(double number)
		{
			return number != Math.Floor(number);
		}

		/// <summary>
		/// int64 goes to long and uint64 to ulong as exact matches. Either goes to any other
		/// integral type or decimal with a range check, weighted like a number downcast. They
		/// never go to double or float: that would bring back the rounding they exist to avoid.
		/// </summary>
		private static int GetExactIntegerWeight(object udObj, Type desiredType)
		{
			if (udObj is LuaInt64)
				return desiredType == typeof(long) ? WEIGHT_EXACT_MATCH
					: IsIntegralOrDecimal(desiredType) ? WEIGHT_NUMBER_DOWNCAST : WEIGHT_NO_MATCH;

			if (udObj is LuaUInt64)
				return desiredType == typeof(ulong) ? WEIGHT_EXACT_MATCH
					: IsIntegralOrDecimal(desiredType) ? WEIGHT_NUMBER_DOWNCAST : WEIGHT_NO_MATCH;

			return WEIGHT_NO_MATCH;
		}

		private static bool IsIntegralOrDecimal(Type t)
		{
			return t == typeof(long) || t == typeof(ulong) || t == typeof(int) || t == typeof(uint)
				|| t == typeof(short) || t == typeof(ushort) || t == typeof(sbyte) || t == typeof(byte)
				|| t == typeof(decimal);
		}

		/// <summary>
		/// Converts an int64 or uint64 to an integral CLR type or decimal. A value that does not
		/// fit raises a script error instead of wrapping.
		/// </summary>
		private static bool TryConvertExactInteger(object udObj, Type desiredType, out object result)
		{
			result = null;

			LuaInt64 i = udObj as LuaInt64;
			LuaUInt64 u = udObj as LuaUInt64;

			if ((i == null && u == null) || !IsIntegralOrDecimal(desiredType))
				return false;

			if (i != null && desiredType == typeof(long))
			{
				result = i.Value;
				return true;
			}

			if (u != null && desiredType == typeof(ulong))
			{
				result = u.Value;
				return true;
			}

			// decimal holds every 64-bit integer exactly, and its explicit conversions throw
			// OverflowException whatever the checked context.
			decimal d = (i != null) ? (decimal)i.Value : (decimal)u.Value;

			try
			{
				if (desiredType == typeof(long)) result = (long)d;
				else if (desiredType == typeof(ulong)) result = (ulong)d;
				else if (desiredType == typeof(int)) result = (int)d;
				else if (desiredType == typeof(uint)) result = (uint)d;
				else if (desiredType == typeof(short)) result = (short)d;
				else if (desiredType == typeof(ushort)) result = (ushort)d;
				else if (desiredType == typeof(sbyte)) result = (sbyte)d;
				else if (desiredType == typeof(byte)) result = (byte)d;
				else result = d;
			}
			catch (OverflowException)
			{
				throw new ScriptRuntimeException("{0} value {1} is out of range for {2}",
					(i != null) ? "int64" : "uint64", udObj, desiredType.Name);
			}

			return true;
		}




	}
}
