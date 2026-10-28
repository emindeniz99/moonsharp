using MoonSharp.Interpreter.DataStructs;

namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// A variable captured by a closure, as in Lua (lfunc.c). While the function that declared the local
	/// runs, the upvalue is open: it points at the local's slot in that processor's value stack, so the
	/// function and every closure that captured the local see one variable. When the local goes out of
	/// scope (block exit, return, error unwinding) the processor closes the upvalue, which copies the
	/// value into the upvalue itself. Closures created in a loop body capture a new upvalue in every
	/// iteration, because the block exit at the end of each iteration closes the previous one.
	/// The idea follows WattleScript's Lua-style upvalues on MoonSharp's own code:
	/// https://github.com/WattleScript/wattlescript/commit/1daaff1215ffaf45e90b7d5d84ebbe7b20cb523d
	/// </summary>
	internal sealed class Upvalue
	{
		static int s_IdCounter = 0;

		FastStack<DynValue> m_Stack;
		int m_Index;
		DynValue m_Value;
		int m_Id;

		/// <summary>
		/// The next open upvalue of the same processor, at a lower stack index.
		/// </summary>
		internal Upvalue NextOpen;

		private Upvalue()
		{
		}

		/// <summary>
		/// An open upvalue for the stack slot at <paramref name="index"/>.
		/// </summary>
		internal Upvalue(FastStack<DynValue> stack, int index)
		{
			m_Stack = stack;
			m_Index = index;
		}

		/// <summary>
		/// A closed upvalue that holds <paramref name="value"/>.
		/// </summary>
		internal static Upvalue CreateClosed(DynValue value)
		{
			return new Upvalue() { m_Value = value };
		}

		/// <summary>
		/// The stack index of an open upvalue.
		/// </summary>
		internal int Index
		{
			get { return m_Index; }
		}

		internal DynValue Get()
		{
			return m_Stack != null ? m_Stack[m_Index] : m_Value;
		}

		internal void Set(DynValue value)
		{
			if (m_Stack != null)
				m_Stack[m_Index] = value;
			else
				m_Value = value;
		}

		internal void Close()
		{
			m_Value = m_Stack[m_Index];
			m_Stack = null;
		}

		/// <summary>
		/// A number that identifies this upvalue (debug.upvalueid), assigned on first use.
		/// </summary>
		internal int Id
		{
			get
			{
				if (m_Id == 0)
					m_Id = ++s_IdCounter;

				return m_Id;
			}
		}
	}
}
