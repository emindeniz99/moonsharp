using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;
using MoonSharp.Interpreter.Tree.Expressions;
using MoonSharp.Interpreter.Tree.Statements;

namespace MoonSharp.Interpreter.Tree
{
	/// <summary>
	/// SPIKE (branch spike/register-vm): a function body the register compiler may take. The stack
	/// compiler records one per function (and per chunk) as it emits its BeginFn.
	/// </summary>
	internal sealed class RegisterCandidate
	{
		public Statement Body;
		public SymbolRef[] Params;
		public int NumLocals;
		public bool VarArgs;
		public SymbolRef EnvLocal;
		public SourceRef End;
		public Instruction BeginFn;
	}

	/// <summary>
	/// SPIKE (branch spike/register-vm): compiles a function to register code (RegisterProto.cs) when
	/// every statement and expression in it is in the supported subset; otherwise the function keeps
	/// only its stack code (a whole-function fallback). The stack code is always there: a debugger,
	/// coroutines and binary dumps use it.
	///
	/// Register allocation follows Lua 5.2's lcode.c in spirit (ideas only, no code): locals keep one
	/// register each for the whole function (parameters first), temporaries are allocated as a stack
	/// above them, and a call places the function and its arguments in consecutive registers at the top,
	/// so a register callee's frame starts at its first argument.
	/// </summary>
	internal sealed class RegisterCompiler
	{
		sealed class Unsupported : Exception
		{
		}

		readonly ByteCode m_Bc;
		readonly List<RInstr> m_Code = new List<RInstr>();
		readonly List<SourceRef> m_Refs = new List<SourceRef>();
		readonly List<LuaValue> m_K = new List<LuaValue>();
		readonly Dictionary<long, int> m_NumK = new Dictionary<long, int>();
		readonly Dictionary<string, int> m_StrK = new Dictionary<string, int>();
		readonly List<Instruction> m_Aux = new List<Instruction>();
		readonly List<int> m_SlowSites = new List<int>();
		readonly Stack<List<int>> m_Breaks = new Stack<List<int>>();
		readonly int[] m_RegOf;
		readonly int m_NumLocals;
		int m_Next, m_Max;
		// Nesting of the recursive walk; deeper trees (long operator chains) stay on the stack VM, whose
		// compiler walks them without recursion.
		int m_Depth;
		const int MaxDepth = 100;
		SourceRef m_Ref;

		RegisterCompiler(ByteCode bc, RegisterCandidate c)
		{
			m_Bc = bc;
			m_NumLocals = c.NumLocals;
			m_RegOf = new int[c.NumLocals];

			for (int k = 0; k < m_RegOf.Length; k++)
				m_RegOf[k] = -1;

			int next = 0;

			foreach (SymbolRef p in c.Params)
				m_RegOf[p.i_Index] = next++;

			for (int k = 0; k < m_RegOf.Length; k++)
				if (m_RegOf[k] < 0)
					m_RegOf[k] = next++;

			m_Next = m_Max = m_NumLocals;
		}

		/// <summary>
		/// Compiles the candidates the last load recorded and attaches the register code to their BeginFn.
		/// </summary>
		internal static void CompileAll(ByteCode bc)
		{
			List<RegisterCandidate> candidates = bc.RegisterCandidates;
			bc.RegisterCandidates = new List<RegisterCandidate>();

			if (!bc.Script.Options.RegisterVm)
				return;

			foreach (RegisterCandidate c in candidates)
			{
				if (c.VarArgs)
					continue;

				try
				{
					c.BeginFn.Register = new RegisterCompiler(bc, c).Compile(c);
				}
				catch (Unsupported)
				{
				}
			}
		}

