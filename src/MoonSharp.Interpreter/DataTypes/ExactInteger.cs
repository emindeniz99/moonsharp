using System;
using System.Globalization;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Shared rules for the int64 and uint64 userdata: which numbers count as exact integers,
	/// parsing, mixed comparisons and Lua 5.3 style integer arithmetic.
	/// </summary>
	internal static class ExactInteger
	{
		/// <summary>
		/// 2^53-1, the largest integer n such that n and every integer below it have their own
		/// double. 2^53 itself is excluded: 2^53+1 rounds to it, so a double equal to 2^53 may
		/// already be a rounded value.
		/// </summary>
		internal const double MaxSafeInteger = 9007199254740991.0;

		private const double TwoPow63 = 9223372036854775808.0;
		private const double TwoPow64 = 18446744073709551616.0;

		internal static bool IsSafeInteger(double d)
		{
			return d >= -MaxSafeInteger && d <= MaxSafeInteger && Math.Floor(d) == d;
		}

		internal static string FormatNumber(double d)
		{
			return d.ToString("R", CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Returns why a number cannot be taken as an exact integer, or null if it can.
		/// </summary>
		internal static string WhyNotSafeInteger(double d)
		{
			if (Math.Floor(d) != d)
				return string.Format("number {0} has no integer representation", FormatNumber(d));
			if (!IsSafeInteger(d))
				return string.Format("number {0} is outside the exact integer range of a number (+/-(2^53-1)); pass it as a decimal string",
					FormatNumber(d));
			return null;
		}

		internal static LuaInt64 AsInt64(DynValue v)
		{
			return v.Type == DataType.UserData ? v.UserData.Object as LuaInt64 : null;
		}

		internal static LuaUInt64 AsUInt64(DynValue v)
		{
			return v.Type == DataType.UserData ? v.UserData.Object as LuaUInt64 : null;
		}

		internal static bool IsExactInteger(DynValue v)
		{
			if (v.Type != DataType.UserData)
				return false;
			object o = v.UserData.Object;
			return o is LuaInt64 || o is LuaUInt64;
		}

		/// <summary>
		/// Lua type name used in messages: int64 and uint64 instead of userdata.
		/// </summary>
		internal static string TypeName(DynValue v)
		{
			if (AsInt64(v) != null) return "int64";
			if (AsUInt64(v) != null) return "uint64";
			return v.Type.ToLuaTypeString();
		}

		/// <summary>
		/// Converts an operand of an int64 operation: an int64 or a number that is an exact
		/// integer. Returns null with a message otherwise.
		/// </summary>
		internal static long? TryOperandToInt64(DynValue v, out string error)
		{
			error = null;

			LuaInt64 i = AsInt64(v);
			if (i != null)
				return i.Value;

			if (v.Type == DataType.Number)
			{
				error = WhyNotSafeInteger(v.Number);
				if (error == null)
					return (long)v.Number;
				return null;
			}

			if (AsUInt64(v) != null)
				error = "cannot mix uint64 and int64 values; convert one with int64() or uint64()";
			else
				error = string.Format("int64 or number expected, got {0}", TypeName(v));

			return null;
		}

		/// <summary>
		/// Converts an operand of a uint64 operation: a uint64 or a non-negative number that is
		/// an exact integer. Returns null with a message otherwise.
		/// </summary>
		internal static ulong? TryOperandToUInt64(DynValue v, out string error)
		{
			error = null;

			LuaUInt64 u = AsUInt64(v);
			if (u != null)
				return u.Value;

			if (v.Type == DataType.Number)
			{
				error = WhyNotSafeInteger(v.Number);
				if (error == null && v.Number < 0)
					error = string.Format("number {0} is negative and has no uint64 representation", FormatNumber(v.Number));
				if (error == null)
					return (ulong)v.Number;
				return null;
			}

			if (AsInt64(v) != null)
				error = "cannot mix int64 and uint64 values; convert one with int64() or uint64()";
			else
				error = string.Format("uint64 or number expected, got {0}", TypeName(v));

			return null;
		}

		internal static long ArgToInt64(CallbackArguments args, int argNum, string funcName)
		{
			string error;
			long? r = TryOperandToInt64(args[argNum], out error);
			if (r.HasValue)
				return r.Value;
			throw ScriptRuntimeException.BadArgument(argNum, funcName, error);
		}

		internal static ulong ArgToUInt64(CallbackArguments args, int argNum, string funcName)
		{
			string error;
			ulong? r = TryOperandToUInt64(args[argNum], out error);
			if (r.HasValue)
				return r.Value;
			throw ScriptRuntimeException.BadArgument(argNum, funcName, error);
		}

		/// <summary>
		/// Reads a shift count: a number that is an exact integer, an int64 or a uint64.
		/// Counts beyond +/-64 are clamped, since they all shift every bit out.
		/// </summary>
		internal static int ArgToShiftCount(CallbackArguments args, int argNum, string funcName)
		{
			DynValue v = args[argNum];
			LuaUInt64 u = AsUInt64(v);
			if (u != null)
				return u.Value > 64 ? 64 : (int)u.Value;

			long n = ArgToInt64(args, argNum, funcName);
			if (n > 64) return 64;
			if (n < -64) return -64;
			return (int)n;
		}

		/// <summary>
		/// Parses a decimal integer: optional surrounding whitespace, an optional sign, digits.
		/// Returns null with a message if the text is not such an integer or is out of range.
		/// </summary>
		internal static long? TryParseInt64(string s, out string error)
		{
			long v;
			if (long.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
				CultureInfo.InvariantCulture, out v))
			{
				error = null;
				return v;
			}

			error = IsDecimalInteger(s)
				? string.Format("'{0}' is out of the int64 range", s)
				: string.Format("'{0}' is not a decimal integer", s);
			return null;
		}

		/// <summary>
		/// Parses a decimal integer for uint64. See <see cref="TryParseInt64"/>.
		/// </summary>
		internal static ulong? TryParseUInt64(string s, out string error)
		{
			ulong v;
			if (ulong.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
				CultureInfo.InvariantCulture, out v))
			{
				error = null;
				return v;
			}

			error = IsDecimalInteger(s)
				? string.Format("'{0}' is out of the uint64 range", s)
				: string.Format("'{0}' is not a decimal integer", s);
			return null;
		}

		private static bool IsDecimalInteger(string s)
		{
			string t = s.Trim();
			int i = 0;
			if (t.Length > 0 && (t[0] == '-' || t[0] == '+'))
				i = 1;
			if (i >= t.Length)
				return false;
			for (; i < t.Length; i++)
				if (t[i] < '0' || t[i] > '9')
					return false;
			return true;
		}

		/// <summary>
		/// Compares two values mathematically, where each is an int64, a uint64 or a number.
		/// Numbers need not be integers here: the result is a boolean, so nothing is rounded.
		/// Returns null when either side is NaN or is not one of those types.
		/// </summary>
		internal static int? Compare(DynValue a, DynValue b)
		{
			LuaInt64 ai = AsInt64(a);
			LuaUInt64 au = AsUInt64(a);

			if (ai != null) return CompareInt64(ai.Value, b);
			if (au != null) return CompareUInt64(au.Value, b);

			if (a.Type == DataType.Number)
			{
				int? c = null;
				LuaInt64 bi = AsInt64(b);
				LuaUInt64 bu = AsUInt64(b);

				if (bi != null) c = CompareInt64ToDouble(bi.Value, a.Number);
				else if (bu != null) c = CompareUInt64ToDouble(bu.Value, a.Number);
				else if (b.Type == DataType.Number && !double.IsNaN(a.Number) && !double.IsNaN(b.Number))
					return a.Number.CompareTo(b.Number);

				return c.HasValue ? -c.Value : (int?)null;
			}

			return null;
		}

		private static int? CompareInt64(long x, DynValue b)
		{
			LuaInt64 bi = AsInt64(b);
			if (bi != null) return x.CompareTo(bi.Value);

			LuaUInt64 bu = AsUInt64(b);
			if (bu != null) return x < 0 ? -1 : ((ulong)x).CompareTo(bu.Value);

			if (b.Type == DataType.Number) return CompareInt64ToDouble(x, b.Number);

			return null;
		}

		private static int? CompareUInt64(ulong x, DynValue b)
		{
			LuaUInt64 bu = AsUInt64(b);
			if (bu != null) return x.CompareTo(bu.Value);

			LuaInt64 bi = AsInt64(b);
			if (bi != null) return bi.Value < 0 ? 1 : x.CompareTo((ulong)bi.Value);

			if (b.Type == DataType.Number) return CompareUInt64ToDouble(x, b.Number);

			return null;
		}

		private static int? CompareInt64ToDouble(long x, double d)
		{
			if (double.IsNaN(d)) return null;
			if (d >= TwoPow63) return -1;
			if (d < -TwoPow63) return 1;

			// -2^63 <= floor(d) < 2^63, so the cast is exact.
			double f = Math.Floor(d);
			long fl = (long)f;
			if (x < fl) return -1;
			if (x > fl) return 1;
			return f == d ? 0 : -1;
		}

		private static int? CompareUInt64ToDouble(ulong x, double d)
		{
			if (double.IsNaN(d)) return null;
			if (d < 0) return 1;
			if (d >= TwoPow64) return -1;

			double f = Math.Floor(d);
			ulong fu = (ulong)f;
			if (x < fu) return -1;
			if (x > fu) return 1;
			return f == d ? 0 : -1;
		}

		/// <summary>
		/// Floor division, as Lua 5.3's // on integers: rounds toward minus infinity and wraps
		/// for minint // -1 instead of throwing.
		/// </summary>
		internal static long FloorDiv(long a, long b)
		{
			if (b == 0)
				throw new ScriptRuntimeException("attempt to perform integer division by zero");
			if (b == -1)
				return unchecked(-a);

			long q = a / b;
			if ((a % b != 0) && ((a ^ b) < 0))
				q--;
			return q;
		}

		/// <summary>
		/// Floor modulo, as Lua's %: the result has the sign of the divisor, so that
		/// a == FloorDiv(a, b) * b + FloorMod(a, b).
		/// </summary>
		internal static long FloorMod(long a, long b)
		{
			if (b == 0)
				throw new ScriptRuntimeException("attempt to perform 'n%0'");
			if (b == -1)
				return 0;

			long r = a % b;
			if (r != 0 && ((r ^ b) < 0))
				r += b;
			return r;
		}

		internal static ulong Div(ulong a, ulong b)
		{
			if (b == 0)
				throw new ScriptRuntimeException("attempt to perform integer division by zero");
			return a / b;
		}

		internal static ulong Mod(ulong a, ulong b)
		{
			if (b == 0)
				throw new ScriptRuntimeException("attempt to perform 'n%0'");
			return a % b;
		}

		/// <summary>
		/// Shift left; a negative count shifts right. Counts of 64 or more give 0, as in Lua 5.3
		/// (the CLR would use the count modulo 64 instead).
		/// </summary>
		internal static ulong ShiftLeft(ulong x, int n)
		{
			if (n < 0) return ShiftRight(x, -n);
			if (n >= 64) return 0;
			return x << n;
		}

		/// <summary>
		/// Logical shift right; a negative count shifts left. Counts of 64 or more give 0.
		/// </summary>
		internal static ulong ShiftRight(ulong x, int n)
		{
			if (n < 0) return ShiftLeft(x, -n);
			if (n >= 64) return 0;
			return x >> n;
		}

		/// <summary>
		/// Arithmetic shift right (copies the sign bit); a negative count shifts left.
		/// </summary>
		internal static long ArithmeticShiftRight(long x, int n)
		{
			if (n < 0) return unchecked((long)ShiftLeft((ulong)x, -n));
			if (n >= 64) return x < 0 ? -1 : 0;
			return x >> n;
		}
	}
}
