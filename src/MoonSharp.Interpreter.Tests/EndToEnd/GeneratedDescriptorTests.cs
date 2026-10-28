using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MoonSharp.Generated;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.BasicDescriptors;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;
using NUnit.Framework;

// The userdata types of the interop test fixtures, plus a few BCL types, get generated descriptors,
// so the fixtures can run against them (Generated_InteropSuites).
[assembly: MoonSharp.Interpreter.MoonSharpUserDataTypes(
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataEnumsTests.EnumOverloadsTestClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataEventsTests.SomeClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataFieldsTests.SomeClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataIndexerTests.IndexerTestClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataMetaTests.ClassWithLength),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataMetaTests.ClassWithCount),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataMetaTests.ArithmOperatorsTestClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataMethodsTests.SomeClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataMethodsTests.Interface1),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataMethodsTests.Interface2),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataMethodsTests.SomeOtherClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataMethodsTests.SomeOtherClassWithDualInterfaces),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataNestedTypesTests.SomeType),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataNestedTypesTests.SomeType.SomeNestedType),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataNestedTypesTests.VSomeType),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataNestedTypesTests.VSomeType.SomeNestedType),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataOverloadsTests.OverloadsTestClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.UserDataPropertiesTests.SomeClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataFieldsTests.SomeClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataIndexerTests.IndexerTestClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataMetaTests.ClassWithLength),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataMetaTests.ClassWithCount),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataMetaTests.ArithmOperatorsTestClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataMethodsTests.SomeClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataMethodsTests.Interface1),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataMethodsTests.Interface2),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataMethodsTests.SomeOtherClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataMethodsTests.SomeOtherClassWithDualInterfaces),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataOverloadsTests.OverloadsTestClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.VtUserDataPropertiesTests.SomeClass),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropBoundMethodTests.BoundProbe),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropBoundMethodTests.EnumSource),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCacheTests.CacheBase),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCacheTests.CacheDerived),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCacheTests.ICacheProbeA),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCacheTests.ICacheProbeB),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCacheTests.CacheProbeImpl),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCacheTests.CachePolicyBase),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCacheTests.CachePolicyDerived),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCacheTests.NumericEcho),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCorrectnessTests.HashProbe),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCorrectnessTests.HashProbeOther),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCorrectnessTests.HashPoint),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCorrectnessTests.EventSender),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.InteropCorrectnessTests.OverloadPicker),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.CollectionsRegisteredTests.RegCollItem),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.CollectionsRegisteredTests.RegCollMethods),
	typeof(MoonSharp.Interpreter.Tests.EndToEnd.ExactInt64InteropTests.Int64Api),
	typeof(System.Collections.Generic.List<int>),
	typeof(System.Collections.Generic.List<MoonSharp.Interpreter.Tests.EndToEnd.CollectionsRegisteredTests.RegCollItem>),
	typeof(System.Collections.Generic.Dictionary<string, int>),
	typeof(System.Text.StringBuilder),
	typeof(System.EventArgs),
	typeof(System.Version),
	typeof(System.DateTime),
	typeof(System.TimeSpan))]

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Descriptors written by MoonSharp.SourceGenerator against the reflection descriptors of the same types:
	/// the same members (names, kinds, access, overload counts), and the interop test fixtures passing
	/// when every type they register gets its generated descriptor.
	/// </summary>
	[TestFixture]
	public class GeneratedDescriptorTests
	{
		static readonly Type[] GeneratedTypes =
		{
			typeof(UserDataEnumsTests.EnumOverloadsTestClass),
			typeof(UserDataEventsTests.SomeClass),
			typeof(UserDataFieldsTests.SomeClass),
			typeof(UserDataIndexerTests.IndexerTestClass),
			typeof(UserDataMetaTests.ClassWithLength),
			typeof(UserDataMetaTests.ClassWithCount),
			typeof(UserDataMetaTests.ArithmOperatorsTestClass),
			typeof(UserDataMethodsTests.SomeClass),
			typeof(UserDataMethodsTests.Interface1),
			typeof(UserDataMethodsTests.Interface2),
			typeof(UserDataMethodsTests.SomeOtherClass),
			typeof(UserDataMethodsTests.SomeOtherClassWithDualInterfaces),
			typeof(UserDataNestedTypesTests.SomeType),
			typeof(UserDataNestedTypesTests.SomeType.SomeNestedType),
			typeof(UserDataNestedTypesTests.VSomeType),
			typeof(UserDataNestedTypesTests.VSomeType.SomeNestedType),
			typeof(UserDataOverloadsTests.OverloadsTestClass),
			typeof(UserDataPropertiesTests.SomeClass),
			typeof(VtUserDataFieldsTests.SomeClass),
			typeof(VtUserDataIndexerTests.IndexerTestClass),
			typeof(VtUserDataMetaTests.ClassWithLength),
			typeof(VtUserDataMetaTests.ClassWithCount),
			typeof(VtUserDataMetaTests.ArithmOperatorsTestClass),
			typeof(VtUserDataMethodsTests.SomeClass),
			typeof(VtUserDataMethodsTests.Interface1),
			typeof(VtUserDataMethodsTests.Interface2),
			typeof(VtUserDataMethodsTests.SomeOtherClass),
			typeof(VtUserDataMethodsTests.SomeOtherClassWithDualInterfaces),
			typeof(VtUserDataOverloadsTests.OverloadsTestClass),
			typeof(VtUserDataPropertiesTests.SomeClass),
			typeof(InteropBoundMethodTests.BoundProbe),
			typeof(InteropBoundMethodTests.EnumSource),
			typeof(InteropCacheTests.CacheBase),
			typeof(InteropCacheTests.CacheDerived),
			typeof(InteropCacheTests.ICacheProbeA),
			typeof(InteropCacheTests.ICacheProbeB),
			typeof(InteropCacheTests.CacheProbeImpl),
			typeof(InteropCacheTests.CachePolicyBase),
			typeof(InteropCacheTests.CachePolicyDerived),
			typeof(InteropCacheTests.NumericEcho),
			typeof(InteropCorrectnessTests.HashProbe),
			typeof(InteropCorrectnessTests.HashProbeOther),
			typeof(InteropCorrectnessTests.HashPoint),
			typeof(InteropCorrectnessTests.EventSender),
			typeof(InteropCorrectnessTests.OverloadPicker),
			typeof(CollectionsRegisteredTests.RegCollItem),
			typeof(CollectionsRegisteredTests.RegCollMethods),
			typeof(ExactInt64InteropTests.Int64Api),
			typeof(InteropParityTests.ParityClass),
			typeof(InteropParityTests.ParityStruct),
			typeof(InteropParityTests.ForeignVector),
			typeof(InteropParityTests.ForeignPoint),
		};

		/// <summary>
		/// Generated against the reference assemblies the tests compile with. The runtime's types can have
		/// more members (List.EnsureCapacity on .NET 8, not in netstandard2.0), so the generated descriptor
		/// shows the compile-time surface and is compared by behaviour, not member lists.
		/// </summary>
		static readonly Type[] GeneratedBclTypes =
		{
			typeof(List<int>),
			typeof(List<CollectionsRegisteredTests.RegCollItem>),
			typeof(Dictionary<string, int>),
			typeof(StringBuilder),
			typeof(EventArgs),
			typeof(Version),
			typeof(DateTime),
			typeof(TimeSpan),
		};

		/// <summary>The interop fixtures run again with generated descriptors (Generated_InteropSuites).</summary>
		static readonly Type[] InteropFixtures =
		{
			typeof(UserDataEnumsTests),
			typeof(UserDataEventsTests),
			typeof(UserDataFieldsTests),
			typeof(UserDataIndexerTests),
			typeof(UserDataMetaTests),
			typeof(UserDataMethodsTests),
			typeof(UserDataNestedTypesTests),
			typeof(UserDataOverloadsTests),
			typeof(UserDataPropertiesTests),
			typeof(VtUserDataFieldsTests),
			typeof(VtUserDataIndexerTests),
			typeof(VtUserDataMetaTests),
			typeof(VtUserDataMethodsTests),
			typeof(VtUserDataOverloadsTests),
			typeof(VtUserDataPropertiesTests),
			typeof(InteropBoundMethodTests),
			typeof(InteropCacheTests),
			typeof(InteropCorrectnessTests),
			typeof(CollectionsRegisteredTests),
			typeof(ExactInt64InteropTests),
		};

		/// <summary>Replaces every reflection descriptor being registered with the generated one for its type.</summary>
		sealed class GeneratedPolicy : IRegistrationPolicy
		{
			public readonly HashSet<Type> Substituted = new HashSet<Type>();

			public IUserDataDescriptor HandleRegistration(IUserDataDescriptor newDescriptor, IUserDataDescriptor oldDescriptor)
			{
				var standard = newDescriptor as StandardUserDataDescriptor;

				if (standard != null && standard.AccessMode != InteropAccessMode.HideMembers)
				{
					IUserDataDescriptor generated = GeneratedUserData.CreateDescriptor(standard.Type);

					if (generated != null)
					{
						Substituted.Add(standard.Type);
						newDescriptor = generated;
					}
				}

				if (newDescriptor == null)
					return null;

				return oldDescriptor ?? newDescriptor;
			}

			public bool AllowTypeAutoRegistration(Type type)
			{
				return false;
			}
		}

		[Test]
		public void Generated_EveryListedTypeHasADescriptor()
		{
			foreach (Type t in GeneratedTypes.Concat(GeneratedBclTypes))
				Assert.IsNotNull(GeneratedUserData.CreateDescriptor(t), t.FullName);

			Assert.IsNull(GeneratedUserData.CreateDescriptor(typeof(Random)));
		}

		[Test]
		public void Generated_SameMembersAsReflection()
		{
			var mismatches = new List<string>();

			// Both descriptors register nested types as they are built.
			using (new RegistrySnapshot(GeneratedTypes))
			{
				foreach (Type t in GeneratedTypes)
				{
					string reflected = Describe(new StandardUserDataDescriptor(t, InteropAccessMode.Reflection));
					string generated = Describe(GeneratedUserData.CreateDescriptor(t));

					if (reflected != generated)
						mismatches.Add(t.FullName + "\n--- reflection\n" + reflected + "--- generated\n" + generated);
				}
			}

			Assert.AreEqual(0, mismatches.Count, string.Join("\n\n", mismatches.ToArray()));
		}

		/// <summary>One line per member: name, kind, access and static-ness, overload count.</summary>
		static string Describe(IUserDataDescriptor descriptor)
		{
			var d = (DispatchingUserDataDescriptor)descriptor;
			var sb = new StringBuilder();
			sb.AppendLine("name " + d.Name + " friendly " + d.FriendlyName);

			foreach (var m in d.Members.OrderBy(m => m.Key, StringComparer.Ordinal))
				sb.AppendLine(m.Key + ": " + DescribeMember(m.Value));

			foreach (var m in d.MetaMembers.OrderBy(m => m.Key, StringComparer.Ordinal))
				sb.AppendLine("meta " + m.Key + ": " + DescribeMember(m.Value));

			return sb.ToString();
		}

		static string DescribeMember(IMemberDescriptor m)
		{
			var overloads = m as OverloadedMethodMemberDescriptor;

			if (overloads == null)
				return (m is DynValueMemberDescriptor ? "value " : "member ") + m.MemberAccess + (m.IsStatic ? " static" : "");

			// Each overload: its parameter types and flags, as overload resolution sees them.
			return "method " + string.Join(" | ", overloads.Overloads
				.Select(o => (o.IsStatic ? "static " : "") + "(" + string.Join(", ", o.Parameters.Select(DescribeParameter).ToArray()) + ")")
				.OrderBy(x => x, StringComparer.Ordinal).ToArray());
		}

		static string DescribeParameter(ParameterDescriptor p)
		{
			return p.Type.FullName + (p.HasDefaultValue ? " =" : "") + (p.IsOut ? " out" : "") + (p.IsRef ? " ref" : "") + (p.IsVarArgs ? " params" : "");
		}

		const string BclProbe = @"
			local r = {}
			local function add(v) r[#r + 1] = tostring(v) end
			add(SB.__new('ab'))
			local l = L.__new()
			l:Add(3) l:Add(1) l:Add(2) l:Sort()
			add(l.Count) add(l[0] .. l[1] .. l[2]) add(l:Contains(2)) add(l:IndexOf(9))
			l[1] = 7 add(l[1])
			local d = D.__new()
			d:Add('k', 4) d['j'] = 5
			add(d.Count) add(d['k'] + d['j']) add(d:ContainsKey('z'))
			local t = TS.FromSeconds(90)
			add(t.TotalMinutes) add(t.Minutes) add((t + t).TotalSeconds) add(t:CompareTo(TS.Zero))
			local dt = DT.__new(2020, 2, 29, 13, 5, 0)
			add(dt.Year .. '-' .. dt.Month .. '-' .. dt.Day) add(dt.DayOfYear) add(dt:AddDays(1).Month)
			add((dt - DT.__new(2020, 1, 1)).Days)
			local v = V.__new(1, 2, 3)
			add(v.Major + v.Minor + v.Build) add(v:ToString(2)) add(v == V.__new('1.2.3'))
			return table.concat(r, ',')";

		static string RunBclProbe(bool generated)
		{
			using (new RegistrySnapshot(GeneratedBclTypes))
			{
				foreach (Type t in GeneratedBclTypes)
				{
					if (generated)
						UserData.RegisterType(GeneratedUserData.CreateDescriptor(t));
					else
						UserData.RegisterType(t, InteropAccessMode.Reflection);
				}

				Script s = new Script();
				s.Globals["SB"] = typeof(StringBuilder);
				s.Globals["L"] = typeof(List<int>);
				s.Globals["D"] = typeof(Dictionary<string, int>);
				s.Globals["TS"] = typeof(TimeSpan);
				s.Globals["DT"] = typeof(DateTime);
				s.Globals["V"] = typeof(Version);
				return s.DoString(BclProbe).String;
			}
		}

		/// <summary>Types compiled from reference assemblies behave as their reflection descriptors do.</summary>
		[Test]
		public void Generated_BclTypesBehaveAsReflection()
		{
			string reflected = RunBclProbe(false);
			Assert.AreEqual("ab,3,123,true,-1,7,2,9,false,1.5,1,180,1,2020-2-29,60,3,59,6,1.2,true", reflected);
			Assert.AreEqual(reflected, RunBclProbe(true));
		}

		/// <summary>
		/// The generated descriptors bind their members with generated code; reflection only where the
		/// generator reported why (here: the mutating members of a struct that is not partial).
		/// </summary>
		[Test]
		public void Generated_MembersAreGeneratedCode()
		{
			var vector = (DispatchingUserDataDescriptor)GeneratedUserData.CreateDescriptor(typeof(InteropParityTests.ForeignVector));
			var parity = (DispatchingUserDataDescriptor)GeneratedUserData.CreateDescriptor(typeof(InteropParityTests.ParityClass));

			Assert.IsFalse(parity.FindMember("Prop") is PropertyMemberDescriptor);
			Assert.IsFalse(parity.FindMember("Field") is FieldMemberDescriptor);
			Assert.IsFalse(vector.FindMember("X") is FieldMemberDescriptor);

			Assert.AreEqual(0, CountReflected(parity), "ParityClass");
			Assert.AreEqual(0, CountReflected((DispatchingUserDataDescriptor)GeneratedUserData.CreateDescriptor(typeof(InteropParityTests.ParityStruct))), "ParityStruct");

			// Scale and LengthSquared may change the struct; X and Y read through generated code and
			// write through reflection (counted above as generated members).
			Assert.AreEqual(2, CountReflected(vector), "ForeignVector");
		}

		static int CountReflected(DispatchingUserDataDescriptor d)
		{
			int n = 0;

			foreach (var m in d.Members)
			{
				var overloads = m.Value as OverloadedMethodMemberDescriptor;

				if (overloads != null)
					n += overloads.Overloads.Count(o => o is MethodMemberDescriptor);
				else if (m.Value is PropertyMemberDescriptor || m.Value is FieldMemberDescriptor || m.Value is EventMemberDescriptor)
					n++;
			}

			return n;
		}

		/// <summary>Fixture tests left out of the second run.</summary>
		static readonly string[] SuiteSkips =
		{
			// Casts the registered descriptor to StandardUserDataDescriptor to remove a member.
			"Interop_Bound_MembersChangedLaterAreSeen",
			// These leave process-wide state that the same tests in the main run expect not to find:
			// an extension type (no way to unregister it), a delegate descriptor already compiled by
			// the first run, and a handler on a static event.
			"Interop_Bound_ExtensionMethodsRegisteredLaterAreFound",
			"Interop_Cache_SameDelegateBuildsOneDescriptor",
			"Interop_SEvent_DetachAndReregister",
			// On Mono it passes only the first time it runs in a process, with any descriptor (measured:
			// reflection, then generated, then reflection again fails the last two), so a second run here
			// would fail the main run's.
			"Interop_Event_SubscribedSenderCanBeCollected",
		};

		[Test]
		public void Generated_InteropSuites()
		{
			var policy = new GeneratedPolicy();
			var failures = new List<string>();
			int ok = 0;

			// The fixtures expect the registry they get in the main run, not what a runner registered.
			using (new RegistrySnapshot(GeneratedTypes.Concat(RegistrySnapshot.UserTypes()).Distinct().ToArray()))
			{
				UserData.RegistrationPolicy = policy;

				var runner = new TestRunner(r => { });

				foreach (TestResult r in runner.IterateOnTests(null, SuiteSkips, InteropFixtures))
				{
					if (r.Type == TestResultType.Fail)
						failures.Add(r.TestName + ": " + r.Message);
					else if (r.Type == TestResultType.Ok)
						ok++;
				}
			}

			Assert.AreEqual(0, failures.Count, string.Join("\n", failures.ToArray()));
			Assert.Greater(ok, 300);
			Assert.Greater(policy.Substituted.Count, 30, "generated descriptors were used");
		}
	}
}
