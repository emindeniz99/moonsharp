using System;
using System.Globalization;
using System.Text;

namespace MoonSharp.Interpreter.CoreLib.StringLib
{
	/// <summary>
	/// string.format. It formats in one pass over the format string into a reused StringBuilder.
	/// It replaces a port of C's sprintf (KopiLua's str_format, which matched a regular expression per
	/// specifier and formatted with String.Format) and keeps that port's output byte for byte, quirks included:
	/// .NET number formats ("1.000000e+006"), precision ignored by %d, width ignored by %c, '0' flags only
	/// before other flags, a specifier the port could not parse printed literally, and the same errors and
	/// exceptions. The port is kept as a test oracle (KopiLuaFormatOracle in the tests).
	/// </summary>
	internal static class LuaFormatter
	{
		// The port copied each formatted item into a char[512]; a longer one overflowed it.
		const int MaxItem = 512;

		[ThreadStatic]
		static StringBuilder t_Builder;

		[ThreadStatic]
		static char[] t_Scratch;

		// Format strings for double.ToString, indexed by conversion and precision (0..99).
		static readonly string[][] s_FloatFormats = new string[128][];

		struct Spec
		{
			public bool HasWidth, Left, Plus, Space, Alternate, ZeroPad;
			public int Width;
		}

		internal static DynValue Format(CallbackArguments args)
		{
			string fmt = CheckString(args[0], 0);

			StringBuilder sb = t_Builder;
			if (sb == null)
				t_Builder = sb = new StringBuilder(128);
			sb.Length = 0;

			FormatInto(sb, fmt, args);

			string result = sb.ToString();

			if (sb.Capacity > 4096)
				t_Builder = null;

			return DynValue.NewString(result);
		}

		static double CheckNumber(DynValue v, int arg)
		{
			return v.Type == DataType.Number ? v.Number : v.CheckType("format", DataType.Number, arg, TypeValidationFlags.AutoConvert).Number;
		}

		static string CheckString(DynValue v, int arg)
		{
			return v.Type == DataType.String ? v.String : v.CheckType("format", DataType.String, arg, TypeValidationFlags.AutoConvert).String;
		}

		static bool IsFlag(char c)
		{
			return c == '-' || c == '+' || c == ' ' || c == '#' || c == '0';
		}

		// The C port's isdigit((byte)c) looked at the low byte only: U+0131 counts as '1'.
		static bool IsDigitByte(char c)
		{
			return (uint)((c & 0xFF) - '0') <= 9;
		}

		static void FormatInto(StringBuilder sb, string fmt, CallbackArguments args)
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

				// Flags, up to two width digits, '.', up to two precision digits. The string ends in a '\0'
				// for this scan, as in C.
				int flagStart = i;
				while (i < n && IsFlag(fmt[i]))
					i++;
				int flagEnd = i;
				if (flagEnd - flagStart >= 6)
					throw new ScriptRuntimeException("invalid format (repeated flags)");

				int widthStart = i;
				if (i < n && IsDigitByte(fmt[i])) i++;
				if (i < n && IsDigitByte(fmt[i])) i++;
				int widthEnd = i;

				int precisionStart = -1;
				if (i < n && fmt[i] == '.')
				{
					precisionStart = ++i;
					if (i < n && IsDigitByte(fmt[i])) i++;
					if (i < n && IsDigitByte(fmt[i])) i++;
				}
				int specEnd = i;

				if (i < n && IsDigitByte(fmt[i]))
					throw new ScriptRuntimeException("invalid format (width or precision too long)");

				char ch = i < n ? fmt[i] : '\0';
				i++;

				DynValue v = args[arg];
				double d = 0;
				string str = null;

				switch (ch)
				{
					case 'q':
						AppendQuoted(sb, CheckString(v, arg));
						continue;
					case 's':
						str = CheckString(v, arg);
						if (precisionStart < 0 && str.Length >= 100)
						{
							// Too long to format: the original value is kept, as in Lua 5.2.
							sb.Append(v.ToPrintString());
							continue;
						}
						break;
					case 'c':
					case 'd':
					case 'i':
					case 'o':
					case 'u':
					case 'x':
					case 'X':
					case 'e':
					case 'E':
					case 'f':
					case 'g':
					case 'G':
						d = CheckNumber(v, arg);
						break;
					default:
						throw new ScriptRuntimeException("invalid option '%" + ch + "' to 'format'", ch);
				}

				int itemStart = sb.Length;

				if (!TryParseSpec(fmt, flagStart, flagEnd, widthStart, widthEnd, precisionStart, specEnd, out Spec spec, out int precision))
				{
					// The port's parser rejected the specifier and printed it as it was given to it,
					// with C's 'l' length modifier for the integer conversions.
					sb.Append('%').Append(fmt, flagStart, specEnd - flagStart);
					if (ch == 'd' || ch == 'i' || ch == 'o' || ch == 'u' || ch == 'x' || ch == 'X')
						sb.Append('l');
					sb.Append(ch);
					continue;
				}

