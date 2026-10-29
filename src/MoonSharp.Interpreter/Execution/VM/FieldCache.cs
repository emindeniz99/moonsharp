namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// The inline cache of one instruction that reads or writes a field or global with a constant string
	/// key: the path the last lookup took, so the next one skips the hash lookups. It is keyed by layout,
	/// not by table: the holder of the key is checked by trying the cached entry index, which every table
	/// that got the same keys in the same order shares (Luau's slot prediction, stage B of T1 in
	/// docs/research/luau.md), so one site over many objects of one shape hits. The tables passed on the
	/// way through __index are checked by identity (<see cref="Table.StampedShape"/>: the same table, no
	/// key added, no entries moved) or, for another table, by looking the key up in it; their metatables by
	/// identity or by their "__index" entry. Every check reads the tables themselves, so no change to a
	/// table can leave a stale hit, and the cache keeps no table alive.
	/// </summary>
	internal sealed class FieldCache
	{
		internal const int MaxDepth = 4;

		const string IndexKey = "__index";

		// A table that did not hold the key: the lookup went on to the table its metatable's __index names.
		internal struct Level
		{
			internal long Shape;
			// the key's dead entry in that table, or -1 when it had none
			internal int DeadEntry;
			// the shape of its metatable, and the entry of "__index" there
			internal long MetaShape;
			internal int IndexEntry;
		}

		readonly string m_Key;
		readonly int m_Hash;

		// Levels passed before the holder; null until a lookup goes through __index.
		Level[] m_Levels;
		// -1: nothing cached
		int m_Depth = -1;
		// the entry of the key in the holder
		int m_Entry;

		internal FieldCache(string key)
		{
			m_Key = key;
			m_Hash = Table.StringHash(key);
		}

		/// <summary>
		/// The value of a lookup of the key starting at <paramref name="t"/> when it takes the cached path to a
		/// live value; false on a miss.
		/// </summary>
		internal bool TryGet(Table t, out LuaValue value)
		{
			int depth = m_Depth;

			for (int k = 0; k < depth; k++)
			{
				ref Level l = ref m_Levels[k];

				if (t.Shape == l.Shape)
				{
					// the same table, unchanged: only its dead entry for the key can have got a value
					if (l.DeadEntry >= 0 && t.EntryValue(l.DeadEntry).Type != DataType.Nil)
						goto miss;
				}
				else
				{
					int e = t.FindStringEntry(m_Key, m_Hash);

					if (e >= 0 && t.EntryValue(e).Type != DataType.Nil)
						goto miss;
				}

				Table m = t.MetaTable;
				LuaValue h;

				if (m == null)
					goto miss;

				// The same metatable, unchanged, still has "__index" in that entry; another one is checked.
				if (m.Shape == l.MetaShape)
					h = m.EntryValue(l.IndexEntry);
				else if (!m.TryGetEntry(l.IndexEntry, IndexKey, out h))
					goto miss;

				if (h.Type != DataType.Table)
					goto miss;

				t = (Table)h.Object;
			}

			// With nothing cached (depth -1) the entry is tried on t itself: the check is exact anyway.
			if (t.TryGetEntry(m_Entry, m_Key, out value) && value.Type != DataType.Nil)
				return true;

			miss:
			value = default(LuaValue);
			return false;
		}

		/// <summary>
		/// Stores <paramref name="value"/> (not nil) when <paramref name="t"/> has a value for the key in the
		/// cached entry, or else anywhere (which is cached then); false when it has none.
		/// </summary>
		internal bool TrySet(Table t, ref LuaValue value)
		{
			if (t.TrySetEntry(m_Entry, m_Key, ref value))
				return true;

			int e = t.FindStringEntry(m_Key, m_Hash);

			if (e < 0 || !t.TrySetEntry(e, m_Key, ref value))
				return false;

			SetHolder(0, e);
			return true;
		}

		/// <summary>
		/// Stores the key, which <paramref name="t"/> has no value for, with <paramref name="value"/> (not nil).
		/// </summary>
		internal void Add(Table t, LuaValue value)
		{
			t.SetString(m_Key, m_Hash, value);
		}

		/// <summary>
		/// Set once a lookup reached something the cache does not cover (an __index that is not a table, or
		/// more than <see cref="MaxDepth"/> levels): the instruction then stops trying the cache.
		/// </summary>
		internal bool Disabled;

		/// <summary>
		/// Looks the key up from <paramref name="t"/> through tables only (raw reads, __index tables) and
		/// caches the path. True with the value (nil when no table has it and the last has no __index); false
		/// when the path reaches something the cache does not cover: the caller then runs the general lookup
		/// from <paramref name="last"/>, the table whose __index it could not follow.
		/// </summary>
		internal bool Lookup(Table t, out Table last, out LuaValue value)
		{
			for (int depth = 0; ; depth++)
			{
				int e = t.FindStringEntry(m_Key, m_Hash);

				if (e >= 0)
				{
					value = t.EntryValue(e);

					if (value.Type != DataType.Nil)
					{
						SetHolder(depth, e);
						last = t;
						return true;
					}
				}

				Table m = t.MetaTable;
				int ie = (m != null) ? m.FindStringEntry(IndexKey) : -1;
				LuaValue h = (ie >= 0) ? m.EntryValue(ie) : LuaValue.Nil;
				last = t;

				if (h.Type == DataType.Nil)
				{
					m_Depth = -1;
					value = LuaValue.Nil;
					return true;
				}

				if (h.Type != DataType.Table || depth == MaxDepth)
				{
					m_Depth = -1;
					Disabled = true;
					value = default(LuaValue);
					return false;
				}

				if (m_Levels == null)
					m_Levels = new Level[MaxDepth];

				ref Level l = ref m_Levels[depth];
				l.Shape = t.StampedShape;
				l.DeadEntry = e;
				l.MetaShape = m.StampedShape;
				l.IndexEntry = ie;

				t = (Table)h.Object;
			}
		}

		void SetHolder(int depth, int entry)
		{
			m_Depth = depth;
			m_Entry = entry;
		}
	}
}
