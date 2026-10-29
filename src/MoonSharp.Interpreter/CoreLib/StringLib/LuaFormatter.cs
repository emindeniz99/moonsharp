using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using MoonSharp.Interpreter.Execution.VM;

namespace MoonSharp.Interpreter.CoreLib.StringLib
{
	/// <summary>
	/// string.format as Lua 5.2.4's str_format, with the conversions of C's printf as glibc writes them, in one
	/// pass over the format string into a reused StringBuilder.
	/// Floating-point conversions write the exact value of the double rounded to the requested digits (halfway
	/// cases to even), as glibc does, instead of using double.ToString: .NET Framework and Mono (and so Unity)
	/// round a 15-digit text there, so "%.2f" of 1.005 was "1.01" and "%.0f" of 2.5 was "3" on those runtimes
	/// and "1.00" and "2" on .NET Core and in Lua. The text is now the same on every runtime.
	/// Not as in C: the sign of a NaN is read from the double, so "%f" of 0/0 is "-nan" where the processor makes
	/// negative NaNs (x86, as Lua 5.2 there) and "nan" where it makes positive ones (ARM64, as native Lua there).
	/// </summary>
	internal static class LuaFormatter
	{
		[ThreadStatic]
		static StringBuilder t_Builder;

		[ThreadStatic]
		static char[] t_Scratch;

		// The text of one number conversion, and the decimal digits it is made from.
		[ThreadStatic]
		static StringBuilder t_Body;

		[ThreadStatic]
		static char[] t_Digits;

		static readonly char[] s_Nan = { 'n', 'a', 'n' }, s_NanUpper = { 'N', 'A', 'N' };
		static readonly char[] s_Inf = { 'i', 'n', 'f' }, s_InfUpper = { 'I', 'N', 'F' };

		const double TwoPow63 = 9223372036854775808.0;
		const double TwoPow64 = 18446744073709551616.0;

		static readonly ulong[] s_Pow10 =
		{
			1UL, 10UL, 100UL, 1000UL, 10000UL, 100000UL, 1000000UL, 10000000UL, 100000000UL, 1000000000UL,
			10000000000UL, 100000000000UL, 1000000000000UL, 10000000000000UL, 100000000000000UL,
			1000000000000000UL, 10000000000000000UL, 100000000000000000UL, 1000000000000000000UL,
			10000000000000000000UL,
		};

		struct Spec
		{
			public bool Left, Plus, Space, Alternate, Zero;
			public int Width;
			// -1 when the specifier has no '.'.
			public int Precision;
		}

		internal static DynValue Format(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			string fmt = CheckString(args, 0);

			StringBuilder sb = t_Builder;
			if (sb == null)
				t_Builder = sb = new StringBuilder(128);
			sb.Length = 0;

			FormatInto(executionContext, sb, fmt, args);

			string result = sb.ToString();

			if (sb.Capacity > 4096)
				t_Builder = null;

			return DynValue.NewString(result);
		}

		// Numbers and strings are read without creating a DynValue; anything else converts or fails as before.
		static double CheckNumber(CallbackArguments args, int arg)
		{
			LuaValue v = args.GetLuaValue(arg);
			return v.Type == DataType.Number ? v.Number : args[arg].CheckType("format", DataType.Number, arg, TypeValidationFlags.AutoConvert).Number;
		}

		static string CheckString(CallbackArguments args, int arg)
		{
			LuaValue v = args.GetLuaValue(arg);
			return v.Type == DataType.String ? (string)v.Object : args[arg].CheckType("format", DataType.String, arg, TypeValidationFlags.AutoConvert).String;
		}

		static bool IsDigit(char c)
		{
			return c >= '0' && c <= '9';
		}

