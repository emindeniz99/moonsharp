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

		[Test]
		public void NewIndexRunsForAbsentKeysOnlyWhenTheMetatableIsSetAfterTheFirstStores()
		{
			// A table without a metatable skips the __newindex lookup; that shortcut must not stick to
			// the table once it gets a metatable, nor when the metatable gains __newindex later.
			string res = Script.RunString(@"
				local log = {}
				local t = {}
				t.a = 1; t[1] = 1
				local mt = {}
				setmetatable(t, mt)
				t.b = 2                       -- metatable without __newindex: raw store
				mt.__newindex = function(tt, k, v) log[#log + 1] = tostring(k) rawset(tt, k, v * 10) end
				t.a = 3; t[1] = 3             -- existing keys: raw store, no __newindex
				t.c = 4; t[2] = 4             -- absent keys: __newindex
				t.a = nil; t.a = 5            -- a became absent: __newindex again
				setmetatable(t, nil)
				t.d = 6                       -- metatable removed: raw store
				return table.concat(log, ',') .. '|' .. t.a .. ',' .. t.b .. ',' .. t.c .. ',' .. t.d .. ',' .. t[1] .. ',' .. t[2]").String;

			Assert.AreEqual("c,2,a|50,2,40,6,3,40", res);
		}

		[Test]
		public void NewIndexTableChainStillStoresIntoTheTargetTable()
		{
			string res = Script.RunString(@"
				local store = {}
				local t = setmetatable({}, { __newindex = store })
				t.x = 1
				return tostring(rawget(t, 'x')) .. ',' .. store.x").String;

			Assert.AreEqual("nil,1", res);
		}

		[Test]
		public void StoringWithANilOrNaNKeyStillFailsWithoutAMetatable()
		{
			string res = Script.RunString(@"
				local t = {}
				local ok1, e1 = pcall(function() t[nil] = 1 end)
				local ok2, e2 = pcall(function() t[0/0] = 1 end)
				return tostring(ok1) .. ':' .. e1 .. '|' .. tostring(ok2) .. ':' .. e2").String;

			StringAssert.StartsWith("false:", res);
			StringAssert.Contains("table index is nil", res);
			StringAssert.Contains("|false:", res);
			StringAssert.Contains("table index is NaN", res);
		}
	}
}
