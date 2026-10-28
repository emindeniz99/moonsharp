using System;

namespace MoonSharp.Hosting
{
	/// <summary>
	/// An error raised by the host layer itself (bad input JSON, an unconvertible result, an unknown module),
	/// as opposed to a Lua syntax or runtime error.
	/// </summary>
	public sealed class HostException : Exception
	{
		/// <summary>One of "input", "output" or "host".</summary>
		public string Kind { get; private set; }

		public HostException(string kind, string message)
			: base(message)
		{
			Kind = kind;
		}
	}
}
