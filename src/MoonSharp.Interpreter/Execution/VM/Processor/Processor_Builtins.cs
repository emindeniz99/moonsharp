using System;
using MoonSharp.Interpreter.CoreLib;
using MoonSharp.Interpreter.CoreLib.StringLib;

namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// Builtin fast calls (Luau's FASTCALL, PLAN A18). A few library functions, when called with arguments of
	/// exactly the types they expect, run here on the value stack without a call frame, an execution context
	/// or an argument list. Anything else (other types, other argument counts, results of calls as
	/// arguments) takes the ordinary call, so results and error messages stay the library's own. The guard is
	/// the identity of the CallbackFunction: only the instance the library registered carries an id, so a
	/// script that replaces math.floor calls its own function. A fast-called function does not appear in a
	/// traceback (owner-approved, see docs/MIGRATING.md).
	/// </summary>
	sealed partial class Processor
	{
		internal const int BuiltinAbs = 1, BuiltinCeil = 2, BuiltinFloor = 3, BuiltinSqrt = 4, BuiltinSin = 5,
			BuiltinCos = 6, BuiltinExp = 7, BuiltinMax = 8, BuiltinMin = 9, BuiltinLen = 10, BuiltinSub = 11,
			BuiltinByte = 12, BuiltinType = 13, BuiltinSelect = 14, BuiltinInsert = 15;

		/// <summary>
		/// The fast-call id of library function <paramref name="name"/> of module <paramref name="module"/>,
		/// or 0.
		/// </summary>
		internal static int BuiltinId(Type module, string name)
		{
			if (module == typeof(MathModule))
			{
				switch (name)
				{
					case "abs": return BuiltinAbs;
					case "ceil": return BuiltinCeil;
					case "floor": return BuiltinFloor;
					case "sqrt": return BuiltinSqrt;
					case "sin": return BuiltinSin;
					case "cos": return BuiltinCos;
					case "exp": return BuiltinExp;
					case "max": return BuiltinMax;
					case "min": return BuiltinMin;
				}
			}
			else if (module == typeof(StringModule))
			{
				switch (name)
				{
					case "len": return BuiltinLen;
					case "sub": return BuiltinSub;
					case "byte": return BuiltinByte;
				}
			}
			else if (module == typeof(BasicModule))
			{
				switch (name)
				{
					case "type": return BuiltinType;
					case "select": return BuiltinSelect;
				}
			}
			else if (module == typeof(TableModule) && name == "insert")
			{
				return BuiltinInsert;
			}

			return 0;
		}

		/// <summary>
		/// Runs builtin <paramref name="id"/> on the top <paramref name="argc"/> stack values and replaces them
		/// and the function with its result; false, with the stack unchanged, when the arguments are not the
		/// exact shape the fast path handles.
		/// </summary>
		private bool TryFastCall(int id, int argc)
		{
			LuaValue[] st = m_ValueStack.Items;
			int first = m_ValueStack.Count - argc;

			// select('#', ...): the call expanded a trailing call result or '...', so every slot is a value.
			if (id == BuiltinSelect)
			{
				if (argc == 0 || st[first].Type != DataType.String || (string)st[first].Object != "#")
					return false;

				return FastResult(argc, LuaValue.FromNumber(argc - 1));
			}

			for (int k = 0; k < argc; k++)
			{
				DataType t = st[first + k].Type;

				if (t == DataType.Tuple || t == DataType.Void)
					return false;
			}

			switch (id)
			{
				case BuiltinAbs:
				case BuiltinCeil:
				case BuiltinFloor:
				case BuiltinSqrt:
				case BuiltinSin:
				case BuiltinCos:
				case BuiltinExp:
					{
						if (argc != 1 || st[first].Type != DataType.Number)
							return false;

						double d = st[first].Number;

						switch (id)
						{
							case BuiltinAbs: d = Math.Abs(d); break;
							case BuiltinCeil: d = Math.Ceiling(d); break;
							case BuiltinFloor: d = Math.Floor(d); break;
							case BuiltinSqrt: d = Math.Sqrt(d); break;
							case BuiltinSin: d = Math.Sin(d); break;
							case BuiltinCos: d = Math.Cos(d); break;
							default: d = Math.Exp(d); break;
						}

						return FastResult(argc, LuaValue.FromNumber(d));
					}
				case BuiltinMax:
				case BuiltinMin:
					{
						if (argc == 0)
							return false;

						for (int k = 0; k < argc; k++)
							if (st[first + k].Type != DataType.Number)
								return false;

						double acc = st[first].Number;

						for (int k = 1; k < argc; k++)
							acc = (id == BuiltinMax) ? Math.Max(acc, st[first + k].Number) : Math.Min(acc, st[first + k].Number);

						return FastResult(argc, LuaValue.FromNumber(acc));
					}
				case BuiltinLen:
					if (argc != 1 || st[first].Type != DataType.String)
						return false;

					return FastResult(argc, LuaValue.FromNumber(((string)st[first].Object).Length));
				case BuiltinSub:
					{
						if (argc < 2 || argc > 3 || st[first].Type != DataType.String || st[first + 1].Type != DataType.Number
							|| (argc == 3 && st[first + 2].Type != DataType.Number))
							return false;

						int i = (int)st[first + 1].Number;
						int j = (argc == 3) ? (int)st[first + 2].Number : -1;
						return FastResult(argc, LuaValue.FromString(StringRange.Apply((string)st[first].Object, i, j)));
					}
				case BuiltinByte:
					{
						if (argc < 1 || argc > 3 || st[first].Type != DataType.String
							|| (argc >= 2 && st[first + 1].Type != DataType.Number)
							|| (argc == 3 && st[first + 2].Type != DataType.Number))
							return false;

						// StringRange.Apply's rules, without the substring
						string s = (string)st[first].Object;
						int i = (argc >= 2) ? (int)st[first + 1].Number : 1;
						int j = (argc == 3) ? (int)st[first + 2].Number : i;

						if (i < 0) i += s.Length + 1;
						if (j < 0) j += s.Length + 1;
						if (i < 1) i = 1;
						if (j > s.Length) j = s.Length;

						// one character gives one value; any other count goes the ordinary way
						if (i != j)
							return false;

						return FastResult(argc, LuaValue.FromNumber(StringModule.Unicode2Ascii(s[i - 1])));
					}
				case BuiltinType:
					if (argc != 1)
						return false;

					return FastResult(argc, LuaValue.FromString(st[first].Type.ToLuaTypeString()));
				case BuiltinInsert:
					{
						// table.insert(t, v): v goes after the border; the library returns t.
						if (argc != 2 || st[first].Type != DataType.Table || st[first + 1].Type == DataType.Nil)
							return false;

						Table t = (Table)st[first].Object;

						if (t.MetaTable != null)
							return false;

						t.Set(t.Length + 1, st[first + 1].ToDynValue());
						return FastResult(argc, st[first]);
					}
				default:
					return false;
			}
		}

		private bool FastResult(int argc, LuaValue result)
		{
			m_ValueStack.RemoveLast(argc);
			m_ValueStack.Peek() = result;
			return true;
		}
	}
}
