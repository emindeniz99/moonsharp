using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Serialization.Json;

namespace MoonSharp.Hosting
{
	/// <summary>
	/// Converts between JSON text and Lua values for the host contract (see docs/hosting.md).
	///
	/// Numbers: Lua 5.2 numbers are doubles, so only integers in [-2^53, 2^53] are exact. An integer
	/// literal outside that range is rejected instead of rounded: send large IDs as strings.
	/// Arrays: input arrays get a shared marker metatable, so an empty input array goes back out as [].
	/// Scripts create empty output arrays with json.emptyarray(). Other empty tables are written as {}.
	/// Null: JSON null becomes nil, except inside arrays, where it becomes json.null() to keep positions.
	/// </summary>
	public sealed class LuaJson
	{
		/// <summary>The largest integer a double holds exactly (2^53).</summary>
		public const double MaxExactInteger = 9007199254740992.0;

		const int MaxDepth = 128;

		readonly Script m_Script;
		readonly Table m_ArrayMeta;

		public LuaJson(Script script)
		{
			m_Script = script;
			m_ArrayMeta = new Table(script);
			m_ArrayMeta.Set("__jsontype", DynValue.NewString("array"));
		}

		/// <summary>The metatable that marks a Lua table as a JSON array.</summary>
		public Table ArrayMetatable { get { return m_ArrayMeta; } }

		// ---------------------------------------------------------------- reading

		public DynValue Parse(string json)
		{
			int pos = 0;
			DynValue v = ParseValue(json, ref pos, 0, false);
			SkipWhitespace(json, ref pos);

			if (pos != json.Length)
				throw Error(json, pos, "unexpected trailing characters");

			return v;
		}

		/// <summary>Parses a JSON array into call arguments. Null entries become nil arguments.</summary>
		public DynValue[] ParseArguments(string json)
		{
			if (string.IsNullOrEmpty(json))
				return new DynValue[0];

			DynValue v = Parse(json);

			if (v.Type != DataType.Table || v.Table.MetaTable != m_ArrayMeta)
				throw new HostException("input", "arguments must be a JSON array");

			var args = new DynValue[v.Table.Length];

			for (int i = 0; i < args.Length; i++)
			{
				DynValue a = v.Table.Get(i + 1);
				args[i] = JsonNull.IsJsonNull(a) ? DynValue.Nil : a;
			}

			return args;
		}

		DynValue ParseValue(string s, ref int pos, int depth, bool inArray)
		{
			if (depth > MaxDepth)
				throw Error(s, pos, "nesting deeper than " + MaxDepth);

			SkipWhitespace(s, ref pos);

			if (pos >= s.Length)
				throw Error(s, pos, "unexpected end of input");

			char c = s[pos];

			switch (c)
			{
				case '{':
					return ParseObject(s, ref pos, depth);
				case '[':
					return ParseArray(s, ref pos, depth);
				case '"':
					return DynValue.NewString(ParseString(s, ref pos));
				case 't':
					Expect(s, ref pos, "true");
					return DynValue.True;
				case 'f':
					Expect(s, ref pos, "false");
					return DynValue.False;
				case 'n':
					Expect(s, ref pos, "null");
					return inArray ? JsonNull.Create() : DynValue.Nil;
				default:
					if (c == '-' || (c >= '0' && c <= '9'))
						return ParseNumber(s, ref pos);

					throw Error(s, pos, "unexpected character '" + c + "'");
			}
		}

		DynValue ParseObject(string s, ref int pos, int depth)
		{
			var t = new Table(m_Script);
			++pos;
			SkipWhitespace(s, ref pos);

			if (pos < s.Length && s[pos] == '}')
			{
				++pos;
				return DynValue.NewTable(t);
			}

			while (true)
			{
				SkipWhitespace(s, ref pos);

				if (pos >= s.Length || s[pos] != '"')
					throw Error(s, pos, "expected a string key");

				string key = ParseString(s, ref pos);
				SkipWhitespace(s, ref pos);

				if (pos >= s.Length || s[pos] != ':')
					throw Error(s, pos, "expected ':'");

				++pos;
				DynValue v = ParseValue(s, ref pos, depth + 1, false);

				if (!v.IsNil())
					t.Set(key, v);

				SkipWhitespace(s, ref pos);

				if (pos < s.Length && s[pos] == ',')
				{
					++pos;
					continue;
				}

				if (pos < s.Length && s[pos] == '}')
				{
					++pos;
					return DynValue.NewTable(t);
				}

				throw Error(s, pos, "expected ',' or '}'");
			}
		}

