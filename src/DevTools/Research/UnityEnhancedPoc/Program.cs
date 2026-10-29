using System;
using System.Diagnostics;
using MoonSharp.Interpreter;

// Measurements for docs/research/unityenhanced.md, section 4, against upstream fbd0367 compiled from source,
// with or without poc.patch (experiment A). Usage: UeProbe <bench|sem|startup|voidchk>
static class P
{
	static int Main(string[] args)
	{
		switch (args.Length > 0 ? args[0] : "")
		{
			case "bench": Bench(); return 0;
			case "sem": Sem(); return 0;
			case "startup": Startup(); return 0;
			case "voidchk": VoidCheck(); return 0;
			default: Console.Error.WriteLine("usage: UeProbe <bench|sem|startup|voidchk>"); return 2;
		}
	}

	// Experiment A table: best of 7 calls of 1,000,000 iterations, ns and bytes per iteration.
	static void Run(string name, string code, int n)
	{
		var s = new Script(CoreModules.Preset_Default);
		var f = s.LoadString(code);
		s.Call(f, 1000); // warm
		var best = double.MaxValue; long bytes = 0; DynValue r = null;
		for (int k = 0; k < 7; k++)
		{
			long a0 = GC.GetAllocatedBytesForCurrentThread(); var sw = Stopwatch.StartNew();
			r = s.Call(f, n); sw.Stop();
			long a1 = GC.GetAllocatedBytesForCurrentThread();
			best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / n); bytes = (a1 - a0) / n;
		}
		Console.WriteLine($"{name,-22} {best,8:F1} ns/iter {bytes,6} B/iter result={r}");
	}

	static void Bench()
	{
		Run("sum_loop", "local n=... local s=0 for i=1,n do s = s + i % 7 end return s", 1000000);
		Run("local_read", "local n=... local a=3 local s=0 for i=1,n do s = a end return s", 1000000);
		Run("closure_upvalue", "local n=... local c=0 local function inc() c = c + 1 end for i=1,n do inc() end return c", 1000000);
		Run("fib", "local n=... local function fib(x) if x < 2 then return x end return fib(x-1)+fib(x-2) end local s=0 for i=1,n/10000 do s = s + fib(15) end return s", 1000000);
	}

	// The eight-case semantics check; both builds must print the same lines.
	static void Sem()
	{
		string[] tests = {
			"local a=1 local t={a} a=2 return t[1]..','..a",
			"local fs={} for i=1,3 do fs[i]=function() return i end end return fs[1]()..fs[2]()..fs[3]()",
			"local x=0 local function inc() x=x+1 end local function get() return x end inc() inc() return get()..','..x",
			"local function mk() local c=0 return function() c=c+1 return c end end local f,g=mk(),mk() f() f() return f()..','..g()",
			"local a=5 local b=a a=a+1 return a..','..b",
			"local function f(p) local g=function() p=p+1 end g() return p end return f(10)",
			"local t={} for i=1,3 do local j=i t[i]=function() j=j*10 return j end end return t[1]()..t[1]()..t[2]()",
			"local function va(...) local a,b = ... return a+b end return va(3,4)",
		};
		foreach (var t in tests) Console.WriteLine(Script.RunString(t).ToPrintString());
	}

	// Script creation cost per core module preset: best of 9 batches of 200.
	static void M(string name, Func<Script> mk)
	{
		for (int i = 0; i < 200; i++) mk();
		double best = double.MaxValue; long bytes = 0;
		for (int r = 0; r < 9; r++)
		{
			long a0 = GC.GetAllocatedBytesForCurrentThread(); var sw = Stopwatch.StartNew();
			for (int i = 0; i < 200; i++) mk();
			sw.Stop(); long a1 = GC.GetAllocatedBytesForCurrentThread();
			best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000 / 200); bytes = (a1 - a0) / 200;
		}
		Console.WriteLine($"{name,-34} {best,9:F1} us {bytes,9} B");
	}

	static void Startup()
	{
		M("new Script(None)", () => new Script(CoreModules.None));
		M("new Script(Basic)", () => new Script(CoreModules.Basic));
		M("new Script(LoadMethods) [Lua require]", () => new Script(CoreModules.LoadMethods));
		M("new Script(Preset_SoftSandbox)", () => new Script(CoreModules.Preset_SoftSandbox));
		M("new Script(Preset_Default)", () => new Script(CoreModules.Preset_Default));
	}

	// What a Lua function without a return statement gives to Script.Call, and what Table.Set stores.
	static void VoidCheck()
	{
		var s = new Script();
		var f = s.DoString("return function(x) local y = x end");
		var r = s.Call(f, "m");
		Console.WriteLine($"type={r.Type} isNilSingleton={(object)r == (object)DynValue.Nil} isVoidSingleton={(object)r == (object)DynValue.Void}");
		var t = new Table(s); t.Set("k", r); Console.WriteLine($"stored={t.Get("k").Type} rawNull={t.RawGet("k") == null}");
	}
}
