using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter.Compatibility;
using MoonSharp.Interpreter.CoreLib;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Platforms;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Class managing modules (mostly as extension methods)
	/// </summary>
	public static class ModuleRegister
	{
		/// <summary>
		/// Register the core modules to a table
		/// </summary>
		/// <param name="table">The table.</param>
		/// <param name="modules">The modules.</param>
		/// <returns></returns>
		public static Table RegisterCoreModules(this Table table, CoreModules modules)
		{
			modules = Script.GlobalOptions.Platform.FilterSupportedCoreModules(modules);

			if (modules.Has(CoreModules.GlobalConsts)) RegisterConstants(table);
			if (modules.Has(CoreModules.TableIterators)) RegisterModuleType<TableIteratorsModule>(table);
			if (modules.Has(CoreModules.Basic)) RegisterModuleType<BasicModule>(table);
			if (modules.Has(CoreModules.Metatables)) RegisterModuleType<MetaTableModule>(table);
			if (modules.Has(CoreModules.String)) RegisterModuleType<StringModule>(table);
			if (modules.Has(CoreModules.LoadMethods)) RegisterModuleType<LoadModule>(table);
			if (modules.Has(CoreModules.Table)) RegisterModuleType<TableModule>(table);
			if (modules.Has(CoreModules.Table)) RegisterModuleType<TableModule_Globals>(table);
			if (modules.Has(CoreModules.ErrorHandling)) RegisterModuleType<ErrorHandlingModule>(table);
			if (modules.Has(CoreModules.Math)) RegisterModuleType<MathModule>(table);
			if (modules.Has(CoreModules.Coroutine)) RegisterModuleType<CoroutineModule>(table);
			if (modules.Has(CoreModules.Bit32)) RegisterModuleType<Bit32Module>(table);
			if (modules.Has(CoreModules.Dynamic)) RegisterModuleType<DynamicModule>(table);
			if (modules.Has(CoreModules.OS_System)) RegisterModuleType<OsSystemModule>(table);
			if (modules.Has(CoreModules.OS_Time)) RegisterModuleType<OsTimeModule>(table);
			if (modules.Has(CoreModules.IO)) RegisterModuleType<IoModule>(table);
			if (modules.Has(CoreModules.Debug)) RegisterModuleType<DebugModule>(table);
			if (modules.Has(CoreModules.Json)) RegisterModuleType<JsonModule>(table);
			if (modules.Has(CoreModules.Int64)) RegisterModuleType<Int64Module>(table);
			if (modules.Has(CoreModules.Int64)) RegisterModuleType<UInt64Module>(table);

			return table;
		}



		/// <summary>
		/// Registers the standard constants (_G, _VERSION, _MOONSHARP) to a table
		/// </summary>
		/// <param name="table">The table.</param>
		/// <returns></returns>
		public static Table RegisterConstants(this Table table)
		{
			DynValue moonsharp_table = DynValue.NewTable(table.OwnerScript);
			Table m = moonsharp_table.Table;

			table.Set("_G", DynValue.NewTable(table));
			table.Set("_VERSION", DynValue.NewString(string.Format("MoonSharp {0}", Script.VERSION)));
			table.Set("_MOONSHARP", moonsharp_table);

			m.Set("version", DynValue.NewString(Script.VERSION));
			m.Set("luacompat", DynValue.NewString(Script.LUA_VERSION));
			m.Set("platform", DynValue.NewString(Script.GlobalOptions.Platform.GetPlatformName()));
			m.Set("is_aot", DynValue.NewBoolean(Script.GlobalOptions.Platform.IsRunningOnAOT()));
			m.Set("is_unity", DynValue.NewBoolean(PlatformAutoDetector.IsRunningOnUnity));
			m.Set("is_mono", DynValue.NewBoolean(PlatformAutoDetector.IsRunningOnMono));
			m.Set("is_clr4", DynValue.NewBoolean(PlatformAutoDetector.IsRunningOnClr4));
			m.Set("banner", DynValue.NewString(Script.GetBanner()));

			return table;
		}



		/// <summary>
		/// Registers a module type to the specified table
		/// </summary>
		/// <param name="gtable">The table.</param>
		/// <param name="t">The type</param>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">If the module contains some incompatibility</exception>
		public static Table RegisterModuleType(this Table gtable, Type t)
		{
			ModuleRegistration reg = GetRegistration(t);

			if (reg == null)
				return RegisterModuleTypeUncached(gtable, t);

			Table table = CreateModuleNamespace(gtable, reg.Namespace);

			foreach (ModuleMember m in reg.Members)
			{
				switch (m.Kind)
				{
					case ModuleMemberKind.Callback:
						// A new CallbackFunction per Script: its AdditionalData and identity belong to the Script.
						DynValue cb = DynValue.NewCallback(m.Callback, m.Name);
						cb.Callback.ReadsArgumentsInPlace = m.IsLibrary;
						cb.Callback.BuiltinId = m.BuiltinId;
						table.Set(m.Name, cb);
						break;
					case ModuleMemberKind.Init:
						m.Method.Invoke(null, new object[2] { gtable, table });
						break;
					case ModuleMemberKind.LuaFunction:
						RegisterLazyScriptField(m.Field, table, m.Name);
						break;
					case ModuleMemberKind.Constant:
						RegisterScriptFieldAsConst(m.Field, null, table, t, m.Name);
						break;
				}
			}

			return gtable;
		}

		/// <summary>
		/// What reflection finds on a module type, in the order the registration applies it. Building it
		/// costs most of a new Script, and it never changes for a type, so it is built once per type.
		/// </summary>
		private sealed class ModuleRegistration
		{
			public string Namespace;
			public ModuleMember[] Members;
		}

		private enum ModuleMemberKind
		{
			Callback,
			Init,
			LuaFunction,
			Constant,
		}

		private sealed class ModuleMember
		{
			public ModuleMemberKind Kind;
			public string Name;
			public Func<ScriptExecutionContext, CallbackArguments, DynValue> Callback;
			// A function of this assembly's standard library, which reads its arguments in place.
			public bool IsLibrary;
			public int BuiltinId;
			public MethodInfo Method;
			public FieldInfo Field;
		}

		private static readonly Dictionary<Type, ModuleRegistration> s_Registrations = new Dictionary<Type, ModuleRegistration>();

		/// <summary>
		/// Returns the cached registration of a module type, or null for a type whose registration fails
		/// part way (no [MoonSharpModule], a wrong signature or field type): those keep the uncached path,
		/// so they fail at the same point, with the same exception and the same partial result as before.
		/// </summary>
		private static ModuleRegistration GetRegistration(Type t)
		{
			ModuleRegistration reg;

			lock (s_Registrations)
			{
				if (s_Registrations.TryGetValue(t, out reg))
					return reg;
			}

			try
			{
				reg = BuildRegistration(t);
			}
			catch (Exception)
			{
				// Reflection failed somewhere: the uncached path rethrows it where it always did.
				reg = null;
			}

			lock (s_Registrations)
			{
				s_Registrations[t] = reg;
			}

			return reg;
		}

		private static ModuleRegistration BuildRegistration(Type t)
		{
			MoonSharpModuleAttribute moduleAttr = (MoonSharpModuleAttribute)Framework.Do.GetCustomAttributes(t, typeof(MoonSharpModuleAttribute), false).FirstOrDefault();

			if (moduleAttr == null)
				return null;

			var members = new List<ModuleMember>();

			foreach (MethodInfo mi in Framework.Do.GetMethods(t).Where(__mi => __mi.IsStatic))
			{
				MoonSharpModuleMethodAttribute attr = (MoonSharpModuleMethodAttribute)mi.GetCustomAttributes(typeof(MoonSharpModuleMethodAttribute), false).FirstOrDefault();

				if (attr != null)
				{
					if (!CallbackFunction.CheckCallbackSignature(mi, true))
						return null;

					var func = (Func<ScriptExecutionContext, CallbackArguments, DynValue>)Delegate.CreateDelegate(typeof(Func<ScriptExecutionContext, CallbackArguments, DynValue>), mi);
					string name = (!string.IsNullOrEmpty(attr.Name)) ? attr.Name : mi.Name;
					members.Add(new ModuleMember { Kind = ModuleMemberKind.Callback, Name = name, Callback = func, IsLibrary = t.Assembly == typeof(ModuleRegister).Assembly, BuiltinId = Execution.VM.Processor.BuiltinId(t, name) });
				}
				else if (mi.Name == "MoonSharpInit")
				{
					members.Add(new ModuleMember { Kind = ModuleMemberKind.Init, Name = mi.Name, Method = mi });
				}
			}

			foreach (FieldInfo fi in Framework.Do.GetFields(t).Where(_mi => _mi.IsStatic && _mi.GetCustomAttributes(typeof(MoonSharpModuleMethodAttribute), false).ToArray().Length > 0))
			{
				if (fi.FieldType != typeof(string))
					return null;

				MoonSharpModuleMethodAttribute attr = (MoonSharpModuleMethodAttribute)fi.GetCustomAttributes(typeof(MoonSharpModuleMethodAttribute), false).First();
				string name = (!string.IsNullOrEmpty(attr.Name)) ? attr.Name : fi.Name;
				members.Add(new ModuleMember { Kind = ModuleMemberKind.LuaFunction, Name = name, Field = fi });
			}

			foreach (FieldInfo fi in Framework.Do.GetFields(t).Where(_mi => _mi.IsStatic && _mi.GetCustomAttributes(typeof(MoonSharpModuleConstantAttribute), false).ToArray().Length > 0))
			{
				if (fi.FieldType != typeof(string) && fi.FieldType != typeof(double))
					return null;

				MoonSharpModuleConstantAttribute attr = (MoonSharpModuleConstantAttribute)fi.GetCustomAttributes(typeof(MoonSharpModuleConstantAttribute), false).First();
				string name = (!string.IsNullOrEmpty(attr.Name)) ? attr.Name : fi.Name;
				members.Add(new ModuleMember { Kind = ModuleMemberKind.Constant, Name = name, Field = fi });
			}

			return new ModuleRegistration { Namespace = moduleAttr.Namespace, Members = members.ToArray() };
		}

		/// <summary>
		/// The registration as it was before the cache, for module types the cache does not take.
		/// </summary>
		private static Table RegisterModuleTypeUncached(Table gtable, Type t)
		{
			Table table = CreateModuleNamespace(gtable, t);

			foreach (MethodInfo mi in Framework.Do.GetMethods(t).Where(__mi => __mi.IsStatic))
			{
				if (mi.GetCustomAttributes(typeof(MoonSharpModuleMethodAttribute), false).ToArray().Length > 0)
				{
					MoonSharpModuleMethodAttribute attr = (MoonSharpModuleMethodAttribute)mi.GetCustomAttributes(typeof(MoonSharpModuleMethodAttribute), false).First();

					if (!CallbackFunction.CheckCallbackSignature(mi, true))
							throw new ArgumentException(string.Format("Method {0} does not have the right signature.", mi.Name));


					Delegate deleg = Delegate.CreateDelegate(typeof(Func<ScriptExecutionContext, CallbackArguments, DynValue>), mi);

					Func<ScriptExecutionContext, CallbackArguments, DynValue> func =
						(Func<ScriptExecutionContext, CallbackArguments, DynValue>)deleg;


					string name = (!string.IsNullOrEmpty(attr.Name)) ? attr.Name : mi.Name;

					table.Set(name, DynValue.NewCallback(func, name));
				}
				else if (mi.Name == "MoonSharpInit")
				{
					object[] args = new object[2] { gtable, table };
					mi.Invoke(null, args);
				}
			}

			foreach (FieldInfo fi in Framework.Do.GetFields(t).Where(_mi => _mi.IsStatic && _mi.GetCustomAttributes(typeof(MoonSharpModuleMethodAttribute), false).ToArray().Length > 0))
			{
				MoonSharpModuleMethodAttribute attr = (MoonSharpModuleMethodAttribute)fi.GetCustomAttributes(typeof(MoonSharpModuleMethodAttribute), false).First();
				string name = (!string.IsNullOrEmpty(attr.Name)) ? attr.Name : fi.Name;

				RegisterScriptField(fi, null, table, t, name);
			}

			foreach (FieldInfo fi in Framework.Do.GetFields(t).Where(_mi => _mi.IsStatic && _mi.GetCustomAttributes(typeof(MoonSharpModuleConstantAttribute), false).ToArray().Length > 0))
			{
				MoonSharpModuleConstantAttribute attr = (MoonSharpModuleConstantAttribute)fi.GetCustomAttributes(typeof(MoonSharpModuleConstantAttribute), false).First();
				string name = (!string.IsNullOrEmpty(attr.Name)) ? attr.Name : fi.Name;

				RegisterScriptFieldAsConst(fi, null, table, t, name);
			}

			return gtable;
		}

		private static void RegisterScriptFieldAsConst(FieldInfo fi, object o, Table table, Type t, string name)
		{
			if (fi.FieldType == typeof(string))
			{
				string val = fi.GetValue(o) as string;
				table.Set(name, DynValue.NewString(val));
			}
			else if (fi.FieldType == typeof(double))
			{
				double val = (double)fi.GetValue(o);
				table.Set(name, DynValue.NewNumber(val));
			}
			else
			{
				throw new ArgumentException(string.Format("Field {0} does not have the right type - it must be string or double.", name));
			}
		}

		/// <summary>
		/// A module member written in Lua (a [MoonSharpModuleMethod] string field, such as `require`) is compiled
		/// on its first call instead of in every new Script. It is a C# function whose calls tail-call the
		/// compiled function, so the Lua code runs exactly as if it had been registered compiled.
		/// </summary>
		private static void RegisterLazyScriptField(FieldInfo fi, Table table, string name)
		{
			string code = fi.GetValue(null) as string;

			// The source is registered now, as before, so chunk names ("chunk_1") do not change.
			Script script = table.OwnerScript;
			SourceCode source = script.AddFunctionSource(code, name);
			DynValue compiled = null;

			table.Set(name, DynValue.NewCallback((context, args) =>
			{
				if (compiled == null)
					compiled = script.CompileFunctionSource(source, table);

				return DynValue.NewTailCallReq(compiled, args.GetArray());
			}, name));
		}

		private static void RegisterScriptField(FieldInfo fi, object o, Table table, Type t, string name)
		{
			if (fi.FieldType != typeof(string))
			{
				throw new ArgumentException(string.Format("Field {0} does not have the right type - it must be string.", name));
			}

			string val = fi.GetValue(o) as string;

			DynValue fn = table.OwnerScript.LoadFunction(val, table, name);

			table.Set(name, fn);
		}


		private static Table CreateModuleNamespace(Table gtable, Type t)
		{
			MoonSharpModuleAttribute attr = (MoonSharpModuleAttribute)(Framework.Do.GetCustomAttributes(t, typeof(MoonSharpModuleAttribute), false).First());

			return CreateModuleNamespace(gtable, attr.Namespace);
		}

		private static Table CreateModuleNamespace(Table gtable, string ns)
		{
			if (string.IsNullOrEmpty(ns))
			{
				return gtable;
			}
			else
			{
				Table table = null;

				DynValue found = gtable.Get(ns);

				if (found.Type == DataType.Table)
				{
					table = found.Table;
				}
				else
				{
					table = new Table(gtable.OwnerScript);
					gtable.Set(ns, DynValue.NewTable(table));
				}


				DynValue package = gtable.RawGet("package");

				if (package == null || package.Type != DataType.Table)
				{
					gtable.Set("package", package = DynValue.NewTable(gtable.OwnerScript));
				}


				DynValue loaded = package.Table.RawGet("loaded");

				if (loaded == null || loaded.Type != DataType.Table)
				{
					package.Table.Set("loaded", loaded = DynValue.NewTable(gtable.OwnerScript));
				}

				loaded.Table.Set(ns, DynValue.NewTable(table));

				return table;
			}
		}

		/// <summary>
		/// Registers a module type to the specified table
		/// </summary>
		/// <typeparam name="T">The module type</typeparam>
		/// <param name="table">The table.</param>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">If the module contains some incompatibility</exception>
		public static Table RegisterModuleType<T>(this Table table)
		{
			return RegisterModuleType(table, typeof(T));
		}


	}
}
