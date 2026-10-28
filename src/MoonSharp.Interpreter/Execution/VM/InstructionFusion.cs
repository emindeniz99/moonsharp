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
	/// Ideas from Luau's JUMPIFLT/JUMPXEQK* compare-and-branch opcodes (Bytecode.h#L214-L245), no code
	/// copied.
	/// </summary>
	internal static class InstructionFusion
	{
		// Operand modes of the fused compares (NumVal2): where the right and the left operands are.
		internal const int RightOnStack = 0;     // both on the stack; replaces the compare (or its Swap)
		internal const int RightConst = 1;       // left on the stack, right = Value; replaces the Literal
		internal const int RightLocal = 2;       // left on the stack, right = local Symbol; replaces its Local
		internal const int LeftLocalConst = 3;   // left = local Symbol, right = Value; replaces the left Local
		internal const int LeftLocalLocal = 4;   // left = local Symbol, right = local NumVal3; replaces the left Local

		/// <summary>
		/// Fuses the instructions in [from, code.Count).
		/// </summary>
		internal static void Fuse(List<Instruction> code, int from)
		{
			for (int p = from; p < code.Count; p++)
				FuseCompare(code, p);
		}

		static void FuseCompare(List<Instruction> code, int p)
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

			if (q + 2 >= code.Count)
				return false;

			bool swapped = code[q].OpCode == OpCode.Swap && code[q].NumVal == 0 && code[q].NumVal2 == 1;
			int c = swapped ? q + 1 : q;

			if (c + 2 >= code.Count)
				return false;

			OpCode compare = code[c].OpCode;
			OpCode next = code[c + 1].OpCode;

			if (compare == OpCode.Less && next == OpCode.ToBool && code[c + 2].OpCode == OpCode.Jf)
			{
				op = swapped ? OpCode.JGt : OpCode.JLt;
				jf = c + 2;
			}
			else if (compare == OpCode.LessEq && next == OpCode.CNot && code[c + 2].OpCode == OpCode.Jf)
			{
				op = swapped ? OpCode.JGe : OpCode.JLe;
				jf = c + 2;
			}
			else if (!swapped && compare == OpCode.Eq && next == OpCode.ToBool)
			{
				if (code[c + 2].OpCode == OpCode.Jf)
				{
					op = OpCode.JEq;
					jf = c + 2;
				}
				else if (c + 3 < code.Count && code[c + 2].OpCode == OpCode.Not && code[c + 3].OpCode == OpCode.Jf)
				{
					op = OpCode.JNe;
					jf = c + 3;
				}
			}

			return jf >= 0;
		}
	}
}
