using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MoonSharp.Interpreter.Tests.EndToEnd
{
	[TestFixture]
	public class ErrorHandlingTests
	{
		[Test]
		public void PCallMultipleReturns()
		{
			string script = @"return pcall(function() return 1,2,3 end)";

			Script S = new Script();
			var res = S.DoString(script);

			Assert.AreEqual(DataType.Tuple, res.Type);
			Assert.AreEqual(4, res.Tuple.Length);
			Assert.AreEqual(true, res.Tuple[0].Boolean);
			Assert.AreEqual(1, res.Tuple[1].Number);
			Assert.AreEqual(2, res.Tuple[2].Number);
			Assert.AreEqual(3, res.Tuple[3].Number);
		}

		[Test]
		public void Errors_PCall_ClrFunction()
		{
			string script = @"
				r, msg = pcall(assert, false, 'catched')
				return r, msg;
								";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.Tuple, res.Type);
			Assert.AreEqual(2, res.Tuple.Length);
			Assert.AreEqual(DataType.Boolean, res.Tuple[0].Type);
			Assert.AreEqual(DataType.String, res.Tuple[1].Type);
			Assert.AreEqual(false, res.Tuple[0].Boolean);
		}

		[Test]
		public void Errors_PCall_Multiples()
		{
			string script = @"
function try(fn)
	local x, y = pcall(fn)
	
	if (x) then
		return y
	else
		return '!'
	end
end

function a()
	return try(b) .. 'a';
end

function b()
	return try(c) .. 'b';
end

function c()
	return try(d) .. 'c';
end

function d()
	local t = { } .. 'x'
end


return a()
";

			DynValue res = Script.RunString(script);

			Assert.AreEqual(DataType.String, res.Type);
			Assert.AreEqual("!cba", res.String);
		}

		[Test]
		public void Errors_TryCatch_Multiples()
		{
			string script = @"
function a()
	return try(b) .. 'a';
end

function b()
	return try(c) .. 'b';
end

function c()
	return try(d) .. 'c';
end

function d()
	local t = { } .. 'x'
end


return a()
";
			Script S = new Script(CoreModules.None);

			S.Globals["try"] = DynValue.NewCallback((c, a) =>
			{
				try
				{
					var v = a[0].Function.Call();
					return v;
				}
				catch (ScriptRuntimeException)
				{
					return DynValue.NewString("!");
				}
			});


			DynValue res = S.DoString(script);

			Assert.AreEqual(DataType.String, res.Type);
			Assert.AreEqual("!cba", res.String);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private DynValue MethodWithError()
		{
			throw new NullReferenceException();
		}
		
		[Test]
		public void Errors_Get_OriginalException()
		{
			string luaScript = 
@"
	callMethodWithError();
";
			Script script = new Script(CoreModules.None);

			script.Globals["callMethodWithError"] = DynValue.NewCallback((c, a) => MethodWithError());

			var exception = Assert.Throws<NullReferenceException>(() => script.DoString(luaScript));
			AssertOriginalExceptionFrame(exception, typeof(ErrorHandlingTests));
		}

		private class ClassWithMember
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			public void MethodWithError()
			{
				throw new NullReferenceException();
			}
		}
		
		[Test]
		public void Errors_Get_OriginalException_WhenUsingMember()
		{
			string luaScript = 
				@"
	objectWithMethod.MethodWithError();
";
			Script script = new Script(CoreModules.None);
			
			UserData.RegisterType<ClassWithMember>();

			script.Globals["objectWithMethod"] = new ClassWithMember();

			var exception = Assert.Throws<NullReferenceException>(() => script.DoString(luaScript));
			AssertOriginalExceptionFrame(exception, typeof(ClassWithMember));
		}

		private class ClassWithReflectedMember
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			public void MethodWithError()
			{
				throw new NullReferenceException();
			}
		}

		// AOT platforms (IL2CPP, iOS) always call through reflection, which wrapped the exception in a
		// TargetInvocationException, so hosts there caught a different type than on .NET or Mono.
		[Test]
		public void Errors_Get_OriginalException_WhenUsingMemberThroughReflection()
		{
			Script script = new Script(CoreModules.None);

			UserData.RegisterType<ClassWithReflectedMember>(InteropAccessMode.Reflection);

			script.Globals["objectWithMethod"] = new ClassWithReflectedMember();

			var exception = Assert.Throws<NullReferenceException>(() => script.DoString("objectWithMethod.MethodWithError()"));
			AssertOriginalExceptionFrame(exception, typeof(ClassWithReflectedMember));
		}

		// A long-lived Script (a game session, a server worker) must survive any number of failed calls.
		// Each failed call used to leave the function, its arguments and the argument count on the value
		// stack, so the Script broke for good after 131072 / (args + 2) errors.
		[Test]
		public void Errors_ScriptStaysUsableAfterManyFailedCalls()
		{
			Script S = new Script(CoreModules.None);
			DynValue fail = S.DoString("return function(...) error('failed') end");

			// 10 arguments leaked 12 slots per call, so this loop overflowed the old stack.
			for (int i = 0; i < 12000; i++)
				Assert.Throws<ScriptRuntimeException>(() => S.Call(fail, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10));

			Assert.AreEqual(2, S.DoString("return 1 + 1").Number);
		}

		[Test]
		public void Errors_ScriptStaysUsableAfterManyClrExceptions()
		{
			Script S = new Script(CoreModules.None);
			S.Globals["throwing"] = (Action)(() => { throw new InvalidOperationException("host failure"); });
			DynValue fail = S.DoString("return function(...) throwing() end");

			for (int i = 0; i < 12000; i++)
				Assert.Throws<InvalidOperationException>(() => S.Call(fail, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10));

			Assert.AreEqual(2, S.DoString("return 1 + 1").Number);
		}

		// Lua reports runaway recursion as a "stack overflow" error that pcall can catch.
		[Test]
		public void Errors_StackOverflowIsCatchableByPcall()
		{
			Script S = new Script(CoreModules.Basic | CoreModules.ErrorHandling);
			DynValue res = S.DoString(@"
				local function f() return 1 + f() end
				return pcall(f)");

			Assert.AreEqual(DataType.Tuple, res.Type);
			Assert.AreEqual(false, res.Tuple[0].Boolean);
			StringAssert.Contains("stack overflow", res.Tuple[1].String);
			Assert.AreEqual(2, S.DoString("return 1 + 1").Number);
		}

		[Test]
		public void Errors_UncaughtStackOverflowIsScriptRuntimeException()
		{
			Script S = new Script(CoreModules.None);

			var ex = Assert.Throws<ScriptRuntimeException>(() => S.DoString("local function f() return 1 + f() end return f()"));
			StringAssert.Contains("stack overflow", ex.Message);
			Assert.AreEqual(2, S.DoString("return 1 + 1").Number);
		}

		// The maximum recursion depth must not shrink: scripts that recursed this deep before keep working.
		[Test]
		public void Errors_DeepRecursionBelowLimitStillWorks()
		{
			Script S = new Script(CoreModules.None);
			DynValue res = S.DoString(@"
				local function depth(n) if n == 0 then return 0 end return 1 + depth(n - 1) end
				return depth(30000)");

			Assert.AreEqual(30000, res.Number);
		}

		private static void AssertOriginalExceptionFrame(Exception exception, Type declaringType)
		{
			var frames = new StackTrace(exception).GetFrames();
			Assert.IsNotNull(frames, "Exception should retain its stack frames");
			Assert.IsTrue(frames.Any(frame =>
			{
				var method = frame.GetMethod();
				return method != null && method.DeclaringType == declaringType && method.Name == "MethodWithError";
			}), "Exception should retain the original C# throwing method");
		}

	}
}
