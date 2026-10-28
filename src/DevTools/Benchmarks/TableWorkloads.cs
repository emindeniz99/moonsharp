using System;
using System.Collections.Generic;
using System.Linq;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Table workloads for <c>ab A B rounds tables</c> and retained bytes per table for
	/// <c>ab A B 0 tablemem</c>. The table cases of <see cref="LuaWorkloads"/> run with a tenth of their
	/// iterations per call; the others are below. Each source returns function(n); time and allocation are
	/// reported per inner iteration (n * PerArg).
	/// </summary>
	static class TableWorkloads
	{
		public sealed class Case
		{
			public string Name;
			public string Source;
			public int Arg;
			public int PerArg = 1;
		}

		static readonly string[] s_FromLuaWorkloads =
		{
			"table_array_insert", "table_array_read", "table_hash_set_get", "gc_small_tables", "table_sort_52",
			"table_concat", "table_insert_len_10k", "table_remove_readd_10k",
		};

		static readonly Case[] s_Own =
		{
			// A sliding window of 32 live string keys out of 64: every iteration deletes one key and stores one
			// that was deleted earlier (dead keys, their collection, rebuilds of the hash part).
			new Case { Name = "hash_delete_reinsert", Arg = 200000, Source = "local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\nreturn function(n) local t = {} for i = 1, 32 do t[keys[i]] = i end local s, j = 0, 1 for i = 1, n do t[keys[j]] = nil local k = keys[(j + 31) % 64 + 1] t[k] = i s = s + t[k] j = j % 64 + 1 end return s end" },
			// A FIFO queue over ever-growing integer keys with 32 live entries (head deleted, tail appended).
			new Case { Name = "queue_int_keys", Arg = 200000, Source = "return function(n) local q, head, tail, s = {}, 1, 1, 0 for i = 1, 32 do q[tail] = i tail = tail + 1 end for i = 1, n do q[tail] = i tail = tail + 1 s = s + q[head] q[head] = nil head = head + 1 end return s end" },
			// pairs over 64 array keys and 64 string keys (per element).
			new Case { Name = "pairs_mixed_128", Arg = 2000, PerArg = 128, Source = "local t = {} for i = 1, 64 do t[i] = i end for i = 1, 64 do t['k' .. i] = i end\nreturn function(n) local s = 0 for r = 1, n do for k, v in pairs(t) do s = s + v end end return s end" },
			// pairs over 128 string keys (per element).
			new Case { Name = "pairs_hash_128", Arg = 2000, PerArg = 128, Source = "local t = {} for i = 1, 128 do t['k' .. i] = i end\nreturn function(n) local s = 0 for r = 1, n do for k, v in pairs(t) do s = s + v end end return s end" },
			// Object-like tables: build a 4-field record by assignment, read every field (per record).
			new Case { Name = "record_build_read", Arg = 100000, Source = "return function(n) local s = 0 for i = 1, n do local r = {} r.id = i r.name = 'x' r.level = 3 r.score = i s = s + r.id + r.level + r.score end return s end" },
		};

		public static IEnumerable<Case> Cases
		{
			get
			{
				return s_FromLuaWorkloads
					.Select(name => LuaWorkloads.Cases.First(c => c.Name == name))
					.Select(c => new Case { Name = c.Name, Source = c.Source, Arg = (int)Math.Max(1, c.Iterations / 10) })
					.Concat(s_Own);
			}
		}

		// Shapes for retained bytes per table (values are small numbers in every shape).
		static readonly string[,] s_Shapes =
		{
			{ "{}", "return function() return {} end" },
			{ "1 string key", "return function() return { x = 1 } end" },
			{ "4 string keys", "return function() return { a = 1, b = 2, c = 3, d = 4 } end" },
			{ "16 string keys", "return function() return { a = 1, b = 2, c = 3, d = 4, e = 5, f = 6, g = 7, h = 8, i = 9, j = 10, k = 11, l = 12, m = 13, n = 14, o = 15, p = 16 } end" },
			{ "1 array item", "return function() return { 1 } end" },
			{ "4 array items", "return function() return { 1, 2, 3, 4 } end" },
			{ "16 array items", "return function() return { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 } end" },
			{ "{ i, i + 1, x = i } (gc_small_tables)", "return function() return { 1, 2, x = 1 } end" },
			{ "4 fields assigned after {}", "return function() local t = {} t.a = 1 t.b = 2 t.c = 3 t.d = 4 return t end" },
			{ "array 4, then 1 string key", "return function() local t = { 1, 2, 3, 4 } t.n = 4 return t end" },
			{ "string key, then 4 array items", "return function() local t = { n = 4 } t[1] = 1 t[2] = 2 t[3] = 3 t[4] = 4 return t end" },
		};

		/// <summary>
		/// Bytes retained per table, the returned DynValue included, for 20,000 live tables of each shape,
		/// then the bytes retained by one queue with 32 live entries after 1M push/pop pairs.
		/// </summary>
		public static int RetainedBytes(Func<string, object>[] run, Func<object, object[], object>[] invoke)
		{
			const int count = 20000;
			Console.WriteLine("| Shape | A bytes/table | B bytes/table |");
			Console.WriteLine("|---|---:|---:|");

			for (int s = 0; s < s_Shapes.GetLength(0); s++)
			{
				var bytes = new long[run.Length];

				for (int b = 0; b < run.Length; b++)
				{
					object fn = run[b](s_Shapes[s, 1]);
					var noArgs = new object[0];
					invoke[b](fn, noArgs);
					var keep = new List<object>(count);
					long before = Settle();

					for (int i = 0; i < count; i++)
						keep.Add(invoke[b](fn, noArgs));

					long after = Settle();
					GC.KeepAlive(keep);
					bytes[b] = (after - before) / count;
				}

				Console.WriteLine("| {0} | {1} | {2} |", s_Shapes[s, 0], bytes[0], bytes[1]);
			}

			var queue = new long[run.Length];

			for (int b = 0; b < run.Length; b++)
			{
				object fn = run[b]("return function(n) local q, head, tail = {}, 1, 1 for i = 1, 32 do q[tail] = i tail = tail + 1 end for i = 1, n do q[tail] = i tail = tail + 1 q[head] = nil head = head + 1 end return q end");
				long before = Settle();
				object q = invoke[b](fn, new object[] { 1000000 });
				queue[b] = Settle() - before;
				GC.KeepAlive(q);
			}

			Console.WriteLine("| queue, 32 live after 1M push/pop (whole table) | {0} | {1} |", queue[0], queue[1]);
			return 0;
		}

		static long Settle()
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			return GC.GetTotalMemory(true);
		}
	}
}
