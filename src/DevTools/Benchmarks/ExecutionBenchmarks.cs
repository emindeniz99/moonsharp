using BenchmarkDotNet.Attributes;
using MoonSharp.Interpreter;

namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Hot-path cost of calling already compiled Lua functions. Compilation happens in setup.
	/// </summary>
	[MemoryDiagnoser]
	public class ExecutionBenchmarks
	{
		Script m_Script;
		DynValue m_Eligible, m_Fib, m_SumLoop, m_TableWork, m_Concat, m_Player;

		[GlobalSetup]
		public void Setup()
		{
			m_Script = new Script();
			m_Eligible = m_Script.DoString(Workloads.Eligibility);
			m_Fib = m_Script.DoString(Workloads.Fib);
			m_SumLoop = m_Script.DoString(Workloads.SumLoop);
			m_TableWork = m_Script.DoString(Workloads.TableWork);
			m_Concat = m_Script.DoString(Workloads.Concat);
			m_Player = m_Script.DoString("return { level = 12, country = 'DE', flags = { beta = true } }");
		}

		[Benchmark]
		public DynValue CallEligibility() => m_Script.Call(m_Eligible, m_Player, "offers");

		[Benchmark]
		public DynValue Fib20() => m_Script.Call(m_Fib, 20);

		[Benchmark]
		public DynValue SumLoop100K() => m_Script.Call(m_SumLoop, 100000);

		[Benchmark]
		public DynValue TableInsertLookup1K() => m_Script.Call(m_TableWork, 1000);

		[Benchmark]
		public DynValue Concat1K() => m_Script.Call(m_Concat, 1000);
	}
}
