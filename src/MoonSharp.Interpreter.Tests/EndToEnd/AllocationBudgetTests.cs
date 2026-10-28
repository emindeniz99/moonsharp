using System;
using System.Reflection;
using MoonSharp.Interpreter.Interop;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// Allocation budgets for VM workloads, in bytes per loop iteration. Allocation is what makes MoonSharp
	/// slow on Unity's Boehm collector, and unlike time it is exact and repeatable, so a change that adds an
	/// allocation to a hot path fails here instead of hiding in benchmark noise. A change that removes
	/// allocations lowers the budget in the same commit, so the gain cannot silently come back.
	///
	/// Measured with GC.GetAllocatedBytesForCurrentThread on 64-bit .NET 8 or later; other runtimes (Mono,
	/// IL2CPP, .NET Framework) skip, because object sizes and runtime allocations differ there. Each workload
	/// runs n and 2n iterations and the difference of the two counts is divided by n, which cancels the fixed
	/// cost of the call itself.
	///
	/// The workloads are the ones in src/DevTools/Benchmarks/LuaWorkloads.cs. The idea of pinning allocation
	/// counts in unit tests comes from Jint:
	/// https://github.com/sebastienros/jint/blob/9ec2352c222dff6b7bff806d16b087cf0f1e6384/Jint.Tests/Runtime/SlotLocationCacheTests.cs#L250-L267
	/// </summary>
	[TestFixture]
	public class AllocationBudgetTests
	{
		const int N = 4000;

		static readonly Func<long> s_AllocatedBytes = FindAllocatedBytesCounter();

		static Func<long> FindAllocatedBytesCounter()
		{
			// Not in the netstandard2.0 surface, so bind it at run time.
			MethodInfo mi = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

			if (mi == null)
				return null;

			return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), mi);
		}

		static void SkipUnlessMeasurable()
		{
			bool coreClr64 = Environment.Version.Major >= 8 && IntPtr.Size == 8 && Type.GetType("Mono.Runtime") == null;

			if (s_AllocatedBytes == null || !coreClr64)
			{
				TestRunner.Skip();
				Assert.Ignore("allocation budgets are measured on 64-bit .NET 8 or later");
			}
		}

		/// <summary>
		/// Bytes allocated per iteration by run(n), without the fixed per-call cost.
		/// </summary>
		internal static double BytesPerIteration(Action<long> run, long n)
		{
			run(n); // warm-up: JIT, lazy caches, stack growth
			run(2 * n);

			long a = s_AllocatedBytes();
			run(n);
			long b = s_AllocatedBytes();
			run(2 * n);
			long c = s_AllocatedBytes();

			return ((c - b) - (b - a)) / (double)n;
		}

		static Action<long> LuaLoop(Script script, string source)
		{
			DynValue fn = script.DoString(source);
			return n => script.Call(fn, DynValue.NewNumber(n));
		}

		static void AssertBudget(string workload, Action<long> run, long n, double budget)
		{
			double measured = BytesPerIteration(run, n);

			// MOONSHARP_ALLOC_REPORT=1 prints every measurement, to set budgets after a change.
			if (Environment.GetEnvironmentVariable("MOONSHARP_ALLOC_REPORT") != null)
				Console.WriteLine("ALLOC {0}: {1:0.0} bytes per iteration, budget {2:0.0}", workload, measured, budget);

			Assert.IsTrue(measured <= budget, string.Format(
				"{0}: {1:0.0} bytes per iteration, budget {2:0.0}. If the increase is intended, raise the budget and say why in the commit.",
				workload, measured, budget));
		}

		static void AssertLuaBudget(string workload, string source, double budget, long n = N)
		{
			SkipUnlessMeasurable();
			AssertBudget(workload, LuaLoop(new Script(), source), n, budget);
		}

		public class Calc
		{
			public int Value { get; set; } = 1;
			public int Add(int a, int b) { return a + b; }
		}

		// ---------------------------------------------------------------------------------------------
		// The harness itself

		[Test]
		public void AllocationBudget_HarnessSeesOneExtraAllocation()
		{
			// The same Lua -> C# loop twice; the second callback allocates one more object per call.
			// The harness must report exactly that object's size, and the budget of the plain loop
			// must reject the second one.
			SkipUnlessMeasurable();

			const string loop = "local f = f\nreturn function(n) local s = 0 for i = 1, n do s = s + f(i) end return s end";

			Script plain = new Script();
			plain.Globals["f"] = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(args[0].Number + 1));

			object keep = null;
			Script extra = new Script();
			extra.Globals["f"] = DynValue.NewCallback((ctx, args) => { keep = new object(); return DynValue.NewNumber(args[0].Number + 1); });

			double basePerIter = BytesPerIteration(LuaLoop(plain, loop), N);
			double extraPerIter = BytesPerIteration(LuaLoop(extra, loop), N);

			Assert.AreEqual(IntPtr.Size * 3, extraPerIter - basePerIter, "one System.Object per iteration");
			Assert.IsNotNull(keep);
			Assert.Throws<AssertionException>(() => AssertBudget("lua_to_cs_call1 + object", LuaLoop(extra, loop), N, basePerIter));
		}

		// ---------------------------------------------------------------------------------------------
		// Budgets. Lower a row when a change removes allocations from that workload.

		// For loops that allocate nothing per iteration. Not 0: a one-off allocation in the measured
		// calls (a stack growing, a cache filling) shows as a fraction of a byte; any per-iteration
		// allocation is at least 24 bytes.
		const double NoAllocation = 1;

		[Test]
		public void AllocationBudget_EmptyLoop()
		{
			AssertLuaBudget("empty_loop", "return function(n) for i = 1, n do end end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_FunctionCall()
		{
			AssertLuaBudget("function_call", "local function f(a) return a + 1 end\nreturn function(n) local x = 0 for i = 1, n do x = f(x) end return x end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_MethodCall()
		{
			AssertLuaBudget("method_call", "local C = {} C.__index = C\nfunction C:inc() self.v = self.v + 1 end\nlocal o = setmetatable({ v = 0 }, C)\nreturn function(n) for i = 1, n do o:inc() end return o.v end", 40);
		}

		[Test]
		public void AllocationBudget_TableArrayRead()
		{
			AssertLuaBudget("table_array_read", "local t = {} for i = 1, 1024 do t[i] = i end\nreturn function(n) local s, j = 0, 1 for i = 1, n do s = s + t[j] j = j + 1 if j > 1024 then j = 1 end end return s end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_TableHashSetGet()
		{
			AssertLuaBudget("table_hash_set_get", "local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\nreturn function(n) local t, s, j = {}, 0, 1 for i = 1, n do local k = keys[j] t[k] = i s = s + t[k] j = j + 1 if j > 64 then j = 1 end end return s end", 40);
		}

		[Test]
		public void AllocationBudget_IntMath()
		{
			AssertLuaBudget("int_math", "return function(n) local x = 1 for i = 1, n do x = (x * 31 + i) % 1000003 end return x end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_FloatMath()
		{
			AssertLuaBudget("float_math", "return function(n) local x, y = 0.5, 0.25 for i = 1, n do x = x * 0.5 + y * 0.25 y = y * 0.5 + x / 3.0 + 0.001 end return x + y end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_ClosureCreateCall()
		{
			AssertLuaBudget("closure_create_call", "return function(n) local s = 0 for i = 1, n do local f = function() return i end s = s + f() end return s end", 152);
		}

		[Test]
		public void AllocationBudget_FibRecursive()
		{
			// One iteration is fib(12): 465 Lua calls.
			AssertLuaBudget("fib_recursive", "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end", NoAllocation, 40);
		}

		[Test]
		public void AllocationBudget_CallsVarArgs()
		{
			// Lua-CSharp's calls-varargs case. '...' stays on the value stack where the caller pushed it: no
			// argument list, no array and no tuple per call (248 bytes before).
			AssertLuaBudget("calls_varargs", "local function f(a, ...) local b, c = ... return a + b + c end\nreturn function(n) local s = 0 for i = 1, n do s = s + f(i, 2, 3) end return s end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_VarArgsForward()
		{
			// '...' passed on to another vararg function, to select('#') and to a library function, and
			// counted: the call copies the values from where they are (808 bytes before).
			AssertLuaBudget("varargs_forward", "local function g(...) return select('#', ...) + math.max(...) end\nlocal function f(...) return g(...) end\nreturn function(n) local s = 0 for i = 1, n do s = s + f(1, 2, 3) end return s end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_ReturnVarArgs()
		{
			// return ... with one value returns it as it is (184 bytes before: the '...' tuple, and a DynValue
			// for the value once it lived on the stack).
			AssertLuaBudget("return_varargs", "local function e(...) return ... end\nreturn function(n) local s = 0 for i = 1, n do s = s + e(i) end return s end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_CoroutineSwitch()
		{
			// Each coroutine reuses its yield request, a resume with no values pushes nil without a DynValue,
			// and an empty argument list is a shared array (400 bytes before). The two calls' contexts and
			// argument views are reused (248 bytes before), the yielded values wait in a buffer the coroutine
			// keeps, and coroutine.wrap hands one value to its caller through its context (72 bytes before).
			AssertLuaBudget("coroutine_switch", "return function(n) local co = coroutine.wrap(function() local i = 0 while true do i = i + 1 coroutine.yield(i) end end)\nlocal s = 0 for i = 1, n do s = s + co() end return s end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_StringFormat()
		{
			// The library reads its arguments in place: no argument array and no DynValue per string or
			// number argument (368 bytes before), through a context and an argument view the processor
			// reuses (216 bytes before). What is left: the result string and its DynValue, and the DynValue
			// the table stores.
			AssertLuaBudget("string_format", "local format = string.format\nreturn function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = format('%d:%s', i, 'x') j = j + 1 if j > 64 then j = 1 end end return buf[64] end", 120);
		}

		[Test]
		public void AllocationBudget_StringPattern()
		{
			// The string_pattern workload: find with two captures, gsub of one character, match at the end.
			// 48,840 bytes with the KopiLua matcher, 968 with the calls' own contexts and argument views; what
			// is left is the captures, results and their DynValues, and gsub's result string.
			AssertLuaBudget("string_pattern", "local s = 'the quick brown fox 123 jumps over the lazy dog 4567'\nreturn function(n) local c = 0 for i = 1, n do local a, b = s:find('(%a+) (%d+)') local r, k = s:gsub('o', '0') c = c + a + k + #s:match('%d+$') end return c end", 680);
		}

		[Test]
		public void AllocationBudget_GsubCapture()
		{
			// Lua-CSharp's "patterns" case: gsub with a capture and a %1 replacement. 7,528 bytes with the
			// KopiLua matcher, 328 before the call's context and argument view were reused.
			AssertLuaBudget("gsub_capture", "local gsub = string.gsub\nreturn function(n) local c = 0 for i = 1, n do c = c + #gsub('hello 123 world 456', '(%a+)', '[%1]') end return c end", 232);
		}

		[Test]
		public void AllocationBudget_BuiltinCalls()
		{
			// Fast-called library functions run on the value stack: no frame, context or argument list.
			AssertLuaBudget("builtin_calls", "local floor, len, byte, max = math.floor, string.len, string.byte, math.max\nreturn function(n) local s = 0 for i = 1, n do s = s + floor(i / 3) + len('abc') + byte('xyz', 2) + max(i, 5) + select('#', i, i) + (type(s) == 'number' and 1 or 0) end return s end", NoAllocation);
		}

		[Test]
		public void AllocationBudget_LuaToCsCall1()
		{
			// The context is reused (192 bytes before). Left: the argument list a host callback may keep (an
			// array and its CallbackArguments), the DynValue of i and the result's DynValue.
			SkipUnlessMeasurable();
			Script script = new Script();
			script.Globals["f"] = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(args[0].Number + 1));
			AssertBudget("lua_to_cs_call1", LuaLoop(script, "local f = f\nreturn function(n) local s = 0 for i = 1, n do s = s + f(i) end return s end"), N, 144);
		}

		[Test]
		public void AllocationBudget_LuaToCsCall1InPlace()
		{
			// A host callback that uses its arguments only during the call reads them in place through a
			// reused view. Left: the DynValue args[0] creates for i and the result's DynValue.
			SkipUnlessMeasurable();
			Script script = new Script();
			script.Globals["f"] = new CallbackFunction((ctx, args) => DynValue.NewNumber(args[0].Number + 1)) { UsesArgumentsOnlyDuringCall = true };
			AssertBudget("lua_to_cs_call1_in_place", LuaLoop(script, "local f = f\nreturn function(n) local s = 0 for i = 1, n do s = s + f(i) end return s end"), N, 80);
		}

		[Test]
		public void AllocationBudget_HostCallsWithoutDynValues()
		{
			// Lua-CSharp's host-calls case written against the allocation-free API: arguments read in place
			// with NumberAt and the result returned with ScriptExecutionContext.Return (240 bytes with
			// DynValue.NewCallback, args[i].Number and DynValue.NewNumber before this branch).
			SkipUnlessMeasurable();
			Script script = new Script();
			script.Globals["add"] = new CallbackFunction((ctx, args) => ctx.Return(args.NumberAt(0) + args.NumberAt(1))) { UsesArgumentsOnlyDuringCall = true };
			AssertBudget("host_calls_no_dynvalues", LuaLoop(script, "local add = add\nreturn function(n) local s = 0 for i = 1, n do s = add(s, i) end return s end"), N, NoAllocation);
		}

		[Test]
		public void AllocationBudget_CsToLuaCall3()
		{
			// Left: the host's params array, the host's DynValue for i and the result's DynValue. The call's
			// calling location is shared and the arguments are pushed without an adjusted copy (224 before).
			SkipUnlessMeasurable();
			Script script = new Script();
			DynValue fn = script.DoString("return function(a, b, c) return a * b + c end");
			DynValue two = DynValue.NewNumber(2), three = DynValue.NewNumber(3);

			AssertBudget("cs_to_lua_call3", n =>
			{
				for (long i = 0; i < n; i++)
					script.Call(fn, DynValue.NewNumber(i), two, three);
			}, N, 128);
		}

		[Test]
		public void AllocationBudget_CsToLuaCall3Typed()
		{
			// The typed call pushes its arguments and reads the result as VM values: no boxes, no argument
			// array, no DynValue per argument or for the result.
			SkipUnlessMeasurable();
			Script script = new Script();
			Closure fn = script.DoString("return function(a, b, c) return a * b + c end").Function;

			AssertBudget("cs_to_lua_call3_typed", n =>
			{
				for (long i = 0; i < n; i++)
					fn.Call<double, double, double, double>(i, 2, 3);
			}, N, NoAllocation);
		}

		[Test]
		public void AllocationBudget_ClrInteropGenerated()
		{
			// The source generator's descriptors (the IL2CPP path) read their arguments in place
			// (CallbackArguments.TypeAt, NumberAt) and give a number or string result through
			// ScriptExecutionContext.Return: no allocation for a method call (248 bytes per call before this
			// branch, docs/source-generator.md).
			SkipUnlessMeasurable();

			using (new RegistrySnapshot(typeof(InteropParityTests.ParityClass)))
			{
				UserData.RegisterType(MoonSharp.Generated.GeneratedUserData.CreateDescriptor(typeof(InteropParityTests.ParityClass)));
				Script script = new Script();
				script.Globals["obj"] = new InteropParityTests.ParityClass();
				AssertBudget("clr_interop_generated", LuaLoop(script, "return function(n) local s = 0 for i = 1, n do s = s + obj:Add(i, 2) + obj:Add(i, i) end return s end"), N, NoAllocation);
			}
		}

		[Test]
		public void AllocationBudget_ClrInteropReflection()
		{
			SkipUnlessMeasurable();
			UserData.UnregisterType<Calc>();
			UserData.RegisterType<Calc>(InteropAccessMode.Reflection);

			try
			{
				Script script = new Script();
				script.Globals["obj"] = new Calc();
				// Userdata methods read their arguments in place through a reused context and view (424 bytes
				// before): no argument array, argument object or DynValue for obj. Left: reflection's object[]
				// and boxes, the DynValue of i, and the DynValues of the two results.
				AssertBudget("clr_interop_reflection", LuaLoop(script, "return function(n) local s = 0 for i = 1, n do s = s + obj:Add(i, 2) + obj.Value end return s end"), N, 256);
			}
			finally
			{
				UserData.UnregisterType<Calc>();
			}
		}
	}
}
