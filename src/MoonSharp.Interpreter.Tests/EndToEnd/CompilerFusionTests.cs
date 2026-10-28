using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter.Debugging;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// The compiler emits fused instructions (a numeric for's step-test-jump in one instruction). These
	/// tests pin the Lua 5.2 behaviour the fused forms must keep, and the instruction counts that show
	/// the fusion happened.
	/// </summary>
	[TestFixture]
	public class CompilerFusionTests
	{
		// Instructions a coroutine runs for fn(n): with AutoYieldCounter = 1 it yields after every one.
		static int InstructionsFor(string chunk, int n)
		{
			Script s = new Script(CoreModules.None);
			DynValue fn = s.DoString(chunk);
			DynValue co = s.CreateCoroutine(fn);
			co.Coroutine.AutoYieldCounter = 1;

			int count = 0;
			DynValue r = co.Coroutine.Resume(n);

			while (r.Type == DataType.YieldRequest)
			{
				count++;
				r = co.Coroutine.Resume();
			}

			return count;
		}

		static int InstructionsPerIteration(string chunk)
		{
			return (InstructionsFor(chunk, 200) - InstructionsFor(chunk, 100)) / 100;
		}

		[Test]
		public void ForLoop_EmptyIterationIsOneInstruction()
		{
			// Before the ForLoop opcode: JFor, StoreLcl, Debug, Clean, Incr, Jump.
			Assert.AreEqual(1, InstructionsPerIteration("return function(n) for i = 1, n do end end"));
		}

		[Test]
		public void ForLoop_ClosuresCaptureEachIterationsVariable()
		{
			DynValue r = new Script().DoString(@"
				local fs = {}
				for i = 1, 3 do
					local j = i * 10
					fs[i] = function() i = i + 100 return i + j end
				end
				return fs[1](), fs[2](), fs[3](), fs[1]()");

			Assert.AreEqual("111\t122\t133\t211", r.ToPrintString());
		}

		[Test]
		public void ForLoop_AssigningTheVariableDoesNotChangeTheCount()
		{
			DynValue r = new Script().DoString(@"
				local seen = {}
				for i = 1, 3 do
					seen[#seen + 1] = i
					i = i * 100
					seen[#seen + 1] = i
				end
				return table.concat(seen, ',')");

			Assert.AreEqual("1,100,2,200,3,300", r.String);
		}

		[TestCase("for i = 3, 1, -1 do t[#t + 1] = i end", "3,2,1")]
		[TestCase("for i = 1, 2, 0.5 do t[#t + 1] = i end", "1,1.5,2")]
		[TestCase("for i = 1, 0 do t[#t + 1] = i end", "")]
		[TestCase("for i = 1, 3, 0/0 do t[#t + 1] = i end", "")]
		[TestCase("for i = 1, 0/0 do t[#t + 1] = i end", "")]
		[TestCase("for i = 0/0, 1 do t[#t + 1] = i end", "")]
		[TestCase("for i = 1, math.huge do t[#t + 1] = i if i == 3 then break end end", "1,2,3")]
		[TestCase("for i = '1', '3', '1' do t[#t + 1] = i end", "1,2,3")]
		[TestCase("for i = 1, 3 do if i == 2 then goto continue end t[#t + 1] = i ::continue:: end", "1,3")]
		[TestCase("for i = 1, 3 do for j = 1, 3 do if j > i then goto next end t[#t + 1] = i .. j end ::next:: end", "11,21,22,31,32,33")]
		[TestCase("for i = 0, 1, 0.25 do t[#t + 1] = i end", "0,0.25,0.5,0.75,1")]
		public void ForLoop_FollowsLua52(string loop, string expected)
		{
			DynValue r = new Script().DoString("local t = {} " + loop + " return table.concat(t, ',')");
			Assert.AreEqual(expected, r.String);
		}

		[TestCase("for i = 1, 'x' do end", "'for' limit must be a number")]
		[TestCase("for i = 1, 2, {} do end", "'for' step must be a number")]
		[TestCase("for i = nil, 2 do end", "'for' initial value must be a number")]
		public void ForLoop_NonNumberBoundsKeepTheirMessages(string loop, string message)
		{
			var ex = Assert.Throws<ScriptRuntimeException>(() => new Script().DoString(loop));
			Assert.AreEqual(message, ex.Message);
		}

		[Test]
		public void ForLoop_ErrorsInTheBodyReportTheirLine()
		{
			var ex = Assert.Throws<ScriptRuntimeException>(() => new Script().DoString("local x\nfor i = 1, 3 do\n  if i == 2 then\n    x = x + i\n  end\nend", null, "loop.lua"));
			StringAssert.StartsWith("loop.lua:(4,", ex.DecoratedMessage);
		}

		[Test]
		public void ForLoop_CoroutineYieldsInsideTheLoop()
		{
			DynValue r = new Script().DoString(@"
				local co = coroutine.wrap(function(n)
					for i = 1, n do
						local captured = function() return i end
						coroutine.yield(captured)
					end
					return 'done'
				end)
				local fs = { co(3), co(), co() }
				return fs[1]() .. fs[2]() .. fs[3]() .. co()");

			Assert.AreEqual("123done", r.String);
		}

		sealed class StepInDebugger : IDebugger
		{
			public List<int> Lines = new List<int>();

			public DebuggerCaps GetDebuggerCaps() { return DebuggerCaps.CanDebugSourceCode; }
			public void SetDebugService(DebugService debugService) { }
			public void SetSourceCode(SourceCode sourceCode) { }
			public void SetByteCode(string[] byteCode) { }
			public bool IsPauseRequested() { return false; }
			public bool SignalRuntimeException(ScriptRuntimeException ex) { return false; }
			public void SignalExecutionEnded() { }
			public void Update(WatchType watchType, IEnumerable<WatchItem> items, int stackFrameIndex) { }
			public List<DynamicExpression> GetWatchItems() { return new List<DynamicExpression>(); }
			public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }

			public DebuggerAction GetAction(int ip, SourceRef sourceref)
			{
				if (sourceref != null)
					Lines.Add(sourceref.FromLine);

				return new DebuggerAction() { Action = DebuggerAction.ActionType.StepIn };
			}
		}

		static string StepLines(string code)
		{
			Script s = new Script(CoreModules.None);
			var debugger = new StepInDebugger();
			s.AttachDebugger(debugger);
			s.DoString(code);
			return string.Join(",", debugger.Lines);
		}

		[Test]
		public void ForLoop_StepInStopsAtTheEndOfEveryIteration()
		{
			// The for line is a stop when the loop starts; after that each iteration ends on the end line.
			// Before the ForLoop opcode the for line was a stop in every iteration too: 1,2,3,4,2,3,4,2,4,5.
			Assert.AreEqual("1,2,3,4,3,4,5", StepLines("local x = 0\nfor i = 1, 2 do\n  x = x + i\nend\nreturn x"));
		}

		[Test]
		public void BinDump_ForLoopRoundTrips()
		{
			Script s1 = new Script();
			DynValue fn = s1.DoString(LoopSource);

			using (MemoryStream ms = new MemoryStream())
			{
				s1.Dump(fn, ms);
				ms.Seek(0, SeekOrigin.Begin);
				DynValue loaded = new Script().LoadStream(ms);
				Assert.AreEqual(20.0, loaded.Function.Call(5).Number);
			}
		}

		[Test]
		public void BinDump_LoadsAPreviousVersionDumpWithLoops()
		{
			using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(LoopDump0x152)))
			{
				DynValue fn = new Script().LoadStream(ms, null, "old.lua");
				Assert.AreEqual(20.0, fn.Function.Call(5).Number);
			}
		}

		const string LoopSource = @"return function(n)
  local fs, s = {}, 0
  for i = 1, n do
    if i ~= 3 and i <= n then fs[#fs + 1] = function() return i end end
  end
  for k = #fs, 1, -1 do s = s + fs[k]() * 2 - 1 end
  return s
end
";

		// LoopSource's function dumped in format 0x152 (before the fused opcodes) by origin/next 90c9c2a.
		const string LoopDump0x152 = "HU1PT04jDRp/UgEBTwcAAAABbgABAQJmcwACAgFzAAMDAWkABAQBawEABQRfRU5WAQED/////////xVPAQYLPGNodW5rXzE6MT4AFgUCBQABAgMEFwEABwAFAQMAAAAAAAAAAAoBAAEKAAACAgILADIDBQEDAAAAAAAA8D8yAgUBAwAAAAAAAPA/MgEUAwMeMQoAAAMLAwUBAwAAAAAAAAhAJDMqIBsLAwsAIi4cLhQEAwYfAgUDGyQVBAEHCzxjaHVua18xOjQ+ABYA/wANBhoBGgAFAQALAQsBKwUBAwAAAAAAAPA/JQ4BAAD/AgIUBAMbLhQDAzEBGw8CAwUBAwAAAAAAAPA/MgMFAQMAAAAAAADwPywyAgsBKzIBFAQEHkwKAAAECwILAQsEDwAYAAgABQEDAAAAAAAAAEAnJQUBAwAAAAAAAPA/JgoAAAICARQEBDEBGzsCAwsCGgEaAAYBAA8DAQEGAQACEwEBAQEGAQACDwEBAQEBAQEBBgEABBkBAQEBAQEBAQEBAQYAADQCAQEBBgAANwgBBgAAQAMGAAAeJQEBAQEBAQEGAABEAwb/AAIPBgIAAgMBAQEGAQACFQEBAQEBAQEBAQEGAAAYFwEBAQEBAQEBAQEBBgAAMAMBAQEGAQACCAEGAQAAAw==";
	}
}
