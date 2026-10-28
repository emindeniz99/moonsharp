#if EMBEDTEST || UNITY_5_3_OR_NEWER || UNITY_EDITOR || UNITY_STANDALONE

// The NUnit subset the tests use, for builds without NUnit: Unity compiles the test sources
// itself (src/Unity/UnityTestBed) and src/TestRunners/EmbeddedTestRunner does the same on .NET
// so a missing member fails a normal build. Comparisons follow NUnit 3: numbers of different
// types compare by value, collections element by element, Throws wants the exact type.

using System;
using System.Collections;

namespace NUnit.Framework
{
	public class TestAttribute : Attribute
	{ }

	public class IgnoreAttribute : Attribute
	{ }

	public class TestFixtureAttribute : Attribute
	{ }

	public class ExpectedExceptionAttribute : Attribute
	{
		public Type ExpectedException { get; set; }

		public ExpectedExceptionAttribute(Type t)
		{
			ExpectedException = t;
		}
	}

	public class AssertionException : Exception
	{
		public AssertionException(string message) : base(message)
		{ }
	}

	public static class Assert
	{
		public static void AreEqual(object expected, object actual, string message = null)
		{
			if (!AreEqualCore(expected, actual))
				Fail(message ?? string.Format("{0} was expected, {1} was returned", Show(expected), Show(actual)));
		}

		public static void AreNotEqual(object expected, object actual, string message = null)
		{
			if (AreEqualCore(expected, actual))
				Fail(message ?? string.Format("anything but {0} was expected", Show(expected)));
		}

		public static void AreSame(object expected, object actual, string message = null)
		{
			if (!ReferenceEquals(expected, actual))
				Fail(message ?? "the same object was expected");
		}

		public static void IsTrue(bool condition, string message = null)
		{
			if (!condition)
				Fail(message ?? "true was expected");
		}

		public static void True(bool condition, string message = null)
		{
			IsTrue(condition, message);
		}

		public static void IsFalse(bool condition, string message = null)
		{
			if (condition)
				Fail(message ?? "false was expected");
		}

		public static void IsNull(object o, string message = null)
		{
			if (o != null)
				Fail(message ?? string.Format("null was expected, {0} was returned", Show(o)));
		}

		public static void IsNotNull(object o, string message = null)
		{
			if (o == null)
				Fail(message ?? "a non-null value was expected");
		}

		public static void IsNotEmpty(string s, string message = null)
		{
			if (string.IsNullOrEmpty(s))
				Fail(message ?? "a non-empty string was expected");
		}

		public static void Greater(double arg1, double arg2, string message = null)
		{
			if (!(arg1 > arg2))
				Fail(message ?? string.Format("{0} was expected to be greater than {1}", arg1, arg2));
		}

		public static void Less(double arg1, double arg2, string message = null)
		{
			if (!(arg1 < arg2))
				Fail(message ?? string.Format("{0} was expected to be less than {1}", arg1, arg2));
		}

		public static void Fail(string message = null)
		{
			throw new AssertionException("Test failed : " + (message ?? "Assert.Fail"));
		}

		// Exactly T, as NUnit's Assert.Throws.
		public static T Throws<T>(Action code) where T : Exception
		{
			Exception ex = Run(code);
			if (ex == null || ex.GetType() != typeof(T))
				Fail(string.Format("{0} was expected, {1}", typeof(T).Name, ex == null ? "nothing was thrown" : ex.GetType().Name + " was thrown"));
			return (T)ex;
		}

		// T or a subclass, as NUnit's Assert.Catch.
		public static T Catch<T>(Action code) where T : Exception
		{
			T ex = Run(code) as T;
			if (ex == null)
				Fail(string.Format("{0} or a subclass was expected", typeof(T).Name));
			return ex;
		}

		static Exception Run(Action code)
		{
			try
			{
				code();
				return null;
			}
			catch (Exception ex)
			{
				return ex;
			}
		}

		internal static bool AreEqualCore(object expected, object actual)
		{
			if (expected == null || actual == null)
				return expected == null && actual == null;

			if (IsNumber(expected) && IsNumber(actual))
			{
				if (expected is double || expected is float || actual is double || actual is float)
				{
					double e = Convert.ToDouble(expected), a = Convert.ToDouble(actual);
					return e.Equals(a); // NaN equals NaN, as in NUnit
				}

				return Convert.ToDecimal(expected) == Convert.ToDecimal(actual);
			}

			if (!(expected is string) && !(actual is string) && expected is IEnumerable && actual is IEnumerable)
			{
				IEnumerator e = ((IEnumerable)expected).GetEnumerator(), a = ((IEnumerable)actual).GetEnumerator();
				while (true)
				{
					bool eMore = e.MoveNext(), aMore = a.MoveNext();
					if (eMore != aMore)
						return false;
					if (!eMore)
						return true;
					if (!AreEqualCore(e.Current, a.Current))
						return false;
				}
			}

			return expected.Equals(actual);
		}

		static bool IsNumber(object o)
		{
			return o is sbyte || o is byte || o is short || o is ushort || o is int || o is uint
				|| o is long || o is ulong || o is float || o is double || o is decimal;
		}

		static string Show(object o)
		{
			return o == null ? "null" : o.ToString();
		}
	}

	public static class StringAssert
	{
		public static void Contains(string expected, string actual, string message = null)
		{
			if (actual == null || actual.IndexOf(expected, StringComparison.Ordinal) < 0)
				Assert.Fail(message ?? string.Format("a string containing \"{0}\" was expected, \"{1}\" was returned", expected, actual));
		}

		public static void DoesNotContain(string expected, string actual, string message = null)
		{
			if (actual != null && actual.IndexOf(expected, StringComparison.Ordinal) >= 0)
				Assert.Fail(message ?? string.Format("a string without \"{0}\" was expected, \"{1}\" was returned", expected, actual));
		}

		public static void StartsWith(string expected, string actual, string message = null)
		{
			if (actual == null || !actual.StartsWith(expected, StringComparison.Ordinal))
				Assert.Fail(message ?? string.Format("a string starting with \"{0}\" was expected, \"{1}\" was returned", expected, actual));
		}
	}

	public static class CollectionAssert
	{
		public static void AreEqual(IEnumerable expected, IEnumerable actual, string message = null)
		{
			Assert.AreEqual(expected, actual, message);
		}
	}
}

#endif
