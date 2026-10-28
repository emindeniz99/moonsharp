using Microsoft.CodeAnalysis;

namespace MoonSharp.SourceGenerator
{
	/// <summary>The MoonSharp and BCL symbols the generator compares against.</summary>
	sealed class KnownSymbols
	{
		public INamedTypeSymbol UserDataAttribute, UserDataTypesAttribute, HiddenAttribute, VisibleAttribute,
			HideMemberAttribute, MetamethodAttribute, UserDataType;
		public INamedTypeSymbol DynValue, Script, ScriptExecutionContext, CallbackArguments, Table, Closure;
		public INamedTypeSymbol Object, ValueType, Delegate, ExtensionAttribute;

		public static KnownSymbols Create(Compilation c)
		{
			var k = new KnownSymbols
			{
				UserDataAttribute = c.GetTypeByMetadataName("MoonSharp.Interpreter.MoonSharpUserDataAttribute"),
				UserDataTypesAttribute = c.GetTypeByMetadataName("MoonSharp.Interpreter.MoonSharpUserDataTypesAttribute"),
				HiddenAttribute = c.GetTypeByMetadataName("MoonSharp.Interpreter.MoonSharpHiddenAttribute"),
				VisibleAttribute = c.GetTypeByMetadataName("MoonSharp.Interpreter.Interop.MoonSharpVisibleAttribute"),
				HideMemberAttribute = c.GetTypeByMetadataName("MoonSharp.Interpreter.MoonSharpHideMemberAttribute"),
				MetamethodAttribute = c.GetTypeByMetadataName("MoonSharp.Interpreter.MoonSharpUserDataMetamethodAttribute"),
				UserDataType = c.GetTypeByMetadataName("MoonSharp.Interpreter.Interop.IUserDataType"),
				DynValue = c.GetTypeByMetadataName("MoonSharp.Interpreter.DynValue"),
				Script = c.GetTypeByMetadataName("MoonSharp.Interpreter.Script"),
				ScriptExecutionContext = c.GetTypeByMetadataName("MoonSharp.Interpreter.ScriptExecutionContext"),
				CallbackArguments = c.GetTypeByMetadataName("MoonSharp.Interpreter.CallbackArguments"),
				Table = c.GetTypeByMetadataName("MoonSharp.Interpreter.Table"),
				Closure = c.GetTypeByMetadataName("MoonSharp.Interpreter.Closure"),
				Object = c.GetSpecialType(SpecialType.System_Object),
				ValueType = c.GetSpecialType(SpecialType.System_ValueType),
				Delegate = c.GetSpecialType(SpecialType.System_Delegate),
				ExtensionAttribute = c.GetTypeByMetadataName("System.Runtime.CompilerServices.ExtensionAttribute"),
			};

			// Without the interpreter (or with one that lacks these types) there is nothing to generate
			// against. MoonSharpUserDataTypesAttribute is optional: older interpreters do not have it.
			INamedTypeSymbol[] required = { k.UserDataAttribute, k.HiddenAttribute, k.VisibleAttribute, k.HideMemberAttribute,
				k.MetamethodAttribute, k.UserDataType, k.DynValue, k.Script, k.ScriptExecutionContext, k.CallbackArguments, k.Table, k.Closure };

			foreach (INamedTypeSymbol s in required)
				if (s == null)
					return null;

			return k;
		}

		public static bool Is(ITypeSymbol a, ITypeSymbol b)
		{
			return b != null && SymbolEqualityComparer.Default.Equals(a, b);
		}
	}
}
