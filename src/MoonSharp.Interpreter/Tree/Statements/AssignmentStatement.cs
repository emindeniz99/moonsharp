using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Execution;

using MoonSharp.Interpreter.Tree.Expressions;

namespace MoonSharp.Interpreter.Tree.Statements
{
	class AssignmentStatement : Statement
	{
		List<IVariable> m_LValues = new List<IVariable>();
		List<Expression> m_RValues;
		SourceRef m_Ref;


		public AssignmentStatement(ScriptLoadingContext lcontext, Token startToken)
			: base(lcontext)
		{
			List<string> names = new List<string>();

			Token first = startToken;

			while (true)
			{
				Token name = CheckTokenType(lcontext, TokenType.Name);
				names.Add(name.Text);

				if (lcontext.Lexer.Current.Type != TokenType.Comma)
					break;

				lcontext.Lexer.Next();
			}

			if (lcontext.Lexer.Current.Type == TokenType.Op_Assignment)
			{
				CheckTokenType(lcontext, TokenType.Op_Assignment);
				m_RValues = Expression.ExprList(lcontext);
			}
			else
			{
				// "local x" is "local x = nil". With no value at all, the stores read whatever happened to
				// be on top of the value stack (a for-in loop's iterator state, for example).
				m_RValues = new List<Expression> { new LiteralExpression(lcontext, DynValue.Nil) };
			}

			foreach (string name in names)
			{
				var localVar = lcontext.Scope.TryDefineLocal(name);
				var symbol = new SymbolRefExpression(lcontext, localVar);
				m_LValues.Add(symbol);
			}

			Token last = lcontext.Lexer.Current;
			m_Ref = first.GetSourceRefUpTo(last);
			lcontext.Source.Refs.Add(m_Ref);

		}


		public AssignmentStatement(ScriptLoadingContext lcontext, Expression firstExpression, Token first)
			: base(lcontext)
		{
			m_LValues.Add(CheckVar(lcontext, firstExpression));

			while (lcontext.Lexer.Current.Type == TokenType.Comma)
			{
				lcontext.Lexer.Next();
				Expression e = Expression.PrimaryExp(lcontext);
				m_LValues.Add(CheckVar(lcontext, e));
			}

			CheckTokenType(lcontext, TokenType.Op_Assignment);

			m_RValues = Expression.ExprList(lcontext);

			Token last = lcontext.Lexer.Current;
			m_Ref = first.GetSourceRefUpTo(last);
			lcontext.Source.Refs.Add(m_Ref);

		}

		private IVariable CheckVar(ScriptLoadingContext lcontext, Expression firstExpression)
		{
			IVariable v = firstExpression as IVariable;

			if (v == null)
				throw new SyntaxErrorException(lcontext.Lexer.Current, "unexpected symbol near '{0}' - not a l-value", lcontext.Lexer.Current);

			return v;
		}


		public override void Compile(Execution.VM.ByteCode bc)
		{
			using (bc.EnterSource(m_Ref))
			{
				if (m_LValues.Count > 1)
				{
					CompileMultiple(bc);
					return;
				}

				foreach (var exp in m_RValues)
				{
					exp.Compile(bc);
				}

				// An IndexSet going through a __newindex metamethod has to make room for the
				// value the metamethod call leaves on the stack, and it does that by popping
				// the top of it. Give it a scratch slot to eat, so it can't consume an r-value
				// a later assignment still needs. The call's return value lands back in that
				// same slot, so one scratch is enough however many targets are indexed.
				bool needsScratch = false;

				foreach (var lvalue in m_LValues)
				{
					if (lvalue is IndexExpression)
					{
						needsScratch = true;
						break;
					}
				}

				if (needsScratch)
					bc.Emit_Literal(DynValue.Nil);

				int scratchOfs = needsScratch ? 1 : 0;

				for (int i = 0; i < m_LValues.Count; i++)
					m_LValues[i].CompileAssignment(bc,
							Math.Max(m_RValues.Count - 1 - i, 0) + scratchOfs, // index of r-value
							i - Math.Min(i, m_RValues.Count - 1)); // index in last tuple

				bc.Emit_Pop(m_RValues.Count + scratchOfs);
			}
		}

		/// <summary>
		/// Several targets. Lua 5.2 (3.3.3): "The assignment statement first evaluates all its expressions and only
		/// then are the assignments performed", so in i, t[i] = i + 1, v the key is the old i. As in reference Lua,
		/// the tables and keys of the targets are evaluated first, left to right, then the values, and the stores
		/// run from the last target to the first (a, a = 1, 2 leaves 1).
		/// </summary>
		private void CompileMultiple(Execution.VM.ByteCode bc)
		{
			// Values each indexed target keeps on the stack: its table, and its key when that is computed.
			int[] slots = new int[m_LValues.Count];
			int saved = 0;

			for (int i = 0; i < m_LValues.Count; i++)
			{
				IndexExpression ie = m_LValues[i] as IndexExpression;
				if (ie != null)
					saved += slots[i] = ie.CompileTarget(bc);
			}

			foreach (var exp in m_RValues)
				exp.Compile(bc);

			// The scratch slot an IndexSet through __newindex eats (see Compile).
			int scratchOfs = saved > 0 ? 1 : 0;
			if (scratchOfs > 0)
				bc.Emit_Literal(DynValue.Nil);

			int block = m_RValues.Count + scratchOfs;
			int below = 0; // saved values of the targets after the current one

			for (int i = m_LValues.Count - 1; i >= 0; i--)
			{
				int stackofs = Math.Max(m_RValues.Count - 1 - i, 0) + scratchOfs;
				int tupleidx = i - Math.Min(i, m_RValues.Count - 1);
				IndexExpression ie = m_LValues[i] as IndexExpression;

				if (ie != null)
					ie.CompileAssignmentFromTarget(bc, block + below, stackofs, tupleidx);
				else
					m_LValues[i].CompileAssignment(bc, stackofs, tupleidx);

				below += slots[i];
			}

			bc.Emit_Pop(block + saved);
		}

	}
}
