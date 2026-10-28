using System;
using MoonSharp.Interpreter.Execution.VM;

namespace MoonSharp.Interpreter.Interop.Converters
{
	/// <summary>
	/// Converts a <typeparamref name="T"/> to a VM value as <see cref="DynValue.FromObject"/> does, without
	/// boxing it when <typeparamref name="T"/> is a common primitive type. The converter is chosen once per
	/// type, so a call does not test the type again (idea 7 of docs/research/xlua.md).
	/// </summary>
	internal static class ClrToScript<T>
	{
		// Null when T has no typed conversion. Created from method groups, not by reflection, so AOT
		// compilers see every instantiation the code uses.
		static readonly Func<Script, T, LuaValue> s_Typed = (Func<Script, T, LuaValue>)TypedConversions.ClrToScriptFor(typeof(T));

		internal static LuaValue Convert(Script script, T value)
		{
			// A custom converter, registered at any time, wins as in ClrToScriptConversions.
			if (s_Typed != null && Script.GlobalOptions.CustomConverters.GetClrToScriptCustomConversion(typeof(T)) == null)
				return s_Typed(script, value);

			LuaValue v = DynValue.FromObject(script, value).ToLuaValue();
			TypedConversions.CheckOwnership(script, ref v);
			return v;
		}
	}

	/// <summary>
	/// Converts a VM value to a <typeparamref name="T"/> as <see cref="DynValue.ToObject{T}"/> does, without a
	/// <see cref="DynValue"/> when <typeparamref name="T"/> is a common type and the value already has it.
	/// </summary>
	internal static class ScriptToClr<T>
	{
		static readonly Func<LuaValue, T> s_Convert = (Func<LuaValue, T>)TypedConversions.ScriptToClrFor(typeof(T)) ?? ToObject;

		internal static T Convert(LuaValue value)
		{
			return s_Convert(value);
		}

		internal static T ToObject(LuaValue value)
		{
			return value.ToDynValue().ToObject<T>();
		}
	}

	internal static class TypedConversions
	{
		internal static Delegate ClrToScriptFor(Type t)
		{
			if (t == typeof(double)) return new Func<Script, double, LuaValue>(FromDouble);
			if (t == typeof(float)) return new Func<Script, float, LuaValue>(FromSingle);
			if (t == typeof(int)) return new Func<Script, int, LuaValue>(FromInt32);
			if (t == typeof(long)) return new Func<Script, long, LuaValue>(FromInt64);
			if (t == typeof(bool)) return new Func<Script, bool, LuaValue>(FromBoolean);
			if (t == typeof(string)) return new Func<Script, string, LuaValue>(FromString);
			if (t == typeof(DynValue)) return new Func<Script, DynValue, LuaValue>(FromDynValue);
			if (t == typeof(Table)) return new Func<Script, Table, LuaValue>(FromTable);
			if (t == typeof(Closure)) return new Func<Script, Closure, LuaValue>(FromClosure);
			return null;
		}

		static LuaValue FromDouble(Script script, double x) { return LuaValue.FromNumber(x); }
		static LuaValue FromSingle(Script script, float x) { return LuaValue.FromNumber(x); }
		static LuaValue FromInt32(Script script, int x) { return LuaValue.FromNumber(x); }
		static LuaValue FromBoolean(Script script, bool x) { return LuaValue.FromBoolean(x); }

		static LuaValue FromInt64(Script script, long x)
		{
			if (script != null && script.Options.ExactInt64Interop)
				return LuaInt64.NewDynValue(x).ToLuaValue();

			return LuaValue.FromNumber(x);
		}

		static LuaValue FromString(Script script, string x)
		{
			return x == null ? LuaValue.Nil : LuaValue.FromString(x);
		}

		static LuaValue FromDynValue(Script script, DynValue x)
		{
			if (x == null)
				return LuaValue.Nil;

			LuaValue v = x.ToLuaValue();
			CheckOwnership(script, ref v);
			return v;
		}

		static LuaValue FromTable(Script script, Table x)
		{
			if (x == null)
				return LuaValue.Nil;

			script.CheckScriptOwnership(x);
			return LuaValue.FromObject(DataType.Table, x);
		}

