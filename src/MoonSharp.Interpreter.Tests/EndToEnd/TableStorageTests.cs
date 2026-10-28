using System;
using System.Collections.Generic;
using System.Linq;
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

		// ---- Work bounds: #t and dead-key collection must not rescan the whole table per operation ----

		// Table counts the entries its length scans and dead-key sweeps visit in a private thread-static
		// counter; the tests read it by reflection to bound the work without timing anything.
		static long ScanSteps()
		{
			FieldInfo f = typeof(Table).GetField("s_ScanSteps", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.IsNotNull(f, "Table.s_ScanSteps not found");
			return (long)f.GetValue(null);
		}

		static long ScanStepsOf(Script script, string code, out DynValue result)
		{
			long before = ScanSteps();
			result = script.DoString(code);
			return ScanSteps() - before;
		}

		[Test]
		public void TableInsertThenLengthDoesLinearWork()
		{
			// 10k table.insert used to rescan keys 1..n on every call (quadratic: ~0.6 s here).
			DynValue res;
			long steps = ScanStepsOf(new Script(), "local t = {} for i = 1, 10000 do table.insert(t, i) end return #t", out res);

			Assert.AreEqual(10000, res.Number);
			Assert.Less(steps, 4 * 10000, "entries visited by length scans");
		}

		[Test]
		public void AppendAtLengthPlusOneDoesLinearWork()
		{
			DynValue res;
			long steps = ScanStepsOf(new Script(), "local t = {} for i = 1, 10000 do t[#t + 1] = i end return #t", out res);

			Assert.AreEqual(10000, res.Number);
			Assert.Less(steps, 4 * 10000, "entries visited by length scans");
		}

		[Test]
		public void RemoveThenReaddChurnDoesLinearWork()
		{
			// Re-inserting after a nil swept every entry of the table looking for nil values, and the
			// sweep reset the cached length: each remove/re-add pair cost two full passes.
			var script = new Script();
			script.DoString("t, keys = {}, {} for i = 1, 10000 do t[i] = i end for i = 1, 64 do keys[i] = 'k' .. i t[keys[i]] = i end");

			DynValue res;
			long steps = ScanStepsOf(script, @"
				local s, j = 0, 1
				for i = 1, 2000 do
					local v = table.remove(t) t[#t + 1] = v
					local k = keys[j] t[k] = nil t[k] = i
					j = j % 64 + 1
					s = s + #t
				end
				return s", out res);

			Assert.AreEqual(2000 * 10000, res.Number);
			Assert.Less(steps, 10 * 2000, "entries visited by length scans and dead-key sweeps");
		}

		// ---- Border semantics: #t is the first border from 1 (the smallest n with t[n] ~= nil and t[n+1] == nil) ----

		[Test]
		public void LengthWithHolesIsTheFirstBorder()
		{
			string res = Script.RunString(@"
				local r = {}
				local function L(t) r[#r + 1] = #t end
				local t = { 1, 2, nil, 4 }; L(t)
				t = { nil, 2 }; L(t)
				t = { n = 1 }; L(t)
				t = {}; t[1] = 1; t[2] = 2; t[4] = 4; L(t)
				t[3] = 3; L(t)                   -- filling the hole joins the run after it
				t[2] = nil; L(t)                 -- hole in the middle
				t[2] = 2; L(t)
				t[4] = nil; L(t)                 -- nil at the end
				t[#t] = nil; L(t)
				t[10] = 10; L(t)                 -- beyond the border: no change
				for i = 3, 9 do t[i] = i end; L(t)
				t[10] = nil; L(t)
				t[1] = nil; L(t)
				t[1] = 1; L(t)
				t[1.0] = 'one'; t[3.5] = 1; t['11'] = 1; L(t)
				t[0] = 0; t[-1] = -1; L(t)
				t[5] = false; L(t)               -- false is a value, not a hole
				return table.concat(r, ',')").String;

			Assert.AreEqual("2,0,0,2,4,1,4,3,2,2,10,9,0,9,9,9,9", res);
		}

		[Test]
		public void LengthIsCorrectAfterEveryAppendAndRemoveAtTheEnd()
		{
			string res = Script.RunString(@"
				local t, bad = {}, {}
				for i = 1, 300 do
					t[#t + 1] = i
					if #t ~= i then bad[#bad + 1] = 'append ' .. i .. ' -> ' .. #t end
				end
				for i = 300, 1, -1 do
					t[#t] = nil
					if #t ~= i - 1 then bad[#bad + 1] = 'remove ' .. i .. ' -> ' .. #t end
				end
				for i = 1, 50 do t[#t + 1] = i; t[#t + 1] = i; t[#t] = nil end
				if #t ~= 50 then bad[#bad + 1] = 'mixed -> ' .. #t end
				return table.concat(bad, '; ')").String;

			Assert.AreEqual("", res);
		}

		[Test]
		public void TableInsertAndRemoveKeepTheLengthAndContents()
		{
			string res = Script.RunString(@"
				local r = {}
				local t = {}
				local function S() r[#r + 1] = #t .. ':' .. table.concat(t, ',') end
				for i = 1, 5 do table.insert(t, i) end; S()
				table.insert(t, 1, 0); S()
				table.insert(t, 4, 9); S()
				table.insert(t, #t + 1, 7); S()
				r[#r + 1] = table.remove(t); S()
				r[#r + 1] = table.remove(t, 1); S()
				r[#r + 1] = table.remove(t, 3); S()
				while #t > 0 do table.remove(t) end; S()
				r[#r + 1] = tostring(table.remove(t)); S()
				table.insert(t, 'a'); table.insert(t, 'b'); S()
				t[4] = 'd'; table.insert(t, 'c'); S()   -- appending into the hole joins the run after it
				return table.concat(r, ' ')").String;

			Assert.AreEqual("5:1,2,3,4,5 6:0,1,2,3,4,5 7:0,1,2,9,3,4,5 8:0,1,2,9,3,4,5,7 7 7:0,1,2,9,3,4,5 0 6:1,2,9,3,4,5 9 5:1,2,3,4,5 0: nil 0: 2:a,b 4:a,b,c,d", res);
		}

		[Test]
		public void LengthMatchesTheFirstBorderUnderRandomEdits()
		{
			// C# API edits (Set, Remove, Append, Clear, CollectDeadKeys) on keys 1..24, checked after each
			// step against a scan of t[1], t[2], ... A fixed seed keeps the sequence reproducible.
			var rnd = new Random(20261028);
			var t = new Table(new Script());

			for (int step = 0; step < 5000; step++)
			{
				int op = rnd.Next(100);
				int k = rnd.Next(1, 25);

				if (op < 45) t.Set(k, DynValue.NewNumber(step));
				else if (op < 80) t.Set(DynValue.NewNumber(k), DynValue.Nil);
				else if (op < 88) t.Remove(k);
				else if (op < 95) t.Append(DynValue.NewNumber(step));
				else if (op < 98) t.CollectDeadKeys();
				else t.Clear();

				int border = 0;
				while (t.RawGet(border + 1) != null && !t.RawGet(border + 1).IsNil())
					border++;

				Assert.AreEqual(border, t.Length, "after step " + step);
			}
		}

		// ---- pairs order: insertion order, with today's rules for keys that were set to nil ----

		const string PairsToString = @"return function(t)
			local r = {}
			for k, v in pairs(t) do r[#r + 1] = tostring(k) .. '=' .. tostring(v) end
			return table.concat(r, ',')
		end";

		[Test]
		public void PairsOrderIsPinnedForMixedKeysWithRemovalsAndReinsertions()
		{
			// Today's rules, pinned: entries iterate in insertion order; assigning nil leaves the entry in
			// place; assigning a value to a key whose entry is still there keeps its place. Storing a value
			// into a new or nil key after a nil was stored drops the FIRST entry that holds nil (only that
			// one), so a key whose entry was dropped moves to the end when it gets a value again.
			string res = Script.RunString(@"
				local r = {}
				local function P(t) local s = {} for k, v in pairs(t) do s[#s + 1] = tostring(k) .. '=' .. tostring(v) end r[#r + 1] = table.concat(s, ',') end
				local t = {}
				t[1] = 'a'; t.x = 'b'; t[2] = 'c'; t.y = 'd'; t[3] = 'e'; P(t)
				t.x = nil; t[2] = nil; P(t)
				t.x = 'B'; P(t)                  -- x keeps its place; the nil entry of 2 is dropped
				t[2] = 'C'; P(t)                 -- 2 is new again: appended
				t.z = 'f'; P(t)
				t[1] = nil; t.w = 'g'; P(t)      -- new key w drops the nil entry of 1
				t[1] = 'A'; P(t)
				t.y = nil; t.y = 'D'; P(t)       -- y re-set before any other insert: keeps its place
				t[3] = nil; t[3] = nil; t.q = nil; t[3] = 'E'; P(t)
				t.x = nil; t.q = 'h'; t.x = 'X'; P(t)
				for k in pairs(t) do t[k] = nil end; P(t)
				t.m = 1; t[1] = 2; P(t)          -- m drops only y's entry; 1's entry is still there
				t.y = 3; t.x = 4; P(t)           -- y was dropped: appended; x still has an entry
				return table.concat(r, ' | ')").String;

			Assert.AreEqual(
				"1=a,x=b,2=c,y=d,3=e | 1=a,y=d,3=e | 1=a,x=B,y=d,3=e | 1=a,x=B,y=d,3=e,2=C | 1=a,x=B,y=d,3=e,2=C,z=f | " +
				"x=B,y=d,3=e,2=C,z=f,w=g | x=B,y=d,3=e,2=C,z=f,w=g,1=A | x=B,y=D,3=e,2=C,z=f,w=g,1=A | " +
				"x=B,y=D,3=E,2=C,z=f,w=g,1=A | y=D,3=E,2=C,z=f,w=g,1=A,q=h,x=X |  | 1=2,m=1 | 1=2,x=4,m=1,y=3", res);
		}

		[Test]
		public void TableRemoveOnAnEmptyTableLeavesNoEntry()
		{
			// table.remove on an empty table used to store nil under an internal integer key 0 that script
			// key 0 cannot reach; when that entry came first, pairs() failed with "invalid key to 'next'".
			// Lua 5.2 returns t[0] and assigns nil to it, which changes nothing when t[0] is nil.
			var script = new Script();
			DynValue res = script.DoString(@"
				t = {}
				local r = table.remove(t)
				t[1] = 'a'
				local s = {}
				for k, v in pairs(t) do s[#s + 1] = tostring(k) .. '=' .. tostring(v) end
				return tostring(r) .. '|' .. table.concat(s, ',') .. '|' .. #t");

			Assert.AreEqual("nil|1=a|1", res.String);
			Assert.AreEqual(1, script.Globals.Get("t").Table.Pairs.Count(), "entries seen by hosts");
		}

		[Test]
		public void TableRemoveOnAnEmptyTableReturnsAndClearsKeyZero()
		{
			// Lua 5.2: with #t == 0 the default position is 0, so t[0] is returned and set to nil.
			string res = Script.RunString(@"
				local t = { [0] = 'x' }
				local r = table.remove(t)
				return tostring(r) .. ',' .. tostring(t[0]) .. ',' .. tostring(next(t))").String;

			Assert.AreEqual("x,nil,nil", res);
		}

		[Test]
		public void TableRemoveOnAnEmptyTableRejectsNegativePositions()
		{
			// Lua 5.2 checks 1 <= pos <= #t + 1 whenever pos differs from #t; MoonSharp skipped the check
			// on empty tables and stored nil under keys pos..0.
			string res = Script.RunString(@"
				local t = {}
				local ok, err = pcall(table.remove, t, -2)
				return tostring(ok) .. '|' .. tostring(err) .. '|' .. tostring(next(t))").String;

			StringAssert.StartsWith("false|", res);
			StringAssert.Contains("position out of bounds", res);
			StringAssert.EndsWith("|nil", res);
		}

		// The dead-key sweep removes the first entry holding nil and stops (see the pinned test above).
		static void RemoveFirstNil(List<KeyValuePair<string, string>> model)
		{
			int i = model.FindIndex(e => e.Value == null);
			if (i >= 0) model.RemoveAt(i);
		}

		[Test]
		public void PairsOrderMatchesTheInsertionModelUnderRandomEdits()
		{
			// Reference model of today's entry list (including entries that hold nil, which Table.Pairs
			// returns to hosts). Random script-style stores of mixed integer and string keys, C# removes and
			// explicit CollectDeadKeys calls must leave the table's entry list identical to the model's.
			var rnd = new Random(1028);
			var script = new Script();
			var t = new Table(script);
			DynValue tv = DynValue.NewTable(t);
			DynValue pairsToString = script.DoString(PairsToString);
			var model = new List<KeyValuePair<string, string>>();
			bool modelHasNil = false;
			string[] keys = { "1", "2", "3", "4", "5", "6", "a", "b", "c", "d", "e", "f" };

			for (int step = 0; step < 3000; step++)
			{
				string key = keys[rnd.Next(keys.Length)];
				DynValue dkey = char.IsDigit(key[0]) ? DynValue.NewNumber(int.Parse(key)) : DynValue.NewString(key);
				int op = rnd.Next(100);
				int idx = model.FindIndex(e => e.Key == key);

				if (op < 90)
				{
					string val = op < 50 ? step.ToString() : null;
					t.Set(dkey, val == null ? DynValue.Nil : DynValue.NewNumber(step));

					bool wasAbsentOrNil = idx < 0 || model[idx].Value == null;
					if (idx >= 0) model[idx] = new KeyValuePair<string, string>(key, val);
					else model.Add(new KeyValuePair<string, string>(key, val));

					if (modelHasNil && val != null && wasAbsentOrNil)
					{
						RemoveFirstNil(model);
						modelHasNil = false;
					}
					else if (val == null)
					{
						modelHasNil = true;
					}
				}
				else if (op < 97)
				{
					t.Remove(dkey);
					if (idx >= 0) model.RemoveAt(idx);
				}
				else
				{
					t.CollectDeadKeys();
					RemoveFirstNil(model);
					modelHasNil = false;
				}

				string expectedAll = string.Join(",", model.Select(e => e.Key + "=" + (e.Value ?? "nil")));
				string actualAll = string.Join(",", t.Pairs.Select(p => p.Key.ToPrintString() + "=" + (p.Value.IsNil() ? "nil" : p.Value.ToPrintString())));
				Assert.AreEqual(expectedAll, actualAll, "Table.Pairs after step " + step);

				string expectedLua = string.Join(",", model.Where(e => e.Value != null).Select(e => e.Key + "=" + e.Value));
				Assert.AreEqual(expectedLua, script.Call(pairsToString, tv).String, "pairs() after step " + step);
			}
		}

		[Test]
		public void NextSurvivesClearingEveryKeyDuringTraversal()
		{
			// Lua allows assigning nil to existing fields while traversing; every key must still be visited.
			string res = Script.RunString(@"
				local t = {}
				for i = 1, 100 do t[i] = i; t['s' .. i] = i end
				local seen = 0
				for k, v in pairs(t) do seen = seen + 1; t[k] = nil end
				return seen .. ',' .. tostring(next(t)) .. ',' .. #t").String;

			Assert.AreEqual("200,nil,0", res);
		}
	}
}
