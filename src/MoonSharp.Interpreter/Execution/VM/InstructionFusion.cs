using System.Collections.Generic;

namespace MoonSharp.Interpreter.Execution.VM
{
	/// <summary>
	/// Rewrites common instruction sequences into fused instructions after a chunk is compiled. A fused
	/// instruction replaces only the FIRST instruction of its sequence and leaves the others where they
	/// are, so no address moves and every jump into the middle of a sequence still lands on the original
	/// instructions. When its operands are plain numbers or strings, the fused instruction does the work of
	/// the whole sequence and skips past it; otherwise it does exactly what the instruction it replaced did
	/// and execution continues with the rest of the unfused sequence (string coercion, metamethods, error
	/// messages and their source locations stay those of the unfused code).
	///
	/// Ideas from Luau's JUMPIFLT/JUMPXEQK* compare-and-branch and ADDK..POWK opcodes
	/// (Bytecode.h#L214-L245), no code copied.
	/// </summary>
	internal static class InstructionFusion
	{
		// Operand modes of the fused compares (NumVal2): where the right and the left operands are.
		internal const int RightOnStack = 0;     // both on the stack; replaces the compare (or its Swap)
		internal const int RightConst = 1;       // left on the stack, right = Value; replaces the Literal
		internal const int RightLocal = 2;       // left on the stack, right = local Symbol; replaces its Local
		internal const int LeftLocalConst = 3;   // left = local Symbol, right = Value; replaces the left Local
		internal const int LeftLocalLocal = 4;   // left = local Symbol, right = local NumVal3; replaces the left Local

		// Fused arithmetic: the mode is in the low bits; ScalarFollows says that a Scalar follows the operation.
		internal const int ModeMask = 7;
		internal const int ScalarFollows = 8;

		/// <summary>
		/// Fuses the instructions in [from, code.Count).
		/// </summary>
		internal static void Fuse(List<Instruction> code, int from)
		{
			for (int p = from; p < code.Count; p++)
			{
				if (!FuseCompare(code, p))
					FuseArithmetic(code, p);
			}
		}

		/// <summary>
		/// Instructions a fused arithmetic instruction covers when its operands are numbers.
		/// </summary>
		internal static int ArithmeticLength(int mode, bool stores)
		{
			int operands = mode & ModeMask;
			int length = operands == RightOnStack ? 1 : (operands >= LeftLocalConst ? 3 : 2);

			if ((mode & ScalarFollows) != 0)
				length++;

			return stores ? length + 2 : length;
		}

		static void FuseArithmetic(List<Instruction> code, int p)
		{
			Instruction first = code[p];
			OpCode op;

			if (IsLocal(first) && p + 1 < code.Count && (IsLocal(code[p + 1]) || IsNumber(code[p + 1]))
				&& (op = ArithmeticAt(code, p + 2)) != OpCode.Invalid)
			{
				Instruction right = code[p + 1];

				if (right.OpCode == OpCode.Literal)
					RewriteArithmetic(code, p, op, LeftLocalConst, right.Value, 0, p + 2);
				else
					RewriteArithmetic(code, p, op, LeftLocalLocal, null, right.Symbol.i_Index, p + 2);
			}
			else if ((IsLocal(first) || IsNumber(first)) && (op = ArithmeticAt(code, p + 1)) != OpCode.Invalid)
			{
				RewriteArithmetic(code, p, op, first.OpCode == OpCode.Literal ? RightConst : RightLocal, first.Value, 0, p + 1);
			}
			else if ((op = ArithmeticAt(code, p)) != OpCode.Invalid)
			{
				// Two stack operands are fused only to skip the store: the operation alone is one instruction.
				if (StoreAt(code, SkipScalar(code, p + 1)) >= 0)
					RewriteArithmetic(code, p, op, RightOnStack, null, 0, p);
			}
		}

		static void RewriteArithmetic(List<Instruction> code, int p, OpCode op, int mode, DynValue constant, int rightLocal, int operation)
		{
			Instruction i = code[p];
			int after = SkipScalar(code, operation + 1);

			if (after > operation + 1)
				mode |= ScalarFollows;

			i.OpCode = op;
			i.NumVal = rightLocal;
			i.NumVal2 = mode;
			i.NumVal3 = StoreAt(code, after);
			i.Value = constant;

			if ((mode & ModeMask) == RightOnStack || (mode & ModeMask) == RightConst)
				i.Symbol = null;
		}

		static bool IsNumber(Instruction i)
		{
			return i.OpCode == OpCode.Literal && i.Value != null && i.Value.Type == DataType.Number;
		}