		static void FormatInto(ScriptExecutionContext executionContext, StringBuilder sb, string fmt, CallbackArguments args)
		{
			int n = fmt.Length;
			int i = 0;
			int arg = 0;
			int argCount = args.Count;

			while (i < n)
			{
				int pct = fmt.IndexOf('%', i);
				if (pct < 0)
				{
					sb.Append(fmt, i, n - i);
					break;
				}

				if (pct > i)
					sb.Append(fmt, i, pct - i);

				i = pct + 1;

				if (i < n && fmt[i] == '%')
				{
					sb.Append('%');
					i++;
					continue;
				}

				if (++arg >= argCount)
					throw ScriptRuntimeException.BadArgument(arg, "format", "no value");

				// Lua 5.2's scanformat: flags, up to two width digits, '.', up to two precision digits.
				Spec spec = new Spec { Precision = -1 };
				int flagStart = i;

				for (; i < n; i++)
				{
					char f = fmt[i];
					if (f == '-') spec.Left = true;
					else if (f == '+') spec.Plus = true;
					else if (f == ' ') spec.Space = true;
					else if (f == '#') spec.Alternate = true;
					else if (f == '0') spec.Zero = true;
					else break;
				}

				if (i - flagStart >= 6)
					throw new ScriptRuntimeException("invalid format (repeated flags)");

				for (int d = 0; d < 2 && i < n && IsDigit(fmt[i]); d++, i++)
					spec.Width = spec.Width * 10 + (fmt[i] - '0');

				if (i < n && fmt[i] == '.')
				{
					i++;
					spec.Precision = 0;
					for (int d = 0; d < 2 && i < n && IsDigit(fmt[i]); d++, i++)
						spec.Precision = spec.Precision * 10 + (fmt[i] - '0');
				}

				if (i < n && IsDigit(fmt[i]))
					throw new ScriptRuntimeException("invalid format (width or precision too long)");

				// The format string ends in a '\0' for this scan, as in C.
				char ch = i < n ? fmt[i] : '\0';
				i++;

				switch (ch)
				{
					case 'c':
						AppendPadded(sb, ToChar(CheckNumber(args, arg)), spec);
						break;
					case 'd':
					case 'i':
						{
							double d = CheckNumber(args, arg);
							if (!(d >= -TwoPow63 && d < TwoPow63))
								throw ScriptRuntimeException.BadArgument(arg, "format", "not a number in proper range");
							long l = (long)d;
							AppendInteger(sb, l < 0 ? (ulong)(-(l + 1)) + 1 : (ulong)l, l < 0, ch, spec);
							break;
						}
					case 'o':
					case 'u':
					case 'x':
					case 'X':
						{
							double d = CheckNumber(args, arg);
							// (-1, 2^64): what Lua 5.2's cast to an unsigned 64-bit integer keeps within one of the value.
							if (!(d > -1 && d < TwoPow64))
								throw ScriptRuntimeException.BadArgument(arg, "format", "not a non-negative number in proper range");
							ulong u = d < TwoPow63 ? (ulong)(long)d : (ulong)(long)(d - TwoPow63) | 0x8000000000000000UL;
							AppendInteger(sb, u, false, ch, spec);
							break;
						}
					case 'e':
					case 'E':
					case 'f':
					case 'g':
					case 'G':
					case 'a':
					case 'A':
						AppendFloat(sb, CheckNumber(args, arg), ch, spec);
						break;
					case 'q':
						AppendQuoted(sb, CheckString(args, arg));
						break;
					case 's':
						AppendStringArg(sb, ToLuaString(executionContext, args, arg), spec);
						break;
					default:
						throw new ScriptRuntimeException("invalid option '%" + ch + "' to 'format'");
				}
			}
		}

		/// <summary>
		/// %c: the number truncated to an integer. 0 to 65535 is that UTF-16 character, as before (Lua 5.2 keeps
		/// the low byte, which is the same for 0 to 255); anything else gives its low byte, as Lua 5.2 does
		/// (luaL_checkint, then C's unsigned char): -1 is 255, and a number outside the 64-bit range gives 0.
		/// </summary>
		static char ToChar(double d)
		{
			long l = d >= -TwoPow63 && d < TwoPow63 ? (long)d : 0;
			return l >= 0 && l <= 0xFFFF ? (char)l : (char)(l & 0xFF);
		}

