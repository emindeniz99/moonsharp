
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

		// Bits of Table.MetaAbsent, one per metamethod the VM looks up on its hot paths. A metatable found
		// without a metamethod remembers it until a string key of it is stored or removed, so the next
		// lookup is one AND instead of a hash lookup (Luau's tmcache). 0: not cached.
		internal const int MetaIndex = 1, MetaNewIndex = 2, MetaEq = 4, MetaLen = 8, MetaCall = 16,
			MetaConcat = 32, MetaUnm = 64, MetaLt = 128, MetaLe = 256, MetaAdd = 512, MetaSub = 1024,
			MetaMul = 2048, MetaDiv = 4096, MetaMod = 8192, MetaPow = 16384;

		/// <summary>
		/// The metamethod <paramref name="eventName"/> of <paramref name="metatable"/>, or null.
		/// </summary>
		internal static DynValue RawGetMetamethod(Table metatable, string eventName, int bit)
		{
			if ((metatable.MetaAbsent & bit) != 0)
				return null;

			DynValue meta = metatable.RawGet(eventName);

			if (meta == null || meta.IsNil())
			{
				metatable.MetaAbsent |= bit;
				return null;
			}

			return meta;
		}

		internal DynValue GetBinaryMetamethod(LuaValue op1, LuaValue op2, string eventName, int bit = 0)
		{
			var op1_MetaTable = GetMetatable(op1);
			if (op1_MetaTable != null)
			{
				DynValue meta1 = RawGetMetamethod(op1_MetaTable, eventName, bit);
				if (meta1 != null)
					return meta1;
			}

			var op2_MetaTable = GetMetatable(op2);
			if (op2_MetaTable != null)
			{
				DynValue meta2 = RawGetMetamethod(op2_MetaTable, eventName, bit);
				if (meta2 != null)
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

		internal DynValue GetMetamethod(LuaValue value, string metamethod, int bit = 0)
		{
			if (value.Type == DataType.UserData)
			{
				UserData ud = (UserData)value.Object;
				DynValue v = ud.Descriptor.MetaIndex(m_Script, ud.Object, metamethod);
				if (v != null)
					return v;
			}

			return GetMetamethodRaw(value, metamethod, bit);
		}


		internal DynValue GetMetamethodRaw(LuaValue value, string metamethod, int bit = 0)
		{
			var metatable = GetMetatable(value);

			if (metatable == null)
				return null;

			return RawGetMetamethod(metatable, metamethod, bit);
		}

		internal Script GetScript()
		{
			return m_Script;
		}
	}
}
