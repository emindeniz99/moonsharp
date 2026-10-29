using System.Collections.Generic;

using MoonSharp.Interpreter.Execution;


namespace MoonSharp.Interpreter.Tree.Statements
{
	class CompositeStatement : Statement 
	{
		List<Statement> m_Statements = new List<Statement>();

		public CompositeStatement(ScriptLoadingContext lcontext)
			: base(lcontext)
		{
			// Every nested block passes here.
			lcontext.EnterSyntaxLevel();

			while (true)
			{
				Token t = lcontext.Lexer.Current;
				if (t.IsEndOfBlock()) break;

				bool forceLast;
				
				Statement s = Statement.CreateStatement(lcontext, out forceLast);
				m_Statements.Add(s);

				if (forceLast) break;
			}

			// eat away all superfluos ';'s
			while (lcontext.Lexer.Current.Type == TokenType.SemiColon)
				lcontext.Lexer.Next();

			// A label followed only by void statements (labels and ';') up to the end of its block is outside the
			// scope of the block's locals: Lua 5.2 3.5, a local's scope "lasts until the last non-void statement
			// of the innermost block" (lparser.c, labelstat). So "goto continue" can jump over a local to
			// "::continue:: end". Not before 'until', whose condition still sees the locals.
			if (lcontext.Lexer.Current.Type != TokenType.Until)
			{
				for (int i = m_Statements.Count - 1; i >= 0; i--)
				{
					LabelStatement label = m_Statements[i] as LabelStatement;

					if (label != null)
						label.SetDefinedVars(0, null);
					else if (!(m_Statements[i] is EmptyStatement))
						break;
				}
			}

			lcontext.SyntaxDepth--;
		}


		public override void Compile(Execution.VM.ByteCode bc)
		{
			if (m_Statements != null)
			{
				foreach (Statement s in m_Statements)
				{
					s.Compile(bc);
				}
			}
		}
	}
}
