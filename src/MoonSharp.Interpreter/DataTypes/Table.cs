using System;
using System.Collections.Generic;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// A class representing a Lua table.
	/// </summary>
	/// <remarks>
	/// Storage: an array part for integer keys 1..capacity and one hash part for every other key.
	/// <list type="bullet">
	/// <item>A key goes to the array part when it is at most max(2 * capacity, 4) and at most 4096 past the
	/// capacity, and when more than half of the grown array would hold values; on growth, hash keys in the new
	/// range move into the array.</item>
	/// <item>The hash part is a compact dictionary: entries are appended in insertion order and chained from
	/// bucket heads.</item>
	/// <item><c>next</c>, <c>pairs</c> and <see cref="Pairs"/> return keys in insertion order across both parts.
	/// Keys 1..m_Prefix, appended in order while the hash part was empty, come first; every other array key
	/// has a pointer entry in the hash part that marks its place.</item>
	/// <item>Assigning nil keeps the key, dead, so <c>next</c> keeps working while a loop clears fields. Dead
	/// keys are forgotten when the next new key is stored (or by <see cref="CollectDeadKeys"/>), so a key that
	/// gets a value again after that goes to the end of the order.</item>
	/// <item><see cref="Length"/> is the first border: the smallest n with t[n + 1] == nil, kept up to date on
	/// every store.</item>
	/// </list>
	/// </remarks>
	public class Table : RefIdObject, IScriptPrivateResource
	{
		struct Entry
		{
			// null in a pointer entry and in a removed entry
			public DynValue Key;
			// null: dead key (or removed entry); s_ArraySlot: pointer entry for array key Hash
			public DynValue Value;
			public int Hash;
			// next entry in the same bucket chain, -1 at the end
			public int Next;
		}

		// Marks a pointer entry: the place in the order of an array key, whose value lives in m_Array.
		static readonly DynValue s_ArraySlot = DynValue.NewString("<array slot>");

		const int MinCapacity = 4;
		const int MaxArrayGap = 4096;

		// Entries visited by length scans, dead-key collection and array growth checks. Only the work-bound
		// tests read it (by reflection).
		[ThreadStatic] static long s_ScanSteps;

		// m_Array[i] holds key i + 1: null when absent (and not placed in the order), a nil value when dead.
		DynValue[] m_Array;
		// Keys 1..m_Prefix were appended in order while the hash part was empty: they come first in the order
		// without pointer entries.
		int m_Prefix;
		Entry[] m_Entries;
		// 1-based index of the first entry of each bucket chain, 0 = empty
		int[] m_Buckets;
		// entries used, dead and removed ones included
		int m_Count;
		int m_Shift;
		// the first border: the smallest n >= 0 with t[n + 1] == nil
		int m_Length;
		// Keys that died since the last insert, collected by the next one: an entry index (>= 0), or ~key
		// for an array key.
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
			m_Prefix = 0;
			m_Entries = null;
			m_Buckets = null;
			m_Count = 0;
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
			//Contract.Ensures(Contract.Result<Table>() != null);
			//Contract.Requires(keys != null);

			Table t = this;
			key = (keys.Length > 0) ? keys[0] : null;

			for (int i = 1; i < keys.Length; ++i)
			{
				DynValue vt = t.RawGet(key);

				if (vt == null)
					throw new ScriptRuntimeException("Key '{0}' did not point to anything", key);

				if (vt.Type != DataType.Table)
					throw new ScriptRuntimeException("Key '{0}' did not point to a table", key);

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
			SetInt(m_Length + 1, value, null);
		}

		#region Hash part

		static int HashString(string s)
		{
			// the same value as DynValue.GetHashCode() for a string, which caches it
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

		int FindString(string s, int hash)
		{
			Entry[] entries = m_Entries;

			if (entries == null)
				return -1;

			for (int i = m_Buckets[Bucket(hash)] - 1; i >= 0; i = entries[i].Next)
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

			for (int i = m_Buckets[Bucket(hash)] - 1; i >= 0; i = entries[i].Next)
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

			for (int i = m_Buckets[Bucket(hash)] - 1; i >= 0; i = entries[i].Next)
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

			for (int i = m_Buckets[Bucket(arrayKey)] - 1; i >= 0; i = entries[i].Next)
			{
				if (entries[i].Hash == arrayKey && object.ReferenceEquals(entries[i].Value, s_ArraySlot))
					return i;
			}

			return -1;
		}

		// The entry of a key that is not an array key in range, or -1.
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

		void Allocate(int capacity)
		{
			int log2 = 0;
			while ((1 << log2) < capacity) log2++;

			m_Entries = new Entry[capacity];
			m_Buckets = new int[capacity];
			m_Shift = 32 - log2;
			m_Count = 0;
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

			return true;
		}

		void AppendEntry(DynValue key, int hash, DynValue value)
		{
			int i = m_Count++;
			int b = Bucket(hash);
			m_Entries[i].Key = key;
			m_Entries[i].Value = value;
			m_Entries[i].Hash = hash;
			m_Entries[i].Next = m_Buckets[b] - 1;
			m_Buckets[b] = i + 1;
		}

		// Appends a key to the order. Callers run AfterInsert once the value is stored.
		void AddEntry(DynValue key, int hash, DynValue value)
		{
			if (m_Entries == null)
				Allocate(MinCapacity);
			else if (m_Count == m_Entries.Length)
				Rebuild(1);

			AppendEntry(key, hash, value);
		}

		// Drops dead and removed entries, keeping the order; doubles the capacity when more than half of it
		// would be live.
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
					AppendEntry(old[i].Key, old[i].Hash, old[i].Value);
				else if (object.ReferenceEquals(old[i].Value, s_ArraySlot))
					m_Array[old[i].Hash - 1] = null;
			}
		}

		// Takes an entry out of its bucket chain; it stays in the entry list as a hole until the next Rebuild.
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

			m_Entries[i].Key = null;
			m_Entries[i].Value = null;
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

		#region Dead keys

		void AddPending(int code)
		{
			if (m_Pending == null)
				m_Pending = new List<int>();

			m_Pending.Add(code);
		}

		// A key got a value it did not have: forget the keys that died since the last insert.
		void AfterInsert()
		{
			if (m_Pending != null && m_Pending.Count > 0)
				Purge();
		}

		// Forgets the keys that are still dead, so a later store of the same key goes to the end of the order.
		void Purge()
		{
			List<int> pending = m_Pending;
			int explodeAt = int.MaxValue;
			s_ScanSteps += pending.Count;

			for (int n = 0; n < pending.Count; n++)
			{
				int code = pending[n];

				if (code >= 0)
				{
					DynValue v = m_Entries[code].Value;

					if (v == null)
					{
						if (m_Entries[code].Key != null)
							Unlink(code);
					}
					else if (object.ReferenceEquals(v, s_ArraySlot))
					{
						// a dead hash key that moved into the array part
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
				ExplodePrefix(explodeAt, false);
		}

		// Key k leaves the prefix, with every key after it: k itself is forgotten, the other live keys (and,
		// with keepDead, the dead ones) become pointer entries placed before every other entry. Entry indices
		// move by the number of new entries; pending entry indices follow.
		void ExplodePrefix(int k, bool keepDead)
		{
			int oldPrefix = m_Prefix;
			int moved = 0;
			s_ScanSteps += oldPrefix - k + 1;

			m_Array[k - 1] = null;

			for (int j = k + 1; j <= oldPrefix; j++)
			{
				DynValue a = m_Array[j - 1];

				if (a.Type != DataType.Nil || keepDead)
					moved++;
				else
					m_Array[j - 1] = null;
			}

			m_Prefix = k - 1;

			if (moved == 0)
				return;

			Entry[] old = m_Entries;
			int oldCount = m_Count;
			int capacity = MinCapacity;

			while (capacity < 2 * (moved + oldCount))
				capacity *= 2;

			Allocate(capacity);

			for (int j = k + 1; j <= oldPrefix; j++)
				if (m_Array[j - 1] != null)
					AppendEntry(null, j, s_ArraySlot);

			for (int i = 0; i < oldCount; i++)
			{
				if (old[i].Key != null || old[i].Value != null)
				{
					AppendEntry(old[i].Key, old[i].Hash, old[i].Value);
				}
				else
				{
					// a removed entry: keep it as a hole so the indices stay in step
					m_Count++;
				}
			}

			if (m_Pending != null)
				for (int n = 0; n < m_Pending.Count; n++)
					if (m_Pending[n] >= 0)
						m_Pending[n] += moved;
		}

		/// <summary>
		/// Collects the dead keys. This frees up memory but invalidates pending iterators.
		/// It's called automatically internally when the semantics of Lua tables allow, but can be forced
		/// externally if it's known that no iterators are pending.
		/// </summary>
		public void CollectDeadKeys()
		{
			if (m_Pending != null && m_Pending.Count > 0)
				Purge();

			if (m_Entries != null)
				Rebuild(0);
		}

		#endregion

		#region Array part

		bool TryGrowArrayFor(int key)
		{
			int len = (m_Array == null) ? 0 : m_Array.Length;
			int newLen = Math.Max(len * 2, MinCapacity);

			if (key > newLen || key - len > MaxArrayGap)
				return false;

			// Lua's rule: grow only when more than half of the new size would hold values. A queue over ever
			// growing integer keys would otherwise grow the array without bound while few slots are live.
			// A full sequence (#t covers the array) needs no count; after a refusal the next len checks are
			// skipped, so the count stays amortised O(1) per insert.
			if (len > 0 && m_Length < len)
			{
				if (m_GrowSkip > 0)
				{
					m_GrowSkip--;
					return false;
				}

				int live = 0;
				s_ScanSteps += len;

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

			// hash keys in (len, newLen] move into the array; their entries keep their place in the order
			for (int i = 0; i < m_Count; i++)
			{
				DynValue k = m_Entries[i].Key;
				int h = m_Entries[i].Hash;

				if (k == null || k.Type != DataType.Number || h <= len || h > newLen || k.Number != h)
					continue;

				grown[h - 1] = m_Entries[i].Value ?? DynValue.Nil;
				m_Entries[i].Key = null;
				m_Entries[i].Value = s_ArraySlot;
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

					AddPending(~key);
				}

				return;
			}

			if (old == null)
			{
				// a new key: place it in the order
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

		// Key m_Length + 1 got a value: move the border past the run that starts there.
		void ExtendLength()
		{
			int n = m_Length;
			long steps = 0;

			while (true)
			{
				steps++;
				DynValue v = RawGet(n + 1);

				if (v == null || v.IsNil())
					break;

				n++;
			}

			m_Length = n;
			s_ScanSteps += steps;
		}

		#endregion

		#region Set

		// keyValue: the caller's key object, kept for a new key when it is read-only (it may be -0, which
		// prints differently from 0); null to create one.
		void SetInt(int key, DynValue value, DynValue keyValue)
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
				UpdateEntry(i, value, key);
				return;
			}

			if (value.IsNil())
				return;

			AddEntry(keyValue == null ? DynValue.NewNumber(key) : keyValue.ReadOnly ? keyValue : DynValue.NewNumber(keyValue.Number), key, value);

			if (key >= 1 && key == m_Length + 1)
				ExtendLength();

			AfterInsert();
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

			AddEntry((keyValue != null && keyValue.ReadOnly) ? keyValue : DynValue.NewString(s), hash, value);
			AfterInsert();
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value; null is the same as nil.</param>
		public void Set(string key, DynValue value)
		{
			if (key == null)
				throw ScriptRuntimeException.TableIndexIsNil();

			value = value ?? DynValue.Nil;
			this.CheckScriptOwnership(value);
			SetString(key, HashString(key), null, value);
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value; null is the same as nil.</param>
		public void Set(int key, DynValue value)
		{
			value = value ?? DynValue.Nil;
			this.CheckScriptOwnership(value);
			SetInt(key, value, null);
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value; null is the same as nil.</param>
		public void Set(DynValue key, DynValue value)
		{
			if (key.IsNilOrNan())
			{
				if (key.IsNil())
					throw ScriptRuntimeException.TableIndexIsNil();
				else
					throw ScriptRuntimeException.TableIndexIsNaN();
			}

			value = value ?? DynValue.Nil;

			if (key.Type == DataType.String)
			{
				this.CheckScriptOwnership(value);
				SetString(key.String, key.GetHashCode(), key, value);
				return;
			}

			if (key.Type == DataType.Number)
			{
				this.CheckScriptOwnership(value);
				double d = key.Number;
				int k = (int)d;

				if (k == d)
				{
					SetInt(k, value, key);
					return;
				}

				int hash = HashNumber(d);
				int i = FindNumber(d, hash);

				if (i >= 0)
				{
					UpdateEntry(i, value, 0);
				}
				else if (!value.IsNil())
				{
					AddEntry(key.ReadOnly ? key : DynValue.NewNumber(d), hash, value);
					AfterInsert();
				}

				return;
			}

			this.CheckScriptOwnership(key);
			this.CheckScriptOwnership(value);

			int h = key.GetHashCode();
			int j = FindOther(key, h);

			if (j >= 0)
			{
				UpdateEntry(j, value, 0);
			}
			else if (!value.IsNil())
			{
				AddEntry(key, h, value);
				AfterInsert();
			}
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
			//Contract.Ensures(Contract.Result<DynValue>() != null);
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(int key)
		{
			//Contract.Ensures(Contract.Result<DynValue>() != null);
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(DynValue key)
		{
			//Contract.Ensures(Contract.Result<DynValue>() != null);
			return RawGet(key) ?? DynValue.Nil;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// (expressed as a <see cref="System.Object"/>).
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue Get(object key)
		{
			//Contract.Ensures(Contract.Result<DynValue>() != null);
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
			//Contract.Ensures(Contract.Result<DynValue>() != null);
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

		// Removes a key at once, dead or not, so it goes to the end of the order if it is set again; the
		// other dead keys stay until the next insert. True when the key had a value.
		bool RemoveEntry(int i, int intKey)
		{
			if (i < 0)
				return false;

			bool wasLive = m_Entries[i].Value != null;
			Unlink(i);

			if (wasLive && intKey > 0 && intKey <= m_Length)
				m_Length = intKey - 1;

			return wasLive;
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(string key)
		{
			return RemoveEntry(FindString(key, HashString(key)), 0);
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(int key)
		{
			DynValue[] arr = m_Array;

			if (arr == null || (uint)(key - 1) >= (uint)arr.Length)
				return RemoveEntry(FindNumber(key, key), key);

			DynValue a = arr[key - 1];

			if (a == null)
				return false;

			bool wasLive = a.Type != DataType.Nil;

			if (key <= m_Prefix)
			{
				ExplodePrefix(key, true);
			}
			else
			{
				Unlink(FindPointer(key));
				arr[key - 1] = null;
			}

			if (wasLive && key <= m_Length)
				m_Length = key - 1;

			return wasLive;
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(DynValue key)
		{
			if (key.Type == DataType.Number)
			{
				double d = key.Number;
				int k = (int)d;

				if (k == d)
					return Remove(k);
			}

			if (key.Type == DataType.String)
				return Remove(key.String);

			return RemoveEntry(FindKey(key), 0);
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

		#region Iteration

		// Finds the first live key at a position >= pos. Positions [0, m_Prefix) are the prefix keys, the
		// rest are entries.
		bool Advance(ref int pos, out TablePair pair)
		{
			int head = m_Prefix;
			DynValue[] arr = m_Array;

			for (; pos < head; pos++)
			{
				DynValue v = arr[pos];

				if (v.Type != DataType.Nil)
				{
					pair = new TablePair(DynValue.NewNumber(pos + 1), v);
					pos++;
					return true;
				}
			}

			Entry[] entries = m_Entries;

			for (int i = pos - head; i < m_Count; i++)
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
				}
				else
				{
					pair = new TablePair(entries[i].Key, v);
				}

				pos = head + i + 1;
				return true;
			}

			pair = TablePair.Nil;
			pos = int.MaxValue;
			return false;
		}

		// The position of a key for next(), or -1 when the key is not in the table.
		int PositionOf(DynValue key)
		{
			if (key.Type == DataType.Number && m_Array != null)
			{
				int idx = GetIntegralKey(key.Number);

				if (idx > 0 && idx <= m_Array.Length)
				{
					if (idx <= m_Prefix)
						return idx - 1;

					int p = FindPointer(idx);
					return (p >= 0) ? m_Prefix + p : -1;
				}
			}

			int i = FindKey(key);
			return (i >= 0) ? m_Prefix + i : -1;
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
