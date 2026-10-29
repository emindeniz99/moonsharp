// Disable warnings about XML documentation
#pragma warning disable 1591

using System;

namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing the int64 Lua functions: exact signed 64-bit integers.
	/// Not part of any preset; enable it with <see cref="CoreModules.Int64"/>.
	/// <c>int64(v)</c> builds a value from an exact integer number, a decimal string, an int64
	/// or a uint64.
	/// </summary>
	[MoonSharpModule(Namespace = "int64")]
	public class Int64Module
	{
		public static void MoonSharpInit(Table globalTable, Table int64Table)
		{
			int64Table.Set("max", LuaInt64.NewDynValue(long.MaxValue));
			int64Table.Set("min", LuaInt64.NewDynValue(long.MinValue));

			Table meta = new Table(globalTable.OwnerScript);
			meta.Set("__call", CallbackFunction.NewInternal(call, "int64"));
			int64Table.MetaTable = meta;
		}

		private static DynValue call(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			// args[0] is the int64 table itself.
			return Construct(args[1], "int64");
		}

		internal static DynValue Construct(DynValue v, string funcName)
		{
			if (ExactInteger.AsInt64(v) != null)
				return v;

			LuaUInt64 u = ExactInteger.AsUInt64(v);
			if (u != null)
			{
				if (u.Value > long.MaxValue)
					throw ScriptRuntimeException.BadArgument(0, funcName, string.Format("uint64 value {0} is out of the int64 range", u));
				return LuaInt64.NewDynValue((long)u.Value);
			}

			string error;

			if (v.Type == DataType.Number)
			{
				error = ExactInteger.WhyNotSafeInteger(v.Number);
				if (error == null)
					return LuaInt64.NewDynValue((long)v.Number);
			}
			else if (v.Type == DataType.String)
			{
				long? parsed = ExactInteger.TryParseInt64(v.String, out error);
				if (parsed.HasValue)
					return LuaInt64.NewDynValue(parsed.Value);
			}
			else
			{
				error = string.Format("number, string, int64 or uint64 expected, got {0}", ExactInteger.TypeName(v));
			}

			throw ScriptRuntimeException.BadArgument(0, funcName, error);
		}

		[MoonSharpModuleMethod]
		public static DynValue @is(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return DynValue.NewBoolean(ExactInteger.AsInt64(args[0]) != null);
		}

		[MoonSharpModuleMethod]
		public static DynValue tostring(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			long v = ExactInteger.ArgToInt64(args, 0, "tostring");
			return DynValue.NewString(new LuaInt64(v).ToString());
		}

		/// <summary>
		/// Returns the value as a number, or nil plus a message when the number would not be exact
		/// (outside +/-(2^53-1)), following Lua's convention for failed conversions.
		/// </summary>
		[MoonSharpModuleMethod]
		public static DynValue tonumber(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			long v = ExactInteger.ArgToInt64(args, 0, "tonumber");

			if (v < -(long)ExactInteger.MaxSafeInteger || v > (long)ExactInteger.MaxSafeInteger)
				return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(string.Format(
					"int64 value {0} has no exact number representation", v)));

			return DynValue.NewNumber(v);
		}

		[MoonSharpModuleMethod]
		public static DynValue eq(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return ExactIntegerFunctions.Eq(args, "eq");
		}

		[MoonSharpModuleMethod]
		public static DynValue band(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			long r = ExactInteger.ArgToInt64(args, 0, "band");
			for (int i = 1; i < args.Count; i++)
				r &= ExactInteger.ArgToInt64(args, i, "band");
			return LuaInt64.NewDynValue(r);
		}

		[MoonSharpModuleMethod]
		public static DynValue bor(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			long r = ExactInteger.ArgToInt64(args, 0, "bor");
			for (int i = 1; i < args.Count; i++)
				r |= ExactInteger.ArgToInt64(args, i, "bor");
			return LuaInt64.NewDynValue(r);
		}

		[MoonSharpModuleMethod]
		public static DynValue bxor(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			long r = ExactInteger.ArgToInt64(args, 0, "bxor");
			for (int i = 1; i < args.Count; i++)
				r ^= ExactInteger.ArgToInt64(args, i, "bxor");
			return LuaInt64.NewDynValue(r);
		}

		[MoonSharpModuleMethod]
		public static DynValue bnot(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return LuaInt64.NewDynValue(~ExactInteger.ArgToInt64(args, 0, "bnot"));
		}

		[MoonSharpModuleMethod]
		public static DynValue shl(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			long x = ExactInteger.ArgToInt64(args, 0, "shl");
			int n = ExactInteger.ArgToShiftCount(args, 1, "shl");
			return LuaInt64.NewDynValue(unchecked((long)ExactInteger.ShiftLeft((ulong)x, n)));
		}

		/// <summary>
		/// Logical shift right: zeros come in from the left, as with Lua 5.3's &gt;&gt;.
		/// </summary>
		[MoonSharpModuleMethod]
		public static DynValue shr(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			long x = ExactInteger.ArgToInt64(args, 0, "shr");
			int n = ExactInteger.ArgToShiftCount(args, 1, "shr");
			return LuaInt64.NewDynValue(unchecked((long)ExactInteger.ShiftRight((ulong)x, n)));
		}

		/// <summary>
		/// Arithmetic shift right: copies of the sign bit come in from the left.
		/// </summary>
		[MoonSharpModuleMethod]
		public static DynValue sar(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			long x = ExactInteger.ArgToInt64(args, 0, "sar");
			int n = ExactInteger.ArgToShiftCount(args, 1, "sar");
			return LuaInt64.NewDynValue(ExactInteger.ArithmeticShiftRight(x, n));
		}
	}

	/// <summary>
	/// Class implementing the uint64 Lua functions: exact unsigned 64-bit integers.
	/// Not part of any preset; enable it with <see cref="CoreModules.Int64"/>.
	/// <c>uint64(v)</c> builds a value from a non-negative exact integer number, a decimal
	/// string, a uint64 or a non-negative int64.
	/// </summary>
	[MoonSharpModule(Namespace = "uint64")]
	public class UInt64Module
	{
		public static void MoonSharpInit(Table globalTable, Table uint64Table)
		{
			uint64Table.Set("max", LuaUInt64.NewDynValue(ulong.MaxValue));

			Table meta = new Table(globalTable.OwnerScript);
			meta.Set("__call", CallbackFunction.NewInternal(call, "uint64"));
			uint64Table.MetaTable = meta;
		}

		private static DynValue call(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			// args[0] is the uint64 table itself.
			return Construct(args[1], "uint64");
		}

		internal static DynValue Construct(DynValue v, string funcName)
		{
			if (ExactInteger.AsUInt64(v) != null)
				return v;

			LuaInt64 i = ExactInteger.AsInt64(v);
			if (i != null)
			{
				if (i.Value < 0)
					throw ScriptRuntimeException.BadArgument(0, funcName, string.Format("int64 value {0} is out of the uint64 range", i));
				return LuaUInt64.NewDynValue((ulong)i.Value);
			}

			string error;

			if (v.Type == DataType.Number)
			{
				error = ExactInteger.WhyNotSafeInteger(v.Number);
				if (error == null && v.Number < 0)
					error = string.Format("number {0} is out of the uint64 range", ExactInteger.FormatNumber(v.Number));
				if (error == null)
					return LuaUInt64.NewDynValue((ulong)v.Number);
			}
			else if (v.Type == DataType.String)
			{
				ulong? parsed = ExactInteger.TryParseUInt64(v.String, out error);
				if (parsed.HasValue)
					return LuaUInt64.NewDynValue(parsed.Value);
			}
			else
			{
				error = string.Format("number, string, int64 or uint64 expected, got {0}", ExactInteger.TypeName(v));
			}

			throw ScriptRuntimeException.BadArgument(0, funcName, error);
		}

		[MoonSharpModuleMethod]
		public static DynValue @is(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return DynValue.NewBoolean(ExactInteger.AsUInt64(args[0]) != null);
		}

		[MoonSharpModuleMethod]
		public static DynValue tostring(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			ulong v = ExactInteger.ArgToUInt64(args, 0, "tostring");
			return DynValue.NewString(new LuaUInt64(v).ToString());
		}

		/// <summary>
		/// Returns the value as a number, or nil plus a message when the number would not be exact
		/// (above 2^53-1).
		/// </summary>
		[MoonSharpModuleMethod]
		public static DynValue tonumber(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			ulong v = ExactInteger.ArgToUInt64(args, 0, "tonumber");

			if (v > (ulong)ExactInteger.MaxSafeInteger)
				return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(string.Format(
					"uint64 value {0} has no exact number representation", v)));

			return DynValue.NewNumber(v);
		}

		[MoonSharpModuleMethod]
		public static DynValue eq(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return ExactIntegerFunctions.Eq(args, "eq");
		}

		[MoonSharpModuleMethod]
		public static DynValue band(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			ulong r = ExactInteger.ArgToUInt64(args, 0, "band");
			for (int i = 1; i < args.Count; i++)
				r &= ExactInteger.ArgToUInt64(args, i, "band");
			return LuaUInt64.NewDynValue(r);
		}

		[MoonSharpModuleMethod]
		public static DynValue bor(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			ulong r = ExactInteger.ArgToUInt64(args, 0, "bor");
			for (int i = 1; i < args.Count; i++)
				r |= ExactInteger.ArgToUInt64(args, i, "bor");
			return LuaUInt64.NewDynValue(r);
		}

		[MoonSharpModuleMethod]
		public static DynValue bxor(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			ulong r = ExactInteger.ArgToUInt64(args, 0, "bxor");
			for (int i = 1; i < args.Count; i++)
				r ^= ExactInteger.ArgToUInt64(args, i, "bxor");
			return LuaUInt64.NewDynValue(r);
		}

		[MoonSharpModuleMethod]
		public static DynValue bnot(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return LuaUInt64.NewDynValue(~ExactInteger.ArgToUInt64(args, 0, "bnot"));
		}

		[MoonSharpModuleMethod]
		public static DynValue shl(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			ulong x = ExactInteger.ArgToUInt64(args, 0, "shl");
			int n = ExactInteger.ArgToShiftCount(args, 1, "shl");
			return LuaUInt64.NewDynValue(ExactInteger.ShiftLeft(x, n));
		}

		[MoonSharpModuleMethod]
		public static DynValue shr(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			ulong x = ExactInteger.ArgToUInt64(args, 0, "shr");
			int n = ExactInteger.ArgToShiftCount(args, 1, "shr");
			return LuaUInt64.NewDynValue(ExactInteger.ShiftRight(x, n));
		}
	}

	internal static class ExactIntegerFunctions
	{
		/// <summary>
		/// Numeric equality across int64, uint64 and numbers, which == does not give
		/// (int64(5) == 5 is false, as for any userdata compared with a number).
		/// </summary>
		internal static DynValue Eq(CallbackArguments args, string funcName)
		{
			for (int i = 0; i < 2; i++)
			{
				DynValue v = args[i];
				if (v.Type != DataType.Number && !ExactInteger.IsExactInteger(v))
					throw ScriptRuntimeException.BadArgument(i, funcName,
						string.Format("number, int64 or uint64 expected, got {0}", ExactInteger.TypeName(v)));
			}

			int? c = ExactInteger.Compare(args[0], args[1]);
			return DynValue.NewBoolean(c.HasValue && c.Value == 0);
		}
	}
}
