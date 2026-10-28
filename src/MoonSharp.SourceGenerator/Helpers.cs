using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace MoonSharp.SourceGenerator
{
	/// <summary>
	/// The conversion helpers generated code calls, written once per assembly into GeneratedUserData and
	/// only when used. Each one has a typed fast path for the common case and otherwise defers to
	/// DynValue.ToObject(Type) / DynValue.FromObject, the conversions the reflection descriptors use, so
	/// results and error texts match them (custom converters included).
	/// </summary>
	sealed class Helpers
	{
		readonly KnownSymbols m_Known;
		readonly SortedDictionary<string, string> m_Used = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
		readonly Dictionary<string, string> m_EnumNames = new Dictionary<string, string>();

		public Helpers(KnownSymbols known)
		{
			m_Known = known;
		}

		public IEnumerable<string> Code { get { return m_Used.Values; } }

		/// <summary>
		/// Script -> CLR: an expression converting the DynValue expression <paramref name="v"/> (evaluated once)
		/// to <paramref name="t"/>, as ScriptToClrConversions.DynValueToObjectOfType(v, t, null, false) does.
		/// </summary>
		public string ToClr(ITypeSymbol t, string v, string prefix)
		{
			string name = Names.Of(t);

			switch (t.SpecialType)
			{
				case SpecialType.System_Double: return Use(prefix, "ToDouble", v, NumberHelper("double", "ToDouble", null, null, "d"));
				case SpecialType.System_Single: return Use(prefix, "ToSingle", v, NumberHelper("float", "ToSingle", null, null, "(float)d"));
				case SpecialType.System_Int32: return Use(prefix, "ToInt32", v, NumberHelper("int", "ToInt32", "-2147483648.0", "2147483647.0", "global::System.Convert.ToInt32(d)"));
				case SpecialType.System_UInt32: return Use(prefix, "ToUInt32", v, NumberHelper("uint", "ToUInt32", "0.0", "4294967295.0", "global::System.Convert.ToUInt32(d)"));
				case SpecialType.System_Int16: return Use(prefix, "ToInt16", v, NumberHelper("short", "ToInt16", "-32768.0", "32767.0", "global::System.Convert.ToInt16(d)"));
				case SpecialType.System_UInt16: return Use(prefix, "ToUInt16", v, NumberHelper("ushort", "ToUInt16", "0.0", "65535.0", "global::System.Convert.ToUInt16(d)"));
				case SpecialType.System_SByte: return Use(prefix, "ToSByte", v, NumberHelper("sbyte", "ToSByte", "-128.0", "127.0", "global::System.Convert.ToSByte(d)"));
				case SpecialType.System_Byte: return Use(prefix, "ToByte", v, NumberHelper("byte", "ToByte", "0.0", "255.0", "global::System.Convert.ToByte(d)"));
				// 2^63 and 2^64 are exact doubles; below them Convert does not overflow.
				case SpecialType.System_Int64: return Use(prefix, "ToInt64", v, NumberHelper("long", "ToInt64", "-9223372036854775808.0", null, "global::System.Convert.ToInt64(d)", "d < 9223372036854775808.0"));
				case SpecialType.System_UInt64: return Use(prefix, "ToUInt64", v, NumberHelper("ulong", "ToUInt64", "0.0", null, "global::System.Convert.ToUInt64(d)", "d < 18446744073709551616.0"));
				case SpecialType.System_Boolean: return Use(prefix, "ToBoolean", v, Simple("bool", "ToBoolean", "Boolean", "v.Boolean"));
				case SpecialType.System_String: return Use(prefix, "ToStringValue", v, Simple("string", "ToStringValue", "String", "v.String"));
				case SpecialType.System_Object: return "(" + v + ").ToObject(typeof(object))";
			}

			if (KnownSymbols.Is(t, m_Known.DynValue))
				return Use(prefix, "ToDynValue", v, DynValueHelper());
			if (KnownSymbols.Is(t, m_Known.Table))
				return Use(prefix, "ToTable", v, Simple("global::MoonSharp.Interpreter.Table", "ToTable", "Table", "v.Table"));
			if (KnownSymbols.Is(t, m_Known.Closure))
				return Use(prefix, "ToClosure", v, Simple("global::MoonSharp.Interpreter.Closure", "ToClosure", "Function", "v.Function"));

			if (t.TypeKind == TypeKind.Enum)
				return Use(prefix, EnumHelperName(t), v, EnumHelper((INamedTypeSymbol)t));

			bool nullable = t.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

			// Classes and interfaces (delegates and arrays are other type kinds).
			if (t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Interface)
				return Use(prefix, "ToUserData<" + name + ">", v, UserDataHelper(false));

			if (t.TypeKind == TypeKind.Struct && !nullable && t.SpecialType == SpecialType.None)
				return Use(prefix, "ToUserDataValue<" + name + ">", v, UserDataHelper(true));

			// Nullable, decimal, char, arrays, delegates and anything else: the general conversion.
			return "((" + name + ")(" + v + ").ToObject(typeof(" + name + ")))";
		}

		/// <summary>CLR -> script: an expression converting <paramref name="x"/> as ClrToScriptConversions.ObjectToDynValue does.</summary>
		public string FromClr(ITypeSymbol t, string x, string prefix)
		{
			switch (t.SpecialType)
			{
				case SpecialType.System_Boolean: return Use(prefix, "FromBoolean", "script, " + x, FromHelper("bool", "FromBoolean", "global::MoonSharp.Interpreter.DynValue.NewBoolean(x)"));
				case SpecialType.System_String: return Use(prefix, "FromString", "script, " + x, FromStringHelper());
				case SpecialType.System_Char: return Use(prefix, "FromChar", "script, " + x, FromHelper("char", "FromChar", "global::MoonSharp.Interpreter.DynValue.NewString(x.ToString())"));
				case SpecialType.System_Double: return Use(prefix, "FromDouble", "script, " + x, FromHelper("double", "FromDouble", "global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_Single: return Use(prefix, "FromSingle", "script, " + x, FromHelper("float", "FromSingle", "global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_Decimal: return Use(prefix, "FromDecimal", "script, " + x, FromHelper("decimal", "FromDecimal", "global::MoonSharp.Interpreter.DynValue.NewNumber((double)x)"));
				case SpecialType.System_Int32: return Use(prefix, "FromInt32", "script, " + x, FromHelper("int", "FromInt32", "global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_UInt32: return Use(prefix, "FromUInt32", "script, " + x, FromHelper("uint", "FromUInt32", "global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_Int16: return Use(prefix, "FromInt16", "script, " + x, FromHelper("short", "FromInt16", "global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_UInt16: return Use(prefix, "FromUInt16", "script, " + x, FromHelper("ushort", "FromUInt16", "global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_SByte: return Use(prefix, "FromSByte", "script, " + x, FromHelper("sbyte", "FromSByte", "global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_Byte: return Use(prefix, "FromByte", "script, " + x, FromHelper("byte", "FromByte", "global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_Int64: return Use(prefix, "FromInt64", "script, " + x, FromHelper("long", "FromInt64",
					"(script != null && script.Options.ExactInt64Interop) ? global::MoonSharp.Interpreter.LuaInt64.NewDynValue(x) : global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
				case SpecialType.System_UInt64: return Use(prefix, "FromUInt64", "script, " + x, FromHelper("ulong", "FromUInt64",
					"(script != null && script.Options.ExactInt64Interop) ? global::MoonSharp.Interpreter.LuaUInt64.NewDynValue(x) : global::MoonSharp.Interpreter.DynValue.NewNumber(x)"));
			}

			if (KnownSymbols.Is(t, m_Known.DynValue))
				return "(" + x + " ?? global::MoonSharp.Interpreter.DynValue.Nil)";

			return "global::MoonSharp.Interpreter.DynValue.FromObject(script, " + x + ")";
		}

		/// <summary>
		/// Whether an optional parameter takes its default value: DynValueToObjectOfNonByRefType returns the
		/// default for a missing argument, and for nil when the parameter is a non-nullable value type.
		/// Null when the default is never used that way (DynValue and object parameters).
		/// </summary>
		public string TakesDefault(ITypeSymbol t, string v, string prefix)
		{
			if (KnownSymbols.Is(t, m_Known.DynValue) || t.SpecialType == SpecialType.System_Object)
				return null;

			NoConv();
			bool valueType = t.IsValueType && t.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
			string test = valueType
				? "(" + v + ".Type == global::MoonSharp.Interpreter.DataType.Void || " + v + ".Type == global::MoonSharp.Interpreter.DataType.Nil)"
				: v + ".Type == global::MoonSharp.Interpreter.DataType.Void";

			return "(" + test + " && " + prefix + "NoConv(" + v + ".Type, typeof(" + Names.Of(t) + ")))";
		}

		string Use(string prefix, string name, string args, string code)
		{
			int generic = name.IndexOf('<');
			string key = generic < 0 ? name : name.Substring(0, generic);

			if (!m_Used.ContainsKey(key))
				m_Used[key] = code;

			return prefix + name + "(" + args + ")";
		}

		void NoConv()
		{
			if (!m_Used.ContainsKey("NoConv"))
			{
				m_Used["NoConv"] =
@"		/// <summary>Whether no custom converter is registered for this script type and CLR type.</summary>
		internal static bool NoConv(global::MoonSharp.Interpreter.DataType from, global::System.Type to)
		{
			return global::MoonSharp.Interpreter.Script.GlobalOptions.CustomConverters.GetScriptToClrCustomConversion(from, to) == null;
		}
";
			}
		}

		string NumberHelper(string type, string name, string min, string max, string convert, string extraTest = null)
		{
			NoConv();
			var tests = new List<string>();
			if (min != null) tests.Add("d >= " + min);
			if (max != null) tests.Add("d <= " + max);
			if (extraTest != null) tests.Add(extraTest);
			string body = tests.Count == 0
				? "				return " + convert + ";\n"
				: "				if (" + string.Join(" && ", tests) + ")\n					return " + convert + ";\n";

			// NaN fails every range test and goes to the general conversion, which reports it.
			return
"		internal static " + type + " " + name + "(global::MoonSharp.Interpreter.DynValue v)\n" +
"		{\n" +
"			if (v.Type == global::MoonSharp.Interpreter.DataType.Number && NoConv(global::MoonSharp.Interpreter.DataType.Number, typeof(" + type + ")))\n" +
"			{\n" +
"				double d = v.Number;\n" +
body +
"			}\n" +
"			return (" + type + ")v.ToObject(typeof(" + type + "));\n" +
"		}\n";
		}

		string Simple(string type, string name, string dataType, string read)
		{
			NoConv();
			return
"		internal static " + type + " " + name + "(global::MoonSharp.Interpreter.DynValue v)\n" +
"		{\n" +
"			if (v.Type == global::MoonSharp.Interpreter.DataType." + dataType + " && NoConv(global::MoonSharp.Interpreter.DataType." + dataType + ", typeof(" + type + ")))\n" +
"				return " + read + ";\n" +
"			return (" + type + ")v.ToObject(typeof(" + type + "));\n" +
"		}\n";
		}

		string DynValueHelper()
		{
			NoConv();
			return
"		internal static global::MoonSharp.Interpreter.DynValue ToDynValue(global::MoonSharp.Interpreter.DynValue v)\n" +
"		{\n" +
"			if (NoConv(v.Type, typeof(global::MoonSharp.Interpreter.DynValue)))\n" +
"				return v;\n" +
"			return (global::MoonSharp.Interpreter.DynValue)v.ToObject(typeof(global::MoonSharp.Interpreter.DynValue));\n" +
"		}\n";
		}

		string UserDataHelper(bool valueType)
		{
			NoConv();
			string name = valueType ? "ToUserDataValue" : "ToUserData";
			string constraint = valueType ? "struct" : "class";
			// IsTypeCompatible is what the reflection path asks too; the default is IsInstanceOfType.
			return
"		internal static T " + name + "<T>(global::MoonSharp.Interpreter.DynValue v) where T : " + constraint + "\n" +
"		{\n" +
"			if (v.Type == global::MoonSharp.Interpreter.DataType.UserData && NoConv(global::MoonSharp.Interpreter.DataType.UserData, typeof(T)))\n" +
"			{\n" +
"				object o = v.UserData.Object;\n" +
"				if (o is T && v.UserData.Descriptor.IsTypeCompatible(typeof(T), o))\n" +
"					return (T)o;\n" +
"			}\n" +
"			return (T)v.ToObject(typeof(T));\n" +
"		}\n";
		}

		string EnumHelperName(ITypeSymbol t)
		{
			string fq = Names.Of(t);
			string name;
			if (!m_EnumNames.TryGetValue(fq, out name))
			{
				name = "ToEnum" + m_EnumNames.Count + "_" + Names.Sanitize(t.Name);
				m_EnumNames[fq] = name;
			}
			return name;
		}

		string EnumHelper(INamedTypeSymbol t)
		{
			NoConv();
			string type = Names.Of(t);
			string under = Names.Of(t.EnumUnderlyingType);
			string min, max, convert, extra = null;

			switch (t.EnumUnderlyingType.SpecialType)
			{
				case SpecialType.System_Int32: min = "-2147483648.0"; max = "2147483647.0"; convert = "ToInt32"; break;
				case SpecialType.System_UInt32: min = "0.0"; max = "4294967295.0"; convert = "ToUInt32"; break;
				case SpecialType.System_Int16: min = "-32768.0"; max = "32767.0"; convert = "ToInt16"; break;
				case SpecialType.System_UInt16: min = "0.0"; max = "65535.0"; convert = "ToUInt16"; break;
				case SpecialType.System_SByte: min = "-128.0"; max = "127.0"; convert = "ToSByte"; break;
				case SpecialType.System_Byte: min = "0.0"; max = "255.0"; convert = "ToByte"; break;
				case SpecialType.System_Int64: min = "-9223372036854775808.0"; max = null; extra = "d < 9223372036854775808.0"; convert = "ToInt64"; break;
				default: min = "0.0"; max = null; extra = "d < 18446744073709551616.0"; convert = "ToUInt64"; break;
			}

			string tests = "d >= " + min + (max != null ? " && d <= " + max : "") + (extra != null ? " && " + extra : "");
			string name = EnumHelperName(t);

			return
"		internal static " + type + " " + name + "(global::MoonSharp.Interpreter.DynValue v)\n" +
"		{\n" +
"			if (v.Type == global::MoonSharp.Interpreter.DataType.Number && NoConv(global::MoonSharp.Interpreter.DataType.Number, typeof(" + type + ")))\n" +
"			{\n" +
"				double d = v.Number;\n" +
"				if (" + tests + ")\n" +
"					return (" + type + ")global::System.Convert." + convert + "(d);\n" +
"			}\n" +
"			return (" + type + ")v.ToObject(typeof(" + type + "));\n" +
"		}\n";
		}

		static string FromHelper(string type, string name, string make)
		{
			// TryObjectToSimpleDynValue: a custom converter for the CLR type wins.
			return
"		internal static global::MoonSharp.Interpreter.DynValue " + name + "(global::MoonSharp.Interpreter.Script script, " + type + " x)\n" +
"		{\n" +
"			var conv = global::MoonSharp.Interpreter.Script.GlobalOptions.CustomConverters.GetClrToScriptCustomConversion(typeof(" + type + "));\n" +
"			if (conv != null)\n" +
"			{\n" +
"				var r = conv(script, x);\n" +
"				if (r != null) return r;\n" +
"			}\n" +
"			return " + make + ";\n" +
"		}\n";
		}

		static string FromStringHelper()
		{
			return
"		internal static global::MoonSharp.Interpreter.DynValue FromString(global::MoonSharp.Interpreter.Script script, string x)\n" +
"		{\n" +
"			if (x == null) return global::MoonSharp.Interpreter.DynValue.Nil;\n" +
"			var conv = global::MoonSharp.Interpreter.Script.GlobalOptions.CustomConverters.GetClrToScriptCustomConversion(typeof(string));\n" +
"			if (conv != null)\n" +
"			{\n" +
"				var r = conv(script, x);\n" +
"				if (r != null) return r;\n" +
"			}\n" +
"			return global::MoonSharp.Interpreter.DynValue.NewString(x);\n" +
"		}\n";
		}
	}
}
