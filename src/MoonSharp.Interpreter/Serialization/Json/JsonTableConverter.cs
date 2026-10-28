using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter.Tree;

namespace MoonSharp.Interpreter.Serialization.Json
{
	/// <summary>
	/// Class performing conversions between Tables and Json.
	/// NOTE : the conversions are done respecting json syntax but using Lua constructs. This means mostly that:
	/// 1) Lua string escapes can be accepted while they are not technically valid JSON, and viceversa
	/// 2) Null values are represented using a static userdata of type JsonNull
	/// 3) Do not use it when input cannot be entirely trusted
	/// </summary>
	public static class JsonTableConverter
	{
		/// <summary>
		/// Converts a table to a json string
		/// </summary>
		/// <param name="table">The table.</param>
		/// <returns></returns>
		public static string TableToJson(this Table table)
		{
			StringBuilder sb = new StringBuilder();
			TableToJson(sb, table, 1);
			return sb.ToString();
		}

		/// <summary>
		/// Tables to json.
		/// </summary>
		/// <param name="sb">The sb.</param>
		/// <param name="table">The table.</param>
		private static void TableToJson(StringBuilder sb, Table table, int depth)
		{
			// Deep or cyclic tables would otherwise recurse until the process dies.
			if (depth > Execution.VM.Processor.MAX_NATIVE_NESTING)
				throw new ScriptRuntimeException("json nesting exceeds {0} levels", Execution.VM.Processor.MAX_NATIVE_NESTING);

			bool first = true;

			if (table.Length == 0)
			{
				sb.Append("{");
				foreach (TablePair pair in table.Pairs)
				{
					if (pair.Key.Type == DataType.String && IsValueJsonCompatible(pair.Value))
					{
						if (!first)
							sb.Append(',');

						ValueToJson(sb, pair.Key, depth);
						sb.Append(':');
						ValueToJson(sb, pair.Value, depth);

						first = false;
					}
				}
				sb.Append("}");
			}
			else
			{
				sb.Append("[");
				for (int i = 1; i <= table.Length; i++)
				{
					DynValue value = table.Get(i);
					if (IsValueJsonCompatible(value))
					{
						if (!first)
							sb.Append(',');

						ValueToJson(sb, value, depth);

						first = false;
					}
				}
				sb.Append("]");
			}
		}

		/// <summary>
		/// Converts a generic object to JSON
		/// </summary>
		public static string ObjectToJson(object obj)
		{
			DynValue v = ObjectValueConverter.SerializeObjectToDynValue(null, obj, JsonNull.Create(), JsonEmptyArray.Create());
			StringBuilder sb = new StringBuilder();
			ValueToJson(sb, v, 0);
			return sb.ToString();
		}



		private static void ValueToJson(StringBuilder sb, DynValue value, int depth)
		{
			switch (value.Type)
			{
				case DataType.Boolean:
					sb.Append(value.Boolean ? "true" : "false");
					break;
				case DataType.Number:
					sb.Append(NumberFormatting.ToShortestString(value.Number).Replace('E', 'e'));
					break;
				case DataType.String:
					sb.Append(EscapeString(value.String ?? ""));
					break;
				case DataType.Table:
					TableToJson(sb, value.Table, depth + 1);
					break;
				case DataType.UserData:
					if (ExactInteger.IsExactInteger(value))
						sb.Append(value.UserData.Object.ToString());
					else
						sb.Append(JsonEmptyArray.IsJsonEmptyArray(value) ? "[]" : "null");
					break;
				case DataType.Nil:
				case DataType.Void:
				default:
					sb.Append("null");
					break;
			}
		}

