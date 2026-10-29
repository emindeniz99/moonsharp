using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.Interop;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// This class wraps a CLR function
	/// </summary>
	public sealed class CallbackFunction : RefIdObject
	{
		private static InteropAccessMode m_DefaultAccessMode = InteropAccessMode.LazyOptimized;

		// One descriptor per method and access mode, so converting the same delegate again reuses its
		// parameter analysis and compiled adapter. Weak keys: a cached method does not keep its
		// assembly loaded.
		private static readonly ConditionalWeakTable<MethodInfo, MethodMemberDescriptor[]> s_DescriptorsByMethod =
			new ConditionalWeakTable<MethodInfo, MethodMemberDescriptor[]>();
		private static readonly ConditionalWeakTable<MethodInfo, MethodMemberDescriptor[]>.CreateValueCallback s_NewDescriptorSlots =
			mi => new MethodMemberDescriptor[(int)InteropAccessMode.Default + 1];

		/// <summary>
		/// Gets the name of the function
		/// </summary>
		public string Name { get; private set; }

		/// <summary>
		/// Gets the call back.
		/// </summary>
		/// <value>
		/// The call back.
		/// </value>
		public Func<ScriptExecutionContext, CallbackArguments, DynValue> ClrCallback { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="CallbackFunction" /> class.
		/// </summary>
		/// <param name="callBack">The callback function to be called.</param>
		/// <param name="name">The callback name, used in stacktraces, debugger, etc..</param>
		public CallbackFunction(Func<ScriptExecutionContext, CallbackArguments, DynValue> callBack, string name = null)
		{
			ClrCallback = callBack;
			Name = name;
		}

		/// <summary>
		/// Invokes the callback function
		/// </summary>
		/// <param name="executionContext">The execution context.</param>
		/// <param name="args">The arguments.</param>
		/// <param name="isMethodCall">if set to <c>true</c> this is a method call.</param>
		/// <returns></returns>
		public DynValue Invoke(ScriptExecutionContext executionContext, IList<DynValue> args, bool isMethodCall = false)
		{
			DynValue ret = InvokeFromProcessor(executionContext, args, isMethodCall);
			return executionContext != null ? executionContext.Resolve(ret) : ret;
		}

		/// <summary>
		/// <see cref="Invoke"/> for the processor, which takes a result left in the context
		/// (<see cref="ScriptExecutionContext.Return(double)"/>) itself.
		/// </summary>
		internal DynValue InvokeFromProcessor(ScriptExecutionContext executionContext, IList<DynValue> args, bool isMethodCall)
		{
			if (isMethodCall)
			{
				var colon = executionContext.GetScript().Options.ColonOperatorClrCallbackBehaviour;

				if (colon == ColonOperatorBehaviour.TreatAsColon)
					isMethodCall = false;
				else if (colon == ColonOperatorBehaviour.TreatAsDotOnUserData)
					isMethodCall = (args.Count > 0 && args[0].Type == DataType.UserData);
			}

			// A callback that returns null means "no value"; the VM would dereference it.
			return ClrCallback(executionContext, new CallbackArguments(args, isMethodCall)) ?? DynValue.Nil;
		}

		/// <summary>
		/// Whether the callback uses its <see cref="CallbackArguments"/> only until it returns. The standard
		/// library, userdata methods and delegates converted by MoonSharp do; set it for a host callback that
		/// does too. A Lua call to such a function then reads the arguments in place on the value stack
		/// through an argument object the processor reuses, instead of copying them into a new array and a new
		/// argument object: the call allocates nothing for its arguments. The callback must not keep the
		/// argument object or read it after it returns (copy what it needs, for example with
		/// <see cref="CallbackArguments.GetArray"/>). Default false: the callback gets arguments of its own,
		/// which it may keep.
		/// </summary>
		public bool UsesArgumentsOnlyDuringCall
		{
			get { return ReadsArgumentsInPlace; }
			set { ReadsArgumentsInPlace = value; }
		}

		internal bool ReadsArgumentsInPlace;

		/// <summary>
		/// Whether a Lua call of this function may get the <see cref="ScriptExecutionContext"/> the processor
		/// lends to every C# call at the same call depth, instead of a new one. Only for MoonSharp's own
		/// functions, which never let host code keep their context: the standard library, internal callbacks,
		/// and userdata and delegate descriptors whose CLR member takes no ScriptExecutionContext or
		/// CallbackArguments. A host callback gets a context of its own on every call, which it may keep
		/// (a coroutine or a timer reading it later sees that call's CallingLocation and AdditionalData).
		/// </summary>
		internal bool ReusesContext;

		/// <summary>
		/// A MoonSharp-internal callback as a DynValue, with <see cref="ReusesContext"/> set: the callback must
		/// not keep its context or hand it to host code (except through <see cref="ScriptExecutionContext.Call"/>,
		/// which gives the callee a context of its own).
		/// </summary>
		internal static DynValue NewInternal(Func<ScriptExecutionContext, CallbackArguments, DynValue> callBack, string name = null)
		{
			DynValue v = DynValue.NewCallback(callBack, name);
			v.Callback.ReusesContext = true;
			return v;
		}

		/// <summary>
		/// The builtin fast-call id of a library function (Processor_Builtins.cs), or 0.
		/// </summary>
		internal int BuiltinId;

		/// <summary>
		/// <see cref="Invoke"/> with the arguments read in place from the value stack, through
		/// <paramref name="args"/>: the top <paramref name="count"/> values from <paramref name="first"/>.
		/// </summary>
		internal DynValue InvokeOnStack(ScriptExecutionContext executionContext, CallbackArguments.StackArguments args,
			Execution.VM.ValueStack stack, int first, int count, bool isMethodCall)
		{
			if (isMethodCall)
			{
				var colon = executionContext.GetScript().Options.ColonOperatorClrCallbackBehaviour;

				if (colon == ColonOperatorBehaviour.TreatAsColon)
					isMethodCall = false;
				else if (colon == ColonOperatorBehaviour.TreatAsDotOnUserData)
					isMethodCall = (count > 0 && stack.Items[first].Type == DataType.UserData);
			}

			args.Reuse(stack, first, count, isMethodCall);
			return ClrCallback(executionContext, args) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets or sets the default access mode used when marshalling delegates
		/// </summary>
		/// <value>
		/// The default access mode. Default, HideMembers and BackgroundOptimized are NOT supported.
		/// </value>
		/// <exception cref="System.ArgumentException">Default, HideMembers and BackgroundOptimized are NOT supported.</exception>
		public static InteropAccessMode DefaultAccessMode
		{
			get { return m_DefaultAccessMode; }
			set
			{
				if (value == InteropAccessMode.Default || value == InteropAccessMode.HideMembers || value == InteropAccessMode.BackgroundOptimized)
					throw new ArgumentException("DefaultAccessMode");

				m_DefaultAccessMode = value;
			}
		}

		/// <summary>
		/// Creates a CallbackFunction from a delegate.
		/// </summary>
		/// <param name="script">The script.</param>
		/// <param name="del">The delegate.</param>
		/// <param name="accessMode">The access mode.</param>
		/// <returns></returns>
		public static CallbackFunction FromDelegate(Script script, Delegate del, InteropAccessMode accessMode = InteropAccessMode.Default)
		{
			if (accessMode == InteropAccessMode.Default)
				accessMode = m_DefaultAccessMode;

			// A delegate compiled at run time (an expression tree, a DynamicMethod) points to a method
			// reflection cannot call as the delegate does: on .NET it has no declaring type, on Mono
			// it is a static method whose first parameter is bound to the delegate's target (the
			// closure); on NativeAOT and IL2CPP the expression is interpreted and reading Method throws.
			// Call such delegates through their own Invoke method instead.
			MethodInfo method = TryGetMethod(del);

			if (method == null || method.DeclaringType == null || (method.IsStatic && del.Target != null))
				return GetMethodDescriptor(del.GetType().GetMethod("Invoke"), accessMode).GetCallbackFunction(script, del);

			MethodMemberDescriptor descr = GetMethodDescriptor(method, accessMode);
			return descr.GetCallbackFunction(script, del.Target);
		}

		private static MethodInfo TryGetMethod(Delegate del)
		{
			try
			{
				return del.Method;
			}
			catch (NotSupportedException)
			{
				return null;
			}
		}

		private static MethodMemberDescriptor GetMethodDescriptor(MethodInfo mi, InteropAccessMode accessMode)
		{
			MethodMemberDescriptor[] slots = s_DescriptorsByMethod.GetValue(mi, s_NewDescriptorSlots);
			int slot = (int)accessMode;

			if (slot < 0 || slot >= slots.Length)
				return new MethodMemberDescriptor(mi, accessMode);

			// A race only builds a duplicate descriptor; both are equivalent.
			MethodMemberDescriptor descr = slots[slot];

			if (descr == null)
			{
				descr = new MethodMemberDescriptor(mi, accessMode);
				slots[slot] = descr;
			}

			return descr;
		}


		/// <summary>
		/// Creates a CallbackFunction from a MethodInfo relative to a function.
		/// </summary>
		/// <param name="script">The script.</param>
		/// <param name="mi">The MethodInfo object.</param>
		/// <param name="obj">The object to which the function applies, or null for static methods.</param>
		/// <param name="accessMode">The access mode.</param>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">The method is not static.</exception>
		public static CallbackFunction FromMethodInfo(Script script, System.Reflection.MethodInfo mi, object obj = null, InteropAccessMode accessMode = InteropAccessMode.Default)
		{
			if (accessMode == InteropAccessMode.Default)
				accessMode = m_DefaultAccessMode;

			MethodMemberDescriptor descr = GetMethodDescriptor(mi, accessMode);
			return descr.GetCallbackFunction(script, obj);
		}



		/// <summary>
		/// Gets or sets an object used as additional data to the callback function (available in the execution context).
		/// </summary>
		public object AdditionalData { get; set; }


		/// <summary>
		/// Checks the callback signature of a method is compatible for callbacks
		/// </summary>
		public static bool CheckCallbackSignature(System.Reflection.MethodInfo mi, bool requirePublicVisibility)
		{
			System.Reflection.ParameterInfo[] pi = mi.GetParameters();

			return (pi.Length == 2 && pi[0].ParameterType == typeof(ScriptExecutionContext)
				&& pi[1].ParameterType == typeof(CallbackArguments) && mi.ReturnType == typeof(DynValue) && (requirePublicVisibility || mi.IsPublic));
		}



	}
}
