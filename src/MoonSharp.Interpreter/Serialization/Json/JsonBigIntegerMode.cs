namespace MoonSharp.Interpreter.Serialization.Json
{
	/// <summary>
	/// How JSON parsing represents integer literals that a number (a double) cannot hold exactly,
	/// that is, those outside +/-(2^53-1). Literals with a fraction or an exponent are always numbers.
	/// </summary>
	public enum JsonBigIntegerMode
	{
		/// <summary>
		/// Parse them as numbers, rounding to the nearest double. The default, and the behaviour
		/// before the other modes existed.
		/// </summary>
		Number = 0,
		/// <summary>
		/// Parse them as int64 userdata, or uint64 above the int64 range. A literal that fits
		/// neither is a parse error.
		/// </summary>
		Int64,
		/// <summary>
		/// Parse them as strings holding the decimal digits.
		/// </summary>
		String,
	}
}