		DynValue ParseArray(string s, ref int pos, int depth)
		{
			var t = new Table(m_Script);
			t.MetaTable = m_ArrayMeta;
			++pos;
			SkipWhitespace(s, ref pos);

			if (pos < s.Length && s[pos] == ']')
			{
				++pos;
				return DynValue.NewTable(t);
			}

			int index = 1;

			while (true)
			{
				t.Set(index++, ParseValue(s, ref pos, depth + 1, true));
				SkipWhitespace(s, ref pos);

				if (pos < s.Length && s[pos] == ',')
				{
					++pos;
					continue;
				}

				if (pos < s.Length && s[pos] == ']')
				{
					++pos;
					return DynValue.NewTable(t);
				}

				throw Error(s, pos, "expected ',' or ']'");
			}
		}

		static DynValue ParseNumber(string s, ref int pos)
		{
			int start = pos;
			bool integer = true;

			if (s[pos] == '-')
				++pos;

			while (pos < s.Length)
			{
				char c = s[pos];

				if (c >= '0' && c <= '9')
					++pos;
				else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
				{
					integer = false;
					++pos;
				}
				else
					break;
			}

			string text = s.Substring(start, pos - start);
			double d;

			if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || double.IsInfinity(d))
				throw Error(s, start, "invalid number '" + text + "'");

			// Check the text, not the parsed double: 2^53 + 1 parses to exactly 2^53.
			if (integer && Math.Abs(d) >= MaxExactInteger && text.TrimStart('-') != "9007199254740992")
				throw new HostException("input", string.Format(CultureInfo.InvariantCulture,
					"integer {0} is outside +/-2^53 and would lose precision as a Lua number; send it as a string", text));

			return DynValue.NewNumber(d);
		}

