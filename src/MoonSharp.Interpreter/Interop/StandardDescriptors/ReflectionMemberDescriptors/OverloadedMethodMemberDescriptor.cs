//#define DEBUG_OVERLOAD_RESOLVER

using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter.Compatibility;
using MoonSharp.Interpreter.Interop.BasicDescriptors;
using MoonSharp.Interpreter.Interop.Converters;

namespace MoonSharp.Interpreter.Interop
{
	/// <summary>
	/// Class providing easier marshalling of overloaded CLR functions
	/// </summary>
	public class OverloadedMethodMemberDescriptor : IOptimizableDescriptor, IMemberDescriptor, IWireableDescriptor
	{
		/// <summary>
		/// Comparer class for IOverloadableMemberDescriptor
		/// </summary>
		private class OverloadableMemberDescriptorComparer : IComparer<IOverloadableMemberDescriptor>
		{
			public int Compare(IOverloadableMemberDescriptor x, IOverloadableMemberDescriptor y)
			{
				return x.SortDiscriminant.CompareTo(y.SortDiscriminant);
			}
		}


		const int CACHE_SIZE = 5;

		private class OverloadCacheItem
		{
			public bool HasObject;
			public IOverloadableMemberDescriptor Method;
			public List<DataType> ArgsDataType;
			public List<Type> ArgsUserDataType;
			public List<bool> ArgsFractional;
			public int HitIndexAtLastHit;
		}

		private List<IOverloadableMemberDescriptor> m_Overloads = new List<IOverloadableMemberDescriptor>();
		private List<IOverloadableMemberDescriptor> m_ExtOverloads = new List<IOverloadableMemberDescriptor>();
		private bool m_Unsorted = true;
		private OverloadCacheItem[] m_Cache = new OverloadCacheItem[CACHE_SIZE];
		private int m_CacheHits = 0;
		private int m_ExtensionMethodVersion = 0;
		private int m_CacheConvertersVersion = 0;

		/// <summary>
		/// Gets or sets a value indicating whether this instance ignores extension methods.
		/// </summary>
		public bool IgnoreExtensionMethods { get; set; }


