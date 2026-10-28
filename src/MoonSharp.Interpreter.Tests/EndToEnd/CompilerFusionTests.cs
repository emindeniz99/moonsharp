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

		// The runners take [Test] methods only, so cases are rows of a table.
		static readonly string[,] ForLoopCases =
		{
			{ "for i = 3, 1, -1 do t[#t + 1] = i end", "3,2,1" },
			{ "for i = 1, 2, 0.5 do t[#t + 1] = i end", "1,1.5,2" },
			{ "for i = 1, 0 do t[#t + 1] = i end", "" },
			{ "for i = 1, 3, 0/0 do t[#t + 1] = i end", "" },
			{ "for i = 1, 0/0 do t[#t + 1] = i end", "" },
			{ "for i = 0/0, 1 do t[#t + 1] = i end", "" },
			{ "for i = 1, math.huge do t[#t + 1] = i if i == 3 then break end end", "1,2,3" },
			{ "for i = '1', '3', '1' do t[#t + 1] = i end", "1,2,3" },
			{ "for i = 1, 3 do if i == 2 then goto continue end t[#t + 1] = i ::continue:: end", "1,3" },
			{ "for i = 1, 3 do for j = 1, 3 do if j > i then goto next end t[#t + 1] = i .. j end ::next:: end", "11,21,22,31,32,33" },
			{ "for i = 0, 1, 0.25 do t[#t + 1] = i end", "0,0.25,0.5,0.75,1" },
		};

		[Test]
		public void ForLoop_FollowsLua52()
		{
			for (int k = 0; k < ForLoopCases.GetLength(0); k++)
			{
				DynValue r = new Script().DoString("local t = {} " + ForLoopCases[k, 0] + " return table.concat(t, ',')");
				Assert.AreEqual(ForLoopCases[k, 1], r.String, ForLoopCases[k, 0]);
			}
		}

		static readonly string[,] ForLoopErrorCases =
		{
			{ "for i = 1, 'x' do end", "'for' limit must be a number" },
			{ "for i = 1, 2, {} do end", "'for' step must be a number" },
			{ "for i = nil, 2 do end", "'for' initial value must be a number" },
		};

		[Test]
		public void ForLoop_NonNumberBoundsKeepTheirMessages()
		{
			for (int k = 0; k < ForLoopErrorCases.GetLength(0); k++)
			{
				string loop = ForLoopErrorCases[k, 0];
				var ex = Assert.Throws<ScriptRuntimeException>(() => new Script().DoString(loop));
				Assert.AreEqual(ForLoopErrorCases[k, 1], ex.Message, loop);
			}
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

		// Instructions per loop iteration (ForLoop included) for each condition. Before fusion
		// `if i < 0` ran Local, Literal, Less, ToBool, Jf.
		static readonly string[,] ConditionCases =
		{
			{ "if i < 0 then c = 1 end", "2" },
			{ "if i > n then c = 1 end", "2" },
			{ "if i == nil then c = 1 end", "2" },
			{ "if i ~= i then c = 1 end", "2" },
			{ "if 0 >= i then c = 1 end", "3" },
			{ "if -i <= -n then c = 1 end", "6" },
		};

		[Test]
		public void Compare_ConditionIsOneInstruction()
		{
			for (int k = 0; k < ConditionCases.GetLength(0); k++)
			{
				string body = ConditionCases[k, 0];
				Assert.AreEqual(int.Parse(ConditionCases[k, 1]), InstructionsPerIteration("return function(n) local c for i = 1, n do " + body + " end end"), body);
			}
		}

		[Test]
		public void Compare_WhileConditionIsOneInstruction()
		{
			// Local i, Local n, LessEq, CNot, Jf and the body's 5 before; now the condition is 1.
			Assert.AreEqual(1 + 5 + 2, (InstructionsFor("return function(n) local i = 0 while i <= n do i = i + 1 end end", 200)
				- InstructionsFor("return function(n) local i = 0 while i <= n do i = i + 1 end end", 100)) / 100);
		}

		// Every comparison operator, on every pair of values, in every operand shape the compiler fuses:
		// the branch an if or a while takes must match the value the same comparison gives when it is
		// not fused (an assignment), errors included.
		const string CompareDifferential = @"
			local MT = {}
			MT.__lt = function(x, y) return x.v < y.v end
			MT.__le = function(x, y) return x.v <= y.v end
			MT.__eq = function(x, y) return x.v == y.v end
			local LT = { __lt = function(x, y) return x.v < y.v end }
			local env = { o1 = setmetatable({ v = 1 }, MT), o2 = setmetatable({ v = 2 }, MT), o1b = setmetatable({ v = 1 }, MT),
				l1 = setmetatable({ v = 1 }, LT), l2 = setmetatable({ v = 2 }, LT), t = {},
				id = function(x) return x end, pcall = pcall, tostring = tostring }

			local literals = { '1', '2', '1.5', '""a""', '""b""', 'nil', 'true', 'false' }
			local values = { '0/0', 't', 'o1', 'o2', 'o1b', 'l1', 'l2' }
			for _, v in ipairs(literals) do values[#values + 1] = v end
			local isLiteral = {}
			for _, v in ipairs(literals) do isLiteral[v] = true end

			local ops = { '<', '<=', '>', '>=', '==', '~=' }

			local function run(src)
				local f = assert(load(src, 'cmp', 't', env))
				local ok, r = pcall(f)
				if ok then return tostring(r) end
				return 'error: ' .. (tostring(r):match('%): (.*)$') or tostring(r))
			end

			local mismatches, checked = {}, 0

			for _, op in ipairs(ops) do
				for _, A in ipairs(values) do
					for _, B in ipairs(values) do
						local shapes = {
							{ 'local a, b = ' .. A .. ', ' .. B, 'a', 'b' },
							{ 'local a, b = ' .. A .. ', ' .. B, 'id(a)', 'id(b)' },
							{ 'local a, b = ' .. A .. ', ' .. B, 'a', 'id(b)' },
						}
						if isLiteral[B] then
							shapes[#shapes + 1] = { 'local a = ' .. A, 'a', B }
							shapes[#shapes + 1] = { 'local a = ' .. A, 'id(a)', B }
						end
						if isLiteral[A] then
							shapes[#shapes + 1] = { 'local b = ' .. B, A, 'b' }
						end

						for _, s in ipairs(shapes) do
							local expr = s[2] .. ' ' .. op .. ' ' .. s[3]
							local plain = run(s[1] .. ' local v = ' .. expr .. ' return v')
							local branch = run(s[1] .. ' if ' .. expr .. ' then return true else return false end')
							local loop = run(s[1] .. ' while ' .. expr .. ' do return true end return false')
							local negated = run(s[1] .. ' if not (' .. expr .. ') then return false else return true end')
							checked = checked + 1
							if branch ~= plain or loop ~= plain or negated ~= plain then
								mismatches[#mismatches + 1] = s[1] .. '; ' .. expr .. ': ' .. plain .. ' / ' .. branch .. ' / ' .. loop .. ' / ' .. negated
							end
						end
					end
				end
			end

			return checked, table.concat(mismatches, '\n')";

		[Test]
		public void Compare_FusedBranchesMatchUnfusedComparisons()
		{
			DynValue r = new Script().DoString(CompareDifferential);
			Assert.Greater(r.Tuple[0].Number, 1000);
			Assert.AreEqual("", r.Tuple[1].String);
		}

		[Test]
		public void Compare_MetamethodsThatYieldInsideAFusedCondition()
		{
			DynValue r = new Script().DoString(@"
				local mt = { __lt = function(a, b) coroutine.yield('lt') return a.v < b.v end,
					__eq = function(a, b) coroutine.yield('eq') return a.v == b.v end }
				local a, b = setmetatable({ v = 1 }, mt), setmetatable({ v = 2 }, mt)
				local co = coroutine.wrap(function()
					local out = {}
					for i = 1, 2 do
						if a < b then out[#out + 1] = 'lt' .. i end
						if a == b then out[#out + 1] = 'eq' .. i end
						if a ~= b then out[#out + 1] = 'ne' .. i end
					end
					return table.concat(out, ',')
				end)
				local steps = {}
				local v = co()
				while v == 'lt' or v == 'eq' do steps[#steps + 1] = v v = co() end
				return table.concat(steps, ',') .. '|' .. v");

			Assert.AreEqual("lt,eq,eq,lt,eq,eq|lt1,ne1,lt2,ne2", r.String);
		}

		[Test]
		public void Compare_ClrMetamethodsInAFusedCondition()
		{
			Script s = new Script();
			s.Globals["lt"] = DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(args[0].Table.Get("v").Number < args[1].Table.Get("v").Number));

			DynValue r = s.DoString(@"
				local mt = { __lt = lt }
				local a, b = setmetatable({ v = 1 }, mt), setmetatable({ v = 2 }, mt)
				local out = {}
				if a < b then out[#out + 1] = 'a<b' end
				if b < a then out[#out + 1] = 'b<a' end
				if b > a then out[#out + 1] = 'b>a' end
				if a <= b then out[#out + 1] = 'a<=b' end
				return table.concat(out, ',')");

			Assert.AreEqual("a<b,b>a,a<=b", r.String);
		}

		[Test]
		public void Compare_JumpsIntoTheMiddleOfAFusedSequence()
		{
			// `and`/`or` jump to instructions inside the fused sequences; those still run unfused.
			DynValue r = new Script().DoString(@"
				local function f(x, a, b, c)
					local out = ''
					if x and a < b then out = out .. 'A' end
					if a < (x and b or c) then out = out .. 'B' end
					if (x and a or c) == 3 then out = out .. 'C' end
					if a > (x and b or 0) then out = out .. 'D' end
					while x and a <= b do out = out .. 'E' a = a + 1 end
					return out
				end
				return f(true, 1, 2, 0) .. '|' .. f(false, 3, 2, 5) .. '|' .. f(nil, 1, 2, 3)");

			Assert.AreEqual("ABEE|BD|BCD", r.String);
		}

		[Test]
		public void Compare_ErrorsReportTheirLine()
		{
			var ex = Assert.Throws<ScriptRuntimeException>(() => new Script().DoString("local a, b = 1, 'x'\nlocal c = 0\nif a < b then\n  c = 1\nend", null, "cmp.lua"));
			StringAssert.StartsWith("cmp.lua:(3,", ex.DecoratedMessage);
			StringAssert.Contains("attempt to compare number with string", ex.Message);
		}

		[Test]
		public void Compare_StepInStopsOnTheSameLines()
		{
			// The same stops as without fusion: a debugger sees the unfused instructions.
			Assert.AreEqual("1,2,3,4,2,3,4,2,5,6,7,8",
				StepLines("local i = 0\nwhile i < 2 do\n  i = i + 1\nend\nif i == 2 then\n  i = 3\nend\nreturn i"));
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