				switch (ch)
				{
					case 'c':
						// Convert.ToChar throws OverflowException outside 0..65535, as the port did; the width is ignored.
						char c = Convert.ToChar((int)d);
						if (c != '\0')
							sb.Append(c);
						break;
					case 'd':
					case 'i':
						{
							long l = (long)d;
							char[] buf = Scratch();
							int start = WriteDecimal(buf, l < 0 ? (ulong)(-(l + 1)) + 1 : (ulong)l);
							if (l < 0)
								buf[--start] = '-';
							AppendNumber(sb, buf, start, buf.Length - start, l >= 0, spec);
							break;
						}
					case 'u':
						{
							char[] buf = Scratch();
							int start = WriteDecimal(buf, (ulong)d);
							spec.Plus = spec.Space = false;
							AppendNumber(sb, buf, start, buf.Length - start, true, spec);
							break;
						}
					case 'x':
					case 'X':
						AppendHex(sb, (ulong)d, precision < 0 ? 0 : precision, ch == 'X', spec);
						break;
					case 'o':
						AppendOctal(sb, (ulong)d, spec);
						break;
					case 's':
						AppendString(sb, str, precision, spec);
						break;
					default:
						{
							string w = d.ToString(FloatFormat(ch, precision < 0 ? 6 : precision), CultureInfo.InvariantCulture);
							char[] buf = Scratch();
							w.CopyTo(0, buf, 0, w.Length);
							AppendNumber(sb, buf, 0, w.Length, d >= 0, spec);
							break;
						}
				}

