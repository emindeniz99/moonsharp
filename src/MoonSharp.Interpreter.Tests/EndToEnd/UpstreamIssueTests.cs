using System;
using System.Linq.Expressions;
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

		public class Connection
		{
			public string Name = "base";
		}

		public class SecureConnection : Connection
		{
			public SecureConnection() { Name = "secure"; }
		}

		public class Command
		{
			public Connection Connection { get; set; }
			public int Timeout = 10;
		}

		public class SecureCommand : Command
		{
			public new SecureConnection Connection { get; set; }
			public new int Timeout = 30;
		}

		public class LegacyCommand : Command
		{
			private new int Timeout = 99;
			public int PrivateTimeout() { return Timeout; }
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/286: a type that hides an inherited
		// property or field with "new" (common in database and UI libraries) could not be
		// registered at all: "Multiple members named Connection are being added".
		// Scripts see the member C# code sees through the derived type.
		[Test]
		public void Issue286_MembersHiddenWithNewUseTheDerivedMember()
		{
			UserData.RegisterType<SecureCommand>();
			UserData.RegisterType<SecureConnection>();

			Script s = new Script();
			s.Globals["cmd"] = new SecureCommand() { Connection = new SecureConnection() };

			DynValue r = s.DoString("return cmd.Connection.Name .. ' ' .. cmd.Timeout");

			Assert.AreEqual("secure 30", r.String);
		}

		// A private "new" member hides nothing from code outside the type, so scripts keep
		// seeing the inherited public one.
		[Test]
		public void Issue286_PrivateNewMemberKeepsTheInheritedOne()
		{
			UserData.RegisterType<LegacyCommand>();

			Script s = new Script();
			s.Globals["cmd"] = new LegacyCommand();

			Assert.AreEqual(10.0, s.DoString("return cmd.Timeout").Number);
		}

		// https://github.com/moonsharp-devs/moonsharp/issues/291: a delegate compiled from an
		// expression tree has a method without a declaring type, and passing it to a script threw
		// NullReferenceException. Hosts build such delegates for rules and generated accessors.
		[Test]
		public void Issue291_CompiledExpressionDelegateIsCallable()
		{
			ParameterExpression x = Expression.Parameter(typeof(int), "x");
			Func<int, int> twice = Expression.Lambda<Func<int, int>>(Expression.Multiply(x, Expression.Constant(2)), x).Compile();

			Script s = new Script();
			s.Globals["twice"] = twice;

			Assert.AreEqual(42.0, s.DoString("return twice(21)").Number);
		}
	}
}
