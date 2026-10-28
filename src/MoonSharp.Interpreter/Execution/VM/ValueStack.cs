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

		// '...' of a running vararg function: its values stay where the caller pushed them, below the
		// function's locals, and the '...' local holds a Tuple pointing at them (start and count packed in
		// Number, exact in a double), so a call allocates nothing for them. Such a value is valid only while
		// that function's frame is on this stack: the VM resolves it before the frame goes (a call expands it
		// in place, return turns it into a DynValue, a store reads one of its values), and DynValue.FromLuaValue
		// copies it into a DynValue tuple.
		const double VARARGS_SCALE = 262144; // 2^18, more than the maximum stack size

		/// <summary>
		/// A Tuple holding the <paramref name="count"/> values from <paramref name="start"/>.
		/// </summary>
		public LuaValue VarArgs(int start, int count)
		{
			LuaValue v;
			v.Object = this;
			v.Number = start * VARARGS_SCALE + count;
			v.Type = DataType.Tuple;
			return v;
		}

		public static int VarArgsStart(ref LuaValue v)
		{
			return (int)(v.Number / VARARGS_SCALE);
		}

		public static int VarArgsCount(ref LuaValue v)
		{
			return (int)(v.Number % VARARGS_SCALE);
		}

		/// <summary>
		/// The value at <paramref name="index"/> of a '...' Tuple from this stack, void past its end.
		/// </summary>
		public LuaValue VarArgsItem(ref LuaValue varArgs, int index)
		{
			return index < VarArgsCount(ref varArgs) ? Items[VarArgsStart(ref varArgs) + index] : LuaValue.Void;
		}

		/// <summary>
		/// The values of a '...' Tuple from this stack as a DynValue, as the '...' local held them before:
		/// an empty tuple, the value itself, or a tuple.
		/// </summary>
		public DynValue VarArgsToDynValue(ref LuaValue varArgs)
		{
			int start = VarArgsStart(ref varArgs);
			int count = VarArgsCount(ref varArgs);

			if (count == 0)
				return DynValue.EmptyTuple;

			if (count == 1)
				return Items[start].ToDynValue();

			DynValue[] values = new DynValue[count];

			for (int i = 0; i < count; i++)
				values[i] = Items[start + i].ToDynValue();

			return DynValue.NewTuple(values);
		}
	}
}