		RegisterProto Compile(RegisterCandidate c)
		{
			Block(c.Body);

			m_Ref = c.End;
			Emit(ROp.Ret0, 0, 0, 0);

			var p = new RegisterProto()
			{
				Code = m_Code.ToArray(),
				K = m_K.ToArray(),
				Aux = m_Aux.ToArray(),
				Refs = m_Refs.ToArray(),
				NumParams = c.Params.Length,
				RegOf = m_RegOf,
				Symbols = c.BeginFn.SymbolList,
				FrameSize = m_Max,
				EnvReg = c.EnvLocal != null ? m_RegOf[c.EnvLocal.i_Index] : -1,
				IndexAux = new Instruction(null) { OpCode = OpCode.Index },
				IndexSetAux = new Instruction(null) { OpCode = OpCode.IndexSet },
			};

			// One resume trampoline per slow site, after everything the load emitted: a Nop that carries the
			// site's position (the stack VM reads the call position at the return address - 1), then RegResume.
			foreach (int pc in m_SlowSites)
			{
				m_Bc.Code.Add(new Instruction(p.Refs[pc]) { OpCode = OpCode.Nop, Name = "register resume" });

				if (p.Code[pc].Op == ROp.CallT)
					m_Bc.Code.Add(new Instruction(p.Refs[pc]) { OpCode = OpCode.Ret, NumVal = 1 });
				else
					m_Bc.Code.Add(new Instruction(p.Refs[pc]) { OpCode = OpCode.RegResume });

				p.Code[pc].D = m_Bc.Code.Count - 1;
			}

			return p;
		}

		// ---- emission ----

		int Emit(ROp op, int a, int b, int c)
		{
			m_Code.Add(new RInstr() { Op = op, A = a, B = b, C = c, D = -1 });
			m_Refs.Add(m_Ref);
			return m_Code.Count - 1;
		}

		int EmitSlow(ROp op, int a, int b, int c)
		{
			int pc = Emit(op, a, b, c);
			m_SlowSites.Add(pc);
			return pc;
		}

		int Here
		{
			get { return m_Code.Count; }
		}

		void Patch(int pc, int target)
		{
			RInstr i = m_Code[pc];

			switch (i.Op)
			{
				case ROp.Jmp: i.A = target; break;
				case ROp.JmpIf:
				case ROp.JmpIfNot: i.B = target; break;
				default: i.C = target; break;
			}

			m_Code[pc] = i;
		}

		int AllocTemp()
		{
			int r = m_Next++;

			if (m_Next > m_Max)
				m_Max = m_Next;

			return r;
		}

		int Const(DynValue v)
		{
			int k;

			switch (v.Type)
			{
				case DataType.Number:
					if (!m_NumK.TryGetValue(BitConverter.DoubleToInt64Bits(v.Number), out k))
					{
						k = m_K.Count;
						m_K.Add(LuaValue.FromNumber(v.Number));
						m_NumK[BitConverter.DoubleToInt64Bits(v.Number)] = k;
					}
					return k;
				case DataType.String:
					if (!m_StrK.TryGetValue(v.String, out k))
					{
						k = m_K.Count;
						m_K.Add(LuaValue.FromString(v.String));
						m_StrK[v.String] = k;
					}
					return k;
				case DataType.Nil:
				case DataType.Boolean:
					m_K.Add(v.ToLuaValue());
					return m_K.Count - 1;
				default:
					throw new Unsupported();
			}
		}

		int FieldAux(string key, OpCode op)
		{
			m_Aux.Add(new Instruction(m_Ref) { OpCode = op, Value = DynValue.NewString(key), Cache = new FieldCache(key) });
			return m_Aux.Count - 1;
		}

		int Reg(SymbolRef local)
		{
			return m_RegOf[local.i_Index];
		}

		// ---- statements ----

		void Block(Statement s)
		{
			if (++m_Depth > MaxDepth)
				throw new Unsupported();

			BlockCore(s);
			m_Depth--;
		}