		private static string EscapeString(string s)
		{
			s = s.Replace(@"\", @"\\");
			s = s.Replace(@"/", @"\/");
			s = s.Replace("\"", "\\\"");
			s = s.Replace("\f", @"\f");
			s = s.Replace("\b", @"\b");
			s = s.Replace("\n", @"\n");
			s = s.Replace("\r", @"\r");
			s = s.Replace("\t", @"\t");
			return "\"" + s + "\"";
		}

		private static bool IsValueJsonCompatible(DynValue value)
		{
			return value.Type == DataType.Boolean || value.IsNil() ||
				value.Type == DataType.Number || value.Type == DataType.String ||
				value.Type == DataType.Table ||
				JsonNull.IsJsonNull(value) ||
				JsonEmptyArray.IsJsonEmptyArray(value) ||
				ExactInteger.IsExactInteger(value);
		}

		/// <summary>
		/// Converts a json string to a table
		/// </summary>
		/// <param name="json">The json.</param>
		/// <param name="script">The script to which the table is assigned (null for prime tables).</param>
		/// <param name="emptyArrays">Whether empty arrays should be parsed as JsonEmptyArray rather than a Table.</param>
		/// <returns>A table containing the representation of the given json.</returns>
		public static DynValue ParseString(string json, Script script = null, bool emptyArrays = false)
		{
			return ParseString(json, script, emptyArrays, JsonBigIntegerMode.Number);
		}

		/// <summary>
		/// Converts a json string to a table, choosing how integers that a number cannot hold
		/// exactly are represented.
		/// </summary>
		/// <param name="json">The json.</param>
		/// <param name="script">The script to which the table is assigned (null for prime tables).</param>
		/// <param name="emptyArrays">Whether empty arrays should be parsed as JsonEmptyArray rather than a Table.</param>
		/// <param name="bigIntegers">How to represent integer literals outside +/-(2^53-1).</param>
		/// <returns>A table containing the representation of the given json.</returns>
		public static DynValue ParseString(string json, Script script, bool emptyArrays, JsonBigIntegerMode bigIntegers)
		{
			Lexer L = new Lexer(0, json, false);

			if (L.Current.Type == TokenType.Brk_Open_Curly)
				return ParseJsonObject(L, script, emptyArrays, bigIntegers, 1);
			else if (L.Current.Type == TokenType.Brk_Open_Square)
				return ParseJsonArray(L, script, emptyArrays, bigIntegers, 1);
			else
				throw new SyntaxErrorException(L.Current, "Unexpected token : '{0}'", L.Current.Text);
		}

		private static void AssertToken(Lexer L, TokenType type)
		{
			if (L.Current.Type != type)
				throw new SyntaxErrorException(L.Current, "Unexpected token : '{0}'", L.Current.Text);
		}
		// Deeply nested input would otherwise recurse until the process dies.
		private static void CheckDepth(Lexer L, int depth)
		{
			if (depth > Execution.VM.Processor.MAX_NATIVE_NESTING)
				throw new SyntaxErrorException(L.Current, "json nesting exceeds {0} levels", Execution.VM.Processor.MAX_NATIVE_NESTING);
		}

		private static DynValue ParseJsonArray(Lexer L, Script script, bool emptyArray, JsonBigIntegerMode bigIntegers, int depth)
		{
			CheckDepth(L, depth);
			Table t = new Table(script);

			L.Next();

			while (L.Current.Type != TokenType.Brk_Close_Square)
			{
				DynValue v = ParseJsonValue(L, script, emptyArray, bigIntegers, depth);
				t.Append(v);
				L.Next();

				if (L.Current.Type == TokenType.Comma)
					L.Next();
			}

			return emptyArray && t.Length == 0 ? JsonEmptyArray.Create() : DynValue.NewTable(t);
		}

		private static DynValue ParseJsonObject(Lexer L, Script script, bool emptyArray, JsonBigIntegerMode bigIntegers, int depth)
		{
			CheckDepth(L, depth);
			Table t = new Table(script);

			L.Next();

			while (L.Current.Type != TokenType.Brk_Close_Curly)
			{
				AssertToken(L, TokenType.String);
				string key = L.Current.Text;
				L.Next();
				AssertToken(L, TokenType.Colon);
				L.Next();
				DynValue v = ParseJsonValue(L, script, emptyArray, bigIntegers, depth);
				t.Set(key, v);
				L.Next();

				if (L.Current.Type == TokenType.Comma)
					L.Next();
			}

			return DynValue.NewTable(t);
		}

		private static DynValue ParseJsonValue(Lexer L, Script script, bool emptyArray, JsonBigIntegerMode bigIntegers, int depth)
		{
			if (L.Current.Type == TokenType.Brk_Open_Curly)
			{
				return ParseJsonObject(L, script, emptyArray, bigIntegers, depth + 1);
			}
			else if (L.Current.Type == TokenType.Brk_Open_Square)
			{
				return ParseJsonArray(L, script, emptyArray, bigIntegers, depth + 1);
			}
			else if (L.Current.Type == TokenType.String)
			{
				return DynValue.NewString(L.Current.Text);
			}
			else if (L.Current.Type == TokenType.Number || L.Current.Type == TokenType.Op_MinusOrSub)
			{
				return ParseJsonNumberValue(L, script, bigIntegers);
			}
			else if (L.Current.Type == TokenType.True)
			{
				return DynValue.True;
			}
			else if (L.Current.Type == TokenType.False)
			{
				return DynValue.False;
			}
			else if (L.Current.Type == TokenType.Name && L.Current.Text == "null")
			{
				return JsonNull.Create();
			}
			else
			{
				throw new SyntaxErrorException(L.Current, "Unexpected token : '{0}'", L.Current.Text);
			}
		}

		private static DynValue ParseJsonNumberValue(Lexer L, Script script, JsonBigIntegerMode bigIntegers)
		{
			bool negative;
			if (L.Current.Type == TokenType.Op_MinusOrSub)
			{
				// Negative number consists of 2 tokens.
				L.Next();
				negative = true;
			}
			else
			{
				negative = false;
			}
			if (L.Current.Type != TokenType.Number)
			{
				throw new SyntaxErrorException(L.Current, "Unexpected token : '{0}'", L.Current.Text);
			}
			if (bigIntegers != JsonBigIntegerMode.Number && IsDigits(L.Current.Text))
			{
				DynValue big = ParseBigInteger(L.Current, negative, bigIntegers);
				if (big != null)
					return big;
			}
			var numberValue = L.Current.GetNumberValue();
			if (negative)
			{
				numberValue = -numberValue;
			}
			return DynValue.NewNumber(numberValue).AsReadOnly();
		}

		private static bool IsDigits(string s)
		{
			if (string.IsNullOrEmpty(s))
				return false;
			foreach (char c in s)
				if (c < '0' || c > '9')
					return false;
			return true;
		}

		/// <summary>
		/// Returns an exact representation of an integer literal outside +/-(2^53-1), or null for
		/// one a number holds exactly (that one parses as before).
		/// </summary>
		private static DynValue ParseBigInteger(Token token, bool negative, JsonBigIntegerMode bigIntegers)
		{
			ulong magnitude;
			bool fits = ulong.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out magnitude);

			if (fits && magnitude <= (ulong)ExactInteger.MaxSafeInteger)
				return null;

			if (bigIntegers == JsonBigIntegerMode.String)
			{
				string digits = fits ? magnitude.ToString(CultureInfo.InvariantCulture) : token.Text;
				return DynValue.NewString(negative ? "-" + digits : digits).AsReadOnly();
			}

			if (fits && !negative && magnitude <= long.MaxValue)
				return LuaInt64.NewDynValue((long)magnitude).AsReadOnly();

			if (fits && !negative)
				return LuaUInt64.NewDynValue(magnitude).AsReadOnly();

			// -2^63 is the most negative int64; its magnitude is long.MaxValue + 1.
			if (fits && negative && magnitude <= (ulong)long.MaxValue + 1)
				return LuaInt64.NewDynValue(unchecked((long)(0UL - magnitude))).AsReadOnly();

			throw new SyntaxErrorException(token, "integer {0}{1} does not fit in int64 or uint64",
				negative ? "-" : "", token.Text);
		}
	}
}