		/// <summary>One character padded with spaces to the width ('0' pads with spaces too, as glibc does).</summary>
		static void AppendPadded(StringBuilder sb, char c, Spec spec)
		{
			int pad = spec.Width - 1;
			if (!spec.Left && pad > 0)
				sb.Append(' ', pad);
			sb.Append(c);
			if (spec.Left && pad > 0)
				sb.Append(' ', pad);
		}

		/// <summary>
		/// %d %i %o %u %x %X: the precision is the minimum number of digits (0 with a precision of 0 has none),
		/// and with a precision '0' does not pad; '#' adds "0x" to a non-zero hexadecimal number and a leading 0 to
		/// an octal one; '+' and ' ' sign the signed conversions only.
		/// </summary>
		static void AppendInteger(StringBuilder sb, ulong magnitude, bool negative, char conv, Spec spec)
		{
			char[] buf = Scratch(24 + Math.Max(spec.Precision, 0));
			int p = buf.Length;
			int radix = conv == 'o' ? 8 : (conv == 'x' || conv == 'X') ? 16 : 10;
			string digits = conv == 'X' ? "0123456789ABCDEF" : "0123456789abcdef";

			if (!(magnitude == 0 && spec.Precision == 0))
			{
				ulong v = magnitude;
				do
				{
					buf[--p] = digits[(int)(v % (ulong)radix)];
					v /= (ulong)radix;
				}
				while (v != 0);
			}

			while (buf.Length - p < spec.Precision)
				buf[--p] = '0';

			if (conv == 'o' && spec.Alternate && (p == buf.Length || buf[p] != '0'))
				buf[--p] = '0';

			string prefix = "";
			if (conv == 'd' || conv == 'i')
				prefix = negative ? "-" : spec.Plus ? "+" : spec.Space ? " " : "";
			else if (spec.Alternate && magnitude != 0 && radix == 16)
				prefix = conv == 'X' ? "0X" : "0x";

			AppendField(sb, prefix, buf, p, buf.Length - p, spec, spec.Zero && spec.Precision < 0);
		}

		/// <summary>
		/// Writes sign or prefix and body padded to the width: spaces before them, zeros between them
		/// (<paramref name="zeroPad"/>, not with '-'), or spaces after them with '-'.
		/// </summary>
		static void AppendField(StringBuilder sb, string prefix, char[] body, int start, int length, Spec spec, bool zeroPad)
		{
			int pad = spec.Width - prefix.Length - length;

			if (pad > 0 && !spec.Left && !zeroPad)
				sb.Append(' ', pad);
			sb.Append(prefix);
			if (pad > 0 && !spec.Left && zeroPad)
				sb.Append('0', pad);
			sb.Append(body, start, length);
			if (pad > 0 && spec.Left)
				sb.Append(' ', pad);
		}

		static char[] Scratch(int length)
		{
			char[] buf = t_Scratch;
			if (buf == null || buf.Length < length)
				t_Scratch = buf = new char[Math.Max(length, 128)];
			return buf;
		}

		// --------------------------------------------------------------------------------------------------------
		// Floating point
		// --------------------------------------------------------------------------------------------------------

