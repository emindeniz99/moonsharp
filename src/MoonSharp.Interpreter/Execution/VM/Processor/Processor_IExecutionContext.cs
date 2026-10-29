
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
			LuaValue meta;
			return GetBinaryMetamethod(op1.ToLuaValue(), op2.ToLuaValue(), eventName, 0, out meta) ? meta.ToDynValue() : null;
		}

		// Bits of Table.MetaAbsent, one per metamethod the VM looks up on its hot paths. A metatable found
		// without a metamethod remembers it until a string key of it is stored or removed, so the next
		// lookup is one AND instead of a hash lookup (Luau's tmcache). 0: not cached.
		internal const int MetaIndex = 1, MetaNewIndex = 2, MetaEq = 4, MetaLen = 8, MetaCall = 16,
			MetaConcat = 32, MetaUnm = 64, MetaLt = 128, MetaLe = 256, MetaAdd = 512, MetaSub = 1024,
			MetaMul = 2048, MetaDiv = 4096, MetaMod = 8192, MetaPow = 16384;

		/// <summary>
		/// The metamethod <paramref name="eventName"/> of <paramref name="metatable"/>; false when it has none.
		/// </summary>
		internal static bool RawGetMetamethod(Table metatable, string eventName, int bit, out LuaValue meta)
		{
			if ((metatable.MetaAbsent & bit) != 0)
			{
				meta = default(LuaValue);
				return false;
			}

			meta = metatable.RawGetValue(eventName);

			if (meta.Type == DataType.Nil)
			{
				metatable.MetaAbsent |= bit;
				return false;
			}

			return true;
		}

		internal bool GetBinaryMetamethod(LuaValue op1, LuaValue op2, string eventName, int bit, out LuaValue meta)
		{
			var op1_MetaTable = GetMetatable(op1);
			if (op1_MetaTable != null && RawGetMetamethod(op1_MetaTable, eventName, bit, out meta))
				return true;

			var op2_MetaTable = GetMetatable(op2);
			if (op2_MetaTable != null && RawGetMetamethod(op2_MetaTable, eventName, bit, out meta))
				return true;

			if (op1.Type == DataType.UserData)
			{
				UserData ud = (UserData)op1.Object;
				DynValue m = ud.Descriptor.MetaIndex(this.m_Script, ud.Object, eventName);

				if (m != null)
				{
					meta = m.ToLuaValue();
					return true;
				}
			}

			if (op2.Type == DataType.UserData)
			{
				UserData ud = (UserData)op2.Object;
				DynValue m = ud.Descriptor.MetaIndex(this.m_Script, ud.Object, eventName);

				if (m != null)
				{
					meta = m.ToLuaValue();
					return true;
				}
			}

			meta = default(LuaValue);
			return false;
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

			LuaValue meta;
			return GetMetamethodRaw(value, metamethod, bit, out meta) ? meta.ToDynValue() : null;
		}


		internal bool GetMetamethodRaw(LuaValue value, string metamethod, int bit, out LuaValue meta)
		{
			var metatable = GetMetatable(value);

			if (metatable == null)
			{
				meta = default(LuaValue);
				return false;
			}

			return RawGetMetamethod(metatable, metamethod, bit, out meta);
		}

		internal Script GetScript()
		{
			return m_Script;
		}
	}
}
