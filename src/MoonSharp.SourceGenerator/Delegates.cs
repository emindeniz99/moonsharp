using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace MoonSharp.SourceGenerator
{
	/// <summary>
	/// Factories that turn Lua functions into delegates, registered with UserData.RegisterDelegateType from
	/// GeneratedUserData.RegisterAll. Without them the interpreter builds such delegates by reflection
	/// (MakeGenericMethod), which an AOT compiler may have no code for. One is written for each delegate
	/// type listed with [assembly: MoonSharpUserDataTypes] and each delegate type a generated member takes as
	/// a parameter or as the value it sets.
	/// </summary>
	static class Delegates
	{
		const int MaxParameters = 4;

		/// <summary>Null when a factory can be written for the delegate type, else why not.</summary>
		public static string WhyNotAdapted(INamedTypeSymbol type, Compilation compilation)
		{
			if (type.TypeKind != TypeKind.Delegate || type.DelegateInvokeMethod == null)
				return "not a delegate type";
			if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
				return "it is not accessible from generated code";
			if (type.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter))
				return "open generic types are not generated; list closed types with [assembly: MoonSharpUserDataTypes(typeof(...))]";

			IMethodSymbol invoke = type.DelegateInvokeMethod;

			if (invoke.Parameters.Length > MaxParameters || invoke.ReturnsByRef || invoke.ReturnsByRefReadonly
				|| invoke.Parameters.Any(p => p.RefKind != RefKind.None || !CanBeTypeArgument(p.Type))
				|| (!invoke.ReturnsVoid && !CanBeTypeArgument(invoke.ReturnType)))
				return "a Lua function converts only to delegates with up to " + MaxParameters + " parameters, none by reference, pointer or ref struct";

			return null;
		}

		static bool CanBeTypeArgument(ITypeSymbol t)
		{
			return t.TypeKind != TypeKind.Pointer && t.TypeKind != TypeKind.FunctionPointer && !t.IsRefLikeType;
		}

		/// <summary>The delegate types generated members take as parameters or set as values, in a stable order.</summary>
		public static IEnumerable<INamedTypeSymbol> UsedBy(IEnumerable<TypePlan> plans)
		{
			foreach (TypePlan plan in plans)
			{
				foreach (MethodPlan m in plan.Constructors.Concat(plan.Methods))
					if (m.Fallback == null)
						foreach (IParameterSymbol p in m.Symbol.Parameters)
							if (p.Type.TypeKind == TypeKind.Delegate)
								yield return (INamedTypeSymbol)p.Type;

				foreach (ValuePlan v in plan.Properties.Concat(plan.Fields))
					if (v.Fallback == null && v.CanWrite && v.SetFallback == null && v.ValueType.TypeKind == TypeKind.Delegate)
						yield return (INamedTypeSymbol)v.ValueType;
			}
		}

		/// <summary>
		/// The registration of a factory that calls the function with the typed calls of Closure, for
		/// example <c>f =&gt; new Action&lt;int&gt;((a0) =&gt; { f.Call&lt;int, DynValue&gt;(a0); })</c>.
		/// </summary>
		public static string EmitRegistration(INamedTypeSymbol type)
		{
			IMethodSymbol invoke = type.DelegateInvokeMethod;
			string name = Names.Of(type);
			var pars = invoke.Parameters.Select((p, i) => "a" + i).ToList();
			var typeArgs = invoke.Parameters.Select(p => Names.Of(p.Type)).ToList();
			typeArgs.Add(invoke.ReturnsVoid ? "global::MoonSharp.Interpreter.DynValue" : Names.Of(invoke.ReturnType));

			string call = "f.Call<" + string.Join(", ", typeArgs) + ">(" + string.Join(", ", pars) + ")";
			string body = invoke.ReturnsVoid ? "{ " + call + "; }" : call;

			var w = new StringBuilder();
			w.Append("global::MoonSharp.Interpreter.UserData.RegisterDelegateType<").Append(name).Append(">(f => new ").Append(name)
				.Append("((").Append(string.Join(", ", pars)).Append(") => ").Append(body).Append("));");
			return w.ToString();
		}
	}
}