		static void AppendFloat(StringBuilder sb, double d, char conv, Spec spec)
		{
			bool upper = conv == 'E' || conv == 'G' || conv == 'A';
			bool negative = BitConverter.DoubleToInt64Bits(d) < 0;
			string sign = negative ? "-" : spec.Plus ? "+" : spec.Space ? " " : "";

			if (double.IsNaN(d) || double.IsInfinity(d))
			{
				char[] text = double.IsNaN(d) ? (upper ? s_NanUpper : s_Nan) : (upper ? s_InfUpper : s_Inf);
				AppendField(sb, sign, text, 0, text.Length, spec, false);
				return;
			}

			double ad = Math.Abs(d);
			StringBuilder body = t_Body;
			if (body == null)
				t_Body = body = new StringBuilder(64);
			body.Length = 0;
			string prefix = sign;

			switch (conv)
			{
				case 'f':
					AppendFixed(body, ad, spec.Precision < 0 ? 6 : spec.Precision, spec.Alternate);
					break;
				case 'e':
				case 'E':
					AppendExponential(body, ad, spec.Precision < 0 ? 6 : spec.Precision, spec.Alternate, conv);
					break;
				case 'g':
				case 'G':
					AppendGeneral(body, ad, spec.Precision < 0 ? 6 : spec.Precision == 0 ? 1 : spec.Precision, spec.Alternate, upper);
					break;
				default:
					prefix = sign + (upper ? "0X" : "0x");
					AppendHexFloat(body, ad, spec.Precision, spec.Alternate, upper);
					break;
			}

			char[] buf = Scratch(body.Length);
			body.CopyTo(0, buf, 0, body.Length);
			AppendField(sb, prefix, buf, 0, body.Length, spec, spec.Zero);
		}

		/// <summary>%f: <paramref name="ad"/> rounded to <paramref name="precision"/> decimals.</summary>
		static void AppendFixed(StringBuilder sb, double ad, int precision, bool alternate)
		{
			AppendWithPoint(sb, RoundedDigits(ad, -precision), precision, alternate);
		}

		// The <count> digits of round(ad * 10^decimals) in t_Digits, with a decimal point before the last
		// <decimals> of them.
		static void AppendWithPoint(StringBuilder sb, int count, int decimals, bool alternate)
		{
			char[] digits = t_Digits;

			if (count <= decimals)
			{
				sb.Append('0');
				if (decimals > 0 || alternate)
					sb.Append('.');
				sb.Append('0', decimals - count);
				sb.Append(digits, 0, count);
				return;
			}

			sb.Append(digits, 0, count - decimals);
			if (decimals > 0 || alternate)
				sb.Append('.');
			sb.Append(digits, count - decimals, decimals);
		}

		/// <summary>
		/// The digits of <paramref name="ad"/> (positive or zero) rounded to <paramref name="precision"/> + 1
		/// significant digits, into t_Digits, and the decimal exponent of the first one.
		/// </summary>
		static void SignificantDigits(double ad, int precision, out int exponent)
		{
			if (ad == 0)
			{
				exponent = 0;
				char[] zeros = DigitBuffer(precision + 1);
				for (int k = 0; k <= precision; k++)
					zeros[k] = '0';
				return;
			}

			// The logarithm is only an estimate; the digit count of the rounded result settles it.
			exponent = (int)Math.Floor(Math.Log10(ad));

			for (int attempt = 0; ; attempt++)
			{
				int count = RoundedDigits(ad, exponent - precision);

				if (count == precision + 1 || attempt == 4)
				{
					// 10^precision can also come from an exponent estimated one too high (1e-7 is just below
					// 10^-7): one exponent lower, the digits fit unless they round up to a power of ten there too.
					bool powerOfTen = IsPowerOfTen(count);

					if (powerOfTen && RoundedDigits(ad, exponent - 1 - precision) == precision + 1)
					{
						exponent--;
						return;
					}

					// Write the power of ten back (the check above overwrote it).
					if (powerOfTen)
						RoundedDigits(ad, exponent - precision);

					return;
				}

				exponent += count > precision + 1 ? 1 : -1;
			}
		}

		static bool IsPowerOfTen(int count)
		{
			char[] digits = t_Digits;
			if (digits[0] != '1')
				return false;
			for (int k = 1; k < count; k++)
				if (digits[k] != '0')
					return false;
			return true;
		}

