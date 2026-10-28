using System;
using System.Globalization;
using System.Text;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Turns numbers into text the same way on every runtime. .NET Core 3.0 and later print the shortest
	/// text that parses back to the same double; .NET Framework, Mono and Unity (Mono and IL2CPP) stop at
	/// 15 significant digits and print "0" for negative zero, so the same script printed 9007199254740992
	/// on one host and 9.00719925474099E+15 on another. This reproduces .NET Core's output everywhere.
	/// </summary>
	internal static class NumberFormatting
	{
		const double SmallestNormal = 2.2250738585072014E-308;

		/// <summary>
		/// .NET Core's shortest round-trip text in the invariant culture: the fewest significant digits that
		/// parse back to <paramref name="d"/>, written without an exponent when the decimal exponent is
		/// between -5 and 17 (exclusive), otherwise as 1.5E+17. For example 0.30000000000000004,
		/// 10000000000000000, 1E+17, 1E-05, -0.
		/// </summary>
		internal static string ToShortestString(double d)
		{
			if (d == 0)
				return (1 / d) < 0 ? "-0" : "0";

			if (double.IsNaN(d) || double.IsInfinity(d))
				return d.ToString(CultureInfo.InvariantCulture);

			if (d > -1e15 && d < 1e15 && Math.Floor(d) == d)
				return ((long)d).ToString(CultureInfo.InvariantCulture);

			// Scientific text with the shortest precision, for example -7.598786874487429E+016.
			string sci = Scientific(d, ShortestPrecision(d));
			int ePos = sci.IndexOf('E');
			int start = sci[0] == '-' ? 1 : 0;
			string digits = sci.Substring(start, ePos - start).Replace(".", "").TrimEnd('0');
			int exponent = int.Parse(sci.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

			StringBuilder sb = new StringBuilder(digits.Length + 8);
			if (start == 1)
				sb.Append('-');

			if (exponent > -5 && exponent < 17)
			{
				if (exponent < 0)
				{
					sb.Append("0.").Append('0', -exponent - 1).Append(digits);
				}
				else if (digits.Length <= exponent + 1)
				{
					sb.Append(digits).Append('0', exponent + 1 - digits.Length);
				}
				else
				{
					sb.Append(digits, 0, exponent + 1).Append('.').Append(digits, exponent + 1, digits.Length - exponent - 1);
				}
			}
			else
			{
				sb.Append(digits[0]);
				if (digits.Length > 1)
					sb.Append('.').Append(digits, 1, digits.Length - 1);
				sb.Append('E').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture));
			}

			return sb.ToString();
		}

		// The fewest significant digits whose rounding reads back as d. For normal numbers a 15-digit
		// rounding already ends in zeros when fewer digits suffice; subnormals carry fewer digits, so a
		// 15-digit rounding can round-trip without being the shortest (5E-324).
		static int ShortestPrecision(double d)
		{
			int from = (d > -SmallestNormal && d < SmallestNormal) ? 1 : 15;

			for (int precision = from; precision < 17; precision++)
			{
				if (RoundTrips(Scientific(d, precision), d))
					return precision;
			}

			return 17;
		}

		static string Scientific(double d, int precision)
		{
			return d.ToString("E" + (precision - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		}

		// TryParse, not Parse: Mono and .NET Framework throw on a rounding above double.MaxValue
		// (1.79769313486232E+308) where .NET Core returns infinity.
		static bool RoundTrips(string s, double d)
		{
			double r;
			return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r) && r == d;
		}
	}
}