		void BlockCore(Statement s)
		{
			if (s is CompositeStatement)
			{
				foreach (Statement x in ((CompositeStatement)s).m_Statements)
					Block(x);
			}
			else if (s is ScopeBlockStatement)
			{
				Block(((ScopeBlockStatement)s).m_Block);
			}
			else if (s is EmptyStatement)
			{
			}
			else if (s is AssignmentStatement)
			{
				Assignment((AssignmentStatement)s);
			}
			else if (s is FunctionCallStatement)
			{
				FunctionCallExpression fc = ((FunctionCallStatement)s).m_FunctionCallExpression;
				m_Ref = fc.SourceRef;
				int save = m_Next;
				Call(fc, false, -1);
				m_Next = save;
			}
			else if (s is IfStatement)
			{
				If((IfStatement)s);
			}
			else if (s is WhileStatement)
			{
				While((WhileStatement)s);
			}
			else if (s is RepeatStatement)
			{
				Repeat((RepeatStatement)s);
			}
			else if (s is ForLoopStatement)
			{
				NumericFor((ForLoopStatement)s);
			}
			else if (s is ReturnStatement)
			{
				Return((ReturnStatement)s);
			}
			else if (s is BreakStatement)
			{
				m_Ref = ((BreakStatement)s).m_Ref;

				if (m_Breaks.Count == 0)
					throw new Unsupported();

				m_Breaks.Peek().Add(Emit(ROp.Jmp, -1, 0, 0));
			}
			else
			{
				throw new Unsupported();
			}
		}

		void Assignment(AssignmentStatement s)
		{
			m_Ref = s.m_Ref;
			List<IVariable> lvalues = s.m_LValues;
			List<Expression> rvalues = s.m_RValues;
			Expression lastR = rvalues[rvalues.Count - 1];

			// A call or '...' that would supply several values.
			if (rvalues.Count < lvalues.Count && (lastR is FunctionCallExpression || IsVarArgs(lastR)))
				throw new Unsupported();

			int save = m_Next;

			if (lvalues.Count == 1 && rvalues.Count == 1)
			{
				SymbolRefExpression target = lvalues[0] as SymbolRefExpression;

				if (target != null && target.m_Ref != null && target.m_Ref.i_Type == SymbolRefType.Local)
					Expr(rvalues[0], Reg(target.m_Ref));
				else
					Store(lvalues[0], Expr(rvalues[0], -1));

				m_Next = save;
				return;
			}

			// Every value first, then the stores, left to right.
			int[] values = new int[lvalues.Count];

			for (int k = 0; k < rvalues.Count; k++)
			{
				if (k < lvalues.Count)
				{
					values[k] = AllocTemp();
					Expr(rvalues[k], values[k]);
				}
				else if (rvalues[k] is FunctionCallExpression)
				{
					int s2 = m_Next;
					Call((FunctionCallExpression)rvalues[k], false, -1);
					m_Next = s2;
				}
				else
				{
					int s2 = m_Next;
					Expr(rvalues[k], -1);
					m_Next = s2;
				}
			}

			for (int k = rvalues.Count; k < lvalues.Count; k++)
			{
				values[k] = AllocTemp();
				Emit(ROp.LoadK, values[k], Const(DynValue.Nil), 0);
			}

			for (int k = 0; k < lvalues.Count; k++)
				Store(lvalues[k], values[k]);

			m_Next = save;
		}