		/// <summary>%e: d.ddde+XX, at least two exponent digits.</summary>
		static void AppendExponential(StringBuilder sb, double ad, int precision, bool alternate, char e)
		{
			int exponent;
			SignificantDigits(ad, precision, out exponent);

			sb.Append(t_Digits[0]);
			if (precision > 0 || alternate)
				sb.Append('.');
			sb.Append(t_Digits, 1, precision);
			AppendExponent(sb, e, exponent);
		}

		static void AppendExponent(StringBuilder sb, char e, int exponent)
		{
			sb.Append(e).Append(exponent < 0 ? '-' : '+');
			exponent = Math.Abs(exponent);
			if (exponent >= 100)
				sb.Append((char)('0' + exponent / 100));
			sb.Append((char)('0' + exponent / 10 % 10)).Append((char)('0' + exponent % 10));
		}

		/// <summary>
		/// %g: %e when the exponent X of the %e form is below -4 or not below the precision P, otherwise %f with
		/// P - 1 - X decimals; trailing zeros (and a trailing point) go unless '#'.
		/// </summary>
		static void AppendGeneral(StringBuilder sb, double ad, int precision, bool alternate, bool upper)
		{
			int exponent;
			SignificantDigits(ad, precision - 1, out exponent);
			int start = sb.Length;

			if (exponent < -4 || exponent >= precision)
			{
				sb.Append(t_Digits[0]);
				if (precision > 1 || alternate)
					sb.Append('.');
				sb.Append(t_Digits, 1, precision - 1);
				if (!alternate)
					TrimZeros(sb, start);
				AppendExponent(sb, upper ? 'E' : 'e', exponent);
			}
			else
			{
				// The same digits as the %e form: precision significant digits.
				AppendWithPoint(sb, precision, precision - 1 - exponent, alternate);
				if (!alternate)
					TrimZeros(sb, start);
			}
		}

		static void TrimZeros(StringBuilder sb, int start)
		{
			int point = -1;
			for (int k = start; k < sb.Length; k++)
				if (sb[k] == '.')
					point = k;

			if (point < 0)
				return;

			int end = sb.Length;
			while (end > point + 1 && sb[end - 1] == '0')
				end--;
			if (end == point + 1)
				end = point;
			sb.Length = end;
		}

		/// <summary>
		/// %a: 0x1.hhhp+d for normal numbers, 0x0.hhhp-1022 for subnormals; without a precision as many hex digits
		/// as the value needs, with one rounded half to even (the leading digit can become 2, as in glibc).
		/// </summary>
		static void AppendHexFloat(StringBuilder sb, double ad, int precision, bool alternate, bool upper)
		{
			long bits = BitConverter.DoubleToInt64Bits(ad);
			int biased = (int)(bits >> 52) & 0x7FF;
			ulong fraction = (ulong)bits & 0xFFFFFFFFFFFFFUL;
			ulong lead = biased == 0 ? 0UL : 1UL;
			int exponent = ad == 0 ? 0 : biased == 0 ? -1022 : biased - 1023;
			int digits = 13;

			if (precision < 0)
			{
				while (digits > 0 && (fraction & 0xF) == 0)
				{
					fraction >>= 4;
					digits--;
				}
			}
			else if (precision < 13)
			{
				int dropped = (13 - precision) * 4;
				ulong rest = fraction & ((1UL << dropped) - 1);
				ulong half = 1UL << (dropped - 1);
				fraction >>= dropped;
				digits = precision;

				if (rest > half || (rest == half && (((digits == 0 ? lead : fraction) & 1) == 1)))
				{
					fraction++;
					if (fraction >> (digits * 4) != 0)
					{
						fraction &= (1UL << (digits * 4)) - 1;
						lead++;
					}
				}
			}

			string hex = upper ? "0123456789ABCDEF" : "0123456789abcdef";
			sb.Append(hex[(int)lead]);

			if (digits > 0 || precision > 0 || alternate)
				sb.Append('.');

			for (int k = digits - 1; k >= 0; k--)
				sb.Append(hex[(int)((fraction >> (k * 4)) & 0xF)]);

			for (int k = digits; k < precision; k++)
				sb.Append('0');

			sb.Append(upper ? 'P' : 'p').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString(CultureInfo.InvariantCulture));
		}

