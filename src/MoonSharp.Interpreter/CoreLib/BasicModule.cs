// Disable warnings about XML documentation
#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Tree;

namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing basic Lua functions (print, type, tostring, etc) as a MoonSharp module.
	/// </summary>
	[MoonSharpModule]
	public class BasicModule
	{
		//type (v)
		//----------------------------------------------------------------------------------------------------------------
		//Returns the type of its only argument, coded as a string. The possible results of this function are "nil"
		//(a string, not the value nil), "number", "string", "boolean", "table", "function", "thread", and "userdata".
		[MoonSharpModuleMethod]
		public static DynValue type(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			if (args.Count < 1) throw ScriptRuntimeException.BadArgumentValueExpected(0, "type");

			DynValue v = args[0];
			return DynValue.NewString(v.Type.ToLuaTypeString());
		}



		//assert (v [, message])
		//----------------------------------------------------------------------------------------------------------------
		//Issues an error when the value of its argument v is false (i.e., nil or false);
		//otherwise, returns all its arguments. message is an error message; when absent, it defaults to "assertion failed!"
		[MoonSharpModuleMethod]
		public static DynValue assert(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue v = args[0];
			DynValue message = args[1];

			if (!v.CastToBool())
			{
				if (message.IsNil())
					throw new ScriptRuntimeException("assertion failed!"); // { DoNotDecorateMessage = true };
				else
					throw new ScriptRuntimeException(message.ToPrintString()); // { DoNotDecorateMessage = true };
			}

			return DynValue.NewTupleNested(args.GetArray());
		}

		// collectgarbage  ([opt [, arg]])
		// ----------------------------------------------------------------------------------------------------------------
		// This function is mostly a stub towards the CLR GC. If mode is nil, "collect" or "restart", a GC is forced.
		[MoonSharpModuleMethod]
		public static DynValue collectgarbage(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue opt = args[0];

			string mode = opt.CastToString();

			if (mode == null || mode == "collect" || mode == "restart")
			{
#if ENABLE_DOTNET
				GC.Collect();
#else
				GC.Collect(2, GCCollectionMode.Forced);
#endif
			}

			return DynValue.Nil;
		}

		// error (message [, level])
		// ----------------------------------------------------------------------------------------------------------------
		// Terminates the last protected function called and returns message as the error message. Function error never returns.
		// Usually, error adds some information about the error position at the beginning of the message.
		// The level argument specifies how to get the error position.
		// With level 1 (the default), the error position is where the error function was called.
		// Level 2 points the error to where the function that called error was called; and so on.
		// Passing a level 0 avoids the addition of error position information to the message.
		[MoonSharpModuleMethod]
		public static DynValue error(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue message = args.AsType(0, "error", DataType.String, false);
			DynValue level = args.AsType(1, "error", DataType.Number, true);

			Coroutine cor = executionContext.GetCallingCoroutine();

			WatchItem[] stacktrace = cor.GetStackTrace(0, executionContext.CallingLocation);

			var e = new ScriptRuntimeException(message.String);

			if (level.IsNil())
			{
				level = DynValue.NewNumber(1); // Default
			}

			if (level.Number > 0 && level.Number < stacktrace.Length)
			{
				// Lua allows levels up to max. value of a double, while this has to be cast to int
				// Probably never will be a problem, just leaving this note here
				WatchItem wi = stacktrace[(int)level.Number];

				e.DecorateMessage(executionContext.GetScript(), wi.Location);
			}
			else
			{
				e.DoNotDecorateMessage = true;
			}

			throw e;
		}


		// tostring (v)
		// ----------------------------------------------------------------------------------------------------------------
		// Receives a value of any type and converts it to a string in a reasonable format. (For complete control of how
		// numbers are converted, use string.format.)
		//
		// If the metatable of v has a "__tostring" field, then tostring calls the corresponding value with v as argument,
		// and uses the result of the call as its result.
		[MoonSharpModuleMethod]
		public static DynValue tostring(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			if (args.Count < 1) throw ScriptRuntimeException.BadArgumentValueExpected(0, "tostring");

			DynValue v = args[0];
			DynValue tail = executionContext.GetMetamethodTailCall(v, "__tostring", v);

			if (tail == null || tail.IsNil())
				return DynValue.NewString(v.ToPrintString());

			tail.TailCallData.Continuation = new CallbackFunction(__tostring_continuation, "__tostring");

			return tail;
		}

		private static DynValue __tostring_continuation(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue b = args[0].ToScalar();

			if (b.IsNil())
				return b;

			if (b.Type != DataType.String)
				throw new ScriptRuntimeException("'tostring' must return a string");


			return b;
		}

		// select (index, ...)
		// -----------------------------------------------------------------------------
		// If index is a number, returns all arguments after argument number index; a negative number indexes from
		// the end (-1 is the last argument). Otherwise, index must be the string "#", and select returns the total
		// number of extra arguments it received.
		[MoonSharpModuleMethod]
		public static DynValue select(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			if (args[0].Type == DataType.String && args[0].String == "#")
			{
				if (args[args.Count - 1].Type == DataType.Tuple)
				{
					return DynValue.NewNumber(args.Count - 1 + args[args.Count - 1].Tuple.Length);
				}
				else
				{
					return DynValue.NewNumber(args.Count - 1);
				}
			}

			DynValue v_num = args.AsType(0, "select", DataType.Number, false);
			int num = (int)v_num.Number;

			List<DynValue> values = new List<DynValue>();

			if (num > 0)
			{
				for (int i = num; i < args.Count; i++)
					values.Add(args[i]);
			}
			else if (num < 0)
			{
				num = args.Count + num;

				if (num < 1)
					throw ScriptRuntimeException.BadArgumentIndexOutOfRange("select", 0);

				for (int i = num; i < args.Count; i++)
					values.Add(args[i]);
			}
			else
			{
				throw ScriptRuntimeException.BadArgumentIndexOutOfRange("select", 0);
			}

			return DynValue.NewTupleNested(values.ToArray());
		}




		// tonumber (e [, base])
		// ----------------------------------------------------------------------------------------------------------------
		// When called with no base, tonumber tries to convert its argument to a number. If the argument is already
		// a number or a string convertible to a number (see §3.4.2), then tonumber returns this number; otherwise,
		// it returns nil.
		//
		// When called with base, then e should be a string to be interpreted as an integer numeral in that base.
		// The base may be any integer between 2 and 36, inclusive. In bases above 10, the letter 'A' (in either
		// upper or lower case) represents 10, 'B' represents 11, and so forth, with 'Z' representing 35. If the
		// string e is not a valid numeral in the given base, the function returns nil.
		[MoonSharpModuleMethod]
		public static DynValue tonumber(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			if (args.Count < 1) throw ScriptRuntimeException.BadArgumentValueExpected(0, "tonumber");

			DynValue e = args[0];
			DynValue b = args.AsType(1, "tonumber", DataType.Number, true);

			if (b.IsNil())
			{
				if (e.Type == DataType.Number)
					return e;

				if (e.Type != DataType.String)
					return DynValue.Nil;

				double d;
				if (TryParseLuaNumber(e.String, out d))
				{
					return DynValue.NewNumber(d);
				}
				return DynValue.Nil;
			}
			else
			{
				DynValue ee;

				if (args[0].Type != DataType.Number)
					ee = args.AsType(0, "tonumber", DataType.String, false);
				else
					ee = DynValue.NewString(args[0].Number.ToString(CultureInfo.InvariantCulture));

				int bb = (int)b.Number;

				if (bb < 2 || bb > 36)
					throw new ScriptRuntimeException("bad argument #2 to 'tonumber' (base out of range)");

				return StringToNumberInBase(ee.String, bb);
			}
		}

		/// <summary>
		/// Converts a string to a number as Lua 5.2 does (luaO_str2d): a decimal numeral, or a
		/// hexadecimal one with an optional fraction and binary exponent ("0x1.8p3"), with an
		/// optional sign and surrounding whitespace. "inf" and "nan" are not numerals. String to
		/// number coercion in arithmetic should follow the same rules.
		/// </summary>
		internal static bool TryParseLuaNumber(string s, out double result)
		{
			result = 0.0;

			if (s == null || s.IndexOf('n') >= 0 || s.IndexOf('N') >= 0)
				return false;

			if (s.IndexOf('x') >= 0 || s.IndexOf('X') >= 0)
				return TryParseLuaHexNumber(s, out result);

			// double.TryParse also accepts culture symbols such as "Infinity" or "\u221E"; Lua does not
			foreach (char c in s)
			{
				if (!LexerUtils.CharIsDigit(c) && c != '+' && c != '-' && c != '.' && c != 'e' && c != 'E' && !IsLuaSpace(c))
					return false;
			}

			return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
		}

		private static bool TryParseLuaHexNumber(string s, out double result)
		{
			result = 0.0;
			int i = 0;

			while (i < s.Length && IsLuaSpace(s[i]))
				i++;

			bool neg = (i < s.Length && s[i] == '-');

			if (i < s.Length && (s[i] == '-' || s[i] == '+'))
				i++;

			if (i + 1 >= s.Length || s[i] != '0' || (s[i + 1] != 'x' && s[i + 1] != 'X'))
				return false;

			i += 2;

			double mantissa = 0.0;
			int exponent = 0;
			int digits = 0;

			for (; i < s.Length && LexerUtils.CharIsHexDigit(s[i]); i++, digits++)
				mantissa = mantissa * 16 + LexerUtils.HexDigit2Value(s[i]);

			if (i < s.Length && s[i] == '.')
			{
				for (i++; i < s.Length && LexerUtils.CharIsHexDigit(s[i]); i++, digits++, exponent -= 4)
					mantissa = mantissa * 16 + LexerUtils.HexDigit2Value(s[i]);
			}

			if (digits == 0)
				return false;

			if (i < s.Length && (s[i] == 'p' || s[i] == 'P'))
			{
				int j = i + 1;
				bool expNeg = (j < s.Length && s[j] == '-');

				if (j < s.Length && (s[j] == '-' || s[j] == '+'))
					j++;

				// without a digit the 'p' is not part of the numeral, and is rejected below
				if (j < s.Length && LexerUtils.CharIsDigit(s[j]))
				{
					int exp = 0;

					for (; j < s.Length && LexerUtils.CharIsDigit(s[j]); j++)
						exp = Math.Min(exp * 10 + (s[j] - '0'), 100000);

					exponent += expNeg ? -exp : exp;
					i = j;
				}
			}

			while (i < s.Length && IsLuaSpace(s[i]))
				i++;

			if (i != s.Length)
				return false;

			result = (neg ? -mantissa : mantissa) * Math.Pow(2, exponent);
			return true;
		}

		// isspace in the C locale, which Lua uses
		private static bool IsLuaSpace(char c)
		{
			return c == ' ' || (c >= '\t' && c <= '\r');
		}

		// Integer numeral parsing for tonumber(e, base), following standard Lua semantics:
		// optional surrounding whitespace, an optional '-' or '+' sign, then one or more digits
		// (0-9, a-z/A-Z for 10-35) all below the base. Any other input yields nil — never
		// a CLR exception, which would escape the interpreter as a non-Lua error.
		private static DynValue StringToNumberInBase(string str, int numBase)
		{
			// A string DynValue can wrap a null (e.g. host-created DynValue.NewString(null));
			// treat it as unparseable rather than dereferencing it into a NullReferenceException.
			if (str == null)
				return DynValue.Nil;

			string s = str.Trim();
			int i = 0;
			bool neg = false;

			if (i < s.Length && (s[i] == '-' || s[i] == '+'))
			{
				neg = s[i] == '-';
				i++;
			}

			if (i >= s.Length)
				return DynValue.Nil;

			double value = 0.0;

			for (; i < s.Length; i++)
			{
				char c = s[i];
				int digit;

				if (c >= '0' && c <= '9')
					digit = c - '0';
				else if (c >= 'a' && c <= 'z')
					digit = c - 'a' + 10;
				else if (c >= 'A' && c <= 'Z')
					digit = c - 'A' + 10;
				else
					return DynValue.Nil;

				if (digit >= numBase)
					return DynValue.Nil;

				value = value * numBase + digit;
			}

			return DynValue.NewNumber(neg ? -value : value);
		}

		[MoonSharpModuleMethod]
		public static DynValue print(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			StringBuilder sb = new StringBuilder();

			for (int i = 0; i < args.Count; i++)
			{
				if (args[i].IsVoid())
					break;

				if (i != 0)
					sb.Append('\t');

				sb.Append(args.AsStringUsingMeta(executionContext, i, "print"));
			}

			executionContext.GetScript().Options.DebugPrint(sb.ToString());

			return DynValue.Nil;
		}
	}
}
