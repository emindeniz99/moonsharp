using System;
using System.IO;
using MoonSharp.Interpreter.Loaders;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// This class contains options to customize behaviour of Script objects.
	/// </summary>
	public class ScriptOptions
	{
		internal ScriptOptions()
		{
		}

		internal ScriptOptions(ScriptOptions defaults)
		{
			this.DebugInput = defaults.DebugInput;
			this.DebugPrint = defaults.DebugPrint;

			this.UseLuaErrorLocations = defaults.UseLuaErrorLocations;
			this.Stdin = defaults.Stdin;
			this.Stdout = defaults.Stdout;
			this.Stderr = defaults.Stderr;
			this.TailCallOptimizationThreshold = defaults.TailCallOptimizationThreshold;

			this.ScriptLoader = defaults.ScriptLoader;

			this.CheckThreadAccess = defaults.CheckThreadAccess;
			this.ExactInt64Interop = defaults.ExactInt64Interop;
			this.InterruptHandler = defaults.InterruptHandler;
		}

		/// <summary>
		/// Gets or sets the current script-loader.
		/// </summary>
		public IScriptLoader ScriptLoader { get; set; }

		/// <summary>
		/// Gets or sets the debug print handler
		/// </summary>
		public Action<string> DebugPrint { get; set; }

		/// <summary>
		/// Gets or sets the debug input handler (takes a prompt as an input, for interactive interpreters, like debug.debug).
		/// </summary>
		public Func<string, string> DebugInput { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether error messages will use Lua error locations instead of MoonSharp 
		/// improved ones. Use this for compatibility with legacy Lua code which parses error messages.
		/// </summary>
		public bool UseLuaErrorLocations { get; set; }

		/// <summary>
		/// Gets or sets a value which dictates the behaviour of the colon (':') operator in callbacks to CLR code.
		/// </summary>
		public ColonOperatorBehaviour ColonOperatorClrCallbackBehaviour { get; set; }

		/// <summary>
		/// Gets or sets the stream used as stdin. If null, a default stream is used.
		/// </summary>
		public Stream Stdin { get; set; }

		/// <summary>
		/// Gets or sets the stream used as stdout. If null, a default stream is used.
		/// </summary>
		public Stream Stdout { get; set; }

		/// <summary>
		/// Gets or sets the stream used as stderr. If null, a default stream is used.
		/// </summary>
		public Stream Stderr { get; set; }

		/// <summary>
		/// Gets or sets the stack depth threshold at which MoonSharp starts doing
		/// tail call optimizations.
		/// TCOs can provide the little benefit of avoiding stack overflows in corner case
		/// scenarios, at the expense of losing debug information and error stack traces 
		/// in all other, more common scenarios. MoonSharp choice is to start performing
		/// TCOs only after a certain threshold of stack usage is reached - by default
		/// half the current stack depth (128K entries), thus 64K entries, on either
		/// the internal stacks.
		/// Set this to int.MaxValue to disable TCOs entirely, or to 0 to always have
		/// TCOs enabled.
		/// </summary>
		public int TailCallOptimizationThreshold { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the thread check is enabled.
		/// A "lazy" thread check is performed everytime execution is entered to ensure that no two threads
		/// calls MoonSharp execution concurrently. However 1) the check is performed best effort (thus, it might
		/// not detect all issues) and 2) it might trigger in very odd legal situations (like, switching threads 
		/// inside a CLR-callback without actually having concurrency.
		/// 
		/// Disable this option if the thread check is giving problems in your scenario, but please check that
		/// you are not calling MoonSharp execution concurrently as it is not supported.
		/// </summary>
		public bool CheckThreadAccess { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether CLR <c>long</c> and <c>ulong</c> values reach scripts
		/// as exact <c>int64</c> and <c>uint64</c> userdata (see <see cref="LuaInt64"/>) instead of
		/// numbers, which round above 2^53-1. It applies to every CLR-to-Lua conversion done for this
		/// script: method results and out parameters, properties, fields, arguments of Script.Call and
		/// values stored through Table indexers. Defaults to false, which keeps the number behaviour.
		/// Passing int64 and uint64 values to CLR parameters works either way.
		/// </summary>
		public bool ExactInt64Interop { get; set; }

		/// <summary>
		/// Gets or sets a handler the VM calls every 10,000 instructions with the number of instructions
		/// run since the host's outermost call into this script began (a call, a callback's call or a
		/// coroutine resume from the host). Return true to stop the script: the VM throws
		/// <see cref="ScriptInterruptedException"/>, which pcall cannot catch, and the script stays usable
		/// for later calls. The count is the same on every platform, so <c>n =&gt; n &gt;= limit</c> is a
		/// deterministic budget; a handler can also check a deadline or a CancellationToken.
		/// Long library loops (pattern matching, string.rep, table.concat, table.sort) also call it every
		/// 10,000 steps, with the count unchanged: their steps are not instructions.
		/// </summary>
		public Func<long, bool> InterruptHandler { get; set; }

	}
}