		static char[] DigitBuffer(int length)
		{
			char[] buf = t_Digits;
			if (buf == null || buf.Length < length)
				t_Digits = buf = new char[Math.Max(length, 64)];
			return buf;
		}

		/// <summary>
		/// Writes the decimal digits of round(<paramref name="ad"/> / 10^<paramref name="k"/>), the exact value of
		/// the double rounded half to even ("0" for zero), into t_Digits and returns their count. A 128-bit path
		/// covers the common cases (up to 19 decimals of a number below about 10^19, and integers below 2^64);
		/// BigInteger does the rest.
		/// </summary>
		static int RoundedDigits(double ad, int k)
		{
			long bits = BitConverter.DoubleToInt64Bits(ad);
			int biased = (int)(bits >> 52) & 0x7FF;
			ulong m = (ulong)bits & 0xFFFFFFFFFFFFFUL;
			int e;

			if (biased == 0)
			{
				e = -1074;
			}
			else
			{
				m |= 1UL << 52;
				e = biased - 1075;
			}

			if (m == 0)
				return WriteDigits(0);

			while ((m & 1) == 0)
			{
				m >>= 1;
				e++;
			}

			ulong q;
			if (TryRoundSmall(m, e, k, out q))
				return WriteDigits(q);

			// ad = m * 2^e: num / den = ad / 10^k.
			BigInteger num = new BigInteger(m), den = BigInteger.One;

			if (e >= 0) num <<= e;
			else den <<= -e;

			if (k >= 0) den *= BigInteger.Pow(10, k);
			else num *= BigInteger.Pow(10, -k);

			BigInteger rem;
			BigInteger quot = BigInteger.DivRem(num, den, out rem);
			int c = (rem << 1).CompareTo(den);

			if (c > 0 || (c == 0 && !quot.IsEven))
				quot += BigInteger.One;

			string text = quot.ToString(CultureInfo.InvariantCulture);
			text.CopyTo(0, DigitBuffer(text.Length), 0, text.Length);
			return text.Length;
		}

		static int WriteDigits(ulong v)
		{
			int count = 1;
			for (ulong t = v; t >= 10; t /= 10)
				count++;

			char[] buf = DigitBuffer(count);
			for (int k = count - 1; k >= 0; k--)
			{
				buf[k] = (char)('0' + (int)(v % 10));
				v /= 10;
			}

			return count;
		}

		static bool TryRoundSmall(ulong m, int e, int k, out ulong q)
		{
			q = 0;

			if (k > 0)
			{
				// An integer below 2^64 divided by 10^k.
				if (e < 0 || e > 11 || k >= s_Pow10.Length)
					return false;
				ulong n = m << e;
				if ((n >> e) != m)
					return false;
				ulong d = s_Pow10[k];
				q = n / d;
				ulong r = n - q * d;
				if (r > d - r || (r == d - r && (q & 1) == 1))
					q++;
				return true;
			}

			if (-k >= s_Pow10.Length)
				return false;

			// m * 10^-k as 128 bits; m < 2^53 and 10^19 < 2^64, so it fits.
			ulong hi, lo;
			Multiply(m, s_Pow10[-k], out hi, out lo);

			if (e >= 0)
			{
				if (hi != 0 || e >= 64 || (e > 0 && (lo >> (64 - e)) != 0))
					return false;
				q = lo << e;
				return true;
			}

			int s = -e;
			int cmp;

			if (s >= 128)
			{
				// Below 2^117 against half of 2^128 or more.
				return true;
			}
			else if (s < 64)
			{
				if ((hi >> s) != 0)
					return false;
				q = (lo >> s) | (hi << (64 - s));
				ulong rest = lo & ((1UL << s) - 1);
				cmp = rest.CompareTo(1UL << (s - 1));
			}
			else if (s == 64)
			{
				q = hi;
				cmp = lo.CompareTo(1UL << 63);
			}
			else
			{
				int t = s - 64;
				q = hi >> t;
				ulong restHi = hi & ((1UL << t) - 1);
				ulong halfHi = 1UL << (t - 1);
				cmp = restHi != halfHi ? restHi.CompareTo(halfHi) : (lo == 0 ? 0 : 1);
			}

			if (cmp > 0 || (cmp == 0 && (q & 1) == 1))
			{
				if (q == ulong.MaxValue)
					return false;
				q++;
			}

			return true;
		}

