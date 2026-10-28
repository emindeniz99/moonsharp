
namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		internal Table GetMetatable(DynValue value)
		{
			return GetMetatable(value.ToLuaValue());
		}

		internal Table GetMetatable(LuaValue value)
		{
			if (value.Type == DataType.Table)
			{
				return ((Table)value.Object).MetaTable;
			}
			else if (value.Type.CanHaveTypeMetatables())
			{
				return m_Script.GetTypeMetatable(value.Type);
			}
			else
			{
				return null;
			}
		}

		internal DynValue GetBinaryMetamethod(DynValue op1, DynValue op2, string eventName)
		{
			return GetBinaryMetamethod(op1.ToLuaValue(), op2.ToLuaValue(), eventName);
		}

		internal DynValue GetBinaryMetamethod(LuaValue op1, LuaValue op2, string eventName)
		{
			var op1_MetaTable = GetMetatable(op1);
			if (op1_MetaTable != null)
			{
				DynValue meta1 = op1_MetaTable.RawGet(eventName);
				if (meta1 != null && meta1.IsNotNil())
					return meta1;
			}

			var op2_MetaTable = GetMetatable(op2);
			if (op2_MetaTable != null)
			{
				DynValue meta2 = op2_MetaTable.RawGet(eventName);
				if (meta2 != null && meta2.IsNotNil())
					return meta2;
			}

			if (op1.Type == DataType.UserData)
			{
				UserData ud = (UserData)op1.Object;
				DynValue meta = ud.Descriptor.MetaIndex(this.m_Script, ud.Object, eventName);

				if (meta != null)
					return meta;
			}

			if (op2.Type == DataType.UserData)
			{
				UserData ud = (UserData)op2.Object;
				DynValue meta = ud.Descriptor.MetaIndex(this.m_Script, ud.Object, eventName);

				if (meta != null)
					return meta;
			}

			return null;
		}

		internal DynValue GetMetamethod(DynValue value, string metamethod)
		{
			return GetMetamethod(value.ToLuaValue(), metamethod);
		}

		internal DynValue GetMetamethod(LuaValue value, string metamethod)
		{
			if (value.Type == DataType.UserData)
			{
				UserData ud = (UserData)value.Object;
				DynValue v = ud.Descriptor.MetaIndex(m_Script, ud.Object, metamethod);
				if (v != null)
					return v;
			}

			return GetMetamethodRaw(value, metamethod);
		}


		internal DynValue GetMetamethodRaw(LuaValue value, string metamethod)
		{
			var metatable = GetMetatable(value);

			if (metatable == null)
				return null;

			var metameth = metatable.RawGet(metamethod);
			
			if (metameth == null || metameth.IsNil())
				return null;

			return metameth;
		}

		internal Script GetScript()
		{
			return m_Script;
		}
	}
}
