using System;
using System.Collections.Generic;
using System.Linq;
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

		// Every DataType value is a valid argument: one beyond the converter table (TailCallRequest,
		// YieldRequest) has no converter, as the docs say, instead of throwing IndexOutOfRangeException.
		// Tools that ask about every DataType (the LuaCATS generator) hit this.
		[Test]
		public void CustomConverters_AnyDataTypeIsAccepted()
		{
			CustomConvertersCollection converters = Script.GlobalOptions.CustomConverters;

			foreach (DataType dt in Enum.GetValues(typeof(DataType)))
				Assert.IsNull(converters.GetScriptToClrCustomConversion(dt, typeof(InteropTests)), dt.ToString());

			Assert.Throws<ArgumentException>(() => converters.SetScriptToClrCustomConversion(DataType.YieldRequest, typeof(InteropTests), v => null));
		}

	}
}
