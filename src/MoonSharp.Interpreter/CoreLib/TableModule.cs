// Disable warnings about XML documentation
#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Text;
using MoonSharp.Interpreter.Execution.VM;

namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing table Lua functions 
	/// </summary>
	[MoonSharpModule(Namespace = "table")]
	public class TableModule
	{
		const int MaxUnpackResults = 1000000;

		// A number as luaL_optint reads it: through a 64-bit integer, keeping the low 32 bits.
		static int ToInt(double d)
		{
			return unchecked((int)(long)d);
		}

		[MoonSharpModuleMethod]
		public static DynValue unpack(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue s = args.AsType(0, "unpack", DataType.Table, false);
			DynValue vi = args.AsType(1, "unpack", DataType.Number, true);
			DynValue vj = args.AsType(2, "unpack", DataType.Number, true);

			int ii = vi.IsNil() ? 1 : ToInt(vi.Number);
			int ij = vj.IsNil() ? GetTableLength(executionContext, s) : ToInt(vj.Number);

			if (ii > ij)
				return DynValue.Void;

			// Lua 5.2 fails when the results do not fit on its stack (LUAI_MAXSTACK slots).
			if ((long)ij - ii >= MaxUnpackResults)
				throw new ScriptRuntimeException("too many results to unpack");

			Table t = s.Table;

			DynValue[] v = new DynValue[ij - ii + 1];

			int tidx = 0;
			for (int i = ii; i <= ij; i++)
				v[tidx++] = t.Get(i);

			return DynValue.NewTuple(v);
		}

		[MoonSharpModuleMethod]
		public static DynValue pack(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			Table t = new Table(executionContext.GetScript());
			DynValue v = DynValue.NewTable(t);

			for (int i = 0; i < args.Count; i++)
				t.Set(i + 1, args[i]);

			t.Set("n", DynValue.NewNumber(args.Count));

			return v;
		}

		[MoonSharpModuleMethod]
		public static DynValue sort(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue vlist = args.AsType(0, "sort", DataType.Table, false);
			DynValue lt = args[1];

			if (lt.Type != DataType.Function && lt.Type != DataType.ClrFunction && lt.IsNotNil())
				args.AsType(1, "sort", DataType.Function, true); // this throws

			int end = GetTableLength(executionContext, vlist);

			List<DynValue> values = new List<DynValue>();

			for (int i = 1; i <= end; i++)
				values.Add(vlist.Table.Get(i));

			// Comparisons are counted for the host's interrupt handler, which can stop a long sort.
			int comparisons = 0;

			try
			{
				values.Sort((a, b) =>
				{
					if (++comparisons == 1024)
					{
						comparisons = 0;
						executionContext.LibraryWork(1024);
					}

					return SortComparer(executionContext, a, b, lt);
				});
			}
			catch (InvalidOperationException ex)
			{
				// List.Sort wraps what the comparer throws. A script error and an interruption go on as
				// themselves; anything else as it came, rather than leaving the table half sorted.
				if (ex.InnerException is InterpreterException)
					throw ex.InnerException;

				throw;
			}

			for (int i = 0; i < values.Count; i++)
			{
				vlist.Table.Set(i + 1, values[i]);
			}

			return vlist;
		}

		private static int SortComparer(ScriptExecutionContext executionContext, DynValue a, DynValue b, DynValue lt)
		{
			if (lt == null || lt.IsNil())
			{
				lt = executionContext.GetBinaryMetamethod(a, b, "__lt");

				if (lt == null || lt.IsNil())
				{
					if (a.Type == DataType.Number && b.Type == DataType.Number)
						return a.Number.CompareTo(b.Number);
					if (a.Type == DataType.String && b.Type == DataType.String)
						return string.CompareOrdinal(a.String, b.String);

					throw ScriptRuntimeException.CompareInvalidType(a, b);
				}
				else
				{
					return LuaComparerToClrComparer(
						executionContext.GetScript().Call(lt, a, b),
						executionContext.GetScript().Call(lt, b, a));
				}
			}
			else
			{
				return LuaComparerToClrComparer(
					executionContext.GetScript().Call(lt, a, b),
					executionContext.GetScript().Call(lt, b, a));
			}
		}

		private static int LuaComparerToClrComparer(DynValue dynValue1, DynValue dynValue2)
		{
			bool v1 = dynValue1.CastToBool();
			bool v2 = dynValue2.CastToBool();

			if (v1 && !v2)
				return -1;
			if (v2 && !v1)
				return 1;

			if (v1 || v2)
				throw new ScriptRuntimeException("invalid order function for sorting");

			return 0;
		}

		[MoonSharpModuleMethod]
		public static DynValue insert(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue vlist = args.AsType(0, "table.insert", DataType.Table, false);

			// Lua 5.2's tinsert: the argument count, not a nil value, picks the form.
			if (args.Count != 2 && args.Count != 3)
				throw new ScriptRuntimeException("wrong number of arguments to 'insert'");

			int len = GetTableLength(executionContext, vlist);
			Table list = vlist.Table;
			DynValue vpos = args[1];
			DynValue vvalue = args[2];

			if (args.Count == 2)
			{
				vvalue = vpos;
				vpos = DynValue.NewNumber(len + 1);
			}

			if (vpos.Type != DataType.Number)
				throw ScriptRuntimeException.BadArgument(1, "table.insert", DataType.Number, vpos.Type, false);

			int pos = ToInt(vpos.Number);

			if (pos > len + 1 || pos < 1)
				throw new ScriptRuntimeException("bad argument #2 to 'insert' (position out of bounds)");

			for (int i = len; i >= pos; i--)
			{
				list.SetValue(i + 1, list.RawGetValue(i));
			}

			list.Set(pos, vvalue);

			return vlist;
		}


		[MoonSharpModuleMethod]
		public static DynValue remove(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue vlist = args.AsType(0, "table.remove", DataType.Table, false);
			DynValue vpos = args.AsType(1, "table.remove", DataType.Number, true);

			int len = GetTableLength(executionContext, vlist);
			Table list = vlist.Table;

			int pos = vpos.IsNil() ? len : ToInt(vpos.Number);

			// Lua 5.2's tremove: a position other than #t must be in 1..#t + 1. It returns t[pos], moves
			// the elements after it down and clears the last one (t[0] for an empty table).
			if (pos != len && (pos < 1 || pos > len + 1))
				throw new ScriptRuntimeException("bad argument #1 to 'remove' (position out of bounds)");

			DynValue ret = list.Get(pos);

			for (; pos < len; pos++)
				list.SetValue(pos, list.RawGetValue(pos + 1));

			list.SetValue(pos, LuaValue.Nil);
			return ret;
		}


		//table.concat (list [, sep [, i [, j]]])
		//Given a list where all elements are strings or numbers, returns the string list[i]..sep..list[i+1] (...) sep..list[j]. 
		//The default value for sep is the empty string, the default for i is 1, and the default for j is #list. If i is greater 
		//than j, returns the empty string. 
		[MoonSharpModuleMethod]
		public static DynValue concat(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			DynValue vlist = args.AsType(0, "concat", DataType.Table, false);
			DynValue vsep = args.AsType(1, "concat", DataType.String, true);
			DynValue vstart = args.AsType(2, "concat", DataType.Number, true);
			DynValue vend = args.AsType(3, "concat", DataType.Number, true);

			Table list = vlist.Table;
			string sep = vsep.IsNil() ? "" : vsep.String;
			int start = vstart.IsNilOrNan() ? 1 : (int)vstart.Number;
			int end; 

			if (vend.IsNilOrNan())
			{
				end = GetTableLength(executionContext, vlist);
			}
			else 
			{
				end = (int)vend.Number;
			}

			if (end < start)
				return DynValue.NewString(string.Empty);

			StringBuilder sb = new StringBuilder();

			for (int i = start; i <= end; i++)
			{
				LuaValue v = list.RawGetValue(i);

				if (v.Type != DataType.Number && v.Type != DataType.String)
					throw new ScriptRuntimeException("invalid value ({1}) at index {0} in table for 'concat'", i, v.Type.ToLuaTypeString());

				string s = v.CastToString();

				if (i != start)
					sb.Append(sep);
				
				sb.Append(s);

				// Lets the host's interrupt handler stop a concatenation of millions of values.
				if (((i - start) & 1023) == 1023)
					executionContext.LibraryWork(1024);

			}

			return DynValue.NewString(sb.ToString());
		}

		//table.maxn (table)
		//Lua 5.1's function, which Lua 5.2's default build (LUA_COMPAT_ALL) keeps: the largest positive numerical
		//index of the table, or zero. It reads the table raw and counts fractional keys too, as Lua does.
		[MoonSharpModuleMethod]
		public static DynValue maxn(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			Table t = args.AsType(0, "maxn", DataType.Table, false).Table;
			double max = 0;
			int n = 0;

			foreach (DynValue key in t.Keys)
			{
				if (key.Type == DataType.Number && key.Number > max)
					max = key.Number;

				if ((++n & 1023) == 0)
					executionContext.LibraryWork(1024);
			}

			return DynValue.NewNumber(max);
		}

		private static int GetTableLength(ScriptExecutionContext executionContext, DynValue vlist)
		{
			DynValue __len = executionContext.GetMetamethod(vlist, "__len");

			if (__len != null)
			{
				DynValue lenv = executionContext.GetScript().Call(__len, vlist);

				double? len = lenv.CastToNumber();

				if (len == null)
					throw new ScriptRuntimeException("object length is not a number");

				return (int)len;
			}
			else
			{
				return (int)vlist.Table.Length;
			}
		}
	}


	/// <summary>
	/// Class exposing table.unpack and table.pack in the global namespace (to work around the most common Lua 5.1 compatibility issue).
	/// </summary>
	[MoonSharpModule]
	public class TableModule_Globals
	{
		[MoonSharpModuleMethod]
		public static DynValue unpack(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return TableModule.unpack(executionContext, args);
		}

		[MoonSharpModuleMethod]
		public static DynValue pack(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return TableModule.pack(executionContext, args);
		}
	}


}
