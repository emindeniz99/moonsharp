using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MoonSharp.SourceGenerator
{
	/// <summary>
	/// Writes a MoonSharp userdata descriptor for every type marked [MoonSharpUserData] in the
	/// compilation and every type listed with [assembly: MoonSharpUserDataTypes(...)], plus a
	/// GeneratedUserData class that registers them. See docs/source-generator.md.
	/// </summary>
	[Generator]
	public sealed class UserDataGenerator : ISourceGenerator
	{
		public void Initialize(GeneratorInitializationContext context)
		{
			context.RegisterForSyntaxNotifications(() => new Receiver());
		}

		/// <summary>Collects class and struct declarations that carry attributes.</summary>
		sealed class Receiver : ISyntaxReceiver
		{
			public readonly List<TypeDeclarationSyntax> Candidates = new List<TypeDeclarationSyntax>();

			public void OnVisitSyntaxNode(SyntaxNode node)
			{
				var decl = node as TypeDeclarationSyntax;

				if (decl != null && decl.AttributeLists.Count > 0 && (decl is ClassDeclarationSyntax || decl is StructDeclarationSyntax))
					Candidates.Add(decl);
			}
		}

		public void Execute(GeneratorExecutionContext context)
		{
			Compilation compilation = context.Compilation;

			// The interpreter itself is compiled from source in Unity next to the generator.
			if (compilation.AssemblyName == "MoonSharp.Interpreter")
				return;

			KnownSymbols known = KnownSymbols.Create(compilation);

			if (known == null)
				return;

			var targets = new List<Target>();
			var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
			var receiver = (Receiver)context.SyntaxReceiver;

			foreach (TypeDeclarationSyntax decl in receiver.Candidates)
			{
				context.CancellationToken.ThrowIfCancellationRequested();

				SemanticModel model = compilation.GetSemanticModel(decl.SyntaxTree);
				var type = model.GetDeclaredSymbol(decl) as INamedTypeSymbol;

				if (type == null || seen.Contains(type))
					continue;

				AttributeData attr = type.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.UserDataAttribute));

				if (attr == null)
					continue;

				seen.Add(type);
				targets.Add(new Target(type, AccessModeOf(attr), decl.GetLocation()));
			}

			foreach (AttributeData attr in compilation.Assembly.GetAttributes())
			{
				if (known.UserDataTypesAttribute == null || !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, known.UserDataTypesAttribute))
					continue;

				Location location = attr.ApplicationSyntaxReference != null ? attr.ApplicationSyntaxReference.GetSyntax(context.CancellationToken).GetLocation() : Location.None;

				foreach (TypedConstant arg in attr.ConstructorArguments)
				{
					IEnumerable<TypedConstant> values = arg.Kind == TypedConstantKind.Array ? arg.Values : new[] { arg };

					foreach (TypedConstant value in values)
					{
						var type = value.Value as INamedTypeSymbol;

						if (type != null && seen.Add(type))
							targets.Add(new Target(type, DefaultAccessMode, location));
					}
				}
			}

			if (targets.Count == 0)
				return;

			// Stable output whatever the declaration order.
			targets.Sort((a, b) => string.CompareOrdinal(a.Type.ToDisplayString(Names.Fq), b.Type.ToDisplayString(Names.Fq)));

			var plans = new List<TypePlan>();
			var registerByReflection = new List<Target>();

			for (int i = 0; i < targets.Count; i++)
			{
				Target t = targets[i];
				string reason = TypePlan.WhyNotGenerated(t.Type, compilation, known);

				if (reason != null)
				{
					// Enums and IUserDataType types get MoonSharp's own descriptors, which call no member by reflection.
					if (!TypePlan.IsStandardRegistration(t.Type, known))
						context.ReportDiagnostic(Diagnostic.Create(Diagnostics.TypeNotGenerated, t.Location, t.Type.ToDisplayString(), reason));

					if (compilation.IsSymbolAccessibleWithin(t.Type, compilation.Assembly))
						registerByReflection.Add(t);

					continue;
				}

				plans.Add(TypePlan.Build(t, plans.Count, compilation, known, context));
			}

			var helpers = new Helpers(known);

			foreach (TypePlan plan in plans)
			{
				context.CancellationToken.ThrowIfCancellationRequested();
				string source = Emitter.EmitType(plan, plans, helpers, known);
				context.AddSource(plan.HintName, SourceText.From(source, Encoding.UTF8));

				if (plan.StructAccess)
					context.AddSource(plan.HintName.Replace(".g.cs", ".Struct.g.cs"), SourceText.From(Emitter.EmitStructPartial(plan, helpers, known), Encoding.UTF8));
			}

			context.AddSource("MoonSharp.GeneratedUserData.g.cs", SourceText.From(Emitter.EmitRegistry(plans, registerByReflection, helpers), Encoding.UTF8));
		}

		/// <summary>InteropAccessMode.Default.</summary>
		const int DefaultAccessMode = 7;

		static int AccessModeOf(AttributeData attr)
		{
			foreach (var named in attr.NamedArguments)
				if (named.Key == "AccessMode" && named.Value.Value is int)
					return (int)named.Value.Value;

			return DefaultAccessMode;
		}
	}

	/// <summary>A type to generate, with the InteropAccessMode its attribute asks for.</summary>
	sealed class Target
	{
		public readonly INamedTypeSymbol Type;
		public readonly int AccessMode;
		public readonly Location Location;

		public Target(INamedTypeSymbol type, int accessMode, Location location)
		{
			Type = type;
			AccessMode = accessMode;
			Location = location;
		}
	}

	static class Diagnostics
	{
		public static readonly DiagnosticDescriptor MemberByReflection = new DiagnosticDescriptor(
			"MSUD001",
			"Member uses reflection",
			"{0}.{1} is bound by reflection instead of generated code: {2}",
			"MoonSharp.SourceGenerator",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true,
			description: "The member keeps working through MoonSharp's reflection descriptor. On IL2CPP and NativeAOT keep it from being stripped (link.xml or a trimming descriptor).");

		public static readonly DiagnosticDescriptor TypeNotGenerated = new DiagnosticDescriptor(
			"MSUD002",
			"Type has no generated descriptor",
			"No descriptor is generated for {0}: {1}",
			"MoonSharp.SourceGenerator",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true);
	}
}