		static void Multiply(ulong a, ulong b, out ulong hi, out ulong lo)
		{
			ulong aLo = (uint)a, aHi = a >> 32, bLo = (uint)b, bHi = b >> 32;
			ulong ll = aLo * bLo, lh = aLo * bHi, hl = aHi * bLo, hh = aHi * bHi;
			ulong mid = (ll >> 32) + (uint)lh + (uint)hl;
			lo = (mid << 32) | (uint)ll;
			hi = hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
		}

		// --------------------------------------------------------------------------------------------------------
		// Strings
		// --------------------------------------------------------------------------------------------------------

		/// <summary>%s takes any value, as Lua 5.2's luaL_tolstring: its __tostring, or tostring's text.</summary>
		static string ToLuaString(ScriptExecutionContext executionContext, CallbackArguments args, int arg)
		{
			LuaValue v = args.GetLuaValue(arg);
			if (v.Type == DataType.String)
				return (string)v.Object;

			DynValue value = args[arg];
			DynValue meta = executionContext.GetMetamethod(value, "__tostring");

			if (meta == null || meta.IsNil())
				return value.ToPrintString();

			DynValue r = executionContext.Call(meta, value).ToScalar();

			if (r.Type != DataType.String && r.Type != DataType.Number)
				throw new ScriptRuntimeException("'__tostring' must return a string");

			return r.ToPrintString();
		}

		/// <summary>
		/// %s: a string of 100 characters or more without a precision goes in whole; otherwise it ends at its
		/// first '\0' (C's sprintf sees a C string), the precision cuts it, and spaces pad it to the width.
		/// </summary>
		static void AppendStringArg(StringBuilder sb, string s, Spec spec)
		{
			if (spec.Precision < 0 && s.Length >= 100)
			{
				sb.Append(s);
				return;
			}

			int len = s.IndexOf('\0');
			if (len < 0)
				len = s.Length;
			if (spec.Precision >= 0 && spec.Precision < len)
				len = spec.Precision;

			int pad = spec.Width - len;
			if (!spec.Left && pad > 0)
				sb.Append(' ', pad);
			sb.Append(s, 0, len);
			if (spec.Left && pad > 0)
				sb.Append(' ', pad);
		}

		/// <summary>
		/// %q as Lua 5.2's addquoted: '"', '\\' and a newline get a backslash, other control characters (0-31, 127)
		/// become \ddd (three digits when a digit follows), and flags, width and precision are ignored.
		/// </summary>
		static void AppendQuoted(StringBuilder sb, string s)
		{
			sb.Append('"');
			for (int k = 0; k < s.Length; k++)
			{
				char c = s[k];
				if (c == '"' || c == '\\' || c == '\n')
				{
					sb.Append('\\').Append(c);
				}
				else if (c < 32 || c == 127)
				{
					sb.Append('\\');
					if (k + 1 < s.Length && IsDigit(s[k + 1]))
						sb.Append(((int)c).ToString("000", CultureInfo.InvariantCulture));
					else
						sb.Append(((int)c).ToString(CultureInfo.InvariantCulture));
				}
				else
				{
					sb.Append(c);
				}
			}
			sb.Append('"');
		}
	}
}
