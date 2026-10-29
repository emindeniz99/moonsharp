using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// The standard library is registered by reflection over [MoonSharpModuleMethod] members, which a
	/// trimmer removes unless told otherwise. The trimmer is told by ILLink.Descriptors.xml, embedded
	/// in the assembly. These tests keep that file complete. The real proof is the browser publish in
	/// src/DevTools/TrimTest; these catch a forgotten module without needing a browser.
	/// </summary>
	/// <remarks>
	/// Skipped on Unity: Unity compiles the sources, so there is no embedded resource there (the
	/// package ships the same file as link.xml, and the IL2CPP player run proves it keeps the modules).
	/// </remarks>
	[TestFixture]
	public class TrimDescriptorTests
	{
		private static readonly Assembly Interpreter = typeof(Script).Assembly;

		// Types the library registers as userdata itself and exposes to Lua by reflection.
		private static readonly string[] SelfRegisteredUserData = new string[]
		{
			"MoonSharp.Interpreter.CoreLib.IO.FileUserDataBase",
			"MoonSharp.Interpreter.Serialization.Json.JsonNull",
			"MoonSharp.Interpreter.Serialization.Json.JsonEmptyArray",
		};

		private static Dictionary<string, string> LoadPreservedTypes()
		{
#if UNITY_5_3_OR_NEWER
			TestRunner.Skip();
#endif
			using (Stream stream = Interpreter.GetManifestResourceStream("ILLink.Descriptors.xml"))
			{
				Assert.IsNotNull(stream, "ILLink.Descriptors.xml must be embedded in the assembly under exactly that name, or the trimmer ignores it");

				XElement assembly = XDocument.Load(stream).Root.Elements("assembly").Single();
				Assert.AreEqual(Interpreter.GetName().Name, (string)assembly.Attribute("fullname"));

				return assembly.Elements("type").ToDictionary(
					t => (string)t.Attribute("fullname"),
					t => (string)t.Attribute("preserve"));
			}
		}

		[Test]
		public void Descriptor_PreservesEveryModuleType()
		{
			Dictionary<string, string> preserved = LoadPreservedTypes();

			string[] modules = Interpreter.GetTypes()
				.Where(t => t.GetCustomAttributes(typeof(MoonSharpModuleAttribute), false).Length > 0)
				.Select(t => t.FullName)
				.ToArray();

			Assert.Greater(modules.Length, 10, "the module scan found too few types to be meaningful");

			foreach (string module in modules)
			{
				Assert.IsTrue(preserved.ContainsKey(module) && preserved[module] == "all",
					"module " + module + " is registered by reflection but ILLink.Descriptors.xml does not preserve it");
			}
		}

		[Test]
		public void Descriptor_PreservesSelfRegisteredUserData()
		{
			Dictionary<string, string> preserved = LoadPreservedTypes();

			foreach (string name in SelfRegisteredUserData)
			{
				Assert.IsTrue(preserved.ContainsKey(name) && preserved[name] == "all",
					name + " is exposed to Lua by reflection but ILLink.Descriptors.xml does not preserve it");
			}
		}

		[Test]
		public void Descriptor_NamesOnlyTypesThatExist()
		{
			// A renamed class would silently stop being preserved: the trimmer ignores unknown names.
			foreach (string name in LoadPreservedTypes().Keys)
			{
				Assert.IsNotNull(Interpreter.GetType(name), "ILLink.Descriptors.xml names a type that does not exist: " + name);
			}
		}

		[Test]
		public void Substitutions_StubOnlyMembersThatExist()
		{
			// ILLink.Substitutions.xml lets a trimmed app without dynamic code drop System.Linq.Expressions
			// (112 KB brotli in the browser package). A renamed method would silently keep it: the trimmer
			// only warns about a member it cannot find.
#if UNITY_5_3_OR_NEWER
			TestRunner.Skip();
#endif
			XElement assembly;

			using (Stream stream = Interpreter.GetManifestResourceStream("ILLink.Substitutions.xml"))
			{
				Assert.IsNotNull(stream, "ILLink.Substitutions.xml must be embedded in the assembly under exactly that name, or the trimmer ignores it");
				assembly = XDocument.Load(stream).Root.Elements("assembly").Single();
			}

			Assert.AreEqual(Interpreter.GetName().Name, (string)assembly.Attribute("fullname"));
			Assert.AreEqual("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", (string)assembly.Attribute("feature"),
				"the stubs are only right where the runtime reports no dynamic code: IsRunningOnAOT is true there, so no descriptor optimizes");
			Assert.AreEqual("false", (string)assembly.Attribute("featurevalue"));

			int methods = 0;

			foreach (XElement type in assembly.Elements("type"))
			{
				Type t = Interpreter.GetType((string)type.Attribute("fullname"));
				Assert.IsNotNull(t, "ILLink.Substitutions.xml names a type that does not exist: " + (string)type.Attribute("fullname"));

				foreach (XElement method in type.Elements("method"))
				{
					// "ReturnType Name()": every stubbed method takes no arguments.
					string signature = (string)method.Attribute("signature");
					string[] parts = signature.Split(' ');
					Assert.AreEqual(2, parts.Length, signature);
					Assert.IsTrue(parts[1].EndsWith("()"), signature);
					string name = parts[1].Substring(0, parts[1].Length - 2);

					MethodInfo found = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
						.SingleOrDefault(m => m.Name == name && m.GetParameters().Length == 0 && m.ReturnType.FullName == parts[0]);

					Assert.IsNotNull(found, "ILLink.Substitutions.xml stubs a method that does not exist: " + t.FullName + "::" + signature);
					methods++;
				}
			}

			Assert.AreEqual(5, methods, "every member that references System.Linq.Expressions is stubbed (docs/hosting.md, \"Browser budget\")");
		}
	}
}
