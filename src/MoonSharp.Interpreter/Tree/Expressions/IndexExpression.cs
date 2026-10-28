using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;

namespace MoonSharp.Interpreter.Tree.Expressions
{
	class IndexExpression : Expression, IVariable
	{
		Expression m_BaseExp;
		Expression m_IndexExp;
		string m_Name;


		public IndexExpression(Expression baseExp, Expression indexExp, ScriptLoadingContext lcontext)
			: base(lcontext)
		{
			m_BaseExp = baseExp;
			m_IndexExp = indexExp;
		}

		public IndexExpression(Expression baseExp, string name, ScriptLoadingContext lcontext)
			: base(lcontext)
		{
			m_BaseExp = baseExp;
			m_Name = name;
		}


		internal override Expression SuffixBase
		{
			get { return m_BaseExp; }
		}

		public override void Compile(ByteCode bc)
		{
			CompileSuffixBase(bc, m_BaseExp);
			CompileSuffix(bc);
		}

		internal override void CompileSuffix(ByteCode bc)
		{
			if (m_Name != null)
			{
				bc.Emit_Index(DynValue.NewString(m_Name), true);
			}
			else if (m_IndexExp is LiteralExpression)
			{
				LiteralExpression lit = (LiteralExpression)m_IndexExp;
				bc.Emit_Index(lit.Value);
			}
			else
			{
				m_IndexExp.Compile(bc);
				bc.Emit_Index(isExpList: (m_IndexExp is ExprListExpression));
			}
		}

		public void CompileAssignment(ByteCode bc, int stackofs, int tupleidx)
		{
			CompileSuffixBase(bc, m_BaseExp);

			if (m_Name != null)
			{
				bc.Emit_IndexSet(stackofs, tupleidx, DynValue.NewString(m_Name), isNameIndex: true);
			}
			else if (m_IndexExp is LiteralExpression)
			{
				LiteralExpression lit = (LiteralExpression)m_IndexExp;
				bc.Emit_IndexSet(stackofs, tupleidx, lit.Value);
			}
			else
			{
				m_IndexExp.Compile(bc);
				bc.Emit_IndexSet(stackofs, tupleidx, isExpList: (m_IndexExp is ExprListExpression));
			}
		}

		public override DynValue Eval(ScriptExecutionContext context)
		{
			// Like CompileSuffixBase: innermost first, then each index in a loop.
			var chain = new System.Collections.Generic.Stack<IndexExpression>();
			Expression e = this;

			for (var ie = e as IndexExpression; ie != null; ie = e as IndexExpression)
			{
				chain.Push(ie);
				e = ie.m_BaseExp;
			}

			DynValue b = e.Eval(context).ToScalar();

			while (chain.Count > 0)
				b = chain.Pop().EvalIndex(context, b);

			return b;
		}

		private DynValue EvalIndex(ScriptExecutionContext context, DynValue b)
		{
			DynValue i = m_IndexExp != null ? m_IndexExp.Eval(context).ToScalar() : DynValue.NewString(m_Name);

			if (b.Type != DataType.Table) throw new DynamicExpressionException("Attempt to index non-table.");
			else if (i.IsNilOrNan()) throw new DynamicExpressionException("Attempt to index with nil or nan key.");
			return b.Table.Get(i) ?? DynValue.Nil;
		}
	}
}
