namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		/// <summary>
		/// Replaces the last of the top <paramref name="argsCount"/> values, a Tuple or Void, with the values it
		/// holds, and returns the new argument count: a call's results or '...' (copied from where they are,
		/// no array) pass all their values, a call that returned nothing passes none.
		/// </summary>
		private int ExpandLastArgument(int argsCount)
		{
			LuaValue last = m_ValueStack.Pop();
			argsCount--;

			if (last.Type == DataType.Void)
				return argsCount;

			ValueStack varArgs = last.Object as ValueStack;

			if (varArgs != null)
			{
				int first = ValueStack.VarArgsStart(last.Number);
				int count = ValueStack.VarArgsCount(last.Number);

				// Items is read again on every push, which may move the array.
				for (int k = 0; k < count; k++)
					m_ValueStack.Push(varArgs.Items[first + k]);

				return argsCount + count;
			}

			DynValue tuple = last.Object as DynValue;

			if (tuple == null)
			{
				// The generic for's iterator state is never an argument; leave anything else as it was.
				m_ValueStack.Push(last);
				return argsCount + 1;
			}

			return argsCount + PushHostArguments(tuple.Tuple);
		}

		/// <summary>
		/// Turns every Tuple among the top <paramref name="argsCount"/> values into its first value, before a
		/// tail call removes the frame whose '...' such a value may point into.
		/// </summary>
		private void ScalarizeArguments(int argsCount)
		{
			LuaValue[] items = m_ValueStack.Items;

			for (int k = m_ValueStack.Count - argsCount; k < m_ValueStack.Count; k++)
			{
				if (items[k].Type == DataType.Tuple)
					items[k] = items[k].ToScalar();
			}
		}
	}
}