		void Store(IVariable lvalue, int src)
		{
			SymbolRefExpression sym = lvalue as SymbolRefExpression;

			if (sym != null)
			{
				SymbolRef r = sym.m_Ref;

				if (r == null)
					throw new Unsupported();

				switch (r.i_Type)
				{
					case SymbolRefType.Local:
						if (Reg(r) != src)
							Emit(ROp.Move, Reg(r), src, 0);
						return;
					case SymbolRefType.Upvalue:
						Emit(ROp.SetUpval, src, r.i_Index, 0);
						return;
					case SymbolRefType.Global:
						{
							SymbolRef env = r.i_Env;
							int aux = FieldAux(r.i_Name, OpCode.IndexSetN);

							if (env != null && env.i_Type == SymbolRefType.Upvalue)
								EmitSlow(ROp.SetTabUp, env.i_Index, aux, src);
							else if (env != null && env.i_Type == SymbolRefType.Local)
								EmitSlow(ROp.SetField, Reg(env), aux, src);
							else
								throw new Unsupported();
						}
						return;
					default:
						throw new Unsupported();
				}
			}

			IndexExpression ie = lvalue as IndexExpression;

			if (ie == null)
				throw new Unsupported();

			int save = m_Next;
			int obj = Expr(ie.m_BaseExp, -1);
			string name = ConstantName(ie);

			if (name != null)
			{
				EmitSlow(ROp.SetField, obj, FieldAux(name, OpCode.IndexSetN), src);
			}
			else
			{
				int key = Operand(ie.m_IndexExp);
				EmitSlow(ROp.SetTable, obj, key, src);
			}

			m_Next = save;
		}

		void If(IfStatement s)
		{
			var ends = new List<int>();

			for (int k = 0; k < s.m_Ifs.Count; k++)
			{
				IfStatement.IfBlock b = s.m_Ifs[k];
				m_Ref = b.Source;
				var falseJumps = new List<int>();
				Condition(b.Exp, false, falseJumps);
				Block(b.Block);

				if (k < s.m_Ifs.Count - 1 || s.m_Else != null)
				{
					m_Ref = s.m_End;
					ends.Add(Emit(ROp.Jmp, -1, 0, 0));
				}

				foreach (int j in falseJumps)
					Patch(j, Here);
			}

			if (s.m_Else != null)
				Block(s.m_Else.Block);

			foreach (int j in ends)
				Patch(j, Here);
		}

		void While(WhileStatement s)
		{
			m_Ref = s.m_Start;
			int start = Here;
			var exits = new List<int>();
			Condition(s.m_Condition, false, exits);

			m_Breaks.Push(exits);
			Block(s.m_Block);
			m_Breaks.Pop();

			m_Ref = s.m_End;
			Emit(ROp.Jmp, start, 0, 0);

			foreach (int j in exits)
				Patch(j, Here);
		}

		void Repeat(RepeatStatement s)
		{
			m_Ref = s.m_Repeat;
			int start = Here;
			var exits = new List<int>();

			m_Breaks.Push(exits);
			Block(s.m_Block);
			m_Breaks.Pop();

			m_Ref = s.m_Until;
			var again = new List<int>();
			Condition(s.m_Condition, false, again);

			foreach (int j in again)
				Patch(j, start);

			foreach (int j in exits)
				Patch(j, Here);
		}

		void NumericFor(ForLoopStatement s)
		{
			m_Ref = s.m_RefFor;
			int save = m_Next;
			int b = AllocTemp();
			AllocTemp();
			AllocTemp();

			// The stack compiler's order: limit, step, then the initial value, each checked as it comes.
			Expr(s.m_End, b + 1);
			Emit(ROp.ForNum, b + 1, 3, 0);
			Expr(s.m_Step, b + 2);
			Emit(ROp.ForNum, b + 2, 2, 0);
			Expr(s.m_Start, b);
			Emit(ROp.ForNum, b, 1, 0);

			int var = Reg(s.m_VarName);
			int prep = Emit(ROp.ForPrep, b, var, -1);
			int body = Here;

			var exits = new List<int>();
			m_Breaks.Push(exits);
			Block(s.m_InnerBlock);
			m_Breaks.Pop();

			m_Ref = s.m_RefEnd;
			Emit(ROp.ForLoop, b, var, body);

			Patch(prep, Here);

			foreach (int j in exits)
				Patch(j, Here);

			m_Next = save;
		}

