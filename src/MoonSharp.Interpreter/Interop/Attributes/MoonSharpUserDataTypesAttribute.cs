using System;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Lists types, typically ones you do not own, for which MoonSharp.SourceGenerator writes userdata
	/// descriptors, as it does for types marked with <see cref="MoonSharpUserDataAttribute"/>.
	/// Only the source generator reads it, at compile time.
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class MoonSharpUserDataTypesAttribute : Attribute
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MoonSharpUserDataTypesAttribute"/> class.
		/// </summary>
		/// <param name="types">The types to generate descriptors for.</param>
		public MoonSharpUserDataTypesAttribute(params Type[] types)
		{
		}
	}
}
