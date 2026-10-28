//
// This part taken from KopiLua - https://github.com/NLua/KopiLua
//
// =========================================================================================================
//
// Kopi Lua License
// ----------------
// MIT License for KopiLua
// Copyright (c) 2012 LoDC
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
// associated documentation files (the "Software"), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial
// portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
// LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ===============================================================================
// Lua License
// -----------
// Lua is licensed under the terms of the MIT license reproduced below.
// This means that Lua is free software and can be used for both academic
// and commercial purposes at absolutely no cost.
// For details and rationale, see http://www.lua.org/license.html .
// ===============================================================================
// Copyright (C) 1994-2008 Lua.org, PUC-Rio.
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter.Interop.LuaStateInterop;
using lua_Integer = System.Int32;
using LUA_INTFRM_T = System.Int64;
using UNSIGNED_LUA_INTFRM_T = System.UInt64;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// The KopiLua string.format emulation that MoonSharp used before LuaFormatter, kept verbatim as a test
	/// oracle (KopiLua_StringLib.str_format and Tools.sprintf, from the interpreter at 0c0e5d4). The
	/// interpreter no longer contains it; StringFormatTests compares the managed formatter against it.
	/// </summary>
	internal class KopiLuaFormatOracle : LuaBase
	{
		public const char L_ESC = '%';
		protected const string LUA_INTFRMLEN = "l";

		// LuaBase helpers that only string.format used, removed from the interpreter with it.
		static CharPtr strchr(CharPtr str, char c)
		{
			for (int index = str.index; str.chars[index] != 0; index++)
				if (str.chars[index] == c)
					return new CharPtr(str.chars, index);
			return null;
		}

		static CharPtr strcpy(CharPtr dst, CharPtr src)
		{
			int i;
			for (i = 0; src[i] != '\0'; i++)
				dst[i] = src[i];
			dst[i] = '\0';
			return dst;
		}

		static CharPtr strncpy(CharPtr dst, CharPtr src, int length)
		{
			int index = 0;
			while ((src[index] != '\0') && (index < length))
			{
				dst[index] = src[index];
				index++;
			}
			while (index < length)
				dst[index++] = '\0';
			return dst;
		}

		static int strlen(CharPtr str)
		{
			int index = 0;
			while (str[index] != '\0')
				index++;
			return index;
		}

		static bool isdigit(int c) { return Char.IsDigit((char)c); }

		static void sprintf(CharPtr buffer, CharPtr str, params object[] argv)
		{
			string temp = Tools.sprintf(str.ToString(), argv);
			strcpy(buffer, temp);
		}

		static void LuaLAddString(LuaLBuffer b, string s)
		{
			b.StringBuilder.Append(s.ToString());
		}

		static double LuaLCheckNumber(LuaState L, lua_Integer pos)
		{
			DynValue v = ArgAsType(L, pos, DataType.Number, false);
			return v.Number;
		}

		static lua_Integer LuaGetTop(LuaState L)
		{
			return L.Count;
		}

		/* maximum size of each formatted item (> len(format('%99.99f', -1e308))) */
		public const int MAX_ITEM = 512;
		/* valid flags in a format specification */
		public const string FLAGS = "-+ #0";
		/*
		** maximum size of each format specification (such as '%-099.99d')
		** (+10 accounts for %99.99x plus margin of error)
		*/
		public static readonly int MAX_FORMAT = (FLAGS.Length + 1) + (LUA_INTFRMLEN.Length + 1) + 10;


		private static void addquoted(LuaState L, LuaLBuffer b, int arg)
		{
			uint l;
			CharPtr s = LuaLCheckLString(L, arg, out l);
			LuaLAddChar(b, '"');
			while ((l--) != 0)
			{
				switch (s[0])
				{
					case '"':
					case '\\':
					case '\n':
						{
							LuaLAddChar(b, '\\');
							LuaLAddChar(b, s[0]);
							break;
						}
					case '\r':
						{
							LuaLAddLString(b, "\\r", 2);
							break;
						}
					default:
						{
							if (s[0] < (char)16)
							{
								bool isfollowedbynum = false;

								if (l >= 1)
								{
									if (char.IsNumber(s[1]))
										isfollowedbynum = true;
								}

								if (isfollowedbynum)
									LuaLAddString(b, string.Format("\\{0:000}", (int)s[0]));
								else
									LuaLAddString(b, string.Format("\\{0}", (int)s[0]));
							}
							else
							{
								LuaLAddChar(b, s[0]);
							}
							break;
						}
				}
				s = s.next();
			}
			LuaLAddChar(b, '"');
		}

		private static CharPtr scanformat(LuaState L, CharPtr strfrmt, CharPtr form)
		{
			CharPtr p = strfrmt;
			while (p[0] != '\0' && strchr(FLAGS, p[0]) != null) p = p.next();  /* skip flags */
			if ((uint)(p - strfrmt) >= (FLAGS.Length + 1))
				LuaLError(L, "invalid format (repeated flags)");
			if (isdigit((byte)(p[0]))) p = p.next();  /* skip width */
			if (isdigit((byte)(p[0]))) p = p.next();  /* (2 digits at most) */
			if (p[0] == '.')
			{
				p = p.next();
				if (isdigit((byte)(p[0]))) p = p.next();  /* skip precision */
				if (isdigit((byte)(p[0]))) p = p.next();  /* (2 digits at most) */
			}
			if (isdigit((byte)(p[0])))
				LuaLError(L, "invalid format (width or precision too long)");
			form[0] = '%';
			form = form.next();
			strncpy(form, strfrmt, p - strfrmt + 1);
			form += p - strfrmt + 1;
			form[0] = '\0';
			return p;
		}


		private static void addintlen(CharPtr form)
		{
			uint l = (uint)strlen(form);
			char spec = form[l - 1];
			strcpy(form + l - 1, LUA_INTFRMLEN);
			form[l + (LUA_INTFRMLEN.Length + 1) - 2] = spec;
			form[l + (LUA_INTFRMLEN.Length + 1) - 1] = '\0';
		}


		public static int str_format(LuaState L)
		{
			int top = LuaGetTop(L);
			int arg = 1;
			uint sfl;
			CharPtr strfrmt = LuaLCheckLString(L, arg, out sfl);
			CharPtr strfrmt_end = strfrmt + sfl;
			LuaLBuffer b = new LuaLBuffer(L);
			LuaLBuffInit(L, b);
			while (strfrmt < strfrmt_end)
			{
				if (strfrmt[0] != L_ESC)
				{
					LuaLAddChar(b, strfrmt[0]);
					strfrmt = strfrmt.next();
				}
				else if (strfrmt[1] == L_ESC)
				{
					LuaLAddChar(b, strfrmt[0]);  /* %% */
					strfrmt = strfrmt + 2;
				}
				else
				{ /* format item */
					strfrmt = strfrmt.next();
					CharPtr form = new char[MAX_FORMAT];  /* to store the format (`%...') */
					CharPtr buff = new char[MAX_ITEM];  /* to store the formatted item */
					if (++arg > top)
						LuaLArgError(L, arg, "no value");
					strfrmt = scanformat(L, strfrmt, form);
					char ch = strfrmt[0];
					strfrmt = strfrmt.next();
					switch (ch)
					{
						case 'c':
							{
								sprintf(buff, form, (int)LuaLCheckNumber(L, arg));
								break;
							}
						case 'd':
						case 'i':
							{
								addintlen(form);
								sprintf(buff, form, (LUA_INTFRM_T)LuaLCheckNumber(L, arg));
								break;
							}
						case 'o':
						case 'u':
						case 'x':
						case 'X':
							{
								addintlen(form);
								sprintf(buff, form, (UNSIGNED_LUA_INTFRM_T)LuaLCheckNumber(L, arg));
								break;
							}
						case 'e':
						case 'E':
						case 'f':
						case 'g':
						case 'G':
							{
								sprintf(buff, form, (double)LuaLCheckNumber(L, arg));
								break;
							}
						case 'q':
							{
								addquoted(L, b, arg);
								continue;  /* skip the 'addsize' at the end */
							}
						case 's':
							{
								uint l;
								CharPtr s = LuaLCheckLString(L, arg, out l);
								if ((strchr(form, '.') == null) && l >= 100)
								{
									/* no precision and string is too long to be formatted;
									   keep original string */
									LuaPushValue(L, arg);
									LuaLAddValue(b);
									continue;  /* skip the `addsize' at the end */
								}
								else
								{
									sprintf(buff, form, s);
									break;
								}
							}
						default:
							{  /* also treat cases `pnLlh' */
								return LuaLError(L, "invalid option " + LUA_QL("%" + ch) + " to " +
													 LUA_QL("format"), strfrmt[-1]);
							}
					}
					LuaLAddLString(b, buff, (uint)strlen(buff));
				}
			}
			LuaLPushResult(b);
			return 1;
		}

		static class Tools
		{
		/// <summary>
		/// Determines whether the specified value is of numeric type.
		/// </summary>
		/// <param name="o">The object to check.</param>
		/// <returns>
		/// 	<c>true</c> if o is a numeric type; otherwise, <c>false</c>.
		/// </returns>
		public static bool IsNumericType(object o)
		{
			return (o is byte ||
				o is sbyte ||
				o is short ||
				o is ushort ||
				o is int ||
				o is uint ||
				o is long ||
				o is ulong ||
				o is float ||
				o is double ||
				o is decimal);
		}
		/// <summary>
		/// Determines whether the specified value is positive.
		/// </summary>
		/// <param name="Value">The value.</param>
		/// <param name="ZeroIsPositive">if set to <c>true</c> treats 0 as positive.</param>
		/// <returns>
		/// 	<c>true</c> if the specified value is positive; otherwise, <c>false</c>.
		/// </returns>
		public static bool IsPositive(object Value, bool ZeroIsPositive)
		{
			Type t = Value.GetType();

			if (t == typeof(sbyte))
				return (ZeroIsPositive ? (sbyte)Value >= 0 : (sbyte)Value > 0);
			if (t == typeof(short))
				return (ZeroIsPositive ? (short)Value >= 0 : (short)Value > 0);
			if (t == typeof(int))
				return (ZeroIsPositive ? (int)Value >= 0 : (int)Value > 0);
			if (t == typeof(long))
				return (ZeroIsPositive ? (long)Value >= 0 : (long)Value > 0);
			if (t == typeof(byte))
				return (ZeroIsPositive ? true : (byte)Value > 0);
			if (t == typeof(ushort))
				return (ZeroIsPositive ? true : (ushort)Value > 0);
			if (t == typeof(uint))
				return (ZeroIsPositive ? true : (uint)Value > 0);
			if (t == typeof(ulong))
				return (ZeroIsPositive ? true : (ulong)Value > 0);
			if (t == typeof(float))
				return (ZeroIsPositive ? (float)Value >= 0 : (float)Value > 0);
			if (t == typeof(double))
				return (ZeroIsPositive ? (double)Value >= 0 : (double)Value > 0);
			if (t == typeof(decimal))
				return (ZeroIsPositive ? (decimal)Value >= 0 : (decimal)Value > 0);
			if (t == typeof(char))
				return (ZeroIsPositive ? true : (char)Value != '\0');

			return ZeroIsPositive;
		}
		/// <summary>
		/// Converts the specified values boxed type to its correpsonding unsigned
		/// type.
		/// </summary>
		/// <param name="Value">The value.</param>
		/// <returns>A boxed numeric object whos type is unsigned.</returns>
		public static object ToUnsigned(object Value)
		{
			Type t = Value.GetType();

			if (t == typeof(sbyte))
				return (byte)((sbyte)Value);
			if (t == typeof(short))
				return (ushort)((short)Value);
			if (t == typeof(int))
				return (uint)((int)Value);
			if (t == typeof(long))
				return (ulong)((long)Value);
			if (t == typeof(byte))
				return Value;
			if (t == typeof(ushort))
				return Value;
			if (t == typeof(uint))
				return Value;
			if (t == typeof(ulong))
				return Value;
			if (t == typeof(float))
				return (uint)((float)Value);
			if (t == typeof(double))
				return (ulong)((double)Value);
			if (t == typeof(decimal))
				return (ulong)((decimal)Value);

			return null;
		}
		/// <summary>
		/// Converts the specified values boxed type to its correpsonding integer
		/// type.
		/// </summary>
		/// <param name="Value">The value.</param>
		/// <param name="Round">if set to <c>true</c> [round].</param>
		/// <returns>
		/// A boxed numeric object whos type is an integer type.
		/// </returns>
		public static object ToInteger(object Value, bool Round)
		{
			Type t = Value.GetType();

			if (t == typeof(sbyte))
				return Value;
			if (t == typeof(short))
				return Value;
			if (t == typeof(int))
				return Value;
			if (t == typeof(long))
				return Value;
			if (t == typeof(byte))
				return Value;
			if (t == typeof(ushort))
				return Value;
			if (t == typeof(uint))
				return Value;
			if (t == typeof(ulong))
				return Value;
			if (t == typeof(float))
				return (Round ? (int)Math.Round((float)Value) : (int)((float)Value));
			if (t == typeof(double))
				return (Round ? (long)Math.Round((double)Value) : (long)((double)Value));
			if (t == typeof(decimal))
				return (Round ? Math.Round((decimal)Value) : (decimal)Value);

			return null;
		}
		public static long UnboxToLong(object Value, bool Round)
		{
			Type t = Value.GetType();

			if (t == typeof(sbyte))
				return (long)((sbyte)Value);
			if (t == typeof(short))
				return (long)((short)Value);
			if (t == typeof(int))
				return (long)((int)Value);
			if (t == typeof(long))
				return (long)Value;
			if (t == typeof(byte))
				return (long)((byte)Value);
			if (t == typeof(ushort))
				return (long)((ushort)Value);
			if (t == typeof(uint))
				return (long)((uint)Value);
			if (t == typeof(ulong))
				return (long)((ulong)Value);
			if (t == typeof(float))
				return (Round ? (long)Math.Round((float)Value) : (long)((float)Value));
			if (t == typeof(double))
				return (Round ? (long)Math.Round((double)Value) : (long)((double)Value));
			if (t == typeof(decimal))
				return (Round ? (long)Math.Round((decimal)Value) : (long)((decimal)Value));

			return 0;
		}
		internal static Regex r = new Regex(@"\%(\d*\$)?([\'\#\-\+ ]*)(\d*)(?:\.(\d+))?([hl])?([dioxXucsfeEgGpn%])");
		public static string sprintf(string Format, params object[] Parameters)
		{
			StringBuilder f = new StringBuilder();
			//Regex r = new Regex( @"\%(\d*\$)?([\'\#\-\+ ]*)(\d*)(?:\.(\d+))?([hl])?([dioxXucsfeEgGpn%])" );
			//"%[parameter][flags][width][.precision][length]type"
			Match m = null;
			string w = String.Empty;
			int defaultParamIx = 0;
			int paramIx;
			object o = null;

			bool flagLeft2Right = false;
			bool flagAlternate = false;
			bool flagPositiveSign = false;
			bool flagPositiveSpace = false;
			bool flagZeroPadding = false;
			bool flagGroupThousands = false;

			int fieldLength = 0;
			int fieldPrecision = 0;
			char shortLongIndicator = '\0';
			char formatSpecifier = '\0';
			char paddingCharacter = ' ';

			// find all format parameters in format string
			f.Append(Format);
			m = r.Match(f.ToString());
			while (m.Success)
			{
				paramIx = defaultParamIx;
				if (m.Groups[1] != null && m.Groups[1].Value.Length > 0)
				{
					string val = m.Groups[1].Value.Substring(0, m.Groups[1].Value.Length - 1);
					paramIx = Convert.ToInt32(val) - 1;
				};

				// extract format flags
				flagAlternate = false;
				flagLeft2Right = false;
				flagPositiveSign = false;
				flagPositiveSpace = false;
				flagZeroPadding = false;
				flagGroupThousands = false;
				if (m.Groups[2] != null && m.Groups[2].Value.Length > 0)
				{
					string flags = m.Groups[2].Value;

					flagAlternate = (flags.IndexOf('#') >= 0);
					flagLeft2Right = (flags.IndexOf('-') >= 0);
					flagPositiveSign = (flags.IndexOf('+') >= 0);
					flagPositiveSpace = (flags.IndexOf(' ') >= 0);
					flagGroupThousands = (flags.IndexOf('\'') >= 0);

					// positive + indicator overrides a
					// positive space character
					if (flagPositiveSign && flagPositiveSpace)
						flagPositiveSpace = false;
				}

				// extract field length and
				// pading character
				paddingCharacter = ' ';
				fieldLength = int.MinValue;
				if (m.Groups[3] != null && m.Groups[3].Value.Length > 0)
				{
					fieldLength = Convert.ToInt32(m.Groups[3].Value);
					flagZeroPadding = (m.Groups[3].Value[0] == '0');
				}

				if (flagZeroPadding)
					paddingCharacter = '0';

				// left2right allignment overrides zero padding
				if (flagLeft2Right && flagZeroPadding)
				{
					flagZeroPadding = false;
					paddingCharacter = ' ';
				}

				// extract field precision
				fieldPrecision = int.MinValue;
				if (m.Groups[4] != null && m.Groups[4].Value.Length > 0)
					fieldPrecision = Convert.ToInt32(m.Groups[4].Value);

				// extract short / long indicator
				shortLongIndicator = Char.MinValue;
				if (m.Groups[5] != null && m.Groups[5].Value.Length > 0)
					shortLongIndicator = m.Groups[5].Value[0];

				// extract format
				formatSpecifier = Char.MinValue;
				if (m.Groups[6] != null && m.Groups[6].Value.Length > 0)
					formatSpecifier = m.Groups[6].Value[0];

				// default precision is 6 digits if none is specified except
				if (fieldPrecision == int.MinValue &&
					formatSpecifier != 's' &&
					formatSpecifier != 'c' &&
					Char.ToUpper(formatSpecifier) != 'X' &&
					formatSpecifier != 'o')
					fieldPrecision = 6;

				// get next value parameter and convert value parameter depending on short / long indicator
				if (Parameters == null || paramIx >= Parameters.Length)
					o = null;
				else
				{
					o = Parameters[paramIx];

					if (shortLongIndicator == 'h')
					{
						if (o is int)
							o = (short)((int)o);
						else if (o is long)
							o = (short)((long)o);
						else if (o is uint)
							o = (ushort)((uint)o);
						else if (o is ulong)
							o = (ushort)((ulong)o);
					}
					else if (shortLongIndicator == 'l')
					{
						if (o is short)
							o = (long)((short)o);
						else if (o is int)
							o = (long)((int)o);
						else if (o is ushort)
							o = (ulong)((ushort)o);
						else if (o is uint)
							o = (ulong)((uint)o);
					}
				}

				// convert value parameters to a string depending on the formatSpecifier
				w = String.Empty;
				switch (formatSpecifier)
				{
					case '%':   // % character
						w = "%";
						break;
					case 'd':   // integer
						w = FormatNumber((flagGroupThousands ? "n" : "d"), flagAlternate,
										fieldLength, int.MinValue, flagLeft2Right,
										flagPositiveSign, flagPositiveSpace,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'i':   // integer
						goto case 'd';
					case 'o':   // octal integer - no leading zero
						w = FormatOct("o", flagAlternate,
										fieldLength, int.MinValue, flagLeft2Right,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'x':   // hex integer - no leading zero
						w = FormatHex("x", flagAlternate,
										fieldLength, fieldPrecision, flagLeft2Right,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'X':   // same as x but with capital hex characters
						w = FormatHex("X", flagAlternate,
										fieldLength, fieldPrecision, flagLeft2Right,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'u':   // unsigned integer
						w = FormatNumber((flagGroupThousands ? "n" : "d"), flagAlternate,
										fieldLength, int.MinValue, flagLeft2Right,
										false, false,
										paddingCharacter, ToUnsigned(o));
						defaultParamIx++;
						break;
					case 'c':   // character
						if (IsNumericType(o))
							w = Convert.ToChar(o).ToString();
						else if (o is char)
							w = ((char)o).ToString();
						else if (o is string && ((string)o).Length > 0)
							w = ((string)o)[0].ToString();
						defaultParamIx++;
						break;
					case 's':   // string
								//string t = "{0" + ( fieldLength != int.MinValue ? "," + ( flagLeft2Right ? "-" : String.Empty ) + fieldLength.ToString() : String.Empty ) + ":s}";
						w = o.ToString();
						if (fieldPrecision >= 0)
							w = w.Substring(0, fieldPrecision);

						if (fieldLength != int.MinValue)
							if (flagLeft2Right)
								w = w.PadRight(fieldLength, paddingCharacter);
							else
								w = w.PadLeft(fieldLength, paddingCharacter);
						defaultParamIx++;
						break;
					case 'f':   // double
						w = FormatNumber((flagGroupThousands ? "n" : "f"), flagAlternate,
										fieldLength, fieldPrecision, flagLeft2Right,
										flagPositiveSign, flagPositiveSpace,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'e':   // double / exponent
						w = FormatNumber("e", flagAlternate,
										fieldLength, fieldPrecision, flagLeft2Right,
										flagPositiveSign, flagPositiveSpace,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'E':   // double / exponent
						w = FormatNumber("E", flagAlternate,
										fieldLength, fieldPrecision, flagLeft2Right,
										flagPositiveSign, flagPositiveSpace,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'g':   // double / exponent
						w = FormatNumber("g", flagAlternate,
										fieldLength, fieldPrecision, flagLeft2Right,
										flagPositiveSign, flagPositiveSpace,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'G':   // double / exponent
						w = FormatNumber("G", flagAlternate,
										fieldLength, fieldPrecision, flagLeft2Right,
										flagPositiveSign, flagPositiveSpace,
										paddingCharacter, o);
						defaultParamIx++;
						break;
					case 'p':   // pointer
						if (o is IntPtr)
#if ENABLE_DOTNET
							w = ( (IntPtr)o ).ToString();
#else
							w = "0x" + ((IntPtr)o).ToString("x");
#endif
						defaultParamIx++;
						break;
					case 'n':   // number of characters so far
						w = FormatNumber("d", flagAlternate,
										fieldLength, int.MinValue, flagLeft2Right,
										flagPositiveSign, flagPositiveSpace,
										paddingCharacter, m.Index);
						break;
					default:
						w = String.Empty;
						defaultParamIx++;
						break;
				}

				// replace format parameter with parameter value
				// and start searching for the next format parameter
				// AFTER the position of the current inserted value
				// to prohibit recursive matches if the value also
				// includes a format specifier
				f.Remove(m.Index, m.Length);
				f.Insert(m.Index, w);
				m = r.Match(f.ToString(), m.Index + w.Length);
			}

			return f.ToString();
		}
		private static string FormatOct(string NativeFormat, bool Alternate,
											int FieldLength, int FieldPrecision,
											bool Left2Right,
											char Padding, object Value)
		{
			string w = String.Empty;
			string lengthFormat = "{0" + (FieldLength != int.MinValue ?
											"," + (Left2Right ?
													"-" :
													String.Empty) + FieldLength.ToString() :
											String.Empty) + "}";

			if (IsNumericType(Value))
			{
				w = Convert.ToString(UnboxToLong(Value, true), 8);

				if (Left2Right || Padding == ' ')
				{
					if (Alternate && w != "0")
						w = "0" + w;
					w = String.Format(lengthFormat, w);
				}
				else
				{
					if (FieldLength != int.MinValue)
						w = w.PadLeft(FieldLength - (Alternate && w != "0" ? 1 : 0), Padding);
					if (Alternate && w != "0")
						w = "0" + w;
				}
			}

			return w;
		}
		private static string FormatHex(string NativeFormat, bool Alternate,
											int FieldLength, int FieldPrecision,
											bool Left2Right,
											char Padding, object Value)
		{
			string w = String.Empty;
			string lengthFormat = "{0" + (FieldLength != int.MinValue ?
											"," + (Left2Right ?
													"-" :
													String.Empty) + FieldLength.ToString() :
											String.Empty) + "}";
			string numberFormat = "{0:" + NativeFormat + (FieldPrecision != int.MinValue ?
											FieldPrecision.ToString() :
											String.Empty) + "}";

			if (IsNumericType(Value))
			{
				w = String.Format(numberFormat, Value);

				if (Left2Right || Padding == ' ')
				{
					if (Alternate)
						w = (NativeFormat == "x" ? "0x" : "0X") + w;
					w = String.Format(lengthFormat, w);
				}
				else
				{
					if (FieldLength != int.MinValue)
						w = w.PadLeft(FieldLength - (Alternate ? 2 : 0), Padding);
					if (Alternate)
						w = (NativeFormat == "x" ? "0x" : "0X") + w;
				}
			}

			return w;
		}
		private static string FormatNumber(string NativeFormat, bool Alternate,
											int FieldLength, int FieldPrecision,
											bool Left2Right,
											bool PositiveSign, bool PositiveSpace,
											char Padding, object Value)
		{
			string w = String.Empty;
			string lengthFormat = "{0" + (FieldLength != int.MinValue ?
											"," + (Left2Right ?
													"-" :
													String.Empty) + FieldLength.ToString() :
											String.Empty) + "}";
			string numberFormat = "{0:" + NativeFormat + (FieldPrecision != int.MinValue ?
											FieldPrecision.ToString() :
											"0") + "}";

			if (IsNumericType(Value))
			{
				w = String.Format(CultureInfo.InvariantCulture, numberFormat, Value);

				if (Left2Right || Padding == ' ')
				{
					if (IsPositive(Value, true))
						w = (PositiveSign ?
								"+" : (PositiveSpace ? " " : String.Empty)) + w;
					w = String.Format(lengthFormat, w);
				}
				else
				{
					if (w.StartsWith("-"))
						w = w.Substring(1);
					if (FieldLength != int.MinValue)
						if (PositiveSign)  // xan - change here
							w = w.PadLeft(FieldLength - 1, Padding);
						else
							w = w.PadLeft(FieldLength, Padding);
					if (IsPositive(Value, true))
						w = (PositiveSign ?
								"+" : "") + w;  // xan - change here
					else
						w = "-" + w;
				}
			}

			return w;
		}
		}
	}
}
