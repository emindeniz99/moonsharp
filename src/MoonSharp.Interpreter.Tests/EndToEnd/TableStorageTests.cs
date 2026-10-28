using System;
using System.Reflection;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Pins how a Table stores entries: which operations allocate, which keys a border (#t) sees and
	/// the order pairs() returns. Scripts and hosts depend on the last two, so a storage change must
	/// keep them exactly as they are.
	/// </summary>
	[TestFixture]
	public class TableStorageTests
	{
		// GC.GetAllocatedBytesForCurrentThread exists on .NET Core 3.0+ only; look it up at run time so
		// this project still builds for netstandard2.0 / net45 and the tests skip elsewhere.
		static readonly Func<long> s_AllocatedBytes = CreateAllocatedBytesReader();

		static Func<long> CreateAllocatedBytesReader()
		{
			MethodInfo mi = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
			return mi == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), mi);
		}

		static long AllocatedBytes(Action action)
		{
			if (s_AllocatedBytes == null)
				Assert.Ignore("GC.GetAllocatedBytesForCurrentThread is not available on this runtime");

			action(); // JIT and first-use allocations happen outside the measurement
			long before = s_AllocatedBytes();
			action();
			return s_AllocatedBytes() - before;
		}

		[Test]
		public void UpdatingAnExistingKeyDoesNotAllocate()
		{
			// A store to a key that already exists must reuse the stored key. Allocating a fresh key
			// DynValue per store made every field update in a loop (o.v = o.v + 1) produce garbage.
			var t = new Table(new Script());
			DynValue one = DynValue.NewNumber(1);
			DynValue floatKey = DynValue.NewNumber(1.5);
			t.Set("field", one);
			t.Set(7, one);
			t.Set(floatKey, one);

			long bytes = AllocatedBytes(() =>
			{
				for (int i = 0; i < 1000; i++)
				{
					t.Set("field", one);
					t.Set(7, one);
					t.Set(DynValue.NewString("field"), one);
					t.Set(floatKey, one);
				}
			});

			// 1000 DynValue.NewString per run are the only allocations left (the caller's key).
			Assert.Less(bytes, 1000 * 80, "bytes allocated by 4000 updates of existing keys: " + bytes);

			bytes = AllocatedBytes(() =>
			{
				for (int i = 0; i < 1000; i++)
				{
					t.Set("field", one);
					t.Set(7, one);
				}
			});

			Assert.AreEqual(0, bytes, "bytes allocated by 2000 updates of existing string and integer keys");
		}

		[Test]
		public void UpdatingAKeyKeepsOneEntryWithTheOriginalKey()
		{
			// Integral float keys and integer keys are the same key; an update must not add a second entry
			// or change how the key prints.
			string res = Script.RunString(@"
				local t = {}
				t[1] = 'a'; t[1.0] = 'b'; t[2^53] = 'c'; t[2^53] = 'd'; t[1.5] = 'e'; t[1.5] = 'f'
				t.s = 'g'; t['s'] = 'h'
				local r = {}
				for k, v in pairs(t) do r[#r + 1] = type(k) .. ':' .. tostring(k) .. '=' .. v end
				return table.concat(r, ',')").String;

			Assert.AreEqual("number:1=b,number:9007199254740992=d,number:1.5=f,string:s=h", res);
		}
	}
}
