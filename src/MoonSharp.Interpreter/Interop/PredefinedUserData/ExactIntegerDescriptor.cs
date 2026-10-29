using System;
using MoonSharp.Interpreter.Compatibility;

namespace MoonSharp.Interpreter.Interop
{
	/// <summary>
	/// Descriptor of the int64 and uint64 userdata (<see cref="LuaInt64"/>, <see cref="LuaUInt64"/>).
	/// Hand-written rather than reflected so that the operators cost one delegate call, need no
	/// registration in the global type registry and work under AOT/IL2CPP without codegen.
	/// </summary>
	internal sealed class ExactIntegerDescriptor : IUserDataDescriptor
	{
		internal static readonly ExactIntegerDescriptor Int64 = new ExactIntegerDescriptor("int64", typeof(LuaInt64));
		internal static readonly ExactIntegerDescriptor UInt64 = new ExactIntegerDescriptor("uint64", typeof(LuaUInt64));

		// Shared by every script: the callbacks are stateless.
		private static readonly DynValue s_Add = Meta(Add, "__add");
		private static readonly DynValue s_Sub = Meta(Sub, "__sub");
		private static readonly DynValue s_Mul = Meta(Mul, "__mul");
		private static readonly DynValue s_Div = Meta(Div, "__div");
		private static readonly DynValue s_Mod = Meta(Mod, "__mod");
		private static readonly DynValue s_Pow = Meta(Pow, "__pow");
		private static readonly DynValue s_Unm = Meta(Unm, "__unm");
		private static readonly DynValue s_Eq = Meta(Eq, "__eq");
		private static readonly DynValue s_Lt = Meta(Lt, "__lt");
		private static readonly DynValue s_Le = Meta(Le, "__le");
		private static readonly DynValue s_Concat = Meta(Concat, "__concat");

		private ExactIntegerDescriptor(string name, Type type)
		{
			Name = name;
			Type = type;
		}

		public string Name { get; private set; }

		public Type Type { get; private set; }

		public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
		{
			return null;
		}

		public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
		{
			return false;
		}

		public string AsString(object obj)
		{
			return obj != null ? obj.ToString() : Name;
		}

		public DynValue MetaIndex(Script script, object obj, string metaname)
		{
			switch (metaname)
			{
				case "__add": return s_Add;
				case "__sub": return s_Sub;
				case "__mul": return s_Mul;
				case "__div": return s_Div;
				case "__mod": return s_Mod;
				case "__pow": return s_Pow;
				case "__unm": return s_Unm;
				case "__eq": return s_Eq;
				case "__lt": return s_Lt;
				case "__le": return s_Le;
				case "__concat": return s_Concat;
				default: return null;
			}
		}

		public bool IsTypeCompatible(Type type, object obj)
		{
			return Framework.Do.IsInstanceOfType(type, obj);
		}

		private static DynValue Meta(Func<ScriptExecutionContext, CallbackArguments, DynValue> f, string name)
		{
			return CallbackFunction.NewInternal(f, name).AsReadOnly();
		}

		private enum Op { Add, Sub, Mul, Div, Mod }

		private static DynValue Add(ScriptExecutionContext ctx, CallbackArguments args) { return Arith(args, Op.Add); }
		private static DynValue Sub(ScriptExecutionContext ctx, CallbackArguments args) { return Arith(args, Op.Sub); }
		private static DynValue Mul(ScriptExecutionContext ctx, CallbackArguments args) { return Arith(args, Op.Mul); }
		private static DynValue Div(ScriptExecutionContext ctx, CallbackArguments args) { return Arith(args, Op.Div); }
		private static DynValue Mod(ScriptExecutionContext ctx, CallbackArguments args) { return Arith(args, Op.Mod); }

		/// <summary>
		/// Arithmetic wraps around like Lua 5.3 integers. If either side is a uint64 the operation
		/// is unsigned; otherwise it is signed. A number operand must be an exact integer.
		/// </summary>
		private static DynValue Arith(CallbackArguments args, Op op)
		{
			DynValue a = args[0];
			DynValue b = args[1];

			if (ExactInteger.AsUInt64(a) != null || ExactInteger.AsUInt64(b) != null)
			{
				ulong x = UInt64Operand(a);
				ulong y = UInt64Operand(b);
				ulong r;

				switch (op)
				{
					case Op.Add: r = unchecked(x + y); break;
					case Op.Sub: r = unchecked(x - y); break;
					case Op.Mul: r = unchecked(x * y); break;
					case Op.Div: r = ExactInteger.Div(x, y); break;
					default: r = ExactInteger.Mod(x, y); break;
				}

				return LuaUInt64.NewDynValue(r);
			}
			else
			{
				long x = Int64Operand(a);
				long y = Int64Operand(b);
				long r;

				switch (op)
				{
					case Op.Add: r = unchecked(x + y); break;
					case Op.Sub: r = unchecked(x - y); break;
					case Op.Mul: r = unchecked(x * y); break;
					case Op.Div: r = ExactInteger.FloorDiv(x, y); break;
					default: r = ExactInteger.FloorMod(x, y); break;
				}

				return LuaInt64.NewDynValue(r);
			}
		}

