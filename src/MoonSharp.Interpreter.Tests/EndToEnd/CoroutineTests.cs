using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	[TestFixture]
	class CoroutineTests
	{
		[Test]
		public void Coroutine_Basic()
		{
			string script = @"
				s = ''

				function foo()
					for i = 1, 4 do
						s = s .. i;
						coroutine.yield();
					end
				end

				function bar()
					for i = 5, 9 do
						s = s .. i;
						coroutine.yield();
					end
				end

				cf = coroutine.create(foo);
				cb = coroutine.create(bar);

				for i = 1, 4 do
					coroutine.resume(cf);
					s = s .. '-';
					coroutine.resume(cb);
					s = s .. ';';
				end

				return s;
				";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.String, res.Type);
			Assert.AreEqual("1-5;2-6;3-7;4-8;", res.String);
		}

		[Test]
		public void Coroutine_Wrap()
		{
			string script = @"
				s = ''

				function foo()
					for i = 1, 4 do
						s = s .. i;
						coroutine.yield();
					end
				end

				function bar()
					for i = 5, 9 do
						s = s .. i;
						coroutine.yield();
					end
				end

				cf = coroutine.wrap(foo);
				cb = coroutine.wrap(bar);

				for i = 1, 4 do
					cf();
					s = s .. '-';
					cb();
					s = s .. ';';
				end

				return s;
				";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.String, res.Type);
			Assert.AreEqual("1-5;2-6;3-7;4-8;", res.String);
		}

		[Test]
		public void Coroutine_ClrBoundaryHandling()
		{
			string code = @"
				function a()
					callback(b)
				end

				function b()
					coroutine.yield();
				end						

				c = coroutine.create(a);

				return coroutine.resume(c);		
				";

			// Load the code and get the returned function
			Script script = new Script();

			script.Globals["callback"] = DynValue.NewCallback(
				(ctx, args) => args[0].Function.Call()
				);

			DynValue ret = script.DoString(code);

			Assert.AreEqual(DataType.Tuple, ret.Type);
			Assert.AreEqual(2, ret.Tuple.Length);
			Assert.AreEqual(DataType.Boolean, ret.Tuple[0].Type);
			Assert.AreEqual(false, ret.Tuple[0].Boolean);
			Assert.AreEqual(DataType.String, ret.Tuple[1].Type);
			Assert.IsTrue(ret.Tuple[1].String.EndsWith("attempt to yield across a CLR-call boundary"));
		}

		[Test]
		public void Coroutine_VariousErrorHandling()
		{
			string last = "";
			string code = @"

function checkresume(step, ex, ey)
	local x, y = coroutine.resume(c)
	
	assert(x == ex, 'Step ' .. step .. ': ' .. tostring(ex) .. ' was expected, got ' .. tostring(x));
	assert(y:endsWith(ey), 'Step ' .. step .. ': ' .. tostring(ey) .. ' was expected, got ' .. tostring(y));
end


t = { }
m = { __tostring = function() print('2'); coroutine.yield(); print('3'); end }

setmetatable(t, m);


function a()
	checkresume(1, false, 'cannot resume non-suspended coroutine');
	coroutine.yield('ok');
	print(t);
	coroutine.yield('ok'); 
end

c = coroutine.create(a);

checkresume(2, true, 'ok');
checkresume(3, false, 'attempt to yield across a CLR-call boundary');
checkresume(4, false, 'cannot resume dead coroutine');
checkresume(5, false, 'cannot resume dead coroutine');
checkresume(6, false, 'cannot resume dead coroutine');

				";

			// Load the code and get the returned function
			Script script = new Script();

			script.Options.DebugPrint = (s) => last = s;

			script.DoString(code);

			Assert.AreEqual(last, "2");
		}

		[Test]
		public void Coroutine_Direct_Resume()
		{
			string code = @"
				return function()
					local x = 0
					while true do
						x = x + 1
						coroutine.yield(x)
						if (x > 5) then
							return 7
						end
					end
				end
				";

			// Load the code and get the returned function
			Script script = new Script();
			DynValue function = script.DoString(code);

			// Create the coroutine in C#
			DynValue coroutine = script.CreateCoroutine(function);

			// Loop the coroutine 
			string ret = "";
			while (coroutine.Coroutine.State != CoroutineState.Dead)
			{
				DynValue x = coroutine.Coroutine.Resume();
				ret = ret + x.ToString();
			}

			Assert.AreEqual("1234567", ret);
		}


		[Test]
		public void Coroutine_Direct_AsEnumerable()
		{
			string code = @"
				return function()
					local x = 0
					while true do
						x = x + 1
						coroutine.yield(x)
						if (x > 5) then
							return 7
						end
					end
				end
				";

			// Load the code and get the returned function
			Script script = new Script();
			DynValue function = script.DoString(code);

			// Create the coroutine in C#
			DynValue coroutine = script.CreateCoroutine(function);

			// Loop the coroutine 
			string ret = "";

			foreach (DynValue x in coroutine.Coroutine.AsTypedEnumerable())
			{
				ret = ret + x.ToString();
			}

			Assert.AreEqual("1234567", ret);
		}


		[Test]
		public void Coroutine_AutoYield()
		{
			string code = @"
				function fib(n)
					if (n == 0 or n == 1) then
						return 1;
					else
						return fib(n - 1) + fib(n - 2);
					end
				end
				";

			// Load the code and get the returned function
			Script script = new Script(CoreModules.None);
			script.DoString(code);

			// get the function
			DynValue function = script.Globals.Get("fib");

			// Create the coroutine in C#
			DynValue coroutine = script.CreateCoroutine(function);

			// Set the automatic yield counter every 10 instructions. 
			// 10 is a too small! Use a much bigger value in your code to avoid interrupting too often!
			coroutine.Coroutine.AutoYieldCounter = 10;

			int cycles = 0;
			DynValue result = null;

			// Cycle until we get that the coroutine has returned something useful and not an automatic yield..
			for (result = coroutine.Coroutine.Resume(8); 
				result.Type == DataType.YieldRequest;
				result = coroutine.Coroutine.Resume()) 
			{
				cycles += 1;
			}

			// Check the values of the operation
			Assert.AreEqual(DataType.Number, result.Type);
			Assert.AreEqual(34, result.Number);

			// Check the autoyield actually triggered
			Assert.IsTrue(cycles > 10);
		}

		// Hosts create coroutines freely (one per running task or animation). Each one used to
		// allocate two 131072-slot stacks, about 2 MB, before running a single instruction.
		[Test]
		public void Coroutine_IdleCoroutinesRetainLittleMemory()
		{
			Script S = new Script(CoreModules.Coroutine);
			DynValue body = S.DoString("return function() coroutine.yield(1) end");
			var keep = new List<DynValue>();

			long before = GC.GetTotalMemory(true);

			for (int i = 0; i < 200; i++)
				keep.Add(S.CreateCoroutine(body));

			long perCoroutine = (GC.GetTotalMemory(true) - before) / keep.Count;
			GC.KeepAlive(keep);

			Assert.Less(perCoroutine, 64 * 1024, "bytes retained per idle coroutine");
		}

		// Stacks grow on demand, and a coroutine must still reach the full depth.
		[Test]
		public void Coroutine_DeepRecursionInsideCoroutine()
		{
			Script S = new Script(CoreModules.Coroutine);
			DynValue res = S.DoString(@"
				local function depth(n) if n == 0 then return 0 end return 1 + depth(n - 1) end
				local co = coroutine.create(function() coroutine.yield(depth(30000)) end)
				local ok, v = coroutine.resume(co)
				return v");

			Assert.AreEqual(30000, res.Number);
		}

		[Test]
		public void Coroutine_BodyCanBeACSharpFunction()
		{
			// Lua accepts any function as a coroutine body, C# functions included.
			DynValue r = new Script().DoString(@"
				local ok1, a, b = coroutine.resume(coroutine.create(select), 2, 'x', 'y', 'z')
				local up = coroutine.wrap(string.upper)('abc')

				local co = coroutine.create(error)
				local ok2, msg = coroutine.resume(co, 'boom', 0)
				return ok1, a, b, up, ok2, msg, coroutine.status(co)");

			Assert.IsTrue(r.Tuple[0].Boolean);
			Assert.AreEqual("y", r.Tuple[1].String);
			Assert.AreEqual("z", r.Tuple[2].String);
			Assert.AreEqual("ABC", r.Tuple[3].String);
			Assert.IsFalse(r.Tuple[4].Boolean);
			Assert.AreEqual("boom", r.Tuple[5].String);
			Assert.AreEqual("dead", r.Tuple[6].String);
		}

		[Test]
		public void Coroutine_CSharpBodyCanYieldAndTailCall()
		{
			DynValue r = new Script().DoString(@"
				-- coroutine.yield as the body: the first resume yields its arguments, the second
				-- resume's arguments are the body's results.
				local co = coroutine.create(coroutine.yield)
				local s0 = coroutine.status(co)
				local ok1, y = coroutine.resume(co, 1)
				local s1 = coroutine.status(co)
				local ok2, z = coroutine.resume(co, 2)
				local s2 = coroutine.status(co)
				local ok3 = coroutine.resume(co)

				-- pcall as the body runs the Lua function it is given, which may yield.
				local p = coroutine.create(pcall)
				local okA, v = coroutine.resume(p, function() coroutine.yield(5) return 7 end)
				local okB, pcallOk, w = coroutine.resume(p)
				return s0, ok1, y, s1, ok2, z, s2, ok3, okA, v, okB, pcallOk, w");

			object[] expected = { "suspended", true, 1.0, "suspended", true, 2.0, "dead", false, true, 5.0, true, true, 7.0 };

			for (int i = 0; i < expected.Length; i++)
			{
				DynValue v = r.Tuple[i];
				object actual = v.Type == DataType.Boolean ? (object)v.Boolean : v.Type == DataType.Number ? (object)v.Number : v.String;
				Assert.AreEqual(expected[i], actual, "result " + (i + 1));
			}
		}

		[Test]
		public void Coroutine_HostCanResumeACSharpBody()
		{
			Script s = new Script();
			DynValue body = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(args[0].Number * 2));
			DynValue co = s.CreateCoroutine(body);

			Assert.AreEqual(CoroutineState.NotStarted, co.Coroutine.State);
			Assert.AreEqual(42, co.Coroutine.Resume(DynValue.NewNumber(21)).Number);
			Assert.AreEqual(CoroutineState.Dead, co.Coroutine.State);
		}

		[Test]
		public void Coroutine_ValuesInBothDirections()
		{
			// Every count of values through yield and resume, nil holes included, with one coroutine
			// yielding many times (it reuses its yield request) and a host resume in between.
			Script s = new Script();
			DynValue r = s.DoString(@"
				local function pack(...) return select('#', ...), table.concat((function(...) local t = {} for i = 1, select('#', ...) do t[i] = tostring((select(i, ...))) end return t end)(...), ',') end
				local log = {}
				local co = coroutine.create(function(...)
					log[#log + 1] = table.concat({ pack(...) }, ':')
					log[#log + 1] = table.concat({ pack(coroutine.yield()) }, ':')
					log[#log + 1] = table.concat({ pack(coroutine.yield(1)) }, ':')
					log[#log + 1] = table.concat({ pack(coroutine.yield(1, nil, 3)) }, ':')
					return 'done', nil
				end)
				local out = {}
				out[#out + 1] = table.concat({ pack(select(2, coroutine.resume(co, 'a'))) }, ':')
				out[#out + 1] = table.concat({ pack(select(2, coroutine.resume(co))) }, ':')
				out[#out + 1] = table.concat({ pack(select(2, coroutine.resume(co, 'x', nil))) }, ':')
				out[#out + 1] = table.concat({ pack(select(2, coroutine.resume(co, nil, 'y'))) }, ':')
				local w = coroutine.wrap(function(a) local b, c = coroutine.yield(a + 1) return b + c end)
				out[#out + 1] = w(1) .. ',' .. w(2, 3)
				return table.concat(log, ' | '), table.concat(out, ' | ')");

			// The output of lua5.2 for the same script: no values arrive as no values, not as one nil
			// (upstream #287), in both directions.
			Assert.AreEqual("1:a | 0: | 2:x,nil | 2:nil,y", r.Tuple[0].String);
			Assert.AreEqual("0: | 1:1 | 3:1,nil,3 | 2:done,nil | 2,5", r.Tuple[1].String);
		}

		[Test]
		public void Coroutine_YieldRequestsOfTwoCoroutinesStaySeparate()
		{
			// Each coroutine reuses its own yield request; a coroutine resuming another one in between must
			// not see the other's values.
			DynValue r = new Script().DoString(@"
				local inner = coroutine.wrap(function() for i = 1, 3 do coroutine.yield('in' .. i) end end)
				local outer = coroutine.wrap(function() for i = 1, 3 do local v = inner() coroutine.yield('out' .. i .. v) end end)
				return outer() .. outer() .. outer()");

			Assert.AreEqual("out1in1out2in2out3in3", r.String);
		}

	}
}
