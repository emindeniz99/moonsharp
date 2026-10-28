using System;
using System.Collections.Generic;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// SPIKE (spike/table-order): iteration order of <c>next</c>/<c>pairs</c> and <see cref="Table.Pairs"/>.
	/// </summary>
	internal enum TableOrder
	{
		/// <summary>(a) Full insertion order across array and hash keys (master's behaviour).</summary>
		Insertion,
		/// <summary>(b) Array part 1..n first, then the other keys in insertion order (compact dict).</summary>
		ArrayFirst,
		/// <summary>(c) Lua order: array part, then hash slot order (Lua-style node table, no order guarantee).</summary>
		Unordered,
	}

	/// <summary>
	/// A class representing a Lua table.
	/// </summary>
	/// <remarks>
	/// SPIKE layout: an array part for keys 1..n (Lua-CSharp growth rule: a key goes to the array when it is
	/// at most max(2 * capacity, 4) and at most 4096 past the capacity; on growth, hash keys in the new range
	/// migrate into the array), and one hash part of struct entries for every other key. Assigning nil leaves a
	/// tombstone so <c>next</c> keeps working while a loop clears fields; tombstones are purged when the hash
	/// part is rebuilt, which only happens when a new key is inserted.
	/// The hash part is a compact dict (dense entries in insertion order plus bucket heads) for
	/// <see cref="TableOrder.Insertion"/> and <see cref="TableOrder.ArrayFirst"/>, and a Lua-style node table
	/// (chained scatter table with Brent's variation) for <see cref="TableOrder.Unordered"/>.
	/// </remarks>
	public class Table : RefIdObject, IScriptPrivateResource
	{
		/// <summary>
		/// SPIKE: the ordering variant, read once from MOONSHARP_TABLE_ORDER ("a", "b" or "c"). Without the
		/// variable the build's default applies (-p:TableOrder=A|B|C, default A). static readonly, so the
		/// tier-1 JIT folds the checks below into one variant.
		/// </summary>
		internal static readonly TableOrder DefaultOrder = ReadOrder();

#if TABLE_ORDER_DYNAMIC
		// (d) a per-table switch: the order is an instance field, so no check can be folded away.
		readonly TableOrder m_Order = DefaultOrder;
		TableOrder Order { get { return m_Order; } }
#else
		static readonly TableOrder Order = DefaultOrder;
#endif

		static TableOrder ReadOrder()
		{
			string s = null;

			try { s = Environment.GetEnvironmentVariable("MOONSHARP_TABLE_ORDER"); }
			catch { }

			switch (s)
			{
				case "a": return TableOrder.Insertion;
				case "b": return TableOrder.ArrayFirst;
				case "c": return TableOrder.Unordered;
			}

#if TABLE_ORDER_B
			return TableOrder.ArrayFirst;
#elif TABLE_ORDER_C
			return TableOrder.Unordered;
#else
			return TableOrder.Insertion;
#endif
		}

		struct Entry
		{
			// null: a pointer entry (Insertion: the value lives in m_Array[Hash - 1]) or a free node (Unordered)
			public DynValue Key;
			// null: tombstone (or free node); s_ArraySlot: pointer entry
			public DynValue Value;
			public int Hash;
			// next entry in the same chain, -1 at the end
			public int Next;
		}

		// Marks a pointer entry: an array key logged in the insertion order (variant (a) only).
		static readonly DynValue s_ArraySlot = DynValue.NewString("<array slot>");

		const int MinCapacity = 4;
		const int MaxArrayGap = 4096;

		// m_Array[i] holds key i + 1. null: absent and (Insertion) not logged; a nil value: dead.
		DynValue[] m_Array;
		// Insertion only: keys 1..m_Prefix were appended in order while the hash part was empty, so they
		// come first without pointer entries.
		int m_Prefix;
		Entry[] m_Entries;
		// Insertion/ArrayFirst: 1-based index of the chain head per bucket, 0 = empty.
		int[] m_Buckets;
		// Insertion/ArrayFirst: entries used, tombstones included.
		int m_Count;
		// Unordered: free-position cursor (Lua's lastfree).
		int m_LastFree;
		int m_Shift;
		// The first border: the smallest n >= 0 with t[n + 1] == nil (master's Length semantics).
		int m_Length;
		// Insertion/ArrayFirst: keys that died since the last insert. master's CollectDeadKeys removes dead
		// keys on the next insert, so a key set again after that goes to the end; this list reproduces it.
		// An entry index (>= 0), or ~key for an array key (Insertion only).
		List<int> m_Pending;
		// array growth checks to skip after the array was found too sparse to grow
		int m_GrowSkip;

		readonly Script m_Owner;

		int m_InitArray = 0;

		/// <summary>
		/// Initializes a new instance of the <see cref="Table"/> class.
		/// </summary>
		/// <param name="owner">The owner script.</param>
		public Table(Script owner)
		{
			m_Owner = owner;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Table"/> class.
		/// </summary>
		/// <param name="owner">The owner.</param>
		/// <param name="arrayValues">The values for the "array-like" part of the table.</param>
		public Table(Script owner, params DynValue[] arrayValues)
			: this(owner)
		{
			for (int i = 0; i < arrayValues.Length; i++)
			{
				this.Set(DynValue.NewNumber(i + 1), arrayValues[i]);
			}
		}

		/// <summary>
		/// Gets the script owning this resource.
		/// </summary>
		public Script OwnerScript
		{
			get { return m_Owner; }
		}

		/// <summary>
		/// Removes all items from the Table.
		/// </summary>
		public void Clear()
		{
			m_Array = null;
			m_Entries = null;
			m_Buckets = null;
			m_Count = 0;
			m_LastFree = 0;
			m_Prefix = 0;
			m_Length = 0;
			m_Pending = null;
			m_GrowSkip = 0;
		}

		/// <summary>
		/// Gets the integral key from a double.
		/// </summary>
		private int GetIntegralKey(double d)
		{
			int v = ((int)d);

			if (d >= 1.0 && d == v)
				return v;

			return -1;
		}

		/// <summary>
		/// Gets or sets the
		/// <see cref="System.Object" /> with the specified key(s).
		/// This will marshall CLR and MoonSharp objects in the best possible way.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <value>
		/// The <see cref="System.Object" />.
		/// </value>
		/// <param name="keys">The keys to access the table and subtables</param>
		public object this[params object[] keys]
		{
			get
			{
				return Get(keys).ToObject();
			}
			set
			{
				Set(keys, DynValue.FromObject(OwnerScript, value));
			}
		}

		/// <summary>
		/// Gets or sets the <see cref="System.Object"/> with the specified key(s).
		/// This will marshall CLR and MoonSharp objects in the best possible way.
		/// </summary>
		/// <value>
		/// The <see cref="System.Object"/>.
		/// </value>
		/// <param name="key">The key.</param>
		/// <returns></returns>
		public object this[object key]
		{
			get
			{
				return Get(key).ToObject();
			}
			set
			{
				Set(key, DynValue.FromObject(OwnerScript, value));
			}
		}

		private Table ResolveMultipleKeys(object[] keys, out object key)
		{
			Table t = this;
			key = (keys.Length > 0) ? keys[0] : null;

			for (int i = 1; i < keys.Length; ++i)
			{
				DynValue vt = t.RawGet(key);

				if (vt == null)
					throw new ScriptRuntimeException("Key '{0}' did not point to anything");

				if (vt.Type != DataType.Table)
					throw new ScriptRuntimeException("Key '{0}' did not point to a table");

				t = vt.Table;
				key = keys[i];
			}

			return t;
		}

		/// <summary>
		/// Append the value to the table using the next available integer index.
		/// </summary>
		/// <param name="value">The value.</param>
		public void Append(DynValue value)
		{
			this.CheckScriptOwnership(value);
			SetInt(m_Length + 1, value);
		}

		#region Hashing

		static int HashString(string s)
		{
			// identical to DynValue.GetHashCode() for strings, which caches it
			return (((int)DataType.String) << 27) ^ s.GetHashCode();
		}

		static int HashNumber(double d)
		{
			int i = (int)d;
			return (i == d) ? i : d.GetHashCode();
		}

		int Bucket(int hash)
		{
			// Fibonacci hashing: integral keys hash to themselves, so mix before taking the top bits
			return (int)(((uint)hash * 2654435769u) >> m_Shift);
		}

		int ChainStart(int hash)
		{
			int b = Bucket(hash);
			return Order == TableOrder.Unordered ? b : m_Buckets[b] - 1;
		}

		int FindString(string s, int hash)
		{
			Entry[] entries = m_Entries;

			if (entries == null)
				return -1;

			for (int i = ChainStart(hash); i >= 0; i = entries[i].Next)
			{
				DynValue k = entries[i].Key;

				if (entries[i].Hash == hash && k != null && k.Type == DataType.String && string.Equals(k.String, s))
					return i;
			}

			return -1;
		}

		int FindNumber(double d, int hash)
		{
			Entry[] entries = m_Entries;

			if (entries == null)
				return -1;

			for (int i = ChainStart(hash); i >= 0; i = entries[i].Next)
			{
				DynValue k = entries[i].Key;

				if (entries[i].Hash == hash && k != null && k.Type == DataType.Number && k.Number == d)
					return i;
			}

			return -1;
		}

		int FindOther(DynValue key, int hash)
		{
			Entry[] entries = m_Entries;

			if (entries == null)
				return -1;

			for (int i = ChainStart(hash); i >= 0; i = entries[i].Next)
			{
				DynValue k = entries[i].Key;

				if (entries[i].Hash == hash && k != null && (object.ReferenceEquals(k, key) || k.Equals(key)))
					return i;
			}

			return -1;
		}

		int FindPointer(int arrayKey)
		{
			Entry[] entries = m_Entries;

			if (entries == null)
				return -1;

			for (int i = ChainStart(arrayKey); i >= 0; i = entries[i].Next)
			{
				if (entries[i].Hash == arrayKey && entries[i].Key == null && object.ReferenceEquals(entries[i].Value, s_ArraySlot))
					return i;
			}

			return -1;
		}

		int FindKey(DynValue key)
		{
			switch (key.Type)
			{
				case DataType.String:
					return FindString(key.String, key.GetHashCode());
				case DataType.Number:
					return FindNumber(key.Number, HashNumber(key.Number));
				default:
					return FindOther(key, key.GetHashCode());
			}
		}

		#endregion

		#region Hash part storage

		void Allocate(int capacity)
		{
			int log2 = 0;
			while ((1 << log2) < capacity) log2++;

			m_Entries = new Entry[capacity];
			m_Shift = 32 - log2;

			if (Order == TableOrder.Unordered)
			{
				for (int i = 0; i < capacity; i++)
					m_Entries[i].Next = -1;

				m_LastFree = capacity;
			}
			else
			{
				m_Buckets = new int[capacity];
				m_Count = 0;
			}
		}

		bool IsLive(ref Entry e)
		{
			DynValue v = e.Value;

			if (v == null)
				return false;

			if (object.ReferenceEquals(v, s_ArraySlot))
			{
				DynValue a = m_Array[e.Hash - 1];
				return a != null && a.Type != DataType.Nil;
			}

			return e.Key != null;
		}

		void AddKey(DynValue key, int hash, DynValue value)
		{
			if (Order == TableOrder.Unordered)
			{
				AddNode(key, hash, value);
			}
			else
			{
				AddEntry(key, hash, value);
				AfterInsert();
			}
		}

		// Compact dict: append in insertion order.
		void AddEntry(DynValue key, int hash, DynValue value)
		{
			if (m_Entries == null)
				Allocate(MinCapacity);
			else if (m_Count == m_Entries.Length)
				Rebuild(1);

			int i = m_Count++;
			int b = Bucket(hash);
			m_Entries[i].Key = key;
			m_Entries[i].Value = value;
			m_Entries[i].Hash = hash;
			m_Entries[i].Next = m_Buckets[b] - 1;
			m_Buckets[b] = i + 1;
		}

		// Compact dict: purge tombstones (and, for (a), dead pointer entries), keep the order; grow if
		// more than half the capacity is live.
		void Rebuild(int extra)
		{
			if (m_Pending != null && m_Pending.Count > 0)
				Purge();

			Entry[] old = m_Entries;
			int oldCount = m_Count;
			int live = 0;

			for (int i = 0; i < oldCount; i++)
				if (IsLive(ref old[i]))
					live++;

			int capacity = old.Length;

			if (live + extra > capacity / 2)
				capacity *= 2;

			while (capacity < live + extra)
				capacity *= 2;

			Allocate(capacity);

			for (int i = 0; i < oldCount; i++)
			{
				if (IsLive(ref old[i]))
				{
					int j = m_Count++;
					int b = Bucket(old[i].Hash);
					m_Entries[j] = old[i];
					m_Entries[j].Next = m_Buckets[b] - 1;
					m_Buckets[b] = j + 1;
				}
				else if (object.ReferenceEquals(old[i].Value, s_ArraySlot))
				{
					// dead array key: forget its place in the order, a later insert logs it again at the end
					m_Array[old[i].Hash - 1] = null;
				}
			}
		}

		// Lua node table (ltable.c luaH_newkey): put the key at its main position; on a collision move the
		// colliding node away if it is not in its own main position, else chain the new key to a free node.
		void AddNode(DynValue key, int hash, DynValue value)
		{
			if (m_Entries == null)
				Allocate(MinCapacity);

			while (true)
			{
				Entry[] n = m_Entries;
				int mp = Bucket(hash);

				if (n[mp].Value != null)
				{
					int f = GetFreePos();

					if (f < 0)
					{
						Rehash(1);
						continue;
					}

					int othern = Bucket(n[mp].Hash);

					if (othern != mp)
					{
						while (n[othern].Next != mp)
							othern = n[othern].Next;

						n[othern].Next = f;
						n[f] = n[mp];
						n[mp].Next = -1;
						n[mp].Value = null;
					}
					else
					{
						n[f].Next = n[mp].Next;
						n[mp].Next = f;
						mp = f;
					}
				}

				// a free or dead node at the main position is reused in place, keeping its chain link
				n[mp].Key = key;
				n[mp].Hash = hash;
				n[mp].Value = value;
				return;
			}
		}

		int GetFreePos()
		{
			while (m_LastFree > 0)
			{
				m_LastFree--;

				if (m_Entries[m_LastFree].Key == null)
					return m_LastFree;
			}

			return -1;
		}

		void Rehash(int extra)
		{
			Entry[] old = m_Entries;
			int live = 0;

			for (int i = 0; i < old.Length; i++)
				if (old[i].Key != null && old[i].Value != null)
					live++;

			int capacity = MinCapacity;
			while (capacity < live + extra)
				capacity *= 2;

			Allocate(capacity);

			for (int i = 0; i < old.Length; i++)
				if (old[i].Key != null && old[i].Value != null)
					AddNode(old[i].Key, old[i].Hash, old[i].Value);
		}

		void UpdateEntry(int i, DynValue value, int intKey)
		{
			DynValue old = m_Entries[i].Value;

			if (value.IsNil())
			{
				if (old != null)
				{
					m_Entries[i].Value = null;

					if (intKey > 0 && intKey <= m_Length)
						m_Length = intKey - 1;

					if (Order != TableOrder.Unordered)
						AddPending(i);
				}
			}
			else
			{
				m_Entries[i].Value = value;

				if (old == null)
				{
					if (intKey > 0 && intKey == m_Length + 1)
						ExtendLength();

					AfterInsert();
				}
			}
		}

		#endregion

		#region master's dead-key collection (Insertion/ArrayFirst)

		void AddPending(int code)
		{
			if (m_Pending == null)
				m_Pending = new List<int>();

			m_Pending.Add(code);
		}

		void AfterInsert()
		{
			if (Order != TableOrder.Unordered && m_Pending != null && m_Pending.Count > 0)
				Purge();
		}

		void Unlink(int i)
		{
			int b = Bucket(m_Entries[i].Hash);
			int j = m_Buckets[b] - 1;

			if (j == i)
			{
				m_Buckets[b] = m_Entries[i].Next + 1;
			}
			else
			{
				while (m_Entries[j].Next != i)
					j = m_Entries[j].Next;

				m_Entries[j].Next = m_Entries[i].Next;
			}

			// a removed entry: no key, no value; skipped by lookups and iteration, dropped by Rebuild
			m_Entries[i].Key = null;
			m_Entries[i].Value = null;
		}

		// Removes the keys that are still dead, so a later insert of the same key goes to the end.
		void Purge()
		{
			List<int> pending = m_Pending;
			int explodeAt = int.MaxValue;

			for (int n = 0; n < pending.Count; n++)
			{
				int code = pending[n];

				if (code >= 0)
				{
					if (code >= m_Count)
						continue;

					DynValue v = m_Entries[code].Value;

					if (v == null && m_Entries[code].Key != null)
					{
						Unlink(code);
					}
					else if (object.ReferenceEquals(v, s_ArraySlot))
					{
						// a dead hash key that migrated into the array part
						int k = m_Entries[code].Hash;
						DynValue a = m_Array[k - 1];

						if (a == null || a.Type == DataType.Nil)
						{
							Unlink(code);
							m_Array[k - 1] = null;
						}
					}
				}
				else
				{
					int k = ~code;
					DynValue a = m_Array[k - 1];

					if (a == null || a.Type != DataType.Nil)
						continue;

					if (k <= m_Prefix)
					{
						explodeAt = Math.Min(explodeAt, k);
					}
					else
					{
						int p = FindPointer(k);

						if (p >= 0)
							Unlink(p);

						m_Array[k - 1] = null;
					}
				}
			}

			pending.Clear();

			if (explodeAt <= m_Prefix)
				ExplodePrefix(explodeAt);
		}

		// A key inside the implicit prefix died and was collected: the prefix ends before it, and the live
		// keys after it become pointer entries placed before every other entry.
		void ExplodePrefix(int k)
		{
			int oldPrefix = m_Prefix;
			List<int> moved = null;

			for (int j = k; j <= oldPrefix; j++)
			{
				DynValue a = m_Array[j - 1];

				if (a != null && a.Type != DataType.Nil)
				{
					if (moved == null) moved = new List<int>();
					moved.Add(j);
				}
				else
				{
					m_Array[j - 1] = null;
				}
			}

			m_Prefix = k - 1;

			if (moved == null)
				return;

			Entry[] old = m_Entries;
			int oldCount = m_Count;
			int live = moved.Count;

			for (int i = 0; i < oldCount; i++)
				if (IsLive(ref old[i]))
					live++;

			int capacity = MinCapacity;
			while (capacity < 2 * (live + 1))
				capacity *= 2;

			Allocate(capacity);

			foreach (int j in moved)
				AppendRaw(null, j, s_ArraySlot);

			for (int i = 0; i < oldCount; i++)
			{
				if (IsLive(ref old[i]))
					AppendRaw(old[i].Key, old[i].Hash, old[i].Value);
				else if (object.ReferenceEquals(old[i].Value, s_ArraySlot))
					m_Array[old[i].Hash - 1] = null;
			}
		}

		void AppendRaw(DynValue key, int hash, DynValue value)
		{
			int i = m_Count++;
			int b = Bucket(hash);
			m_Entries[i].Key = key;
			m_Entries[i].Value = value;
			m_Entries[i].Hash = hash;
			m_Entries[i].Next = m_Buckets[b] - 1;
			m_Buckets[b] = i + 1;
		}

		#endregion

		#region Array part

		bool TryGrowArrayFor(int key)
		{
			int len = (m_Array == null) ? 0 : m_Array.Length;
			int newLen = Math.Max(len * 2, MinCapacity);

			if (key > newLen || key - len > MaxArrayGap)
				return false;

			// Lua's "more than half used" rule, checked only when growing (amortised by the doubling): a queue
			// over ever-growing integer keys would otherwise grow the array without bound while few slots live.
			if (len > 0)
			{
				// after a refusal, skip the next len checks so the scan stays amortised O(1) per insert
				if (m_GrowSkip > 0)
				{
					m_GrowSkip--;
					return false;
				}

				int live = 0;

				for (int i = 0; i < len; i++)
				{
					DynValue a = m_Array[i];

					if (a != null && a.Type != DataType.Nil)
						live++;
				}

				if (live + 1 <= newLen / 2)
				{
					m_GrowSkip = len;
					return false;
				}
			}

			DynValue[] grown = new DynValue[newLen];

			if (len > 0)
				Array.Copy(m_Array, grown, len);

			m_Array = grown;

			// migrate hash keys in (len, newLen] into the array
			if (m_Entries != null)
			{
				int limit = (Order == TableOrder.Unordered) ? m_Entries.Length : m_Count;

				for (int i = 0; i < limit; i++)
				{
					DynValue k = m_Entries[i].Key;
					int h = m_Entries[i].Hash;

					if (k == null || k.Type != DataType.Number || h <= len || h > newLen || k.Number != h)
						continue;

					DynValue v = m_Entries[i].Value;

					if (Order == TableOrder.Insertion)
					{
						// keep its place in the order: the entry becomes a pointer to the array slot
						grown[h - 1] = v ?? DynValue.Nil;
						m_Entries[i].Key = null;
						m_Entries[i].Value = s_ArraySlot;
					}
					else
					{
						if (v != null)
							grown[h - 1] = v;

						m_Entries[i].Value = null;
					}
				}
			}

			return true;
		}

		void SetArraySlot(int key, DynValue value)
		{
			DynValue old = m_Array[key - 1];
			bool wasLive = old != null && old.Type != DataType.Nil;

			if (value.IsNil())
			{
				if (wasLive)
				{
					m_Array[key - 1] = DynValue.Nil;

					if (key <= m_Length)
						m_Length = key - 1;

					if (Order == TableOrder.Insertion)
						AddPending(~key);
				}

				return;
			}

			if (Order == TableOrder.Insertion && old == null)
			{
				// a new key: (a) must remember where it goes in the order
				if (m_Count == 0 && key == m_Prefix + 1)
					m_Prefix = key;
				else
					AddEntry(null, key, s_ArraySlot);
			}

			m_Array[key - 1] = value;

			if (!wasLive)
			{
				if (key == m_Length + 1)
					ExtendLength();

				AfterInsert();
			}
		}

		void ExtendLength()
		{
			int n = m_Length;

			while (true)
			{
				DynValue v = RawGet(n + 1);

				if (v == null || v.IsNil())
					break;

				n++;
			}

			m_Length = n;
		}

		#endregion

		#region Set

		void SetInt(int key, DynValue value, DynValue keyValue = null)
		{
			if (key >= 1)
			{
				DynValue[] arr = m_Array;

				if (arr != null && key <= arr.Length)
				{
					SetArraySlot(key, value);
					return;
				}

				if (!value.IsNil() && TryGrowArrayFor(key))
				{
					SetArraySlot(key, value);
					return;
				}
			}

			int i = FindNumber(key, key);

			if (i >= 0)
			{
				UpdateEntry(i, value, key >= 1 ? key : 0);
				return;
			}

			if (value.IsNil())
				return;

			// keep the caller's key object when possible (it may be -0, which prints differently from 0)
			AddKey(keyValue == null ? DynValue.NewNumber(key) : keyValue.ReadOnly ? keyValue : DynValue.NewNumber(keyValue.Number), key, value);

			if (key >= 1 && key == m_Length + 1)
				ExtendLength();
		}

		void SetString(string s, int hash, DynValue keyValue, DynValue value)
		{
			int i = FindString(s, hash);

			if (i >= 0)
			{
				UpdateEntry(i, value, 0);
				return;
			}

			if (value.IsNil())
				return;

			AddKey((keyValue != null && keyValue.ReadOnly) ? keyValue : DynValue.NewString(s), hash, value);
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(string key, DynValue value)
		{
			if (key == null)
				throw ScriptRuntimeException.TableIndexIsNil();

			this.CheckScriptOwnership(value);
			SetString(key, HashString(key), null, value);
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(int key, DynValue value)
		{
			this.CheckScriptOwnership(value);
			SetInt(key, value);
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(DynValue key, DynValue value)
		{
			if (key.IsNilOrNan())
			{
				if (key.IsNil())
					throw ScriptRuntimeException.TableIndexIsNil();
				else
					throw ScriptRuntimeException.TableIndexIsNaN();
			}

			if (key.Type == DataType.String)
			{
				this.CheckScriptOwnership(value);
				SetString(key.String, key.GetHashCode(), key, value);
				return;
			}

			if (key.Type == DataType.Number)
			{
				double d = key.Number;
				int k = (int)d;

				this.CheckScriptOwnership(value);

				if (k == d)
				{
					SetInt(k, value, key);
					return;
				}

				int hash = HashNumber(d);
				int i = FindNumber(d, hash);

				if (i >= 0)
					UpdateEntry(i, value, 0);
				else if (!value.IsNil())
					AddKey(key.ReadOnly ? key : DynValue.NewNumber(d), hash, value);

				return;
			}

			this.CheckScriptOwnership(key);
			this.CheckScriptOwnership(value);

			int h = key.GetHashCode();
			int j = FindOther(key, h);

			if (j >= 0)
				UpdateEntry(j, value, 0);
			else if (!value.IsNil())
				AddKey(key, h, value);
		}

		/// <summary>
		/// Sets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(object key, DynValue value)
		{
			if (key == null)
				throw ScriptRuntimeException.TableIndexIsNil();

			if (key is string)
				Set((string)key, value);
			else if (key is int)
				Set((int)key, value);
			else
				Set(DynValue.FromObject(OwnerScript, key), value);
		}

		/// <summary>
		/// Sets the value associated with the specified keys.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <param name="key">The keys.</param>
		/// <param name="value">The value.</param>
		public void Set(object[] keys, DynValue value)
		{
			if (keys == null || keys.Length <= 0)
				throw ScriptRuntimeException.TableIndexIsNil();

			object key;
			ResolveMultipleKeys(keys, out key).Set(key, value);
		}

		#endregion

		#region Get

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(string key)
		{
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(int key)
		{
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(DynValue key)
		{
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// (expressed as a <see cref="System.Object"/>).
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(object key)
		{
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified keys (expressed as an
		/// array of <see cref="System.Object"/>).
		/// This will marshall CLR and MoonSharp objects in the best possible way.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <param name="keys">The keys to access the table and subtables</param>
		public DynValue Get(params object[] keys)
		{
			return RawGet(keys) ?? DynValue.Nil;
		}

		#endregion

		#region RawGet

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(string key)
		{
			int i = FindString(key, HashString(key));
			return (i >= 0) ? m_Entries[i].Value : null;
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(int key)
		{
			DynValue[] arr = m_Array;

			if (arr != null && (uint)(key - 1) < (uint)arr.Length)
				return arr[key - 1];

			int i = FindNumber(key, key);
			return (i >= 0) ? m_Entries[i].Value : null;
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(DynValue key)
		{
			if (key.Type == DataType.String)
			{
				int i = FindString(key.String, key.GetHashCode());
				return (i >= 0) ? m_Entries[i].Value : null;
			}

			if (key.Type == DataType.Number)
			{
				double d = key.Number;
				int k = (int)d;

				if (k == d)
					return RawGet(k);

				int i = FindNumber(d, HashNumber(d));
				return (i >= 0) ? m_Entries[i].Value : null;
			}

			int j = FindOther(key, key.GetHashCode());
			return (j >= 0) ? m_Entries[j].Value : null;
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(object key)
		{
			if (key == null)
				return null;

			if (key is string)
				return RawGet((string)key);

			if (key is int)
				return RawGet((int)key);

			return RawGet(DynValue.FromObject(OwnerScript, key));
		}

		/// <summary>
		/// Gets the value associated with the specified keys (expressed as an
		/// array of <see cref="System.Object"/>).
		/// This will marshall CLR and MoonSharp objects in the best possible way.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <param name="keys">The keys to access the table and subtables</param>
		public DynValue RawGet(params object[] keys)
		{
			if (keys == null || keys.Length <= 0)
				return null;

			object key;
			return ResolveMultipleKeys(keys, out key).RawGet(key);
		}

		#endregion

		#region Remove

		bool RemoveLive(DynValue current, Action clear)
		{
			if (current == null || current.IsNil())
				return false;

			clear();
			return true;
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(string key)
		{
			return RemoveLive(RawGet(key), () => SetString(key, HashString(key), null, DynValue.Nil));
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(int key)
		{
			return RemoveLive(RawGet(key), () => SetInt(key, DynValue.Nil));
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(DynValue key)
		{
			if (key.IsNilOrNan())
				return false;

			return RemoveLive(RawGet(key), () => Set(key, DynValue.Nil));
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(object key)
		{
			if (key is string)
				return Remove((string)key);

			if (key is int)
				return Remove((int)key);

			return Remove(DynValue.FromObject(OwnerScript, key));
		}

		/// <summary>
		/// Remove the value associated with the specified keys from the table.
		/// Multiple keys can be used to access subtables.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(params object[] keys)
		{
			if (keys == null || keys.Length <= 0)
				return false;

			object key;
			return ResolveMultipleKeys(keys, out key).Remove(key);
		}

		#endregion

		/// <summary>
		/// Collects the dead keys. This frees up memory but invalidates pending iterators.
		/// It's called automatically internally when the semantics of Lua tables allow, but can be forced
		/// externally if it's known that no iterators are pending.
		/// </summary>
		public void CollectDeadKeys()
		{
			if (m_Entries == null)
				return;

			if (Order == TableOrder.Unordered)
				Rehash(0);
			else
				Rebuild(0); // purges pending dead keys first
		}

		#region Iteration

		int HeadLength
		{
			get
			{
				if (Order == TableOrder.Insertion)
					return m_Prefix;

				return (m_Array == null) ? 0 : m_Array.Length;
			}
		}

		// Finds the first live position >= pos. Positions [0, head) are array slots, the rest hash entries.
		bool Advance(ref int pos, out TablePair pair)
		{
			int head = HeadLength;
			DynValue[] arr = m_Array;

			for (; pos < head; pos++)
			{
				DynValue v = arr[pos];

				if (v != null && v.Type != DataType.Nil)
				{
					pair = new TablePair(DynValue.NewNumber(pos + 1), v);
					pos++;
					return true;
				}
			}

			Entry[] entries = m_Entries;

			if (entries != null)
			{
				int limit = (Order == TableOrder.Unordered) ? entries.Length : m_Count;

				for (int i = pos - head; i < limit; i++)
				{
					DynValue v = entries[i].Value;

					if (v == null)
						continue;

					if (object.ReferenceEquals(v, s_ArraySlot))
					{
						int k = entries[i].Hash;
						DynValue av = arr[k - 1];

						if (av == null || av.Type == DataType.Nil)
							continue;

						pair = new TablePair(DynValue.NewNumber(k), av);
						pos = head + i + 1;
						return true;
					}

					pair = new TablePair(entries[i].Key, v);
					pos = head + i + 1;
					return true;
				}
			}

			pair = TablePair.Nil;
			pos = int.MaxValue;
			return false;
		}

		// The position of a key for next(), or -1 when the key is not in the table.
		int PositionOf(DynValue key)
		{
			int head = HeadLength;

			if (key.Type == DataType.Number && m_Array != null)
			{
				int idx = GetIntegralKey(key.Number);

				if (idx > 0 && idx <= m_Array.Length)
				{
					if (Order != TableOrder.Insertion || idx <= m_Prefix)
						return idx - 1;

					int p = FindPointer(idx);
					return (p >= 0) ? head + p : -1;
				}
			}

			int i = FindKey(key);
			return (i >= 0) ? head + i : -1;
		}

		/// <summary>
		/// Returns the next pair from a value
		/// </summary>
		public TablePair? NextKey(DynValue v)
		{
			int pos = 0;

			if (!v.IsNil())
			{
				pos = PositionOf(v);

				if (pos < 0)
					return null;

				pos++;
			}

			TablePair pair;
			Advance(ref pos, out pair);
			return pair;
		}

		IEnumerable<TablePair> EnumeratePairs()
		{
			int pos = 0;
			TablePair pair;

			while (Advance(ref pos, out pair))
				yield return pair;
		}

		#endregion

		/// <summary>
		/// Gets the length of the "array part".
		/// </summary>
		public int Length
		{
			get { return m_Length; }
		}

		internal void InitNextArrayKeys(DynValue val, bool lastpos)
		{
			if (val.Type == DataType.Tuple && lastpos)
			{
				foreach (DynValue v in val.Tuple)
					InitNextArrayKeys(v, true);
			}
			else
			{
				Set(++m_InitArray, val.ToScalar());
			}
		}

		/// <summary>
		/// Gets the meta-table associated with this instance.
		/// </summary>
		public Table MetaTable
		{
			get { return m_MetaTable; }
			set { this.CheckScriptOwnership(m_MetaTable); m_MetaTable = value; }
		}
		private Table m_MetaTable;

		/// <summary>
		/// Enumerates the key/value pairs.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<TablePair> Pairs
		{
			get
			{
				return EnumeratePairs();
			}
		}

		/// <summary>
		/// Enumerates the keys.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<DynValue> Keys
		{
			get
			{
				foreach (TablePair p in EnumeratePairs())
					yield return p.Key;
			}
		}

		/// <summary>
		/// Enumerates the values
		/// </summary>
		/// <returns></returns>
		public IEnumerable<DynValue> Values
		{
			get
			{
				foreach (TablePair p in EnumeratePairs())
					yield return p.Value;
			}
		}
	}
}