		void Return(ReturnStatement s)
		{
			m_Ref = s.m_Ref;

			if (s.m_Expression == null)
			{
				Emit(ROp.Ret0, 0, 0, 0);
				return;
			}

			Expression e = s.m_Expression;
			ExprListExpression list = e as ExprListExpression;

			if (list != null)
			{
				Expression[] es = list.GetExpressions();

				if (es.Length != 1)
					throw new Unsupported();

				e = es[0];
			}

			if (IsVarArgs(e))
				throw new Unsupported();

			if (e is FunctionCallExpression)
			{
				int s1 = m_Next;
				CallInto((FunctionCallExpression)e, ROp.CallT, -1);
				m_Next = s1;
				return;
			}

			int save = m_Next;
			Emit(ROp.Ret1, Expr(e, -1), 0, 0);
			m_Next = save;
		}

		// ---- conditions ----

		/// <summary>
		/// Jumps (patch list) when the truth of e equals jumpIf; falls through otherwise.
		/// </summary>
		void Condition(Expression e, bool jumpIf, List<int> jumps)
		{
			if (++m_Depth > MaxDepth)
				throw new Unsupported();

			ConditionCore(e, jumpIf, jumps);
			m_Depth--;
		}

		void ConditionCore(Expression e, bool jumpIf, List<int> jumps)
		{
			while (e is AdjustmentExpression)
				e = ((AdjustmentExpression)e).expression;

			UnaryOperatorExpression u = e as UnaryOperatorExpression;

			if (u != null && u.m_OpText == "not")
			{
				Condition(u.m_Exp, !jumpIf, jumps);
				return;
			}

			BinaryOperatorExpression b = e as BinaryOperatorExpression;

			if (b != null && IsComparison(b.m_Operator))
			{
				int save = m_Next;
				int l = Operand(b.m_Exp1);
				int r = Operand(b.m_Exp2);
				m_Next = save;

				ROp op;
				bool sense = jumpIf;

				switch (b.m_Operator)
				{
					case BinaryOperatorExpression.Operator.Less: op = ROp.LtJf; break;
					case BinaryOperatorExpression.Operator.LessOrEqual: op = ROp.LeJf; break;
					// a > b is b < a, a >= b is b <= a (Lua 5.2 3.4.3), as the stack compiler does.
					case BinaryOperatorExpression.Operator.Greater: op = ROp.LtJf; Swap(ref l, ref r); break;
					case BinaryOperatorExpression.Operator.GreaterOrEqual: op = ROp.LeJf; Swap(ref l, ref r); break;
					case BinaryOperatorExpression.Operator.Equal: op = ROp.EqJf; break;
					default: op = ROp.EqJf; sense = !sense; break;
				}

				if (sense)
					op = op + 1;

				jumps.Add(EmitSlow(op, l, r, -1));
				return;
			}

			int save2 = m_Next;
			int v = Expr(e, -1);
			m_Next = save2;
			jumps.Add(Emit(jumpIf ? ROp.JmpIf : ROp.JmpIfNot, v, -1, 0));
		}

		static void Swap(ref int a, ref int b)
		{
			int t = a;
			a = b;
			b = t;
		}

		static bool IsComparison(BinaryOperatorExpression.Operator op)
		{
			switch (op)
			{
				case BinaryOperatorExpression.Operator.Less:
				case BinaryOperatorExpression.Operator.LessOrEqual:
				case BinaryOperatorExpression.Operator.Greater:
				case BinaryOperatorExpression.Operator.GreaterOrEqual:
				case BinaryOperatorExpression.Operator.Equal:
				case BinaryOperatorExpression.Operator.NotEqual:
					return true;
				default:
					return false;
			}
		}

		// ---- expressions ----

		static bool IsVarArgs(Expression e)
		{
			SymbolRefExpression s = e as SymbolRefExpression;
			return s != null && (s.m_VarName == WellKnownSymbols.VARARGS || (s.m_Ref != null && s.m_Ref.i_Name == WellKnownSymbols.VARARGS));
		}

