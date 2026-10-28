namespace System.Diagnostics.CodeAnalysis
{
	// The trimming annotations of .NET 5+, declared here because netstandard2.0 and net45 lack them.
	// The .NET trimmer and NativeAOT match them by name, so members that interop finds by reflection
	// survive. Only the values used in this assembly are declared; they must equal the platform's.
	[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	internal sealed class DynamicDependencyAttribute : Attribute
	{
		public DynamicDependencyAttribute(DynamicallyAccessedMemberTypes memberTypes, Type type)
		{
			MemberTypes = memberTypes;
			Type = type;
		}

		public DynamicallyAccessedMemberTypes MemberTypes { get; private set; }
		public Type Type { get; private set; }
	}

	[Flags]
	internal enum DynamicallyAccessedMemberTypes
	{
		PublicParameterlessConstructor = 0x0001,
	}
}
