using BenchmarkDotNet.Attributes;
using MoonSharp.Interpreter;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Cost of creating the objects a host creates often: scripts, coroutines and compiled chunks.
	/// </summary>
	[MemoryDiagnoser]
	public class LifecycleBenchmarks
	{
		Script m_Script;
		DynValue m_CoroutineBody;

		[GlobalSetup]
		public void Setup()
		{
			m_Script = new Script();
			m_CoroutineBody = m_Script.DoString("return function(n) for i = 1, n do coroutine.yield(i) end end");
		}

		[Benchmark]
		public Script NewScript_Default() => new Script();

		[Benchmark]
		public Script NewScript_SoftSandbox() => new Script(CoreModules.Preset_SoftSandbox);

		[Benchmark]
		public Script NewScript_NoModules() => new Script(CoreModules.None);

		[Benchmark]
		public DynValue CreateCoroutine() => m_Script.CreateCoroutine(m_CoroutineBody);

		// A Script keeps every chunk it ever compiled, so compiling into one long-lived Script leaks.
		// Compile into a fresh empty Script instead; subtract NewScript_NoModules for the compile cost.
		[Benchmark]
		public DynValue NewScriptAndCompileSmallChunk() => new Script(CoreModules.None).LoadString(Workloads.Eligibility);
	}
}