		static string ConstantName(IndexExpression ie)
		{
			if (ie.m_Name != null)
				return ie.m_Name;

			LiteralExpression lit = ie.m_IndexExp as LiteralExpression;

			if (lit != null && lit.Value.Type == DataType.String)
				return lit.Value.String;

			return null;
		}

		/// <summary>
		/// An RK operand: a constant (encoded as ~k) for a literal, otherwise a register.
		/// </summary>
		int Operand(Expression e)
		{
			LiteralExpression lit = e as LiteralExpression;

			if (lit != null)
				return ~Const(lit.Value);

			return Expr(e, -1);
		}

		/// <summary>
		/// A constant operand (~k) for a number literal, which the arithmetic opcodes read without a type
		/// check; any other expression in a register.
		/// </summary>
		int ArithmeticOperand(Expression e)
		{
			LiteralExpression lit = e as LiteralExpression;

			if (lit != null && lit.Value.Type == DataType.Number)
				return ~Const(lit.Value);

			return Expr(e, -1);
		}

		/// <summary>
		/// Compiles e and returns the register that holds its value; with dst >= 0 the value goes there.
		/// A local is returned as its own register (no copy) when dst is -1.
		/// </summary>
		int Expr(Expression e, int dst)
		{
			if (++m_Depth > MaxDepth)
				throw new Unsupported();

			int r = ExprCore(e, dst);
			m_Depth--;
			return r;
		}

		int ExprCore(Expression e, int dst)
		{
			if (e is LiteralExpression)
			{
				int r = dst >= 0 ? dst : AllocTemp();
				Emit(ROp.LoadK, r, Const(((LiteralExpression)e).Value), 0);
				return r;
			}

			if (e is SymbolRefExpression)
			{
				SymbolRefExpression s = (SymbolRefExpression)e;
				SymbolRef sym = s.m_Ref;

				if (sym == null || IsVarArgs(e))
					throw new Unsupported();

				switch (sym.i_Type)
				{
					case SymbolRefType.Local:
						{
							int reg = Reg(sym);

							if (dst < 0 || dst == reg)
								return reg;

							Emit(ROp.Move, dst, reg, 0);
							return dst;
						}
					case SymbolRefType.Upvalue:
						{
							int r = dst >= 0 ? dst : AllocTemp();
							Emit(ROp.GetUpval, r, sym.i_Index, 0);
							return r;
						}
					case SymbolRefType.Global:
						{
							SymbolRef env = sym.i_Env;
							int r = dst >= 0 ? dst : AllocTemp();
							int aux = FieldAux(sym.i_Name, OpCode.IndexN);

							if (env != null && env.i_Type == SymbolRefType.Upvalue)
								EmitSlow(ROp.GetTabUp, r, env.i_Index, aux);
							else if (env != null && env.i_Type == SymbolRefType.Local)
								EmitSlow(ROp.GetField, r, Reg(env), aux);
							else
								throw new Unsupported();

							return r;
						}
					default:
						throw new Unsupported();
				}
			}

			if (e is IndexExpression)
			{
				IndexExpression ie = (IndexExpression)e;

				if (ie.m_IndexExp is ExprListExpression)
					throw new Unsupported();

				int save = m_Next;
				int obj = Expr(ie.m_BaseExp, -1);
				string name = ConstantName(ie);
				int key = name == null ? Operand(ie.m_IndexExp) : 0;
				m_Next = save;
				int r = dst >= 0 ? dst : AllocTemp();

				if (name != null)
					EmitSlow(ROp.GetField, r, obj, FieldAux(name, OpCode.IndexN));
				else
					EmitSlow(ROp.GetTable, r, obj, key);

				return r;
			}

			if (e is BinaryOperatorExpression)
			{
				BinaryOperatorExpression b = (BinaryOperatorExpression)e;

				if (IsComparison(b.m_Operator))
				{
					// A comparison as a value: branch to load true or false.
					int r = dst >= 0 ? dst : AllocTemp();
					var whenFalse = new List<int>();
					Condition(b, false, whenFalse);
					Emit(ROp.LoadK, r, Const(DynValue.True), 0);
					int end = Emit(ROp.Jmp, -1, 0, 0);

					foreach (int j in whenFalse)
						Patch(j, Here);

					Emit(ROp.LoadK, r, Const(DynValue.False), 0);
					Patch(end, Here);
					return r;
				}

				int op;

				switch (b.m_Operator)
				{
					case BinaryOperatorExpression.Operator.Add: op = 0; break;
					case BinaryOperatorExpression.Operator.Sub: op = 1; break;
					case BinaryOperatorExpression.Operator.Mul: op = 2; break;
					case BinaryOperatorExpression.Operator.Div: op = 3; break;
					case BinaryOperatorExpression.Operator.Mod: op = 4; break;
					case BinaryOperatorExpression.Operator.Power: op = 5; break;
					default: throw new Unsupported(); // and, or, ..
				}

				int save = m_Next;
				int l = ArithmeticOperand(b.m_Exp1);
				int rr = ArithmeticOperand(b.m_Exp2);

				// Two constants: the left one goes to a register.
				if (l < 0 && rr < 0)
				{
					int t = AllocTemp();
					Emit(ROp.LoadK, t, ~l, 0);
					l = t;
				}

				m_Next = save;
				int res = dst >= 0 ? dst : AllocTemp();

				if (rr < 0)
					EmitSlow((ROp)((int)ROp.AddRK + op), res, l, ~rr);
				else if (l < 0)
					EmitSlow((ROp)((int)ROp.AddKR + op), res, ~l, rr);
				else
					EmitSlow((ROp)((int)ROp.AddRR + op), res, l, rr);

				return res;
			}

			if (e is UnaryOperatorExpression)
			{
				UnaryOperatorExpression u = (UnaryOperatorExpression)e;

				if (u.m_OpText != "-" && u.m_OpText != "not")
					throw new Unsupported();

				int save = m_Next;
				int v = Expr(u.m_Exp, -1);
				m_Next = save;
				int r = dst >= 0 ? dst : AllocTemp();

				if (u.m_OpText == "-")
					EmitSlow(ROp.Unm, r, v, 0);
				else
					Emit(ROp.Not, r, v, 0);

				return r;
			}

			if (e is AdjustmentExpression)
				return Expr(((AdjustmentExpression)e).expression, dst);

			if (e is FunctionCallExpression)
			{
				int r = Call((FunctionCallExpression)e, true, dst);

				if (dst >= 0 && dst != r)
				{
					Emit(ROp.Move, dst, r, 0);
					return dst;
				}

				return r;
			}

			throw new Unsupported();
		}

