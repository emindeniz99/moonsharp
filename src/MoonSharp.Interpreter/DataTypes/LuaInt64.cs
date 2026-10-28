using System;
using System.Globalization;
using MoonSharp.Interpreter.Interop;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// An exact signed 64-bit integer as seen by scripts: the <c>int64</c> userdata.
	/// Lua 5.2 numbers are doubles and hold integers exactly only up to 2^53-1, so values such
	/// as 64-bit ids and bitfields are carried in this wrapper instead.
	/// Instances are immutable, compare and hash by value, and can be used as table keys.
	/// </summary>
	public sealed class LuaInt64 : IEquatable<LuaInt64>
	{
		private readonly long m_Value;

		/// <summary>
		/// Initializes a new instance wrapping the given value.
		/// </summary>
		public LuaInt64(long value)
		{
			m_Value = value;
		}

		/// <summary>
		/// Gets the wrapped value.
		/// </summary>
		public long Value
		{
			get { return m_Value; }
		}

		/// <summary>
		/// Creates a script value (an <c>int64</c> userdata) holding the given integer.
		/// </summary>
		public static DynValue NewDynValue(long value)
		{
			return UserData.Create(new LuaInt64(value), ExactIntegerDescriptor.Int64);
		}

		/// <summary>
		/// Creates a script value (an <c>int64</c> userdata) holding this integer.
		/// </summary>
		public DynValue ToDynValue()
		{
			return UserData.Create(this, ExactIntegerDescriptor.Int64);
		}

		/// <summary>
		/// Returns true if <paramref name="other"/> holds the same value.
		/// </summary>
		public bool Equals(LuaInt64 other)
		{
			return other != null && other.m_Value == m_Value;
		}

		/// <summary>
		/// Returns true if <paramref name="obj"/> is a <see cref="LuaInt64"/> holding the same value.
		/// A <see cref="LuaUInt64"/> is never equal to a <see cref="LuaInt64"/>, even with the same
		/// numeric value, matching how the two are kept apart as table keys.
		/// </summary>
		public override bool Equals(object obj)
		{
			return Equals(obj as LuaInt64);
		}

		/// <summary>
		/// Returns a hash code derived from the value.
		/// </summary>
		public override int GetHashCode()
		{
			return m_Value.GetHashCode();
		}

		/// <summary>
		/// Returns the value as a decimal string, in the invariant culture.
		/// </summary>
		public override string ToString()
		{
			return m_Value.ToString(CultureInfo.InvariantCulture);
		}
	}

	/// <summary>
	/// An exact unsigned 64-bit integer as seen by scripts: the <c>uint64</c> userdata.
	/// See <see cref="LuaInt64"/> for why it exists.
	/// Instances are immutable, compare and hash by value, and can be used as table keys.
	/// </summary>
	public sealed class LuaUInt64 : IEquatable<LuaUInt64>
	{
		private readonly ulong m_Value;

		/// <summary>
		/// Initializes a new instance wrapping the given value.
		/// </summary>
		public LuaUInt64(ulong value)
		{
			m_Value = value;
		}

		/// <summary>
		/// Gets the wrapped value.
		/// </summary>
		public ulong Value
		{
			get { return m_Value; }
		}

		/// <summary>
		/// Creates a script value (a <c>uint64</c> userdata) holding the given integer.
		/// </summary>
		public static DynValue NewDynValue(ulong value)
		{
			return UserData.Create(new LuaUInt64(value), ExactIntegerDescriptor.UInt64);
		}

		/// <summary>
		/// Creates a script value (a <c>uint64</c> userdata) holding this integer.
		/// </summary>
		public DynValue ToDynValue()
		{
			return UserData.Create(this, ExactIntegerDescriptor.UInt64);
		}

		/// <summary>
		/// Returns true if <paramref name="other"/> holds the same value.
		/// </summary>
		public bool Equals(LuaUInt64 other)
		{
			return other != null && other.m_Value == m_Value;
		}

		/// <summary>
		/// Returns true if <paramref name="obj"/> is a <see cref="LuaUInt64"/> holding the same value.
		/// </summary>
		public override bool Equals(object obj)
		{
			return Equals(obj as LuaUInt64);
		}

		/// <summary>
		/// Returns a hash code derived from the value.
		/// </summary>
		public override int GetHashCode()
		{
			return m_Value.GetHashCode();
		}

		/// <summary>
		/// Returns the value as a decimal string, in the invariant culture.
		/// </summary>
		public override string ToString()
		{
			return m_Value.ToString(CultureInfo.InvariantCulture);
		}
	}
}
