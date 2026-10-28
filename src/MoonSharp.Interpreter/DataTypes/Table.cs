using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter.DataStructs;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// A class representing a Lua table.
	/// </summary>
	public class Table : RefIdObject, IScriptPrivateResource
	{
		readonly LinkedList<TablePair> m_Values;
		readonly LinkedListIndex<DynValue, TablePair> m_ValueMap;
		readonly LinkedListIndex<string, TablePair> m_StringMap;
		readonly LinkedListIndex<int, TablePair> m_ArrayMap;
		readonly Script m_Owner;

		// Entries visited by length scans and dead-key sweeps. Only the work-bound tests read it (by reflection).
		[System.ThreadStatic] static long s_ScanSteps;

		int m_InitArray = 0;

		// Length cache: keys 1..m_CachedLength all hold non-nil values. When m_LengthKnown is set, key
		// m_CachedLength + 1 does not, so m_CachedLength is the first border (the value of #t).
		int m_CachedLength = 0;
		bool m_LengthKnown = true;

		// Set when a nil is stored; the next store of a value into a new or nil key then runs CollectDeadKeys.
		bool m_ContainsNilEntries = false;

		// Entries holding nil that CollectDeadKeys can remove (all but integer keys below 1 set through
		// Set(int), which Remove(DynValue) cannot find). When zero, CollectDeadKeys has nothing to walk for.
		int m_SweepableNilEntries = 0;

		/// <summary>
		/// Initializes a new instance of the <see cref="Table"/> class.
		/// </summary>
		/// <param name="owner">The owner script.</param>
		public Table(Script owner)
		{
			m_Values = new LinkedList<TablePair>();
			m_StringMap = new LinkedListIndex<string, TablePair>(m_Values);
			m_ArrayMap = new LinkedListIndex<int, TablePair>(m_Values);
			m_ValueMap = new LinkedListIndex<DynValue, TablePair>(m_Values);
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
			m_Values.Clear();
			m_StringMap.Clear();
			m_ArrayMap.Clear();
			m_ValueMap.Clear();
			m_CachedLength = 0;
			m_LengthKnown = true;
			m_SweepableNilEntries = 0;
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
			int key = Length + 1;
			LinkedListNode<TablePair> node = m_ArrayMap.Find(key);
			PerformTableSet(m_ArrayMap, key, node, node == null ? DynValue.NewNumber(key) : null, value, true, key);
		}

		#region Set

		/// <summary>
		/// Stores a value. <paramref name="node"/> is the existing entry for the key (or null); when it exists
		/// its key DynValue is kept, so callers create <paramref name="keyDynValue"/> only for a new key.
		/// <paramref name="intKey"/> is the key when <paramref name="isArrayMap"/> is set.
		/// </summary>
		private void PerformTableSet<T>(LinkedListIndex<T, TablePair> listIndex, T key, LinkedListNode<TablePair> node, DynValue keyDynValue, DynValue value, bool isArrayMap, int intKey)
		{
			DynValue prev = null;

			if (node != null)
			{
				prev = node.Value.Value;
				node.Value = new TablePair(node.Value.Key, value);
			}
			else
			{
				listIndex.Add(key, new TablePair(keyDynValue, value));
			}

			bool wasNil = prev == null || prev.IsNil();
			bool isNil = value.IsNil();

			if (!isArrayMap || intKey > 0)
			{
				if (isNil)
				{
					if (prev == null || !wasNil)
						m_SweepableNilEntries++;
				}
				else if (prev != null && wasNil)
				{
					m_SweepableNilEntries--;
				}
			}

			if (isArrayMap && intKey > 0)
			{
				if (wasNil && !isNil)
				{
					// Filling the slot right after the known prefix extends it; Length looks further on demand.
					if (intKey == m_CachedLength + 1)
					{
						m_CachedLength = intKey;
						m_LengthKnown = false;
					}
				}
				else if (!wasNil && isNil && intKey <= m_CachedLength)
				{
					m_CachedLength = intKey - 1;
					m_LengthKnown = true;
				}
			}

			// Storing a value into a new or nil key after a nil was stored runs the dead-key collection.
			if (m_ContainsNilEntries && !isNil && wasNil)
				CollectDeadKeys();
			else if (isNil)
				m_ContainsNilEntries = true;
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
			LinkedListNode<TablePair> node = m_StringMap.Find(key);
			PerformTableSet(m_StringMap, key, node, node == null ? DynValue.NewString(key) : null, value, false, -1);
		}

		/// <summary>
		/// Sets the value associated to the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void Set(int key, DynValue value)
		{
			this.CheckScriptOwnership(value);
			LinkedListNode<TablePair> node = m_ArrayMap.Find(key);
			PerformTableSet(m_ArrayMap, key, node, node == null ? DynValue.NewNumber(key) : null, value, true, key);
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
				Set(key.String, value);
				return;
			}

			if (key.Type == DataType.Number)
			{
				int idx = GetIntegralKey(key.Number);

				if (idx > 0)
				{
					Set(idx, value);
					return;
				}
			}

			this.CheckScriptOwnership(key);
			this.CheckScriptOwnership(value);

			PerformTableSet(m_ValueMap, key, m_ValueMap.Find(key), key, value, false, -1);
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

		private static DynValue RawGetValue(LinkedListNode<TablePair> linkedListNode)
		{
			return (linkedListNode != null) ? linkedListNode.Value.Value : null;
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(string key)
		{
			return RawGetValue(m_StringMap.Find(key));
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(int key)
		{
			return RawGetValue(m_ArrayMap.Find(key));
		}

		/// <summary>
		/// Gets the value associated with the specified key,
		/// without bringing to Nil the non-existant values.
		/// </summary>
		/// <param name="key">The key.</param>
		public DynValue RawGet(DynValue key)
		{
			if (key.Type == DataType.String)
				return RawGet(key.String);

			if (key.Type == DataType.Number)
			{
				int idx = GetIntegralKey(key.Number);
				if (idx > 0)
					return RawGet(idx);
			}

			return RawGetValue(m_ValueMap.Find(key));
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

		private bool PerformTableRemove<T>(LinkedListIndex<T, TablePair> listIndex, T key, bool isArrayMap, int intKey)
		{
			LinkedListNode<TablePair> node = listIndex.Find(key);

			if (node == null)
				return false;

			bool wasNil = node.Value.Value.IsNil();
			listIndex.Remove(key);

			if (wasNil)
			{
				if (!isArrayMap || intKey > 0)
					m_SweepableNilEntries--;
			}
			else if (isArrayMap && intKey > 0 && intKey <= m_CachedLength)
			{
				m_CachedLength = intKey - 1;
				m_LengthKnown = true;
			}

			return true;
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(string key)
		{
			return PerformTableRemove(m_StringMap, key, false, 0);
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(int key)
		{
			return PerformTableRemove(m_ArrayMap, key, true, key);
		}

		/// <summary>
		/// Remove the value associated with the specified key from the table.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns><c>true</c> if values was successfully removed; otherwise, <c>false</c>.</returns>
		public bool Remove(DynValue key)
		{
			if (key.Type == DataType.String)
				return Remove(key.String);

			if (key.Type == DataType.Number)
			{
				int idx = GetIntegralKey(key.Number);
				if (idx > 0)
					return Remove(idx);
			}

			return PerformTableRemove(m_ValueMap, key, false, 0);
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
			// Removes every removable entry that holds nil. With none left the walk stops; with none at all
			// it is skipped.
			if (m_SweepableNilEntries > 0)
			{
				long steps = 0;
				LinkedListNode<TablePair> node = m_Values.First;

				while (node != null && m_SweepableNilEntries > 0)
				{
					steps++;
					// read Next first: LinkedList.Remove clears it
					LinkedListNode<TablePair> next = node.Next;

					if (node.Value.Value.IsNil())
						Remove(node.Value.Key);

					node = next;
				}

				s_ScanSteps += steps;
			}

			m_ContainsNilEntries = false;
		}


		/// <summary>
		/// Returns the next pair from a value
		/// </summary>
		public TablePair? NextKey(DynValue v)
		{
			if (v.IsNil())
			{
				LinkedListNode<TablePair> node = m_Values.First;

				if (node == null)
					return TablePair.Nil;
				else
				{
					if (node.Value.Value.IsNil())
						return NextKey(node.Value.Key);
					else
						return node.Value;
				}
			}

			if (v.Type == DataType.String)
			{
				return GetNextOf(m_StringMap.Find(v.String));
			}

			if (v.Type == DataType.Number)
			{
				int idx = GetIntegralKey(v.Number);

				if (idx > 0)
				{
					return GetNextOf(m_ArrayMap.Find(idx));
				}
			}

			return GetNextOf(m_ValueMap.Find(v));
		}

		private TablePair? GetNextOf(LinkedListNode<TablePair> linkedListNode)
		{
			while (true)
			{
				if (linkedListNode == null)
					return null;

				if (linkedListNode.Next == null)
					return TablePair.Nil;

				linkedListNode = linkedListNode.Next;

				if (!linkedListNode.Value.Value.IsNil())
					return linkedListNode.Value;
			}
		}


		/// <summary>
		/// Gets the length of the "array part".
		/// </summary>
		public int Length
		{
			get
			{
				if (!m_LengthKnown)
				{
					// Keys 1..m_CachedLength are known to hold values: continue from there, not from 1.
					int n = m_CachedLength;
					long steps = 0;

					while (true)
					{
						steps++;
						LinkedListNode<TablePair> next = m_ArrayMap.Find(n + 1);

						if (next == null || next.Value.Value.IsNil())
							break;

						n++;
					}

					m_CachedLength = n;
					m_LengthKnown = true;
					s_ScanSteps += steps;
				}

				return m_CachedLength;
			}
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
				return m_Values.Where(n => !n.Value.IsNil());
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
				return m_Values.Where(n => !n.Value.IsNil()).Select(n => n.Key);
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
				return m_Values.Where(n => !n.Value.IsNil()).Select(n => n.Value);
			}
		}
	}
}
