using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Tree;

namespace MoonSharp.Interpreter.Execution
{
	class ScriptLoadingContext
	{
		public Script Script { get; private set; }
		public BuildTimeScope Scope { get; set; }
		public SourceCode Source { get; set; }
		public bool Anonymous { get; set; }
		public bool IsDynamicExpression { get; set; }
		public Lexer Lexer { get; set; }

		// Nesting of the expressions and blocks being parsed. The caller decrements it on the way out;
		// after an error the context is dropped, so nothing needs to.
		public int SyntaxDepth;

		public void EnterSyntaxLevel()
		{
			if (++SyntaxDepth > VM.Processor.MAX_NATIVE_NESTING)
				throw new SyntaxErrorException(Lexer.Current, "chunk has too many syntax levels");
		}

		public ScriptLoadingContext(Script s)
		{
			Script = s;
		}

	}
}
