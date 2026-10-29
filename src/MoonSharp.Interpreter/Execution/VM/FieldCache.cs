namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// The inline cache of one instruction that reads or writes a field or global with a constant string
	/// key: where the last lookup found the key, so the next one on the same tables skips every hash
	/// lookup. Monomorphic and keyed by table identity (Luau's GETTABLEKS cache, stage A of T1 in
	/// docs/research/luau.md). Valid while each table it passed keeps its shape
	/// (<see cref="Table.StampedShape"/>: the same table, no key added, no entries moved), its metatable
	/// and that metatable's __index. It holds shapes, not tables, so it keeps no table alive.
	/// </summary>
	internal sealed class FieldCache
	{
		internal const int MaxDepth = 4;

		// A table that did not hold the key: the lookup went on to the table its metatable's __index names.
		internal struct Level
		{
			internal long Shape;
			// the key's dead entry in the table, or -1 when it had none
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
		long m_HolderShape;
		int m_Entry;

		internal FieldCache(string key)
		{
			m_Key = key;
			m_Hash = Table.StringHash(key);
		}

		/// <summary>
		/// The cached value for a lookup of the key starting at <paramref name="t"/>; false on a miss.
		/// </summary>
		internal bool TryGet(Table t, out LuaValue value)
		{
			int depth = m_Depth;

			for (int k = 0; k < depth; k++)
			{
				ref Level l = ref m_Levels[k];

				if (t.Shape != l.Shape || (l.DeadEntry >= 0 && t.EntryValue(l.DeadEntry).Type != DataType.Nil))
					goto miss;

				Table m = t.MetaTable;

				if (m == null || m.Shape != l.MetaShape)
					goto miss;

				LuaValue h = m.EntryValue(l.IndexEntry);

				if (h.Type != DataType.Table)
					goto miss;

				t = (Table)h.Object;
			}

			if (depth < 0 || t.Shape != m_HolderShape)
				goto miss;

			// nil when the key died or was removed since
			value = t.EntryValue(m_Entry);
			return value.Type != DataType.Nil;

			miss:
			value = default(LuaValue);
			return false;
		}

		/// <summary>
		/// Stores <paramref name="value"/> (not nil) when the key is cached as live in <paramref name="t"/>
		/// itself; false on a miss.
		/// </summary>
		internal bool TrySet(Table t, LuaValue value)
		{
			if (m_Depth != 0 || t.Shape != m_HolderShape || t.EntryValue(m_Entry).Type == DataType.Nil)
				return false;

			t.SetEntryValue(m_Entry, value);
			return true;
		}

		/// <summary>
		/// The live entry of the key in <paramref name="t"/>, cached as a lookup that found it there; -1
		/// when <paramref name="t"/> has no value for it.
		/// </summary>
		internal int FindLive(Table t)
		{
			int e = t.FindStringEntry(m_Key, m_Hash);

			if (e >= 0 && t.EntryValue(e).Type != DataType.Nil)
			{
				SetHolder(0, t, e);
				return e;
			}

			return -1;
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
						SetHolder(depth, t, e);
						last = t;
						return true;
					}
				}

				Table m = t.MetaTable;
				int ie = (m != null) ? m.FindStringEntry("__index") : -1;
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

		void SetHolder(int depth, Table t, int entry)
		{
			m_Depth = depth;
			m_HolderShape = t.StampedShape;
			m_Entry = entry;
		}
	}
}