				if (sb.Length - itemStart >= MaxItem)
					throw new IndexOutOfRangeException();
			}
		}

		/// <summary>
		/// The port's parser, a regular expression that took flags from "#-+ " only (so '0' flags had to come
		/// last, as the start of the width), plain digits, and at least one digit after '.'.
		/// </summary>
		static bool TryParseSpec(string fmt, int flagStart, int flagEnd, int widthStart, int widthEnd, int precisionStart, int specEnd,
			out Spec spec, out int precision)
		{
			spec = new Spec();
			precision = -1;

			int zeroFlags = 0;
			for (int k = flagStart; k < flagEnd; k++)
			{
				char f = fmt[k];
				if (f == '0')
				{
					zeroFlags++;
					continue;
				}
				if (zeroFlags > 0)
					return false;
				if (f == '-') spec.Left = true;
				else if (f == '+') spec.Plus = true;
				else if (f == ' ') spec.Space = true;
				else spec.Alternate = true;
			}

			int width = 0;
			for (int k = widthStart; k < widthEnd; k++)
			{
				if (fmt[k] > '9')
					return false;
				width = width * 10 + (fmt[k] - '0');
			}

			if (precisionStart >= 0)
			{
				if (precisionStart == specEnd)
					return false;
				precision = 0;
				for (int k = precisionStart; k < specEnd; k++)
				{
					if (fmt[k] > '9')
						return false;
					precision = precision * 10 + (fmt[k] - '0');
				}
			}

			if (spec.Plus)
				spec.Space = false;
			spec.HasWidth = zeroFlags + widthEnd - widthStart > 0;
			spec.Width = width;
			spec.ZeroPad = zeroFlags > 0 && !spec.Left;
			return true;
		}

		static char[] Scratch()
		{
			char[] buf = t_Scratch;
			if (buf == null)
				t_Scratch = buf = new char[MaxItem];
			return buf;
		}

		static string FloatFormat(char conversion, int precision)
		{
			string[] row = s_FloatFormats[conversion];
			if (row == null)
				s_FloatFormats[conversion] = row = new string[100];

			string f = row[precision];
			if (f == null)
				row[precision] = f = conversion.ToString() + precision.ToString(CultureInfo.InvariantCulture);
			return f;
		}

		/// <summary>Writes the digits of <paramref name="v"/> at the end of <paramref name="buf"/>; returns where they start.</summary>
		static int WriteDecimal(char[] buf, ulong v)
		{
			int p = buf.Length;
			do
			{
				ulong q = v / 10;
				buf[--p] = (char)('0' + (int)(v - q * 10));
				v = q;
			}
			while (v != 0);
			return p;
		}

		/// <summary>Zero padding to a negative width threw ArgumentOutOfRangeException from PadLeft; so does this.</summary>
		static void CheckPadWidth(int width)
		{
			if (width < 0)
				string.Empty.PadLeft(width, '0');
		}

		/// <summary>
		/// Signed numbers: <paramref name="buf"/> holds the number as .NET prints it, '-' included. Zero padding
		/// strips the sign, pads, and puts the sign back in front ("-0042"); '+' takes one column from the width
		/// there, ' ' is ignored there.
		/// </summary>
		static void AppendNumber(StringBuilder sb, char[] buf, int start, int len, bool positive, Spec spec)
		{
			if (!spec.ZeroPad)
			{
				char prefix = positive ? (spec.Plus ? '+' : spec.Space ? ' ' : '\0') : '\0';
				int total = len + (prefix != '\0' ? 1 : 0);
				int pad = spec.HasWidth ? spec.Width - total : 0;

				if (!spec.Left && pad > 0)
					sb.Append(' ', pad);
				if (prefix != '\0')
					sb.Append(prefix);
				sb.Append(buf, start, len);
				if (spec.Left && pad > 0)
					sb.Append(' ', pad);
			}
			else
			{
				if (len > 0 && buf[start] == '-')
				{
					start++;
					len--;
				}

				int target = spec.Width - (spec.Plus ? 1 : 0);
				CheckPadWidth(target);

				if (!positive)
					sb.Append('-');
				else if (spec.Plus)
					sb.Append('+');
				if (target > len)
					sb.Append('0', target - len);
				sb.Append(buf, start, len);
			}
		}

		/// <summary>Hexadecimal with the precision as the minimum digit count; '#' adds "0x" even to 0.</summary>
		static void AppendHex(StringBuilder sb, ulong v, int minDigits, bool upper, Spec spec)
		{
			char[] buf = Scratch();
			string digits = upper ? "0123456789ABCDEF" : "0123456789abcdef";
			int p = buf.Length;
			do
			{
				buf[--p] = digits[(int)(v & 0xF)];
				v >>= 4;
			}
			while (v != 0);
			while (buf.Length - p < minDigits)
				buf[--p] = '0';

			int len = buf.Length - p;
			int prefix = spec.Alternate ? 2 : 0;

			if (!spec.ZeroPad)
			{
				int pad = spec.HasWidth ? spec.Width - len - prefix : 0;
				if (!spec.Left && pad > 0)
					sb.Append(' ', pad);
				if (spec.Alternate)
					sb.Append(upper ? "0X" : "0x");
				sb.Append(buf, p, len);
				if (spec.Left && pad > 0)
					sb.Append(' ', pad);
			}
			else
			{
				int target = spec.Width - prefix;
				CheckPadWidth(target);
				if (spec.Alternate)
					sb.Append(upper ? "0X" : "0x");
				if (target > len)
					sb.Append('0', target - len);
				sb.Append(buf, p, len);
			}
		}

		/// <summary>Octal of the 64-bit pattern; the precision is ignored, and '#' adds a '0' unless the text is "0".</summary>
		static void AppendOctal(StringBuilder sb, ulong v, Spec spec)
		{
			char[] buf = Scratch();
			int p = buf.Length;
			do
			{
				buf[--p] = (char)('0' + (int)(v & 7));
				v >>= 3;
			}
			while (v != 0);

			int len = buf.Length - p;
			bool isZero = len == 1 && buf[p] == '0';

			if (!spec.ZeroPad)
			{
				bool prefix = spec.Alternate && !isZero;
				int pad = spec.HasWidth ? spec.Width - len - (prefix ? 1 : 0) : 0;
				if (!spec.Left && pad > 0)
					sb.Append(' ', pad);
				if (prefix)
					sb.Append('0');
				sb.Append(buf, p, len);
				if (spec.Left && pad > 0)
					sb.Append(' ', pad);
			}
			else
			{
				int target = spec.Width - (spec.Alternate && !isZero ? 1 : 0);
				CheckPadWidth(target);
				int zeros = target > len ? target - len : 0;
				// The port tested the padded text against "0" again.
				if (spec.Alternate && !(isZero && zeros == 0))
					sb.Append('0');
				if (zeros > 0)
					sb.Append('0', zeros);
				sb.Append(buf, p, len);
			}
		}

		/// <summary>
		/// %s: the string ends at its first '\0' (the port passed it as a C string); the precision cuts it and
		/// throws beyond its length; the width pads with the padding character, which can be '0'.
		/// </summary>
		static void AppendString(StringBuilder sb, string s, int precision, Spec spec)
		{
			int len = s.IndexOf('\0');
			if (len < 0)
				len = s.Length;

			if (precision >= 0)
			{
				if (precision > len)
					s.Substring(0, len).Substring(0, precision); // throws ArgumentOutOfRangeException, as the port did
				len = precision;
			}

			int pad = spec.HasWidth ? spec.Width - len : 0;

			if (!spec.Left && pad > 0)
				sb.Append(spec.ZeroPad ? '0' : ' ', pad);

			sb.Append(s, 0, len);

			if (spec.Left && pad > 0)
				sb.Append(' ', pad);
		}

		/// <summary>%q, which differs from Lua 5.2: bytes 16-31 stay raw.</summary>
		static void AppendQuoted(StringBuilder sb, string s)
		{
			sb.Append('"');
			for (int k = 0; k < s.Length; k++)
			{
				char c = s[k];
				switch (c)
				{
					case '"':
					case '\\':
					case '\n':
						sb.Append('\\').Append(c);
						break;
					case '\r':
						sb.Append("\\r");
						break;
					default:
						if (c < (char)16)
						{
							sb.Append('\\');
							if (k + 1 < s.Length && char.IsNumber(s[k + 1]))
								sb.Append('0').Append((char)('0' + c / 10)).Append((char)('0' + c % 10));
							else if (c >= 10)
								sb.Append('1').Append((char)('0' + c - 10));
							else
								sb.Append((char)('0' + c));
						}
						else
						{
							sb.Append(c);
						}
						break;
				}
			}
			sb.Append('"');
		}
	}
}
