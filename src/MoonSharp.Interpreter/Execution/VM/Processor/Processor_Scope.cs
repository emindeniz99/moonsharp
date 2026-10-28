using System;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		private void ClearBlockData(Instruction I, int basePointer)
		{
			int from = I.NumVal;
			int to = I.NumVal2;

			if (to >= 0 && from >= 0 && to >= from)
			{

				// The block's locals go out of scope: closures that captured them keep their values. Locals
				// of enclosing blocks sit in lower slots, so only this block's and nested blocks' close.
				if (m_OpenUpvalues != null)
					CloseUpvalues(basePointer + from);

				for (int k = from; k <= to; k++)
					m_ValueStack[basePointer + k] = LuaValue.Undeclared;
			}
		}


		public DynValue GetGenericSymbol(SymbolRef symref, int stackFrameIndex = -1)
		{
			if (m_CoroutinesStack != null && m_CoroutinesStack.Count > 0)
			{
				return m_CoroutinesStack[m_CoroutinesStack.Count - 1].GetGenericSymbol(symref, stackFrameIndex);
			}

			switch (symref.i_Type)
			{
				case  SymbolRefType.DefaultEnv:
					return DynValue.NewTable(this.GetScript().Globals);
				case SymbolRefType.Global:
					return GetGlobalSymbol(GetGenericSymbol(symref.i_Env), symref.i_Name);
				case SymbolRefType.Local:
					return m_ValueStack[LocalSlot(ref m_ExecutionStack.Peek(GetStackNonClrFunction(stackFrameIndex)), symref.i_Index)].ToDynValue();
				case SymbolRefType.Upvalue:
					return m_ExecutionStack.Peek(GetStackNonClrFunction(stackFrameIndex)).Upvalues[symref.i_Index].Get().ToDynValue();
				default:
					throw new InternalErrorException("Unexpected {0} LRef at resolution: {1}", symref.i_Type, symref.i_Name);
			}
		}

		private DynValue GetGlobalSymbol(DynValue dynValue, string name)
		{
			if (dynValue.Type != DataType.Table)
				throw new InvalidOperationException(string.Format("_ENV is not a table but a {0}", dynValue.Type));

			return dynValue.Table.Get(name);
		}

		private void SetGlobalSymbol(DynValue dynValue, string name, DynValue value)
		{
			if (dynValue.Type != DataType.Table)
				throw new InvalidOperationException(string.Format("_ENV is not a table but a {0}", dynValue.Type));

			dynValue.Table.Set(name, value ?? DynValue.Nil);
		}


		public void AssignGenericSymbol(SymbolRef symref, DynValue value, int stackFrameIndex = -1)
		{
			switch (symref.i_Type)
			{
				case SymbolRefType.Global:
					SetGlobalSymbol(GetGenericSymbol(symref.i_Env), symref.i_Name, value);
					break;
				case SymbolRefType.Local:
					{
						ref CallStackItem stackframe = ref m_ExecutionStack.Peek(GetStackNonClrFunction(stackFrameIndex));
						m_ValueStack[LocalSlot(ref stackframe, symref.i_Index)] = value.ToLuaValue();
					}
					break;
				case SymbolRefType.Upvalue:
					{
						ref CallStackItem stackframe = ref m_ExecutionStack.Peek(GetStackNonClrFunction(stackFrameIndex));
						stackframe.Upvalues[symref.i_Index].Set(value.ToLuaValue());
					}
					break;
				case SymbolRefType.DefaultEnv:
					{
						throw new ArgumentException("Can't AssignGenericSymbol on a DefaultEnv symbol");
					}
				default:
					throw new InternalErrorException("Unexpected {0} LRef at resolution: {1}", symref.i_Type, symref.i_Name);
			}
		}

		/// <summary>
		/// The value stack slot of the local with symbol index <paramref name="index"/> in a frame. SPIKE: a
		/// register frame keeps its locals in register order (parameters first).
		/// </summary>
		private int LocalSlot(ref CallStackItem frame, int index)
		{
			if ((frame.Flags & CallStackItemFlags.RegisterFrame) != 0)
				return frame.BasePointer + frame.Function.Register.RegOf[index];

			return frame.BasePointer + index;
		}

		/// <summary>
		/// The index (from the top) of the frame of a script function: the given one, or with -1 the
		/// innermost. Returns -1 when that frame does not exist or runs a C# function.
		/// </summary>
		int GetStackNonClrFunction(int stackFrameIndex)
		{
			if (stackFrameIndex >= 0)
			{
				if (stackFrameIndex < m_ExecutionStack.Count && m_ExecutionStack.Peek(stackFrameIndex).ClrFunction == null)
					return stackFrameIndex;

				return -1;
			}

			for (int i = 0; i < m_ExecutionStack.Count; i++)
			{
				if (m_ExecutionStack.Peek(i).ClrFunction == null)
					return i;
			}

			return -1;
		}


		public SymbolRef FindSymbolByName(string name, int stackFrameIndex = -1)
		{
			if (m_CoroutinesStack != null && m_CoroutinesStack.Count > 0)
			{
				return m_CoroutinesStack[m_CoroutinesStack.Count - 1].FindSymbolByName(name, stackFrameIndex);
			}

			if (m_ExecutionStack.Count > 0)
			{
				int frameIndex = GetStackNonClrFunction(stackFrameIndex);

				if (frameIndex >= 0)
				{
					ref CallStackItem stackframe = ref m_ExecutionStack.Peek(frameIndex);

					if (stackframe.Debug_Symbols != null)
					{
						for (int i = stackframe.Debug_Symbols.Length - 1; i >= 0; i--)
						{
							var l = stackframe.Debug_Symbols[i];

							if (l.i_Name == name && !m_ValueStack[LocalSlot(ref stackframe, i)].IsUndeclared)
								return l;
						}
					}


					var closure = stackframe.Function;

					if (closure != null)
					{
						for (int i = 0; i < closure.GetUpvaluesCount(); i++)
							if (closure.GetUpvalueName(i) == name)
								return SymbolRef.Upvalue(name, i);
					}
				}
			}

			if (name != WellKnownSymbols.ENV)
			{
				SymbolRef env = FindSymbolByName(WellKnownSymbols.ENV);
				return SymbolRef.Global(name, env);
			}
			else
			{
				return SymbolRef.DefaultEnv;
			}
		}

	}
}
