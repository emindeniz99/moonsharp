#if !USE_DYNAMIC_STACKS

using System;
using System.Collections.Generic;

namespace MoonSharp.Interpreter.DataStructs
{
	/// <summary>
	/// An array-backed stack that starts small and doubles its storage on demand, up to a fixed maximum.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	internal class FastStack<T> : IList<T>
	{
		// Items sit in a struct so stores skip the array covariance check: a T[] of a reference type
		// is checked on every store because shared generic code cannot know the array's real type.
		struct Slot { public T Value; }

		Slot[] m_Storage;
		int m_HeadIdx = 0;
		readonly int m_MaxCapacity;

		public FastStack(int maxCapacity)
			: this(maxCapacity, maxCapacity)
		{
		}

		public FastStack(int initialCapacity, int maxCapacity)
		{
			m_Storage = new Slot[initialCapacity];
			m_MaxCapacity = maxCapacity;
		}

		public T this[int index]
		{
			get { return m_Storage[index].Value; }
			set { m_Storage[index].Value = value; }
		}

		public T Push(T item)
		{
			Slot[] storage = m_Storage;
			int head = m_HeadIdx;

			if ((uint)head >= (uint)storage.Length)
				storage = Grow(head + 1);

			storage[head].Value = item;
			m_HeadIdx = head + 1;
			return item;
		}

		public void Expand(int size)
		{
			if (size > m_Storage.Length - m_HeadIdx)
				Grow(m_HeadIdx + size);

			m_HeadIdx += size;
		}

		private Slot[] Grow(int requiredCapacity)
		{
			if (requiredCapacity > m_MaxCapacity)
				throw new ScriptRuntimeException("stack overflow");

			int newCapacity = Math.Max(requiredCapacity, Math.Min(m_Storage.Length * 2, m_MaxCapacity));
			Array.Resize(ref m_Storage, newCapacity);
			return m_Storage;
		}

		// Clears the range [from, to), so a full stack can be cleared without indexing past its end.
		// A loop is cheaper than Array.Clear for the few slots a pop usually frees.
		private void Zero(int from, int to)
		{
			if (to - from <= 8)
			{
				for (int i = from; i < to; i++)
					m_Storage[i].Value = default(T);
			}
			else
			{
				Array.Clear(m_Storage, from, to - from);
			}
		}

		public T Peek(int idxofs = 0)
		{
			return m_Storage[m_HeadIdx - 1 - idxofs].Value;
		}

		public void Set(int idxofs, T item)
		{
			m_Storage[m_HeadIdx - 1 - idxofs].Value = item;
		}

		public void CropAtCount(int p)
		{
			if (Count > p)
				RemoveLast(Count - p);
		}

		public void RemoveLast( int cnt = 1)
		{
			if (cnt == 1)
			{
				--m_HeadIdx;
				m_Storage[m_HeadIdx].Value = default(T);
			}
			else
			{
				int oldhead = m_HeadIdx;
				m_HeadIdx -= cnt;
				Zero(m_HeadIdx, oldhead);
			}
		}

		public T Pop()
		{
			--m_HeadIdx;
			T retval = m_Storage[m_HeadIdx].Value;
			m_Storage[m_HeadIdx].Value = default(T);
			return retval;
		}

		public void Clear()
		{
			Array.Clear(m_Storage, 0, m_Storage.Length);
			m_HeadIdx = 0;
		}

		public void ClearUsed()
		{
			Array.Clear(m_Storage, 0, m_HeadIdx);
			m_HeadIdx = 0;
		}

		public int Count
		{
			get { return m_HeadIdx; }
		}


		#region IList<T> Impl.

		int IList<T>.IndexOf(T item)
		{
			throw new NotImplementedException();
		}

		void IList<T>.Insert(int index, T item)
		{
			throw new NotImplementedException();
		}

		void IList<T>.RemoveAt(int index)
		{
			throw new NotImplementedException();
		}

		T IList<T>.this[int index]
		{
			get
			{
				return this[index];
			}
			set
			{
				this[index] = value;
			}
		}

		void ICollection<T>.Add(T item)
		{
			Push(item);
		}

		void ICollection<T>.Clear()
		{
			Clear();
		}

		bool ICollection<T>.Contains(T item)
		{
			throw new NotImplementedException();
		}

		void ICollection<T>.CopyTo(T[] array, int arrayIndex)
		{
			throw new NotImplementedException();
		}

		int ICollection<T>.Count
		{
			get { return this.Count; }
		}

		bool ICollection<T>.IsReadOnly
		{
			get { return false; }
		}

		bool ICollection<T>.Remove(T item)
		{
			throw new NotImplementedException();
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator()
		{
			throw new NotImplementedException();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			throw new NotImplementedException();
		}

		#endregion

	}
}

#endif