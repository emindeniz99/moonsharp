using System;
using System.Reflection;
using System.Text;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;

namespace MoonSharp.Interpreter.Interop.Converters
{
	internal static class ClrToScriptConversions
	{
		/// <summary>
		/// Tries to convert a CLR object to a MoonSharp value, using "trivial" logic.
		/// Skips on custom conversions, etc.
		/// Does NOT throw on failure.
		/// </summary>
		internal static DynValue TryObjectToTrivialDynValue(Script script, object obj)
		{
			if (obj == null)
				return DynValue.Nil;

			if (obj is DynValue)
				return (DynValue)obj;

			Type t = obj.GetType();

			if (obj is bool)
				return DynValue.NewBoolean((bool)obj);

			if (obj is string || obj is StringBuilder || obj is char)
				return DynValue.NewString(obj.ToString());

			if (NumericConversions.IsNumericType(t))
				return DynValue.NewNumber(NumericConversions.TypeToDouble(t, obj));

			if (obj is Table)
				return DynValue.NewTable((Table)obj);

			return null;
		}


		/// <summary>
		/// Tries to convert a CLR object to a MoonSharp value, using "simple" logic.
		/// Does NOT throw on failure.
		/// </summary>
		internal static DynValue TryObjectToSimpleDynValue(Script script, object obj)
		{
			if (obj == null)
				return DynValue.Nil;

			if (obj is DynValue)
				return (DynValue)obj;


			var converter = Script.GlobalOptions.CustomConverters.GetClrToScriptCustomConversion(obj.GetType());
			if (converter != null)
			{
				var v = converter(script, obj);
				if (v != null)
					return v;
			}

			Type t = obj.GetType();

			if (obj is bool)
				return DynValue.NewBoolean((bool)obj);

			if (obj is string || obj is StringBuilder || obj is char)
				return DynValue.NewString(obj.ToString());

			if (obj is Closure)
				return DynValue.NewClosure((Closure)obj);

			if (NumericConversions.IsNumericType(t))
			{
				if (script != null && script.Options.ExactInt64Interop)
				{
					if (t == typeof(long))
						return LuaInt64.NewDynValue((long)obj);
					if (t == typeof(ulong))
						return LuaUInt64.NewDynValue((ulong)obj);
				}

				return DynValue.NewNumber(NumericConversions.TypeToDouble(t, obj));
			}

			if (obj is Table)
				return DynValue.NewTable((Table)obj);

			if (obj is CallbackFunction)
				return DynValue.NewCallback((CallbackFunction)obj);

			// A type check, not d.Method: reading Method throws on NativeAOT and IL2CPP for a delegate
			// compiled from an expression tree, and the old signature check cast any other delegate
			// type with the same signature, which threw InvalidCastException.
			var callback = obj as Func<ScriptExecutionContext, CallbackArguments, DynValue>;
			if (callback != null)
				return DynValue.NewCallback(callback);

			return null;
		}


		/// <summary>
		/// Tries to convert a CLR object to a MoonSharp value, using more in-depth analysis
		/// </summary>
		internal static DynValue ObjectToDynValue(Script script, object obj)
		{
			DynValue v = TryObjectToSimpleDynValue(script, obj);

			if (v != null) return v;

			// int64 and uint64 carry their own descriptor and need no registration.
			if (obj is LuaInt64)
				return ((LuaInt64)obj).ToDynValue();
			if (obj is LuaUInt64)
				return ((LuaUInt64)obj).ToDynValue();

			v = UserData.Create(obj);
			if (v != null) return v;

			if (obj is Type)
				v = UserData.CreateStatic(obj as Type);

			// unregistered enums go as integers
			if (obj is Enum)
				return DynValue.NewNumber(NumericConversions.TypeToDouble(Enum.GetUnderlyingType(obj.GetType()), obj));

			if (v != null) return v;

			if (obj is Delegate)
				return DynValue.NewCallback(CallbackFunction.FromDelegate(script, (Delegate)obj));

			if (obj is MethodInfo)
			{
				MethodInfo mi = (MethodInfo)obj;

				if (mi.IsStatic)
				{
					return DynValue.NewCallback(CallbackFunction.FromMethodInfo(script, mi));
				}
			}

			if (obj is System.Collections.IList)
			{
				Table t = TableConversions.ConvertIListToTable(script, (System.Collections.IList)obj);
				return DynValue.NewTable(t);
			}

			if (obj is System.Collections.IDictionary)
			{
				Table t = TableConversions.ConvertIDictionaryToTable(script, (System.Collections.IDictionary)obj);
				return DynValue.NewTable(t);
			}

			var enumerator = EnumerationToDynValue(script, obj);
			if (enumerator != null) return enumerator;


			throw ScriptRuntimeException.ConvertObjectFailed(obj);
		}

		/// <summary>
		/// Converts an IEnumerable or IEnumerator to a DynValue
		/// </summary>
		/// <param name="script">The script.</param>
		/// <param name="obj">The object.</param>
		/// <returns></returns>
		public static DynValue EnumerationToDynValue(Script script, object obj)
		{
			if (obj is System.Collections.IEnumerable)
			{
				var enumer = (System.Collections.IEnumerable)obj;
				return EnumerableWrapper.ConvertIterator(script, enumer.GetEnumerator());
			}

			if (obj is System.Collections.IEnumerator)
			{
				var enumer = (System.Collections.IEnumerator)obj;
				return EnumerableWrapper.ConvertIterator(script, enumer);
			}

			return null;
		}



	}
}
