using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MoonSharp.Interpreter.Interop;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.Units
{
	[TestFixture]
	public class InteropTests
	{
		[Test]
		public void Converter_FromObject()
		{
			//DynValue v;
			//int? x = 3;
			//int? y = null;

			//v = Converter.FromObject(1);
			//v = Converter.FromObject(x);
			//v = Converter.FromObject(y);




		}

		// Where the runtime cannot generate code (NativeAOT, IL2CPP), MoonSharp must know it, or it builds
		// expression-tree adapters that run interpreted (NativeAOT) or fail. NativeAOT's expression
		// interpreter fooled the old probe, which compiled a lambda and waited for an exception.
		[Test]
		public void Platform_IsRunningOnAOT_WhenRuntimeHasNoDynamicCode()
		{
			PropertyInfo dynamicCode = Type.GetType("System.Runtime.CompilerServices.RuntimeFeature")?.GetProperty("IsDynamicCodeSupported");

			if (dynamicCode != null && !(bool)dynamicCode.GetValue(null, null))
				Assert.IsTrue(Script.GlobalOptions.Platform.IsRunningOnAOT());
		}

	}
}
