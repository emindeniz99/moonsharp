using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MoonSharp.SourceGenerator
{
	/// <summary>How generated code reaches the instance a member works on.</summary>
	enum Receiver
	{
		/// <summary>Static member: no instance.</summary>
		Static,
		/// <summary>A reference to the object: <c>((T)obj)</c> for classes and interfaces, or a call on the box
		/// through a base class or interface of a struct, which works on the boxed value in place.</summary>
		Reference,
		/// <summary>A copy of a boxed struct, for members that do not change it.</summary>
		Copy,
		/// <summary>Through the generated interface on a partial struct, which works on the box in place.</summary>
		StructAccess,
	}

	sealed class MethodPlan
	{
		public IMethodSymbol Symbol;
		/// <summary>The member name scripts use (conversion operators get MoonSharp's __toX names).</summary>
		public string Name;
		public List<string> MetaNames = new List<string>();
		public bool IsConstructor;
		public string Fallback;
		public Receiver Receiver;
		/// <summary>The type the receiver is cast to (for <see cref="Receiver.Reference"/>).</summary>
		public ITypeSymbol ReceiverType;
		public int Id;
		/// <summary>Position in the struct's generated interface switch, for <see cref="Receiver.StructAccess"/>.</summary>
		public int StructId = -1;
	}

	sealed class ValuePlan
	{
		public ISymbol Symbol; // IPropertySymbol or IFieldSymbol
		public string Name;
		public ITypeSymbol ValueType;
		public bool IsStatic, CanRead, CanWrite, IsConst;
		/// <summary>The whole member goes to reflection.</summary>
		public string Fallback;
		/// <summary>Only writing goes to reflection (a struct that cannot be changed in place).</summary>
		public string SetFallback;
		public Receiver GetReceiver, SetReceiver;
		public ITypeSymbol ReceiverType;
		public int Id;
		public int StructGetId = -1, StructSetId = -1;
	}

	sealed class NestedPlan
	{
		public INamedTypeSymbol Type;
	}

	/// <summary>
	/// Everything generated for one type. Members are listed in the order, and by the rules, that
	/// StandardUserDataDescriptor.FillMemberList uses with reflection, so both descriptors expose the
	/// same members under the same names.
	/// </summary>
	sealed class TypePlan
	{
		public INamedTypeSymbol Type;
		public string ClassName, HintName;
		public bool HideMembers;
		public bool StructAccess;
		public List<MethodPlan> Constructors = new List<MethodPlan>();
		public bool ValueTypeDefaultCtor;
		public List<MethodPlan> Methods = new List<MethodPlan>();
		public List<ValuePlan> Properties = new List<ValuePlan>();
		public List<ValuePlan> Fields = new List<ValuePlan>();
		public List<IEventSymbol> Events = new List<IEventSymbol>();
		public List<NestedPlan> Nested = new List<NestedPlan>();
		public int MethodCount, ValueCount, StructMethodCount, StructValueCount;

		/// <summary>Null when a descriptor can be generated, else why not.</summary>
		public static string WhyNotGenerated(INamedTypeSymbol type, Compilation compilation, KnownSymbols known)
		{
			if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
				return "it is not accessible from generated code";
			if (type.TypeKind == TypeKind.Enum)
				return "enums use MoonSharp's standard enum descriptor";
			if (type.TypeKind == TypeKind.Delegate)
				return "delegate types are not generated";
			if (type.IsUnboundGenericType || type.TypeParameters.Length > 0 && type.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter))
				return "open generic types are not generated; list closed types with [assembly: MoonSharpUserDataTypes(typeof(...))]";
			if (type.TypeKind == TypeKind.Array || type.TypeKind == TypeKind.Pointer)
				return "arrays and pointers are not generated";
			if (known.UserDataType != null && type.AllInterfaces.Any(i => KnownSymbols.Is(i, known.UserDataType)))
				return "IUserDataType implementations describe themselves";
			if (type.IsRefLikeType)
				return "ref struct types cannot be boxed into a userdata";
			return null;
		}

		/// <summary>Whether a type that is not generated still gets a standard registration without a warning.</summary>
		public static bool IsStandardRegistration(INamedTypeSymbol type, KnownSymbols known)
		{
			return type.TypeKind == TypeKind.Enum
				|| (known.UserDataType != null && type.AllInterfaces.Any(i => KnownSymbols.Is(i, known.UserDataType)));
		}

		public static TypePlan Build(Target target, int index, Compilation compilation, KnownSymbols known, GeneratorExecutionContext context)
		{
			INamedTypeSymbol type = target.Type;
			var plan = new TypePlan
			{
				Type = type,
				ClassName = "D" + index + "_" + Names.Sanitize(type.Name),
				HintName = "MoonSharp.UserData." + index + "." + Names.Sanitize(type.ToDisplayString()) + ".g.cs",
				// InteropAccessMode.HideMembers = 5
				HideMembers = target.AccessMode == 5,
			};

			plan.StructAccess = type.IsValueType && IsPartialChain(type);

			if (plan.HideMembers)
				return plan;

			var builder = new Builder(plan, compilation, known, context, target.Location);
			builder.Fill();
			return plan;
		}

		/// <summary>Whether the type and every type containing it are declared partial in this compilation.</summary>
		static bool IsPartialChain(INamedTypeSymbol type)
		{
			for (INamedTypeSymbol t = type; t != null; t = t.ContainingType)
			{
				if (t.TypeKind != TypeKind.Class && t.TypeKind != TypeKind.Struct)
					return false;

				bool partial = t.DeclaringSyntaxReferences.Any(r =>
				{
					var decl = r.GetSyntax() as TypeDeclarationSyntax;
					return decl != null && (decl is ClassDeclarationSyntax || decl is StructDeclarationSyntax)
						&& decl.Modifiers.Any(m => m.Text == "partial");
				});

				if (!partial)
					return false;
			}

			return true;
		}

		sealed class Builder
		{
			readonly TypePlan m_Plan;
			readonly INamedTypeSymbol m_Type;
			readonly Compilation m_Compilation;
			readonly KnownSymbols m_Known;
			readonly GeneratorExecutionContext m_Context;
			readonly Location m_Location;
			readonly HashSet<string> m_Hidden;

			public Builder(TypePlan plan, Compilation compilation, KnownSymbols known, GeneratorExecutionContext context, Location location)
			{
				m_Plan = plan;
				m_Type = plan.Type;
				m_Compilation = compilation;
				m_Known = known;
				m_Context = context;
				m_Location = location;

				// [MoonSharpHideMember] is inherited.
				m_Hidden = new HashSet<string>();
				for (INamedTypeSymbol t = m_Type; t != null; t = t.BaseType)
					foreach (AttributeData a in t.GetAttributes())
						if (KnownSymbols.Is(a.AttributeClass, known.HideMemberAttribute) && a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string)
							m_Hidden.Add((string)a.ConstructorArguments[0].Value);
			}

			public void Fill()
			{
				List<ISymbol> members = ReflectionMembers().ToList();
				bool isDelegate = m_Type.TypeKind == TypeKind.Delegate;

				if (!isDelegate)
				{
					if (!m_Hidden.Contains("__new"))
					{
						foreach (IMethodSymbol ctor in m_Type.InstanceConstructors)
						{
							// A struct's implicit parameterless constructor is not in metadata.
							if (ctor.IsImplicitlyDeclared && m_Type.IsValueType)
								continue;

							if (!IsCompatible(ctor) || !(Visibility(ctor) ?? ctor.DeclaredAccessibility == Accessibility.Public))
								continue;

							m_Plan.Constructors.Add(PlanMethod(ctor, "__new", true));
						}

						if (m_Type.IsValueType)
							m_Plan.ValueTypeDefaultCtor = true;
					}
				}

				foreach (IMethodSymbol mi in members.OfType<IMethodSymbol>())
				{
					if (mi.MethodKind == MethodKind.Constructor || mi.MethodKind == MethodKind.StaticConstructor)
						continue;

					string name = mi.MetadataName;

					if (m_Hidden.Contains(name) || !IsCompatible(mi) || !(Visibility(mi) ?? mi.DeclaredAccessibility == Accessibility.Public))
						continue;

					if (mi.MethodKind == MethodKind.Conversion)
						name = Names.ConversionMethodName(mi.ReturnType);

					MethodPlan mp = PlanMethod(mi, name, false);

					foreach (AttributeData a in InheritedAttributes(mi))
						if (KnownSymbols.Is(a.AttributeClass, m_Known.MetamethodAttribute) && a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string)
							mp.MetaNames.Add((string)a.ConstructorArguments[0].Value);

					m_Plan.Methods.Add(mp);
				}

				foreach (IPropertySymbol pi in members.OfType<IPropertySymbol>())
				{
					if (pi.IsIndexer || pi.Parameters.Length > 0 || m_Hidden.Contains(pi.MetadataName))
						continue;

					ValuePlan vp = PlanProperty(pi);

					if (vp != null)
						m_Plan.Properties.Add(vp);
				}

				foreach (IFieldSymbol fi in members.OfType<IFieldSymbol>())
				{
					if (m_Hidden.Contains(fi.MetadataName) || !(Visibility(fi) ?? fi.DeclaredAccessibility == Accessibility.Public))
						continue;

					m_Plan.Fields.Add(PlanField(fi));
				}

				foreach (IEventSymbol ei in members.OfType<IEventSymbol>())
				{
					if (m_Hidden.Contains(ei.MetadataName))
						continue;

					m_Plan.Events.Add(ei);

					bool visible = Visibility(ei) ?? (ei.AddMethod != null && ei.AddMethod.DeclaredAccessibility == Accessibility.Public
						&& ei.RemoveMethod != null && ei.RemoveMethod.DeclaredAccessibility == Accessibility.Public);

					if (visible && !m_Type.IsValueType)
						Report(ei.MetadataName, "events are bound by MoonSharp's event descriptor");
				}

				foreach (INamedTypeSymbol nested in m_Type.GetTypeMembers())
				{
					if (m_Hidden.Contains(nested.MetadataName) || nested.IsGenericType && nested.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter))
						continue;

					bool hasAttr = nested.GetAttributes().Any(a => KnownSymbols.Is(a.AttributeClass, m_Known.UserDataAttribute));

					if (nested.DeclaredAccessibility == Accessibility.Public || hasAttr)
						m_Plan.Nested.Add(new NestedPlan { Type = nested });
				}

				AssignIds();
			}

			void AssignIds()
			{
				foreach (MethodPlan m in m_Plan.Constructors.Concat(m_Plan.Methods))
				{
					m.Id = m_Plan.MethodCount++;
					if (m.Fallback == null && m.Receiver == Receiver.StructAccess)
						m.StructId = m_Plan.StructMethodCount++;
				}

				foreach (ValuePlan v in m_Plan.Properties.Concat(m_Plan.Fields))
				{
					v.Id = m_Plan.ValueCount++;
					if (v.Fallback != null)
						continue;
					if (v.CanRead && v.GetReceiver == Receiver.StructAccess)
						v.StructGetId = m_Plan.StructValueCount++;
					if (v.CanWrite && v.SetFallback == null && v.SetReceiver == Receiver.StructAccess)
						v.StructSetId = m_Plan.StructValueCount++;
				}
			}

			/// <summary>
			/// The members StandardUserDataDescriptor reads by reflection: everything declared on the type,
			/// plus the non-private instance members of its base classes and their non-private static
			/// members, except statics declared in the core library (upstream #175). Overridden members
			/// appear once, as the override; a base property or event hidden by name (and index parameters),
			/// and a base static method or field hidden by a derived one, are left out, as reflection does.
			/// </summary>
			IEnumerable<ISymbol> ReflectionMembers()
			{
				var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
				var propertyNames = new HashSet<string>();
				var eventNames = new HashSet<string>();
				var hidingStatics = new HashSet<string>();

				for (INamedTypeSymbol cur = m_Type; cur != null; cur = m_Type.TypeKind == TypeKind.Interface ? null : cur.BaseType)
				{
					bool own = SymbolEqualityComparer.Default.Equals(cur, m_Type);
					bool coreLibrary = SymbolEqualityComparer.Default.Equals(cur.ContainingAssembly, m_Known.Object.ContainingAssembly);

					foreach (ISymbol m in cur.GetMembers())
					{
						if (!own && m.DeclaredAccessibility == Accessibility.Private)
							continue;

						string hidingKey = HidingKey(m);
						if (!own && m.IsStatic && (coreLibrary || (hidingKey != null && hidingStatics.Contains(hidingKey))))
							continue;
						if (hidingKey != null)
							hidingStatics.Add(hidingKey);

						if (overridden.Contains(m))
							continue;

						var prop = m as IPropertySymbol;
						if (prop != null)
						{
							string sig = prop.MetadataName + "(" + string.Join(",", prop.Parameters.Select(p => p.Type.ToDisplayString(Names.Fq))) + ")";
							if (!own && propertyNames.Contains(sig))
								continue;
							propertyNames.Add(sig);
						}

						var ev = m as IEventSymbol;
						if (ev != null)
						{
							if (!own && eventNames.Contains(ev.MetadataName))
								continue;
							eventNames.Add(ev.MetadataName);
						}

						for (ISymbol o = Overridden(m); o != null; o = Overridden(o))
							overridden.Add(o);

						yield return m;
					}
				}
			}

			/// <summary>
			/// What a derived member hides a base static by: a static method its signature, a field, property
			/// or event its name (StandardUserDataDescriptor.IsHiddenBySignature and IsHiddenByDerivedMember).
			/// </summary>
			static string HidingKey(ISymbol m)
			{
				var method = m as IMethodSymbol;
				if (method != null)
					return method.IsStatic && method.MethodKind == MethodKind.Ordinary
						? method.MetadataName + "(" + string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(Names.Fq))) + ")"
						: null;
				return m is IFieldSymbol || m is IPropertySymbol || m is IEventSymbol ? m.MetadataName : null;
			}

			static ISymbol Overridden(ISymbol m)
			{
				var method = m as IMethodSymbol;
				if (method != null) return method.OverriddenMethod;
				var prop = m as IPropertySymbol;
				if (prop != null) return prop.OverriddenProperty;
				var ev = m as IEventSymbol;
				if (ev != null) return ev.OverriddenEvent;
				return null;
			}

			/// <summary>MethodMemberDescriptor.CheckMethodIsCompatible: no open generics, no pointers.</summary>
			static bool IsCompatible(IMethodSymbol m)
			{
				if (m.IsGenericMethod)
					return false;

				if (m.Parameters.Any(p => p.Type.TypeKind == TypeKind.Pointer))
					return false;

				if (m.MethodKind != MethodKind.Constructor && m.ReturnType.TypeKind == TypeKind.Pointer)
					return false;

				return true;
			}

			/// <summary>
			/// DescriptorHelpers.GetVisibilityFromAttributes: [MoonSharpHidden] hides, [MoonSharpVisible(b)] decides,
			/// else null. Both attributes are inherited, so overridden members are searched too.
			/// </summary>
			bool? Visibility(ISymbol m)
			{
				foreach (AttributeData a in InheritedAttributes(m))
				{
					if (KnownSymbols.Is(a.AttributeClass, m_Known.HiddenAttribute))
						return false;
				}

				foreach (AttributeData a in InheritedAttributes(m))
				{
					if (KnownSymbols.Is(a.AttributeClass, m_Known.VisibleAttribute) && a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is bool)
						return (bool)a.ConstructorArguments[0].Value;
				}

				return null;
			}

			static IEnumerable<AttributeData> InheritedAttributes(ISymbol m)
			{
				// Fields do not inherit attributes; methods, properties and events walk their overrides.
				for (ISymbol s = m; s != null; s = (m is IFieldSymbol) ? null : Overridden(s))
					foreach (AttributeData a in s.GetAttributes())
						yield return a;
			}

			MethodPlan PlanMethod(IMethodSymbol m, string name, bool isCtor)
			{
				var mp = new MethodPlan { Symbol = m, Name = name, IsConstructor = isCtor };
				mp.Fallback = WhyMethodNotGenerated(m, isCtor);

				if (mp.Fallback == null)
				{
					if (m.IsStatic || isCtor)
						mp.Receiver = Receiver.Static;
					else
						mp.Fallback = ChooseReceiver(m, m.MethodKind == MethodKind.PropertyGet || m.IsReadOnly, out mp.Receiver, out mp.ReceiverType);
				}

				if (mp.Fallback != null)
					Report(isCtor ? ".ctor" : m.MetadataName, mp.Fallback);

				return mp;
			}

			/// <summary>
			/// Picks how a generated instance member reaches its object, or says why it cannot be generated.
			/// A boxed struct can only be changed in place through a base class or interface call on the box,
			/// or through the generated interface of a partial struct; a copy would lose the change.
			/// </summary>
			string ChooseReceiver(IMethodSymbol m, bool readOnly, out Receiver receiver, out ITypeSymbol receiverType)
			{
				INamedTypeSymbol declaring = m.ContainingType;
				receiverType = declaring;
				receiver = Receiver.Reference;

				// Classes and interfaces: through the declaring type, so a base method hidden by a derived
				// one with the same signature is still the one called, as reflection calls it.
				if (!m_Type.IsValueType)
					return null;

				if (!SymbolEqualityComparer.Default.Equals(declaring, m_Type))
					return null; // object or ValueType: called on the box

				if (m_Plan.StructAccess)
				{
					receiver = Receiver.StructAccess;
					return null;
				}

				if (readOnly || m_Type.IsReadOnly)
				{
					receiver = Receiver.Copy;
					receiverType = m_Type;
					return null;
				}

				// An override of object or ValueType, called as a virtual call on the box.
				for (IMethodSymbol o = m.OverriddenMethod; o != null; o = o.OverriddenMethod)
				{
					if (o.OverriddenMethod == null)
					{
						receiverType = o.ContainingType;
						return null;
					}
				}

				// An implicit interface implementation, called through the interface on the box.
				foreach (INamedTypeSymbol iface in m_Type.AllInterfaces)
				{
					foreach (ISymbol im in iface.GetMembers(m.Name))
					{
						if (SymbolEqualityComparer.Default.Equals(m_Type.FindImplementationForInterfaceMember(im), m)
							&& m_Compilation.IsSymbolAccessibleWithin(iface, m_Compilation.Assembly))
						{
							receiverType = iface;
							return null;
						}
					}
				}

				return "a struct method that is not readonly may change the struct, and generated code cannot change a boxed struct in place (declare the struct partial, or the method readonly)";
			}

			string WhyMethodNotGenerated(IMethodSymbol m, bool isCtor)
			{
				if (!m.CanBeReferencedByName && m.MethodKind == MethodKind.Ordinary)
					return "its name cannot be written in C#";
				if (!m_Compilation.IsSymbolAccessibleWithin(m, m_Compilation.Assembly))
					return "it is not accessible from generated code";
				if (isCtor && m_Type.IsAbstract)
					return "the type is abstract";
				if (m.IsVararg)
					return "__arglist methods are not supported";
				if (m.ReturnsByRef || m.ReturnsByRefReadonly)
					return "it returns by reference";
				if (!isCtor && !TypeOk(m.ReturnType, true))
					return "its return type " + m.ReturnType.ToDisplayString() + " is not supported";

				foreach (IParameterSymbol p in m.Parameters)
				{
					if (p.RefKind != RefKind.None && p.RefKind != RefKind.Ref && p.RefKind != RefKind.Out && p.RefKind != RefKind.In)
						return "parameter " + p.Name + " has an unsupported ref kind";
					if (!TypeOk(p.Type, false))
						return "parameter " + p.Name + " has the unsupported type " + p.Type.ToDisplayString();
					if (p.IsOptional && !Literals.CanWriteDefault(p))
						return "the default value of parameter " + p.Name + " cannot be written in C#";
				}

				switch (m.MethodKind)
				{
					case MethodKind.UserDefinedOperator:
						if (Names.OperatorSyntax(m.MetadataName, m.Parameters.Length) == null)
							return "C# cannot call operator " + m.MetadataName + " directly";
						break;
					case MethodKind.PropertySet:
						if (m.IsInitOnly)
							return "an init-only setter cannot be called outside an initializer";
						break;
					case MethodKind.Ordinary:
					case MethodKind.Constructor:
					case MethodKind.PropertyGet:
					case MethodKind.EventAdd:
					case MethodKind.EventRemove:
					case MethodKind.Conversion:
						break;
					default:
						return "methods of kind " + m.MethodKind + " are not supported";
				}

				if (m.MethodKind == MethodKind.PropertyGet || m.MethodKind == MethodKind.PropertySet)
				{
					var prop = (IPropertySymbol)m.AssociatedSymbol;
					if (prop.IsIndexer && !prop.CanBeReferencedByName && prop.MetadataName != "Item")
						return "indexers with a custom name are called as methods only through reflection";
				}

				return null;
			}

			/// <summary>Whether generated code can convert values of the type.</summary>
			bool TypeOk(ITypeSymbol t, bool isReturn)
			{
				if (isReturn && t.SpecialType == SpecialType.System_Void)
					return true;
				if (t.TypeKind == TypeKind.Pointer || t.TypeKind == TypeKind.FunctionPointer || t.IsRefLikeType || t.TypeKind == TypeKind.TypeParameter || t.TypeKind == TypeKind.Error)
					return false;
				if (t.SpecialType == SpecialType.System_TypedReference || t.SpecialType == SpecialType.System_ArgIterator || t.SpecialType == SpecialType.System_RuntimeArgumentHandle)
					return false;
				if (!m_Compilation.IsSymbolAccessibleWithin(t, m_Compilation.Assembly))
					return false;
				var arr = t as IArrayTypeSymbol;
				if (arr != null)
					return TypeOk(arr.ElementType, false);
				return true;
			}

			ValuePlan PlanProperty(IPropertySymbol pi)
			{
				IMethodSymbol getter = pi.GetMethod, setter = pi.SetMethod;
				bool? pvisible = Visibility(pi);
				bool? gvisible = getter == null ? false : Visibility(getter);
				bool? svisible = setter == null ? false : Visibility(setter);

				bool canRead, canWrite;

				if (pvisible.HasValue)
				{
					canRead = getter != null && (gvisible ?? pvisible.Value);
					canWrite = setter != null && (svisible ?? pvisible.Value);
				}
				else
				{
					canRead = getter != null && (gvisible ?? getter.DeclaredAccessibility == Accessibility.Public);
					canWrite = setter != null && (svisible ?? setter.DeclaredAccessibility == Accessibility.Public);
				}

				if (!canRead && !canWrite)
					return null;

				var vp = new ValuePlan
				{
					Symbol = pi,
					Name = pi.MetadataName,
					ValueType = pi.Type,
					IsStatic = (canRead ? getter : setter).IsStatic,
					CanRead = canRead,
					CanWrite = canWrite,
				};

				if (!pi.CanBeReferencedByName)
					vp.Fallback = "its name cannot be written in C#";
				else if (pi.ReturnsByRef || pi.ReturnsByRefReadonly)
					vp.Fallback = "it returns by reference";
				else if (!TypeOk(pi.Type, false))
					vp.Fallback = "its type " + pi.Type.ToDisplayString() + " is not supported";
				else if (canRead && !m_Compilation.IsSymbolAccessibleWithin(getter, m_Compilation.Assembly)
					|| canWrite && !m_Compilation.IsSymbolAccessibleWithin(setter, m_Compilation.Assembly))
					vp.Fallback = "it is not accessible from generated code";

				if (vp.Fallback == null)
				{
					PlanValueReceivers(vp, pi.ContainingType);

					if (canWrite && setter.IsInitOnly)
						vp.SetFallback = "an init-only setter cannot be called outside an initializer";
				}

				ReportValue(vp);
				return vp;
			}

			ValuePlan PlanField(IFieldSymbol fi)
			{
				bool readOnly = fi.IsReadOnly || fi.IsConst;

				var vp = new ValuePlan
				{
					Symbol = fi,
					Name = fi.MetadataName,
					ValueType = fi.Type,
					IsStatic = fi.IsStatic || fi.IsConst,
					IsConst = fi.IsConst,
					CanRead = true,
					CanWrite = !readOnly,
				};

				if (!fi.CanBeReferencedByName)
					vp.Fallback = "its name cannot be written in C#";
				else if (!TypeOk(fi.Type, false))
					vp.Fallback = "its type " + fi.Type.ToDisplayString() + " is not supported";
				else if (!m_Compilation.IsSymbolAccessibleWithin(fi, m_Compilation.Assembly))
					vp.Fallback = "it is not accessible from generated code";

				if (vp.Fallback == null)
					PlanValueReceivers(vp, fi.ContainingType);

				ReportValue(vp);
				return vp;
			}

			void PlanValueReceivers(ValuePlan vp, INamedTypeSymbol declaring)
			{
				if (vp.IsStatic)
				{
					vp.GetReceiver = vp.SetReceiver = Receiver.Static;
					return;
				}

				vp.ReceiverType = m_Type.IsValueType ? m_Type : (ITypeSymbol)declaring;
				vp.GetReceiver = vp.SetReceiver = Receiver.Reference;

				if (!m_Type.IsValueType)
					return;

				if (m_Plan.StructAccess)
				{
					vp.GetReceiver = vp.SetReceiver = Receiver.StructAccess;
					return;
				}

				// Reading works on a copy (property getters are assumed not to change the struct, as the
				// optimized reflection modes also assume); writing must reach the box.
				vp.GetReceiver = Receiver.Copy;
				if (vp.CanWrite)
					vp.SetFallback = "generated code cannot change a boxed struct in place (declare the struct partial)";
			}

			void ReportValue(ValuePlan vp)
			{
				if (vp.Fallback != null)
					Report(vp.Name, vp.Fallback);
				else if (vp.SetFallback != null)
					Report(vp.Name, "writing it: " + vp.SetFallback);
			}

			void Report(string member, string reason)
			{
				m_Context.ReportDiagnostic(Diagnostic.Create(Diagnostics.MemberByReflection, m_Location, m_Type.ToDisplayString(), member, reason));
			}
		}
	}
}