		static LuaValue FromClosure(Script script, Closure x)
		{
			if (x == null)
				return LuaValue.Nil;

			script.CheckScriptOwnership(x);
			return LuaValue.FromObject(DataType.Function, x);
		}

		/// <summary>
		/// The check Script.Call makes on each argument: tables, functions, coroutines and userdata of another
		/// script are refused.
		/// </summary>
		internal static void CheckOwnership(Script script, ref LuaValue v)
		{
			IScriptPrivateResource resource = v.Type == DataType.String ? null : v.Object as IScriptPrivateResource;

			if (resource != null)
				script.CheckScriptOwnership(resource);
		}

		internal static Delegate ScriptToClrFor(Type t)
		{
			if (t == typeof(double)) return new Func<LuaValue, double>(ToDouble);
			if (t == typeof(float)) return new Func<LuaValue, float>(ToSingle);
			if (t == typeof(int)) return new Func<LuaValue, int>(ToInt32);
			if (t == typeof(long)) return new Func<LuaValue, long>(ToInt64);
			if (t == typeof(bool)) return new Func<LuaValue, bool>(ToBoolean);
			if (t == typeof(string)) return new Func<LuaValue, string>(ToStringValue);
			if (t == typeof(DynValue)) return new Func<LuaValue, DynValue>(ToDynValue);
			if (t == typeof(Table)) return new Func<LuaValue, Table>(ToTable);
			if (t == typeof(Closure)) return new Func<LuaValue, Closure>(ToClosure);
			return null;
		}

		// Whether no custom converter is registered from this script type to this CLR type.
		static bool NoConv(DataType from, Type to)
		{
			return Script.GlobalOptions.CustomConverters.GetScriptToClrCustomConversion(from, to) == null;
		}

		// The ranges and Convert calls below are NumericConversions.DoubleToType's: a number outside them
		// takes the general conversion, which reports the error.
		static double ToDouble(LuaValue v)
		{
			if (v.Type == DataType.Number && NoConv(DataType.Number, typeof(double)))
				return v.Number;
			return ScriptToClr<double>.ToObject(v);
		}

		static float ToSingle(LuaValue v)
		{
			if (v.Type == DataType.Number && NoConv(DataType.Number, typeof(float)))
				return System.Convert.ToSingle(v.Number);
			return ScriptToClr<float>.ToObject(v);
		}

		static int ToInt32(LuaValue v)
		{
			if (v.Type == DataType.Number && NoConv(DataType.Number, typeof(int)) && v.Number >= -2147483648.0 && v.Number <= 2147483647.0)
				return System.Convert.ToInt32(v.Number);
			return ScriptToClr<int>.ToObject(v);
		}

		static long ToInt64(LuaValue v)
		{
			// 2^63 is an exact double; below it Convert does not overflow.
			if (v.Type == DataType.Number && NoConv(DataType.Number, typeof(long)) && v.Number >= -9223372036854775808.0 && v.Number < 9223372036854775808.0)
				return System.Convert.ToInt64(v.Number);
			return ScriptToClr<long>.ToObject(v);
		}

		static bool ToBoolean(LuaValue v)
		{
			if (v.Type == DataType.Boolean && NoConv(DataType.Boolean, typeof(bool)))
				return v.Number != 0;
			return ScriptToClr<bool>.ToObject(v);
		}

		static string ToStringValue(LuaValue v)
		{
			if (v.Type == DataType.String && NoConv(DataType.String, typeof(string)))
				return (string)v.Object;
			return ScriptToClr<string>.ToObject(v);
		}

		static DynValue ToDynValue(LuaValue v)
		{
			if (NoConv(v.Type, typeof(DynValue)))
				return v.ToDynValue();
			return ScriptToClr<DynValue>.ToObject(v);
		}

		static Table ToTable(LuaValue v)
		{
			if (v.Type == DataType.Table && NoConv(DataType.Table, typeof(Table)))
				return (Table)v.Object;
			return ScriptToClr<Table>.ToObject(v);
		}

		static Closure ToClosure(LuaValue v)
		{
			if (v.Type == DataType.Function && NoConv(DataType.Function, typeof(Closure)))
				return (Closure)v.Object;
			return ScriptToClr<Closure>.ToObject(v);
		}
	}
}
