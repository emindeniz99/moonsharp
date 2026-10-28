using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter.Compatibility;
using MoonSharp.Interpreter.Interop.BasicDescriptors;
using MoonSharp.Interpreter.Interop.UserDataRegistries;

namespace MoonSharp.Interpreter.Interop
{
	/// <summary>
	/// Standard descriptor for userdata types.
	/// </summary>
	public class StandardUserDataDescriptor : DispatchingUserDataDescriptor, IWireableDescriptor
	{
		/// <summary>
		/// Gets the interop access mode this descriptor uses for members access
		/// </summary>
		public InteropAccessMode AccessMode { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="StandardUserDataDescriptor"/> class.
		/// </summary>
		/// <param name="type">The type this descriptor refers to.</param>
		/// <param name="accessMode">The interop access mode this descriptor uses for members access</param>
		/// <param name="friendlyName">A human readable friendly name of the descriptor.</param>
		public StandardUserDataDescriptor(Type type, InteropAccessMode accessMode, string friendlyName = null)
			: base(type, friendlyName)
		{
			if (accessMode == InteropAccessMode.NoReflectionAllowed)
				throw new ArgumentException("Can't create a StandardUserDataDescriptor under a NoReflectionAllowed access mode");

			if (Script.GlobalOptions.Platform.IsRunningOnAOT())
				accessMode = InteropAccessMode.Reflection;

			if (accessMode == InteropAccessMode.Default)
				accessMode = UserData.DefaultAccessMode;

			AccessMode = accessMode;

			FillMemberList();
		}

		/// <summary>
		/// Whether a more derived type declares a visible member with the same name, as C# "new"
		/// does. Reflection returns both members; a script, like C# code using this type, sees the
		/// derived one. Allocates only when two members share a name.
		/// </summary>
		private bool IsHiddenByDerivedMember(MemberInfo member, PropertyInfo[] properties, FieldInfo[] fields, EventInfo[] events)
		{
			return IsHiddenBy(member, properties) || IsHiddenBy(member, fields) || IsHiddenBy(member, events);
		}

		private bool IsHiddenBy(MemberInfo member, MemberInfo[] others)
		{
			foreach (MemberInfo other in others)
			{
				if (other.Name == member.Name && other.DeclaringType != member.DeclaringType
					&& Framework.Do.IsAssignableFrom(member.DeclaringType, other.DeclaringType)
					&& CreateIfVisible(other) != null)
					return true;
			}

			return false;
		}

		private void AddDataMember(MemberInfo member, PropertyInfo[] properties, FieldInfo[] fields, EventInfo[] events)
		{
			IMemberDescriptor desc = CreateIfVisible(member);

			if (desc != null && !IsHiddenByDerivedMember(member, properties, fields, events))
				AddMember(member.Name, desc);
		}

		private IMemberDescriptor CreateIfVisible(MemberInfo member)
		{
			if (member is PropertyInfo)
				return PropertyMemberDescriptor.TryCreateIfVisible((PropertyInfo)member, this.AccessMode);
			if (member is FieldInfo)
				return FieldMemberDescriptor.TryCreateIfVisible((FieldInfo)member, this.AccessMode);
			return EventMemberDescriptor.TryCreateIfVisible((EventInfo)member, this.AccessMode);
		}

		/// <summary>
		/// Fills the member list.
		/// </summary>
		private void FillMemberList()
		{
			HashSet<string> membersToIgnore = new HashSet<string>(
				Framework.Do.GetCustomAttributes(this.Type, typeof(MoonSharpHideMemberAttribute), true)
					.OfType<MoonSharpHideMemberAttribute>()
					.Select(a => a.MemberName)
				);

			Type type = this.Type;

			if (AccessMode == InteropAccessMode.HideMembers)
				return;

			if (!type.IsDelegateType())
			{
				// add declared constructors
				foreach (ConstructorInfo ci in Framework.Do.GetConstructors(type))
				{
					if (membersToIgnore.Contains("__new"))
						continue;

					AddMember("__new", MethodMemberDescriptor.TryCreateIfVisible(ci, this.AccessMode));
				}

				// valuetypes don't reflect their empty ctor.. actually empty ctors are a perversion, we don't care and implement ours
				if (Framework.Do.IsValueType(type) && !membersToIgnore.Contains("__new"))
					AddMember("__new", new ValueTypeDefaultCtorMemberDescriptor(type));
			}


			// add methods to method list and metamethods
			foreach (MethodInfo mi in Framework.Do.GetMethods(type))
			{
				if (membersToIgnore.Contains(mi.Name)) continue;

				MethodMemberDescriptor md = MethodMemberDescriptor.TryCreateIfVisible(mi, this.AccessMode);

				if (md != null)
				{
					if (!MethodMemberDescriptor.CheckMethodIsCompatible(mi, false))
						continue;

					// transform explicit/implicit conversions to a friendlier name.
					string name = mi.Name;
					if (mi.IsSpecialName && (mi.Name == SPECIALNAME_CAST_EXPLICIT || mi.Name == SPECIALNAME_CAST_IMPLICIT))
					{
						name = mi.ReturnType.GetConversionMethodName();
					}

					AddMember(name, md);

					foreach (string metaname in mi.GetMetaNamesFromAttributes())
					{
						AddMetaMember(metaname, md);
					}
				}
			}

			// properties, fields and events; reflection also returns the inherited members a derived
			// type hides with "new"
			PropertyInfo[] properties = Framework.Do.GetProperties(type);
			FieldInfo[] fields = Framework.Do.GetFields(type);
			EventInfo[] events = Framework.Do.GetEvents(type);

			foreach (PropertyInfo pi in properties)
			{
				if (pi.IsSpecialName || pi.GetIndexParameters().Any() || membersToIgnore.Contains(pi.Name))
					continue;

				AddDataMember(pi, properties, fields, events);
			}

			foreach (FieldInfo fi in fields)
			{
				if (fi.IsSpecialName || membersToIgnore.Contains(fi.Name))
					continue;

				AddDataMember(fi, properties, fields, events);
			}

			foreach (EventInfo ei in events)
			{
				if (ei.IsSpecialName || membersToIgnore.Contains(ei.Name))
					continue;

				AddDataMember(ei, properties, fields, events);
			}

			// get nested types and create statics
			foreach (Type nestedType in Framework.Do.GetNestedTypes(type))
			{
				if (membersToIgnore.Contains(nestedType.Name))
					continue;

				if (!Framework.Do.IsGenericTypeDefinition(nestedType))
				{
					if (Framework.Do.IsNestedPublic(nestedType) || Framework.Do.GetCustomAttributes(nestedType, typeof(MoonSharpUserDataAttribute), true).Length > 0)
					{
						// Not UserData.RegisterType: its trimming annotation is for the host's calls, and
						// the annotation on this type's registration already kept the nested types.
						var descr = TypeDescriptorRegistry.RegisterType_Impl(nestedType, this.AccessMode, null, null);

						if (descr != null)
							AddDynValue(nestedType.Name, UserData.CreateStatic(nestedType));
					}
				}
			}

			if (!membersToIgnore.Contains("[this]"))
			{
				if (Type.IsArray)
				{
					int rank = Type.GetArrayRank();

					ParameterDescriptor[] get_pars = new ParameterDescriptor[rank];
					ParameterDescriptor[] set_pars = new ParameterDescriptor[rank + 1];

					for (int i = 0; i < rank; i++)
						get_pars[i] = set_pars[i] = new ParameterDescriptor("idx" + i.ToString(), typeof(int));

					set_pars[rank] = new ParameterDescriptor("value", Type.GetElementType());

					AddMember(SPECIALNAME_INDEXER_SET, new ArrayMemberDescriptor(SPECIALNAME_INDEXER_SET, true, set_pars));
					AddMember(SPECIALNAME_INDEXER_GET, new ArrayMemberDescriptor(SPECIALNAME_INDEXER_GET, false, get_pars));
				}
				else if (Type == typeof(Array))
				{
					AddMember(SPECIALNAME_INDEXER_SET, new ArrayMemberDescriptor(SPECIALNAME_INDEXER_SET, true));
					AddMember(SPECIALNAME_INDEXER_GET, new ArrayMemberDescriptor(SPECIALNAME_INDEXER_GET, false));
				}
			}
		}




		public void PrepareForWiring(Table t)
		{
			if (AccessMode == InteropAccessMode.HideMembers || Framework.Do.GetAssembly(Type) == Framework.Do.GetAssembly(this.GetType()))
			{
				t.Set("skip", DynValue.NewBoolean(true));
			}
			else
			{
				t.Set("visibility", DynValue.NewString(this.Type.GetClrVisibility()));

				t.Set("class", DynValue.NewString(this.GetType().FullName));
				DynValue tm = DynValue.NewPrimeTable();
				t.Set("members", tm);
				DynValue tmm = DynValue.NewPrimeTable();
				t.Set("metamembers", tmm);

				Serialize(tm.Table, Members);
				Serialize(tmm.Table, MetaMembers);
			}
		}

		private void Serialize(Table t, IEnumerable<KeyValuePair<string, IMemberDescriptor>> members)
		{
			foreach (var pair in members)
			{
				IWireableDescriptor sd = pair.Value as IWireableDescriptor;

				if (sd != null)
				{
					DynValue mt = DynValue.NewPrimeTable();
					t.Set(pair.Key, mt);
					sd.PrepareForWiring(mt.Table);
				}
				else
				{
					t.Set(pair.Key, DynValue.NewString("unsupported member type : " + pair.Value.GetType().FullName));
				}
			}
		}
	}
}
