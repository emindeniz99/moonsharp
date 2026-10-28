using System.Collections.Generic;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		private DynValue[] Internal_AdjustTuple(IList<DynValue> values)
		{
			if (values == null || values.Count == 0)
				return new DynValue[0];

			if (values[values.Count - 1].Type == DataType.Tuple)
			{
				int baseLen = values.Count - 1 + values[values.Count - 1].Tuple.Length;
				DynValue[] result = new DynValue[baseLen];

				for (int i = 0; i < values.Count - 1; i++)
				{
					result[i] = values[i].ToScalar();
				}

				for (int i = 0; i < values[values.Count - 1].Tuple.Length; i++)
				{
					result[values.Count + i - 1] = values[values.Count - 1].Tuple[i];
				}

				if (result[result.Length - 1].Type == DataType.Tuple)
					return Internal_AdjustTuple(result);
				else
					return result;
			}
			else
			{
				DynValue[] result = new DynValue[values.Count];

				for (int i = 0; i < values.Count; i++)
				{
					result[i] = values[i].ToScalar();
				}

				return result;
			}
		}



		private int Internal_InvokeUnaryMetaMethod(LuaValue op1, string eventName, int bit, int instructionPtr)
		{
			DynValue m = null;

			if (op1.Type == DataType.UserData)
			{
				UserData ud = (UserData)op1.Object;
				m = ud.Descriptor.MetaIndex(m_Script, ud.Object, eventName);
			}

			if (m == null)
			{
				var op1_MetaTable = GetMetatable(op1);

				if (op1_MetaTable != null)
					m = RawGetMetamethod(op1_MetaTable, eventName, bit);
			}

			if (m != null)
			{
				m_ValueStack.Push(m);
				m_ValueStack.Push(op1);
				return Internal_ExecCall(1, instructionPtr, metamethod: true);
			}
			else
			{
				return -1;
			}
		}

		private int Internal_InvokeBinaryMetaMethod(LuaValue l, LuaValue r, string eventName, int bit, int instructionPtr)
		{
			var m = GetBinaryMetamethod(l, r, eventName, bit);

			if (m != null)
			{
				m_ValueStack.Push(m);
				m_ValueStack.Push(l);
				m_ValueStack.Push(r);
				return Internal_ExecCall(2, instructionPtr, metamethod: true);
			}
			else
			{
				return -1;
			}
		}

		private int Internal_InvokeBinaryMetaMethod(LuaValue l, LuaValue r, string eventName, int bit, int instructionPtr, LuaValue extraPush)
		{
			var m = GetBinaryMetamethod(l, r, eventName, bit);

			if (m != null)
			{
				m_ValueStack.Push(extraPush);
				m_ValueStack.Push(m);
				m_ValueStack.Push(l);
				m_ValueStack.Push(r);
				return Internal_ExecCall(2, instructionPtr, metamethod: true);
			}
			else
			{
				return -1;
			}
		}
	}
}
