using System;
using System.Runtime.CompilerServices;

namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// The value stack of a processor: arguments, the locals of every running script function and
	/// temporaries, as <see cref="LuaValue"/> structs. It starts small and doubles on demand up to a
	/// maximum. A ref into it is valid only until the next push, which may move the array.
	/// </summary>
	internal sealed class ValueStack
	{
		internal LuaValue[] Items;
		internal int Count;
		readonly int m_MaxCapacity;

		public ValueStack(int initialCapacity, int maxCapacity)
		{
			Items = new LuaValue[initialCapacity];
			m_MaxCapacity = maxCapacity;
		}

		public ref LuaValue this[int index]
		{
			get { return ref Items[index]; }
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Push(LuaValue item)
		{
			int head = Count;

			if (head == Items.Length)
				Grow(head + 1);

			Items[head] = item;
			Count = head + 1;
		}

		public void Push(DynValue item)
		{
			Push(item.ToLuaValue());
		}

		/// <summary>
		/// Adds <paramref name="size"/> slots holding <paramref name="value"/>.
		/// </summary>
		public void Expand(int size, LuaValue value)
		{
			int head = Count;

			if (size > Items.Length - head)
				Grow(head + size);

			LuaValue[] items = Items;

			for (int i = head; i < head + size; i++)
				items[i] = value;

			Count = head + size;
		}

		private void Grow(int requiredCapacity)
		{
			if (requiredCapacity > m_MaxCapacity)
				throw new ScriptRuntimeException("stack overflow");

			int newCapacity = Math.Max(requiredCapacity, Math.Min(Items.Length * 2, m_MaxCapacity));
			Array.Resize(ref Items, newCapacity);
		}

		/// <summary>
		/// The value <paramref name="idxofs"/> slots below the top (0 is the top).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref LuaValue Peek(int idxofs = 0)
		{
			return ref Items[Count - 1 - idxofs];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public LuaValue Pop()
		{
			int top = --Count;
			LuaValue v = Items[top];
			Items[top] = default(LuaValue);
			return v;
		}

		public void RemoveLast(int cnt = 1)
		{
			int oldTop = Count;
			int newTop = oldTop - cnt;

			// Cleared so the stack does not keep popped objects alive.
			for (int i = newTop; i < oldTop; i++)
				Items[i] = default(LuaValue);

			Count = newTop;
		}

		public void CropAtCount(int p)
		{
			if (Count > p)
				RemoveLast(Count - p);
		}

		public void ClearUsed()
		{
			Array.Clear(Items, 0, Count);
			Count = 0;
		}
	}
}
