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
			LuaValue m = default(LuaValue);
			bool found = false;

			if (op1.Type == DataType.UserData)
			{
				UserData ud = (UserData)op1.Object;
				DynValue d = ud.Descriptor.MetaIndex(m_Script, ud.Object, eventName);

				if (d != null)
				{
					m = d.ToLuaValue();
					found = true;
				}
			}

			if (!found)
			{
				var op1_MetaTable = GetMetatable(op1);

				if (op1_MetaTable != null)
					found = RawGetMetamethod(op1_MetaTable, eventName, bit, out m);
			}

			if (found)
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
			LuaValue m;

			if (GetBinaryMetamethod(l, r, eventName, bit, out m))
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
			LuaValue m;

			if (GetBinaryMetamethod(l, r, eventName, bit, out m))
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
