using System;
using MoonSharp.Interpreter;

static class Program
{
	// One script through a default Script, which registers the standard library, and one library
	// call, so the trimmer keeps what a game that runs scripts keeps. size.sh checks the output.
	static void Main()
	{
		Console.WriteLine(Script.RunString("return string.rep('ab', 2) .. 6 * 7").String);
	}
}
