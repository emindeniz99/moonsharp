using System;
using System.IO;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

static class P
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: msrun <file.lua> [args...]");
            return 64;
        }
        Script.DefaultOptions.ScriptLoader = new FileSystemScriptLoader();
        var s = new Script(CoreModules.Preset_Complete);
        var argt = new Table(s);
        argt[0] = args[0];
        for (int i = 1; i < args.Length; i++) argt[i] = args[i];
        s.Globals["arg"] = argt;
        try
        {
            s.DoFile(args[0]);
            Console.Out.Flush();
            return 0;
        }
        catch (InterpreterException ex)
        {
            Console.Out.Flush();
            Console.Error.WriteLine("MSERR " + ex.GetType().Name + ": " + (ex.DecoratedMessage ?? ex.Message));
            return 1;
        }
        catch (Exception ex)
        {
            Console.Out.Flush();
            Console.Error.WriteLine("MSCRASH " + ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }
}