		/// <summary>
		/// Initializes a new instance of the <see cref="OverloadedMethodMemberDescriptor"/> class.
		/// </summary>
		public OverloadedMethodMemberDescriptor(string name, Type declaringType)
		{
			Name = name;
			DeclaringType = declaringType;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="OverloadedMethodMemberDescriptor" /> class.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="declaringType">The declaring type.</param>
		/// <param name="descriptor">The descriptor of the first overloaded method.</param>
		public OverloadedMethodMemberDescriptor(string name, Type declaringType, IOverloadableMemberDescriptor descriptor)
			: this(name, declaringType)
		{
			m_Overloads.Add(descriptor);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="OverloadedMethodMemberDescriptor" /> class.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="declaringType">The declaring type.</param>
		/// <param name="descriptors">The descriptors of the overloaded methods.</param>
		public OverloadedMethodMemberDescriptor(string name, Type declaringType, IEnumerable<IOverloadableMemberDescriptor> descriptors)
			: this(name, declaringType)
		{
			m_Overloads.AddRange(descriptors);
		}

		/// <summary>
		/// Sets the extension methods snapshot.
		/// </summary>
		/// <param name="version">The version.</param>
		/// <param name="extMethods">The ext methods.</param>
		internal void SetExtensionMethodsSnapshot(int version, List<IOverloadableMemberDescriptor> extMethods)
		{
			m_ExtOverloads = extMethods;
			m_ExtensionMethodVersion = version;
		}



		/// <summary>
		/// Gets the name of the first described overload
		/// </summary>
		public string Name
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the name of the first described overload
		/// </summary>
		public Type DeclaringType
		{
			get;
			private set;
		}

		/// <summary>
		/// Adds an overload.
		/// </summary>
		/// <param name="overload">The overload.</param>
		public void AddOverload(IOverloadableMemberDescriptor overload)
		{
			m_Overloads.Add(overload);
			m_Unsorted = true;
			m_Signatures = null;
		}

		/// <summary>
		/// Gets the number of overloaded methods contained in this collection
		/// </summary>
		/// <value>
		/// The overload count.
		/// </value>
		public int OverloadCount
		{
			get { return m_Overloads.Count; }
		}

		/// <summary>
		/// Gets a copy of the overloads a call resolves against, without extension methods. Tools that
		/// describe the API to script authors (the LuaCATS generator) read the signatures from here.
		/// </summary>
		public IOverloadableMemberDescriptor[] Overloads
		{
			get { return m_Overloads.ToArray(); }
		}

		/// <summary>
		/// Performs the overloaded call.
		/// </summary>
		/// <param name="script">The script.</param>
		/// <param name="obj">The object.</param>
		/// <param name="context">The context.</param>
		/// <param name="args">The arguments.</param>
		/// <returns></returns>
		/// <exception cref="ScriptRuntimeException">function call doesn't match any overload</exception>
		private DynValue PerformOverloadedCall(Script script, object obj, ScriptExecutionContext context, CallbackArguments args)
		{
			bool extMethodCacheNotExpired = IgnoreExtensionMethods || (obj == null) || m_ExtensionMethodVersion == UserData.GetExtensionMethodsChangeVersion();

			// common case, let's optimize for it
			if (m_Overloads.Count == 1 && m_ExtOverloads.Count == 0 && extMethodCacheNotExpired)
				return m_Overloads[0].Execute(script, obj, context, args);

			// Arguments whose weights depend only on their type (and, for numbers, on having a fraction)
			// choose the same overload every time: look the choice up by that signature. It holds only
			// while no extension method competes and no custom converter changes the weights.
			long signature;

			if ((IgnoreExtensionMethods || obj == null || (extMethodCacheNotExpired && m_ExtOverloads.Count == 0))
				&& !Script.GlobalOptions.CustomConverters.HasScriptToClrConversions
				&& TryGetSignature(obj != null, args, out signature))
			{
				IOverloadableMemberDescriptor known = FindSignature(signature);

				if (known == null)
				{
					known = Resolve(obj, context, args, extMethodCacheNotExpired);
					AddSignature(signature, known);
				}

				return known.Execute(script, obj, context, args);
			}

			return Resolve(obj, context, args, extMethodCacheNotExpired).Execute(script, obj, context, args);
		}

		/// <summary>
		/// The overload MoonSharp's weights choose for these arguments: from the cache of recent argument
		/// types, or by scoring every overload.
		/// </summary>
		private IOverloadableMemberDescriptor Resolve(object obj, ScriptExecutionContext context, CallbackArguments args, bool extMethodCacheNotExpired)
		{
			if (m_Unsorted)
			{
				m_Overloads.Sort(new OverloadableMemberDescriptorComparer());
				m_Unsorted = false;
			}

			// Custom converters change the weights, so a choice made under other converters is stale.
			int convertersVersion = Script.GlobalOptions.CustomConverters.ScriptToClrVersion;

			if (m_CacheConvertersVersion != convertersVersion)
			{
				m_Cache = new OverloadCacheItem[CACHE_SIZE];
				m_CacheHits = 0;
				m_CacheConvertersVersion = convertersVersion;
			}

			if (extMethodCacheNotExpired)
			{
				for (int i = 0; i < m_Cache.Length; i++)
				{
					if (m_Cache[i] != null && CheckMatch(obj != null, args, m_Cache[i]))
					{
#if DEBUG_OVERLOAD_RESOLVER
						System.Diagnostics.Debug.WriteLine(string.Format("[OVERLOAD] : CACHED! slot {0}, hits: {1}", i, m_CacheHits));
#endif
						return m_Cache[i].Method;
					}
				}
			}

			// resolve on overloads first
			int maxScore = 0;
			IOverloadableMemberDescriptor bestOverload = null;

			for (int i = 0; i < m_Overloads.Count; i++)
			{
				if (obj != null || m_Overloads[i].IsStatic)
				{
					int score = CalcScoreForOverload(context, args, m_Overloads[i], false);

					if (score > maxScore)
					{
						maxScore = score;
						bestOverload = m_Overloads[i];
					}
				}
			}

			if (!IgnoreExtensionMethods && (obj != null))
			{
				if (!extMethodCacheNotExpired)
				{
					m_ExtensionMethodVersion = UserData.GetExtensionMethodsChangeVersion();
					m_ExtOverloads = UserData.GetExtensionMethodsByNameAndType(this.Name, this.DeclaringType);
				}

				for (int i = 0; i < m_ExtOverloads.Count; i++)
				{
					int score = CalcScoreForOverload(context, args, m_ExtOverloads[i], true);

					if (score > maxScore)
					{
						maxScore = score;
						bestOverload = m_ExtOverloads[i];
					}
				}
			}


			if (bestOverload != null)
			{
				Cache(obj != null, args, bestOverload);
				return bestOverload;
			}

			throw new ScriptRuntimeException("function call doesn't match any overload");
		}

		// The argument signatures seen, with the overload each one chose. A signature packs, per scored
		// argument, 3 bits for its kind (nil, void, boolean, whole number, fractional number, string, Lua
		// function, C# function), then the argument count and whether there is an object and a method call.
		// Tables, userdata and other kinds weigh by their contents or runtime type and are not packed.
		const int MAX_SIGNATURE_ARGS = 16;
		const int MAX_SIGNATURES = 8;

		sealed class Signature
		{
			public long Key;
			public IOverloadableMemberDescriptor Method;
		}

		// Replaced, never changed in place, so a reader on another thread sees a whole table.
		Signature[] m_Signatures;
		// ClosureDelegates.Version when the table was filled: registering a delegate factory can make a Lua
		// function match a delegate parameter it did not match before.
		int m_SignaturesDelegateVersion;

		private static bool TryGetSignature(bool hasObject, CallbackArguments args, out long signature)
		{
			int first = args.IsMethodCall ? 1 : 0;
			int count = args.Count - first;
			long key = 0;
			signature = 0;

			if (count > MAX_SIGNATURE_ARGS)
				return false;

			for (int i = 0; i < count; i++)
			{
				DynValue arg = args.RawGet(first + i, false);
				int kind;

				switch (arg.Type)
				{
					case DataType.Nil: kind = 0; break;
					case DataType.Void: kind = 1; break;
					case DataType.Boolean: kind = 2; break;
					case DataType.Number: kind = ScriptToClrConversions.IsFractional(arg.Number) ? 4 : 3; break;
					case DataType.String: kind = 5; break;
					case DataType.Function: kind = 6; break;
					case DataType.ClrFunction: kind = 7; break;
					default: return false;
				}

				key = (key << 3) | (long)kind;
			}

			signature = (((key << 5) | (long)count) << 2) | (hasObject ? 2L : 0L) | (args.IsMethodCall ? 1L : 0L);
			return true;
		}

		private IOverloadableMemberDescriptor FindSignature(long signature)
		{
			Signature[] table = m_Signatures;

			if (table == null)
				return null;

			if (m_SignaturesDelegateVersion != ClosureDelegates.Version)
			{
				m_Signatures = null;
				return null;
			}

			for (int i = 0; i < table.Length; i++)
				if (table[i].Key == signature)
					return table[i].Method;

			return null;
		}

		private void AddSignature(long signature, IOverloadableMemberDescriptor method)
		{
			Signature[] table = m_Signatures;
			int n = table == null ? 0 : table.Length;

			if (n >= MAX_SIGNATURES)
				return;

			Signature[] grown = new Signature[n + 1];

			if (table != null)
				Array.Copy(table, grown, n);

			grown[n] = new Signature() { Key = signature, Method = method };

			if (table == null)
				m_SignaturesDelegateVersion = ClosureDelegates.Version;

			m_Signatures = grown;
		}

		private void Cache(bool hasObject, CallbackArguments args, IOverloadableMemberDescriptor bestOverload)
		{
			int lowestHits = int.MaxValue;
			OverloadCacheItem found = null;
			for (int i = 0; i < m_Cache.Length; i++)
			{
				if (m_Cache[i] == null)
				{
					found = new OverloadCacheItem() { ArgsDataType = new List<DataType>(), ArgsUserDataType = new List<Type>(), ArgsFractional = new List<bool>() };
					m_Cache[i] = found;
					break;
				}
				else if (m_Cache[i].HitIndexAtLastHit < lowestHits)
				{
					lowestHits = m_Cache[i].HitIndexAtLastHit;
					found = m_Cache[i];
				}
			}

			if (found == null)
			{
				// overflow..
				m_Cache = new OverloadCacheItem[CACHE_SIZE];
				found = new OverloadCacheItem() { ArgsDataType = new List<DataType>(), ArgsUserDataType = new List<Type>(), ArgsFractional = new List<bool>() };
				m_Cache[0] = found;
				m_CacheHits = 0;
			}

			found.Method = bestOverload;
			found.HitIndexAtLastHit = ++m_CacheHits;
			found.ArgsDataType.Clear();
			found.ArgsUserDataType.Clear();
			found.ArgsFractional.Clear();
			found.HasObject = hasObject;

			for (int i = 0; i < args.Count; i++)
			{
				found.ArgsDataType.Add(args[i].Type);
				found.ArgsFractional.Add(args[i].Type == DataType.Number && ScriptToClrConversions.IsFractional(args[i].Number));

				if (args[i].Type == DataType.UserData)
				{
					found.ArgsUserDataType.Add(args[i].UserData.Descriptor.Type);
				}
				else
				{
					found.ArgsUserDataType.Add(null);
				}
			}
		}

		private bool CheckMatch(bool hasObject, CallbackArguments args, OverloadCacheItem overloadCacheItem)
		{
			if (overloadCacheItem.HasObject && !hasObject)
				return false;

			if (args.Count != overloadCacheItem.ArgsDataType.Count)
				return false;

			for (int i = 0; i < args.Count; i++)
			{
				DynValue arg = args[i];

				if (arg.Type != overloadCacheItem.ArgsDataType[i])
					return false;

				if (arg.Type == DataType.Number)
				{
					if (ScriptToClrConversions.IsFractional(arg.Number) != overloadCacheItem.ArgsFractional[i])
						return false;
				}
				else if (arg.Type == DataType.UserData)
				{
					if (arg.UserData.Descriptor.Type != overloadCacheItem.ArgsUserDataType[i])
						return false;
				}
			}

			overloadCacheItem.HitIndexAtLastHit = ++m_CacheHits;
			return true;
		}


		/// <summary>
		/// Calculates the score for the overload.
		/// </summary>
		/// <param name="context">The context.</param>
		/// <param name="args">The arguments.</param>
		/// <param name="method">The method.</param>
		/// <param name="isExtMethod">if set to <c>true</c>, is an extension method.</param>
		/// <returns></returns>
		private int CalcScoreForOverload(ScriptExecutionContext context, CallbackArguments args, IOverloadableMemberDescriptor method, bool isExtMethod)
		{
			int totalScore = ScriptToClrConversions.WEIGHT_EXACT_MATCH;
			int argsBase = args.IsMethodCall ? 1 : 0;
			int argsCnt = argsBase;
			bool varArgsUsed = false;


			for (int i = 0; i < method.Parameters.Length; i++)
			{
				if (isExtMethod && i == 0)
					continue;

				if (method.Parameters[i].IsOut)
					continue;

				Type parameterType = method.Parameters[i].Type;

				if ((parameterType == typeof(Script)) || (parameterType == typeof(ScriptExecutionContext)) || (parameterType == typeof(CallbackArguments)))
					continue;

				if (i == method.Parameters.Length - 1 && method.VarArgsArrayType != null)
				{
					int varargCnt = 0;
					DynValue firstArg = null;
					int scoreBeforeVargars = totalScore;

					// update score for varargs
					while (true)
					{
						var arg = args.RawGet(argsCnt, false);
						if (arg == null) break;

						if (firstArg == null) firstArg = arg;

						argsCnt += 1;

						varargCnt += 1;

						int score = CalcScoreForSingleArgument(method.Parameters[i], method.VarArgsElementType, arg, isOptional: false);
						totalScore = Math.Min(totalScore, score);
					}

					// check if exact-match
					if (varargCnt == 1)
					{
						if (firstArg.Type == DataType.UserData && firstArg.UserData.Object != null)
						{
							if (Framework.Do.IsAssignableFrom(method.VarArgsArrayType, firstArg.UserData.Object.GetType()))
							{
								totalScore = scoreBeforeVargars;
								continue;
							}
						}
					}

					// apply varargs penalty to score
					if (varargCnt == 0)
						totalScore = Math.Min(totalScore, ScriptToClrConversions.WEIGHT_VARARGS_EMPTY);

					varArgsUsed = true;
				}
				else
				{
					var arg = args.RawGet(argsCnt, false) ?? DynValue.Void;

					int score = CalcScoreForSingleArgument(method.Parameters[i], parameterType, arg, method.Parameters[i].HasDefaultValue);

					totalScore = Math.Min(totalScore, score);

					argsCnt += 1;
				}
			}

			if (totalScore > 0)
			{
				if ((args.Count - argsBase) <= method.Parameters.Length)
				{
					totalScore += ScriptToClrConversions.WEIGHT_NO_EXTRA_PARAMS_BONUS;
					totalScore *= 1000;
				}
				else if (varArgsUsed)
				{
					totalScore -= ScriptToClrConversions.WEIGHT_VARARGS_MALUS;
					totalScore *= 1000;
				}
				else
				{
					totalScore *= 1000;
					totalScore -= ScriptToClrConversions.WEIGHT_EXTRA_PARAMS_MALUS * ((args.Count - argsBase) - method.Parameters.Length);
					totalScore = Math.Max(1, totalScore);
				}
			}

#if DEBUG_OVERLOAD_RESOLVER
			System.Diagnostics.Debug.WriteLine(string.Format("[OVERLOAD] : Score {0} for method {1}", totalScore, method.SortDiscriminant));
#endif
			return totalScore;
		}

		private static int CalcScoreForSingleArgument(ParameterDescriptor desc, Type parameterType, DynValue arg, bool isOptional)
		{
			int score = ScriptToClrConversions.DynValueToObjectOfTypeWeight(arg,
				parameterType, isOptional);

			if (parameterType.IsByRef || desc.IsOut || desc.IsRef)
				score = Math.Max(0, score + ScriptToClrConversions.WEIGHT_BYREF_BONUSMALUS);

			return score;
		}



		/// <summary>
		/// Gets a callback function as a delegate
		/// </summary>
		/// <param name="script">The script for which the callback must be generated.</param>
		/// <param name="obj">The object (null for static).</param>
		/// <returns></returns>
		public Func<ScriptExecutionContext, CallbackArguments, DynValue> GetCallback(Script script, object obj)
		{
			return (context, args) => PerformOverloadedCall(script, obj, context, args);
		}


		void IOptimizableDescriptor.Optimize()
		{
			// A copy: this can run on a background thread while the first call sorts the list.
			foreach (var d in m_Overloads.ToArray().OfType<IOptimizableDescriptor>())
				d.Optimize();
		}

		/// <summary>
		/// Gets the callback function.
		/// </summary>
		/// <param name="script">The script for which the callback must be generated.</param>
		/// <param name="obj">The object (null for static).</param>
		/// <returns></returns>
		public CallbackFunction GetCallbackFunction(Script script, object obj = null)
		{
			return new CallbackFunction(GetCallback(script, obj), this.Name);
		}

		/// <summary>
		/// Gets a value indicating whether there is at least one static method in the resolution list
		/// </summary>
		/// <exception cref="System.NotImplementedException"></exception>
		public bool IsStatic
		{
			get { return m_Overloads.Any(o => o.IsStatic); }
		}

		/// <summary>
		/// Gets the types of access supported by this member
		/// </summary>
		public MemberDescriptorAccess MemberAccess
		{
			get { return MemberDescriptorAccess.CanExecute | MemberDescriptorAccess.CanRead; }
		}

		/// <summary>
		/// Gets the value of this member as a <see cref="DynValue" /> to be exposed to scripts.
		/// </summary>
		/// <param name="script">The script.</param>
		/// <param name="obj">The object owning this member, or null if static.</param>
		/// <returns>
		/// The value of this member as a <see cref="DynValue" />.
		/// </returns>
		public DynValue GetValue(Script script, object obj)
		{
			return DynValue.NewCallback(this.GetCallbackFunction(script, obj));
		}

		/// <summary>
		/// Sets the value of this member from a <see cref="DynValue" />.
		/// </summary>
		/// <param name="script">The script.</param>
		/// <param name="obj">The object owning this member, or null if static.</param>
		/// <param name="value">The value to be set.</param>
		/// <exception cref="System.NotImplementedException"></exception>
		public void SetValue(Script script, object obj, DynValue value)
		{
			this.CheckAccess(MemberDescriptorAccess.CanWrite, obj);
		}

		/// <summary>
		/// Prepares the descriptor for hard-wiring.
		/// The descriptor fills the passed table with all the needed data for hardwire generators to generate the appropriate code.
		/// </summary>
		/// <param name="t">The table to be filled</param>
		public void PrepareForWiring(Table t)
		{
			t.Set("class", DynValue.NewString(this.GetType().FullName));
			t.Set("name", DynValue.NewString(this.Name));
			t.Set("decltype", DynValue.NewString(this.DeclaringType.FullName));
			DynValue mst = DynValue.NewPrimeTable();
			t.Set("overloads", mst);

			int i = 0;

			foreach (var m in this.m_Overloads)
			{
				IWireableDescriptor sd = m as IWireableDescriptor;

				if (sd != null)
				{
					DynValue mt = DynValue.NewPrimeTable();
					mst.Table.Set(++i, mt);
					sd.PrepareForWiring(mt.Table);
				}
				else
				{
					mst.Table.Set(++i, DynValue.NewString(string.Format("unsupported - {0} is not serializable", m.GetType().FullName)));
				}
			}
		}
	}
}
