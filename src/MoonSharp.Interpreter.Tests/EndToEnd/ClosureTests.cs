using System;
using MoonSharp.Interpreter.Execution;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	[TestFixture]
	public class ClosureTests
	{
		[Test]
		public void ClosureOnParam()
		{
			string script = @"
				local function g (z)
				  local function f(a)
					return a + z;
				  end
				  return f;
				end

				return (g(3)(2));";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Number, res.Type);
			Assert.AreEqual(5, res.Number);
		}

		[Test]
		public void LambdaFunctions()
		{
			string script = @"
g = |f, x|f(x, x+1)
f = |x, y, z|x*(y+z)
return g(|x,y|f(x,y,1), 2)
";
			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Number, res.Type);
			Assert.AreEqual(8, res.Number);
		}



		[Test]
		public void ClosureOnParamLambda()
		{
			string script = @"
				local function g (z)
				  return |a| a + z
				end

				return (g(3)(2));";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Number, res.Type);
			Assert.AreEqual(5, res.Number);
		}


		[Test]
		public void Closures()
		{
			// expected : 201 2001 20001 200001 2000001
			string script = @"
						a = {}
						x = 0

						function container()
							local x = 20

							for i=1,5 do
								local y = 0
								a[i] = function () y=y+1; x = x * 10; return x+y end
							end
						end

						container();

						x = 4000

						return a[1](), a[2](), a[3](), a[4](), a[5]()";


			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Tuple, res.Type);
			Assert.AreEqual(5, res.Tuple.Length);
			Assert.AreEqual(DataType.Number, res.Tuple[0].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[1].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[2].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[3].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[4].Type);
			Assert.AreEqual(201, res.Tuple[0].Number);
			Assert.AreEqual(2001, res.Tuple[1].Number);
			Assert.AreEqual(20001, res.Tuple[2].Number);
			Assert.AreEqual(200001, res.Tuple[3].Number);
			Assert.AreEqual(2000001, res.Tuple[4].Number);
		}

		[Test]
		public void ClosuresNonAnonymousLocal()
		{
			// expected : 201 2001 20001 200001 2000001
			string script = @"
						a = {}
						x = 0

						function container()
							local x = 20

							for i=1,5 do
								local y = 0
								local function zz() y=y+1; x = x * 10; return x+y end
								a[i] = zz;
							end
						end

						container();

						x = 4000

						return a[1](), a[2](), a[3](), a[4](), a[5]()";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Tuple, res.Type);
			Assert.AreEqual(5, res.Tuple.Length);
			Assert.AreEqual(DataType.Number, res.Tuple[0].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[1].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[2].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[3].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[4].Type);
			Assert.AreEqual(201, res.Tuple[0].Number);
			Assert.AreEqual(2001, res.Tuple[1].Number);
			Assert.AreEqual(20001, res.Tuple[2].Number);
			Assert.AreEqual(200001, res.Tuple[3].Number);
			Assert.AreEqual(2000001, res.Tuple[4].Number);
		}


		[Test]
		public void ClosuresNonAnonymous()
		{
			// expected : 201 2001 20001 200001 2000001
			string script = @"
						a = {}
						x = 0

						function container()
							local x = 20

							for i=1,5 do
								local y = 0
								function zz() y=y+1; x = x * 10; return x+y end
								a[i] = zz;
							end
						end

						container();

						x = 4000

						return a[1](), a[2](), a[3](), a[4](), a[5]()";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Tuple, res.Type);
			Assert.AreEqual(5, res.Tuple.Length);
			Assert.AreEqual(DataType.Number, res.Tuple[0].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[1].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[2].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[3].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[4].Type);
			Assert.AreEqual(201, res.Tuple[0].Number);
			Assert.AreEqual(2001, res.Tuple[1].Number);
			Assert.AreEqual(20001, res.Tuple[2].Number);
			Assert.AreEqual(200001, res.Tuple[3].Number);
			Assert.AreEqual(2000001, res.Tuple[4].Number);
		}

		[Test]
		public void ClosureNoTable()
		{
			string script = @"
				x = 0

				function container()
					local x = 20

					for i=1,5 do
						local y = 0
		
						function zz() y=y+1; x = x * 10; return x+y end
		
						a1 = a2;
						a2 = a3;
						a3 = a4;
						a4 = a5;
						a5 = zz;
					end
				end

				container();

				x = 4000

				return a1(), a2(), a3(), a4(), a5()";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Tuple, res.Type);
			Assert.AreEqual(5, res.Tuple.Length);
			Assert.AreEqual(DataType.Number, res.Tuple[0].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[1].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[2].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[3].Type);
			Assert.AreEqual(DataType.Number, res.Tuple[4].Type);
			Assert.AreEqual(201, res.Tuple[0].Number);
			Assert.AreEqual(2001, res.Tuple[1].Number);
			Assert.AreEqual(20001, res.Tuple[2].Number);
			Assert.AreEqual(200001, res.Tuple[3].Number);
			Assert.AreEqual(2000001, res.Tuple[4].Number);
		}


		[Test]
		public void NestedUpvalues()
		{
			string script = @"
	local y = y;

	local x = 0;
	local m = { };

	function m:a()
		self.t = {
			dojob = function() 
				if (x == 0) then return 1; else return 0; end
			end,
		};
	end

	m:a();

	return 10 * m.t.dojob();
								";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Number, res.Type);
			Assert.AreEqual(10, res.Number);
		}

		[Test]
		public void NestedOutOfScopeUpvalues()
		{
			string script = @"

	function X()
		local y = y;

		local x = 0;
		local m = { };

		function m:a()
			self.t = {
				dojob = function() 
					if (x == 0) then return 1; else return 0; end
				end,
			};
		end

		return m;
	end

	Q = X();

	Q:a();

	return 10 * Q.t.dojob();
								";

			DynValue res = new Script(CoreModules.Preset_HardSandbox).DoString(script);

			Assert.AreEqual(DataType.Number, res.Type);
			Assert.AreEqual(10, res.Number);
		}


		[Test]
		public void LocalRedefinition()
		{
			string script = @"

				result = ''

				local hi = 'hello'

				local function test()
					result = result .. hi;
				end

				test();

				hi = 'X'

				test();

				local hi = '!';

				test();

				return result;
								";

			DynValue res = new Script(CoreModules.Preset_HardSandbox).DoString(script);

			Assert.AreEqual(DataType.String, res.Type);
			Assert.AreEqual("helloXX", res.String);
		}

		[Test]
		public void ClosuresInLoopCaptureFreshUpvalues()
		{
			// Each iteration's closure keeps its own i, and its own copy of a block local.
			DynValue res = new Script().DoString(@"
				local fs = {}
				for i = 1, 3 do
					local j = i * 10
					fs[i] = function() j = j + 1 return i, j end
				end
				local a1, b1 = fs[1]()
				local a2, b2 = fs[2]()
				local a3, b3 = fs[1]()
				return a1 .. ',' .. b1 .. ',' .. a2 .. ',' .. b2 .. ',' .. a3 .. ',' .. b3");

			Assert.AreEqual("1,11,2,21,1,12", res.String);
		}

		[Test]
		public void ClosuresShareAnUpvalue()
		{
			DynValue res = new Script().DoString(@"
				local function counter()
					local n = 0
					return function() n = n + 1 return n end, function() return n end
				end
				local inc1, get1 = counter()
				local inc2, get2 = counter()
				inc1() inc1() inc2()
				return get1() .. ',' .. get2()");

			Assert.AreEqual("2,1", res.String);
		}

		[Test]
		public void ClosureUpvalueNamesAndOrder()
		{
			// Names and order as debug.getupvalue reports them, and the same through the host API.
			Script s = new Script(CoreModules.Preset_Complete);
			DynValue res = s.DoString(@"
				local a, b, c = 1, 'two', true
				local function f() return c, a, b, print end
				local names = {}
				local i = 1
				while true do
					local name, value = debug.getupvalue(f, i)
					if name == nil then break end
					names[#names + 1] = name .. '=' .. (type(value) == 'table' and 'table' or tostring(value))
					i = i + 1
				end
				return table.concat(names, ' '), f");

			// _ENV comes first: the function body loads it before anything else.
			Assert.AreEqual("_ENV=table c=true a=1 b=two", res.Tuple[0].String);

			Closure f = res.Tuple[1].Function;
			Assert.AreEqual(4, f.GetUpvaluesCount());
			Assert.AreEqual(WellKnownSymbols.ENV, f.GetUpvalueName(0));
			Assert.AreEqual("c", f.GetUpvalueName(1));
			Assert.AreEqual("a", f.GetUpvalueName(2));
			Assert.AreEqual("b", f.GetUpvalueName(3));
			Assert.AreEqual(Closure.UpvaluesType.Closure, f.GetUpvaluesType());
		}

		[Test]
		public void ClosureWithOnlyEnvSurvivesDump()
		{
			Script s = new Script();
			DynValue f = s.DoString("x = 5 return function(y) return x + y end");

			Assert.AreEqual(Closure.UpvaluesType.Environment, f.Function.GetUpvaluesType());

			var ms = new System.IO.MemoryStream();
			s.Dump(f, ms);
			ms.Position = 0;

			DynValue g = s.LoadStream(ms);
			Assert.AreEqual(7, s.Call(g, 2).Number);
		}


	}
}
