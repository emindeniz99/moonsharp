using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	public static class Utils
	{
		/// <summary>
		/// Runs code under another thread culture. Scripts must give the same results on every device,
		/// whatever its regional settings.
		/// </summary>
		public static T WithCulture<T>(string cultureName, Func<T> action)
		{
			var thread = System.Threading.Thread.CurrentThread;
			var previous = thread.CurrentCulture;

			try
			{
				thread.CurrentCulture = new System.Globalization.CultureInfo(cultureName);
				return action();
			}
			finally
			{
				thread.CurrentCulture = previous;
			}
		}

		public static void DynAssert(DynValue result, params object[] args)
		{
			if (args == null)
				args = new object[1] { DataType.Void };


			if (args.Length == 1)
			{
				DynAssertValue(args[0], result);
			}
			else
			{
				Assert.AreEqual(DataType.Tuple, result.Type);
				Assert.AreEqual(args.Length, result.Tuple.Length);

				for(int i = 0; i < args.Length; i++)
					DynAssertValue(args[i], result.Tuple[i]);
			}
		}

		private static void DynAssertValue(object reference, DynValue dynValue)
		{
			if (reference == (object)DataType.Void)
			{
				Assert.AreEqual(DataType.Void, dynValue.Type);
			}
			else if (reference == null)
			{
				Assert.AreEqual(DataType.Nil, dynValue.Type);
			}
			else if (reference is double)
			{
				Assert.AreEqual(DataType.Number, dynValue.Type);
				Assert.AreEqual((double)reference, dynValue.Number);
			}
			else if (reference is int)
			{
				Assert.AreEqual(DataType.Number, dynValue.Type);
				Assert.AreEqual((int)reference, dynValue.Number);
			}
			else if (reference is string)
			{
				Assert.AreEqual(DataType.String, dynValue.Type);
				Assert.AreEqual((string)reference, dynValue.String);
			}
		}


	}
}
