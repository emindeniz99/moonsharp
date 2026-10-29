// Disable warnings about XML documentation
#pragma warning disable 1591

using System;

namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing bit32 Lua functions 
	/// </summary>
	[MoonSharpModule(Namespace = "bit32")]
	public class Bit32Module
	{
		const double TwoPow32 = 4294967296.0;
		const double TwoPow63 = 9223372036854775808.0;

		/// <summary>
		/// luaL_checkunsigned as Lua 5.2 does it on IEEE machines: rounded to the nearest integer (halves to even)
		/// and taken modulo 2^32, so -1 is 0xFFFFFFFF and 1.5 is 2. A double outside the uint range is never
		/// cast directly: .NET 9 and ARM64 saturate such casts to 0, .NET 8 on x64 wraps them. NaN and the
		/// infinities give 0, as Lua 5.2 on x86-64.
		/// </summary>
		static uint CheckUnsigned(CallbackArguments args, int argNum, string funcName)
		{
			double d = args.AsType(argNum, funcName, DataType.Number, false).Number;

			if (double.IsNaN(d) || double.IsInfinity(d))
				return 0;

			double r = Math.Round(d, MidpointRounding.ToEven);
			// Exact: r is an integer and the result lies in [0, 2^32).
			return (uint)(r - Math.Floor(r / TwoPow32) * TwoPow32);
		}

		/// <summary>
		/// luaL_checkint as Lua 5.2 does it on x86-64: truncated to a 64-bit integer, then its low 32 bits; a
		/// number outside the 64-bit range (or NaN) gives 0.
		/// </summary>
		static int CheckInt(CallbackArguments args, int argNum, string funcName)
		{
			return ToInt(args.AsType(argNum, funcName, DataType.Number, false).Number);
		}

		static int ToInt(double d)
		{
			return d >= -TwoPow63 && d < TwoPow63 ? unchecked((int)(long)d) : 0;
		}

		/// <summary>The low <paramref name="width"/> bits set, for 1 to 32.</summary>
		static uint Mask(int width)
		{
			return (uint)((1UL << width) - 1);
		}

		static uint Fold(CallbackArguments args, string funcName, uint start, Func<uint, uint, uint> op)
		{
			uint r = start;

			for (int i = 0; i < args.Count; i++)
				r = op(r, CheckUnsigned(args, i, funcName));

			return r;
		}

		/// <summary>Lua 5.2's fieldargs: a field position and width (1 by default) that fit in 32 bits.</summary>
		static int FieldArgs(CallbackArguments args, int fieldArg, string funcName, out int width)
		{
			int f = CheckInt(args, fieldArg, funcName);
			DynValue w = args[fieldArg + 1];
			width = w.IsNil() ? 1 : CheckInt(args, fieldArg + 1, funcName);

			if (f < 0)
				throw ScriptRuntimeException.BadArgument(fieldArg, funcName, "field cannot be negative");

			if (width <= 0)
				throw ScriptRuntimeException.BadArgument(fieldArg + 1, funcName, "width must be positive");

			if ((long)f + width > 32)
				throw new ScriptRuntimeException("trying to access non-existent bits");

			return f;
		}

		/// <summary>Lua 5.2's b_shift: left for a positive count, right for a negative one, 0 from 32 bits on.</summary>
		static DynValue Shift(uint r, long i)
		{
			if (i < 0)
				r = -i >= 32 ? 0 : r >> (int)-i;
			else
				r = i >= 32 ? 0 : r << (int)i;

			return DynValue.NewNumber(r);
		}

		static DynValue Rotate(uint r, int i)
		{
			i &= 31;

			if (i != 0)
				r = (r << i) | (r >> (32 - i));

			return DynValue.NewNumber(r);
		}


		[MoonSharpModuleMethod]
		public static DynValue extract(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			uint r = CheckUnsigned(args, 0, "extract");
			int width;
			int f = FieldArgs(args, 1, "extract", out width);

			return DynValue.NewNumber((r >> f) & Mask(width));
		}


		[MoonSharpModuleMethod]
		public static DynValue replace(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			uint r = CheckUnsigned(args, 0, "replace");
			uint v = CheckUnsigned(args, 1, "replace");
			int width;
			int f = FieldArgs(args, 2, "replace", out width);
			uint m = Mask(width);

			return DynValue.NewNumber((r & ~(m << f)) | ((v & m) << f));
		}


		[MoonSharpModuleMethod]
		public static DynValue arshift(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			uint r = CheckUnsigned(args, 0, "arshift");
			int i = CheckInt(args, 1, "arshift");

			if (i < 0 || (r & 0x80000000u) == 0)
				return Shift(r, -(long)i);

			// The sign bit fills from the left; the result stays an unsigned number, as in Lua 5.2.
			return DynValue.NewNumber(i >= 32 ? 0xFFFFFFFFu : (r >> i) | ~(0xFFFFFFFFu >> i));
		}


		[MoonSharpModuleMethod]
		public static DynValue rshift(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return Shift(CheckUnsigned(args, 0, "rshift"), -(long)CheckInt(args, 1, "rshift"));
		}


		[MoonSharpModuleMethod]
		public static DynValue lshift(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return Shift(CheckUnsigned(args, 0, "lshift"), CheckInt(args, 1, "lshift"));
		}

		// With no arguments band is all ones and bor and bxor are 0, as in Lua 5.2.
		[MoonSharpModuleMethod]
		public static DynValue band(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return DynValue.NewNumber(Fold(args, "band", 0xFFFFFFFFu, (x, y) => x & y));
		}

		[MoonSharpModuleMethod]
		public static DynValue btest(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return DynValue.NewBoolean(0 != Fold(args, "btest", 0xFFFFFFFFu, (x, y) => x & y));
		}

		[MoonSharpModuleMethod]
		public static DynValue bor(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return DynValue.NewNumber(Fold(args, "bor", 0, (x, y) => x | y));
		}

		[MoonSharpModuleMethod]
		public static DynValue bnot(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return DynValue.NewNumber(~CheckUnsigned(args, 0, "bnot"));
		}

		[MoonSharpModuleMethod]
		public static DynValue bxor(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return DynValue.NewNumber(Fold(args, "bxor", 0, (x, y) => x ^ y));
		}

		[MoonSharpModuleMethod]
		public static DynValue lrotate(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return Rotate(CheckUnsigned(args, 0, "lrotate"), CheckInt(args, 1, "lrotate"));
		}

		[MoonSharpModuleMethod]
		public static DynValue rrotate(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return Rotate(CheckUnsigned(args, 0, "rrotate"), -CheckInt(args, 1, "rrotate"));
		}

	}
}
