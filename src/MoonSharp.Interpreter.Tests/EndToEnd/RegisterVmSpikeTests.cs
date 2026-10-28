using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	/// <summary>
	/// SPIKE (branch spike/register-vm): the register VM must give the same results and the same errors,
	/// positions included, as the stack VM. Every case runs once with ScriptOptions.RegisterVm off and once
	/// with it on, on values that take the fast paths (numbers, strings, plain tables) and values that
	/// leave them (string coercion, metamethods written in Lua and C#, __index and __newindex functions
	/// and tables, __call, errors).
	/// </summary>
	[TestFixture]
	public class RegisterVmSpikeTests
	{
		const string Setup = @"
			MT = {}
			for _, e in ipairs({ '__add', '__sub', '__mul', '__div', '__mod', '__pow' }) do
				MT[e] = function(x, y) return e .. ':' .. type(x) .. ',' .. type(y) end
			end
			MT.__unm = function(x) return 'unm' end
			MT.__lt = function(x, y) return type(x) < type(y) end
			MT.__le = function(x, y) return false end
			MT.__eq = function(x, y) return true end
			MT.__index = function(t, k) return 'idx:' .. tostring(k) end
			MT.__newindex = function(t, k, v) rawset(t, '_' .. tostring(k), v) end
			MT.__call = function(self, a) return 'called:' .. tostring(a) end
			o = setmetatable({}, MT)
			o2 = setmetatable({}, MT)
			LT = { __lt = function(x, y) return x.v < y.v end }
			l1 = setmetatable({ v = 1 }, LT)
			l2 = setmetatable({ v = 2 }, LT)
			it = setmetatable({}, { __index = { x = 'inherited', y = 2 } })
			t = { x = 1, y = 'yy', [1] = 'one', [2] = 2 }
			id = function(x) return x end
			cl = math.floor
			function g(a, b) return a end
			function h(...) return ... end
			function err() error('boom') end
			function two() return 1, 2 end
			ops = {}
			function ops.add(a, b) return a + b end
			function ops.lt(a, b) return a < b end";

		static Script NewScript(bool registers)
		{
			Script s = new Script();
			s.Options.RegisterVm = registers;
			s.DoString(Setup);
			return s;
		}

		static string Run(Script s, string src)
		{
			try
			{
				DynValue r = s.DoString(src, null, "case");
				return Show(r);
			}
			catch (InterpreterException e)
			{
				return "error: " + e.DecoratedMessage;
			}
		}

		static string Show(DynValue r)
		{
			if (r.Type == DataType.Tuple)
			{
				var sb = new StringBuilder("tuple");
				foreach (DynValue v in r.Tuple)
					sb.Append(' ').Append(Show(v));
				return sb.ToString();
			}

			if (r.Type == DataType.Table || r.Type == DataType.Function || r.Type == DataType.ClrFunction)
				return r.Type.ToString();

			return r.Type + " " + r.ToPrintString();
		}

		static void AssertSame(IEnumerable<string> sources, int minimum)
		{
			Script off = NewScript(false), on = NewScript(true);
			var mismatches = new List<string>();
			int checkedCount = 0;

			foreach (string src in sources)
			{
				checkedCount++;
				string a = Run(off, src), b = Run(on, src);

				if (a != b)
					mismatches.Add(src.Replace("\n", " | ") + "\n    stack:    " + a + "\n    register: " + b);
			}

			Assert.IsTrue(checkedCount >= minimum, checkedCount + " sources checked, expected at least " + minimum);

			if (mismatches.Count > 0)
				Assert.Fail(string.Format("{0} of {1} differ:\n{2}", mismatches.Count, checkedCount, string.Join("\n", mismatches.GetRange(0, Math.Min(40, mismatches.Count)).ToArray())));
		}

		static readonly string[] Literals = { "1", "2.5", "-3", "0", "\"10\"", "\"0x10\"", "\" 5 \"", "\"a\"", "nil", "true" };
		static readonly string[] Others = { "0/0", "1/0", "o", "o2", "t", "l1", "l2", "id", "cl" };
		static readonly string[] Arithmetic = { "+", "-", "*", "/", "%", "^" };
		static readonly string[] Comparisons = { "<", "<=", ">", ">=", "==", "~=" };

		static IEnumerable<string> All()
		{
			foreach (string v in Literals) yield return v;
			foreach (string v in Others) yield return v;
		}

		static IEnumerable<string> BinarySources(string[] ops, bool compare)
		{
			var literal = new HashSet<string>(Literals);

			foreach (string op in ops)
				foreach (string A in All())
					foreach (string B in All())
					{
						string e = "a " + op + " b";
						yield return "local a, b = " + A + ", " + B + "\nreturn " + e;
						yield return "local a, b = " + A + ", " + B + "\nlocal r\nr = " + e + "\nreturn r";
						yield return "local a, b = " + A + ", " + B + "\nlocal r = id(a) " + op + " id(b)\nreturn r";

						if (literal.Contains(B))
							yield return "local a = " + A + "\nreturn a " + op + " " + B;
						if (literal.Contains(A))
							yield return "local b = " + B + "\nreturn " + A + " " + op + " b";

						if (compare)
						{
							yield return "local a, b = " + A + ", " + B + "\nif " + e + " then return 'then' else return 'else' end";
							yield return "local a, b = " + A + ", " + B + "\nif not (" + e + ") then return 'then' end\nreturn 'after'";
							yield return "local a, b = " + A + ", " + B + "\nwhile " + e + " do return 'body' end\nreturn 'done'";
							yield return "local a, b = " + A + ", " + B + "\nlocal n = 0\nrepeat n = n + 1 until " + e + " or n > 2\nreturn n";
						}
					}
		}

		[Test]
		public void RegisterVm_ArithmeticMatchesTheStackVm()
		{
			AssertSame(BinarySources(Arithmetic, false), 5000);
		}

		[Test]
		public void RegisterVm_ComparisonsMatchTheStackVm()
		{
			AssertSame(BinarySources(Comparisons, true), 10000);
		}

		[Test]
		public void RegisterVm_UnaryOperatorsMatchTheStackVm()
		{
			var sources = new List<string>();

			foreach (string A in All())
			{
				sources.Add("local a = " + A + "\nreturn -a");
				sources.Add("local a = " + A + "\nlocal r = not a\nreturn r");
				sources.Add("local a = " + A + "\nif not a then return 1 end\nreturn 2");
				sources.Add("local a = " + A + "\nif a then return 1 end\nreturn 2");
			}

			AssertSame(sources, 70);
		}

		[Test]
		public void RegisterVm_IndexingMatchesTheStackVm()
		{
			string[] objects = { "t", "it", "o", "\"str\"", "nil", "5", "{}" };
			string[] keys = { "\"x\"", "\"y\"", "1", "2", "1.5", "nil", "0/0", "\"len\"" };
			var sources = new List<string>();

			foreach (string T in objects)
			{
				sources.Add("local T = " + T + "\nreturn T.x");
				sources.Add("local T = " + T + "\nlocal v = T.len\nreturn v");
				sources.Add("local T = " + T + "\nT.x = 7\nreturn T.x, rawget(T, '_x')");
				sources.Add("local T = " + T + "\nT.z = nil\nreturn T.z");
				sources.Add("Gt = " + T + "\nreturn Gt.x");

				foreach (string K in keys)
				{
					sources.Add("local T, k = " + T + ", " + K + "\nreturn T[k]");
					sources.Add("local T = " + T + "\nreturn T[" + K + "]");
					sources.Add("local T, k = " + T + ", " + K + "\nT[k] = 'v'\nreturn T[k]");
					sources.Add("local T = " + T + "\nT[" + K + "] = 3\nlocal r = T[" + K + "]\nreturn r");
				}
			}

			sources.Add("zz = 5\nreturn zz");
			sources.Add("return undefined_global");
			sources.Add("return undefined_global.x");
			sources.Add("local x = t.x\nx = x + t[2]\nt.x = x\nreturn t.x");

			AssertSame(sources, 200);
		}

		[Test]
		public void RegisterVm_CallsMatchTheStackVm()
		{
			string[] callees = { "g", "h", "cl", "o", "t", "nil", "err", "two", "ops.add", "undefined_global" };
			var sources = new List<string>();

			foreach (string F in callees)
			{
				sources.Add("local r = " + F + "(1.5, 2)\nreturn r");
				sources.Add("local r = " + F + "(1.5, 2)\nr = " + F + "(r, 2)\nreturn r");
				sources.Add(F + "(1, 2)\nreturn 'ok'");
				sources.Add("return " + F + "(1, 2)");
				sources.Add("local a = 2\nreturn " + F + "(a)");
				sources.Add("local ok = pcall(" + F + ", 1)\nreturn ok");
				sources.Add("local x = 0\nfor i = 1, 3 do x = x + id(i) end\nlocal r = " + F + "(x)\nreturn r");
			}

			sources.Add("local function f(n) if n < 2 then return n end return f(n - 1) + f(n - 2) end\nreturn f(15)");
			sources.Add("local s = 0\nfor i = 1, 10 do s = s + ops.add(i, s) end\nreturn s");
			sources.Add("local r = two()\nreturn r");
			sources.Add("return two()");
			sources.Add("local a, b = two()\nreturn b");
			sources.Add("return id(two())");

			AssertSame(sources, 70);
		}

		[Test]
		public void RegisterVm_NumericForMatchesTheStackVm()
		{
			string[] bounds = { "1", "3", "-1", "0", "0.5", "\"2\"", "nil", "0/0", "1/0", "t" };
			var sources = new List<string>();

			foreach (string A in bounds)
				foreach (string B in bounds)
				{
					string[] steps = { null, "1", "-1", "0.5", "0", "\"1\"", "nil", "0/0" };

					foreach (string C in steps)
					{
						string range = A + ", " + B + (C != null ? ", " + C : "");
						sources.Add("local c, s = 0, 0\nfor i = " + range + " do\n  c = c + 1 s = s + i\n  if c > 20 then break end\nend\nreturn c * 1000 + s");
					}
				}

			sources.Add("local s = 0\nfor i = 1, 3 do for j = i, 3 do s = s + i * j end end\nreturn s");
			sources.Add("local s = 0\nfor i = 1, 3 do i = i * 10 s = s + i end\nreturn s");
			sources.Add("local n = 0\nwhile true do n = n + 1 if n == 5 then break end end\nreturn n");
			sources.Add("local n = 0\nrepeat local m = n n = n + 1 until m >= 3\nreturn n");

			AssertSame(sources, 800);
		}

		[Test]
		public void RegisterVm_ErrorsReportTheirLine()
		{
			var sources = new List<string>
			{
				"local x\nfor i = 1, 3 do\n  if i == 2 then\n    x = x + i\n  end\nend",
				"local a, b = 1, {}\nlocal c = 0\nif a < b then\n  c = 1\nend",
				"local t = nil\nlocal y = 1\nreturn t.x",
				"local f = 5\nlocal y = 1\n\nreturn f(y)",
				"local y = 1\nlocal z = undefined_fn(y)\nreturn z",
				"local t = {}\nt[nil] = 1",
				"for i = 1, 'x' do end",
				"local y = 2\ny = -{}\nreturn y",
			};

			AssertSame(sources, sources.Count);
		}

		[Test]
		public void RegisterVm_RunsFewerInstructionsPerIteration()
		{
			// The interrupt handler first runs after 10000 instructions: the loop has then run 10000 divided
			// by its instructions per iteration. A loop that stores into a global is 2 register instructions
			// (SetTabUp, ForLoop), more on the stack VM.
			int[] iterations = new int[2];

			for (int k = 0; k < 2; k++)
			{
				Script s = new Script();
				s.Options.RegisterVm = k == 1;
				s.Options.InterruptHandler = executed => true;
				Assert.Throws<ScriptInterruptedException>(() => s.DoString("for i = 1, 1e9 do c = i end"));
				iterations[k] = (int)s.Globals.Get("c").Number;
			}

			Assert.Greater(iterations[1], iterations[0] * 3 / 2, string.Format("register {0}, stack {1}", iterations[1], iterations[0]));
		}
	}
}
