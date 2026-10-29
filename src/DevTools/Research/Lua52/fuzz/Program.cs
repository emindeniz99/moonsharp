using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

// Calls every library function with combinations of odd arguments under pcall and reports
// each CLR exception that escapes to the host (one a script cannot catch).
static class P
{
    static readonly string[] Vals = {
        "nil", "true", "false", "0", "-0.0", "1", "-1", "0.5", "-2.5", "3", "255", "256", "2^31", "2^32", "2^53", "2^63",
        "-2^63", "1e15", "1e308", "1/0", "-1/0", "0/0", "''", "'a'", "'abc'", "'10'", "'%'", "'%d'", "'*l'", "'*n'",
        "'*a'", "'r'", "'w+'", "'set'", "'end'", "'cur'", "'x\\0y'", "{}", "{1,2,3}", "function() return 1, 2 end",
        "coroutine.create(function() end)", "F", "setmetatable({}, {__index = function() error('x') end})", "string.rep('a', 200)"
    };
    static readonly string[] Small = { "nil", "-1", "0", "1", "2^31", "1/0", "0/0", "'a'", "''", "{}", "'*l'", "F" };

    static int Main(string[] args)
    {
        string only = args.Length > 0 ? args[0] : null;
        Script.DefaultOptions.ScriptLoader = new FileSystemScriptLoader();
        var names = new List<string>();
        {
            var s = new Script(CoreModules.Preset_Complete);
            foreach (var lib in new[] { "_G", "string", "table", "math", "io", "os", "bit32", "coroutine", "debug", "json", "dynamic" })
            {
                var t = lib == "_G" ? s.Globals : s.Globals.Get(lib).Table;
                if (t == null) continue;
                foreach (var kv in t.Pairs)
                    if (kv.Value.Type == DataType.ClrFunction || kv.Value.Type == DataType.Function)
                        names.Add(lib == "_G" ? kv.Key.String : lib + "." + kv.Key.String);
            }
            foreach (var m in new[] { "read", "write", "lines", "seek", "close", "flush", "setvbuf" })
                names.Add("F:" + m);
        }
        var skip = new HashSet<string> { "os.exit", "os.execute", "os.remove", "os.rename", "debug.debug", "print", "io.write",
            "debug.setmetatable" };
        int found = 0;
        var seen = new HashSet<string>();
        foreach (string name in names)
        {
            if (only != null && name != only) continue;
            if (skip.Contains(name) && only == null) continue;
            string call = name.StartsWith("F:") ? "F." + name.Substring(2) : name;
            bool method = name.StartsWith("F:");
            var combos = new List<string>();
            foreach (var a in Vals) combos.Add(a);
            foreach (var a in Vals) foreach (var b in Vals) combos.Add(a + ", " + b);
            foreach (var a in Small) foreach (var b in Small) foreach (var c in Small) combos.Add(a + ", " + b + ", " + c);
            combos.Insert(0, "");
            if (name == "string.format" && only != null)
            {
                combos.Clear();
                var fl = new[] { "", "-", "+", " ", "#", "0", "-0", "0-", "+ ", "#0", "-#" };
                var wd = new[] { "", "1", "8", "99" };
                var pr = new[] { "", ".", ".0", ".1", ".3", ".99" };
                foreach (char cv in "cdiouxXeEfgGaAqs")
                    foreach (var f in fl) foreach (var w in wd) foreach (var p in pr)
                        foreach (var v in Vals)
                            combos.Add("'%" + f + w + p + cv + "', " + v);
            }
            Script s = null;
            for (int i = 0; i < combos.Count; i++)
            {
                if (s == null || i % 200 == 0)
                {
                    s = new Script(CoreModules.Preset_Complete);
                    s.Options.Stdin = new MemoryStream(new byte[] { 49, 10, 50 });
                    s.Options.Stdout = Stream.Null;
                    s.DoString("local w = io.open('fuzz.tmp', 'w') w:write('12 ab\\n3\\n') w:close() F = io.open('fuzz.tmp', 'r')");
                }
                string code = method
                    ? "return pcall(function(...) return F:" + name.Substring(2) + "(...) end" + (combos[i].Length > 0 ? ", " : "") + combos[i] + ")"
                    : "local f = " + call + " return pcall(f" + (combos[i].Length > 0 ? ", " : "") + combos[i] + ")";
                try
                {
                    var t0 = DateTime.UtcNow;
                    s.DoString(code);
                    if ((DateTime.UtcNow - t0).TotalSeconds > 2) Console.Error.WriteLine("SLOW " + code);
                }
                catch (InterpreterException) { }
                catch (Exception ex)
                {
                    string key = name + " " + ex.GetType().Name;
                    found++;
                    if (seen.Add(key) || only != null)
                        Console.WriteLine("CRASH\t" + name + "\t" + ex.GetType().Name + "\t" + combos[i] + "\t" + ex.Message.Replace('\n', ' '));
                    s = null;
                }
            }
        }
        Console.WriteLine("crashing calls: " + found);
        return 0;
    }
}
