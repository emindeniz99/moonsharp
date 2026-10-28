using System;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MoonSharp.SourceGenerator
{
	static class Names
	{
		/// <summary>global::-qualified names with C# keywords for special types, without nullable annotations.</summary>
		public static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat
			.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

		public static string Of(ITypeSymbol t)
		{
			return t.ToDisplayString(Fq);
		}

		public static string Sanitize(string s)
		{
			var sb = new StringBuilder(s.Length);
			foreach (char c in s)
				sb.Append(char.IsLetterOrDigit(c) ? c : '_');
			return sb.ToString();
		}

		public static string Escape(string identifier)
		{
			return SyntaxFacts.GetKeywordKind(identifier) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None ? "@" + identifier : identifier;
		}

		public static string Literal(string s)
		{
			return SymbolDisplay.FormatLiteral(s, true);
		}

		/// <summary>System.Type.Name of a type: the metadata name, with [] for arrays.</summary>
		public static string ClrName(ITypeSymbol t)
		{
			var arr = t as IArrayTypeSymbol;
			if (arr != null)
				return ClrName(arr.ElementType) + (arr.Rank == 1 ? "[]" : "[" + new string(',', arr.Rank - 1) + "]");
			return t.MetadataName;
		}

		/// <summary>DescriptorHelpers.GetConversionMethodName: "__to" plus the type name with non-alphanumerics as '_'.</summary>
		public static string ConversionMethodName(ITypeSymbol returnType)
		{
			return "__to" + Sanitize(ClrName(returnType));
		}

		/// <summary>The C# expression format for an operator method, or null when C# cannot call it directly.</summary>
		public static string OperatorSyntax(string metadataName, int arity)
		{
			if (arity == 2)
			{
				switch (metadataName)
				{
					case "op_Addition": return "({0} + {1})";
					case "op_Subtraction": return "({0} - {1})";
					case "op_Multiply": return "({0} * {1})";
					case "op_Division": return "({0} / {1})";
					case "op_Modulus": return "({0} % {1})";
					case "op_BitwiseAnd": return "({0} & {1})";
					case "op_BitwiseOr": return "({0} | {1})";
					case "op_ExclusiveOr": return "({0} ^ {1})";
					case "op_LeftShift": return "({0} << {1})";
					case "op_RightShift": return "({0} >> {1})";
					case "op_Equality": return "({0} == {1})";
					case "op_Inequality": return "({0} != {1})";
					case "op_LessThan": return "({0} < {1})";
					case "op_GreaterThan": return "({0} > {1})";
					case "op_LessThanOrEqual": return "({0} <= {1})";
					case "op_GreaterThanOrEqual": return "({0} >= {1})";
				}
			}
			else if (arity == 1)
			{
				switch (metadataName)
				{
					case "op_UnaryNegation": return "(-{0})";
					case "op_UnaryPlus": return "(+{0})";
					case "op_LogicalNot": return "(!{0})";
					case "op_OnesComplement": return "(~{0})";
				}
			}

			return null;
		}
	}

	/// <summary>C# text for parameter default values.</summary>
	static class Literals
	{
		public static bool CanWriteDefault(IParameterSymbol p)
		{
			return Default(p) != null;
		}

		/// <summary>The default value of an optional parameter as a C# expression of the parameter type, or null.</summary>
		public static string Default(IParameterSymbol p)
		{
			string type = Names.Of(p.Type);

			if (!p.HasExplicitDefaultValue || p.ExplicitDefaultValue == null)
				return "default(" + type + ")";

			object v = p.ExplicitDefaultValue;
			string lit = Primitive(v);

			if (lit == null)
				return null;

			return "(" + type + ")(" + lit + ")";
		}

		static string Primitive(object v)
		{
			var inv = CultureInfo.InvariantCulture;

			if (v is string) return Names.Literal((string)v);
			if (v is bool) return (bool)v ? "true" : "false";
			if (v is char) return SymbolDisplay.FormatLiteral((char)v, true);
			if (v is double) return Floating((double)v, "D", "double");
			if (v is float) return Floating((float)v, "F", "float");
			if (v is decimal) return ((decimal)v).ToString(inv) + "M";
			if (v is long) return Signed((long)v, "L");
			if (v is ulong) return ((ulong)v).ToString(inv) + "UL";
			if (v is int) return Signed((int)v, "");
			if (v is uint) return ((uint)v).ToString(inv) + "U";
			if (v is short) return Signed((short)v, "");
			if (v is ushort) return ((ushort)v).ToString(inv);
			if (v is sbyte) return Signed((sbyte)v, "");
			if (v is byte) return ((byte)v).ToString(inv);
			return null;
		}

		static string Signed(long v, string suffix)
		{
			if (v == long.MinValue)
				return "long.MinValue";
			return v.ToString(CultureInfo.InvariantCulture) + suffix;
		}

		static string Floating(double v, string suffix, string keyword)
		{
			if (double.IsNaN(v)) return keyword + ".NaN";
			if (double.IsPositiveInfinity(v)) return keyword + ".PositiveInfinity";
			if (double.IsNegativeInfinity(v)) return keyword + ".NegativeInfinity";
			return v.ToString("R", CultureInfo.InvariantCulture) + suffix;
		}
	}
}
