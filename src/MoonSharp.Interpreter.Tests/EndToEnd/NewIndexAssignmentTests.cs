using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Multiple assignment through a __newindex metamethod. See issue #236 -- only the
	/// first value used to reach the metamethod, the rest arrived as Void. The stores run from the
	/// last target to the first, as in Lua 5.2 (the order lua5.2 logs).
	/// </summary>
	[TestFixture]
	public class NewIndexAssignmentTests
	{
		// Records every (key, value) pair __newindex receives into a log string, so both
		// the values and the number of invocations are asserted.
		private static string Log(string assignment)
		{
			string script = @"
				local log = ''
				local MT = {}
				function MT.__newindex(t, k, v)
					log = log .. k .. '=' .. tostring(v) .. ';'
				end
				local T = setmetatable({}, MT)
				" + assignment + @"
				return log
			";

			return new Script().DoString(script).String;
		}

		[Test]
		public void SingleAssignment()
		{
			Assert.AreEqual("A=1;", Log("T.A = 1"));
		}

		[Test]
		public void TwoValues()
		{
			Assert.AreEqual("B=2;A=1;", Log("T.A, T.B = 1, 2"));
		}

		[Test]
		public void ThreeValues()
		{
			Assert.AreEqual("C=3;B=2;A=1;", Log("T.A, T.B, T.C = 1, 2, 3"));
		}

		[Test]
		public void MoreTargetsThanValues()
		{
			// Lua pads the missing values with nil.
			Assert.AreEqual("C=nil;B=nil;A=1;", Log("T.A, T.B, T.C = 1"));
		}

		[Test]
		public void MoreValuesThanTargets()
		{
			Assert.AreEqual("B=2;A=1;", Log("T.A, T.B = 1, 2, 3"));
		}

		[Test]
		public void ComputedKeys()
		{
			Assert.AreEqual("B=2;A=1;", Log("local x, y = 'A', 'B' T[x], T[y] = 1, 2"));
		}

		[Test]
		public void MixedWithLocalTarget()
		{
			Assert.AreEqual("B=2;", Log("local n n, T.B = 1, 2"));
		}

		[Test]
		public void ValuesFromAFunctionCall()
		{
			Assert.AreEqual("B=2;A=1;", Log("local function f() return 1, 2 end T.A, T.B = f()"));
		}

		[Test]
		public void AssignmentsAreIndependentOfSurroundingStack()
		{
			// A stack leak or over-pop inside the assignment would corrupt these locals.
			string script = @"
				local MT = {}
				function MT.__newindex(t, k, v) end
				local T = setmetatable({}, MT)
				local a, b, c = 10, 20, 30
				T.A, T.B, T.C = 1, 2, 3
				return a + b + c
			";

			Assert.AreEqual(60.0, new Script().DoString(script).Number);
		}
	}
}