		private static long Int64Operand(DynValue v)
		{
			string error;
			long? r = ExactInteger.TryOperandToInt64(v, out error);
			if (r.HasValue)
				return r.Value;
			throw OperandError(v, error);
		}

		private static ulong UInt64Operand(DynValue v)
		{
			string error;
			ulong? r = ExactInteger.TryOperandToUInt64(v, out error);
			if (r.HasValue)
				return r.Value;
			throw OperandError(v, error);
		}

		private static ScriptRuntimeException OperandError(DynValue v, string error)
		{
			if (v.Type != DataType.Number && !ExactInteger.IsExactInteger(v))
				return new ScriptRuntimeException("attempt to perform arithmetic on a {0} value", v.Type.ToLuaTypeString());

			return new ScriptRuntimeException(error);
		}

		private static DynValue Pow(ScriptExecutionContext ctx, CallbackArguments args)
		{
			throw new ScriptRuntimeException("attempt to raise an int64 or uint64 value to a power; convert it with int64.tonumber first");
		}

		private static DynValue Unm(ScriptExecutionContext ctx, CallbackArguments args)
		{
			DynValue v = args[0];

			LuaUInt64 u = ExactInteger.AsUInt64(v);
			if (u != null)
				return LuaUInt64.NewDynValue(unchecked(0UL - u.Value));

			return LuaInt64.NewDynValue(unchecked(-Int64Operand(v)));
		}

		/// <summary>
		/// Raw Lua 5.2 equality: an int64 equals only an int64 of the same value (likewise uint64).
		/// int64 == number is false, which keeps == consistent with table keys; int64.eq compares
		/// across types.
		/// </summary>
		private static DynValue Eq(ScriptExecutionContext ctx, CallbackArguments args)
		{
			DynValue a = args[0];
			DynValue b = args[1];

			bool eq = ExactInteger.IsExactInteger(a) && ExactInteger.IsExactInteger(b)
				&& a.UserData.Object.Equals(b.UserData.Object);

			return DynValue.NewBoolean(eq);
		}

		private static DynValue Lt(ScriptExecutionContext ctx, CallbackArguments args)
		{
			int? c = CompareOperands(args[0], args[1]);
			return DynValue.NewBoolean(c.HasValue && c.Value < 0);
		}

		private static DynValue Le(ScriptExecutionContext ctx, CallbackArguments args)
		{
			int? c = CompareOperands(args[0], args[1]);
			return DynValue.NewBoolean(c.HasValue && c.Value <= 0);
		}

		/// <summary>
		/// Compares exactly; returns null for NaN so that every comparison with NaN is false.
		/// </summary>
		private static int? CompareOperands(DynValue a, DynValue b)
		{
			int? c = ExactInteger.Compare(a, b);

			if (c == null && (!IsNumeric(a) || !IsNumeric(b)))
			{
				string ta = ExactInteger.TypeName(a);
				string tb = ExactInteger.TypeName(b);
				throw new ScriptRuntimeException("attempt to compare {0} with {1}", ta, tb);
			}

			return c;
		}

		private static bool IsNumeric(DynValue v)
		{
			return v.Type == DataType.Number || ExactInteger.IsExactInteger(v);
		}

		private static DynValue Concat(ScriptExecutionContext ctx, CallbackArguments args)
		{
			DynValue a = args[0];
			DynValue b = args[1];

			string sa = ConcatPart(a);
			string sb = ConcatPart(b);

			if (sa == null || sb == null)
				throw new ScriptRuntimeException("attempt to concatenate a {0} value", (sa == null ? a : b).Type.ToLuaTypeString());

			return DynValue.NewString(sa + sb);
		}

		private static string ConcatPart(DynValue v)
		{
			if (ExactInteger.IsExactInteger(v))
				return v.UserData.Object.ToString();

			// Same conversion as the VM uses for number .. string.
			return v.CastToString();
		}
	}
}
