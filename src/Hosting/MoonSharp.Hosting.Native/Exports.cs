using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MoonSharp.Hosting.Native
{
	/// <summary>
	/// C entry points around <see cref="ScriptHost"/>. Strings cross as UTF-8 with an explicit length
	/// (-1 for a missing string); results are UTF-8 envelopes allocated here and released with moonsharp_managed_free.
	///
	/// The same methods serve both in-process builds: hostfxr finds them by type and method name
	/// (UNMANAGEDCALLERSONLY_METHOD), and a NativeAOT build exports them under their EntryPoint names.
	///
	/// No exception may leave these methods: an exception that reaches an [UnmanagedCallersOnly] frame
	/// terminates the process, and here the process is the JVM. Every method catches everything.
	///
	/// Thread safety: a ScriptHost is not thread-safe, so every call takes the context's lock. Calls on
	/// different contexts run in parallel; calls on one context from several threads run one at a time.
	/// </summary>
	public static unsafe class Exports
	{
		public const int OpEcho = 0;
		public const int OpLoad = 1;
		public const int OpInvoke = 2;
		public const int OpRun = 3;
		public const int OpValidate = 4;
		public const int OpUnload = 5;
		public const int OpVersion = 6;

		static readonly ConcurrentDictionary<long, ScriptHost> s_Contexts = new ConcurrentDictionary<long, ScriptHost>();
		static long s_NextId;

		/// <summary>Option bit for <see cref="ContextCreate"/>: the int64 and uint64 modules.</summary>
		public const int ContextInt64 = 1;

		/// <summary>
		/// Creates a script context with the given options (<see cref="ContextInt64"/>, or 0) and returns its
		/// id (never 0), or 0 if it could not be created.
		/// </summary>
		[UnmanagedCallersOnly(EntryPoint = "moonsharp_managed_context_create")]
		public static long ContextCreate(int options)
		{
			try
			{
				long id = Interlocked.Increment(ref s_NextId);
				s_Contexts[id] = ScriptHost.Create((options & ContextInt64) != 0);
				return id;
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>Drops a context. A call already running on it finishes normally. Returns 1 if it existed.</summary>
		[UnmanagedCallersOnly(EntryPoint = "moonsharp_managed_context_dispose")]
		public static int ContextDispose(long context)
		{
			try
			{
				ScriptHost removed;
				return s_Contexts.TryRemove(context, out removed) ? 1 : 0;
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>
		/// Runs one operation and returns the result envelope as UTF-8 (NUL-terminated, length in *outLength).
		/// Arguments by op: load (module, source), invoke (module, function, argsJson), run (chunk, source,
		/// inputJson), validate (chunk, source), unload (module), version, echo (a; returns a unchanged,
		/// for measuring the boundary). Returns null only when the result cannot be allocated.
		/// </summary>
		[UnmanagedCallersOnly(EntryPoint = "moonsharp_managed_call")]
		public static byte* Call(long context, int op, byte* a, int aLength, byte* b, int bLength, byte* c, int cLength, int* outLength)
		{
			string result;

			try
			{
				result = Dispatch(context, op, Text(a, aLength), Text(b, bLength), Text(c, cLength));
			}
			catch (Exception ex)
			{
				result = Envelopes.Error("host", ex.GetType().FullName + ": " + ex.Message);
			}

			try
			{
				return ToUtf8(result, outLength);
			}
			catch
			{
				*outLength = -1;
				return null;
			}
		}

		[UnmanagedCallersOnly(EntryPoint = "moonsharp_managed_free")]
		public static void Free(byte* p)
		{
			NativeMemory.Free(p);
		}

		static string Dispatch(long context, int op, string a, string b, string c)
		{
			switch (op)
			{
				case OpEcho:
					return a ?? "";
				case OpVersion:
					return Envelopes.Ok(Envelopes.Quote(ScriptHost.EngineVersion));
			}

			ScriptHost host;

			if (!s_Contexts.TryGetValue(context, out host))
				return Envelopes.Error("host", "script context " + context + " does not exist");

			lock (host)
			{
				switch (op)
				{
					case OpLoad:
						return host.Load(a, b);
					case OpInvoke:
						return host.Invoke(a, b, c ?? "[]");
					case OpRun:
						return host.Run(a ?? "chunk", b, c);
					case OpValidate:
						return host.Validate(a ?? "chunk", b);
					case OpUnload:
						return Envelopes.Ok(host.Unload(a) ? "true" : "false");
					default:
						return Envelopes.Error("host", "unknown operation " + op);
				}
			}
		}

		static string Text(byte* p, int length)
		{
			return length < 0 || p == null ? null : Encoding.UTF8.GetString(p, length);
		}

		static byte* ToUtf8(string s, int* outLength)
		{
			int n = Encoding.UTF8.GetByteCount(s);
			byte* p = (byte*)NativeMemory.Alloc((nuint)n + 1);
			Encoding.UTF8.GetBytes(s, new Span<byte>(p, n));
			p[n] = 0;
			*outLength = n;
			return p;
		}
	}

	/// <summary>Envelopes for results that do not come from a ScriptHost method, in the same shape as the worker's.</summary>
	static class Envelopes
	{
		public static string Ok(string valueJson)
		{
			return "{\"ok\":true,\"value\":" + valueJson + ",\"output\":\"\",\"timing\":{}}";
		}

		public static string Error(string kind, string message)
		{
			var sb = new StringBuilder("{\"ok\":false,\"error\":{\"kind\":");
			LuaJson.WriteString(kind, sb);
			sb.Append(",\"message\":");
			LuaJson.WriteString(message, sb);
			sb.Append(",\"chunk\":null,\"line\":null,\"stack\":[]},\"output\":\"\"}");
			return sb.ToString();
		}

		public static string Quote(string s)
		{
			var sb = new StringBuilder();
			LuaJson.WriteString(s, sb);
			return sb.ToString();
		}
	}
}
