using System;
using System.Runtime.CompilerServices;

namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// The call frames of a processor: a growable array of <see cref="CallStackItem"/> structs, read and
	/// written through ref. A ref into it is valid only until the next push, which may move the array:
	/// never hold one across a push or a call that can run script code.
	/// </summary>
	internal sealed class CallStack
	{
		CallStackItem[] m_Items;
		int m_Count;
		readonly int m_MaxCapacity;

		public CallStack(int initialCapacity, int maxCapacity)
		{
			m_Items = new CallStackItem[initialCapacity];
			m_MaxCapacity = maxCapacity;
		}

		public int Count
		{
			get { return m_Count; }
		}

		/// <summary>
		/// Adds a cleared frame on top and returns it.
		/// </summary>
		public ref CallStackItem Push()
		{
			if (m_Count == m_Items.Length)
				Grow();

			return ref m_Items[m_Count++];
		}

		private void Grow()
		{
			if (m_Count >= m_MaxCapacity)
				throw new ScriptRuntimeException("stack overflow");

			Array.Resize(ref m_Items, Math.Max(m_Count + 1, Math.Min(m_Items.Length * 2, m_MaxCapacity)));
		}

		/// <summary>
		/// The frame idx levels below the top (0 is the top).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref CallStackItem Peek(int idx = 0)
		{
			return ref m_Items[m_Count - 1 - idx];
		}

		/// <summary>
		/// Removes the top frame and returns a copy of it.
		/// </summary>
		public CallStackItem Pop()
		{
			int top = --m_Count;
			CallStackItem item = m_Items[top];
			m_Items[top] = default(CallStackItem);
			return item;
		}

		/// <summary>
		/// Removes the top frame.
		/// </summary>
		public void RemoveLast()
		{
			m_Items[--m_Count] = default(CallStackItem);
		}

		public void CropAtCount(int count)
		{
			while (m_Count > count)
				m_Items[--m_Count] = default(CallStackItem);
		}

		public void ClearUsed()
		{
			Array.Clear(m_Items, 0, m_Count);
			m_Count = 0;
		}
	}
}