		static OpCode ArithmeticAt(List<Instruction> code, int q)
		{
			if (q >= code.Count)
				return OpCode.Invalid;

			switch (code[q].OpCode)
			{
				case OpCode.Add: return OpCode.AddF;
				case OpCode.Sub: return OpCode.SubF;
				case OpCode.Mul: return OpCode.MulF;
				case OpCode.Div: return OpCode.DivF;
				case OpCode.Mod: return OpCode.ModF;
				case OpCode.Power: return OpCode.PowF;
				default: return OpCode.Invalid;
			}
		}

		/// <summary>
		/// The local slot that `local = value` stores the value at the top of the stack in (StoreLcl of the
		/// top, then Pop 1, as AssignmentStatement emits a single assignment), or -1.
		/// </summary>
		static int StoreAt(List<Instruction> code, int q)
		{
			if (q + 1 < code.Count && code[q].OpCode == OpCode.StoreLcl && code[q].Symbol.Type == SymbolRefType.Local
				&& code[q].NumVal == 0 && code[q].NumVal2 == 0 && code[q + 1].OpCode == OpCode.Pop && code[q + 1].NumVal == 1)
				return code[q].Symbol.i_Index;

			return -1;
		}

		static bool FuseCompare(List<Instruction> code, int p)
		{
			Instruction first = code[p];
			OpCode op;
			int jf;

			if (IsLocal(first) && p + 1 < code.Count && (IsLocal(code[p + 1]) || code[p + 1].OpCode == OpCode.Literal)
				&& MatchCompare(code, p + 2, out op, out jf))
			{
				Instruction right = code[p + 1];

				if (right.OpCode == OpCode.Literal)
					Rewrite(first, op, jf - p, LeftLocalConst, right.Value, 0);
				else
					Rewrite(first, op, jf - p, LeftLocalLocal, null, right.Symbol.i_Index);
			}
			else if ((IsLocal(first) || first.OpCode == OpCode.Literal) && MatchCompare(code, p + 1, out op, out jf))
			{
				if (first.OpCode == OpCode.Literal)
					Rewrite(first, op, jf - p, RightConst, first.Value, 0);
				else
					Rewrite(first, op, jf - p, RightLocal, null, 0);
			}
			else if (MatchCompare(code, p, out op, out jf))
			{
				Rewrite(first, op, jf - p, RightOnStack, null, 0);
			}
			else
			{
				return false;
			}

			return true;
		}

		static void Rewrite(Instruction i, OpCode op, int distanceToJf, int mode, DynValue constant, int rightLocal)
		{
			i.OpCode = op;
			i.NumVal = distanceToJf;
			i.NumVal2 = mode;
			i.NumVal3 = rightLocal;
			i.Value = constant;

			if (mode == RightOnStack || mode == RightConst)
				i.Symbol = null;
		}

		static bool IsLocal(Instruction i)
		{
			return i.OpCode == OpCode.Local && i.Symbol.Type == SymbolRefType.Local;
		}

		/// <summary>
		/// Matches the comparisons BinaryOperatorExpression emits, followed by the Jf of a condition:
		/// a &lt; b: Less ToBool Jf; a &lt;= b: LessEq CNot Jf; a &gt; b and a &gt;= b: Swap(0, 1) before those;
		/// a == b: Eq ToBool Jf; a ~= b (and not (a == b)): Eq ToBool Not Jf.
		/// </summary>
		static bool MatchCompare(List<Instruction> code, int q, out OpCode op, out int jf)
		{
			op = OpCode.Invalid;
			jf = -1;

			bool swapped = OpAt(code, q) == OpCode.Swap && code[q].NumVal == 0 && code[q].NumVal2 == 1;
			int c = swapped ? q + 1 : q;
			OpCode compare = OpAt(code, c);
			OpCode next = OpAt(code, c + 1);

			if (compare == OpCode.Less && next == OpCode.ToBool)
				op = swapped ? OpCode.JGt : OpCode.JLt;
			else if (compare == OpCode.LessEq && next == OpCode.CNot)
				op = swapped ? OpCode.JGe : OpCode.JLe;
			else if (!swapped && compare == OpCode.Eq && next == OpCode.ToBool)
				op = OpCode.JEq;
			else
				return false;

			int k = SkipScalar(code, c + 2);

			if (op == OpCode.JEq && OpAt(code, k) == OpCode.Not)
			{
				op = OpCode.JNe;
				k = SkipScalar(code, k + 1);
			}

			if (OpAt(code, k) != OpCode.Jf)
				return false;

			jf = k;
			return true;
		}

		static OpCode OpAt(List<Instruction> code, int q)
		{
			return q < code.Count ? code[q].OpCode : OpCode.Invalid;
		}

		// A parenthesized expression adds a Scalar, which does nothing to a number or a boolean.
		static int SkipScalar(List<Instruction> code, int q)
		{
			return OpAt(code, q) == OpCode.Scalar ? q + 1 : q;
		}
	}
}
