using System;
using System.Globalization;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Turns numbers into text the same way on every runtime. .NET Core 3.0 and later already print the
	/// shortest text that parses back to the same double; .NET Framework, Mono and Unity (Mono and IL2CPP)
	/// stop at 15 significant digits and print "0" for negative zero, so the same script printed
	/// 9007199254740992 on one host and 9.00719925474099E+15 on another.
	/// </summary>
	internal static class NumberFormatting
	{
		const double SmallestNormal = 2.2250738585072014E-308;

		/// <summary>
		/// The invariant-culture "G" text with the fewest significant digits that parses back to
		/// <paramref name="d"/> (for example 0.30000000000000004, 1E+15, -0). The digits match .NET Core's
		/// shortest round-trip output; the layout follows "G" for that digit count on every runtime.
		/// </summary>
		internal static string ToShortestString(double d)
		{
			if (d == 0)
				return (1 / d) < 0 ? "-0" : "0";

			if (double.IsNaN(d) || double.IsInfinity(d))
				return d.ToString(CultureInfo.InvariantCulture);

			if (d > -1e15 && d < 1e15 && Math.Floor(d) == d)
				return ((long)d).ToString(CultureInfo.InvariantCulture);

			// Subnormals carry fewer than 15 significant digits, so a 15-digit rounding can round-trip
			// without being the shortest text (4.94065645841247E-324 instead of 5E-324).
			if (d > -SmallestNormal && d < SmallestNormal)
			{
				for (int digits = 1; digits < 17; digits++)
				{
					string t = d.ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
					if (RoundTrips(t, d))
						return t;
				}
				return d.ToString("G17", CultureInfo.InvariantCulture);
			}

			string s = d.ToString("G15", CultureInfo.InvariantCulture);
			if (RoundTrips(s, d))
				return s;

			s = d.ToString("G16", CultureInfo.InvariantCulture);
			if (RoundTrips(s, d))
				return s;

			return d.ToString("G17", CultureInfo.InvariantCulture);
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
