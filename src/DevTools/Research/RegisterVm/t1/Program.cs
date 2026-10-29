using System; using System.IO; using System.Linq; using System.Reflection; using MoonSharp.Interpreter;
static class P {
  static readonly (string name, int n, string src)[] W = {
    ("empty_loop", 4000000, "return function(n) for i = 1, n do end end"),
    ("int_math", 2000000, "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end"),
    ("float_math", 2000000, "return function(n) local x, y = 0.5, 0.25 for i = 1, n do x = x * 0.5 + y * 0.25 y = y * 0.5 + x / 3.0 + 0.001 end return string.format('%.6f', x + y) end"),
    ("function_call", 1000000, "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end"),
    ("fib_recursive", 2000, "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end"),
  };
  static void Main(string[] a) {
    if (a.Length > 0 && a[0] == "dump") { Dump(a[1]); return; }
    if (a.Length > 0 && a[0] == "alloc") { Alloc(); return; }
    if (a.Length > 0 && a[0] == "coverage") { Coverage(a.Skip(1).ToArray()); return; }
    Console.WriteLine("case\tstack instr/iter\tregister instr/iter");
    foreach (var w in W) {
      double[] per = new double[2];
      for (int k = 0; k < 2; k++) {
        var s = new Script(); s.Options.RegisterVm = k == 1;
        long calls = 0; s.Options.InterruptHandler = e => { calls++; return false; };
        var fn = s.DoString(w.src); s.Call(fn, w.n);
        per[k] = calls * 10000.0 / w.n;
      }
      Console.WriteLine($"{w.name}\t{per[0]:0.0}\t{per[1]:0.0}");
    }
    foreach (var (name, src, execs) in new[] { ("numeric-loop", "local s=0; for i=1,20000 do s=s+i end; return s", 20), ("nbody", MoonSharp.Benchmarks.LuaCSharpScripts.NBody.Replace("or 1000", "or 100") + "\nreturn energy(bodies,nbody)", 20) }) {
      double[] per = new double[2];
      for (int k = 0; k < 2; k++) {
        var s = new Script(CoreModules.Preset_Complete); s.Options.RegisterVm = k == 1;
        long calls = 0; s.Options.InterruptHandler = e => { calls++; return false; };
        var fn = s.LoadString(src); for (int i = 0; i < execs; i++) s.Call(fn);
        per[k] = calls * 10000.0 / execs;
      }
      Console.WriteLine($"{name}\t{per[0]:0}\t{per[1]:0}\t(per execution)");
    }
  }
  static void Alloc() {
    foreach (var (name, src) in new[] { ("numeric-loop", "local s=0; for i=1,20000 do s=s+i end; return s"), ("nbody", MoonSharp.Benchmarks.LuaCSharpScripts.NBody.Replace("or 1000", "or 100") + "\nreturn energy(bodies,nbody)") })
      for (int k = 0; k < 2; k++) {
        var s = new Script(CoreModules.Preset_Complete); s.Options.RegisterVm = k == 1;
        var fn = s.LoadString(src); for (int i = 0; i < 5; i++) s.Call(fn);
        long a0 = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 20; i++) s.Call(fn);
        Console.WriteLine($"{name}\t{(k == 1 ? "register" : "stack")}\t{(GC.GetAllocatedBytesForCurrentThread() - a0) / 20} B per execution");
      }
  }
  // Compiles every file (no run) and prints how many functions got register code and why the others did not.
  static void Coverage(string[] files) {
    var rc = typeof(Script).Assembly.GetType("MoonSharp.Interpreter.Tree.RegisterCompiler");
    var compiled = rc.GetField("s_Compiled", BindingFlags.NonPublic | BindingFlags.Static);
    var fallbacks = (System.Collections.Generic.Dictionary<string, int>)rc.GetField("s_Fallbacks", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    int failed = 0;
    foreach (var f in files) {
      try { new Script(CoreModules.Preset_Complete).LoadString(File.ReadAllText(f), null, Path.GetFileName(f)); }
      catch (InterpreterException) { failed++; }
    }
    int c = (int)compiled.GetValue(null), fb = fallbacks.Values.Sum();
    Console.WriteLine($"files {files.Length} (syntax errors {failed}); functions {c + fb}: register {c} ({100.0 * c / (c + fb):0}%), stack only {fb}");
    foreach (var kv in fallbacks.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Value,5}  {kv.Key}");
  }
  // Prints the register code of the first function the source returns.
  static void Dump(string src) {
    var s = new Script(); var fn = s.DoString(src); s.Call(fn, 1);
    var closure = fn.Function;
    var reg = typeof(Closure).GetField("Register", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(closure);
    if (reg == null) { Console.WriteLine("(stack only)"); return; }
    var code = (Array)reg.GetType().GetField("Code").GetValue(reg);
    foreach (var ins in code) {
      var t = ins.GetType();
      Console.WriteLine($"{t.GetField("Op").GetValue(ins),-10} {t.GetField("A").GetValue(ins),4} {t.GetField("B").GetValue(ins),4} {t.GetField("C").GetValue(ins),4}");
    }
  }
}