		static string ParseString(string s, ref int pos)
		{
			var sb = new StringBuilder();
			++pos;

			while (pos < s.Length)
			{
				char c = s[pos++];

				if (c == '"')
					return sb.ToString();

				if (c < 0x20)
					throw Error(s, pos - 1, "control character in string");

				if (c != '\\')
				{
					sb.Append(c);
					continue;
				}

				if (pos >= s.Length)
					break;

				char e = s[pos++];

				switch (e)
				{
					case '"': sb.Append('"'); break;
					case '\\': sb.Append('\\'); break;
					case '/': sb.Append('/'); break;
					case 'b': sb.Append('\b'); break;
					case 'f': sb.Append('\f'); break;
					case 'n': sb.Append('\n'); break;
					case 'r': sb.Append('\r'); break;
					case 't': sb.Append('\t'); break;
					case 'u':
						if (pos + 4 > s.Length)
							throw Error(s, pos, "truncated \\u escape");

						int code;

						if (!int.TryParse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
							throw Error(s, pos, "invalid \\u escape");

						sb.Append((char)code);
						pos += 4;
						break;
					default:
						throw Error(s, pos - 1, "invalid escape '\\" + e + "'");
				}
			}

			throw Error(s, pos, "unterminated string");
		}

		static void Expect(string s, ref int pos, string word)
		{
			if (string.CompareOrdinal(s, pos, word, 0, word.Length) != 0)
				throw Error(s, pos, "expected '" + word + "'");

			pos += word.Length;
		}

		static void SkipWhitespace(string s, ref int pos)
		{
			while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r'))
				++pos;
		}

		static HostException Error(string s, int pos, string message)
		{
			return new HostException("input", string.Format(CultureInfo.InvariantCulture, "invalid JSON at offset {0}: {1}", pos, message));
		}

		// ---------------------------------------------------------------- writing

		public string Serialize(DynValue value)
		{
			var sb = new StringBuilder();
			Write(value, sb);
			return sb.ToString();
		}

		public void Write(DynValue value, StringBuilder sb)
		{
			Write(value, sb, new HashSet<Table>(), "$");
		}

		void Write(DynValue v, StringBuilder sb, HashSet<Table> visiting, string path)
		{
			if (v == null)
			{
				sb.Append("null");
				return;
			}

			switch (v.Type)
			{
				case DataType.Nil:
				case DataType.Void:
					sb.Append("null");
					return;
				case DataType.Boolean:
					sb.Append(v.Boolean ? "true" : "false");
					return;
				case DataType.Number:
					WriteNumber(v.Number, sb, path);
					return;
				case DataType.String:
					CheckSurrogates(v.String, path);
					WriteString(v.String, sb);
					return;
				case DataType.Tuple:
					// A function returning several values: the first one is the result, as in Lua assignments.
					Write(v.Tuple.Length > 0 ? v.Tuple[0] : DynValue.Nil, sb, visiting, path);
					return;
				case DataType.Table:
					WriteTable(v.Table, sb, visiting, path);
					return;
				case DataType.UserData:
					if (JsonNull.IsJsonNull(v))
					{
						sb.Append("null");
						return;
					}

					if (JsonEmptyArray.IsJsonEmptyArray(v))
					{
						sb.Append("[]");
						return;
					}

					break;
			}

			throw new HostException("output", string.Format("{0}: a {1} cannot be converted to JSON", path, v.Type.ToLuaTypeString()));
		}

		void WriteTable(Table t, StringBuilder sb, HashSet<Table> visiting, string path)
		{
			if (!visiting.Add(t))
				throw new HostException("output", path + ": table contains a reference cycle");

			if (visiting.Count > MaxDepth)
				throw new HostException("output", path + ": nesting deeper than " + MaxDepth);

			int length = t.Length;
			bool isArray = t.MetaTable == m_ArrayMeta || (length > 0 && CountKeys(t) == length);

			if (isArray)
			{
				sb.Append('[');

				for (int i = 1; i <= length; i++)
				{
					if (i > 1) sb.Append(',');
					Write(t.Get(i), sb, visiting, path + "[" + i + "]");
				}

				sb.Append(']');
			}
			else
			{
				sb.Append('{');
				bool first = true;

				foreach (TablePair pair in t.Pairs)
				{
					// Pairs also lists fields set to nil (by table.remove, for example); Lua's pairs skips them.
					if (pair.Value.IsNil())
						continue;

					string key = KeyToString(pair.Key, path);

					if (!first) sb.Append(',');
					first = false;

					WriteString(key, sb);
					sb.Append(':');
					Write(pair.Value, sb, visiting, path + "." + key);
				}

				sb.Append('}');
			}

			visiting.Remove(t);
		}

		static int CountKeys(Table t)
		{
			int n = 0;

			foreach (TablePair pair in t.Pairs)
				if (!pair.Value.IsNil())
					++n;

			return n;
		}

		static string KeyToString(DynValue key, string path)
		{
			if (key.Type == DataType.String)
			{
				CheckSurrogates(key.String, path);
				return key.String;
			}

			if (key.Type == DataType.Number && IsExactInteger(key.Number))
				return ((long)key.Number).ToString(CultureInfo.InvariantCulture);

			throw new HostException("output", string.Format("{0}: a {1} key cannot be a JSON object key", path, key.Type.ToLuaTypeString()));
		}

		static void WriteNumber(double d, StringBuilder sb, string path)
		{
			if (double.IsNaN(d) || double.IsInfinity(d))
				throw new HostException("output", path + ": NaN and infinity have no JSON representation");

			if (IsExactInteger(d))
				sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
			else
				sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
		}

		static bool IsExactInteger(double d)
		{
			return Math.Floor(d) == d && Math.Abs(d) <= MaxExactInteger;
		}

		/// <summary>
		/// Rejects a string with an unpaired surrogate (from string.sub or string.reverse on a pair, for example).
		/// UTF-8 cannot encode it and System.Text.Json cannot read its \u escape, so each transport would
		/// deliver something different; an error is the same everywhere.
		/// </summary>
		static void CheckSurrogates(string s, string path)
		{
			for (int i = 0; i < s.Length; i++)
			{
				if (!char.IsSurrogate(s[i]))
					continue;

				if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
				{
					++i;
					continue;
				}

				throw new HostException("output", string.Format(CultureInfo.InvariantCulture,
					"{0}: a string with an unpaired surrogate at code unit {1} has no JSON representation", path, i + 1));
			}
		}

		public static void WriteString(string s, StringBuilder sb)
		{
			sb.Append('"');

			foreach (char c in s)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					case '\b': sb.Append("\\b"); break;
					case '\f': sb.Append("\\f"); break;
					default:
						if (c < 0x20)
							sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
						else
							sb.Append(c);
						break;
				}
			}

			sb.Append('"');
		}
	}
}