		/// <summary>
		/// Calls with the function and its arguments in consecutive registers on top of the temporaries;
		/// the one result (scalar) lands in the function's register, which stays allocated.
		/// </summary>
		int Call(FunctionCallExpression fc, bool scalar, int dst)
		{
			return CallInto(fc, scalar ? ROp.CallS : ROp.CallD, dst);
		}

		int CallInto(FunctionCallExpression fc, ROp op, int dst)
		{
			// Method calls (o:m()) are not in the subset.
			if (fc.m_Name != null)
				throw new Unsupported();

			List<Expression> args = fc.m_Arguments;

			if (args.Count > 0 && (args[args.Count - 1] is FunctionCallExpression || IsVarArgs(args[args.Count - 1])))
				throw new Unsupported();

			// The result can go straight into dst when dst is the newest temporary.
			int fn = (dst >= m_NumLocals && dst == m_Next - 1) ? dst : AllocTemp();
			Expr(fc.m_Function, fn);

			foreach (Expression a in args)
				Expr(a, AllocTemp());

			m_Aux.Add(new Instruction(m_Ref) { OpCode = OpCode.Call, Name = fc.m_DebugErr });
			EmitSlow(op, fn, args.Count, m_Aux.Count - 1);

			m_Next = fn + 1;
			return fn;
		}
	}
}
