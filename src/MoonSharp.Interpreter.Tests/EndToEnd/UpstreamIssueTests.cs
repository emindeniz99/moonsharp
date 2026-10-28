using System;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	public static class UpstreamIssueWidgetSizeExtensions
	{
		public static UpstreamIssueTests.Widget Grow(this UpstreamIssueTests.Widget w) { return w; }
	}

	public static class UpstreamIssueWidgetColorExtensions
	{
		public static UpstreamIssueTests.Widget Paint(this UpstreamIssueTests.Widget w) { return w; }
		public static string Describe(this UpstreamIssueTests.Widget w) { return "painted"; }
	}

	/// <summary>
	/// Regression tests for bugs reported on upstream MoonSharp
	/// (https://github.com/moonsharp-devs/moonsharp/issues). Each test names its issue;
	/// docs/upstream-issues.md lists every issue and what this fork does about it.
	/// </summary>
	[TestFixture]
	public class UpstreamIssueTests
	{
		public class Widget
		{
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/102 (also #305, #328): a host that
		// splits its helpers over several extension classes could call only the first
		// extension method a script used; every later one failed with "cannot access field".
		[Test]
		public void Issue102_EveryRegisteredExtensionMethodIsFound()
		{
			UserData.RegisterType<Widget>();
			UserData.RegisterExtensionType(typeof(UpstreamIssueWidgetSizeExtensions));
			UserData.RegisterExtensionType(typeof(UpstreamIssueWidgetColorExtensions));

			Script s = new Script();
			s.Globals["w"] = new Widget();

			DynValue r = s.DoString("return w:Grow():Paint():Describe()");

			Assert.AreEqual("painted", r.String);
		}
	}
}
